using System.Runtime.CompilerServices;
using System.Text.Json;
using Melville.Pdf.FontLibrary;
using Melville.Pdf.LowLevel.Parsing.ParserContext;
using Melville.Pdf.MauiGraphics.Drawing;
using Melville.Pdf.Model;
using Melville.Pdf.Model.Renderers.DocumentRenderers;
using SkiaSharp;
using Xunit;

namespace Melville.Pdf.MauiGraphics.Tests;

/// <summary>
/// The PDFs bundled with the sample app (Melville.Pdf.Maui.Sample/Resources/Raw/samples, written by
/// tools/BuildSampleCorpus.cs): each one opens with the password its index gives and every page records.
/// </summary>
public class SampleCorpusTest(ITestOutputHelper output)
{
    public sealed record Entry(string File, string Title, string Description, string? Password);

    private static string Folder([CallerFilePath] string here = "") =>
        Path.Combine(Path.GetDirectoryName(here)!, "..", "Melville.Pdf.Maui.Sample", "Resources", "Raw", "samples");

    private static IReadOnlyList<Entry> Index() =>
        JsonSerializer.Deserialize<List<Entry>>(File.ReadAllText(Path.Combine(Folder(), "index.json")),
            new JsonSerializerOptions(JsonSerializerDefaults.Web))!;

    public static TheoryData<string> Files() => new(Index().Select(e => e.File));

    [Fact]
    public void EveryFileInTheFolderIsIndexed()
    {
        var indexed = Index().Select(e => e.File).Order().ToList();
        var present = Directory.GetFiles(Folder(), "*.pdf").Select(Path.GetFileName).Order().ToList();
        Assert.Equal<string?>(present, indexed);
    }

    [Theory]
    [MemberData(nameof(Files))]
    public async Task EveryPageRecords(string file)
    {
        var entry = Index().Single(e => e.File == file);
        using var document = await OpenAsync(entry, entry.Password);
        Assert.True(document.TotalPages > 0);
        for (int page = 1; page <= document.TotalPages; page++)
        {
            var list = await RenderWithMauiGraphics.RecordPageAsync(document, page);
            Assert.True(list.PageWidth > 0 && list.PageHeight > 0, $"{file} page {page} has no size");
        }
    }

    [Fact]
    public async Task TheEncryptedSampleNeedsItsPassword()
    {
        var entry = Index().Single(e => e.Password == "melville");
        await Assert.ThrowsAnyAsync<Exception>(async () => (await OpenAsync(entry, "wrong")).Dispose());
    }

    [Fact]
    public async Task TheLayersSampleHasThreeLayers()
    {
        using var document = await OpenAsync(Index().Single(e => e.File == "layers.pdf"), null);
        var state = document.OptionalContentState;
        var layers = await state.ConstructUiModelAsync(state.SelectedConfiguration?.Order);
        Assert.Equal(["Grid", "Shapes", "Labels"], layers.Select(l => l.Name));
        Assert.All(layers, l => Assert.True(l.Visible, $"{l.Name} should start visible"));
    }

    [Fact]
    public async Task HidingALayerRemovesItsContent()
    {
        using var document = await OpenAsync(Index().Single(e => e.File == "layers.pdf"), null);
        var state = document.OptionalContentState;
        var shapes = (await state.ConstructUiModelAsync(state.SelectedConfiguration?.Order)).Single(l => l.Name == "Shapes");
        var shown = await RenderWithMauiGraphics.RecordPageAsync(document, 1);
        shapes.Visible = false;
        var hidden = await RenderWithMauiGraphics.RecordPageAsync(document, 1);
        Assert.Equal(shown.FillCount - 2, hidden.FillCount); // the red square and the blue bar
    }

    /// <summary>First page of each sample as a PNG in MELVILLE_DUMP_DIR (or the temp folder), for a visual check.</summary>
    [Fact(Explicit = true)]
    public async Task DumpFirstPages()
    {
        var folder = Environment.GetEnvironmentVariable("MELVILLE_DUMP_DIR") ?? Path.GetTempPath();
        foreach (var entry in Index())
        {
            using var document = await OpenAsync(entry, entry.Password);
            var page = await RenderWithMauiGraphics.RecordPageAsync(document, 1);
            const int width = 500;
            var height = (int)(page.PageHeight * (width / page.PageWidth));
            using var surface = SKSurface.Create(new SKImageInfo(width, height));
            surface.Canvas.Clear(SKColors.White);
            page.Draw(new FixedSkiaCanvas { Canvas = surface.Canvas }, SkiaImageFactory.Instance, CanvasQuirks.Default,
                width / page.PageWidth);
            var path = Path.Combine(folder, Path.ChangeExtension(entry.File, ".png"));
            using var data = surface.Snapshot().Encode(SKEncodedImageFormat.Png, 100);
            await File.WriteAllBytesAsync(path, data.ToArray(), TestContext.Current.CancellationToken);
            output.WriteLine($"{path} ({document.TotalPages} pages)");
        }
    }

    private static async Task<DocumentRenderer> OpenAsync(Entry entry, string? password) =>
        await new PdfReader(new ConstantPasswordSource(PasswordType.User, password ?? ""), SelfContainedDefaultFonts.Instance)
            .ReadFromAsync(await File.ReadAllBytesAsync(Path.Combine(Folder(), entry.File)));
}
