using BenchmarkDotNet.Attributes;
using Melville.Pdf.FontLibrary;
using Melville.Pdf.LowLevel.Parsing.ParserContext;
using Melville.Pdf.MauiGraphics;
using Melville.Pdf.MauiGraphics.Drawing;
using Melville.Pdf.Model;
using Melville.Pdf.Model.Renderers.DocumentRenderers;
using Melville.Pdf.ReferenceDocuments.Infrastructure;
using Melville.Pdf.SkiaSharp;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Graphics.Skia;
using SkiaSharp;
using IImage = Microsoft.Maui.Graphics.IImage;

namespace Melville.Pdf.RuntimeBenchmarks;

/// <summary>
/// The Maui.Graphics renderer against the existing SkiaSharp renderer on an open document: recording the display
/// list (done once per page change) and replaying it (done on every redraw, so it must be cheap).
/// Run: dotnet run -c Release -f net10.0 -- --filter "*RendererBenchmarks*" --preset short
/// </summary>
[MemoryDiagnoser]
public class RendererBenchmarks
{
    [Params("-StandardFonts", "-EmbeddedTrueType", "-JpegImage", "-ColoredTilePattern", "-Type2LinearShader")]
    public string Document { get; set; } = "";

    private DocumentRenderer renderer = null!;
    private PdfDisplayList page = null!;
    private SKSurface surface = null!;

    [GlobalSetup]
    public async Task SetupAsync()
    {
        var generator = GeneratorFactory.AllGenerators.Single(i => i.Prefix == Document);
        using var bytes = new MemoryStream();
        await generator.WritePdfAsync(bytes);
        renderer = await new PdfReader(new ConstantPasswordSource(PasswordType.User, generator.Password),
            SelfContainedDefaultFonts.Instance).ReadFromAsync(bytes.ToArray());
        page = await RenderWithMauiGraphics.RecordPageAsync(renderer, 1);
        surface = SKSurface.Create(new SKImageInfo(1024, (int)(1024 * page.PageHeight / page.PageWidth)));
        Replay(); // create the platform images once, as the first draw on screen would
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        surface.Dispose();
        renderer.Dispose();
    }

    [Benchmark(Baseline = true)]
    public async Task<int> SkiaSharpRender()
    {
        using var result = await RenderWithSkia.ToSurfaceAsync(renderer, 1, 1024, -1);
        return result.Canvas.DeviceClipBounds.Width;
    }

    [Benchmark]
    public async Task<int> MauiGraphicsRecord() => (await RenderWithMauiGraphics.RecordPageAsync(renderer, 1)).Count;

    [Benchmark]
    public int MauiGraphicsReplay()
    {
        Replay();
        return page.Count;
    }

    private void Replay()
    {
        surface.Canvas.Clear(SKColors.White);
        page.Draw(new BenchSkiaCanvas { Canvas = surface.Canvas }, BenchImages.Instance, CanvasQuirks.Default,
            1024 / page.PageWidth);
        surface.Canvas.Flush();
    }

    private sealed class BenchSkiaCanvas : SkiaCanvas
    {
        protected override void PlatformConcatenateTransform(System.Numerics.Matrix3x2 t)
        {
            var m = new SKMatrix(t.M11, t.M21, t.M31, t.M12, t.M22, t.M32, 0, 0, 1);
            Canvas.Concat(in m);
        }
    }

    private sealed class BenchImages : IPdfImageFactory
    {
        public static BenchImages Instance { get; } = new();

        public unsafe IImage Create(PdfImage image)
        {
            var bitmap = new SKBitmap(new SKImageInfo(image.Width, image.Height, SKColorType.Bgra8888, SKAlphaType.Premul));
            fixed (byte* source = image.Pbgra)
                Buffer.MemoryCopy(source, (void*)bitmap.GetPixels(), bitmap.ByteCount, (long)image.Width * image.Height * 4);
            return new SkiaImage(bitmap);
        }
    }
}
