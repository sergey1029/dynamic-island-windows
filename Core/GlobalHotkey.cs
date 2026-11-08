using System.Windows.Input;

namespace DynamicIsland;

/// <summary>Системная горячая клавиша через RegisterHotKey.</summary>
public sealed class GlobalHotkey : IDisposable
{
    public const int Id = 0x4449;
    const uint MOD_ALT = 0x1, MOD_CONTROL = 0x2, MOD_SHIFT = 0x4, MOD_WIN = 0x8, MOD_NOREPEAT = 0x4000;

    readonly IntPtr _hwnd;
    bool _registered;

    public GlobalHotkey(IntPtr hwnd) => _hwnd = hwnd;

    /// <summary>Основная (не модификатор) клавиша — по ней отслеживаем отпускание.</summary>
    public int VirtualKey { get; private set; }

    public bool Register(string text)
    {
        Unregister();
        if (!TryParse(text, out var modifiers, out var vk)) return false;
        VirtualKey = vk;
        _registered = Native.RegisterHotKey(_hwnd, Id, modifiers | MOD_NOREPEAT, (uint)vk);
        return _registered;
    }

    public void Unregister()
    {
        if (_registered) Native.UnregisterHotKey(_hwnd, Id);
        _registered = false;
    }

    public void Dispose() => Unregister();

    static bool TryParse(string text, out uint modifiers, out int vk)
    {
        modifiers = 0;
        vk = 0;
        foreach (var raw in text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            switch (raw.ToLowerInvariant())
            {
                case "ctrl" or "control": modifiers |= MOD_CONTROL; break;
                case "alt": modifiers |= MOD_ALT; break;
                case "shift": modifiers |= MOD_SHIFT; break;
                case "win": modifiers |= MOD_WIN; break;
                default:
                    var token = raw.Length == 1 && char.IsDigit(raw[0]) ? "D" + raw : raw;
                    if (!Enum.TryParse<Key>(token, true, out var key)) return false;
                    vk = KeyInterop.VirtualKeyFromKey(key);
                    break;
            }
        }
        return vk != 0;
    }
}
