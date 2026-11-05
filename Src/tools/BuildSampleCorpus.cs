#:project ../Melville.Pdf.ReferenceDocuments/Melville.Pdf.ReferenceDocuments.csproj
#:property PublishAot=false

// Writes the PDFs bundled with Melville.Pdf.Maui.Sample (Resources/Raw/samples) and their index.json.
// Every document is generated here or by Melville.Pdf.ReferenceDocuments, so the corpus has no licensing
// questions. Run from Src:  ./.dotnet/dotnet run tools/BuildSampleCorpus.cs
//
// The output is committed; rerun only when the list changes. Encrypted documents use random keys, so their
// bytes change on every run.

using System.Text;
using System.Text.Json;
using Melville.Pdf.LowLevel.Encryption.SecurityHandlers;
using Melville.Pdf.LowLevel.Model.Conventions;
using Melville.Pdf.LowLevel.Model.Objects;
using Melville.Pdf.LowLevel.Model.Wrappers;
using Melville.Pdf.LowLevel.Writers;
using Melville.Pdf.LowLevel.Writers.Builder;
using Melville.Pdf.LowLevel.Writers.ContentStreams;
using Melville.Pdf.LowLevel.Writers.DocumentWriters;
using Melville.Pdf.Model.Creators;
using Melville.Pdf.ReferenceDocuments.Infrastructure;

var output = Path.GetFullPath(args.Length > 0 ? args[0] : "Melville.Pdf.Maui.Sample/Resources/Raw/samples");
Directory.CreateDirectory(output);
var index = new List<Entry>();

await WriteAsync("text-multipage.pdf", "Long text document", "120 pages of text in the standard fonts.",
    s => TextDocumentAsync(s, pages: 120));
await WriteAsync("large-page.pdf", "Very large page", "One A0 page (2384 × 3370 pt) with fine line work.",
    LargePageAsync);
await WriteAsync("layers.pdf", "Layers", "Three optional-content groups; turn them on and off in the layers panel.",
    LayersAsync);
await WriteAsync("encrypted.pdf", "Encrypted (password: melville)", "AES-256 encryption. The user password is \"melville\".",
    EncryptedAsync, password: "melville");

// Reference documents that exercise the image codecs, patterns and shadings.
string[][] references =
[
    ["-JpegImage", "JPEG image", "DCT-encoded colour image."],
    ["-Jpeg2000ColorImage", "JPEG 2000 image", "JPX-encoded colour image."],
    ["-JBigSampleBitStream1", "JBIG2 scan", "JBIG2-encoded bilevel image, as produced by scanners."],
    ["-CcittType4Encoded", "CCITT fax scan", "CCITT Group 4 encoded bilevel image."],
    ["-ColoredTilePattern", "Tiling pattern", "Coloured tiling pattern used as a fill."],
    ["-Type3RadialShader", "Radial shading", "Type 3 (radial) shading."],
];
var generators = GeneratorFactory.AllGenerators.ToDictionary(g => g.Prefix);
foreach (var (prefix, title, description) in references.Select(r => (r[0], r[1], r[2])))
{
    var generator = generators[prefix];
    await WriteAsync($"reference{prefix}.pdf", title, description, s => generator.WritePdfAsync(s).AsTask(),
        password: generator.Password.Length > 0 ? generator.Password : null);
}

