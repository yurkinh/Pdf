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
/// ContentWidth and ContentHeight are the scaled page size, for scrolling.
/// </summary>
public readonly record struct PageLayout(
    double Scale, double OffsetX, double OffsetY, double ContentWidth, double ContentHeight)
{
    /// <summary>
    /// Lay a page out in a viewport. A page smaller than the viewport is centred on that axis;
    /// a larger one starts at 0 so it can scroll.
    /// </summary>
    public static PageLayout Compute(
        double viewportWidth, double viewportHeight, double pageWidth, double pageHeight,
        ZoomMode mode, double customScale)
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
        return new PageLayout(scale, Center(viewportWidth, width), Center(viewportHeight, height), width, height);
    }

    private static double Center(double viewport, double content) => Math.Max(0, (viewport - content) / 2);
}
