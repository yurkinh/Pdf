using Melville.Pdf.ReferenceDocuments.Infrastructure;
using SkiaSharp;
using Xunit;

namespace Melville.Pdf.MauiGraphics.Tests;

/// <summary>
/// Writes "maui | skiasharp" side-by-side PNGs for the documents named in MELVILLE_DUMP (comma separated),
/// into MELVILLE_DUMP_DIR or the temp folder, MELVILLE_DUMP_WIDTH pixels wide per render (default 400). For looking at differences the comparison report flags.
/// </summary>
public class DumpRendersTest(ITestOutputHelper output)
{
    [Fact(Explicit = true)]
    public async Task DumpSideBySide()
    {
        var names = (Environment.GetEnvironmentVariable("MELVILLE_DUMP") ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries);
        var folder = Environment.GetEnvironmentVariable("MELVILLE_DUMP_DIR") ?? Path.GetTempPath();
        var width = int.TryParse(Environment.GetEnvironmentVariable("MELVILLE_DUMP_WIDTH"), out var w) ? w : 400;
        Directory.CreateDirectory(folder);
        foreach (var name in names)
        {
            var generator = GeneratorFactory.AllGenerators.Single(g => g.Prefix == name);
            var (_, maui, skia) = await Renders.CompareAsync(generator, width);
            var path = Path.Combine(folder, $"{name.TrimStart('-')}.png");
            WriteSideBySide(path, maui, skia);
            output.WriteLine(path);
        }
    }

    /// <summary>Write two renders side by side, with the difference panel.</summary>
    public static void WritePair(string path, Raster left, Raster right) => WriteSideBySide(path, left, right);

    private static unsafe void WriteSideBySide(string path, Raster left, Raster right)
    {
        using var bitmap = new SKBitmap(left.Width * 3 + 16, left.Height);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(SKColors.Magenta);
            Draw(canvas, left, 0);
            Draw(canvas, right, left.Width + 8);
            Draw(canvas, DiffMask(left, right), 2 * (left.Width + 8));
        }
        using var data = bitmap.Encode(SKEncodedImageFormat.Png, 100);
        File.WriteAllBytes(path, data.ToArray());

        // Third panel: the reference in light grey, pixels that differ beyond the comparison tolerance in red.
        static Raster DiffMask(Raster a, Raster b)
        {
            var mask = new byte[a.Rgba.Length];
            for (int i = 0; i < mask.Length; i += 4)
            {
                var d = 0;
                for (int c = 0; c < 4; c++) d = Math.Max(d, Math.Abs(a.Rgba[i + c] - b.Rgba[i + c]));
                var grey = (byte)(255 - (255 - b.Rgba[i]) / 4);
                (mask[i], mask[i + 1], mask[i + 2], mask[i + 3]) = d > 48 ? ((byte)255, (byte)0, (byte)0, (byte)255) : (grey, grey, grey, (byte)255);
            }
            return a with { Rgba = mask };
        }

        static void Draw(SKCanvas canvas, Raster raster, int x)
        {
            var info = new SKImageInfo(raster.Width, raster.Height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
            fixed (byte* p = raster.Rgba)
            {
                using var image = SKImage.FromPixelCopy(info, (nint)p, raster.Width * 4);
                // Checkerboard behind transparent pixels, so "nothing drawn" is visible.
                using var paint = new SKPaint { Color = new SKColor(220, 220, 220) };
                for (int cy = 0; cy < raster.Height; cy += 16)
                for (int cx = 0; cx < raster.Width; cx += 16)
                    if ((cx + cy) / 16 % 2 == 0) canvas.DrawRect(x + cx, cy, 16, 16, paint);
                canvas.DrawImage(image, x, 0);
            }
        }
    }
}

public class InkBoundsTest(ITestOutputHelper output)
{
    /// <summary>Prints the bounding box of non-white pixels in both renders of MELVILLE_DUMP documents.</summary>
    [Fact(Explicit = true)]
    public async Task PrintInkBounds()
    {
        foreach (var name in (Environment.GetEnvironmentVariable("MELVILLE_DUMP") ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            var generator = GeneratorFactory.AllGenerators.Single(g => g.Prefix == name);
            var maui = await Renders.MauiGraphicsAsync(generator, 400);
            var skia = await Renders.SkiaSharpAsync(generator, maui.Width, maui.Height);
            var line = $"{name} {maui.Width}x{maui.Height}: maui {Ink(maui)}  skia {Ink(skia)}";
            output.WriteLine(line);
            if (Environment.GetEnvironmentVariable("MELVILLE_DUMP_DIR") is { } folder)
                await File.AppendAllTextAsync(Path.Combine(folder, "ink.txt"), line + "\n", TestContext.Current.CancellationToken);
        }
    }

    private static string Ink(Raster r)
    {
        int minX = int.MaxValue, minY = int.MaxValue, maxX = -1, maxY = -1;
        for (int y = 0; y < r.Height; y++)
        for (int x = 0; x < r.Width; x++)
        {
            var i = (y * r.Width + x) * 4;
            if (r.Rgba[i + 3] > 0 && (r.Rgba[i] < 200 || r.Rgba[i + 1] < 200 || r.Rgba[i + 2] < 200))
            {
                minX = Math.Min(minX, x); minY = Math.Min(minY, y); maxX = Math.Max(maxX, x); maxY = Math.Max(maxY, y);
            }
        }
        return maxX < 0 ? "none" : $"({minX},{minY})-({maxX},{maxY})";
    }
}
