using Temhalo.Infrastructure.Windows.Accounts;
using Xunit;

namespace Temhalo.Infrastructure.Tests;

public sealed class OAuthClientConfigurationTests
{
    [Fact]
    public void FreshInstallCanStartTwitchConnectionWithoutAPersonalConfigurationFile()
    {
        string folder = Path.Combine(Path.GetTempPath(), "Temhalo-oauth-" + Guid.NewGuid().ToString("N"));
        var configuration = OAuthClientConfiguration.Load(folder);
        configuration.Require(StreamingProvider.Twitch);
        Assert.Equal(OAuthClientConfiguration.PublicTwitchClientId, configuration.TwitchClientId);
        Assert.Empty(configuration.YouTubeDesktopClientSecret);
        Assert.False(Directory.Exists(folder)); // Loading creates no credentials or files.
        Assert.Throws<AccountConnectionException>(() => configuration.Require(StreamingProvider.YouTube));
        Assert.Throws<AccountConnectionException>(() => configuration.Require(StreamingProvider.Kick));
    }

    [Fact]
    public void ExplicitClientOverridesArePreservedWhileOmittedTwitchUsesPublicDefault()
    {
        string folder = Path.Combine(Path.GetTempPath(), "Temhalo-oauth-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder); string path = Path.Combine(folder, "oauth-clients.json");
        try
        {
            File.WriteAllText(path, """{"YouTubeClientId":"custom-google"}""");
            var configuration = OAuthClientConfiguration.Load(folder);
            Assert.Equal("custom-google", configuration.YouTubeClientId);
            Assert.Equal(OAuthClientConfiguration.PublicTwitchClientId, configuration.TwitchClientId);
            File.WriteAllText(path, """{"TwitchClientId":"custom-twitch"}""");
            Assert.Equal("custom-twitch", OAuthClientConfiguration.Load(folder).TwitchClientId);
            File.WriteAllText(path, """{"TwitchClientId":""}""");
            Assert.Throws<AccountConnectionException>(() => OAuthClientConfiguration.Load(folder).Require(StreamingProvider.Twitch));
        }
        finally { File.Delete(path); Directory.Delete(folder); }
    }
}
