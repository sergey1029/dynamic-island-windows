using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace DynamicIsland;

/// <summary>
/// Окно настроек. Все изменения применяются сразу: правим общий объект Settings
/// и зовём <see cref="MainWindow.SettingsEdited"/>, который сохраняет файл и обновляет остров.
/// </summary>
public partial class SettingsWindow : Window
{
    sealed record Service(string Id, string Title, string Help, string? Site, string? SiteTitle);

    static readonly Service[] Services =
    [
        new("google", "Google", "1. Откройте настройки Google Календаря.\n2. Слева выберите нужный календарь.\n3. Прокрутите до «Секретный адрес в формате iCal» и скопируйте ссылку.",
            "https://calendar.google.com/calendar/r/settings", "Открыть настройки Google Календаря"),
        new("yandex", "Яндекс", "1. Откройте Яндекс Календарь.\n2. Наведите на календарь в списке слева → ⚙ → «Экспорт».\n3. Скопируйте ссылку в формате iCal.",
            "https://calendar.yandex.ru", "Открыть Яндекс Календарь"),
        new("outlook", "Outlook", "1. Откройте Outlook → Параметры → Календарь → Общие календари.\n2. В «Опубликовать календарь» выберите календарь и «Может просматривать все сведения».\n3. Нажмите «Опубликовать» и скопируйте ссылку ICS.",
            "https://outlook.live.com/calendar/0/options/calendar/SharedCalendars", "Открыть настройки Outlook"),
        new("icloud", "iCloud", "1. Откройте Календарь на icloud.com или на iPhone.\n2. Нажмите значок общего доступа рядом с календарём.\n3. Включите «Публичный календарь» и скопируйте ссылку (webcal://…).",
            "https://www.icloud.com/calendar", "Открыть iCloud Календарь"),
        new("ical", "Другой", "Вставьте любую ссылку на календарь в формате iCal (https:// или webcal://) или путь к файлу .ics на компьютере.",
            null, null),
    ];

    static readonly string[] Palette = ["#FF453A", "#FF9F0A", "#FFD60A", "#30D158", "#64D2FF", "#0A84FF", "#BF5AF2", "#FF375F"];

    readonly MainWindow _main;
    readonly WeatherService _weather = new();
    readonly CalendarService _calendar = new();
    readonly DispatcherTimer _previewTimer = new() { Interval = TimeSpan.FromMilliseconds(200) };
    readonly List<(Border Card, ClockFace Face, string Id)> _clockCards = new();
    string _service = "google";
    string _color = Palette[0];
    bool _loading;

    Settings S => _main.CurrentSettings;

