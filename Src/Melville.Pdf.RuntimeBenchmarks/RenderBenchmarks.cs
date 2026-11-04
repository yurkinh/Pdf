using BenchmarkDotNet.Attributes;
using Melville.Pdf.FontLibrary;
using Melville.Pdf.LowLevel.Parsing.ParserContext;
using Melville.Pdf.Model;
using Melville.Pdf.Model.Renderers.DocumentRenderers;
using Melville.Pdf.ReferenceDocuments.Infrastructure;
using Melville.Pdf.SkiaSharp;
using SkiaSharp;

namespace Melville.Pdf.RuntimeBenchmarks;

/// <summary>
/// Renders page 1 of representative reference documents with the Skia backend.
/// Compared across jobs (net10.0, net11.0 with and without runtime async) in Program.cs.
/// </summary>
[MemoryDiagnoser]
public class RenderBenchmarks
{
    [Params(
        "-StandardFonts", "-EmbeddedTrueType",                        // text
        "-JpegImage", "-JBigSampleBitStream1", "-Jpeg2000ColorImage", // images
        "-ColoredTilePattern", "-Type2LinearShader")]                 // patterns and shadings
    public string Document { get; set; } = "";

    private byte[] pdf = [];
    private string password = "";
    private DocumentRenderer openRenderer = null!;

    [GlobalSetup]
    public async Task SetupAsync()
    {
        var generator = GeneratorFactory.AllGenerators.Single(i => i.Prefix == Document);
        password = generator.Password;
        using var bytes = new MemoryStream();
        await generator.WritePdfAsync(bytes);
        pdf = bytes.ToArray();
        openRenderer = await OpenAsync();
        using var _ = await RenderAsync(openRenderer); // warm the renderer's caches like a viewer redraw
    }

    [GlobalCleanup]
    public void Cleanup() => openRenderer.Dispose();

    /// <summary>The viewer redraw path: the document is already open.</summary>
    [Benchmark]
    public async Task<int> RenderOpenDocument()
    {
        using var surface = await RenderAsync(openRenderer);
        return surface.Canvas.DeviceClipBounds.Width;
    }

    /// <summary>The first-display path: parse the file, then render.</summary>
    [Benchmark]
    public async Task<int> OpenAndRender()
    {
        using var renderer = await OpenAsync();
        using var surface = await RenderAsync(renderer);
        return surface.Canvas.DeviceClipBounds.Width;
    }

    // Each open reads its own copy, as a viewer opening a file would. (Opening a second document over the
    // same IMultiplexSource while the first is open fails for -StandardFonts: a follow-up to investigate.)
    // Self-contained fonts so results don't depend on the machine's font folders.
    private ValueTask<DocumentRenderer> OpenAsync() =>
        new PdfReader(new ConstantPasswordSource(PasswordType.User, password), SelfContainedDefaultFonts.Instance)
            .ReadFromAsync(pdf.ToArray());

    private static ValueTask<SKSurface> RenderAsync(DocumentRenderer renderer) =>
        RenderWithSkia.ToSurfaceAsync(renderer, 1, -1, 1024);
}
