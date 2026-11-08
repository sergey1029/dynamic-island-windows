using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace DynamicIsland;

/// <summary>Страница таймера/секундомера и их компактный вид.</summary>
public partial class MainWindow
{
    static readonly Color OrangeColor = Color.FromRgb(0xFF, 0x9F, 0x0A);

    bool TimerActive => _timers.CountdownState != CountdownState.Idle || _timers.StopwatchRunning;

    void UpdateTimers()
    {
        var stopwatch = _timers.StopwatchElapsed;
        StopwatchText.Text = FormatStopwatch(stopwatch);
        HookStopwatchFrames(_timers.StopwatchRunning);
        StopwatchToggle.Content = _timers.StopwatchRunning ? "" : "";

        var remaining = _timers.CountdownRemaining;
        CountdownText.Text = FormatCountdown(remaining);
        CountdownToggle.Content = _timers.CountdownState == CountdownState.Running ? "" : "";

        if (_timers.CountdownState != CountdownState.Idle)
        {
            TimerCompactGlyph.Text = "";
            TimerCompactLabel.Text = _timers.CountdownState == CountdownState.Paused ? "Таймер · пауза" : "Таймер";
            TimerCompactText.Text = FormatCountdown(remaining);
        }
        else if (_timers.StopwatchRunning)
        {
            TimerCompactGlyph.Text = "";
            TimerCompactLabel.Text = "Секундомер";
            TimerCompactText.Text = FormatStopwatch(stopwatch);
        }

        if (_timers.CheckFinished())
        {
            Enqueue(new IslandNotification
            {
                Kind = NotificationKind.Alarm,
                AppName = "Таймер",
                Title = "Время вышло!",
                Body = $"Таймер на {FormatCountdown(_timers.CountdownDuration)}",
                Glyph = "",
                GlyphColor = OrangeColor,
            });
        }
    }

    static string FormatStopwatch(TimeSpan t) =>
        t.TotalHours >= 1 ? t.ToString(@"h\:mm\:ss") : $"{(int)t.TotalMinutes}:{t.Seconds:00},{t.Milliseconds / 10:00}";

    bool _stopwatchFramesHooked;

    /// <summary>Пока секундомер идёт, обновляем цифры каждый кадр, а не раз в 250 мс — без рывков.</summary>
    void HookStopwatchFrames(bool running)
    {
        if (running == _stopwatchFramesHooked) return;
        _stopwatchFramesHooked = running;
        if (running) CompositionTarget.Rendering += OnStopwatchFrame;
        else CompositionTarget.Rendering -= OnStopwatchFrame;
    }

    void OnStopwatchFrame(object? sender, EventArgs e)
    {
        var text = FormatStopwatch(_timers.StopwatchElapsed);
        if (StopwatchText.Text != text) StopwatchText.Text = text;
        if (_timers.CountdownState == CountdownState.Idle && TimerCompactText.Text != text) TimerCompactText.Text = text;
    }

    static string FormatCountdown(TimeSpan t)
    {
        // округляем вверх, чтобы 0:00 появлялось ровно в момент окончания
        var seconds = (int)Math.Ceiling(t.TotalSeconds);
        return seconds >= 3600 ? $"{seconds / 3600}:{seconds / 60 % 60:00}:{seconds % 60:00}" : $"{seconds / 60}:{seconds % 60:00}";
    }

    void TimersChanged()
    {
        UpdateTimers();
        UpdateIsland();
    }

    void StopwatchToggle_Click(object sender, RoutedEventArgs e) { _timers.ToggleStopwatch(); TimersChanged(); }
    void StopwatchReset_Click(object sender, RoutedEventArgs e) { _timers.ResetStopwatch(); TimersChanged(); }
    void CountdownToggle_Click(object sender, RoutedEventArgs e) { _timers.ToggleCountdown(); TimersChanged(); }
    void CountdownReset_Click(object sender, RoutedEventArgs e) { _timers.ResetCountdown(); TimersChanged(); }
    void CountdownMinus_Click(object sender, RoutedEventArgs e) { _timers.AdjustCountdown(TimeSpan.FromMinutes(-1)); TimersChanged(); }
    void CountdownPlus_Click(object sender, RoutedEventArgs e) { _timers.AdjustCountdown(TimeSpan.FromMinutes(1)); TimersChanged(); }

    void Preset_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string tag } && int.TryParse(tag, out var minutes))
        {
            _timers.StartCountdown(TimeSpan.FromMinutes(minutes));
            TimersChanged();
        }
    }
}
