using FluentAssertions;
using Xunit;

namespace Melville.Pdf.ViewerModel.Tests;

public class PdfViewerViewModelTest
{
    private readonly FakePageRenderer renderer = new();

    private async Task<PdfViewerViewModel<FakePage>> CreateAsync(int pages = 3) =>
        await PdfViewerViewModel<FakePage>.CreateAsync(await TestDocuments.MultiPageAsync(pages), renderer);

    [Fact]
    public async Task CreateRendersTheFirstPage()
    {
        await using var sut = await CreateAsync();

        sut.PageCount.Should().Be(3);
        sut.PageSelector.MaxPage.Should().Be(3);
        sut.CurrentPage!.Content.Number.Should().Be(1);
        (sut.CurrentPage.Width, sut.CurrentPage.Height).Should().Be((FakePageRenderer.Width, FakePageRenderer.Height));
        sut.IsRendering.Should().BeFalse();
        sut.RenderError.Should().BeNull();
    }

    [Fact]
    public async Task ChangingThePageRendersIt()
    {
        await using var sut = await CreateAsync();

        sut.PageSelector.Page = 2;
        await sut.WaitUntilAsync(() => sut.CurrentPage?.Content.Number == 2 && !sut.IsRendering);

        renderer.Requested.Should().Equal(1, 2);
    }

    [Fact]
    public async Task RapidPageChangesPublishOnlyTheLatestAndNeverOverlap()
    {
        await using var sut = await CreateAsync();
        renderer.Hold(2);

        sut.PageSelector.Page = 2;  // starts rendering page 2, which is held open
        sut.PageSelector.Page = 3;  // supersedes it while it is still rendering
        renderer.Release(2);
        await sut.WaitUntilAsync(() => sut.CurrentPage?.Content.Number == 3 && !sut.IsRendering);

        renderer.MaxConcurrent.Should().Be(1, "one document is never rendered on two threads at once");
        renderer.Produced.Single(p => p.Number == 2).Disposed.Should().BeTrue("the superseded page is discarded");
        renderer.Produced.Single(p => p.Number == 3).Disposed.Should().BeFalse();
    }

    [Fact]
    public async Task PublishedPagesAreLeftForTheViewToRelease()
    {
        await using var sut = await CreateAsync();
        var first = sut.CurrentPage!.Content;

        sut.PageSelector.Page = 2;
        await sut.WaitUntilAsync(() => sut.CurrentPage?.Content.Number == 2);

        first.Disposed.Should().BeFalse("the view may still be drawing the page it was showing");
    }

    [Fact]
    public async Task RenderFailureIsReportedAndClearedByTheNextRender()
    {
        await using var sut = await CreateAsync();

        renderer.FailWith = new InvalidOperationException("broken page");
        sut.PageSelector.Page = 2;
        await sut.WaitUntilAsync(() => sut.RenderError is not null && !sut.IsRendering);
        sut.RenderError.Should().Be("broken page");
        sut.CurrentPage!.Content.Number.Should().Be(1, "the last good page stays on screen");

        renderer.FailWith = null;
        sut.PageSelector.Page = 3;
        await sut.WaitUntilAsync(() => sut.CurrentPage?.Content.Number == 3);
        sut.RenderError.Should().BeNull();
    }

    [Fact]
    public async Task FitPageLayoutFollowsTheViewport()
    {
        await using var sut = await CreateAsync();

        sut.SetViewport(306, 1000);

        sut.ZoomMode.Should().Be(ZoomMode.FitPage);
        sut.Layout.Scale.Should().Be(0.5);
        sut.Layout.OffsetY.Should().Be((1000 - 396) / 2.0);
    }

