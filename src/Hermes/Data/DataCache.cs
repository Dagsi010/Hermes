using System.Text.Json;
using Hermes.Core;

namespace Hermes.Data;

/// <summary>
/// The last successful fetch, kept in %LOCALAPPDATA%\Hermes\cache.json so the overlay has
/// something to show offline. Also remembers when each grade and assignment first appeared,
/// which is what makes "new grade" and "new assignment" notices possible.
/// </summary>
public static class DataCache
{
    public static SchoolData? Load()
    {
        try
        {
            if (File.Exists(Paths.CacheFile))
                return JsonSerializer.Deserialize<SchoolData>(File.ReadAllText(Paths.CacheFile), SettingsStore.JsonOptions);
        }
        catch (Exception ex)
        {
            Log.Error("Could not read the data cache", ex);
        }
        return null;
    }

    public static void Save(SchoolData data)
    {
        try
        {
            var tmp = Paths.CacheFile + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(data, SettingsStore.JsonOptions));
            File.Move(tmp, Paths.CacheFile, overwrite: true);
        }
        catch (Exception ex)
        {
            Log.Error("Could not save the data cache", ex);
        }
    }

    /// <summary>
    /// Carries first-seen times over from the previous fetch. On the very first real fetch
    /// everything counts as old, so the overlay isn't flooded with "new" notices.
    /// </summary>
    public static SchoolData Merge(SchoolData? previous, SchoolData fresh)
    {
        var firstFetch = previous == null || previous.IsSample;
        var old = firstFetch ? DateTime.MinValue : fresh.FetchedAt;

        var known = previous?.IsSample == false ? previous.Grades.ToDictionary(g => g.Id, g => g.Posted) : new();
        fresh.Grades = fresh.Grades
            .Select(g => g with { Posted = known.TryGetValue(g.Id, out var seen) ? seen : old })
            .ToList();

        var seenWork = previous?.IsSample == false ? previous.AssignmentFirstSeen : new();
        fresh.AssignmentFirstSeen = fresh.Assignments.ToDictionary(
            a => a.Id,
            a => seenWork.TryGetValue(a.Id, out var seen) ? seen : old);
        return fresh;
    }
}
