using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Temhalo.Infrastructure.Windows.Deployment;
using Temhalo.Infrastructure.Windows.Settings;
using Xunit;

namespace Temhalo.Infrastructure.Tests;

public sealed class UpdateTests
{
    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        { var response = await send(request, cancellationToken); response.RequestMessage = request; return response; }
    }
    private static ReleaseVersion Version(string value) => ReleaseVersion.TryParse(value, out var parsed) ? parsed : throw new ArgumentException(value);
    private static readonly byte[] InstallerBytes = Encoding.UTF8.GetBytes("MZ-fake-installer-only-for-unit-tests");
    private static readonly string Hash = Convert.ToHexString(SHA256.HashData(InstallerBytes)).ToLowerInvariant();
    private static UpdateRelease Release(string version = "0.7.0-preview.2") => new(Version(version),
        new Uri($"{GitHubUpdateService.Repository}/releases/download/v{version}/Temhalo-{version}-Setup-x64.exe"),
        new Uri($"{GitHubUpdateService.Repository}/releases/tag/v{version}"), InstallerBytes.Length, Hash);
    private static object Metadata(string version, bool draft = false, string? url = null, string? digest = null, long? bytes = null) => new
    {
        tag_name = "v" + version, draft, prerelease = version.Contains("preview", StringComparison.Ordinal),
        assets = new[] { new { name = $"Temhalo-{version}-Setup-x64.exe", state = "uploaded",
            browser_download_url = url ?? Release(version).DownloadUrl.AbsoluteUri, size = bytes ?? InstallerBytes.Length, digest = digest ?? "sha256:" + Hash } }
    };
    private static UpdateRelease? Select(bool previews, params object[] releases)
    {
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(releases));
        return GitHubUpdateService.SelectRelease(json.RootElement, Version("0.7.0-preview.1"), previews);
    }

    [Theory]
    [InlineData("0.7.0-preview.2", "0.7.0-preview.10")]
    [InlineData("0.7.0-preview.10", "0.7.0")]
    [InlineData("0.7.0", "0.7.1-preview.1")]
    [InlineData("0.7.9", "0.8.0")]
    public void VersionsUseNumericOrderingAndStableFollowsPreview(string lower, string higher)
        => Assert.True(Version(higher).CompareTo(Version(lower)) > 0);

    [Theory]
    [InlineData("../../installer")]
    [InlineData("01.2.3")]
    [InlineData("1.2.3-preview.65535")]
    [InlineData("65536.0.0")]
    [InlineData("1.0.0-beta.1")]
    [InlineData("1.0.0\n")]
    public void InvalidReleaseVersionsAreRejected(string value) => Assert.False(ReleaseVersion.TryParse(value, out _));

    [Fact]
    public void SelectionSkipsDraftsDowngradesAndPreviewWhenDisabled()
    {
        object[] releases = [Metadata("0.6.0"), Metadata("0.7.0-preview.1"), Metadata("0.7.0-preview.2"), Metadata("0.7.0"), Metadata("0.8.0-preview.1"), Metadata("9.0.0", draft: true)];
        Assert.Equal("0.7.0", Select(false, releases)!.Version.ToString());
        Assert.Equal("0.8.0-preview.1", Select(true, releases)!.Version.ToString());
        Assert.Null(Select(false, Metadata("0.7.0-preview.2")));
    }

    [Theory]
    [InlineData("http://github.com/Zeeroz/Temhalo/releases/download/v0.7.0/Temhalo-0.7.0-Setup-x64.exe")]
    [InlineData("https://github.com/another/repo/releases/download/v0.7.0/Temhalo-0.7.0-Setup-x64.exe")]
    [InlineData("https://example.test/installer.exe")]
    public void OnlyExactOfficialRepositoryAssetsAreAccepted(string url) => Assert.Null(Select(true, Metadata("0.7.0", url: url)));

    [Fact]
    public void MissingHashInvalidSizeAndNonInstallerAssetsAreRejected()
    {
        Assert.Null(Select(true, Metadata("0.7.0", digest: "")));
        Assert.Null(Select(true, Metadata("0.7.0", digest: "sha256:" + new string('z', 64))));
        Assert.Null(Select(true, Metadata("0.7.0", bytes: 0)));
        Assert.Null(Select(true, Metadata("0.7.0", bytes: GitHubUpdateService.MaximumInstallerBytes + 1)));
        using var json = JsonDocument.Parse("[{\"tag_name\":\"v0.7.0\",\"draft\":false,\"prerelease\":false,\"assets\":[{\"name\":\"Temhalo-0.7.0-Setup-x64.exe\",\"state\":\"uploaded\",\"size\":\"invalid\"}]}]");
        Assert.Null(GitHubUpdateService.SelectRelease(json.RootElement, Version("0.6.0"), true));
    }

    [Fact]
    public async Task CheckUsesPublicApiWithoutOAuthCredentials()
    {
        using var client = new HttpClient(new Handler((request, _) =>
        {
            Assert.Equal("api.github.com", request.RequestUri!.Host);
            Assert.Null(request.Headers.Authorization);
            Assert.Contains("Temhalo", request.Headers.UserAgent.ToString());
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(new[] { Metadata("0.7.0") })) });
        }));
        Assert.Equal("0.7.0", (await new GitHubUpdateService(client).CheckAsync(Version("0.6.0"), false, default))!.Version.ToString());
    }

    [Fact]
    public async Task DownloadVerifiesHashAndReverificationDetectsTampering()
    {
        string folder = Path.Combine(Path.GetTempPath(), "Temhalo-update-" + Guid.NewGuid().ToString("N"));
        using var client = Client(InstallerBytes);
        try
        {
            var update = await new GitHubUpdateService(client).DownloadAsync(Release(), folder, null, default);
            Assert.Equal(InstallerBytes, await File.ReadAllBytesAsync(update.FilePath));
            await GitHubUpdateService.VerifyAsync(update, default);
            byte[] damaged = (byte[])InstallerBytes.Clone(); damaged[^1] ^= 1;
            await File.WriteAllBytesAsync(update.FilePath, damaged);
            await Assert.ThrowsAsync<InvalidDataException>(() => GitHubUpdateService.VerifyAsync(update, default));
        }
        finally { DeleteTestFiles(folder); }
    }

    [Theory]
    [InlineData(0)] // Same length, wrong hash.
    [InlineData(-1)] // Truncated stream.
    [InlineData(1)] // Oversized stream.
    public async Task CorruptDownloadsNeverProduceAnExecutable(int difference)
    {
        string folder = Path.Combine(Path.GetTempPath(), "Temhalo-update-" + Guid.NewGuid().ToString("N"));
        byte[] damaged = new byte[InstallerBytes.Length + difference];
        using var client = Client(damaged, omitLength: true);
        try
        {
            await Assert.ThrowsAsync<InvalidDataException>(() => new GitHubUpdateService(client).DownloadAsync(Release(), folder, null, default));
            Assert.Empty(Directory.GetFiles(folder, "*", SearchOption.AllDirectories));
        }
        finally { DeleteTestFiles(folder); }
    }

    [Fact]
    public async Task CancelledDownloadLeavesNoExecutableOrPartialFile()
    {
        string folder = Path.Combine(Path.GetTempPath(), "Temhalo-update-" + Guid.NewGuid().ToString("N"));
        using var source = new CancellationTokenSource();
        using var client = new HttpClient(new Handler(async (_, token) => { source.Cancel(); await Task.Delay(Timeout.Infinite, token); return null!; }));
        try
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new GitHubUpdateService(client).DownloadAsync(Release(), folder, null, source.Token));
            Assert.Empty(Directory.GetFiles(folder, "*", SearchOption.AllDirectories));
        }
        finally { DeleteTestFiles(folder); }
    }

    [Fact]
    public async Task DevelopmentCopyNeverStartsAnInstaller()
    {
        string folder = Path.Combine(Path.GetTempPath(), "Temhalo-update-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            Assert.False(InstalledApplication.IsCurrentInstallation(folder));
            var update = new DownloadedUpdate(Release(), Path.Combine(folder, "nonexistent.exe"));
            await Assert.ThrowsAsync<InvalidOperationException>(() => InstalledApplication.LaunchInstallerAsync(update, folder, default));
            Assert.Empty(Directory.GetFiles(folder));
        }
        finally { DeleteTestFiles(folder); }
    }

    [Fact]
    public async Task UpdatePreferencesRoundTripWithExistingEncryptedSettings()
    {
        string folder = Path.Combine(Path.GetTempPath(), "Temhalo-update-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new DpapiSettingsStore(folder);
            await store.SaveAsync(new AppSettings { CheckUpdatesOnStartup = false, IncludePreviewUpdates = false, MemoryLimitMiB = 768 });
            var loaded = await store.LoadAsync();
            Assert.False(loaded.CheckUpdatesOnStartup); Assert.False(loaded.IncludePreviewUpdates); Assert.Equal(768, loaded.MemoryLimitMiB);
            // JSON files written before the updater existed still acquire safe defaults.
            Assert.True(JsonSerializer.Deserialize<AppSettings>("{\"Version\":1}")!.CheckUpdatesOnStartup);
        }
        finally { DeleteTestFiles(folder); }
    }

    private sealed class UnknownLengthContent(byte[] bytes) : HttpContent
    {
        protected override bool TryComputeLength(out long length) { length = 0; return false; }
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) => stream.WriteAsync(bytes).AsTask();
    }
    private static HttpClient Client(byte[] bytes, bool omitLength = false) => new(new Handler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        { Content = omitLength ? new UnknownLengthContent(bytes) : new ByteArrayContent(bytes) })));
    private static void DeleteTestFiles(string folder)
    {
        if (!Directory.Exists(folder)) return;
        foreach (string file in Directory.GetFiles(folder, "*", SearchOption.AllDirectories)) File.Delete(file);
        foreach (string directory in Directory.GetDirectories(folder, "*", SearchOption.AllDirectories).OrderByDescending(value => value.Length)) Directory.Delete(directory);
        Directory.Delete(folder);
    }
}
