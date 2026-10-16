using System.Diagnostics;
using System.Threading.Channels;
using Temhalo.Core.Abstractions;
using Temhalo.Core.Flv;
using Temhalo.Core.Timing;

namespace Temhalo.Core.Outputs;

public sealed record FanoutTarget(string Id, IFlvOutputConnection Connection, Func<bool>? IsReady = null);
public sealed record FanoutSnapshot(string Id, string State, long BytesWritten, long QueuedBytes,
    long DroppedTags, long Reconnections, long ExtraDelayMilliseconds);

/// <summary>
/// One delayed producer, independently paced outputs. Payloads retain the original budgeted tags;
/// there is no FLV serialization/parsing or payload copy between the common buffer and the workers.
/// </summary>
public sealed class FanoutFlvOutput : Stream, IFlvOutputConnection, IFlvPacketSink, IAsyncDisposable
{
    private readonly Worker[] _workers;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private FlvTag? _avc, _aac;
    private int _nalLength, _closed;
    private long _generation = -1;
    private FlvHeader _header = new(true, true);
    public const int DefaultCapacity = 4096;
    // Conservative allowance for channel slots, packet envelopes and references, reserved by the host.
    public static long ReservedBytes(int destinations, int capacity = DefaultCapacity) => destinations * capacity * 128L;
    public IReadOnlyList<FanoutSnapshot> Snapshots => _workers.Select(worker => worker.Snapshot).ToArray();

    public FanoutFlvOutput(IReadOnlyList<FanoutTarget> targets, int capacity = DefaultCapacity,
        long maximumQueueBytes = 16L * 1024 * 1024, Action<string>? diagnostic = null)
    {
        if (targets.Count is < 2 or > 4 || capacity is < 2 or > DefaultCapacity || maximumQueueBytes < 1024 ||
            targets.Select(target => target.Id).Distinct().Count() != targets.Count)
            throw new ArgumentException("Le multistream attend deux à quatre sorties distinctes et des files bornées.");
        _workers = targets.Select(target => new Worker(this, target, capacity, maximumQueueBytes, diagnostic)).ToArray();
        foreach (var worker in _workers) worker.Start();
    }

    public ValueTask<Stream> OpenAsync(CancellationToken token) { token.ThrowIfCancellationRequested(); return ValueTask.FromResult<Stream>(this); }
    public ValueTask WriteHeaderAsync(FlvHeader header, CancellationToken token)
    { token.ThrowIfCancellationRequested(); _header = header; return ValueTask.CompletedTask; }

    public async ValueTask WriteTagAsync(FlvTag tag, uint timestamp, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (Volatile.Read(ref _closed) != 0) throw new IOException("La distribution des sorties est fermée.");
        bool video = tag.Type == FlvTagType.Video;
        if (tag.Payload.Span[1] == 0)
        {
            if (video)
            {
                if (_generation != tag.InputGeneration) { _aac?.Dispose(); _aac = null; _generation = tag.InputGeneration; }
                _nalLength = AvcAacInspector.ValidateAvcConfiguration(tag.Payload.Span);
                _avc?.Dispose(); _avc = tag.Retain();
            }
            else { _aac?.Dispose(); _aac = tag.Retain(); }
            return;
        }
        bool keyframe = video && _avc is not null && AvcAacInspector.IsIdr(tag.Payload.Span, _nalLength);
        var packet = new Packet(tag, timestamp, keyframe ? _avc : null, keyframe ? _aac : null,
            tag.SourceHeader ?? _header, _clock.ElapsedMilliseconds);
        foreach (var worker in _workers) await worker.EnqueueAsync(packet, token).ConfigureAwait(false);
    }

    /// <summary>An explicit reduction/zero also abandons branch-specific recovery backlog.</summary>
    public void CatchUp(bool onlyBehind = false)
    { foreach (var worker in _workers) if (!onlyBehind || worker.Snapshot.ExtraDelayMilliseconds >= 500) worker.RequestTrim(); }
    private bool OtherReady(Worker worker) => _workers.Any(other => other != worker && other.Ready);

