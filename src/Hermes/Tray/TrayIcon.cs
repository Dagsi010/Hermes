using System.Drawing;
using System.Windows.Forms;
using Hermes.Core;
using Microsoft.Win32;
using Application = System.Windows.Application;

namespace Hermes.Tray;

/// <summary>Notification-area icon with the Verzló caduceus and the right-click menu.</summary>
public sealed class TrayIcon : IDisposable
{
    readonly NotifyIcon _icon = new() { Text = "Hermes" };
    readonly ContextMenuStrip _menu = new();
    readonly ToolStripMenuItem _refresh = new();
    readonly ToolStripMenuItem _edit = new();
    readonly ToolStripMenuItem _toggle = new();
    readonly ToolStripMenuItem _settings = new();
    readonly ToolStripMenuItem _signOut = new();
    readonly ToolStripMenuItem _exit = new();

    public event Action? RefreshClicked;
    public event Action? EditLayoutClicked;
    public event Action? ToggleOverlayClicked;
    public event Action? SettingsClicked;
    public event Action? SignOutClicked;
    public event Action? ExitClicked;

    public event Action? SignInClicked;

    Action? _balloonAction;

    /// <summary>A notification from the tray icon (shown as a Windows notification); clicking it runs <paramref name="onClick"/>.</summary>
    public void Notify(string title, string text, Action? onClick)
    {
        _balloonAction = onClick;
        _icon.ShowBalloonTip(10_000, title, text, ToolTipIcon.Info);
    }

    /// <summary>Hover text: "Hermes" plus the current class line. Windows allows 127 characters.</summary>
    public void SetStatus(string status)
    {
        var text = string.IsNullOrEmpty(status) ? "Hermes" : $"Hermes\n{status}";
        _icon.Text = text.Length <= 127 ? text : text[..124] + "...";
    }

    public Func<bool> IsSignedIn { get; set; } = () => false;
    public Func<bool> IsOverlayVisible { get; set; } = () => true;
    public Func<bool> IsEditMode { get; set; } = () => false;

    public TrayIcon()
    {
        _menu.Renderer = new ToolStripProfessionalRenderer(new DarkColors()) { RoundedEdges = false };
        _menu.BackColor = DarkColors.Background;
        _menu.ForeColor = Color.White;
        _menu.Font = new Font("Segoe UI", 9.5f);
        _menu.ShowImageMargin = false;
        _menu.ShowCheckMargin = true;
        _menu.Items.AddRange(new ToolStripItem[]
        {
            _refresh, _edit, _toggle, new ToolStripSeparator(), _settings, _signOut, new ToolStripSeparator(), _exit,
        });
        foreach (ToolStripItem item in _menu.Items)
        {
            item.ForeColor = Color.White;
            item.Padding = new Padding(4, 3, 4, 3);
        }
        _menu.Opening += (_, _) =>
        {
            UpdateTexts();
            _menu.BackColor = DarkColors.Background;
        };

        _refresh.Click += (_, _) => RefreshClicked?.Invoke();
        _edit.Click += (_, _) => EditLayoutClicked?.Invoke();
        _toggle.Click += (_, _) => ToggleOverlayClicked?.Invoke();
        _settings.Click += (_, _) => SettingsClicked?.Invoke();
        _signOut.Click += (_, _) =>
        {
            if (IsSignedIn()) SignOutClicked?.Invoke();
            else SignInClicked?.Invoke();
        };
        _exit.Click += (_, _) => ExitClicked?.Invoke();
        _icon.BalloonTipClicked += (_, _) =>
        {
            var action = _balloonAction;
            _balloonAction = null;
            action?.Invoke();
        };

        _icon.ContextMenuStrip = _menu;
        _icon.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left) SettingsClicked?.Invoke();
        };

        UpdateIcon();
        UpdateTexts();
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
        _icon.Visible = true;
    }

    public void UpdateTexts()
    {
        _refresh.Text = Loc.T("tray.refresh");
        _edit.Text = Loc.T("tray.editLayout");
        _edit.Checked = IsEditMode();
        _toggle.Text = Loc.T(IsOverlayVisible() ? "tray.hide" : "tray.show");
        _settings.Text = Loc.T("tray.settings");
        _signOut.Text = Loc.T(IsSignedIn() ? "tray.signOut" : "tray.signIn");
        _exit.Text = Loc.T("tray.exit");
    }

    void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category == UserPreferenceCategory.General) Application.Current.Dispatcher.BeginInvoke(UpdateIcon);
    }

    /// <summary>White caduceus on a dark taskbar, navy on a light one.</summary>
    void UpdateIcon()
    {
        var name = TaskbarIsLight() ? "hermes.ico" : "hermes-light.ico";
        var stream = Application.GetResourceStream(new Uri($"pack://application:,,,/Assets/{name}"))!.Stream;
        var old = _icon.Icon;
        _icon.Icon = new Icon(stream, SystemInformation.SmallIconSize);
        old?.Dispose();
    }

    static bool TaskbarIsLight()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        return key?.GetValue("SystemUsesLightTheme") is int v && v != 0;
    }

    public void Dispose()
    {
        SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
        _icon.Visible = false;
        _icon.Dispose();
        _menu.Dispose();
    }

    /// <summary>Menu colours from the current theme, read live so a theme change needs no rebuild.</summary>
    sealed class DarkColors : ProfessionalColorTable
    {
        public static Color Background => Convert(Themes.Current.Card);
        static Color Hover => Convert(Themes.Current.Band);
        static Color Border => Convert(Themes.Current.ControlBorder);

        static Color Convert(System.Windows.Media.Color c) => Color.FromArgb(c.R, c.G, c.B);

        public override Color ToolStripDropDownBackground => Background;
        public override Color MenuBorder => Border;
        public override Color MenuItemBorder => Hover;
        public override Color MenuItemSelected => Hover;
        public override Color MenuItemSelectedGradientBegin => Hover;
        public override Color MenuItemSelectedGradientEnd => Hover;
        public override Color ImageMarginGradientBegin => Background;
        public override Color ImageMarginGradientMiddle => Background;
        public override Color ImageMarginGradientEnd => Background;
        public override Color CheckBackground => Hover;
        public override Color CheckSelectedBackground => Hover;
        public override Color CheckPressedBackground => Hover;
        public override Color SeparatorDark => Border;
        public override Color SeparatorLight => Background;
    }
}
