using System.Windows;
using Hermes.Core;
using Hermes.Inna;
using Microsoft.Web.WebView2.Core;

namespace Hermes.Ui;

/// <summary>
/// Inna's own login page in Hermes' WebView2 profile. The user signs in themselves;
/// Hermes only watches for Inna's token to appear, then closes.
/// </summary>
public partial class SignInWindow : Window
{
    readonly InnaSession _session;
    string? _method;
    bool _triedEdgeUserAgent;
    bool _done;

    public SignInWindow(InnaSession session)
    {
        _session = session;
        InitializeComponent();
        Themes.ApplyFluentAccent(Resources, Themes.Current);
        Loaded += async (_, _) => await StartAsync();
    }

    async Task StartAsync()
    {
        try
        {
            await Browser.EnsureCoreWebView2Async(await InnaBrowser.Environment);
            var core = Browser.CoreWebView2;
            InnaBrowser.Configure(core);
            core.NavigationStarting += OnNavigationStarting;
            core.NavigationCompleted += OnNavigationCompleted;
            core.HistoryChanged += OnHistoryChanged;
            core.NewWindowRequested += (_, e) =>
            {
                e.Handled = true;
                core.Navigate(e.Uri);
            };
            core.Navigate(InnaBrowser.Home);
        }
        catch (Exception ex)
        {
            Log.Error("WebView2 could not start", ex);
            MessageBox.Show(this, ex.Message, "Hermes", MessageBoxButton.OK, MessageBoxImage.Error);
            Close();
        }
    }

    void OnNavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
    {
        // Remember which of Inna's sign-in routes was used, so background checks know
        // whether a silent Google sign-in is possible. Only the route name is kept.
        if (!Uri.TryCreate(e.Uri, UriKind.Absolute, out var uri) || !uri.Host.EndsWith("inna.is")) return;
        if (uri.AbsolutePath.StartsWith("/auth/google")) _method = "google";
        else if (uri.AbsolutePath.StartsWith("/auth/island")) _method = "island";
        else if (uri.AbsolutePath.StartsWith("/auth/o365") || uri.AbsolutePath.StartsWith("/auth/azure")) _method = "microsoft";
    }

    async void OnNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        if (_done) return;
        var core = Browser.CoreWebView2;

        if (await InnaBrowser.IsGoogleBlockPageAsync(core))
        {
            Log.Info($"Google blocked embedded sign-in (Edge UA tried: {_triedEdgeUserAgent})");
            if (!_triedEdgeUserAgent)
            {
                _triedEdgeUserAgent = true;
                _session.RememberEdgeUserAgent();
                InnaBrowser.Configure(core);
                core.Navigate(InnaBrowser.Home + "auth/google");
            }
            else
            {
                BlockedPanel.Visibility = Visibility.Visible;
            }
            return;
        }

        if (InnaBrowser.IsSignedInUrl(core.Source)) await SucceedAsync();
    }

    // Inna's pages move between routes with script as well; those only show up here.
    async void OnHistoryChanged(object? sender, object e)
    {
        if (!_done && InnaBrowser.IsSignedInUrl(Browser.CoreWebView2.Source)) await SucceedAsync();
    }

    async Task SucceedAsync()
    {
        _done = true;
        _session.MarkSignedIn(_method ?? "password");
        SuccessPanel.Visibility = Visibility.Visible;
        await Task.Delay(1300);
        Close();
    }

    void Island_Click(object sender, RoutedEventArgs e)
    {
        BlockedPanel.Visibility = Visibility.Collapsed;
        Browser.CoreWebView2?.Navigate(InnaBrowser.Home + "auth/island");
    }

    void Password_Click(object sender, RoutedEventArgs e)
    {
        BlockedPanel.Visibility = Visibility.Collapsed;
        Browser.CoreWebView2?.Navigate(InnaBrowser.Home);
    }

    void RetryGoogle_Click(object sender, RoutedEventArgs e)
    {
        BlockedPanel.Visibility = Visibility.Collapsed;
        Browser.CoreWebView2?.Navigate(InnaBrowser.Home + "auth/google");
    }

    protected override void OnClosed(EventArgs e)
    {
        Browser.Dispose();
        base.OnClosed(e);
    }
}
