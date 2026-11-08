using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace DynamicIsland;

/// <summary>Музыкальный плеер: обложка, эквалайзер, прогресс, кнопки.</summary>
public partial class MainWindow
{
    MediaSnapshot? _music;
    DateTime _lastPlayingAt = DateTime.MinValue;
    string? _artKey;
    string? _lastTrackKey;
    ImageSource? _art;
    TimeSpan _posBase;
    DateTime _posBaseAt;
    bool _posRunning;
    (TimeSpan, DateTimeOffset) _timelineKey;

    readonly SolidColorBrush _accent = new(Colors.White);
    readonly List<ScaleTransform> _bars = new();
    bool? _barsPlaying;
    readonly Random _random = new();

    bool HasMusic => _music != null;

    bool MusicActive => _music != null &&
        (_music.IsPlaying || DateTime.Now - _lastPlayingAt < TimeSpan.FromSeconds(_settings.PausedMusicSeconds));

    void BuildEqualizer(Panel host, int count, double height)
    {
        for (int i = 0; i < count; i++)
        {
            var scale = new ScaleTransform(1, 0.25);
            host.Children.Add(new System.Windows.Shapes.Rectangle
            {
                Width = 3,
                Height = height,
                RadiusX = 1.5,
                RadiusY = 1.5,
                Fill = _accent,
                Margin = new Thickness(1.5, 0, 1.5, 0),
                RenderTransformOrigin = new Point(0.5, 0.5),
                RenderTransform = scale,
            });
            _bars.Add(scale);
        }
    }

    void SetBarsPlaying(bool playing)
    {
        if (_barsPlaying == playing) return;
        _barsPlaying = playing;
        foreach (var bar in _bars)
        {
            if (playing)
            {
                bar.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(
                    0.2 + _random.NextDouble() * 0.2,
                    0.75 + _random.NextDouble() * 0.25,
                    TimeSpan.FromMilliseconds(260 + _random.Next(320)))
                {
                    AutoReverse = true,
                    RepeatBehavior = RepeatBehavior.Forever,
                    EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
                });
            }
            else
            {
                bar.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.25, TimeSpan.FromMilliseconds(250)));
            }
        }
    }

    void ApplyMedia() => ApplyMedia(_media.Current);

    void ApplyMedia(MediaSnapshot? m)
    {
        _music = m;
        if (m == null)
        {
            SetBarsPlaying(false);
            UpdateIsland();
            return;
        }

        var now = DateTime.Now;
        if (m.IsPlaying) _lastPlayingAt = now;
        MusicTitle.Text = m.Title;
        MusicArtist.Text = m.Artist;
        MusicSource.Text = m.Source;
        PlayPauseButton.Content = m.IsPlaying ? "" : "";
        SetBarsPlaying(m.IsPlaying);

        // обложка и акцентный цвет (обложка часто приходит чуть позже названия)
        var artKey = m.TrackKey + ":" + (m.Thumbnail?.Length ?? 0);
        if (artKey != _artKey)
        {
            _artKey = artKey;
            var bitmap = ImageHelper.Load(m.Thumbnail, 200);
            _art = bitmap;
            var brush = bitmap != null
                ? new ImageBrush(bitmap) { Stretch = Stretch.UniformToFill }
                : (Brush)FindResource("ArtFallback");
            CompactArt.Background = brush;
            MusicArt.Background = brush;
            var glyph = bitmap == null ? Visibility.Visible : Visibility.Collapsed;
            CompactArtGlyph.Visibility = glyph;
            MusicArtGlyph.Visibility = glyph;

            var accent = bitmap != null ? ImageHelper.GetAccent(bitmap) : Colors.White;
            _accent.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation(accent, TimeSpan.FromMilliseconds(400)));

            if (_notification is { Kind: NotificationKind.Track } && _notification.Title == m.Title && bitmap != null)
                SetNotifyIcon(bitmap, _notification);
        }

        // позиция: интерполируем сами, т.к. приложения обновляют таймлайн редко
        bool trackChanged = m.TrackKey != _lastTrackKey;
        var timelineKey = (m.Position, m.LastUpdated);
        if (timelineKey != _timelineKey)
        {
            _timelineKey = timelineKey;
            var pos = m.Position;
            if (m.IsPlaying && m.LastUpdated.Year > 2000)
            {
                var lag = DateTimeOffset.Now - m.LastUpdated;
                if (lag > TimeSpan.Zero && lag < TimeSpan.FromHours(1)) pos += lag;
            }
            _posBase = pos;
        }
        else
        {
            _posBase = trackChanged ? TimeSpan.Zero : CurrentPosition();
        }
        _posBaseAt = now;
        _posRunning = m.IsPlaying;

        if (trackChanged)
        {
            bool first = _lastTrackKey == null;
            _lastTrackKey = m.TrackKey;
            if (!first && m.IsPlaying && _settings.ShowTrackChanges && !Expanded)
            {
                Enqueue(new IslandNotification
                {
                    Kind = NotificationKind.Track,
                    AppName = m.Source,
                    Title = m.Title,
                    Body = m.Artist,
                    Image = _art,
                });
            }
        }

        UpdateProgress();
        UpdateIsland();
    }

    TimeSpan CurrentPosition() => _posRunning ? _posBase + (DateTime.Now - _posBaseAt) : _posBase;

    void UpdateProgress()
    {
        var m = _music;
        if (m == null || m.Duration <= TimeSpan.FromSeconds(1))
        {
            ProgressRow.Visibility = Visibility.Hidden;
            return;
        }
        ProgressRow.Visibility = Visibility.Visible;

        var pos = CurrentPosition();
        if (pos > m.Duration) pos = m.Duration;
        if (pos < TimeSpan.Zero) pos = TimeSpan.Zero;
        PositionText.Text = FormatTime(pos);
        DurationText.Text = "-" + FormatTime(m.Duration - pos);
        ProgressScale.ScaleX = pos.TotalSeconds / m.Duration.TotalSeconds;
    }

    static string FormatTime(TimeSpan t) =>
        t.TotalHours >= 1 ? t.ToString(@"h\:mm\:ss") : t.ToString(@"m\:ss");

    async void PlayPause_Click(object sender, RoutedEventArgs e) => await _media.TogglePlayPauseAsync();
    async void Next_Click(object sender, RoutedEventArgs e) => await _media.NextAsync();
    async void Previous_Click(object sender, RoutedEventArgs e) => await _media.PreviousAsync();

    async void Progress_MouseDown(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        var m = _music;
        if (m == null || m.Duration <= TimeSpan.Zero || ProgressTrack.ActualWidth <= 0) return;
        var fraction = Math.Clamp(e.GetPosition(ProgressTrack).X / ProgressTrack.ActualWidth, 0, 1);
        var target = TimeSpan.FromSeconds(m.Duration.TotalSeconds * fraction);
        _posBase = target;
        _posBaseAt = DateTime.Now;
        UpdateProgress();
        await _media.SeekAsync(target);
    }
}
