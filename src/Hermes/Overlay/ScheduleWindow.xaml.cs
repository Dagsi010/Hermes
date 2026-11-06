using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using Hermes.Core;
using Hermes.Data;

namespace Hermes.Overlay;

public partial class ScheduleWindow : OverlayWindow
{
    readonly ObservableCollection<LessonVM> _today = new();
    bool _showWeek;

    /// <summary>The user switched between today and week view (true = week).</summary>
    public event Action<bool>? ViewToggled;

    public ScheduleWindow()
    {
        InitializeComponent();
        Part = "schedule";
        TodayList.ItemsSource = _today;
        InitParts(Body, Chrome);
    }

    public void Render(SchoolData data, DateTime now, bool showWeek)
    {
        _showWeek = showWeek;
        TodayTab.Text = Loc.T("ov.today");
        WeekTab.Text = Loc.T("ov.week");
        TodayTab.Opacity = showWeek ? 0.5 : 1;
        WeekTab.Opacity = showWeek ? 1 : 0.5;
        TodayTab.TextDecorations = showWeek ? null : TextDecorations.Underline;
        WeekTab.TextDecorations = showWeek ? TextDecorations.Underline : null;

        // Today, or the next school day if there are no classes today.
        var lessons = data.Lessons.Where(l => l.Start.Date == now.Date).OrderBy(l => l.Start).ToList();
        var shownDay = now.Date;
        if (lessons.Count == 0)
        {
            var next = data.Lessons.Where(l => l.Start.Date > now.Date).OrderBy(l => l.Start).FirstOrDefault();
            if (next != null)
            {
                shownDay = next.Start.Date;
                lessons = data.Lessons.Where(l => l.Start.Date == shownDay).OrderBy(l => l.Start).ToList();
            }
        }

        _today.Clear();
        foreach (var l in lessons.Concat(Breaks.For(lessons)).OrderBy(l => l.Start)) _today.Add(new LessonVM(l));

        var dateText = Loc.DateLong(shownDay);
        DateLabel.Text = shownDay == now.Date ? dateText : $"{Loc.T("ov.noClasses")} · {Loc.T("ov.next")}: {dateText}";
        NoClasses.Text = Loc.T("ov.noClasses");

        WeekList.ItemsSource = BuildWeek(data, now);

        TodayList.Visibility = showWeek ? Visibility.Collapsed : Visibility.Visible;
        NoClasses.Visibility = !showWeek && lessons.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        WeekList.Visibility = showWeek ? Visibility.Visible : Visibility.Collapsed;

        // "Uppfært 16:02" today, "Uppfært fim 5. 16:02" for an older (offline) copy.
        var when = data.FetchedAt.Date == now.Date
            ? Loc.Time(data.FetchedAt)
            : $"{Loc.DayShort(data.FetchedAt)} {Loc.Time(data.FetchedAt)}";
        var updated = Loc.F("ov.updated", when);
        UpdatedLabel.Text = data.IsSample ? $"{Loc.T("ov.sample")} · {updated}" : updated;
        Tick(now);
    }

    public void Tick(DateTime now)
    {
        foreach (var vm in _today) vm.Update(now);
    }

    static List<WeekDayVM> BuildWeek(SchoolData data, DateTime now)
    {
        var monday = now.Date.AddDays(-(((int)now.DayOfWeek + 6) % 7));
        // On weekends show the coming week.
        if (now.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) monday = monday.AddDays(7);

        var days = new List<WeekDayVM>();
        for (var i = 0; i < 5; i++)
        {
            var date = monday.AddDays(i);
            var lessons = data.Lessons.Where(l => l.Start.Date == date).OrderBy(l => l.Start).ToList();
            if (lessons.Count == 0) continue;

            // "Ísl · Dan · Þýs ×2 · Ísl · Töl ×2 · Ens"
            var groups = new List<(string Name, int Count)>();
            foreach (var l in lessons)
            {
                var name = Abbreviate(l.Subject);
                if (groups.Count > 0 && groups[^1].Name == name) groups[^1] = (name, groups[^1].Count + 1);
                else groups.Add((name, 1));
            }

            days.Add(new WeekDayVM
            {
                Header = $"{Loc.DayShort(date)} · {Loc.Time(lessons[0].Start)}–{Loc.Time(lessons[^1].End)}",
                Subjects = string.Join(" · ", groups.Select(g => g.Count > 1 ? $"{g.Name} ×{g.Count}" : g.Name)),
                IsToday = date == now.Date,
                IsPast = date < now.Date,
            });
        }
        return days;
    }

    static string Abbreviate(string subject) => subject.Length <= 4 ? subject : subject[..3];

    void TodayTab_Click(object sender, MouseButtonEventArgs e)
    {
        if (_showWeek) ViewToggled?.Invoke(false);
    }

    void WeekTab_Click(object sender, MouseButtonEventArgs e)
    {
        if (!_showWeek) ViewToggled?.Invoke(true);
    }
}
