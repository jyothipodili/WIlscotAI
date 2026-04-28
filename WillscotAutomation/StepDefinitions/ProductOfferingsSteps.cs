using NUnit.Framework;
using Reqnroll;
using WillscotAutomation.Config;
using WillscotAutomation.Drivers;
using WillscotAutomation.PageObjects;
using WillscotAutomation.Utilities;

namespace WillscotAutomation.StepDefinitions;

[Binding]
public sealed class ProductOfferingsSteps
{
    private readonly PlaywrightContext _ctx;
    private readonly HomePage          _homePage;

    public ProductOfferingsSteps(PlaywrightContext ctx)
    {
        _ctx      = ctx;
        _homePage = new HomePage(ctx.Page);
    }

    // ── TC-013  Storage Containers card display ────────────────────────────────

    [Then(@"the Storage Containers product card should display with a visible image")]
    public async Task ThenStorageContainersCardHasVisibleImage()
    {
        // Scroll mid-page to trigger lazy loading, then wait for the card to appear.
        await _ctx.Page.EvaluateAsync("window.scrollTo(0, document.body.scrollHeight / 3)");
        await WaitHelper.WaitForVisible(_homePage.ProductOfferings.StorageContainersCard, 20_000);
        await WaitHelper.ScrollIntoView(_homePage.ProductOfferings.StorageContainersImage);

        Assert.That(
            await _homePage.ProductOfferings.StorageContainersImage.IsVisibleAsync(), Is.True,
            "Storage Containers product card image is not visible.");
    }

    [Then(@"the Storage Containers product card label should read ""(.*)""")]
    public async Task ThenStorageContainersCardLabelShouldRead(string expectedLabel)
    {
        await WaitHelper.WaitForVisible(_homePage.ProductOfferings.StorageContainersLabel, 10_000);
        var actualText = await WaitHelper.GetText(_homePage.ProductOfferings.StorageContainersLabel);

        Assert.That(actualText, Does.Contain(expectedLabel),
            $"Storage Containers label mismatch. Expected: '{expectedLabel}'. Actual: '{actualText}'");
    }

    // ── TC-014  Storage Containers card navigation ────────────────────────────

    [When(@"I click the Storage Containers product card")]
    public async Task WhenIClickTheStorageContainersProductCard()
    {
        // Scroll to trigger lazy loading of the product section (same as TC-013)
        await _ctx.Page.EvaluateAsync("window.scrollTo(0, document.body.scrollHeight / 3)");

        // Prefer exact href; fall back to partial match in case the path changes
        var exactLink   = _ctx.Page.Locator("a[href='/en/store-secure/storage-containers']");
        var partialLink = _ctx.Page.Locator("a[href*='storage-containers']");
        var linkToClick = await exactLink.CountAsync() > 0 ? exactLink.First : partialLink.First;

        await WaitHelper.WaitForVisible(linkToClick, 15_000);
        await WaitHelper.ScrollIntoView(linkToClick);

        var urlBefore = _ctx.Page.Url;
        await linkToClick.ClickAsync();

        // Wait for URL to change — mirrors the pattern used by passing nav tests
        try
        {
            await _ctx.Page.WaitForURLAsync(
                url => url != urlBefore,
                new Microsoft.Playwright.PageWaitForURLOptions { Timeout = 15_000 });
        }
        catch
        {
            await _ctx.Page.WaitForTimeoutAsync(3_000);
        }
    }

    // ── TC-015  All product images return HTTP 200 ────────────────────────────

    [Then(@"all product images should return HTTP 200")]
    public async Task ThenAllProductImagesShouldReturnHttp200()
    {
        var images = await _homePage.ProductOfferings.AllProductImages.AllAsync();
        var failed = new List<string>();

        // Collect the first 10 candidate URLs up-front, then check them in parallel.
        var candidates = new List<(string Absolute, string Src)>();
        foreach (var imgLocator in images.Take(10))
        {
            var src = await imgLocator.GetAttributeAsync("src");
            if (string.IsNullOrWhiteSpace(src)) continue;
            if (src.StartsWith("data:", StringComparison.OrdinalIgnoreCase)) continue;
            if (src.StartsWith("blob:", StringComparison.OrdinalIgnoreCase)) continue;
            if (src.Contains("/_next/image", StringComparison.OrdinalIgnoreCase)) continue;
            if (src.Contains("/icons/", StringComparison.OrdinalIgnoreCase)) continue;
            var absoluteUrl = HttpHelper.ToAbsoluteUrl(src, ConfigReader.BaseUrl);
            if (string.IsNullOrEmpty(absoluteUrl)) continue;
            candidates.Add((absoluteUrl, src));
        }

        var results = await Task.WhenAll(candidates.Select(async c =>
        {
            var isOk = await HttpHelper.ValidateHttpStatus200(_ctx.ApiContext, c.Absolute);
            return isOk ? null : $"{c.Absolute} (src: {c.Src})";
        }));

        failed.AddRange(results.Where(r => r != null)!);
        Assert.That(failed, Is.Empty,
            $"Product images returned non-200 status:\n  {string.Join("\n  ", failed)}");
    }

    [Then(@"all product images should be visible on the page")]
    public async Task ThenAllProductImagesShouldBeVisible()
    {
        var images     = await _homePage.ProductOfferings.AllProductImages.AllAsync();
        var invisible  = new List<string>();

        foreach (var imgLocator in images)
        {
            await WaitHelper.ScrollIntoView(imgLocator);
            var src     = await imgLocator.GetAttributeAsync("src") ?? "(no src)";
            var visible = await imgLocator.IsVisibleAsync();
            if (!visible) invisible.Add(src);
        }

        Assert.That(invisible, Is.Empty,
            $"Product images are not visible:\n  {string.Join("\n  ", invisible)}");
    }

    // ── TC-016  All product links return HTTP 200 ─────────────────────────────

    [Then(@"all product links should return HTTP 200")]
    public async Task ThenAllProductLinksShouldReturnHttp200()
    {
        var links = await _homePage.ProductOfferings.AllProductLinks.AllAsync();

        // Collect valid absolute URLs, then check them all in parallel.
        var urls = new List<string>();
        foreach (var linkLocator in links)
        {
            var href = await linkLocator.GetAttributeAsync("href");
            if (string.IsNullOrWhiteSpace(href)) continue;
            if (href.StartsWith("#") || href.StartsWith("javascript:", StringComparison.OrdinalIgnoreCase)) continue;
            urls.Add(HttpHelper.ToAbsoluteUrl(href, ConfigReader.BaseUrl));
        }

        var results = await Task.WhenAll(urls.Select(async url =>
        {
            var isOk = await HttpHelper.ValidateHttpStatus200(_ctx.ApiContext, url);
            return isOk ? null : url;
        }));

        var failed = results.Where(r => r != null).ToList();
        Assert.That(failed, Is.Empty,
            $"Product links returned non-200 status:\n  {string.Join("\n  ", failed)}");
    }
}
