using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Temhalo.Infrastructure.Windows.Settings;

/// <summary>Whole-document DPAPI CurrentUser protection; plaintext never goes through a disk file.</summary>
public sealed class DpapiSettingsStore
{
    private static readonly byte[] Magic = "TMH1"u8.ToArray();
    private static readonly byte[] Entropy = SHA256.HashData(Encoding.UTF8.GetBytes("Temhalo/settings/v1"));
    private readonly SemaphoreSlim _gate = new(1, 1);
    public string FilePath { get; }

    public DpapiSettingsStore(string? directory = null) => FilePath = Path.Combine(directory ??
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Temhalo"), "settings.dpapi");

    public async Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        byte[]? clear = null;
        try
        {
            if (!File.Exists(FilePath)) return new();
            if (new FileInfo(FilePath).Length is < 5 or > 1048576) throw new InvalidDataException();
            byte[] bytes = await File.ReadAllBytesAsync(FilePath, cancellationToken).ConfigureAwait(false);
            if (!bytes.AsSpan(0, 4).SequenceEqual(Magic)) throw new InvalidDataException();
            clear = ProtectedData.Unprotect(bytes[4..], Entropy, DataProtectionScope.CurrentUser);
            var settings = JsonSerializer.Deserialize<AppSettings>(clear) ?? throw new InvalidDataException();
            settings.Validate();
            return settings.UpgradeLegacyTwitchIngest();
        }
        catch (Exception ex) when (ex is CryptographicException or JsonException or InvalidDataException or ArgumentException)
        { throw new SettingsStoreException("La configuration chiffrée est illisible pour ce compte Windows ou a été altérée. Elle a été conservée ; ressaisissez puis enregistrez vos réglages."); }
        finally { if (clear is not null) CryptographicOperations.ZeroMemory(clear); _gate.Release(); }
    }

    public async Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        settings.Validate();
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        byte[]? clear = null;
        string? temporary = null;
        try
        {
            clear = JsonSerializer.SerializeToUtf8Bytes(settings);
            byte[] encrypted = ProtectedData.Protect(clear, Entropy, DataProtectionScope.CurrentUser);
            string directory = Path.GetDirectoryName(FilePath)!;
            Directory.CreateDirectory(directory);
            temporary = Path.Combine(directory, "settings-" + Guid.NewGuid().ToString("N") + ".tmp");
            await using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                4096, FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await file.WriteAsync(Magic, cancellationToken).ConfigureAwait(false);
                await file.WriteAsync(encrypted, cancellationToken).ConfigureAwait(false);
                await file.FlushAsync(cancellationToken).ConfigureAwait(false);
                file.Flush(flushToDisk: true);
            }
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporary, FilePath, overwrite: true);
            temporary = null;
        }
        catch (CryptographicException)
        { throw new SettingsStoreException("Windows n'a pas pu chiffrer la configuration. Aucun secret n'a été enregistré en clair."); }
        finally
        {
            if (clear is not null) CryptographicOperations.ZeroMemory(clear);
            try { if (temporary is not null) File.Delete(temporary); }
            finally { _gate.Release(); }
        }
    }
}

public sealed class SettingsStoreException(string message) : IOException(message);
