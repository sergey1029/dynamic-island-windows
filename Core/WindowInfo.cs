using System.Runtime.InteropServices;

namespace DynamicIsland;

public enum ForegroundKind
{
    /// <summary>Временное системное окно (Пуск, Alt+Tab, меню) — состояние не меняем.</summary>
    Unknown,
    None,
    Window,
    Maximized,
    Fullscreen,
}

readonly record struct ForegroundState(ForegroundKind Kind, Native.RECT Bounds);

/// <summary>Определяет, что сейчас на переднем плане основного монитора.</summary>
static class WindowInfo
{
    static readonly HashSet<string> DesktopClasses = ["Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd"];

    static readonly HashSet<string> TransientClasses =
    [
        "Windows.UI.Core.CoreWindow", "XamlExplorerHostIslandWindow", "ForegroundStaging", "MultitaskingViewFrame",
        "#32768", "TaskListThumbnailWnd", "NotifyIconOverflowWindow", "TopLevelWindowForOverflowXamlIsland",
    ];

    public static ForegroundState GetForeground()
    {
        var hwnd = Native.GetForegroundWindow();
        if (hwnd == IntPtr.Zero) return new(ForegroundKind.Unknown, default);

        Native.GetWindowThreadProcessId(hwnd, out var pid);
        if (pid == Environment.ProcessId) return new(ForegroundKind.Unknown, default);

        var cls = Native.ClassName(hwnd);
        if (DesktopClasses.Contains(cls)) return new(ForegroundKind.None, default);
        if (TransientClasses.Contains(cls)) return new(ForegroundKind.Unknown, default);
        if (!Native.IsWindowVisible(hwnd) || Native.IsIconic(hwnd) || Native.IsCloaked(hwnd))
            return new(ForegroundKind.None, default);

        var primary = Native.MonitorFromPoint(new Native.POINT(), Native.MONITOR_DEFAULTTOPRIMARY);
        if (Native.MonitorFromWindow(hwnd, Native.MONITOR_DEFAULTTONEAREST) != primary)
            return new(ForegroundKind.None, default);

        var bounds = Native.GetFrameBounds(hwnd);
        if (Native.IsZoomed(hwnd)) return new(ForegroundKind.Maximized, bounds);

        var info = new Native.MONITORINFO { cbSize = Marshal.SizeOf<Native.MONITORINFO>() };
        Native.GetMonitorInfo(primary, ref info);
        var m = info.rcMonitor;
        bool fullscreen = bounds.Left <= m.Left && bounds.Top <= m.Top && bounds.Right >= m.Right && bounds.Bottom >= m.Bottom;
        return new(fullscreen ? ForegroundKind.Fullscreen : ForegroundKind.Window, bounds);
    }
}
