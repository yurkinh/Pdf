using FluentAssertions;
using Xunit;

namespace Melville.Pdf.ViewerModel.Tests;

public class ContinuousLayoutTest
{
    private const double Gap = ContinuousLayout.DefaultGap;
    private static readonly PageSize Letter = new(612, 792);

    private static PageSize[] Pages(int count) => Enumerable.Repeat(Letter, count).ToArray();

    [Fact]
    public void FitWidthStacksPagesWithGaps()
    {
        var sut = ContinuousLayout.Compute(Pages(3), ScrollOrientation.Vertical, ZoomMode.FitWidth, 1, 628, 400);

        sut.Scale.Should().Be(1, "(628 - 2 gaps) / 612");
        sut.ContentHeight.Should().Be(3 * 792 + 4 * Gap);
        sut.PageLayoutOf(1).Should().Be(new PageLayout(1, Gap, Gap, 612, 792));
        sut.PageLayoutOf(2).OffsetY.Should().Be(792 + 2 * Gap);
    }

    [Fact]
    public void FitPageShowsTheLargestPageWhole()
    {
        PageSize[] pages = [Letter, new(612 * 2, 792)]; // a double-width spread
        var sut = ContinuousLayout.Compute(pages, ScrollOrientation.Vertical, ZoomMode.FitPage, 1, 1240, 808);

        sut.Scale.Should().Be(1, "the spread is (1240 - 16) wide, both are (808 - 16) high");
        sut.PageLayoutOf(1).OffsetX.Should().Be(Gap + 306, "the narrower page is centred across the strip");
    }

    [Fact]
    public void ScrollingMovesPagesAndIsClamped()
    {
        var sut = ContinuousLayout.Compute(Pages(3), ScrollOrientation.Vertical, ZoomMode.FitWidth, 1, 628, 400,
            scrollY: 1000);

        sut.ScrollY.Should().Be(1000);
        sut.PageLayoutOf(2).OffsetY.Should().Be(792 + 2 * Gap - 1000);
        sut.ScrolledTo(0, 1e9).ScrollY.Should().Be(sut.MaxScrollY).And.Be(sut.ContentHeight - 400);
        sut.ScrolledTo(0, -5).ScrollY.Should().Be(0);
    }

    [Fact]
    public void ContentSmallerThanTheViewportStartsAtTheTopCentredAcross()
    {
        var sut = ContinuousLayout.Compute(Pages(1), ScrollOrientation.Vertical, ZoomMode.Custom, 0.5, 1000, 1000);

        sut.MaxScrollY.Should().Be(0);
        var page = sut.PageLayoutOf(1);
        page.OffsetX.Should().Be((1000 - 306) / 2.0);
        page.OffsetY.Should().Be(Gap, "a short document starts at the top, as a scrolling list does");
    }

    [Fact]
    public void ContentSmallerThanTheViewportStartsAtTheLeftWhenScrollingHorizontally()
    {
        var sut = ContinuousLayout.Compute(Pages(1), ScrollOrientation.Horizontal, ZoomMode.Custom, 0.5, 1000, 1000);

        var page = sut.PageLayoutOf(1);
        page.OffsetX.Should().Be(Gap);
        page.OffsetY.Should().Be((1000 - 396) / 2.0);
    }

    [Fact]
    public void ToPageFindsThePageAndThePointOnIt()
    {
        var sut = ContinuousLayout.Compute(Pages(3), ScrollOrientation.Vertical, ZoomMode.FitWidth, 1, 628, 400,
            scrollY: 792 + Gap); // page 2 starts at the top of the viewport, less the gap

        sut.ToPage(Gap + 10, Gap + 20).Should().Be((2, 10, 20));
        sut.ToPage(Gap, 1).Page.Should().Be(1, "the upper half of the gap above page 2 still belongs to page 1");
        sut.ToPage(Gap, Gap / 2 + 1).Page.Should().Be(2, "its lower half belongs to page 2");
        sut.CurrentPage.Should().Be(2);
    }

