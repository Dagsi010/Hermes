using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Shapes;

namespace Hermes.Overlay;

/// <summary>Outline, label, Done button and corner grips shown in Edit layout mode.</summary>
public partial class EditChrome : UserControl
{
    enum DragMode { None, Move, TL, TR, BL, BR }

    DragMode _mode;
    Native.POINT _startCursor;
    Native.RECT _startRect;

    /// <summary>Raised when a drag or resize ends.</summary>
    public event Action? LayoutChanged;

    public event Action? DoneClicked;

    public string Title
    {
        get => TitleText.Text;
        set => TitleText.Text = value;
    }

    public string DoneText
    {
        set => DoneButton.Content = value;
    }

    public EditChrome()
    {
        InitializeComponent();
        foreach (var element in new FrameworkElement[] { Frame, GripTL, GripTR, GripBL, GripBR })
        {
            element.MouseLeftButtonDown += OnDown;
            element.MouseMove += OnMove;
            element.MouseLeftButtonUp += OnUp;
            element.LostMouseCapture += (_, _) => _mode = DragMode.None;
        }
    }

    OverlayWindow? Host => Window.GetWindow(this) as OverlayWindow;

    void OnDown(object sender, MouseButtonEventArgs e)
    {
        if (Host is not { } host) return;
        _mode = sender == Frame ? DragMode.Move : Enum.Parse<DragMode>((string)((Rectangle)sender).Tag);
        Native.GetCursorPos(out _startCursor);
        _startRect = host.ScreenRect;
        ((UIElement)sender).CaptureMouse();
        e.Handled = true;
    }

    void OnMove(object sender, MouseEventArgs e)
    {
        if (_mode == DragMode.None || Host is not { } host) return;
        Native.GetCursorPos(out var p);
        int dx = p.X - _startCursor.X, dy = p.Y - _startCursor.Y;
        var r = _startRect;
        int left = r.Left, top = r.Top, right = r.Right, bottom = r.Bottom;
        var scale = VisualTreeHelperDpi(host);
        int minW = (int)(140 * scale), minH = (int)(60 * scale);

        switch (_mode)
        {
            case DragMode.Move: left += dx; right += dx; top += dy; bottom += dy; break;
            case DragMode.TL: left = Math.Min(left + dx, right - minW); top = Math.Min(top + dy, bottom - minH); break;
            case DragMode.TR: right = Math.Max(right + dx, left + minW); top = Math.Min(top + dy, bottom - minH); break;
            case DragMode.BL: left = Math.Min(left + dx, right - minW); bottom = Math.Max(bottom + dy, top + minH); break;
            case DragMode.BR: right = Math.Max(right + dx, left + minW); bottom = Math.Max(bottom + dy, top + minH); break;
        }
        host.SetScreenRect(left, top, right - left, bottom - top);
    }

    void OnUp(object sender, MouseButtonEventArgs e)
    {
        if (_mode == DragMode.None) return;
        _mode = DragMode.None;
        ((UIElement)sender).ReleaseMouseCapture();
        LayoutChanged?.Invoke();
    }

    void DoneButton_Click(object sender, RoutedEventArgs e) => DoneClicked?.Invoke();

    static double VisualTreeHelperDpi(System.Windows.Media.Visual v) => System.Windows.Media.VisualTreeHelper.GetDpi(v).DpiScaleX;
}