    public SettingsWindow(MainWindow main)
    {
        _main = main;
        InitializeComponent();
        VersionText.Text = "Версия " + (Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0");
        FolderText.Text = Settings.Folder;

        BuildClockStyles();
        BuildServices();
        BuildSwatches();
        LoadValues();

        _previewTimer.Tick += (_, _) =>
        {
            var now = DateTime.Now;
            foreach (var card in _clockCards) card.Face.Update(now);
        };
        _previewTimer.Start();
        Closed += (_, _) =>
        {
            _previewTimer.Stop();
            _main.ResumeHotkey();
        };
        _ = RefreshWeatherPreviewAsync();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        // тёмный заголовок окна Windows 11
        var hwnd = new WindowInteropHelper(this).Handle;
        int dark = 1;
        DwmSetWindowAttribute(hwnd, 20, ref dark, sizeof(int));
    }

    public void ShowPage(string page)
    {
        var nav = page switch
        {
            "appearance" => NavAppearance,
            "weather" => NavWeather,
            "calendars" => NavCalendars,
            "notifications" => NavNotifications,
            "about" => NavAbout,
            _ => NavGeneral,
        };
        nav.IsChecked = true;
    }

    /// <summary>Перечитать значения в контролы (файл настроек поменяли снаружи).</summary>
    public void LoadValues()
    {
        _loading = true;
        try
        {
            AutostartToggle.IsChecked = Autostart.IsEnabled;
            foreach (var option in AutoHideOptions.Children.OfType<RadioButton>())
                option.IsChecked = string.Equals((string)option.Tag, S.AutoHide, StringComparison.OrdinalIgnoreCase);
            HotkeyBox.Text = S.Hotkey.Replace("+", " + ");
            UnitCelsius.IsChecked = S.TemperatureUnit != "fahrenheit";
            UnitFahrenheit.IsChecked = S.TemperatureUnit == "fahrenheit";
            CurrentCityText.Text = $"Сейчас: {S.City}";

            foreach (var toggle in FindTagged<CheckBox>(this))
                toggle.IsChecked = (bool?)Property(toggle.Tag)?.GetValue(S);
            foreach (var box in FindTagged<NumberBox>(this))
                box.SetSilently(Convert.ToDouble(Property(box.Tag)?.GetValue(S) ?? 0));

            UpdateClockSelection();
            RenderCalendars();
        }
        finally
        {
            _loading = false;
        }
    }

    void Changed()
    {
        if (!_loading) _main.SettingsEdited();
    }

    #region Навигация

    void Nav_Checked(object sender, RoutedEventArgs e)
    {
        if (PageGeneral == null) return;
        var pages = new[] { PageGeneral, PageAppearance, PageWeather, PageCalendars, PageNotifications, PageAbout };
        var navs = new[] { NavGeneral, NavAppearance, NavWeather, NavCalendars, NavNotifications, NavAbout };
        for (int i = 0; i < pages.Length; i++)
            pages[i].Visibility = navs[i] == sender ? Visibility.Visible : Visibility.Collapsed;
        ContentScroll.ScrollToTop();
    }

    #endregion

    #region Общие обработчики

    static PropertyInfo? Property(object? tag) => tag is string name ? typeof(Settings).GetProperty(name) : null;

    static IEnumerable<T> FindTagged<T>(DependencyObject root) where T : FrameworkElement
    {
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
        {
            if (child is T element && Property(element.Tag) != null) yield return element;
            foreach (var nested in FindTagged<T>(child)) yield return nested;
        }
    }

    void Toggle_Click(object sender, RoutedEventArgs e)
    {
        if (sender is CheckBox toggle && Property(toggle.Tag) is { } prop)
        {
            prop.SetValue(S, toggle.IsChecked == true);
            Changed();
        }
    }

    void Number_Changed(object? sender, EventArgs e)
    {
        if (sender is NumberBox box && Property(box.Tag) is { } prop)
        {
            prop.SetValue(S, prop.PropertyType == typeof(int) ? (int)box.Value : box.Value);
            Changed();
        }
    }

    #endregion

    #region Общие

    void Autostart_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Autostart.Set(AutostartToggle.IsChecked == true);
        }
        catch (Exception ex)
        {
            Log.Write("autostart", ex);
            AutostartToggle.IsChecked = Autostart.IsEnabled;
        }
    }

    void AutoHide_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton { Tag: string mode })
        {
            S.AutoHide = mode;
            Changed();
        }
    }

    void HotkeyBox_GotFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        // пока записываем сочетание, глобальная клавиша не должна его перехватывать
        _main.SuspendHotkey();
        HotkeyStatus.Foreground = (Brush)FindResource("DimText");
        HotkeyStatus.Text = "Нажмите новое сочетание с Ctrl, Alt, Shift или Win…";
    }

    void HotkeyBox_LostFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        bool ok = _main.ResumeHotkey();
        HotkeyStatus.Foreground = ok ? (Brush)FindResource("DimText") : new SolidColorBrush(Color.FromRgb(0xFF, 0x45, 0x3A));
        HotkeyStatus.Text = ok ? "" : "Это сочетание занято другой программой — выберите другое.";
    }

    void HotkeyBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        e.Handled = true;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift
            or Key.LWin or Key.RWin or Key.Tab)
            return;
        if (key == Key.Escape)
        {
            Keyboard.ClearFocus();
            return;
        }

        var parts = new List<string>();
        var mods = Keyboard.Modifiers;
        if (mods.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
        if (mods.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
        if (mods.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
        if (mods.HasFlag(ModifierKeys.Windows) || Keyboard.IsKeyDown(Key.LWin) || Keyboard.IsKeyDown(Key.RWin)) parts.Add("Win");
        if (parts.Count == 0)
        {
            HotkeyStatus.Text = "Нужна хотя бы одна клавиша-модификатор: Ctrl, Alt, Shift или Win.";
            return;
        }

        parts.Add(key >= Key.D0 && key <= Key.D9 ? ((int)(key - Key.D0)).ToString() : key.ToString());
        S.Hotkey = string.Join("+", parts);
        HotkeyBox.Text = string.Join(" + ", parts);
        Changed();
        Keyboard.ClearFocus();
        FocusManager.SetFocusedElement(this, this);
    }

    #endregion

    #region Внешний вид

    void BuildClockStyles()
    {
        foreach (var style in ClockStyles.All)
        {
            var face = new ClockFace { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            face.SetStyle(style.Id);

            var preview = new Border
            {
                Height = 130,
                CornerRadius = new CornerRadius(22),
                Background = Brushes.Black,
                Padding = new Thickness(16),
                Child = new Viewbox { Stretch = Stretch.Uniform, StretchDirection = StretchDirection.DownOnly, Child = face },
            };
            var info = new StackPanel { Margin = new Thickness(4, 12, 4, 0) };
            info.Children.Add(new TextBlock { Text = style.Name, FontSize = 14, FontWeight = FontWeights.SemiBold });
            info.Children.Add(new TextBlock
            {
                Text = style.Description,
                FontSize = 12,
                Foreground = (Brush)FindResource("DimText"),
                TextWrapping = TextWrapping.Wrap,
            });

            var card = new Border
            {
                Width = 212,
                Margin = new Thickness(0, 0, 12, 12),
                Padding = new Thickness(10),
                CornerRadius = new CornerRadius(16),
                Background = (Brush)FindResource("CardBg"),
                BorderThickness = new Thickness(2),
                Cursor = Cursors.Hand,
                Child = new StackPanel { Children = { preview, info } },
            };
            var id = style.Id;
            card.MouseLeftButtonUp += (_, _) =>
            {
                S.ClockStyle = id;
                UpdateClockSelection();
                Changed();
            };
            ClockStyleList.Children.Add(card);
            _clockCards.Add((card, face, id));
        }
    }

    void UpdateClockSelection()
    {
        var selected = ClockStyles.Get(S.ClockStyle).Id;
        foreach (var (card, _, id) in _clockCards)
            card.BorderBrush = id == selected ? (Brush)FindResource("Accent") : (Brush)FindResource("Line");
    }

    #endregion

    #region Погода

    void CitySearchBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) CitySearch_Click(sender, e);
    }

    async void CitySearch_Click(object sender, RoutedEventArgs e)
    {
        var query = CitySearchBox.Text.Trim();
        if (query.Length < 2) return;
        CityResults.Children.Clear();
        CitySearchButton.IsEnabled = false;
        SetCityStatus("Ищу…");
        try
        {
            var places = await _weather.SearchAsync(query);
            SetCityStatus(places.Count == 0 ? "Ничего не нашлось. Попробуйте написать иначе." : null);
            foreach (var place in places) CityResults.Children.Add(CreatePlaceButton(place));
        }
        catch (Exception ex)
        {
            Log.Write("city search", ex);
            SetCityStatus("Не удалось выполнить поиск — проверьте интернет.");
        }
        finally
        {
            CitySearchButton.IsEnabled = true;
        }
    }

    void SetCityStatus(string? text)
    {
        CitySearchStatus.Text = text ?? "";
        CitySearchStatus.Visibility = text == null ? Visibility.Collapsed : Visibility.Visible;
    }

    FrameworkElement CreatePlaceButton(GeoPlace place)
    {
        var row = new Border
        {
            Background = (Brush)FindResource("Control"),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12, 8, 12, 8),
            Margin = new Thickness(0, 0, 0, 4),
            Cursor = Cursors.Hand,
            Child = new StackPanel
            {
                Children =
                {
                    new TextBlock { Text = place.Name, FontWeight = FontWeights.SemiBold },
                    new TextBlock { Text = place.Details, FontSize = 12, Foreground = (Brush)FindResource("DimText") },
                },
            },
        };
        row.MouseEnter += (_, _) => row.Background = (Brush)FindResource("ControlHover");
        row.MouseLeave += (_, _) => row.Background = (Brush)FindResource("Control");
        row.MouseLeftButtonUp += (_, _) =>
        {
            S.City = place.Name;
            S.Latitude = place.Latitude;
            S.Longitude = place.Longitude;
            Changed();
            CurrentCityText.Text = $"Сейчас: {place.Name}" + (place.Details.Length > 0 ? $" ({place.Details})" : "");
            CityResults.Children.Clear();
            CitySearchBox.Text = "";
            _ = RefreshWeatherPreviewAsync();
        };
        return row;
    }

    void Unit_Checked(object sender, RoutedEventArgs e)
    {
        if (_loading || sender is not RadioButton { Tag: string unit }) return;
        S.TemperatureUnit = unit;
        Changed();
        _ = RefreshWeatherPreviewAsync();
    }

    void WeatherRefresh_Click(object sender, RoutedEventArgs e) => _ = RefreshWeatherPreviewAsync();

    async Task RefreshWeatherPreviewAsync()
    {
        WeatherPreviewText.Text = "Загрузка…";
        var w = await _weather.GetAsync(S);
        if (w == null)
        {
            WeatherPreviewIcon.Text = "🌡";
            WeatherPreviewTemp.Text = "—";
            WeatherPreviewText.Text = $"Не удалось получить погоду для «{S.City}»";
            return;
        }
        var (icon, text) = WeatherService.Describe(w.Code);
        WeatherPreviewIcon.Text = icon;
        WeatherPreviewTemp.Text = $"{(int)Math.Round(w.Temperature)}°{(S.TemperatureUnit == "fahrenheit" ? "F" : "C")}";
        WeatherPreviewText.Text = $"{text} · {w.City}";
    }

    #endregion

    #region Календари

    void BuildServices()
    {
        foreach (var service in Services)
        {
            var button = new RadioButton
            {
                Style = (Style)FindResource("Segment"),
                GroupName = "Service",
                Content = service.Title,
                IsChecked = service.Id == _service,
            };
            button.Checked += (_, _) =>
            {
                _service = service.Id;
                UpdateServiceHelp();
            };
            ServiceButtons.Children.Add(button);
        }
        UpdateServiceHelp();
    }

    void UpdateServiceHelp()
    {
        var service = Services.First(s => s.Id == _service);
        ServiceHelp.Text = service.Help;
        ServiceOpenButton.Visibility = service.Site == null ? Visibility.Collapsed : Visibility.Visible;
        ServiceOpenText.Text = (service.SiteTitle ?? "") + " ↗";
    }

    void ServiceOpen_Click(object sender, RoutedEventArgs e)
    {
        var site = Services.First(s => s.Id == _service).Site;
        if (site != null) Process.Start(new ProcessStartInfo(site) { UseShellExecute = true });
    }

    void BuildSwatches()
    {
        foreach (var hex in Palette)
        {
            var color = (Color)ColorConverter.ConvertFromString(hex);
            var swatch = new Border
            {
                Width = 26,
                Height = 26,
                CornerRadius = new CornerRadius(13),
                Margin = new Thickness(0, 0, 8, 0),
                Background = new SolidColorBrush(color),
                BorderThickness = new Thickness(2),
                Cursor = Cursors.Hand,
                Tag = hex,
            };
            swatch.MouseLeftButtonUp += (_, _) =>
            {
                _color = hex;
                UpdateSwatches();
            };
            ColorSwatches.Children.Add(swatch);
        }
        UpdateSwatches();
    }

    void UpdateSwatches()
    {
        foreach (Border swatch in ColorSwatches.Children)
            swatch.BorderBrush = (string)swatch.Tag == _color ? Brushes.White : Brushes.Transparent;
    }

    void RenderCalendars()
    {
        CalendarsList.Children.Clear();
        CalendarsEmpty.Visibility = S.Calendars.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        foreach (var source in S.Calendars) CalendarsList.Children.Add(CreateCalendarRow(source));
    }

    FrameworkElement CreateCalendarRow(CalendarSource source)
    {
        var grid = new Grid { Margin = new Thickness(0, 6, 0, 6) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        Color color;
        try { color = (Color)ColorConverter.ConvertFromString(source.Color); }
        catch { color = Colors.Gray; }
        grid.Children.Add(new System.Windows.Shapes.Ellipse
        {
            Width = 12,
            Height = 12,
            Fill = new SolidColorBrush(color),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 12, 0),
        });

        var service = Services.FirstOrDefault(s => s.Id == source.Service)?.Title ?? "iCal";
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(new TextBlock { Text = source.Name, FontWeight = FontWeights.SemiBold });
        text.Children.Add(new TextBlock
        {
            Text = $"{service} · {ShortUrl(source.Url)}",
            FontSize = 12,
            Foreground = (Brush)FindResource("DimText"),
            TextTrimming = TextTrimming.CharacterEllipsis,
        });
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);

        var toggle = new CheckBox { Style = (Style)FindResource("Toggle"), IsChecked = source.Enabled, Margin = new Thickness(12, 0, 16, 0) };
        toggle.Click += (_, _) =>
        {
            source.Enabled = toggle.IsChecked == true;
            Changed();
        };
        Grid.SetColumn(toggle, 2);
        grid.Children.Add(toggle);

        var remove = new Button
        {
            Style = (Style)FindResource("Link"),
            Content = "Удалить",
            Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0x45, 0x3A)),
            VerticalAlignment = VerticalAlignment.Center,
        };
        remove.Click += (_, _) =>
        {
            S.Calendars.Remove(source);
            Changed();
            RenderCalendars();
        };
        Grid.SetColumn(remove, 3);
        grid.Children.Add(remove);
        return grid;
    }

    static string ShortUrl(string url)
    {
        if (Uri.TryCreate(url.Replace("webcal://", "https://"), UriKind.Absolute, out var uri) && !uri.IsFile) return uri.Host;
        return Path.GetFileName(url);
    }

    async void AddCalendar_Click(object sender, RoutedEventArgs e)
    {
        var url = CalendarUrlBox.Text.Trim();
        if (url.Length == 0)
        {
            SetCalendarStatus("Вставьте ссылку на календарь.", false);
            return;
        }
        if (S.Calendars.Any(c => c.Url == url))
        {
            SetCalendarStatus("Этот календарь уже подключён.", false);
            return;
        }

        AddCalendarButton.IsEnabled = false;
        SetCalendarStatus("Проверяю ссылку…", null);
        var (ok, count, error) = await _calendar.TestAsync(url);
        AddCalendarButton.IsEnabled = true;
        if (!ok)
        {
            SetCalendarStatus(error, false);
            return;
        }

        var service = Services.First(s => s.Id == _service);
        var name = CalendarNameBox.Text.Trim();
        S.Calendars.Add(new CalendarSource
        {
            Name = name.Length > 0 ? name : service.Id == "ical" ? "Календарь" : service.Title,
            Url = url,
            Service = service.Id,
            Color = _color,
        });
        Changed();
        RenderCalendars();
        CalendarNameBox.Text = "";
        CalendarUrlBox.Text = "";
        SetCalendarStatus($"Подключено! Событий на ближайший месяц: {count}.", true);
    }

    void SetCalendarStatus(string text, bool? success)
    {
        CalendarStatus.Text = text;
        CalendarStatus.Foreground = success switch
        {
            true => new SolidColorBrush(Color.FromRgb(0x30, 0xD1, 0x58)),
            false => new SolidColorBrush(Color.FromRgb(0xFF, 0x45, 0x3A)),
            null => (Brush)FindResource("DimText"),
        };
    }

    #endregion

    #region Прочее

    void TestNotification_Click(object sender, RoutedEventArgs e) => _main.SendTestNotification();

    void OpenFolder_Click(object sender, RoutedEventArgs e) =>
        Process.Start(new ProcessStartInfo(Settings.Folder) { UseShellExecute = true });

    void OpenLog_Click(object sender, RoutedEventArgs e)
    {
        var log = Path.Combine(Settings.Folder, "log.txt");
        if (!File.Exists(log)) File.WriteAllText(log, "");
        Process.Start(new ProcessStartInfo("notepad.exe", $"\"{log}\"") { UseShellExecute = true });
    }

    [DllImport("dwmapi.dll")]
    static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    #endregion
}
