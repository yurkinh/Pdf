using FluentAssertions;
using Melville.Pdf.TextExtractor;
using Xunit;

namespace Melville.Pdf.ViewerModel.Tests;

/// <summary>Matching text in a page's text layer (page 1 of the Links reference document).</summary>
public class TextSearchTest
{
    private static async Task<PageText> LinksPageAsync(int page = 1)
    {
        using var document = await TestDocuments.FromGeneratorAsync("-Links");
        return await document.PageTextLayerAsync(page);
    }

    [Fact]
    public async Task MatchingIgnoresCaseUnlessAsked()
    {
        var text = await LinksPageAsync();

        TextSearch.Find(text, 1, "LINKS").Should().ContainSingle().Which.Start.Should().Be(0);
        TextSearch.Find(text, 1, "LINKS", matchCase: true).Should().BeEmpty();
        TextSearch.Find(text, 1, "page").Should().HaveCount(3, "\"Page 3\", \"Page 2\" and \"Next page\"");
    }

    [Fact]
    public async Task AMatchIsHighlightedOverItsGlyphs()
    {
        var text = await LinksPageAsync();

        var match = TextSearch.Find(text, 1, "Links").Single();

        // 24 pt Helvetica on a baseline 52 points from the top: 0.8 em above it, 0.2 em below.
        var area = match.Areas.Should().ContainSingle().Subject;
        area.X.Should().BeApproximately(36, 0.01);
        area.Y.Should().BeApproximately(52 - 19.2, 0.01);
        area.Bottom.Should().BeApproximately(52 + 4.8, 0.01);
        area.Width.Should().BeInRange(50, 70, "\"Links\" is about 2.4 em wide");
    }

    [Fact]
    public async Task APhraseIsFoundAcrossALineBreakWithOneAreaPerLine()
    {
        var text = await LinksPageAsync();

        var match = TextSearch.Find(text, 1, "destination)   Page   2").Should().ContainSingle().Subject;

        text.Text.Substring(match.Start, match.Length).Should().Be("destination)\nPage 2");
        match.Areas.Should().HaveCount(2);
        match.Areas[1].Y.Should().BeGreaterThan(match.Areas[0].Bottom - 1, "the second line is below the first");
    }

    [Fact]
    public async Task BlankQueriesFindNothing()
    {
        var text = await LinksPageAsync();

        TextSearch.Find(text, 1, "").Should().BeEmpty();
        TextSearch.Find(text, 1, " \n\t").Should().BeEmpty();
        TextSearch.Find(PageText.Empty, 1, "Links").Should().BeEmpty();
    }
}

/// <summary>Searching a document through the view model: pages that each say "Page n".</summary>
public class SearchViewModelTest
{
    private static async Task<PdfViewerViewModel<FakePage>> CreateAsync(int pages = 20)
    {
        var sut = await PdfViewerViewModel<FakePage>.CreateAsync(await TestDocuments.WithTextAsync(pages), new FakePageRenderer());
        sut.SetViewport(612, 792);
        return sut;
    }

    [Fact]
    public async Task EveryPageIsSearchedAndMatchesAreInDocumentOrder()
    {
        await using var sut = await CreateAsync();

        var count = await sut.SearchAsync("page 1");

        // Page 1 and pages 10 to 19.
        count.Should().Be(11);
        sut.SearchMatches.Select(m => m.Page).Should().Equal(1, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19);
        sut.SearchQuery.Should().Be("page 1");
        sut.IsSearching.Should().BeFalse();
        sut.SearchedPages.Should().Be(20);
        sut.MatchesOn(12).Should().ContainSingle();
        sut.MatchesOn(2).Should().BeEmpty();
    }

    [Fact]
    public async Task TheFirstMatchFromTheCurrentPageIsShown()
    {
        await using var sut = await CreateAsync();
        sut.PageSelector.Page = 5;

        await sut.SearchAsync("page 1");

        sut.CurrentMatch!.Page.Should().Be(10, "pages before the current one come last");
        sut.CurrentMatchIndex.Should().Be(1, "the list is still in document order");
        await sut.WaitUntilAsync(() => sut.CurrentPage?.Content.Number == 10);
        sut.PageSelector.Page.Should().Be(10);
    }

