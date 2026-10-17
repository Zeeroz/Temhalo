using System.Text;
using Temhalo.Infrastructure.Windows.Logging;
using Temhalo.Infrastructure.Windows.Settings;
using Xunit;

namespace Temhalo.Infrastructure.Tests;

public sealed class SettingsTests
{
    [Fact]
    public async Task DpapiRoundTripNeverWritesPlaintextAndForgettingKeyPreservesOtherSettings()
    {
        string folder = Path.Combine(Path.GetTempPath(), "Temhalo-tests-" + Guid.NewGuid().ToString("N"));
        var store = new DpapiSettingsStore(folder);
        string canary = "fake-key-" + Guid.NewGuid().ToString("N");
        try
        {
            Assert.Equal("", (await store.LoadAsync()).StreamKey);
            var settings = new AppSettings { OutputMode = 1, ServerUrl = "rtmp://127.0.0.1:1936/live", StreamKey = canary, MemoryLimitMiB = 768 };
            await store.SaveAsync(settings);
            byte[] encrypted = await File.ReadAllBytesAsync(store.FilePath);
            Assert.False(encrypted.AsSpan().IndexOf(Encoding.UTF8.GetBytes(canary)) >= 0);
            Assert.False(encrypted.AsSpan().IndexOf(Encoding.UTF8.GetBytes(settings.ServerUrl)) >= 0);
            var loaded = await store.LoadAsync();
            Assert.Equal(settings, loaded);
            Assert.DoesNotContain(canary, loaded.ToString());
            await store.SaveAsync(loaded with { StreamKey = "" });
            var forgotten = await store.LoadAsync();
            Assert.Equal("", forgotten.StreamKey); Assert.Equal(768, forgotten.MemoryLimitMiB);
            Assert.Equal(settings.Hotkeys, forgotten.Hotkeys);
            Assert.Single(Directory.GetFiles(folder));
        }
        finally { if (File.Exists(store.FilePath)) File.Delete(store.FilePath); if (Directory.Exists(folder)) Directory.Delete(folder); }
    }

    [Fact]
    public async Task CorruptedCiphertextIsReportedWithoutOverwritingIt()
    {
        string folder = Path.Combine(Path.GetTempPath(), "Temhalo-tests-" + Guid.NewGuid().ToString("N"));
        var store = new DpapiSettingsStore(folder);
        try
        {
            await store.SaveAsync(new());
            byte[] damaged = await File.ReadAllBytesAsync(store.FilePath); damaged[^1] ^= 0x7F;
            await File.WriteAllBytesAsync(store.FilePath, damaged);
            var error = await Assert.ThrowsAsync<SettingsStoreException>(() => store.LoadAsync());
            Assert.Contains("conservée", error.Message);
            Assert.Equal(damaged, await File.ReadAllBytesAsync(store.FilePath));
        }
        finally { if (File.Exists(store.FilePath)) File.Delete(store.FilePath); if (Directory.Exists(folder)) Directory.Delete(folder); }
    }

    [Fact]
    public async Task LegacyTwitchPresetUpgradesWithoutChangingKeysOrOtherDestinations()
    {
        string folder = Path.Combine(Path.GetTempPath(), "Temhalo-ingest-" + Guid.NewGuid().ToString("N"));
        var store = new DpapiSettingsStore(folder);
        const string legacy = "rtmp://live-cdg.twitch.tv/app";
        const string automatic = "rtmp://ingest.global-contribute.live-video.net/app";
        var twitch = new DestinationSettings { Provider = 0, Name = "Twitch", ServerUrl = legacy, StreamKey = "fake-twitch-key" };
        var custom = new DestinationSettings { Provider = 3, Name = "Custom", ServerUrl = legacy, StreamKey = "fake-custom-key" };
        var otherServer = new DestinationSettings { Provider = 0, Name = "Other", ServerUrl = "rtmp://euc10.contribute.live-video.net/app", StreamKey = "fake-other-key" };
        try
        {
            await store.SaveAsync(new AppSettings { Provider = 0, ServerUrl = legacy, StreamKey = "fake-root-key", Destinations = [twitch, custom, otherServer] });
            byte[] original = await File.ReadAllBytesAsync(store.FilePath);
            var upgraded = await store.LoadAsync();
            Assert.Equal(automatic, upgraded.ServerUrl); Assert.Equal("fake-root-key", upgraded.StreamKey);
            Assert.Equal(twitch with { ServerUrl = automatic }, upgraded.Destinations![0]);
            Assert.Equal(custom, upgraded.Destinations[1]); Assert.Equal(otherServer, upgraded.Destinations[2]);
            Assert.Equal(original, await File.ReadAllBytesAsync(store.FilePath)); // Loading never rewrites credentials.
            await store.SaveAsync(upgraded);
            Assert.Equal(upgraded, await store.LoadAsync() with { Destinations = upgraded.Destinations });
        }
        finally
        {
            if (File.Exists(store.FilePath)) File.Delete(store.FilePath);
            if (Directory.Exists(folder)) Directory.Delete(folder);
        }
    }

    [Fact]
    public void SecretsAreRemovedEvenOutsideUrlOrNamedCredentialFields()
    {
        var redactor = new SecretRedactor();
        string key = "test-canary/" + Guid.NewGuid().ToString("N");
        redactor.Remember(key);
        string clean = redactor.Redact("failure: " + key + " rtmp://127.0.0.1/app/" + key);
        Assert.DoesNotContain(key, clean); Assert.DoesNotContain("rtmp://", clean);
    }

    [Fact]
    public async Task RetiredFeaturesCannotBlockOrReactivateAfterUpgradeAndAccountsArePreserved()
    {
        string folder = Path.Combine(Path.GetTempPath(), "Temhalo-upgrade-" + Guid.NewGuid().ToString("N"));
        var store = new DpapiSettingsStore(folder);
        var destination = new DestinationSettings { Name = "Destination conservée", Provider = 3,
            ServerUrl = "rtmps://example.test/live", StreamKey = "test-only-key" };
        var old = new AppSettings { OutputMode = 0, ObsDockEnabled = true, Destinations = [destination],
            Hotkeys = new() { Enabled = true, Increase = "legacy-invalid-key" },
            Interface = new() { Presets = [-1] }, LocalAccess = new() { Port = -1 } };
        try
        {
            await store.SaveAsync(old);
            byte[] original = await File.ReadAllBytesAsync(store.FilePath);
            var upgraded = (await store.LoadAsync()).ForDesktop();
            Assert.Equal(1, upgraded.OutputMode); Assert.False(upgraded.ObsDockEnabled);
            Assert.False(upgraded.Hotkeys.Enabled);
            Assert.Equal(destination, Assert.Single(upgraded.Destinations!));
            Assert.Equal(original, await File.ReadAllBytesAsync(store.FilePath));
            await store.SaveAsync(upgraded);
            Assert.Equal(destination, Assert.Single((await store.LoadAsync()).Destinations!));
        }
        finally { File.Delete(store.FilePath); Directory.Delete(folder); }
    }
}
