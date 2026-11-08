using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DynamicIsland;

/// <summary>Подключённый календарь (iCal-ссылка или .ics файл).</summary>
public sealed class CalendarSource
{
    public string Name { get; set; } = "";
    public string Url { get; set; } = "";

    /// <summary>google, yandex, outlook, icloud, ical.</summary>
    public string Service { get; set; } = "ical";
    public string Color { get; set; } = "#FF453A";
    public bool Enabled { get; set; } = true;
}

public sealed class Settings
{
    // общие
    public string AutoHide { get; set; } = "AnyWindow";
    public string Hotkey { get; set; } = "Ctrl+Alt+I";
    public double TopOffset { get; set; } = 6;

    // внешний вид
    public string ClockStyle { get; set; } = "classic";

    // погода
    public string City { get; set; } = "Москва";
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }

    /// <summary>celsius или fahrenheit.</summary>
    public string TemperatureUnit { get; set; } = "celsius";

    // календари
    public List<CalendarSource> Calendars { get; set; } = new();
    public int ReminderMinutes { get; set; } = 10;

    /// <summary>Старый формат (список ссылок) — переносится в Calendars при загрузке.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? CalendarUrls { get; set; }

    // уведомления и индикаторы
    public int NotificationSeconds { get; set; } = 5;
    public bool ShowTrackChanges { get; set; } = true;
    public int PausedMusicSeconds { get; set; } = 60;
    public bool ShowEventsInFullscreen { get; set; } = false;
    public bool ShowVolume { get; set; } = true;
    public bool ShowBrightness { get; set; } = true;
    public bool ShowCharging { get; set; } = true;

    public static string Folder { get; } = CreateFolder();
    public static string FilePath => Path.Combine(Folder, "settings.json");

    /// <summary>Что мы сами записали последним — чтобы не перечитывать собственные сохранения.</summary>
    public static string? LastWrittenText { get; private set; }

    static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    static string CreateFolder()
    {
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DynamicIsland");
        Directory.CreateDirectory(dir);
        return dir;
    }

    public static Settings Load()
    {
        try
        {
            if (!File.Exists(FilePath))
            {
                var fresh = new Settings();
                fresh.Save();
                return fresh;
            }
            var text = File.ReadAllText(FilePath);
            var settings = JsonSerializer.Deserialize<Settings>(text, JsonOptions) ?? new Settings();

            if (settings.CalendarUrls is { Count: > 0 } urls)
            {
                foreach (var url in urls)
                    settings.Calendars.Add(new CalendarSource { Name = "Календарь", Url = url });
            }
            settings.CalendarUrls = null;

            // дописываем в файл новые параметры, чтобы их было видно (только если что-то изменилось)
            if (JsonSerializer.Serialize(settings, JsonOptions) != text) settings.Save();
            return settings;
        }
        catch (Exception ex)
        {
            Log.Write("settings load", ex);
            return new Settings();
        }
    }

    public void Save()
    {
        try
        {
            var text = JsonSerializer.Serialize(this, JsonOptions);
            LastWrittenText = text;
            File.WriteAllText(FilePath, text);
        }
        catch (Exception ex)
        {
            Log.Write("settings save", ex);
        }
    }

    public Settings Clone() => JsonSerializer.Deserialize<Settings>(JsonSerializer.Serialize(this, JsonOptions), JsonOptions)!;

    /// <summary>Копирует значения в этот же объект — ссылки на него (окно настроек) остаются рабочими.</summary>
    public void CopyFrom(Settings other)
    {
        foreach (var prop in typeof(Settings).GetProperties())
            if (prop.CanWrite && prop.GetIndexParameters().Length == 0) prop.SetValue(this, prop.GetValue(other));
    }

    public string CalendarsKey() => JsonSerializer.Serialize(Calendars);
}
