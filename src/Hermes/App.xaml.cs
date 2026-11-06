using System.Globalization;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using Hermes.Core;
using Hermes.Data;
using Hermes.Inna;
using Hermes.Overlay;
using Hermes.Tray;
using Hermes.Ui;

namespace Hermes;

public partial class App : Application
{
    const string MutexName = @"Local\Hermes.SingleInstance.7c1d2f0e";
    const string ActivateEventName = @"Local\Hermes.Activate.7c1d2f0e";

    Mutex? _mutex;
    EventWaitHandle? _activate;
    SettingsStore _settings = null!;
    OverlayManager _overlay = null!;
    TrayIcon? _tray;
    HotkeyService? _hotkey;
    SettingsWindow? _settingsWindow;
    SignInWindow? _signInWindow;
    InnaSession _inna = null!;
    bool _hotkeyOk = true;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += (_, args) =>
        {
            Log.Error("Unhandled exception", args.Exception);
            args.Handled = true;
        };

        var args = new Args(e.Args);
        if (args.FakeNow is { } fake) Clock.Fake(fake);

        if (args.IcsFile is { } ics)
        {
            // Writes the cached data as .ics and exits (also usable from Task Scheduler).
            if (DataCache.Load() is { } cached) IcsExport.Export(ics, cached);
            Shutdown();
            return;
        }

        if (args.DiagnoseFile is { } diagnose)
        {
            InnaBrowser.UseEdgeUserAgent = SettingsStore.Load().Current.InnaEdgeUserAgent;
            Dispatcher.BeginInvoke(async () =>
            {
                if (args.CaptureTest) await InnaDiagnostics.RunCaptureTestAsync(diagnose);
                else await InnaDiagnostics.RunAsync(diagnose);
                Shutdown();
            });
            return;
        }

        // Only one copy: a second launch asks the first one to open Settings and exits.
        _mutex = new Mutex(true, MutexName, out var first);
        _activate = new EventWaitHandle(false, EventResetMode.AutoReset, ActivateEventName);
        if (!first && args.SnapshotDir == null)
        {
            _activate.Set();
            Shutdown();
            return;
        }
        var activate = _activate;
        new Thread(() =>
        {
            while (activate.WaitOne()) Dispatcher.BeginInvoke(OpenSettings);
        }) { IsBackground = true, Name = "Hermes activation" }.Start();

        _settings = SettingsStore.Load();
        Loc.Instance.Language = _settings.Current.Language;
        Themes.Apply(Themes.Get(_settings.Current.Theme));
        if (args.SnapshotDir == null) StartupRegistration.Sync(_settings.Current.StartWithWindows);

        _overlay = new OverlayManager(_settings);
        _overlay.Start();

