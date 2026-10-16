using System.Runtime.ExceptionServices;
using Temhalo.Core.Abstractions;
using Temhalo.Core.Flv;
using Temhalo.Core.Timing;

namespace Temhalo.Core.Buffering;

/// <summary>Session-owned SPSC buffer; input and output connections can restart independently.</summary>
public partial class FlvDelayMediaTransfer : IMediaTransfer
{
    // Do not report success when an output holds media a full second past its scheduled delay.
    private const long OutputBacklogToleranceMilliseconds = 1000;
    private readonly FixedDelayOptions _options;
    private readonly IMediaClock _clock;
    private readonly FlvMemoryBudget _budget;
    private readonly DelayController _control;
    private readonly DelayTransitionPlanner _planner;
    private bool _collapseNextSegment;
    private SpscFlvRing? _ring;
    private FlvTagWriter? _writer;
    private FlvTag? _avc, _aac;
    private readonly FlvTimestampRewriter _timeline = new();
    private int _started, _waiting = 1, _inputCompleted, _truncated;
    private int _nalLengthSize;
    private bool _outputHeaderWritten;
    private long? _lastVideoDts, _lastAudioDts, _segmentStart;
    private long _readTags, _emitted, _skipped, _discontinuities;
    private long _inputBytes;
    private IFlvOutputConnection? _connection;
    private sealed record WriterCounters(long Completed, FlvTagWriter? Active);
    private WriterCounters _writerCounters = new(0, null);
    private long _recoveryDelay, _outputReconnections, _inputSessions;
    private long _inputGeneration = -1;
    private FlvHeader _sourceHeader = new(true, true);
    private bool _recoveringOutput;
    private long _minimumAge = long.MaxValue, _lastAge = -1, _maximumAge = -1;

    public FlvDelayMediaTransfer(FixedDelayOptions options, IMediaClock? clock = null, DelayController? control = null)
    {
        options.Validate();
        _options = options;
        _clock = clock ?? new MonotonicMediaClock();
        _budget = new FlvMemoryBudget(options.MaximumBufferBytes);
        _control = control ?? new DelayController(options.DelaySeconds);
        _planner = new DelayTransitionPlanner(options, _control.Request);
    }

    public long BytesTransferred
    {
        get { var counters = Volatile.Read(ref _writerCounters); return counters.Completed + (counters.Active?.BytesWritten ?? 0); }
    }
    private void SetWriter(FlvTagWriter? writer)
    {
        var previous = _writerCounters;
        _writer = writer;
        // Atomically replace active/completed counters: no transient double count during a reconnect.
        Volatile.Write(ref _writerCounters, new(previous.Completed + (writer is null ? previous.Active?.BytesWritten ?? 0 : 0), writer));
    }
    public FixedDelaySnapshot Snapshot => new(_control.Request.TargetSeconds, _budget.UsedBytes, _budget.PeakBytes,
        _budget.MaximumBytes, _budget.UsedBytes >= _budget.MaximumBytes * _options.WarningUsageRatio,
        _budget.PeakBytes >= _budget.MaximumBytes * _options.WarningUsageRatio, Volatile.Read(ref _ring)?.Count ?? 0,
        Interlocked.Read(ref _readTags), Interlocked.Read(ref _emitted), Interlocked.Read(ref _skipped),
        Interlocked.Read(ref _discontinuities), Volatile.Read(ref _waiting) != 0, Volatile.Read(ref _inputCompleted) != 0,
        Volatile.Read(ref _truncated) != 0, Interlocked.Read(ref _minimumAge) is var min && min != long.MaxValue ? min : null,
        Interlocked.Read(ref _lastAge) is var last && last >= 0 ? last : null,
        Interlocked.Read(ref _maximumAge) is var max && max >= 0 ? max : null, ControlSnapshot(),
        Interlocked.Read(ref _inputBytes), Interlocked.Read(ref _recoveryDelay),
        Interlocked.Read(ref _outputReconnections), Interlocked.Read(ref _inputSessions));

    private DelayTransitionSnapshot ControlSnapshot()
    {
        var control = _planner.Snapshot(_clock.NowMilliseconds);
        if (Interlocked.Read(ref _recoveryDelay) > 0)
            return control with { TargetReached = false, Phase = control.Phase == "Stable" ? "RecoveryBacklog" : control.Phase };
        // A slow write need not fail or reconnect. Report the measured backlog separately from
        // the scheduler's target, keeping the actual age visible and retaining all queued media.
        return control.TargetReached &&
            Interlocked.Read(ref _lastAge) - control.ActiveDelayMilliseconds >= OutputBacklogToleranceMilliseconds
            ? control with { TargetReached = false, Phase = "OutputBacklog" }
            : control;
    }

