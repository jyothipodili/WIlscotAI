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

    // JS-based scroll — re-queries the DOM on every call, immune to stale handle
    // errors that occur when React re-renders during lazy loading.
    public static async Task ScrollIntoView(ILocator locator)
    {
        try
        {
            await locator.EvaluateAsync(
                "el => el.scrollIntoView({ behavior: 'instant', block: 'center' })");
        }
        catch
        {
            // Element gone during re-render — the caller's visibility assertion will catch it.
        }
    }

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

    // Retry an async operation up to `retries` extra times on any exception.
    public static async Task<T> RetryAsync<T>(
        Func<Task<T>> action, int retries = 2, int delayMs = 2_000)
    {
        Exception? last = null;
        for (var attempt = 0; attempt <= retries; attempt++)
        {
            try { return await action(); }
            catch (Exception ex)
            {
                last = ex;
                if (attempt < retries)
                    await Task.Delay(delayMs);
            }
        }
        throw last!;
    }

    // GotoAsync with up to `retries` retries — handles transient navigation timeouts.
    public static async Task NavigateWithRetryAsync(
        IPage page, string url, PageGotoOptions? options = null, int retries = 2)
    {
        Exception? last = null;
        for (var attempt = 0; attempt <= retries; attempt++)
        {
            try { await page.GotoAsync(url, options); return; }
            catch (Exception ex)
            {
                last = ex;
                if (attempt < retries)
                    await page.WaitForTimeoutAsync(2_000);
            }
        }
        throw last!;
    }

    // Three-pass scroll to trigger lazy-loaded images, then return to top.
    public static async Task ScrollAndWaitForImagesAsync(IPage page, int pauseMs = 1_500)
    {
        await page.EvaluateAsync("window.scrollTo(0, document.body.scrollHeight / 3)");
        await page.WaitForTimeoutAsync(pauseMs);
        await page.EvaluateAsync("window.scrollTo(0, document.body.scrollHeight * 2 / 3)");
        await page.WaitForTimeoutAsync(pauseMs);
        await page.EvaluateAsync("window.scrollTo(0, document.body.scrollHeight)");
        await page.WaitForTimeoutAsync(pauseMs);
        await page.EvaluateAsync("window.scrollTo(0, 0)");
    }
}
