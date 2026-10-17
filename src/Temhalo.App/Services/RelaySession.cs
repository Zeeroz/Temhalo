using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using Temhalo.Core.Buffering;
using Temhalo.Infrastructure.Windows.Logging;
using Temhalo.Infrastructure.Windows.Processes;
using Temhalo.Infrastructure.Windows.Deployment;
using Temhalo.Infrastructure.Windows.Preview;

namespace Temhalo.App.Services;

/// <summary>Composes the existing supervisor; no media work runs on the dispatcher thread.</summary>
internal sealed class RelaySession : IAsyncDisposable
{
    private readonly ConcurrentQueue<string> _events = new();
    private LocalRelaySupervisor? _supervisor;
    private Task? _run;
    private CancellationTokenSource? _cancellation;
    private Logger? _log;
    private ILoggerFactory? _factory;
    private string? _error;
    private bool _wasConnected;
    private LocalPreviewReceiver? _previewReceiver;
    private string? _mediaMtxExecutable;
    private bool _bufferWarning;
    private long _observedReplays;
    private string? _phase;
    private readonly Stopwatch _elapsed = new(), _live = new();
    public bool Running => _run is { IsCompleted: false };
    public bool Stopping => Running && _cancellation is { IsCancellationRequested: true };
    public bool Network { get; private set; }
    public string? DirectoryPath { get; private set; }
    public string LogsDirectoryPath { get; } = Path.Combine(AppStorage.DataDirectory, "logs");
    public string? Error => Volatile.Read(ref _error);
    public string? LastAction { get; private set; }
    public double ElapsedSeconds => _elapsed.Elapsed.TotalSeconds;
    public TimeSpan LiveDuration => _live.Elapsed;
    public LocalRelaySnapshot? Snapshot => _supervisor?.Snapshot;
    public bool TryReadEvent(out string? value) => _events.TryDequeue(out value);
    public RelaySession()
    {
        _log = SafeFileLogger.Create(LogsDirectoryPath, new RecentEvents(_events));
        _factory = LoggerFactory.Create(builder => builder.ClearProviders().AddSerilog(_log, dispose: false));
        RecordControlEvent("Temhalo démarré. Les journaux masquent les clés et les URL de publication.");
    }
    public void RecordControlEvent(string message) => _log?.Information("{Event}", SecretRedactor.Shared.Redact(message));
    public void RequestDelay(int seconds)
    {
        if (!Running) return;
        int previous = _supervisor!.DelayControl.Request.TargetSeconds;
        if (seconds <= previous) _supervisor.CatchUpOutputs(onlyBehind: seconds == previous);
        _supervisor!.DelayControl.SetTarget(seconds);
        LastAction = $"{DateTime.Now:HH:mm:ss} · Cible demandée : {seconds} s.";
        _log?.Information("Cible demandée : {Seconds} s", seconds);
    }

