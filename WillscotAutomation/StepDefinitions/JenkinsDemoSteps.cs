using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Playwright;
using Reqnroll;
using Serilog;
using WillscotAutomation.Config;
using WillscotAutomation.Drivers;

namespace WillscotAutomation.StepDefinitions;

[Binding]
public sealed class JenkinsDemoSteps : IDisposable
{
    private readonly IPage           _page;
    private readonly JenkinsSettings _cfg;
    private readonly HttpClient      _http;
    private int _buildNumber;

    public JenkinsDemoSteps(PlaywrightContext ctx)
    {
        _page = ctx.Page;
        _cfg  = ConfigReader.JenkinsDemoSettings;
        _http = new HttpClient { BaseAddress = new Uri(_cfg.BaseUrl) };
        var auth = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_cfg.Username}:{_cfg.ApiToken}"));
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", auth);
    }

    public void Dispose() => _http.Dispose();

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

        await _page.WaitForTimeoutAsync(2_000);
        Log.Information("[JenkinsDemo] Job page ready: {Url}", _page.Url);
    }

    [Given(@"I load the latest build without triggering a new one")]
    public async Task LoadLatestBuildWithoutTrigger()
    {
        _buildNumber = await FetchLastBuildNumberAsync();
        Log.Information("[JenkinsDemo] Recording latest build #{N} (no trigger)", _buildNumber);

        await _page.GotoAsync(
            $"{_cfg.BaseUrl}/job/{_cfg.JobName}/{_buildNumber}/",
            new PageGotoOptions { WaitUntil = WaitUntilState.Load, Timeout = 30_000 });
        await _page.WaitForTimeoutAsync(2_000);
    }

    [Given(@"I trigger a new build and wait for it to start")]
    public async Task TriggerBuildAndWaitForStart()
    {
        var preBuild = await FetchLastBuildNumberAsync();
        Log.Information("[JenkinsDemo] Last build before trigger: #{N}", preBuild);

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
            var latest = await FetchLastBuildNumberAsync();
            if (latest > preBuild)
            {
                _buildNumber = latest;
                Log.Information("[JenkinsDemo] Build #{N} started", _buildNumber);
                return;
            }
            await _page.WaitForTimeoutAsync(3_000);
        }

        _buildNumber = await FetchLastBuildNumberAsync();
        Log.Warning("[JenkinsDemo] Proceeding with build #{N}", _buildNumber);
    }

    [When(@"I navigate to the pipeline view for the running build")]
    public async Task NavigateToPipelineView()
    {
        var blueUrl =
            $"{_cfg.BaseUrl}/blue/organizations/jenkins/{_cfg.JobName}" +
            $"/detail/{_cfg.JobName}/{_buildNumber}/pipeline";

        await _page.GotoAsync(blueUrl,
            new PageGotoOptions { WaitUntil = WaitUntilState.Load, Timeout = 30_000 });

        var notFoundCount  = await _page.Locator("text=Not Found").CountAsync();
        var blueOceanFailed = !_page.Url.Contains("/blue/") || notFoundCount > 0;

        if (blueOceanFailed)
        {
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
        var timeout     = TimeSpan.FromMinutes(90);
        var started     = DateTime.UtcNow;
        var consoleUrl  = $"{_cfg.BaseUrl}/job/{_cfg.JobName}/{_buildNumber}/consoleFull";
        var pipelineUrl = $"{_cfg.BaseUrl}/job/{_cfg.JobName}/{_buildNumber}/";
        int tick = 0;

        Log.Information("[JenkinsDemo] Watching build #{N} — alternating pipeline/console every 15 s", _buildNumber);

        while (DateTime.UtcNow - started < timeout)
        {
            // Navigate immediately — show activity every cycle
            if (tick % 2 == 0)
            {
                await _page.GotoAsync(consoleUrl,
                    new PageGotoOptions { WaitUntil = WaitUntilState.Load, Timeout = 30_000 });
                // Scroll to bottom — wrapped in try-catch because the page may auto-refresh
                try { await _page.EvaluateAsync("window.scrollTo(0, document.body.scrollHeight)"); }
                catch { /* page refreshed mid-scroll — harmless */ }
                await _page.WaitForTimeoutAsync(3_000);
            }
            else
            {
                await _page.GotoAsync(pipelineUrl,
                    new PageGotoOptions { WaitUntil = WaitUntilState.Load, Timeout = 30_000 });
                await _page.WaitForTimeoutAsync(3_000);
            }
            tick++;

            // Use HttpClient so a page auto-refresh never kills the status check
            var stillBuilding = await IsBuildRunningAsync();

            if (!stillBuilding)
            {
                Log.Information("[JenkinsDemo] Build #{N} complete — elapsed {E}",
                    _buildNumber, DateTime.UtcNow - started);
                break;
            }

            Log.Information("[JenkinsDemo] Build #{N} running… elapsed {E}",
                _buildNumber, DateTime.UtcNow - started);

            await _page.WaitForTimeoutAsync(15_000);
        }

        // End on the completed pipeline stage view
        await _page.GotoAsync(pipelineUrl,
            new PageGotoOptions { WaitUntil = WaitUntilState.Load, Timeout = 30_000 });
        await _page.WaitForTimeoutAsync(3_000);
    }

    [Then(@"I scroll through each pipeline stage to highlight individual results")]
    public async Task ScrollThroughStages()
    {
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
            // Classic view — smooth scroll
            try { await _page.EvaluateAsync("window.scrollTo({ top: document.body.scrollHeight / 2, behavior: 'smooth' })"); } catch { }
            await _page.WaitForTimeoutAsync(2_000);
            try { await _page.EvaluateAsync("window.scrollTo({ top: document.body.scrollHeight, behavior: 'smooth' })"); } catch { }
            await _page.WaitForTimeoutAsync(3_000);
        }
    }

    [Then(@"I open the Allure report for the completed build and browse the results")]
    public async Task OpenAllureReport()
    {
        var allureUrl = $"{_cfg.BaseUrl}/job/{_cfg.JobName}/{_buildNumber}/allure-report/";
        Log.Information("[JenkinsDemo] Opening Allure report: {Url}", allureUrl);

        await _page.GotoAsync(allureUrl,
            new PageGotoOptions { WaitUntil = WaitUntilState.Load, Timeout = 30_000 });

        if (_page.Url.Contains("404") || await _page.Locator("h1:has-text('404')").CountAsync() > 0)
        {
            var altUrl = $"{_cfg.BaseUrl}/job/{_cfg.JobName}/{_buildNumber}/allure/";
            Log.Information("[JenkinsDemo] Trying alternative Allure URL: {Url}", altUrl);
            await _page.GotoAsync(altUrl,
                new PageGotoOptions { WaitUntil = WaitUntilState.Load, Timeout = 30_000 });
        }

        await _page.WaitForTimeoutAsync(5_000);
        try { await _page.EvaluateAsync("window.scrollTo({ top: 300, behavior: 'smooth' })"); } catch { }
        await _page.WaitForTimeoutAsync(2_000);

        try
        {
            var firstTest = _page.Locator(".test-result__info, .allure-report .test-case").First;
            if (await firstTest.IsVisibleAsync())
            {
                await firstTest.ClickAsync(new LocatorClickOptions { Timeout = 5_000 });
                await _page.WaitForTimeoutAsync(3_000);
                try { await _page.EvaluateAsync("window.scrollTo({ top: document.body.scrollHeight, behavior: 'smooth' })"); } catch { }
                await _page.WaitForTimeoutAsync(4_000);
            }
        }
        catch { /* Allure UI may differ between versions — skip */ }

        await _page.WaitForTimeoutAsync(5_000);
        Log.Information("[JenkinsDemo] Demo recording complete");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private async Task<int> FetchLastBuildNumberAsync()
    {
        try
        {
            var json = await _http.GetStringAsync($"/job/{_cfg.JobName}/api/json?tree=lastBuild[number]");
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.GetProperty("lastBuild").GetProperty("number").GetInt32();
        }
        catch { return 0; }
    }

    private async Task<bool> IsBuildRunningAsync()
    {
        try
        {
            var json = await _http.GetStringAsync(
                $"/job/{_cfg.JobName}/{_buildNumber}/api/json?tree=building,result");
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.GetProperty("building").GetBoolean();
        }
        catch { return true; }
    }
}
