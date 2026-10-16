using Temhalo.Core.Buffering;
using Temhalo.Core.Flv;
using Temhalo.Core.Timing;
using Xunit;
using static Temhalo.Core.Tests.FlvTestData;

namespace Temhalo.Core.Tests;

public sealed class FixedDelayTests
{
    private static FixedDelayOptions Options(int delay = 0) => new() { DelaySeconds = delay, TagCapacity = 128, MaximumBufferBytes = 4 * 1024 * 1024 };
    private static byte[] Sample => File((FlvTagType.Video, 900, Avc), (FlvTagType.Audio, 900, Aac),
        (FlvTagType.Video, 1000, Video(true, 40)), (FlvTagType.Audio, 1010, Audio()),
        (FlvTagType.Video, 1033, Video(false, -10)), (FlvTagType.Audio, 1034, Audio(2)));

    [Fact]
    public async Task FixedDelayNeverEmitsEarlyAndDrainsAfterInputEof()
    {
        var clock = new ManualMediaClock();
        var engine = new FixedDelayMediaTransfer(Options(5), clock);
        using var input = new MemoryStream(Sample);
        using var output = new MemoryStream();
        Task running = engine.TransferAsync(input, output, CancellationToken.None);
        await UntilAsync(() => engine.Snapshot.InputCompleted);
        Assert.Equal(0, engine.BytesTransferred);
        Assert.True(engine.Snapshot.BufferBytes > 0);
        clock.AdvanceTo(4999);
        Assert.Equal(0, engine.BytesTransferred);
        clock.AdvanceTo(5000);
        await running.WaitAsync(TimeSpan.FromSeconds(5));
        var tags = await ReadAllAsync(output.ToArray());
        Assert.Equal(new uint[] { 0, 0, 0, 10, 33, 34 }, tags.Select(t => t.Timestamp));
        Assert.Equal(Video(true, 40), tags[2].Payload);
        Assert.Equal(Video(false, -10), tags[4].Payload);
        Assert.Equal(5000, engine.Snapshot.MinimumMediaAgeMilliseconds);
        Assert.Equal(5000, engine.Snapshot.MaximumMediaAgeMilliseconds);
        Assert.Equal(0, engine.Snapshot.BufferBytes);
    }

