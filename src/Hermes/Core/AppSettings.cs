using System.Text.Json;
using System.Text.Json.Serialization;

namespace Hermes.Core;

public enum TextColorMode { Light, Dark, Auto }

public enum HotkeyAction { Refresh, ToggleOverlay }

/// <summary>Position of one overlay part as fractions of its monitor's bounds.</summary>
public sealed class RelRect
{
    public double X { get; set; }
    public double Y { get; set; }
    public double W { get; set; }
    public double H { get; set; }

    public RelRect() { }
    public RelRect(double x, double y, double w, double h) { X = x; Y = y; W = w; H = h; }
}

public sealed class OverlayLayout
{
    public RelRect Schedule { get; set; } = new(0.07, 0.22, 0.24, 0.56);
    public RelRect Work { get; set; } = new(0.66, 0.22, 0.27, 0.56);
    public RelRect Progress { get; set; } = new(0.25, 0.82, 0.50, 0.10);
}

public sealed class AppSettings
{
    public bool StartWithWindows { get; set; } = true;
    public string RefreshTime { get; set; } = "16:00";
    public TextColorMode TextColor { get; set; } = TextColorMode.Light;
    /// <summary>Colour theme id (see <see cref="Themes"/>).</summary>
    public string Theme { get; set; } = "navy";
    public double TextScale { get; set; } = 1.0;
    /// <summary>Device name of the monitor (e.g. \\.\DISPLAY1); null means the primary monitor.</summary>
    public string? Monitor { get; set; }
    public string Language { get; set; } = "is";

    public bool NotifyEveningReminder { get; set; } = true;
    public string ReminderTime { get; set; } = "19:00";
    public bool NotifyNewAssignments { get; set; } = true;
    public bool NotifyNewGrades { get; set; } = true;
    public int AttendanceThreshold { get; set; } = 90;

    public string Hotkey { get; set; } = "Ctrl+Alt+H";
    public HotkeyAction HotkeyAction { get; set; } = HotkeyAction.Refresh;

    /// <summary>The user has completed (or dismissed) the first-run Inna sign-in.</summary>
    public bool InnaSetupDone { get; set; }
    /// <summary>How the user signs in to Inna: "google", "island", "password". Not a credential.</summary>
    public string? InnaLoginMethod { get; set; }
    /// <summary>Google once blocked sign-in, so the explicit Edge user agent is used.</summary>
    public bool InnaEdgeUserAgent { get; set; }
    /// <summary>The first-run sign-in window has been shown once.</summary>
    public bool InnaPrompted { get; set; }

    /// <summary>Rewrite the exported .ics file after every fetch.</summary>
    public bool CalendarSync { get; set; }
    /// <summary>Where the .ics file was last exported to.</summary>
    public string? IcsPath { get; set; }
    public bool DebugCapture { get; set; }

    public bool OverlayVisible { get; set; } = true;
    public bool ShowWeek { get; set; }

    /// <summary>Overlay positions keyed by monitor (device name and resolution).</summary>
    public Dictionary<string, OverlayLayout> Layouts { get; set; } = new();
}

public sealed class SettingsStore
{
    static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public AppSettings Current { get; private set; } = new();

    public event Action? Changed;

    public static SettingsStore Load()
    {
        var store = new SettingsStore();
        try
        {
            if (File.Exists(Paths.SettingsFile))
                store.Current = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(Paths.SettingsFile), Json) ?? new();
        }
        catch (Exception ex)
        {
            Log.Error("Could not read settings, using defaults", ex);
        }
        return store;
    }

    public void Save()
    {
        try
        {
            var tmp = Paths.SettingsFile + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(Current, Json));
            File.Move(tmp, Paths.SettingsFile, overwrite: true);
        }
        catch (Exception ex)
        {
            Log.Error("Could not save settings", ex);
        }
        Changed?.Invoke();
    }

    public static JsonSerializerOptions JsonOptions => Json;
}
