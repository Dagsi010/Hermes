using System.Diagnostics;
using System.Windows.Interop;
using Microsoft.Web.WebView2.Core;

namespace Hermes.Inna;

/// <summary>
/// A WebView2 on a window that is never shown, for checking the Inna session in the
/// background. Created per use and disposed afterwards so its browser processes exit.
/// </summary>
public sealed class HiddenBrowser : IDisposable
{
    const int WsPopup = unchecked((int)0x80000000);
    const int WsExToolWindow = 0x80;

    HwndSource? _host;
    CoreWebView2Controller? _controller;
    bool _navigating;
    DateTime _lastActivity = DateTime.UtcNow;

    public CoreWebView2Environment? Environment { get; private set; }

    /// <summary>Error of the last top-level navigation that failed, if any.</summary>
    public CoreWebView2WebErrorStatus? LastError { get; private set; }

    public CoreWebView2 Core => _controller?.CoreWebView2 ?? throw new InvalidOperationException("Not started");

    public async Task<CoreWebView2> StartAsync(CoreWebView2Environment env)
    {
        Environment = env;
        // A regular-sized window that is simply never made visible (no WS_VISIBLE).
        _host = new HwndSource(new HwndSourceParameters("HermesInnaHidden", 1280, 900)
        {
            WindowStyle = WsPopup,
            ExtendedWindowStyle = WsExToolWindow,
        });
        _controller = await env.CreateCoreWebView2ControllerAsync(_host.Handle);
        _controller.Bounds = new System.Drawing.Rectangle(0, 0, 1280, 900);
        var core = _controller.CoreWebView2;
        InnaBrowser.Configure(core);
        core.NavigationStarting += (_, _) => { _navigating = true; _lastActivity = DateTime.UtcNow; };
        core.SourceChanged += (_, _) => _lastActivity = DateTime.UtcNow;
        core.NavigationCompleted += (_, e) =>
        {
            _navigating = false;
            _lastActivity = DateTime.UtcNow;
            LastError = e.IsSuccess ? null : e.WebErrorStatus;
        };
        // Popups (some sign-in flows open one) stay in this view.
        core.NewWindowRequested += (_, e) =>
        {
            e.Handled = true;
            core.Navigate(e.Uri);
        };
        return core;
    }

    /// <summary>
    /// Navigates and waits until redirects stop: no navigation for <paramref name="quiet"/>,
    /// or <paramref name="max"/> has passed.
    /// </summary>
    public async Task NavigateAndSettleAsync(string url, TimeSpan quiet, TimeSpan max)
    {
        LastError = null;
        _navigating = true;
        _lastActivity = DateTime.UtcNow;
        Core.Navigate(url);
        var clock = Stopwatch.StartNew();
        while (clock.Elapsed < max)
        {
            await Task.Delay(250);
            if (!_navigating && DateTime.UtcNow - _lastActivity > quiet) return;
        }
    }

    public void Dispose()
    {
        _controller?.Close();
        _controller = null;
        _host?.Dispose();
        _host = null;
    }
}
