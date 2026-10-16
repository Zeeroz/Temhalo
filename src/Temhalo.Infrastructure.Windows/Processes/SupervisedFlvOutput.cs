using Microsoft.Extensions.Logging;
using Temhalo.Core.Abstractions;

namespace Temhalo.Infrastructure.Windows.Processes;

internal sealed record OutputConnectionState(int? Pid = null, int Attempt = 0, string? File = null,
    bool Ready = false, bool Reconnecting = false);

/// <summary>Only the consumer accesses lifecycle methods. Diagnostics publish immutable telemetry.</summary>
internal sealed class SupervisedFlvOutput(LocalRelayOptions options, WindowsProcessJob job, ILogger logger)
    : IFlvOutputConnection, IAsyncDisposable
{
    private NativeProcess? _process;
    private int _failures;
    private OutputConnectionState _state = new();
    public OutputConnectionState State => Volatile.Read(ref _state);

    public async ValueTask<Stream> OpenAsync(CancellationToken cancellationToken)
    {
        if (_failures > 0)
        {
            double milliseconds = Math.Min(options.RetryMaximum.TotalMilliseconds,
                options.RetryMinimum.TotalMilliseconds * Math.Pow(2, Math.Min(_failures - 1, 10)));
            logger.LogInformation("Reconnexion de la sortie dans {DelayMs} ms ; buffer RAM conservé", milliseconds);
            await Task.Delay(TimeSpan.FromMilliseconds(milliseconds), cancellationToken).ConfigureAwait(false);
        }
        int attempt = State.Attempt + 1;
        string file = Path.Combine(options.OutputDirectory, $"relay-{attempt:D4}.flv");
        Volatile.Write(ref _state, new(null, attempt, options.Destination is null ? file : null,
            Reconnecting: attempt > 1));
        try
        {
            _process = NativeProcess.Start(options.FfmpegExecutable,
                LocalRelaySupervisor.EmitterArguments(options.Destination?.PublishUrl ?? file, options.Destination is not null),
                "FFmpeg émetteur", true, false, job, logger, diagnostic: line =>
                {
                    if (line.StartsWith("out_time_us=", StringComparison.Ordinal) &&
                        long.TryParse(line.AsSpan(12), out long time) && time > 0)
                    { Update(s => s with { Ready = true, Reconnecting = false }); Interlocked.Exchange(ref _failures, 0); }
                }, sensitiveValue: options.Destination?.Key);
            NativeProcess current = _process;
            int processId = current.Id;
            Update(s => s with { Pid = processId });
            _ = current.Exited.ContinueWith(_ => Update(s => s.Pid == processId
                ? s with { Pid = null, Ready = false, Reconnecting = true } : s),
                CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            return new BoundedPipeWriter(current.Input, options.OutputWriteTimeout,
                () => current.StopAsync(false, TimeSpan.Zero));
        }
        catch (System.ComponentModel.Win32Exception)
        { throw new IOException("Le processus émetteur n'a pas pu démarrer."); }
    }

    public async ValueTask CloseAsync(bool failed, CancellationToken cancellationToken)
    {
        Update(s => s with { Pid = null, Ready = false, Reconnecting = failed });
        if (failed)
        {
            Interlocked.Increment(ref _failures);
            logger.LogWarning("Sortie interrompue. Reprise sur image clé avec le buffer conservé ; les données déjà confiées au processus ne sont pas acquittées par la plateforme.");
        }
        var process = _process;
        _process = null;
        if (process is not null)
        {
            process.CloseInput();
            await process.StopAsync(false, failed ? TimeSpan.FromMilliseconds(200) : TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            await process.DisposeAsync().ConfigureAwait(false);
        }
    }

    private void Update(Func<OutputConnectionState, OutputConnectionState> update)
    {
        OutputConnectionState observed;
        do { observed = State; }
        while (!ReferenceEquals(Interlocked.CompareExchange(ref _state, update(observed), observed), observed));
    }

    public async ValueTask DisposeAsync() => await CloseAsync(false, CancellationToken.None).ConfigureAwait(false);

    private sealed class BoundedPipeWriter(Stream inner, TimeSpan timeout, Func<Task> terminate) : Stream
    {
        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            ValueTask write = inner.WriteAsync(buffer, cancellationToken);
            if (write.IsCompletedSuccessfully) { await write.ConfigureAwait(false); return; }
            Task pending = write.AsTask();
            try { await pending.WaitAsync(timeout, cancellationToken).ConfigureAwait(false); }
            catch (TimeoutException)
            {
                await terminate().ConfigureAwait(false);
                try { await pending.ConfigureAwait(false); } catch (Exception ex) when (ex is IOException or OperationCanceledException or ObjectDisposedException) { }
                throw new IOException("L'émetteur ne consomme plus les données FLV ; reconnexion demandée.");
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
}
