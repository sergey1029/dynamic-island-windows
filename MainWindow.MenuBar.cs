using System.Windows;
using System.Windows.Media;

namespace DynamicIsland;

/// <summary>
/// Встраивание в строку меню оболочки Custom UI. Если она запущена и разрешает это
/// (свойство окна «CustomUI.IslandDock»), остров становится её «вырезом», как у MacBook:
/// прилегает к верхнему краю без отступа, в компактном виде высотой со строку меню,
/// верхние углы прямые и плавно переходят в край экрана, а раскрывается он прямо из строки.
/// Строка меню сама оставляет под остров место посередине и держится под ним.
/// </summary>
public partial class MainWindow
{
    const double FloatingHeight = 34;
    const string MenuBarClass = "CustomUI.MenuBar";
    const string DockProperty = "CustomUI.IslandDock";

    bool _docked;
    double _dockHeight = FloatingHeight;

    double CompactHeight => _docked ? _dockHeight : FloatingHeight;
    double TopOffset => _docked ? 0 : _settings.TopOffset;

    void CheckMenuBar()
    {
        bool docked = false;
        double height = FloatingHeight;
        var bar = Native.FindWindow(MenuBarClass, null);
        if (bar != IntPtr.Zero && Native.IsWindowVisible(bar) && Native.GetProp(bar, DockProperty) != IntPtr.Zero
            && Native.GetWindowRect(bar, out var r) && r.Top <= 0 && r.Bottom > r.Top)
        {
            docked = true;
            height = Math.Round((r.Bottom - r.Top) / _dpiY);
        }
        if (docked == _docked && height == _dockHeight) return;

        _docked = docked;
        _dockHeight = height;
        Log.Write(docked ? $"встроен в строку меню Custom UI, высота {height}" : "отдельно от строки меню");
        ApplyDock();
    }

    void ApplyDock()
    {
        foreach (var panel in new[] { PanelIdle, PanelMusicCompact, PanelTimerCompact, PanelOsd, PanelCharging })
            panel.Height = CompactHeight;
        // обложка с небольшим полем сверху и снизу
        double art = Math.Min(24, CompactHeight - 8);
        CompactArt.Width = CompactArt.Height = art;
        CompactArt.CornerRadius = new CornerRadius(art * 0.3);
        CompactArt.Margin = new Thickness(_docked ? 10 : 6, 0, 0, 0);

        var ears = _docked ? Visibility.Visible : Visibility.Collapsed;
        EarLeft.Visibility = EarRight.Visibility = ears;

        PositionWindow();
        if (IslandHost.ActualWidth > 0) UpdateShape(IslandHost.ActualWidth, IslandHost.ActualHeight);
        UpdateForeground();
        UpdateIsland();
    }

    /// <summary>Форма острова: пилюля на отступе либо «вырез» с прямыми верхними углами.</summary>
    void UpdateShape(double w, double h)
    {
        if (_docked)
        {
            // у компактного выреза углы небольшие, у раскрытого — как у обычного острова
            double r = Math.Min(h * 0.36, 26);
            IslandBg.CornerRadius = new CornerRadius(0, 0, r, r);
            // скругление выше верхнего края, чтобы сверху углы остались прямыми
            ContentRoot.Clip = new RectangleGeometry(new Rect(0, -r, w, h + r), r, r);
        }
        else
        {
            double r = Math.Min(h / 2, 30);
            IslandBg.CornerRadius = new CornerRadius(r);
            ContentRoot.Clip = new RectangleGeometry(new Rect(0, 0, w, h), r, r);
        }
    }
}
