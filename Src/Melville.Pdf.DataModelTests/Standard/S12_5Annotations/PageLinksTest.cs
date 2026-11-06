using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Melville.Pdf.FontLibrary;
using Melville.Pdf.LowLevel.Model.Conventions;
using Melville.Pdf.LowLevel.Model.Objects;
using Melville.Pdf.LowLevel.Model.Wrappers;
using Melville.Pdf.LowLevel.Writers.Builder;
using Melville.Pdf.LowLevel.Writers.DocumentWriters;
using Melville.Pdf.Model;
using Melville.Pdf.Model.Creators;
using Melville.Pdf.Model.Documents;
using Melville.Pdf.Model.Renderers.DocumentRenderers;
using Melville.Pdf.ReferenceDocuments.Infrastructure;
using Melville.Pdf.ReferenceDocuments.PageProperties;
using Xunit;

namespace Melville.Pdf.DataModelTests.Standard.S12_5Annotations;

public class PageLinksTest
{
    [Fact]
    public async Task ReadsEveryKindOfLinkAsync()
    {
        using var document = await new Links().AsDocumentRendererAsync(SelfContainedDefaultFonts.Instance);

        var links = await document.GetPageLinksAsync(1);

        links.Select(l => l.Target).Should().Equal(
            new PdfPageLinkTarget(3, 0, 400),
            new PdfPageLinkTarget(2),
            new PdfPageLinkTarget(4, null, 792),
            new PdfPageLinkTarget(2),
            new PdfUriLinkTarget(Links.WebAddress));
        links[0].Area.Should().Be(new PdfRect(36, 680, 576, 710));
    }

    [Fact]
    public async Task GoToActionsAreFollowedAsync()
    {
        using var document = await new Links().AsDocumentRendererAsync(SelfContainedDefaultFonts.Instance);

        (await document.GetPageLinksAsync(2)).Single().Target.Should().Be(new PdfPageLinkTarget(1));
    }

    [Fact]
    public async Task ARotatedPagesLinksAreInItsVisibleBoxAsync()
    {
        using var document = await new Links().AsDocumentRendererAsync(SelfContainedDefaultFonts.Instance);
        var box = await document.GetPageBoxAsync(2);

        var area = (await document.GetPageLinksAsync(2)).Single().Area;

        // As a fraction of the page as drawn (top-left origin). Turned a quarter clockwise, the strip near the top
        // of the portrait page (y 680..710 of 792, x 36..576 of 612) is near the right edge of the landscape page.
        double FractionX(double x) => (x - box.Left) / (box.Right - box.Left);
        double FractionY(double y) => (box.Top - y) / (box.Top - box.Bottom);
        var xs = new[] { FractionX(area.Left), FractionX(area.Right) }.Order().ToArray();
        var ys = new[] { FractionY(area.Bottom), FractionY(area.Top) }.Order().ToArray();
        xs[0].Should().BeApproximately(680 / 792.0, 0.001);
        xs[1].Should().BeApproximately(710 / 792.0, 0.001);
        ys[0].Should().BeApproximately(36 / 612.0, 0.001);
        ys[1].Should().BeApproximately(576 / 612.0, 0.001);
    }

    [Fact]
    public async Task LinksThatCannotBeFollowedAreLeftOutAsync()
    {
        var builder = new PdfDocumentCreator();
        var page = builder.Pages.CreatePage();
        PdfIndirectObject Annotation(PdfDirectObject subtype, PdfDirectObject key, PdfIndirectObject value) =>
            new DictionaryBuilder()
                .WithItem(KnownNames.Subtype, subtype)
                .WithItem(KnownNames.Rect, new PdfArray(0, 0, 10, 10))
                .WithItem(key, value)
                .AsDictionary();
        page.AddMetadata(KnownNames.Annots, new PdfArray(
            Annotation(KnownNames.Link, KnownNames.Dest, PdfDirectObject.CreateName("Missing")),
            Annotation(KnownNames.Link, KnownNames.Dest, "missing"),
            Annotation(KnownNames.Link, KnownNames.Dest, new PdfArray(42, KnownNames.Fit)), // no page 43
            Annotation(KnownNames.Link, KnownNames.A, new DictionaryBuilder()
                .WithItem(KnownNames.S, KnownNames.Named).WithItem(KnownNames.N, KnownNames.NextPage).AsDictionary()),
            Annotation(KnownNames.Watermark, KnownNames.Dest, new PdfArray(0, KnownNames.Fit)), // not a link
            Annotation(KnownNames.Link, KnownNames.Dest, new PdfArray(0, KnownNames.Fit)))); // page index 0: page 1
        using var bytes = new MemoryStream();
        await builder.CreateDocument().WriteToAsync(bytes);
        using var document = await new PdfReader(SelfContainedDefaultFonts.Instance).ReadFromAsync(bytes.ToArray());

        (await document.GetPageLinksAsync(1)).Select(l => l.Target).Should().Equal(new PdfPageLinkTarget(1));
    }

    [Fact]
    public async Task APageWithoutAnnotationsHasNoLinksAsync()
    {
        var builder = new PdfDocumentCreator();
        builder.Pages.CreatePage();
        using var bytes = new MemoryStream();
        await builder.CreateDocument().WriteToAsync(bytes);
        using var plain = await new PdfReader(SelfContainedDefaultFonts.Instance).ReadFromAsync(bytes.ToArray());

        (await plain.GetPageLinksAsync(1)).Should().BeEmpty();
    }
}
