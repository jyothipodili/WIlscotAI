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
        // Scroll first so lazy-loaded images are present in the DOM.
        await WaitHelper.ScrollAndWaitForImagesAsync(_ctx.Page);

        // Collect srcs via JS — avoids holding Playwright element handles across
        // page reflows, which causes "Element is not attached to the DOM" errors.
        var srcs = await _ctx.Page.EvaluateAsync<string[]>(@"() => {
            const imgs = document.querySelectorAll(
                ""[class*='product'] img, [class*='offering'] img, "" +
                ""[class*='card'] img, [class*='tile'] img, [class*='item'] img"");
            return Array.from(imgs)
                .map(img => img.getAttribute('src') || '')
                .filter(s => s &&
                    !s.startsWith('data:') &&
                    !s.startsWith('blob:') &&
                    !s.includes('/_next/image') &&
                    !s.includes('/icons/'));
        }");

        var candidates = srcs
            .Take(10)
            .Select(src => (Absolute: HttpHelper.ToAbsoluteUrl(src, ConfigReader.BaseUrl), Src: src))
            .Where(c => !string.IsNullOrEmpty(c.Absolute))
            .ToList();

        var failed = new List<string>();

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
        // Three-pass scroll to fully trigger lazy loading.
        await WaitHelper.ScrollAndWaitForImagesAsync(_ctx.Page);

        // Retry up to 3 times — images may still be decoding after the scroll settles.
        // Pure JS: no Playwright element handles held across reflows.
        string[] invisible = [];
        for (var attempt = 0; attempt < 3 && (attempt == 0 || invisible.Length > 0); attempt++)
        {
            if (attempt > 0) await _ctx.Page.WaitForTimeoutAsync(2_000);
            invisible = await _ctx.Page.EvaluateAsync<string[]>(@"() => {
            const imgs = document.querySelectorAll(
                ""[class*='product'] img, [class*='offering'] img, "" +
                ""[class*='card'] img, [class*='tile'] img, [class*='item'] img"");
            const failed = [];
            for (const img of imgs) {
                img.scrollIntoView({ behavior: 'instant', block: 'center' });
                const s = window.getComputedStyle(img);
                const r = img.getBoundingClientRect();
                const visible = s.display !== 'none' &&
                                s.visibility !== 'hidden' &&
                                parseFloat(s.opacity) > 0 &&
                                r.width > 0 && r.height > 0;
                if (!visible) failed.push(img.src || '(no src)');
            }
            return failed;
        }");
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
