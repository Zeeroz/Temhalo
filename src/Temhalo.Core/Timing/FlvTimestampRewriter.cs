namespace Temhalo.Core.Timing;

/// <summary>One A/V offset, not independent per-packet increments. Composition time stays in payload.</summary>
public sealed class FlvTimestampRewriter
{
    private long? _reference;
    private long _offset, _outputHigh = -1, _presentationHigh = -1;
    private bool _started;
    public void ResetSourceReference() => _reference = null;
    public long Unwrap(uint timestamp)
    {
        long reference = _reference ?? timestamp;
        long candidate = (reference & ~0xFFFFFFFFL) | timestamp;
        if (candidate - reference > 0x80000000L) candidate -= 0x100000000L;
        else if (reference - candidate > 0x80000000L) candidate += 0x100000000L;
        _reference = Math.Max(reference, candidate);
        return candidate;
    }

    public void BeginSegment(long sourceDts, int compositionMilliseconds = 0, bool collapseGap = false)
    {
        long start = _started ? (collapseGap ? _outputHigh + 1 : Math.Max(_outputHigh + 1, sourceDts + _offset)) : 0;
        if (_started) start = Math.Max(start, _presentationHigh + 1 - compositionMilliseconds);
        _offset = start - sourceDts;
        _started = true;
    }

    public uint Map(long sourceDts, int compositionMilliseconds = 0)
    {
        if (!_started) throw new InvalidOperationException("A keyframe must start the timeline.");
        long mapped = checked(sourceDts + _offset);
        if (mapped is < 0 or > uint.MaxValue)
            throw new InvalidDataException("Timestamp de sortie hors de la plage FLV (environ 49,7 jours).");
        _outputHigh = Math.Max(_outputHigh, mapped);
        _presentationHigh = Math.Max(_presentationHigh, mapped + compositionMilliseconds);
        return (uint)mapped;
    }
}