    public Task TransferAsync(Stream input, Stream output, CancellationToken cancellationToken)
        => ExecuteAsync(SingleInput(input), output, null, cancellationToken);

    /// <summary>Input enumeration owns its streams. Ending it drains the queue; cancellation aborts.</summary>
    public Task TransferSessionsAsync(IAsyncEnumerable<Stream> inputs, IFlvOutputConnection output,
        CancellationToken cancellationToken) => ExecuteAsync(inputs, null, output, cancellationToken);

    private static async IAsyncEnumerable<Stream> SingleInput(Stream input)
    { yield return input; await Task.CompletedTask; }

    private async Task ExecuteAsync(IAsyncEnumerable<Stream> inputs, Stream? output, IFlvOutputConnection? connection,
        CancellationToken cancellationToken)
    {
        if (Interlocked.Exchange(ref _started, 1) != 0) throw new InvalidOperationException("Une instance correspond à une seule session FLV.");
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        using var ring = new SpscFlvRing(_options.TagCapacity, _budget);
        Volatile.Write(ref _ring, ring);
        _connection = connection;
        if (output is not null) SetWriter(new FlvTagWriter(output));
        try
        {
            // Independent ingest must keep running while the consumer waits for a due time.
            Task producer = Task.Run(() => ProduceSessionsAsync(inputs, ring, cancellation), CancellationToken.None);
            Task consumer = ConsumeAsync(ring, cancellation);
            try { await Task.WhenAll(producer, consumer).ConfigureAwait(false); }
            catch
            {
                // Preserve the actual failure instead of masking it with cancellation of its peer.
                Exception? failure = producer.Exception?.GetBaseException() ?? consumer.Exception?.GetBaseException();
                if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
                throw;
            }
            if (output is not null) await output.FlushAsync(cancellation.Token).ConfigureAwait(false);
        }
        finally
        {
            _avc?.Dispose(); _aac?.Dispose();
            _avc = _aac = null;
            // The using scope releases queued tags and ring storage after both tasks have ended.
        }
    }

    private async Task ProduceSessionsAsync(IAsyncEnumerable<Stream> inputs, SpscFlvRing ring, CancellationTokenSource cancellation)
    {
        try
        {
            long generation = 0;
            await foreach (Stream input in inputs.WithCancellation(cancellation.Token).ConfigureAwait(false))
            {
                var reader = new FlvTagReader(input, _budget, _clock, _options.MaximumTagBytes);
                bool headerRead = false;
                try
                {
                    FlvHeader header = await reader.ReadHeaderAsync(cancellation.Token).ConfigureAwait(false);
                    if (!header.HasVideo) throw new InvalidDataException("Le MVP attend une piste vidéo H.264.");
                    headerRead = true;
                    Volatile.Write(ref _inputCompleted, 0);
                    Interlocked.Increment(ref _inputSessions);
                    while (await reader.ReadTagAsync(cancellation.Token).ConfigureAwait(false) is { } tag)
                    {
                        tag.InputGeneration = generation;
                        tag.SourceHeader = header;
                        Interlocked.Increment(ref _readTags);
                        Interlocked.Add(ref _inputBytes, tag.Payload.Length + 15L);
                        if (!ring.TryWrite(tag))
                        {
                            tag.Dispose();
                            throw new BufferLimitExceededException("Capacité de tags du buffer atteinte ; arrêt sans réduction du délai.");
                        }
                    }
                }
                catch (EndOfStreamException) { if (headerRead) Volatile.Write(ref _truncated, 1); }
                catch (IOException) when (_connection is not null)
                { if (headerRead) Volatile.Write(ref _truncated, 1); } // A broken input pipe ends this connection, not the RAM session.
                finally { generation++; Volatile.Write(ref _inputCompleted, 1); }
            }
        }
        catch { cancellation.Cancel(); throw; }
        finally { Volatile.Write(ref _inputCompleted, 1); ring.Complete(); }
    }

