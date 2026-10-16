using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using Temhalo.Infrastructure.Windows.Deployment;
using Xunit;

namespace Temhalo.Infrastructure.Tests;

public sealed class DeploymentTests
{
    [Fact]
    public void PortableUsesItsOwnFilesAndUserDataEvenInsideAnotherSourceTree()
    {
        using var fixture = new Fixture();
        string app = Path.Combine(fixture.Root, "install with spaces"); Directory.CreateDirectory(Path.Combine(app, "config"));
        File.WriteAllText(Path.Combine(fixture.Root, "Temhalo.sln"), "");
        File.WriteAllText(Path.Combine(app, "temhalo.portable.json"), "{}");
        File.WriteAllText(Path.Combine(app, "config", "native-tools.lock.json"), "{}");
        File.WriteAllText(Path.Combine(app, "config", "mediamtx.yml"), "");
        var layout = ApplicationLayout.Resolve(app, fixture.Root, Path.Combine(fixture.Root, "user"));
        Assert.True(layout.IsPortable); Assert.Equal(app, layout.Root);
        Assert.StartsWith(Path.Combine(fixture.Root, "user"), layout.SessionDirectory);
        Assert.StartsWith(Path.Combine(fixture.Root, "user"), layout.NativeCacheDirectory);
    }
    [Fact]
    public void IncompletePortableDoesNotFallBackToSourceTree()
    {
        using var fixture = new Fixture();
        File.WriteAllText(Path.Combine(fixture.Root, "temhalo.portable.json"), "{}");
        File.WriteAllText(Path.Combine(fixture.Root, "Temhalo.sln"), "");
        Assert.Throws<IOException>(() => ApplicationLayout.Resolve(fixture.Root, fixture.Root, fixture.Root));
    }
    [Fact]
    public void DevelopmentKeepsOriginalSessionLocation()
    {
        using var fixture = new Fixture();
        File.WriteAllText(Path.Combine(fixture.Root, "Temhalo.sln"), "");
        var layout = ApplicationLayout.Resolve(Path.Combine(fixture.Root, "bin"), fixture.Root, Path.Combine(fixture.Root, "user"));
        Assert.False(layout.IsPortable); Assert.Equal(Path.Combine(fixture.Root, "artifacts", "relay-ui"), layout.SessionDirectory);
    }
    [Fact]
    public async Task VerifiedDownloadIsReusableOfflineAndChangedDllIsRejected()
    {
        using var fixture = new Fixture();
        var first = await fixture.Provisioner.EnsureAsync();
        Assert.True(File.Exists(first.MediaMtx)); Assert.True(File.Exists(first.Ffmpeg));
        Assert.Equal(2, fixture.Handler.Requests);
        fixture.Handler.Offline = true;
        var second = await fixture.Provisioner.EnsureAsync();
        Assert.Equal(first, second); Assert.Equal(2, fixture.Handler.Requests);
        File.WriteAllText(Path.Combine(Path.GetDirectoryName(first.Ffmpeg)!, "codec.dll"), "tampered");
        await Assert.ThrowsAsync<InvalidDataException>(() => fixture.Provisioner.EnsureAsync());
    }
    [Fact]
    public async Task WrongArchiveHashNeverPublishesExecutableAndCleansPartialFile()
    {
        using var fixture = new Fixture(wrongHash: true);
        await Assert.ThrowsAsync<InvalidDataException>(() => fixture.Provisioner.EnsureAsync());
        Assert.Empty(Directory.EnumerateFiles(fixture.Cache, "*.exe", SearchOption.AllDirectories));
        Assert.Empty(Directory.EnumerateFiles(fixture.Cache, "*.partial-*"));
    }
    [Fact]
    public async Task TraversalArchiveIsRejectedWithoutWritingOutsideCache()
    {
        using var fixture = new Fixture(traversal: true);
        await Assert.ThrowsAsync<InvalidDataException>(() => fixture.Provisioner.EnsureAsync());
        Assert.False(File.Exists(Path.Combine(fixture.Root, "escape.exe")));
        Assert.Empty(Directory.EnumerateDirectories(fixture.Cache, "staging-*"));
    }
    [Fact]
    public async Task OfflineFirstRunCanRetryWithoutPartialInstallation()
    {
        using var fixture = new Fixture(); fixture.Handler.Offline = true;
        await Assert.ThrowsAsync<HttpRequestException>(() => fixture.Provisioner.EnsureAsync());
        Assert.Empty(Directory.EnumerateFiles(fixture.Cache, "*.partial-*"));
        fixture.Handler.Offline = false;
        Assert.True(File.Exists((await fixture.Provisioner.EnsureAsync()).Ffmpeg));
    }
    [Fact]
    public async Task CancellationDuringAcquisitionReleasesLockAndAllowsRetry()
    {
        using var fixture = new Fixture(); fixture.Handler.DelayMilliseconds = 2000;
        using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fixture.Provisioner.EnsureAsync(token: cancel.Token));
        Assert.Empty(Directory.EnumerateFiles(fixture.Cache, "*.partial-*"));
        fixture.Handler.DelayMilliseconds = 10;
        Assert.True(File.Exists((await fixture.Provisioner.EnsureAsync()).Ffmpeg));
    }
    [Fact]
    public async Task ConcurrentPreparationsSerializeAndHonorCancellation()
    {
        using var fixture = new Fixture();
        var results = await Task.WhenAll(fixture.Provisioner.EnsureAsync(), fixture.Provisioner.EnsureAsync());
        Assert.Equal(results[0], results[1]); Assert.Equal(2, fixture.Handler.Requests);
        using var cancel = new CancellationTokenSource(); cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fixture.Provisioner.EnsureAsync(token: cancel.Token));
    }

    private sealed class Fixture : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "Temhalo-deployment-" + Guid.NewGuid().ToString("N"));
        public string Cache => Path.Combine(Root, "cache");
        public FakeHttp Handler { get; } = new();
        private readonly HttpClient _http;
        public NativeToolProvisioner Provisioner { get; }
        public Fixture(bool wrongHash = false, bool traversal = false)
        {
            Directory.CreateDirectory(Root); _http = new(Handler);
            byte[] mtx = Archive((traversal ? "../escape.exe" : "mediamtx.exe", "mtx"));
            byte[] ff = Archive(("ffroot/bin/ffmpeg.exe", "ff"), ("ffroot/bin/codec.dll", "codec"));
            Handler.Files.Add("/mtx.zip", mtx); Handler.Files.Add("/ff.zip", ff);
            object Tool(string name, string file, string directory, byte[] bytes, string root, string executable) => new
            {
                Name = name, Version = "test", Url = "https://github.com/" + file,
                Sha256 = wrongHash ? new string('0', 64) : Convert.ToHexString(SHA256.HashData(bytes)),
                ArchiveName = file, ArchiveRoot = root, DirectoryName = directory, Executable = executable
            };
            string manifest = Path.Combine(Root, "lock.json");
            File.WriteAllText(manifest, JsonSerializer.Serialize(new { SchemaVersion = 1, Architecture = "win-x64", Tools = new[]
            { Tool("MediaMTX", "mtx.zip", "mediamtx", mtx, "", "mediamtx.exe"), Tool("FFmpeg", "ff.zip", "ffmpeg", ff, "ffroot", "bin/ffmpeg.exe") } }));
            Provisioner = new(manifest, Cache, _http);
        }
        public void Dispose() { _http.Dispose(); Directory.Delete(Root, recursive: true); }
        private static byte[] Archive(params (string Path, string Text)[] entries)
        {
            using var memory = new MemoryStream();
            using (var zip = new ZipArchive(memory, ZipArchiveMode.Create, true))
                foreach (var entry in entries) { using var writer = new StreamWriter(zip.CreateEntry(entry.Path).Open()); writer.Write(entry.Text); }
            return memory.ToArray();
        }
    }
    private sealed class FakeHttp : HttpMessageHandler
    {
        public Dictionary<string, byte[]> Files { get; } = new();
        public bool Offline; public int Requests; public int DelayMilliseconds = 10;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Interlocked.Increment(ref Requests); await Task.Delay(DelayMilliseconds, token);
            if (Offline) throw new HttpRequestException("Synthetic offline");
            return new(HttpStatusCode.OK) { Content = new ByteArrayContent(Files[request.RequestUri!.AbsolutePath]) };
        }
    }
}
