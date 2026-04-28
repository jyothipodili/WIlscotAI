using Microsoft.Playwright;

namespace WillscotAutomation.PageObjects.Components;

/// <summary>
/// Strongly-typed locators for the global site header.
/// </summary>
public sealed class HeaderComponent
{
    private readonly IPage _page;

    // ── Locators ──────────────────────────────────────────────────────────────

    /// <summary>
    /// "Request a Quote" CTA in the header (link or button variant).
    /// </summary>
    public ILocator RequestAQuoteButton =>
        _page.Locator("header a:has-text('Request a Quote'), header button:has-text('Request a Quote')")
             .Or(_page.GetByRole(AriaRole.Link, new PageGetByRoleOptions { Name = "Request a Quote" }))
             .First;

    /// <summary>
    /// "Request Support" / "Request Service" / "Contact" link — anywhere on page.
    /// The live site places this in the utility bar above the main header.
    /// </summary>
    public ILocator RequestSupportButton =>
        _page.Locator(
            "a:has-text('Request Support'), button:has-text('Request Support'), " +
            "a:has-text('Request Service'), button:has-text('Request Service'), " +
            "a:has-text('Support'), button:has-text('Support')")
             .First;

    /// <summary>The site logo / home anchor.</summary>
    public ILocator SiteLogo =>
        _page.Locator("header a[href='/en'], header [class*='logo']").First;

    public HeaderComponent(IPage page) => _page = page;
}
