using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;
using Temhalo.Core.Buffering;
using Temhalo.Infrastructure.Windows.Logging;
using Temhalo.Infrastructure.Windows.Processes;

return await RelayProgram.RunAsync(args);

internal static class RelayProgram
{
    public static async Task<int> RunAsync(string[] args)
    {
        Console.OutputEncoding = new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        if (args.Contains("--help", StringComparer.Ordinal))
        {
            Console.WriteLine("Temhalo étape 3 — relais local H.264/AAC, délai pilotable de 0 à 300 secondes.");
            Console.WriteLine("--repo-root <dossier> --output-directory <dossier> --run-seconds <1..86400> --stop-file <fichier>");
            Console.WriteLine("--delay-seconds <0..300> --buffer-mib <1..4096>");
            Console.WriteLine("--pause-step-seconds <1..20> --resume-seconds <1..10>");
            Console.WriteLine("Pendant le live : tools/Set-LocalDelay.ps1 -Seconds 30, dans un second terminal.");
            Console.WriteLine("Ctrl+C arrête la lecture puis vidange le buffer au délai prévu. Aucune sortie réseau.");
            return 0;
        }
        try { return await ExecuteAsync(args); }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Temhalo: {NativeLogSanitizer.Sanitize(ex.Message)}");
            return 1;
        }
    }

    private static async Task<int> ExecuteAsync(string[] args)
    {
        var command = Parse(args);
        string root = FindRoot(command.GetValueOrDefault("--repo-root"));
        string output = Path.GetFullPath(command.GetValueOrDefault("--output-directory") ??
            Path.Combine(root, "artifacts", "relay-local"));
        string runDirectory = Path.Combine(output, $"run-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(runDirectory);
        string? stopFile = command.TryGetValue("--stop-file", out var stop) ? Path.GetFullPath(stop) : null;
        if (stopFile is not null && File.Exists(stopFile)) throw new IOException("Le fichier d'arrêt existe déjà.");
        int? duration = command.TryGetValue("--run-seconds", out var seconds)
            ? int.Parse(seconds, CultureInfo.InvariantCulture) : null;
        if (duration is < 1 or > 86400) throw new ArgumentException("--run-seconds doit être compris entre 1 et 86400.");
        var defaults = JsonSerializer.Deserialize<RelayDefaults>(await File.ReadAllTextAsync(
            Path.Combine(root, "config", "temhalo.defaults.json"))) ?? throw new IOException("Configuration par défaut invalide.");
        int delaySeconds = command.TryGetValue("--delay-seconds", out var delayText)
            ? int.Parse(delayText, CultureInfo.InvariantCulture) : defaults.Delay.InitialSeconds;
        long bufferBytes = command.TryGetValue("--buffer-mib", out var bufferText)
            ? checked(long.Parse(bufferText, CultureInfo.InvariantCulture) * 1024 * 1024) : defaults.Buffer.MaximumBytes;
        if (delaySeconds is < 0 or > 300 || bufferBytes is < 1048576 or > 4294967296)
            throw new ArgumentException("Délai attendu : 0..300 s ; mémoire attendue : 1..4096 MiB.");
        int pauseSeconds = command.TryGetValue("--pause-step-seconds", out var pauseText)
            ? int.Parse(pauseText, CultureInfo.InvariantCulture) : defaults.Delay.RequestedMaximumPauseStepSeconds;
        int resumeSeconds = command.TryGetValue("--resume-seconds", out var resumeText)
            ? int.Parse(resumeText, CultureInfo.InvariantCulture) : defaults.Delay.ResumeBetweenStepsSeconds;
        if (pauseSeconds is < 1 or > 20 || resumeSeconds is < 1 or > 10)
            throw new ArgumentException("Palier attendu : 1..20 s ; reprise intermédiaire : 1..10 s.");
        var tools = JsonSerializer.Deserialize<ResolvedTools>(await File.ReadAllTextAsync(
            Path.Combine(root, "artifacts", "native-tools.resolved.json")))
            ?? throw new IOException("Manifeste natif invalide. Exécuter Get-NativeTools.ps1.");
        string Tool(string name)
        {
            string exe = tools.Tools.Single(t => t.Name == name).Executable;
            if (!File.Exists(exe)) throw new FileNotFoundException($"Binaire {name} absent. Exécuter Get-NativeTools.ps1.");
            return Path.GetFullPath(exe);
        }
        var options = new LocalRelayOptions(Tool("MediaMTX"), Tool("FFmpeg"),
            Path.Combine(root, "config", "mediamtx.yml"), runDirectory)
        {
            FixedDelay = new FixedDelayOptions { DelaySeconds = delaySeconds, MaximumBufferBytes = bufferBytes,
                WarningUsageRatio = defaults.Buffer.WarningUsageRatio, MaximumPauseStepSeconds = pauseSeconds,
                ResumeBetweenStepsSeconds = resumeSeconds }
        };
        using var fileLog = SafeFileLogger.Create(Path.Combine(runDirectory, "logs"), prefix: "relay-");
        var builder = Host.CreateApplicationBuilder(Array.Empty<string>());
        builder.Logging.ClearProviders();
        builder.Logging.AddSimpleConsole(o => { o.SingleLine = true; o.TimestampFormat = "HH:mm:ss "; });
        builder.Logging.AddSerilog(fileLog, dispose: false);
        builder.Services.AddSingleton(options);
        builder.Services.AddSingleton<LocalRelaySupervisor>();
        using var host = builder.Build();
        // The console owns cancellation; composition is shared with the future WinUI host.
        using var cancellation = new CancellationTokenSource();
        ConsoleCancelEventHandler cancel = (_, e) => { e.Cancel = true; cancellation.Cancel(); };
        Console.CancelKeyPress += cancel;
        var supervisor = host.Services.GetRequiredService<LocalRelaySupervisor>();
        DateTimeOffset started = DateTimeOffset.UtcNow;
        var elapsed = Stopwatch.StartNew();
        string statusFile = Path.Combine(runDirectory, "status.json");
        Console.WriteLine($"Fichiers de cette session : {runDirectory}");
        Console.WriteLine($"Délai initial : {delaySeconds} s ; buffer : {bufferBytes / 1048576} MiB ; paliers : {pauseSeconds} s.");
        Task relay = supervisor.RunAsync(cancellation.Token);
        bool warningLogged = false;
        string? lastRequestText = null;
        string? acceptedId = null;
        long? acceptedVersion = null;
        string? previousPhase = null;
        try
        {
            while (!relay.IsCompleted)
            {
                if (!cancellation.IsCancellationRequested)
                {
                    string requestFile = Path.Combine(runDirectory, "delay-request.json");
                    if (File.Exists(requestFile))
                    {
                        try
                        {
                            if (new FileInfo(requestFile).Length > 1024) throw new InvalidDataException("Commande trop volumineuse.");
                            string requestText = await File.ReadAllTextAsync(requestFile);
                            if (requestText != lastRequestText)
                            {
                                lastRequestText = requestText;
                                var request = JsonSerializer.Deserialize<LocalDelayRequest>(requestText)
                                    ?? throw new InvalidDataException("Commande vide.");
                                if (!Guid.TryParse(request.Id, out _)) throw new InvalidDataException("Identifiant de commande invalide.");
                                if (!request.Seconds.HasValue) throw new InvalidDataException("Délai manquant dans la commande.");
                                var result = supervisor.DelayControl.SetTarget(request.Seconds.Value);
                                acceptedId = request.Id; acceptedVersion = result.Version;
                                host.Services.GetRequiredService<ILogger<LocalRelaySupervisor>>()
                                    .LogInformation("Cible demandée : {Seconds} s, commande {Version}", request.Seconds, result.Version);
                            }
                        }
                        catch (Exception ex) when (ex is IOException or InvalidDataException or JsonException or ArgumentException)
                        {
                            host.Services.GetRequiredService<ILogger<LocalRelaySupervisor>>()
                                .LogWarning("Commande locale refusée ou indisponible : {Reason}", NativeLogSanitizer.Sanitize(ex.Message));
                        }
                    }
                }
                var current = supervisor.Snapshot;
                string? phase = current.Delay?.Control.Phase;
                if (phase != previousPhase)
                {
                    previousPhase = phase;
                    host.Services.GetRequiredService<ILogger<LocalRelaySupervisor>>()
                        .LogInformation("Transition du délai : {Phase}", phase);
                }
                await WriteStatusAsync(statusFile, runDirectory, started, current, acceptedId, acceptedVersion, !cancellation.IsCancellationRequested);
                if (!warningLogged && supervisor.Snapshot.Delay is { WarningObserved: true })
                {
                    host.Services.GetRequiredService<ILogger<LocalRelaySupervisor>>()
                        .LogWarning("Le buffer a atteint le seuil d'alerte mémoire ({Ratio:P0})", defaults.Buffer.WarningUsageRatio);
                    warningLogged = true;
                }
                if ((duration.HasValue && elapsed.Elapsed.TotalSeconds >= duration.Value) ||
                    (stopFile is not null && File.Exists(stopFile))) cancellation.Cancel();
                await Task.WhenAny(relay, Task.Delay(250));
            }
            await relay;
            return 0;
        }
        finally
        {
            cancellation.Cancel();
            try { await relay; }
            finally
            {
                Console.CancelKeyPress -= cancel;
                await WriteStatusAsync(statusFile, runDirectory, started, supervisor.Snapshot, acceptedId, acceptedVersion, false);
            }
        }
    }

    private static Dictionary<string, string> Parse(string[] args)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        string[] allowed = ["--repo-root", "--output-directory", "--run-seconds", "--stop-file", "--delay-seconds", "--buffer-mib", "--pause-step-seconds", "--resume-seconds"];
        for (int i = 0; i < args.Length; i += 2)
        {
            if (!allowed.Contains(args[i], StringComparer.Ordinal) || i + 1 >= args.Length ||
                !result.TryAdd(args[i], args[i + 1])) throw new ArgumentException("Arguments invalides. Utiliser --help.");
        }
        return result;
    }

    private static string FindRoot(string? requested)
    {
        if (requested is not null)
        {
            string root = Path.GetFullPath(requested);
            if (File.Exists(Path.Combine(root, "Temhalo.sln"))) return root;
            throw new DirectoryNotFoundException("--repo-root doit contenir Temhalo.sln.");
        }
        foreach (string start in new[] { Environment.CurrentDirectory, AppContext.BaseDirectory })
        {
            for (var folder = new DirectoryInfo(start); folder is not null; folder = folder.Parent)
                if (File.Exists(Path.Combine(folder.FullName, "Temhalo.sln"))) return folder.FullName;
        }
        throw new DirectoryNotFoundException("Racine Temhalo introuvable ; utiliser --repo-root.");
    }

    private static async Task WriteStatusAsync(string file, string directory, DateTimeOffset started,
        LocalRelaySnapshot snapshot, string? acceptedId = null, long? acceptedVersion = null, bool acceptsCommands = true)
    {
        string temporary = file + ".tmp";
        string json = JsonSerializer.Serialize(new
        {
            SchemaVersion = 3, Stage = 3, HostPid = Environment.ProcessId,
            StartedUtc = started, UpdatedUtc = DateTimeOffset.UtcNow, RunDirectory = directory, Relay = snapshot,
            AcceptedDelayCommandId = acceptedId, AcceptedDelayCommandVersion = acceptedVersion,
            AcceptingDelayCommands = acceptsCommands
        }, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(temporary, json);
        // A Windows reader may briefly hold the previous status without FILE_SHARE_DELETE.
        // Retry the rename; never expose a partially rewritten JSON document.
        for (int attempt = 0; ; attempt++)
        {
            try { File.Move(temporary, file, overwrite: true); break; }
            catch (Exception ex) when (attempt < 12 && ex is IOException or UnauthorizedAccessException)
            { await Task.Delay(25); }
        }
    }

    private sealed record ResolvedTools(ResolvedTool[] Tools);
    private sealed record ResolvedTool(string Name, string Executable);
    private sealed record RelayDefaults(DelayDefaults Delay, BufferDefaults Buffer);
    private sealed record DelayDefaults(int InitialSeconds, int RequestedMaximumPauseStepSeconds, int ResumeBetweenStepsSeconds);
    private sealed record BufferDefaults(long MaximumBytes, double WarningUsageRatio);
    private sealed record LocalDelayRequest(string Id, int? Seconds);
}
