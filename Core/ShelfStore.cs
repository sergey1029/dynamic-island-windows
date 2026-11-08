using System.Text.Json;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DynamicIsland;

/// <summary>Список файлов на полке (храним только пути).</summary>
public static class ShelfStore
{
    static string FilePath => Path.Combine(Settings.Folder, "shelf.json");
    static readonly HashSet<string> ImageExtensions = [".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp", ".tif", ".tiff"];

    public static List<string> Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return new();
            var paths = JsonSerializer.Deserialize<List<string>>(File.ReadAllText(FilePath)) ?? new();
            return paths.Where(Exists).ToList();
        }
        catch (Exception ex)
        {
            Log.Write("shelf load", ex);
            return new();
        }
    }

    public static void Save(IEnumerable<string> paths)
    {
        try
        {
            File.WriteAllText(FilePath, JsonSerializer.Serialize(paths));
        }
        catch (Exception ex)
        {
            Log.Write("shelf save", ex);
        }
    }

    public static bool Exists(string path) => File.Exists(path) || Directory.Exists(path);

    /// <summary>Превью для картинок, системная иконка для остального.</summary>
    public static ImageSource? GetIcon(string path)
    {
        if (ImageExtensions.Contains(Path.GetExtension(path).ToLowerInvariant()))
        {
            try
            {
                var image = new BitmapImage();
                image.BeginInit();
                image.UriSource = new Uri(path);
                image.DecodePixelWidth = 96;
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.EndInit();
                image.Freeze();
                return image;
            }
            catch
            {
                // упадём на обычную иконку
            }
        }

        var info = new Native.SHFILEINFO();
        const uint SHGFI_ICON = 0x100;
        Native.SHGetFileInfo(path, 0, ref info, (uint)System.Runtime.InteropServices.Marshal.SizeOf(info), SHGFI_ICON);
        if (info.hIcon == IntPtr.Zero) return null;
        try
        {
            var source = Imaging.CreateBitmapSourceFromHIcon(info.hIcon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            source.Freeze();
            return source;
        }
        finally
        {
            Native.DestroyIcon(info.hIcon);
        }
    }
}
