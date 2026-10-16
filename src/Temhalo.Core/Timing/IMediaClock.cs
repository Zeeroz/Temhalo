using System.Diagnostics;

namespace Temhalo.Core.Timing;

public interface IMediaClock
{
    long NowMilliseconds { get; }
    Task DelayUntilAsync(long deadlineMilliseconds, CancellationToken cancellationToken);
}

public sealed class MonotonicMediaClock : IMediaClock
{
    private readonly long _origin = Stopwatch.GetTimestamp();
    public long NowMilliseconds => Stopwatch.GetElapsedTime(_origin).Ticks / TimeSpan.TicksPerMillisecond;

    public async Task DelayUntilAsync(long deadlineMilliseconds, CancellationToken cancellationToken)
    {
        while (deadlineMilliseconds - NowMilliseconds is > 0 and var remaining)
            await Task.Delay(TimeSpan.FromMilliseconds(Math.Min(remaining, 1000)), cancellationToken).ConfigureAwait(false);
    }
}
