using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Temhalo.Core.Buffering;
using Temhalo.Core.Flv;
using Temhalo.Core.Timing;
using Temhalo.Infrastructure.Windows.Logging;
using Temhalo.Infrastructure.Windows.Processes;
using Temhalo.Infrastructure.Windows.Settings;

if (args.Length is < 1 or > 2 || !File.Exists(Path.Combine(args[0], "Temhalo.sln")))
{ Console.Error.WriteLine("Usage: Temhalo.NativeValidation <racine Temhalo>"); return 1; }
if (args.Length == 2 && args[1] == "--ui-fixture")
{
    var store = new DpapiSettingsStore(Path.Combine(Path.GetFullPath(args[0]), "artifacts", "validation", "stage5bis-ui-settings"));
    await store.SaveAsync(new AppSettings { OutputMode = 1, Hotkeys = new() { Enabled = false },
        LocalAccess = new() { Port = 43121, OverlayToken = new string('A', 64), DockToken = new string('B', 64) },
        Destinations = [new() { Name = "Aperçu local A", ServerUrl = "rtmp://127.0.0.1:1936/live", StreamKey = "preview-a" },
            new() { Name = "Aperçu local B", ServerUrl = "rtmp://127.0.0.1:1936/live", StreamKey = "preview-b" }] });
    Console.WriteLine("Configuration fictive chiffrée créée dans " + Path.GetDirectoryName(store.FilePath)); return 0;
}
if (args.Length == 2 && args[1] == "--provision")
{
    string cache = Path.Combine(Path.GetFullPath(args[0]), "artifacts", "validation", "stage6", "portable-data", "tools");
    var provisioner = new Temhalo.Infrastructure.Windows.Deployment.NativeToolProvisioner(Path.Combine(args[0], "config", "native-tools.lock.json"), cache);
    var result = await provisioner.EnsureAsync(new Progress<string>(Console.WriteLine));
    var cached = await provisioner.EnsureAsync();
    if (result != cached) throw new InvalidDataException("Les chemins du cache ont changé.");
    Console.WriteLine("Outils téléchargés, extraits, revérifiés et réutilisés : " + cache);
    return 0;
}
if (args.Length == 2 && args[1] == "--conflict-fixtures")
{
    for (int i = 0; i < 2; i++)
    {
        string directory = Path.Combine(Path.GetFullPath(args[0]), "artifacts", "validation", "stage6", "conflict-" + i);
        await new DpapiSettingsStore(directory).SaveAsync(new AppSettings { Hotkeys = new() { Enabled = true },
            LocalAccess = new() { Port = 43122 + i, OverlayToken = new string('A', 64), DockToken = new string('B', 64) } });
        Console.WriteLine("Configuration fictive de conflit : " + directory);
    }
    return 0;
}
if (args.Length == 2 && args[1].StartsWith("--robustness=", StringComparison.Ordinal))
{
    if (!int.TryParse(args[1][13..], out int seconds) || seconds is < 600 or > 7200)
    { Console.Error.WriteLine("La durée de robustesse doit être comprise entre 600 et 7200 secondes."); return 1; }
    return await new Validation(Path.GetFullPath(args[0])).RunRobustnessAsync(seconds);
}
if (args.Length == 2 && args[1] != "--multistream")
{ Console.Error.WriteLine("Option de validation inconnue."); return 1; }
return args.Length == 2 ? await new Validation(Path.GetFullPath(args[0])).RunMultistreamAsync()
    : await new Validation(Path.GetFullPath(args[0])).RunAsync();

internal sealed partial class Validation(string root)
{
    private readonly string _directory = Path.Combine(root, "artifacts", "validation", "stage5-" + Guid.NewGuid().ToString("N"));
    private readonly List<OwnedProcess> _children = [];
    private readonly List<object> _checks = [];
    private string _ffmpeg = "", _mtx = "", _source = "";
    private TextWriter _diagnostics = TextWriter.Null;
    private CaptureLogger _logger = null!;

