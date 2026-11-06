using Hermes.Core;

namespace Hermes.Data;

/// <summary>
/// Breaks between classes, worked out from the gaps in the timetable. Only gaps of 15 minutes
/// or more are listed (the frímínútur after first class, hádegishlé); the few minutes between
/// back-to-back classes are not.
/// </summary>
public static class Breaks
{
    public static readonly TimeSpan MinListed = TimeSpan.FromMinutes(15);

    /// <summary>Listed breaks for the given classes, as lessons with <see cref="Lesson.IsBreak"/> set.</summary>
    public static List<Lesson> For(IEnumerable<Lesson> lessons)
    {
        var breaks = new List<Lesson>();
        foreach (var day in lessons.Where(l => !l.IsBreak).GroupBy(l => l.Start.Date))
        {
            var sorted = day.OrderBy(l => l.Start).ToList();
            for (var i = 0; i + 1 < sorted.Count; i++)
            {
                DateTime from = sorted[i].End, to = sorted[i + 1].Start;
                if (to - from >= MinListed) breaks.Add(new Lesson(from, to, NameFor(from, to), "", "", IsBreak: true));
            }
        }
        return breaks;
    }

    /// <summary>"Hádegishlé" for the long midday gap (12:20-13:00 at Verzló), otherwise "Frímínútur".</summary>
    public static string NameFor(DateTime from, DateTime to) =>
        to - from >= TimeSpan.FromMinutes(30) && from.TimeOfDay < new TimeSpan(13, 30, 0) && to.TimeOfDay > new TimeSpan(11, 30, 0)
            ? Loc.T("br.lunch")
            : Loc.T("br.break");
}
