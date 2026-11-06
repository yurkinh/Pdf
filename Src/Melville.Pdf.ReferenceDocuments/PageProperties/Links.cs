using Melville.Pdf.LowLevel.Model.Wrappers;
using Melville.Pdf.LowLevel.Writers.ContentStreams;

namespace Melville.Pdf.ReferenceDocuments.PageProperties;

/// <summary>
/// Four pages of link annotations, one of each kind a viewer follows: an explicit destination with a position, a
/// named destination in the catalog's /Dests dictionary, one in the /Names /Dests name tree, GoTo, Named and URI
/// actions, and a link on a page rotated by 90 degrees. Every link area is outlined in blue.
/// </summary>
public class Links : CreatePdfParser
{
    /// <summary>The URI of the web link on the first page.</summary>
    public const string WebAddress = "https://github.com/DrJohnMelville/Pdf";

    public Links() : base("Links to pages, named destinations and a web address")
    {
    }

    public override async ValueTask WritePdfAsync(Stream target)
    {
        var creator = new PdfDocumentCreator();
        creator.Pages.AddBox(BoxName.MediaBox, new PdfRect(0, 0, 612, 792));
        creator.Pages.AddStandardFont("F1"u8, BuiltInFontName.Helvetica, FontEncodingName.WinAnsiEncoding);
        var pages = new PageCreator[4];
        var references = new PdfIndirectObject[4];
        for (int i = 0; i < pages.Length; i++)
        {
            pages[i] = creator.Pages.CreatePage();
            references[i] = pages[i].InitializePromiseObject(creator.LowLevelCreator);
        }

        // Page 1: one link of each kind.
        await AddPageAsync(pages[0], "Links",
        [
            ("Page 3, at the line marked 400 (explicit /XYZ destination)",
                Dest(new PdfArray(references[2], KnownNames.XYZ, 0, 400, PdfDirectObject.CreateNull()))),
            ("Page 2 (the name /Chapter2 in the catalog's /Dests)", Dest(PdfDirectObject.CreateName("Chapter2"))),
            ("The appendix (the string 'appendix' in the /Names tree)", Dest("appendix")),
            ("Next page (Named action)", Action(KnownNames.Named, KnownNames.N, KnownNames.NextPage)),
            ("The Melville.Pdf project on GitHub (URI action)", Action(KnownNames.URI, KnownNames.URI, WebAddress)),
        ]);

        // Page 2: rotated a quarter turn; its link still has to line up with its outline.
        pages[1].AddRotate(90);
        await AddPageAsync(pages[1], "Chapter 2 (rotated 90 degrees)",
            [("Back to page 1 (GoTo action)", Action(KnownNames.GoTo, KnownNames.D, new PdfArray(references[0], KnownNames.Fit)))]);

        // Page 3: the explicit destination's target line.
        await AddPageAsync(pages[2], "Page 3", [("Back to page 1", Dest(new PdfArray(references[0], KnownNames.Fit)))],
            csw =>
            {
                csw.MoveTo(36, 400);
                csw.LineTo(576, 400);
                csw.StrokePath();
            }, ("400 - the link lands here", 40, 404));

        await AddPageAsync(pages[3], "Appendix", [("Back to page 1", Dest(new PdfArray(references[0], KnownNames.Fit)))]);

        creator.AddToRootDictionary(KnownNames.Dests, new DictionaryBuilder()
            .WithItem(PdfDirectObject.CreateName("Chapter2"), new PdfArray(references[1], KnownNames.Fit))
            .AsDictionary());
        creator.AddToRootDictionary(KnownNames.Names, new DictionaryBuilder()
            .WithItem(KnownNames.Dests, new DictionaryBuilder()
                .WithItem(KnownNames.Names, new PdfArray("appendix",
                    new DictionaryBuilder().WithItem(KnownNames.D, new PdfArray(references[3], KnownNames.FitH, 792)).AsDictionary()))
                .AsDictionary())
            .AsDictionary());
        await creator.CreateDocument().WriteToAsync(target);
    }

    private static (PdfDirectObject Key, PdfIndirectObject Value) Dest(PdfIndirectObject destination) =>
        (KnownNames.Dest, destination);

    private static (PdfDirectObject Key, PdfIndirectObject Value) Action(
        PdfDirectObject kind, PdfDirectObject key, PdfIndirectObject value) =>
        (KnownNames.A, new DictionaryBuilder()
            .WithItem(KnownNames.S, kind)
            .WithItem(key, value)
            .AsDictionary());

    private const double Left = 36, LinkHeight = 30, FirstLink = 680, Spacing = 48;

    private static async ValueTask AddPageAsync(PageCreator page, string title,
        (string Text, (PdfDirectObject Key, PdfIndirectObject Value) Target)[] links,
        Action<ContentStreamWriter>? extra = null, (string Text, double X, double Y)? note = null)
    {
        var annotations = new List<PdfIndirectObject>();
        for (int i = 0; i < links.Length; i++)
        {
            var bottom = FirstLink - i * Spacing;
            annotations.Add(new DictionaryBuilder()
                .WithItem(KnownNames.Type, KnownNames.Annot)
                .WithItem(KnownNames.Subtype, KnownNames.Link)
                .WithItem(KnownNames.Rect, new PdfArray(Left, bottom, 576, bottom + LinkHeight))
                .WithItem(links[i].Target.Key, links[i].Target.Value)
                .AsDictionary());
        }
        page.AddMetadata(KnownNames.Annots, new PdfArray(annotations.ToArray()));
        await page.AddToContentStreamAsync(async csw =>
        {
            await WriteAsync(csw, title, 24, Left, 740);
            await csw.SetStrokeRGBAsync(0, 0, 1);
            for (int i = 0; i < links.Length; i++)
            {
                var bottom = FirstLink - i * Spacing;
                csw.Rectangle(Left, bottom, 576 - Left, LinkHeight);
                csw.StrokePath();
                await WriteAsync(csw, links[i].Text, 14, Left + 8, bottom + 10);
            }
            extra?.Invoke(csw);
            if (note is { } n) await WriteAsync(csw, n.Text, 12, n.X, n.Y);
        });
    }

    private static async ValueTask WriteAsync(ContentStreamWriter csw, string text, double size, double x, double y)
    {
        using var block = csw.StartTextBlock();
        await csw.SetFontAsync(PdfDirectObject.CreateName("F1"), size);
        block.MovePositionBy(x, y);
        await block.ShowStringAsync(text);
    }
}
