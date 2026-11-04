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
