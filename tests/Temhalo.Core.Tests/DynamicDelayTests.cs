using System.Threading.Channels;
using Temhalo.Core.Buffering;
using Temhalo.Core.Flv;
using Temhalo.Core.Timing;
using Xunit;
using static Temhalo.Core.Tests.FlvTestData;

namespace Temhalo.Core.Tests;

public sealed class DynamicDelayTests
{
    [Fact]
    public void PauseCapSurvivesRetargetingAndRequiresPlaybackBetweenSteps()
    {
        var control = new DelayController();
        var planner = new DelayTransitionPlanner(new() { MaximumPauseStepSeconds = 2 }, control.Request);
        planner.MarkMedia(0, 0);
        Assert.False(planner.Apply(control.SetTarget(6), 0));
        Assert.True(planner.CanStartPause(false));
        planner.StartPause(100);
        planner.Apply(control.SetTarget(300), 1000);
        Assert.Equal(2100, planner.PauseEnd);
        Assert.Equal(900, planner.Snapshot(1000).ActiveDelayMilliseconds);
        planner.FinishPause(2100);
        Assert.False(planner.CanStartPause(false));
        planner.MarkMedia(1000, 2100);
        planner.MarkMedia(2000, 2500);
        Assert.False(planner.CanStartPause(false)); // Enough media, insufficient wall time.
        planner.MarkMedia(2001, 3100);
        Assert.True(planner.CanStartPause(false));
        Assert.False(planner.CanStartPause(true)); // No new pauses after EOF.
        planner.StartPause(3100);
        Assert.True(planner.Apply(control.SetTarget(0), 3600));
        Assert.Equal(0, planner.Snapshot(3600).ActiveDelayMilliseconds);
        Assert.Equal("SeekingKeyframe", planner.Snapshot(3600).Phase);
        planner.MarkMedia(2002, 3600);
        Assert.True(planner.Snapshot(3600).TargetReached);
        Assert.Equal(1, planner.Snapshot(3600).CompletedSeeks);
        Assert.Equal(2000, planner.Snapshot(3600).MaximumPauseMilliseconds);
    }

    [Fact]
    public void ZeroIsIdempotentOnceAlreadyLive()
    {
        var planner = new DelayTransitionPlanner(new(), new(0, 0));
        planner.MarkMedia(0, 0);
        Assert.False(planner.Apply(new(1, 0), 10));
        Assert.True(planner.Snapshot(10).TargetReached);
        Assert.Equal(0, planner.Snapshot(10).CompletedSeeks);
    }

