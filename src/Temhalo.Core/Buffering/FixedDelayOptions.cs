namespace Temhalo.Core.Buffering;

public sealed record FixedDelayOptions
{
    public int DelaySeconds { get; init; }
    public long MaximumBufferBytes { get; init; } = 512L * 1024 * 1024;
    public double WarningUsageRatio { get; init; } = 0.8;
    public int TagCapacity { get; init; } = 65536;
    public int MaximumTagBytes { get; init; } = 4 * 1024 * 1024;
    public int MaximumPauseStepSeconds { get; init; } = 5;
    public int ResumeBetweenStepsSeconds { get; init; } = 1;
    /// <summary>Keep the last five minutes for an immediate replay when increasing the live delay.</summary>
    public bool ReplayOnIncrease { get; init; } = true;

    internal void Validate()
    {
        if (DelaySeconds is < 0 or > 300 || MaximumBufferBytes < 1024 ||
            !double.IsFinite(WarningUsageRatio) || WarningUsageRatio is <= 0 or >= 1 || TagCapacity is < 2 or > 1_048_576 ||
            MaximumTagBytes is < 1 or > 0xFFFFFF || MaximumPauseStepSeconds is < 1 or > 20 || ResumeBetweenStepsSeconds is < 1 or > 10)
            throw new ArgumentOutOfRangeException(nameof(FixedDelayOptions), "Options du délai fixe invalides.");
    }
}

public sealed record FixedDelaySnapshot(int TargetSeconds, long BufferBytes, long PeakBufferBytes,
    long MaximumBufferBytes, bool BufferWarning, bool WarningObserved, long BufferedTags,
    long ReadTags, long EmittedMediaTags, long SkippedMediaTags, long Discontinuities,
    bool WaitingForKeyframe, bool InputCompleted, bool TruncatedInput,
    long? MinimumMediaAgeMilliseconds, long? LastMediaAgeMilliseconds, long? MaximumMediaAgeMilliseconds,
    Temhalo.Core.Timing.DelayTransitionSnapshot Control, long InputBytes = 0,
    long RecoveryDelayMilliseconds = 0, long OutputReconnections = 0, long InputSessions = 0,
    long HistoryAvailableMilliseconds = 0, long HistoryTags = 0, long HistoryEvictedTags = 0, long ReplayedMediaTags = 0);
