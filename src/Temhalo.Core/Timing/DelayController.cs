namespace Temhalo.Core.Timing;

public sealed record DelayRequest(long Version, int TargetSeconds);

/// <summary>Bounded latest-value mailbox. UI and adapters never mutate the media queue.</summary>
public sealed class DelayController
{
    private DelayRequest _request;
    private TaskCompletionSource _changed = NewSignal();
    private static TaskCompletionSource NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    public DelayController(int initialSeconds = 0)
    {
        Validate(initialSeconds);
        _request = new(0, initialSeconds);
    }
    public DelayRequest Request => Volatile.Read(ref _request);
    internal Task Changed => Volatile.Read(ref _changed).Task;

    public DelayRequest SetTarget(int seconds)
    {
        Validate(seconds);
        while (true)
        {
            var observed = Request;
            var next = new DelayRequest(checked(observed.Version + 1), seconds);
            if (Interlocked.CompareExchange(ref _request, next, observed) != observed) continue;
            Interlocked.Exchange(ref _changed, NewSignal()).TrySetResult();
            return next;
        }
    }
    private static void Validate(int seconds)
    {
        if (seconds is < 0 or > 300) throw new ArgumentOutOfRangeException(nameof(seconds), "Délai attendu : 0..300 s.");
    }
}
