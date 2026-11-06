using System.ComponentModel;
using System.Runtime.CompilerServices;
using Hermes.Core;
using Hermes.Data;

namespace Hermes.Overlay;

public abstract class Observable : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}

public enum LessonState { Past, Current, Future }

public sealed class LessonVM : Observable
{
    LessonState _state;

    public LessonVM(Lesson lesson) => Lesson = lesson;

    public Lesson Lesson { get; }
    /// <summary>"08:30 - 09:20"</summary>
    public string Time => $"{Loc.Time(Lesson.Start)} - {Loc.Time(Lesson.End)}";
    public string Subject => Lesson.Subject;
    public string Room => Lesson.Room;
    public bool IsBreak => Lesson.IsBreak;

    public LessonState State
    {
        get => _state;
        private set => Set(ref _state, value);
    }

    public void Update(DateTime now) =>
        State = now >= Lesson.End ? LessonState.Past : now >= Lesson.Start ? LessonState.Current : LessonState.Future;
}

/// <summary>One day in the compact week view.</summary>
public sealed class WeekDayVM
{
    public required string Header { get; init; }
    public required string Subjects { get; init; }
    public bool IsToday { get; init; }
    public bool IsPast { get; init; }
}

public enum Urgency { Overdue, Today, Tomorrow, Later }

public sealed class AssignmentVM : Observable
{
    string _countdown = "";
    Urgency _urgency;
    bool _isDone;

    public AssignmentVM(Assignment a) => Assignment = a;

    public Assignment Assignment { get; }
    public string Title => Assignment.Title;
    public string Course => Assignment.Status == "exam" ? $"{Assignment.Course} · {Loc.T("ov.exam")}" : Assignment.Course;

    public string Countdown { get => _countdown; private set => Set(ref _countdown, value); }
    public Urgency Urgency { get => _urgency; private set => Set(ref _urgency, value); }
    public bool IsDone { get => _isDone; set => Set(ref _isDone, value); }

    public void Update(DateTime now)
    {
        var days = (Assignment.Due.Date - now.Date).Days;
        Urgency = days < 0 || Assignment.Due < now ? Urgency.Overdue
            : days == 0 ? Urgency.Today
            : days == 1 ? Urgency.Tomorrow
            : Urgency.Later;

        // "í dag 22:00": the hand-in time matters when it is close. A due time of exactly
        // midnight usually means Inna only gave a date, so it is left out.
        var countdown = Loc.Countdown(Assignment.Due, now);
        if (Urgency is Urgency.Today or Urgency.Tomorrow && Assignment.Due.TimeOfDay != TimeSpan.Zero)
            countdown += " " + Loc.Time(Assignment.Due);
        Countdown = countdown;
    }
}

public enum NoticeKind { Info, Warning, Link }

public sealed class NoticeVM
{
    public required string Text { get; init; }
    public NoticeKind Kind { get; init; }
    public bool IsLink => Kind == NoticeKind.Link;
    public Action? Action { get; init; }
}
