using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace DynamicIsland;

/// <summary>Страница календаря и напоминания о ближайших событиях.</summary>
public partial class MainWindow
{
    static readonly Color CalendarColor = Color.FromRgb(0xFF, 0x45, 0x3A);

    List<CalendarEvent> _events = new();
    DateTime _nextCalendar = DateTime.MinValue;
    readonly HashSet<string> _reminded = new();

    async Task RefreshCalendarAsync()
    {
        var today = DateTime.Today;
        _events = await _calendar.LoadAsync(_settings.Calendars, today, today.AddDays(8));
        RenderCalendar();
        CheckReminders();
    }

    void RenderCalendar()
    {
        var now = DateTime.Now;
        CalendarDate.Text = Capitalize(now.ToString("dddd, d MMMM", CultureInfo.CurrentCulture));
        CalendarList.Children.Clear();

        var upcoming = _events
            .Where(e => e.AllDay ? e.End > now.Date : e.End > now || e.Start > now)
            .Take(3)
            .ToList();

        CalendarHint.Text = !_settings.Calendars.Any(c => c.Enabled)
            ? "Нажмите, чтобы подключить календарь\n(Google, Яндекс, Outlook, iCloud)"
            : "Нет событий на ближайшую неделю";
        CalendarHint.TextDecorations = _settings.Calendars.Any(c => c.Enabled) ? null : TextDecorations.Underline;
        CalendarHint.Visibility = upcoming.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        foreach (var e in upcoming)
        {
            var row = new Grid { Margin = new Thickness(0, 0, 0, 8) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition());

            bool now_ = !e.AllDay && e.Start <= now && e.End > now;
            row.Children.Add(new Border
            {
                Width = 3,
                CornerRadius = new CornerRadius(1.5),
                Background = new SolidColorBrush(ParseColor(e.Color)),
                Opacity = now_ ? 1 : 0.7,
            });

            var text = new StackPanel { Margin = new Thickness(10, 0, 0, 0) };
            text.Children.Add(new TextBlock
            {
                Text = e.Title,
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                TextTrimming = TextTrimming.CharacterEllipsis,
            });
            var when = e.AllDay ? "весь день" : now_ ? $"сейчас · до {e.End:HH:mm}" : e.End > e.Start ? $"{e.Start:HH:mm}–{e.End:HH:mm}" : $"{e.Start:HH:mm}";
            var details = $"{DayLabel(e.Start)} · {when}" + (e.Location.Length > 0 ? $" · {e.Location}" : "");
            text.Children.Add(new TextBlock
            {
                Text = details,
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromArgb(0x8E, 0xFF, 0xFF, 0xFF)),
                TextTrimming = TextTrimming.CharacterEllipsis,
            });
            Grid.SetColumn(text, 1);
            row.Children.Add(text);
            CalendarList.Children.Add(row);
        }
    }

    static Color ParseColor(string hex)
    {
        try
        {
            return (Color)ColorConverter.ConvertFromString(hex);
        }
        catch
        {
            return CalendarColor;
        }
    }

    void CalendarHint_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (!_settings.Calendars.Any(c => c.Enabled)) OpenSettings("calendars");
    }

    static string DayLabel(DateTime date)
    {
        var days = (date.Date - DateTime.Today).Days;
        return days switch
        {
            <= 0 => "Сегодня",
            1 => "Завтра",
            _ => date.ToString("ddd, d MMM", CultureInfo.CurrentCulture),
        };
    }

    void CheckReminders()
    {
        var now = DateTime.Now;
        var window = TimeSpan.FromMinutes(Math.Max(1, _settings.ReminderMinutes));
        foreach (var e in _events)
        {
            if (e.AllDay || e.Start <= now || e.Start - now > window) continue;
            if (!_reminded.Add($"{e.Uid}|{e.Start:O}")) continue;

            var minutes = (int)Math.Ceiling((e.Start - now).TotalMinutes);
            Enqueue(new IslandNotification
            {
                Kind = NotificationKind.Reminder,
                AppName = "Календарь",
                Title = e.Title,
                Body = $"Через {minutes} мин · {e.Start:HH:mm}" + (e.Location.Length > 0 ? $" · {e.Location}" : ""),
                Glyph = "",
                GlyphColor = CalendarColor,
            });
        }
    }
}
