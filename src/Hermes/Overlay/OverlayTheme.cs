using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;
using Hermes.Core;

namespace Hermes.Overlay;

/// <summary>
/// Overlay colours and sizes, published as resources so XAML can use {DynamicResource Fg}
/// etc. and pick up changes live. Sizes are app-wide; colours are set per overlay part,
/// because in Auto mode each part picks light or dark text for the wallpaper behind it.
/// </summary>
public static class OverlayTheme
{
    const double Pt = 96.0 / 72.0;

    public static void ApplySizes(double scale)
    {
        var r = Application.Current.Resources;
        r["SizeSmall"] = 11 * Pt * scale;
        r["SizeItem"] = 13 * Pt * scale;
        r["SizeCurrent"] = 15 * Pt * scale;
    }

    public static void ApplyColors(ResourceDictionary r, bool darkText, Theme theme)
    {
        // Theme colours are tuned for light text on a dark wallpaper; dark text gets deeper shades.
        Color Shade(Color c) => darkText ? Themes.Mix(c, Colors.Black, 0.45) : c;

        r["Accent"] = Themes.Brush(Shade(theme.Accent));
        r["DueToday"] = Themes.Brush(Shade(theme.DueToday));
        r["DueTomorrow"] = Themes.Brush(Shade(theme.DueTomorrow));
        r["Warn"] = Themes.Brush(Shade(theme.DueTomorrow));
        r["DueLater"] = darkText ? Brush("#5A6270") : Themes.Brush(theme.DueLater);

        if (darkText)
        {
            r["Fg"] = Brush("#141414");
            r["Track"] = Brush("#30000000");
            r["Finished"] = Brush("#C0141414");
        }
        else
        {
            r["Fg"] = Brush("#FFFFFF");
            r["Track"] = Brush("#40FFFFFF");
            r["Finished"] = Brush("#D8FFFFFF");
        }
    }

    /// <summary>The soft dark shadow that keeps light text readable; dark text gets none.</summary>
    public static Effect? CreateShadow(bool darkText) => darkText ? null : new DropShadowEffect
    {
        Color = Colors.Black,
        BlurRadius = 10,
        ShadowDepth = 0,
        Opacity = 0.9,
    };

    static SolidColorBrush Brush(string hex)
    {
        var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        b.Freeze();
        return b;
    }
}
