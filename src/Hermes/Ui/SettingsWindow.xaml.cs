using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Hermes.Core;
using Hermes.Inna;
using Screen = System.Windows.Forms.Screen;

namespace Hermes.Ui;

/// <summary>Settings apply and save immediately, Windows 11 style; there is no Save button.</summary>
public partial class SettingsWindow : Window
{
    readonly SettingsStore _store;
    bool _loading = true;
    int _versionClicks;

    /// <summary>Raised after any setting that affects the overlay or hotkey changed.</summary>
    public event Action? SettingsApplied;
    public event Action? EditLayoutRequested;

    AppSettings S => _store.Current;

    readonly InnaSession _inna;

    public event Action? SignInRequested;

    public SettingsWindow(SettingsStore store, InnaSession inna)
    {
        _store = store;
        _inna = inna;
        InitializeComponent();
        _inna.StateChanged += OnInnaChanged;
        _inna.DataFetched += OnDataFetched;
        Closed += (_, _) =>
        {
            _inna.StateChanged -= OnInnaChanged;
            _inna.DataFetched -= OnDataFetched;
        };
        Loaded += (_, _) => UpdateInna();
        Load();
        _loading = false;
        RefreshTimePicker.TimeChanged += t => { S.RefreshTime = t; Commit(); };
        ReminderTimePicker.TimeChanged += t => { S.ReminderTime = t; Commit(); };

        Themes.ApplyFluentAccent(Resources, Themes.Current);
    }

    /// <summary>
    /// The Fluent controls bake some accent colours in when they are built, so a theme
    /// change asks for a fresh window; this carries the scroll position over.
    /// </summary>
    public event Action? RebuildRequested;

    public double ScrollOffset
    {
        get => Scroller.VerticalOffset;
        set => Loaded += (_, _) => Scroller.ScrollToVerticalOffset(value);
    }

    void BuildThemeList() =>
        ThemeList.ItemsSource = Themes.All.Select(t => new ThemeSwatch(t, t.Id == Themes.Current.Id)).ToList();