    private async Task ConsumeAsync(SpscFlvRing ring, CancellationTokenSource cancellation)
    {
        FlvTag? pending = null;
        try
        {
            while (true)
            {
                cancellation.Token.ThrowIfCancellationRequested();
                Task changed = _control.Changed; // Capture before inspecting the mailbox: no lost wakeup.
                var request = _control.Request;
                bool catchUpSeek = request.Version != _planner.Version &&
                    ((_recoveryDelay > 0 && request.TargetSeconds * 1000L <= _planner.ScheduledDelay + _recoveryDelay) ||
                     (request.TargetSeconds == 0 && Interlocked.Read(ref _lastAge) >= OutputBacklogToleranceMilliseconds));
                bool seek = _planner.Apply(request, _clock.NowMilliseconds);
                // A repeated explicit zero can clear slow-output backlog even without IOException.
                // Healthy zero remains idempotent; no automatic content skipping is introduced.
                if (catchUpSeek) { _planner.SeekToTarget(_clock.NowMilliseconds); seek = true; }
                if (seek)
                {
                    Interlocked.Exchange(ref _recoveryDelay, 0);
                    Volatile.Write(ref _waiting, 1);
                    _collapseNextSegment = true;
                    var owned = pending;
                    pending = null; // Seek owns the old pending tag, also on exceptions.
                    pending = await SeekAsync(owned, ring, cancellation.Token).ConfigureAwait(false);
                    continue;
                }
                pending ??= ring.TryRead();
                if (pending is null)
                {
                    if (ring.Completed) pending = ring.TryRead();
                    if (pending is null)
                    {
                        if (ring.Completed) { _planner.MarkEnded(); break; }
                        await WaitForWorkAsync(ring, changed, cancellation.Token).ConfigureAwait(false);
                        continue;
                    }
                }
                long now = _clock.NowMilliseconds;
                if (_planner.Paused)
                {
                    if (now < _planner.PauseEnd)
                    { await WaitForDeadlineAsync(_planner.PauseEnd, changed, cancellation.Token).ConfigureAwait(false); continue; }
                    _planner.FinishPause(now);
                    Volatile.Write(ref _waiting, 1); // Pauses only start before an IDR; resend both configurations.
                }
                long deadline = pending.ArrivalMilliseconds + _planner.ScheduledDelay + _recoveryDelay;
                if (_clock.NowMilliseconds < deadline)
                { await WaitForDeadlineAsync(deadline, changed, cancellation.Token).ConfigureAwait(false); continue; }
                if (_planner.CanStartPause(ring.Completed || Volatile.Read(ref _inputCompleted) != 0) && IsReadyIdr(pending))
                { _planner.StartPause(_clock.NowMilliseconds); continue; }
                try { await ProcessTagAsync(pending, cancellation.Token).ConfigureAwait(false); }
                catch (Exception ex) when (_connection is not null && (ex is IOException or ObjectDisposedException))
                {
                    // Keep the pending tag and all queued media. Only a future IDR may resume output.
                    SetWriter(null);
                    _outputHeaderWritten = false;
                    _collapseNextSegment = _recoveringOutput = true;
                    Volatile.Write(ref _waiting, 1);
                    Interlocked.Increment(ref _outputReconnections);
                    await _connection!.CloseAsync(true, cancellation.Token).ConfigureAwait(false);
                    continue;
                }
                pending.Dispose(); pending = null;
            }
        }
        catch { cancellation.Cancel(); throw; }
        finally { pending?.Dispose(); }
    }

