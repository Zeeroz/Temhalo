namespace Temhalo.Infrastructure.Windows.Logging;

/// <summary>Remember secrets only in RAM, before validation can produce a diagnostic.</summary>
public sealed class SecretRedactor
{
    public static SecretRedactor Shared { get; } = new();
    private string[] _values = [];
    public void Remember(string? value)
    {
        if (string.IsNullOrEmpty(value)) return;
        string[] observed;
        do
        {
            observed = Volatile.Read(ref _values);
            if (observed.Contains(value, StringComparer.Ordinal)) return;
        } while (Interlocked.CompareExchange(ref _values, [.. observed, value], observed) != observed);
    }
    public string Redact(string value)
    {
        foreach (string secret in Volatile.Read(ref _values)) value = value.Replace(secret, "[redacted]", StringComparison.Ordinal);
        return NativeLogSanitizer.Sanitize(value);
    }
}
