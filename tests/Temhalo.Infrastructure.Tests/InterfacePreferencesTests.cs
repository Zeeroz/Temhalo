using System.Text;
using Temhalo.Infrastructure.Windows.Settings;
using Xunit;

namespace Temhalo.Infrastructure.Tests;

public sealed class InterfacePreferencesTests
{
    [Fact]
    public async Task OptionalExtensionPreservesLegacyDocumentAndEncryptsPerStylePreferences()
    {
        string directory = Path.Combine(Path.GetTempPath(), "Temhalo-interface-" + Guid.NewGuid().ToString("N"));
        var store = new DpapiSettingsStore(directory);
        try
        {
            await store.SaveAsync(new AppSettings { MemoryLimitMiB = 768 });
            var legacy = await store.LoadAsync();
            Assert.Null(legacy.Interface);
            var preferences = new InterfacePreferences { Presets = [0, 15, 30, 45, 60], OverlayStyle = 3 }
                .WithAppearance(0, new OverlayAppearance { Label = "Signature canari", Accent = "#B9B2FF" })
                .WithAppearance(3, new OverlayAppearance { Position = 2, ScalePercent = 125, Label = "Chrono canari", VisibleWhenLive = false });
            await store.SaveAsync(legacy with { Interface = preferences });
            var loaded = await store.LoadAsync();
            Assert.Equal(768, loaded.MemoryLimitMiB);
            Assert.Equal(preferences.Presets, loaded.Interface!.Presets);
            Assert.Equal(3, loaded.Interface.OverlayStyle);
            for (int style = 0; style < 4; style++)
                Assert.Equal(preferences.Appearance(style), loaded.Interface.Appearance(style));
            byte[] encrypted = await File.ReadAllBytesAsync(store.FilePath);
            Assert.True(encrypted.AsSpan().IndexOf(Encoding.UTF8.GetBytes("Chrono canari")) < 0);
        }
        finally
        {
            if (File.Exists(store.FilePath)) File.Delete(store.FilePath);
            if (Directory.Exists(directory)) Directory.Delete(directory);
        }
    }

    [Fact]
    public void InvalidPresetsAndOverlayValuesAreRejectedBeforeSaving()
    {
        foreach (var presets in new[] { new[] { 15, 30 }, new[] { 0, 15, 15 }, new[] { 0, 301 }, Array.Empty<int>() })
            Assert.Throws<ArgumentException>(() => (new InterfacePreferences { Presets = presets }).Validate());
        foreach (string accent in new[] { "#ZZZZZZ", "red", "#1234567" })
            Assert.Throws<ArgumentException>(() => (new OverlayAppearance { Accent = accent }).Validate());
        Assert.Throws<ArgumentException>(() => (new OverlayAppearance { Label = "ligne\ninterdite" }).Validate());
        Assert.Throws<ArgumentException>(() => (new OverlayAppearance { ScalePercent = 151 }).Validate());
    }
}