    [Fact]
    public async Task WaitsForRealIdrAndReplaysBothCodecHeadersAfterTimestampReset()
    {
        byte[] wire = File((FlvTagType.Video, 0, Avc), (FlvTagType.Audio, 0, Aac),
            (FlvTagType.Video, 900, Video(false, keyFlag: true)), (FlvTagType.Audio, 950, Audio(0)),
            (FlvTagType.Video, 1000, Video(true)), (FlvTagType.Audio, 1010, Audio()),
            (FlvTagType.Video, 1033, Video(false)), (FlvTagType.Video, 0, Video(false)),
            (FlvTagType.Audio, 10, Audio(2)), (FlvTagType.Video, 100, Video(true)),
            (FlvTagType.Audio, 110, Audio(3)));
        var engine = new FixedDelayMediaTransfer(Options());
        using var output = new MemoryStream();
        await engine.TransferAsync(new MemoryStream(wire), output, CancellationToken.None);
        var tags = await ReadAllAsync(output.ToArray());
        Assert.Equal(9, tags.Count);
        Assert.Equal(Avc, tags[0].Payload); Assert.Equal(Aac, tags[1].Payload);
        Assert.Equal(Video(true), tags[2].Payload);
        Assert.Equal(Avc, tags[5].Payload); Assert.Equal(Aac, tags[6].Payload);
        Assert.Equal(34u, tags[7].Timestamp); Assert.Equal(44u, tags[8].Timestamp);
        Assert.Equal(1, engine.Snapshot.Discontinuities);
        Assert.Equal(4, engine.Snapshot.SkippedMediaTags);
        Assert.Equal(0, engine.Snapshot.BufferBytes);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CodecChangeUsesConfigurationOfDueGenerationAndRequiresNextIdr(bool audioChange)
    {
        byte[] changed = audioChange ? Aac : Avc;
        if (audioChange) changed[3] = 0x88; else changed[7] = 1;
        var clock = new ManualMediaClock();
        byte[] wire = File((FlvTagType.Video, 0, Avc), (FlvTagType.Audio, 0, Aac),
            (FlvTagType.Video, 0, Video(true)), (FlvTagType.Audio, 10, Audio()),
            (audioChange ? FlvTagType.Audio : FlvTagType.Video, 50, changed), (FlvTagType.Video, 66, Video(false)),
            (FlvTagType.Video, 100, Video(true)));
        var engine = new FixedDelayMediaTransfer(Options(1), clock);
        using var output = new MemoryStream();
        Task running = engine.TransferAsync(new MemoryStream(wire), output, CancellationToken.None);
        await UntilAsync(() => engine.Snapshot.InputCompleted);
        clock.AdvanceTo(1000);
        await running.WaitAsync(TimeSpan.FromSeconds(5));
        var tags = await ReadAllAsync(output.ToArray());
        Assert.Equal(Avc, tags[0].Payload);
        Assert.Equal(audioChange ? Avc : changed, tags[4].Payload);
        Assert.Equal(audioChange ? changed : Aac, tags[5].Payload);
        Assert.Equal(100u, tags[6].Timestamp);
        Assert.Equal(1, engine.Snapshot.Discontinuities);
    }

    [Fact]
    public async Task TimestampWrapKeepsOneAudioVideoOffsetAndCompositionBytes()
    {
        byte[] wire = File((FlvTagType.Video, 0, Avc), (FlvTagType.Audio, 0, Aac),
            (FlvTagType.Video, uint.MaxValue - 20, Video(true, 40)),
            (FlvTagType.Audio, uint.MaxValue - 10, Audio()),
            (FlvTagType.Video, 12, Video(false, -10)), (FlvTagType.Audio, 13, Audio()));
        var engine = new FixedDelayMediaTransfer(Options());
        using var output = new MemoryStream();
        await engine.TransferAsync(new MemoryStream(wire), output, CancellationToken.None);
        var tags = await ReadAllAsync(output.ToArray());
        Assert.Equal(new uint[] { 0, 0, 0, 10, 33, 34 }, tags.Select(t => t.Timestamp));
        Assert.Equal(0, engine.Snapshot.Discontinuities);
    }

    [Fact]
    public async Task PartialTailDrainsCompleteTagsAndReportsTruncation()
    {
        byte[] wire = Sample;
        var engine = new FixedDelayMediaTransfer(Options());
        using var output = new MemoryStream();
        await engine.TransferAsync(new MemoryStream(wire[..^2]), output, CancellationToken.None);
        Assert.True(engine.Snapshot.TruncatedInput);
        Assert.Equal(5, (await ReadAllAsync(output.ToArray())).Count);
        Assert.Equal(0, engine.Snapshot.BufferBytes);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task MemoryOrRingLimitFailsClosedAndReleasesEveryAllocation(bool memoryLimit)
    {
        byte[] wire = File(Enumerable.Range(0, 50).Select(i => (FlvTagType.Script, (uint)i, new byte[1024])).ToArray());
        var options = new FixedDelayOptions { DelaySeconds = 300, MaximumBufferBytes = memoryLimit ? 5000 : 1024 * 1024,
            TagCapacity = memoryLimit ? 64 : 2 };
        var engine = new FixedDelayMediaTransfer(options, new ManualMediaClock());
        await Assert.ThrowsAsync<BufferLimitExceededException>(() => engine.TransferAsync(new MemoryStream(wire), Stream.Null, CancellationToken.None));
        Assert.InRange(engine.Snapshot.PeakBufferBytes, 1, options.MaximumBufferBytes);
        Assert.Equal(0, engine.Snapshot.BufferBytes);
        Assert.Equal(0, engine.BytesTransferred);
    }

    [Fact]
    public async Task CancellationWhileWaitingForDeadlineReleasesBuffer()
    {
        var engine = new FixedDelayMediaTransfer(Options(300), new ManualMediaClock());
        using var cancellation = new CancellationTokenSource();
        Task running = engine.TransferAsync(new MemoryStream(Sample), Stream.Null, cancellation.Token);
        await UntilAsync(() => engine.Snapshot.InputCompleted);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => running.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(0, engine.Snapshot.BufferBytes);
    }

    [Fact]
    public void RejectsInvalidOptionsAndUnrepresentableOutputTimestamps()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new FixedDelayMediaTransfer(Options(-1)));
        Assert.Throws<ArgumentOutOfRangeException>(() => new FixedDelayMediaTransfer(Options(301)));
        Assert.Throws<ArgumentOutOfRangeException>(() => new FixedDelayMediaTransfer(Options() with { WarningUsageRatio = double.NaN }));
        var timeline = new FlvTimestampRewriter();
        timeline.BeginSegment(0);
        Assert.Throws<InvalidDataException>(() => timeline.Map((long)uint.MaxValue + 1));
    }

    [Fact]
    public async Task ResumeDoesNotOverlapPreviousPresentationWindow()
    {
        byte[] wire = File((FlvTagType.Video, 0, Avc), (FlvTagType.Audio, 0, Aac),
            (FlvTagType.Video, 1000, Video(true, 80)), (FlvTagType.Video, 1033, Video(false, 80)),
            (FlvTagType.Audio, 1034, Audio()), (FlvTagType.Video, 0, Video(true)),
            (FlvTagType.Audio, 10, Audio(2)));
        var engine = new FixedDelayMediaTransfer(Options());
        using var output = new MemoryStream();
        await engine.TransferAsync(new MemoryStream(wire), output, CancellationToken.None);
        var tags = await ReadAllAsync(output.ToArray());
        Assert.Equal(114u, tags[7].Timestamp); // Previous video PTS = 33 + 80.
        Assert.Equal(124u, tags[8].Timestamp); // Same A/V offset, not independent clamping.
    }

    internal static async Task UntilAsync(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (!condition()) await Task.Delay(1, timeout.Token);
    }
}