    public async Task<int> RunAsync()
    {
        try
        {
            CheckPort(1935); CheckPort(1936);
            Directory.CreateDirectory(_directory);
            _diagnostics = TextWriter.Synchronized(new StreamWriter(Path.Combine(_directory, "native.log")) { AutoFlush = true });
            _logger = new CaptureLogger(_diagnostics);
            using var manifest = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(root, "artifacts", "native-tools.resolved.json")));
            foreach (var tool in manifest.RootElement.GetProperty("Tools").EnumerateArray())
                if (tool.GetProperty("Name").GetString() == "FFmpeg") _ffmpeg = tool.GetProperty("Executable").GetString()!;
                else if (tool.GetProperty("Name").GetString() == "MediaMTX") _mtx = tool.GetProperty("Executable").GetString()!;
            _source = Path.Combine(_directory, "source.flv");
            Console.WriteLine("Création de la mire H.264/AAC (seule étape encodée du test).");
            var source = Start(_ffmpeg, ["-hide_banner", "-loglevel", "error", "-f", "lavfi", "-i", "testsrc2=size=640x360:rate=30", "-f", "lavfi", "-i", "sine=frequency=880:sample_rate=48000", "-t", "8", "-c:v", "h264_mf", "-hw_encoding", "0", "-b:v", "1500k", "-g", "30", "-bf", "0", "-c:a", "aac", "-b:a", "160k", "-f", "flv", "-n", _source]);
            await source.RequireSuccessAsync();
            await FileRecoveryAsync();
            await NetworkRecoveryAsync();
            foreach (var child in _children) await child.DisposeAsync();
            string report = Path.Combine(_directory, "result.json");
            await File.WriteAllTextAsync(report, JsonSerializer.Serialize(new { Stage = 5, Status = "passed", CheckedUtc = DateTime.UtcNow,
                Checks = _checks, OwnedProcessesRemaining = _children.Count(c => !c.Exited) }, new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine("Validation réussie : " + report);
            return 0;
        }
        catch (Exception ex)
        { Console.Error.WriteLine("Échec : " + SecretRedactor.Shared.Redact(ex.Message)); return 1; }
        finally { foreach (var child in _children) await child.DisposeAsync(); await _diagnostics.DisposeAsync(); }
    }

    private LocalRelaySupervisor Supervisor(string name, RelayDestination? destination = null) => new(new(
        _mtx, _ffmpeg, Path.Combine(root, "config", "mediamtx.yml"), Path.Combine(_directory, name))
    {
        Destination = destination, FixedDelay = new() { DelaySeconds = 3, MaximumBufferBytes = 32 * 1048576 },
        RetryMinimum = TimeSpan.FromMilliseconds(250), RetryMaximum = TimeSpan.FromSeconds(1),
        ShutdownTimeout = TimeSpan.FromSeconds(20), OutputWriteTimeout = TimeSpan.FromSeconds(12)
    }, _logger);

