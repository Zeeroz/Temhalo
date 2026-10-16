using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;

namespace Temhalo.Infrastructure.Windows.Deployment;

public sealed record UpdateRelease(ReleaseVersion Version, Uri DownloadUrl, Uri ReleaseUrl, long Bytes, string Sha256)
{
    public string FileName => $"Temhalo-{Version}-Setup-x64.exe";
}
public sealed record DownloadedUpdate(UpdateRelease Release, string FilePath);

/// <summary>Reads public GitHub Releases; never sends account tokens or stream settings.</summary>
public sealed class GitHubUpdateService(HttpClient http)
{
    public const string Repository = "https://github.com/Zeeroz/Temhalo";
    public const long MaximumInstallerBytes = 800L * 1024 * 1024;
    private const int MaximumMetadataBytes = 4 * 1024 * 1024;

    public async Task<UpdateRelease?> CheckAsync(ReleaseVersion current, bool includePreviews, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(25));
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.github.com/repos/Zeeroz/Temhalo/releases?per_page=100");
        request.Headers.UserAgent.ParseAdd("Temhalo/" + current);
        request.Headers.Accept.ParseAdd("application/vnd.github+json");
        request.Headers.Add("X-GitHub-Api-Version", "2026-03-10");
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        using var content = new MemoryStream();
        await CopyBoundedAsync(await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false), content,
            MaximumMetadataBytes, null, timeout.Token).ConfigureAwait(false);
        using var document = JsonDocument.Parse(content.ToArray(), new JsonDocumentOptions { MaxDepth = 32 });
        return SelectRelease(document.RootElement, current, includePreviews);
    }

    internal static UpdateRelease? SelectRelease(JsonElement releases, ReleaseVersion current, bool includePreviews)
    {
        if (releases.ValueKind != JsonValueKind.Array) throw new InvalidDataException("Réponse GitHub invalide.");
        UpdateRelease? selected = null;
        foreach (var release in releases.EnumerateArray())
        {
            if (release.ValueKind != JsonValueKind.Object || !release.TryGetProperty("draft", out var draft) || draft.ValueKind != JsonValueKind.False ||
                !release.TryGetProperty("tag_name", out var tag) || tag.ValueKind != JsonValueKind.String ||
                !ReleaseVersion.TryParse(tag.GetString(), out var version) || tag.GetString() != "v" + version || version.CompareTo(current) <= 0 ||
                version.Preview is not null && !includePreviews ||
                !release.TryGetProperty("prerelease", out var preview) || preview.ValueKind != (version.Preview is null ? JsonValueKind.False : JsonValueKind.True) ||
                !release.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array) continue;
            string filename = $"Temhalo-{version}-Setup-x64.exe";
            string expectedUrl = $"{Repository}/releases/download/v{version}/{filename}";
            foreach (var asset in assets.EnumerateArray())
            {
                if (asset.ValueKind != JsonValueKind.Object || !StringIs(asset, "name", filename) || !StringIs(asset, "state", "uploaded") ||
                    !StringIs(asset, "browser_download_url", expectedUrl) ||
                    !asset.TryGetProperty("size", out var size) || size.ValueKind != JsonValueKind.Number || !size.TryGetInt64(out long bytes) || bytes is <= 0 or > MaximumInstallerBytes ||
                    !asset.TryGetProperty("digest", out var digest) || digest.ValueKind != JsonValueKind.String) continue;
                string hash = digest.GetString()!;
                if (!hash.StartsWith("sha256:", StringComparison.Ordinal) || hash.Length != 71 || !hash.AsSpan(7).ContainsOnlyHex()) continue;
                var candidate = new UpdateRelease(version, new Uri(expectedUrl), new Uri($"{Repository}/releases/tag/v{version}"), bytes, hash[7..].ToLowerInvariant());
                if (selected is null || version.CompareTo(selected.Version) > 0) selected = candidate;
            }
        }
        return selected;
    }

    public async Task<DownloadedUpdate> DownloadAsync(UpdateRelease release, string dataDirectory, IProgress<int>? progress, CancellationToken cancellationToken)
    {
        ValidateRelease(release);
        string directory = Path.Combine(Path.GetFullPath(dataDirectory), "updates", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string partial = Path.Combine(directory, "installer.partial");
        string destination = Path.Combine(directory, release.FileName);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(15));
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, release.DownloadUrl);
            request.Headers.UserAgent.ParseAdd("Temhalo-Updater/1.0");
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            if (response.RequestMessage?.RequestUri?.Scheme != Uri.UriSchemeHttps ||
                response.Content.Headers.ContentLength is { } length && length != release.Bytes)
                throw new InvalidDataException("Taille ou transport de la mise à jour invalide.");
            await using (var file = new FileStream(partial, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, FileOptions.Asynchronous))
                await CopyBoundedAsync(await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false), file, release.Bytes, progress, timeout.Token).ConfigureAwait(false);
            await VerifyAsync(new(release, partial), timeout.Token).ConfigureAwait(false);
            File.Move(partial, destination);
            progress?.Report(100);
            return new(release, destination);
        }
        catch
        {
            // Only our exact temporary file is removed. Never recursively delete a data directory.
            try { File.Delete(partial); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            throw;
        }
    }

    public static async Task VerifyAsync(DownloadedUpdate update, CancellationToken cancellationToken)
    {
        ValidateRelease(update.Release);
        await using var file = new FileStream(update.FilePath, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, FileOptions.Asynchronous);
        if (file.Length != update.Release.Bytes) throw new InvalidDataException("La taille de l’installeur ne correspond pas à la version publiée.");
        byte[] hash = await SHA256.HashDataAsync(file, cancellationToken).ConfigureAwait(false);
        if (!CryptographicOperations.FixedTimeEquals(hash, Convert.FromHexString(update.Release.Sha256)))
            throw new InvalidDataException("L’empreinte SHA-256 de l’installeur est incorrecte. Installation annulée.");
    }

    private static void ValidateRelease(UpdateRelease release)
    {
        if (!ReleaseVersion.TryParse(release.Version.ToString(), out _) || release.Bytes is <= 0 or > MaximumInstallerBytes ||
            release.DownloadUrl.AbsoluteUri != $"{Repository}/releases/download/v{release.Version}/{release.FileName}" ||
            release.Sha256.Length != 64 || !release.Sha256.AsSpan().ContainsOnlyHex())
            throw new InvalidDataException("Source de mise à jour non autorisée.");
    }

    private static bool StringIs(JsonElement element, string property, string expected) => element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String && value.GetString() == expected;

    private static async Task CopyBoundedAsync(Stream source, Stream destination, long maximum, IProgress<int>? progress, CancellationToken token)
    {
        await using (source.ConfigureAwait(false))
        {
            byte[] buffer = new byte[65536];
            long total = 0;
            int previous = -1, read;
            while ((read = await source.ReadAsync(buffer, token).ConfigureAwait(false)) != 0)
            {
                total += read;
                if (total > maximum) throw new InvalidDataException("Le téléchargement dépasse la taille autorisée.");
                await destination.WriteAsync(buffer.AsMemory(0, read), token).ConfigureAwait(false);
                int percent = (int)(total * 100 / maximum);
                if (percent != previous) { progress?.Report(percent); previous = percent; }
            }
        }
    }
}

internal static class UpdateHashExtensions
{
    internal static bool ContainsOnlyHex(this ReadOnlySpan<char> value)
    {
        foreach (char item in value) if (!char.IsAsciiHexDigit(item)) return false;
        return true;
    }
}
