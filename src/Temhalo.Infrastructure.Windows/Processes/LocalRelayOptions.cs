using Temhalo.Core.Buffering;

namespace Temhalo.Infrastructure.Windows.Processes;

public sealed record LocalRelayOptions(string MediaMtxExecutable, string FfmpegExecutable,
    string MediaMtxConfiguration, string OutputDirectory)
{
    public const string IngestUrl = "rtmp://127.0.0.1:1935/live/stream";
    public TimeSpan RetryMinimum { get; init; } = TimeSpan.FromSeconds(1);
    public TimeSpan RetryMaximum { get; init; } = TimeSpan.FromSeconds(10);
    public FixedDelayOptions FixedDelay { get; init; } = new();
    public RelayDestination? Destination { get; init; }
    public IReadOnlyList<RelayOutput>? Outputs { get; init; }
    public TimeSpan OutputWriteTimeout { get; init; } = TimeSpan.FromSeconds(15);
    public TimeSpan ShutdownTimeout { get; init; } = TimeSpan.FromSeconds(320);
}

public sealed record LocalRelaySnapshot(string State, int? MediaMtxPid, int? ReaderPid, int? EmitterPid,
    int Attempt, long BytesTransferred, string? OutputFile, string? LastEvent, FixedDelaySnapshot? Delay = null,
    bool IngestConnected = false, bool OutputReady = false, IReadOnlyList<RelayOutputSnapshot>? Outputs = null);

public sealed record RelayOutput(string Id, string Name, RelayDestination? Destination = null);
public sealed record RelayOutputSnapshot(string Id, string Name, int? Pid, int Attempt, bool Ready,
    bool Reconnecting, long BytesWritten, long QueuedBytes, long SkippedTags, long ExtraDelayMilliseconds, string State);
