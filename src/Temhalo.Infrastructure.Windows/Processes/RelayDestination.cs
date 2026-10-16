namespace Temhalo.Infrastructure.Windows.Processes;

/// <summary>A session-only RTMP destination. Never serialize this object or log its arguments.</summary>
public sealed class RelayDestination
{
    public string Redact(string value) => Logging.NativeLogSanitizer.Sanitize(value.Replace(_key, "[redacted]", StringComparison.Ordinal));
    private readonly string _key;
    public string ServerUrl { get; }
    internal string Key => _key;
    internal string PublishUrl => ServerUrl.TrimEnd('/') + "/" + _key;

    public RelayDestination(string serverUrl, string key)
    {
        if (!Uri.TryCreate(serverUrl, UriKind.Absolute, out var uri) || uri.Scheme is not ("rtmp" or "rtmps") ||
            string.IsNullOrWhiteSpace(uri.Host) || !string.IsNullOrEmpty(uri.UserInfo) ||
            !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment) ||
            serverUrl.Any(char.IsWhiteSpace))
            throw new ArgumentException("Saisissez une URL RTMP ou RTMPS sans identifiant, clé, paramètres ni fragment.");
        if (string.IsNullOrWhiteSpace(key) || key.Any(char.IsWhiteSpace) || key.Any(char.IsControl))
            throw new ArgumentException("La clé de stream est vide ou contient des espaces interdits.");
        ServerUrl = serverUrl.TrimEnd('/');
        _key = key;
    }

    public override string ToString() => "Destination RTMP [masquée]";
}
