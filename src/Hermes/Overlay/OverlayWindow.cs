using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace Hermes.Overlay;

/// <summary>Attached flag for elements that should catch clicks; everything else is click-through.</summary>
public static class Hit
{
    public static readonly DependencyProperty InteractiveProperty = DependencyProperty.RegisterAttached(
        "Interactive", typeof(bool), typeof(Hit), new PropertyMetadata(false));

    public static bool GetInteractive(DependencyObject d) => (bool)d.GetValue(InteractiveProperty);
    public static void SetInteractive(DependencyObject d, bool value) => d.SetValue(InteractiveProperty, value);
}

/// <summary>
/// A transparent, borderless overlay part that lives on the desktop: no taskbar
/// button, not in Alt+Tab, never activated, never raised above apps.
/// </summary>
public class OverlayWindow : Window
{
    bool _clickThrough = true;

    public IntPtr Handle { get; private set; }

    /// <summary>Which part this is ("schedule", "work", "progress"), for layout storage.</summary>
    public string Part { get; protected set; } = "";

    public OverlayWindow()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        ShowActivated = false;
        Topmost = false;
        Focusable = false;
        // Kept true so WPF does not give the window a hidden owner (which would tie its z-order to that owner);
        // WS_EX_TOOLWINDOW below keeps it off the taskbar and out of Alt+Tab.
        ShowInTaskbar = true;
        SourceInitialized += OnSourceInitialized;
    }

    void OnSourceInitialized(object? sender, EventArgs e)
    {
        Handle = new WindowInteropHelper(this).Handle;
        var ex = Native.GetExStyle(Handle);
        ex |= Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE | Native.WS_EX_TRANSPARENT;
        ex &= ~Native.WS_EX_APPWINDOW;
        Native.SetExStyle(Handle, ex);
        HwndSource.FromHwnd(Handle)!.AddHook(WndProc);
    }

    IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        switch (msg)
        {
            case Native.WM_MOUSEACTIVATE:
                handled = true;
                return new IntPtr(Native.MA_NOACTIVATE);

            case Native.WM_WINDOWPOSCHANGING when !DesktopLayer.Moving:
                // Only DesktopLayer decides where we sit in the z-order.
                var pos = Marshal.PtrToStructure<Native.WINDOWPOS>(lParam);
                if ((pos.flags & Native.SWP_NOZORDER) == 0)
                {
                    pos.flags |= Native.SWP_NOZORDER;
                    Marshal.StructureToPtr(pos, lParam, false);
                }
                break;
        }
        return IntPtr.Zero;
    }

    /// <summary>Lets clicks fall through to the desktop (WS_EX_TRANSPARENT) or catches them.</summary>
    public void SetClickThrough(bool on)
    {
        if (Handle == IntPtr.Zero || on == _clickThrough) return;
        _clickThrough = on;
        var ex = Native.GetExStyle(Handle);
        Native.SetExStyle(Handle, on ? ex | Native.WS_EX_TRANSPARENT : ex & ~Native.WS_EX_TRANSPARENT);
    }

    public bool IsClickThrough => _clickThrough;

    internal bool ContainsScreenPoint(Native.POINT p) =>
        Handle != IntPtr.Zero && Native.GetWindowRect(Handle, out var r) &&
        p.X >= r.Left && p.X < r.Right && p.Y >= r.Top && p.Y < r.Bottom;

    /// <summary>True when the element under the screen point (device pixels) is marked <see cref="Hit.Interactive"/>.</summary>
    internal bool IsInteractiveAt(Native.POINT p)
    {
        if (!IsVisible) return false;
        Point local;
        try { local = PointFromScreen(new Point(p.X, p.Y)); }
        catch (InvalidOperationException) { return false; }

        DependencyObject? d = VisualTreeHelper.HitTest(this, local)?.VisualHit;
        while (d != null)
        {
            if (Hit.GetInteractive(d)) return true;
            d = d is Visual or Visual3D ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d);
        }
        return false;
    }

    internal Native.RECT ScreenRect
    {
        get
        {
            Native.GetWindowRect(Handle, out var r);
            return r;
        }
    }

    /// <summary>Moves/resizes in device pixels without touching the z-order.</summary>
    public void SetScreenRect(int x, int y, int width, int height) =>
        Native.SetWindowPos(Handle, IntPtr.Zero, x, y, width, height, Native.SWP_NOZORDER | Native.SWP_NOACTIVATE);

    FrameworkElement? _body;
    EditChrome? _chrome;

    /// <summary>A drag or resize finished in Edit layout mode.</summary>
    public event Action? LayoutEdited;

    /// <summary>The Done button was pressed in Edit layout mode.</summary>
    public event Action? EditDone;

    /// <summary>Wires up the shadowed content and the edit outline; call from the subclass constructor.</summary>
    protected void InitParts(FrameworkElement body, EditChrome chrome)
    {
        _body = body;
        _chrome = chrome;
        chrome.LayoutChanged += () => LayoutEdited?.Invoke();
        chrome.DoneClicked += () => EditDone?.Invoke();
    }

    public void SetEditMode(bool on, string title, string doneText)
    {
        if (_chrome == null) return;
        _chrome.Title = title;
        _chrome.DoneText = doneText;
        _chrome.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>Whether this part currently uses dark text (Auto mode decides per part).</summary>
    public bool DarkText { get; private set; }

    public void ApplyTheme(bool darkText, Core.Theme theme)
    {
        DarkText = darkText;
        OverlayTheme.ApplyColors(Resources, darkText, theme);
        if (_body != null) _body.Effect = OverlayTheme.CreateShadow(darkText);
    }

    /// <summary>Where the visible content sits on screen, in device pixels (what Auto mode samples).</summary>
    public Rect ContentScreenRect
    {
        get
        {
            if (_body is not { ActualWidth: > 0 } body || PresentationSource.FromVisual(body) == null) return Rect.Empty;
            var topLeft = body.PointToScreen(new Point(0, 0));
            var bottomRight = body.PointToScreen(new Point(body.ActualWidth, body.ActualHeight));
            return new Rect(topLeft, bottomRight);
        }
    }

    /// <summary>Soft fade used when new data arrives.</summary>
    public void FadeIn()
    {
        _body?.BeginAnimation(OpacityProperty, new System.Windows.Media.Animation.DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(450)));
    }
}