    [Fact]
    public void PagesInViewIncludesOnlyIntersectingPagesPlusTheMargin()
    {
        var sut = ContinuousLayout.Compute(Pages(10), ScrollOrientation.Vertical, ZoomMode.FitWidth, 1, 628, 400,
            scrollY: 4 * (792 + Gap)); // page 5 at the top

        sut.PagesInView().Should().Equal(5);
        sut.PagesInView(margin: 1).Should().Equal(4, 5); // 400 units either side: page 6 starts at 4008, past 4000
        sut.PagesInView(margin: 2).Should().Equal(4, 5, 6);
    }

    [Fact]
    public void ScrollForPutsAPagePointAtTheTopLeft()
    {
        var sut = ContinuousLayout.Compute(Pages(5), ScrollOrientation.Vertical, ZoomMode.Custom, 2, 300, 400);

        var (x, y) = sut.ScrollFor(3, 100, 50);
        var moved = sut.ScrolledTo(x, y);

        moved.ToPage(0, 0).Should().Be((3, 100, 50));
    }

    [Fact]
    public void HorizontalOrientationLaysPagesSideBySide()
    {
        var sut = ContinuousLayout.Compute(Pages(3), ScrollOrientation.Horizontal, ZoomMode.Custom, 1, 700, 900);

        sut.ContentWidth.Should().Be(3 * 612 + 4 * Gap);
        sut.PageLayoutOf(2).OffsetX.Should().Be(612 + 2 * Gap);
        sut.ScrollToStartOf(3).X.Should().Be(2 * (612 + Gap));
    }

    [Fact]
    public void AMarginSurroundsEveryPage()
    {
        var margin = new PageInsets(16, 8, 16, 8);
        var sut = ContinuousLayout.Compute(Pages(3), ScrollOrientation.Vertical, ZoomMode.FitWidth, 1, 644, 400, 0, 0,
            margin, PageInsets.Zero);

        sut.Scale.Should().Be(1, "(644 - 16 - 16) / 612");
        sut.PageLayoutOf(1).Should().Be(new PageLayout(1, 16, 8, 612, 792));
        sut.PageLayoutOf(2).OffsetY.Should().Be(8 + 792 + 16, "one page's bottom margin and the next one's top");
        sut.ContentHeight.Should().Be(8 + 3 * 792 + 2 * 16 + 8);
        sut.ScrollToStartOf(2).Y.Should().Be(792 + 16);
    }

    [Fact]
    public void AMarginFollowsAHorizontalStrip()
    {
        var margin = new PageInsets(16, 8, 16, 8);
        var sut = ContinuousLayout.Compute(Pages(3), ScrollOrientation.Horizontal, ZoomMode.Custom, 1, 700, 900, 0, 0,
            margin, PageInsets.Zero);

        sut.PageLayoutOf(2).OffsetX.Should().Be(16 + 612 + 32);
        sut.PageLayoutOf(2).OffsetY.Should().Be((900 - 808) / 2 + 8, "the strip is centred across");
    }

    [Fact]
    public void ACropShowsPartOfEveryPageInPagePoints()
    {
        var crop = new PageInsets(50, 100, 50, 100);
        var sut = ContinuousLayout.Compute(Pages(3), ScrollOrientation.Vertical, ZoomMode.FitWidth, 1, 528, 400, 0, 0,
            null, crop);

        sut.Scale.Should().Be(1, "(528 - 2 gaps) / the cropped 512");
        var first = sut.PageLayoutOf(1);
        first.Should().Be(new PageLayout(1, Gap, Gap, 512, 592, OriginX: 50, OriginY: 100));
        sut.ToPage(Gap, Gap).Should().Be((1, 50, 100));
        first.ToPage(Gap + 10, Gap + 20).Should().Be((60, 120));
        first.ToView(60, 120).Should().Be((Gap + 10, Gap + 20));
        sut.ScrollFor(2, 50, 100).Should().Be((Gap, Gap + 592 + Gap));
        first.Uncropped(612, 792).Should().Be(new PageLayout(1, Gap - 50, Gap - 100, 612, 792));
    }
}
