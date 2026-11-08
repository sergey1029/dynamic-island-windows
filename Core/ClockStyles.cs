using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Effects;
using Shapes = System.Windows.Shapes;

namespace DynamicIsland;

/// <summary>
/// Стиль часов на главной странице острова. Чтобы добавить новый стиль — реализуй этот интерфейс
/// и допиши экземпляр в <see cref="ClockStyles.All"/>; в настройках он появится автоматически.
/// </summary>
public interface IClockStyle
{
    string Id { get; }
    string Name { get; }
    string Description { get; }

    /// <summary>Создаёт визуальное дерево часов и функцию, которая обновляет его под текущее время.</summary>
    FrameworkElement Create(out Action<DateTime> update);
}

public static class ClockStyles
{
    public static IReadOnlyList<IClockStyle> All { get; } =
    [
        new ClassicClock(),
        new BoldClock(),
        new AnalogClock(),
        new StackedClock(),
        new NeonClock(),
    ];

    public static IClockStyle Get(string? id) => All.FirstOrDefault(s => s.Id == id) ?? All[0];

    internal static readonly Brush Dim = Freeze(new SolidColorBrush(Color.FromArgb(0x8E, 0xFF, 0xFF, 0xFF)));
    internal static readonly Brush Orange = Freeze(new SolidColorBrush(Color.FromRgb(0xFF, 0x9F, 0x0A)));

    internal static Brush Freeze(Brush brush)
    {
        brush.Freeze();
        return brush;
    }

    internal static string DateLabel(DateTime now)
    {
        var s = now.ToString("dddd, d MMMM", CultureInfo.CurrentCulture);
        return s.Length == 0 ? s : char.ToUpper(s[0]) + s[1..];
    }

    internal static TextBlock Text(double size, FontWeight weight, Brush? foreground = null) => new()
    {
        FontSize = size,
        FontWeight = weight,
        Foreground = foreground ?? Brushes.White,
    };
}

/// <summary>Отображает выбранный стиль часов; используется и на острове, и в превью настроек.</summary>
public sealed class ClockFace : ContentControl
{
    string? _styleId;
    Action<DateTime>? _update;

    public void SetStyle(string? id)
    {
        var style = ClockStyles.Get(id);
        if (style.Id == _styleId) return;
        _styleId = style.Id;
        Content = style.Create(out var update);
        _update = update;
        update(DateTime.Now);
    }

    public void Update(DateTime now) => _update?.Invoke(now);
}

sealed class ClassicClock : IClockStyle
{
    public string Id => "classic";
    public string Name => "Классика";
    public string Description => "Тонкие цифры и дата";

    public FrameworkElement Create(out Action<DateTime> update)
    {
        var time = ClockStyles.Text(40, FontWeights.Light);
        var date = ClockStyles.Text(12, FontWeights.Normal, ClockStyles.Dim);
        date.TextWrapping = TextWrapping.Wrap;
        date.MaxWidth = 112;
        update = now =>
        {
            time.Text = now.ToString("HH:mm");
            date.Text = ClockStyles.DateLabel(now);
        };
        return new StackPanel { Children = { time, date } };
    }
}

sealed class BoldClock : IClockStyle
{
    public string Id => "bold";
    public string Name => "Жирный";
    public string Description => "Крупные цифры с оранжевыми минутами";

    public FrameworkElement Create(out Action<DateTime> update)
    {
        var date = ClockStyles.Text(11, FontWeights.SemiBold, ClockStyles.Orange);
        var hours = ClockStyles.Text(44, FontWeights.Bold);
        var colon = ClockStyles.Text(44, FontWeights.Bold, ClockStyles.Dim);
        colon.Text = ":";
        colon.Margin = new Thickness(1, -4, 1, 0);
        var minutes = ClockStyles.Text(44, FontWeights.Bold, ClockStyles.Orange);
        update = now =>
        {
            date.Text = now.ToString("ddd, d MMM", CultureInfo.CurrentCulture).ToUpper(CultureInfo.CurrentCulture);
            hours.Text = now.ToString("HH");
            minutes.Text = now.ToString("mm");
        };
        var line = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, -4, 0, 0), Children = { hours, colon, minutes } };
        return new StackPanel { Children = { date, line } };
    }
}

sealed class AnalogClock : IClockStyle
{
    const double Size = 96, C = Size / 2;

    public string Id => "analog";
    public string Name => "Стрелки";
    public string Description => "Циферблат с плавной секундной стрелкой";

