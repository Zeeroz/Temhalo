using System.Text;
using System.Text.Json;
using System.Security.Cryptography;
using Temhalo.Infrastructure.Windows.Settings;
using Xunit;

namespace Temhalo.Infrastructure.Tests;

public sealed class InterfacePreferencesTests
{
    [Fact]
    public async Task OptionalPreferencesPreserveLegacyDocumentAndEncryptPresets()
    {
        string directory = Path.Combine(Path.GetTempPath(), "Temhalo-interface-" + Guid.NewGuid().ToString("N"));
        var store = new DpapiSettingsStore(directory);
        try
        {
            await store.SaveAsync(new AppSettings { MemoryLimitMiB = 768 });
            var legacy = await store.LoadAsync();
            Assert.Null(legacy.Interface);
            var preferences = new InterfacePreferences { Presets = [0, 15, 30, 45, 60] };
            await store.SaveAsync(legacy with { Interface = preferences });
            var loaded = await store.LoadAsync();
            Assert.Equal(768, loaded.MemoryLimitMiB);
            Assert.Equal(preferences.Presets, loaded.Interface!.Presets);
            byte[] encrypted = await File.ReadAllBytesAsync(store.FilePath);
            Assert.True(encrypted.AsSpan().IndexOf(Encoding.UTF8.GetBytes("Presets")) < 0);
        }
        finally
        {
            if (File.Exists(store.FilePath)) File.Delete(store.FilePath);
            if (Directory.Exists(directory)) Directory.Delete(directory);
        }
    }

    [Fact]
    public void InvalidPresetsAreRejectedBeforeSaving()
    {
        foreach (var presets in new[] { new[] { 15, 30 }, new[] { 0, 15, 15 }, new[] { 0, 301 }, Array.Empty<int>() })
            Assert.Throws<ArgumentException>(() => (new InterfacePreferences { Presets = presets }).Validate());
    }

    [Fact]
    public async Task RemovingOverlayReadsTheOldEncryptedFormatWithoutLosingKeysOrPresets()
    {
        string directory = Path.Combine(Path.GetTempPath(), "Temhalo-migration-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory); var store = new DpapiSettingsStore(directory);
        byte[] legacy = Encoding.UTF8.GetBytes("{\"Version\":1,\"MemoryLimitMiB\":768,\"ServerUrl\":\"rtmp://127.0.0.1:1936/live\",\"StreamKey\":\"legacy-canary\",\"Interface\":{\"Presets\":[0,12,24,48],\"OverlayStyle\":3,\"Signature\":{\"Label\":\"old overlay\"}}}");
        try
        {
            byte[] encrypted = ProtectedData.Protect(legacy, SHA256.HashData(Encoding.UTF8.GetBytes("Temhalo/settings/v1")), DataProtectionScope.CurrentUser);
            await File.WriteAllBytesAsync(store.FilePath, "TMH1"u8.ToArray().Concat(encrypted).ToArray());
            var settings = await store.LoadAsync();
            Assert.Equal("legacy-canary", settings.StreamKey); Assert.Equal(768, settings.MemoryLimitMiB);
            Assert.Equal(new[] { 0, 12, 24, 48 }, settings.Interface!.Presets);
            Assert.False(settings.ObsDockEnabled);
            await store.SaveAsync(settings); Assert.Equal(settings.StreamKey, (await store.LoadAsync()).StreamKey);
            using var json = JsonDocument.Parse(JsonSerializer.SerializeToUtf8Bytes(settings));
            Assert.False(json.RootElement.GetProperty("Interface").TryGetProperty("OverlayStyle", out _));
        }
        finally { File.Delete(store.FilePath); Directory.Delete(directory); CryptographicOperations.ZeroMemory(legacy); }
    }
}
