using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Hermes.Data;
using Microsoft.Web.WebView2.Core;

namespace Hermes.Inna;

/// <summary>
/// Reads the student's data from Inna's own JSON API on nam.inna.is, the same calls the
/// student pages make. Runs inside a signed-in browser so Inna's session cookies apply;
/// nothing is sent anywhere else.
/// </summary>
public static class InnaClient
{
    // All requests run in one script on the student page. Only the fields Hermes uses come back.
    const string FetchScript = """
        (async () => {
          const get = async (path) => {
            const r = await fetch(path, { credentials: 'include', headers: { Accept: 'application/json, text/plain, */*' } });
            if (!r.ok) throw new Error(path.split('?')[0] + ' ' + r.status);
            const text = await r.text();
            return text ? JSON.parse(text) : null;
          };
          const user = await get('/api/UserData/GetLoggedInUser');
          const studentId = user.studentId, termId = user.defaultTermId;
          const [groups, courses, timetable, work, exams, attendance] = await Promise.all([
            get('/api/StudentInformation/currentGroups'),
            get('/api/StudentGrades/GetStudentGrades?termId=' + termId),
            get('/api/Timetable/GetTimetable?staff_id=&student_id=' + studentId +
                '&moduleId=&classroom_id=&class_id=&groupId=&terms=&date_from=__FROM__&date_to=__TO__&attendanceOverview=0'),
            get('/api/GetAssignments/GetStudentAssignments?type=0&control=0&order=0'),
            get('/api/GetAssignments/GetStudentAssignments?type=1&control=0&order=0'),
            get('/api/Attendance/GetAttendance?termId=&type=0'),
          ]);
          const graded = await Promise.all((groups || []).map(g =>
            get('/api/GetAssignments/GetStudentAssignmentsGroup?control=2&groupId=' + g.id + '&order=0&type=').catch(() => [])));
          return JSON.stringify({
            courses: (courses || []).map(c => ({ module: c.moduleName, subject: c.subjectName })),
            timetable: (timetable || []).map(t => ({
              start: t.start, startClock: t.startClock, endClock: t.endClock,
              module: t.titleShort, teacher: t.teacher, room: t.classroom })),
            assignments: [...(work || []), ...(exams || [])].map(a => ({
              id: a.assignmentId, name: a.name, module: a.module, due: a.handInFullDate,
              handedIn: String(a.handedIn), exam: String(a.exam) })),
            grades: graded.flat().filter(a => a && a.grade).map(a => ({
              id: a.assignmentId, name: a.name, module: a.module, grade: a.grade })),
            attendance: attendance && attendance.attendanceTerm ? attendance.attendanceTerm.realAttendance : null,
          });
        })()
        """;

    /// <summary>Fetches everything for this and next week. The browser must already be signed in on nam.inna.is.</summary>
    public static async Task<SchoolData> FetchAsync(CoreWebView2 core, DateTime now)
    {
        var monday = now.Date.AddDays(-(((int)now.DayOfWeek + 6) % 7));
        var script = FetchScript
            .Replace("__FROM__", monday.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture))
            .Replace("__TO__", monday.AddDays(13).ToString("dd.MM.yyyy", CultureInfo.InvariantCulture));

        var json = await EvaluateAsync(core, script);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        // Course codes (ÍSLE2RM06) to subject names (Íslenska).
        var subjects = new Dictionary<string, string>();
        foreach (var c in root.GetProperty("courses").EnumerateArray())
            if (Str(c, "module") is { } m && Str(c, "subject") is { Length: > 0 } s) subjects[m] = s;
        string Subject(string? module) =>
            module == null ? "" : subjects.TryGetValue(module, out var s) ? s : module;

        var data = new SchoolData { FetchedAt = now, IsSample = false };

        foreach (var t in root.GetProperty("timetable").EnumerateArray())
        {
            // "start" is "2026-11-06T08:30:00.000Z"; only its date is used, with the local clock times.
            if (Str(t, "start") is not { Length: >= 10 } start
                || !DateTime.TryParseExact(start[..10], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
                || !TimeSpan.TryParse(Str(t, "startClock"), CultureInfo.InvariantCulture, out var from)
                || !TimeSpan.TryParse(Str(t, "endClock"), CultureInfo.InvariantCulture, out var to)) continue;
            data.Lessons.Add(new Lesson(date + from, date + to, Subject(Str(t, "module")), Room(Str(t, "room")), Str(t, "teacher") ?? ""));
        }
        // The same class can come back twice (shared groups); keep one.
        data.Lessons = data.Lessons.DistinctBy(l => (l.Start, l.Subject)).OrderBy(l => l.Start).ToList();

        foreach (var a in root.GetProperty("assignments").EnumerateArray())
        {
            if (Str(a, "id") is not { } id
                || !DateTime.TryParseExact(Str(a, "due"), "dd.MM.yyyy HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var due))
                continue;
            var status = Str(a, "handedIn") == "1" ? "handedIn" : Str(a, "exam") == "1" ? "exam" : "open";
            data.Assignments.Add(new Assignment(id, Str(a, "name") ?? "", Subject(Str(a, "module")), due, status));
        }
        data.Assignments = data.Assignments.DistinctBy(a => a.Id).ToList();

        foreach (var g in root.GetProperty("grades").EnumerateArray())
        {
            // Only real marks ("8,5", "A"); "Skilað" etc. just mean handed in.
            if (Str(g, "id") is not { } id || Str(g, "grade") is not { } value || !IsMark(value)) continue;
            data.Grades.Add(new Grade(id, Subject(Str(g, "module")), Str(g, "name") ?? "", value, now));
        }
        data.Grades = data.Grades.DistinctBy(g => g.Id).ToList();

        if (Str(root, "attendance") is { } att && double.TryParse(att.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var pct))
            data.Attendance = pct;

        return data;
    }

    static bool IsMark(string value) =>
        Regex.IsMatch(value, @"\d") || Regex.IsMatch(value.Trim(), @"^[A-F][+-]?$");

    static string Room(string? room) =>
        string.IsNullOrWhiteSpace(room) ? "" : Regex.IsMatch(room, @"^\d") ? $"Stofa {room}" : room;

    static string? Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) ? v.ValueKind switch
        {
            JsonValueKind.String => v.GetString(),
            JsonValueKind.Number => v.GetRawText(),
            JsonValueKind.Null or JsonValueKind.Undefined => null,
            _ => v.GetRawText(),
        } : null;

    /// <summary>Runs an async script and returns its string result (DevTools Runtime.evaluate awaits the promise).</summary>
    static async Task<string> EvaluateAsync(CoreWebView2 core, string expression)
    {
        var request = JsonSerializer.Serialize(new { expression, awaitPromise = true, returnByValue = true });
        var response = await core.CallDevToolsProtocolMethodAsync("Runtime.evaluate", request);
        using var doc = JsonDocument.Parse(response);
        if (doc.RootElement.TryGetProperty("exceptionDetails", out var ex))
        {
            var message = ex.TryGetProperty("exception", out var e) && e.TryGetProperty("description", out var d)
                ? d.GetString() : ex.GetRawText();
            throw new InvalidOperationException($"Inna request failed: {message}");
        }
        return doc.RootElement.GetProperty("result").GetProperty("value").GetString()
            ?? throw new InvalidOperationException("Inna returned nothing");
    }
}
