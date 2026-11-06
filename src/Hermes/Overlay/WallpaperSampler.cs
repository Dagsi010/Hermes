using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Hermes.Core;
using Microsoft.Win32;

namespace Hermes.Overlay;

/// <summary>
/// How bright the wallpaper is behind a given screen area, for the "Auto" text colour.
/// Reproduces how Windows places the picture (Fill, Fit, Stretch, Center, Tile, Span) and
/// the solid background colour around it, then averages a grid of samples.
/// </summary>
internal static class WallpaperSampler
{
    const int DecodeWidth = 960;

    static string? _cachedKey;
    static byte[]? _pixels; // Bgra32 of the decoded (downscaled) image
    static int _width, _height; // decoded size
    static int _originalWidth, _originalHeight;

    /// <summary>Average perceived brightness (0 dark .. 1 bright) of the wallpaper under <paramref name="area"/>, or null if unknown.</summary>
    public static double? Brightness(Rect area, Rect monitor)
    {
        try
        {
            var background = BackgroundColor();
            var path = WallpaperPath();
            if (path == null) return Luma(background);
            if (!Load(path)) return null;

            var (style, tile) = Style();
            var target = style == 22 ? VirtualScreen() : monitor; // Span covers all monitors
            var samples = new List<double>();
            const int grid = 16;
            for (var gy = 0; gy < grid; gy++)
            for (var gx = 0; gx < grid; gx++)
            {
                var x = area.Left + (gx + 0.5) * area.Width / grid;
                var y = area.Top + (gy + 0.5) * area.Height / grid;
                samples.Add(Luma(SampleAt(x, y, target, style, tile) ?? background));
            }
            // Not the mean: thin white text is lost on a bright patch even if the rest is dark,
            // so judge by the brightness that three quarters of the area stays under.
            samples.Sort();
            return samples[(int)(samples.Count * 0.75)];
        }
        catch (Exception ex)
        {
            Log.Error("Wallpaper sampling failed", ex);
            return null;
        }
    }

    /// <summary>For --snapshot: the background colour, the picture and where Windows draws it (tiling not reproduced).</summary>
    public static (Color Background, BitmapSource? Image, Rect Dest) Placement(Rect monitor)
    {
        var background = BackgroundColor();
        if (WallpaperPath() is not { } path || !Load(path)) return (background, null, Rect.Empty);
        var (style, _) = Style();
        var target = style == 22 ? VirtualScreen() : monitor;
        double iw = _originalWidth, ih = _originalHeight, sx, sy;
        switch (style)
        {
            case 2: sx = target.Width / iw; sy = target.Height / ih; break;
            case 6: sx = sy = Math.Min(target.Width / iw, target.Height / ih); break;
            case 0: sx = sy = 1; break;
            default: sx = sy = Math.Max(target.Width / iw, target.Height / ih); break;
        }
        var dest = new Rect(target.Left + (target.Width - iw * sx) / 2, target.Top + (target.Height - ih * sy) / 2, iw * sx, ih * sy);
        var image = BitmapSource.Create(_width, _height, 96, 96, PixelFormats.Bgra32, null, _pixels, _width * 4);
        return (background, image, dest);
    }

    /// <summary>The image colour at a screen point, or null where the background colour shows.</summary>
    static Color? SampleAt(double x, double y, Rect target, int style, bool tile)
    {
        double iw = _originalWidth, ih = _originalHeight;
        double u, v; // position in the original image, in pixels

        if (tile)
        {
            u = Mod(x - target.Left, iw);
            v = Mod(y - target.Top, ih);
        }
        else
        {
            double sx, sy;
            switch (style)
            {
                case 2: // Stretch
                    sx = target.Width / iw; sy = target.Height / ih; break;
                case 6: // Fit
                    sx = sy = Math.Min(target.Width / iw, target.Height / ih); break;
                case 0: // Center
                    sx = sy = 1; break;
                default: // 10 Fill, 22 Span
                    sx = sy = Math.Max(target.Width / iw, target.Height / ih); break;
            }
            var left = target.Left + (target.Width - iw * sx) / 2;
            var top = target.Top + (target.Height - ih * sy) / 2;
            u = (x - left) / sx;
            v = (y - top) / sy;
            if (u < 0 || v < 0 || u >= iw || v >= ih) return null;
        }

        var px = Math.Clamp((int)(u * _width / iw), 0, _width - 1);
        var py = Math.Clamp((int)(v * _height / ih), 0, _height - 1);
        var i = (py * _width + px) * 4;
        return Color.FromRgb(_pixels![i + 2], _pixels[i + 1], _pixels[i]);
    }

    static bool Load(string path)
    {
        var info = new FileInfo(path);
        var key = $"{path}|{info.Length}|{info.LastWriteTimeUtc.Ticks}";
        if (key == _cachedKey && _pixels != null) return true;

        using (var stream = File.OpenRead(path))
        {
            var frame = BitmapDecoder.Create(stream, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None).Frames[0];
            _originalWidth = frame.PixelWidth;
            _originalHeight = frame.PixelHeight;
        }

        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.UriSource = new Uri(path);
        image.DecodePixelWidth = Math.Min(DecodeWidth, _originalWidth);
        image.EndInit();
        var converted = new FormatConvertedBitmap(image, PixelFormats.Bgra32, null, 0);
        _width = converted.PixelWidth;
        _height = converted.PixelHeight;
        _pixels = new byte[_width * _height * 4];
        converted.CopyPixels(_pixels, _width * 4, 0);
        _cachedKey = key;
        return true;
    }

    static string? WallpaperPath()
    {
        var sb = new StringBuilder(520);
        if (Native.SystemParametersInfo(Native.SPI_GETDESKWALLPAPER, (uint)sb.Capacity, sb, 0) && File.Exists(sb.ToString()))
            return sb.ToString();
        // Slideshows and Spotlight hand Windows a copy here.
        var transcoded = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            @"Microsoft\Windows\Themes\TranscodedWallpaper");
        return File.Exists(transcoded) ? transcoded : null;
    }

    static (int Style, bool Tile) Style()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Control Panel\Desktop");
        var style = int.TryParse(key?.GetValue("WallpaperStyle") as string, out var s) ? s : 10;
        var tile = key?.GetValue("TileWallpaper") as string == "1";
        return (style, tile);
    }

    static Color BackgroundColor()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Control Panel\Colors");
        var parts = (key?.GetValue("Background") as string ?? "0 0 0").Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length == 3 && byte.TryParse(parts[0], out var r) && byte.TryParse(parts[1], out var g) && byte.TryParse(parts[2], out var b)
            ? Color.FromRgb(r, g, b)
            : Colors.Black;
    }

    static Rect VirtualScreen()
    {
        var v = System.Windows.Forms.SystemInformation.VirtualScreen;
        return new Rect(v.Left, v.Top, v.Width, v.Height);
    }

    static double Luma(Color c) => (0.299 * c.R + 0.587 * c.G + 0.114 * c.B) / 255.0;

    static double Mod(double a, double m) => ((a % m) + m) % m;
}
