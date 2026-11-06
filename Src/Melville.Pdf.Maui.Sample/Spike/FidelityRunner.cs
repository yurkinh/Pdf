using System.Diagnostics;
using Melville.Pdf.FontLibrary;
using Melville.Pdf.LowLevel.Parsing.ParserContext;
using Melville.Pdf.Maui.Rendering;
using Melville.Pdf.MauiGraphics;
using Melville.Pdf.Model;
using Melville.Pdf.Model.Renderers.DocumentRenderers;
using Melville.Pdf.ReferenceDocuments.Infrastructure;
using Melville.Pdf.SkiaSharp;
using Microsoft.Maui.Graphics.Platform;
using SkiaSharp;

namespace Melville.Pdf.Maui.Sample.Spike;

/// <summary>
/// Phase 3 device check: every reference document drawn by the Maui.Graphics renderer on this platform's
/// native backend, compared with the SkiaSharp renderer on the same device. Both are rendered at 4x and
/// box-filtered down, so anti-aliasing differences cancel and rendering differences remain.
/// Logged as [FIDELITY] lines.
/// </summary>
public static class FidelityRunner
{
    private const int Width = 200;
    private const int Supersample = 4;
    private const int Tolerance = 48;

    public static async Task<IReadOnlyList<SpikeLine>> RunAsync()
    {
        var lines = new List<SpikeLine>();
        var buckets = new int[5]; // 0%, <0.5%, <2%, <10%, >=10%
        var failures = 0;
        var watch = Stopwatch.StartNew();
        foreach (var generator in KnownGenerators.All.OrderBy(g => g.Prefix, StringComparer.Ordinal))
        {
            string result;
            try
            {
                var difference = await CompareAsync(generator);
                buckets[difference == 0 ? 0 : difference < 0.005 ? 1 : difference < 0.02 ? 2 : difference < 0.1 ? 3 : 4]++;
                result = $"{difference:P2}";
            }
            catch (Exception e)
            {
                failures++;
                result = $"THREW {e.GetType().Name}: {e.Message.ReplaceLineEndings(" ")}";
            }
            Console.WriteLine($"[FIDELITY] {generator.Prefix} => {result}");
            if (!result.EndsWith("0.00%")) lines.Add(new($"Fidelity {generator.Prefix}", result));
        }
        var summary = $"0%: {buckets[0]}, <0.5%: {buckets[1]}, 0.5-2%: {buckets[2]}, 2-10%: {buckets[3]}, " +
                      $">=10%: {buckets[4]}, exceptions: {failures}, {watch.Elapsed.TotalSeconds:F0} s";
        Console.WriteLine($"[FIDELITY-SUMMARY] {summary}");
        lines.Insert(0, new("Fidelity vs SkiaSharp (native backend)", summary));
        return lines;
    }

    private static async Task<double> CompareAsync(IPdfGenerator generator)
    {
        using var document = await OpenAsync(generator);
        var page = await RenderWithMauiGraphics.RecordPageAsync(document, 1);
        var width = Width * Supersample;
        var height = (int)(page.PageHeight * (width / page.PageWidth));

        byte[] native;
        using (var context = new PlatformBitmapExportService().CreateContext(width, height, 1))
        {
            PdfPageDrawing.Draw(page, context.Canvas, width / page.PageWidth);
            using var png = new MemoryStream();
            context.Image.Save(png, ImageFormat.Png);
            native = DecodeRgba(png.ToArray(), width, height);
        }

        byte[] reference;
        using (var surface = await RenderWithSkia.ToSurfaceAsync(document, 1, width, height))
            reference = ReadRgba(surface, width, height);

        var small = (Native: Downsample(native, width, height), Reference: Downsample(reference, width, height));
        var difference = DifferingFraction(small.Native, small.Reference);
        if (difference > 0.01) SaveSideBySide(generator.Prefix, small.Native, small.Reference, width / Supersample, height / Supersample);
        return difference;
    }

    // "native | SkiaSharp" for documents worth a look, in the app's cache folder (path logged).
    private static unsafe void SaveSideBySide(string name, byte[] left, byte[] right, int width, int height)
    {
        var info = new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        using var bitmap = new SKBitmap(width * 2 + 4, height);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(SKColors.Magenta);
            fixed (byte* l = left) using (var image = SKImage.FromPixelCopy(info, (nint)l, width * 4)) canvas.DrawImage(image, 0, 0);
            fixed (byte* r = right) using (var image = SKImage.FromPixelCopy(info, (nint)r, width * 4)) canvas.DrawImage(image, width + 4, 0);
        }
        var path = Path.Combine(FileSystem.CacheDirectory, $"fidelity{name}.png");
        using var data = bitmap.Encode(SKEncodedImageFormat.Png, 100);
        File.WriteAllBytes(path, data.ToArray());
        Console.WriteLine($"[FIDELITY-IMAGE] {path}");
    }

    private static async Task<DocumentRenderer> OpenAsync(IPdfGenerator generator)
    {
        using var bytes = new MemoryStream();
        await generator.WritePdfAsync(bytes);
        return await new PdfReader(new ConstantPasswordSource(PasswordType.User, generator.Password),
            SelfContainedDefaultFonts.Instance).ReadFromAsync(bytes.ToArray());
    }

    private static unsafe byte[] DecodeRgba(byte[] png, int width, int height)
    {
        using var decoded = SKBitmap.Decode(png);
        var bytes = new byte[width * height * 4];
        fixed (byte* p = bytes)
        {
            using var pixmap = decoded.PeekPixels();
            pixmap.ReadPixels(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Unpremul), (nint)p, width * 4, 0, 0);
        }
        return bytes;
    }

    private static unsafe byte[] ReadRgba(SKSurface surface, int width, int height)
    {
        var bytes = new byte[width * height * 4];
        fixed (byte* p = bytes)
            surface.ReadPixels(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Unpremul), (nint)p, width * 4, 0, 0);
        return bytes;
    }

    // Alpha-weighted box filter, as in the desktop comparison tests.
    private static byte[] Downsample(byte[] rgba, int width, int height)
    {
        int w = width / Supersample, h = height / Supersample;
        var result = new byte[w * h * 4];
        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
        {
            long r = 0, g = 0, b = 0, a = 0;
            for (int dy = 0; dy < Supersample; dy++)
            for (int dx = 0; dx < Supersample; dx++)
            {
                var i = ((y * Supersample + dy) * width + x * Supersample + dx) * 4;
                int alpha = rgba[i + 3];
                r += rgba[i] * alpha; g += rgba[i + 1] * alpha; b += rgba[i + 2] * alpha; a += alpha;
            }
            var o = (y * w + x) * 4;
            if (a > 0) { result[o] = (byte)(r / a); result[o + 1] = (byte)(g / a); result[o + 2] = (byte)(b / a); }
            result[o + 3] = (byte)(a / (Supersample * Supersample));
        }
        return result;
    }

    private static double DifferingFraction(byte[] a, byte[] b)
    {
        long differing = 0;
        for (int i = 0; i < a.Length; i += 4)
        {
            var d = 0;
            for (int c = 0; c < 4; c++) d = Math.Max(d, Math.Abs(a[i + c] - b[i + c]));
            if (d > Tolerance) differing++;
        }
        return (double)differing / (a.Length / 4);
    }
}
