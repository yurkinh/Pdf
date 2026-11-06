using FluentAssertions;
using Melville.Pdf.Model.Documents;
using Xunit;

namespace Melville.Pdf.ViewerModel.Tests;

/// <summary>
/// Links of the Links reference document (four letter pages, 612x792; page 2 is rotated 90 degrees). Its first
/// link spans x 36..576 and y 680..710 in PDF coordinates, so 82..112 points from the top of the page.
/// </summary>
public class LinksViewModelTest
{
    private readonly FakePageRenderer renderer = new();

    private async Task<PdfViewerViewModel<FakePage>> CreateAsync(double width = 612, double height = 792)
    {
        var sut = await PdfViewerViewModel<FakePage>.CreateAsync(await TestDocuments.FromGeneratorAsync("-Links"), renderer);
        sut.SetViewport(width, height);
        return sut;
    }

    [Fact]
    public async Task LinkAreasAreInPagePointsFromTheTopLeft()
    {
        await using var sut = await CreateAsync();

        var links = await sut.GetLinksAsync(1);

        links.Should().HaveCount(5);
        links[0].Should().Be(new PageLink(new PageRect(36, 82, 540, 30), new PdfPageLinkTarget(3, 0, 400)));
    }

    [Fact]
    public async Task ARotatedPagesLinksTurnWithIt()
    {
        await using var sut = await CreateAsync();

        var link = (await sut.GetLinksAsync(2)).Single();

        // A quarter turn clockwise: the strip 82..112 points below the top edge is now 82..112 points left of the
        // right edge of the 792 x 612 page, and the left margin is now the top margin.
        link.Area.X.Should().BeApproximately(680, 0.01);
        link.Area.Y.Should().BeApproximately(36, 0.01);
        link.Area.Width.Should().BeApproximately(30, 0.01);
        link.Area.Height.Should().BeApproximately(540, 0.01);
    }

    [Fact]
    public async Task TheLinkUnderAPointIsFound()
    {
        await using var sut = await CreateAsync(); // fit page: scale 1, no offset

        (await sut.LinkAtAsync(100, 90)).Should().Be(new PdfPageLinkTarget(3, 0, 400));
        (await sut.LinkAtAsync(100, 90 + 48 * 4)).Should().Be(new PdfUriLinkTarget("https://github.com/DrJohnMelville/Pdf"));
        (await sut.LinkAtAsync(10, 10)).Should().BeNull();
        (await sut.LinkAtAsync(700, 90)).Should().BeNull("there is no page there");
    }

    [Fact]
    public async Task FollowingALinkShowsItsPositionAtTheTop()
    {
        await using var sut = await CreateAsync(612, 400);
        sut.ZoomMode = ZoomMode.FitWidth;

        sut.GoTo(new PdfPageLinkTarget(3, 0, 600));
        await sut.WaitUntilAsync(() => sut.CurrentPage?.Content.Number == 3 && sut.Layout.ScrollY > 0);

        sut.PageSelector.Page.Should().Be(3);
        sut.ViewState.PageY.Should().Be(192, "y 600 is 192 points below the top of the page");
        sut.ZoomMode.Should().Be(ZoomMode.FitWidth);
    }

    [Fact]
    public async Task FollowingALinkInContinuousModeScrollsTheStrip()
    {
        await using var sut = await CreateAsync(612, 400);
        sut.ZoomMode = ZoomMode.FitWidth;
        sut.DisplayMode = DisplayMode.Continuous;

        sut.GoTo(new PdfPageLinkTarget(3, 0, 600));

        sut.PageSelector.Page.Should().Be(3);
        sut.ViewState.Page.Should().Be(3);
        sut.ViewState.PageY.Should().BeApproximately(192, 0.01);
    }

    [Fact]
    public async Task ALinkWithoutAPositionShowsTheTopOfThePage()
    {
        await using var sut = await CreateAsync(612, 400);
        sut.ZoomMode = ZoomMode.FitWidth;
        sut.DisplayMode = DisplayMode.Continuous;
        sut.PanBy(0, -300);

        sut.GoTo(new PdfPageLinkTarget(2));

        sut.ViewState.Page.Should().Be(2);
        sut.ViewState.PageY.Should().BeApproximately(0, 0.01);
    }
}
