using System.Security.Cryptography;
using Temhalo.Core.Buffering;
using Xunit;

namespace Temhalo.Core.Tests;

public sealed class PassthroughMediaTransferTests
{
    [Fact]
    public async Task PreservesBytesAcrossFragmentedReadsWithoutOwningStreams()
    {
        byte[] source = RandomNumberGenerator.GetBytes(1_000_013);
        using var input = new FragmentedStream(source);
        using var output = new MemoryStream();
        var transfer = new PassthroughMediaTransfer();
        await transfer.TransferAsync(input, output, CancellationToken.None);
        Assert.Equal(source, output.ToArray());
        Assert.Equal(source.Length, transfer.BytesTransferred);
        Assert.True(input.CanRead);
        Assert.True(output.CanWrite);
    }

    [Fact]
    public async Task CancellationInterruptsBackpressureWithoutConsumingWholeInput()
    {
        using var input = new MemoryStream(new byte[1_000_000]);
        using var output = new BlockedOutput();
        using var cancellation = new CancellationTokenSource();
        var transfer = new PassthroughMediaTransfer();
        Task pumping = transfer.TransferAsync(input, output, cancellation.Token);
        await output.WriteStarted.Task.WaitAsync(TimeSpan.FromSeconds(3));
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pumping.WaitAsync(TimeSpan.FromSeconds(3)));
        Assert.Equal(0, transfer.BytesTransferred);
        Assert.InRange(input.Position, 1, 64 * 1024);
    }

    private sealed class FragmentedStream(byte[] bytes) : MemoryStream(bytes)
    {
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
            => base.ReadAsync(buffer[..Math.Min(buffer.Length, 137)], cancellationToken);
    }

    private sealed class BlockedOutput : MemoryStream
    {
        public TaskCompletionSource WriteStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            WriteStarted.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        }
    }
}
