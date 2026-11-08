using Windows.Foundation;
using Windows.Media.Control;
using Windows.Storage.Streams;
using Session = Windows.Media.Control.GlobalSystemMediaTransportControlsSession;
using PlaybackStatus = Windows.Media.Control.GlobalSystemMediaTransportControlsSessionPlaybackStatus;

namespace DynamicIsland;

public sealed record MediaSnapshot(
    string Title,
    string Artist,
    string Source,
    byte[]? Thumbnail,
    bool IsPlaying,
    TimeSpan Position,
    TimeSpan Duration,
    DateTimeOffset LastUpdated)
{
    public string TrackKey => $"{Source}\u0001{Title}\u0001{Artist}";
}

/// <summary>Следит за системными медиасессиями (Spotify, браузеры, Яндекс Музыка и т.д.).</summary>
public sealed class MediaService
{
    GlobalSystemMediaTransportControlsSessionManager? _manager;
    Session? _session;
    readonly List<Session> _subscribed = new();
    readonly SemaphoreSlim _refreshLock = new(1, 1);
    int _refreshPending;
    string? _thumbKey;
    byte[]? _thumb;

    public MediaSnapshot? Current { get; private set; }

    /// <summary>Срабатывает в фоновом потоке.</summary>
    public event Action? Changed;

    public async Task InitAsync()
    {
        _manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
        _manager.SessionsChanged += (_, _) => { Resubscribe(); RequestRefresh(); };
        _manager.CurrentSessionChanged += (_, _) => RequestRefresh();
        Resubscribe();
        RequestRefresh();
    }

    public Task TogglePlayPauseAsync() => Run(s => s.TryTogglePlayPauseAsync());
    public Task NextAsync() => Run(s => s.TrySkipNextAsync());
    public Task PreviousAsync() => Run(s => s.TrySkipPreviousAsync());
    public Task SeekAsync(TimeSpan position) => Run(s => s.TryChangePlaybackPositionAsync(position.Ticks));

    async Task Run(Func<Session, IAsyncOperation<bool>> action)
    {
        var session = _session;
        if (session == null) return;
        try
        {
            await action(session);
        }
        catch (Exception ex)
        {
            Log.Write("media control", ex);
        }
    }

    void Resubscribe()
    {
        lock (_subscribed)
        {
            foreach (var s in _subscribed)
            {
                try
                {
                    s.MediaPropertiesChanged -= OnMediaPropertiesChanged;
                    s.PlaybackInfoChanged -= OnPlaybackInfoChanged;
                    s.TimelinePropertiesChanged -= OnTimelinePropertiesChanged;
                }
                catch
                {
                    // сессия уже закрыта
                }
            }
            _subscribed.Clear();

            foreach (var s in _manager!.GetSessions())
            {
                s.MediaPropertiesChanged += OnMediaPropertiesChanged;
                s.PlaybackInfoChanged += OnPlaybackInfoChanged;
                s.TimelinePropertiesChanged += OnTimelinePropertiesChanged;
                _subscribed.Add(s);
            }
        }
    }

    void OnMediaPropertiesChanged(Session s, MediaPropertiesChangedEventArgs e) => RequestRefresh();
    void OnPlaybackInfoChanged(Session s, PlaybackInfoChangedEventArgs e) => RequestRefresh();
    void OnTimelinePropertiesChanged(Session s, TimelinePropertiesChangedEventArgs e) => RequestRefresh();

    /// <summary>Склеивает пачку событий в одно обновление.</summary>
    void RequestRefresh()
    {
        if (Interlocked.Exchange(ref _refreshPending, 1) == 1) return;
        _ = Task.Run(async () =>
        {
            await Task.Delay(80);
            Interlocked.Exchange(ref _refreshPending, 0);
            await RefreshAsync();
        });
    }

    async Task RefreshAsync()
    {
        await _refreshLock.WaitAsync();
        try
        {
            var session = PickSession();
            _session = session;
            if (session == null)
            {
                Publish(null);
                return;
            }

            var props = await session.TryGetMediaPropertiesAsync();
            var title = props?.Title ?? "";
            if (props == null || string.IsNullOrWhiteSpace(title))
            {
                Publish(null);
                return;
            }

            var playback = session.GetPlaybackInfo();
            var timeline = session.GetTimelineProperties();
            var artist = !string.IsNullOrWhiteSpace(props.Artist) ? props.Artist : props.AlbumArtist ?? "";
            var source = PrettySource(session.SourceAppUserModelId);

            var key = $"{source}|{title}|{artist}";
            if (key != _thumbKey || _thumb == null)
            {
                _thumb = await ReadAllAsync(props.Thumbnail);
                _thumbKey = key;
            }

            Publish(new MediaSnapshot(
                title,
                artist,
                source,
                _thumb,
                playback?.PlaybackStatus == PlaybackStatus.Playing,
                timeline.Position - timeline.StartTime,
                timeline.EndTime - timeline.StartTime,
                timeline.LastUpdatedTime));
        }
        catch (Exception ex)
        {
            Log.Write("media refresh", ex);
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    void Publish(MediaSnapshot? snapshot)
    {
        Current = snapshot;
        Changed?.Invoke();
    }

    /// <summary>Предпочитаем ту сессию, что реально играет, а не «последнюю активную».</summary>
    Session? PickSession()
    {
        if (_manager == null) return null;
        var sessions = _manager.GetSessions();
        foreach (var s in sessions)
        {
            try
            {
                if (s.GetPlaybackInfo()?.PlaybackStatus == PlaybackStatus.Playing) return s;
            }
            catch
            {
                // сессия могла исчезнуть
            }
        }
        return _manager.GetCurrentSession() ?? sessions.FirstOrDefault();
    }

    static async Task<byte[]?> ReadAllAsync(IRandomAccessStreamReference? reference)
    {
        if (reference == null) return null;
        try
        {
            using var ras = await reference.OpenReadAsync();
            using var stream = ras.AsStreamForRead();
            using var ms = new MemoryStream();
            await stream.CopyToAsync(ms);
            return ms.ToArray();
        }
        catch
        {
            return null;
        }
    }

    static string PrettySource(string? aumid)
    {
        if (string.IsNullOrEmpty(aumid)) return "";
        var lower = aumid.ToLowerInvariant();
        if (lower.Contains("spotify")) return "Spotify";
        if (lower.Contains("yandex")) return "Яндекс";
        if (lower.Contains("chrome")) return "Chrome";
        if (lower.Contains("msedge")) return "Edge";
        if (lower.Contains("opera")) return "Opera";
        if (lower.Contains("firefox") || aumid == "308046B0AF4A39CB") return "Firefox";
        if (lower.Contains("zunemusic")) return "Медиаплеер";
        if (lower.Contains("telegram")) return "Telegram";
        if (lower.Contains("vlc")) return "VLC";

        var name = aumid;
        var bang = name.LastIndexOf('!');
        if (bang >= 0) name = name[(bang + 1)..];
        if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) name = name[..^4];
        return name.Length > 0 ? char.ToUpper(name[0]) + name[1..] : aumid;
    }
}
