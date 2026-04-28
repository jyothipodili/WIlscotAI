using System.Collections.Concurrent;
using System.Text.RegularExpressions;

namespace WillscotAutomation.Utilities;

/// <summary>
/// Thread-safe, static accumulator for test-run totals AND per-scenario results.
///
/// Called from AfterScenario (runs on multiple threads during parallel execution)
/// and read once from AfterTestRun (single-threaded teardown).
/// </summary>
public static class TestRunTracker
{
    private static int      _passed;
    private static int      _failed;
    private static int      _skipped;
    private static DateTime _startUtc = DateTime.UtcNow;

    // Per-scenario: TC-ID → passed(true) / failed(false)
    private static readonly ConcurrentDictionary<string, bool> _scenarioResults = new();

    // Regex to extract "TC-001" etc. from a scenario title
    private static readonly Regex _tcPattern =
        new(@"\bTC-\d{3}\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // ── Record ─────────────────────────────────────────────────────────────────

    public static void Start()
    {
        _startUtc = DateTime.UtcNow;
        Interlocked.Exchange(ref _passed,  0);
        Interlocked.Exchange(ref _failed,  0);
        Interlocked.Exchange(ref _skipped, 0);
        _scenarioResults.Clear();
    }

    /// <summary>
    /// Records result for a scenario. Extracts TC-ID from the title automatically.
    ///
    /// Retry-safe: if the same scenario is recorded more than once (because
    /// RetryOnFailure re-runs AfterScenario on every attempt), the counters are
    /// adjusted so each logical test is counted exactly once using its final result.
    /// </summary>
    public static void RecordScenario(string scenarioTitle, bool passed)
    {
        var match = _tcPattern.Match(scenarioTitle);
        // Use the TC-ID as the dedup key when present; fall back to full title.
        var key = match.Success ? match.Value.ToUpper() : scenarioTitle;

        if (_scenarioResults.TryGetValue(key, out var previousResult))
        {
            // This scenario was already recorded — it is a retry attempt.
            // Update the stored result and adjust counters to reflect the new outcome.
            if (_scenarioResults.TryUpdate(key, passed, previousResult))
            {
                if (!previousResult && passed)
                {
                    // Failed on first attempt, passed on retry → undo the failure count
                    Interlocked.Decrement(ref _failed);
                    Interlocked.Increment(ref _passed);
                }
                else if (previousResult && !passed)
                {
                    // Passed on first attempt, failed on retry (rare) → undo the pass count
                    Interlocked.Decrement(ref _passed);
                    Interlocked.Increment(ref _failed);
                }
                // Same result both times → no counter change needed
            }
            return;
        }

        // First time we've seen this scenario — add it and update counters
        if (_scenarioResults.TryAdd(key, passed))
        {
            if (passed) Interlocked.Increment(ref _passed);
            else        Interlocked.Increment(ref _failed);
        }
    }

    public static void RecordPassed()  => Interlocked.Increment(ref _passed);
    public static void RecordFailed()  => Interlocked.Increment(ref _failed);
    public static void RecordSkipped() => Interlocked.Increment(ref _skipped);

    // ── Read ───────────────────────────────────────────────────────────────────

    public static TestRunSummary GetSummary()
    {
        var p = Volatile.Read(ref _passed);
        var f = Volatile.Read(ref _failed);
        var s = Volatile.Read(ref _skipped);
        return new TestRunSummary
        {
            Passed         = p,
            Failed         = f,
            Skipped        = s,
            Total          = p + f + s,
            StartUtc       = _startUtc,
            FinishUtc      = DateTime.UtcNow,
            ScenarioResults = new Dictionary<string, bool>(_scenarioResults)
        };
    }
}

/// <summary>Immutable snapshot of a completed test run.</summary>
public sealed class TestRunSummary
{
    public int      Passed    { get; init; }
    public int      Failed    { get; init; }
    public int      Skipped   { get; init; }
    public int      Total     { get; init; }
    public DateTime StartUtc  { get; init; }
    public DateTime FinishUtc { get; init; }
    public TimeSpan Duration  => FinishUtc - StartUtc;

    /// <summary>TC-ID → true (passed) / false (failed)</summary>
    public IReadOnlyDictionary<string, bool> ScenarioResults { get; init; }
        = new Dictionary<string, bool>();
}
