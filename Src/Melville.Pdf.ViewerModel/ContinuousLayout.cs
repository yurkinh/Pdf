namespace Melville.Pdf.ViewerModel;

/// <summary>How a viewer shows the document.</summary>
public enum DisplayMode
{
    /// <summary>One page at a time; page navigation replaces it.</summary>
    SinglePage,

    /// <summary>All pages in one scrolling strip.</summary>
    Continuous,
}

/// <summary>The direction pages follow each other in <see cref="DisplayMode.Continuous"/> mode.</summary>
public enum ScrollOrientation
{
    /// <summary>Pages stacked top to bottom.</summary>
    Vertical,

    /// <summary>Pages side by side, left to right.</summary>
    Horizontal,
}

/// <summary>The size of a page in PDF points (after its default rotation).</summary>
public readonly record struct PageSize(double Width, double Height);

/// <summary>
/// Where every page of a document sits in a continuous (scrolling) view. Pages follow each other along the scroll
/// axis with a gap, centred across it; the content scrolls by (ScrollX, ScrollY) view units and is centred when it
/// is smaller than the viewport. One scale applies to every page, computed from the largest page so that fit modes
/// do not jump between pages of different sizes. Pages are numbered from 1.
/// </summary>
public sealed class ContinuousLayout
{
    /// <summary>The gap between pages, and around them, in view units.</summary>
    public const double DefaultGap = 8;

    private readonly IReadOnlyList<PageSize> pages;
    private readonly double[] start;       // view units from the content edge to each page's leading edge
    private readonly ScrollOrientation orientation;
    private readonly double gap, maxCross, offsetX, offsetY;

    private ContinuousLayout(IReadOnlyList<PageSize> pages, ScrollOrientation orientation, double scale, double gap,
        double viewportWidth, double viewportHeight, double scrollX, double scrollY)
    {
        this.pages = pages;
        this.orientation = orientation;
        this.gap = gap;
        Scale = scale;
        ViewportWidth = viewportWidth;
        ViewportHeight = viewportHeight;

        start = new double[pages.Count + 1];
        double main = gap, cross = 0;
        for (int i = 0; i < pages.Count; i++)
        {
            start[i] = main;
            main += Along(pages[i]) * scale + gap;
            cross = Math.Max(cross, Across(pages[i]) * scale);
        }
        start[pages.Count] = main;
        maxCross = cross;
        var crossExtent = cross + 2 * gap;
        (ContentWidth, ContentHeight) = orientation == ScrollOrientation.Vertical ? (crossExtent, main) : (main, crossExtent);

        MaxScrollX = Math.Max(0, ContentWidth - viewportWidth);
        MaxScrollY = Math.Max(0, ContentHeight - viewportHeight);
        ScrollX = Math.Clamp(scrollX, 0, MaxScrollX);
        ScrollY = Math.Clamp(scrollY, 0, MaxScrollY);
        offsetX = Math.Max(0, (viewportWidth - ContentWidth) / 2);
        offsetY = Math.Max(0, (viewportHeight - ContentHeight) / 2);
    }

    /// <summary>View units per point, for every page.</summary>
    public double Scale { get; }

    /// <summary>Width of the viewport, in view units.</summary>
    public double ViewportWidth { get; }

    /// <summary>Height of the viewport, in view units.</summary>
    public double ViewportHeight { get; }

    /// <summary>Width of all pages and gaps, in view units.</summary>
    public double ContentWidth { get; }

    /// <summary>Height of all pages and gaps, in view units.</summary>
    public double ContentHeight { get; }

    /// <summary>Horizontal scroll position, clamped to 0..<see cref="MaxScrollX"/>.</summary>
    public double ScrollX { get; }

    /// <summary>Vertical scroll position, clamped to 0..<see cref="MaxScrollY"/>.</summary>
    public double ScrollY { get; }

    /// <summary>The largest horizontal scroll (0 when the content is not wider than the viewport).</summary>
    public double MaxScrollX { get; }

    /// <summary>The largest vertical scroll (0 when the content is not taller than the viewport).</summary>
    public double MaxScrollY { get; }

    /// <summary>Number of pages.</summary>
    public int PageCount => pages.Count;

    /// <summary>
    /// Lay out <paramref name="pages"/> in a viewport. The scale follows <paramref name="mode"/>: FitPage shows
    /// the largest page whole, FitWidth fits its width, Custom uses <paramref name="customScale"/>.
    /// </summary>
    public static ContinuousLayout Compute(IReadOnlyList<PageSize> pages, ScrollOrientation orientation, ZoomMode mode,
        double customScale, double viewportWidth, double viewportHeight, double scrollX = 0, double scrollY = 0,
        double gap = DefaultGap)
    {
        double maxWidth = 0, maxHeight = 0;
        foreach (var page in pages)
        {
            maxWidth = Math.Max(maxWidth, page.Width);
            maxHeight = Math.Max(maxHeight, page.Height);
        }
        var scale = ScaleFor(mode, customScale, maxWidth, maxHeight, viewportWidth, viewportHeight, gap);
        return new ContinuousLayout(pages, orientation, scale, gap, viewportWidth, viewportHeight, scrollX, scrollY);
    }

