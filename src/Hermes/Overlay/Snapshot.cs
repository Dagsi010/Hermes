using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Screen = System.Windows.Forms.Screen;

namespace Hermes.Overlay;

/// <summary>
/// Development aid (--snapshot &lt;dir&gt;): renders the three parts at their real
/// positions over the current wallpaper into overlay.png, without touching the screen.
/// </summary>
internal static class Snapshot
{
    public static void Save(string dir, OverlayManager overlay, string? monitor)
    {
        Directory.CreateDirectory(dir);
        var screen = Screen.AllScreens.FirstOrDefault(s => s.DeviceName == monitor) ?? Screen.PrimaryScreen!;
        var b = screen.Bounds;

        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            // The wallpaper as Windows places it (Fill, Fit, ...), over the desktop background colour.
            var (background, image, dest) = WallpaperSampler.Placement(new Rect(b.Left, b.Top, b.Width, b.Height));
            dc.DrawRectangle(new SolidColorBrush(background), null, new Rect(0, 0, b.Width, b.Height));
            if (image != null) dc.DrawImage(image, new Rect(dest.Left - b.Left, dest.Top - b.Top, dest.Width, dest.Height));

            foreach (var window in overlay.AllWindows)
            {
                if (window.Content is not FrameworkElement root || root.ActualWidth < 1) continue;
                var dpi = VisualTreeHelper.GetDpi(window);
                var r = window.ScreenRect;
                var bmp = new RenderTargetBitmap(r.Right - r.Left, r.Bottom - r.Top, 96 * dpi.DpiScaleX, 96 * dpi.DpiScaleY, PixelFormats.Pbgra32);
                bmp.Render(root);
                dc.DrawImage(bmp, new Rect(r.Left - b.Left, r.Top - b.Top, r.Right - r.Left, r.Bottom - r.Top));
            }
        }

        var target = new RenderTargetBitmap(b.Width, b.Height, 96, 96, PixelFormats.Pbgra32);
        target.Render(visual);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(target));
        using var file = File.Create(Path.Combine(dir, "overlay.png"));
        encoder.Save(file);
    }
}
