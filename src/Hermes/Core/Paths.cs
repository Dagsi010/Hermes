namespace Hermes.Core;

public static class Paths
{
    /// <summary>%APPDATA%\Hermes: settings.</summary>
    public static string RoamingDir => Ensure(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Hermes"));

    /// <summary>%LOCALAPPDATA%\Hermes: cache, local state, WebView2 profile, logs.</summary>
    public static string LocalDir => Ensure(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Hermes"));

    public static string SettingsFile => Path.Combine(RoamingDir, "settings.json");
    public static string LocalStateFile => Path.Combine(LocalDir, "local-state.json");
    public static string CacheFile => Path.Combine(LocalDir, "cache.json");
    public static string WebViewDataDir => Path.Combine(LocalDir, "WebView2");
    public static string LogFile => Path.Combine(LocalDir, "hermes.log");

    static string Ensure(string dir)
    {
        Directory.CreateDirectory(dir);
        return dir;
    }
}
