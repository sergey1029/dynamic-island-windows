using Windows.Foundation.Metadata;
using Windows.UI.Notifications;
using Windows.UI.Notifications.Management;

namespace DynamicIsland;

/// <summary>Читает тосты Windows через UserNotificationListener (опрос раз в секунду).</summary>
public sealed class NotificationService
{
    UserNotificationListener? _listener;
    HashSet<uint> _known = new();
    Timer? _timer;
    int _polling;

    public string Status { get; private set; } = "";

    /// <summary>Срабатывает в фоновом потоке.</summary>
    public event Action<IslandNotification>? Received;

    /// <summary>Вызывать из UI-потока: может показать системный запрос доступа.</summary>
    public async Task<bool> StartAsync()
    {
        try
        {
            if (!ApiInformation.IsTypePresent("Windows.UI.Notifications.Management.UserNotificationListener"))
            {
                Status = "API уведомлений недоступно в этой версии Windows";
                return false;
            }

            _listener = UserNotificationListener.Current;
            var access = await _listener.RequestAccessAsync();
            if (access != UserNotificationListenerAccessStatus.Allowed)
            {
                Status = $"доступ: {access}";
                return false;
            }

            foreach (var n in await _listener.GetNotificationsAsync(NotificationKinds.Toast))
                _known.Add(n.Id);

            _timer = new Timer(_ => _ = PollAsync(), null, 1000, 1000);
            Status = "OK";
            return true;
        }
        catch (Exception ex)
        {
            Status = ex.Message;
            Log.Write("notifications start", ex);
            return false;
        }
    }

    async Task PollAsync()
    {
        if (Interlocked.Exchange(ref _polling, 1) == 1) return;
        try
        {
            var list = await _listener!.GetNotificationsAsync(NotificationKinds.Toast);
            var current = new HashSet<uint>();
            var fresh = new List<UserNotification>();
            foreach (var n in list)
            {
                current.Add(n.Id);
                if (!_known.Contains(n.Id)) fresh.Add(n);
            }
            _known = current;

            foreach (var n in fresh.OrderBy(n => n.CreationTime))
            {
                if (DateTimeOffset.Now - n.CreationTime > TimeSpan.FromMinutes(1)) continue;
                var item = await ConvertAsync(n);
                if (item != null) Received?.Invoke(item);
            }
        }
        catch (Exception ex)
        {
            Log.Write("notifications poll", ex);
        }
        finally
        {
            Volatile.Write(ref _polling, 0);
        }
    }

    static async Task<IslandNotification?> ConvertAsync(UserNotification n)
    {
        var binding = n.Notification?.Visual?.GetBinding(KnownNotificationBindings.ToastGeneric);
        if (binding == null) return null;
        var texts = binding.GetTextElements()
            .Select(t => t.Text)
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .ToList();
        if (texts.Count == 0) return null;

        var app = "Уведомление";
        string? appId = null;
        byte[]? icon = null;
        try
        {
            app = n.AppInfo.DisplayInfo.DisplayName;
            appId = n.AppInfo.AppUserModelId;
        }
        catch
        {
            // у некоторых Win32-приложений нет AppInfo
        }
        try
        {
            var logo = n.AppInfo.DisplayInfo.GetLogo(new Windows.Foundation.Size(64, 64));
            using var ras = await logo.OpenReadAsync();
            using var stream = ras.AsStreamForRead();
            using var ms = new MemoryStream();
            await stream.CopyToAsync(ms);
            icon = ms.ToArray();
        }
        catch
        {
            // без иконки покажем букву
        }

        return new IslandNotification
        {
            AppName = app,
            Title = texts[0],
            Body = string.Join(" · ", texts.Skip(1)),
            IconBytes = icon,
            AppId = appId,
        };
    }
}
