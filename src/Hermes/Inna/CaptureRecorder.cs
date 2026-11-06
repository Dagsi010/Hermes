using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Hermes.Core;
using Microsoft.Web.WebView2.Core;

namespace Hermes.Inna;

/// <summary>
/// Debug capture: records the requests Inna's pages make (URL, method, status, type and,
/// for data responses, the body) so the endpoints behind the timetable, assignments, grades
/// and attendance can be found. Before anything is kept, cookies and auth headers are dropped
/// and tokens, kennitölur and email addresses are masked.
/// </summary>
public sealed partial class CaptureRecorder
{
    const int MaxDataBody = 300_000;
    const int MaxHtmlBody = 150_000;

    public sealed class Entry
    {
        public required string Time { get; init; }
        public required string Kind { get; init; }
        public required string Method { get; init; }
        public required string Url { get; init; }
        public int Status { get; init; }
        public string? ContentType { get; init; }
        public Dictionary<string, string>? RequestHeaders { get; init; }
        public string? RequestBody { get; init; }
        public string? Body { get; set; }
        public int? BodyLength { get; set; }
        public bool Truncated { get; set; }
    }

    readonly List<Entry> _entries = new();
    readonly DateTime _started = DateTime.Now;

    public event Action? Changed;

    public int Count
    {
        get { lock (_entries) return _entries.Count; }
    }

    public int DataCount
    {
        get { lock (_entries) return _entries.Count(e => e.Kind == "data"); }
    }

    public void Attach(CoreWebView2 core)
    {
        core.WebResourceResponseReceived += async (_, e) =>
        {
            try { await RecordAsync(e); }
            catch (Exception ex) { Log.Error("Capture failed for one response", ex); }
        };
        core.NavigationStarting += (_, e) => Add(new Entry
        {
            Time = Now, Kind = "navigation", Method = "GET", Url = RedactUrl(e.Uri),
        });
        core.HistoryChanged += (_, _) => Add(new Entry
        {
            Time = Now, Kind = "route", Method = "-", Url = RedactUrl(core.Source),
        });
    }

    async Task RecordAsync(CoreWebView2WebResourceResponseReceivedEventArgs e)
    {
        if (!Uri.TryCreate(e.Request.Uri, UriKind.Absolute, out var uri) || !uri.Host.EndsWith("inna.is")) return;

        var contentType = e.Response.Headers.Contains("Content-Type") ? e.Response.Headers.GetHeader("Content-Type") : null;
        var kind = Classify(uri, contentType);
        if (kind == "static") return; // images, fonts, stylesheets: noise

        var entry = new Entry
        {
            Time = Now,
            Kind = kind,
            Method = e.Request.Method,
            Url = RedactUrl(uri.ToString()),
            Status = e.Response.StatusCode,
            ContentType = contentType,
            RequestHeaders = KeptHeaders(e.Request.Headers),
            RequestBody = e.Request.Method != "GET" ? await ReadRequestBody(e.Request) : null,
        };

        if (kind is "data" or "html")
        {
            try
            {
                using var stream = await e.Response.GetContentAsync();
                if (stream != null)
                {
                    var text = await Task.Run(() => new StreamReader(stream, Encoding.UTF8).ReadToEnd());
                    entry.BodyLength = text.Length;
                    var max = kind == "html" ? MaxHtmlBody : MaxDataBody;
                    if (text.Length > max)
                    {
                        text = text[..max];
                        entry.Truncated = true;
                    }
                    entry.Body = RedactText(text);
                }
            }
            catch (Exception)
            {
                // Redirects and aborted requests have no readable content.
            }
        }
        Add(entry);
    }

    static string Classify(Uri uri, string? contentType)
    {
        var ct = contentType?.ToLowerInvariant() ?? "";
        var path = uri.AbsolutePath.ToLowerInvariant();
        if (ct.StartsWith("image/") || ct.Contains("font") || ct.Contains("text/css")
            || Regex.IsMatch(path, @"\.(png|jpe?g|gif|svg|ico|woff2?|ttf|eot|css|map)$")) return "static";
        if (ct.Contains("javascript") || path.EndsWith(".js")) return "script";
        if (ct.Contains("html")) return "html";
        return "data"; // json, xml, plain text and anything unlabelled
    }

