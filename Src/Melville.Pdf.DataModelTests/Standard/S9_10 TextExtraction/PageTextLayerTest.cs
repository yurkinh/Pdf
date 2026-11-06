using System.IO;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using FluentAssertions;
using Melville.Pdf.FontLibrary;
using Melville.Pdf.LowLevel.Model.Conventions;
using Melville.Pdf.LowLevel.Model.Objects;
using Melville.Pdf.LowLevel.Writers.Builder;
using Melville.Pdf.LowLevel.Writers.DocumentWriters;
using Melville.Pdf.Model;
using Melville.Pdf.Model.Creators;
using Melville.Pdf.Model.Renderers.DocumentRenderers;
using Melville.Pdf.ReferenceDocuments.Infrastructure;
using Melville.Pdf.ReferenceDocuments.PageProperties;
using Melville.Pdf.ReferenceDocuments.Text.TrueType;
using Melville.Pdf.TextExtractor;
using Xunit;

namespace Melville.Pdf.DataModelTests.Standard.S9_10_TextExtraction;

public class PageTextLayerTest
{
    private static ValueTask<DocumentRenderer> LinksAsync() =>
        new Links().AsDocumentRendererAsync(SelfContainedDefaultFonts.Instance);

    [Fact]
    public async Task TextIsJoinedIntoWordsAndLinesAsync()
    {
        using var document = await LinksAsync();

        var layer = await document.PageTextLayerAsync(1);

        layer.Text.Split('\n').Should().Equal(
            "Links",
            "Page 3, at the line marked 400 (explicit /XYZ destination)",
            "Page 2 (the name /Chapter2 in the catalog's /Dests)",
            "The appendix (the string 'appendix' in the /Names tree)",
            "Next page (Named action)",
            "The Melville.Pdf project on GitHub (URI action)");
    }

    [Fact]
    public async Task GlyphsAreInTopLeftPagePointsAsync()
    {
        using var document = await LinksAsync();

        var first = (await document.PageTextLayerAsync(1)).Glyphs[0];

        // "Links" is 24 pt Helvetica with its baseline at y = 740 of 792: 52 points below the top.
        first.Text.Should().Be("L");
        first.BaselineStart.Should().Be(new Vector2(36, 52));
        first.Ascent.Y.Should().BeApproximately(-0.8f * 24, 0.001f, "up the page is towards y = 0");
        var (left, top, right, bottom) = first.Bounds;
        (left, top, bottom).Should().Be((36f, 52 - 19.2f, 52 + 4.8f));
        right.Should().BeGreaterThan(left + 10, "an L is about half an em wide");
    }

    [Fact]
    public async Task ARotatedPagesTextTurnsWithItAsync()
    {
        using var document = await LinksAsync();

        var layer = await document.PageTextLayerAsync(2);

        layer.Text.Should().StartWith("Chapter 2 (rotated 90 degrees)\nBack to page 1");
        // A quarter turn clockwise: the title, 36 points from the left of the portrait page, runs down the
        // landscape page from 36 points below its top, at x = 740 (its distance from the portrait bottom).
        var c = layer.Glyphs[0];
        c.BaselineStart.X.Should().BeApproximately(740, 0.01f);
        c.BaselineStart.Y.Should().BeApproximately(36, 0.01f);
        (c.BaselineEnd - c.BaselineStart).Y.Should().BeGreaterThan(0, "the text runs down");
    }

    [Fact]
    public async Task EachPageGetsOnlyItsOwnTextAsync()
    {
        // Wrapped fonts are cached per document, so a font used on page 1 is reused on page 2.
        using var document = await LinksAsync();

        var first = await document.PageTextLayerAsync(1);
        var third = await document.PageTextLayerAsync(3);
        var firstAgain = await document.PageTextLayerAsync(1);

        third.Text.Should().StartWith("Page 3\n").And.NotContain("Links");
        firstAgain.Text.Should().Be(first.Text);
        firstAgain.Glyphs.Should().Equal(first.Glyphs);
    }

    [Fact]
    public async Task PositionsInTheTextMapBackToGlyphsAsync()
    {
        using var document = await LinksAsync();
        var layer = await document.PageTextLayerAsync(1);

        var start = layer.Text.IndexOf("marked 400");
        var glyphs = layer.GlyphsIn(start, "marked 400".Length).ToList();

        string.Concat(glyphs.Select(g => g.Text)).Should().Be("marked 400", "the document draws its spaces");
        layer.GlyphAt(layer.Text.IndexOf('\n')).Should().Be(-1);
        glyphs.Select(g => g.BaselineStart.Y).Distinct().Should().ContainSingle("they are on one line");
    }

