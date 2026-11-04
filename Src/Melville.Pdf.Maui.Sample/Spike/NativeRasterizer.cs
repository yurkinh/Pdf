using MauiGraphicsSpike;
using Microsoft.Maui.Graphics.Platform;
using SkiaSharp;
using IImage = Microsoft.Maui.Graphics.IImage;

namespace Melville.Pdf.Maui.Sample.Spike;

/// <summary>
/// Runs the canvas checks on this platform's native Maui.Graphics backend (CoreGraphics on iOS and
/// Mac Catalyst, Android Canvas, Win2D): draws offscreen with PlatformBitmapExportService, then reads
/// the pixels back through PNG with SkiaSharp.
/// </summary>
internal sealed class NativeRasterizer : ICanvasRasterizer
{
    private readonly PlatformBitmapExportService exporter = new();

    public string Name => $"{DeviceInfo.Platform} native ({exporter.GetType().Name})";

    public Pixels Render(int width, int height, Action<ICanvas> draw)
    {
        using var context = exporter.CreateContext(width, height, 1);
        var canvas = context.Canvas;
        canvas.FillColor = Colors.White;
        canvas.FillRectangle(0, 0, width, height);
        draw(canvas);
        using var png = new MemoryStream();
        context.Image.Save(png, ImageFormat.Png);
        png.Position = 0;
        return Decode(png, width, height);
    }

    // Maui.Graphics has no cross-platform "image from pixels" API; this is the PNG fallback the plan names.
    public unsafe IImage ImageFromPbgra(int width, int height, uint[] pbgra)
    {
        using var bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul));
        var dest = (uint*)bitmap.GetPixels();
        for (int i = 0; i < pbgra.Length; i++) dest[i] = pbgra[i];
        using var encoded = SKImage.FromBitmap(bitmap).Encode(SKEncodedImageFormat.Png, 100);
        return PlatformImage.FromStream(new MemoryStream(encoded.ToArray()), ImageFormat.Png);
    }

    private static unsafe Pixels Decode(Stream png, int width, int height)
    {
        using var decoded = SKBitmap.Decode(png);
        var info = new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        var bytes = new byte[width * height * 4];
        fixed (byte* p = bytes)
        {
            using var pixmap = decoded.PeekPixels();
            pixmap.ReadPixels(info, (nint)p, width * 4, 0, 0);
        }
        return new Pixels(width, height, bytes);
    }
}
