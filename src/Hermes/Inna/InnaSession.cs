using Hermes.Core;
using Microsoft.Web.WebView2.Core;

namespace Hermes.Inna;

public enum SessionState { Unknown, Checking, SignedIn, NeverSignedIn, Expired, Offline }

/// <summary>
/// Whether Hermes can reach Inna as the user. Checks run in a hidden browser that signs in
/// through /auth/google, which completes without any input as long as the Google session in
/// Hermes' profile is still valid (Google keeps it for about a year).
/// </summary>
public sealed class InnaSession
{
    static readonly CoreWebView2WebErrorStatus[] OfflineErrors =
    {
        CoreWebView2WebErrorStatus.CannotConnect, CoreWebView2WebErrorStatus.HostNameNotResolved,
        CoreWebView2WebErrorStatus.Disconnected, CoreWebView2WebErrorStatus.Timeout,
        CoreWebView2WebErrorStatus.ServerUnreachable, CoreWebView2WebErrorStatus.ConnectionAborted,
        CoreWebView2WebErrorStatus.ConnectionReset,
    };

    readonly SettingsStore _settings;
    SessionState _state = SessionState.Unknown;
    Task? _running;

    public InnaSession(SettingsStore settings)
    {
        _settings = settings;
        InnaBrowser.UseEdgeUserAgent = settings.Current.InnaEdgeUserAgent;
    }

    public SessionState State
    {
        get => _state;
        private set
        {
            if (_state == value) return;
            var previous = _state;
            _state = value;
            Log.Info($"Inna session: {previous} -> {value}");
            StateChanged?.Invoke(previous, value);
        }
    }

    /// <summary>(previous, current)</summary>
    public event Action<SessionState, SessionState>? StateChanged;

    /// <summary>Checks the session; concurrent calls share one check.</summary>
    public Task CheckAsync() => _running ??= RunCheck();

    async Task RunCheck()
    {
        var before = State;
        State = SessionState.Checking;
        var browser = new HiddenBrowser();
        try
        {
            await browser.StartAsync(await InnaBrowser.Environment);

            // The school system's own session ends when the browser closes, so every check signs in
            // again through Google. That needs no input while Google still knows the user in this
            // profile. Also tried before the first confirmed sign-in, in case one already exists.
            var method = _settings.Current.InnaLoginMethod;
            if (method is null or "google")
            {
                await browser.NavigateAndSettleAsync(InnaBrowser.GoogleSignIn, TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(45));
                if (browser.LastError is { } error && OfflineErrors.Contains(error))
                {
                    State = SessionState.Offline;
                    return;
                }
                if (InnaBrowser.IsSignedInUrl(browser.Core.Source))
                {
                    Log.Info($"Signed in to Inna without input, at {Redact(browser.Core.Source)}");
                    MarkSignedIn("google");
                    await FetchAsync(browser);
                    return;
                }
                Log.Info($"Silent Google sign-in stopped at {Redact(browser.Core.Source)}");
            }

            // Ísland.is and password sign-ins always need the user.
            State = _settings.Current.InnaSetupDone ? SessionState.Expired : SessionState.NeverSignedIn;
        }
        catch (Exception ex)
        {
            Log.Error("Inna session check failed", ex);
            State = before == SessionState.Checking ? SessionState.Unknown : before;
        }
        finally
        {
            browser.Dispose();
            _running = null;
        }
    }

    /// <summary>Fresh data from Inna after a successful sign-in.</summary>
    public event Action<Data.SchoolData>? DataFetched;

    async Task FetchAsync(HiddenBrowser browser)
    {
        try
        {
            var data = await InnaClient.FetchAsync(browser.Core, Clock.Now);
            Log.Info($"Fetched from Inna: {data.Lessons.Count} classes, {data.Assignments.Count} assignments, " +
                     $"{data.Grades.Count} grades, attendance {(data.Attendance.HasValue ? "yes" : "no")}");
            DataFetched?.Invoke(data);
        }
        catch (Exception ex)
        {
            // The overlay keeps showing the cached copy.
            Log.Error("Fetching data from Inna failed", ex);
        }
    }

    /// <summary>Called by the sign-in window once Inna has issued a token.</summary>
    public void MarkSignedIn(string? method)
    {
        if (!_settings.Current.InnaSetupDone || (method != null && _settings.Current.InnaLoginMethod != method))
        {
            _settings.Current.InnaSetupDone = true;
            if (method != null) _settings.Current.InnaLoginMethod = method;
            _settings.Save();
        }
        State = SessionState.SignedIn;
    }

    public void RememberEdgeUserAgent()
    {
        InnaBrowser.UseEdgeUserAgent = true;
        _settings.Current.InnaEdgeUserAgent = true;
        _settings.Save();
    }

    public async Task SignOutAsync()
    {
        try
        {
            await InnaBrowser.SignOutAsync();
        }
        catch (Exception ex)
        {
            Log.Error("Sign-out failed", ex);
        }
        _settings.Current.InnaSetupDone = false;
        _settings.Current.InnaLoginMethod = null;
        _settings.Save();
        State = SessionState.NeverSignedIn;
    }

    /// <summary>Host and path only, so query strings with tokens never reach the log.</summary>
    static string Redact(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var u) ? $"{u.Host}{u.AbsolutePath}" : "?";
}
