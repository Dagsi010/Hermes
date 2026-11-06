using System.Text.Json;
using System.Text.RegularExpressions;
using Hermes.Core;
using Microsoft.Web.WebView2.Core;

namespace Hermes.Inna;

/// <summary>
/// The one WebView2 profile Hermes uses for Inna, kept in %LOCALAPPDATA%\Hermes\WebView2
/// so the sign-in survives reboots. Only cookies and site storage live there; password
/// saving and autofill are switched off, so no password is ever stored.
/// </summary>
public static class InnaBrowser
{
    public const string Home = "https://www.inna.is/";
    public const string Host = "www.inna.is";

    static Task<CoreWebView2Environment>? _environment;

    /// <summary>Retry with an explicit Edge user agent (set after Google once blocked sign-in).</summary>
    public static bool UseEdgeUserAgent { get; set; }

    public static Task<CoreWebView2Environment> Environment =>
        _environment ??= CoreWebView2Environment.CreateAsync(null, Paths.WebViewDataDir, new CoreWebView2EnvironmentOptions());

    public static void Configure(CoreWebView2 core)
    {
        core.Settings.IsPasswordAutosaveEnabled = false;
        core.Settings.IsGeneralAutofillEnabled = false;
        core.Settings.AreDefaultContextMenusEnabled = true;
        if (UseEdgeUserAgent) core.Settings.UserAgent = EdgeUserAgent(core.Settings.UserAgent);
    }

    /// <summary>
    /// The WebView2 default agent string already reads like Edge on most machines; this strips any
    /// embedded-browser markers and makes sure the Edge token is present.
    /// </summary>
    static string EdgeUserAgent(string ua)
    {
        var cleaned = Regex.Replace(ua, @"\s*(WebView2|wv)(/[\d.]+)?", "", RegexOptions.IgnoreCase).Trim();
        if (!cleaned.Contains("Edg/"))
        {
            var chrome = Regex.Match(cleaned, @"Chrome/([\d.]+)");
            if (chrome.Success) cleaned += $" Edg/{chrome.Groups[1].Value}";
        }
        return cleaned;
    }

    /// <summary>
    /// The sign-in route through Google. With a live Google session in Hermes' profile it runs
    /// without any input: accounts.google.com, r.inna.is/auth/google/callback, r.inna.is/adgangur,
    /// then the school system (nam.inna.is).
    /// </summary>
    public const string GoogleSignIn = Home + "auth/google";

    /// <summary>
    /// True once the browser has landed in a school system, e.g.
    /// nam.inna.is/Components/Students/Students.html. The portal (www), the sign-in relay (r)
    /// and /auth/ steps don't count: those are on the way in.
    /// </summary>
    public static bool IsSignedInUrl(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var u)
        && u.Host.EndsWith(".inna.is", StringComparison.OrdinalIgnoreCase)
        && u.Host is not ("www.inna.is" or "r.inna.is" or "inna.is")
        && !u.AbsolutePath.StartsWith("/auth/", StringComparison.OrdinalIgnoreCase);

    /// <summary>True on Google's "This browser or app may not be secure" / "Couldn't sign you in" page.</summary>
    public static async Task<bool> IsGoogleBlockPageAsync(CoreWebView2 core)
    {
        if (core.Source is not { } source || !source.Contains("accounts.google.com")) return false;
        if (source.Contains("/signin/rejected") || source.Contains("disallowed_useragent")) return true;
        try
        {
            var text = JsonSerializer.Deserialize<string?>(
                await core.ExecuteScriptAsync("document.body ? document.body.innerText.slice(0, 3000) : ''")) ?? "";
            return Regex.IsMatch(text,
                "may not be secure|Couldn.t sign you in|hugsanlega ekki öruggur|Ekki tókst að skrá þig inn",
                RegexOptions.IgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    public static async Task SignOutAsync()
    {
        // Clearing the whole profile also signs Google out inside Hermes; nothing outside Hermes is touched.
        var env = await Environment;
        var browser = new HiddenBrowser();
        try
        {
            var core = await browser.StartAsync(env);
            await core.Profile.ClearBrowsingDataAsync(CoreWebView2BrowsingDataKinds.AllProfile);
        }
        finally
        {
            browser.Dispose();
        }
    }
}
