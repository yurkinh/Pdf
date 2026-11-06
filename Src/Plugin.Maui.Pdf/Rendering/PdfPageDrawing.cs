using Melville.Pdf.MauiGraphics;
using Melville.Pdf.MauiGraphics.Drawing;

namespace Plugin.Maui.Pdf.Rendering;

/// <summary>
/// Draws recorded PDF pages on this platform's Maui.Graphics canvas, with the image factory and backend
/// workarounds the platform needs.
/// </summary>
public static class PdfPageDrawing
{
    /// <summary>The image factory for this platform.</summary>
    public static IPdfImageFactory ImageFactory { get; } =
#if ANDROID
        AndroidImageFactory.Instance;
#elif IOS || MACCATALYST
        AppleImageFactory.Instance;
#else
        PngImageFactory.Instance; // Windows: a Win2D factory is still to do
#endif

    /// <summary>The backend differences to compensate for on this platform.</summary>
    public static CanvasQuirks Quirks { get; } =
#if ANDROID
        AndroidPdfCanvas.Quirks;
#else
        CanvasQuirks.Default; // CoreGraphics is correct; Windows (Win2D) has not been measured yet
#endif

    /// <summary>
    /// Draw a page with its top-left corner at the canvas origin, at <paramref name="scale"/> canvas units per PDF
    /// point. The canvas state is unchanged afterwards.
    /// </summary>
    public static void Draw(PdfDisplayList page, ICanvas canvas, float scale) =>
        Draw(page, canvas, scale, 0, 0);

    /// <summary>
    /// Draw a page at <paramref name="scale"/> canvas units per PDF point with its top-left corner at
    /// (<paramref name="x"/>, <paramref name="y"/>). The canvas state is unchanged afterwards.
    /// </summary>
    public static void Draw(PdfDisplayList page, ICanvas canvas, float scale, float x, float y)
    {
        var placement = System.Numerics.Matrix3x2.CreateScale(scale) * System.Numerics.Matrix3x2.CreateTranslation(x, y);
#if ANDROID
        // Drawn on the native canvas under the ScalingCanvas, so its density becomes part of the transform.
        var (target, density) = AndroidPdfCanvas.Unwrap(canvas);
        page.Draw(target, ImageFactory, Quirks, placement * System.Numerics.Matrix3x2.CreateScale(density));
#else
        page.Draw(canvas, ImageFactory, Quirks, placement);
#endif
    }
}
