using Microsoft.Playwright;

namespace WillscotAutomation.PageObjects.Components;

public sealed class NavigationMenu
{
    private readonly IPage _page;

    // ── All top-level nav links ────────────────────────────────────────────────

    public ILocator Products =>
        GetNavItem("Products");

    public ILocator StorageContainersNav =>
        GetNavItem("Storage Containers");

    public ILocator OfficeTrailers =>
        GetNavItem("Office Trailers");

    public ILocator BrowseByUse =>
        GetNavItem("Browse by Use");

    public ILocator Solutions =>
        GetNavItem("Solutions");

    public ILocator AboutUs =>
        GetNavItem("About Us");

    public ILocator Locations =>
        GetNavItem("Locations");

    public ILocator OfficeTrailersForSale =>
        GetNavItem("Office Trailers for Sale");

    // Searches inside <nav> first; falls back to a site-wide role-based lookup.
    public ILocator GetNavItem(string label)
    {
        // Primary: look inside nav elements
        var inNav = _page.Locator("nav")
                         .Filter(new LocatorFilterOptions { HasText = label })
                         .Locator($"a, button")
                         .Filter(new LocatorFilterOptions { HasText = label })
                         .First;

        // Fallback: any link / button with the exact accessible name
        var byRole = _page.GetByRole(AriaRole.Link,
                         new PageGetByRoleOptions { Name = label, Exact = true })
                         .Or(_page.GetByRole(AriaRole.Button,
                             new PageGetByRoleOptions { Name = label, Exact = true }))
                         .First;

        return inNav.Or(byRole).First;
    }

    public NavigationMenu(IPage page) => _page = page;
}
