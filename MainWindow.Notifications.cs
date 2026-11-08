using System.Diagnostics;
using System.Media;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace DynamicIsland;

/// <summary>Очередь уведомлений, открытие приложения по клику, звук будильника.</summary>
public partial class MainWindow
{
    readonly Queue<IslandNotification> _queue = new();
    IslandNotification? _notification;
    readonly DispatcherTimer _notificationTimer = new();
    SoundPlayer? _alarmPlayer;

    void Enqueue(IslandNotification n)
    {
        if (n.Kind == NotificationKind.Track)
        {
            // о старых сменах трека в очереди уже нет смысла сообщать
            var rest = _queue.Where(q => q.Kind != NotificationKind.Track).ToList();
            _queue.Clear();
            foreach (var q in rest) _queue.Enqueue(q);
        }
        _queue.Enqueue(n);
        if (_notification == null || _notification.Kind == NotificationKind.Track) ShowNextNotification();
    }

    void ShowNextNotification()
    {
        _notificationTimer.Stop();
        StopAlarm();
        if (_queue.Count == 0)
        {
            _notification = null;
            UpdateIsland();
            return;
        }

        var n = _queue.Dequeue();
        bool replacing = _notification != null;
        _notification = n;

        NotifyApp.Text = n.AppName;
        NotifyTitle.Text = n.Title;
        NotifyBody.Text = n.Body.Replace('\n', ' ');
        NotifyBody.Visibility = string.IsNullOrWhiteSpace(n.Body) ? Visibility.Collapsed : Visibility.Visible;
        NotifyTime.Visibility = n.Kind is NotificationKind.App or NotificationKind.Info ? Visibility.Visible : Visibility.Collapsed;
        SetNotifyIcon(n.Image ?? ImageHelper.Load(n.IconBytes, 96), n);

        _notificationTimer.Interval = n.Kind switch
        {
            NotificationKind.Track => TimeSpan.FromSeconds(3),
            NotificationKind.Alarm => TimeSpan.FromSeconds(60),
            NotificationKind.Reminder => TimeSpan.FromSeconds(15),
            _ => TimeSpan.FromSeconds(Math.Max(1, _settings.NotificationSeconds)),
        };
        if (!IslandHost.IsMouseOver || n.Kind == NotificationKind.Alarm) _notificationTimer.Start();
        if (n.Kind == NotificationKind.Alarm) StartAlarm();

        if (replacing && _activePanel == PanelNotify)
            PanelNotify.BeginAnimation(OpacityProperty, new DoubleAnimation(0.2, 1, TimeSpan.FromMilliseconds(220)));
        UpdateIsland();
    }

    void SetNotifyIcon(ImageSource? image, IslandNotification n)
    {
        NotifyIcon.CornerRadius = new CornerRadius(n.Kind == NotificationKind.Track ? 10 : 13);
        if (n.Glyph != null)
        {
            NotifyIcon.Background = new SolidColorBrush(n.GlyphColor ?? Colors.Gray);
            NotifyInitial.FontFamily = new FontFamily("Segoe MDL2 Assets");
            NotifyInitial.Text = n.Glyph;
        }
        else if (image != null)
        {
            NotifyIcon.Background = new ImageBrush(image) { Stretch = Stretch.UniformToFill };
            NotifyInitial.Text = "";
        }
        else
        {
            NotifyIcon.Background = new SolidColorBrush(ColorFor(n.AppName));
            NotifyInitial.FontFamily = FontFamily;
            NotifyInitial.Text = string.IsNullOrEmpty(n.AppName) ? "•" : char.ToUpper(n.AppName[0]).ToString();
        }
    }

    /// <summary>Клик по уведомлению: открыть приложение-источник и закрыть уведомление.</summary>
    void OpenNotification()
    {
        var n = _notification;
        ShowNextNotification();
        if (n?.AppId is not { Length: > 0 } appId) return;
        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"shell:AppsFolder\\{appId}") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Log.Write("open app " + appId, ex);
        }
    }

    void NotifyClose_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        ShowNextNotification();
    }

    void StartAlarm()
    {
        try
        {
            var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Media", "Alarm01.wav");
            if (File.Exists(path))
            {
                _alarmPlayer = new SoundPlayer(path);
                _alarmPlayer.PlayLooping();
            }
            else
            {
                SystemSounds.Exclamation.Play();
            }
        }
        catch (Exception ex)
        {
            Log.Write("alarm", ex);
        }
    }

    void StopAlarm()
    {
        _alarmPlayer?.Stop();
        _alarmPlayer?.Dispose();
        _alarmPlayer = null;
    }

    static Color ColorFor(string name)
    {
        Color[] palette =
        [
            Color.FromRgb(0x0A, 0x84, 0xFF), Color.FromRgb(0x30, 0xD1, 0x58), Color.FromRgb(0xFF, 0x9F, 0x0A),
            Color.FromRgb(0xFF, 0x37, 0x5F), Color.FromRgb(0xBF, 0x5A, 0xF2), Color.FromRgb(0x64, 0xD2, 0xFF),
        ];
        int hash = 0;
        foreach (var c in name) hash = hash * 31 + c;
        return palette[Math.Abs(hash % palette.Length)];
    }
}
