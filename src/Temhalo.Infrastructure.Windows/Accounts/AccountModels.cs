using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Temhalo.Infrastructure.Windows.Logging;

namespace Temhalo.Infrastructure.Windows.Accounts;

public enum StreamingProvider { Twitch, YouTube, Kick, Custom, TikTok, Facebook, Restream }

/// <summary>Only serialize inside the DPAPI settings document. Never log this record.</summary>
public sealed record ConnectedAccount
{
    public StreamingProvider Provider { get; init; }
    public string ClientId { get; init; } = "";
    public string UserId { get; init; } = "";
    public string DisplayName { get; init; } = "";
    public string AccessToken { get; init; } = "";
    public string RefreshToken { get; init; } = "";
    public DateTimeOffset ExpiresAt { get; init; }
    public DateTimeOffset ValidatedAt { get; init; }
    public string BroadcastId { get; init; } = "";
    public string BroadcastTitle { get; init; } = "";
    public void Validate()
    {
        if (Provider is < StreamingProvider.Twitch or > StreamingProvider.Kick ||
            new[] { ClientId, UserId, DisplayName, AccessToken, RefreshToken, BroadcastId, BroadcastTitle }
                .Any(value => value is null || value.Length > 16384 || value.Any(char.IsControl)) ||
            ClientId.Length == 0 || UserId.Length == 0 || DisplayName.Length == 0)
            throw new ArgumentException("Le compte lié est incomplet. Reconnectez la plateforme.");
    }
    public void ProtectDiagnostics()
    { SecretRedactor.Shared.Remember(AccessToken); SecretRedactor.Shared.Remember(RefreshToken); }
    public override string ToString() => "Compte de diffusion [jetons masqués]";
}

public sealed record YouTubeBroadcast(string Id, string Title, string StreamId);
public sealed record AccountStream(string ServerUrl, string StreamKey)
{
    public override string ToString() => "Flux de compte [clé masquée]";
}
public sealed record AuthorizationPrompt(string Message, Uri? BrowserUri = null);
public sealed class AccountConnectionException(string message, bool reconnect = false) : Exception(message)
{ public bool RequiresReconnect { get; } = reconnect; }

/// <summary>Public client configuration. The confidential Kick secret belongs ONLY to the broker.</summary>
public sealed record OAuthClientConfiguration
{
    public string TwitchClientId { get; init; } = "";
    public string YouTubeClientId { get; init; } = "";
    // Desktop Google credentials are not confidential; a web-app secret must never be used here.
    public string YouTubeDesktopClientSecret { get; init; } = "";
    public string KickClientId { get; init; } = "";
    public string KickBrokerUrl { get; init; } = "";
    public int KickCallbackPort { get; init; } = 18473;
    public string ClientId(StreamingProvider provider) => provider switch
    { StreamingProvider.Twitch => TwitchClientId, StreamingProvider.YouTube => YouTubeClientId, StreamingProvider.Kick => KickClientId, _ => "" };
    public void Require(StreamingProvider provider)
    {
        if (string.IsNullOrWhiteSpace(ClientId(provider)) || ClientId(provider).StartsWith("REPLACE", StringComparison.Ordinal))
            throw new AccountConnectionException("La connexion par compte doit encore être activée pour cette plateforme. Consultez le guide COMPTES-OAUTH ; vous pouvez utiliser une clé manuellement dès maintenant.");
        if (provider == StreamingProvider.Kick)
        {
            if (!Uri.TryCreate(KickBrokerUrl, UriKind.Absolute, out var uri) ||
                !(uri.Scheme == "https" || uri.Scheme == "http" && uri.Host == "127.0.0.1") ||
                uri.UserInfo.Length != 0 || uri.Query.Length != 0 || uri.Fragment.Length != 0 || KickCallbackPort is < 1024 or > 65535)
                throw new AccountConnectionException("Le service de connexion Kick doit être configuré en HTTPS (ou sur 127.0.0.1 pour un test local). La clé manuelle reste disponible.");
        }
    }
    public static OAuthClientConfiguration Load(string directory)
    {
        string local = Path.Combine(directory, "oauth-clients.json");
        string bundled = Path.Combine(AppContext.BaseDirectory, "oauth-clients.json");
        string path = File.Exists(local) ? local : bundled;
        if (!File.Exists(path)) return new();
        if (new FileInfo(path).Length > 32768) throw new InvalidDataException("La configuration OAuth est trop volumineuse.");
        try
        {
            var value = JsonSerializer.Deserialize<OAuthClientConfiguration>(File.ReadAllText(path)) ?? new();
            if (new[] { value.TwitchClientId, value.YouTubeClientId, value.YouTubeDesktopClientSecret, value.KickClientId, value.KickBrokerUrl }.Any(text => text is null || text.Any(char.IsControl)))
                throw new InvalidDataException("Le fichier oauth-clients.json contient des valeurs invalides.");
            return value;
        }
        catch (JsonException) { throw new InvalidDataException("Le fichier oauth-clients.json est invalide. Les clés manuelles restent utilisables."); }
    }
    public override string ToString() => "Configuration des clients OAuth [masquée]";
}

public static class OAuthProof
{
    public static string Random() => Base64Url(RandomNumberGenerator.GetBytes(32));
    public static string Challenge(string verifier) => Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
    private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    public static bool Matches(string expected, string actual) => expected.Length == actual.Length &&
        CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(expected), Encoding.UTF8.GetBytes(actual));
}
