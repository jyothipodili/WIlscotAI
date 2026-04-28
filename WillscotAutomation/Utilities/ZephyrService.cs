using System.IO.Compression;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Serilog;
using WillscotAutomation.Config;

namespace WillscotAutomation.Utilities;

/// <summary>
/// Post-run Zephyr Essential + Jira integration service.
///
/// Orchestrates all 7 goals automatically after each test run:
///   1.  Create a Zephyr Test Plan  (named "WillScot Regression — yyyy-MM-dd HH:mm")
///   2.  Create one Test Case per TC-ID found in the run results
///   3.  Add all Test Cases to the Test Plan
///   4.  Create a Test Cycle linked to the Plan
///   5.  Add all Test Cases to the Cycle  (creates Executions)
///   6.  Update each Execution status: Pass / Fail
///   7a. Attach individual failure screenshots to their Executions
///   7b. Attach allure-results.zip to the Test Cycle
///   8.  Transition the configured Jira story to "Done" + add comment (all-pass only)
///
/// Settings are read from appsettings.{ENV}.json → "ZephyrSettings".
/// Set EnableZephyr = false to skip entirely (e.g. local dev runs).
/// </summary>
public static class ZephyrService
{
    private static readonly JsonSerializerOptions _jsonOpts =
        new() { WriteIndented = false, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    // ── Public entry point ─────────────────────────────────────────────────────

    /// <summary>
    /// Main orchestration method.  Called once from AfterTestRun.
    /// Never throws — all errors are caught and logged.
    /// </summary>
    public static async Task RunZephyrIntegrationAsync(TestRunSummary summary)
    {
        var cfg = ConfigReader.ZephyrSettings;

        if (!cfg.EnableZephyr)
        {
            Log.Information("[Zephyr] Integration disabled — set ZephyrSettings:EnableZephyr = true to enable.");
            return;
        }

        if (string.IsNullOrWhiteSpace(cfg.ZephyrJwtToken))
        {
            Log.Warning("[Zephyr] ZephyrJwtToken is empty — skipping. " +
                        "Set ZephyrSettings:ZephyrJwtToken in appsettings.json " +
                        "or via the ZEPHYR_JWT_TOKEN environment variable.");
            return;
        }

        if (summary.ScenarioResults.Count == 0)
        {
            Log.Warning("[Zephyr] No TC-ID results to publish — skipping.");
            return;
        }

        Log.Information("[Zephyr] Starting integration — {Total} tests, {Passed} passed, {Failed} failed.",
            summary.Total, summary.Passed, summary.Failed);

        // ── Diagnose the token so misconfiguration is visible immediately ───────
        DiagnoseToken(cfg.ZephyrJwtToken, cfg.ZephyrApiBaseUrl);

        try
        {
            using var zephyrHttp = BuildZephyrClient(cfg);
            using var jiraHttp   = BuildJiraClient(cfg);

            var runDate    = DateTime.Now.ToString("yyyy-MM-dd HH:mm");
            var planName   = $"{cfg.TestPlanNamePrefix} — {runDate}";
            var cycleName  = $"{cfg.TestCycleNamePrefix} — {runDate}";
            var attachMeta = ZephyrAttachmentTracker.GetAll();

            // ── Goal 1: Create Test Plan ────────────────────────────────────
            var planKey = await CreateTestPlanAsync(zephyrHttp, cfg, planName);
            if (planKey == null)
            {
                Log.Warning("[Zephyr] Aborting: could not create Test Plan.");
                return;
            }

            // ── Goal 2: Create Test Cases ───────────────────────────────────
            var tcKeyMap = await CreateTestCasesAsync(zephyrHttp, cfg, summary, attachMeta);
            if (tcKeyMap.Count == 0)
            {
                Log.Warning("[Zephyr] Aborting: no test cases were created.");
                return;
            }

            // ── Goal 3: Add Test Cases to Plan ──────────────────────────────
            await AddTestCasesToPlanAsync(zephyrHttp, cfg, planKey, tcKeyMap.Values.ToList());

            // ── Goal 4: Create Test Cycle ───────────────────────────────────
            var cycleKey = await CreateTestCycleAsync(zephyrHttp, cfg, cycleName, planKey);
            if (cycleKey == null)
            {
                Log.Warning("[Zephyr] Aborting: could not create Test Cycle.");
                return;
            }

            // ── Goal 5: Add Test Cases to Cycle (creates Executions) ────────
            var execMap = await AddTestCasesToCycleAsync(zephyrHttp, cfg, cycleKey, tcKeyMap, summary);

            // ── Goal 6: Update Execution statuses (Pass / Fail) ─────────────
            await UpdateExecutionStatusesAsync(zephyrHttp, execMap);

            // ── Goal 7a: Attach failure screenshots to failed Executions ────
            await AttachScreenshotsAsync(zephyrHttp, execMap, attachMeta);

            // ── Goal 7b: Attach allure-results.zip to the Test Cycle ────────
            await AttachAllureZipAsync(zephyrHttp, cycleKey);

            // ── Goal 8: Transition Jira story to Done (all-pass only) ───────
            if (!string.IsNullOrWhiteSpace(cfg.JiraStoryKey) && summary.Failed == 0)
                await TransitionJiraIssueDoneAsync(jiraHttp, cfg, summary);
            else if (summary.Failed > 0 && !string.IsNullOrWhiteSpace(cfg.JiraStoryKey))
                await AddJiraCommentAsync(jiraHttp, cfg, summary);

            Log.Information("[Zephyr] Integration complete — Plan: {Plan}, Cycle: {Cycle}",
                planKey, cycleKey);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[Zephyr] Unexpected error during Zephyr integration.");
        }
    }

    // ── Goal 1: Create Test Plan ───────────────────────────────────────────────

    private static async Task<string?> CreateTestPlanAsync(
        HttpClient http, ZephyrSettings cfg, string name)
    {
        try
        {
            var body = Serialize(new
            {
                projectKey = cfg.ProjectKey,
                name,
                status     = new { name = "Active" }
            });

            var resp = await http.PostAsync("testplans", body);
            var json = await resp.Content.ReadAsStringAsync();

            if (!resp.IsSuccessStatusCode)
            {
                Log.Warning("[Zephyr] CreateTestPlan HTTP {Code}: {Body}",
                    (int)resp.StatusCode, Truncate(json));
                return null;
            }

            using var doc = JsonDocument.Parse(json);
            var key = doc.RootElement.GetProperty("key").GetString();
            Log.Information("[Zephyr] ✓ Test Plan created: {Key} — \"{Name}\"", key, name);
            return key;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[Zephyr] CreateTestPlan threw.");
            return null;
        }
    }

    // ── Goal 2: Create one Test Case per TC-ID ─────────────────────────────────

    private static async Task<Dictionary<string, string>> CreateTestCasesAsync(
        HttpClient http,
        ZephyrSettings cfg,
        TestRunSummary summary,
        IReadOnlyDictionary<string, ZephyrAttachmentTracker.ScenarioMeta> attachMeta)
    {
        // tcId ("TC-001") → zephyr test-case key ("SCRUM-T1")
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var tcId in summary.ScenarioResults.Keys.OrderBy(k => k))
        {
            try
            {
                // Use the full scenario title if available, else fall back to TC-ID
                var title = attachMeta.TryGetValue(tcId, out var meta) ? meta.Title : tcId;

                var body = Serialize(new
                {
                    projectKey = cfg.ProjectKey,
                    name       = title,
                    status     = new { name = "Approved" },
                    priority   = new { name = "Normal" }
                });

                var resp = await http.PostAsync("testcases", body);
                var json = await resp.Content.ReadAsStringAsync();

                if (!resp.IsSuccessStatusCode)
                {
                    Log.Warning("[Zephyr] CreateTestCase({Id}) HTTP {Code}: {Body}",
                        tcId, (int)resp.StatusCode, Truncate(json));
                    continue;
                }

                using var doc = JsonDocument.Parse(json);
                var key = doc.RootElement.GetProperty("key").GetString()!;
                result[tcId] = key;
                Log.Debug("[Zephyr]   Test case: {ZKey} ← {TcId}", key, tcId);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[Zephyr] CreateTestCase({Id}) threw.", tcId);
            }
        }

        Log.Information("[Zephyr] ✓ Created {Count}/{Total} test cases.",
            result.Count, summary.ScenarioResults.Count);
        return result;
    }

