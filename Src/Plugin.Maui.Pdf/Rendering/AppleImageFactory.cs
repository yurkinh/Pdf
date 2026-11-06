#if IOS || MACCATALYST
using CoreGraphics;
using Melville.Pdf.MauiGraphics.Drawing;
using Microsoft.Maui.Graphics.Platform;
using UIKit;
using IImage = Microsoft.Maui.Graphics.IImage;

namespace Plugin.Maui.Pdf.Rendering;

/// <summary>Creates CoreGraphics images from premultiplied BGRA pixels without copying or converting them.</summary>
public sealed class AppleImageFactory : IPdfImageFactory
{
    /// <summary>The shared instance.</summary>
    public static AppleImageFactory Instance { get; } = new();

    /// <inheritdoc />
    public IImage Create(PdfImage image)
    {
        using var colorSpace = CGColorSpace.CreateDeviceRGB();
        using var provider = new CGDataProvider(image.Pbgra, 0, image.Width * image.Height * 4);
        // Premultiplied BGRA in memory is 32-bit little-endian ARGB with premultiplied alpha first.
        using var cgImage = new CGImage(image.Width, image.Height, 8, 32, image.Width * 4, colorSpace,
            CGBitmapFlags.PremultipliedFirst | CGBitmapFlags.ByteOrder32Little, provider, null, false,
            CGColorRenderingIntent.Default);
        return new PlatformImage(new UIImage(cgImage));
    }
}
#endif
