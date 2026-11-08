using System.Windows.Media;

namespace DynamicIsland;

public enum NotificationKind
{
    App,
    Track,
    Alarm,
    Reminder,
    Info,
}

public sealed class IslandNotification
{
    public NotificationKind Kind { get; init; } = NotificationKind.App;
    public string AppName { get; init; } = "";
    public string Title { get; init; } = "";
    public string Body { get; init; } = "";
    public byte[]? IconBytes { get; init; }
    public ImageSource? Image { get; init; }

    /// <summary>AppUserModelId приложения — по нему открываем приложение при клике.</summary>
    public string? AppId { get; init; }

    /// <summary>Значок из Segoe MDL2 Assets вместо иконки приложения.</summary>
    public string? Glyph { get; init; }
    public Color? GlyphColor { get; init; }
}
