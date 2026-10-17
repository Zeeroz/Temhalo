using System.Threading.Channels;
using Temhalo.Core.Abstractions;
using Temhalo.Core.Buffering;
using Temhalo.Core.Flv;
using Temhalo.Core.Timing;
using Xunit;
using static Temhalo.Core.Tests.FlvTestData;

namespace Temhalo.Core.Tests;

public sealed class ReplayDelayTests
{
    [Fact]
    public async Task FourDeckCommandsReplayWithoutPausesAndZeroKeepsTheSameOutputTimeline()
    {
        await using var test = new Harness();
        for (int i = 0; i <= 30; i++) await test.Feed(i);
        test.Control.SetTarget(15);
        await FixedDelayTests.UntilAsync(() => test.Engine.Snapshot.Control.CompletedReplays == 1);
        Assert.Equal(15000, test.Engine.Snapshot.LastMediaAgeMilliseconds);
        for (int i = 31; i <= 35; i++)
        {
            test.Clock.AdvanceTo(i * 1000L); test.Input.Frame(i);
            await FixedDelayTests.UntilAsync(() => test.Engine.Snapshot.LastMediaAgeMilliseconds == 15000 &&
                test.Engine.Snapshot.EmittedMediaTags >= 64 + (i - 30) * 2);
        }
        test.Control.SetTarget(25);
        await FixedDelayTests.UntilAsync(() => test.Engine.Snapshot.Control.CompletedReplays == 2);
        Assert.Equal(25000, test.Engine.Snapshot.LastMediaAgeMilliseconds);
        test.Control.SetTarget(15);
        await FixedDelayTests.UntilAsync(() => test.Engine.Snapshot.Control.CompletedSeeks == 1);
        Assert.Equal(15000, test.Engine.Snapshot.LastMediaAgeMilliseconds);
        test.Control.SetTarget(0);
        await FixedDelayTests.UntilAsync(() => test.Engine.Snapshot.Control.CompletedSeeks == 2);
        Assert.Equal(0, test.Engine.Snapshot.LastMediaAgeMilliseconds);
        Assert.Equal(0, test.Engine.Snapshot.Control.CompletedPauseSteps);
        Assert.Equal(0, test.Engine.Snapshot.OutputReconnections);
        await test.Finish();
        var tags = await ReadAllAsync(test.Output.ToArray());
        var video = tags.Where(t => t.Type == FlvTagType.Video && t.Payload[1] == 1).ToArray();
        Assert.Equal(new byte[] { 15, 16, 17, 18, 19, 20, 10, 20, 35 }, video.Skip(31).Select(t => t.Payload[^1]));
        AssertTimeline(tags);
        Assert.Equal(0, test.Engine.Snapshot.BufferBytes);
    }

    [Fact]
    public async Task ShortHistoryIsReplayedFirstAndOnlyMissingTimeIsPaused()
    {
        await using var test = new Harness();
        for (int i = 0; i <= 3; i++) await test.Feed(i);
        test.Control.SetTarget(6);
        await FixedDelayTests.UntilAsync(() => test.Engine.Snapshot.Control.CompletedReplays == 1);
        Assert.Equal(3000, test.Engine.Snapshot.Control.ActiveDelayMilliseconds);
        test.Clock.AdvanceTo(4000); test.Input.Frame(4);
        await FixedDelayTests.UntilAsync(() => test.Engine.Snapshot.Control.IsPaused);
        Assert.Equal(3000, test.Engine.Snapshot.Control.PauseRemainingMilliseconds);
        test.Clock.AdvanceTo(7000); test.Input.Frame(7);
        await FixedDelayTests.UntilAsync(() => test.Engine.Snapshot.Control.CompletedPauseSteps == 1 &&
            test.Engine.Snapshot.LastMediaAgeMilliseconds == 6000);
        Assert.Equal(3000, test.Engine.Snapshot.Control.LastPauseMilliseconds);
        await test.Finish(20000);
        AssertTimeline(await ReadAllAsync(test.Output.ToArray()));
        Assert.Equal(0, test.Engine.Snapshot.BufferBytes);
    }

    [Fact]
    public async Task ConsecutiveIncreasesAndImmediateZeroCanStayInsideRetainedHistory()
    {
        await using var test = new Harness();
        for (int i = 0; i <= 30; i++) await test.Feed(i);
        test.Control.SetTarget(15);
        await FixedDelayTests.UntilAsync(() => test.Engine.Snapshot.Control.CompletedReplays == 1);
        test.Control.SetTarget(25);
        await FixedDelayTests.UntilAsync(() => test.Engine.Snapshot.Control.CompletedReplays == 2);
        Assert.Equal(25000, test.Engine.Snapshot.LastMediaAgeMilliseconds);
        test.Control.SetTarget(0);
        await FixedDelayTests.UntilAsync(() => test.Engine.Snapshot.Control.CompletedSeeks == 1);
        Assert.Equal(0, test.Engine.Snapshot.LastMediaAgeMilliseconds);
        Assert.Equal(0, test.Engine.Snapshot.Control.CompletedPauseSteps);
        await test.Finish();
        var tags = await ReadAllAsync(test.Output.ToArray());
        Assert.Equal(new byte[] { 15, 5, 30 }, tags.Where(t => t.Type == FlvTagType.Video && t.Payload[1] == 1)
            .Skip(31).Select(t => t.Payload[^1]));
        AssertTimeline(tags);
    }

