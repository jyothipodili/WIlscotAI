using System.Text;
using Allure.Net.Commons;
using Microsoft.Playwright;
using Serilog;

namespace WillscotAutomation.Utilities;

// Writes attachments directly to allure-results/ (reliable) and also tries
// AllureApi.AddAttachment (may lose async context in Reqnroll hooks — silently ignored).
public static class AllureHelper
{
    private const string AllureResultsDir = "allure-results";

    private static string WriteToResultsDir(byte[] data, string extension)
    {
        Directory.CreateDirectory(AllureResultsDir);
        var fileName = $"{Guid.NewGuid()}-attachment{extension}";
        var fullPath = Path.Combine(AllureResultsDir, fileName);
        File.WriteAllBytes(fullPath, data);
        return fullPath;
    }

    public static void AttachScreenshot(byte[] screenshotBytes, string name = "Screenshot")
    {
        var path = WriteToResultsDir(screenshotBytes, ".png");
        Log.Debug("Screenshot saved: {Path}", path);

        try { AllureApi.AddAttachment(name, "image/png", screenshotBytes, ".png"); }
        catch { /* AsyncLocal context not available in Reqnroll AfterScenario — file saved above */ }
    }

    public static void AttachText(string content, string name,
        string mimeType = "text/plain", string ext = ".txt")
    {
        if (string.IsNullOrWhiteSpace(content)) return;

        var bytes = Encoding.UTF8.GetBytes(content);
        var path  = WriteToResultsDir(bytes, ext);
        Log.Debug("Artefact saved: {Name} → {Path}", name, path);

        try { AllureApi.AddAttachment(name, mimeType, bytes, ext); }
        catch { /* same reason as above */ }
    }

    public static void AttachConsoleErrors(IEnumerable<string> errors,
        string name = "Browser Console Errors")
    {
        var content = string.Join(Environment.NewLine, errors);
        if (!string.IsNullOrWhiteSpace(content)) AttachText(content, name);
    }

    public static void AttachNetworkFailures(IEnumerable<string> failures,
        string name = "Network Failed Requests")
    {
        var content = string.Join(Environment.NewLine, failures);
        if (!string.IsNullOrWhiteSpace(content)) AttachText(content, name);
    }

    public static void AttachJsExceptions(IEnumerable<string> exceptions,
        string name = "JS Exceptions")
    {
        var content = string.Join(Environment.NewLine, exceptions);
        if (!string.IsNullOrWhiteSpace(content)) AttachText(content, name);
    }

    public static async Task AttachPageHtml(IPage page, string name = "Page HTML Dump")
    {
        var html = await page.ContentAsync();
        AttachText(html, name, "text/html", ".html");
    }

    public static async Task AttachFailureBundle(
        IPage page, LogCollector logCollector, string scenarioTitle)
    {
        var safeTitle = string.Concat(scenarioTitle.Split(Path.GetInvalidFileNameChars()));

        // Screenshot
        var screenshot = await ScreenshotHelper.CaptureScreenshot(page);
        AttachScreenshot(screenshot, $"FAIL — {safeTitle}");

        // Console errors
        if (logCollector.ConsoleErrors.Count > 0)
            AttachConsoleErrors(logCollector.ConsoleErrors);

        // JS exceptions
        if (logCollector.JsExceptions.Count > 0)
            AttachJsExceptions(logCollector.JsExceptions);

        // Network failures
        if (logCollector.NetworkFailures.Count > 0)
            AttachNetworkFailures(logCollector.NetworkFailures);

        // HTML dump
        await AttachPageHtml(page);

        Log.Information("Failure bundle saved to {Dir} for scenario: {Title}",
            AllureResultsDir, scenarioTitle);
    }
}
