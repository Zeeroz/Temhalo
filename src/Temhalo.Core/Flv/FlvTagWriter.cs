using System.Buffers.Binary;
using Temhalo.Core.Abstractions;

namespace Temhalo.Core.Flv;

public sealed class FlvTagWriter(Stream output)
{
    private readonly byte[] _header = new byte[11];
    private readonly byte[] _previous = new byte[4];
    private long _bytesWritten;
    public long BytesWritten => Interlocked.Read(ref _bytesWritten);

    public async Task WriteHeaderAsync(FlvHeader header, CancellationToken token)
    {
        if (output is IFlvPacketSink packets)
        { await packets.WriteHeaderAsync(header, token).ConfigureAwait(false); Interlocked.Add(ref _bytesWritten, 13); return; }
        byte[] bytes = [(byte)'F', (byte)'L', (byte)'V', 1, (byte)((header.HasAudio ? 4 : 0) | (header.HasVideo ? 1 : 0)), 0, 0, 0, 9, 0, 0, 0, 0];
        await output.WriteAsync(bytes, token).ConfigureAwait(false);
        Interlocked.Add(ref _bytesWritten, bytes.Length);
    }

    public async ValueTask WriteTagAsync(FlvTag tag, uint timestamp, CancellationToken token)
    {
        if (output is IFlvPacketSink packets)
        { await packets.WriteTagAsync(tag, timestamp, token).ConfigureAwait(false); Interlocked.Add(ref _bytesWritten, tag.Payload.Length + 15); return; }
        _header[0] = (byte)tag.Type;
        WriteUInt24(_header.AsSpan(1, 3), (uint)tag.Payload.Length);
        WriteUInt24(_header.AsSpan(4, 3), timestamp);
        _header[7] = (byte)(timestamp >> 24);
        // StreamID remains zero. Composition offsets and codec bytes are never modified.
        BinaryPrimitives.WriteUInt32BigEndian(_previous, (uint)(tag.Payload.Length + 11));
        await output.WriteAsync(_header, token).ConfigureAwait(false);
        await output.WriteAsync(tag.Payload, token).ConfigureAwait(false);
        await output.WriteAsync(_previous, token).ConfigureAwait(false);
        Interlocked.Add(ref _bytesWritten, tag.Payload.Length + 15);
    }

    private static void WriteUInt24(Span<byte> target, uint value)
    { target[0] = (byte)(value >> 16); target[1] = (byte)(value >> 8); target[2] = (byte)value; }
}
