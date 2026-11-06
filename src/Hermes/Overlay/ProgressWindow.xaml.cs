using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media.Animation;
using Hermes.Data;

namespace Hermes.Overlay;

public partial class ProgressWindow : OverlayWindow
{
    const double Gap = 4;

    sealed class Segment
    {
        public required Lesson Lesson { get; init; }
        public required Border Track { get; init; }
        public required Border Fill { get; init; }
        public double Fraction { get; set; }
    }

    readonly List<Segment> _segments = new();

    public ProgressWindow()
    {
        InitializeComponent();
        Part = "progress";
        InitParts(Body, Chrome);
    }

    public void Render(DayState state, DateTime now)
    {
        Headline.Inlines.Clear();
        for (var i = 0; i < state.Headline.Count; i++)
        {
            if (i > 0) Headline.Inlines.Add(new Run("  ·  "));
            var run = new Run(state.Headline[i]);
            if (i == 0)
            {
                run.FontWeight = FontWeights.SemiBold;
                if (state.Phase == DayPhase.InClass) run.SetResourceReference(TextElement.ForegroundProperty, "Accent");
            }
            Headline.Inlines.Add(run);
        }
        Footer.Text = state.Footer;
        Footer.Visibility = state.Footer.Length > 0 ? Visibility.Visible : Visibility.Collapsed;

        if (!_segments.Select(s => s.Lesson).SequenceEqual(state.Lessons)) Rebuild(state.Lessons);

        foreach (var s in _segments)
        {
            // Nothing is under way on a day without school; the bar is a faint preview of the next one.
            s.Fraction = state.Phase == DayPhase.NoSchool ? 0 : state.FillOf(s.Lesson, now);
            s.Fill.SetResourceReference(Border.BackgroundProperty, s.Fraction >= 1 ? "Finished" : "Accent");
        }
        Layout(animate: true);
    }

    void Rebuild(IReadOnlyList<Lesson> lessons)
    {
        Bar.Children.Clear();
        _segments.Clear();
        foreach (var lesson in lessons)
        {
            // Breaks are drawn as a thinner, softer line between the class segments.
            var height = lesson.IsBreak ? Bar.Height / 2 : Bar.Height;
            var top = (Bar.Height - height) / 2;
            var track = new Border { CornerRadius = new CornerRadius(2), Height = height, Opacity = lesson.IsBreak ? 0.7 : 1 };
            track.SetResourceReference(Border.BackgroundProperty, "Track");
            var fill = new Border { CornerRadius = new CornerRadius(2), Height = height, Width = 0, Opacity = lesson.IsBreak ? 0.7 : 1 };
            Canvas.SetTop(track, top);
            Canvas.SetTop(fill, top);
            Bar.Children.Add(track);
            Bar.Children.Add(fill);
            _segments.Add(new Segment { Lesson = lesson, Track = track, Fill = fill });
        }
    }

    void Layout(bool animate)
    {
        if (_segments.Count == 0) return;
        var available = Math.Max(0, Bar.ActualWidth - Gap * (_segments.Count - 1));
        var total = _segments.Sum(s => (s.Lesson.End - s.Lesson.Start).TotalSeconds);
        var x = 0.0;
        foreach (var s in _segments)
        {
            var width = available * (s.Lesson.End - s.Lesson.Start).TotalSeconds / total;
            Canvas.SetLeft(s.Track, x);
            Canvas.SetLeft(s.Fill, x);
            s.Track.Width = width;

            var target = width * s.Fraction;
            if (animate)
            {
                s.Fill.BeginAnimation(WidthProperty, new DoubleAnimation(target, TimeSpan.FromMilliseconds(800))
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
                });
            }
            else
            {
                s.Fill.BeginAnimation(WidthProperty, null);
                s.Fill.Width = target;
            }
            x += width + Gap;
        }
    }

    void Bar_SizeChanged(object sender, SizeChangedEventArgs e) => Layout(animate: false);
}
