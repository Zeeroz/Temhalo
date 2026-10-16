using System.Text.Json;

namespace Temhalo.Infrastructure.Windows.Processes;

/// <summary>Tracks the publisher connection from the pinned MediaMTX JSON diagnostics.</summary>
internal sealed class PublisherTracker
{
    private string? _publisher;
    public bool Connected => Volatile.Read(ref _publisher) is not null;
    public void Reset() => Volatile.Write(ref _publisher, null);
    public void Observe(string diagnostic)
    {
        try
        {
            using var json = JsonDocument.Parse(diagnostic);
            if (!json.RootElement.TryGetProperty("message", out var value)) return;
            string text = value.GetString() ?? "";
            int end = text.IndexOf(']', "[RTMP] [conn ".Length);
            if (!text.StartsWith("[RTMP] [conn ", StringComparison.Ordinal) || end < 0) return;
            string connection = text[..(end + 1)];
            if (text.Contains("is publishing to path 'live/stream'", StringComparison.Ordinal))
                Volatile.Write(ref _publisher, connection);
            else if (text.Contains("] closed:", StringComparison.Ordinal) && connection == Volatile.Read(ref _publisher)) Reset();
        }
        catch (JsonException) { } // Non-JSON startup diagnostics cannot establish connectivity.
    }
}
