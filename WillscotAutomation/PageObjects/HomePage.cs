using Microsoft.Playwright;
using WillscotAutomation.PageObjects.Components;
using WillscotAutomation.PageObjects.Sections;

namespace WillscotAutomation.PageObjects;

public sealed class HomePage(IPage page)
{
    public HeaderComponent          Header            { get; } = new HeaderComponent(page);
    public NavigationMenu           Navigation        { get; } = new NavigationMenu(page);
    public ProductOfferingsSection  ProductOfferings  { get; } = new ProductOfferingsSection(page);
    public IndustrySolutionsSection IndustrySolutions { get; } = new IndustrySolutionsSection(page);

    // Only one h1 on the homepage — safe to target directly.
    public ILocator HeroBannerHeadline => page.Locator("h1").First;

    public ILocator LearnMoreCta =>
        page.Locator(
            "a:has-text('Learn more'), button:has-text('Learn more'), " +
            "a:has-text('Learn More'), button:has-text('Learn More')")
            .First;

    public ILocator AllImages => page.Locator("img[src]");
}
