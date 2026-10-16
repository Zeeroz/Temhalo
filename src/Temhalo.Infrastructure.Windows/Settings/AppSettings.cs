using Temhalo.Infrastructure.Windows.Hotkeys;
using Temhalo.Infrastructure.Windows.Processes;
using Temhalo.Infrastructure.Windows.Accounts;

namespace Temhalo.Infrastructure.Windows.Settings;

public sealed record AppSettings
{
    public int Version { get; init; } = 1;
    public int OutputMode { get; init; }
    public int Provider { get; init; }
    public string ServerUrl { get; init; } = "";
    public string StreamKey { get; init; } = "";
    public int MemoryLimitMiB { get; init; } = 512;
    public HotkeySettings Hotkeys { get; init; } = new();
    public InterfacePreferences? Interface { get; init; }
    public DestinationSettings[]? Destinations { get; init; }
    public bool ObsDockEnabled { get; init; }
    public LocalAccessSettings? LocalAccess { get; init; }
    public bool ObsInstructionsAcknowledged { get; init; }
    public bool CheckUpdatesOnStartup { get; init; } = true;
    public bool IncludePreviewUpdates { get; init; } = true;

    // Upgrade only Temhalo's old Twitch preset. Explicit custom-platform destinations and
    // other Twitch servers retain their chosen address; keys/tokens remain unchanged.
    internal AppSettings UpgradeLegacyTwitchIngest()
    {
        static string Upgrade(int provider, string server) => provider == 0 &&
            server.TrimEnd('/').Equals("rtmp://live-cdg.twitch.tv/app", StringComparison.OrdinalIgnoreCase)
                ? StreamingAccountService.TwitchIngestUrl : server;
        return this with
        {
            ServerUrl = Upgrade(Provider, ServerUrl),
            Destinations = Destinations?.Select(destination => destination with
                { ServerUrl = Upgrade(destination.Provider, destination.ServerUrl) }).ToArray()
        };
    }

    public void Validate()
    {
        if (Version != 1 || OutputMode is < 0 or > 1 || Provider is < 0 or > 3 || MemoryLimitMiB is < 1 or > 4096 ||
            ServerUrl is null || StreamKey is null || ServerUrl.Length > 2048 || StreamKey.Length > 4096 || Hotkeys is null)
            throw new ArgumentException("Configuration Temhalo invalide.");
        if (ServerUrl.Length > 0) _ = new RelayDestination(ServerUrl, StreamKey.Length > 0 ? StreamKey : "validation");
        else if (StreamKey.Length > 0) throw new ArgumentException("Renseignez le serveur avant d'enregistrer une clé.");
        Hotkeys.Validate();
        Interface?.Validate();
        LocalAccess?.Validate();
        if (Destinations is { } destinations)
        {
            if (destinations.Length > 4 || destinations.Any(destination => destination is null) ||
                destinations.Select(destination => destination.Id).Distinct().Count() != destinations.Length)
                throw new ArgumentException("Conservez au maximum quatre destinations distinctes.");
            foreach (var destination in destinations) destination.Validate();
        }
    }

    public override string ToString() => "Configuration Temhalo [secrets masqués]";
}

public sealed record HotkeySettings
{
    public bool Enabled { get; init; } = true;
    public int ProtectionSeconds { get; init; } = 300;
    public string Protection { get; init; } = "Ctrl+Alt+F6";
    public string Zero { get; init; } = "Ctrl+Alt+F7";
    public string Increase { get; init; } = "Ctrl+Alt+F8";
    public string Decrease { get; init; } = "Ctrl+Alt+F9";

    public IReadOnlyDictionary<HotkeyAction, HotkeyGesture> Gestures() => new Dictionary<HotkeyAction, HotkeyGesture>
    {
        [HotkeyAction.Protection] = HotkeyGesture.Parse(Protection),
        [HotkeyAction.Zero] = HotkeyGesture.Parse(Zero),
        [HotkeyAction.Increase] = HotkeyGesture.Parse(Increase),
        [HotkeyAction.Decrease] = HotkeyGesture.Parse(Decrease)
    };
    public void Validate()
    {
        if (ProtectionSeconds is < 0 or > 300) throw new ArgumentException("Le preset de protection doit être compris entre 0 et 300 s.");
        if (Gestures().Values.Distinct().Count() != 4) throw new ArgumentException("Chaque action doit avoir un raccourci différent.");
    }
}