    [Fact]
    public async Task ZeroInterruptsTheMissingHistoryPauseWithoutWaitingForItsEnd()
    {
        await using var test = new Harness();
        for (int i = 0; i <= 3; i++) await test.Feed(i);
        test.Control.SetTarget(30);
        await FixedDelayTests.UntilAsync(() => test.Engine.Snapshot.Control.CompletedReplays == 1);
        test.Clock.AdvanceTo(4000); test.Input.Frame(4);
        await FixedDelayTests.UntilAsync(() => test.Engine.Snapshot.Control.IsPaused && test.Engine.Snapshot.ReadTags == 12);
        test.Clock.AdvanceTo(4500); test.Control.SetTarget(0);
        await FixedDelayTests.UntilAsync(() => test.Engine.Snapshot.Control.CompletedSeeks == 1);
        Assert.False(test.Engine.Snapshot.Control.IsPaused);
        Assert.Equal(500, test.Engine.Snapshot.Control.MaximumPauseMilliseconds);
        Assert.Equal(500, test.Engine.Snapshot.LastMediaAgeMilliseconds);
        await test.Finish();
        AssertTimeline(await ReadAllAsync(test.Output.ToArray()));
    }

    [Fact]
    public async Task ReplayAcrossObsReconnectRestoresHeadersAndKeepsTheOutputOpen()
    {
        var clock = new ManualMediaClock(); var control = new DelayController();
        var sources = Channel.CreateUnbounded<Stream>();
        using var output = new MemoryStream(); using var abort = new CancellationTokenSource();
        var connection = new OutputConnection(output);
        var engine = new FlvDelayMediaTransfer(new(), clock, control);
        Task running = engine.TransferSessionsAsync(ReadInputs(sources.Reader), connection, abort.Token);
        try
        {
            sources.Writer.TryWrite(new MemoryStream(File((FlvTagType.Video, 1000, Avc), (FlvTagType.Audio, 1000, Aac),
                (FlvTagType.Video, 1000, Video(true)), (FlvTagType.Audio, 1010, Audio(1)))));
            await FixedDelayTests.UntilAsync(() => engine.Snapshot.EmittedMediaTags == 2);
            clock.AdvanceTo(1000);
            byte[] changed = Avc; changed[7] = 2;
            sources.Writer.TryWrite(new MemoryStream(File((FlvTagType.Video, 0, changed), (FlvTagType.Audio, 0, Aac),
                (FlvTagType.Video, 0, Video(true)), (FlvTagType.Audio, 10, Audio(2)))));
            await FixedDelayTests.UntilAsync(() => engine.Snapshot.EmittedMediaTags == 4);
            control.SetTarget(1);
            await FixedDelayTests.UntilAsync(() => engine.Snapshot.Control.CompletedReplays == 1);
            clock.AdvanceTo(2000); sources.Writer.TryComplete();
            await running.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(1, connection.Opens);
            var tags = await ReadAllAsync(output.ToArray());
            Assert.Equal(new[] { Avc, changed, Avc, changed }, tags.Where(t => t.Type == FlvTagType.Video && t.Payload[1] == 0).Select(t => t.Payload));
            AssertTimeline(tags);
            Assert.Equal(0, engine.Snapshot.BufferBytes);
        }
        finally { abort.Cancel(); sources.Writer.TryComplete(); try { await running; } catch (OperationCanceledException) { } }
    }

    private static async IAsyncEnumerable<Stream> ReadInputs(ChannelReader<Stream> reader)
    { await foreach (var input in reader.ReadAllAsync()) { using (input) yield return input; } }

    private sealed class OutputConnection(Stream stream) : IFlvOutputConnection
    {
        public int Opens { get; private set; }
        public ValueTask<Stream> OpenAsync(CancellationToken token) { Opens++; return ValueTask.FromResult(stream); }
        public ValueTask CloseAsync(bool failed, CancellationToken token) => ValueTask.CompletedTask;
    }

