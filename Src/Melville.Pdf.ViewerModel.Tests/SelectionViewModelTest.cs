using FluentAssertions;
using Melville.Pdf.TextExtractor;
using Xunit;

namespace Melville.Pdf.ViewerModel.Tests;

/// <summary>Finding the text under a point (page 1 of the Links reference document).</summary>
public class TextHitTestTest
{
    [Fact]
    public async Task ThePointOnAGlyphFindsItsCharacter()
    {
        using var document = await TestDocuments.FromGeneratorAsync("-Links");
        var text = await document.PageTextLayerAsync(1);

        // "Links": 24 pt Helvetica, baseline 52 points from the top, starting at x = 36.
        text.Text[TextHitTest.IndexAt(text, 38, 45)].Should().Be('L');
        text.Text[TextHitTest.IndexAt(text, 30, 45)].Should().Be('L', "the nearest glyph is found from the margin");
        TextHitTest.IndexAt(PageText.Empty, 10, 10).Should().Be(-1);
    }

    [Theory]
    [InlineData("hello world", 2, "hello")]
    [InlineData("hello world", 5, " ")]
    [InlineData("don't stop", 1, "don't")]
    [InlineData("a well-known fact", 4, "well-known")]
    [InlineData("(explicit /XYZ)", 0, "(")]
    [InlineData("page 400.", 6, "400")]
    public void AWordIsLettersAndDigitsJoinedByApostrophesAndHyphens(string text, int index, string word)
    {
        var (start, end) = TextHitTest.WordAt(text, index);

        text[start..end].Should().Be(word);
    }
}

/// <summary>Selecting text through the view model: Links page 1 at fit page, scale 1, so viewport = page points.</summary>
public class SelectionViewModelTest
{
    private static async Task<PdfViewerViewModel<FakePage>> CreateAsync()
    {
        var sut = await PdfViewerViewModel<FakePage>.CreateAsync(
            await TestDocuments.FromGeneratorAsync("-Links"), new FakePageRenderer());
        sut.SetViewport(612, 792);
        await sut.WaitUntilAsync(() => sut.VisiblePages.Count == 1);
        return sut;
    }

    [Fact]
    public async Task PressingAWordSelectsIt()
    {
        await using var sut = await CreateAsync();

        (await sut.SelectWordAtAsync(45, 45)).Should().BeTrue();

        sut.Selection!.Text.Should().Be("Links");
        sut.Selection.Page.Should().Be(1);
        sut.Selection.Areas.Should().ContainSingle().Which.X.Should().BeApproximately(36, 0.01);
    }

    [Fact]
    public async Task PressingAMarginSelectsNothing()
    {
        await using var sut = await CreateAsync();
        await sut.SelectWordAtAsync(45, 45);

        (await sut.SelectWordAtAsync(300, 780)).Should().BeFalse();

        sut.Selection.Should().BeNull();
    }

    [Fact]
    public async Task DraggingExtendsTheSelectionFromTheFirstWordAcrossLines()
    {
        await using var sut = await CreateAsync();
        var text = (await sut.GetTextAsync(1)).Text;
        await sut.SelectWordAtAsync(45, 45);

        // Down to the next line: from "Links" up to and including the glyph under the point.
        await sut.ExtendSelectionToAsync(70, 98);

        sut.Selection!.Text.Should().StartWith("Links\nPage");
        sut.Selection.Start.Should().Be(0);
        sut.Selection.Areas.Should().HaveCount(2, "one area per line");
        text.Should().StartWith(sut.Selection.Text);
    }

    [Fact]
    public async Task DraggingBeforeTheFirstWordSelectsBackwardsAndKeepsTheWord()
    {
        await using var sut = await CreateAsync();
        var text = (await sut.GetTextAsync(1)).Text;
        var line = text.IndexOf("Page 3");
        await sut.SelectWordAtAsync(45, 45);
        await sut.ExtendSelectionToAsync(70, 98);
        var forward = sut.Selection!;

        await sut.SelectWordAtAsync(forward.Areas[1].Right - 2, 98); // a word on the second line
        var word = sut.Selection!;
        await sut.ExtendSelectionToAsync(38, 45);                      // back up to "Links"

        sut.Selection!.Start.Should().Be(0);
        (sut.Selection.Start + sut.Selection.Length).Should().Be(word.Start + word.Length);
        line.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task SelectAllTakesTheWholePageAndClearingForgetsIt()
    {
        await using var sut = await CreateAsync();

        sut.SelectAllCommand.Execute(null);
        await sut.WaitUntilAsync(() => sut.Selection is not null);

        sut.Selection!.Text.Should().Be((await sut.GetTextAsync(1)).Text);
        sut.ClearSelection();
        sut.Selection.Should().BeNull();
    }

    [Fact]
    public async Task GoingToAnotherPageClearsTheSelection()
    {
        await using var sut = await CreateAsync();
        await sut.SelectWordAtAsync(45, 45);

        sut.PageSelector.Page = 2;

        sut.Selection.Should().BeNull();
    }
}