    private async ValueTask ProcessTagAsync(FlvTag tag, CancellationToken token, bool discardOnly = false)
    {
        if (tag.InputGeneration != _inputGeneration) EnterInputGeneration(tag);
        FlvHeader header = _sourceHeader;
        if (tag.Type == FlvTagType.Script) return; // FFmpeg reconstructs output metadata; old duration/index is invalid.
        bool video = tag.Type == FlvTagType.Video;
        if (video)
        {
            if (tag.Payload.Length < 5 || (tag.Payload.Span[0] & 15) != 7 || (tag.Payload.Span[0] >> 4) is not (1 or 2))
                throw new InvalidDataException("Seul AVC/H.264 FLV classique est pris en charge.");
            if (tag.Payload.Span[1] == 0)
            {
                _nalLengthSize = AvcAacInspector.ValidateAvcConfiguration(tag.Payload.Span);
                ReplaceConfiguration(ref _avc, tag);
                return;
            }
            if (tag.Payload.Span[1] == 2) { Volatile.Write(ref _waiting, 1); return; }
            if (tag.Payload.Span[1] != 1) throw new InvalidDataException("AVCPacketType inconnu.");
        }
        else
        {
            if (!header.HasAudio || tag.Payload.Length < 3 || (tag.Payload.Span[0] >> 4) != 10)
                throw new InvalidDataException("Seule une piste AAC déclarée est prise en charge.");
            if (tag.Payload.Span[1] == 0)
            {
                if (tag.Payload.Length < 4) throw new InvalidDataException("AudioSpecificConfig AAC tronquée.");
                ReplaceConfiguration(ref _aac, tag);
                return;
            }
            if (tag.Payload.Span[1] != 1) throw new InvalidDataException("AACPacketType inconnu.");
        }

        long dts = _timeline.Unwrap(tag.Timestamp);
        long? previous = video ? _lastVideoDts : _lastAudioDts;
        if (previous.HasValue && dts < previous.Value)
        {
            Volatile.Write(ref _waiting, 1);
            _lastVideoDts = _lastAudioDts = null;
            Interlocked.Increment(ref _discontinuities);
        }
        if (video) _lastVideoDts = dts; else _lastAudioDts = dts;
        bool idr = video && _avc is not null && AvcAacInspector.IsIdr(tag.Payload.Span, _nalLengthSize);
        int composition = video ? AvcAacInspector.CompositionTime(tag.Payload.Span) : 0;
        if (discardOnly) { Interlocked.Increment(ref _skipped); return; }
        if (Volatile.Read(ref _waiting) != 0)
        {
            if (!idr || _avc is null || (header.HasAudio && _aac is null))
            { Interlocked.Increment(ref _skipped); return; }
            _timeline.BeginSegment(dts, composition, _collapseNextSegment);
            _collapseNextSegment = false;
            _segmentStart = dts;
            uint timestamp = _timeline.Map(dts, composition);
            if (_writer is null)
                SetWriter(new FlvTagWriter(await _connection!.OpenAsync(token).ConfigureAwait(false)));
            var writer = _writer!;
            if (!_outputHeaderWritten)
            {
                await writer.WriteHeaderAsync(header, token).ConfigureAwait(false);
                _outputHeaderWritten = true;
            }
            await writer.WriteTagAsync(_avc, timestamp, token).ConfigureAwait(false);
            if (_aac is not null) await writer.WriteTagAsync(_aac, timestamp, token).ConfigureAwait(false);
            Volatile.Write(ref _waiting, 0);
        }
        if (dts < _segmentStart)
        { Interlocked.Increment(ref _skipped); return; }
        uint mapped = _timeline.Map(dts, composition);
        await _writer!.WriteTagAsync(tag, mapped, token).ConfigureAwait(false);
        if (_recoveringOutput)
        {
            // Retain the outage backlog and pace it at normal speed instead of bursting stale packets.
            Interlocked.Exchange(ref _recoveryDelay, Math.Max(_recoveryDelay,
                _clock.NowMilliseconds - tag.ArrivalMilliseconds - _planner.ScheduledDelay));
            _recoveringOutput = false;
        }
        _planner.MarkMedia(mapped, _clock.NowMilliseconds);
        Interlocked.Increment(ref _emitted);
        long age = _clock.NowMilliseconds - tag.ArrivalMilliseconds;
        Interlocked.Exchange(ref _lastAge, age);
        long min;
        while (age < (min = Interlocked.Read(ref _minimumAge)) && Interlocked.CompareExchange(ref _minimumAge, age, min) != min) { }
        long max;
        while (age > (max = Interlocked.Read(ref _maximumAge)) && Interlocked.CompareExchange(ref _maximumAge, age, max) != max) { }
    }

    private void ReplaceConfiguration(ref FlvTag? cache, FlvTag tag)
    {
        if (cache is not null && cache.Payload.Span.SequenceEqual(tag.Payload.Span)) return;
        bool change = cache is not null;
        cache?.Dispose();
        cache = tag.Retain();
        Volatile.Write(ref _waiting, 1);
        if (change) Interlocked.Increment(ref _discontinuities);
    }

    private void EnterInputGeneration(FlvTag tag)
    {
        if (_inputGeneration >= 0)
        { _collapseNextSegment = true; Interlocked.Increment(ref _discontinuities); }
        _inputGeneration = tag.InputGeneration;
        _sourceHeader = tag.SourceHeader ?? new(true, true);
        _avc?.Dispose(); _aac?.Dispose(); _avc = _aac = null;
        _nalLengthSize = 0;
        _lastVideoDts = _lastAudioDts = _segmentStart = null;
        _timeline.ResetSourceReference();
        Volatile.Write(ref _waiting, 1);
    }
}
