using Temhalo.Core.Buffering;

namespace Temhalo.Core.Timing;

public sealed record DelayTransitionSnapshot(long AppliedVersion, string Phase, long ActiveDelayMilliseconds,
    bool IsPaused, long? PauseRemainingMilliseconds, long CompletedPauseSteps, long CompletedSeeks,
    long LastPauseMilliseconds, long MaximumPauseMilliseconds, bool TargetReached);

/// <summary>Consumer-owned transition state; only immutable snapshots cross thread boundaries.</summary>
internal sealed class DelayTransitionPlanner
{
    private readonly long _step, _resume;
    private long _active, _target, _version, _pauseStart, _pauseEnd, _pauseBase;
    private long _steps, _seeks, _lastPause, _maxPause, _outputHigh = -1, _resumeOutput, _resumeWall;
    private bool _paused, _hasOutput, _seeking, _cooldown, _captureResume, _ended;
    private sealed record Published(long Version, long Active, long Target, string Phase, bool Paused,
        long PauseStart, long PauseEnd, long Steps, long Seeks, long LastPause, long MaxPause, bool Seeking, bool HasOutput, bool Resuming);
    private Published _published;

    public DelayTransitionPlanner(FixedDelayOptions options, DelayRequest initial)
    {
        _step = options.MaximumPauseStepSeconds * 1000L;
        _resume = options.ResumeBetweenStepsSeconds * 1000L;
        _active = _target = initial.TargetSeconds * 1000L;
        _version = initial.Version;
        _published = Build();
    }
    public long Version => _version;
    public long ScheduledDelay => _active;
    public bool Paused => _paused;
    public long PauseEnd => _pauseEnd;

    public DelayTransitionSnapshot Snapshot(long now)
    {
        var p = Volatile.Read(ref _published);
        long active = p.Paused ? p.Active + Math.Clamp(now - p.PauseStart, 0, p.PauseEnd - p.PauseStart) : p.Active;
        return new(p.Version, p.Phase, active, p.Paused, p.Paused ? Math.Max(0, p.PauseEnd - now) : null,
            p.Steps, p.Seeks, p.LastPause, p.MaxPause, active == p.Target && !p.Paused && !p.Seeking && p.HasOutput && !p.Resuming);
    }

    // True requests a keyframe seek. A lower TARGET can still mean increasing the actual delay.
    public bool Apply(DelayRequest request, long now)
    {
        if (request.Version == _version) return false;
        long current = _paused ? _pauseBase + Math.Clamp(now - _pauseStart, 0, _pauseEnd - _pauseStart) : _active;
        long previousTarget = _target;
        _version = request.Version;
        _target = request.TargetSeconds * 1000L;
        bool seek = _target < current || (_target == 0 && _hasOutput && (previousTarget != 0 || _paused));
        if (seek)
        {
            if (_paused) FinishPause(now);
            _active = _target;
            _seeking = true;
            _cooldown = _captureResume = false;
        }
        else if (_paused)
        {
            // Repeated commands cannot extend the current quiet period past the original cap.
            _pauseEnd = _pauseStart + Math.Min(_step, _target - _pauseBase);
        }
        else if (!_hasOutput) _active = _target; // Initial fill has no output session to interrupt.
        Publish();
        return seek;
    }

    public bool CanStartPause(bool inputEnded) => !inputEnded && !_paused && !_seeking && _hasOutput &&
        !_cooldown && !_captureResume && _target > _active;

    public void StartPause(long now)
    {
        _pauseBase = _active;
        _pauseStart = now;
        _pauseEnd = now + Math.Min(_step, _target - _active);
        _paused = true;
        Publish();
    }

    public void FinishPause(long now)
    {
        if (!_paused) return;
        long accrued = Math.Clamp(now - _pauseStart, 0, _pauseEnd - _pauseStart);
        _active = _pauseBase + accrued;
        _lastPause = now - _pauseStart;
        _maxPause = Math.Max(_maxPause, _lastPause);
        _steps++;
        _paused = false;
        _captureResume = true;
        Publish();
    }

    public void MarkMedia(long outputDts, long now)
    {
        _outputHigh = Math.Max(_outputHigh, outputDts);
        bool publish = !_hasOutput || _seeking || _captureResume;
        _hasOutput = true;
        if (_seeking) { _seeking = false; _seeks++; }
        if (_captureResume)
        {
            _captureResume = false; _cooldown = true;
            _resumeWall = now; _resumeOutput = _outputHigh;
        }
        if (_cooldown && now - _resumeWall >= _resume && _outputHigh - _resumeOutput >= _resume)
        { _cooldown = false; publish = true; }
        if (publish) Publish();
    }

    public void MarkEnded() { _ended = true; Publish(); }
    public void SeekToTarget(long now)
    {
        if (_paused) FinishPause(now);
        _active = _target;
        _seeking = true;
        _cooldown = _captureResume = false;
        Publish();
    }
    private Published Build() => new(_version, _paused ? _pauseBase : _active, _target,
        _paused ? "IncreasingPause" : _seeking ? "SeekingKeyframe" : _captureResume ? "ResumingKeyframe" : _ended && _target > _active ? "TargetUnreachedAtInputEnd" :
        _target > _active ? (_cooldown || _captureResume ? "IncreasingPlayback" : "IncreasingAwaitingKeyframe") : "Stable",
        _paused, _pauseStart, _pauseEnd, _steps, _seeks, _lastPause, _maxPause, _seeking, _hasOutput, _captureResume);
    private void Publish() => Volatile.Write(ref _published, Build());
}
