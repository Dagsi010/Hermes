using Hermes.Core;

namespace Hermes.Data;

public enum DayPhase { NoSchool, Before, InClass, Break, After }

/// <summary>Where we are in the school day: drives the progress bar and the schedule highlight.</summary>
public sealed class DayState
{
    public DayPhase Phase { get; private init; }
    /// <summary>Lessons shown on the bar: today's, or the next school day's when there is no school today.</summary>
    public IReadOnlyList<Lesson> Lessons { get; private init; } = Array.Empty<Lesson>();
    public Lesson? Current { get; private init; }
    public Lesson? Next { get; private init; }
    public Lesson? NextDayFirst { get; private init; }
    public double DayFraction { get; private init; }

    /// <summary>Headline parts: the first one is emphasised.</summary>
    public IReadOnlyList<string> Headline { get; private init; } = Array.Empty<string>();
    public string Footer { get; private init; } = "";

    public double FillOf(Lesson lesson, DateTime now)
    {
        if (now >= lesson.End) return 1;
        if (now <= lesson.Start) return 0;
        return (now - lesson.Start).TotalSeconds / (lesson.End - lesson.Start).TotalSeconds;
    }

    public static DayState Compute(IEnumerable<Lesson> all, DateTime now)
    {
        var sorted = all.Where(l => !l.IsBreak).OrderBy(l => l.Start).ToList();
        var today = sorted.Where(l => l.Start.Date == now.Date).ToList();
        var nextDayFirst = sorted.FirstOrDefault(l => l.Start.Date > now.Date);

        // The bar shows classes and the listed breaks (15 min or longer) between them.
        static List<Lesson> WithBreaks(List<Lesson> day) => day.Concat(Breaks.For(day)).OrderBy(l => l.Start).ToList();

        if (today.Count == 0)
        {
            var nextDay = nextDayFirst == null
                ? new List<Lesson>()
                : sorted.Where(l => l.Start.Date == nextDayFirst.Start.Date).ToList();
            var parts = new List<string> { Loc.T("pr.noSchool") };
            if (nextDayFirst != null) parts.Add(NextDayText(nextDayFirst, now));
            return new DayState { Phase = DayPhase.NoSchool, Lessons = WithBreaks(nextDay), NextDayFirst = nextDayFirst, Headline = parts };
        }
        var segments = WithBreaks(today);

        var first = today[0];
        var last = today[^1];
        var fraction = Math.Clamp((now - first.Start).TotalSeconds / (last.End - first.Start).TotalSeconds, 0, 1);
        var current = today.FirstOrDefault(l => l.Start <= now && now < l.End);
        var next = today.FirstOrDefault(l => l.Start > now);
        var dayDone = Loc.F("pr.dayDone", (int)Math.Floor(fraction * 100));
        var endsAt = Loc.F("pr.endsAt", Loc.Time(last.End));

        if (now < first.Start)
        {
            return new DayState
            {
                Phase = DayPhase.Before, Lessons = segments, Next = first, NextDayFirst = nextDayFirst,
                Headline = new[] { Loc.F("pr.before", first.Subject, Loc.Time(first.Start)), Loc.F("pr.beforeIn", Loc.Duration(first.Start - now)) },
                Footer = Capitalise(endsAt),
            };
        }

        if (now >= last.End)
        {
            var parts = new List<string> { Loc.T("pr.after") };
            if (nextDayFirst != null) parts.Add(NextDayText(nextDayFirst, now));
            return new DayState
            {
                Phase = DayPhase.After, Lessons = segments, NextDayFirst = nextDayFirst, DayFraction = 1,
                Headline = parts, Footer = Loc.F("pr.endedAt", Loc.Time(last.End)),
            };
        }

        if (current != null)
        {
            var parts = new List<string>
            {
                current.Subject,
                Loc.F("pr.left", Loc.Duration(current.End - now)),
                next != null ? Loc.F("pr.next", next.Subject, Loc.Time(next.Start)) : Loc.T("pr.last"),
            };
            return new DayState
            {
                Phase = DayPhase.InClass, Lessons = segments, Current = current, Next = next, NextDayFirst = nextDayFirst,
                DayFraction = fraction, Headline = parts, Footer = $"{dayDone} · {endsAt}",
            };
        }

        // Between classes: "Hádegishlé · 12 mín eftir · næst Íslenska 13:00". Short gaps aren't
        // listed in the schedule, but the bar text still names them.
        var previousEnd = today.Last(l => l.End <= now).End;
        var breakName = segments.FirstOrDefault(l => l.IsBreak && l.Start <= now && now < l.End)?.Subject
                        ?? Breaks.NameFor(previousEnd, next!.Start);
        return new DayState
        {
            Phase = DayPhase.Break, Lessons = segments, Current = null, Next = next, NextDayFirst = nextDayFirst, DayFraction = fraction,
            Headline = new[]
            {
                breakName,
                Loc.F("pr.left", Loc.Duration(next!.Start - now)),
                Loc.F("pr.next", next.Subject, Loc.Time(next.Start)),
            },
            Footer = $"{dayDone} · {endsAt}",
        };
    }

    static string NextDayText(Lesson first, DateTime now) =>
        first.Start.Date == now.Date.AddDays(1)
            ? Loc.F("pr.tomorrow", first.Subject, Loc.Time(first.Start))
            : Loc.F("pr.onDay", Loc.DayName(first.Start), first.Subject, Loc.Time(first.Start));

    static string Capitalise(string s) => s.Length == 0 ? s : char.ToUpper(s[0], Loc.Instance.Culture) + s[1..];
}
