using Microsoft.Playwright;

namespace WillscotAutomation.Utilities;

public static class ScreenshotHelper
{
    public static async Task<byte[]> CaptureScreenshot(IPage page)
    {
        // Stop pending font/CDN loads so Playwright's font-ready check doesn't hang.
        try { await page.EvaluateAsync("() => window.stop()"); } catch { }
        return await page.ScreenshotAsync(new PageScreenshotOptions
        {
            FullPage = false,
            Type     = ScreenshotType.Png,
            Timeout  = 30_000
        });
    }
}
