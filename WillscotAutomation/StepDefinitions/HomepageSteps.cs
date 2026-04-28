using Microsoft.Playwright;
using NUnit.Framework;
using Reqnroll;
using WillscotAutomation.Config;
using WillscotAutomation.Drivers;
using WillscotAutomation.PageObjects;
using WillscotAutomation.Utilities;

namespace WillscotAutomation.StepDefinitions;

[Binding]
public sealed class HomepageSteps
{
    private readonly PlaywrightContext _ctx;
    private readonly HomePage          _homePage;

    // Stored during Background navigation so TC-001 can read elapsed time.
    private DateTime _navigationStartUtc;

    public HomepageSteps(PlaywrightContext ctx)
    {
        _ctx      = ctx;
        _homePage = new HomePage(ctx.Page);
    }

    // ── Background ─────────────────────────────────────────────────────────────

    [Given(@"I am on the WillScot homepage")]
    public async Task GivenIAmOnTheWillScotHomepage()
    {
        _navigationStartUtc = DateTime.UtcNow;

        // Use DOMContentLoaded — the live site has long-running background requests
        // that prevent NetworkIdle from completing within a reasonable timeout.
        await _ctx.Page.GotoAsync(ConfigReader.BaseUrl, new PageGotoOptions
        {
            WaitUntil = WaitUntilState.DOMContentLoaded,
            Timeout   = ConfigReader.NavigationTimeout
        });

        // Wait for the h1 hero headline to be visible — signals JS hydration is done.
        await _ctx.Page.WaitForSelectorAsync("h1",
            new PageWaitForSelectorOptions { State = WaitForSelectorState.Visible, Timeout = ConfigReader.DefaultTimeout });
    }

    // ── TC-001 ─────────────────────────────────────────────────────────────────

    [Then(@"the page should have loaded within 4 seconds")]
    public async Task ThenThePageShouldHaveLoadedWithin4Seconds()
    {
        // Use the browser's Navigation Timing API for precision.
        // loadEventEnd − navigationStart gives the total load time in ms.
        var loadTimeMs = await _ctx.Page.EvaluateAsync<double>(
            "() => {"                                                                           +
            "  const t = window.performance.timing;"                                           +
            "  return t.loadEventEnd > 0 ? t.loadEventEnd - t.navigationStart : -1;"          +
            "}");

        var threshold = ConfigReader.PageLoadThresholdMs;

        if (loadTimeMs < 0)
        {
            // Timing API not ready; fall back to wall-clock measurement.
            var elapsed = (DateTime.UtcNow - _navigationStartUtc).TotalMilliseconds;
            Assert.That(elapsed, Is.LessThanOrEqualTo(threshold),
                $"Page wall-clock load time was {elapsed:F0} ms — expected ≤ {threshold} ms.");
        }
        else
        {
            Assert.That(loadTimeMs, Is.LessThanOrEqualTo(threshold),
                $"Page load time was {loadTimeMs:F0} ms — expected ≤ {threshold} ms.");
        }
    }

    // ── TC-002 ─────────────────────────────────────────────────────────────────

    [Then(@"the hero banner should display the headline ""(.*)""")]
    public async Task ThenTheHeroBannerShouldDisplayTheHeadline(string expectedHeadline)
    {
        await WaitHelper.WaitForVisible(_homePage.HeroBannerHeadline, 10_000);
        var actualText = await WaitHelper.GetText(_homePage.HeroBannerHeadline);

        Assert.That(actualText, Does.Contain(expectedHeadline),
            $"Hero banner headline mismatch. Expected to contain: '{expectedHeadline}'. " +
            $"Actual: '{actualText}'");
    }

    // ── TC-003 ─────────────────────────────────────────────────────────────────

    [Then(@"the ""(.*)"" CTA button should be visible")]
    public async Task ThenTheCtaButtonShouldBeVisible(string buttonLabel)
    {
        var cta = ResolveCta(buttonLabel);
        await WaitHelper.WaitForVisible(cta, 10_000);
        Assert.That(await cta.IsVisibleAsync(), Is.True,
            $"CTA button '{buttonLabel}' is not visible.");
    }

    [Then(@"the ""(.*)"" CTA button should be enabled")]
    public async Task ThenTheCtaButtonShouldBeEnabled(string buttonLabel)
    {
        var cta = ResolveCta(buttonLabel);
        await WaitHelper.WaitForVisible(cta, 10_000);
        Assert.That(await cta.IsEnabledAsync(), Is.True,
            $"CTA button '{buttonLabel}' is not enabled (clickable).");
    }

    // ── TC-004 ─────────────────────────────────────────────────────────────────

    [Then(@"there should be no broken images on the page")]
    public async Task ThenThereShouldBeNoBrokenImages()
    {
        // Scroll to trigger lazy loading before checking
        await _ctx.Page.EvaluateAsync("window.scrollTo(0, document.body.scrollHeight / 2)");
        await _ctx.Page.WaitForTimeoutAsync(1_500);

        // Use browser-side naturalWidth check — this tests what the user actually
        // sees and avoids false positives from Next.js /_next/image proxy URLs or
        // CDN redirects that return non-200 from an API context but load fine in
        // the real browser environment.
        // SVG files are excluded because they are scalable and always report
        // naturalWidth === 0 regardless of whether they loaded successfully.
        var brokenSrcs = await _ctx.Page.EvaluateAsync<string[]>(
            @"() => {
                const imgs = document.querySelectorAll('img[src]');
                return Array.from(imgs)
                    .filter(img => img.complete && img.naturalWidth === 0)
                    .map(img => img.src)
                    .filter(src => src &&
                                   !src.startsWith('data:') &&
                                   !src.startsWith('blob:') &&
                                   !src.toLowerCase().endsWith('.svg') &&
                                   (src.startsWith('/') ||
                                    src.startsWith('https://www.willscot.com') ||
                                    src.startsWith('https://willscot.com')));
            }");

        Assert.That(brokenSrcs, Is.Empty,
            $"Broken images found (failed to load in browser):\n  {string.Join("\n  ", brokenSrcs)}");
    }

    [Then(@"there should be no browser console errors")]
    public void ThenThereShouldBeNoBrowserConsoleErrors()
    {
        Assert.That(_ctx.LogCollector.ConsoleErrors, Is.Empty,
            "Browser console errors detected:\n" +
            _ctx.LogCollector.FormatConsoleErrors());
    }

    [Then(@"there should be no uncaught JavaScript exceptions")]
    public void ThenThereShouldBeNoUncaughtJavaScriptExceptions()
    {
        Assert.That(_ctx.LogCollector.JsExceptions, Is.Empty,
            "Uncaught JavaScript exceptions detected:\n" +
            _ctx.LogCollector.FormatJsExceptions());
    }

    // ── TC-008 ─────────────────────────────────────────────────────────────────

    [Then(@"the page title should contain ""(.*)""")]
    public async Task ThenThePageTitleShouldContain(string expectedText)
    {
        var title = await _ctx.Page.TitleAsync();
        Assert.That(title, Does.Contain(expectedText),
            $"Page title '{title}' does not contain expected text '{expectedText}'.");
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private ILocator ResolveCta(string label) => label.ToLowerInvariant() switch
    {
        "learn more" => _homePage.LearnMoreCta,
        _ => _ctx.Page.Locator(
                 $"a:has-text('{label}'), button:has-text('{label}')").First
    };
}
