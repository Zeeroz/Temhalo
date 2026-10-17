using Temhalo.Core.Timing;

namespace Temhalo.Core.Buffering;

/// <summary>Stage 2 compatibility adapter: no externally supplied controller, hence a fixed target.</summary>
public sealed class FixedDelayMediaTransfer(FixedDelayOptions options, IMediaClock? clock = null)
    : FlvDelayMediaTransfer(options with { ReplayOnIncrease = false }, clock);