    public async Task StartAsync(int initialSeconds, long bufferBytes, RelayDestination? destination, IReadOnlyList<RelayOutput>? outputs = null,
        LocalPreviewTarget? previewTarget = null)
    {
        if (Running) throw new InvalidOperationException("Le relais est déjà actif.");
        _cancellation?.Dispose(); _cancellation = new CancellationTokenSource();
        var preparationToken = _cancellation.Token;
        Volatile.Write(ref _error, null);
        var layout = ApplicationLayout.Resolve(AppContext.BaseDirectory, Environment.CurrentDirectory, AppStorage.DataDirectory);
        string mediaMtx, ffmpeg;
        if (layout.IsPortable)
        {
            var progress = new Progress<string>(message => { LastAction = message; RecordControlEvent(message); });
            var native = await new NativeToolProvisioner(Path.Combine(layout.ConfigurationDirectory, "native-tools.lock.json"),
                layout.NativeCacheDirectory).EnsureAsync(progress, preparationToken);
            mediaMtx = native.MediaMtx; ffmpeg = native.Ffmpeg;
        }
        else
        {
            var tools = JsonSerializer.Deserialize<ResolvedTools>(await File.ReadAllTextAsync(Path.Combine(layout.Root, "artifacts", "native-tools.resolved.json")))
                ?? throw new IOException("Préparez les binaires avec tools/native/Get-NativeTools.ps1.");
            string Tool(string name)
            {
                string path = tools.Tools.Single(t => t.Name == name).Executable;
                if (!File.Exists(path)) throw new FileNotFoundException($"Binaire {name} absent. Exécutez Get-NativeTools.ps1.");
                return path;
            }
            mediaMtx = Tool("MediaMTX"); ffmpeg = Tool("FFmpeg");
        }
        preparationToken.ThrowIfCancellationRequested();
        if (_previewReceiver is not null) { await _previewReceiver.DisposeAsync(); _previewReceiver = null; }
        _mediaMtxExecutable = mediaMtx;
        DirectoryPath = Path.Combine(layout.SessionDirectory, $"run-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(DirectoryPath);
        // Prepare before OBS arrives, so this optional local output cannot build a recovery backlog.
        if (previewTarget is not null)
        {
            var receiver = new LocalPreviewReceiver(mediaMtx, DirectoryPath, _factory!.CreateLogger<LocalPreviewReceiver>());
            try { await receiver.EnsureAsync(previewTarget, preparationToken); _previewReceiver = receiver; }
            catch { await receiver.DisposeAsync(); throw; }
        }
        Network = outputs is not null ? outputs.Any(output => output.Destination is not null) : destination is not null;
        var options = new LocalRelayOptions(mediaMtx, ffmpeg,
            Path.Combine(layout.ConfigurationDirectory, "mediamtx.yml"), DirectoryPath)
        {
            Destination = destination,
            Outputs = outputs,
            FixedDelay = new FixedDelayOptions { DelaySeconds = initialSeconds, MaximumBufferBytes = bufferBytes }
        };
        _supervisor = new LocalRelaySupervisor(options, _factory!.CreateLogger<LocalRelaySupervisor>());
        LastAction = "Relais démarré. En attente de la publication OBS.";
        _live.Reset(); _elapsed.Restart();
        _wasConnected = false;
        _bufferWarning = false; _phase = null; _observedReplays = 0;
        var supervisor = _supervisor;
        _run = Task.Run(() => RunAsync(supervisor, preparationToken));
    }

    private async Task RunAsync(LocalRelaySupervisor supervisor, CancellationToken token)
    {
        try { await supervisor.RunAsync(token).ConfigureAwait(false); }
        catch (Exception ex)
        {
            string error = SecretRedactor.Shared.Redact(ex.Message);
            Volatile.Write(ref _error, error);
            LastAction = "Relais arrêté : " + error;
            _log?.Error("Session interrompue : {Reason}", error);
        }
        finally { _live.Stop(); _elapsed.Stop(); }
    }

    public void ObserveLive()
    {
        var delay = Snapshot?.Delay;
        if (Running && delay is not null && delay.Control.CompletedReplays != _observedReplays)
        {
            _observedReplays = delay.Control.CompletedReplays;
            _log?.Information("Relecture de l’historique : cible {Seconds} s, reprise sur image clé, audio et vidéo conservés.", delay.TargetSeconds);
        }
        if (Running && delay?.Control.Phase != _phase)
        { _phase = delay?.Control.Phase; if (_phase is not null) _log?.Information("Transition du délai : {Phase}", _phase); }
        if (Running && delay?.BufferWarning == true && !_bufferWarning)
            _log?.Warning("Le buffer RAM a dépassé son seuil d'alerte : {Bytes} / {MaximumBytes} octets", delay.BufferBytes, delay.MaximumBufferBytes);
        _bufferWarning = delay?.BufferWarning == true;
        bool connected = Snapshot?.IngestConnected == true && Running;
        if (connected != _wasConnected)
        {
            _wasConnected = connected;
            LastAction = $"{DateTime.Now:HH:mm:ss} · " + (connected ? "Publication OBS connectée." : "Publication OBS déconnectée.");
        }
        if (Running && !_live.IsRunning && (Snapshot?.IngestConnected == true || Snapshot?.Delay is { ReadTags: > 0 })) _live.Start();
    }

    public async Task StopAsync()
    {
        if (Running) LastAction = "Arrêt demandé. Vidange des tags complets en cours.";
        _cancellation?.Cancel();
        if (_run is not null) await _run;
        if (_previewReceiver is not null) { await _previewReceiver.DisposeAsync(); _previewReceiver = null; }
        if (_run is not null) LastAction = Network ? "Relais arrêté. Émission terminée." : "Relais arrêté. Fichier de session finalisé.";
    }

    public async Task EnsurePreviewReceiverAsync(LocalPreviewTarget target, CancellationToken token)
    {
        if (!Running || Stopping || _mediaMtxExecutable is null || DirectoryPath is null)
            throw new InvalidOperationException("Démarrez le relais avant l’aperçu.");
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(token, _cancellation!.Token);
        _previewReceiver ??= new LocalPreviewReceiver(_mediaMtxExecutable, DirectoryPath, _factory!.CreateLogger<LocalPreviewReceiver>());
        await _previewReceiver.EnsureAsync(target, lifetime.Token);
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        await DisposeResourcesAsync();
    }

    private Task DisposeResourcesAsync()
    {
        _cancellation?.Dispose(); _cancellation = null;
        _factory?.Dispose(); _factory = null;
        _log?.Dispose(); _log = null;
        return Task.CompletedTask;
    }

    private sealed class RecentEvents(ConcurrentQueue<string> queue) : ILogEventSink
    {
        public void Emit(LogEvent logEvent)
        {
            queue.Enqueue($"{logEvent.Timestamp:HH:mm:ss}  {logEvent.Level}  {SecretRedactor.Shared.Redact(logEvent.RenderMessage(CultureInfo.GetCultureInfo("fr-FR")))}");
            while (queue.Count > 200) queue.TryDequeue(out _);
        }
    }
    private sealed record ResolvedTools(ResolvedTool[] Tools);
    private sealed record ResolvedTool(string Name, string Executable);
}
