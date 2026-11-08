using System.Management;

namespace DynamicIsland;

/// <summary>Изменения яркости встроенного экрана (ноутбуки). На внешних мониторах WMI молчит.</summary>
public sealed class BrightnessService : IDisposable
{
    ManagementEventWatcher? _watcher;

    /// <summary>Яркость 0..100. Срабатывает в фоновом потоке.</summary>
    public event Action<int>? Changed;

    public bool Start()
    {
        try
        {
            _watcher = new ManagementEventWatcher(
                new ManagementScope(@"root\WMI"),
                new EventQuery("SELECT * FROM WmiMonitorBrightnessEvent"));
            _watcher.EventArrived += (_, e) => Changed?.Invoke(Convert.ToInt32(e.NewEvent["Brightness"]));
            _watcher.Start();
            return true;
        }
        catch
        {
            _watcher = null;
            return false;
        }
    }

    public void Dispose() => _watcher?.Dispose();
}