    [Fact]
    public async Task ZoomInStartsFromTheCurrentFitScale()
    {
        await using var sut = await CreateAsync();
        sut.SetViewport(306, 1000); // fit scale 0.5

        sut.ZoomInCommand.Execute(null);

        sut.ZoomMode.Should().Be(ZoomMode.Custom);
        sut.Layout.Scale.Should().Be(0.625);
        sut.FitPageCommand.CanExecute(null).Should().BeTrue();

        sut.FitWidthCommand.Execute(null);
        sut.Layout.Scale.Should().Be(0.5);
        sut.FitWidthCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public async Task CustomScaleIsClamped()
    {
        await using var sut = await CreateAsync();
        sut.CustomScale = 1000;
        sut.CustomScale.Should().Be(16);
        sut.CustomScale = 0;
        sut.CustomScale.Should().Be(0.1);
    }

    [Fact]
    public async Task DocumentsWithoutOptionalContentHaveNoLayers()
    {
        await using var sut = await CreateAsync();
        sut.Layers.Should().BeEmpty();
    }

    [Fact]
    public async Task LayersFollowTheOptionalContentOrder()
    {
        await using var sut = await PdfViewerViewModel<FakePage>.CreateAsync(
            await TestDocuments.WithLayersAsync(), renderer);

        sut.Layers.Select(l => (l.Name, l.Depth, l.ShowCheck, l.Visible))
            .Should().Equal(("Background", 0, true, true), ("Annotations", 0, true, true));
    }

    [Fact]
    public async Task OptionalContentWithoutAnOrderShowsNoLayers()
    {
        // Upstream lists only groups named in /Order; /Order is optional in the PDF spec.
        await using var sut = await PdfViewerViewModel<FakePage>.CreateAsync(
            await TestDocuments.FromGeneratorAsync("-HideOptionalImageOn"), renderer);

        sut.Layers.Should().BeEmpty();
    }

    [Fact]
    public async Task TogglingALayerRerendersThePage()
    {
        await using var sut = await PdfViewerViewModel<FakePage>.CreateAsync(
            await TestDocuments.WithLayersAsync(), renderer);
        var layer = sut.Layers[1];
        var before = renderer.Requested.Count;

        layer.Visible = !layer.Visible;
        await sut.WaitUntilAsync(() => renderer.Requested.Count > before && !sut.IsRendering);

        renderer.Requested.Should().HaveCount(before + 1);
    }

    [Fact]
    public async Task AfterDisposePageChangesDoNotRender()
    {
        var sut = await CreateAsync();
        await sut.DisposeAsync();
        var before = renderer.Requested.Count;

        sut.PageSelector.Page = 2;
        await sut.RenderCurrentPageAsync();

        renderer.Requested.Should().HaveCount(before);
    }
}

public class PdfViewerViewModelPanZoomTest
{
    private readonly FakePageRenderer renderer = new();

    private async Task<PdfViewerViewModel<FakePage>> CreateAsync()
    {
        var sut = await PdfViewerViewModel<FakePage>.CreateAsync(await TestDocuments.MultiPageAsync(3), renderer);
        sut.SetViewport(306, 396); // the 612x792 page fits at scale 0.5
        return sut;
    }

    [Fact]
    public async Task ZoomAtKeepsThePointUnderTheFingerInPlace()
    {
        await using var sut = await CreateAsync();
        var before = sut.Layout.ToPage(200, 100);

        sut.ZoomAt(4, 200, 100);

        sut.ZoomMode.Should().Be(ZoomMode.Custom);
        sut.Layout.Scale.Should().Be(2);
        var after = sut.Layout.ToPage(200, 100);
        after.X.Should().BeApproximately(before.X, 1e-9);
        after.Y.Should().BeApproximately(before.Y, 1e-9);
    }

    [Fact]
    public async Task PanByScrollsAndIsClamped()
    {
        await using var sut = await CreateAsync();
        sut.ZoomAt(2, 0, 0); // scale 1: the page is 612x792 in a 306x396 viewport

        sut.PanBy(-100, -50); // drag up-left: the page moves up-left, so the view scrolls right and down
        (sut.Layout.ScrollX, sut.Layout.ScrollY).Should().Be((100, 50));

        sut.PanBy(10_000, 10_000);
        (sut.Layout.ScrollX, sut.Layout.ScrollY).Should().Be((0, 0));
    }

    [Fact]
    public async Task ANewPageStartsAtTheTopLeft()
    {
        await using var sut = await CreateAsync();
        sut.ZoomAt(2, 0, 0);
        sut.PanBy(-100, -100);

        sut.PageSelector.Page = 2;
        await sut.WaitUntilAsync(() => sut.CurrentPage?.Content.Number == 2);

        (sut.Layout.ScrollX, sut.Layout.ScrollY).Should().Be((0, 0));
        sut.Layout.Scale.Should().Be(1, "the zoom stays");
    }