    private async Task FileRecoveryAsync()
    {
        Console.WriteLine("Test fichier : crash lecteur, coupure OBS, reprise OBS, crash MediaMTX puis émetteur.");
        var relay = Supervisor("file");
        using var stop = new CancellationTokenSource();
        Task running = relay.RunAsync(stop.Token);
        try
        {
            await Until(() => relay.Snapshot.MediaMtxPid.HasValue, "ingest initial");
            var firstSource = Publish();
            await Until(() => relay.Snapshot.OutputReady && relay.Snapshot.Delay is { EmittedMediaTags: > 100 }, "première émission");
            var initial = relay.Snapshot;
            KillOwnedRelayPid(initial.ReaderPid, "ffmpeg");
            await Until(() => relay.Snapshot.Delay!.InputSessions > initial.Delay!.InputSessions && relay.Snapshot.ReaderPid != initial.ReaderPid &&
                relay.Snapshot.Delay.EmittedMediaTags > initial.Delay.EmittedMediaTags + 80, "reconnexion lecteur");
            Require(relay.Snapshot.EmitterPid == initial.EmitterPid && relay.Snapshot.Attempt == initial.Attempt, "Un crash lecteur ne doit pas recréer la sortie.");
            await firstSource.DisposeAsync();
            await Until(() => !relay.Snapshot.IngestConnected && relay.Snapshot.Delay is { InputCompleted: true, BufferedTags: 0 }, "vidange après déconnexion OBS");
            var idle = relay.Snapshot;
            Require(!running.IsCompleted && idle.EmitterPid == initial.EmitterPid, "La session doit attendre OBS avec sa sortie conservée.");
            var secondSource = Publish();
            await Until(() => relay.Snapshot.IngestConnected && relay.Snapshot.Delay!.InputSessions > idle.Delay!.InputSessions &&
                relay.Snapshot.Delay.EmittedMediaTags > idle.Delay.EmittedMediaTags + 80, "nouvelle publication OBS");
            var beforeServerCrash = relay.Snapshot;
            KillOwnedRelayPid(beforeServerCrash.MediaMtxPid, "mediamtx");
            await Until(() => relay.Snapshot.MediaMtxPid.HasValue && relay.Snapshot.MediaMtxPid != beforeServerCrash.MediaMtxPid,
                "redémarrage MediaMTX");
            await secondSource.DisposeAsync();
            var thirdSource = Publish();
            await Until(() => relay.Snapshot.Delay!.InputSessions > beforeServerCrash.Delay!.InputSessions && relay.Snapshot.IngestConnected &&
                relay.Snapshot.Delay.EmittedMediaTags > beforeServerCrash.Delay.EmittedMediaTags + 80, "reprise après crash serveur");
            Require(relay.Snapshot.EmitterPid == initial.EmitterPid, "Le buffer et l'émetteur doivent survivre au crash de l'ingest.");
            var beforeEmitter = relay.Snapshot;
            KillOwnedRelayPid(beforeEmitter.EmitterPid, "ffmpeg");
            await Until(() => relay.Snapshot.OutputReady && relay.Snapshot.Attempt > beforeEmitter.Attempt && relay.Snapshot.Delay!.OutputReconnections > 0,
                "reconnexion émetteur");
            var recovered = relay.Snapshot;
            Require(recovered.ReaderPid == beforeEmitter.ReaderPid, "Un crash émetteur ne doit pas interrompre le lecteur.");
            await thirdSource.DisposeAsync();
            stop.Cancel(); await running.WaitAsync(TimeSpan.FromSeconds(30));
            Require(relay.Snapshot.Delay!.BufferBytes == 0, "Le buffer doit être libéré après arrêt.");
            await Decode(recovered.OutputFile!);
            await Inspect(recovered.OutputFile!);
            _checks.Add(new { Name = "IndependentNativeLifetimes", Status = "passed", SameEmitterAcrossInputFailures = true,
                SameReaderAcrossOutputFailure = true, InputSessions = recovered.Delay!.InputSessions,
                OutputReconnections = recovered.Delay.OutputReconnections, FinalBufferBytes = relay.Snapshot.Delay.BufferBytes,
                DecodedFile = recovered.OutputFile });
        }
        finally { stop.Cancel(); await running.WaitAsync(TimeSpan.FromSeconds(30)); }
    }

