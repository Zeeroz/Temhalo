using Temhalo.Core.Flv;

namespace Temhalo.Core.Buffering;

public partial class FlvDelayMediaTransfer
{
    private FlvPlaybackHistory? _history;
    private long? _replayPosition;
    private long _lastEmittedArrival = -1, _latestInputArrival = -1, _historyOldest = -1, _historyTags, _historyEvicted, _replayed, _replayQueued;
    private sealed record MemoryRequest(long Bytes, TaskCompletionSource<bool> Completion);
    private MemoryRequest? _memoryRequest;

    private async ValueTask ReservePayloadAsync(long bytes, SpscFlvRing ring, CancellationToken token)
    {
        if (_budget.TryReserve(bytes)) return;
        // The producer asks the consumer to release historical references. Neither thread locks the other.
        var request = new MemoryRequest(bytes, new(TaskCreationOptions.RunContinuationsAsynchronously));
        Volatile.Write(ref _memoryRequest, request);
        ring.SignalChange();
        bool reclaimed;
        try { reclaimed = await request.Completion.Task.WaitAsync(TimeSpan.FromSeconds(1), token).ConfigureAwait(false); }
        catch (TimeoutException)
        { throw new BufferLimitExceededException("Limite RAM atteinte pendant une sortie bloquée. Relais interrompu."); }
        if (!reclaimed || !_budget.TryReserve(bytes))
            throw new BufferLimitExceededException("Historique libéré, mais la limite RAM ne permet plus de conserver le délai demandé. Relais interrompu.");
    }

    private void MaintainHistory()
    {
        long protectedPosition = _replayPosition ?? long.MaxValue;
        _history?.Trim(_clock.NowMilliseconds - 300_000, protectedPosition);
        var request = Interlocked.Exchange(ref _memoryRequest, null);
        if (request is not null)
            request.Completion.TrySetResult(_history?.Reclaim(request.Bytes, protectedPosition) == true);
        PublishHistory();
    }

    private void PublishHistory()
    {
        Interlocked.Exchange(ref _historyOldest, _history?.OldestKeyframeArrival ?? -1);
        Interlocked.Exchange(ref _historyTags, _history?.Count ?? 0);
        Interlocked.Exchange(ref _historyEvicted, _history?.EvictedTags ?? 0);
        Interlocked.Exchange(ref _replayQueued, _history is not null && _replayPosition.HasValue
            ? Math.Max(0, _history.Tail - _replayPosition.Value) : 0);
    }

    private void Remember(FlvTag tag)
    {
        if (_history is null || tag.Type == FlvTagType.Script) return;
        bool idr = IsReadyIdr(tag);
        _history.Add(tag, idr ? _avc : null, idr ? _aac : null);
        PublishHistory();
    }

    private FlvTag? ReadReplay()
    {
        if (_replayPosition is not { } position || _history is null) return null;
        if (position >= _history.Tail) { _replayPosition = null; return null; }
        _replayPosition = position + 1;
        Interlocked.Exchange(ref _replayQueued, _history.Tail - _replayPosition.Value);
        return _history.At(position).Tag.Retain();
    }

    private FlvTag? TakeNext(SpscFlvRing ring, out bool historical)
    {
        var replay = ReadReplay();
        historical = replay is not null;
        if (replay is not null) return replay;
        var live = _livePending; _livePending = null;
        return live ?? ring.TryRead();
    }

    private void RestoreReplay(long position)
    {
        var entry = _history!.At(position);
        if (entry.Avc is null) throw new InvalidOperationException("La relecture doit commencer sur une image clé indexée.");
        // Acquire before releasing: an old configuration can still be the currently active one.
        var avc = entry.Avc.Retain(); var aac = entry.Aac?.Retain();
        _avc?.Dispose(); _aac?.Dispose(); _avc = avc; _aac = aac;
        _inputGeneration = entry.Tag.InputGeneration;
        _sourceHeader = entry.Tag.SourceHeader ?? new(true, true);
        _nalLengthSize = AvcAacInspector.ValidateAvcConfiguration(avc.Payload.Span);
        _lastVideoDts = _lastAudioDts = _segmentStart = null;
        _timeline.ResetSourceReference();
        _collapseNextSegment = true;
        Volatile.Write(ref _waiting, 1);
        _replayPosition = position;
    }

    private bool TryIncreaseFromHistory(ref FlvTag? pending, ref bool historical, int targetSeconds, long now)
    {
        // Use the request already applied by the planner, even if a newer UI command arrives meanwhile.
        long cutoff = now - targetSeconds * 1000L;
        if (_history?.FindKeyframe(cutoff, olderThanArrival: _lastEmittedArrival) is not { } position) return false;
        long available = Math.Max(0, now - _history.At(position).Tag.ArrivalMilliseconds);
        if (available <= _planner.Snapshot(now).ActiveDelayMilliseconds) return false;
        // A newly received, not-yet-processed tag must remain before the producer queue.
        // Reaching the end of replay rejoins that tag without losing it.
        if (pending is not null && !historical)
        {
            // Such a tag has not been emitted: keep it separately until history playback catches up.
            _livePending = pending; pending = null;
        }
        pending?.Dispose(); pending = null;
        RestoreReplay(position);
        Interlocked.Exchange(ref _recoveryDelay, 0);
        _planner.ReplayFrom(available, now);
        pending = ReadReplay(); historical = true;
        return true;
    }

    // At most one live tag is already dequeued when a backward jump is requested.
    private FlvTag? _livePending;
}
