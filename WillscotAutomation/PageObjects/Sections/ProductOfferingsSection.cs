using Microsoft.Playwright;

namespace WillscotAutomation.PageObjects.Sections;

/// <summary>
/// Strongly-typed locators for the product offerings / cards section
/// on the WillScot homepage.
/// </summary>
public sealed class ProductOfferingsSection
{
    private readonly IPage _page;

    // ── Storage Containers card ────────────────────────────────────────────────

    /// <summary>
    /// The full card / tile element for Storage Containers.
    /// Uses a broad ancestor-search: find any clickable container that contains
    /// the text "Storage Containers".  This is resilient to BEM / utility-class changes.
    /// </summary>
    public ILocator StorageContainersCard =>
        _page.Locator("a, li, article, section, div")
             .Filter(new LocatorFilterOptions { HasText = "Storage Containers" })
             .Filter(new LocatorFilterOptions
             {
                 Has = _page.Locator("img")   // must also contain an image
             })
             .First;

    /// <summary>Image inside the Storage Containers card.</summary>
    public ILocator StorageContainersImage =>
        StorageContainersCard.Locator("img").First;

    /// <summary>Text label inside the Storage Containers card.</summary>
    public ILocator StorageContainersLabel =>
        _page.Locator("h1, h2, h3, h4, p, span, a")
             .Filter(new LocatorFilterOptions { HasText = "Storage Containers" })
             .First;

    // ── All product images & links ─────────────────────────────────────────────

    /// <summary>
    /// All product/offering images — broad selector covering common CMS class patterns.
    /// </summary>
    public ILocator AllProductImages =>
        _page.Locator(
            "[class*='product'] img, [class*='offering'] img, [class*='card'] img, " +
            "[class*='tile'] img, [class*='item'] img");

    /// <summary>All product-area anchor links with an href attribute.</summary>
    public ILocator AllProductLinks =>
        _page.Locator(
            "[class*='product'] a[href], [class*='offering'] a[href], " +
            "[class*='card'] a[href], [class*='tile'] a[href]");

    public ProductOfferingsSection(IPage page) => _page = page;
}
