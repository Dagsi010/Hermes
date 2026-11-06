using System.Diagnostics;
using System.Windows;
using Hermes.Core;
using Hermes.Inna;

namespace Hermes.Ui;

/// <summary>
/// Inna in a normal browser view, signed in through Hermes' profile, while every request is
/// recorded. The user clicks through the pages that matter and saves the log.
/// </summary>
public partial class CaptureWindow : Window
{
    readonly CaptureRecorder _recorder = new();
    string? _savedFile;
    int _savedCount;

    public CaptureWindow()
    {
        InitializeComponent();
        Themes.ApplyFluentAccent(Resources, Themes.Current);
        _recorder.Changed += () => Dispatcher.BeginInvoke(UpdateCount);
        UpdateCount();
        Loaded += async (_, _) => await StartAsync();
    }

    async Task StartAsync()
    {
        try
        {
            await Browser.EnsureCoreWebView2Async(await InnaBrowser.Environment);
            var core = Browser.CoreWebView2;
            InnaBrowser.Configure(core);
            _recorder.Attach(core);
            core.NewWindowRequested += (_, e) =>
            {
                e.Handled = true;
                core.Navigate(e.Uri);
            };
            // Signs in silently with the saved Google session and lands on the student pages.
            core.Navigate(InnaBrowser.GoogleSignIn);
            Log.Info("Debug capture started");
        }
        catch (Exception ex)
        {
            Log.Error("Capture browser could not start", ex);
            MessageBox.Show(this, ex.Message, "Hermes", MessageBoxButton.OK, MessageBoxImage.Error);
            Close();
        }
    }

    void UpdateCount() => CountText.Text = Loc.F("cap.count", _recorder.Count, _recorder.DataCount);

    void Save_Click(object sender, RoutedEventArgs e)
    {
        SaveNow();
        // Opened through the shell: "explorer /select,<file>" failed with "Location is not
        // available" here, likely over the non-ASCII user folder name.
        if (_savedFile != null && Path.GetDirectoryName(_savedFile) is { } folder)
            Process.Start(new ProcessStartInfo(folder) { UseShellExecute = true });
    }

    void SaveNow()
    {
        try
        {
            // The file name comes from the start time, so saving again updates the same file.
            _savedFile = _recorder.Save();
            _savedCount = _recorder.Count;
            Log.Info($"Debug capture saved ({_savedCount} entries) to {_savedFile}");
        }
        catch (Exception ex)
        {
            Log.Error("Could not save capture", ex);
            MessageBox.Show(this, ex.Message, "Hermes", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    void Close_Click(object sender, RoutedEventArgs e) => Close();

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        // Never lose a capture: keep whatever was recorded since the last save.
        if (_recorder.Count > _savedCount) SaveNow();
        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        Browser.Dispose();
        base.OnClosed(e);
    }
}
