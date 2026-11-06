namespace Hermes.Data;

/// <summary>
/// Stand-in data until Inna is wired up. The timetable is the real one from
/// the layout sketch; rooms, teachers, assignments and grades are made up.
/// </summary>
public static class SampleData
{
    static readonly (string Start, string Subject, string Room, string Teacher)[] Day =
    {
        ("08:30", "Íslenska", "Stofa 204", "GÞ"),
        ("09:50", "Danska", "Stofa 112", "SK"),
        ("10:40", "Þýska", "Stofa 305", "HM"),
        ("11:30", "Þýska", "Stofa 305", "HM"),
        ("12:20", "Íslenska", "Stofa 204", "GÞ"),
        ("13:00", "Tölvur", "Tölvustofa 2", "ÁB"),
        ("13:50", "Tölvur", "Tölvustofa 2", "ÁB"),
        ("14:40", "Enska", "Stofa 118", "RJ"),
    };

    static readonly TimeSpan SchoolEnds = new(15, 30, 0);

    public static SchoolData Create(DateTime now)
    {
        var data = new SchoolData { FetchedAt = now, IsSample = true, Attendance = 94.2 };

        // This week and next, Monday to Friday. Each class ends 10 minutes before the next one.
        var monday = now.Date.AddDays(-(((int)now.DayOfWeek + 6) % 7));
        for (var d = 0; d < 12; d++)
        {
            var date = monday.AddDays(d);
            if (date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) continue;
            for (var i = 0; i < Day.Length; i++)
            {
                var start = date + TimeSpan.Parse(Day[i].Start);
                var end = i + 1 < Day.Length
                    ? date + TimeSpan.Parse(Day[i + 1].Start) - TimeSpan.FromMinutes(10)
                    : date + SchoolEnds;
                data.Lessons.Add(new Lesson(start, end, Day[i].Subject, Day[i].Room, Day[i].Teacher));
            }
        }

        var today = now.Date;
        data.Assignments.AddRange(new[]
        {
            new Assignment("s1", "Ritgerð um Njálu", "Íslenska", today.AddHours(22), "open"),
            new Assignment("s2", "Verkefnablað 4", "Danska", today.AddDays(1).AddHours(23).AddMinutes(59), "open"),
            new Assignment("s3", "Python: lykkjur", "Tölvur", today.AddDays(4).AddHours(23).AddMinutes(59), "open"),
            new Assignment("s4", "Glósur úr kafla 6", "Þýska", today.AddDays(6).AddHours(23).AddMinutes(59), "open"),
            new Assignment("s5", "Short story essay", "Enska", today.AddDays(9).AddHours(23).AddMinutes(59), "open"),
        });

        data.Grades.Add(new Grade("g1", "Danska", "Stíll 3", "8,5", now.AddHours(-2)));
        foreach (var a in data.Assignments) data.AssignmentFirstSeen[a.Id] = a.Id == "s3" ? now : DateTime.MinValue;
        return data;
    }
}