    [Fact]
    public void InitialFillAndLatestMailboxRemainBoundedAndValidated()
    {
        var control = new DelayController();
        Parallel.For(0, 1000, i => control.SetTarget(i % 301));
        Assert.Equal(1000, control.Request.Version);
        Assert.Throws<ArgumentOutOfRangeException>(() => control.SetTarget(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => control.SetTarget(301));
        var planner = new DelayTransitionPlanner(new(), new(0, 0));
        planner.Apply(new(1, 300), 100);
        Assert.Equal(300000, planner.Snapshot(100).ActiveDelayMilliseconds);
        Assert.False(planner.Snapshot(100).TargetReached); // No media emitted yet.
    }

    [Fact]
    public async Task ZeroInterruptsOldDeadlineAndChoosesLatestBufferedIdr()
    {
        var control = new DelayController(300);
        var clock = new ManualMediaClock();
        byte[] wire = File((FlvTagType.Video, 0, Avc), (FlvTagType.Audio, 0, Aac),
            (FlvTagType.Video, 0, Video(true, 10)), (FlvTagType.Audio, 10, Audio(1)),
            (FlvTagType.Video, 1000, Video(true, 20)), (FlvTagType.Audio, 1010, Audio(2)),
            (FlvTagType.Video, 2000, Video(true, 30)), (FlvTagType.Audio, 2010, Audio(3)));
        var engine = new FlvDelayMediaTransfer(new() { DelaySeconds = 300, TagCapacity = 128 }, clock, control);
        using var output = new MemoryStream();
        Task running = engine.TransferAsync(new MemoryStream(wire), output, CancellationToken.None);
        await FixedDelayTests.UntilAsync(() => engine.Snapshot.InputCompleted);
        control.SetTarget(0);
        await running.WaitAsync(TimeSpan.FromSeconds(5));
        var tags = await ReadAllAsync(output.ToArray());
        Assert.Equal(4, tags.Count);
        Assert.Equal(Video(true, 30), tags[2].Payload);
        Assert.Equal(Audio(3), tags[3].Payload);
        Assert.Equal(1, engine.Snapshot.Control.CompletedSeeks);
        Assert.Equal(0, engine.Snapshot.BufferBytes);
    }

    [Fact]
    public async Task LiveIncreaseKeepsEveryFrameAndZeroCollapsesSkippedSourceTime()
    {
        var control = new DelayController();
        var clock = new ManualMediaClock();
        using var input = new PushStream();
        using var output = new MemoryStream();
        var engine = new FlvDelayMediaTransfer(new() { MaximumPauseStepSeconds = 2 }, clock, control);
        input.Push(File((FlvTagType.Video, 0, Avc), (FlvTagType.Audio, 0, Aac),
            (FlvTagType.Video, 0, Video(true))));
        Task running = engine.TransferAsync(input, output, CancellationToken.None);
        await FixedDelayTests.UntilAsync(() => engine.Snapshot.EmittedMediaTags == 1);
        control.SetTarget(2);
        clock.AdvanceTo(1000);
        input.Tag(FlvTagType.Video, 1000, Video(true));
        await FixedDelayTests.UntilAsync(() => engine.Snapshot.Control.IsPaused);
        input.Tag(FlvTagType.Audio, 1010, Audio(1));
        await FixedDelayTests.UntilAsync(() => engine.Snapshot.ReadTags == 5);
        clock.AdvanceTo(2000);
        input.Tag(FlvTagType.Video, 2000, Video(true));
        input.Tag(FlvTagType.Audio, 2010, Audio(2));
        await FixedDelayTests.UntilAsync(() => engine.Snapshot.ReadTags == 7);
        Assert.Equal(1, engine.Snapshot.EmittedMediaTags);
        clock.AdvanceTo(3000);
        await FixedDelayTests.UntilAsync(() => engine.Snapshot.EmittedMediaTags == 3);
        Assert.Equal(2000, engine.Snapshot.Control.ActiveDelayMilliseconds);
        Assert.Equal(1, engine.Snapshot.Control.CompletedPauseSteps);
        // The pending next keyframe is a second behind live. Zero must keep that latest GOP.
        control.SetTarget(0);
        await FixedDelayTests.UntilAsync(() => engine.Snapshot.Control.CompletedSeeks == 1);
        input.Complete();
        await running.WaitAsync(TimeSpan.FromSeconds(5));
        var tags = await ReadAllAsync(output.ToArray());
        var video = tags.Where(t => t.Type == FlvTagType.Video && t.Payload[1] == 1).ToArray();
        Assert.Equal(3, video.Length); // Increase did not discard any frame.
        Assert.Equal(0u, video[0].Timestamp);
        Assert.Equal(1000u, video[1].Timestamp);
        Assert.Equal(1011u, video[2].Timestamp); // Source jumped from 1000 to 2000; gap collapsed.
        Assert.Equal(0, engine.Snapshot.BufferBytes);
    }

    [Fact]
    public async Task SeekingAcrossCodecChangesUsesCandidateGenerationNotLookaheadGeneration()
    {
        var control = new DelayController(300);
        byte[] changed = Avc; changed[7] = 1;
        var clock = new ManualMediaClock();
        using var input = new PushStream();
        using var output = new MemoryStream();
        var engine = new FlvDelayMediaTransfer(new() { DelaySeconds = 300 }, clock, control);
        input.Push(File((FlvTagType.Video, 0, Avc), (FlvTagType.Audio, 0, Aac),
            (FlvTagType.Video, 0, Video(true)), (FlvTagType.Audio, 10, Audio(1))));
        Task running = engine.TransferAsync(input, output, CancellationToken.None);
        await FixedDelayTests.UntilAsync(() => engine.Snapshot.ReadTags == 4);
        clock.AdvanceTo(1000);
        input.Tag(FlvTagType.Video, 1000, changed);
        input.Tag(FlvTagType.Video, 1000, Video(true));
        input.Tag(FlvTagType.Audio, 1010, Audio(2));
        await FixedDelayTests.UntilAsync(() => engine.Snapshot.ReadTags == 7);
        control.SetTarget(1); // cutoff=0; select old IDR before target, not future generation.
        await FixedDelayTests.UntilAsync(() => engine.Snapshot.EmittedMediaTags == 2);
        var partial = await ReadAllAsync(output.ToArray());
        Assert.Equal(Avc, partial[0].Payload);
        clock.AdvanceTo(2000);
        input.Complete();
        await running.WaitAsync(TimeSpan.FromSeconds(5));
        var tags = await ReadAllAsync(output.ToArray());
        Assert.Equal(changed, tags[4].Payload);
    }

    [Fact]
    public async Task EmergencyZeroInterruptsAnIncreasePauseWithoutWaitingForItsDeadline()
    {
        var control = new DelayController();
        var clock = new ManualMediaClock();
        using var input = new PushStream();
        using var output = new MemoryStream();
        var engine = new FlvDelayMediaTransfer(new(), clock, control);
        input.Push(File((FlvTagType.Video, 0, Avc), (FlvTagType.Audio, 0, Aac), (FlvTagType.Video, 0, Video(true))));
        Task running = engine.TransferAsync(input, output, CancellationToken.None);
        await FixedDelayTests.UntilAsync(() => engine.Snapshot.EmittedMediaTags == 1);
        control.SetTarget(30);
        clock.AdvanceTo(1000);
        input.Tag(FlvTagType.Video, 1000, Video(true));
        await FixedDelayTests.UntilAsync(() => engine.Snapshot.Control.IsPaused);
        clock.AdvanceTo(1500);
        input.Tag(FlvTagType.Audio, 1010, Audio(1));
        input.Tag(FlvTagType.Video, 1500, Video(true));
        input.Tag(FlvTagType.Audio, 1510, Audio(2));
        await FixedDelayTests.UntilAsync(() => engine.Snapshot.ReadTags == 7);
        control.SetTarget(0);
        await FixedDelayTests.UntilAsync(() => engine.Snapshot.Control.CompletedSeeks == 1);
        Assert.False(engine.Snapshot.Control.IsPaused);
        Assert.Equal(500, engine.Snapshot.Control.MaximumPauseMilliseconds);
        input.Complete();
        await running.WaitAsync(TimeSpan.FromSeconds(5));
        var tags = await ReadAllAsync(output.ToArray());
        Assert.Equal(1u, tags[5].Timestamp);
        Assert.Equal(0, engine.Snapshot.BufferBytes);
    }

    [Fact]
    public async Task SlowOutputIsReportedEvenWithoutAnOutputDisconnect()
    {
        var clock = new ManualMediaClock();
        using var input = new PushStream();
        using var output = new BlockFirstVideoStream();
        var engine = new FlvDelayMediaTransfer(new(), clock);
        input.Push(File((FlvTagType.Video, 0, Avc), (FlvTagType.Audio, 0, Aac),
            (FlvTagType.Video, 0, Video(true))));
        Task running = engine.TransferAsync(input, output, CancellationToken.None);
        try
        {
            await output.Blocked.Task.WaitAsync(TimeSpan.FromSeconds(5));
            clock.AdvanceTo(2500);
            output.Release.TrySetResult();
            await FixedDelayTests.UntilAsync(() => engine.Snapshot.LastMediaAgeMilliseconds == 2500);
            Assert.Equal(2500, engine.Snapshot.LastMediaAgeMilliseconds);
            Assert.Equal(0, engine.Snapshot.OutputReconnections);
            Assert.False(engine.Snapshot.Control.TargetReached);
            Assert.Equal("OutputBacklog", engine.Snapshot.Control.Phase);
            // No media is dropped automatically to conceal the measured lag.
            Assert.Equal(0, engine.Snapshot.SkippedMediaTags);
            input.Tag(FlvTagType.Video, 2500, Video(false));
            await FixedDelayTests.UntilAsync(() => engine.Snapshot.LastMediaAgeMilliseconds == 0);
            Assert.Equal(0, engine.Snapshot.LastMediaAgeMilliseconds);
            Assert.True(engine.Snapshot.Control.TargetReached);
        }
        finally
        {
            output.Release.TrySetResult(); input.Complete();
            await running.WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    [Fact]
    public async Task ReapplyingZeroDropsResidualBacklogOnLatestIdrWithoutRestartingOutput()
    {
        var clock = new ManualMediaClock();
        var control = new DelayController();
        using var input = new PushStream();
        using var output = new BlockFirstVideoStream();
        var engine = new FlvDelayMediaTransfer(new(), clock, control);
        input.Push(File((FlvTagType.Video, 0, Avc), (FlvTagType.Audio, 0, Aac),
            (FlvTagType.Video, 0, Video(true, 10))));
        Task running = engine.TransferAsync(input, output, CancellationToken.None);
        try
        {
            await output.Blocked.Task.WaitAsync(TimeSpan.FromSeconds(5));
            clock.AdvanceTo(1000); input.Tag(FlvTagType.Video, 1000, Video(true, 20));
            await FixedDelayTests.UntilAsync(() => engine.Snapshot.ReadTags == 4);
            clock.AdvanceTo(3000); input.Tag(FlvTagType.Video, 3000, Video(true, 30));
            input.Tag(FlvTagType.Audio, 3010, Audio(3));
            await FixedDelayTests.UntilAsync(() => engine.Snapshot.ReadTags == 6);
            control.SetTarget(0); // The scheduled target is already zero; there was no IOException.
            output.Release.TrySetResult();
            await FixedDelayTests.UntilAsync(() => engine.Snapshot.EmittedMediaTags >= 3);
            input.Complete(); await running.WaitAsync(TimeSpan.FromSeconds(5));
            var tags = await ReadAllAsync(output.ToArray());
            var media = tags.Where(t => t.Type == FlvTagType.Video && t.Payload[1] == 1).ToArray();
            Assert.Equal(new[] { Video(true, 10), Video(true, 30) }, media.Select(t => t.Payload));
            Assert.Equal(1, engine.Snapshot.Control.CompletedSeeks);
            Assert.Equal(0, engine.Snapshot.LastMediaAgeMilliseconds);
            Assert.Equal(0, engine.Snapshot.OutputReconnections);
            Assert.Equal(0, engine.Snapshot.BufferBytes);
            Assert.True(engine.Snapshot.Control.TargetReached);
        }
        finally
        {
            output.Release.TrySetResult(); input.Complete();
            await running.WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    private sealed class BlockFirstVideoStream : MemoryStream
    {
        private int _block = 1;
        public TaskCompletionSource Blocked { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (buffer.Length > 1 && buffer.Span[0] == 0x17 && buffer.Span[1] == 1 && Interlocked.Exchange(ref _block, 0) != 0)
            { Blocked.TrySetResult(); await Release.Task.WaitAsync(cancellationToken); }
            await base.WriteAsync(buffer, cancellationToken);
        }
    }

    private sealed class PushStream : Stream
    {
        private readonly Channel<byte[]> _chunks = Channel.CreateUnbounded<byte[]>();
        private byte[]? _current;
        private int _offset;
        public void Push(byte[] bytes) => _chunks.Writer.TryWrite(bytes);
        public void Tag(FlvTagType type, uint timestamp, byte[] payload)
        { using var packet = new MemoryStream(); WriteTag(packet, type, timestamp, payload); Push(packet.ToArray()); }
        public void Complete() => _chunks.Writer.TryComplete();
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            while (_current is null || _offset == _current.Length)
            {
                if (!await _chunks.Reader.WaitToReadAsync(cancellationToken)) return 0;
                if (!_chunks.Reader.TryRead(out _current)) continue;
                _offset = 0;
            }
            int count = Math.Min(buffer.Length, _current.Length - _offset);
            _current.AsMemory(_offset, count).CopyTo(buffer); _offset += count;
            return count;
        }
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
