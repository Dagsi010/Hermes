using System.Windows;
using System.Windows.Threading;
using Hermes.Core;
using Hermes.Data;
using Microsoft.Win32;
using Screen = System.Windows.Forms.Screen;

namespace Hermes.Overlay;

/// <summary>Owns the three overlay parts: data, layout, clock ticks, click-through and edit mode.</summary>
public sealed class OverlayManager
{
    readonly SettingsStore _settings;
    readonly LocalState _local = LocalState.Load();
    readonly ScheduleWindow _schedule = new();
    readonly WorkWindow _work = new();
    readonly ProgressWindow _progress = new();
    readonly DispatcherTimer _clock = new() { Interval = TimeSpan.FromSeconds(15) };
    readonly DispatcherTimer _hover = new() { Interval = TimeSpan.FromMilliseconds(40) };
    readonly DispatcherTimer _wallpaperCheck = new() { Interval = TimeSpan.FromMinutes(10) };

    SchoolData _data;

    public bool EditMode { get; private set; }
    public bool Visible => _settings.Current.OverlayVisible;

    public event Action? StateChanged;

    IEnumerable<OverlayWindow> Windows => new OverlayWindow[] { _schedule, _work, _progress };

    public OverlayManager(SettingsStore settings)
    {
        _settings = settings;
        // Last fetch from Inna if there is one; sample data only before the first fetch.
        _data = DataCache.Load() ?? SampleData.Create(Clock.Now);

        _schedule.ViewToggled += week =>
        {
            _settings.Current.ShowWeek = week;
            _settings.Save();
            _schedule.Render(_data, Clock.Now, week);
            _schedule.FadeIn();
        };
        _work.AssignmentToggled += ToggleDone;

        foreach (var w in Windows)
        {
            w.LayoutEdited += SaveLayout;
            w.EditDone += () => SetEditMode(false);
        }

        _clock.Tick += (_, _) => Tick();
        _hover.Tick += (_, _) => UpdateClickThrough();
        SystemEvents.DisplaySettingsChanged += (_, _) => Application.Current.Dispatcher.BeginInvoke(ApplyLayout);
        // Auto text colour follows the wallpaper: on change, and every 10 minutes for slideshows.
        SystemEvents.UserPreferenceChanged += (_, e) =>
        {
            if (e.Category == UserPreferenceCategory.Desktop) Application.Current.Dispatcher.BeginInvoke(ApplyAppearance);
        };
        _wallpaperCheck.Tick += (_, _) =>
        {
            if (_settings.Current.TextColor == TextColorMode.Auto) ApplyAppearance();
        };
        Loc.Instance.LanguageChanged += RenderAll;
    }

