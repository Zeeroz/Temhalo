namespace Temhalo.Infrastructure.Windows.Deployment;

/// <summary>Immutable installation files and writable per-user data have separate roots.</summary>
public sealed record ApplicationLayout(string Root, string DataDirectory, bool IsPortable)
{
    public string ConfigurationDirectory => Path.Combine(Root, "config");
    public string SessionDirectory => IsPortable ? Path.Combine(DataDirectory, "sessions") : Path.Combine(Root, "artifacts", "relay-ui");
    public string NativeCacheDirectory => Path.Combine(DataDirectory, "tools");

    public static ApplicationLayout Resolve(string baseDirectory, string workingDirectory, string dataDirectory)
    {
        string app = Path.GetFullPath(baseDirectory);
        if (File.Exists(Path.Combine(app, "temhalo.portable.json")))
        {
            if (!File.Exists(Path.Combine(app, "config", "native-tools.lock.json")) || !File.Exists(Path.Combine(app, "config", "mediamtx.yml")))
                throw new IOException("Le dossier Temhalo est incomplet. Extrayez toute l'archive dans un même dossier.");
            return new(app, Path.GetFullPath(dataDirectory), true);
        }
        foreach (string start in new[] { app, workingDirectory })
            for (var folder = new DirectoryInfo(start); folder is not null; folder = folder.Parent)
                if (File.Exists(Path.Combine(folder.FullName, "Temhalo.sln"))) return new(folder.FullName, Path.GetFullPath(dataDirectory), false);
        throw new DirectoryNotFoundException("Installation Temhalo introuvable. Extrayez toute l'archive, puis lancez Temhalo.App.exe.");
    }
}
