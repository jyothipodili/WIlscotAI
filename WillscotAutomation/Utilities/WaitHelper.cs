using Microsoft.Playwright;
using WillscotAutomation.Config;

namespace WillscotAutomation.Utilities;

public static class WaitHelper
{
    public static async Task WaitForVisible(ILocator locator, int? timeoutMs = null)
        => await locator.WaitForAsync(new LocatorWaitForOptions
        {
            State   = WaitForSelectorState.Visible,
            Timeout = timeoutMs ?? ConfigReader.DefaultTimeout
        });

    public static async Task WaitForHidden(ILocator locator, int? timeoutMs = null)
        => await locator.WaitForAsync(new LocatorWaitForOptions
        {
            State   = WaitForSelectorState.Hidden,
            Timeout = timeoutMs ?? ConfigReader.DefaultTimeout
        });

    public static async Task<bool> IsVisible(ILocator locator)
        => await locator.IsVisibleAsync();

    public static async Task Click(ILocator locator)
    {
        await locator.ScrollIntoViewIfNeededAsync();
        await locator.ClickAsync();
    }

    public static async Task ScrollIntoView(ILocator locator)
        => await locator.ScrollIntoViewIfNeededAsync();

    public static async Task<string> GetText(ILocator locator)
        => (await locator.InnerTextAsync()).Trim();

    // Uses DOMContentLoaded instead of NetworkIdle — the live site has long-running
    // ad/analytics requests that would block NetworkIdle indefinitely.
    public static async Task WaitForNetworkIdle(IPage page, int? timeoutMs = null)
    {
        try
        {
            await page.WaitForLoadStateAsync(LoadState.DOMContentLoaded,
                new PageWaitForLoadStateOptions { Timeout = timeoutMs ?? 15_000 });
        }
        catch { /* best-effort */ }

        await page.WaitForTimeoutAsync(1500);
    }
}
