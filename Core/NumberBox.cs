using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace DynamicIsland;

/// <summary>Числовой степпер «− значение +» для окна настроек.</summary>
public sealed class NumberBox : Border
{
    readonly TextBlock _text;
    double _value;

    public double Minimum { get; set; }
    public double Maximum { get; set; } = 100;
    public double Step { get; set; } = 1;
    public string Suffix { get; set; } = "";

    public event EventHandler? ValueChanged;

    public NumberBox()
    {
        Background = new SolidColorBrush(Color.FromRgb(0x2C, 0x2C, 0x2E));
        CornerRadius = new CornerRadius(8);
        Height = 32;
        HorizontalAlignment = HorizontalAlignment.Right;

        _text = new TextBlock
        {
            MinWidth = 64,
            TextAlignment = TextAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = Brushes.White,
            FontSize = 13,
        };
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        panel.Children.Add(StepButton("", -1));
        panel.Children.Add(_text);
        panel.Children.Add(StepButton("", +1));
        Child = panel;
        Render();
    }

    public double Value
    {
        get => _value;
        set => SetValue(value, notify: true);
    }

    /// <summary>Установить значение без события (при загрузке настроек).</summary>
    public void SetSilently(double value) => SetValue(value, notify: false);

    void SetValue(double value, bool notify)
    {
        value = Math.Clamp(value, Minimum, Maximum);
        if (value == _value && _text.Text.Length > 0) return;
        _value = value;
        Render();
        if (notify) ValueChanged?.Invoke(this, EventArgs.Empty);
    }

    void Render() => _text.Text = _value.ToString("0.##", CultureInfo.CurrentCulture) + Suffix;

    FrameworkElement StepButton(string glyph, int direction)
    {
        var button = new Border
        {
            Width = 32,
            Background = Brushes.Transparent,
            CornerRadius = new CornerRadius(8),
            Cursor = Cursors.Hand,
            Child = new TextBlock
            {
                Text = glyph,
                FontFamily = new FontFamily("Segoe MDL2 Assets"),
                FontSize = 11,
                Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        };
        button.MouseEnter += (_, _) => button.Background = new SolidColorBrush(Color.FromRgb(0x3A, 0x3A, 0x3C));
        button.MouseLeave += (_, _) => button.Background = Brushes.Transparent;
        button.MouseLeftButtonUp += (_, _) => Value = _value + direction * Step;
        return button;
    }
}