    // ── Goal 3: Link Test Cases to Plan ───────────────────────────────────────

    private static Task AddTestCasesToPlanAsync(
        HttpClient http, ZephyrSettings cfg, string planKey, List<string> testCaseKeys)
    {
        // Zephyr Scale v2 does not expose a direct "link test cases to plan" endpoint.
        // Test cases become visible under a plan through their test cycle, which is
        // already linked to the plan via testPlanKey in CreateTestCycleAsync.
        Log.Information("[Zephyr] ✓ Test cases visible in plan {Plan} via linked cycle.", planKey);
        return Task.CompletedTask;
    }

    // ── Goal 4: Create Test Cycle ──────────────────────────────────────────────

    private static async Task<string?> CreateTestCycleAsync(
        HttpClient http, ZephyrSettings cfg, string name, string planKey)
    {
        try
        {
            // Include testPlanKey in the body — this is the correct way to link
            // a cycle to a plan in the Zephyr Essential/Scale v2 API.
            var body = Serialize(new
            {
                projectKey  = cfg.ProjectKey,
                name,
                status      = new { name = "In Progress" },
                testPlanKey = planKey
            });

            var resp = await http.PostAsync("testcycles", body);
            var json = await resp.Content.ReadAsStringAsync();

            if (!resp.IsSuccessStatusCode)
            {
                Log.Warning("[Zephyr] CreateTestCycle HTTP {Code}: {Body}",
                    (int)resp.StatusCode, Truncate(json));
                return null;
            }

            using var doc = JsonDocument.Parse(json);
            var key = doc.RootElement.GetProperty("key").GetString();
            Log.Information("[Zephyr] ✓ Test Cycle created: {Key} — \"{Name}\" (linked to plan {Plan})",
                key, name, planKey);
            return key;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[Zephyr] CreateTestCycle threw.");
            return null;
        }
    }

