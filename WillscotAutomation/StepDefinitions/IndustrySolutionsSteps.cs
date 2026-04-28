using NUnit.Framework;
using Reqnroll;
using WillscotAutomation.Drivers;
using WillscotAutomation.PageObjects;
using WillscotAutomation.Utilities;

namespace WillscotAutomation.StepDefinitions;

[Binding]
public sealed class IndustrySolutionsSteps
{
    private readonly PlaywrightContext _ctx;
    private readonly HomePage          _homePage;

    public IndustrySolutionsSteps(PlaywrightContext ctx)
    {
        _ctx      = ctx;
        _homePage = new HomePage(ctx.Page);
    }

    // ── TC-017  Industry tabs visible ─────────────────────────────────────────

    [Then(@"the following industry solution tabs should be displayed")]
    public async Task ThenTheFollowingIndustrySolutionTabsShouldBeDisplayed(Table table)
    {
        // Scroll to bottom of the page first so lazy-loaded sections are rendered
        await _ctx.Page.EvaluateAsync("window.scrollTo(0, document.body.scrollHeight / 2)");
        await _ctx.Page.WaitForTimeoutAsync(1500);

        var failures = new List<string>();

        foreach (var row in table.Rows)
        {
            var tabName = row["TabName"];
            var locator = _homePage.IndustrySolutions.GetTab(tabName);

            try
            {
                await WaitHelper.ScrollIntoView(locator);
                await WaitHelper.WaitForVisible(locator, 10_000);

                var visible = await locator.IsVisibleAsync();
                if (!visible) failures.Add(tabName);
            }
            catch
            {
                failures.Add(tabName);
            }
        }

        Assert.That(failures, Is.Empty,
            $"The following industry solution tabs were NOT visible:\n  " +
            string.Join("\n  ", failures));
    }
}
