using Temhalo.Core.Flv;

namespace Temhalo.Core.Buffering;

public partial class FlvDelayMediaTransfer
{
    private bool IsReadyIdr(FlvTag tag) => tag.InputGeneration == _inputGeneration &&
        _avc is not null && (!_sourceHeader.HasAudio || _aac is not null) &&
        tag.Type == FlvTagType.Video && tag.Payload.Length >= 5 && (tag.Payload.Span[0] & 15) == 7 &&
        tag.Payload.Span[1] == 1 && AvcAacInspector.IsIdr(tag.Payload.Span, _nalLengthSize);

    private async Task WaitForDeadlineAsync(long deadline, Task changed, CancellationToken token)
    {
        using var wait = CancellationTokenSource.CreateLinkedTokenSource(token);
        Task due = _clock.DelayUntilAsync(deadline, wait.Token);
        await Task.WhenAny(due, changed.WaitAsync(wait.Token)).ConfigureAwait(false);
        wait.Cancel();
        try { await due.ConfigureAwait(false); }
        catch (OperationCanceledException) when (!token.IsCancellationRequested) { }
        token.ThrowIfCancellationRequested();
    }

    private static async Task WaitForWorkAsync(SpscFlvRing ring, Task changed, CancellationToken token)
    {
        using var wait = CancellationTokenSource.CreateLinkedTokenSource(token);
        Task available = ring.WaitForChangeAsync(wait.Token);
        await Task.WhenAny(available, changed.WaitAsync(wait.Token)).ConfigureAwait(false);
        wait.Cancel();
        try { await available.ConfigureAwait(false); }
        catch (OperationCanceledException) when (!token.IsCancellationRequested) { }
        token.ThrowIfCancellationRequested();
    }

    private async Task<FlvTag?> SeekAsync(FlvTag? pending, SpscFlvRing ring, CancellationToken token)
    {
        long head = ring.HeadPosition, tail = ring.TailPosition;
        long pendingPosition = head - 1;
        long cutoff = _clock.NowMilliseconds - _planner.ScheduledDelay;
        long? before = null, after = null;
        int lengthSize = _nalLengthSize;
        bool hasAvc = _avc is not null, hasAac = _aac is not null;
        long generation = _inputGeneration;
        bool Inspect(FlvTag tag)
        {
            if (tag.InputGeneration != generation)
            { generation = tag.InputGeneration; lengthSize = 0; hasAvc = hasAac = false; }
            return InspectIdr(tag, tag.SourceHeader ?? _sourceHeader, ref lengthSize, ref hasAvc, ref hasAac);
        }
        try
        {
            // No dequeue during lookahead: all payloads and codec generations remain owned by the ring.
            if (pending is not null && Inspect(pending))
            {
                if (pending.ArrivalMilliseconds <= cutoff) before = pendingPosition;
                else after = pendingPosition;
            }
            for (long position = head; position < tail; position++)
            {
                if ((position & 255) == 0) token.ThrowIfCancellationRequested();
                var tag = ring.Peek(position);
                if (!Inspect(tag)) continue;
                if (tag.ArrivalMilliseconds <= cutoff) before = position;
                else after ??= position;
            }
            // At zero this is the latest available IDR. Otherwise prefer the last IDR before the target.
            long selected = before ?? after ?? tail;
            if (pending is not null && selected == pendingPosition)
            { var result = pending; pending = null; return result; }
            if (pending is not null)
            {
                var discarded = pending;
                pending = null;
                using (discarded) await ProcessTagAsync(discarded, token, discardOnly: true).ConfigureAwait(false);
            }
            while (ring.HeadPosition < selected)
            {
                using var dropped = ring.TryRead() ?? throw new InvalidOperationException("FLV seek lost a queue slot.");
                await ProcessTagAsync(dropped, token, discardOnly: true).ConfigureAwait(false);
            }
            return selected < tail ? ring.TryRead() : null;
        }
        finally { pending?.Dispose(); }
    }

    private static bool InspectIdr(FlvTag tag, FlvHeader header, ref int lengthSize, ref bool hasAvc, ref bool hasAac)
    {
        if (tag.Type == FlvTagType.Script) return false;
        if (tag.Type == FlvTagType.Audio)
        {
            if (!header.HasAudio || tag.Payload.Length < 3 || (tag.Payload.Span[0] >> 4) != 10)
                throw new InvalidDataException("Piste AAC invalide pendant la recherche d'IDR.");
            if (tag.Payload.Span[1] == 0)
            {
                if (tag.Payload.Length < 4) throw new InvalidDataException("Configuration AAC tronquée.");
                hasAac = true;
            }
            return false;
        }
        if (tag.Payload.Length < 5 || (tag.Payload.Span[0] & 15) != 7)
            throw new InvalidDataException("Piste AVC invalide pendant la recherche d'IDR.");
        if (tag.Payload.Span[1] == 0)
        { lengthSize = AvcAacInspector.ValidateAvcConfiguration(tag.Payload.Span); hasAvc = true; return false; }
        return hasAvc && (!header.HasAudio || hasAac) && tag.Payload.Span[1] == 1 &&
            AvcAacInspector.IsIdr(tag.Payload.Span, lengthSize);
    }
}
