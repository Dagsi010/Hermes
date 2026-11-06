using System.Text.Json;
using Hermes.Core;

namespace Hermes.Data;

/// <summary>State that only lives on this PC, such as assignments ticked off by hand.</summary>
public sealed class LocalState
{
    /// <summary>How long a ticked-off assignment stays visible (struck through) before it hides.</summary>
    public static readonly TimeSpan DoneVisibleFor = TimeSpan.FromDays(1);

    /// <summary>Assignment id to the time it was ticked off.</summary>
    public Dictionary<string, DateTime> Done { get; set; } = new();

    /// <summary>The day the evening reminder last went out.</summary>
    public DateTime? LastReminder { get; set; }

    public static LocalState Load()
    {
        try
        {
            if (File.Exists(Paths.LocalStateFile))
                return JsonSerializer.Deserialize<LocalState>(File.ReadAllText(Paths.LocalStateFile), SettingsStore.JsonOptions) ?? new();
        }
        catch (Exception ex)
        {
            Log.Error("Could not read local state", ex);
        }
        return new();
    }

    public void Save()
    {
        try
        {
            File.WriteAllText(Paths.LocalStateFile, JsonSerializer.Serialize(this, SettingsStore.JsonOptions));
        }
        catch (Exception ex)
        {
            Log.Error("Could not save local state", ex);
        }
    }

    public bool IsHidden(string id, DateTime now) =>
        Done.TryGetValue(id, out var at) && now - at > DoneVisibleFor;
}
