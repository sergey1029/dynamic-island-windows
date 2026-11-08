using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DynamicIsland;

/// <summary>
/// Режим разработчика: рисует скриншоты для README на демо-данных (без личной музыки и уведомлений)
/// и графику для установщика. Запуск: <c>DynamicIsland.exe --screenshots docs/screenshots</c>
/// или <c>DynamicIsland.exe --assets Assets</c>.
/// </summary>
public partial class MainWindow
{
    bool _showcase;

    async Task RunShowcaseAsync(string mode, string dir)
    {
        _showcase = true;
        try
        {
            Directory.CreateDirectory(dir);
            if (mode == "assets") ShowcaseArt.GenerateAssets(dir);
            else await CaptureScreenshotsAsync(dir);
        }
        catch (Exception ex)
        {
            Log.Write("showcase", ex);
        }
        Close();
    }

    async Task CaptureScreenshotsAsync(string dir)
    {
        var root = (FrameworkElement)Content;
        _nextCalendar = DateTime.MaxValue;
        _settings.CopyFrom(DemoSettings());
        HomeClock.SetStyle("classic");

        var artBytes = ShowcaseArt.Png(ShowcaseArt.AlbumArt(300), 300, 300);
        var art = ImageHelper.Load(artBytes);
        ApplyMedia(new MediaSnapshot("Midnight City Lights", "Neon Avenue", "Музыка", artBytes, true,
            TimeSpan.FromSeconds(83), TimeSpan.FromSeconds(214), DateTimeOffset.Now));
        _events = DemoEvents();
        RenderCalendar();
        _shelf.Clear();
        _shelf.AddRange(DemoShelfFiles());
        RenderShelf();
        _tick.Start();
        await RefreshWeatherAsync();

        async Task<BitmapSource> Snap(Action setup)
        {
            setup();
            UpdateIsland();
            await Task.Delay(1200);
            return Crop(Capture(root), _targetWidth + 60, _targetHeight + 40);
        }

        void Reset()
        {
            _notificationTimer.Stop();
            _queue.Clear();
            _notification = null;
            _osdUntil = DateTime.MinValue;
            _hoverExpanded = false;
            _timers.ResetCountdown();
            _timers.ResetStopwatch();
        }

        var shots = new Dictionary<string, BitmapSource>
        {
            ["music-compact"] = await Snap(Reset),
            ["music"] = await Snap(() => { _hoverExpanded = true; UpdateIsland(); _page = IslandPage.Music; }),
            ["home"] = await Snap(() => { _page = IslandPage.Home; UpdateSystemInfo(); }),
            ["calendar"] = await Snap(() => _page = IslandPage.Calendar),
            ["timer"] = await Snap(() =>
            {
                _timers.SetDemo(TimeSpan.FromSeconds(754.3), TimeSpan.FromMinutes(25), TimeSpan.FromSeconds(1122));
                _page = IslandPage.Timer;
            }),
            ["shelf"] = await Snap(() => _page = IslandPage.Shelf),
        };

        var clocks = new List<(IClockStyle Style, BitmapSource Shot)>();
        foreach (var style in ClockStyles.All)
            clocks.Add((style, await Snap(() => { _page = IslandPage.Home; HomeClock.SetStyle(style.Id); })));
        HomeClock.SetStyle("classic");

        shots["timer-compact"] = await Snap(() => _hoverExpanded = false);
        shots["notification"] = await Snap(() =>
        {
            Reset();
            Enqueue(new IslandNotification
            {
                AppName = "Сообщения",
                Title = "Аня",
                Body = "Встречаемся в семь у кинотеатра?",
                Glyph = "",
                GlyphColor = Color.FromRgb(0x30, 0xD1, 0x58),
            });
        });
        shots["track"] = await Snap(() =>
        {
            Reset();
            Enqueue(new IslandNotification { Kind = NotificationKind.Track, AppName = "Музыка", Title = "Midnight City Lights", Body = "Neon Avenue", Image = art });
        });
        shots["reminder"] = await Snap(() =>
        {
            Reset();
            Enqueue(new IslandNotification
            {
                Kind = NotificationKind.Reminder,
                AppName = "Календарь",
                Title = "Обед с Мишей",
                Body = "Через 10 мин · Кафе на Пятницкой",
                Glyph = "",
                GlyphColor = Color.FromRgb(0xFF, 0x45, 0x3A),
            });
        });
        shots["volume"] = await Snap(() => { Reset(); ShowOsd(OsdKind.Volume, 0.64); });
        shots["charging"] = await Snap(() => { Reset(); ShowOsd(OsdKind.Charging, 82); });
        shots["idle"] = await Snap(() => { Reset(); ApplyMedia(null); });

        // отдельные картинки
        foreach (var name in new[] { "music", "home", "calendar", "timer", "shelf", "notification" })
            ShowcaseArt.Save(ShowcaseArt.OnWallpaper(shots[name], 620), Path.Combine(dir, $"island-{name}.png"));

        ShowcaseArt.Save(ShowcaseArt.Stack(
        [
            ("Музыка играет", shots["music-compact"]),
            ("Идёт таймер", shots["timer-compact"]),
            ("Громкость", shots["volume"]),
            ("Зарядка", shots["charging"]),
            ("Смена трека", shots["track"]),
            ("Напоминание", shots["reminder"]),
        ]), Path.Combine(dir, "compact-states.png"));

        ShowcaseArt.Save(ShowcaseArt.Grid(clocks.Select(c => (c.Style.Name, c.Shot)).ToList(), 3), Path.Combine(dir, "clock-styles.png"));
        ShowcaseArt.Save(ShowcaseArt.Hero(shots["music"], shots["notification"], shots["timer-compact"], shots["volume"]), Path.Combine(dir, "hero.png"));

        await CaptureSettingsAsync(dir);
    }