    public void Start()
    {
        ApplyAppearance();
        foreach (var w in Windows)
        {
            // Create the HWND without showing it so it can be placed before it appears.
            new System.Windows.Interop.WindowInteropHelper(w).EnsureHandle();
            DesktopLayer.Register(w.Handle);
        }
        ApplyLayout();
        RenderAll();
        if (Visible) ShowWindows();
        _clock.Start();
        _hover.Start();
        _wallpaperCheck.Start();
        // Auto colour needs the parts laid out on screen first.
        Application.Current.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, ApplyAppearance);
    }

    /// <summary>New data from Inna: merged with the cache (for "new" notices), saved, shown.</summary>
    public void SetData(SchoolData fresh)
    {
        _data = DataCache.Merge(_data, fresh);
        DataCache.Save(_data);
        RenderAll();
        foreach (var w in Windows) w.FadeIn();

        // First seen in this very fetch = new since last time (the first fetch ever counts as old).
        var grades = _data.Grades.Where(g => g.Posted == _data.FetchedAt).ToList();
        var work = _data.Assignments.Where(a => a.Status != "handedIn"
            && _data.AssignmentFirstSeen.TryGetValue(a.Id, out var seen) && seen == _data.FetchedAt).ToList();
        if (grades.Count > 0 || work.Count > 0) NewItems?.Invoke(grades, work);
    }

    /// <summary>Grades and assignments that appeared in the latest fetch.</summary>
    public event Action<IReadOnlyList<Grade>, IReadOnlyList<Assignment>>? NewItems;

    public SchoolData Data => _data;

    /// <summary>Time of the last successful fetch from Inna; null before the first one.</summary>
    public DateTime? LastFetch => _data.IsSample ? null : _data.FetchedAt;

    /// <summary>Open work due tomorrow, for the evening reminder.</summary>
    public List<Assignment> DueTomorrow(DateTime now) => _data.IsSample ? new() : _data.Assignments
        .Where(a => a.Due.Date == now.Date.AddDays(1) && a.Status != "handedIn" && !_local.Done.ContainsKey(a.Id))
        .OrderBy(a => a.Due)
        .ToList();

    /// <summary>True the first time it is asked on a given day: the evening reminder goes out once.</summary>
    public bool ClaimReminder(DateTime day)
    {
        if (_local.LastReminder?.Date == day.Date) return false;
        _local.LastReminder = day.Date;
        _local.Save();
        return true;
    }

    public void Refresh()
    {
        // The Inna fetch itself runs through InnaSession; this redraws what is there now.
        if (_data.IsSample) _data = SampleData.Create(Clock.Now);
        RenderAll();
        foreach (var w in Windows) w.FadeIn();
    }

    public void ApplySettings()
    {
        ApplyAppearance();
        ApplyLayout();
        RenderAll();
    }

    public void SetVisible(bool visible)
    {
        _settings.Current.OverlayVisible = visible;
        _settings.Save();
        if (visible) ShowWindows();
        else foreach (var w in Windows) w.Hide();
        StateChanged?.Invoke();
    }

    public void SetEditMode(bool on)
    {
        if (EditMode == on) return;
        EditMode = on;
        if (on && !Visible) SetVisible(true);
        _schedule.SetEditMode(on, Loc.T("ov.part.schedule"), Loc.T("ov.done"));
        _work.SetEditMode(on, Loc.T("ov.part.work"), Loc.T("ov.done"));
        _progress.SetEditMode(on, Loc.T("ov.part.progress"), Loc.T("ov.done"));
        if (!on) SaveLayout();
        StateChanged?.Invoke();
    }

    public IEnumerable<OverlayWindow> AllWindows => Windows;

    void ShowWindows()
    {
        foreach (var w in Windows)
        {
            w.Show();
            w.FadeIn();
        }
        DesktopLayer.Enforce();
    }

    void ApplyAppearance()
    {
        var s = _settings.Current;
        var theme = Themes.Get(s.Theme);
        OverlayTheme.ApplySizes(s.TextScale);
        OverlayTheme.ApplyColors(Application.Current.Resources, s.TextColor == TextColorMode.Dark, theme);
        foreach (var w in Windows) w.ApplyTheme(DarkTextFor(w), theme);
    }

    readonly HashSet<string> _autoLogged = new();

    /// <summary>
    /// Light or dark text for one part. In Auto mode: dark when the wallpaper behind the part is
    /// bright, with a gap between the two thresholds so a borderline wallpaper doesn't flip back and forth.
    /// </summary>
    bool DarkTextFor(OverlayWindow w)
    {
        var mode = _settings.Current.TextColor;
        if (mode != TextColorMode.Auto) return mode == TextColorMode.Dark;
        var area = w.ContentScreenRect;
        if (area.IsEmpty) return w.DarkText;
        var b = TargetScreen.Bounds;
        if (WallpaperSampler.Brightness(area, new Rect(b.Left, b.Top, b.Width, b.Height)) is not { } brightness) return false;
        var dark = w.DarkText ? brightness > 0.5 : brightness > 0.6;
        if (dark != w.DarkText || !_autoLogged.Contains(w.Part))
        {
            _autoLogged.Add(w.Part);
            Log.Info($"Auto colour {w.Part}: wallpaper brightness {brightness:0.00} -> {(dark ? "dark" : "light")} text");
        }
        return dark;
    }

    void RenderAll()
    {
        var now = Clock.Now;
        _schedule.Render(_data, now, _settings.Current.ShowWeek);
        _work.Render(_data, _local, BuildNotices(now), now);
        RenderProgress(now);
    }

    void Tick()
    {
        var now = Clock.Now;
        _schedule.Tick(now);
        _work.Tick(now, _local);
        RenderProgress(now);
    }

    void RenderProgress(DateTime now)
    {
        var state = DayState.Compute(_data.Lessons, now);
        _progress.Render(state, now);
        StatusChanged?.Invoke(string.Join(" · ", state.Headline));
    }

    /// <summary>The progress line ("Danska · 35 mín eftir · ..."), for the tray tooltip.</summary>
    public event Action<string>? StatusChanged;

    string? _signInKey;
    Action? _signInAction;

    /// <summary>Shows a "Sign in again" style link at the top of Tilkynningar; null key removes it.</summary>
    public void SetSignInNotice(string? key, Action? action)
    {
        if (_signInKey == key) return;
        _signInKey = key;
        _signInAction = action;
        RenderAll();
    }

    List<NoticeVM> BuildNotices(DateTime now)
    {
        var notices = new List<NoticeVM>();
        if (_signInKey != null)
            notices.Add(new NoticeVM { Text = Loc.T(_signInKey), Kind = NoticeKind.Link, Action = _signInAction });
        foreach (var g in _data.Grades.Where(g => now - g.Posted < TimeSpan.FromDays(3)).OrderByDescending(g => g.Posted))
            notices.Add(new NoticeVM { Text = Loc.F("n.grade", g.Course, g.Value) });
        foreach (var a in _data.Assignments.Where(a => a.Status != "handedIn"
                     && _data.AssignmentFirstSeen.TryGetValue(a.Id, out var seen) && now - seen < TimeSpan.FromDays(2)))
            notices.Add(new NoticeVM { Text = Loc.F("n.newWork", a.Title) });
        if (_data.Attendance is { } attendance)
        {
            notices.Add(new NoticeVM
            {
                Text = Loc.F("n.attendance", attendance.ToString("0.#", Loc.Instance.Culture)),
                Kind = attendance < _settings.Current.AttendanceThreshold ? NoticeKind.Warning : NoticeKind.Info,
            });
        }
        return notices;
    }

    void ToggleDone(string id)
    {
        var done = !_local.Done.ContainsKey(id);
        if (done) _local.Done[id] = Clock.Now;
        else _local.Done.Remove(id);
        _local.Save();
        _work.SetDone(id, done);
    }

    /// <summary>
    /// Clicks pass through to the desktop unless the cursor is over an interactive element
    /// (task rows, links, the view toggle) or Edit layout mode is on.
    /// </summary>
    void UpdateClickThrough()
    {
        Native.GetCursorPos(out var p);
        var buttonDown = System.Windows.Input.Mouse.LeftButton == System.Windows.Input.MouseButtonState.Pressed;
        foreach (var w in Windows)
        {
            if (EditMode) { w.SetClickThrough(false); continue; }
            var interactive = w.ContainsScreenPoint(p) && w.IsInteractiveAt(p);
            // Don't flip mid-click, or the button-up would land on the desktop.
            if (!interactive && buttonDown && !w.IsClickThrough) continue;
            w.SetClickThrough(!interactive);
        }
    }

    // ---- Layout -------------------------------------------------------------

    Screen TargetScreen =>
        Screen.AllScreens.FirstOrDefault(s => s.DeviceName == _settings.Current.Monitor) ?? Screen.PrimaryScreen ?? Screen.AllScreens[0];

    static string LayoutKey(Screen s) => $"{s.DeviceName}|{s.Bounds.Width}x{s.Bounds.Height}";

    OverlayLayout CurrentLayout =>
        _settings.Current.Layouts.TryGetValue(LayoutKey(TargetScreen), out var l) ? l : new OverlayLayout();

    void ApplyLayout()
    {
        var screen = TargetScreen;
        var layout = CurrentLayout;
        Place(_schedule, layout.Schedule, screen);
        Place(_work, layout.Work, screen);
        Place(_progress, layout.Progress, screen);
        // Moving onto a monitor with another DPI makes WPF resize the window; place it again afterwards.
        Application.Current.Dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
        {
            Place(_schedule, layout.Schedule, screen);
            Place(_work, layout.Work, screen);
            Place(_progress, layout.Progress, screen);
        });
    }

    static void Place(OverlayWindow w, RelRect r, Screen screen)
    {
        var b = screen.Bounds;
        w.SetScreenRect(
            b.Left + (int)Math.Round(r.X * b.Width),
            b.Top + (int)Math.Round(r.Y * b.Height),
            (int)Math.Round(r.W * b.Width),
            (int)Math.Round(r.H * b.Height));
    }

    void SaveLayout()
    {
        var screen = TargetScreen;
        _settings.Current.Layouts[LayoutKey(screen)] = new OverlayLayout
        {
            Schedule = ToRel(_schedule, screen),
            Work = ToRel(_work, screen),
            Progress = ToRel(_progress, screen),
        };
        _settings.Save();
        ApplyAppearance(); // moved parts may now sit on a lighter or darker area
    }

    static RelRect ToRel(OverlayWindow w, Screen screen)
    {
        var b = screen.Bounds;
        var r = w.ScreenRect;
        return new RelRect(
            (r.Left - b.Left) / (double)b.Width,
            (r.Top - b.Top) / (double)b.Height,
            (r.Right - r.Left) / (double)b.Width,
            (r.Bottom - r.Top) / (double)b.Height);
    }
}
