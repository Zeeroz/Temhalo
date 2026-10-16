using System.Net;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Temhalo.AccountBroker;
using Temhalo.Infrastructure.Windows.Accounts;
using Temhalo.Infrastructure.Windows.Logging;
using Temhalo.Infrastructure.Windows.Settings;
using Xunit;

namespace Temhalo.Infrastructure.Tests;

public sealed class AccountConnectionTests
{
    private static readonly OAuthClientConfiguration Clients = new() { TwitchClientId = "test-twitch", YouTubeClientId = "test-google", KickClientId = "test-kick", KickBrokerUrl = "https://broker.example.test" };
    private static ConnectedAccount Account(StreamingProvider provider) => new() { Provider = provider, ClientId = Clients.ClientId(provider),
        UserId = "42", DisplayName = "Test", AccessToken = "fake-access-canary", RefreshToken = "fake-refresh-canary", ExpiresAt = DateTimeOffset.UtcNow.AddHours(1) };
    private sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> send) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => send(request); }
    private static HttpResponseMessage Json(string json, HttpStatusCode code = HttpStatusCode.OK) => new(code) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
    private static StreamingAccountService Service(HttpMessageHandler handler) => new(new HttpClient(handler), Clients, (_, _) => Task.CompletedTask);

    [Fact]
    public void PkceMatchesRfc7636VectorAndStateIsCaseSensitive()
    {
        Assert.Equal("E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM", OAuthProof.Challenge("dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk"));
        Assert.True(OAuthProof.Matches("nonce-A", "nonce-A")); Assert.False(OAuthProof.Matches("nonce-A", "nonce-a"));
        Assert.Equal(43, OAuthProof.Random().Length); Assert.NotEqual(OAuthProof.Random(), OAuthProof.Random());
    }
    [Fact]
    public async Task MissingClientDoesNotOpenBrowserOrCallPlatform()
    {
        var service = new StreamingAccountService(new HttpClient(new Handler(_ => throw new Exception("No HTTP expected"))), new(), (_, _) => throw new Exception("No browser expected"));
        await Assert.ThrowsAsync<AccountConnectionException>(() => service.ConnectAsync(StreamingProvider.Twitch, new Progress<AuthorizationPrompt>(), default));
    }
    [Fact]
    public async Task GoogleBrowserFlowUsesSystemBrowserPkceStateOfflineAccessAndReadOnlyScope()
    {
        string? challenge = null;
        var service = new StreamingAccountService(new HttpClient(new Handler(async request =>
        {
            if (request.RequestUri!.Host == "oauth2.googleapis.com")
            {
                var form = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(await request.Content!.ReadAsStringAsync());
                Assert.Equal(challenge, OAuthProof.Challenge(form["code_verifier"].ToString()));
                Assert.Equal("browser-code", form["code"].ToString());
                return Json("""{"access_token":"google-access","refresh_token":"google-refresh","expires_in":3600}""");
            }
            return Json("""{"items":[{"id":"42","snippet":{"title":"My channel"}}]}""");
        })), Clients, async (uri, token) =>
        {
            Assert.Equal("accounts.google.com", uri.Host);
            var query = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(uri.Query);
            Assert.Equal(StreamingAccountService.YouTubeScope, query["scope"].ToString());
            Assert.Equal("offline", query["access_type"].ToString());
            Assert.Equal("S256", query["code_challenge_method"].ToString()); challenge = query["code_challenge"].ToString();
            using var http = new HttpClient();
            using var result = await http.GetAsync(query["redirect_uri"] + "?state=" + query["state"] + "&code=browser-code", token);
            Assert.Equal(HttpStatusCode.OK, result.StatusCode);
        });
        var account = await service.ConnectAsync(StreamingProvider.YouTube, new Progress<AuthorizationPrompt>(), default);
        Assert.Equal("My channel", account.DisplayName); Assert.Equal("google-refresh", account.RefreshToken);
    }
    [Fact]
    public async Task GoogleBrowserCancelClosesReceiverWithoutExchangingCode()
    {
        using var cancellation = new CancellationTokenSource();
        var service = new StreamingAccountService(new HttpClient(new Handler(_ => throw new Exception("Token exchange must not occur"))), Clients,
            (_, _) => { cancellation.Cancel(); return Task.CompletedTask; });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.ConnectAsync(StreamingProvider.YouTube, new Progress<AuthorizationPrompt>(), cancellation.Token));
    }
    [Fact]
    public async Task KickBrowserFlowUsesPkceAndBrokerNeverReceivesAClientSecretFromDesktop()
    {
        var reservation = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0); reservation.Start();
        int port = ((IPEndPoint)reservation.LocalEndpoint).Port; reservation.Stop();
        var clients = Clients with { KickCallbackPort = port }; string? challenge = null;
        var service = new StreamingAccountService(new HttpClient(new Handler(async request =>
        {
            if (request.Method == HttpMethod.Post)
            {
                Assert.Equal("broker.example.test", request.RequestUri!.Host);
                Assert.Equal("desktop", Assert.Single(request.Headers.GetValues("X-Temhalo-Client")));
                string body = await request.Content!.ReadAsStringAsync(); Assert.DoesNotContain("client_secret", body);
                var form = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(body);
                Assert.Equal(challenge, OAuthProof.Challenge(form["code_verifier"].ToString()));
                return Json("""{"access_token":"kick-access","refresh_token":"kick-refresh","expires_in":3600}""");
            }
            return Json("""{"data":[{"broadcaster_user_id":42,"slug":"test-kick","stream":{}}]}""");
        })), clients, async (uri, token) =>
        {
            Assert.Equal("id.kick.com", uri.Host);
            var query = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(uri.Query);
            Assert.Equal(StreamingAccountService.KickScopes, query["scope"].ToString());
            challenge = query["code_challenge"].ToString();
            Assert.Equal($"http://localhost:{port}/oauth/callback", query["redirect_uri"].ToString());
            using var http = new HttpClient();
            using var response = await http.GetAsync(query["redirect_uri"] + "?state=" + query["state"] + "&code=kick-code", token);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        });
        var result = await service.ConnectAsync(StreamingProvider.Kick, new Progress<AuthorizationPrompt>(), default);
        Assert.Equal("@test-kick", result.DisplayName); Assert.Equal("kick-refresh", result.RefreshToken);
    }
    [Fact]
    public async Task TwitchDevicePollingHandlesPendingThenValidatesClientScopeAndIdentity()
    {
        int polls = 0; string? opened = null;
        var service = new StreamingAccountService(new HttpClient(new Handler(async request =>
        {
            string body = request.Content is null ? "" : await request.Content.ReadAsStringAsync();
            Assert.DoesNotContain("client_secret", body);
            return request.RequestUri!.AbsolutePath switch
            {
                "/oauth2/device" => Json("""{"device_code":"fake-device","user_code":"CODE","verification_uri":"https://www.twitch.tv/activate","interval":1,"expires_in":60}"""),
                "/oauth2/token" when ++polls == 1 => Json("""{"error":"authorization_pending"}""", HttpStatusCode.BadRequest),
                "/oauth2/token" => Json("""{"access_token":"device-access","refresh_token":"device-refresh","expires_in":14400}"""),
                "/oauth2/validate" => Json("""{"client_id":"test-twitch","user_id":"42","login":"test","scopes":["channel:read:stream_key"]}"""),
                _ => throw new InvalidOperationException("Unexpected endpoint")
            };
        })), Clients, (uri, _) => { opened = uri.AbsoluteUri; return Task.CompletedTask; });
        var account = await service.ConnectAsync(StreamingProvider.Twitch, new Progress<AuthorizationPrompt>(), default);
        Assert.Equal(2, polls); Assert.Equal("https://www.twitch.tv/activate", opened); Assert.Equal("@test", account.DisplayName);
        Assert.Equal("42", account.UserId); Assert.DoesNotContain("device-access", account.ToString());
    }
    [Fact]
    public async Task RefreshRotatesTokensWithoutSecretForPublicTwitchClient()
    {
        var service = Service(new Handler(async request =>
        {
            Assert.Equal("id.twitch.tv", request.RequestUri!.Host);
            string body = await request.Content!.ReadAsStringAsync();
            Assert.Contains("grant_type=refresh_token", body); Assert.DoesNotContain("client_secret", body);
            return Json("""{"access_token":"new-access","refresh_token":"rotated-refresh","expires_in":14400}""");
        }));
        var account = await service.RefreshAsync(Account(StreamingProvider.Twitch) with { ExpiresAt = DateTimeOffset.MinValue }, default);
        Assert.Equal("rotated-refresh", account.RefreshToken); Assert.Equal("42", account.UserId);
        Assert.DoesNotContain("rotated-refresh", SecretRedactor.Shared.Redact("rotated-refresh"));
    }
    [Fact]
    public async Task RevokedRefreshHasActionableSafeError()
    {
        var service = Service(new Handler(_ => Task.FromResult(Json("""{"error":"invalid_grant","description":"secret-canary"}""", HttpStatusCode.BadRequest))));
        var error = await Assert.ThrowsAsync<AccountConnectionException>(() => service.RefreshAsync(Account(StreamingProvider.Twitch) with { ExpiresAt = DateTimeOffset.MinValue }, default));
        Assert.True(error.RequiresReconnect); Assert.DoesNotContain("secret-canary", error.Message);
    }
    [Fact]
    public async Task TwitchValidationRejectsDifferentClient()
    {
        var service = Service(new Handler(_ => Task.FromResult(Json("""{"client_id":"other-client","user_id":"42","login":"test","scopes":["channel:read:stream_key"]}"""))));
        Assert.True((await Assert.ThrowsAsync<AccountConnectionException>(() => service.IdentifyAsync(Account(StreamingProvider.Twitch), default))).RequiresReconnect);
    }
    [Fact]
    public async Task KickReadsOfficialStreamUrlAndKeyWithBearerToken()
    {
        var service = Service(new Handler(request =>
        {
            Assert.Equal("https://api.kick.com/public/v1/channels", request.RequestUri!.AbsoluteUri);
            Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
            return Task.FromResult(Json("""{"data":[{"broadcaster_user_id":42,"slug":"test","stream":{"url":"rtmps://ingest.example.test/app","key":"kick-key-canary"}}]}"""));
        }));
        var stream = await service.ResolveStreamAsync(Account(StreamingProvider.Kick), default);
        Assert.Equal("rtmps://ingest.example.test/app", stream.ServerUrl); Assert.Equal("kick-key-canary", stream.StreamKey);
        Assert.DoesNotContain(stream.StreamKey, stream.ToString());
    }
    [Fact]
    public async Task TwitchResolvesKeyWithOfficialAutomaticIngestInsteadOfALegacyCityAlias()
    {
        var service = Service(new Handler(request =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal("https://api.twitch.tv/helix/streams/key?broadcaster_id=42", request.RequestUri!.AbsoluteUri);
            Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
            return Task.FromResult(Json("""{"data":[{"stream_key":"twitch-test-key-canary"}]}"""));
        }));
        var stream = await service.ResolveStreamAsync(Account(StreamingProvider.Twitch), default);
        Assert.Equal("rtmp://ingest.global-contribute.live-video.net/app", stream.ServerUrl);
        Assert.Equal("twitch-test-key-canary", stream.StreamKey);
        Assert.DoesNotContain(stream.StreamKey, stream.ToString());
    }
    [Fact]
    public async Task YouTubeListsOnlyUsableBroadcastsAndPaginatesWithoutCombiningExclusiveFilters()
    {
        int pages = 0;
        var service = Service(new Handler(request =>
        {
            string url = request.RequestUri!.AbsoluteUri;
            Assert.Contains("mine=true", url); Assert.DoesNotContain("broadcastStatus", url);
            return Task.FromResult(++pages == 1 ? Json("""{"items":[{"id":"old","snippet":{"title":"Old"},"status":{"lifeCycleStatus":"complete"},"contentDetails":{"boundStreamId":"old-stream"}},{"id":"unbound","snippet":{"title":"No stream"},"status":{"lifeCycleStatus":"ready"},"contentDetails":{}}],"nextPageToken":"page-two"}""") :
                Json("""{"items":[{"id":"live","snippet":{"title":"My live"},"status":{"lifeCycleStatus":"ready"},"contentDetails":{"boundStreamId":"stream"}}]}"""));
        }));
        var broadcasts = await service.BroadcastsAsync(Account(StreamingProvider.YouTube), default);
        Assert.Equal(2, pages); Assert.Equal("live", Assert.Single(broadcasts).Id);
    }
    [Fact]
    public async Task YouTubeResolvesBoundStreamAndPrefersRtmpsWithoutPublishing()
    {
        var service = Service(new Handler(request =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            return Task.FromResult(request.RequestUri!.AbsolutePath.EndsWith("liveBroadcasts", StringComparison.Ordinal) ?
                Json("""{"items":[{"id":"live","snippet":{"channelId":"42"},"status":{"lifeCycleStatus":"ready"},"contentDetails":{"boundStreamId":"stream-id"}}]}""") :
                Json("""{"items":[{"cdn":{"ingestionInfo":{"rtmpsIngestionAddress":"rtmps://ingest.example.test/live","ingestionAddress":"rtmp://ingest.example.test/live","streamName":"youtube-canary"}}}]}"""));
        }));
        var stream = await service.ResolveStreamAsync(Account(StreamingProvider.YouTube) with { BroadcastId = "live" }, default);
        Assert.StartsWith("rtmps:", stream.ServerUrl); Assert.Equal("youtube-canary", stream.StreamKey);
    }
    [Fact]
    public async Task LoopbackRejectsWrongStateAndHostThenAcceptsExactlyTheBoundCallback()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var receiver = await LoopbackAuthorization.StartAsync("test-state", 0, false, timeout.Token);
        using var http = new HttpClient();
        using var wrongState = await http.GetAsync(receiver.RedirectUri + "?state=other&code=wrong", timeout.Token);
        Assert.Equal(HttpStatusCode.BadRequest, wrongState.StatusCode);
        using var request = new HttpRequestMessage(HttpMethod.Get, receiver.RedirectUri + "?state=test-state&code=wrong-host"); request.Headers.Host = "evil.test";
        using var wrongHost = await http.SendAsync(request, timeout.Token); Assert.Equal(HttpStatusCode.BadRequest, wrongHost.StatusCode);
        using var right = await http.GetAsync(receiver.RedirectUri + "?state=test-state&code=accepted", timeout.Token);
        Assert.Equal("no-store", right.Headers.CacheControl?.ToString());
        Assert.Equal("accepted", await receiver.WaitAsync(timeout.Token));
        Assert.DoesNotContain("accepted", await right.Content.ReadAsStringAsync(timeout.Token));
    }
    [Fact]
    public async Task LoopbackCancellationReleasesPort()
    {
        int port;
        await using (var receiver = await LoopbackAuthorization.StartAsync("state", 0, false, default))
        {
            port = receiver.RedirectUri.Port; using var cancel = new CancellationTokenSource(); cancel.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => receiver.WaitAsync(cancel.Token));
        }
        await using var again = await LoopbackAuthorization.StartAsync("new-state", port, false, default);
        Assert.Equal(port, again.RedirectUri.Port);
    }
    [Fact]
    public async Task AccountsAndManualDestinationSurviveDpapiRoundTripWithoutPlaintextTokens()
    {
        string dir = Path.Combine(Path.GetTempPath(), "Temhalo-accounts-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new DpapiSettingsStore(dir);
            var linked = new DestinationSettings { Name = "Account", Provider = 0, Account = Account(StreamingProvider.Twitch) };
            var manual = new DestinationSettings { Name = "Manual", Provider = 3, ServerUrl = "rtmp://127.0.0.1:1936/live", StreamKey = "manual-canary" };
            await store.SaveAsync(new() { Destinations = [linked, manual], ObsInstructionsAcknowledged = true });
            string encrypted = Encoding.UTF8.GetString(await File.ReadAllBytesAsync(store.FilePath));
            foreach (string secret in new[] { linked.Account.AccessToken, linked.Account.RefreshToken, manual.StreamKey }) Assert.DoesNotContain(secret, encrypted);
            var read = await store.LoadAsync(); Assert.True(read.ObsInstructionsAcknowledged); Assert.Equal(linked, read.Destinations![0]); Assert.Equal(manual, read.Destinations[1]);
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); }
    }
    [Fact]
    public void AccountCannotBeReusedWithAnotherPlatformAndKickBrokerMustUseHttps()
    {
        Assert.Throws<ArgumentException>(() => new DestinationSettings { Provider = 1, Account = Account(StreamingProvider.Twitch) }.Validate());
        Assert.Throws<AccountConnectionException>(() => (Clients with { KickBrokerUrl = "http://broker.example.test" }).Require(StreamingProvider.Kick));
        (Clients with { KickBrokerUrl = "http://127.0.0.1:18474" }).Require(StreamingProvider.Kick);
    }
    [Fact]
    public async Task BrokerEnforcesFixedClientCallbackAndDoesNotReturnServerSecret()
    {
        int calls = 0;
        var app = BrokerApplication.Build([], new("test-kick", "server-secret-canary"), new Handler(async request =>
        {
            calls++; Assert.Equal("https://id.kick.com/oauth/token", request.RequestUri!.AbsoluteUri);
            string body = await request.Content!.ReadAsStringAsync(); Assert.Contains("client_secret=server-secret-canary", body);
            return Json("""{"access_token":"kick-access","refresh_token":"kick-refresh","expires_in":3600}""");
        }));
        app.Urls.Add("http://127.0.0.1:0");
        await app.StartAsync();
        try
        {
            using var http = new HttpClient { BaseAddress = new Uri(app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single()) };
            http.DefaultRequestHeaders.Add("X-Temhalo-Client", "desktop");
            var form = new Dictionary<string,string> { ["client_id"] = "test-kick", ["grant_type"] = "authorization_code", ["redirect_uri"] = "https://evil.test/callback", ["code"] = "code", ["code_verifier"] = OAuthProof.Random() };
            using var rejected = await http.PostAsync("/oauth/token", new FormUrlEncodedContent(form)); Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode); Assert.Equal(0, calls);
            form["redirect_uri"] = "http://localhost:18473/oauth/callback";
            using var accepted = await http.PostAsync("/oauth/token", new FormUrlEncodedContent(form)); Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
            Assert.DoesNotContain("server-secret-canary", await accepted.Content.ReadAsStringAsync()); Assert.Equal(1, calls);
            http.DefaultRequestHeaders.Add("Origin", "https://evil.test");
            using var browser = await http.PostAsync("/oauth/token", new FormUrlEncodedContent(form)); Assert.Equal(HttpStatusCode.BadRequest, browser.StatusCode); Assert.Equal(1, calls);
        }
        finally { await app.StopAsync(); await app.DisposeAsync(); }
    }
}