    async Task CaptureSettingsAsync(string dir)
    {
        var window = new SettingsWindow(this)
        {
            WindowStartupLocation = WindowStartupLocation.Manual,
            Left = -10000,
            Top = 0,
            Height = 720,
            ShowActivated = false,
        };
        window.Show();
        foreach (var page in new[] { "general", "appearance", "weather", "calendars", "notifications" })
        {
            window.ShowPage(page);
            await Task.Delay(page == "weather" ? 3000 : 1000);
            var shot = Capture((FrameworkElement)window.Content);
            ShowcaseArt.Save(ShowcaseArt.WindowOnWallpaper(shot, "Dynamic Island — настройки"), Path.Combine(dir, $"settings-{page}.png"));
        }
        window.Close();
    }

    static Settings DemoSettings() => new()
    {
        City = "Москва",
        Latitude = 55.7558,
        Longitude = 37.6173,
        Calendars =
        [
            new CalendarSource { Name = "Работа", Service = "google", Url = "https://calendar.google.com/calendar/ical/demo/basic.ics", Color = "#0A84FF" },
            new CalendarSource { Name = "Личное", Service = "yandex", Url = "https://calendar.yandex.ru/export/ics.xml?private_token=demo", Color = "#30D158" },
            new CalendarSource { Name = "Спорт", Service = "icloud", Url = "webcal://p01-caldav.icloud.com/published/2/demo", Color = "#FF9F0A", Enabled = false },
        ],
    };

    static List<CalendarEvent> DemoEvents()
    {
        var now = DateTime.Now;
        var start = now.Date.AddHours(now.Hour).AddMinutes(-20);
        return
        [
            new("1", "Дейли-созвон команды", start, start.AddMinutes(45), false, "Zoom") { Color = "#0A84FF" },
            new("2", "Обед с Мишей", start.AddHours(3), start.AddHours(4), false, "Кафе на Пятницкой") { Color = "#30D158" },
            new("3", "Тренировка", now.Date.AddDays(1).AddHours(19), now.Date.AddDays(1).AddHours(20.5), false, "") { Color = "#FF9F0A" },
        ];
    }

    static List<string> DemoShelfFiles()
    {
        var dir = Path.Combine(Path.GetTempPath(), "DynamicIslandDemo");
        Directory.CreateDirectory(dir);
        var files = new List<string>();
        foreach (var name in new[] { "Отчёт за квартал.pdf", "Презентация.pptx", "Фото с отпуска.jpg", "Заметки.txt", "Проект.zip" })
        {
            var path = Path.Combine(dir, name);
            if (name.EndsWith(".jpg"))
                File.WriteAllBytes(path, ShowcaseArt.Encode(new JpegBitmapEncoder(), ShowcaseArt.AlbumArt(200), 200, 200));
            else if (!File.Exists(path))
                File.WriteAllText(path, "");
            files.Add(path);
        }
        return files;
    }