    static async Task<string?> ReadRequestBody(CoreWebView2WebResourceRequest request)
    {
        try
        {
            if (request.Content is not { } stream) return null;
            var text = await Task.Run(() => new StreamReader(stream, Encoding.UTF8).ReadToEnd());
            return RedactText(text.Length > 20_000 ? text[..20_000] : text);
        }
        catch
        {
            return null;
        }
    }

    // Only headers that describe the request; never Cookie, Authorization or CSRF tokens.
    static readonly string[] HeaderAllowList = { "Accept", "Content-Type", "X-Requested-With", "Referer" };

    static Dictionary<string, string>? KeptHeaders(CoreWebView2HttpRequestHeaders headers)
    {
        var kept = new Dictionary<string, string>();
        foreach (var name in HeaderAllowList)
            if (headers.Contains(name))
                kept[name] = name == "Referer" ? RedactUrl(headers.GetHeader(name)) : headers.GetHeader(name);
        return kept.Count > 0 ? kept : null;
    }

    void Add(Entry entry)
    {
        lock (_entries) _entries.Add(entry);
        Changed?.Invoke();
    }

    static string Now => DateTime.Now.ToString("HH:mm:ss.fff");

    /// <summary>Writes the capture to %LOCALAPPDATA%\Hermes\captures and returns the file path.</summary>
    public string Save()
    {
        var dir = Path.Combine(Paths.LocalDir, "captures");
        Directory.CreateDirectory(dir);
        var file = Path.Combine(dir, $"inna-capture-{_started:yyyyMMdd-HHmmss}.json");
        List<Entry> copy;
        lock (_entries) copy = _entries.ToList();
        var doc = new
        {
            hermes = typeof(App).Assembly.GetName().Version?.ToString(),
            started = _started.ToString("s"),
            saved = DateTime.Now.ToString("s"),
            note = "Cookies, auth headers, tokens, kennitölur and email addresses are masked. Names and grades are not.",
            entries = copy,
        };
        File.WriteAllText(file, JsonSerializer.Serialize(doc, new JsonSerializerOptions
        {
            WriteIndented = true,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        }));
        return file;
    }

    // ---- Redaction ------------------------------------------------------------

    [GeneratedRegex(@"eyJ[\w-]{5,}\.[\w-]{5,}\.[\w-]+")]
    private static partial Regex Jwt();

    [GeneratedRegex(@"(?<!\d)\d{6}-?\d{4}(?!\d)")]
    private static partial Regex Kennitala();

    [GeneratedRegex(@"[\w.+-]+@[\w-]+(\.[\w-]+)+")]
    private static partial Regex Email();

    // Query parameters whose values are secrets or one-time codes.
    [GeneratedRegex(@"^(.*token.*|code|state|session.*|sid|.*auth.*|key|ticket|sig.*|t|nonce|jsessionid)$", RegexOptions.IgnoreCase)]
    private static partial Regex SecretParam();

    public static string RedactText(string text)
    {
        text = Jwt().Replace(text, "<jwt>");
        text = Kennitala().Replace(text, "######-####");
        return Email().Replace(text, "<email>");
    }

    public static string RedactUrl(string? url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var u)) return url ?? "";
        var path = Regex.Replace(u.AbsolutePath, @";jsessionid=[^/?]*", ";jsessionid=***", RegexOptions.IgnoreCase);
        var query = u.Query.TrimStart('?');
        if (query.Length > 0)
        {
            var parts = query.Split('&').Select(p =>
            {
                var i = p.IndexOf('=');
                if (i < 0) return p;
                var name = Uri.UnescapeDataString(p[..i]);
                return SecretParam().IsMatch(name) ? $"{p[..i]}=***" : RedactText(p);
            });
            query = "?" + string.Join("&", parts);
        }
        return RedactText($"{u.Scheme}://{u.Host}{path}{query}");
    }
}
