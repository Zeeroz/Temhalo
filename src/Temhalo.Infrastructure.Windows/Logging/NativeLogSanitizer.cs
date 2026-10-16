using System.Text.RegularExpressions;

namespace Temhalo.Infrastructure.Windows.Logging;

public static partial class NativeLogSanitizer
{
    // All native diagnostics cross this boundary. Never log process argument lists.
    public static string Sanitize(string value)
    {
        string clean = StreamUrl().Replace(value, "[stream-url]");
        clean = Credential().Replace(clean, "$1=[redacted]");
        return clean.Length <= 4096 ? clean : clean[..4096] + "…";
    }

    [GeneratedRegex(@"(?i)\b(?:rtmps?|https?|srt)://[^\s\""'<>]+")]
    private static partial Regex StreamUrl();

    [GeneratedRegex(@"(?i)\b(stream_?key|password|token|secret)\s*[=:]\s*[^\s,;]+")]
    private static partial Regex Credential();
}