    private async Task NetworkRecoveryAsync()
    {
        Console.WriteLine("Test réseau local : destination indisponible, buffer conservé, reconnexion puis retour à zéro.");
        string key = "test-" + Guid.NewGuid().ToString("N");
        SecretRedactor.Shared.Remember(key);
        string config = Path.Combine(_directory, "destination-test.yml");
        await File.WriteAllTextAsync(config, (await File.ReadAllTextAsync(Path.Combine(root, "config", "mediamtx-preview.yml")))
            .Replace("live/preview", "live/" + key, StringComparison.Ordinal));
        var destination = Start(_mtx, [config]);
        await Until(() => Listening(1936), "destination initiale");
        var relay = Supervisor("network", new RelayDestination("rtmp://127.0.0.1:1936/live", key));
        using var stop = new CancellationTokenSource();
        Task running = relay.RunAsync(stop.Token);
        try
        {
            await Until(() => relay.Snapshot.MediaMtxPid.HasValue, "ingest réseau");
            var publisher = Publish();
            await Until(() => relay.Snapshot.OutputReady, "émission réseau initiale");
            var before = relay.Snapshot;
            await destination.DisposeAsync();
            await Until(() => relay.Snapshot.State == "RetryingOutput" && relay.Snapshot.Delay!.OutputReconnections > 0, "panne réseau détectée");
            long readAtFailure = relay.Snapshot.Delay!.ReadTags;
            await Until(() => relay.Snapshot.Delay!.ReadTags > readAtFailure + 120 && relay.Snapshot.Delay.BufferedTags > before.Delay!.BufferedTags + 80,
                "entrée maintenue pendant panne");
            long retained = relay.Snapshot.Delay!.BufferBytes;
            Require(relay.Snapshot.IngestConnected && !running.IsCompleted, "L'entrée doit rester active pendant la panne de destination.");
            var replacement = Start(_mtx, [config]);
            await Until(() => Listening(1936), "destination redémarrée");
            await Until(() => relay.Snapshot.OutputReady && relay.Snapshot.EmitterPid != before.EmitterPid, "émission réseau rétablie");
            var recovered = relay.Snapshot;
            Require(recovered.ReaderPid == before.ReaderPid && recovered.Delay!.InputSessions == before.Delay!.InputSessions,
                "L'entrée ne doit pas redémarrer avec la sortie réseau.");
            string captured = Path.Combine(_directory, "network-recovered.flv");
            // A reader joining mid-GOP must be allowed to inspect more than the 1 s keyframe interval.
            var receiver = Start(_ffmpeg, ["-hide_banner", "-loglevel", "warning", "-rw_timeout", "5000000", "-analyzeduration", "2000000", "-probesize", "4194304",
                "-i", "rtmp://127.0.0.1:1936/live/" + key, "-t", "5", "-map", "0:v:0", "-map", "0:a:0", "-c", "copy", "-f", "flv", "-n", captured]);
            relay.DelayControl.SetTarget(0);
            await Until(() => relay.Snapshot.Delay is { RecoveryDelayMilliseconds: 0, LastMediaAgeMilliseconds: <= 1500, Control.CompletedSeeks: > 0 }, "retour à zéro après panne");
            await receiver.RequireSuccessAsync();
            await publisher.DisposeAsync();
            stop.Cancel(); await running.WaitAsync(TimeSpan.FromSeconds(30));
            await Decode(captured); await Inspect(captured);
            await replacement.DisposeAsync();
            await _diagnostics.FlushAsync();
            using var logReader = new StreamReader(new FileStream(Path.Combine(_directory, "native.log"), FileMode.Open, FileAccess.Read, FileShare.ReadWrite));
            string logs = await logReader.ReadToEndAsync();
            Require(!logs.Contains(key, StringComparison.Ordinal), "La clé synthétique ne doit apparaître dans aucun diagnostic.");
            _checks.Add(new { Name = "NetworkOutage", Status = "passed", RetainedBufferBytes = retained,
                RecoveryDelayMilliseconds = recovered.Delay!.RecoveryDelayMilliseconds, SameReader = true,
                KeyOccurrencesInLogs = 0, ZeroRecovered = true, DecodedFile = captured });
        }
        finally { stop.Cancel(); await running.WaitAsync(TimeSpan.FromSeconds(30)); }
    }

