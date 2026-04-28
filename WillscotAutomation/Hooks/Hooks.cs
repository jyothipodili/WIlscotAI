using System.Text.RegularExpressions;
using Allure.Net.Commons;
using Reqnroll;
using Reqnroll.BoDi;
using Serilog;
using WillscotAutomation.Drivers;
using WillscotAutomation.Utilities;

namespace WillscotAutomation.Hooks;

// Lifecycle order: [BeforeTestRun] → [BeforeScenario] → test → [AfterScenario] → [AfterTestRun]
[Binding]
public sealed class Hooks(IObjectContainer container, ScenarioContext scenarioContext)
{
    private static readonly Regex TcCodeRegex = new(@"\bTC-\d{3}\b", RegexOptions.IgnoreCase);

    // ── Test Run ───────────────────────────────────────────────────────────────

    [BeforeTestRun]
    public static void BeforeTestRun()
    {
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.Console(outputTemplate:
                "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
            .WriteTo.File(
                Path.Combine("logs", "test-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 7,
                outputTemplate:
                    "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
            .CreateLogger();

        Log.Information("=== WillScot Automation Test Run Started at {Time} ===", DateTime.UtcNow);
        Log.Information("Environment : {Env}",      Environment.GetEnvironmentVariable("TEST_ENV") ?? "QA");
        Log.Information("Browser     : {Browser}",  Config.ConfigReader.BrowserType);
        Log.Information("Headless    : {Headless}",  Config.ConfigReader.Headless);
        Log.Information("Base URL    : {Url}",      Config.ConfigReader.BaseUrl);

        TestRunTracker.Start();
        WriteAllureEnvironmentProperties();
    }

    [AfterTestRun]
    public static async Task AfterTestRun()
    {
        var summary = TestRunTracker.GetSummary();

        Log.Information("=== WillScot Automation Test Run Completed at {Time} ===", DateTime.UtcNow);
        Log.Information("Results — Total: {Total}  Passed: {Passed}  Failed: {Failed}  Skipped: {Skipped}  Duration: {Duration}",
            summary.Total, summary.Passed, summary.Failed, summary.Skipped,
            $"{summary.Duration.Minutes}m {summary.Duration.Seconds}s");

        // Each of these is skipped when its Enable* flag is false in config.
        await EmailService.SendTestResultEmailAsync(summary);
        await TeamsNotifier.SendNotificationAsync(summary);
        await ZephyrService.RunZephyrIntegrationAsync(summary);

        Log.CloseAndFlush();
    }

    // ── Scenario ───────────────────────────────────────────────────────────────

    [BeforeScenario(Order = 0)]
    public async Task BeforeScenario()
    {
        // NUnit increments CurrentRepeatCount on each retry attempt.
        var attempt = NUnit.Framework.TestContext.CurrentContext.CurrentRepeatCount;
        if (attempt > 0)
            Log.Warning("[RETRY #{Attempt}] {Title}", attempt, scenarioContext.ScenarioInfo.Title);
        else
            Log.Information("[SCENARIO START] {Title}", scenarioContext.ScenarioInfo.Title);

        var playwrightContext = new PlaywrightContext();
        await playwrightContext.InitializeAsync();
        container.RegisterInstanceAs(playwrightContext);

        SetPerTestAllureLabels();
    }

    [AfterScenario(Order = 0)]
    public async Task AfterScenario()
    {
        PlaywrightContext? playwrightContext;

        try
        {
            playwrightContext = container.Resolve<PlaywrightContext>();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Could not resolve PlaywrightContext during AfterScenario.");
            return;
        }

        if (scenarioContext.TestError != null)
        {
            Log.Error("[SCENARIO FAILED] {Title} — {Error}",
                scenarioContext.ScenarioInfo.Title,
                scenarioContext.TestError.Message);

            try
            {
                await AllureHelper.AttachFailureBundle(
                    playwrightContext.Page,
                    playwrightContext.LogCollector,
                    scenarioContext.ScenarioInfo.Title);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to attach Allure artefacts.");
            }
        }
        else
        {
            Log.Information("[SCENARIO PASSED] {Title}", scenarioContext.ScenarioInfo.Title);

            // Attach a pass screenshot so every Allure result has a final page state image.
            try
            {
                var screenshot = await ScreenshotHelper.CaptureScreenshot(playwrightContext.Page);
                AllureHelper.AttachScreenshot(screenshot, $"PASS — {scenarioContext.ScenarioInfo.Title}");
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Could not capture pass screenshot for {Title}.",
                    scenarioContext.ScenarioInfo.Title);
            }
        }

        if (scenarioContext.ScenarioExecutionStatus == ScenarioExecutionStatus.Skipped)
            TestRunTracker.RecordSkipped();
        else
            TestRunTracker.RecordScenario(scenarioContext.ScenarioInfo.Title, scenarioContext.TestError == null);

        await TrackZephyrScenarioAsync(playwrightContext);
        await playwrightContext.DisposeAsync();
    }

    // ── Private helpers ────────────────────────────────────────────────────────

    private static void WriteAllureEnvironmentProperties()
    {
        try
        {
            const string resultsDir = "allure-results";
            Directory.CreateDirectory(resultsDir);

            var env     = Environment.GetEnvironmentVariable("TEST_ENV") ?? "QA";
            var browser = Config.ConfigReader.BrowserType;

            var lines = new[]
            {
                $"Project=WillScot Homepage Automation",
                $"Environment={env}",
                $"Browser={char.ToUpper(browser[0]) + browser[1..]}",
                $"Headless={Config.ConfigReader.Headless}",
                $"Base URL={Config.ConfigReader.BaseUrl}",
                $"Platform=Windows 10",
                $"Language=C# .NET 8",
                $"Framework=Playwright 1.44 + Reqnroll 2.2 + NUnit 4",
                $"Reporter=Allure 2.35",
                $"Retry On Failure=1 retry (2 total attempts)",
                $"Executed At={DateTime.Now:yyyy-MM-dd HH:mm:ss}",
            };

            File.WriteAllLines(Path.Combine(resultsDir, "environment.properties"), lines);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to write Allure environment.properties.");
        }
    }

    private async Task TrackZephyrScenarioAsync(PlaywrightContext playwrightContext)
    {
        var title = scenarioContext.ScenarioInfo.Title;
        string? screenshotPath = null;

        if (scenarioContext.TestError != null)
        {
            try
            {
                const string dir = "zephyr-attachments";
                Directory.CreateDirectory(dir);

                var tcMatch = TcCodeRegex.Match(title);
                var fileName = tcMatch.Success
                    ? $"{tcMatch.Value.ToUpper()}.png"
                    : $"FAIL-{Guid.NewGuid():N}.png";
                screenshotPath = Path.Combine(dir, fileName);

                await playwrightContext.Page.ScreenshotAsync(
                    new Microsoft.Playwright.PageScreenshotOptions { FullPage = true, Path = screenshotPath });
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "[Zephyr] Could not save failure screenshot for \"{Title}\".", title);
                screenshotPath = null;
            }
        }

        ZephyrAttachmentTracker.Track(title, screenshotPath);
    }

    private void SetPerTestAllureLabels()
    {
        try
        {
            var title = scenarioContext.ScenarioInfo.Title;
            var tags  = scenarioContext.ScenarioInfo.Tags ?? [];

            AllureLifecycle.Instance.UpdateTestCase(result =>
            {
                result.labels.Add(Label.Story(title));

                var severity = tags.Contains("smoke") || tags.Contains("performance")
                    ? SeverityLevel.critical
                    : tags.Contains("quality")
                        ? SeverityLevel.minor
                        : SeverityLevel.normal;
                result.labels.Add(Label.Severity(severity));

                foreach (var tag in tags)
                    result.labels.Add(Label.Tag(tag));

                result.name = title;
            });
        }
        catch
        {
            // AllureLifecycle async-local context may not be active in some edge cases.
        }
    }
}