    [Fact]
    public async Task TrueTypeTextIsReadAsync()
    {
        using var document = await new EmbeddedTrueType().AsDocumentRendererAsync(SelfContainedDefaultFonts.Instance);

        var layer = await document.PageTextLayerAsync(1);

        layer.Text.Should().Be("Is Text\nIs Text");
        layer.Glyphs.Should().OnlyContain(g => g.BaselineEnd.X > g.BaselineStart.X);
    }

    [Fact]
    public async Task ToUnicodeMapsGiveTheTextAndTheFontKeepsItsWidthsAsync()
    {
        // Helvetica, with a ToUnicode map: code A is Cyrillic Zhe, code B the two letters "fi" (a ligature).
        var builder = new PdfDocumentCreator();
        var page = builder.Pages.CreatePage();
        var toUnicode = builder.LowLevelCreator.Add(new DictionaryBuilder().AsStream("""
            /CIDInit /ProcSet findresource begin
            12 dict begin
            begincmap
            /CMapName /Test def
            1 begincodespacerange <00> <FF> endcodespacerange
            2 beginbfchar
            <41> <0416>
            <42> <00660069>
            endbfchar
            endcmap
            CMapName currentdict /CMap defineresource pop
            end
            end
            """));
        page.AddResourceObject(ResourceTypeName.Font, PdfDirectObject.CreateName("F1"), new DictionaryBuilder()
            .WithItem(KnownNames.Type, KnownNames.Font)
            .WithItem(KnownNames.Subtype, KnownNames.Type1)
            .WithItem(KnownNames.BaseFont, PdfDirectObject.CreateName("Helvetica"))
            .WithItem(KnownNames.ToUnicode, toUnicode)
            .AsDictionary());
        await page.AddToContentStreamAsync(async csw =>
        {
            using var block = csw.StartTextBlock();
            await csw.SetFontAsync(PdfDirectObject.CreateName("F1"), 10);
            block.MovePositionBy(100, 700);
            await block.ShowStringAsync("ABA");
        });
        using var bytes = new MemoryStream();
        await builder.CreateDocument().WriteToAsync(bytes);
        using var document = await new PdfReader(SelfContainedDefaultFonts.Instance).ReadFromAsync(bytes.ToArray());

        var layer = await document.PageTextLayerAsync(1);

        layer.Text.Should().Be("ЖfiЖ");
        layer.Glyphs.Select(g => g.Text).Should().Equal("Ж", "fi", "Ж");
        layer.GlyphsIn(1, 2).Should().ContainSingle("both letters of the ligature come from one glyph");
        // The glyphs are Helvetica's A and B, so the positions follow their widths, not the mapped text's.
        layer.Glyphs[1].BaselineStart.X.Should().BeApproximately(layer.Glyphs[0].BaselineEnd.X, 0.01f);
        (layer.Glyphs[0].BaselineEnd.X - layer.Glyphs[0].BaselineStart.X).Should().BeInRange(6, 7.5f, "an A is about 0.67 em");
    }

    [Fact]
    public async Task WordsPlacedApartGetASpaceAndAdjacentStringsDoNotAsync()
    {
        var builder = new PdfDocumentCreator();
        builder.Pages.AddStandardFont("F1"u8, BuiltInFontName.Helvetica, FontEncodingName.WinAnsiEncoding);
        var page = builder.Pages.CreatePage();
        await page.AddToContentStreamAsync(async csw =>
        {
            using var block = csw.StartTextBlock();
            await csw.SetFontAsync(PdfDirectObject.CreateName("F1"), 10);
            block.MovePositionBy(100, 700);
            await block.ShowStringAsync("Hel");
            await block.ShowStringAsync("lo");     // continues where "Hel" ended
            block.MovePositionBy(60, 0);           // 60 points on from the start of "Hel": a word gap
            await block.ShowStringAsync("world");
            block.MovePositionBy(0, -20);          // the next line
            await block.ShowStringAsync("again");
        });
        using var bytes = new MemoryStream();
        await builder.CreateDocument().WriteToAsync(bytes);
        using var document = await new PdfReader(SelfContainedDefaultFonts.Instance).ReadFromAsync(bytes.ToArray());

        var layer = await document.PageTextLayerAsync(1);

        layer.Text.Should().Be("Hello world\nagain");
        layer.GlyphAt(5).Should().Be(-1, "the space was inserted");
    }

    [Fact]
    public async Task APageWithoutTextHasAnEmptyLayerAsync()
    {
        var builder = new PdfDocumentCreator();
        builder.Pages.CreatePage();
        using var bytes = new MemoryStream();
        await builder.CreateDocument().WriteToAsync(bytes);
        using var document = await new PdfReader(SelfContainedDefaultFonts.Instance).ReadFromAsync(bytes.ToArray());

        var layer = await document.PageTextLayerAsync(1);

        layer.Text.Should().BeEmpty();
        layer.Glyphs.Should().BeEmpty();
    }
}
