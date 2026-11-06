using System.Windows.Media;
using Hermes.Core;

namespace Hermes.Ui;

/// <summary>One preview tile in the Settings theme picker.</summary>
public sealed class ThemeSwatch
{
    public ThemeSwatch(Theme theme, bool selected)
    {
        Theme = theme;
        RingBrush = Themes.Brush(selected ? theme.Accent : theme.CardBorder);
    }

    public Theme Theme { get; }
    public string Name => Theme.Name;
    public Brush BackgroundBrush => Themes.Brush(Theme.Background);
    public Brush CardBrush => Themes.Brush(Theme.Card);
    public Brush AccentBrush => Themes.Brush(Theme.Accent);
    public Brush TodayBrush => Themes.Brush(Theme.DueToday);
    public Brush TomorrowBrush => Themes.Brush(Theme.DueTomorrow);
    public Brush RingBrush { get; }
}
