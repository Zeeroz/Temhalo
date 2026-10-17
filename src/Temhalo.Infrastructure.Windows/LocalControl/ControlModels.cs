namespace Temhalo.Infrastructure.Windows.LocalControl;

// Shared with the Stream Deck pipe protocol. No HTTP server is involved.
public sealed record PublicOutputState(string Id, string Name, bool Ready, bool Reconnecting, long ExtraDelayMilliseconds);
public sealed record LocalViewState(bool Running, bool CanControl, int TargetSeconds, long? CurrentDelayMilliseconds,
    string Phase, bool IngestConnected, int[] Presets, int ProtectionSeconds, PublicOutputState[] Outputs);
public sealed record DelayCommand(string Action, int? Seconds = null);
public sealed record DelayCommandResult(bool Accepted, int TargetSeconds, string Message);
