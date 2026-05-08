using Microsoft.Playwright;
using WillscotAutomation.Utilities;

namespace WillscotAutomation.Drivers;

// One instance per scenario — holds the browser, page, and log collector for that scenario.
public sealed class PlaywrightContext : IAsyncDisposable
{
    public IPlaywright    Playwright       { get; private set; } = null!;
    public IBrowser       Browser          { get; private set; } = null!;
    public IBrowserContext BrowserContext  { get; private set; } = null!;
    public IPage          Page             { get; private set; } = null!;
    public IAPIRequestContext ApiContext   { get; private set; } = null!;
    public LogCollector   LogCollector     { get; private set; } = null!;
    public string?        VideoPath        { get; private set; }

    private bool _pageContextClosed;

    public async Task InitializeAsync()
    {
        Playwright    = await Microsoft.Playwright.Playwright.CreateAsync();
        Browser       = await PlaywrightDriver.CreateBrowserAsync(Playwright);
        BrowserContext = await PlaywrightDriver.CreateContextAsync(Browser);

        BrowserContext.SetDefaultTimeout(Config.ConfigReader.DefaultTimeout);
        BrowserContext.SetDefaultNavigationTimeout(Config.ConfigReader.NavigationTimeout);

        Page = await BrowserContext.NewPageAsync();

        // Dedicated API context for HTTP status validation
        ApiContext = await Playwright.APIRequest.NewContextAsync(new APIRequestNewContextOptions
        {
            BaseURL            = Config.ConfigReader.BaseUrl,
            IgnoreHTTPSErrors  = true,
            Timeout            = Config.ConfigReader.DefaultTimeout
        });

        // Attach log collectors immediately so no events are missed
        LogCollector = new LogCollector(Page);
    }

    // Closes page + browser context, finalizing the recorded video.
    // Call this before DisposeAsync so the video path is ready while the
    // Allure test-case lifecycle is still open and can accept attachments.
    public async Task<string?> FinalizeVideoAsync()
    {
        if (_pageContextClosed) return VideoPath;

        try { if (Page?.Video != null) VideoPath = await Page.Video.PathAsync(); } catch { /* swallow */ }
        try { if (ApiContext    != null) await ApiContext.DisposeAsync();    } catch { /* swallow */ }
        try { if (Page          != null) await Page.CloseAsync();            } catch { /* swallow */ }
        // Video is finalized when the browser context closes.
        try { if (BrowserContext != null) await BrowserContext.CloseAsync(); } catch { /* swallow */ }

        _pageContextClosed = true;
        return VideoPath;
    }

    public async ValueTask DisposeAsync()
    {
        if (!_pageContextClosed)
            await FinalizeVideoAsync();

        try { if (Browser    != null) await Browser.CloseAsync(); } catch { /* swallow */ }
        try { Playwright?.Dispose();                               } catch { /* swallow */ }
    }
}