await File.WriteAllTextAsync(Path.Combine(output, "index.json"),
    JsonSerializer.Serialize(index, new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
Console.WriteLine($"{index.Count} documents written to {output}");

async Task WriteAsync(string file, string title, string description, Func<Stream, Task> write, string? password = null)
{
    await using (var stream = File.Create(Path.Combine(output, file))) await write(stream);
    index.Add(new Entry(file, title, description, password));
    Console.WriteLine($"  {file} ({new FileInfo(Path.Combine(output, file)).Length / 1024} KB)");
}

static async Task TextDocumentAsync(Stream target, int pages)
{
    var creator = new PdfDocumentCreator();
    creator.Pages.AddBox(BoxName.MediaBox, new PdfRect(0, 0, 612, 792));
    for (int page = 1; page <= pages; page++)
    {
        var pc = creator.Pages.CreatePage();
        var body = pc.AddStandardFont("/F1", BuiltInFontName.TimesRoman, FontEncodingName.WinAnsiEncoding);
        var heading = pc.AddStandardFont("/F2", BuiltInFontName.HelveticaBold, FontEncodingName.WinAnsiEncoding);
        var lines = Prose.Lines(page, charactersPerLine: 92).Take(48).ToList();
        await pc.AddToContentStreamAsync(async w =>
        {
            using (var block = w.StartTextBlock())
            {
                await w.SetFontAsync(heading, 16);
                block.MovePositionBy(72, 720);
                await block.ShowStringAsync($"Chapter {(page + 9) / 10}, page {page}");
                await w.SetFontAsync(body, 11);
                w.SetTextLeading(12.5);
                block.MovePositionBy(0, -28);
                foreach (var line in lines) await block.MoveToNextLineAndShowStringAsync(line);
            }
            using (var block = w.StartTextBlock())
            {
                await w.SetFontAsync(body, 9);
                block.MovePositionBy(290, 40);
                await block.ShowStringAsync($"{page} / {pages}");
            }
        });
    }
    await creator.CreateDocument().WriteToAsync(target);
}

static async Task LargePageAsync(Stream target)
{
    const double width = 2384, height = 3370;
    var creator = new PdfDocumentCreator();
    var pc = creator.Pages.CreatePage();
    pc.AddBox(BoxName.MediaBox, new PdfRect(0, 0, width, height));
    var font = pc.AddStandardFont("/F1", BuiltInFontName.Helvetica, FontEncodingName.WinAnsiEncoding);
    await pc.AddToContentStreamAsync(async w =>
    {
        // A 10 pt grid of hairlines, heavier every 100 pt: detail that only shows when zoomed in.
        for (double x = 0; x <= width; x += 10)
        {
            w.SetLineWidth(x % 100 == 0 ? 1.5 : 0.25);
            await w.SetStrokeGrayAsync(x % 100 == 0 ? 0.4 : 0.8);
            w.MoveTo(x, 0); w.LineTo(x, height); w.StrokePath();
        }
        for (double y = 0; y <= height; y += 10)
        {
            w.SetLineWidth(y % 100 == 0 ? 1.5 : 0.25);
            await w.SetStrokeGrayAsync(y % 100 == 0 ? 0.4 : 0.8);
            w.MoveTo(0, y); w.LineTo(width, y); w.StrokePath();
        }
        await w.SetNonstrokingRgbAsync(0.1, 0.3, 0.7);
        using var block = w.StartTextBlock();
        await w.SetFontAsync(font, 160);
        block.MovePositionBy(200, height - 400);
        await block.ShowStringAsync("A0 page");
        await w.SetFontAsync(font, 4);
        block.MovePositionBy(0, -200);
        await block.ShowStringAsync("This line is 4 pt tall: zoom in to read it.");
    });
    await creator.CreateDocument().WriteToAsync(target);
}

static async Task LayersAsync(Stream target)
{
    var creator = new PdfDocumentCreator();
    string[] names = ["Grid", "Shapes", "Labels"];
    var groups = names.Select(n => creator.LowLevelCreator.Add(new DictionaryBuilder()
        .WithItem(KnownNames.Type, KnownNames.OCG)
        .WithItem(KnownNames.Name, n)
        // Spelled out: Melville.Pdf treats a group without /Intent as hidden (the spec's default is /View).
        .WithItem(KnownNames.Intent, KnownNames.View)
        .AsDictionary())).ToArray();
    creator.AddToRootDictionary(KnownNames.OCProperties, creator.LowLevelCreator.Add(new DictionaryBuilder()
        .WithItem(KnownNames.OCGs, new PdfArray(groups))
        .WithItem(KnownNames.D, new DictionaryBuilder()
            .WithItem(KnownNames.BaseState, KnownNames.ON)
            .WithItem(KnownNames.Order, new PdfArray(groups))
            .AsDictionary())
        .AsDictionary()));

    var pc = creator.Pages.CreatePage();
    pc.AddBox(BoxName.MediaBox, new PdfRect(0, 0, 612, 792));
    for (int i = 0; i < groups.Length; i++)
        pc.AddResourceObject(ResourceTypeName.Properties, PdfDirectObject.CreateName($"L{i}"), groups[i]);
    var font = pc.AddStandardFont("/F1", BuiltInFontName.Helvetica, FontEncodingName.WinAnsiEncoding);
    await pc.AddToContentStreamAsync(async w =>
    {
        using (await w.BeginMarkedRangeAsync(KnownNames.OC, PdfDirectObject.CreateName("L0")))
        {
            await w.SetStrokeGrayAsync(0.75);
            for (int x = 36; x <= 576; x += 36) { w.MoveTo(x, 36); w.LineTo(x, 756); }
            for (int y = 36; y <= 756; y += 36) { w.MoveTo(36, y); w.LineTo(576, y); }
            w.StrokePath();
        }
        using (await w.BeginMarkedRangeAsync(KnownNames.OC, PdfDirectObject.CreateName("L1")))
        {
            await w.SetNonstrokingRgbAsync(0.9, 0.3, 0.2);
            w.Rectangle(108, 468, 180, 180); w.FillPath();
            await w.SetNonstrokingRgbAsync(0.2, 0.5, 0.9);
            w.Rectangle(324, 252, 180, 252); w.FillPath();
        }
        using (await w.BeginMarkedRangeAsync(KnownNames.OC, PdfDirectObject.CreateName("L2")))
        {
            await w.SetNonstrokingGrayAsync(0);
            using var block = w.StartTextBlock();
            await w.SetFontAsync(font, 18);
            block.MovePositionBy(108, 440);
            await block.ShowStringAsync("Red square (Shapes)");
            block.MovePositionBy(216, -216);
            await block.ShowStringAsync("Blue bar (Shapes)");
        }
    });
    await creator.CreateDocument().WriteToAsync(target);
}

static async Task EncryptedAsync(Stream target)
{
    var creator = new PdfDocumentCreator();
    creator.LowLevelCreator.AddEncryption(DocumentEncryptorFactory.V6("melville", "melville-owner", PdfPermission.None));
    var pc = creator.Pages.CreatePage();
    pc.AddBox(BoxName.MediaBox, new PdfRect(0, 0, 612, 792));
    var font = pc.AddStandardFont("/F1", BuiltInFontName.TimesRoman, FontEncodingName.WinAnsiEncoding);
    await pc.AddToContentStreamAsync(async w =>
    {
        using var block = w.StartTextBlock();
        await w.SetFontAsync(font, 24);
        block.MovePositionBy(72, 700);
        await block.ShowStringAsync("This document was encrypted with AES-256.");
        await w.SetFontAsync(font, 14);
        block.MovePositionBy(0, -36);
        await block.ShowStringAsync("You opened it with the user password \"melville\".");
    });
    await creator.CreateDocument().WriteToAsync(target, "melville");
}

internal sealed record Entry(string File, string Title, string Description, string? Password);

internal static class Prose
{
    private static readonly string[] Sentences =
    [
        "A PDF page is a program: a content stream of operators that move a pen, set colours and show glyphs.",
        "The viewer interprets that program once and records the drawing commands it produces.",
        "Every redraw then replays the recording, scaled and offset for the current zoom and scroll position.",
        "Pinch to zoom around your fingers, drag to move the page, and double-tap to return to the whole page.",
        "Fonts are embedded in the document or supplied by the font library that ships with the control.",
        "Images arrive compressed with JPEG, JPEG 2000, JBIG2, CCITT fax or plain Flate encoding.",
        "Tiling patterns repeat a small cell across a shape, while shadings blend colours smoothly over an area.",
        "Optional content lets one page carry layers that a reader can show or hide independently.",
        "This text is generated, so the sample carries no third-party content and no licensing questions.",
        "Long documents test the page selector: jump to the last page, then type a page number to come back.",
    ];

    public static IEnumerable<string> Lines(int seed, int charactersPerLine)
    {
        var line = new StringBuilder();
        for (int i = 0; ; i++)
        {
            foreach (var word in Sentences[(seed * 7 + i) % Sentences.Length].Split(' '))
            {
                if (line.Length + word.Length + 1 > charactersPerLine)
                {
                    yield return line.ToString();
                    line.Clear();
                }
                if (line.Length > 0) line.Append(' ');
                line.Append(word);
            }
            if (i % 4 == 3)
            {
                yield return line.ToString();
                yield return "";
                line.Clear();
            }
        }
    }
}