    public async ValueTask CloseAsync(bool failed, CancellationToken token)
    {
        if (Interlocked.Exchange(ref _closed, 1) != 0) return;
        foreach (var worker in _workers) worker.Complete();
        if (failed) await _lifetime.CancelAsync().ConfigureAwait(false);
        try { await Task.WhenAll(_workers.Select(worker => worker.Completion)).WaitAsync(token).ConfigureAwait(false); }
        catch (OperationCanceledException)
        {
            await _lifetime.CancelAsync().ConfigureAwait(false);
            await Task.WhenAll(_workers.Select(worker => worker.Completion)).ConfigureAwait(false);
            throw;
        }
        finally { _avc?.Dispose(); _aac?.Dispose(); _avc = _aac = null; }
    }

    public override async ValueTask DisposeAsync()
    {
        try { await CloseAsync(true, CancellationToken.None).ConfigureAwait(false); }
        finally { _lifetime.Dispose(); GC.SuppressFinalize(this); }
    }

    private readonly record struct Packet(FlvTag Tag, uint Timestamp, FlvTag? Avc, FlvTag? Aac, FlvHeader Header, long EnqueuedAt)
    {
        public bool Keyframe => Avc is not null;
        public Packet Retain() { Tag.Retain(); Avc?.Retain(); Aac?.Retain(); return this; }
        public void Release() { Tag.Dispose(); Avc?.Dispose(); Aac?.Dispose(); }
    }

    private sealed class Worker
    {
        private readonly FanoutFlvOutput _owner;
        private readonly FanoutTarget _target;
        private readonly Channel<Packet> _queue;
        private readonly Channel<bool> _wake = Channel.CreateBounded<bool>(new BoundedChannelOptions(1)
        { FullMode = BoundedChannelFullMode.DropWrite, SingleReader = true, AllowSynchronousContinuations = false });
        private readonly long _maximumBytes;
        private readonly Action<string>? _diagnostic;
        private CancellationTokenSource? _connectionCancellation;
        private long _queuedBytes, _dropped, _bytes, _reconnections, _extraDelay, _reset, _acknowledged, _trim, _trimAcknowledged;
        private string _state = "WaitingForMedia";
        public Task Completion { get; private set; } = Task.CompletedTask;
        public bool Ready => Volatile.Read(ref _state) == "Relaying" && (_target.IsReady?.Invoke() ?? true);
        public FanoutSnapshot Snapshot => new(_target.Id, Volatile.Read(ref _state), Interlocked.Read(ref _bytes),
            Interlocked.Read(ref _queuedBytes), Interlocked.Read(ref _dropped), Interlocked.Read(ref _reconnections), Interlocked.Read(ref _extraDelay));

        public Worker(FanoutFlvOutput owner, FanoutTarget target, int capacity, long maximumBytes, Action<string>? diagnostic)
        {
            _owner = owner; _target = target; _maximumBytes = maximumBytes; _diagnostic = diagnostic;
            _queue = Channel.CreateBounded<Packet>(new BoundedChannelOptions(capacity)
            { SingleReader = true, SingleWriter = true, FullMode = BoundedChannelFullMode.Wait, AllowSynchronousContinuations = false });
        }
        public void Start() => Completion = Task.Run(RunAsync);
        public void Complete() { _queue.Writer.TryComplete(); _wake.Writer.TryWrite(true); }
        public void RequestReset()
        {
            Interlocked.Increment(ref _reset);
            _wake.Writer.TryWrite(true);
            try { if (Volatile.Read(ref _connectionCancellation) is { } cancellation) _ = cancellation.CancelAsync(); }
            catch (ObjectDisposedException) { }
        }
        public void RequestTrim() { Interlocked.Increment(ref _trim); _wake.Writer.TryWrite(true); }

