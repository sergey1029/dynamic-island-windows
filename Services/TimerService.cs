namespace DynamicIsland;

public enum CountdownState
{
    Idle,
    Running,
    Paused,
}

/// <summary>Секундомер и таймер обратного отсчёта (без потоков — UI опрашивает состояние).</summary>
public sealed class TimerService
{
    static readonly TimeSpan MinDuration = TimeSpan.FromMinutes(1);
    static readonly TimeSpan MaxDuration = TimeSpan.FromHours(24);

    TimeSpan _stopwatchAccumulated;
    DateTime? _stopwatchStartedAt;
    DateTime _countdownEndsAt;
    TimeSpan _countdownPausedRemaining;

    public bool StopwatchRunning => _stopwatchStartedAt != null;

    public TimeSpan StopwatchElapsed =>
        _stopwatchAccumulated + (_stopwatchStartedAt is { } started ? DateTime.Now - started : TimeSpan.Zero);

    public void ToggleStopwatch()
    {
        if (_stopwatchStartedAt != null)
        {
            _stopwatchAccumulated = StopwatchElapsed;
            _stopwatchStartedAt = null;
        }
        else
        {
            _stopwatchStartedAt = DateTime.Now;
        }
    }

    public void ResetStopwatch()
    {
        _stopwatchAccumulated = TimeSpan.Zero;
        _stopwatchStartedAt = null;
    }

    public CountdownState CountdownState { get; private set; }
    public TimeSpan CountdownDuration { get; private set; } = TimeSpan.FromMinutes(5);

    public TimeSpan CountdownRemaining => CountdownState switch
    {
        CountdownState.Running => _countdownEndsAt - DateTime.Now > TimeSpan.Zero ? _countdownEndsAt - DateTime.Now : TimeSpan.Zero,
        CountdownState.Paused => _countdownPausedRemaining,
        _ => CountdownDuration,
    };

    public void ToggleCountdown()
    {
        switch (CountdownState)
        {
            case CountdownState.Idle:
                _countdownEndsAt = DateTime.Now + CountdownDuration;
                CountdownState = CountdownState.Running;
                break;
            case CountdownState.Running:
                _countdownPausedRemaining = CountdownRemaining;
                CountdownState = CountdownState.Paused;
                break;
            case CountdownState.Paused:
                _countdownEndsAt = DateTime.Now + _countdownPausedRemaining;
                CountdownState = CountdownState.Running;
                break;
        }
    }

    public void StartCountdown(TimeSpan duration)
    {
        CountdownDuration = duration;
        CountdownState = CountdownState.Idle;
        ToggleCountdown();
    }

    public void ResetCountdown() => CountdownState = CountdownState.Idle;

    public void AdjustCountdown(TimeSpan delta)
    {
        switch (CountdownState)
        {
            case CountdownState.Idle:
                CountdownDuration = Clamp(CountdownDuration + delta);
                break;
            case CountdownState.Running:
                var remaining = CountdownRemaining + delta;
                if (remaining > TimeSpan.FromSeconds(1)) _countdownEndsAt = DateTime.Now + remaining;
                break;
            case CountdownState.Paused:
                if (_countdownPausedRemaining + delta > TimeSpan.FromSeconds(1)) _countdownPausedRemaining += delta;
                break;
        }
    }

    /// <summary>true один раз — в момент, когда таймер дошёл до нуля.</summary>
    public bool CheckFinished()
    {
        if (CountdownState != CountdownState.Running || DateTime.Now < _countdownEndsAt) return false;
        CountdownState = CountdownState.Idle;
        return true;
    }

    /// <summary>Для скриншотов: запустить оба таймера с заданными значениями.</summary>
    public void SetDemo(TimeSpan stopwatch, TimeSpan countdownTotal, TimeSpan countdownRemaining)
    {
        _stopwatchAccumulated = stopwatch;
        _stopwatchStartedAt = DateTime.Now;
        CountdownDuration = countdownTotal;
        _countdownEndsAt = DateTime.Now + countdownRemaining;
        CountdownState = CountdownState.Running;
    }

    static TimeSpan Clamp(TimeSpan t) => t < MinDuration ? MinDuration : t > MaxDuration ? MaxDuration : t;
}