    [Fact]
    public async Task PendingLivePacketIsNotLostWhenAReplayInterruptsItsDeadline()
    {
        await using var test = new Harness(new() { DelaySeconds = 1 });
        test.Input.Frame(0);
        await FixedDelayTests.UntilAsync(() => test.Engine.Snapshot.ReadTags == 4);
        test.Clock.AdvanceTo(1000);
        await FixedDelayTests.UntilAsync(() => test.Engine.Snapshot.EmittedMediaTags == 2);
        for (int i = 1; i <= 5; i++)
        {
            test.Input.Frame(i);
            await FixedDelayTests.UntilAsync(() => test.Engine.Snapshot.ReadTags == 2 + (i + 1) * 2);
            test.Clock.AdvanceTo((i + 1) * 1000);
            await FixedDelayTests.UntilAsync(() => test.Engine.Snapshot.EmittedMediaTags >= (i + 1) * 2);
        }
        test.Input.Frame(6); // Dequeued but due at wall time 7s; interrupt it at 6s.
        await FixedDelayTests.UntilAsync(() => test.Engine.Snapshot.ReadTags == 16);
        test.Control.SetTarget(3);
        await FixedDelayTests.UntilAsync(() => test.Engine.Snapshot.Control.CompletedReplays == 1);
        await test.Finish(20000);
        var tags = await ReadAllAsync(test.Output.ToArray());
        Assert.Equal(new byte[] { 0, 1, 2, 3, 4, 5, 3, 4, 5, 6 },
            tags.Where(t => t.Type == FlvTagType.Video && t.Payload[1] == 1).Select(t => t.Payload[^1]));
        AssertTimeline(tags);
    }

    [Fact]
    public async Task ReplayRestoresTheCodecPairAtItsKeyframeAndLaterRejoinsTheNewPair()
    {
        await using var test = new Harness();
        for (int i = 0; i <= 4; i++) await test.Feed(i);
        byte[] changedAvc = Avc, changedAac = Aac; changedAvc[7] = 1; changedAac[3] = 0x88;
        test.Input.Tag(FlvTagType.Video, 5000, changedAvc);
        test.Input.Tag(FlvTagType.Audio, 5000, changedAac);
        for (int i = 5; i <= 10; i++) await test.Feed(i);
        long before = test.Engine.Snapshot.EmittedMediaTags;
        test.Control.SetTarget(10);
        await FixedDelayTests.UntilAsync(() => test.Engine.Snapshot.Control.CompletedReplays == 1);
        var partial = await ReadAllAsync(test.Output.ToArray());
        int replayIdr = partial.FindLastIndex(t => t.Type == FlvTagType.Video && t.Payload[1] == 1 && t.Payload[^1] == 0);
        Assert.Equal(Avc, partial[replayIdr - 2].Payload);
        Assert.Equal(Aac, partial[replayIdr - 1].Payload);
        test.Clock.AdvanceTo(15000);
        await FixedDelayTests.UntilAsync(() => test.Engine.Snapshot.EmittedMediaTags >= before + 12);
        await test.Finish(30000);
        var tags = await ReadAllAsync(test.Output.ToArray());
        Assert.Equal(2, tags.Count(t => t.Payload.SequenceEqual(changedAvc)));
        Assert.Equal(2, tags.Count(t => t.Payload.SequenceEqual(changedAac)));
        AssertTimeline(tags);
        Assert.Equal(0, test.Engine.Snapshot.BufferBytes);
    }

    [Fact]
    public async Task HistoryRollsForMoreThanFiveMinutesWithoutGrowingForeverAtZero()
    {
        await using var test = new Harness(new() { TagCapacity = 2048, MaximumBufferBytes = 2 * 1024 * 1024 });
        for (int i = 0; i <= 350; i++) await test.Feed(i);
        Assert.InRange(test.Engine.Snapshot.HistoryAvailableMilliseconds, 300000, 300000);
        Assert.InRange(test.Engine.Snapshot.HistoryTags, 600, 606);
        Assert.True(test.Engine.Snapshot.HistoryEvictedTags > 0);
        Assert.Equal(0, test.Engine.Snapshot.LastMediaAgeMilliseconds);
        Assert.InRange(test.Engine.Snapshot.BufferBytes, 1, 2 * 1024 * 1024);
        test.Control.SetTarget(300);
        await FixedDelayTests.UntilAsync(() => test.Engine.Snapshot.Control.CompletedReplays == 1);
        Assert.Equal(300000, test.Engine.Snapshot.LastMediaAgeMilliseconds);
        Assert.False(test.Engine.Snapshot.Control.IsPaused);
        await test.Finish(700000);
        Assert.Equal(0, test.Engine.Snapshot.BufferBytes);
    }

