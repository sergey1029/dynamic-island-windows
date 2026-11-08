using System.Windows;

namespace DynamicIsland;

public partial class App : Application
{
    const string ShowSettingsEventName = "DynamicIsland.ShowSettings";

    Mutex? _mutex;
    EventWaitHandle? _showSettings;

    /// <summary>Режим разработчика: "screenshots" или "assets" (см. MainWindow.Showcase.cs).</summary>
    public static string? ShowcaseMode { get; private set; }
    public static string? ShowcaseDir { get; private set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        int flag = Array.FindIndex(e.Args, a => a is "--screenshots" or "--assets");
        if (flag >= 0 && flag + 1 < e.Args.Length)
        {
            ShowcaseMode = e.Args[flag].TrimStart('-');
            ShowcaseDir = Path.GetFullPath(e.Args[flag + 1]);
            base.OnStartup(e);
            // рисуем за пределами экрана, чтобы ничего не мелькало
            MainWindow = new MainWindow { WindowStartupLocation = WindowStartupLocation.Manual, Left = -10000, Top = 0 };
            MainWindow.Show();
            return;
        }

        _mutex = new Mutex(true, "DynamicIsland.SingleInstance", out var created);
        if (!created)
        {
            // остров уже запущен — повторный запуск открывает его настройки
            try
            {
                EventWaitHandle.OpenExisting(ShowSettingsEventName).Set();
            }
            catch
            {
                // первая копия ещё не успела создать событие
            }
            Shutdown();
            return;
        }

        DispatcherUnhandledException += (_, args) =>
        {
            Log.Write("ui", args.Exception);
            args.Handled = true;
        };
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Log.Write("task", args.Exception);
            args.SetObserved();
        };

        base.OnStartup(e);
        var window = new MainWindow();
        MainWindow = window;
        window.Show();

        _showSettings = new EventWaitHandle(false, EventResetMode.AutoReset, ShowSettingsEventName);
        new Thread(() =>
        {
            while (_showSettings.WaitOne())
                Dispatcher.Invoke(() => window.OpenSettings());
        }) { IsBackground = true }.Start();

        if (e.Args.Contains("--settings")) window.OpenSettings();
    }
}
