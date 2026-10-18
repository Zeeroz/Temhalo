using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Temhalo.Infrastructure.Windows.Logging;
using Temhalo.Infrastructure.Windows.Processes;

namespace Temhalo.Infrastructure.Windows.Accounts;

/// <summary>Control-plane HTTP only. Never called from the FLV pipeline; all media still uses -c copy.</summary>
public sealed class StreamingAccountService(HttpClient http, OAuthClientConfiguration clients,
    Func<Uri, CancellationToken, Task> openBrowser)
{
    public const string TwitchScope = "channel:read:stream_key";
    // Twitch's official automatic ingest. Legacy city aliases can redirect to a distant origin.
    // https://help.twitch.tv/s/twitch-ingest-recommendation?language=en_US
    public const string TwitchIngestUrl = "rtmp://ingest.global-contribute.live-video.net/app";
    public const string YouTubeScope = "https://www.googleapis.com/auth/youtube.readonly";
    public const string KickScopes = "channel:read streamkey:read";

    /// <summary>Project capability only; this never contacts a platform or inspects user tokens.</summary>
    public bool IsConnectionConfigured(StreamingProvider provider)
    {
        try { clients.Require(provider); return true; }
        catch (AccountConnectionException) { return false; }
    }

    public async Task<ConnectedAccount> ConnectAsync(StreamingProvider provider, IProgress<AuthorizationPrompt> progress, CancellationToken token)
    {
        clients.Require(provider);
        ConnectedAccount account = provider == StreamingProvider.Twitch
            ? await TwitchDeviceAsync(progress, token).ConfigureAwait(false)
            : await BrowserCodeAsync(provider, progress, token).ConfigureAwait(false);
        return await IdentifyAsync(account, token).ConfigureAwait(false);
    }

    private async Task<ConnectedAccount> TwitchDeviceAsync(IProgress<AuthorizationPrompt> progress, CancellationToken token)
    {
        using var device = await PostAsync("https://id.twitch.tv/oauth2/device",
            new() { ["client_id"] = clients.TwitchClientId, ["scopes"] = TwitchScope }, token).ConfigureAwait(false);
        string deviceCode = Required(device.RootElement, "device_code");
        SecretRedactor.Shared.Remember(deviceCode);
        string userCode = Required(device.RootElement, "user_code");
        var uri = new Uri(Required(device.RootElement, "verification_uri"));
        if (uri.Scheme != "https" || uri.Host is not ("www.twitch.tv" or "id.twitch.tv"))
            throw new AccountConnectionException("Twitch a renvoyé une adresse de connexion non reconnue.");
        int interval = Math.Clamp(Number(device.RootElement, "interval", 5), 1, 60);
        var expiry = DateTimeOffset.UtcNow.AddSeconds(Math.Clamp(Number(device.RootElement, "expires_in", 300), 1, 1800));
        progress.Report(new("Dans Twitch, confirmez le code « " + userCode + " ». Revenez ici après autorisation.", uri));
        await openBrowser(uri, token).ConfigureAwait(false);
        while (DateTimeOffset.UtcNow < expiry)
        {
            await Task.Delay(TimeSpan.FromSeconds(interval), token).ConfigureAwait(false);
            using var response = await PostRawAsync("https://id.twitch.tv/oauth2/token", new()
            {
                ["client_id"] = clients.TwitchClientId, ["scopes"] = TwitchScope, ["device_code"] = deviceCode,
                ["grant_type"] = "urn:ietf:params:oauth:grant-type:device_code"
            }, token).ConfigureAwait(false);
            using var json = await ReadAsync(response, token).ConfigureAwait(false);
            if (response.IsSuccessStatusCode) return Tokens(StreamingProvider.Twitch, json.RootElement);
            string error = Text(json.RootElement, "error");
            string message = Text(json.RootElement, "message");
            if (error == "authorization_pending" || message == "authorization_pending") continue;
            if (error == "slow_down" || message == "slow_down") { interval = Math.Min(interval + 5, 60); continue; }
            throw Failure(response.StatusCode, error);
        }
        throw new AccountConnectionException("Le code Twitch a expiré. Relancez la connexion.");
    }

    private async Task<ConnectedAccount> BrowserCodeAsync(StreamingProvider provider, IProgress<AuthorizationPrompt> progress, CancellationToken token)
    {
        string verifier = OAuthProof.Random(), state = OAuthProof.Random();
        await using var receiver = await LoopbackAuthorization.StartAsync(state,
            provider == StreamingProvider.Kick ? clients.KickCallbackPort : 0, provider == StreamingProvider.Kick, token).ConfigureAwait(false);
        string redirect = receiver.RedirectUri.AbsoluteUri;
        var parameters = new Dictionary<string, string>
        {
            ["client_id"] = clients.ClientId(provider), ["response_type"] = "code", ["redirect_uri"] = redirect,
            ["scope"] = provider == StreamingProvider.YouTube ? YouTubeScope : KickScopes,
            ["state"] = state, ["code_challenge"] = OAuthProof.Challenge(verifier), ["code_challenge_method"] = "S256"
        };
        if (provider == StreamingProvider.YouTube)
        { parameters["access_type"] = "offline"; parameters["prompt"] = "consent select_account"; }
        var uri = new Uri((provider == StreamingProvider.YouTube ? "https://accounts.google.com/o/oauth2/v2/auth" : "https://id.kick.com/oauth/authorize") + "?" + Query(parameters));
        progress.Report(new("Terminez la connexion sur le site officiel, puis revenez dans Temhalo.", uri));
        await openBrowser(uri, token).ConfigureAwait(false);
        string code = await receiver.WaitAsync(token).ConfigureAwait(false);
        SecretRedactor.Shared.Remember(code);
        var form = new Dictionary<string, string>
        {
            ["client_id"] = clients.ClientId(provider), ["grant_type"] = "authorization_code", ["code"] = code,
            ["code_verifier"] = verifier, ["redirect_uri"] = redirect
        };
        if (provider == StreamingProvider.YouTube && clients.YouTubeDesktopClientSecret.Length > 0)
            form["client_secret"] = clients.YouTubeDesktopClientSecret;
        using var result = await PostAsync(TokenEndpoint(provider), form, token).ConfigureAwait(false);
        return Tokens(provider, result.RootElement);
    }

    /// <summary>Caller must commit returned tokens to DPAPI BEFORE the next API request (rotating refresh tokens).</summary>
    public async Task<ConnectedAccount> RefreshAsync(ConnectedAccount account, CancellationToken token)
    {
        clients.Require(account.Provider);
        if (account.ClientId != clients.ClientId(account.Provider))
            throw new AccountConnectionException("La configuration de connexion a changé. Reconnectez ce compte.", true);
        account.ProtectDiagnostics();
        if (account.AccessToken.Length > 0 && account.ExpiresAt > DateTimeOffset.UtcNow.AddMinutes(2)) return account;
        if (account.RefreshToken.Length == 0) throw new AccountConnectionException("Reconnectez votre compte pour cette session, ou utilisez une clé manuelle.", true);
        var form = new Dictionary<string, string>
        { ["client_id"] = account.ClientId, ["grant_type"] = "refresh_token", ["refresh_token"] = account.RefreshToken };
        if (account.Provider == StreamingProvider.YouTube && clients.YouTubeDesktopClientSecret.Length > 0)
            form["client_secret"] = clients.YouTubeDesktopClientSecret;
        using var result = await PostAsync(TokenEndpoint(account.Provider), form, token).ConfigureAwait(false);
        var tokens = Tokens(account.Provider, result.RootElement, account.RefreshToken);
        return account with { AccessToken = tokens.AccessToken, RefreshToken = tokens.RefreshToken, ExpiresAt = tokens.ExpiresAt };
    }

    public async Task<ConnectedAccount> IdentifyAsync(ConnectedAccount account, CancellationToken token)
    {
        account.ProtectDiagnostics();
        if (account.Provider == StreamingProvider.Twitch)
        {
            using var validated = await GetAsync("https://id.twitch.tv/oauth2/validate", account, token, twitchValidation: true).ConfigureAwait(false);
            var root = validated.RootElement;
            if (Required(root, "client_id") != clients.TwitchClientId || !root.GetProperty("scopes").EnumerateArray().Any(scope => scope.GetString() == TwitchScope))
                throw new AccountConnectionException("L’autorisation Twitch ne permet pas de lire votre clé de stream. Reconnectez le compte.", true);
            string id = Required(root, "user_id");
            if (account.UserId.Length > 0 && account.UserId != id) throw new AccountConnectionException("Le compte Twitch a changé. Reconnectez-le.", true);
            return account with { UserId = id, DisplayName = "@" + Required(root, "login"), ValidatedAt = DateTimeOffset.UtcNow };
        }
        if (account.Provider == StreamingProvider.YouTube)
        {
            using var channels = await GetAsync("https://www.googleapis.com/youtube/v3/channels?part=snippet&mine=true", account, token).ConfigureAwait(false);
            var channel = First(channels.RootElement, "items", "Aucune chaîne YouTube accessible. Activez le live dans YouTube Studio.");
            string id = Required(channel, "id");
            if (account.UserId.Length > 0 && account.UserId != id) throw new AccountConnectionException("La chaîne YouTube a changé. Reconnectez-la.", true);
            return account with { UserId = id, DisplayName = Required(channel.GetProperty("snippet"), "title"), ValidatedAt = DateTimeOffset.UtcNow };
        }
        using var kick = await GetAsync("https://api.kick.com/public/v1/channels", account, token).ConfigureAwait(false);
        var data = First(kick.RootElement, "data", "Aucune chaîne Kick accessible pour ce compte.");
        string kickId = data.GetProperty("broadcaster_user_id").ToString();
        if (account.UserId.Length > 0 && account.UserId != kickId) throw new AccountConnectionException("Le compte Kick a changé. Reconnectez-le.", true);
        return account with { UserId = kickId, DisplayName = "@" + Required(data, "slug"), ValidatedAt = DateTimeOffset.UtcNow };
    }

    public async Task<IReadOnlyList<YouTubeBroadcast>> BroadcastsAsync(ConnectedAccount account, CancellationToken token)
    {
        if (account.Provider != StreamingProvider.YouTube) throw new ArgumentException("Choisissez YouTube.");
        var items = new List<YouTubeBroadcast>();
        string page = "";
        // mine and broadcastStatus are mutually exclusive filters. Filter lifecycle locally.
        do
        {
            using var result = await GetAsync("https://www.googleapis.com/youtube/v3/liveBroadcasts?part=snippet,contentDetails,status&mine=true&broadcastType=all&maxResults=50" +
                (page.Length > 0 ? "&pageToken=" + Uri.EscapeDataString(page) : ""), account, token).ConfigureAwait(false);
            foreach (var item in result.RootElement.GetProperty("items").EnumerateArray())
            {
                string status = Text(item.GetProperty("status"), "lifeCycleStatus");
                string stream = Text(item.GetProperty("contentDetails"), "boundStreamId");
                if (status is "complete" or "revoked" || stream.Length == 0) continue;
                items.Add(new(Required(item, "id"), Required(item.GetProperty("snippet"), "title"), stream));
            }
            page = Text(result.RootElement, "nextPageToken");
        } while (page.Length > 0 && items.Count < 500);
        return items;
    }

    public async Task<AccountStream> ResolveStreamAsync(ConnectedAccount account, CancellationToken token)
    {
        account.Validate(); account.ProtectDiagnostics();
        AccountStream stream;
        if (account.Provider == StreamingProvider.Twitch)
        {
            using var result = await GetAsync("https://api.twitch.tv/helix/streams/key?broadcaster_id=" + Uri.EscapeDataString(account.UserId), account, token).ConfigureAwait(false);
            stream = new(TwitchIngestUrl, Required(First(result.RootElement, "data", "Clé Twitch indisponible."), "stream_key"));
        }
        else if (account.Provider == StreamingProvider.Kick)
        {
            using var result = await GetAsync("https://api.kick.com/public/v1/channels", account, token).ConfigureAwait(false);
            var channel = First(result.RootElement, "data", "Clé Kick indisponible.");
            if (channel.GetProperty("broadcaster_user_id").ToString() != account.UserId)
                throw new AccountConnectionException("Le compte Kick ne correspond plus. Reconnectez-le.", true);
            var info = channel.GetProperty("stream");
            stream = new(Required(info, "url"), Required(info, "key"));
        }
        else
        {
            if (account.BroadcastId.Length == 0) throw new AccountConnectionException("Choisissez un live YouTube avant de démarrer.");
            using var broadcast = await GetAsync("https://www.googleapis.com/youtube/v3/liveBroadcasts?part=contentDetails,snippet,status&id=" + Uri.EscapeDataString(account.BroadcastId), account, token).ConfigureAwait(false);
            var item = First(broadcast.RootElement, "items", "Ce live YouTube est introuvable. Choisissez un autre live.");
            if (Required(item.GetProperty("snippet"), "channelId") != account.UserId || Text(item.GetProperty("status"), "lifeCycleStatus") is "complete" or "revoked")
                throw new AccountConnectionException("Ce live YouTube est terminé ou appartient à une autre chaîne.");
            string streamId = Required(item.GetProperty("contentDetails"), "boundStreamId");
            using var result = await GetAsync("https://www.googleapis.com/youtube/v3/liveStreams?part=cdn&id=" + Uri.EscapeDataString(streamId), account, token).ConfigureAwait(false);
            var info = First(result.RootElement, "items", "Préparez un flux associé à ce live dans YouTube Studio.").GetProperty("cdn").GetProperty("ingestionInfo");
            string server = Text(info, "rtmpsIngestionAddress");
            stream = new(server.Length > 0 ? server : Required(info, "ingestionAddress"), Required(info, "streamName"));
        }
        SecretRedactor.Shared.Remember(stream.StreamKey);
        _ = new RelayDestination(stream.ServerUrl, stream.StreamKey);
        return stream;
    }

    private string TokenEndpoint(StreamingProvider provider) => provider switch
    {
        StreamingProvider.Twitch => "https://id.twitch.tv/oauth2/token",
        StreamingProvider.YouTube => "https://oauth2.googleapis.com/token",
        StreamingProvider.Kick => clients.KickBrokerUrl.TrimEnd('/') + "/oauth/token",
        _ => throw new ArgumentOutOfRangeException(nameof(provider))
    };
    private ConnectedAccount Tokens(StreamingProvider provider, JsonElement root, string fallbackRefresh = "")
    {
        string access = Required(root, "access_token"), refresh = Text(root, "refresh_token");
        if (access.Length > 16384 || refresh.Length > 16384 || access.Any(char.IsControl) || refresh.Any(char.IsControl))
            throw new AccountConnectionException("La plateforme a renvoyé des jetons non valides. Relancez la connexion.");
        SecretRedactor.Shared.Remember(access); SecretRedactor.Shared.Remember(refresh);
        return new() { Provider = provider, ClientId = clients.ClientId(provider), AccessToken = access,
            RefreshToken = refresh.Length > 0 ? refresh : fallbackRefresh,
            ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(Math.Clamp(Number(root, "expires_in", 3600), 1, 604800)) };
    }
    private async Task<JsonDocument> GetAsync(string url, ConnectedAccount account, CancellationToken token, bool twitchValidation = false)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new(twitchValidation ? "OAuth" : "Bearer", account.AccessToken);
        if (account.Provider == StreamingProvider.Twitch && !twitchValidation) request.Headers.Add("Client-Id", account.ClientId);
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode) throw Failure(response.StatusCode, "");
        return await ReadAsync(response, token).ConfigureAwait(false);
    }
    private async Task<JsonDocument> PostAsync(string url, Dictionary<string, string> form, CancellationToken token)
    {
        using var response = await PostRawAsync(url, form, token).ConfigureAwait(false);
        using var result = await ReadAsync(response, token).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode) throw Failure(response.StatusCode, Text(result.RootElement, "error"));
        return JsonDocument.Parse(result.RootElement.GetRawText());
    }
    private Task<HttpResponseMessage> PostRawAsync(string url, Dictionary<string, string> form, CancellationToken token)
    {
        // HttpClient consumes this form before the awaited response returns; dispose with the request.
        return SendFormAsync(url, form, token);
    }
    private async Task<HttpResponseMessage> SendFormAsync(string url, Dictionary<string, string> form, CancellationToken token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = new FormUrlEncodedContent(form) };
        if (url == clients.KickBrokerUrl.TrimEnd('/') + "/oauth/token") request.Headers.Add("X-Temhalo-Client", "desktop");
        return await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
    }
    private static async Task<JsonDocument> ReadAsync(HttpResponseMessage response, CancellationToken token)
    {
        if (response.Content.Headers.ContentLength > 1048576) throw new AccountConnectionException("La plateforme a renvoyé une réponse trop volumineuse.");
        await using var source = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
        using var bytes = new MemoryStream();
        byte[] buffer = new byte[8192]; int count;
        while ((count = await source.ReadAsync(buffer, token).ConfigureAwait(false)) > 0)
        {
            if (bytes.Length + count > 1048576) throw new AccountConnectionException("Réponse de plateforme trop volumineuse.");
            bytes.Write(buffer, 0, count);
        }
        try { return JsonDocument.Parse(bytes.ToArray()); }
        catch (JsonException) { throw new AccountConnectionException("La plateforme est indisponible ou a renvoyé une réponse inattendue. Réessayez ou utilisez une clé manuelle."); }
    }
    private static AccountConnectionException Failure(HttpStatusCode status, string code) => code switch
    {
        "access_denied" => new("Autorisation refusée. Réessayez ou utilisez une clé manuelle."),
        "expired_token" => new("La demande de connexion a expiré. Réessayez."),
        "invalid_grant" or "invalid_token" => new("L’autorisation a expiré ou a été révoquée. Reconnectez ce compte.", true),
        _ when status == HttpStatusCode.Unauthorized => new("L’autorisation n’est plus valide. Reconnectez ce compte.", true),
        _ when status == HttpStatusCode.Forbidden => new("La plateforme refuse cet accès. Vérifiez les permissions accordées et l’activation du live, ou utilisez une clé manuelle."),
        _ when status == HttpStatusCode.TooManyRequests => new("La plateforme limite les demandes. Patientez avant de réessayer."),
        _ => new("La connexion à la plateforme a échoué. Réessayez ou utilisez une clé manuelle.")
    };
    private static JsonElement First(JsonElement root, string array, string emptyMessage)
    {
        if (!root.TryGetProperty(array, out var items) || items.ValueKind != JsonValueKind.Array || items.GetArrayLength() == 0)
            throw new AccountConnectionException(emptyMessage);
        return items[0];
    }
    private static string Required(JsonElement root, string name)
        => Text(root, name) is { Length: > 0 } value ? value : throw new AccountConnectionException("La plateforme ne fournit pas toutes les informations nécessaires. Vérifiez le compte et les permissions, ou utilisez une clé manuelle.");
    private static string Text(JsonElement root, string name) => root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";
    private static int Number(JsonElement root, string name, int fallback) => root.TryGetProperty(name, out var value) &&
        int.TryParse(value.ToString(), out int result) ? result : fallback;
    internal static string Query(Dictionary<string, string> values) => string.Join("&", values.Select(pair => Uri.EscapeDataString(pair.Key) + "=" + Uri.EscapeDataString(pair.Value)));
}