    private OwnedProcess Publish() => Start(_ffmpeg, ["-hide_banner", "-loglevel", "warning", "-re", "-stream_loop", "-1", "-i", _source,
        "-t", "90", "-map", "0:v:0", "-map", "0:a:0", "-c", "copy", "-f", "flv", "-flvflags", "no_duration_filesize", "rtmp://127.0.0.1:1935/live/stream"]);
    private async Task Decode(string file) => await Start(_ffmpeg, ["-hide_banner", "-v", "error", "-xerror", "-i", file,
        "-map", "0:v:0", "-map", "0:a:0", "-f", "null", "NUL"]).RequireSuccessAsync();
    private static async Task Inspect(string file)
    {
        await using var input = File.OpenRead(file);
        var budget = new FlvMemoryBudget(8 * 1048576);
        var reader = new FlvTagReader(input, budget, new MonotonicMediaClock());
        await reader.ReadHeaderAsync(CancellationToken.None);
        uint? video = null, audio = null; int nal = 0; bool first = true;
        while (await reader.ReadTagAsync(CancellationToken.None) is { } tag)
        {
            using (tag)
            {
                if (tag.Type == FlvTagType.Video && tag.Payload.Span[1] == 0) nal = AvcAacInspector.ValidateAvcConfiguration(tag.Payload.Span);
                if (tag.Type == FlvTagType.Video && tag.Payload.Span[1] == 1)
                {
                    if (first) { Require(nal > 0 && AvcAacInspector.IsIdr(tag.Payload.Span, nal), "La première vidéo doit être une IDR précédée de sa configuration."); first = false; }
                    Require(video is null || tag.Timestamp >= video, "DTS vidéo non monotone."); video = tag.Timestamp;
                }
                if (tag.Type == FlvTagType.Audio && tag.Payload.Span[1] == 1)
                { Require(audio is null || tag.Timestamp >= audio, "DTS audio non monotone."); audio = tag.Timestamp; }
            }
        }
        Require(video.HasValue && audio.HasValue && budget.UsedBytes == 0, "Pistes audio/vidéo absentes ou fuite du parseur.");
    }
    private OwnedProcess Start(string executable, string[] arguments)
    { var owned = new OwnedProcess(executable, arguments, _diagnostics); _children.Add(owned); return owned; }
    private static void CheckPort(int port)
    {
        var listener = new TcpListener(IPAddress.Loopback, port); listener.Server.ExclusiveAddressUse = true;
        try { listener.Start(); } catch (SocketException) { throw new IOException($"Le port {port} est occupé. Arrêtez le relais et l'aperçu locaux avant ce test."); }
        finally { listener.Stop(); }
    }
    private static bool Listening(int port)
    { using var socket = new TcpClient(); try { socket.Connect(IPAddress.Loopback, port); return true; } catch (SocketException) { return false; } }
    private static async Task Until(Func<bool> predicate, string label)
    {
        var watch = Stopwatch.StartNew();
        while (!predicate())
        { if (watch.Elapsed > TimeSpan.FromSeconds(35)) throw new TimeoutException("Attente dépassée : " + label); await Task.Delay(100); }
        Console.WriteLine("  OK · " + label);
    }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidDataException(message); }
    private static void KillOwnedRelayPid(int? id, string expectedName)
    {
        if (!id.HasValue) throw new InvalidDataException("PID du relais absent.");
        // The PID comes exclusively from this in-memory supervisor's active snapshot.
        using var process = Process.GetProcessById(id.Value);
        Require(process.ProcessName == expectedName, "Le processus de test a changé avant terminaison.");
        process.Kill(entireProcessTree: true);
    }
}

internal sealed class CaptureLogger(TextWriter writer) : ILogger<LocalRelaySupervisor>
{
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public bool IsEnabled(LogLevel logLevel) => true;
    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        => writer.WriteLine(SecretRedactor.Shared.Redact($"{DateTime.UtcNow:O} {logLevel} {formatter(state, exception)}"));
}

internal sealed class OwnedProcess : IAsyncDisposable
{
    private readonly Process _process;
    private readonly Task[] _drains;
    private bool _disposed;
    public bool Exited => _disposed || _process.HasExited;
    public OwnedProcess(string executable, string[] arguments, TextWriter diagnostics)
    {
        var info = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardError = true, RedirectStandardOutput = true };
        foreach (string key in info.Environment.Keys.Where(k => k.StartsWith("MTX_", StringComparison.OrdinalIgnoreCase)).ToArray()) info.Environment.Remove(key);
        info.Environment.Remove("FFREPORT");
        foreach (string argument in arguments) info.ArgumentList.Add(argument);
        _process = Process.Start(info) ?? throw new IOException("Processus de test non démarré.");
        _drains = [Drain(_process.StandardError), Drain(_process.StandardOutput)];
        async Task Drain(StreamReader reader)
        { while (await reader.ReadLineAsync() is { } line) await diagnostics.WriteLineAsync(SecretRedactor.Shared.Redact(line)); }
    }
    public async Task RequireSuccessAsync()
    {
        await _process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(25));
        await Task.WhenAll(_drains);
        if (_process.ExitCode != 0) throw new IOException("Un outil de validation a échoué (code " + _process.ExitCode + "). Consulter native.log.");
    }
    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        if (!_process.HasExited) _process.Kill(entireProcessTree: true);
        await _process.WaitForExitAsync(); await Task.WhenAll(_drains); _process.Dispose(); _disposed = true;
    }
}
