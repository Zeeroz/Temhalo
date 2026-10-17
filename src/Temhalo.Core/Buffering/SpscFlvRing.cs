using Temhalo.Core.Flv;

namespace Temhalo.Core.Buffering;

/// <summary>Single producer/single consumer circular queue; no blocking lock on the packet path.</summary>
internal sealed class SpscFlvRing : IDisposable
{
    private readonly FlvTag?[] _slots;
    private readonly FlvMemoryBudget _budget;
    private readonly long _charge;
    private long _head, _tail;
    private int _completed;
    private TaskCompletionSource _changed = NewSignal();
    private static TaskCompletionSource NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    public bool Completed => Volatile.Read(ref _completed) != 0;
    internal long HeadPosition => Volatile.Read(ref _head);
    internal long TailPosition => Volatile.Read(ref _tail);
    // Consumer-only lookahead: head does not advance, so the producer cannot reuse these slots.
    internal FlvTag Peek(long position)
    {
        if (position < _head || position >= Volatile.Read(ref _tail)) throw new ArgumentOutOfRangeException(nameof(position));
        return _slots[position % _slots.Length] ?? throw new InvalidOperationException("Unpublished FLV slot.");
    }
    public long Count => Math.Max(0, Volatile.Read(ref _tail) - Volatile.Read(ref _head));

    public SpscFlvRing(int capacity, FlvMemoryBudget budget)
    {
        _budget = budget;
        _charge = (long)capacity * IntPtr.Size + 512;
        budget.Reserve(_charge);
        try { _slots = new FlvTag?[capacity]; }
        catch { budget.Release(_charge); throw; }
    }

    public bool TryWrite(FlvTag tag)
    {
        long tail = _tail;
        if (tail - Volatile.Read(ref _head) >= _slots.Length) return false;
        _slots[tail % _slots.Length] = tag;
        Volatile.Write(ref _tail, tail + 1);
        Volatile.Read(ref _changed).TrySetResult();
        return true;
    }

    public FlvTag? TryRead()
    {
        long head = _head;
        if (head == Volatile.Read(ref _tail)) return null;
        int index = (int)(head % _slots.Length);
        var tag = _slots[index];
        _slots[index] = null;
        Volatile.Write(ref _head, head + 1);
        return tag;
    }

    public Task WaitForChangeAsync(CancellationToken token)
    {
        var observed = Volatile.Read(ref _changed);
        if (observed.Task.IsCompleted) Interlocked.CompareExchange(ref _changed, NewSignal(), observed);
        return observed.Task.WaitAsync(token);
    }

    public void Complete()
    { Volatile.Write(ref _completed, 1); Volatile.Read(ref _changed).TrySetResult(); }

    internal void SignalChange() => Volatile.Read(ref _changed).TrySetResult();

    // Only after producer AND consumer have completed.
    public void Dispose()
    {
        while (TryRead() is { } tag) tag.Dispose();
        _budget.Release(_charge);
    }
}
