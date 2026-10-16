using Temhalo.Core.Buffering;

namespace Temhalo.Core.Flv;

public enum FlvTagType : byte { Audio = 8, Video = 9, Script = 18 }
public sealed record FlvHeader(bool HasAudio, bool HasVideo);

/// <summary>One immutable payload. Reference counting keeps cached codec headers charged once.</summary>
public sealed class FlvTag : IDisposable
{
    private readonly FlvMemoryBudget _budget;
    private int _references = 1;
    internal const int AllocationOverhead = 128;
    public FlvTagType Type { get; }
    public uint Timestamp { get; }
    public long ArrivalMilliseconds { get; }
    public ReadOnlyMemory<byte> Payload { get; }
    // Set by the sole producer before publication. Codec generations follow the queued media.
    public long InputGeneration { get; internal set; }
    public FlvHeader? SourceHeader { get; internal set; }

    internal FlvTag(FlvTagType type, uint timestamp, long arrival, byte[] payload, FlvMemoryBudget budget)
        => (Type, Timestamp, ArrivalMilliseconds, Payload, _budget) = (type, timestamp, arrival, payload, budget);

    /// <summary>Acquire one owned reference; pair it with Dispose. Call only while already owning a reference.</summary>
    public FlvTag Retain() { Interlocked.Increment(ref _references); return this; }
    public void Dispose()
    {
        if (Interlocked.Decrement(ref _references) == 0) _budget.Release(Payload.Length + AllocationOverhead);
    }
}
