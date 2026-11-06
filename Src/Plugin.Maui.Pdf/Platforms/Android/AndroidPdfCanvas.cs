using System.Numerics;
using Melville.Pdf.MauiGraphics.Drawing;
using Microsoft.Maui.Graphics.Platform;
using AndroidBitmap = Android.Graphics.Bitmap;
using AndroidMatrix = Android.Graphics.Matrix;
using IImage = Microsoft.Maui.Graphics.IImage;

namespace Plugin.Maui.Pdf.Rendering;

/// <summary>
/// Android support. Maui.Graphics 10 gets ConcatenateTransform wrong three ways on Android (phase 1 spike):
/// ScalingCanvas applies scale twice, PlatformCanvas post-concatenates, and the Matrix3x2 conversion swaps
/// the x and y translation. Pages are therefore drawn on the native canvas underneath, with transforms
/// concatenated there directly.
/// </summary>
internal static class AndroidPdfCanvas
{
    public static CanvasQuirks Quirks { get; } = new(Concatenate);

    /// <summary>
    /// Go around a ScalingCanvas (which GraphicsView uses for screen density): returns the platform canvas and the
    /// scale the ScalingCanvas would have applied, to be applied as a real transform instead.
    /// </summary>
    public static (ICanvas Canvas, float Scale) Unwrap(ICanvas canvas) =>
        canvas is ScalingCanvas { ParentCanvas: PlatformCanvas platform } scaling
            ? (platform, scaling.GetScale())
            : (canvas, 1);

    private static void Concatenate(ICanvas canvas, Matrix3x2 m)
    {
        if (canvas is not PlatformCanvas platform)
        {
            canvas.ConcatenateTransform(m);
            return;
        }
        // Matrix3x2 is row-vector (x' = x*M11 + y*M21 + M31); Android's Matrix is column-vector.
        using var native = new AndroidMatrix();
        native.SetValues([m.M11, m.M21, m.M31, m.M12, m.M22, m.M32, 0, 0, 1]);
        platform.Canvas.Concat(native); // Canvas.concat pre-concatenates: the transform acts in user space
    }
}

/// <summary>Creates Android bitmaps from premultiplied BGRA pixels.</summary>
public sealed class AndroidImageFactory : IPdfImageFactory
{
    /// <summary>The shared instance.</summary>
    public static AndroidImageFactory Instance { get; } = new();

    /// <inheritdoc />
    public IImage Create(PdfImage image)
    {
        // ARGB_8888 is stored as R, G, B, A bytes, premultiplied: swap red and blue.
        var rgba = new byte[image.Width * image.Height * 4];
        var source = image.Pbgra;
        for (int i = 0; i < rgba.Length; i += 4)
        {
            rgba[i] = source[i + 2];
            rgba[i + 1] = source[i + 1];
            rgba[i + 2] = source[i];
            rgba[i + 3] = source[i + 3];
        }
        var bitmap = AndroidBitmap.CreateBitmap(image.Width, image.Height, AndroidBitmap.Config.Argb8888!)!;
        using (var buffer = Java.Nio.ByteBuffer.Wrap(rgba))
            bitmap.CopyPixelsFromBuffer(buffer);
        return new PlatformImage(bitmap);
    }
}
