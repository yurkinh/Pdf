using Melville.Pdf.MauiGraphics.Drawing;

namespace Melville.Pdf.MauiGraphics.Rendering;

/// <summary>
/// Maui.Graphics has no image-sampling control, and backends differ: Skia draws nearest-neighbour, native
/// backends may smooth. Resampling images to roughly the size they are drawn makes the PDF's choice
/// (interpolate or not) visible on every backend, and keeps very large images from costing memory.
/// </summary>
internal static class ImageSizing
{
    private const double TargetPerUnit = 2;   // 2 x the recording resolution: about 4 pixels per point
    private const double KeepPerUnit = 4;     // larger images are shrunk to this: about 8 pixels per point
    private const int MaxSide = 2048;
    private const long ShrinkAbovePixels = 1_000_000;

    /// <param name="image">The decoded image.</param>
    /// <param name="drawnWidth">Width the image is drawn at, in recording units.</param>
    /// <param name="drawnHeight">Height the image is drawn at, in recording units.</param>
    /// <param name="interpolate">Whether the PDF asks for smooth scaling.</param>
    public static PdfImage Prepare(PdfImage image, double drawnWidth, double drawnHeight, bool interpolate)
    {
        if (drawnWidth < 1 || drawnHeight < 1 || image.Width == 0 || image.Height == 0) return image;

        double targetWidth = drawnWidth * TargetPerUnit, targetHeight = drawnHeight * TargetPerUnit;
        if (image.Width * 2 <= targetWidth || image.Height * 2 <= targetHeight)
        {
            var (w, h) = Fit(Math.Max(image.Width, targetWidth), Math.Max(image.Height, targetHeight));
            return w > image.Width || h > image.Height ? image.Resize(w, h, interpolate) : image;
        }

        double keepWidth = drawnWidth * KeepPerUnit, keepHeight = drawnHeight * KeepPerUnit;
        if ((long)image.Width * image.Height > ShrinkAbovePixels &&
            image.Width > keepWidth * 2 && image.Height > keepHeight * 2)
        {
            var (w, h) = Fit(keepWidth, keepHeight);
            return image.Shrink(w, h);
        }
        return image;
    }

    private static (int Width, int Height) Fit(double width, double height)
    {
        var scale = Math.Min(1, MaxSide / Math.Max(width, height));
        return (Math.Max(1, (int)Math.Round(width * scale)), Math.Max(1, (int)Math.Round(height * scale)));
    }
}