    public FrameworkElement Create(out Action<DateTime> update)
    {
        var canvas = new Canvas { Width = Size, Height = Size };
        canvas.Children.Add(new Shapes.Ellipse
        {
            Width = Size,
            Height = Size,
            Fill = new SolidColorBrush(Color.FromRgb(0x1C, 0x1C, 0x1E)),
            Stroke = new SolidColorBrush(Color.FromRgb(0x3A, 0x3A, 0x3C)),
            StrokeThickness = 1,
        });
        for (int i = 0; i < 12; i++)
        {
            bool major = i % 3 == 0;
            canvas.Children.Add(new Shapes.Line
            {
                X1 = C, Y1 = 6, X2 = C, Y2 = major ? 14 : 10,
                Stroke = major ? Brushes.White : ClockStyles.Dim,
                StrokeThickness = major ? 2 : 1.2,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                RenderTransform = new RotateTransform(i * 30, C, C),
            });
        }

        var hour = Hand(24, 3.5, Brushes.White);
        var minute = Hand(36, 2.5, Brushes.White);
        var second = Hand(40, 1.2, ClockStyles.Orange);
        canvas.Children.Add(hour.Line);
        canvas.Children.Add(minute.Line);
        canvas.Children.Add(second.Line);
        var dot = new Shapes.Ellipse { Width = 6, Height = 6, Fill = ClockStyles.Orange };
        Canvas.SetLeft(dot, C - 3);
        Canvas.SetTop(dot, C - 3);
        canvas.Children.Add(dot);

        update = now =>
        {
            double s = now.Second + now.Millisecond / 1000.0;
            double m = now.Minute + s / 60;
            hour.Rotate.Angle = (now.Hour % 12 + m / 60) * 30;
            minute.Rotate.Angle = m * 6;
            second.Rotate.Angle = s * 6;
        };
        return canvas;
    }

    static (Shapes.Line Line, RotateTransform Rotate) Hand(double length, double thickness, Brush brush)
    {
        var rotate = new RotateTransform(0, C, C);
        var line = new Shapes.Line
        {
            X1 = C, Y1 = C + 6, X2 = C, Y2 = C - length,
            Stroke = brush,
            StrokeThickness = thickness,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            RenderTransform = rotate,
        };
        return (line, rotate);
    }
}

sealed class StackedClock : IClockStyle
{
    public string Id => "stacked";
    public string Name => "Стопка";
    public string Description => "Часы над минутами и календарный листок";

    public FrameworkElement Create(out Action<DateTime> update)
    {
        TextBlock Digits(Brush brush)
        {
            var t = ClockStyles.Text(42, FontWeights.SemiBold, brush);
            t.LineHeight = 40;
            t.LineStackingStrategy = LineStackingStrategy.BlockLineHeight;
            return t;
        }

        var hours = Digits(Brushes.White);
        var minutes = Digits(ClockStyles.Dim);
        var weekday = ClockStyles.Text(12, FontWeights.SemiBold, ClockStyles.Orange);
        var day = ClockStyles.Text(26, FontWeights.SemiBold);
        day.Margin = new Thickness(0, -2, 0, -2);
        var month = ClockStyles.Text(11, FontWeights.Normal, ClockStyles.Dim);

        update = now =>
        {
            hours.Text = now.ToString("HH");
            minutes.Text = now.ToString("mm");
            weekday.Text = now.ToString("ddd", CultureInfo.CurrentCulture).ToUpper(CultureInfo.CurrentCulture);
            day.Text = now.Day.ToString();
            month.Text = now.ToString("MMM", CultureInfo.CurrentCulture);
        };

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var digits = new StackPanel { Children = { hours, minutes } };
        var sheet = new StackPanel { Margin = new Thickness(12, 4, 0, 0), VerticalAlignment = VerticalAlignment.Center, Children = { weekday, day, month } };
        Grid.SetColumn(sheet, 1);
        grid.Children.Add(digits);
        grid.Children.Add(sheet);
        return grid;
    }
}

sealed class NeonClock : IClockStyle
{
    static readonly Color Cyan = Color.FromRgb(0x64, 0xD2, 0xFF);

    public string Id => "neon";
    public string Name => "Неон";
    public string Description => "Светящиеся цифры с мигающим двоеточием";

    public FrameworkElement Create(out Action<DateTime> update)
    {
        var bright = ClockStyles.Freeze(new SolidColorBrush(Cyan));
        var faded = ClockStyles.Freeze(new SolidColorBrush(Color.FromArgb(0x40, Cyan.R, Cyan.G, Cyan.B)));

        var hours = new Run();
        var colon = new Run(":");
        var minutes = new Run();
        var time = ClockStyles.Text(40, FontWeights.Light, bright);
        time.Inlines.Add(hours);
        time.Inlines.Add(colon);
        time.Inlines.Add(minutes);
        time.Effect = new DropShadowEffect { Color = Cyan, BlurRadius = 18, ShadowDepth = 0, Opacity = 0.9 };

        var seconds = ClockStyles.Text(12, FontWeights.Normal, faded);
        var date = ClockStyles.Text(12, FontWeights.Normal, ClockStyles.Freeze(new SolidColorBrush(Color.FromArgb(0xB0, Cyan.R, Cyan.G, Cyan.B))));

        update = now =>
        {
            hours.Text = now.ToString("HH");
            minutes.Text = now.ToString("mm");
            colon.Foreground = now.Millisecond < 500 ? bright : faded;
            seconds.Text = now.ToString("ss");
            date.Text = ClockStyles.DateLabel(now);
        };

        seconds.Margin = new Thickness(4, 0, 0, 8);
        seconds.VerticalAlignment = VerticalAlignment.Bottom;
        var line = new StackPanel { Orientation = Orientation.Horizontal, Children = { time, seconds } };
        date.TextWrapping = TextWrapping.Wrap;
        date.MaxWidth = 112;
        return new StackPanel { Children = { line, date } };
    }
}