    [Fact]
    public async Task MemoryPressureEvictsHistoryWithoutDroppingUnreadOrLiveMedia()
    {
        await using var test = new Harness(new() { TagCapacity = 64, MaximumBufferBytes = 1024 * 1024 });
        for (int i = 0; i < 12; i++)
        {
            test.Clock.AdvanceTo(i * 1000L);
            byte[] frame = new byte[400000]; frame[0] = 0x17; frame[1] = 1;
            int size = frame.Length - 9;
            frame[5] = (byte)(size >> 24); frame[6] = (byte)(size >> 16); frame[7] = (byte)(size >> 8); frame[8] = (byte)size; frame[9] = 0x65;
            test.Input.Tag(FlvTagType.Video, (uint)(i * 1000), frame);
            await FixedDelayTests.UntilAsync(() => test.Engine.Snapshot.EmittedMediaTags >= i + 1);
        }
        Assert.True(test.Engine.Snapshot.HistoryEvictedTags > 0);
        Assert.InRange(test.Engine.Snapshot.PeakBufferBytes, 1, 1024 * 1024);
        Assert.Equal(0, test.Engine.Snapshot.SkippedMediaTags);
        Assert.Equal(12, test.Engine.Snapshot.EmittedMediaTags);
        await test.Finish();
        Assert.Equal(0, test.Engine.Snapshot.BufferBytes);
    }

    private static void AssertTimeline(List<(FlvTagType Type, uint Timestamp, byte[] Payload)> tags)
    {
        uint previous = 0;
        foreach (var tag in tags) { Assert.True(tag.Timestamp >= previous); previous = tag.Timestamp; }
        var media = tags.Where(t => t.Payload[1] == 1).ToArray();
        for (int i = 0; i + 1 < media.Length; i += 2)
        {
            Assert.Equal(FlvTagType.Video, media[i].Type); Assert.Equal(FlvTagType.Audio, media[i + 1].Type);
            Assert.Equal(10u, media[i + 1].Timestamp - media[i].Timestamp);
        }
    }

    private sealed class Harness : IAsyncDisposable
    {
        public ManualMediaClock Clock { get; } = new();
        public DelayController Control { get; }
        public PushStream Input { get; } = new();
        public MemoryStream Output { get; } = new();
        public FlvDelayMediaTransfer Engine { get; }
        private readonly CancellationTokenSource _cancel = new();
        private readonly Task _running;
        public Harness(FixedDelayOptions? options = null)
        {
            options ??= new(); Control = new(options.DelaySeconds);
            Engine = new(options, Clock, Control);
            Input.Push(File((FlvTagType.Video, 0, Avc), (FlvTagType.Audio, 0, Aac)));
            _running = Engine.TransferAsync(Input, Output, _cancel.Token);
        }
        public async Task Feed(int seconds)
        {
            Clock.AdvanceTo(seconds * 1000L); Input.Frame(seconds);
            await FixedDelayTests.UntilAsync(() => Engine.Snapshot.EmittedMediaTags >= (seconds + 1) * 2);
            await FixedDelayTests.UntilAsync(() => Engine.Snapshot.HistoryTags > 0);
        }
        public async Task Finish(long? at = null)
        {
            Input.Complete();
            if (at.HasValue) Clock.AdvanceTo(at.Value);
            await _running.WaitAsync(TimeSpan.FromSeconds(5));
        }
        public async ValueTask DisposeAsync()
        {
            _cancel.Cancel(); Input.Complete();
            try { await _running.WaitAsync(TimeSpan.FromSeconds(5)); } catch (OperationCanceledException) { }
            finally { _cancel.Dispose(); Input.Dispose(); Output.Dispose(); }
        }
    }

    private sealed class PushStream : Stream
    {
        private readonly Channel<byte[]> _chunks = Channel.CreateUnbounded<byte[]>();
        private byte[]? _current; private int _offset;
        public void Push(byte[] value) => _chunks.Writer.TryWrite(value);
        public void Tag(FlvTagType type, uint timestamp, byte[] payload)
        { using var stream = new MemoryStream(); WriteTag(stream, type, timestamp, payload); Push(stream.ToArray()); }
        public void Frame(int seconds)
        {
            byte[] video = Video(true); video[^1] = (byte)seconds;
            Tag(FlvTagType.Video, (uint)(seconds * 1000), video);
            Tag(FlvTagType.Audio, (uint)(seconds * 1000 + 10), Audio((byte)seconds));
        }
        public void Complete() => _chunks.Writer.TryComplete();
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken token = default)
        {
            while (_current is null || _offset == _current.Length)
            {
                if (!await _chunks.Reader.WaitToReadAsync(token)) return 0;
                if (!_chunks.Reader.TryRead(out _current)) continue;
                _offset = 0;
            }
            int count = Math.Min(buffer.Length, _current.Length - _offset);
            _current.AsMemory(_offset, count).CopyTo(buffer); _offset += count; return count;
        }
        public override bool CanRead => true; public override bool CanSeek => false; public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
