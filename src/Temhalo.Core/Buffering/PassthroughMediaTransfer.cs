using System.Buffers;
using Temhalo.Core.Abstractions;

namespace Temhalo.Core.Buffering;

/// <summary>Bounded byte transport: no decoding, timestamp modification or intentional delay.</summary>
public sealed class PassthroughMediaTransfer : IMediaTransfer
{
    private long _bytesTransferred;
    public long BytesTransferred => Interlocked.Read(ref _bytesTransferred);

    public async Task TransferAsync(Stream input, Stream output, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(output);
        byte[] buffer = ArrayPool<byte>.Shared.Rent(64 * 1024);
        try
        {
            while (true)
            {
                int count = await input.ReadAsync(buffer.AsMemory(0, 64 * 1024), cancellationToken).ConfigureAwait(false);
                if (count == 0) break;
                await output.WriteAsync(buffer.AsMemory(0, count), cancellationToken).ConfigureAwait(false);
                Interlocked.Add(ref _bytesTransferred, count);
            }
            await output.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        finally { ArrayPool<byte>.Shared.Return(buffer); }
        // Streams belong to the process supervisor, including their shutdown ordering.
    }
}
