using Microsoft.Playwright;

namespace WillscotAutomation.Utilities;

// Listens to browser page events and collects console errors, JS exceptions,
// and failed network requests for the duration of one scenario.
public sealed class LogCollector
{
    private readonly List<string> _consoleErrors   = new();
    private readonly List<string> _jsExceptions    = new();
    private readonly List<string> _networkFailures = new();

    private readonly object _lock = new();

    public IReadOnlyList<string> ConsoleErrors   => _consoleErrors.AsReadOnly();
    public IReadOnlyList<string> JsExceptions    => _jsExceptions.AsReadOnly();
    public IReadOnlyList<string> NetworkFailures => _networkFailures.AsReadOnly();

    public bool HasErrors =>
        _consoleErrors.Count   > 0 ||
        _jsExceptions.Count    > 0 ||
        _networkFailures.Count > 0;

    public LogCollector(IPage page)
    {
        page.Console += OnConsoleMessage;
        page.PageError += OnPageError;
        page.RequestFailed += OnRequestFailed;
    }

    private void OnConsoleMessage(object? sender, IConsoleMessage msg)
    {
        // Capture only true errors — warnings/info from third-party analytics
        // and deprecation notices are expected noise on live sites.
        if (msg.Type is not "error") return;

        var text = msg.Text;

        // Skip known third-party infrastructure errors — these are not WillScot
        // product defects and cannot be fixed by the dev team:
        //
        //  • blob: URL load errors — Unbounce A/B testing tool loads its own
        //    blob URLs cross-origin; the browser security model blocks them.
        //  • chat widget errors — Salesforce / Drift / LiveChat widgets fail when
        //    their remote CSS (my.site.com) has CORS misconfiguration, or when
        //    the chat config is unreachable at page-load time.
        //  • CORS policy errors from third-party domains (e.g. my.site.com,
        //    salesforce.com) — external service misconfig, not a WillScot defect.
        //  • Generic net::ERR_FAILED — almost always a downstream consequence of
        //    the CORS block above; paired with the CORS message already filtered.
        if (text.Contains("Not allowed to load local resource: blob:",
                StringComparison.OrdinalIgnoreCase)) return;
        if (text.Contains("chat launch",  StringComparison.OrdinalIgnoreCase)) return;
        if (text.Contains("launchChat",   StringComparison.OrdinalIgnoreCase)) return;
        if (text.Contains("my.site.com",  StringComparison.OrdinalIgnoreCase)) return;
        if (text.Contains("Access-Control-Allow-Origin",
                StringComparison.OrdinalIgnoreCase)) return;
        if (text.Contains("CORS policy",  StringComparison.OrdinalIgnoreCase)) return;
        if (text.Equals("Failed to load resource: net::ERR_FAILED",
                StringComparison.OrdinalIgnoreCase)) return;

        lock (_lock)
        {
            _consoleErrors.Add($"[{msg.Type.ToUpper()}] {text}");
        }
    }

    private void OnPageError(object? sender, string error)
    {
        lock (_lock)
        {
            _jsExceptions.Add($"[JS EXCEPTION] {error}");
        }
    }

    private void OnRequestFailed(object? sender, IRequest request)
    {
        lock (_lock)
        {
            _networkFailures.Add(
                $"[NETWORK FAILED] {request.Method} {request.Url} — {request.Failure}");
        }
    }

    public string FormatConsoleErrors() => string.Join(Environment.NewLine, _consoleErrors);
    public string FormatJsExceptions()  => string.Join(Environment.NewLine, _jsExceptions);
}