    [Fact]
    public async Task WithNoMatchAfterTheCurrentPageTheSearchWrapsToTheStart()
    {
        await using var sut = await CreateAsync();
        sut.PageSelector.Page = 15;

        await sut.SearchAsync("page 2");

        sut.SearchMatches.Select(m => m.Page).Should().Equal(2, 20);
        sut.CurrentMatch!.Page.Should().Be(20);

        sut.PageSelector.Page = 20;
        await sut.SearchAsync("page 3");
        sut.CurrentMatch!.Page.Should().Be(3, "nothing after page 20 matches");
        sut.CurrentMatchIndex.Should().Be(0);
    }

    [Fact]
    public async Task NextAndPreviousStepThroughMatchesAndWrap()
    {
        await using var sut = await CreateAsync();
        await sut.SearchAsync("page 2");
        sut.CurrentMatch!.Page.Should().Be(2);

        sut.NextMatchCommand.Execute(null);
        sut.CurrentMatch!.Page.Should().Be(20);
        sut.NextMatchCommand.Execute(null);
        sut.CurrentMatch!.Page.Should().Be(2, "after the last match comes the first");
        sut.PreviousMatchCommand.Execute(null);
        sut.CurrentMatch!.Page.Should().Be(20, "before the first match comes the last");
        await sut.WaitUntilAsync(() => sut.CurrentPage?.Content.Number == 20);
    }

    [Fact]
    public async Task SearchingAgainGivesTheSameMatchesFromTheKnownText()
    {
        await using var sut = await CreateAsync();
        var first = await sut.SearchAsync("page 1");
        var areas = sut.SearchMatches.Select(m => m.Areas.Single()).ToList();

        (await sut.SearchAsync("PAGE 1")).Should().Be(first);

        sut.SearchMatches.Select(m => m.Areas.Single()).Should().Equal(areas);
        (await sut.SearchAsync("page 21")).Should().Be(0);
    }

    [Fact]
    public async Task ANewSearchReplacesTheOneRunning()
    {
        await using var sut = await CreateAsync();

        var first = sut.SearchAsync("page");
        var second = sut.SearchAsync("page 7");
        await Task.WhenAll(first, second);

        sut.SearchQuery.Should().Be("page 7");
        sut.SearchMatches.Should().ContainSingle().Which.Page.Should().Be(7);
        sut.IsSearching.Should().BeFalse();
    }

    [Fact]
    public async Task ClearingForgetsTheMatches()
    {
        await using var sut = await CreateAsync();
        await sut.SearchAsync("page");

        sut.ClearSearch();

        sut.SearchMatches.Should().BeEmpty();
        sut.CurrentMatch.Should().BeNull();
        sut.SearchQuery.Should().BeEmpty();
        sut.NextMatchCommand.CanExecute(null).Should().BeFalse();
        (await sut.SearchAsync("  ")).Should().Be(0);
    }

    [Fact]
    public async Task InContinuousModeAMatchIsScrolledIntoView()
    {
        await using var sut = await CreateAsync();
        sut.SetViewport(612, 400);
        sut.ZoomMode = ZoomMode.FitWidth;
        sut.DisplayMode = DisplayMode.Continuous;

        await sut.SearchAsync("page 18");

        var match = sut.CurrentMatch!;
        match.Page.Should().Be(18);
        sut.ViewState.Page.Should().Be(18);
        var strip = sut.Continuous!;
        var placed = strip.PageLayoutOf(18);
        var top = placed.OffsetY + match.Bounds.Y * placed.Scale;
        top.Should().BeInRange(0, 400 - match.Bounds.Height * placed.Scale, "the match is in view");
        // A third of the way down would scroll above the page: the text is 62 points from its top.
        sut.ViewState.PageY.Should().BeApproximately(0, 0.01, "the page is shown from its top");
    }

    [Fact]
    public async Task AMatchAlreadyInViewDoesNotMoveThePage()
    {
        await using var sut = await CreateAsync();
        await sut.SearchAsync("page 1");
        await sut.WaitUntilAsync(() => sut.CurrentPage?.Content.Number == 1);
        var before = sut.ViewState;

        sut.ShowMatch(0);

        sut.ViewState.Should().Be(before);
    }
}
