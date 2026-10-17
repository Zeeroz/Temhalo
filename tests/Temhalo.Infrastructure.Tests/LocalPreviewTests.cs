using Temhalo.Infrastructure.Windows.Preview;
using Temhalo.Infrastructure.Windows.Processes;
using Microsoft.Extensions.Logging.Abstractions;
using System.Net;
using System.Net.Sockets;
using Xunit;

namespace Temhalo.Infrastructure.Tests;

public sealed class LocalPreviewTests
{
    [Theory]
    [InlineData("rtmp://live.twitch.tv/app", "private-key")]
    [InlineData("rtmp://127.0.0.1.evil.test:1936/live", "preview")]
    [InlineData("rtmp://localhost.evil.test:1936/live", "preview")]
    [InlineData("rtmp://127.0.0.1:1935/live", "stream")]
    [InlineData("rtmp://localhost/live", "stream")]
    [InlineData("rtmps://localhost:1936/live", "preview")]
    [InlineData("rtmp://user:password@localhost:1936/live", "preview")]
    [InlineData("rtmp://localhost:1936/live?token=secret", "preview")]
    [InlineData("rtmp://localhost:1936/live", "preview?token=secret")]
    [InlineData("rtmp://localhost:1936/live", "../stream")]
    [InlineData("rtmp://localhost:1936/live", "")]
    public void RemoteOutputsIngestAndAmbiguousPathsAreNotPreviewTargets(string server, string key)
        => Assert.Null(LocalPreviewTarget.TryCreate(server, key));

    [Theory]
    [InlineData("127.0.0.1", "127.0.0.1:1937")]
    [InlineData("localhost", "127.0.0.1:1937")]
    [InlineData("[::1]", "[::1]:1937")]
    public void LocalOutputDeterminesPortPathAndCorrectedPlayerProfile(string host, string listener)
    {
        var target = Assert.IsType<LocalPreviewTarget>(LocalPreviewTarget.TryCreate($"rtmp://{host}:1937/custom/path/", "local-key"));
        Assert.Equal(listener, target.ListenAddress);
        Assert.Equal("custom/path/local-key", target.StreamPath);
        var start = target.VlcStartInfo(@"C:\Program Files\VideoLAN\VLC\vlc.exe");
        Assert.False(start.UseShellExecute);
        Assert.Equal("rtmp://" + listener + "/custom/path/local-key", start.ArgumentList[^1]);
        Assert.Contains("--network-caching=150", start.ArgumentList);
        Assert.Contains("--clock-jitter=0", start.ArgumentList);
        Assert.Contains("--avio-options={rtmp_buffer=0}", start.ArgumentList);
        Assert.Contains("--no-one-instance", start.ArgumentList);
        Assert.DoesNotContain(start.ArgumentList, argument => argument.StartsWith("--clock-synchro=") || argument.Contains("rc-host"));
        Assert.DoesNotContain("local-key", target.ToString());
    }

    [Fact]
    public void ReceiverDoesNotPersistTheKeyAndExposesOnlyLoopbackRtmp()
    {
        var target = LocalPreviewTarget.TryCreate("rtmp://127.0.0.1:1936/live", "never-save-this-key")!;
        string config = LocalPreviewReceiver.Configuration(target);
        Assert.DoesNotContain("never-save-this-key", config);
        Assert.Contains("127.0.0.1:1936", config);
        foreach (string disabled in new[] { "api", "metrics", "pprof", "rtsp", "hls", "webrtc", "srt", "moq", "playback" })
            Assert.Contains(disabled + ": false", config);
    }

    [Fact]
    public async Task AnExistingListenerIsReusedAndNeverStoppedByThePreview()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var target = LocalPreviewTarget.TryCreate($"rtmp://127.0.0.1:{port}/live", "preview")!;
        await using (var receiver = new LocalPreviewReceiver("not-launched.exe", Path.GetTempPath(), NullLogger.Instance))
            await receiver.EnsureAsync(target, CancellationToken.None);
        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, port).WaitAsync(TimeSpan.FromSeconds(3));
        Assert.True(client.Connected);
    }
}
