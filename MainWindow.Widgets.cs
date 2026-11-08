using System.Windows.Media;
using System.Windows.Media.Animation;

namespace DynamicIsland;

public enum OsdKind
{
    Volume,
    Brightness,
    Charging,
}

/// <summary>Главная (система, погода) и всплывающие индикаторы: громкость, яркость, зарядка.</summary>
public partial class MainWindow
{
    OsdKind _osdKind;
    DateTime _osdUntil;
    bool? _wasPluggedIn;

    void ShowOsd(OsdKind kind, double value, bool muted = false)
    {
        var duration = TimeSpan.FromMilliseconds(200);
        _osdKind = kind;
        _osdUntil = DateTime.Now.AddSeconds(kind == OsdKind.Charging ? 3 : 1.8);
        switch (kind)
        {
            case OsdKind.Volume:
                OsdGlyph.Text = muted || value < 0.005 ? "" : "";
                OsdText.Text = muted ? "—" : $"{value * 100:0}";
                OsdScale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(muted ? 0 : value, duration));
                break;
            case OsdKind.Brightness:
                OsdGlyph.Text = "";
                OsdText.Text = $"{value * 100:0}";
                OsdScale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(value, duration));
                break;
            case OsdKind.Charging:
                ChargeText.Text = $"{value:0}%";
                ChargeScale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0, value / 100, TimeSpan.FromMilliseconds(900))
                {
                    BeginTime = TimeSpan.FromMilliseconds(200),
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
                });
                break;
        }
        UpdateIsland();
    }

    void CheckCharging()
    {
        var power = _system.ReadPower();
        if (!power.HasBattery) return;
        if (_wasPluggedIn == false && power.PluggedIn && _settings.ShowCharging)
            ShowOsd(OsdKind.Charging, power.Percent);
        _wasPluggedIn = power.PluggedIn;
    }

    void UpdateSystemInfo()
    {
        var s = _system.Read();
        var duration = TimeSpan.FromMilliseconds(400);

        CpuText.Text = $"{s.Cpu:0}%";
        CpuScale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(s.Cpu / 100, duration));
        RamText.Text = $"{s.RamUsedGb:0.0} / {s.RamTotalGb:0} ГБ";
        RamScale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(s.Ram / 100, duration));

        if (s.HasBattery)
        {
            BatteryLabel.Text = s.PluggedIn ? "Заряжается" : "Батарея";
            BatteryText.Text = $"{s.Battery}%";
            BatteryFill.Background = s.PluggedIn ? (Brush)FindResource("Green")
                : s.Battery <= 20 ? new SolidColorBrush(Color.FromRgb(0xFF, 0x45, 0x3A))
                : Brushes.White;
            BatteryScale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(s.Battery / 100.0, duration));
        }
        else
        {
            BatteryLabel.Text = "Питание";
            BatteryText.Text = "от сети";
            BatteryFill.Background = (Brush)FindResource("Green");
            BatteryScale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1, duration));
        }
    }

    async Task RefreshWeatherAsync()
    {
        var w = await _weather.GetAsync(_settings);
        if (w == null)
        {
            WeatherDesc.Text = "Нет данных";
            WeatherCity.Text = _settings.City;
            _nextWeather = DateTime.Now.AddMinutes(2);
            return;
        }
        var (icon, text) = WeatherService.Describe(w.Code);
        WeatherIcon.Text = icon;
        WeatherTemp.Text = $"{(int)Math.Round(w.Temperature)}°";
        WeatherDesc.Text = text;
        WeatherCity.Text = w.City;
    }
}
