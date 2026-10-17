using System.Buffers.Binary;
using Temhalo.Core.Buffering;
using Temhalo.Core.Timing;

namespace Temhalo.Core.Flv;

/// <summary>Strict streaming FLV v1 framing reader, including PreviousTagSize validation.</summary>
public sealed class FlvTagReader(Stream input, FlvMemoryBudget budget, IMediaClock clock, int maximumTagBytes = 4 * 1024 * 1024,
    Func<long, CancellationToken, ValueTask>? reservePayload = null)
{
    private readonly byte[] _header = new byte[11];
    private readonly byte[] _previous = new byte[4];
    private bool _headerRead;

    public async Task<FlvHeader> ReadHeaderAsync(CancellationToken token)
    {
        if (_headerRead) throw new InvalidOperationException("FLV header already read.");
        await input.ReadExactlyAsync(_header.AsMemory(0, 9), token).ConfigureAwait(false);
        if (_header[0] != 'F' || _header[1] != 'L' || _header[2] != 'V' || _header[3] != 1 || (_header[4] & ~5) != 0)
            throw new InvalidDataException("En-tête FLV v1 invalide.");
        var result = new FlvHeader((_header[4] & 4) != 0, (_header[4] & 1) != 0);
        uint offset = BinaryPrimitives.ReadUInt32BigEndian(_header.AsSpan(5, 4));
        if (offset is < 9 or > 4096) throw new InvalidDataException("Offset d'en-tête FLV hors limites.");
        // Extended headers are bounded and skipped without allocating from the media budget.
        for (uint remaining = offset - 9; remaining > 0;)
        {
            int count = (int)Math.Min(remaining, (uint)_header.Length);
            await input.ReadExactlyAsync(_header.AsMemory(0, count), token).ConfigureAwait(false);
            remaining -= (uint)count;
        }
        await input.ReadExactlyAsync(_previous, token).ConfigureAwait(false);
        if (BinaryPrimitives.ReadUInt32BigEndian(_previous) != 0) throw new InvalidDataException("PreviousTagSize0 doit être nul.");
        _headerRead = true;
        return result;
    }

    public async ValueTask<FlvTag?> ReadTagAsync(CancellationToken token)
    {
        if (!_headerRead) throw new InvalidOperationException("Read the FLV header first.");
        if (await input.ReadAsync(_header.AsMemory(0, 1), token).ConfigureAwait(false) == 0) return null;
        await input.ReadExactlyAsync(_header.AsMemory(1, 10), token).ConfigureAwait(false);
        if (_header[0] is not (8 or 9 or 18) || _header[8] != 0 || _header[9] != 0 || _header[10] != 0)
            throw new InvalidDataException("Type, filtre ou StreamID FLV non pris en charge.");
        int length = ReadUInt24(_header.AsSpan(1, 3));
        if (length > maximumTagBytes) throw new InvalidDataException("Tag FLV trop volumineux.");
        uint timestamp = (uint)ReadUInt24(_header.AsSpan(4, 3)) | ((uint)_header[7] << 24);
        long charge = length + FlvTag.AllocationOverhead;
        if (reservePayload is null) budget.Reserve(charge);
        else await reservePayload(charge, token).ConfigureAwait(false);
        try
        {
            // Exact sized arrays avoid hidden pool capacity and retained payloads after release.
            byte[] payload = GC.AllocateUninitializedArray<byte>(length);
            await input.ReadExactlyAsync(payload, token).ConfigureAwait(false);
            await input.ReadExactlyAsync(_previous, token).ConfigureAwait(false);
            if (BinaryPrimitives.ReadUInt32BigEndian(_previous) != length + 11)
                throw new InvalidDataException("PreviousTagSize FLV incorrect.");
            return new FlvTag((FlvTagType)_header[0], timestamp, clock.NowMilliseconds, payload, budget);
        }
        catch { budget.Release(charge); throw; }
    }

    internal static int ReadUInt24(ReadOnlySpan<byte> value) => (value[0] << 16) | (value[1] << 8) | value[2];
}
