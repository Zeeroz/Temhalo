using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using Temhalo.Core.Buffering;
using Temhalo.Core.Timing;
using Temhalo.Infrastructure.Windows.Logging;

namespace Temhalo.Infrastructure.Windows.Processes;

/// <summary>A session owns the RAM buffer; readers, server and emitters have independent lifetimes.</summary>
public sealed class LocalRelaySupervisor(LocalRelayOptions options, ILogger<LocalRelaySupervisor> logger)
{
    private LocalRelaySnapshot _snapshot = new("Starting", null, null, null, 0, 0, null, null);
    private readonly PublisherTracker _publisher = new();
    private FlvDelayMediaTransfer? _transfer;
    private RelayOutputGroup? _outputs;
    private NativeProcess? _server;
    private int _serverPid, _readerPid, _stopping;
    public DelayController DelayControl { get; } = new(options.FixedDelay.DelaySeconds);
    public void CatchUpOutputs(bool onlyBehind = false) => Volatile.Read(ref _outputs)?.CatchUp(onlyBehind);

    public LocalRelaySnapshot Snapshot
    {
        get
        {
            var stored = Volatile.Read(ref _snapshot);
            var delay = Volatile.Read(ref _transfer)?.Snapshot;
            var outputs = Volatile.Read(ref _outputs)?.Snapshots(_transfer?.BytesTransferred ?? 0) ?? [];
            var output = outputs.FirstOrDefault();
            bool running = stored.State is not ("Stopped" or "Faulted");
            long reservation = running ? _outputs?.ReservedBytes ?? 0 : 0;
            if (delay is not null && _outputs?.ReservedBytes > 0)
                delay = delay with { BufferBytes = delay.BufferBytes + reservation, PeakBufferBytes = delay.PeakBufferBytes + _outputs.ReservedBytes,
                    MaximumBufferBytes = options.FixedDelay.MaximumBufferBytes,
                    BufferWarning = delay.BufferBytes + reservation >= options.FixedDelay.MaximumBufferBytes * options.FixedDelay.WarningUsageRatio };
            string state = !running ? stored.State : Volatile.Read(ref _stopping) != 0 ? "Draining" :
                outputs.Count > 0 && outputs.All(o => !o.Ready) && outputs.Any(o => o.Reconnecting) ? "RetryingOutput" : delay is not { ReadTags: > 0 } ? "WaitingForMedia" :
                delay.Control.IsPaused ? "IncreasingDelay" : delay.Control.Phase == "SeekingKeyframe" ? "SeekingKeyframe" :
                !_publisher.Connected && delay.BufferBytes > 0 ? "DrainingInput" :
                delay.EmittedMediaTags > 0 ? "Relaying" : "Buffering";
            return stored with
            {
                State = state,
                MediaMtxPid = running && Volatile.Read(ref _serverPid) is > 0 and var server ? server : null,
                ReaderPid = running && Volatile.Read(ref _readerPid) is > 0 and var reader ? reader : null,
                EmitterPid = running ? output?.Pid : null,
                Attempt = outputs.Count == 0 ? 0 : outputs.Max(o => o.Attempt), OutputFile = _outputs?.FirstFile,
                BytesTransferred = outputs.Sum(o => o.BytesWritten), Delay = delay,
                IngestConnected = running && _publisher.Connected,
                OutputReady = running && outputs.Any(o => o.Ready && o.Pid is not null), Outputs = outputs
            };
        }
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        // Fail explicitly on a competing service; never stop or attach to its process.
        EnsurePortAvailable();
        Directory.CreateDirectory(options.OutputDirectory);
        using var job = new WindowsProcessJob();
        using var stopInput = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        using var abort = new CancellationTokenSource();
        using var registration = cancellationToken.Register(() =>
        {
            Volatile.Write(ref _stopping, 1);
            abort.CancelAfter(options.ShutdownTimeout);
        });
        await using var group = new RelayOutputGroup(options, job, logger);
        var output = group.Connection;
        if (group.ReservedBytes > 0 && options.FixedDelay.MaximumBufferBytes - group.ReservedBytes < 1024 * 1024)
            throw new ArgumentException("Prévoyez au moins 4 MiB pour le buffer multistream et ses files de sortie.");
        var transfer = new FlvDelayMediaTransfer(options.FixedDelay with { MaximumBufferBytes = options.FixedDelay.MaximumBufferBytes - group.ReservedBytes }, control: DelayControl);
        Volatile.Write(ref _outputs, group);
        Volatile.Write(ref _transfer, transfer);
        string? fault = null;
        try
        {
            await transfer.TransferSessionsAsync(ReadInputsAsync(job, stopInput.Token), output, abort.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning("Délai maximal de vidange dépassé ({Seconds} s). Le contenu restant ne peut pas être transmis.", options.ShutdownTimeout.TotalSeconds);
        }
        catch (Exception ex)
        {
            fault = options.Destination?.Redact(ex.Message) ?? NativeLogSanitizer.Sanitize(ex.Message);
            logger.LogError("Session interrompue : {Reason}", fault);
            throw;
        }
        finally
        {
            stopInput.Cancel();
            if (transfer.Snapshot.TruncatedInput)
                logger.LogWarning("Une entrée FLV a été interrompue ou tronquée ; seuls les tags complets ont été conservés.");
            _publisher.Reset();
            Volatile.Write(ref _stopping, 1);
            try { await output.CloseAsync(fault is not null, abort.Token).ConfigureAwait(false); }
            catch (OperationCanceledException) when (abort.IsCancellationRequested)
            { logger.LogWarning("Fin de vidange des sorties bornée ; contenu restant libéré."); }
            if (_server is not null)
            {
                await _server.StopAsync(false, TimeSpan.FromMilliseconds(250)).ConfigureAwait(false);
                await _server.DisposeAsync().ConfigureAwait(false);
                _server = null;
            }
            Volatile.Write(ref _serverPid, 0);
            Volatile.Write(ref _readerPid, 0);
            Publish(fault is null ? "Stopped" : "Faulted", fault ?? "Relais arrêté");
        }
    }

    private async IAsyncEnumerable<Stream> ReadInputsAsync(WindowsProcessJob job, CancellationToken stopInput,
        [EnumeratorCancellation] CancellationToken abort = default)
    {
        using var stopping = CancellationTokenSource.CreateLinkedTokenSource(stopInput, abort);
        while (!stopping.IsCancellationRequested)
        {
            NativeProcess server;
            try { server = await EnsureServerAsync(job, stopping.Token).ConfigureAwait(false); }
            catch (OperationCanceledException) when (stopping.IsCancellationRequested) { yield break; }
            if (!_publisher.Connected)
            {
                // Keep one ingest server idle instead of spawning readers while OBS is absent.
                try { await Task.Delay(100, stopping.Token).ConfigureAwait(false); }
                catch (OperationCanceledException) when (stopping.IsCancellationRequested) { yield break; }
                continue;
            }
            NativeProcess reader = NativeProcess.Start(options.FfmpegExecutable, ReaderArguments(),
                "FFmpeg lecteur", true, true, job, logger);
            Volatile.Write(ref _readerPid, reader.Id);
            Publish("WaitingForMedia", "Lecture OBS démarrée ; buffer de session conservé");
            using var monitorCancellation = CancellationTokenSource.CreateLinkedTokenSource(stopping.Token);
            Task monitor = MonitorReaderAsync(reader, server, monitorCancellation.Token);
            try { yield return reader.Output; }
            finally
            {
                monitorCancellation.Cancel();
                await monitor.ConfigureAwait(false);
                Volatile.Write(ref _readerPid, 0);
                await reader.StopAsync(true, TimeSpan.FromSeconds(2)).ConfigureAwait(false);
                await reader.DisposeAsync().ConfigureAwait(false);
                logger.LogInformation("Lecture OBS terminée. Les tags complets continuent de se vider pendant l'attente de reconnexion.");
            }
            if (!stopping.IsCancellationRequested)
            {
                try { await Task.Delay(options.RetryMinimum, stopping.Token).ConfigureAwait(false); }
                catch (OperationCanceledException) when (stopping.IsCancellationRequested) { yield break; }
            }
        }
    }

    private static async Task MonitorReaderAsync(NativeProcess reader, NativeProcess server, CancellationToken stopping)
    {
        Task stop = Task.Delay(Timeout.InfiniteTimeSpan, stopping);
        await Task.WhenAny(reader.Exited, server.Exited, stop).ConfigureAwait(false);
        if (!reader.HasExited) await reader.StopAsync(true, TimeSpan.FromSeconds(2)).ConfigureAwait(false);
    }

    private async Task<NativeProcess> EnsureServerAsync(WindowsProcessJob job, CancellationToken token)
    {
        while (true)
        {
            token.ThrowIfCancellationRequested();
            if (_server is { HasExited: false }) return _server;
            if (_server is not null) { await _server.DisposeAsync().ConfigureAwait(false); _server = null; }
            _publisher.Reset();
            Volatile.Write(ref _serverPid, 0);
            try
            {
                EnsurePortAvailable();
                _server = NativeProcess.Start(options.MediaMtxExecutable, [options.MediaMtxConfiguration],
                    "MediaMTX", false, false, job, logger, clearMediaMtxEnvironment: true, diagnostic: _publisher.Observe);
                Volatile.Write(ref _serverPid, _server.Id);
                Publish("StartingIngest", "Démarrage de MediaMTX");
                await WaitForIngestAsync(_server, token).ConfigureAwait(false);
                return _server;
            }
            catch (Exception ex) when (ex is IOException or TimeoutException or System.ComponentModel.Win32Exception)
            {
                if (_server is not null)
                { await _server.DisposeAsync().ConfigureAwait(false); _server = null; }
                Volatile.Write(ref _serverPid, 0);
                Publish("RetryingIngest", "Ingest indisponible ; nouvelle tentative automatique");
                logger.LogWarning("Ingest : {Reason}", NativeLogSanitizer.Sanitize(ex.Message));
                await Task.Delay(options.RetryMaximum, token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!token.IsCancellationRequested)
            {
                if (_server is not null) { await _server.DisposeAsync().ConfigureAwait(false); _server = null; }
                Volatile.Write(ref _serverPid, 0);
                logger.LogWarning("Démarrage de l'ingest trop long ; nouvelle tentative automatique.");
                await Task.Delay(options.RetryMaximum, token).ConfigureAwait(false);
            }
        }
    }

    private void Publish(string state, string message)
    {
        Volatile.Write(ref _snapshot, new(state, null, null, null, 0, 0, null, message));
        logger.LogInformation("{State}: {Event}", state, message);
    }

    private static void EnsurePortAvailable()
    {
        var probe = new TcpListener(IPAddress.Loopback, 1935);
        probe.Server.ExclusiveAddressUse = true;
        try { probe.Start(); }
        catch (SocketException ex) { throw new IOException("Le port local 1935 est déjà occupé.", ex); }
        finally { probe.Stop(); }
    }

    private static async Task WaitForIngestAsync(NativeProcess server, CancellationToken token)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        while (!timeout.IsCancellationRequested)
        {
            if (server.HasExited) throw new IOException("MediaMTX s'est arrêté avant de devenir disponible.");
            using var client = new TcpClient();
            try
            {
                await client.ConnectAsync(IPAddress.Loopback, 1935, timeout.Token).ConfigureAwait(false);
                if (server.HasExited) throw new IOException("MediaMTX s'est arrêté au démarrage.");
                return;
            }
            catch (SocketException) { await Task.Delay(100, timeout.Token).ConfigureAwait(false); }
        }
        token.ThrowIfCancellationRequested();
        throw new TimeoutException("L'ingest local n'est pas devenu disponible.");
    }

    internal static string[] ReaderArguments() =>
    [
        "-hide_banner", "-loglevel", "warning", "-nostats", "-rw_timeout", "5000000",
        "-analyzeduration", "1000000", "-probesize", "1048576", "-rtmp_live", "live",
        "-i", LocalRelayOptions.IngestUrl, "-map", "0:v:0", "-map", "0:a:0?", "-c", "copy",
        "-map_metadata", "-1", "-f", "flv", "-flvflags", "no_duration_filesize", "-flush_packets", "1", "pipe:1"
    ];

    internal static string[] EmitterArguments(string file, bool network = false) =>
    [
        "-hide_banner", "-loglevel", "warning", "-nostats", "-nostdin", "-n", "-f", "flv",
        "-analyzeduration", "1000000", "-probesize", "1048576", "-i", "pipe:0",
        "-map", "0:v:0", "-map", "0:a:0?", "-c", "copy", "-map_metadata", "-1",
        "-progress", "pipe:2", "-stats_period", "1", "-flush_packets", "1", "-f", "flv",
        // Live FLV is flushed per packet. Avoid TCP coalescing/delayed-ACK waits on small RTMP writes.
        .. (network ? new[] { "-flvflags", "no_duration_filesize", "-rw_timeout", "10000000", "-tcp_nodelay", "1" } : Array.Empty<string>()), file
    ];
}
