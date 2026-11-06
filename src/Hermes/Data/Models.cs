namespace Hermes.Data;

/// <param name="IsBreak">A break worked out from the gaps (see <see cref="Breaks"/>), not a class.</param>
public sealed record Lesson(DateTime Start, DateTime End, string Subject, string Room, string Teacher, bool IsBreak = false);

/// <param name="Status">"open", "exam" (a próf) or "handedIn".</param>
public sealed record Assignment(string Id, string Title, string Course, DateTime Due, string Status);

/// <param name="Posted">When Hermes first saw this grade (Inna gives no date).</param>
public sealed record Grade(string Id, string Course, string Name, string Value, DateTime Posted);

/// <summary>Everything the overlay shows, as fetched from Inna (or sample data).</summary>
public sealed class SchoolData
{
    public List<Lesson> Lessons { get; set; } = new();
    public List<Assignment> Assignments { get; set; } = new();
    public List<Grade> Grades { get; set; } = new();
    /// <summary>Attendance in percent, if known.</summary>
    public double? Attendance { get; set; }
    /// <summary>When each assignment (by id) first showed up, for "new assignment" notices.</summary>
    public Dictionary<string, DateTime> AssignmentFirstSeen { get; set; } = new();
    public DateTime FetchedAt { get; set; }
    public bool IsSample { get; set; }
}
