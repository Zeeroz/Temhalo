using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text;
using Temhalo.Infrastructure.Windows.LocalWeb;
using Temhalo.Infrastructure.Windows.Settings;
using Xunit;

namespace Temhalo.Infrastructure.Tests;

public sealed class LocalControlServerTests
{
    [Fact]
    public async Task CapabilitiesOriginHostAndValidationProtectControlAndState()
    {
        var access = new LocalAccessSettings(); int target = 0, commands = 0;
        LocalViewState State() => new(false, true, target, null, "Stopped", false, [0, 15, 30, 60], 300, []);
        await using var server = new LocalControlServer(access, State, (command, _) =>
        { commands++; target = command.Seconds ?? 300; return Task.FromResult(new DelayCommandResult(true, target, "Appliqué")); });
        await server.StartAsync(dynamicPortForTests: true);
        using var client = new HttpClient { BaseAddress = server.Address };
        Assert.Equal("127.0.0.1", server.Address!.Host);
        Assert.DoesNotContain(access.DockToken, await client.GetStringAsync("dock"));
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("api/state")).StatusCode);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", new string('A', 64));
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("api/state")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("api/delay", new DelayCommand("set", 30))).StatusCode);
        Assert.Equal(0, commands);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", access.DockToken);
        var state = await client.GetStringAsync("api/state"); Assert.DoesNotContain(access.DockToken, state);
        using var foreign = new HttpRequestMessage(HttpMethod.Post, "api/delay")
        { Content = JsonContent.Create(new DelayCommand("set", 30)) };
        foreign.Headers.Add("Origin", "https://example.invalid");
        Assert.Equal(HttpStatusCode.Forbidden, (await client.SendAsync(foreign)).StatusCode);
        using var rebound = new HttpRequestMessage(HttpMethod.Get, "api/state"); rebound.Headers.Host = "example.invalid";
        Assert.Equal(HttpStatusCode.Forbidden, (await client.SendAsync(rebound)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("api/delay", new DelayCommand("set", 301))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("api/delay", new DelayCommand("stop"))).StatusCode);
        Assert.Equal(HttpStatusCode.UnsupportedMediaType, (await client.PostAsync("api/delay", new StringContent("seconds=30"))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("api/delay", new DelayCommand("set", 30))).StatusCode);
        Assert.Equal(30, target); Assert.Equal(1, commands);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("overlay")).StatusCode);
        using var response = await client.GetAsync("dock");
        Assert.DoesNotContain("id=\"overlay\"", await response.Content.ReadAsStringAsync());
        Assert.Equal("no-referrer", response.Headers.GetValues("Referrer-Policy").Single());
        Assert.False(response.Headers.Contains("Access-Control-Allow-Origin"));
        Assert.True((await client.GetByteArrayAsync("assets/Inter.ttf")).Length > 1000);
    }

    [Fact]
    public async Task OccupiedPortIsReportedAndExistingListenerSurvives()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
        try
        {
            var access = new LocalAccessSettings { Port = ((IPEndPoint)listener.LocalEndpoint).Port };
            await using var server = new LocalControlServer(access,
                () => new(false, false, 0, null, "Stopped", false, [0], 300, []),
                (_, _) => throw new InvalidOperationException());
            await Assert.ThrowsAnyAsync<IOException>(() => server.StartAsync());
            using var client = new TcpClient(); await client.ConnectAsync(IPAddress.Loopback, access.Port);
            Assert.True(client.Connected);
        }
        finally { listener.Stop(); }
    }

    [Fact]
    public async Task DestinationsAndStablePrivateLinksAreEncryptedTogether()
    {
        string directory = Path.Combine(Path.GetTempPath(), "Temhalo-links-" + Guid.NewGuid().ToString("N"));
        var store = new DpapiSettingsStore(directory); var access = new LocalAccessSettings();
        var settings = new AppSettings { LocalAccess = access, Destinations = [
            new() { Name = "Première", ServerUrl = "rtmp://127.0.0.1:1936/live", StreamKey = "canary-one" },
            new() { Name = "Seconde", ServerUrl = "rtmp://127.0.0.1:1937/live", StreamKey = "canary-two" }] };
        try
        {
            await store.SaveAsync(settings); var loaded = await store.LoadAsync();
            Assert.Equal(access, loaded.LocalAccess); Assert.Equal(settings.Destinations, loaded.Destinations);
            byte[] encrypted = await File.ReadAllBytesAsync(store.FilePath);
            foreach (string secret in new[] { access.DockToken, "canary-one", "canary-two" })
                Assert.True(encrypted.AsSpan().IndexOf(Encoding.UTF8.GetBytes(secret)) < 0);
            Assert.Throws<ArgumentException>(() => (settings with { Destinations = [settings.Destinations[0], settings.Destinations[0]] }).Validate());
        }
        finally { if (File.Exists(store.FilePath)) File.Delete(store.FilePath); if (Directory.Exists(directory)) Directory.Delete(directory); }
    }
}
