using System.Numerics;
using Melville.Pdf.FontLibrary;
using Melville.Pdf.LowLevel.Parsing.ParserContext;
using Melville.Pdf.MauiGraphics.Drawing;
using Melville.Pdf.Model;
using Melville.Pdf.Model.Renderers.DocumentRenderers;
using Melville.Pdf.ReferenceDocuments.Infrastructure;
using Melville.Pdf.SkiaSharp;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Graphics.Skia;
using SkiaSharp;
using IImage = Microsoft.Maui.Graphics.IImage;

namespace Melville.Pdf.MauiGraphics.Tests;

/// <summary>Creates Skia-backed IImages from PdfImage pixels.</summary>
public sealed class SkiaImageFactory : IPdfImageFactory
{
    public static SkiaImageFactory Instance { get; } = new();

    public unsafe IImage Create(PdfImage image)
    {
        var bitmap = new SKBitmap(new SKImageInfo(image.Width, image.Height, SKColorType.Bgra8888, SKAlphaType.Premul));
        fixed (byte* source = image.Pbgra)
            Buffer.MemoryCopy(source, (void*)bitmap.GetPixels(), bitmap.ByteCount, (long)image.Width * image.Height * 4);
        return new SkiaImage(bitmap);
    }
}

/// <summary>
/// Maui.Graphics.Skia 10.0.x throws in PlatformConcatenateTransform with SkiaSharp 4 (phase 1 spike);
/// concatenate on the SKCanvas directly.
/// </summary>
public sealed class FixedSkiaCanvas : SkiaCanvas
{
    protected override void PlatformConcatenateTransform(Matrix3x2 transform)
    {
        var m = new SKMatrix(transform.M11, transform.M21, transform.M31,
                             transform.M12, transform.M22, transform.M32, 0, 0, 1);
        Canvas.Concat(in m);
    }
}

/// <summary>Straight RGBA pixels of a render.</summary>
public sealed record Raster(int Width, int Height, byte[] Rgba);

public static class Renders
{
    public static async Task<DocumentRenderer> OpenAsync(IPdfGenerator generator)
    {
        using var bytes = new MemoryStream();
        await generator.WritePdfAsync(bytes);
        return await new PdfReader(new ConstantPasswordSource(PasswordType.User, generator.Password),
            SelfContainedDefaultFonts.Instance).ReadFromAsync(bytes.ToArray());
    }

    /// <summary>Record page 1 into a display list and draw it on Skia through Maui.Graphics, at a given pixel width.</summary>
    public static async Task<Raster> MauiGraphicsAsync(IPdfGenerator generator, int width)
    {
        using var document = await OpenAsync(generator);
        var page = await RenderWithMauiGraphics.RecordPageAsync(document, 1);
        var height = (int)(page.PageHeight * (width / page.PageWidth));
        using var surface = SKSurface.Create(new SKImageInfo(width, height));
        surface.Canvas.Clear(SKColors.Transparent);
        // The reference renderer does not anti-alias (SkiaSharp's default SKPaint) and Maui.Graphics.Skia always
        // anti-aliases fills, so comparisons supersample both (see CompareAsync).
        var canvas = new FixedSkiaCanvas { Canvas = surface.Canvas };
        page.Draw(canvas, SkiaImageFactory.Instance, CanvasQuirks.Default, width / page.PageWidth);
        return Read(surface, width, height);
    }

    /// <summary>The same page from the existing SkiaSharp renderer, as the reference.</summary>
    public static async Task<Raster> SkiaSharpAsync(IPdfGenerator generator, int width, int height)
    {
        using var document = await OpenAsync(generator);
        using var surface = await RenderWithSkia.ToSurfaceAsync(document, 1, width, height);
        return Read(surface, width, height);
    }

    private static unsafe Raster Read(SKSurface surface, int width, int height)
    {
        var info = new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        var bytes = new byte[width * height * 4];
        fixed (byte* p = bytes) surface.ReadPixels(info, (nint)p, width * 4, 0, 0);
        return new Raster(width, height, bytes);
    }

    /// <summary>
    /// Fraction of pixels that differ between the two renderers at <paramref name="width"/>. Both are rendered at
    /// <paramref name="supersample"/> times the size and box-filtered down, so aliased and anti-aliased edges agree
    /// and real geometry, colour or clip differences remain.
    /// </summary>
    public static async Task<(double Fraction, Raster Maui, Raster Skia)> CompareAsync(
        IPdfGenerator generator, int width, int supersample = 4)
    {
        var maui = await MauiGraphicsAsync(generator, width * supersample);
        var skia = await SkiaSharpAsync(generator, maui.Width, maui.Height);
        var small = (Maui: Downsample(maui, supersample), Skia: Downsample(skia, supersample));
        return (DifferingFraction(small.Maui, small.Skia), small.Maui, small.Skia);
    }

    /// <summary>Average each factor x factor block (premultiplied by alpha, so transparent pixels do not darken).</summary>
    public static Raster Downsample(Raster r, int factor)
    {
        int w = r.Width / factor, h = r.Height / factor;
        var result = new byte[w * h * 4];
        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
        {
            long cr = 0, cg = 0, cb = 0, ca = 0;
            for (int dy = 0; dy < factor; dy++)
            for (int dx = 0; dx < factor; dx++)
            {
                var i = ((y * factor + dy) * r.Width + x * factor + dx) * 4;
                int a = r.Rgba[i + 3];
                cr += r.Rgba[i] * a; cg += r.Rgba[i + 1] * a; cb += r.Rgba[i + 2] * a; ca += a;
            }
            var o = (y * w + x) * 4;
            if (ca > 0) { result[o] = (byte)(cr / ca); result[o + 1] = (byte)(cg / ca); result[o + 2] = (byte)(cb / ca); }
            result[o + 3] = (byte)(ca / (factor * factor));
        }
        return new Raster(w, h, result);
    }

    /// <summary>Fraction of pixels whose largest channel difference exceeds a tolerance (anti-aliasing noise).</summary>
    public static double DifferingFraction(Raster a, Raster b, int tolerance = 48)
    {
        if (a.Width != b.Width || a.Height != b.Height) return 1;
        long differing = 0;
        for (int i = 0; i < a.Rgba.Length; i += 4)
        {
            var d = Math.Max(Math.Max(Math.Abs(a.Rgba[i] - b.Rgba[i]), Math.Abs(a.Rgba[i + 1] - b.Rgba[i + 1])),
                Math.Max(Math.Abs(a.Rgba[i + 2] - b.Rgba[i + 2]), Math.Abs(a.Rgba[i + 3] - b.Rgba[i + 3])));
            if (d > tolerance) differing++;
        }
        return (double)differing / (a.Width * a.Height);
    }
}
