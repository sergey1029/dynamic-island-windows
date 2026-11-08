using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Microsoft.Win32;

namespace DynamicIsland;

public enum IslandPage
{
    Music,
    Home,
    Calendar,
    Timer,
    Shelf,
}

/// <summary>Ядро острова: состояния, размеры, переключение панелей.</summary>
public partial class MainWindow : Window
{
    const double CompactHeight = 34;
    const double ExpandedWidth = 440;

    readonly MediaService _media = new();
    readonly NotificationService _notifications = new();
    readonly SystemInfoService _system = new();
    readonly WeatherService _weather = new();
    readonly AudioService _audio = new();
    readonly BrightnessService _brightness = new();
    readonly TimerService _timers = new();
    readonly CalendarService _calendar = new();
    readonly Settings _settings = Settings.Load();
    Settings _appliedSettings;
    FileSystemWatcher? _settingsWatcher;
    SettingsWindow? _settingsWindow;
    IntPtr _hwnd;
    double _dpiX = 1, _dpiY = 1;

    readonly DispatcherTimer _tick = new() { Interval = TimeSpan.FromMilliseconds(250) };
    int _tickCount;
    DateTime _nextWeather = DateTime.MinValue;

    // раскрытие
    readonly DispatcherTimer _hoverTimer = new();
    bool _hoverTarget, _hoverExpanded, _wasExpanded;
    DateTime _pinnedUntil;
    IslandPage _page = IslandPage.Home;
    List<IslandPage> _dotPages = new();

    // анимация
    FrameworkElement? _activePanel;
    double _targetWidth, _targetHeight;

    public MainWindow()
    {
        InitializeComponent();
        _appliedSettings = _settings.Clone();
        HomeClock.SetStyle(_settings.ClockStyle);
        BuildEqualizer(CompactEq, 4, 14);
        BuildEqualizer(ExpandedEq, 5, 22);

        _hoverTimer.Tick += (_, _) =>
        {
            _hoverTimer.Stop();
            _hoverExpanded = _hoverTarget;
            if (_hoverExpanded) UpdateSystemInfo();
            UpdateIsland();
        };
        _notificationTimer.Tick += (_, _) => ShowNextNotification();
        _tick.Tick += (_, _) => OnTick();
        Loaded += OnLoaded;
        Closed += (_, _) =>
        {
            _hotkey?.Dispose();
            _brightness.Dispose();
            StopAlarm();
        };
    }

    bool Expanded => _hoverExpanded || _hotkeyReveal || _dragActive || DateTime.Now < _pinnedUntil;

    List<IslandPage> Pages
    {
        get
        {
            var pages = new List<IslandPage>();
            if (HasMusic) pages.Add(IslandPage.Music);
            pages.AddRange([IslandPage.Home, IslandPage.Calendar, IslandPage.Timer, IslandPage.Shelf]);
            return pages;
        }
    }

    IslandPage DefaultPage => HasMusic ? IslandPage.Music : TimerActive ? IslandPage.Timer : IslandPage.Home;

    async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (PresentationSource.FromVisual(this)?.CompositionTarget is { } target)
        {
            _dpiX = target.TransformToDevice.M11;
            _dpiY = target.TransformToDevice.M22;
        }
        if (App.ShowcaseMode is { } mode)
        {
            await RunShowcaseAsync(mode, App.ShowcaseDir!);
            return;
        }
        PositionWindow();
        SystemEvents.DisplaySettingsChanged += (_, _) => Dispatcher.InvokeAsync(PositionWindow);
        RenderShelf();
        RenderCalendar();
        InitAutoHide();
        UpdateIsland();
        OnTick();
        _tick.Start();
        WatchSettings();

        _audio.Changed += (level, muted) => Dispatcher.InvokeAsync(() =>
        {
            if (_settings.ShowVolume) ShowOsd(OsdKind.Volume, level, muted);
        });
        try
        {
            _audio.Init();
        }
        catch (Exception ex)
        {
            Log.Write("audio init", ex);
        }