    [Fact]
    public async Task ZoomButtonsZoomAroundTheViewportCentre()
    {
        await using var sut = await CreateAsync();
        var centre = sut.Layout.ToPage(153, 198);

        sut.ZoomInCommand.Execute(null);

        var after = sut.Layout.ToPage(153, 198);
        after.X.Should().BeApproximately(centre.X, 1e-9);
        after.Y.Should().BeApproximately(centre.Y, 1e-9);
    }

    // CreateAsync sets a 306x396 viewport; pages are 612x792 points, so fit page is scale 0.5.

    [Fact]
    public async Task ViewStateReportsPageZoomAndScrollInPagePoints()
    {
        await using var sut = await CreateAsync();
        sut.ZoomAt(4, 0, 0);     // fit scale 0.5 x 4 = 2
        sut.PanBy(-100, -60);    // scrolled 100x60 viewport units = 50x30 page points

        sut.ViewState.Should().Be(new PdfViewState(1, ZoomMode.Custom, 2, 50, 30));
    }

    [Fact]
    public async Task RestoreGoesToThePageZoomAndScroll()
    {
        await using var sut = await CreateAsync();

        sut.Restore(new PdfViewState(3, ZoomMode.Custom, 2, 50, 30));
        await sut.WaitUntilAsync(() => sut.CurrentPage?.Content.Number == 3 && !sut.IsRendering);

        sut.PageSelector.Page.Should().Be(3);
        sut.ZoomMode.Should().Be(ZoomMode.Custom);
        sut.Layout.Scale.Should().Be(2);
        (sut.Layout.ScrollX, sut.Layout.ScrollY).Should().Be((100, 60));
        sut.ViewState.Should().Be(new PdfViewState(3, ZoomMode.Custom, 2, 50, 30));
    }

    [Fact]
    public async Task RestoreOnTheCurrentPageAppliesAtOnce()
    {
        await using var sut = await CreateAsync();
        sut.SetViewport(306, 200); // fit width is scale 0.5, and the 396-unit-tall page scrolls

        sut.Restore(new PdfViewState(1, ZoomMode.FitWidth, 0, 0, 100));

        sut.ZoomMode.Should().Be(ZoomMode.FitWidth);
        sut.Layout.Scale.Should().Be(0.5);
        sut.Layout.ScrollY.Should().Be(50, "page y 100 at scale 0.5");
    }

    [Fact]
    public async Task RestoreClampsThePageAndTheScroll()
    {
        await using var sut = await CreateAsync();

        sut.Restore(new PdfViewState(99, ZoomMode.Custom, 1, 10_000, 10_000));
        await sut.WaitUntilAsync(() => sut.CurrentPage?.Content.Number == 3 && !sut.IsRendering);

        sut.PageSelector.Page.Should().Be(3);
        (sut.Layout.ScrollX, sut.Layout.ScrollY).Should().Be((sut.Layout.MaxScrollX, sut.Layout.MaxScrollY));
    }

    [Fact]
    public async Task ViewStateDuringAPendingRestoreIsTheRestoredState()
    {
        await using var sut = await CreateAsync();
        renderer.Hold(2);
        var saved = new PdfViewState(2, ZoomMode.Custom, 2, 50, 30);

        sut.Restore(saved);

        sut.ViewState.Should().Be(saved, "saving again before page 2 arrives must not lose the restore");
        renderer.Release(2);
        await sut.WaitUntilAsync(() => sut.CurrentPage?.Content.Number == 2 && !sut.IsRendering);
        sut.ViewState.Should().Be(saved);
    }

    [Fact]
    public async Task ResizingWhileTheNextPageRendersKeepsTheOldPagesScroll()
    {
        await using var sut = await CreateAsync();
        sut.ZoomAt(4, 0, 0);
        sut.PanBy(-100, -60);
        renderer.Hold(2);

        sut.PageSelector.Page = 2;
        sut.SetViewport(306, 390); // page 1 is still on screen

        (sut.Layout.ScrollX, sut.Layout.ScrollY).Should().Be((100, 60));
        renderer.Release(2);
        await sut.WaitUntilAsync(() => sut.CurrentPage?.Content.Number == 2 && !sut.IsRendering);
        (sut.Layout.ScrollX, sut.Layout.ScrollY).Should().Be((0, 0), "the new page starts at the top-left");
    }
}
