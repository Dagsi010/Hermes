using System.Globalization;
using System.Text;
using Hermes.Data;

namespace Hermes.Core;

/// <summary>
/// iCalendar (.ics) export of the timetable and deadlines, for Google Calendar, Outlook or
/// the Windows calendar. With "keep updated" on, the file is rewritten after every fetch.
/// </summary>
public static class IcsExport
{
    public static string Build(SchoolData data)
    {
        var sb = new StringBuilder();
        void Line(string s) => sb.Append(Fold(s)).Append("\r\n");
        var stamp = Utc(DateTime.Now);

        Line("BEGIN:VCALENDAR");
        Line("VERSION:2.0");
        Line("PRODID:-//Hermes//Inna timetable//IS");
        Line("CALSCALE:GREGORIAN");
        Line("X-WR-CALNAME:Hermes · Verzló");

        foreach (var l in data.Lessons.Where(l => !l.IsBreak))
        {
            Line("BEGIN:VEVENT");
            Line($"UID:lesson-{l.Start:yyyyMMddHHmm}-{StableHash(l.Subject)}@hermes");
            Line($"DTSTAMP:{stamp}");
            Line($"DTSTART:{Utc(l.Start)}");
            Line($"DTEND:{Utc(l.End)}");
            Line($"SUMMARY:{Escape(l.Subject)}");
            if (l.Room.Length > 0) Line($"LOCATION:{Escape(l.Room)}");
            if (l.Teacher.Length > 0) Line($"DESCRIPTION:{Escape(Loc.T("cal.teacher") + ": " + l.Teacher)}");
            Line("END:VEVENT");
        }

        foreach (var a in data.Assignments.Where(a => a.Status != "handedIn"))
        {
            // A short block ending at the hand-in time, with a reminder an hour before.
            var label = Loc.T(a.Status == "exam" ? "cal.exam" : "cal.due");
            Line("BEGIN:VEVENT");
            Line($"UID:assignment-{a.Id}@hermes");
            Line($"DTSTAMP:{stamp}");
            Line($"DTSTART:{Utc(a.Due.AddMinutes(-30))}");
            Line($"DTEND:{Utc(a.Due)}");
            Line($"SUMMARY:{Escape($"{label}: {a.Title} ({a.Course})")}");
            Line("BEGIN:VALARM");
            Line("ACTION:DISPLAY");
            Line($"DESCRIPTION:{Escape(a.Title)}");
            Line("TRIGGER:-PT1H");
            Line("END:VALARM");
            Line("END:VEVENT");
        }

        Line("END:VCALENDAR");
        return sb.ToString();
    }

    public static void Export(string path, SchoolData data) =>
        File.WriteAllText(path, Build(data), new UTF8Encoding(false));

    public static void AutoExport(AppSettings settings, SchoolData data)
    {
        if (!settings.CalendarSync || string.IsNullOrEmpty(settings.IcsPath) || data.IsSample) return;
        try
        {
            Export(settings.IcsPath, data);
        }
        catch (Exception ex)
        {
            Log.Error("Could not update the .ics file", ex);
        }
    }

    /// <summary>
    /// FNV-1a: UIDs must be identical on every export so calendars update events instead of
    /// duplicating them (string.GetHashCode changes between runs).
    /// </summary>
    static string StableHash(string s)
    {
        var hash = 2166136261u;
        foreach (var b in Encoding.UTF8.GetBytes(s)) hash = (hash ^ b) * 16777619u;
        return hash.ToString("x8");
    }

    static string Utc(DateTime local) => local.ToUniversalTime().ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);

    static string Escape(string s) =>
        s.Replace("\\", "\\\\").Replace(";", "\\;").Replace(",", "\\,").Replace("\r", "").Replace("\n", "\\n");

    /// <summary>RFC 5545: lines over 75 octets continue on the next line after a space.</summary>
    static string Fold(string line)
    {
        if (Encoding.UTF8.GetByteCount(line) <= 75) return line;
        var sb = new StringBuilder();
        var bytes = 0;
        foreach (var ch in line)
        {
            var size = Encoding.UTF8.GetByteCount(ch.ToString());
            if (bytes + size > 74)
            {
                sb.Append("\r\n ");
                bytes = 1;
            }
            sb.Append(ch);
            bytes += size;
        }
        return sb.ToString();
    }
}
