using Temhalo.Infrastructure.Windows.Logging;
using Xunit;

namespace Temhalo.Infrastructure.Tests;

public sealed class NativeLogSanitizerTests
{
    [Theory]
    [InlineData("Cannot open rtmps://ingest.example/app/private-key", "private-key")]
    [InlineData("HTTP error https://example.test/?token=abc123", "abc123")]
    [InlineData("stream_key=abc123 rejected", "abc123")]
    [InlineData("password:abc123", "abc123")]
    public void RemovesCredentialsBeforeLogging(string nativeMessage, string secret)
        => Assert.DoesNotContain(secret, NativeLogSanitizer.Sanitize(nativeMessage), StringComparison.Ordinal);

    [Fact]
    public void KeepsUsefulErrors()
        => Assert.Equal("Broken pipe", NativeLogSanitizer.Sanitize("Broken pipe"));
}
