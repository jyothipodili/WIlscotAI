using Microsoft.Playwright;
using Reqnroll;
using Serilog;
using WillscotAutomation.Config;
using WillscotAutomation.Drivers;

namespace WillscotAutomation.StepDefinitions;

[Binding]
public sealed class JenkinsDemoSteps(PlaywrightContext ctx)
{
    private readonly IPage           _page = ctx.Page;
    private readonly JenkinsSettings _cfg  = ConfigReader.JenkinsDemoSettings;
    private int _buildNumber;

    // ── Steps ─────────────────────────────────────────────────────────────────

    [Given(@"I open Jenkins job ""(.*)"" and authenticate if prompted")]
    public async Task OpenJenkinsJob(string jobName)
    {
        await _page.GotoAsync(
            $"{_cfg.BaseUrl}/job/{jobName}/",
            new PageGotoOptions { WaitUntil = WaitUntilState.Load, Timeout = 30_000 });

        if (_page.Url.Contains("login"))
        {
            Log.Information("[JenkinsDemo] Logging in as {User}", _cfg.Username);
            await _page.FillAsync("#j_username", _cfg.Username);
            await _page.FillAsync("input[name='j_password']", _cfg.ApiToken);
            await _page.ClickAsync("[name='Submit']", new PageClickOptions { Timeout = 10_000 });
            await _page.WaitForURLAsync("**/job/**", new PageWaitForURLOptions { Timeout = 30_000 });
        }

        // Pause so the stage-history view is fully visible in the recording
        await _page.WaitForTimeoutAsync(2_000);
        Log.Information("[JenkinsDemo] Job page ready: {Url}", _page.Url);
    }

    [Given(@"I trigger a new build and wait for it to start")]
    public async Task TriggerBuildAndWaitForStart()
    {
        var apiUrl = $"{_cfg.BaseUrl}/job/{_cfg.JobName}/api/json?tree=lastBuild[number]";
        var preBuild = await FetchLastBuildNumberAsync(apiUrl);
        Log.Information("[JenkinsDemo] Last build before trigger: #{N}", preBuild);

        // Try every known Jenkins selector for the Build Now sidebar link
        var triggered = false;
        foreach (var sel in new[]
        {
            "a[href*='buildWithParameters']",
            "a[href*='build?delay']",
            "a.task-link:has-text('Build Now')",
            "//a[contains(.,'Build Now')]"
        })
        {
            try
            {
                var loc = sel.StartsWith("//")
                    ? _page.Locator($"xpath={sel}")
                    : _page.Locator(sel);

                if (await loc.CountAsync() > 0)
                {
                    await loc.First.ClickAsync(new LocatorClickOptions { Timeout = 5_000 });
                    triggered = true;
                    Log.Information("[JenkinsDemo] Build Now clicked via selector: {Sel}", sel);
                    break;
                }
            }
            catch { /* try next selector */ }
        }

        if (!triggered)
            throw new InvalidOperationException(
                "[JenkinsDemo] Could not find Build Now button. " +
                "Check Jenkins is accessible and the job name is correct.");

        await _page.WaitForTimeoutAsync(3_000);

        // Poll up to 90 s for the new build to appear
        var deadline = DateTime.UtcNow.AddSeconds(90);
        while (DateTime.UtcNow < deadline)
        {
            var latest = await FetchLastBuildNumberAsync(apiUrl);
            if (latest > preBuild)
            {
                _buildNumber = latest;
                Log.Information("[JenkinsDemo] Build #{N} started", _buildNumber);
                return;
            }
            await _page.WaitForTimeoutAsync(3_000);
        }

        _buildNumber = await FetchLastBuildNumberAsync(apiUrl);
        Log.Warning("[JenkinsDemo] Proceeding with build #{N}", _buildNumber);
    }

