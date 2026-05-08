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

        // If Jenkins security is enabled it redirects to /login
        if (_page.Url.Contains("login"))
        {
            Log.Information("[JenkinsDemo] Login required — authenticating as {User}", _cfg.Username);
            await _page.FillAsync("#j_username", _cfg.Username);
            await _page.FillAsync("input[name='j_password']", _cfg.ApiToken);
            await _page.ClickAsync("[name='Submit']", new LocatorClickOptions { Timeout = 10_000 });
            await _page.WaitForURLAsync("**/job/**", new PageWaitForURLOptions { Timeout = 30_000 });
        }

        Log.Information("[JenkinsDemo] Jenkins job page loaded: {Url}", _page.Url);
    }

    [Given(@"I trigger a new build and wait for it to start")]
    public async Task TriggerBuildAndWaitForStart()
    {
        var apiUrl = $"{_cfg.BaseUrl}/job/{_cfg.JobName}/api/json?tree=lastBuild[number]";

        // Snapshot build number before triggering so we can detect the new one
        var preBuild = await FetchLastBuildNumberAsync(apiUrl);
        Log.Information("[JenkinsDemo] Last build before trigger: #{N}", preBuild);

        // Click the Build Now link (browser session handles CSRF automatically)
        await _page.ClickAsync("a[href*='build?']", new LocatorClickOptions { Timeout = 10_000 });
        await _page.WaitForTimeoutAsync(2_000);

        // Poll up to 60 s for the new build to appear
        var deadline = DateTime.UtcNow.AddSeconds(60);
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

        // Fallback in case the build was already incremented before we polled
        _buildNumber = await FetchLastBuildNumberAsync(apiUrl);
        Log.Warning("[JenkinsDemo] Using build #{N} after trigger", _buildNumber);
    }

    [When(@"I navigate to the pipeline view for the running build")]
    public async Task NavigateToPipelineView()
    {
        // Try Blue Ocean pipeline view first (richer stage graph)
        var blueOceanUrl =
            $"{_cfg.BaseUrl}/blue/organizations/jenkins/{_cfg.JobName}" +
            $"/detail/{_cfg.JobName}/{_buildNumber}/pipeline";

        await _page.GotoAsync(blueOceanUrl,
            new PageGotoOptions { WaitUntil = WaitUntilState.Load, Timeout = 30_000 });

        // Fall back to classic build page if Blue Ocean plugin is not installed
        if (!_page.Url.Contains("/blue/"))
        {
            Log.Information("[JenkinsDemo] Blue Ocean unavailable — falling back to classic view");
            await _page.GotoAsync(
                $"{_cfg.BaseUrl}/job/{_cfg.JobName}/{_buildNumber}/",
                new PageGotoOptions { WaitUntil = WaitUntilState.Load, Timeout = 30_000 });
        }

        Log.Information("[JenkinsDemo] Recording pipeline view: {Url}", _page.Url);
    }

    [Then(@"I record the pipeline progress until all stages complete or timeout after 90 minutes")]
    public async Task RecordUntilComplete()
    {
        var timeout  = TimeSpan.FromMinutes(90);
        var started  = DateTime.UtcNow;
        var pollMs   = 30_000;
        var statusUrl = $"{_cfg.BaseUrl}/job/{_cfg.JobName}/{_buildNumber}/api/json?tree=building,result";

        Log.Information("[JenkinsDemo] Watching build #{N} — polling every 30 s, max 90 min", _buildNumber);

        while (DateTime.UtcNow - started < timeout)
        {
            // Use browser fetch so Jenkins session cookies are included automatically
            var stillBuilding = await _page.EvaluateAsync<bool>(@"async (url) => {
                try {
                    const r = await fetch(url, { credentials: 'include' });
                    const j = await r.json();
                    return !!j.building;
                } catch { return true; }
            }", statusUrl);

            if (!stillBuilding)
            {
                Log.Information("[JenkinsDemo] Build #{N} complete — elapsed {Elapsed}",
                    _buildNumber, DateTime.UtcNow - started);
                break;
            }

            Log.Information("[JenkinsDemo] Still running… elapsed {Elapsed}", DateTime.UtcNow - started);
            await _page.WaitForTimeoutAsync(pollMs);
            await _page.ReloadAsync(new PageReloadOptions { WaitUntil = WaitUntilState.Load });
        }

        // Final reload so the recording ends on the completed pipeline view
        await _page.ReloadAsync(new PageReloadOptions { WaitUntil = WaitUntilState.Load });
        await _page.WaitForTimeoutAsync(3_000);
    }

    [Then(@"I scroll through each pipeline stage to highlight individual results")]
    public async Task ScrollThroughStages()
    {
        // Blue Ocean: each stage is a clickable node button that expands its log panel
        var stageButtons = _page.Locator(
            "[class*='PipelineGraph'] button, [class*='pipeline-node'] button");

        int count;
        try { count = await stageButtons.CountAsync(); }
        catch { count = 0; }

        if (count > 0)
        {
            Log.Information("[JenkinsDemo] Clicking {N} stage nodes in Blue Ocean", count);
            for (var i = 0; i < count; i++)
            {
                try
                {
                    await stageButtons.Nth(i).ScrollIntoViewIfNeededAsync();
                    await stageButtons.Nth(i).ClickAsync(new LocatorClickOptions { Timeout = 5_000 });
                    await _page.WaitForTimeoutAsync(2_500);
                }
                catch { /* stage button may be disabled or loading — skip */ }
            }
        }
        else
        {
            // Classic Jenkins view — scroll through the full build result page
            Log.Information("[JenkinsDemo] Classic view — scrolling through build result page");
            await _page.EvaluateAsync(
                "window.scrollTo({ top: document.body.scrollHeight / 2, behavior: 'smooth' })");
            await _page.WaitForTimeoutAsync(2_000);
            await _page.EvaluateAsync(
                "window.scrollTo({ top: document.body.scrollHeight, behavior: 'smooth' })");
            await _page.WaitForTimeoutAsync(3_000);
        }
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