        public async ValueTask EnqueueAsync(Packet packet, CancellationToken token)
        {
            while (true)
            {
                token.ThrowIfCancellationRequested();
                if (Completion.IsCompleted) throw new IOException("Une tâche de distribution s'est arrêtée.");
                if (Interlocked.Read(ref _reset) != Interlocked.Read(ref _acknowledged))
                { Interlocked.Increment(ref _dropped); return; }
                if (Interlocked.Read(ref _queuedBytes) + packet.Tag.Payload.Length <= _maximumBytes)
                {
                    var retained = packet.Retain();
                    Interlocked.Add(ref _queuedBytes, packet.Tag.Payload.Length);
                    if (_queue.Writer.TryWrite(retained)) { _wake.Writer.TryWrite(true); return; }
                    Interlocked.Add(ref _queuedBytes, -packet.Tag.Payload.Length); retained.Release();
                }
                if (_owner.OtherReady(this))
                {
                    _diagnostic?.Invoke("Une sortie lente dépasse sa file ; reprise de cette seule sortie sur image clé.");
                    RequestReset(); Interlocked.Increment(ref _dropped); return;
                }
                // All outputs unavailable: retain the common delay buffer rather than silently discarding it.
                await Task.Delay(25, token).ConfigureAwait(false);
            }
        }

        private void Drain()
        {
            while (_queue.Reader.TryRead(out var packet))
            { Interlocked.Add(ref _queuedBytes, -packet.Tag.Payload.Length); Interlocked.Increment(ref _dropped); packet.Release(); }
        }

