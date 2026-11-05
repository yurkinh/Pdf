using FluentAssertions;
using Xunit;

namespace Melville.Pdf.ViewerModel.Tests;

public class PageLayoutTest
{
    [Fact]
    public void FitPageShowsTheWholePageCentredOnTheLongAxis()
    {
        // 200x400 page in a 400x400 viewport: height limits the scale to 1.
        var layout = PageLayout.Compute(400, 400, 200, 400, ZoomMode.FitPage, customScale: 3);

        layout.Scale.Should().Be(1);
        layout.OffsetX.Should().Be(100);
        layout.OffsetY.Should().Be(0);
        (layout.ContentWidth, layout.ContentHeight).Should().Be((200, 400));
    }

    [Fact]
    public void FitWidthFillsTheWidthAndLetsTheHeightScroll()
    {
        var layout = PageLayout.Compute(400, 400, 200, 400, ZoomMode.FitWidth, customScale: 3);

        layout.Scale.Should().Be(2);
        layout.OffsetX.Should().Be(0);
        layout.OffsetY.Should().Be(0, "content taller than the viewport starts at the top");
        layout.ContentHeight.Should().Be(800);
    }

    [Fact]
    public void CustomUsesTheGivenScaleAndCentresSmallPages()
    {
        var layout = PageLayout.Compute(400, 400, 200, 100, ZoomMode.Custom, customScale: 0.5);

        layout.Scale.Should().Be(0.5);
        (layout.OffsetX, layout.OffsetY).Should().Be((150, 175));
    }

    [Theory]
    [InlineData(0, 400, 200, 400)]
    [InlineData(400, 400, 0, 400)]
    public void EmptyViewportOrPageGivesNoLayout(double vw, double vh, double pw, double ph)
    {
        PageLayout.Compute(vw, vh, pw, ph, ZoomMode.FitPage, 1).Should().Be(default(PageLayout));
    }
}

public class PageLayoutScrollTest
{
    [Fact]
    public void ScrollMovesAPageLargerThanTheViewport()
    {
        // 400x400 page at scale 2 is 800x800 in a 400x400 viewport: 400 to scroll each way.
        var layout = PageLayout.Compute(400, 400, 400, 400, ZoomMode.Custom, 2, scrollX: 100, scrollY: 50);

        (layout.MaxScrollX, layout.MaxScrollY).Should().Be((400, 400));
        (layout.ScrollX, layout.ScrollY).Should().Be((100, 50));
        (layout.OffsetX, layout.OffsetY).Should().Be((-100, -50));
    }

    [Theory]
    [InlineData(-50, 0)]
    [InlineData(1000, 400)]
    public void ScrollIsClampedToTheContent(double requested, double expected) =>
        PageLayout.Compute(400, 400, 400, 400, ZoomMode.Custom, 2, requested, requested)
            .ScrollX.Should().Be(expected);

    [Fact]
    public void APageThatFitsIgnoresScrollAndStaysCentred()
    {
        var layout = PageLayout.Compute(400, 400, 100, 100, ZoomMode.Custom, 1, scrollX: 30, scrollY: 30);
        (layout.OffsetX, layout.OffsetY, layout.ScrollX).Should().Be((150, 150, 0));
    }

    [Fact]
    public void ToPageInvertsTheLayout()
    {
        var layout = PageLayout.Compute(400, 400, 400, 400, ZoomMode.Custom, 2, 100, 50);
        layout.ToPage(0, 0).Should().Be((50, 25));
    }
}
