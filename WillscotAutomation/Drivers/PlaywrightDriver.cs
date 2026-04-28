using Microsoft.Playwright;
using WillscotAutomation.Config;

namespace WillscotAutomation.Drivers;

// Launches browsers and creates contexts configured for testing willscot.com.
public static class PlaywrightDriver
{
    public static async Task<IBrowser> CreateBrowserAsync(IPlaywright playwright)
    {
        var args = new List<string>
        {
            "--disable-dev-shm-usage",
            "--no-sandbox",
            "--disable-setuid-sandbox",
            // Stealth: prevent WAF bot-detection fingerprinting headless Chrome
            "--disable-blink-features=AutomationControlled",
            "--disable-infobars",
            "--window-size=1920,1080",
            "--disable-extensions"
        };

        // --disable-gpu is required in headless/Docker (no display adapter).
        // In headed/UI mode on Windows it forces software rendering → blank browser windows.
        if (ConfigReader.Headless)
            args.Add("--disable-gpu");

        var launchOptions = new BrowserTypeLaunchOptions
        {
            //Headless = ConfigReader.Headless,
            Headless = false, // Force headless mode to avoid blank headed browsers on Windows
            //SlowMo  = ConfigReader.SlowMo,
            SlowMo = 200,
            Args    = args
        };

        return ConfigReader.BrowserType.ToLowerInvariant() switch
        {
            "firefox" => await playwright.Firefox.LaunchAsync(launchOptions),
            "webkit"  => await playwright.Webkit.LaunchAsync(launchOptions),
            _         => await playwright.Chromium.LaunchAsync(launchOptions)
        };
    }

    public static async Task<IBrowserContext> CreateContextAsync(IBrowser browser)
    {
        var context = await browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize
            {
                Width  = ConfigReader.ViewportWidth,
                Height = ConfigReader.ViewportHeight
            },
            IgnoreHTTPSErrors  = true,
            AcceptDownloads    = false,
            JavaScriptEnabled  = true,
            Locale             = "en-US",
            TimezoneId         = "America/New_York",
            // Real Chrome 120 User-Agent — avoids HeadlessChrome fingerprint
            UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) " +
                        "AppleWebKit/537.36 (KHTML, like Gecko) " +
                        "Chrome/120.0.0.0 Safari/537.36",
            ExtraHTTPHeaders = new Dictionary<string, string>
            {
                ["Accept-Language"] = "en-US,en;q=0.9",
                ["Accept-Encoding"] = "gzip, deflate, br",
                ["Accept"]          = "text/html,application/xhtml+xml,application/xml;q=0.9,image/avif,image/webp,*/*;q=0.8"
            }
        });

        // Override navigator.webdriver = false to prevent JS-based bot detection
        await context.AddInitScriptAsync(@"
            Object.defineProperty(navigator, 'webdriver', {
                get: () => undefined
            });
            Object.defineProperty(navigator, 'plugins', {
                get: () => [1, 2, 3, 4, 5]
            });
            Object.defineProperty(navigator, 'languages', {
                get: () => ['en-US', 'en']
            });
            window.chrome = { runtime: {} };
        ");

        return context;
    }
}
