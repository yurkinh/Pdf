using FluentAssertions;
using Xunit;

namespace Melville.Pdf.ViewerModel.Tests;

/// <summary>Continuous mode: 10 letter pages (612x792) in a 628x400 viewport, so fit width is scale 1.</summary>
public class ContinuousViewModelTest
{
    private const double Gap = ContinuousLayout.DefaultGap;
    private const double Stride = 792 + Gap;
    private readonly FakePageRenderer renderer = new();

    private async Task<PdfViewerViewModel<FakePage>> CreateAsync(int pages = 10)
    {
        var sut = await PdfViewerViewModel<FakePage>.CreateAsync(await TestDocuments.MultiPageAsync(pages), renderer);
        sut.SetViewport(628, 400);
        sut.ZoomMode = ZoomMode.FitWidth;
        sut.DisplayMode = DisplayMode.Continuous;
        return sut;
    }

    private static bool AllRendered(PdfViewerViewModel<FakePage> sut) =>
        sut.VisiblePages.Count > 0 && sut.VisiblePages.All(p => p.Rendered is not null) && !sut.IsRendering;

    [Fact]
    public async Task VisiblePagesAreRenderedAndPlacedInTheStrip()
    {
        await using var sut = await CreateAsync();
        await sut.WaitUntilAsync(() => AllRendered(sut));

        sut.Continuous!.Scale.Should().Be(1);
        sut.VisiblePages.Select(p => p.Page).Should().Equal(1);
        sut.VisiblePages[0].Layout.OffsetY.Should().Be(Gap);
        sut.VisiblePages[0].Rendered!.Content.Number.Should().Be(1);
    }

    [Fact]
    public async Task ScrollingChangesTheCurrentPageWithoutJumping()
    {
        await using var sut = await CreateAsync();

        sut.PanBy(0, -(3 * Stride + 100)); // drag up: page 4 under the centre

        sut.PageSelector.Page.Should().Be(4);
        sut.Continuous!.ScrollY.Should().Be(3 * Stride + 100, "following the scroll must not scroll to the page start");
        await sut.WaitUntilAsync(() => AllRendered(sut));
        sut.VisiblePages.Select(p => p.Page).Should().Equal(4); // page 4 (2408-3200) fills the 2500-2900 view
    }

    [Fact]
    public async Task NavigatingScrollsToThePage()
    {
        await using var sut = await CreateAsync();

        sut.PageSelector.Page = 7;

        sut.Continuous!.ScrollY.Should().Be(6 * Stride, "page 7's top edge, less the gap");
        sut.VisiblePages[0].Page.Should().Be(7);
    }

    [Fact]
    public async Task OnlyPagesNearTheViewAreRendered()
    {
        await using var sut = await CreateAsync(pages: 50);
        await sut.WaitUntilAsync(() => AllRendered(sut));

        sut.PageSelector.Page = 40;
        await sut.WaitUntilAsync(() => AllRendered(sut) && sut.VisiblePages[0].Page == 40);

        renderer.Requested.Should().NotContain(20, "pages scrolled past by navigation are never rendered");
        renderer.Requested.Should().Contain(40);
        renderer.MaxConcurrent.Should().Be(1);
    }

    [Fact]
    public async Task ZoomKeepsThePointUnderTheFinger()
    {
        await using var sut = await CreateAsync();
        sut.PanBy(0, -1000);
        var before = sut.Continuous!.ToPage(300, 200);

        sut.ZoomAt(2, 300, 200);

        sut.ZoomMode.Should().Be(ZoomMode.Custom);
        sut.Continuous!.Scale.Should().Be(2);
        var after = sut.Continuous.ToPage(300, 200);
        after.Page.Should().Be(before.Page);
        after.X.Should().BeApproximately(before.X, 1e-9);
        after.Y.Should().BeApproximately(before.Y, 1e-9);
    }

    [Fact]
    public async Task ViewStateRoundTripsInContinuousMode()
    {
        await using var sut = await CreateAsync();
        sut.PanBy(0, -(2 * Stride + 300));
        var saved = sut.ViewState;
        saved.Page.Should().Be(3);
        saved.PageY.Should().BeApproximately(300 - Gap, 1e-9);

        await using var other = await CreateAsync();
        other.Restore(saved);

        other.ViewState.Page.Should().Be(3);
        other.ViewState.PageY.Should().BeApproximately(saved.PageY, 1e-9);
        other.Continuous!.ScrollY.Should().BeApproximately(sut.Continuous!.ScrollY, 1e-9);
    }

    [Fact]
    public async Task SwitchingModesKeepsThePage()
    {
        await using var sut = await CreateAsync();
        sut.PageSelector.Page = 5;

        sut.DisplayMode = DisplayMode.SinglePage;
        await sut.WaitUntilAsync(() => sut.CurrentPage?.Content.Number == 5 && !sut.IsRendering);
        sut.VisiblePages.Should().ContainSingle().Which.Page.Should().Be(5);

        sut.DisplayMode = DisplayMode.Continuous;
        sut.PageSelector.Page.Should().Be(5);
        sut.VisiblePages[0].Page.Should().Be(5);
    }

    [Fact]
    public async Task TogglingALayerRerendersThePagesInView()
    {
        await using var sut = await PdfViewerViewModel<FakePage>.CreateAsync(await TestDocuments.WithLayersAsync(), renderer);
        sut.SetViewport(628, 400);
        sut.DisplayMode = DisplayMode.Continuous;
        await sut.WaitUntilAsync(() => AllRendered(sut));
        var first = sut.VisiblePages[0].Rendered!.Content;

        sut.Layers[0].Visible = false;

        await sut.WaitUntilAsync(() => AllRendered(sut) && !ReferenceEquals(sut.VisiblePages[0].Rendered!.Content, first));
    }
}
