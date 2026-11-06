using System.Globalization;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation;

namespace Hermes.Ui;

/// <summary>A time field that expands into iPhone-style hour and minute wheels.</summary>
public partial class TimePicker : UserControl
{
    const double OpenHeight = 196;
    bool _open;

    /// <summary>Raised with "HH:mm" whenever a wheel settles on a new value.</summary>
    public event Action<string>? TimeChanged;

    public TimePicker()
    {
        InitializeComponent();
        Hours.ValueChanged += _ => OnWheelChanged();
        Minutes.ValueChanged += _ => OnWheelChanged();
    }

    /// <summary>The time as "HH:mm".</summary>
    public string Time
    {
        get => $"{Hours.Value:00}:{Minutes.Value:00}";
        set
        {
            if (!TimeSpan.TryParseExact(value, @"h\:mm", CultureInfo.InvariantCulture, out var t)) return;
            Hours.Select(t.Hours);
            Minutes.Select(t.Minutes);
            TimeText.Text = Time;
        }
    }

    void OnWheelChanged()
    {
        TimeText.Text = Time;
        TimeChanged?.Invoke(Time);
    }

    void Header_Click(object sender, MouseButtonEventArgs e)
    {
        _open = !_open;
        var duration = TimeSpan.FromMilliseconds(220);
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        WheelHost.BeginAnimation(HeightProperty, new DoubleAnimation(_open ? OpenHeight : 0, duration) { EasingFunction = ease });
        ChevronRotation.BeginAnimation(System.Windows.Media.RotateTransform.AngleProperty,
            new DoubleAnimation(_open ? 180 : 0, duration) { EasingFunction = ease });
        if (_open) Hours.Focus();
    }
}