    // ── Goal 5: Add Test Cases to Cycle (creates Executions) ──────────────────

    /// <summary>Creates one Execution per test case and returns tcId → ExecEntry.</summary>
    private static async Task<Dictionary<string, ExecEntry>> AddTestCasesToCycleAsync(
        HttpClient http,
        ZephyrSettings cfg,
        string cycleKey,
        Dictionary<string, string> tcKeyMap,
        TestRunSummary summary)
    {
        var result = new Dictionary<string, ExecEntry>(StringComparer.OrdinalIgnoreCase);

        foreach (var (tcId, tcKey) in tcKeyMap)
        {
            try
            {
                var passed     = summary.ScenarioResults.TryGetValue(tcId, out var p) && p;
                var statusName = passed ? "Pass" : "Fail";

                var body = Serialize(new
                {
                    projectKey   = cfg.ProjectKey,
                    testCaseKey  = tcKey,
                    testCycleKey = cycleKey,
                    statusName                // set final Pass/Fail at creation
                });

                var resp = await http.PostAsync("testexecutions", body);
                var json = await resp.Content.ReadAsStringAsync();

                if (!resp.IsSuccessStatusCode)
                {
                    Log.Warning("[Zephyr] CreateExecution({Id}) HTTP {Code}: {Body}",
                        tcId, (int)resp.StatusCode, Truncate(json));
                    continue;
                }

                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                // The response may use "key" (string) or "id" (number) depending on API version
                string? execKey = null;
                if (root.TryGetProperty("key", out var kProp) && kProp.ValueKind == JsonValueKind.String)
                    execKey = kProp.GetString();
                else if (root.TryGetProperty("id", out var idProp))
                    execKey = idProp.ValueKind == JsonValueKind.String
                        ? idProp.GetString()
                        : idProp.GetInt64().ToString();

                if (result.Count == 0)
                    Log.Debug("[Zephyr] CreateExecution response sample: {Body}", Truncate(json));

                if (execKey == null)
                {
                    Log.Warning("[Zephyr] CreateExecution({Id}) — no 'key'/'id' in response: {Body}",
                        tcId, Truncate(json));
                    continue;
                }

                result[tcId] = new ExecEntry(execKey, tcKey, passed);
                Log.Debug("[Zephyr]   Execution: {ExecKey} for {TcId} → {Status}",
                    execKey, tcId, passed ? "Pass" : "Fail");
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[Zephyr] CreateExecution({Id}) threw.", tcId);
            }
        }

        Log.Information("[Zephyr] ✓ Created {Count} executions in cycle {Cycle}.",
            result.Count, cycleKey);
        return result;
    }

    // ── Goal 6: Update Execution statuses ─────────────────────────────────────

