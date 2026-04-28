using System.Collections.Concurrent;
using System.Text.RegularExpressions;

namespace WillscotAutomation.Utilities;

/// <summary>
/// Thread-safe store that maps TC-IDs (e.g. "TC-001") to their full scenario
/// title and, for failed tests, the path of a saved failure screenshot.
///
/// Populated during AfterScenario; consumed by ZephyrService during AfterTestRun.
/// </summary>
public static class ZephyrAttachmentTracker
{
    private static readonly ConcurrentDictionary<string, ScenarioMeta> _data = new();

    private static readonly Regex _tcPattern =
        new(@"\bTC-\d{3}\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // ── Write ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// Records the full scenario title and optional failure screenshot path.
    /// Extracts the TC-ID automatically from the title (e.g. "TC-001").
    /// Safe to call from parallel AfterScenario hooks.
    /// </summary>
    public static void Track(string scenarioTitle, string? screenshotPath = null)
    {
        var match = _tcPattern.Match(scenarioTitle);
        if (!match.Success) return;

        var tcId = match.Value.ToUpper();
        _data.AddOrUpdate(
            tcId,
            new ScenarioMeta(scenarioTitle, screenshotPath),
            (_, existing) => existing with { ScreenshotPath = screenshotPath ?? existing.ScreenshotPath });
    }

    // ── Read ───────────────────────────────────────────────────────────────────

    /// <summary>Returns a snapshot of all tracked scenarios keyed by TC-ID.</summary>
    public static IReadOnlyDictionary<string, ScenarioMeta> GetAll()
        => new Dictionary<string, ScenarioMeta>(_data);

    /// <summary>Clears all tracked data (useful for test-isolation in unit tests).</summary>
    public static void Clear() => _data.Clear();

    // ── Model ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// Metadata stored per scenario.
    /// </summary>
    /// <param name="Title">Full Gherkin scenario title (e.g. "TC-001 Verify homepage loads").</param>
    /// <param name="ScreenshotPath">Absolute or relative path to the failure screenshot PNG, or null for passing tests.</param>
    public sealed record ScenarioMeta(string Title, string? ScreenshotPath);
}
