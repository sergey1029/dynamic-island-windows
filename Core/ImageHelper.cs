using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DynamicIsland;

public static class ImageHelper
{
    public static BitmapSource? Load(byte[]? data, int decodeWidth = 0)
    {
        if (data == null || data.Length == 0) return null;
        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.StreamSource = new MemoryStream(data);
            if (decodeWidth > 0) image.DecodePixelWidth = decodeWidth;
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Яркий доминирующий цвет обложки — для эквалайзера.</summary>
    public static Color GetAccent(BitmapSource source)
    {
        try
        {
            BitmapSource scaled = source.PixelWidth > 32
                ? new TransformedBitmap(source, new ScaleTransform(32.0 / source.PixelWidth, 32.0 / source.PixelHeight))
                : source;
            var bgra = new FormatConvertedBitmap(scaled, PixelFormats.Bgra32, null, 0);
            int w = bgra.PixelWidth, h = bgra.PixelHeight;
            var px = new byte[w * h * 4];
            bgra.CopyPixels(px, w * 4, 0);

            double r = 0, g = 0, b = 0, total = 0;
            for (int i = 0; i < px.Length; i += 4)
            {
                double pb = px[i] / 255.0, pg = px[i + 1] / 255.0, pr = px[i + 2] / 255.0;
                double max = Math.Max(pr, Math.Max(pg, pb)), min = Math.Min(pr, Math.Min(pg, pb));
                double sat = max == 0 ? 0 : (max - min) / max;
                double weight = 0.03 + sat * sat * max;
                r += pr * weight; g += pg * weight; b += pb * weight; total += weight;
            }
            r /= total; g /= total; b /= total;

            double peak = Math.Max(r, Math.Max(g, b));
            if (peak < 0.01) return Colors.White;
            double k = 0.95 / peak;
            return Color.FromRgb((byte)(r * k * 255), (byte)(g * k * 255), (byte)(b * k * 255));
        }
        catch
        {
            return Colors.White;
        }
    }
}
