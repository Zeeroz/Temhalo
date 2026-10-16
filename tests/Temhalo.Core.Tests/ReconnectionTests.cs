using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Temhalo.Core.Abstractions;
using Temhalo.Core.Buffering;
using Temhalo.Core.Flv;
using Temhalo.Core.Timing;
using Xunit;
using static Temhalo.Core.Tests.FlvTestData;

namespace Temhalo.Core.Tests;

public sealed class ReconnectionTests
{
    private static byte[] Segment(uint start = 0) => File((FlvTagType.Video, start, Avc),
        (FlvTagType.Audio, start, Aac), (FlvTagType.Video, start, Video(true)), (FlvTagType.Audio, start + 10, Audio()));

    [Fact]
    public async Task InputEofDrainsAndNextSourceResumesWithHeadersOnSameOutput()
    {
        var clock = new ManualMediaClock();
        var sources = Channel.CreateUnbounded<Stream>();
        var output = new Output();
        var engine = new FlvDelayMediaTransfer(new() { DelaySeconds = 2 }, clock);
        var running = engine.TransferSessionsAsync(Inputs(sources), output, CancellationToken.None);
        sources.Writer.TryWrite(new MemoryStream(Segment(1000)));
        await FixedDelayTests.UntilAsync(() => engine.Snapshot.InputCompleted);
        Assert.Equal(0, engine.Snapshot.EmittedMediaTags);
        clock.AdvanceTo(2000);
        await FixedDelayTests.UntilAsync(() => engine.Snapshot.EmittedMediaTags == 2);
        Assert.False(running.IsCompleted);
        clock.AdvanceTo(3000);
        sources.Writer.TryWrite(new MemoryStream(Segment(0)));
        await FixedDelayTests.UntilAsync(() => engine.Snapshot.InputSessions == 2 && engine.Snapshot.InputCompleted);
        clock.AdvanceTo(5000);
        await FixedDelayTests.UntilAsync(() => engine.Snapshot.EmittedMediaTags == 4);
        sources.Writer.TryComplete();
        await running.WaitAsync(TimeSpan.FromSeconds(5));
        var tags = await ReadAllAsync(output.Current.ToArray());
        Assert.Equal(1, output.Opens);
        Assert.Equal(8, tags.Count);
        Assert.Equal(Avc, tags[4].Payload); Assert.Equal(Aac, tags[5].Payload);
        Assert.Equal(11u, tags[6].Timestamp); Assert.Equal(21u, tags[7].Timestamp);
        Assert.Equal(0, engine.Snapshot.BufferBytes);
    }

    [Fact]
    public async Task OutputOutagePreservesQueueAndIngestAndSameTargetCanClearRecoveryBacklog()
    {
        var clock = new ManualMediaClock();
        var control = new DelayController();
        var sources = Channel.CreateUnbounded<Stream>();
        var output = new Output();
        var engine = new FlvDelayMediaTransfer(new(), clock, control);
        var running = engine.TransferSessionsAsync(Inputs(sources), output, CancellationToken.None);
        sources.Writer.TryWrite(new MemoryStream(Segment()));
        await FixedDelayTests.UntilAsync(() => engine.Snapshot.EmittedMediaTags == 2);
        output.Current.Broken = true;
        clock.AdvanceTo(1000);
        sources.Writer.TryWrite(new MemoryStream(Segment(1000)));
        await FixedDelayTests.UntilAsync(() => output.Opens == 2);
        clock.AdvanceTo(2000);
        sources.Writer.TryWrite(new MemoryStream(Segment(2000)));
        await FixedDelayTests.UntilAsync(() => engine.Snapshot.ReadTags == 12);
        Assert.True(engine.Snapshot.BufferedTags > 0);
        Assert.Equal(2, engine.Snapshot.EmittedMediaTags);
        clock.AdvanceTo(5000);
        output.AllowReconnect.TrySetResult();
        await FixedDelayTests.UntilAsync(() => engine.Snapshot.EmittedMediaTags == 4);
        Assert.Equal(4000, engine.Snapshot.RecoveryDelayMilliseconds);
        Assert.Equal(1, engine.Snapshot.OutputReconnections);
        // Reapplying zero must seek even when the requested target was already zero before the outage.
        control.SetTarget(0);
        await FixedDelayTests.UntilAsync(() => engine.Snapshot.Control.CompletedSeeks == 1);
        Assert.Equal(0, engine.Snapshot.RecoveryDelayMilliseconds);
        sources.Writer.TryComplete();
        await running.WaitAsync(TimeSpan.FromSeconds(5));
        var resumed = await ReadAllAsync(output.Current.ToArray());
        Assert.Equal(8, resumed.Count); // Both queued GOPs retained; each source brings its codec headers.
        Assert.Equal(Avc, resumed[0].Payload); Assert.Equal(Aac, resumed[1].Payload);
        Assert.Equal(Video(true), resumed[2].Payload);
        Assert.Equal(0, engine.Snapshot.BufferBytes);
    }