    static BitmapSource Capture(FrameworkElement element)
    {
        double w = element.ActualWidth, h = element.ActualHeight;
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            var brush = new VisualBrush(element)
            {
                Viewbox = new Rect(0, 0, w, h),
                ViewboxUnits = BrushMappingMode.Absolute,
                Stretch = Stretch.Fill,
            };
            dc.DrawRectangle(brush, null, new Rect(0, 0, w, h));
        }
        var bitmap = new RenderTargetBitmap((int)(w * ShowcaseArt.Scale), (int)(h * ShowcaseArt.Scale),
            96 * ShowcaseArt.Scale, 96 * ShowcaseArt.Scale, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }

    static BitmapSource Crop(BitmapSource source, double width, double height)
    {
        var w = (int)Math.Min(source.PixelWidth, width * ShowcaseArt.Scale);
        var h = (int)Math.Min(source.PixelHeight, height * ShowcaseArt.Scale);
        var cropped = new CroppedBitmap(source, new Int32Rect((source.PixelWidth - w) / 2, 0, w, h));
        cropped.Freeze();
        return cropped;
    }
}

/// <summary>Рисование фонов, коллажей и иконок для витрины.</summary>
static class ShowcaseArt
{
    public const double Scale = 2;

    static readonly Typeface Display = new(new FontFamily("Segoe UI Variable Display, Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);
    static readonly Typeface Text = new(new FontFamily("Segoe UI Variable Text, Segoe UI"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
    static readonly Typeface Icons = new(new FontFamily("Segoe MDL2 Assets"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);

    static double W(BitmapSource b) => b.PixelWidth / Scale;
    static double H(BitmapSource b) => b.PixelHeight / Scale;

    public static void DrawWallpaper(DrawingContext dc, Rect r)
    {
        dc.DrawRectangle(new LinearGradientBrush(Color.FromRgb(0x17, 0x10, 0x35), Color.FromRgb(0x05, 0x14, 0x30), 35), null, r);

        void Blob(double x, double y, double radius, Color c, byte alpha)
        {
            var brush = new RadialGradientBrush(Color.FromArgb(alpha, c.R, c.G, c.B), Color.FromArgb(0, c.R, c.G, c.B));
            var rad = radius * Math.Max(r.Width, r.Height);
            dc.DrawEllipse(brush, null, new Point(r.X + x * r.Width, r.Y + y * r.Height), rad, rad);
        }

        Blob(0.12, 0.15, 0.45, Color.FromRgb(0x7B, 0x61, 0xFF), 170);
        Blob(0.9, 0.25, 0.4, Color.FromRgb(0x0A, 0x84, 0xFF), 150);
        Blob(0.55, 1.05, 0.45, Color.FromRgb(0xFF, 0x37, 0x5F), 110);
        Blob(0.3, 0.9, 0.3, Color.FromRgb(0x64, 0xD2, 0xFF), 70);
    }

    static BitmapSource Render(double w, double h, Action<DrawingContext> draw)
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen()) draw(dc);
        var bitmap = new RenderTargetBitmap((int)(w * Scale), (int)(h * Scale), 96 * Scale, 96 * Scale, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }

    static void DrawImage(DrawingContext dc, BitmapSource image, double x, double y) =>
        dc.DrawImage(image, new Rect(x, y, W(image), H(image)));

    static FormattedText Label(string text, double size, Typeface face, Brush brush) =>
        new(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, face, size, brush, Scale);

    static void DrawCentered(DrawingContext dc, FormattedText text, double centerX, double y) =>
        dc.DrawText(text, new Point(centerX - text.Width / 2, y));

    static readonly Brush DimBrush = new SolidColorBrush(Color.FromArgb(0xB0, 0xFF, 0xFF, 0xFF));

    /// <summary>Остров по центру сверху на фоне обоев.</summary>
    public static BitmapSource OnWallpaper(BitmapSource island, double width) =>
        Render(width, H(island) + 30, dc =>
        {
            DrawWallpaper(dc, new Rect(0, 0, width, H(island) + 30));
            DrawImage(dc, island, (width - W(island)) / 2, 0);
        });

    /// <summary>Компактные состояния столбиком с подписями слева.</summary>
    public static BitmapSource Stack(List<(string Title, BitmapSource Shot)> items)
    {
        const double width = 820, labelWidth = 200, pad = 24;
        double height = pad + items.Sum(i => H(i.Shot) - 6) + pad;
        return Render(width, height, dc =>
        {
            DrawWallpaper(dc, new Rect(0, 0, width, height));
            double y = pad;
            foreach (var (title, shot) in items)
            {
                var label = Label(title, 15, Text, DimBrush);
                double panelHeight = H(shot) - 46;
                dc.DrawText(label, new Point(pad + 8, y + 6 + (panelHeight - label.Height) / 2));
                DrawImage(dc, shot, labelWidth + (width - labelWidth - W(shot)) / 2, y);
                y += H(shot) - 6;
            }
        });
    }

    /// <summary>Сетка картинок с подписями (стили часов).</summary>
    public static BitmapSource Grid(List<(string Title, BitmapSource Shot)> items, int columns)
    {
        double cellW = W(items[0].Shot), cellH = H(items[0].Shot) + 28, pad = 20;
        int rows = (items.Count + columns - 1) / columns;
        double width = pad * 2 + cellW * columns, height = pad + rows * cellH + pad;
        return Render(width, height, dc =>
        {
            DrawWallpaper(dc, new Rect(0, 0, width, height));
            for (int i = 0; i < items.Count; i++)
            {
                int row = i / columns, col = i % columns;
                int inRow = Math.Min(columns, items.Count - row * columns);
                double offset = (columns - inRow) * cellW / 2;
                double x = pad + offset + col * cellW, y = pad + row * cellH;
                DrawImage(dc, items[i].Shot, x, y);
                DrawCentered(dc, Label(items[i].Title, 16, Display, Brushes.White), x + cellW / 2, y + cellH - 38);
            }
        });
    }

    /// <summary>Обложка README: развёрнутый остров, заголовок и ряд компактных состояний.</summary>
    public static BitmapSource Hero(BitmapSource expanded, BitmapSource notification, BitmapSource compactA, BitmapSource compactB)
    {
        const double width = 1280, height = 640;
        return Render(width, height, dc =>
        {
            DrawWallpaper(dc, new Rect(0, 0, width, height));
            DrawImage(dc, expanded, (width - W(expanded)) / 2, 0);
            DrawCentered(dc, Label("Dynamic Island для Windows", 54, Display, Brushes.White), width / 2, 270);
            DrawCentered(dc, Label("Музыка, уведомления, виджеты, таймеры и календарь — в одной живой пилюле над экраном", 19, Text, DimBrush), width / 2, 345);

            var row = new[] { compactA, notification, compactB };
            const double gap = 28;
            double total = row.Sum(W) + gap * (row.Length - 1);
            double x = (width - total) / 2;
            foreach (var shot in row)
            {
                double y = shot == notification ? 420 : 440;
                DrawImage(dc, shot, x, y);
                x += W(shot) + gap;
            }
        });
    }

    /// <summary>Окно с заголовком, скруглением и тенью на фоне обоев.</summary>
    public static BitmapSource WindowOnWallpaper(BitmapSource content, string title)
    {
        const double pad = 56, titleBar = 34, radius = 10;
        double w = W(content), h = H(content) + titleBar;
        double width = w + pad * 2, height = h + pad * 2;
        return Render(width, height, dc =>
        {
            DrawWallpaper(dc, new Rect(0, 0, width, height));
            var frame = new Rect(pad, pad, w, h);
            for (int i = 14; i >= 1; i--)
            {
                var shadow = frame;
                shadow.Inflate(i * 2, i * 2);
                shadow.Offset(0, 8);
                dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromArgb(9, 0, 0, 0)), null, shadow, radius + i * 2, radius + i * 2);
            }

            dc.PushClip(new RectangleGeometry(frame, radius, radius));
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(0x1F, 0x1F, 0x22)), null, new Rect(pad, pad, w, titleBar));
            dc.DrawText(Label(title, 12, Text, Brushes.White), new Point(pad + 14, pad + 9));
            double bx = pad + w;
            foreach (var glyph in new[] { "", "", "" })
            {
                bx -= 46;
                dc.DrawText(Label(glyph, 10, Icons, Brushes.White), new Point(bx + 18, pad + 12));
            }
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(0x16, 0x16, 0x18)), null, new Rect(pad, pad + titleBar, w, H(content)));
            DrawImage(dc, content, pad, pad + titleBar);
            dc.Pop();
            dc.DrawRoundedRectangle(null, new Pen(new SolidColorBrush(Color.FromArgb(0x50, 0xFF, 0xFF, 0xFF)), 1), frame, radius, radius);
        });
    }

    /// <summary>Демо-обложка трека: закат в стиле synthwave.</summary>
    public static Drawing AlbumArt(double size)
    {
        var group = new DrawingGroup();
        using var dc = group.Open();
        var rect = new Rect(0, 0, size, size);
        var sky = new LinearGradientBrush(
        [
            new GradientStop(Color.FromRgb(0x2B, 0x0F, 0x54), 0),
            new GradientStop(Color.FromRgb(0xAB, 0x1F, 0x65), 0.55),
            new GradientStop(Color.FromRgb(0xFF, 0x6F, 0x3C), 1),
        ], 90);
        dc.DrawRectangle(sky, null, rect);
        var sun = new LinearGradientBrush(Color.FromRgb(0xFF, 0xE0, 0x5A), Color.FromRgb(0xFF, 0x3C, 0x7A), 90);
        dc.PushClip(new RectangleGeometry(new Rect(0, 0, size, size * 0.66)));
        dc.DrawEllipse(sun, null, new Point(size / 2, size * 0.6), size * 0.3, size * 0.3);
        for (int i = 0; i < 5; i++)
            dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(0xFF, 0x8A, 0x1C, 0x63)), null,
                new Rect(0, size * (0.45 + i * 0.045), size, size * 0.006 * (i + 1)));
        dc.Pop();
        dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(0x1A, 0x06, 0x33)), null, new Rect(0, size * 0.66, size, size * 0.34));
        var grid = new Pen(new SolidColorBrush(Color.FromArgb(0xC0, 0xFF, 0x3C, 0xC8)), size * 0.006);
        for (int i = -6; i <= 6; i++)
            dc.DrawLine(grid, new Point(size / 2 + i * size * 0.02, size * 0.66), new Point(size / 2 + i * size * 0.16, size));
        for (int i = 0; i < 6; i++)
        {
            double y = size * 0.66 + Math.Pow(i / 6.0, 2) * size * 0.34;
            dc.DrawLine(grid, new Point(0, y), new Point(size, y));
        }
        return group;
    }

    /// <summary>Иконка: пилюля острова на фиолетово-синем скруглённом квадрате.</summary>
    public static Drawing AppIcon(double s, bool background = true)
    {
        var group = new DrawingGroup();
        using var dc = group.Open();
        if (background)
        {
            var bg = new LinearGradientBrush(Color.FromRgb(0x8E, 0x5B, 0xFF), Color.FromRgb(0x0A, 0x84, 0xFF), 45);
            dc.DrawRoundedRectangle(bg, null, new Rect(0, 0, s, s), s * 0.23, s * 0.23);
        }
        double pw = s * 0.74, ph = s * 0.26;
        var pill = new Rect((s - pw) / 2, s * 0.37 - ph / 2 + s * 0.08, pw, ph);
        dc.DrawRoundedRectangle(Brushes.Black, null, pill, ph / 2, ph / 2);
        if (s >= 32)
        {
            double d = ph * 0.62;
            var art = new LinearGradientBrush(Color.FromRgb(0xFF, 0xB3, 0x40), Color.FromRgb(0xFF, 0x37, 0x5F), 45);
            dc.DrawRoundedRectangle(art, null, new Rect(pill.X + (ph - d) / 2, pill.Y + (ph - d) / 2, d, d), d * 0.28, d * 0.28);
            double bw = Math.Max(1.5, s * 0.032), gap = bw * 0.9;
            double[] heights = [0.35, 0.7, 0.5, 0.85];
            double bx = pill.Right - ph * 0.5 - heights.Length * (bw + gap);
            foreach (var k in heights)
            {
                double bh = ph * 0.6 * k;
                dc.DrawRoundedRectangle(Brushes.White, null, new Rect(bx, pill.Y + (ph - bh) / 2, bw, bh), bw / 2, bw / 2);
                bx += bw + gap;
            }
        }
        return group;
    }

    public static byte[] Png(Drawing drawing, int w, int h) => Encode(new PngBitmapEncoder(), drawing, w, h);

    public static byte[] Encode(BitmapEncoder encoder, Drawing drawing, int w, int h, Color? background = null)
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            if (background is { } bg) dc.DrawRectangle(new SolidColorBrush(bg), null, new Rect(0, 0, w, h));
            dc.DrawDrawing(drawing);
        }
        var bitmap = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        BitmapSource frame = bitmap;
        if (encoder is BmpBitmapEncoder or JpegBitmapEncoder) frame = new FormatConvertedBitmap(bitmap, PixelFormats.Bgr24, null, 0);
        encoder.Frames.Add(BitmapFrame.Create(frame));
        using var ms = new MemoryStream();
        encoder.Save(ms);
        return ms.ToArray();
    }

    public static void Save(BitmapSource bitmap, string path)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(path);
        encoder.Save(file);
    }

    /// <summary>Иконка приложения (.ico с PNG внутри), картинки мастера установки и логотип для README.</summary>
    public static void GenerateAssets(string dir)
    {
        int[] sizes = [16, 20, 24, 32, 40, 48, 64, 128, 256];
        var images = sizes.Select(s => Png(AppIcon(s), s, s)).ToList();
        using (var ico = new BinaryWriter(File.Create(Path.Combine(dir, "icon.ico"))))
        {
            ico.Write((short)0);
            ico.Write((short)1);
            ico.Write((short)sizes.Length);
            int offset = 6 + 16 * sizes.Length;
            for (int i = 0; i < sizes.Length; i++)
            {
                ico.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i]));
                ico.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i]));
                ico.Write((byte)0);
                ico.Write((byte)0);
                ico.Write((short)1);
                ico.Write((short)32);
                ico.Write(images[i].Length);
                ico.Write(offset);
                offset += images[i].Length;
            }
            foreach (var image in images) ico.Write(image);
        }
        File.WriteAllBytes(Path.Combine(dir, "logo.png"), Png(AppIcon(256), 256, 256));

        foreach (var (suffix, k) in new[] { ("", 1.0), ("-2x", 2.0) })
        {
            File.WriteAllBytes(Path.Combine(dir, $"wizard-large{suffix}.bmp"),
                Encode(new BmpBitmapEncoder(), WizardImage(164 * k, 314 * k), (int)(164 * k), (int)(314 * k)));
            var small = new DrawingGroup();
            using (var dc = small.Open())
            {
                dc.PushTransform(new TranslateTransform(4 * k, 4 * k));
                dc.DrawDrawing(AppIcon(47 * k));
                dc.Pop();
            }
            File.WriteAllBytes(Path.Combine(dir, $"wizard-small{suffix}.bmp"),
                Encode(new BmpBitmapEncoder(), small, (int)(55 * k), (int)(55 * k), Colors.White));
        }
    }

    static Drawing WizardImage(double w, double h)
    {
        var group = new DrawingGroup();
        using var dc = group.Open();
        DrawWallpaper(dc, new Rect(0, 0, w, h));
        double k = w / 164;
        dc.PushTransform(new TranslateTransform((w - 120 * k) / 2, h * 0.16));
        dc.DrawDrawing(AppIcon(120 * k));
        dc.Pop();
        var title = new FormattedText("Dynamic\nIsland", CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Display, 26 * k, Brushes.White, 1)
        {
            TextAlignment = TextAlignment.Center,
        };
        dc.DrawText(title, new Point(w / 2, h * 0.6));
        var sub = new FormattedText("для Windows", CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Text, 13 * k, DimBrush, 1)
        {
            TextAlignment = TextAlignment.Center,
        };
        dc.DrawText(sub, new Point(w / 2, h * 0.6 + title.Height + 6 * k));
        return group;
    }
}
