using System.Buffers.Binary;
using Temhalo.Core.Buffering;
using Temhalo.Core.Flv;
using Temhalo.Core.Timing;

namespace Temhalo.Core.Tests;

internal static class FlvTestData
{
    public static byte[] Avc => [0x17, 0, 0, 0, 0, 1, 0x64, 0, 0x1F, 0xFF, 0xE1, 0, 2, 0x67, 0x64, 1, 0, 2, 0x68, 0xEE];
    public static byte[] Aac => [0xAF, 0, 0x11, 0x90];
    public static byte[] Video(bool idr, int composition = 0, bool? keyFlag = null)
        => [(byte)((keyFlag ?? idr) ? 0x17 : 0x27), 1, (byte)(composition >> 16), (byte)(composition >> 8), (byte)composition, 0, 0, 0, 2, (byte)(idr ? 0x65 : 0x41), 0x88];
    public static byte[] Audio(byte marker = 1) => [0xAF, 1, 0x21, marker];

    public static byte[] File(params (FlvTagType Type, uint Timestamp, byte[] Payload)[] tags)
    {
        using var stream = new MemoryStream();
        stream.Write([0x46, 0x4C, 0x56, 1, 5, 0, 0, 0, 9, 0, 0, 0, 0]);
        foreach (var tag in tags) WriteTag(stream, tag.Type, tag.Timestamp, tag.Payload);
        return stream.ToArray();
    }

    public static void WriteTag(Stream stream, FlvTagType type, uint timestamp, byte[] payload)
    {
        byte[] header = [(byte)type, (byte)(payload.Length >> 16), (byte)(payload.Length >> 8), (byte)payload.Length,
            (byte)(timestamp >> 16), (byte)(timestamp >> 8), (byte)timestamp, (byte)(timestamp >> 24), 0, 0, 0];
        stream.Write(header); stream.Write(payload);
        Span<byte> previous = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(previous, (uint)(11 + payload.Length));
        stream.Write(previous);
    }

    public static async Task<List<(FlvTagType Type, uint Timestamp, byte[] Payload)>> ReadAllAsync(byte[] bytes)
    {
        using var input = new MemoryStream(bytes);
        var budget = new FlvMemoryBudget(64 * 1024 * 1024);
        var reader = new FlvTagReader(input, budget, new MonotonicMediaClock());
        await reader.ReadHeaderAsync(CancellationToken.None);
        var result = new List<(FlvTagType, uint, byte[])>();
        while (await reader.ReadTagAsync(CancellationToken.None) is { } tag)
        {
            using (tag) result.Add((tag.Type, tag.Timestamp, tag.Payload.ToArray()));
        }
        if (budget.UsedBytes != 0) throw new InvalidOperationException("Test decoder leaked a tag.");
        return result;
    }
}

internal sealed class ManualMediaClock : IMediaClock
{
    private long _now;
    private TaskCompletionSource _advanced = NewSignal();
    private static TaskCompletionSource NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    public long NowMilliseconds => Interlocked.Read(ref _now);
    public void AdvanceTo(long milliseconds)
    {
        if (milliseconds < NowMilliseconds) throw new ArgumentOutOfRangeException(nameof(milliseconds));
        Interlocked.Exchange(ref _now, milliseconds);
        Interlocked.Exchange(ref _advanced, NewSignal()).TrySetResult();
    }
    public async Task DelayUntilAsync(long deadlineMilliseconds, CancellationToken cancellationToken)
    {
        while (true)
        {
            var changed = Volatile.Read(ref _advanced);
            if (NowMilliseconds >= deadlineMilliseconds) return;
            await changed.Task.WaitAsync(cancellationToken);
        }
    }
}