        if (args.SnapshotDir is { } dir)
        {
            Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, async () =>
            {
                await Task.Delay(1200); // let the fade-in finish
                Snapshot.Save(dir, _overlay, _settings.Current.Monitor);
                Shutdown();
            });
            return;
        }

        _inna = new InnaSession(_settings);
        _inna.StateChanged += OnInnaStateChanged;
        _inna.DataFetched += data =>
        {
            _overlay.SetData(data);
            IcsExport.AutoExport(_settings.Current, _overlay.Data);
        };
        _overlay.NewItems += OnNewItems;
        _schedule.Tick += (_, _) => OnScheduleTick();
        _schedule.Start();
        Microsoft.Win32.SystemEvents.PowerModeChanged += OnPowerModeChanged;

        _tray = new TrayIcon
        {
            IsOverlayVisible = () => _overlay.Visible,
            IsEditMode = () => _overlay.EditMode,
            IsSignedIn = () => _settings.Current.InnaSetupDone,
        };
        _tray.RefreshClicked += Refresh;
        _tray.SignInClicked += OpenSignIn;
        _tray.SignOutClicked += async () => await _inna.SignOutAsync();
        _tray.EditLayoutClicked += () => _overlay.SetEditMode(!_overlay.EditMode);
        _tray.ToggleOverlayClicked += () => _overlay.SetVisible(!_overlay.Visible);
        _tray.SettingsClicked += OpenSettings;
        _tray.ExitClicked += Shutdown;
        _overlay.StateChanged += () => _tray.UpdateTexts();
        _overlay.StatusChanged += status => _tray.SetStatus(status);
        Loc.Instance.LanguageChanged += () => _tray.UpdateTexts();

        _hotkey = new HotkeyService();
        _hotkey.Pressed += OnHotkey;
        RegisterHotkey();

        Log.Info($"Started {string.Join(' ', e.Args)}");

        // Sign in to Inna by itself in the background. Only if that is impossible on the very first
        // run does the sign-in window open (once); later failures show up as a link in the
        // overlay and a notification instead, so nothing pops up at startup.
        Dispatcher.BeginInvoke(async () =>
        {
            await Task.Delay(TimeSpan.FromSeconds(_settings.Current.InnaSetupDone ? 5 : 1));
            await _inna.CheckAsync();
            if (_inna.State == SessionState.NeverSignedIn && !_settings.Current.InnaPrompted)
            {
                _settings.Current.InnaPrompted = true;
                _settings.Save();
                OpenSignIn();
            }
        });
    }

    void Refresh()
    {
        _lastAttempt = Clock.Now;
        _overlay.Refresh();
        _ = _inna.CheckAsync();
    }

    // ---- Schedule ----------------------------------------------------------------

    readonly DispatcherTimer _schedule = new() { Interval = TimeSpan.FromMinutes(1) };
    DateTime? _lastAttempt;

    /// <summary>
    /// Once a minute: the daily refresh (default 16:00; also catches up after sleep or a late
    /// start, and retries every 15 minutes while offline) and the evening reminder.
    /// </summary>
    void OnScheduleTick()
    {
        var now = Clock.Now;
        var s = _settings.Current;

        if (TimeSpan.TryParse(s.RefreshTime, CultureInfo.InvariantCulture, out var at))
        {
            var due = now.Date + at;
            var missed = now >= due && (_overlay.LastFetch is not { } last || last < due);
            var retryOk = _lastAttempt is not { } tried || now - tried >= TimeSpan.FromMinutes(15);
            if (missed && retryOk && _inna.State != SessionState.Checking)
            {
                Log.Info("Scheduled refresh");
                Refresh();
            }
        }

        if (s.NotifyEveningReminder && TimeSpan.TryParse(s.ReminderTime, CultureInfo.InvariantCulture, out var remindAt)
            && now >= now.Date + remindAt && _overlay.DueTomorrow(now) is { Count: > 0 } due2 && _overlay.ClaimReminder(now))
        {
            var lines = due2.Select(a => $"{a.Course}: {a.Title} {Loc.Time(a.Due)}");
            _tray?.Notify(Loc.F("t.reminder.title", due2.Count), Trim(string.Join("\n", lines)), null);
        }
    }

    /// <summary>One notification for everything new in a fetch, so they don't replace each other.</summary>
    void OnNewItems(IReadOnlyList<Data.Grade> grades, IReadOnlyList<Data.Assignment> work)
    {
        var s = _settings.Current;
        var parts = new List<string>();
        if (s.NotifyNewGrades) parts.AddRange(grades.Select(g => Loc.F("n.grade", g.Course, g.Value)));
        if (s.NotifyNewAssignments) parts.AddRange(work.Select(a => Loc.F("n.newWork", $"{a.Title} ({a.Course})")));
        if (parts.Count == 0) return;
        Log.Info($"Notifying {parts.Count} new item(s)");
        if (parts.Count == 1) _tray?.Notify("Hermes", parts[0], null);
        else _tray?.Notify(Loc.F("t.news.title", parts.Count), Trim(string.Join("\n", parts)), null);
    }

    /// <summary>Windows caps notification text at about 255 characters.</summary>
    static string Trim(string text) => text.Length <= 250 ? text : text[..247] + "...";

    void OnPowerModeChanged(object? sender, Microsoft.Win32.PowerModeChangedEventArgs e)
    {
        if (e.Mode != Microsoft.Win32.PowerModes.Resume) return;
        // Give Wi-Fi a moment after waking, then catch up on a missed 16:00 refresh or reminder.
        Dispatcher.BeginInvoke(async () =>
        {
            await Task.Delay(TimeSpan.FromSeconds(20));
            _lastAttempt = null;
            OnScheduleTick();
        });
    }

    bool _expiryNotified;

    void OnInnaStateChanged(SessionState previous, SessionState current)
    {
        switch (current)
        {
            case SessionState.SignedIn:
                _expiryNotified = false;
                _overlay.SetSignInNotice(null, null);
                // Signed in from the window rather than a background check: fetch the data now.
                if (previous != SessionState.Checking) _ = _inna.CheckAsync();
                break;
            case SessionState.NeverSignedIn:
                _overlay.SetSignInNotice("n.signInFirst", OpenSignIn);
                break;
            case SessionState.Expired:
                _overlay.SetSignInNotice("n.signIn", OpenSignIn);
                if (!_expiryNotified)
                {
                    _expiryNotified = true;
                    _tray?.Notify(Loc.T("b.expired.title"), Loc.T("b.expired.text"), OpenSignIn);
                }
                break;
        }
        _tray?.UpdateTexts();
    }

    CaptureWindow? _captureWindow;

    void OpenCapture()
    {
        if (_captureWindow != null)
        {
            _captureWindow.Activate();
            return;
        }
        _captureWindow = new CaptureWindow();
        _captureWindow.Closed += (_, _) => _captureWindow = null;
        _captureWindow.Show();
        _captureWindow.Activate();
    }

    void OpenSignIn()
    {
        if (_signInWindow != null)
        {
            _signInWindow.Activate();
            return;
        }
        _signInWindow = new SignInWindow(_inna);
        _signInWindow.Closed += (_, _) => _signInWindow = null;
        _signInWindow.Show();
        _signInWindow.Activate();
    }

    void RegisterHotkey()
    {
        _hotkeyOk = _hotkey?.Register(_settings.Current.Hotkey) ?? true;
        if (!_hotkeyOk) Log.Info($"Hotkey {_settings.Current.Hotkey} could not be registered");
        _settingsWindow?.ShowHotkeyError(!_hotkeyOk);
    }

    void OnHotkey()
    {
        if (_settings.Current.HotkeyAction == HotkeyAction.ToggleOverlay) _overlay.SetVisible(!_overlay.Visible);
        else Refresh();
    }

    void OpenSettings()
    {
        if (_settingsWindow != null)
        {
            if (_settingsWindow.WindowState == WindowState.Minimized) _settingsWindow.WindowState = WindowState.Normal;
            _settingsWindow.Activate();
            return;
        }
        ShowSettingsWindow(null);
    }

    /// <summary>Opens Settings; with <paramref name="replacing"/>, takes over its place and scroll position.</summary>
    void ShowSettingsWindow(SettingsWindow? replacing)
    {
        var window = new SettingsWindow(_settings, _inna);
        window.SignInRequested += OpenSignIn;
        window.CaptureRequested += OpenCapture;
        window.DataSource = () => _overlay.Data;
        window.LastFetch = () => _overlay.LastFetch;
        window.SettingsApplied += () =>
        {
            _overlay.ApplySettings();
            RegisterHotkey();
        };
        window.EditLayoutRequested += () => _overlay.SetEditMode(true);
        window.RebuildRequested += () => ShowSettingsWindow(window);
        // While Settings records a new hotkey, release the old one so it can be pressed again.
        window.HotkeyRecording += recording =>
        {
            if (recording) _hotkey?.Unregister();
            else RegisterHotkey();
        };
        window.Closed += (_, _) =>
        {
            if (_settingsWindow == window) _settingsWindow = null;
        };
        window.ShowHotkeyError(!_hotkeyOk);

        if (replacing != null)
        {
            window.WindowStartupLocation = WindowStartupLocation.Manual;
            window.Left = replacing.Left;
            window.Top = replacing.Top;
            window.Width = replacing.Width;
            window.Height = replacing.Height;
            window.ScrollOffset = replacing.ScrollOffset;
        }

        _settingsWindow = window;
        window.Show();
        window.Activate();
        replacing?.Close();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _tray?.Dispose();
        _hotkey?.Dispose();
        _mutex?.Dispose();
        base.OnExit(e);
    }

    /// <summary>
    /// Command line: --startup (from the Run key), and for development
    /// --fake-now "2026-11-05 14:12" and --snapshot &lt;dir&gt;.
    /// </summary>
    sealed class Args
    {
        public DateTime? FakeNow { get; }
        public string? SnapshotDir { get; }
        public string? DiagnoseFile { get; }
        public bool CaptureTest { get; }
        public string? IcsFile { get; }

        public Args(string[] args)
        {
            for (var i = 0; i < args.Length; i++)
            {
                if (args[i] == "--fake-now" && i + 1 < args.Length &&
                    DateTime.TryParse(args[++i], CultureInfo.InvariantCulture, DateTimeStyles.None, out var t))
                    FakeNow = t;
                else if (args[i] == "--snapshot" && i + 1 < args.Length)
                    SnapshotDir = args[++i];
                else if (args[i] == "--inna-diagnose" && i + 1 < args.Length)
                    DiagnoseFile = args[++i];
                else if (args[i] == "--export-ics" && i + 1 < args.Length)
                    IcsFile = args[++i];
                else if (args[i] == "--capture-test" && i + 1 < args.Length)
                {
                    DiagnoseFile = args[++i];
                    CaptureTest = true;
                }
            }
        }
    }
}
