using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using Microsoft.Win32;
using Temhalo.Infrastructure.Windows.LocalWeb;
using Temhalo.Infrastructure.Windows.Settings;

namespace Temhalo.CleanWindowsValidation;

internal static class Program
{
    internal const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\Temhalo";
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };
    private static readonly TimeSpan ManualTimeout = TimeSpan.FromMinutes(45);
    private static readonly List<object> Checks = [];
    private static string _results = "";
    private static string _stage = "not-started";
    private static bool _baselineEstablished;

    public static async Task<int> Main(string[] args)
    {
        if (args is ["--self-test"])
        {
            var settings = ExampleSettings();
            settings.Validate();
            if (Snapshot(settings) != Snapshot(settings with { })) return 1;
            if (Snapshot(settings) == Snapshot(settings with { MemoryLimitMiB = 512 })) return 2;
            if (Hash("a"u8.ToArray()) != "CA978112CA1BBDCAFAC231B39A23DC4DA786EFF8147C4E72B9807785AFEE48BB") return 3;
            return 0;
        }
        if (args is not ["--run", var input, var results]) return 64;

        // Refuse the development PC before creating files, installing or changing
        // registry/settings. This agent is intentionally not a general installer.
        if (!string.Equals(Environment.UserName, "WDAGUtilityAccount", StringComparison.OrdinalIgnoreCase))
            return 90;
        try
        {
            var plan = JsonSerializer.Deserialize<Plan>(await File.ReadAllTextAsync(Path.Combine(input, "plan.json")), Json)
                ?? throw new InvalidDataException();
            if (string.Equals(plan.SourceHost, Environment.MachineName, StringComparison.OrdinalIgnoreCase)) return 90;
            if (!Path.GetFullPath(input).Equals(@"C:\TemhaloValidation\Input", StringComparison.OrdinalIgnoreCase) ||
                !Path.GetFullPath(results).Equals(@"C:\TemhaloValidation\Results", StringComparison.OrdinalIgnoreCase)) return 90;
            _results = Path.Combine(results, "run-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N")[..8]);
            Directory.CreateDirectory(_results);
            await File.WriteAllTextAsync(Path.Combine(results, "latest-run.json"), JsonSerializer.Serialize(new
            { RunDirectory = Path.GetFileName(_results), ResultFile = Path.Combine(Path.GetFileName(_results), "result.json") }, Json));
            using var timeout = new CancellationTokenSource(TimeSpan.FromHours(3));
            await RunAsync(input, plan, timeout.Token);
            State("completed", "passed", "Validation terminée dans cette session Windows Sandbox.");
            return 0;
        }
        catch (Exception error)
        {
            // Never export settings, bearer tokens, destination keys or exception
            // messages that could include a request URL. Details stay in VM logs.
            if (_results.Length > 0)
                State(_stage, "failed", "Échec de validation : " + error.GetType().Name + ". Consultez les applications dans la session.");
            return 1;
        }
    }

    private static async Task RunAsync(string input, Plan plan, CancellationToken token)
    {
        string install = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Temhalo");
        string data = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Temhalo");
        using (var registry = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Registry64))
        using (var existing = registry.OpenSubKey(UninstallKey))
            Check(existing is null && !Directory.Exists(install) && !Directory.Exists(data), "Baseline sans installation ni profil Temhalo");
        Check(!Directory.Exists(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "dotnet", "sdk")),
            "Aucun SDK .NET global dans le Windows invité");
        Check(!Directory.Exists(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "obs-studio")) &&
            !Directory.Exists(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "obs-studio")),
            "Aucune installation ni profil OBS préexistant");
        _baselineEstablished = true;
        await File.WriteAllTextAsync(Path.Combine(_results, "baseline.json"), JsonSerializer.Serialize(new
        {
            OS = Environment.OSVersion.VersionString, Machine = Environment.MachineName, Account = Environment.UserName,
            Elevated = new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator),
            SourceHostIsDifferent = true, WindowsSandbox = true, NoSdkInstalled = true,
            Note = "Le compte Sandbox peut être administrateur ; ce test ne remplace pas un compte standard sur un autre PC."
        }, Json), token);
        foreach (var artifact in plan.Artifacts)
            Check(Hash(await File.ReadAllBytesAsync(Path.Combine(input, artifact.Name), token)) == artifact.Sha256.ToUpperInvariant(),
                "Empreinte de l'entrée " + artifact.Name);

        State("installation", "running", "Installation réelle de Temhalo dans le profil vierge.");
        await RunProcess(Path.Combine(input, plan.Setup), ["/S"], TimeSpan.FromMinutes(3), token);
        await VerifyPayload(install, plan.InitialFiles, token);
        VerifyRegistration(install, plan.InitialNumericVersion);
        VerifyShortcuts(install);
        var store = new DpapiSettingsStore(data);
        await store.SaveAsync(ExampleSettings(), token);
        var app = StartApp(install);
        State("first-launch", "running", "Premier lancement avant l'installation d'OBS.");
        await WaitFor(() => !app.HasExited && app.MainWindowHandle != IntPtr.Zero, TimeSpan.FromMinutes(2), token);
        Check(app.Modules.Cast<ProcessModule>().Any(module => module.ModuleName.Equals("coreclr.dll", StringComparison.OrdinalIgnoreCase) &&
            module.FileName.StartsWith(install + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)),
            ".NET chargé depuis le dossier installé de Temhalo");
        Check(app.Modules.Cast<ProcessModule>().Any(module => module.ModuleName.Equals("Microsoft.UI.Xaml.dll", StringComparison.OrdinalIgnoreCase) &&
            module.FileName.StartsWith(install + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)),
            "WinUI chargé depuis le dossier installé de Temhalo");

        var loaded = await store.LoadAsync(token);
        var access = loaded.LocalAccess ?? throw new InvalidDataException();
        using var http = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{access.Port}/"), Timeout = TimeSpan.FromSeconds(5) };
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", access.DockToken);
        State("awaiting-first-relay", "waiting-for-action",
            "Dans Temhalo, conserver Fichier local et cliquer Démarrer le relais. L'application doit acquérir MediaMTX/FFmpeg avant l'installation d'OBS.");
        await WaitFor(async () =>
        {
            if (app.HasExited) throw new IOException();
            try { return await http.GetFromJsonAsync<LocalViewState>("api/state", Json, token) is { Running: true }; }
            catch (HttpRequestException) { return false; }
            catch (TaskCanceledException) when (!token.IsCancellationRequested) { return false; }
        }, ManualTimeout, token);
        string toolsDirectory = Path.Combine(data, "tools");
        string nativeFfmpeg = Directory.EnumerateFiles(toolsDirectory, "ffmpeg.exe", SearchOption.AllDirectories).Single();
        string nativeMtx = Directory.EnumerateFiles(toolsDirectory, "mediamtx.exe", SearchOption.AllDirectories).Single();
        await RunProcess(nativeFfmpeg, ["-hide_banner", "-version"], TimeSpan.FromSeconds(30), token);
        await RunProcess(nativeMtx, ["--version"], TimeSpan.FromSeconds(30), token);
        Check(true, "Acquisition à froid par Temhalo et lancement des outils média avant installation d'OBS");

        State("obs-installation", "running", "Installation d'OBS officiel après le premier lancement de Temhalo.");
        await RunProcess(Path.Combine(input, plan.ObsSetup), ["/S"], TimeSpan.FromMinutes(10), token);
        string obsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "obs-studio", "bin", "64bit", "obs64.exe");
        Check(File.Exists(obsPath), "OBS officiel installé dans l'invité");
        using var obs = Process.Start(new ProcessStartInfo(obsPath)
        {
            WorkingDirectory = Path.GetDirectoryName(obsPath)!,
            UseShellExecute = true,
            Arguments = "--disable-shutdown-check"
        }) ?? throw new IOException();

        State("awaiting-obs", "waiting-for-action",
            @"Dans Temhalo : Démarrer le relais. Dans OBS : flux personnalisé rtmp://127.0.0.1:1935/live, clé stream ; H.264/AAC, CBR, keyframe 1 s. Ajouter C:\TemhaloValidation\Input\test-pattern.mp4 en source média en boucle, puis démarrer la diffusion. Aucune plateforme publique.");
        LocalViewState? view = null;
        await WaitFor(async () =>
        {
            if (app.HasExited || obs.HasExited) throw new IOException();
            try { view = await http.GetFromJsonAsync<LocalViewState>("api/state", Json, token); }
            catch (HttpRequestException) { return false; }
            catch (TaskCanceledException) when (!token.IsCancellationRequested) { return false; }
            return view is { Running: true, CanControl: true, IngestConnected: true };
        }, ManualTimeout, token);
        Check(true, "OBS connecté au relais installé, contrôle du moteur disponible");
        await Task.Delay(TimeSpan.FromSeconds(10), token);
        await ChangeDelay(http, 15, token);
        State("delay-15", "running", "Mesure du délai pendant la publication OBS.");
        await WaitFor(async () =>
        {
            view = await http.GetFromJsonAsync<LocalViewState>("api/state", Json, token);
            return view is { IngestConnected: true, TargetSeconds: 15, CurrentDelayMilliseconds: >= 13000 and <= 17000 };
        }, TimeSpan.FromSeconds(70), token);
        Check(true, "Délai mesuré par le moteur entre 13 et 17 secondes pour une cible de 15 secondes");
        await ChangeDelay(http, 0, token);
        await WaitFor(async () =>
        {
            view = await http.GetFromJsonAsync<LocalViewState>("api/state", Json, token);
            return view is { IngestConnected: true, TargetSeconds: 0, CurrentDelayMilliseconds: >= 0 and <= 2000 };
        }, TimeSpan.FromSeconds(20), token);
        Check(true, "Retour au minimum local sur le flux OBS");
        await Task.Delay(TimeSpan.FromSeconds(10), token);
        State("close-before-upgrade", "waiting-for-action",
            "Arrêter la diffusion OBS, arrêter le relais Temhalo, puis fermer normalement la fenêtre Temhalo. L'agent poursuivra la mise à jour.");
        await app.WaitForExitAsync(token).WaitAsync(ManualTimeout, token);
        Check(app.ExitCode == 0, "Premier arrêt normal de Temhalo");
        app.Dispose();
        await VerifyRecording(data, token);
        string encryptedBefore = Hash(await File.ReadAllBytesAsync(store.FilePath, token));
        string settingsBefore = Snapshot(await store.LoadAsync(token));

        State("upgrade", "running", "Mise à jour de la révision de l'installeur, mêmes binaires applicatifs validés.");
        await RunProcess(Path.Combine(input, plan.UpgradeSetup), ["/S"], TimeSpan.FromMinutes(3), token);
        await VerifyPayload(install, plan.UpgradeFiles, token);
        VerifyRegistration(install, plan.UpgradeNumericVersion);
        VerifyShortcuts(install);
        Check(encryptedBefore == Hash(await File.ReadAllBytesAsync(store.FilePath, token)), "Octets DPAPI conservés pendant la mise à jour");
        Check(settingsBefore == Snapshot(await store.LoadAsync(token)), "Réglages et clé témoin déchiffrables après mise à jour");
        using var updatedApp = StartApp(install);
        State("verify-updated-ui", "waiting-for-action",
            "Vérifier l'interface mise à jour : mémoire 384 MiB, presets 0/12/24/48 s, overlay Fil, raccourcis désactivés. Fermer normalement Temhalo pour poursuivre la désinstallation.");
        await WaitFor(() => !updatedApp.HasExited && updatedApp.MainWindowHandle != IntPtr.Zero, TimeSpan.FromMinutes(2), token);
        await updatedApp.WaitForExitAsync(token).WaitAsync(ManualTimeout, token);
        Check(updatedApp.ExitCode == 0, "Relancement et arrêt normal après mise à jour");
        Check(settingsBefore == Snapshot(await store.LoadAsync(token)), "Réglages conservés après relancement de la version mise à jour");

        encryptedBefore = Hash(await File.ReadAllBytesAsync(store.FilePath, token));
        string canary = Path.Combine(install, "fichier-personnel-validation.txt");
        await File.WriteAllTextAsync(canary, "Fichier témoin ajouté par le test, hors inventaire de l'application.", token);
        State("uninstall", "running", "Désinstallation réelle, conservation des données et des fichiers ajoutés.");
        await RunProcess(Path.Combine(install, "Uninstall.exe"), ["/S"], TimeSpan.FromMinutes(3), token);
        await WaitFor(() =>
        {
            using var root = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Registry64);
            using var remainingEntry = root.OpenSubKey(UninstallKey);
            return !File.Exists(Path.Combine(install, "Uninstall.exe")) && remainingEntry is null;
        }, TimeSpan.FromMinutes(1), token);
        using (var registry = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Registry64))
        using (var remaining = registry.OpenSubKey(UninstallKey))
            Check(remaining is null, "Entrée Windows désinstallée");
        Check(!File.Exists(DesktopShortcut) && !Directory.Exists(MenuDirectory), "Raccourcis Bureau et menu Démarrer retirés");
        string[] leftovers = Directory.GetFiles(install, "*", SearchOption.AllDirectories);
        Check(leftovers.Length == 1 && leftovers[0] == canary, "Seul le fichier personnel ajouté reste dans le dossier");
        Check(encryptedBefore == Hash(await File.ReadAllBytesAsync(store.FilePath, token)) &&
            settingsBefore == Snapshot(await store.LoadAsync(token)), "Réglages et clé témoin conservés après désinstallation");
        // Only this explicit test-owned file is removed; no recursive deletion.
        File.Delete(canary);
        Directory.Delete(install, recursive: false);
        Check(!Directory.Exists(install), "Dossier applicatif vide retiré");
    }

    private static AppSettings ExampleSettings() => new()
    {
        OutputMode = 0, Provider = 3, ServerUrl = "rtmp://127.0.0.1:1936/local",
        StreamKey = "TEMHALO-TEST-" + Guid.NewGuid().ToString("N"), MemoryLimitMiB = 384,
        Hotkeys = new() { Enabled = false, ProtectionSeconds = 48 },
        Interface = new() { Presets = [0, 12, 24, 48], OverlayStyle = 2 },
        Destinations = [], LocalAccess = new()
    };
    private static string Snapshot(AppSettings settings) => Hash(JsonSerializer.SerializeToUtf8Bytes(settings));
    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
    private static string DesktopShortcut => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "Temhalo.lnk");
    private static string MenuDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "Temhalo");

    private static Process StartApp(string install) => Process.Start(new ProcessStartInfo(Path.Combine(install, "Temhalo.App.exe"))
    { WorkingDirectory = install, UseShellExecute = true }) ?? throw new IOException();
    private static async Task RunProcess(string executable, string[] arguments, TimeSpan deadline, CancellationToken token)
    {
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true };
        foreach (string argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new IOException();
        await process.WaitForExitAsync(token).WaitAsync(deadline, token);
        if (process.ExitCode != 0) throw new IOException("Processus de validation non terminé normalement.");
    }
    private static async Task VerifyPayload(string install, ExpectedFile[] files, CancellationToken token)
    {
        string prefix = Path.GetFullPath(install) + Path.DirectorySeparatorChar;
        foreach (var file in files)
        {
            string path = Path.GetFullPath(Path.Combine(install, file.Path));
            if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException();
            Check(File.Exists(path) && Hash(await File.ReadAllBytesAsync(path, token)) == file.Sha256.ToUpperInvariant(),
                "Fichier installé conforme : " + file.Path, persist: false);
        }
        State(_stage, "running", files.Length + " fichiers installés vérifiés par SHA-256.");
    }
    private static void VerifyRegistration(string install, string numericVersion)
    {
        using var registry = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Registry64);
        using var entry = registry.OpenSubKey(UninstallKey);
        Check(entry?.GetValue("InstallLocation") as string == install &&
            entry?.GetValue("NumericVersion") as string == numericVersion, "Révision et dossier enregistrés dans Windows");
    }
    private static void VerifyShortcuts(string install)
    {
        Type shellType = Type.GetTypeFromProgID("WScript.Shell") ?? throw new IOException();
        object shell = Activator.CreateInstance(shellType)!;
        try
        {
            foreach (string path in new[] { DesktopShortcut, Path.Combine(MenuDirectory, "Temhalo.lnk") })
            {
                Check(File.Exists(path), "Raccourci présent : " + Path.GetFileName(Path.GetDirectoryName(path)));
                object link = shellType.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, [path])!;
                try
                {
                    var target = (string)link.GetType().InvokeMember("TargetPath", BindingFlags.GetProperty, null, link, null)!;
                    var icon = (string)link.GetType().InvokeMember("IconLocation", BindingFlags.GetProperty, null, link, null)!;
                    Check(target == Path.Combine(install, "Temhalo.App.exe") &&
                        icon == Path.Combine(install, @"Assets\Brand\temhalo.ico") + ",0", "Cible et icône du raccourci");
                }
                finally { Marshal.FinalReleaseComObject(link); }
            }
            Check(File.Exists(Path.Combine(MenuDirectory, "Désinstaller Temhalo.lnk")), "Entrée Désinstaller dans le menu Démarrer");
        }
        finally { Marshal.FinalReleaseComObject(shell); }
    }
    private static async Task ChangeDelay(HttpClient http, int seconds, CancellationToken token)
    {
        using var response = await http.PostAsJsonAsync("api/delay", new DelayCommand("set", seconds), token);
        Check(response.IsSuccessStatusCode, "Commande locale de délai acceptée : " + seconds + " s");
    }
    private static async Task VerifyRecording(string data, CancellationToken token)
    {
        string file = Directory.EnumerateFiles(Path.Combine(data, "sessions"), "*.flv", SearchOption.AllDirectories)
            .OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault() ?? throw new IOException();
        Check(new FileInfo(file).Length > 1024 * 1024, "Enregistrement OBS relayé non vide");
        string probe = Directory.EnumerateFiles(Path.Combine(data, "tools"), "ffprobe.exe", SearchOption.AllDirectories).Single();
        var start = new ProcessStartInfo(probe) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (string argument in new[] { "-v", "error", "-show_streams", "-of", "json", file }) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new IOException();
        Task<string> stdout = process.StandardOutput.ReadToEndAsync(token);
        Task<string> stderr = process.StandardError.ReadToEndAsync(token);
        await process.WaitForExitAsync(token).WaitAsync(TimeSpan.FromSeconds(30), token);
        string output = await stdout; _ = await stderr;
        using var info = JsonDocument.Parse(output);
        var codecs = info.RootElement.GetProperty("streams").EnumerateArray().Select(stream => stream.GetProperty("codec_name").GetString()).ToArray();
        Check(process.ExitCode == 0 && codecs.Contains("h264") && codecs.Contains("aac"), "Fichier relayé reconnu H.264 et AAC");
        string decoder = Directory.EnumerateFiles(Path.Combine(data, "tools"), "ffmpeg.exe", SearchOption.AllDirectories).Single();
        await RunProcess(decoder, ["-hide_banner", "-v", "error", "-xerror", "-err_detect", "explode", "-i", file, "-f", "null", "-"],
            TimeSpan.FromMinutes(2), token);
        Check(true, "Décodage complet du fichier relayé sans erreur FFmpeg");
        await File.WriteAllTextAsync(Path.Combine(_results, "media-probe.json"), output, token);
        File.Copy(file, Path.Combine(_results, "obs-relayed.flv"), overwrite: false);
    }
    private static Task WaitFor(Func<bool> condition, TimeSpan deadline, CancellationToken token) =>
        WaitFor(() => Task.FromResult(condition()), deadline, token);
    private static async Task WaitFor(Func<Task<bool>> condition, TimeSpan deadline, CancellationToken token)
    {
        DateTime end = DateTime.UtcNow + deadline;
        while (!await condition())
        {
            token.ThrowIfCancellationRequested();
            if (DateTime.UtcNow > end) throw new TimeoutException();
            await Task.Delay(1000, token);
        }
    }
    private static void Check(bool success, string name, bool persist = true)
    {
        Checks.Add(new { Name = name, Passed = success, At = DateTimeOffset.UtcNow });
        if (!success) throw new InvalidOperationException("Critère de validation non satisfait.");
        if (persist && _results.Length > 0) State(_stage, "running", name);
    }
    private static void State(string stage, string status, string message)
    {
        _stage = stage;
        string path = Path.Combine(_results, "result.json");
        string temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(new
        {
            Status = status, Stage = stage, Message = message, Updated = DateTimeOffset.UtcNow,
            CleanBaselineEstablished = _baselineEstablished, Checks, ApplicationPayloadUnchangedDuringUpgrade = true,
            Claims = new { Windows10Tested = false, OtherWindows11BuildsTested = false, PublicPlatformTested = false,
                IndependentStopwatchLatencyTested = false, PerceptualAudioVideoSyncTested = false }
        }, Json), Encoding.UTF8);
        File.Move(temporary, path, overwrite: true);
    }
}

internal sealed record ExpectedFile(string Path, string Sha256);
internal sealed record Artifact(string Name, string Sha256);
internal sealed record Plan(string SourceHost, string Setup, string UpgradeSetup, string ObsSetup,
    string InitialNumericVersion, string UpgradeNumericVersion, Artifact[] Artifacts,
    ExpectedFile[] InitialFiles, ExpectedFile[] UpgradeFiles);
