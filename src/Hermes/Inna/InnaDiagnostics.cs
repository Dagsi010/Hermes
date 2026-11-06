using System.Text;
using System.Text.Json;
using Hermes.Core;

namespace Hermes.Inna;

/// <summary>
/// Development aid (--inna-diagnose &lt;file&gt;): describes what Inna has stored in Hermes' profile
/// and where a silent Google sign-in ends up. Records only hosts, paths, key names and the
/// shape of the token, never cookie values, tokens or query strings.
/// </summary>
public static class InnaDiagnostics
{
    public static async Task RunAsync(string file)
    {
        var report = new StringBuilder();
        void Line(string s) => report.AppendLine(s);

        var browser = new HiddenBrowser();
        try
        {
            var core = await browser.StartAsync(await InnaBrowser.Environment);
            Line($"userAgent: {core.Settings.UserAgent}");
            core.NavigationStarting += (_, e) => Line($"  nav-start  {Redact(e.Uri)}");
            core.NavigationCompleted += (_, e) =>
                Line($"  nav-done   ok={e.IsSuccess} http={e.HttpStatusCode} err={e.WebErrorStatus} at {Redact(core.Source)}");
            core.HistoryChanged += (_, _) => Line($"  history    {Redact(core.Source)}");

            foreach (var url in new[] { InnaBrowser.Home, InnaBrowser.GoogleSignIn })
            {
                Line($"== {url}");
                await browser.NavigateAndSettleAsync(url, TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(40));
                await Task.Delay(2000);
                Line($"final: {Redact(core.Source)}");
                Line($"storage: {await Script(core, StorageShape)}");
                Line($"signedInUrl: {InnaBrowser.IsSignedInUrl(core.Source)}");
            }

            var cookies = await core.CookieManager.GetCookiesAsync(null);
            Line("cookies (name @ domain, expiry):");
            foreach (var c in cookies.Where(c => c.Domain.Contains("inna") || c.Domain.Contains("google")))
                Line($"  {c.Name} @ {c.Domain} session={c.IsSession} expires={(c.IsSession ? "-" : c.Expires.ToString("yyyy-MM-dd"))}");
        }
        catch (Exception ex)
        {
            Line($"error: {ex}");
        }
        finally
        {
            browser.Dispose();
            File.WriteAllText(file, report.ToString());
        }
    }

    /// <summary>
    /// Development aid (--capture-test &lt;file&gt;): runs the capture recorder on Inna's public front
    /// page only (no account pages) and checks the masking on made-up samples.
    /// </summary>
    public static async Task RunCaptureTestAsync(string file)
    {
        var report = new StringBuilder();
        var samples = new[]
        {
            "https://nam.inna.is/api/x?token=abc123&week=45&code=XYZ&state=s1",
            "https://nam.inna.is/Students.html;jsessionid=ABCDEF0123?nemandi=1203052390",
            "{\"kennitala\":\"120305-2390\",\"email\":\"nemandi@verslo.is\",\"jwt\":\"eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiIxIn0.c2lnbmF0dXJl\",\"grade\":8.5,\"time\":\"08:30\",\"room\":\"204\"}",
        };
        foreach (var s in samples)
            report.AppendLine($"{s}\n  -> {(s.StartsWith("http") ? CaptureRecorder.RedactUrl(s) : CaptureRecorder.RedactText(s))}");

        var recorder = new CaptureRecorder();
        var browser = new HiddenBrowser();
        try
        {
            var core = await browser.StartAsync(await InnaBrowser.Environment);
            recorder.Attach(core);
            await browser.NavigateAndSettleAsync(InnaBrowser.Home, TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(30));
            await Task.Delay(1500);
            report.AppendLine($"recorded {recorder.Count} entries ({recorder.DataCount} data) -> {recorder.Save()}");
        }
        catch (Exception ex)
        {
            report.AppendLine($"error: {ex}");
        }
        finally
        {
            browser.Dispose();
            File.WriteAllText(file, report.ToString());
        }
    }

    // Key names, plus for anything that looks like a JWT: its JSON wrapping, segment count and
    // payload field names with their JSON types. Values are not read out.
    const string StorageShape = """
        (() => {
          const out = { origin: location.origin, keys: Object.keys(localStorage), session: Object.keys(sessionStorage), tokens: {} };
          for (const k of out.keys) {
            const raw = localStorage.getItem(k) || '';
            let v = raw; let wrapped = false;
            try { const p = JSON.parse(raw); if (typeof p === 'string') { v = p; wrapped = true; } } catch {}
            const parts = v.split('.');
            if (parts.length === 3 && parts[0].startsWith('eyJ')) {
              let fields = {};
              try {
                const json = JSON.parse(atob(parts[1].replace(/-/g, '+').replace(/_/g, '/')));
                for (const f in json) fields[f] = typeof json[f] + (f === 'exp' || f === 'iat' ? '=' + json[f] : '');
              } catch (e) { fields = 'unparsable: ' + e.message; }
              out.tokens[k] = { rawLength: raw.length, jsonWrapped: wrapped, fields };
            }
          }
          return JSON.stringify(out);
        })()
        """;

    static async Task<string> Script(Microsoft.Web.WebView2.Core.CoreWebView2 core, string js)
    {
        try
        {
            return JsonSerializer.Deserialize<string>(await core.ExecuteScriptAsync(js)) ?? "null";
        }
        catch (Exception ex)
        {
            return "script error: " + ex.Message;
        }
    }

    static string Redact(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var u) ? $"{u.Scheme}://{u.Host}{u.AbsolutePath}" : url ?? "?";
}