    [When(@"I navigate to the pipeline view for the running build")]
    public async Task NavigateToPipelineView()
    {
        // Try Blue Ocean pipeline view (animated stage graph)
        var blueUrl =
            $"{_cfg.BaseUrl}/blue/organizations/jenkins/{_cfg.JobName}" +
            $"/detail/{_cfg.JobName}/{_buildNumber}/pipeline";

        await _page.GotoAsync(blueUrl,
            new PageGotoOptions { WaitUntil = WaitUntilState.Load, Timeout = 30_000 });

        // Blue Ocean serves a "Oops! Not Found" error page (with the /blue/ URL intact)
        // when it is not installed. Detect the error page by its content, not just the URL.
        var notFoundCount = await _page.Locator("text=Not Found").CountAsync();
        var blueOceanFailed = !_page.Url.Contains("/blue/") || notFoundCount > 0;

        if (blueOceanFailed)
        {
            // Blue Ocean not installed — fall back to classic stage view
            Log.Information("[JenkinsDemo] Blue Ocean unavailable, using classic stage view");
            await _page.GotoAsync(
                $"{_cfg.BaseUrl}/job/{_cfg.JobName}/{_buildNumber}/",
                new PageGotoOptions { WaitUntil = WaitUntilState.Load, Timeout = 30_000 });
        }

        await _page.WaitForTimeoutAsync(2_000);
        Log.Information("[JenkinsDemo] Recording build view: {Url}", _page.Url);
    }

    [Then(@"I record the pipeline progress until all stages complete or timeout after 90 minutes")]
    public async Task RecordUntilComplete()
    {
        var timeout    = TimeSpan.FromMinutes(90);
        var started    = DateTime.UtcNow;
        var statusUrl  = $"{_cfg.BaseUrl}/job/{_cfg.JobName}/{_buildNumber}/api/json?tree=building,result";
        var consoleUrl = $"{_cfg.BaseUrl}/job/{_cfg.JobName}/{_buildNumber}/consoleFull";
        var pipelineUrl = $"{_cfg.BaseUrl}/job/{_cfg.JobName}/{_buildNumber}/";
        int tick = 0;

        Log.Information("[JenkinsDemo] Watching build #{N} — alternating pipeline/console every 30 s", _buildNumber);

        while (DateTime.UtcNow - started < timeout)
        {
            var stillBuilding = await _page.EvaluateAsync<bool>(@"async (url) => {
                try {
                    const r = await fetch(url, { credentials: 'include' });
                    const j = await r.json();
                    return !!j.building;
                } catch { return true; }
            }", statusUrl);

            if (!stillBuilding)
            {
                Log.Information("[JenkinsDemo] Build #{N} complete — elapsed {E}",
                    _buildNumber, DateTime.UtcNow - started);
                break;
            }

            Log.Information("[JenkinsDemo] Build #{N} running… elapsed {E}",
                _buildNumber, DateTime.UtcNow - started);

            await _page.WaitForTimeoutAsync(30_000);

            // Alternate: every 2nd tick show the live console log (test output), otherwise pipeline view
            if (tick % 2 == 0)
            {
                await _page.GotoAsync(consoleUrl,
                    new PageGotoOptions { WaitUntil = WaitUntilState.Load, Timeout = 30_000 });
                // Scroll to bottom so latest test output is visible
                await _page.EvaluateAsync("window.scrollTo(0, document.body.scrollHeight)");
                await _page.WaitForTimeoutAsync(4_000);
            }
            else
            {
                await _page.GotoAsync(pipelineUrl,
                    new PageGotoOptions { WaitUntil = WaitUntilState.Load, Timeout = 30_000 });
            }
            tick++;
        }

        // End on the completed pipeline stage view
        await _page.GotoAsync(pipelineUrl,
            new PageGotoOptions { WaitUntil = WaitUntilState.Load, Timeout = 30_000 });
        await _page.WaitForTimeoutAsync(3_000);
    }

