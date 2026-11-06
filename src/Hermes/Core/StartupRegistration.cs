using Microsoft.Win32;

namespace Hermes.Core;

/// <summary>Start with Windows through the HKCU Run key.</summary>
public static class StartupRegistration
{
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string ValueName = "Hermes";

    public static void Sync(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey, writable: true);
            if (enabled)
            {
                var command = $"\"{Environment.ProcessPath}\" --startup";
                if (key.GetValue(ValueName) as string != command) key.SetValue(ValueName, command);
            }
            else
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
            }
        }
        catch (Exception ex)
        {
            Log.Error("Could not update the startup entry", ex);
        }
    }
}
