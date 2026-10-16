using Temhalo.Infrastructure.Windows.Processes;
using Xunit;

namespace Temhalo.Infrastructure.Tests;

public sealed class DestinationTests
{
    [Theory]
    [InlineData("https://example.test/app")]
    [InlineData("rtmp://user:password@example.test/app")]
    [InlineData("rtmp://example.test/app?key=private")]
    [InlineData("rtmp://example.test/app#private")]
    public void RejectsAmbiguousOrSecretBearingServerUrls(string url)
        => Assert.Throws<ArgumentException>(() => new RelayDestination(url, "test-key"));

    [Fact]
    public void NetworkArgumentsKeepCopyAndSessionKeyIsNotPrinted()
    {
        var destination = new RelayDestination("rtmps://example.test/app/", "session-only-key");
        Assert.DoesNotContain("session-only-key", destination.ToString());
        var arguments = LocalRelaySupervisor.EmitterArguments(destination.PublishUrl, network: true);
        Assert.Equal("rtmps://example.test/app/session-only-key", arguments[^1]);
        Assert.Contains("copy", arguments); Assert.Contains("10000000", arguments); Assert.Contains("no_duration_filesize", arguments);
    }

    [Fact]
    public void PublisherTrackerDistinguishesPublisherFromReaderAndProbe()
    {
        var tracker = new PublisherTracker();
        tracker.Observe("{\"message\":\"[RTMP] [conn 127.0.0.1:100] is publishing to path 'live/stream'\"}");
        Assert.True(tracker.Connected);
        tracker.Observe("{\"message\":\"[RTMP] [conn 127.0.0.1:101] closed: EOF\"}");
        Assert.True(tracker.Connected);
        tracker.Observe("{\"message\":\"[RTMP] [conn 127.0.0.1:100] closed: EOF\"}");
        Assert.False(tracker.Connected);
    }
}
