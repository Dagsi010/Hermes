using System.Windows.Input;
using System.Windows.Interop;
using Hermes.Overlay;

namespace Hermes.Core;

/// <summary>One global hotkey (e.g. "Ctrl+Alt+H") registered on a message-only window.</summary>
public sealed class HotkeyService : IDisposable
{
    const int Id = 0x4845; // "HE"
    const uint ModAlt = 0x1, ModControl = 0x2, ModShift = 0x4, ModWin = 0x8, ModNoRepeat = 0x4000;

    readonly HwndSource _source;
    bool _registered;

    public event Action? Pressed;

    public HotkeyService()
    {
        // HWND_MESSAGE parent: a message-only window that never shows up anywhere.
        _source = new HwndSource(new HwndSourceParameters("HermesHotkey") { ParentWindow = new IntPtr(-3), WindowStyle = 0 });
        _source.AddHook(WndProc);
    }

    /// <summary>Registers the combination; returns false if it is invalid or taken by another app.</summary>
    public bool Register(string combo)
    {
        Unregister();
        if (!TryParse(combo, out var mods, out var key)) return false;
        _registered = Native.RegisterHotKey(_source.Handle, Id, mods | ModNoRepeat, (uint)KeyInterop.VirtualKeyFromKey(key));
        return _registered;
    }

    public static bool TryParse(string combo, out uint modifiers, out Key key)
    {
        modifiers = 0;
        key = Key.None;
        foreach (var part in combo.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            switch (part.ToLowerInvariant())
            {
                case "ctrl": case "control": modifiers |= ModControl; break;
                case "alt": modifiers |= ModAlt; break;
                case "shift": modifiers |= ModShift; break;
                case "win": modifiers |= ModWin; break;
                default:
                    if (!Enum.TryParse(part, ignoreCase: true, out key)) return false;
                    break;
            }
        }
        return modifiers != 0 && key != Key.None;
    }

    /// <summary>Formats a key press from the settings box as "Ctrl+Alt+H".</summary>
    public static string? Format(ModifierKeys modifiers, Key key)
    {
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift
            or Key.LWin or Key.RWin or Key.System or Key.None) return null;
        var parts = new List<string>();
        if (modifiers.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
        if (modifiers.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
        if (modifiers.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
        if (modifiers.HasFlag(ModifierKeys.Windows)) parts.Add("Win");
        if (parts.Count == 0) return null;
        parts.Add(key.ToString());
        return string.Join("+", parts);
    }

    void Unregister()
    {
        if (_registered) Native.UnregisterHotKey(_source.Handle, Id);
        _registered = false;
    }

    IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == Native.WM_HOTKEY && wParam.ToInt32() == Id)
        {
            handled = true;
            Pressed?.Invoke();
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        Unregister();
        _source.Dispose();
    }
}