    [Then(@"I scroll through each pipeline stage to highlight individual results")]
    public async Task ScrollThroughStages()
    {
        // Blue Ocean: click each stage node to expand its log panel
        var stageNodes = _page.Locator(
            "[class*='PipelineGraph'] button, [class*='pipeline-node'] button");

        int count;
        try { count = await stageNodes.CountAsync(); } catch { count = 0; }

        if (count > 0)
        {
            Log.Information("[JenkinsDemo] Highlighting {N} Blue Ocean stage nodes", count);
            for (var i = 0; i < count; i++)
            {
                try
                {
                    await stageNodes.Nth(i).ScrollIntoViewIfNeededAsync();
                    await stageNodes.Nth(i).ClickAsync(new LocatorClickOptions { Timeout = 5_000 });
                    await _page.WaitForTimeoutAsync(2_500);
                }
                catch { /* stage may be loading — skip */ }
            }
        }
        else
        {
            // Classic view — smooth scroll to show all stages
            await _page.EvaluateAsync(
                "window.scrollTo({ top: document.body.scrollHeight / 2, behavior: 'smooth' })");
            await _page.WaitForTimeoutAsync(2_000);
            await _page.EvaluateAsync(
                "window.scrollTo({ top: document.body.scrollHeight, behavior: 'smooth' })");
            await _page.WaitForTimeoutAsync(3_000);
        }
    }

    [Then(@"I open the Allure report for the completed build and browse the results")]
    public async Task OpenAllureReport()
    {
        // Jenkins Allure plugin publishes the report under /allure-report/ or /allure/
        var allureUrl = $"{_cfg.BaseUrl}/job/{_cfg.JobName}/{_buildNumber}/allure-report/";
        Log.Information("[JenkinsDemo] Opening Allure report: {Url}", allureUrl);

        await _page.GotoAsync(allureUrl,
            new PageGotoOptions { WaitUntil = WaitUntilState.Load, Timeout = 30_000 });

        // If the plugin uses a different path, try the alternative
        if (_page.Url.Contains("404") || await _page.Locator("h1:has-text('404')").CountAsync() > 0)
        {
            var altUrl = $"{_cfg.BaseUrl}/job/{_cfg.JobName}/{_buildNumber}/allure/";
            Log.Information("[JenkinsDemo] Trying alternative Allure URL: {Url}", altUrl);
            await _page.GotoAsync(altUrl,
                new PageGotoOptions { WaitUntil = WaitUntilState.Load, Timeout = 30_000 });
        }

        // Wait for the Allure SPA to render
        await _page.WaitForTimeoutAsync(5_000);

        // Show the summary section
        await _page.EvaluateAsync(
            "window.scrollTo({ top: 300, behavior: 'smooth' })");
        await _page.WaitForTimeoutAsync(2_000);

        // Try clicking on the first test case to show its detail (screenshot / video)
        try
        {
            var firstTest = _page.Locator(".test-result__info, .allure-report .test-case").First;
            if (await firstTest.IsVisibleAsync())
            {
                await firstTest.ClickAsync(new LocatorClickOptions { Timeout = 5_000 });
                await _page.WaitForTimeoutAsync(3_000);
                // Scroll down to show the attached video / screenshot
                await _page.EvaluateAsync(
                    "window.scrollTo({ top: document.body.scrollHeight, behavior: 'smooth' })");
                await _page.WaitForTimeoutAsync(4_000);
            }
        }
        catch { /* Allure UI may differ between versions — skip */ }

        // Final pause so the last frame of the recording is clearly the Allure report
        await _page.WaitForTimeoutAsync(5_000);
        Log.Information("[JenkinsDemo] Demo recording complete");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private async Task<int> FetchLastBuildNumberAsync(string apiUrl)
    {
        try
        {
            return await _page.EvaluateAsync<int>(@"async (url) => {
                try {
                    const r = await fetch(url, { credentials: 'include' });
                    const j = await r.json();
                    return j.lastBuild?.number ?? 0;
                } catch { return 0; }
            }", apiUrl);
        }
        catch { return 0; }
    }
}