        private async Task RunAsync()
        {
            var timeline = new FlvTimestampRewriter();
            FlvTagWriter? writer = null;
            long sourceBase = 0, wallBase = 0, generation = -1;
            bool connected = false, seekAfterTrim = false;
            var stop = _owner._lifetime.Token;
            try
            {
                while (true)
                {
                    stop.ThrowIfCancellationRequested();
                    if (Interlocked.Read(ref _trim) != Interlocked.Read(ref _trimAcknowledged))
                    {
                        Drain(); seekAfterTrim = true;
                        Interlocked.Exchange(ref _trimAcknowledged, Interlocked.Read(ref _trim));
                        Volatile.Write(ref _state, "SeekingKeyframe");
                    }
                    if (Interlocked.Read(ref _reset) != Interlocked.Read(ref _acknowledged))
                    {
                        await _target.Connection.CloseAsync(true, CancellationToken.None).ConfigureAwait(false);
                        connected = false; writer = null; Drain();
                        // Reset can arrive between packets as well as during an awaited write.
                        // Never reuse the canceled connection token for the next IDR/open attempt.
                        _connectionCancellation?.Dispose(); _connectionCancellation = null;
                        Interlocked.Exchange(ref _acknowledged, Interlocked.Read(ref _reset));
                        Volatile.Write(ref _state, "SeekingKeyframe");
                        continue;
                    }
                    if (!_queue.Reader.TryRead(out var packet))
                    {
                        if (_queue.Reader.Completion.IsCompleted) break;
                        await _wake.Reader.ReadAsync(stop).ConfigureAwait(false); continue;
                    }
                    Interlocked.Add(ref _queuedBytes, -packet.Tag.Payload.Length);
                    try
                    {
                        bool finished = false;
                        while (!finished && !stop.IsCancellationRequested)
                        {
                            if (Interlocked.Read(ref _reset) != Interlocked.Read(ref _acknowledged)) break;
                            if (seekAfterTrim && !packet.Keyframe) { Interlocked.Increment(ref _dropped); break; }
                            if (!connected && !packet.Keyframe) { Interlocked.Increment(ref _dropped); break; }
                            _connectionCancellation ??= CancellationTokenSource.CreateLinkedTokenSource(stop);
                            var token = _connectionCancellation.Token;
                            try
                            {
                                long source = timeline.Unwrap(packet.Timestamp);
                                int composition = packet.Tag.Type == FlvTagType.Video ? AvcAacInspector.CompositionTime(packet.Tag.Payload.Span) : 0;
                                if (!connected)
                                {
                                    Volatile.Write(ref _state, "Opening");
                                    writer = new FlvTagWriter(await _target.Connection.OpenAsync(token).ConfigureAwait(false));
                                    await writer.WriteHeaderAsync(packet.Header, token).ConfigureAwait(false);
                                    connected = true; sourceBase = source; wallBase = _owner._clock.ElapsedMilliseconds;
                                    timeline.BeginSegment(source, composition, collapseGap: true);
                                    generation = packet.Tag.InputGeneration;
                                }
                                if (seekAfterTrim)
                                {
                                    sourceBase = source; wallBase = _owner._clock.ElapsedMilliseconds;
                                    timeline.BeginSegment(source, composition, collapseGap: true); seekAfterTrim = false;
                                }
                                if (generation != packet.Tag.InputGeneration)
                                { timeline.BeginSegment(source, composition, collapseGap: false); generation = packet.Tag.InputGeneration; }
                                long deadline = wallBase + Math.Max(0, source - sourceBase);
                                while (Interlocked.Read(ref _trim) == Interlocked.Read(ref _trimAcknowledged) &&
                                    deadline - _owner._clock.ElapsedMilliseconds is > 0 and var remaining)
                                    await Task.Delay((int)Math.Min(50, remaining), token).ConfigureAwait(false);
                                if (Interlocked.Read(ref _trim) != Interlocked.Read(ref _trimAcknowledged))
                                { Interlocked.Increment(ref _dropped); break; }
                                uint timestamp = timeline.Map(source, composition);
                                long before = writer!.BytesWritten;
                                if (packet.Keyframe)
                                {
                                    await writer.WriteTagAsync(packet.Avc!, timestamp, token).ConfigureAwait(false);
                                    if (packet.Header.HasAudio && packet.Aac is not null)
                                        await writer.WriteTagAsync(packet.Aac, timestamp, token).ConfigureAwait(false);
                                }
                                await writer.WriteTagAsync(packet.Tag, timestamp, token).ConfigureAwait(false);
                                Interlocked.Add(ref _bytes, writer.BytesWritten - before);
                                Interlocked.Exchange(ref _extraDelay, Math.Max(0, _owner._clock.ElapsedMilliseconds - packet.EnqueuedAt));
                                Volatile.Write(ref _state, "Relaying"); finished = true;
                            }
                            catch (OperationCanceledException) when (!stop.IsCancellationRequested && Interlocked.Read(ref _reset) != Interlocked.Read(ref _acknowledged))
                            { break; }
                            catch (Exception ex) when (ex is IOException or ObjectDisposedException)
                            {
                                Volatile.Write(ref _state, "RetryingOutput"); Interlocked.Increment(ref _reconnections);
                                await _target.Connection.CloseAsync(true, CancellationToken.None).ConfigureAwait(false);
                                connected = false; writer = null;
                                _connectionCancellation.Dispose(); _connectionCancellation = null;
                                if (!packet.Keyframe) { Interlocked.Increment(ref _dropped); break; }
                            }
                        }
                    }
                    finally { packet.Release(); }
                    if (_connectionCancellation?.IsCancellationRequested == true && !stop.IsCancellationRequested)
                    { _connectionCancellation.Dispose(); _connectionCancellation = null; }
                }
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
            finally
            {
                Volatile.Write(ref _state, "Stopped");
                _connectionCancellation?.Dispose(); _connectionCancellation = null;
                Drain(); await _target.Connection.CloseAsync(false, CancellationToken.None).ConfigureAwait(false);
            }
        }
    }

    public override bool CanRead => false;
    public override bool CanSeek => false;
    public override bool CanWrite => true;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override void Flush() { }
    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
