namespace Melville.Pdf.ViewerModel;

/// <summary>
/// How a page is sized to the viewport.
/// </summary>
public enum ZoomMode
{
    /// <summary>The whole page is visible.</summary>
    FitPage,

    /// <summary>The page fills the viewport width; scroll vertically.</summary>
    FitWidth,

    /// <summary>A fixed scale chosen by the user.</summary>
    Custom,
}

/// <summary>
/// Where a page sits in the viewport: draw the page with Scale, offset by (OffsetX, OffsetY).
/// ContentWidth and ContentHeight are the scaled page size; ScrollX/ScrollY are how far a page larger than
/// the viewport is scrolled, between 0 and MaxScrollX/MaxScrollY.
/// </summary>
public readonly record struct PageLayout(
    double Scale, double OffsetX, double OffsetY, double ContentWidth, double ContentHeight,
    double ScrollX = 0, double ScrollY = 0, double MaxScrollX = 0, double MaxScrollY = 0)
{
    /// <summary>
    /// Lay a page out in a viewport. A page smaller than the viewport is centred on that axis; a larger one is
    /// shifted by the scroll offset, clamped so the page never leaves an empty edge.
    /// </summary>
    public static PageLayout Compute(
        double viewportWidth, double viewportHeight, double pageWidth, double pageHeight,
        ZoomMode mode, double customScale, double scrollX = 0, double scrollY = 0)
    {
        if (viewportWidth <= 0 || viewportHeight <= 0 || pageWidth <= 0 || pageHeight <= 0)
            return default;
        var scale = mode switch
        {
            ZoomMode.FitPage => Math.Min(viewportWidth / pageWidth, viewportHeight / pageHeight),
            ZoomMode.FitWidth => viewportWidth / pageWidth,
            _ => customScale,
        };
        var width = pageWidth * scale;
        var height = pageHeight * scale;
        double maxX = Math.Max(0, width - viewportWidth), maxY = Math.Max(0, height - viewportHeight);
        double sx = Math.Clamp(scrollX, 0, maxX), sy = Math.Clamp(scrollY, 0, maxY);
        return new PageLayout(scale, Offset(viewportWidth, width, sx), Offset(viewportHeight, height, sy),
            width, height, sx, sy, maxX, maxY);
    }

    // Centred when the page fits on this axis, otherwise moved by the scroll offset.
    private static double Offset(double viewport, double content, double scroll) =>
        content <= viewport ? (viewport - content) / 2 : -scroll;

    /// <summary>The point on the page (in PDF points) under a viewport point.</summary>
    public (double X, double Y) ToPage(double viewportX, double viewportY) =>
        Scale <= 0 ? (0, 0) : ((viewportX - OffsetX) / Scale, (viewportY - OffsetY) / Scale);
}
