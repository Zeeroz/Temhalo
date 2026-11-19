using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;

namespace Temhalo.Infrastructure.Windows.Deployment;

public sealed record ProvisionedTools(string MediaMtx, string Ffmpeg);

/// <summary>
/// Pinned downloads tried from each listed mirror in order; never executes a partial or unverified installation.
/// The SHA-256 is the identity of an archive, so any mirror may serve it.
/// </summary>
public sealed class NativeToolProvisioner(string lockFile, string cacheDirectory, HttpClient? client = null)
{
    private sealed record Manifest(int SchemaVersion, string Architecture, Tool[] Tools);
    private sealed record Tool(string Name, string Version, string? Url, string[]? Urls, string Sha256, string ArchiveName,
        string ArchiveRoot, string DirectoryName, string Executable);
    private sealed record Provenance(string ArchiveSha256, Dictionary<string, string> Files);

    public async Task<ProvisionedTools> EnsureAsync(IProgress<string>? progress = null, CancellationToken token = default)
    {
        var manifest = JsonSerializer.Deserialize<Manifest>(await File.ReadAllTextAsync(lockFile, token).ConfigureAwait(false))
            ?? throw new InvalidDataException("Manifeste des outils natifs absent.");
        if (manifest.SchemaVersion is not (1 or 2) || manifest.Architecture != "win-x64" || manifest.Tools.Length != 2 ||
            !manifest.Tools.Select(t => t.Name).Order().SequenceEqual(new[] { "FFmpeg", "MediaMTX" }))
            throw new InvalidDataException("Manifeste des outils natifs invalide.");
        string root = Path.GetFullPath(cacheDirectory); Directory.CreateDirectory(root);
        await using var gate = await AcquireLockAsync(Path.Combine(root, "install.lock"), token).ConfigureAwait(false);
        using var ownedClient = client is null ? new HttpClient { Timeout = TimeSpan.FromMinutes(10) } : null;
        var http = client ?? ownedClient!;
        var results = new Dictionary<string, string>();
        foreach (var tool in manifest.Tools)
        {
            var sources = tool.Urls ?? (tool.Url is null ? Array.Empty<string>() : new[] { tool.Url });
            if (tool.Sha256.Length != 64 || tool.Sha256.Any(c => !Uri.IsHexDigit(c)) ||
                tool.DirectoryName is not ("mediamtx" or "ffmpeg") || Path.GetFileName(tool.ArchiveName) != tool.ArchiveName ||
                sources.Length is 0 or > 8 || !sources.All(IsAllowedSource))
                throw new InvalidDataException("Provenance native non autorisée.");
            string archive = Child(root, tool.ArchiveName);
            if (!File.Exists(archive))
                await DownloadAsync(http, tool, sources, archive, root, progress, token).ConfigureAwait(false);
            progress?.Report($"Vérification SHA-256 de {tool.Name}…");
            await VerifyAsync(archive, tool.Sha256, token).ConfigureAwait(false);
            string target = Child(root, tool.DirectoryName + "-" + tool.Sha256[..12]);
            string provenanceFile = Path.Combine(target, ".temhalo-provenance.json");
            if (!Directory.Exists(target))
            {
                string staging = Child(root, "staging-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(staging);
                try
                {
                    var files = new Dictionary<string, string>();
                    using var zip = ZipFile.OpenRead(archive);
                    long expanded = 0;
                    if (zip.Entries.Count > 10000) throw new InvalidDataException("Trop d'entrées dans l'archive native.");
                    foreach (var entry in zip.Entries)
                    {
                        token.ThrowIfCancellationRequested();
                        string name = entry.FullName.Replace('\\', '/');
                        if (name.StartsWith('/') || name.Contains(':') || ((entry.ExternalAttributes >> 16) & 0xF000) == 0xA000)
                            throw new InvalidDataException("Chemin natif non autorisé.");
                        if (tool.ArchiveRoot.Length > 0)
                        {
                            if (name == tool.ArchiveRoot + "/") continue;
                            if (!name.StartsWith(tool.ArchiveRoot + "/", StringComparison.Ordinal)) throw new InvalidDataException("Racine native inattendue.");
                            name = name[(tool.ArchiveRoot.Length + 1)..];
                        }
                        if (name.Length == 0) continue;
                        string destination = Child(staging, name);
                        if (name.EndsWith('/')) { Directory.CreateDirectory(destination); continue; }
                        expanded += entry.Length;
                        if (expanded > 1024L * 1048576) throw new InvalidDataException("Archive native décompressée trop volumineuse.");
                        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                        await using (var source = entry.Open())
                        await using (var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, true))
                            await source.CopyToAsync(output, token).ConfigureAwait(false);
                        files.Add(name, await HashAsync(destination, token).ConfigureAwait(false));
                    }
                    if (!File.Exists(Child(staging, tool.Executable))) throw new InvalidDataException("Exécutable absent de l'archive native.");
                    await File.WriteAllTextAsync(Path.Combine(staging, ".temhalo-provenance.json"),
                        JsonSerializer.Serialize(new Provenance(tool.Sha256, files)), token).ConfigureAwait(false);
                    Directory.Move(staging, target);
                }
                finally { if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true); }
            }
            var provenance = JsonSerializer.Deserialize<Provenance>(await File.ReadAllTextAsync(provenanceFile, token).ConfigureAwait(false));
            if (provenance is null || provenance.ArchiveSha256 != tool.Sha256 || provenance.Files.Count == 0 || !provenance.Files.ContainsKey(tool.Executable))
                throw new InvalidDataException("Installation native incomplète. Consultez le guide de réparation du cache.");
            foreach (var file in provenance.Files) await VerifyAsync(Child(target, file.Key), file.Value, token).ConfigureAwait(false);
            results.Add(tool.Name, Child(target, tool.Executable));
        }
        progress?.Report("Outils natifs vérifiés. Préparation du relais…");
        return new(results["MediaMTX"], results["FFmpeg"]);
    }
    private static bool IsAllowedSource(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == "https" && uri.Host == "github.com";

    /// <summary>Tries each mirror until one serves the exact pinned archive; a dead or altered source only moves to the next.</summary>
    private static async Task DownloadAsync(HttpClient http, Tool tool, string[] sources, string archive, string root,
        IProgress<string>? progress, CancellationToken token)
    {
        var failures = new List<string>();
        for (int index = 0; index < sources.Length; index++)
        {
            var uri = new Uri(sources[index]);
            progress?.Report(index == 0
                ? $"Téléchargement de {tool.Name} {tool.Version} depuis GitHub…"
                : $"Téléchargement de {tool.Name} {tool.Version} depuis une source de secours ({index + 1}/{sources.Length})…");
            string partial = Child(root, tool.ArchiveName + ".partial-" + Guid.NewGuid().ToString("N"));
            try
            {
                using var response = await http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();
                await using (var output = new FileStream(partial, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, true))
                {
                    await using var input = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
                    var block = new byte[65536]; long length = 0; int count;
                    while ((count = await input.ReadAsync(block, token).ConfigureAwait(false)) != 0)
                    {
                        length += count;
                        if (length > 512L * 1048576) throw new InvalidDataException("Archive native trop volumineuse.");
                        await output.WriteAsync(block.AsMemory(0, count), token).ConfigureAwait(false);
                    }
                }
                await VerifyAsync(partial, tool.Sha256, token).ConfigureAwait(false);
                File.Move(partial, archive);
                return;
            }
            catch (Exception error) when (error is HttpRequestException or InvalidDataException or IOException ||
                error is TaskCanceledException && !token.IsCancellationRequested)
            {
                failures.Add($"{uri.AbsolutePath}: {error.Message}");
                if (index == sources.Length - 1)
                {
                    string message = $"{tool.Name} n'a pu être téléchargé depuis aucune source ({string.Join(" ; ", failures)}). " +
                        $"Vérifiez la connexion, ou placez manuellement {tool.ArchiveName} dans {root} puis relancez le relais.";
                    throw error is InvalidDataException ? new InvalidDataException(message, error) : new HttpRequestException(message, error);
                }
            }
            finally { File.Delete(partial); }
        }
    }
    private static string Child(string root, string relative)
    {
        string prefix = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        string path = Path.GetFullPath(Path.Combine(root, relative));
        if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Un chemin sort du cache natif.");
        return path;
    }
    private static async Task<string> HashAsync(string path, CancellationToken token)
    {
        await using var input = File.OpenRead(path);
        return Convert.ToHexString(await SHA256.HashDataAsync(input, token).ConfigureAwait(false));
    }
    private static async Task VerifyAsync(string path, string expected, CancellationToken token)
    {
        if (!string.Equals(await HashAsync(path, token).ConfigureAwait(false), expected, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Échec de vérification SHA-256. Aucun outil altéré ne sera exécuté.");
    }
    private static async Task<FileStream> AcquireLockAsync(string path, CancellationToken token)
    {
        for (int attempt = 0; ; attempt++)
        {
            token.ThrowIfCancellationRequested();
            try { return new(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException) when (attempt < 240) { await Task.Delay(250, token).ConfigureAwait(false); }
        }
    }
}