    [Fact]
    public async Task MemoryLimitDuringOutageFailsExplicitlyAndReleasesAllAllocations()
    {
        var output = new Output { BlockFirst = true };
        var engine = new FlvDelayMediaTransfer(new() { TagCapacity = 128, MaximumBufferBytes = 4096 });
        byte[] wire = File(new[] { (FlvTagType.Video, 0u, Avc), (FlvTagType.Audio, 0u, Aac), (FlvTagType.Video, 0u, Video(true)) }
            .Concat(Enumerable.Range(0, 100).Select(i => (FlvTagType.Audio, (uint)i, new byte[512]))).ToArray());
        var sources = Channel.CreateUnbounded<Stream>(); sources.Writer.TryWrite(new MemoryStream(wire));
        await Assert.ThrowsAsync<BufferLimitExceededException>(() => engine.TransferSessionsAsync(Inputs(sources), output, CancellationToken.None));
        Assert.Equal(0, engine.Snapshot.BufferBytes);
        Assert.InRange(engine.Snapshot.PeakBufferBytes, 1, 4096);
    }

    [Fact]
    public async Task BrokenInputPipeKeepsCompleteBufferedTagsAndAcceptsNextConnection()
    {
        var sources = Channel.CreateUnbounded<Stream>();
        sources.Writer.TryWrite(new BrokenAtEof(Segment(1000)));
        sources.Writer.TryWrite(new MemoryStream(Segment())); sources.Writer.TryComplete();
        var output = new Output(); var engine = new FlvDelayMediaTransfer(new());
        await engine.TransferSessionsAsync(Inputs(sources), output, CancellationToken.None);
        Assert.Equal(4, engine.Snapshot.EmittedMediaTags);
        Assert.Equal(2, engine.Snapshot.InputSessions); Assert.True(engine.Snapshot.TruncatedInput);
        Assert.Equal(1, output.Opens); Assert.Equal(8, (await ReadAllAsync(output.Current.ToArray())).Count);
        Assert.Equal(0, engine.Snapshot.BufferBytes);
    }

    [Fact]
    public async Task AbortInterruptsReconnectAndReleasesQueue()
    {
        using var abort = new CancellationTokenSource();
        var output = new Output { BlockFirst = true };
        var sources = Channel.CreateUnbounded<Stream>(); sources.Writer.TryWrite(new MemoryStream(Segment()));
        var engine = new FlvDelayMediaTransfer(new());
        Task running = engine.TransferSessionsAsync(Inputs(sources), output, abort.Token);
        await FixedDelayTests.UntilAsync(() => output.Opens == 1);
        abort.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => running.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(0, engine.Snapshot.BufferBytes);
    }

    private static async IAsyncEnumerable<Stream> Inputs(Channel<Stream> channel,
        [EnumeratorCancellation] CancellationToken token = default)
    {
        await foreach (var stream in channel.Reader.ReadAllAsync(token))
        { using (stream) yield return stream; }
    }

    private sealed class Output : IFlvOutputConnection
    {
        private int _opens;
        public int Opens => Volatile.Read(ref _opens);
        public bool BlockFirst { get; init; }
        public TaskCompletionSource AllowReconnect { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public SwitchStream Current { get; private set; } = new();
        public async ValueTask<Stream> OpenAsync(CancellationToken token)
        {
            int number = Interlocked.Increment(ref _opens);
            if (number > 1 || BlockFirst) await AllowReconnect.Task.WaitAsync(token);
            Current = new();
            return Current;
        }
        public ValueTask CloseAsync(bool failed, CancellationToken token) => ValueTask.CompletedTask;
    }
    private sealed class SwitchStream : MemoryStream
    {
        public bool Broken { get; set; }
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
            => Broken ? ValueTask.FromException(new IOException("Synthetic output outage")) : base.WriteAsync(buffer, cancellationToken);
    }
    private sealed class BrokenAtEof(byte[] data) : MemoryStream(data)
    {
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
            => Position == Length ? ValueTask.FromException<int>(new IOException("Synthetic input pipe failure")) : base.ReadAsync(buffer, cancellationToken);
    }
}
