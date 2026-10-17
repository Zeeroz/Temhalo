using Temhalo.Core.Flv;

namespace Temhalo.Core.Buffering;

/// <summary>
/// Consumer-owned rolling history. Payloads are shared with the live queue, codec caches and outputs;
/// only references are retained. Each indexed IDR owns the exact codec headers needed to replay it.
/// </summary>
internal sealed class FlvPlaybackHistory : IDisposable
{
    internal readonly record struct Entry(FlvTag Tag, FlvTag? Avc, FlvTag? Aac);
    private readonly Entry[] _entries;
    private readonly long[] _keyframes;
    private readonly FlvMemoryBudget _budget;
    private readonly long _charge;
    private long _head, _tail, _keyHead, _keyTail;
    public long Head => _head;
    public long Tail => _tail;
    public long Count => _tail - _head;
    public long EvictedTags { get; private set; }
    public long? OldestKeyframeArrival => _keyHead == _keyTail ? null : At(_keyframes[_keyHead % _keyframes.Length]).Tag.ArrivalMilliseconds;

    public FlvPlaybackHistory(int capacity, FlvMemoryBudget budget)
    {
        _budget = budget;
        // Three references per entry + the IDR index, with conservative array/object overhead.
        _charge = capacity * 40L + 512;
        budget.Reserve(_charge);
        try { _entries = new Entry[capacity]; _keyframes = new long[capacity]; }
        catch { budget.Release(_charge); throw; }
    }

    public Entry At(long position) => position >= _head && position < _tail
        ? _entries[position % _entries.Length] : throw new ArgumentOutOfRangeException(nameof(position));

    public void Add(FlvTag tag, FlvTag? avc, FlvTag? aac)
    {
        if (Count == _entries.Length) EvictOne(); // Only reached when reading new live packets, never mid-replay.
        _entries[_tail % _entries.Length] = new(tag.Retain(), avc?.Retain(), aac?.Retain());
        if (avc is not null) _keyframes[_keyTail++ % _keyframes.Length] = _tail;
        _tail++;
    }

    public long? FindKeyframe(long cutoff, long minimumPosition = 0, long? olderThanArrival = null)
    {
        long? before = null, after = null;
        for (long i = _keyHead; i < _keyTail; i++)
        {
            long position = _keyframes[i % _keyframes.Length];
            if (position < minimumPosition) continue;
            long arrival = At(position).Tag.ArrivalMilliseconds;
            if (olderThanArrival.HasValue && arrival >= olderThanArrival.Value) break;
            if (arrival <= cutoff) before = position;
            else { after = position; break; }
        }
        return before ?? after;
    }

    public void Trim(long cutoff, long protectedPosition)
    {
        // Retain the last IDR before the time limit, including its complete GOP.
        while (_keyTail - _keyHead > 1)
        {
            long next = _keyframes[(_keyHead + 1) % _keyframes.Length];
            if (next >= protectedPosition || At(next).Tag.ArrivalMilliseconds > cutoff) break;
            while (_head < next) EvictOne();
        }
    }

    public bool Reclaim(long neededBytes, long protectedPosition)
    {
        while (_budget.MaximumBytes - _budget.UsedBytes < neededBytes && _head < Math.Min(protectedPosition, _tail))
            EvictOne();
        return _budget.MaximumBytes - _budget.UsedBytes >= neededBytes;
    }

    private void EvictOne()
    {
        int slot = (int)(_head % _entries.Length);
        var entry = _entries[slot]; _entries[slot] = default;
        if (_keyHead < _keyTail && _keyframes[_keyHead % _keyframes.Length] == _head) _keyHead++;
        _head++; EvictedTags++;
        entry.Tag.Dispose(); entry.Avc?.Dispose(); entry.Aac?.Dispose();
    }

    public void Dispose()
    {
        while (_head < _tail) EvictOne();
        _budget.Release(_charge);
    }
}
