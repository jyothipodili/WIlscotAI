using Microsoft.Playwright;

namespace WillscotAutomation.Utilities;

public static class ScreenshotHelper
{
    public static async Task<byte[]> CaptureScreenshot(IPage page)
        => await page.ScreenshotAsync(new PageScreenshotOptions
        {
            FullPage = false,
            Type     = ScreenshotType.Png,
            Timeout  = 10_000   // cap at 10 s so teardown doesn't hang on slow font loads
        });
}
