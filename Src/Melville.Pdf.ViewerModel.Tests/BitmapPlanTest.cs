using FluentAssertions;
using Xunit;

namespace Melville.Pdf.ViewerModel.Tests;

public class BitmapPlanTest
{
    private const double PageWidth = 612, PageHeight = 792;
    private const double ViewWidth = 400, ViewHeight = 600;
    private const long Budget = 6_000_000;

    private static PageLayout Layout(ZoomMode mode, double scale = 1, double scrollX = 0, double scrollY = 0) =>
        PageLayout.Compute(ViewWidth, ViewHeight, PageWidth, PageHeight, mode, scale, scrollX, scrollY);

    [Fact]
    public void APageThatFitsTheBudgetIsOneExactBitmap()
    {
        var layout = Layout(ZoomMode.FitPage);

        var plan = BitmapPlan.Compute(PageWidth, PageHeight, layout, ViewWidth, ViewHeight, 2, Budget);

        plan.BackdropScale.Should().Be(layout.Scale);
        plan.Detail.Should().BeNull();
    }

    [Fact]
    public void ZoomedInBeyondTheBudgetTheBackdropShrinksToFitIt()
    {
        var layout = Layout(ZoomMode.Custom, 8); // 612x792 at 8 x density 2: 31 MP

        var plan = BitmapPlan.Compute(PageWidth, PageHeight, layout, ViewWidth, ViewHeight, 2, Budget);

        // Rounded up to whole pixels, as the bitmap will be, it must still fit the budget (it did not, by a few
        // pixels, and the backdrop silently was not made).
        var pixels = Math.Ceiling(PageWidth * plan.BackdropScale * 2) * Math.Ceiling(PageHeight * plan.BackdropScale * 2);
        pixels.Should().BeLessThanOrEqualTo(Budget).And.BeGreaterThan(Budget * 0.99);
        plan.BackdropScale.Should().BeLessThan(8);
    }

    [Fact]
    public void TheDetailCoversTheVisibleRegionPlusAMarginAtTheExactScale()
    {
        var layout = Layout(ZoomMode.Custom, 8, scrollX: 800, scrollY: 1600); // page point (100, 200) at top-left

        var plan = BitmapPlan.Compute(PageWidth, PageHeight, layout, ViewWidth, ViewHeight, 2, Budget);

        var visible = BitmapPlan.Visible(PageWidth, PageHeight, layout, ViewWidth, ViewHeight);
        visible.Should().Be(new PageRect(100, 200, 50, 75));
        plan.Scale.Should().Be(8);
        plan.Detail.Should().Be(new PageRect(87.5, 181.25, 75, 112.5), "a quarter of the visible size on each side");
        plan.Detail!.Value.Contains(visible).Should().BeTrue();
    }

    [Fact]
    public void TheDetailIsClippedToThePage()
    {
        var layout = Layout(ZoomMode.Custom, 8); // top-left corner of the page

        var plan = BitmapPlan.Compute(PageWidth, PageHeight, layout, ViewWidth, ViewHeight, 2, Budget);

        plan.Detail.Should().Be(new PageRect(0, 0, 62.5, 93.75));
    }

    [Fact]
    public void WithoutRoomForTheMarginTheDetailIsJustTheVisibleRegion()
    {
        var layout = Layout(ZoomMode.Custom, 8, scrollX: 800, scrollY: 1600);
        const long tight = 400 * 2 * 600 * 2 + 1000; // the viewport fits, the margin does not

        var plan = BitmapPlan.Compute(PageWidth, PageHeight, layout, ViewWidth, ViewHeight, 2, tight);

        plan.Detail.Should().Be(new PageRect(100, 200, 50, 75));
    }
}
