using Microsoft.Playwright;
using NUnit.Framework;
using Reqnroll;
using WillscotAutomation.Drivers;
using WillscotAutomation.PageObjects;
using WillscotAutomation.PageObjects.Components;
using WillscotAutomation.Utilities;

namespace WillscotAutomation.StepDefinitions;

[Binding]
public sealed class NavigationSteps
{
    private readonly PlaywrightContext _ctx;
    private readonly HomePage          _homePage;

    public NavigationSteps(PlaywrightContext ctx)
    {
        _ctx      = ctx;
        _homePage = new HomePage(ctx.Page);
    }

    // ── TC-005  All nav items visible ──────────────────────────────────────────

    [Then(@"the following navigation items should be visible")]
    public async Task ThenTheFollowingNavigationItemsShouldBeVisible(Table table)
    {
        var failures = new List<string>();

        foreach (var row in table.Rows)
        {
            var label   = row["NavItem"];
            var locator = _homePage.Navigation.GetNavItem(label);

            try
            {
                await WaitHelper.WaitForVisible(locator, 8_000);
                var visible = await locator.IsVisibleAsync();
                if (!visible) failures.Add(label);
            }
            catch
            {
                failures.Add(label);
            }
        }

        Assert.That(failures, Is.Empty,
            $"The following navigation items were NOT visible:\n  {string.Join("\n  ", failures)}");
    }

    // ── TC-006 / TC-007  Nav item click ───────────────────────────────────────

    [When(@"I click the ""(.*)"" navigation item")]
    public async Task WhenIClickTheNavigationItem(string label)
    {
        // Resolve using the direct href link first (most reliable — bypasses
        // dropdown-toggle buttons that share the same label text).
        var locatorToClick = ResolveNavLinkByLabel(label)
            ?? _homePage.Navigation.GetNavItem(label);

        await WaitHelper.WaitForVisible(locatorToClick, 8_000);
        await WaitHelper.ScrollIntoView(locatorToClick);

        // Capture URL before click so we can wait for it to actually change.
        var urlBefore = _ctx.Page.Url;

        await locatorToClick.ClickAsync();

        // Wait until the browser navigates away from the current URL.
        // WaitForURLAsync(predicate) is the modern non-deprecated way to block
        // until navigation completes; the 1.5s fallback covers client-side
        // routing that may not change the URL in the same tick.
        try
        {
            await _ctx.Page.WaitForURLAsync(
                url => url != urlBefore,
                new PageWaitForURLOptions { Timeout = 15_000 });
        }
        catch
        {
            await _ctx.Page.WaitForTimeoutAsync(3_000);
        }
    }

    /// <summary>
    /// Returns a locator that directly targets the <c>&lt;a href="..."&gt;</c> for known
    /// navigation labels so we never accidentally click a dropdown-toggle button.
    /// </summary>
    private Microsoft.Playwright.ILocator? ResolveNavLinkByLabel(string label) =>
        label.ToLowerInvariant() switch
        {
            "locations"                => _ctx.Page.Locator("a[href='/en/locations']").First,
            "office trailers for sale" => _ctx.Page.Locator("a[href='/en/sales-showroom']").First,
            "about us"                 => _ctx.Page.Locator("a[href*='/en/about']").First,
            // NOTE: "products", "solutions", "storage containers", "office trailers"
            // are mega-menu triggers whose direct <a> links are hidden until the
            // dropdown is expanded. Fall back to GetNavItem() for these so Playwright
            // clicks the *visible* top-level trigger element instead.
            _ => null
        };

    // ── TC-010  Request a Quote visible ───────────────────────────────────────

    [Then(@"the ""(.*)"" button should be visible in the header")]
    public async Task ThenTheButtonShouldBeVisibleInTheHeader(string buttonLabel)
    {
        var locator = ResolveHeaderButton(buttonLabel);
        await WaitHelper.WaitForVisible(locator, 8_000);
        Assert.That(await locator.IsVisibleAsync(), Is.True,
            $"Header button '{buttonLabel}' is not visible.");
    }

    // ── TC-011  Request a Quote click ─────────────────────────────────────────

    [When(@"I click the ""(.*)"" button in the header")]
    public async Task WhenIClickTheButtonInTheHeader(string buttonLabel)
    {
        var locator = ResolveHeaderButton(buttonLabel);
        await WaitHelper.WaitForVisible(locator, 8_000);
        await locator.ClickAsync();
        await WaitHelper.WaitForNetworkIdle(_ctx.Page);
    }

    // ── TC-012  Request Support click ─────────────────────────────────────────

    [When(@"I click the ""(.*)"" button")]
    public async Task WhenIClickTheButton(string buttonLabel)
    {
        var locator = ResolveHeaderButton(buttonLabel);
        await WaitHelper.WaitForVisible(locator, 8_000);
        await locator.ClickAsync();
        await WaitHelper.WaitForNetworkIdle(_ctx.Page);
    }

    // ── Shared URL assertion (TC-006, 007, 011, 012, 014) ─────────────────────

    [Then(@"the URL should contain ""(.*)""")]
    public void ThenTheUrlShouldContain(string expectedFragment)
    {
        Assert.That(_ctx.Page.Url, Does.Contain(expectedFragment),
            $"Expected URL to contain '{expectedFragment}' but got '{_ctx.Page.Url}'");
    }

    // ── Private helpers ────────────────────────────────────────────────────────

    private Microsoft.Playwright.ILocator ResolveHeaderButton(string label) =>
        label.ToLowerInvariant() switch
        {
            // Use direct href links — more reliable than text matching
            "request a quote" => _ctx.Page.Locator("a[href='/en/request-quote']").First,
            "request support" => _ctx.Page.Locator("a[href='/en/request-service']").First,
            _ => _ctx.Page.Locator(
                     $"a:has-text('{label}'), button:has-text('{label}')").First
        };
}