    void Theme_Click(object sender, MouseButtonEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not ThemeSwatch swatch || swatch.Theme == Themes.Current) return;
        S.Theme = swatch.Theme.Id;
        Themes.Apply(swatch.Theme);
        Commit();
        RebuildRequested?.Invoke();
    }

    void Load()
    {
        StartWithWindowsBox.IsChecked = S.StartWithWindows;
        Select(LanguageBox, S.Language);
        BuildThemeList();
        Select(TextColorBox, S.TextColor.ToString());
        TextSizeSlider.Value = S.TextScale;

        MonitorBox.Items.Clear();
        var screens = Screen.AllScreens;
        for (var i = 0; i < screens.Length; i++)
        {
            var s = screens[i];
            var primary = s.Primary ? $" ({Loc.T("s.monitor.primary")})" : "";
            MonitorBox.Items.Add(new ComboBoxItem
            {
                Tag = s.DeviceName,
                Content = $"{i + 1}{primary} · {s.Bounds.Width}×{s.Bounds.Height}",
            });
        }
        var current = screens.FirstOrDefault(s => s.DeviceName == S.Monitor) ?? Screen.PrimaryScreen;
        Select(MonitorBox, current?.DeviceName ?? "");

        RefreshTimePicker.Time = S.RefreshTime;
        if (!_recording) HotkeyButton.Content = S.Hotkey;
        Select(HotkeyActionBox, S.HotkeyAction.ToString());

        ReminderBox.IsChecked = S.NotifyEveningReminder;
        ReminderTimePicker.Time = S.ReminderTime;
        NewWorkBox.IsChecked = S.NotifyNewAssignments;
        NewGradesBox.IsChecked = S.NotifyNewGrades;
        AttendanceSlider.Value = S.AttendanceThreshold;
        AttendanceLabel.Text = $"{S.AttendanceThreshold}%";

        CalendarSyncBox.IsChecked = S.CalendarSync;
        IcsPathText.Text = S.IcsPath is { } ics ? Loc.F("s.icsSaved", ics) : "";
        IcsPathText.Visibility = S.IcsPath != null ? Visibility.Visible : Visibility.Collapsed;
        DebugCaptureBox.IsChecked = S.DebugCapture;
        CaptureButton.IsEnabled = S.DebugCapture;
        DebugCard.Visibility = S.DebugCapture ? Visibility.Visible : Visibility.Collapsed;

        UpdateInna();

        var version = typeof(App).Assembly.GetName().Version;
        VersionText.Text = Loc.F("s.version", $"{version?.Major}.{version?.Minor}.{version?.Build}");
    }

    void OnInnaChanged(SessionState previous, SessionState current) => Dispatcher.BeginInvoke(UpdateInna);

    // Runs after the overlay took the data (subscribed earlier), so LastFetch is already new.
    void OnDataFetched(Data.SchoolData data) => Dispatcher.BeginInvoke(UpdateInna);

    void UpdateInna()
    {
        var state = _inna.State;
        var t = Themes.Current;
        var (key, color) = state switch
        {
            SessionState.SignedIn => ("s.inna.signedIn", t.Accent),
            SessionState.Checking => ("s.inna.checking", t.DueLater),
            SessionState.Expired => ("s.inna.expired", t.DueToday),
            SessionState.Offline => ("s.inna.offline", t.DueTomorrow),
            SessionState.NeverSignedIn => ("s.inna.notSignedIn", t.DueLater),
            _ => ("s.inna.unknown", t.DueLater),
        };
        InnaStatus.Text = Loc.T(key);
        InnaDot.Fill = Themes.Brush(color);

        var method = S.InnaLoginMethod switch
        {
            "google" => "Google",
            "island" => "Ísland.is",
            "microsoft" => "Microsoft",
            "password" => Loc.Instance.IsEnglish ? "Inna password" : "Lykilorð Innu",
            _ => null,
        };
        var parts = new List<string>();
        if (method != null && S.InnaSetupDone) parts.Add(Loc.F("s.inna.method", method));
        if (LastFetch?.Invoke() is { } fetched)
            parts.Add(Loc.F("s.inna.lastFetch", fetched.Date == DateTime.Today ? Loc.Time(fetched) : $"{Loc.DayShort(fetched)} {Loc.Time(fetched)}"));
        InnaMethod.Text = string.Join(" · ", parts);
        InnaMethod.Visibility = InnaMethod.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;

        SignInButton.IsEnabled = state is not (SessionState.SignedIn or SessionState.Checking);
        SignOutButton.IsEnabled = S.InnaSetupDone && state != SessionState.Checking;
        CheckButton.IsEnabled = S.InnaSetupDone && state != SessionState.Checking;
    }

    void SignIn_Click(object sender, RoutedEventArgs e) => SignInRequested?.Invoke();

    async void SignOut_Click(object sender, RoutedEventArgs e)
    {
        SignOutButton.IsEnabled = false;
        await _inna.SignOutAsync();
    }

    async void Check_Click(object sender, RoutedEventArgs e) => await _inna.CheckAsync();

    public void ShowHotkeyError(bool show) => HotkeyStatus.Visibility = show ? Visibility.Visible : Visibility.Collapsed;

    static void Select(ComboBox box, string tag) =>
        box.SelectedItem = box.Items.OfType<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == tag);

    static string? TagOf(ComboBox box) => (box.SelectedItem as ComboBoxItem)?.Tag as string;

    void Commit()
    {
        if (_loading) return;
        _store.Save();
        SettingsApplied?.Invoke();
    }

    void StartWithWindows_Click(object sender, RoutedEventArgs e)
    {
        S.StartWithWindows = StartWithWindowsBox.IsChecked == true;
        StartupRegistration.Sync(S.StartWithWindows);
        Commit();
    }

    void Language_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || TagOf(LanguageBox) is not { } lang) return;
        S.Language = lang;
        Loc.Instance.Language = lang;
        Commit();
        // Rebuild the texts that are composed in code (monitor list, version line).
        _loading = true;
        Load();
        _loading = false;
    }

    void TextColor_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || !Enum.TryParse<TextColorMode>(TagOf(TextColorBox), out var mode)) return;
        S.TextColor = mode;
        Commit();
    }

    void TextSize_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_loading) return;
        S.TextScale = Math.Round(TextSizeSlider.Value, 2);
        Commit();
    }

    void Monitor_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || TagOf(MonitorBox) is not { } device) return;
        S.Monitor = device;
        Commit();
    }

    void EditLayout_Click(object sender, RoutedEventArgs e) => EditLayoutRequested?.Invoke();

    // ---- Hotkey recording ------------------------------------------------------------
    // The old read-only text box gave no sign that it was waiting for keys, so it looked like
    // the hotkey couldn't be changed. Now: click the button, it says so, press the combination.

    bool _recording;

    /// <summary>True while waiting for a new combination: the old hotkey is released meanwhile so it can be pressed again.</summary>
    public event Action<bool>? HotkeyRecording;

    void Hotkey_Click(object sender, RoutedEventArgs e)
    {
        if (_recording) StopRecording();
        else StartRecording();
    }

    void StartRecording()
    {
        _recording = true;
        HotkeyRecording?.Invoke(true);
        HotkeyButton.Content = Loc.T("s.hotkey.press");
        if (TryFindResource("AccentButtonStyle") is Style accent) HotkeyButton.Style = accent;
        PreviewKeyDown += OnRecordKey;
        Deactivated += OnRecordDeactivated;
    }

    void StopRecording()
    {
        if (!_recording) return;
        _recording = false;
        PreviewKeyDown -= OnRecordKey;
        Deactivated -= OnRecordDeactivated;
        HotkeyButton.ClearValue(StyleProperty);
        HotkeyButton.Content = S.Hotkey;
        HotkeyRecording?.Invoke(false);
    }

    void OnRecordDeactivated(object? sender, EventArgs e) => StopRecording();

    void OnRecordKey(object sender, KeyEventArgs e)
    {
        e.Handled = true;
        var key = e.Key switch
        {
            Key.System => e.SystemKey,          // anything with Alt
            Key.ImeProcessed => e.ImeProcessedKey,
            Key.DeadCharProcessed => e.DeadCharProcessedKey, // AltGr combinations on the Icelandic layout
            _ => e.Key,
        };
        if (key == Key.Escape)
        {
            StopRecording();
            return;
        }
        var modifiers = Keyboard.Modifiers;
        if (HotkeyService.Format(modifiers, key) is not { } combo)
        {
            // Only modifiers so far: show what is held, e.g. "Ctrl+Alt+...".
            var held = HotkeyService.Format(modifiers, Key.None, partial: true);
            HotkeyButton.Content = string.IsNullOrEmpty(held) ? Loc.T("s.hotkey.press") : held + "+...";
            return;
        }
        S.Hotkey = combo;
        StopRecording(); // re-registers through HotkeyRecording(false)
        Commit();
    }

    void HotkeyAction_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || !Enum.TryParse<HotkeyAction>(TagOf(HotkeyActionBox), out var action)) return;
        S.HotkeyAction = action;
        Commit();
    }

    void Notify_Click(object sender, RoutedEventArgs e)
    {
        S.NotifyEveningReminder = ReminderBox.IsChecked == true;
        S.NotifyNewAssignments = NewWorkBox.IsChecked == true;
        S.NotifyNewGrades = NewGradesBox.IsChecked == true;
        Commit();
    }

    void Attendance_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_loading) return;
        S.AttendanceThreshold = (int)AttendanceSlider.Value;
        AttendanceLabel.Text = $"{S.AttendanceThreshold}%";
        Commit();
    }

    /// <summary>The current data, for the .ics export.</summary>
    public Func<Data.SchoolData>? DataSource { get; set; }

    /// <summary>Time of the last successful fetch, shown on the Inna card.</summary>
    public Func<DateTime?>? LastFetch { get; set; }

    void ExportIcs_Click(object sender, RoutedEventArgs e)
    {
        if (DataSource?.Invoke() is not { } data) return;
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            FileName = "Hermes.ics",
            DefaultExt = ".ics",
            Filter = "iCalendar (*.ics)|*.ics",
            InitialDirectory = S.IcsPath is { } p ? Path.GetDirectoryName(p) : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            IcsExport.Export(dialog.FileName, data);
            S.IcsPath = dialog.FileName;
            Commit();
            IcsPathText.Text = Loc.F("s.icsSaved", dialog.FileName);
            IcsPathText.Visibility = Visibility.Visible;
        }
        catch (Exception ex)
        {
            Log.Error("ics export failed", ex);
            MessageBox.Show(this, ex.Message, "Hermes", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    void CalendarSync_Click(object sender, RoutedEventArgs e)
    {
        S.CalendarSync = CalendarSyncBox.IsChecked == true;
        Commit();
        // Turning it on before any export: ask where the file should live.
        if (S.CalendarSync && S.IcsPath == null) ExportIcs_Click(sender, e);
    }

    public event Action? CaptureRequested;

    void DebugCapture_Click(object sender, RoutedEventArgs e)
    {
        S.DebugCapture = DebugCaptureBox.IsChecked == true;
        CaptureButton.IsEnabled = S.DebugCapture;
        Commit();
    }

    void Capture_Click(object sender, RoutedEventArgs e) => CaptureRequested?.Invoke();

    void OpenDataFolder_Click(object sender, RoutedEventArgs e) =>
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{Paths.LocalDir}\"") { UseShellExecute = true });

    void Version_Click(object sender, MouseButtonEventArgs e)
    {
        if (++_versionClicks >= 5) DebugCard.Visibility = Visibility.Visible;
    }
}
