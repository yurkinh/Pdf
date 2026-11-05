using Melville.Pdf.Maui.Rendering;
using Melville.Pdf.MauiGraphics.Drawing;
using Melville.Pdf.ViewerModel;
using Microsoft.Maui.Graphics.Platform;
using IImage = Microsoft.Maui.Graphics.IImage;

namespace Melville.Pdf.Maui.Controls;

/// <summary>
/// Draws the current page into the viewer's GraphicsView. Replaying a display list costs about as much as rendering
/// the page (phase 3 measurements), so the page is normally drawn from a bitmap made off the UI thread; while a
/// gesture changes the zoom, the previous bitmap is stretched until a sharp one is ready.
/// Members are only touched on the UI thread.
/// </summary>
internal sealed class PdfPageDrawable : IDrawable
{
    public PdfDisplayList? Page { get; set; }
    public PageLayout Layout { get; set; }
    public Color Background { get; set; } = Colors.LightGray;
    public Color Shadow { get; set; } = Color.FromRgba(0, 0, 0, 0.25f);
    public PageBitmap? Bitmap { get; set; }

    public void Draw(ICanvas canvas, RectF dirtyRect)
    {
        canvas.FillColor = Background;
        canvas.FillRectangle(dirtyRect);
        if (Page is not { } page || Layout.Scale <= 0) return;

        float x = (float)Layout.OffsetX, y = (float)Layout.OffsetY;
        float width = (float)Layout.ContentWidth, height = (float)Layout.ContentHeight;
        canvas.FillColor = Shadow;
        canvas.FillRectangle(x + 2, y + 3, width, height);

        if (Bitmap is { } bitmap && ReferenceEquals(bitmap.Page, page))
            canvas.DrawImage(bitmap.Image, x, y, width, height); // exact size when settled, stretched mid-gesture
        else
            PdfPageDrawing.Draw(page, canvas, (float)Layout.Scale, x, y);
    }
}

/// <summary>A page drawn into a platform bitmap at one scale. Owns the export context, which owns the image.</summary>
internal sealed class PageBitmap : IDisposable
{
    /// <summary>Larger pages are not cached (memory); they are replayed directly.</summary>
    public const long MaxPixels = 6_000_000;

    private readonly IDisposable context;

    private PageBitmap(PdfDisplayList page, double scale, IImage image, IDisposable context)
    {
        Page = page;
        Scale = scale;
        Image = image;
        this.context = context;
    }

    public PdfDisplayList Page { get; }
    public double Scale { get; }
    public IImage Image { get; }

    /// <summary>Draw the page at <paramref name="scale"/> view units per point and <paramref name="density"/> pixels per unit.</summary>
    public static PageBitmap? Create(PdfDisplayList page, double scale, double density)
    {
        int width = (int)Math.Ceiling(page.PageWidth * scale * density);
        int height = (int)Math.Ceiling(page.PageHeight * scale * density);
        if (width <= 0 || height <= 0 || (long)width * height > MaxPixels) return null;
        var context = new PlatformBitmapExportService().CreateContext(width, height, 1);
        PdfPageDrawing.Draw(page, context.Canvas, (float)(scale * density));
        // On Android disposing the context can recycle its bitmap, so the context lives as long as the image.
        return new PageBitmap(page, scale, context.Image, context);
    }

    public void Dispose()
    {
        Image.Dispose();
        context.Dispose();
    }
}
