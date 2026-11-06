namespace Melville.Pdf.ViewerModel;

/// <summary>A rectangle on the page, in PDF points (origin at the page's top-left corner, as laid out).</summary>
public readonly record struct PageRect(double X, double Y, double Width, double Height)
{
    /// <summary>X + Width.</summary>
    public double Right => X + Width;

    /// <summary>Y + Height.</summary>
    public double Bottom => Y + Height;

    /// <summary>Whether <paramref name="other"/> lies entirely inside this rectangle.</summary>
    public bool Contains(PageRect other) =>
        other.X >= X && other.Y >= Y && other.Right <= Right && other.Bottom <= Bottom;
}

/// <summary>
/// Which bitmaps a viewer should cache for the current layout, so that redraws during pan and zoom draw bitmaps
/// instead of replaying the page. Replaying is expensive on some platforms: CoreGraphics takes about 140 ms to fill
/// a page of text that Skia renders in 13 ms (phase 8). The backdrop is the whole page, at the exact scale when that
/// fits the pixel budget, otherwise reduced to fit; when it is reduced, a detail bitmap covers the visible part
/// of the page (plus a margin for panning) at the exact scale.
/// </summary>
/// <param name="BackdropScale">View units per point for the whole-page bitmap.</param>
/// <param name="Detail">The region to draw at the exact scale, or null when the backdrop is exact.</param>
/// <param name="Scale">The exact scale (view units per point) of the layout.</param>
public readonly record struct BitmapPlan(double BackdropScale, PageRect? Detail, double Scale)
{
    /// <summary>Fraction of the visible size added on each side of the detail region, so short pans stay sharp.</summary>
    public const double Margin = 0.25;

    /// <summary>
    /// Plan the bitmaps for a page of <paramref name="pageWidth"/> × <paramref name="pageHeight"/> points laid out
    /// as <paramref name="layout"/> in a viewport of the given size, at <paramref name="density"/> pixels per view
    /// unit, with at most <paramref name="maxPixels"/> pixels per bitmap.
    /// </summary>
    public static BitmapPlan Compute(double pageWidth, double pageHeight, PageLayout layout,
        double viewportWidth, double viewportHeight, double density, long maxPixels)
    {
        var scale = layout.Scale;
        if (scale <= 0 || pageWidth <= 0 || pageHeight <= 0 || density <= 0 || maxPixels <= 0)
            return new BitmapPlan(0, null, scale);
        // Whole pixels, rounded up as a bitmap of that size would be: an exact fit of the budget would not fit.
        double PixelsAt(double s, double w, double h) => Math.Ceiling(w * s * density) * Math.Ceiling(h * s * density);

        if (PixelsAt(scale, pageWidth, pageHeight) <= maxPixels)
            return new BitmapPlan(scale, null, scale);

        var backdrop = Math.Sqrt(maxPixels / (pageWidth * pageHeight)) / density;
        while (PixelsAt(backdrop, pageWidth, pageHeight) > maxPixels) backdrop *= 0.999;
        var page = new PageRect(0, 0, pageWidth, pageHeight);
        var visible = Visible(pageWidth, pageHeight, layout, viewportWidth, viewportHeight);
        if (visible.Width <= 0 || visible.Height <= 0) return new BitmapPlan(backdrop, null, scale);

        var withMargin = Intersect(new PageRect(
            visible.X - visible.Width * Margin, visible.Y - visible.Height * Margin,
            visible.Width * (1 + 2 * Margin), visible.Height * (1 + 2 * Margin)), page);
        var detail = PixelsAt(scale, withMargin.Width, withMargin.Height) <= maxPixels ? withMargin : visible;
        // A viewport larger than the budget at this scale (huge screens): give up on detail, the backdrop shows.
        return PixelsAt(scale, detail.Width, detail.Height) <= maxPixels
            ? new BitmapPlan(backdrop, detail, scale)
            : new BitmapPlan(backdrop, null, scale);
    }

    /// <summary>The part of the page visible in the viewport for <paramref name="layout"/>.</summary>
    public static PageRect Visible(double pageWidth, double pageHeight, PageLayout layout, double viewportWidth, double viewportHeight)
    {
        if (layout.Scale <= 0) return default;
        var (left, top) = layout.ToPage(0, 0);
        var (right, bottom) = layout.ToPage(viewportWidth, viewportHeight);
        return Intersect(new PageRect(left, top, right - left, bottom - top), new PageRect(0, 0, pageWidth, pageHeight));
    }

    private static PageRect Intersect(PageRect a, PageRect b)
    {
        double x = Math.Max(a.X, b.X), y = Math.Max(a.Y, b.Y);
        double right = Math.Min(a.Right, b.Right), bottom = Math.Min(a.Bottom, b.Bottom);
        return right > x && bottom > y ? new PageRect(x, y, right - x, bottom - y) : default;
    }
}
