namespace Temhalo.Core.Buffering;

public sealed class BufferLimitExceededException(string message) : Exception(message);

/// <summary>Counts owned payload allocations, conservative tag overhead and the ring itself.</summary>
public sealed class FlvMemoryBudget
{
    private long _used, _peak;
    public long MaximumBytes { get; }
    public long UsedBytes => Interlocked.Read(ref _used);
    public long PeakBytes => Interlocked.Read(ref _peak);

    public FlvMemoryBudget(long maximumBytes)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumBytes, 1024);
        MaximumBytes = maximumBytes;
    }

    internal void Reserve(long bytes)
    {
        if (!TryReserve(bytes))
            throw new BufferLimitExceededException("Limite mémoire du buffer atteinte ; arrêt sans réduction du délai.");
    }

    internal bool TryReserve(long bytes)
    {
        while (true)
        {
            long current = UsedBytes;
            if (bytes > MaximumBytes - current)
                return false;
            long next = current + bytes;
            if (Interlocked.CompareExchange(ref _used, next, current) != current) continue;
            long peak;
            while (next > (peak = PeakBytes) && Interlocked.CompareExchange(ref _peak, next, peak) != peak) { }
            return true;
        }
    }

    internal void Release(long bytes) => Interlocked.Add(ref _used, -bytes);
}
