using Microsoft.Playwright;

namespace WillscotAutomation.PageObjects.Sections;

public sealed class IndustrySolutionsSection
{
    private readonly IPage _page;

    // ── Known industry tabs ────────────────────────────────────────────────────

    public ILocator ConstructionAndBuilders    => GetTab("Construction & Builders");
    public ILocator EducationAndGovernment     => GetTab("Education & Government");
    public ILocator EnergyAndIndustrial        => GetTab("Energy & Industrial");
    public ILocator RetailAndDistribution      => GetTab("Retail & Distribution");
    public ILocator Manufacturing              => GetTab("Manufacturing");
    public ILocator HealthcareAndEntertainment => GetTab("Healthcare & Entertainment");

    // 3-tier fallback so the locator survives CMS class-name changes:
    // 1. ARIA role="tab"  2. element inside a known industry/solution container  3. any text match
    public ILocator GetTab(string label)
    {
        // Strategy 1: semantic ARIA tab
        var byRole = _page.GetByRole(AriaRole.Tab,
            new PageGetByRoleOptions { Name = label, Exact = false });

        // Strategy 2: any interactive element in a solutions/industry section
        var bySection = _page.Locator(
            "[class*='industry'] button, [class*='industry'] a, [class*='industry'] li, " +
            "[class*='solution'] button, [class*='solution'] a, [class*='solution'] li, " +
            "[class*='sector'] button, [class*='sector'] a, [class*='sector'] li, " +
            "[class*='vertical'] button, [class*='vertical'] a, [class*='vertical'] li")
            .Filter(new LocatorFilterOptions { HasText = label });

        // Strategy 3: broadest fallback — any element with that exact text
        var byText = _page.Locator("button, a, li, span, div, p")
            .Filter(new LocatorFilterOptions { HasText = label });

        return byRole.Or(bySection).Or(byText).First;
    }

    public IndustrySolutionsSection(IPage page) => _page = page;
}
