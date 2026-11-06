using System.Windows.Threading;

namespace Hermes.Overlay;

/// <summary>
/// Keeps the overlay windows glued to the desktop, Rainmeter style: they sit
/// directly above the window that hosts the desktop icons (SHELLDLL_DefView,
/// inside Progman or a WorkerW) and below every app window.
///
/// Normally that host is at the bottom of the z-order, so the overlay sits
/// under all apps. When Win+D / Show desktop raises the host above the apps,
/// the overlay follows it up, and drops back down when the apps return.
/// The windows stay top-level (not reparented), so they keep receiving clicks
/// and survive an Explorer restart.
/// </summary>
internal static class DesktopLayer
{
    static readonly List<IntPtr> Windows = new();
    static readonly DispatcherTimer Timer = new(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(250) };
    static IntPtr _host;

    /// <summary>True while we are re-stacking; overlay windows refuse z-order changes otherwise.</summary>
    public static bool Moving { get; private set; }

    static DesktopLayer() => Timer.Tick += (_, _) => Enforce();

    public static void Register(IntPtr hwnd)
    {
        if (!Windows.Contains(hwnd)) Windows.Add(hwnd);
        Timer.Start();
        Enforce();
    }

    public static void Unregister(IntPtr hwnd)
    {
        Windows.Remove(hwnd);
        if (Windows.Count == 0) Timer.Stop();
    }

    public static void Enforce()
    {
        if (Windows.Count == 0) return;
        var host = FindHost();
        if (host == IntPtr.Zero)
        {
            // No icon host found (unusual shell): plain bottom of the z-order.
            Place(Native.HWND_BOTTOM);
            return;
        }
        if (Windows.All(w => SitsDirectlyAbove(w, host))) return;

        var above = Native.GetWindow(host, Native.GW_HWNDPREV);
        while (above != IntPtr.Zero && Windows.Contains(above)) above = Native.GetWindow(above, Native.GW_HWNDPREV);
        // Inserting after a topmost window could make ours topmost; take the top of the normal band instead.
        if (above == IntPtr.Zero || (Native.GetExStyle(above) & Native.WS_EX_TOPMOST) != 0) above = Native.HWND_TOP;
        Place(above);
    }

    /// <summary>True when only invisible windows (or our own) lie between <paramref name="hwnd"/> and the host below it.</summary>
    static bool SitsDirectlyAbove(IntPtr hwnd, IntPtr host)
    {
        for (var w = Native.GetWindow(hwnd, Native.GW_HWNDNEXT); w != IntPtr.Zero; w = Native.GetWindow(w, Native.GW_HWNDNEXT))
        {
            if (w == host) return true;
            if (Windows.Contains(w)) continue;
            if (Native.IsWindowVisible(w) && !Native.IsCloaked(w)) return false;
        }
        return false; // the host is above us (Show desktop) or gone
    }

    static void Place(IntPtr insertAfter)
    {
        Moving = true;
        try
        {
            foreach (var w in Windows)
                Native.SetWindowPos(w, insertAfter, 0, 0, 0, 0,
                    Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE | Native.SWP_NOOWNERZORDER);
        }
        finally
        {
            Moving = false;
        }
    }

    static IntPtr FindHost()
    {
        if (_host != IntPtr.Zero && Native.IsWindow(_host) &&
            Native.FindWindowEx(_host, IntPtr.Zero, "SHELLDLL_DefView", null) != IntPtr.Zero)
            return _host;

        _host = IntPtr.Zero;
        Native.EnumWindows((hwnd, _) =>
        {
            if (Native.FindWindowEx(hwnd, IntPtr.Zero, "SHELLDLL_DefView", null) == IntPtr.Zero) return true;
            _host = hwnd;
            return false;
        }, IntPtr.Zero);
        return _host;
    }
}