    private static async Task UpdateExecutionStatusesAsync(
        HttpClient http, Dictionary<string, ExecEntry> execMap)
    {
        int updated = 0;

        foreach (var (tcId, entry) in execMap)
        {
            try
            {
                var statusName = entry.Passed ? "Pass" : "Fail";
                var body = Serialize(new { statusName });
                var resp = await http.PutAsync($"testexecutions/{entry.ExecKey}", body);
                var json = await resp.Content.ReadAsStringAsync();

                if (resp.IsSuccessStatusCode)
                {
                    updated++;
                    Log.Debug("[Zephyr]   {ExecKey} ({TcId}) → {Status}", entry.ExecKey, tcId, statusName);
                }
                else
                {
                    Log.Warning("[Zephyr] UpdateExecution({ExecKey}) HTTP {Code}: {Body}",
                        entry.ExecKey, (int)resp.StatusCode, Truncate(json));
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[Zephyr] UpdateExecution({ExecKey}) threw.", entry.ExecKey);
            }
        }

        Log.Information("[Zephyr] ✓ Updated {Count}/{Total} execution statuses.",
            updated, execMap.Count);
    }

    // ── Goal 7a: Attach failure screenshots ───────────────────────────────────

    private static async Task AttachScreenshotsAsync(
        HttpClient http,
        Dictionary<string, ExecEntry> execMap,
        IReadOnlyDictionary<string, ZephyrAttachmentTracker.ScenarioMeta> attachMeta)
    {
        int attached = 0;

        foreach (var (tcId, entry) in execMap)
        {
            if (entry.Passed) continue;
            if (!attachMeta.TryGetValue(tcId, out var meta)) continue;
            if (string.IsNullOrEmpty(meta.ScreenshotPath) || !File.Exists(meta.ScreenshotPath)) continue;

            try
            {
                using var form    = new MultipartFormDataContent();
                var fileBytes     = await File.ReadAllBytesAsync(meta.ScreenshotPath);
                var fileContent   = new ByteArrayContent(fileBytes);
                fileContent.Headers.ContentType = new MediaTypeHeaderValue("image/png");
                form.Add(fileContent, "file", Path.GetFileName(meta.ScreenshotPath));

                var resp = await http.PostAsync(
                    $"testexecutions/{entry.ExecKey}/attachments", form);

                if (resp.IsSuccessStatusCode)
                {
                    attached++;
                    Log.Debug("[Zephyr]   Screenshot attached to {ExecKey} ({TcId}).",
                        entry.ExecKey, tcId);
                }
                else
                {
                    var json = await resp.Content.ReadAsStringAsync();
                    Log.Warning("[Zephyr] AttachScreenshot({ExecKey}) HTTP {Code}: {Body}",
                        entry.ExecKey, (int)resp.StatusCode, Truncate(json));
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[Zephyr] AttachScreenshot({ExecKey}) threw.", entry.ExecKey);
            }
        }

        if (attached > 0)
            Log.Information("[Zephyr] ✓ Attached {Count} failure screenshots.", attached);
    }

    // ── Goal 7b: Attach allure-results.zip ────────────────────────────────────

    private static async Task AttachAllureZipAsync(HttpClient http, string cycleKey)
    {
        const string allureDir = "allure-results";
        const string zipPath   = "allure-results.zip";

        try
        {
            if (!Directory.Exists(allureDir))
            {
                Log.Debug("[Zephyr] allure-results/ not found — skipping zip attachment.");
                return;
            }

            if (File.Exists(zipPath)) File.Delete(zipPath);
            ZipFile.CreateFromDirectory(allureDir, zipPath);

            using var form  = new MultipartFormDataContent();
            var zipBytes    = await File.ReadAllBytesAsync(zipPath);
            var fileContent = new ByteArrayContent(zipBytes);
            fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/zip");
            form.Add(fileContent, "file", "allure-results.zip");

            var resp = await http.PostAsync($"testcycles/{cycleKey}/attachments", form);

            if (resp.IsSuccessStatusCode)
                Log.Information("[Zephyr] ✓ Allure report zip attached to cycle {Key}.", cycleKey);
            else
            {
                var json = await resp.Content.ReadAsStringAsync();
                Log.Warning("[Zephyr] AttachAllureZip({Key}) HTTP {Code}: {Body}",
                    cycleKey, (int)resp.StatusCode, Truncate(json));
            }
        }
        catch (Exception ex)
        {
            Log.Warning("[Zephyr] AttachAllureZip skipped: {Type} — {Msg}", ex.GetType().Name, ex.Message);
        }
    }

    // ── Goal 8: Transition Jira story to Done ─────────────────────────────────

    private static async Task TransitionJiraIssueDoneAsync(
        HttpClient http, ZephyrSettings cfg, TestRunSummary summary)
    {
        var storyKey = cfg.JiraStoryKey;
        Log.Information("[Zephyr] All tests passed — transitioning Jira story {Key} to Done.", storyKey);

        try
        {
            // 1. Discover the "Done" transition ID
            var transResp = await http.GetAsync($"rest/api/3/issue/{storyKey}/transitions");
            if (!transResp.IsSuccessStatusCode)
            {
                Log.Warning("[Zephyr] GetTransitions({Key}) HTTP {Code}.",
                    storyKey, (int)transResp.StatusCode);
                return;
            }

            var transJson  = await transResp.Content.ReadAsStringAsync();
            using var tdoc = JsonDocument.Parse(transJson);
            string? doneId = null;

            foreach (var t in tdoc.RootElement.GetProperty("transitions").EnumerateArray())
            {
                var name = t.GetProperty("name").GetString() ?? "";
                if (name.Equals("Done",  StringComparison.OrdinalIgnoreCase) ||
                    name.Equals("Close", StringComparison.OrdinalIgnoreCase))
                {
                    doneId = t.GetProperty("id").GetString();
                    break;
                }
            }

            if (doneId == null)
            {
                Log.Warning("[Zephyr] No 'Done' transition found for {Key}.", storyKey);
                return;
            }

            // 2. Perform the transition
            var transBody = Serialize(new { transition = new { id = doneId } });
            var transPost = await http.PostAsync(
                $"rest/api/3/issue/{storyKey}/transitions", transBody);

            if (transPost.IsSuccessStatusCode)
                Log.Information("[Zephyr] ✓ Jira story {Key} transitioned to Done.", storyKey);
            else
            {
                var errBody = await transPost.Content.ReadAsStringAsync();
                Log.Warning("[Zephyr] Transition({Key}) HTTP {Code}: {Body}",
                    storyKey, (int)transPost.StatusCode, Truncate(errBody));
                return;
            }

            // 3. Add a result comment
            await AddJiraCommentAsync(http, cfg, summary);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[Zephyr] TransitionJiraIssueDone({Key}) threw.", storyKey);
        }
    }

    private static async Task AddJiraCommentAsync(
        HttpClient http, ZephyrSettings cfg, TestRunSummary summary)
    {
        var storyKey = cfg.JiraStoryKey;
        if (string.IsNullOrWhiteSpace(storyKey)) return;

        try
        {
            var allPassed  = summary.Failed == 0;
            var statusIcon = allPassed ? "✅" : "⚠️";
            var statusText = allPassed
                ? $"All {summary.Total} tests passed."
                : $"{summary.Passed}/{summary.Total} tests passed — {summary.Failed} failed.";

            var commentText =
                $"{statusIcon} Automated test run completed — {statusText} " +
                $"Run: {DateTime.Now:yyyy-MM-dd HH:mm}. " +
                $"Duration: {(int)summary.Duration.TotalMinutes}m {summary.Duration.Seconds}s. " +
                $"Framework: Playwright + Reqnroll + NUnit.";

            var commentBody = Serialize(new
            {
                body = new
                {
                    type    = "doc",
                    version = 1,
                    content = new[]
                    {
                        new
                        {
                            type    = "paragraph",
                            content = new[] { new { type = "text", text = commentText } }
                        }
                    }
                }
            });

            var resp = await http.PostAsync(
                $"rest/api/3/issue/{storyKey}/comment", commentBody);

            if (resp.IsSuccessStatusCode)
                Log.Information("[Zephyr] ✓ Comment added to Jira story {Key}.", storyKey);
            else
            {
                var json = await resp.Content.ReadAsStringAsync();
                Log.Warning("[Zephyr] AddComment({Key}) HTTP {Code}: {Body}",
                    storyKey, (int)resp.StatusCode, Truncate(json));
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[Zephyr] AddJiraComment({Key}) threw.", storyKey);
        }
    }

    // ── Token diagnostics ──────────────────────────────────────────────────────

    /// <summary>
    /// Decodes the JWT payload (without verifying the signature) and logs the
    /// issuer + subject so misconfigured tokens are immediately visible.
    ///
    /// The SmartBear Zephyr Scale / Essential REST API at
    /// https://api.zephyrscale.smartbear.com/v2 requires a token whose issuer
    /// is "com.kanoah.tm4j" (Zephyr Scale).
    ///
    /// If the token issuer is "com.thed.zephyr.je" (Zephyr Essential / Squad
    /// Forge app) the REST API will reject it with "Invalid JWT".
    ///
    /// FIX: Generate the token from your Jira profile menu →
    ///      "Zephyr API Access Tokens" (NOT from inside the Zephyr project app).
    /// </summary>
    private static void DiagnoseToken(string token, string apiBaseUrl)
    {
        try
        {
            var parts = token.Split('.');
            if (parts.Length < 2)
            {
                Log.Warning("[Zephyr][Token] Token does not appear to be a JWT " +
                            "(expected 3 dot-separated parts).");
                return;
            }

            // Base64url → Base64 (pad if needed)
            var payload = parts[1];
            payload = payload.Replace('-', '+').Replace('_', '/');
            var padded = payload.PadRight(payload.Length + (4 - payload.Length % 4) % 4, '=');
            var json   = Encoding.UTF8.GetString(Convert.FromBase64String(padded));

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var iss = root.TryGetProperty("iss", out var i) ? i.GetString() : "(none)";
            var sub = root.TryGetProperty("sub", out var s) ? s.GetString() : "(none)";
            var exp = root.TryGetProperty("exp", out var e)
                ? DateTimeOffset.FromUnixTimeSeconds(e.GetInt64()).ToString("yyyy-MM-dd HH:mm:ss UTC")
                : "(none)";

            Log.Information("[Zephyr][Token] Issuer : {Iss}", iss);
            Log.Information("[Zephyr][Token] Subject: {Sub}", sub);
            Log.Information("[Zephyr][Token] Expires: {Exp}", exp);
            Log.Information("[Zephyr][Token] API URL: {Url}", apiBaseUrl);

            // Warn if this looks like an internal Forge-app token rather than
            // the public REST-API token the Scale endpoint requires.
            if (iss == "com.thed.zephyr.je")
            {
                Log.Warning("[Zephyr][Token] *** TOKEN TYPE MISMATCH ***");
                Log.Warning("[Zephyr][Token] This token was issued by the Zephyr Essential " +
                            "Forge app (com.thed.zephyr.je).");
                Log.Warning("[Zephyr][Token] The Scale REST API at {Url} requires a token " +
                            "from your Jira profile menu.", apiBaseUrl);
                Log.Warning("[Zephyr][Token] HOW TO FIX:");
                Log.Warning("[Zephyr][Token]   1. In Jira, click your avatar (top-right corner)");
                Log.Warning("[Zephyr][Token]   2. Select 'Zephyr API Access Tokens'");
                Log.Warning("[Zephyr][Token]   3. Click 'Create access token' and copy it");
                Log.Warning("[Zephyr][Token]   4. Update ZephyrSettings:ZephyrJwtToken in appsettings.json");
                Log.Warning("[Zephyr][Token]   5. Re-run the tests");
            }
            else if (iss == "com.kanoah.tm4j" || iss == "com.kanoah.test-manager")
            {
                Log.Information("[Zephyr][Token] Token issuer recognised as Zephyr Scale — should work.");
            }
            else
            {
                Log.Warning("[Zephyr][Token] Unknown issuer '{Iss}' — may or may not be accepted " +
                            "by the Scale API.", iss);
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[Zephyr][Token] Could not decode JWT for diagnostics.");
        }
    }

    // ── HttpClient factories ───────────────────────────────────────────────────

    private static HttpClient BuildZephyrClient(ZephyrSettings cfg)
    {
        var baseUrl = cfg.ZephyrApiBaseUrl.TrimEnd('/') + "/";
        var client  = new HttpClient
        {
            BaseAddress = new Uri(baseUrl),
            Timeout     = TimeSpan.FromSeconds(60)
        };
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", cfg.ZephyrJwtToken);
        client.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/json"));
        return client;
    }

    private static HttpClient BuildJiraClient(ZephyrSettings cfg)
    {
        var baseUrl     = cfg.JiraBaseUrl.TrimEnd('/') + "/";
        var credentials = Convert.ToBase64String(
            Encoding.UTF8.GetBytes($"{cfg.JiraEmail}:{cfg.JiraApiToken}"));

        var client = new HttpClient
        {
            BaseAddress = new Uri(baseUrl),
            Timeout     = TimeSpan.FromSeconds(30)
        };
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Basic", credentials);
        client.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/json"));
        return client;
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private static StringContent Serialize(object obj) =>
        new(JsonSerializer.Serialize(obj, _jsonOpts), Encoding.UTF8, "application/json");

    private static string Truncate(string s) =>
        s.Length > 400 ? s[..400] + "…" : s;

    // ── Inner types ────────────────────────────────────────────────────────────

    private sealed record ExecEntry(string ExecKey, string TcKey, bool Passed);
}
