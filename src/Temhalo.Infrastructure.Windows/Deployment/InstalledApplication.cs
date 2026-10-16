using System.Diagnostics;
using Microsoft.Win32;

namespace Temhalo.Infrastructure.Windows.Deployment;

public static class InstalledApplication
{
    public static bool IsCurrentInstallation(string baseDirectory)
    {
        using var registry = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Registry64);
        using var key = registry.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\Temhalo");
        return key?.GetValue("InstallLocation") is string location && Path.IsPathFullyQualified(location) &&
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(location)).Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(baseDirectory)), StringComparison.OrdinalIgnoreCase) &&
            File.Exists(Path.Combine(baseDirectory, "Uninstall.exe"));
    }

    public static async Task LaunchInstallerAsync(DownloadedUpdate update, string baseDirectory, CancellationToken token)
    {
        if (!IsCurrentInstallation(baseDirectory)) throw new InvalidOperationException("Cette copie est portable ou de développement. Utilisez l’installeur depuis GitHub pour bénéficier des mises à jour intégrées.");
        await GitHubUpdateService.VerifyAsync(update, token).ConfigureAwait(false);
        // NSIS waits for this process to exit, then upgrades the registered per-user location.
        // Its normal wizard remains visible and never writes into the user's data directory.
        using var process = Process.Start(new ProcessStartInfo(update.FilePath) { UseShellExecute = true, Arguments = $"/WAITPID={Environment.ProcessId}" })
            ?? throw new IOException("Impossible de démarrer l’installeur.");
    }
}
