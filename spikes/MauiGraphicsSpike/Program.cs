// Runs the phase 1 canvas checks on the Skia backend (Microsoft.Maui.Graphics.Skia).
using System.Numerics;
using System.Reflection;
using MauiGraphicsSpike;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Graphics.Skia;
using SkiaSharp;

var rasterizer = new SkiaRasterizer(fixTransform: args.Contains("--fix-transform"));
Console.WriteLine($"Backend: {rasterizer.Name}");
foreach (var (q, a) in CanvasChecks.Run(rasterizer)) Console.WriteLine($"{q}\n    => {a}");

var members = typeof(ICanvas).GetMembers().Select(m => m.Name).ToHashSet();
Console.WriteLine($"ICanvas stroke-paint API: {members.Any(n => n.Contains("Stroke") && n.Contains("Paint"))}; " +
                  $"absolute transform setter: {members.Any(n => n is "SetTransform" or "SetMatrix")}; " +
                  $"sampling control: {string.Join(",", members.Where(n => n.Contains("Interpol") || n.Contains("Filter") || n.Contains("Quality")))}");

sealed class SkiaRasterizer(bool fixTransform) : ICanvasRasterizer
{
    public string Name => fixTransform ? "Skia (SkiaCanvas with transform workaround)" : "Skia (stock SkiaCanvas)";

    public Pixels Render(int width, int height, Action<ICanvas> draw)
    {
        var info = new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        using var surface = SKSurface.Create(info);
        surface.Canvas.Clear(SKColors.White);
        draw(fixTransform ? new FixedSkiaCanvas { Canvas = surface.Canvas } : new SkiaCanvas { Canvas = surface.Canvas });
        surface.Canvas.Flush();
        var bytes = new byte[width * height * 4];
        unsafe { fixed (byte* p = bytes) surface.ReadPixels(info, (nint)p, width * 4, 0, 0); }
        return new Pixels(width, height, bytes);
    }

    public unsafe IImage ImageFromPbgra(int width, int height, uint[] pbgra)
    {
        var bmp = new SKBitmap(new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul));
        var dest = (uint*)bmp.GetPixels();
        for (int i = 0; i < pbgra.Length; i++) dest[i] = pbgra[i];
        return new SkiaImage(bmp);
    }
}

// Maui.Graphics.Skia 10.0.120 with SkiaSharp 4: PlatformConcatenateTransform calls SKMatrix.GetValues with a
// too-short array and throws ArgumentException. Concatenate the matrix directly instead.
sealed class FixedSkiaCanvas : SkiaCanvas
{
    protected override void PlatformConcatenateTransform(Matrix3x2 transform)
    {
        var m = new SKMatrix(transform.M11, transform.M21, transform.M31,
                             transform.M12, transform.M22, transform.M32, 0, 0, 1);
        Canvas.Concat(in m);
    }
}
