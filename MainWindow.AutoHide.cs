using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace DynamicIsland;

/// <summary>
/// Автоскрытие при открытом окне. Показ: наведение в центр верхнего края (кроме полноэкранных
/// приложений) или горячая клавиша (короткое нажатие — открыть/закрыть, удержание — подсмотреть).
/// </summary>
public partial class MainWindow
{
    const double RevealHalfWidth = 220;
    static readonly TimeSpan HoldThreshold = TimeSpan.FromMilliseconds(450);

    readonly DispatcherTimer _poll = new() { Interval = TimeSpan.FromMilliseconds(50) };
    int _pollCount;
    ForegroundState _foreground;
    bool _autoHidden;
    bool _isHidden;

    bool _hoverReveal;
    DateTime _hoverRevealLastInside;

    GlobalHotkey? _hotkey;
    bool _hotkeyReveal, _hotkeyHeld, _hotkeyHovered;
    DateTime _hotkeyDownAt;

    void InitAutoHide()
    {
        _hotkey = new GlobalHotkey(_hwnd);
        RegisterHotkey();
        HwndSource.FromHwnd(_hwnd)?.AddHook(WndProc);
        _poll.Tick += (_, _) => Poll();
        _poll.Start();
        UpdateForeground();
    }

    bool RegisterHotkey()
    {
        if (_hotkey == null) return false;
        if (_hotkey.Register(_settings.Hotkey)) return true;
        Log.Write($"hotkey '{_settings.Hotkey}' не зарегистрирован (неверный формат или занят другим приложением)");
        return false;
    }

    /// <summary>Отключить горячую клавишу, пока в настройках записывают новую.</summary>
    public void SuspendHotkey() => _hotkey?.Unregister();

    public bool ResumeHotkey() => RegisterHotkey();

    IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == Native.WM_HOTKEY && wParam.ToInt32() == GlobalHotkey.Id)
        {
            OnHotkey();
            handled = true;
        }
        return IntPtr.Zero;
    }

    void OnHotkey()
    {
        if (_hotkeyReveal && !_hotkeyHeld)
        {
            _hotkeyReveal = false;
        }
        else
        {
            _hotkeyReveal = true;
            _hotkeyHeld = true;
            _hotkeyHovered = false;
            _hotkeyDownAt = DateTime.Now;
            UpdateSystemInfo();
        }
        UpdateIsland();
    }

    void Poll()
    {
        _pollCount++;
        var now = DateTime.Now;
        if (_pollCount % 5 == 0) UpdateForeground();

        Native.GetCursorPos(out var p);
        var cursor = new Point(p.X / _dpiX, p.Y / _dpiY);
        bool insideIsland = InIslandZone(cursor, 24);

        // горячая клавиша: удержание = подсмотреть, короткое нажатие = открыть до ухода мыши
        if (_hotkeyHeld && _hotkey != null && !Native.IsKeyDown(_hotkey.VirtualKey))
        {
            _hotkeyHeld = false;
            if (now - _hotkeyDownAt > HoldThreshold)
            {
                _hotkeyReveal = false;
                UpdateIsland();
            }
        }
        if (_hotkeyReveal && !_hotkeyHeld)
        {
            if (insideIsland) _hotkeyHovered = true;
            else if (_hotkeyHovered || now - _hotkeyDownAt > TimeSpan.FromSeconds(6))
            {
                _hotkeyReveal = false;
                UpdateIsland();
            }
        }

        // наведение на центр верхнего края
        if (_autoHidden)
        {
            bool atEdge = cursor.Y <= 2 && Math.Abs(cursor.X - (Left + Width / 2)) <= RevealHalfWidth;
            if (!_hoverReveal && atEdge && _foreground.Kind != ForegroundKind.Fullscreen)
            {
                _hoverReveal = true;
                _hoverRevealLastInside = now;
                UpdateIsland();
            }
            if (_hoverReveal)
            {
                if (atEdge || insideIsland) _hoverRevealLastInside = now;
                else if (now - _hoverRevealLastInside > TimeSpan.FromMilliseconds(600) && !Expanded)
                {
                    _hoverReveal = false;
                    UpdateIsland();
                }
            }
        }
        else if (_hoverReveal)
        {
            _hoverReveal = false;
        }

        if (_dragActive && now - _dragLastSeen > TimeSpan.FromMilliseconds(400))
        {
            EndDrag();
            UpdateIsland();
        }
    }

    bool InIslandZone(Point cursor, double margin)
    {
        double centerX = Left + Width / 2;
        double top = Top + TopOffset;
        return cursor.X >= centerX - _targetWidth / 2 - margin && cursor.X <= centerX + _targetWidth / 2 + margin
            && cursor.Y >= Top && cursor.Y <= top + _targetHeight + margin;
    }

    void UpdateForeground()
    {
        var state = WindowInfo.GetForeground();
        if (state.Kind == ForegroundKind.Unknown) return;
        _foreground = state;

        bool hide = _settings.AutoHide.ToLowerInvariant() switch
        {
            "off" => false,
            _ when _docked => state.Kind == ForegroundKind.Fullscreen,
            "fullscreen" => state.Kind == ForegroundKind.Fullscreen,
            "maximized" => state.Kind >= ForegroundKind.Maximized,
            "overlap" => state.Kind >= ForegroundKind.Maximized || (state.Kind == ForegroundKind.Window && OverlapsIsland(state.Bounds)),
            _ => state.Kind >= ForegroundKind.Window,
        };
        if (hide == _autoHidden) return;
        _autoHidden = hide;
        UpdateIsland();
    }

    bool OverlapsIsland(Native.RECT r)
    {
        double centerX = (Left + Width / 2) * _dpiX;
        double halfWidth = (ExpandedWidth / 2 + 10) * _dpiX;
        double bottom = (Top + TopOffset + CompactHeight + 10) * _dpiY;
        return r.Left < centerX + halfWidth && r.Right > centerX - halfWidth && r.Top < bottom;
    }

    void UpdateVisibility()
    {
        bool fullscreen = _foreground.Kind == ForegroundKind.Fullscreen;
        bool transient = _notification != null || _osdUntil > DateTime.Now;
        bool eventsAllowed = !fullscreen || _settings.ShowEventsInFullscreen || _notification?.Kind == NotificationKind.Alarm;
        bool visible = !_autoHidden || _hoverReveal || _hotkeyReveal || _dragActive || _hoverExpanded
            || DateTime.Now < _pinnedUntil || (transient && eventsAllowed);
        SetHidden(!visible);
    }

    /// <summary>Скрытие: остров сплющивается и вжимается в верхний край. Показ — пружиной обратно.</summary>
    void SetHidden(bool hidden)
    {
        if (hidden == _isHidden) return;
        _isHidden = hidden;
        IslandHost.IsHitTestVisible = !hidden;

        if (hidden)
        {
            var duration = TimeSpan.FromMilliseconds(420);
            var ease = new CubicEase { EasingMode = EasingMode.EaseInOut };
            HideScale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.3, duration) { EasingFunction = ease });
            HideScale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.1, duration) { EasingFunction = ease });
            HideShift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(-TopOffset - 2, duration) { EasingFunction = ease });
            ContentRoot.BeginAnimation(OpacityProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(150)));
            IslandHost.BeginAnimation(OpacityProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(160))
            {
                BeginTime = TimeSpan.FromMilliseconds(280),
            });
        }
        else
        {
            var duration = TimeSpan.FromMilliseconds(600);
            HideScale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1, duration) { EasingFunction = new SpringEase() });
            HideScale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(1, duration) { EasingFunction = new SpringEase() });
            HideShift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(350))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            });
            IslandHost.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(120)));
            ContentRoot.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(250))
            {
                BeginTime = TimeSpan.FromMilliseconds(150),
            });
        }
    }
}