        _brightness.Changed += value => Dispatcher.InvokeAsync(() =>
        {
            if (_settings.ShowBrightness) ShowOsd(OsdKind.Brightness, value / 100.0);
        });
        _brightness.Start();

        _media.Changed += () => Dispatcher.InvokeAsync(ApplyMedia);
        try
        {
            await _media.InitAsync();
        }
        catch (Exception ex)
        {
            Log.Write("media init", ex);
        }

        _notifications.Received += n => Dispatcher.InvokeAsync(() => Enqueue(n));
        if (!await _notifications.StartAsync())
        {
            Log.Write("notifications unavailable: " + _notifications.Status);
            Enqueue(new IslandNotification
            {
                Kind = NotificationKind.Info,
                AppName = "Dynamic Island",
                Title = "Нет доступа к уведомлениям",
                Body = "Параметры → Конфиденциальность → Уведомления",
            });
        }
    }

    #region Окно

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _hwnd = new WindowInteropHelper(this).Handle;
        // не показываем в Alt+Tab и не забираем фокус у активного окна
        Native.SetWindowLong(_hwnd, Native.GWL_EXSTYLE,
            Native.GetWindowLong(_hwnd, Native.GWL_EXSTYLE) | Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE);
    }

    void PositionWindow()
    {
        Left = (SystemParameters.PrimaryScreenWidth - Width) / 2;
        Top = 0;
        IslandHost.Margin = new Thickness(0, _settings.TopOffset, 0, 0);
    }

    void KeepOnTop() =>
        Native.SetWindowPos(_hwnd, Native.HWND_TOPMOST, 0, 0, 0, 0, Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);

    void WatchSettings()
    {
        _settingsWatcher = new FileSystemWatcher(Settings.Folder, "settings.json")
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName,
            EnableRaisingEvents = true,
        };
        _settingsWatcher.Changed += (_, _) => Dispatcher.InvokeAsync(async () =>
        {
            await Task.Delay(300);
            try
            {
                // свои же сохранения из окна настроек не перечитываем
                if (File.ReadAllText(Settings.FilePath) == Settings.LastWrittenText) return;
            }
            catch (IOException)
            {
                return;
            }
            _settings.CopyFrom(Settings.Load());
            ApplySettings();
            _settingsWindow?.LoadValues();
        });
    }

    public Settings CurrentSettings => _settings;

    /// <summary>Окно настроек изменило <see cref="CurrentSettings"/>: сохранить и применить.</summary>
    public void SettingsEdited()
    {
        if (!_showcase) _settings.Save();
        ApplySettings();
    }

    void ApplySettings()
    {
        var old = _appliedSettings;
        _appliedSettings = _settings.Clone();

        if (old.City != _settings.City || old.Latitude != _settings.Latitude || old.Longitude != _settings.Longitude
            || old.TemperatureUnit != _settings.TemperatureUnit)
            _nextWeather = DateTime.MinValue;
        if (old.Hotkey != _settings.Hotkey) RegisterHotkey();
        if (old.CalendarsKey() != _settings.CalendarsKey()) _nextCalendar = DateTime.MinValue;
        HomeClock.SetStyle(_settings.ClockStyle);
        PositionWindow();
        UpdateForeground();
        RenderCalendar();
        UpdateIsland();
    }

    public void OpenSettings(string page = "general")
    {
        if (_settingsWindow == null)
        {
            _settingsWindow = new SettingsWindow(this);
            _settingsWindow.Closed += (_, _) => _settingsWindow = null;
            _settingsWindow.ShowPage(page);
            _settingsWindow.Show();
        }
        else
        {
            _settingsWindow.ShowPage(page);
            if (_settingsWindow.WindowState == WindowState.Minimized) _settingsWindow.WindowState = WindowState.Normal;
        }
        _settingsWindow.Activate();
    }

    #endregion

    #region Состояния и анимация

    void UpdateIsland()
    {
        var now = DateTime.Now;
        bool expanded = Expanded;
        if (expanded && !_wasExpanded) _page = _dragActive ? IslandPage.Shelf : DefaultPage;
        _wasExpanded = expanded;
        var pages = Pages;
        if (!pages.Contains(_page)) _page = pages[0];

        FrameworkElement panel;
        double w, h;
        if (_notification != null)
        {
            panel = PanelNotify; w = 400; h = 76;
        }
        else if (_osdUntil > now)
        {
            if (_osdKind == OsdKind.Charging) { panel = PanelCharging; w = 340; }
            else { panel = PanelOsd; w = 320; }
            h = CompactHeight;
        }
        else if (expanded)
        {
            w = ExpandedWidth;
            switch (_page)
            {
                case IslandPage.Music: panel = PanelMusic; h = 180; break;
                case IslandPage.Calendar: panel = PanelCalendar; h = 190; break;
                case IslandPage.Timer: panel = PanelTimer; h = 168; break;
                case IslandPage.Shelf: panel = PanelShelf; h = 150; break;
                default: panel = PanelWidgets; h = 160; break;
            }
        }
        else if (TimerActive)
        {
            panel = PanelTimerCompact; w = 300; h = CompactHeight;
        }
        else if (MusicActive)
        {
            panel = PanelMusicCompact; w = 300; h = CompactHeight;
        }
        else
        {
            panel = PanelIdle; w = 180; h = CompactHeight;
        }

        UpdateDots(panel.Width == ExpandedWidth, pages);
        SwitchPanel(panel);
        AnimateSize(w, h);
        UpdateVisibility();
    }

    void UpdateDots(bool visible, List<IslandPage> pages)
    {
        PageDots.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        if (!visible) return;

        if (!_dotPages.SequenceEqual(pages))
        {
            _dotPages = pages;
            PageDots.Children.Clear();
            foreach (var page in pages)
            {
                var hit = new Border
                {
                    Background = Brushes.Transparent,
                    Padding = new Thickness(4, 2, 4, 2),
                    Cursor = Cursors.Hand,
                    Child = new System.Windows.Shapes.Ellipse { Width = 5, Height = 5, Fill = Brushes.White },
                };
                hit.MouseLeftButtonUp += (_, e) =>
                {
                    _page = page;
                    e.Handled = true;
                    UpdateIsland();
                };
                PageDots.Children.Add(hit);
            }
        }
        for (int i = 0; i < pages.Count; i++)
            PageDots.Children[i].Opacity = pages[i] == _page ? 0.9 : 0.3;
    }

    void AnimateSize(double w, double h)
    {
        if (w == _targetWidth && h == _targetHeight) return;
        bool first = _targetWidth == 0;
        _targetWidth = w;
        _targetHeight = h;
        if (first)
        {
            IslandHost.Width = w;
            IslandHost.Height = h;
            return;
        }

        var duration = TimeSpan.FromMilliseconds(600);
        IslandHost.BeginAnimation(WidthProperty, new DoubleAnimation(w, duration) { EasingFunction = new SpringEase() });
        IslandHost.BeginAnimation(HeightProperty, new DoubleAnimation(h, duration) { EasingFunction = new SpringEase() });
    }

    void SwitchPanel(FrameworkElement panel)
    {
        if (_activePanel == panel) return;
        var old = _activePanel;
        _activePanel = panel;

        if (old != null)
        {
            var fadeOut = new DoubleAnimation(0, TimeSpan.FromMilliseconds(120));
            fadeOut.Completed += (_, _) =>
            {
                if (_activePanel != old) old.Visibility = Visibility.Collapsed;
            };
            old.BeginAnimation(OpacityProperty, fadeOut);
        }

        panel.Visibility = Visibility.Visible;
        var delay = TimeSpan.FromMilliseconds(old == null ? 0 : 110);
        panel.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(260)) { BeginTime = delay });

        var scale = new ScaleTransform(0.92, 0.92);
        panel.RenderTransformOrigin = new Point(0.5, 0);
        panel.RenderTransform = scale;
        var grow = new DoubleAnimation(1, TimeSpan.FromMilliseconds(500)) { BeginTime = delay, EasingFunction = new SpringEase() };
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, grow);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, grow);
    }

    void IslandHost_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        double w = e.NewSize.Width, h = e.NewSize.Height;
        double r = Math.Min(h / 2, 30);
        IslandBg.CornerRadius = new CornerRadius(r);
        ContentRoot.Clip = new RectangleGeometry(new Rect(0, 0, w, h), r, r);
    }

    void OnTick()
    {
        _tickCount++;
        var now = DateTime.Now;
        HomeClock.Update(now);
        UpdateProgress();
        UpdateTimers();

        if (_tickCount % 4 == 0) CheckCharging();
        if (_tickCount % 8 == 1 && Expanded) UpdateSystemInfo();
        if (_tickCount % 20 == 0) KeepOnTop();
        if (_tickCount % 60 == 0) CheckReminders();
        if (_tickCount % 240 == 0) RenderCalendar();
        if (now >= _nextWeather)
        {
            _nextWeather = now.AddMinutes(20);
            _ = RefreshWeatherAsync();
        }
        if (now >= _nextCalendar)
        {
            _nextCalendar = now.AddMinutes(15);
            _ = RefreshCalendarAsync();
        }
        UpdateIsland();
    }

    static string Capitalize(string s) => s.Length == 0 ? s : char.ToUpper(s[0]) + s[1..];

    #endregion

    #region Мышь и меню

    void Island_MouseEnter(object sender, MouseEventArgs e)
    {
        if (_notification != null) _notificationTimer.Stop();
        _hoverTarget = true;
        _hoverTimer.Stop();
        _hoverTimer.Interval = TimeSpan.FromMilliseconds(180);
        _hoverTimer.Start();
    }

    void Island_MouseLeave(object sender, MouseEventArgs e)
    {
        if (_notification != null && _notification.Kind != NotificationKind.Alarm)
        {
            _notificationTimer.Interval = TimeSpan.FromSeconds(1.5);
            _notificationTimer.Start();
        }
        _hoverTarget = false;
        _hoverTimer.Stop();
        _hoverTimer.Interval = TimeSpan.FromMilliseconds(350);
        _hoverTimer.Start();
    }

    void Island_Click(object sender, MouseButtonEventArgs e)
    {
        if (_notification != null) OpenNotification();
    }

    void Island_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (!Expanded || _notification != null || _osdUntil > DateTime.Now) return;
        e.Handled = true;

        // на полке колесо листает файлы, если они не влезают
        if (_page == IslandPage.Shelf && ShelfScroll.IsMouseOver && ShelfScroll.ScrollableWidth > 0)
        {
            var offset = ShelfScroll.HorizontalOffset - e.Delta / 2.0;
            if (offset >= 0 && offset <= ShelfScroll.ScrollableWidth)
            {
                ShelfScroll.ScrollToHorizontalOffset(offset);
                return;
            }
        }

        var pages = Pages;
        int index = pages.IndexOf(_page) + (e.Delta < 0 ? 1 : -1);
        if (index < 0 || index >= pages.Count) return;
        _page = pages[index];
        if (_page == IslandPage.Home) UpdateSystemInfo();
        UpdateIsland();
    }

    void Island_ContextMenuOpening(object sender, ContextMenuEventArgs e) => AutostartItem.IsChecked = Autostart.IsEnabled;

    void Autostart_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Autostart.Set(AutostartItem.IsChecked);
        }
        catch (Exception ex)
        {
            Log.Write("autostart", ex);
        }
    }

    void OpenSettings_Click(object sender, RoutedEventArgs e) => OpenSettings();

    void TestNotification_Click(object sender, RoutedEventArgs e) => SendTestNotification();

    public void SendTestNotification() => Enqueue(new IslandNotification
    {
        Kind = NotificationKind.Info,
        AppName = "Dynamic Island",
        Title = "Привет! 👋",
        Body = "Так будут выглядеть уведомления из приложений",
    });

    void Exit_Click(object sender, RoutedEventArgs e) => Close();

    #endregion
}
