using System.Windows;
using System.Windows.Media;

namespace Hermes.Core;

/// <summary>
/// A colour theme for the Settings panel and the overlay's accents: the current
/// class, the live progress segment and the deadline dots.
/// </summary>
public sealed record Theme(
    string Id,
    string NameIs,
    string NameEn,
    Color Background,
    Color Card,
    Color CardBorder,
    Color Control,
    Color ControlBorder,
    Color Band,
    Color Accent,
    Color DueToday,
    Color DueTomorrow,
    Color DueLater)
{
    public string Name => Loc.Instance.IsEnglish ? NameEn : NameIs;
}

public static class Themes
{
    static Color C(string hex) => (Color)ColorConverter.ConvertFromString(hex);

    public static readonly IReadOnlyList<Theme> All = new[]
    {
        new Theme("navy", "Sjóher", "Navy",
            C("#0E1A2E"), C("#16253D"), C("#22385A"), C("#1E2E4A"), C("#2E4A72"), C("#2B4166"),
            C("#7DB8FF"), C("#FF6B6B"), C("#FFA94D"), C("#C9CED8")),
        new Theme("forest", "Skógur", "Forest",
            C("#0B1C14"), C("#11281D"), C("#1C3D2D"), C("#173326"), C("#285540"), C("#22513B"),
            C("#7BE0A6"), C("#FF7F6E"), C("#F2C14E"), C("#C9DCCF")),
        new Theme("ocean", "Haf", "Ocean",
            C("#0A1C21"), C("#102B33"), C("#1A4049"), C("#14353E"), C("#245460"), C("#1F4C57"),
            C("#5FD9E6"), C("#FF7B7B"), C("#FFB86B"), C("#C5D9DD")),
        new Theme("plum", "Plóma", "Plum",
            C("#170F24"), C("#221635"), C("#352450"), C("#2A1C40"), C("#44306A"), C("#3F2C62"),
            C("#C9A2FF"), C("#FF7096"), C("#FFB86B"), C("#D6CDE3")),
        new Theme("ember", "Glóð", "Ember",
            C("#1C110D"), C("#2A1913"), C("#45291E"), C("#33201A"), C("#5A3628"), C("#50301F"),
            C("#FFB177"), C("#FF6B6B"), C("#FFD166"), C("#E2D3CB")),
        new Theme("graphite", "Grafít", "Graphite",
            C("#121315"), C("#1C1E21"), C("#2C2F34"), C("#24272B"), C("#3A3E44"), C("#34383E"),
            C("#E8C872"), C("#FF6B6B"), C("#FFA94D"), C("#C8CCD2")),
    };

    public static Theme Get(string? id) => All.FirstOrDefault(t => t.Id == id) ?? All[0];

    public static Theme Current { get; private set; } = All[0];

    public static event Action? Changed;

    /// <summary>Publishes the theme's colours as application resources (Set* keys) for the Settings panel.</summary>
    public static void Apply(Theme theme)
    {
        Current = theme;
        var r = Application.Current.Resources;
        r["SetBg"] = Brush(theme.Background);
        r["SetCard"] = Brush(theme.Card);
        r["SetCardBorder"] = Brush(theme.CardBorder);
        r["SetControl"] = Brush(theme.Control);
        r["SetControlBorder"] = Brush(theme.ControlBorder);
        r["SetBand"] = Brush(theme.Band);
        r["SetAccent"] = Brush(theme.Accent);
        // Text greys carry a hint of the accent so they sit well on every background.
        r["SetText"] = Brush(Mix(Colors.White, theme.Accent, 0.12));
        r["SetHint"] = Brush(Mix(Mix(theme.Card, Colors.White, 0.5), theme.Accent, 0.15));
        r["SetFaint"] = Brush(Mix(Mix(theme.Card, Colors.White, 0.35), theme.Accent, 0.15));
        Changed?.Invoke();
    }

    /// <summary>
    /// Points the Fluent theme's accent (checkboxes, sliders, focus lines) at the theme accent.
    /// Must go into the window's own resources, which win over the Fluent dictionaries merged there.
    /// </summary>
    public static void ApplyFluentAccent(ResourceDictionary r, Theme theme)
    {
        var a = theme.Accent;
        r["SystemAccentColor"] = Mix(a, Colors.Black, 0.25);
        r["SystemAccentColorLight1"] = Mix(a, Colors.Black, 0.12);
        r["SystemAccentColorLight2"] = a;
        r["SystemAccentColorLight3"] = Mix(a, Colors.White, 0.3);
        r["SystemAccentColorDark1"] = Mix(a, Colors.Black, 0.35);
        r["SystemAccentColorDark2"] = Mix(a, Colors.Black, 0.5);
        r["SystemAccentColorDark3"] = Mix(a, Colors.Black, 0.65);

        // The Fluent dictionaries read the accent through these SystemColors keys.
        r[SystemColors.AccentColorKey] = Mix(a, Colors.Black, 0.25);
        r[SystemColors.AccentColorLight1Key] = Mix(a, Colors.Black, 0.12);
        r[SystemColors.AccentColorLight2Key] = a;
        r[SystemColors.AccentColorLight3Key] = Mix(a, Colors.White, 0.3);
        r[SystemColors.AccentColorDark1Key] = Mix(a, Colors.Black, 0.35);
        r[SystemColors.AccentColorDark2Key] = Mix(a, Colors.Black, 0.5);
        r[SystemColors.AccentColorDark3Key] = Mix(a, Colors.Black, 0.65);
        r[SystemColors.AccentColorBrushKey] = Brush(Mix(a, Colors.Black, 0.25));
        r[SystemColors.AccentColorLight1BrushKey] = Brush(Mix(a, Colors.Black, 0.12));
        r[SystemColors.AccentColorLight2BrushKey] = Brush(a);
        r[SystemColors.AccentColorLight3BrushKey] = Brush(Mix(a, Colors.White, 0.3));
        r[SystemColors.AccentColorDark1BrushKey] = Brush(Mix(a, Colors.Black, 0.35));
        r[SystemColors.AccentColorDark2BrushKey] = Brush(Mix(a, Colors.Black, 0.5));
        r[SystemColors.AccentColorDark3BrushKey] = Brush(Mix(a, Colors.Black, 0.65));

        r["AccentFillColorDefaultBrush"] = Brush(a);
        r["AccentFillColorSecondaryBrush"] = Brush(Color.FromArgb(0xE6, a.R, a.G, a.B));
        r["AccentFillColorTertiaryBrush"] = Brush(Color.FromArgb(0xCC, a.R, a.G, a.B));
        r["AccentFillColorPrimaryBrush"] = Brush(a);
        r["AccentFillColorSelectedTextBackgroundBrush"] = Brush(Mix(a, Colors.Black, 0.35));
        r["AccentTextFillColorPrimaryBrush"] = Brush(Mix(a, Colors.White, 0.3));
        r["AccentTextFillColorSecondaryBrush"] = Brush(Mix(a, Colors.White, 0.3));
        r["AccentTextFillColorTertiaryBrush"] = Brush(a);
    }

    /// <summary>Linear blend of <paramref name="a"/> towards <paramref name="b"/> by <paramref name="t"/> (0..1).</summary>
    public static Color Mix(Color a, Color b, double t) => Color.FromArgb(a.A,
        (byte)Math.Round(a.R + (b.R - a.R) * t),
        (byte)Math.Round(a.G + (b.G - a.G) * t),
        (byte)Math.Round(a.B + (b.B - a.B) * t));

    public static SolidColorBrush Brush(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }
}