    private static double ScaleFor(ZoomMode mode, double customScale, double maxWidth, double maxHeight,
        double viewportWidth, double viewportHeight, double gap)
    {
        if (mode == ZoomMode.Custom || maxWidth <= 0 || maxHeight <= 0) return customScale;
        double width = Math.Max(1, viewportWidth - 2 * gap), height = Math.Max(1, viewportHeight - 2 * gap);
        return mode == ZoomMode.FitWidth ? width / maxWidth : Math.Min(width / maxWidth, height / maxHeight);
    }

    /// <summary>The same layout at another scroll position (clamped).</summary>
    public ContinuousLayout ScrolledTo(double scrollX, double scrollY) =>
        new(pages, orientation, Scale, gap, ViewportWidth, ViewportHeight, scrollX, scrollY);

    /// <summary>Where page <paramref name="page"/> is drawn in the viewport.</summary>
    public PageLayout PageLayoutOf(int page)
    {
        var size = pages[page - 1];
        var (x, y) = Origin(page);
        return new PageLayout(Scale, x, y, size.Width * Scale, size.Height * Scale);
    }

    /// <summary>
    /// The page under a viewport point (in a gap, the page before the gap's middle; beyond the ends, the first or
    /// last page) and the point on it, in page points. The point can lie outside the page.
    /// </summary>
    public (int Page, double X, double Y) ToPage(double viewportX, double viewportY)
    {
        var along = orientation == ScrollOrientation.Vertical ? viewportY - offsetY + ScrollY : viewportX - offsetX + ScrollX;
        // The last page whose leading edge, less half a gap, is at or before the point.
        int low = 0, high = pages.Count - 1;
        while (low < high)
        {
            var middle = (low + high + 1) / 2;
            if (start[middle] - gap / 2 <= along) low = middle; else high = middle - 1;
        }
        var page = low + 1;
        var (x, y) = Origin(page);
        return (page, (viewportX - x) / Scale, (viewportY - y) / Scale);
    }

    /// <summary>The page at the viewport's centre: the "current" page while scrolling.</summary>
    public int CurrentPage => pages.Count == 0 ? 0 : ToPage(ViewportWidth / 2, ViewportHeight / 2).Page;

    /// <summary>
    /// Pages that intersect the viewport extended by <paramref name="margin"/> viewports before and after it along
    /// the scroll axis (0: just the visible ones), first to last.
    /// </summary>
    public IEnumerable<int> PagesInView(double margin = 0)
    {
        var (viewStart, viewLength) = orientation == ScrollOrientation.Vertical
            ? (ScrollY - offsetY, ViewportHeight) : (ScrollX - offsetX, ViewportWidth);
        double from = viewStart - margin * viewLength, to = viewStart + viewLength * (1 + margin);
        for (int i = 0; i < pages.Count; i++)
        {
            var end = start[i] + Along(pages[i]) * Scale;
            if (end <= from) continue;   // touching the edge is not in view
            if (start[i] >= to) break;
            yield return i + 1;
        }
    }

    /// <summary>The scroll position that puts the point (pageX, pageY) of <paramref name="page"/> at the viewport's
    /// top-left corner, before clamping.</summary>
    public (double X, double Y) ScrollFor(int page, double pageX, double pageY)
    {
        var (x, y) = ContentOrigin(page);
        return (x + pageX * Scale, y + pageY * Scale);
    }

    /// <summary>The scroll position that shows <paramref name="page"/> from its leading edge (less the gap).</summary>
    public (double X, double Y) ScrollToStartOf(int page) => orientation == ScrollOrientation.Vertical
        ? (ScrollX, start[page - 1] - gap)
        : (start[page - 1] - gap, ScrollY);

    // Top-left corner of a page in content coordinates (before scrolling and centring).
    private (double X, double Y) ContentOrigin(int page)
    {
        var size = pages[page - 1];
        var across = gap + (maxCross - Across(size) * Scale) / 2;
        return orientation == ScrollOrientation.Vertical ? (across, start[page - 1]) : (start[page - 1], across);
    }

    private (double X, double Y) Origin(int page)
    {
        var (x, y) = ContentOrigin(page);
        return (x + offsetX - ScrollX, y + offsetY - ScrollY);
    }

    private double Along(PageSize size) => orientation == ScrollOrientation.Vertical ? size.Height : size.Width;
    private double Across(PageSize size) => orientation == ScrollOrientation.Vertical ? size.Width : size.Height;
}
