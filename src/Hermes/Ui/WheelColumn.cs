using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Hermes.Ui;

/// <summary>
/// One looping drum of numbers (00-23 or 00-59), like the iPhone clock wheels.
/// Scroll with the mouse wheel, drag or flick it, click a number, or use the arrow keys.
/// </summary>
public sealed class WheelColumn : FrameworkElement
{
    const double ItemHeight = 34;
    const int VisibleRows = 5;
    /// <summary>Angle between rows on the drum, in radians.</summary>
    const double RowAngle = 0.36;
    const double Radius = ItemHeight / RowAngle;

    public static readonly DependencyProperty PositionProperty = DependencyProperty.Register(
        nameof(Position), typeof(double), typeof(WheelColumn),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    static readonly Typeface Face = new(new FontFamily("Segoe UI Variable Display, Segoe UI"),
        FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);

    double _target;
    bool _dragging;
    double _dragStartY, _dragStartPos, _dragDistance;
    readonly List<(DateTime Time, double Pos)> _samples = new();

    /// <summary>Settled on a new value (after a scroll, drag or click finishes).</summary>
    public event Action<int>? ValueChanged;

    public int Count { get; set; } = 60;

    /// <summary>Fractional row index under the centre line; unbounded, wraps modulo <see cref="Count"/>.</summary>
    public double Position
    {
        get => (double)GetValue(PositionProperty);
        set => SetValue(PositionProperty, value);
    }

    public int Value => Mod((int)Math.Round(Position), Count);

    public WheelColumn()
    {
        Focusable = true;
        ClipToBounds = true;
        Cursor = Cursors.SizeNS;
    }

    public void Select(int value)
    {
        Stop();
        Position = _target = Mod(value, Count);
    }

    protected override Size MeasureOverride(Size available) => new(56, ItemHeight * VisibleRows);

    protected override void OnRender(DrawingContext dc)
    {
        // Transparent fill so the empty space between numbers still takes input.
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(RenderSize));

        var centre = ActualHeight / 2;
        var pos = Position;
        var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var first = (int)Math.Floor(pos) - VisibleRows / 2 - 1;

        for (var i = first; i <= first + VisibleRows + 2; i++)
        {
            var angle = (i - pos) * RowAngle;
            if (Math.Abs(angle) >= Math.PI / 2) continue;

            // Numbers on a drum: closer together, flatter and fainter towards the edges.
            var squash = Math.Cos(angle);
            var y = centre + Radius * Math.Sin(angle);
            var opacity = Math.Clamp(1 - Math.Abs(i - pos) * 0.3, 0, 1);

            var text = new FormattedText(Mod(i, Count).ToString("00", CultureInfo.InvariantCulture),
                CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Face, 22, Brushes.White, dpi);

            var m = Matrix.Identity;
            m.Scale(1, squash);
            m.Translate((ActualWidth - text.Width) / 2, y - text.Height * squash / 2);
            dc.PushOpacity(opacity);
            dc.PushTransform(new MatrixTransform(m));
            dc.DrawText(text, new Point(0, 0));
            dc.Pop();
            dc.Pop();
        }
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        // Wheel down shows the next number, like scrolling a list.
        _target = Math.Round(_target) + (e.Delta < 0 ? 1 : -1);
        AnimateTo(_target);
        e.Handled = true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key is not (Key.Up or Key.Down)) return;
        _target = Math.Round(_target) + (e.Key == Key.Down ? 1 : -1);
        AnimateTo(_target);
        e.Handled = true;
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        Focus();
        Stop();
        _dragging = true;
        _dragStartY = e.GetPosition(this).Y;
        _dragStartPos = Position;
        _dragDistance = 0;
        _samples.Clear();
        _samples.Add((DateTime.UtcNow, Position));
        CaptureMouse();
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (!_dragging) return;
        var dy = e.GetPosition(this).Y - _dragStartY;
        _dragDistance = Math.Max(_dragDistance, Math.Abs(dy));
        // Dragging up brings later numbers into the middle.
        Position = _dragStartPos - dy / ItemHeight;
        _samples.Add((DateTime.UtcNow, Position));
        if (_samples.Count > 6) _samples.RemoveAt(0);
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        if (!_dragging) return;
        _dragging = false;
        ReleaseMouseCapture();

        if (_dragDistance < 3)
        {
            // A click: bring the clicked number to the middle.
            var row = Math.Round(Position + (e.GetPosition(this).Y - ActualHeight / 2) / ItemHeight);
            AnimateTo(_target = row);
            return;
        }

        // A flick keeps spinning for a bit, in proportion to its speed.
        var (t0, p0) = _samples[0];
        var seconds = (DateTime.UtcNow - t0).TotalSeconds;
        var velocity = seconds > 0.005 ? (Position - p0) / seconds : 0; // rows per second
        var coast = Math.Clamp(velocity * 0.18, -12, 12);
        AnimateTo(_target = Math.Round(Position + coast));
    }

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        if (!_dragging) return;
        _dragging = false;
        AnimateTo(_target = Math.Round(Position));
    }

    void AnimateTo(double target)
    {
        var from = Position;
        var distance = Math.Abs(target - from);
        var animation = new DoubleAnimation(from, target, TimeSpan.FromMilliseconds(Math.Min(220 + 45 * distance, 750)))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        };
        animation.Completed += (_, _) =>
        {
            if (_target != target) return; // superseded by a newer scroll
            BeginAnimation(PositionProperty, null);
            Position = _target = Mod((int)target, Count);
            ValueChanged?.Invoke(Value);
        };
        BeginAnimation(PositionProperty, animation);
    }

    void Stop()
    {
        var current = Position;
        BeginAnimation(PositionProperty, null);
        Position = current;
    }

    static int Mod(int x, int m) => ((x % m) + m) % m;
}
