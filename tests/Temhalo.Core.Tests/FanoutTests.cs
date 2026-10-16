using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Temhalo.Core.Abstractions;
using Temhalo.Core.Buffering;
using Temhalo.Core.Outputs;
using Xunit;
using static Temhalo.Core.Tests.FlvTestData;

namespace Temhalo.Core.Tests;

public sealed class FanoutTests
{
    private static byte[] Segment(uint timestamp) => File((Flv.FlvTagType.Video, timestamp, Avc),
        (Flv.FlvTagType.Audio, timestamp, Aac), (Flv.FlvTagType.Video, timestamp, Video(true)),
        (Flv.FlvTagType.Audio, timestamp + 1, Audio()));

    [Fact]
    public async Task TwoOutputsReceiveDecodableHeadersAndSameMediaWithoutLeakingBudget()
    {
        var first = new Connection(); var second = new Connection();
        await using var fanout = new FanoutFlvOutput([new("a", first), new("b", second)], capacity: 8);
        var engine = new FlvDelayMediaTransfer(new() { TagCapacity = 32, MaximumBufferBytes = 65536 });
        var inputs = Channel.CreateUnbounded<Stream>();
        inputs.Writer.TryWrite(new MemoryStream(Segment(0))); inputs.Writer.TryWrite(new MemoryStream(Segment(20))); inputs.Writer.TryComplete();
        await engine.TransferSessionsAsync(Inputs(inputs), fanout, CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));
        await fanout.CloseAsync(false, CancellationToken.None);
        var a = await ReadAllAsync(first.Stream.ToArray()); var b = await ReadAllAsync(second.Stream.ToArray());
        Assert.Equal(a.Select(tag => tag.Timestamp), b.Select(tag => tag.Timestamp));
        Assert.Equal(Avc, a[0].Payload); Assert.Equal(Aac, a[1].Payload); Assert.Equal(Video(true), a[2].Payload);
        Assert.Equal(8, a.Count); Assert.Equal(8, b.Count);
        Assert.Equal(0, engine.Snapshot.BufferBytes);
    }

    [Fact]
    public async Task FailedDestinationDoesNotBlockHealthyOutputAndRecoversOnIdr()
    {
        var first = new Connection(); var second = new Connection();
        await using var fanout = new FanoutFlvOutput([new("a", first), new("b", second)], capacity: 4);
        var engine = new FlvDelayMediaTransfer(new() { TagCapacity = 256 });
        var inputs = Channel.CreateUnbounded<Stream>();
        var running = engine.TransferSessionsAsync(Inputs(inputs), fanout, CancellationToken.None);
        inputs.Writer.TryWrite(new MemoryStream(Segment(0)));
        await FixedDelayTests.UntilAsync(() => fanout.Snapshots.All(output => output.BytesWritten > 0));
        first.Broken = true;
        for (uint i = 1; i <= 20; i++)
        { inputs.Writer.TryWrite(new MemoryStream(Segment(i * 10))); await Task.Delay(15); }
        await FixedDelayTests.UntilAsync(() => fanout.Snapshots[1].BytesWritten > 1000);
        Assert.True(fanout.Snapshots[0].DroppedTags > 0);
        first.Broken = false; first.Reconnect.TrySetResult();
        await FixedDelayTests.UntilAsync(() => fanout.Snapshots[0].State == "SeekingKeyframe" || first.Opens > 1);
        inputs.Writer.TryWrite(new MemoryStream(Segment(300)));
        await FixedDelayTests.UntilAsync(() => first.Opens > 1 && fanout.Snapshots[0].State == "Relaying");
        inputs.Writer.TryComplete(); await running.WaitAsync(TimeSpan.FromSeconds(5));
        await fanout.CloseAsync(false, CancellationToken.None);
        var recovered = await ReadAllAsync(first.Stream.ToArray());
        Assert.Equal(Avc, recovered[0].Payload); Assert.Equal(Aac, recovered[1].Payload);
        Assert.Equal(Video(true), recovered[2].Payload);
        Assert.Equal(0, engine.Snapshot.BufferBytes);
    }

    [Fact]
    public async Task ExplicitCatchUpKeepsHealthyConnectionsOpen()
    {
        var first = new Connection(); var second = new Connection();
        await using var fanout = new FanoutFlvOutput([new("a", first), new("b", second)], capacity: 8);
        var engine = new FlvDelayMediaTransfer(new() { TagCapacity = 32 });
        var inputs = Channel.CreateUnbounded<Stream>();
        var running = engine.TransferSessionsAsync(Inputs(inputs), fanout, CancellationToken.None);
        inputs.Writer.TryWrite(new MemoryStream(Segment(0)));
        await FixedDelayTests.UntilAsync(() => fanout.Snapshots.All(output => output.BytesWritten > 0));
        long before = fanout.Snapshots[0].BytesWritten;
        fanout.CatchUp();
        await FixedDelayTests.UntilAsync(() => fanout.Snapshots.All(output => output.State == "SeekingKeyframe"));
        inputs.Writer.TryWrite(new MemoryStream(Segment(100)));
        await FixedDelayTests.UntilAsync(() => fanout.Snapshots.All(output => output.State == "Relaying") && fanout.Snapshots[0].BytesWritten > before);
        Assert.Equal(1, first.Opens); Assert.Equal(1, second.Opens);
        inputs.Writer.TryComplete(); await running; await fanout.CloseAsync(false, CancellationToken.None);
        Assert.Equal(0, engine.Snapshot.BufferBytes);
    }

    [Fact]
    public async Task AllOutputsUnavailableHoldCommonBufferAndAbortReleasesReferences()
    {
        var first = new Connection { Broken = true }; var second = new Connection { Broken = true };
        await using var fanout = new FanoutFlvOutput([new("a", first), new("b", second)], capacity: 2);
        var engine = new FlvDelayMediaTransfer(new() { TagCapacity = 256 });
        var inputs = Channel.CreateUnbounded<Stream>();
        using var abort = new CancellationTokenSource();
        var running = engine.TransferSessionsAsync(Inputs(inputs), fanout, abort.Token);
        for (uint i = 0; i < 20; i++) inputs.Writer.TryWrite(new MemoryStream(Segment(i * 10)));
        await FixedDelayTests.UntilAsync(() => engine.Snapshot.ReadTags == 80);
        Assert.True(engine.Snapshot.BufferedTags > 0);
        Assert.All(fanout.Snapshots, snapshot => Assert.Equal(0, snapshot.DroppedTags));
        abort.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => running.WaitAsync(TimeSpan.FromSeconds(5)));
        await fanout.CloseAsync(true, CancellationToken.None);
        Assert.Equal(0, engine.Snapshot.BufferBytes);
    }

    private static async IAsyncEnumerable<Stream> Inputs(Channel<Stream> inputs, [EnumeratorCancellation] CancellationToken token = default)
    { await foreach (var stream in inputs.Reader.ReadAllAsync(token)) { using (stream) yield return stream; } }

    private sealed class Connection : IFlvOutputConnection
    {
        public volatile bool Broken;
        public int Opens;
        public MemoryStream Stream { get; private set; } = new();
        public TaskCompletionSource Reconnect { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async ValueTask<Stream> OpenAsync(CancellationToken token)
        {
            if (Broken) await Reconnect.Task.WaitAsync(token);
            Interlocked.Increment(ref Opens);
            Stream = new BrokenStream(this); return Stream;
        }
        public ValueTask CloseAsync(bool failed, CancellationToken token) => ValueTask.CompletedTask;
    }
    private sealed class BrokenStream(Connection connection) : MemoryStream
    {
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
            => connection.Broken ? ValueTask.FromException(new IOException("Synthetic output failure")) : base.WriteAsync(buffer, cancellationToken);
    }
}
