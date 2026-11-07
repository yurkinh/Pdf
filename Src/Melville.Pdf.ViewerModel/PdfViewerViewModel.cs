using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Melville.Pdf.LowLevel.Model.Wrappers;
using Melville.Pdf.Model.Documents;
using Melville.Pdf.Model.OptionalContent;
using Melville.Pdf.TextExtractor;
using Melville.Pdf.Model.Renderers.DocumentRenderers;

namespace Melville.Pdf.ViewerModel;

/// <summary>
/// The UI-independent state of a PDF viewer: the current page, its rendering, zoom and layout, and the
/// optional content (layers) list. Property changes may be raised from a background thread.
/// In <see cref="Melville.Pdf.ViewerModel.DisplayMode.Continuous"/> mode every page sits in one scrolling strip
/// (<see cref="ContinuousLayout"/>); the pages in view are rendered on demand and published in
/// <see cref="VisiblePages"/>, which the view draws in either mode.
/// </summary>
/// <typeparam name="TPage">What the display technology draws for one page.</typeparam>
public sealed class PdfViewerViewModel<TPage> : INotifyPropertyChanged, IAsyncDisposable
{
    private const double ZoomStep = 1.25;
    private const double MinScale = 0.1;
    private const double MaxScale = 16;

    private readonly DocumentRenderer document;
    private readonly IPageRenderer<TPage> renderer;
    private readonly PageSelectorViewModel pageSelector = new();
    private readonly SemaphoreSlim renderGate = new(1, 1);
    private CancellationTokenSource? pendingRender;
    private int renderGeneration;
    private bool disposed;

    private RenderedPage<TPage>? currentPage;
    private bool isRendering;
    private string? renderError;
    private IReadOnlyList<LayerRow> layers = [];
    private ZoomMode zoomMode = ZoomMode.FitPage;
    private double customScale = 1;
    private double viewportWidth;
    private double viewportHeight;
    private double scrollX;
    private double scrollY;
    private int layoutPageNumber;
    private int renderedPageNumber;
    private PdfViewState? pendingRestore;
    private PageLayout layout;

    // Continuous mode. Guarded by continuousLock: layout updates come from the UI (pan, zoom) and from renders.
    private readonly object continuousLock = new();
    private PageSize[] pageSizes = [];
    private PdfRect[] pageBoxes = [];
    private DisplayMode displayMode = DisplayMode.SinglePage;
    private ScrollOrientation scrollOrientation = ScrollOrientation.Vertical;
    private ContinuousLayout? continuousLayout;
    private double continuousScrollX, continuousScrollY;
    private readonly Dictionary<int, RenderedPage<TPage>> renderedPages = [];
    private readonly HashSet<int> failedPages = [];
    private int[] wantedPages = [];
    private int continuousGeneration;
    private int pumping;
    private bool syncingPageSelector;
    private bool scrollToCurrentPageStart; // the next continuous layout shows the current page from its top
    private IReadOnlyList<PagePlacement<TPage>> visiblePages = [];

    private PdfViewerViewModel(DocumentRenderer document, IPageRenderer<TPage> renderer)
    {
        this.document = document;
        this.renderer = renderer;
        pageSelector.MaxPage = document.TotalPages;
        pageSelector.PropertyChanged += OnPageSelectorChanged;
        document.OptionalContentState.SelectedContentChanged += OnSelectedContentChanged;
        if (document.OptionalContentState is INotifyPropertyChanged notifying)
            notifying.PropertyChanged += OnOptionalContentStateChanged;

        ZoomInCommand = new RelayCommand(() => ZoomAt(ZoomStep, viewportWidth / 2, viewportHeight / 2), () => CanZoom(ZoomStep), this);
        ZoomOutCommand = new RelayCommand(() => ZoomAt(1 / ZoomStep, viewportWidth / 2, viewportHeight / 2), () => CanZoom(1 / ZoomStep), this);
        FitPageCommand = new RelayCommand(() => ZoomMode = ZoomMode.FitPage, () => ZoomMode != ZoomMode.FitPage, this);
        FitWidthCommand = new RelayCommand(() => ZoomMode = ZoomMode.FitWidth, () => ZoomMode != ZoomMode.FitWidth, this);
    }

    /// <summary>
    /// Create a view model for an open document, render its first page and build its layers list.
    /// The view model owns the document and disposes it in <see cref="DisposeAsync"/>.
    /// </summary>
    public static async Task<PdfViewerViewModel<TPage>> CreateAsync(
        DocumentRenderer document, IPageRenderer<TPage> renderer)
    {
        var viewModel = new PdfViewerViewModel<TPage>(document, renderer);
        await viewModel.MeasurePagesAsync().ConfigureAwait(false);
        await viewModel.RebuildLayersAsync().ConfigureAwait(false);
        await viewModel.RenderCurrentPageAsync().ConfigureAwait(false);
        return viewModel;
    }

    // Every page's size, for the continuous layout: the page boxes only, no content is parsed.
    private async Task MeasurePagesAsync()
    {
        var sizes = new PageSize[document.TotalPages];
        var boxes = new PdfRect[sizes.Length];
        for (int i = 0; i < sizes.Length; i++)
        {
            var box = boxes[i] = await document.GetPageBoxAsync(i + 1).ConfigureAwait(false);
            sizes[i] = new PageSize(Math.Abs(box.Right - box.Left), Math.Abs(box.Top - box.Bottom));
        }
        pageBoxes = boxes;
        pageSizes = sizes;
    }

    /// <summary>One page at a time (the default) or all pages in a scrolling strip.</summary>
    public DisplayMode DisplayMode
    {
        get => displayMode;
        set
        {
            if (displayMode == value) return;
            displayMode = value;
            OnPropertyChanged(nameof(DisplayMode));
            if (value == DisplayMode.Continuous)
            {
                // Show the current page from its top, as navigating to it does.
                scrollToCurrentPageStart = true;
                UpdateLayout();
            }
            else
            {
                lock (continuousLock) continuousGeneration++; // drop renders in flight for the strip
                layoutPageNumber = 0; // the single page starts at its top-left
                _ = RenderCurrentPageAsync();
                UpdateLayout();
            }
        }
    }

    /// <summary>The direction pages follow each other in continuous mode.</summary>
    public ScrollOrientation ScrollOrientation
    {
        get => scrollOrientation;
        set
        {
            if (scrollOrientation == value) return;
            var anchor = TopLeftAnchor();
            scrollOrientation = value;
            OnPropertyChanged(nameof(ScrollOrientation));
            UpdateLayout(anchor);
        }
    }

    /// <summary>The pages to draw and where: the current page in single-page mode, the pages in view in continuous mode.</summary>
    public IReadOnlyList<PagePlacement<TPage>> VisiblePages
    {
        get => visiblePages;
        private set => Set(ref visiblePages, value);
    }

    /// <summary>The strip layout in continuous mode (null in single-page mode, or before the viewport is known).</summary>
    public ContinuousLayout? Continuous => continuousLayout;

    /// <summary>The page selector: current page, page count and navigation commands.</summary>
    public PageSelectorViewModel PageSelector => pageSelector;

    /// <summary>The number of pages in the document.</summary>
    public int PageCount => document.TotalPages;

    /// <summary>The most recently rendered page, or null before the first render finishes.</summary>
    public RenderedPage<TPage>? CurrentPage
    {
        get => currentPage;
        private set
        {
            if (Set(ref currentPage, value)) UpdateLayout();
        }
    }

    /// <summary>True while a requested page is being rendered.</summary>
    public bool IsRendering
    {
        get => isRendering;
        private set => Set(ref isRendering, value);
    }

    /// <summary>The message of the last render failure, or null when the current page rendered.</summary>
    public string? RenderError
    {
        get => renderError;
        private set => Set(ref renderError, value);
    }

    /// <summary>The optional content (layers) tree, flattened for a list control. Empty when the document has none.</summary>
    public IReadOnlyList<LayerRow> Layers
    {
        get => layers;
        private set => Set(ref layers, value);
    }

    /// <summary>How the page is sized to the viewport.</summary>
    public ZoomMode ZoomMode
    {
        get => zoomMode;
        set
        {
            var anchor = TopLeftAnchor();
            if (Set(ref zoomMode, value)) UpdateLayout(anchor);
        }
    }

    /// <summary>The scale used in <see cref="Melville.Pdf.ViewerModel.ZoomMode.Custom"/> mode, clamped to 0.1–16.</summary>
    public double CustomScale
    {
        get => customScale;
        set
        {
            var anchor = TopLeftAnchor();
            if (Set(ref customScale, Math.Clamp(value, MinScale, MaxScale)) && ZoomMode == ZoomMode.Custom)
                UpdateLayout(anchor);
        }
    }

    /// <summary>Where to draw the current page in the viewport. Updated by <see cref="SetViewport"/>, zoom and page changes.</summary>
    public PageLayout Layout
    {
        get => layout;
        private set
        {
            if (Set(ref layout, value)) OnPropertyChanged(nameof(ViewState));
        }
    }

    /// <summary>
    /// The page, zoom and scroll position, for saving. Changes (raising PropertyChanged) whenever any of them do;
    /// while a <see cref="Restore"/> is waiting for its page to render, this is the state being restored.
    /// </summary>
    public PdfViewState ViewState
    {
        get
        {
            if (pendingRestore is { } pending) return pending;
            if (displayMode == DisplayMode.Continuous && continuousLayout is { } strip)
            {
                var (page, x, y) = strip.ToPage(0, 0);
                return new PdfViewState(page, zoomMode, zoomMode == ZoomMode.Custom ? customScale : strip.Scale, x, y);
            }
            var scale = layout.Scale > 0 ? layout.Scale : customScale;
            return new PdfViewState(pageSelector.Page, zoomMode, zoomMode == ZoomMode.Custom ? customScale : scale,
                layout.Scale > 0 ? layout.ScrollX / layout.Scale : 0, layout.Scale > 0 ? layout.ScrollY / layout.Scale : 0);
        }
    }

    /// <summary>
    /// Go back to a saved <see cref="ViewState"/>: its page (clamped to the document), zoom and scroll position.
    /// The scroll position is applied once that page has rendered, and is clamped to the page as usual.
    /// </summary>
    public void Restore(PdfViewState state)
    {
        var page = Math.Clamp(state.Page, pageSelector.MinPage, pageSelector.MaxPage);
        pendingRestore = state with { Page = page };
        customScale = Math.Clamp(state.Zoom > 0 ? state.Zoom : 1, MinScale, MaxScale);
        zoomMode = state.ZoomMode;
        OnPropertyChanged(nameof(CustomScale));
        OnPropertyChanged(nameof(ZoomMode));
        if (displayMode == DisplayMode.Continuous) UpdateLayout(); // applied as soon as the viewport is known
        else if (pageSelector.Page != page) pageSelector.Page = page; // renders it; the layout follows the render
        else UpdateLayout();
    }

    /// <summary>
    /// Scroll a page that is larger than the viewport by a drag of (dx, dy) viewport units: dragging right
    /// moves the page right. Clamped so the page never leaves an empty edge.
    /// </summary>
    public void PanBy(double dx, double dy)
    {
        if (displayMode == DisplayMode.Continuous)
        {
            lock (continuousLock) (continuousScrollX, continuousScrollY) = (continuousScrollX - dx, continuousScrollY - dy);
            UpdateLayout();
            return;
        }
        scrollX -= dx;
        scrollY -= dy;
        UpdateLayout();
    }

    /// <summary>
    /// Zoom by <paramref name="factor"/> keeping the page point under (viewportX, viewportY) in place, as a pinch
    /// does. Switches to Custom mode; the scale is clamped to 0.1–16.
    /// </summary>
    public void ZoomAt(double factor, double viewportX, double viewportY)
    {
        if (displayMode == DisplayMode.Continuous)
        {
            if (continuousLayout is not { } strip || factor <= 0) return;
            var (page, x, y) = strip.ToPage(viewportX, viewportY);
            customScale = Math.Clamp(strip.Scale * factor, MinScale, MaxScale);
            zoomMode = ZoomMode.Custom;
            // The point under the finger stays there: anchor it at (viewportX, viewportY) instead of the corner.
            UpdateLayout(new Anchor(page, x, y, viewportX, viewportY));
            OnPropertyChanged(nameof(CustomScale));
            OnPropertyChanged(nameof(ZoomMode));
            return;
        }
        if (layout.Scale <= 0 || factor <= 0) return;
        var (pageX, pageY) = layout.ToPage(viewportX, viewportY);
        var scale = Math.Clamp(layout.Scale * factor, MinScale, MaxScale);
        customScale = scale;
        zoomMode = ZoomMode.Custom;
        // Where the page origin must sit for that point to stay under the finger; scroll is minus that offset.
        scrollX = pageX * scale - viewportX;
        scrollY = pageY * scale - viewportY;
        UpdateLayout();
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CustomScale)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ZoomMode)));
    }

    /// <summary>Zoom in by one step around the viewport centre; switches to Custom mode.</summary>
    public ICommand ZoomInCommand { get; }

    /// <summary>Zoom out by one step around the viewport centre; switches to Custom mode.</summary>
    public ICommand ZoomOutCommand { get; }

    /// <summary>Show the whole page.</summary>
    public ICommand FitPageCommand { get; }

    /// <summary>Fit the page to the viewport width.</summary>
    public ICommand FitWidthCommand { get; }

    /// <summary>Tell the view model the size of the area the page is drawn in.</summary>
    public void SetViewport(double width, double height)
    {
        var anchor = TopLeftAnchor();
        viewportWidth = width;
        viewportHeight = height;
        UpdateLayout(anchor);
    }

    /// <summary>
    /// Render the selected page again, cancelling any render still pending. The returned task completes when
    /// this request has been rendered, superseded or failed; it never throws.
    /// </summary>
    public Task RenderCurrentPageAsync()
    {
        if (disposed) return Task.CompletedTask;
        if (displayMode == DisplayMode.Continuous) return RerenderContinuousAsync();
        var cancellation = new CancellationTokenSource();
        Interlocked.Exchange(ref pendingRender, cancellation)?.Cancel();
        var generation = Interlocked.Increment(ref renderGeneration);
        return RenderAsync(pageSelector.Page, generation, cancellation.Token);
    }

    private async Task RenderAsync(int pageNumber, int generation, CancellationToken cancellation)
    {
        IsRendering = true;
        await renderGate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        try
        {
            if (cancellation.IsCancellationRequested) return;
            var page = await renderer.RenderAsync(document, pageNumber, cancellation).ConfigureAwait(false);
            if (cancellation.IsCancellationRequested)
            {
                (page.Content as IDisposable)?.Dispose(); // never published, so nothing else can hold it
                return;
            }
            // Pages that were published are not disposed here: the view may still be drawing the old one.
            renderedPageNumber = pageNumber;
            CurrentPage = page;
            RenderError = null;
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }
        catch (Exception e) when (!cancellation.IsCancellationRequested)
        {
            RenderError = e.Message;
        }
        finally
        {
            renderGate.Release();
            if (Volatile.Read(ref renderGeneration) == generation) IsRendering = false;
        }
    }

    private async Task RebuildLayersAsync()
    {
        var state = document.OptionalContentState;
        var groups = await state.ConstructUiModelAsync(state.SelectedConfiguration?.Order).ConfigureAwait(false);
        Layers = LayerRow.Flatten(groups);
    }

    private void OnPageSelectorChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(PageSelectorViewModel.Page)) return;
        // A restore waiting for another page is abandoned when the user picks a page themselves.
        if (!syncingPageSelector && pendingRestore is { } pending && pending.Page != pageSelector.Page) pendingRestore = null;
        OnPropertyChanged(nameof(ViewState));
        if (displayMode == DisplayMode.Continuous)
        {
            // Scrolling moved the current page: nothing to do. Navigation (toolbar, typed page): scroll to it.
            if (syncingPageSelector || continuousLayout is not { } strip) return;
            lock (continuousLock) (continuousScrollX, continuousScrollY) = strip.ScrollToStartOf(pageSelector.Page);
            UpdateLayout();
            return;
        }
        if (selection is { } selected && selected.Page != pageSelector.Page) ClearSelection(); // it left the view
        _ = RenderCurrentPageAsync();
    }

    private void OnSelectedContentChanged(object? sender, EventArgs e) => _ = RenderCurrentPageAsync();

    private async void OnOptionalContentStateChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(IOptionalContentState.SelectedConfiguration)) return;
        try
        {
            await RebuildLayersAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            RenderError = ex.Message;
        }
    }

    private bool CanZoom(double factor) =>
        layout.Scale > 0 && Math.Clamp(layout.Scale * factor, MinScale, MaxScale) != layout.Scale;

    /// <summary>A page point to keep at a viewport position across a layout change (zoom, resize, mode change).</summary>
    private readonly record struct Anchor(int Page, double PageX, double PageY, double ViewportX = 0, double ViewportY = 0);

    // The page point at the viewport's top-left corner, in whichever mode is showing.
    private Anchor? TopLeftAnchor()
    {
        if (displayMode == DisplayMode.Continuous)
            return continuousLayout is { } strip && strip.PageCount > 0 ? ToAnchor(strip.ToPage(0, 0)) : null;
        return layout.Scale > 0 && renderedPageNumber > 0 ? ToAnchor((renderedPageNumber, layout.ToPage(0, 0).X, layout.ToPage(0, 0).Y)) : null;
        static Anchor ToAnchor((int Page, double X, double Y) point) => new(point.Page, point.X, point.Y);
    }

    private void UpdateLayout(Anchor? anchor = null)
    {
        if (displayMode == DisplayMode.Continuous)
        {
            UpdateContinuousLayout(anchor);
            return;
        }
        UpdateSinglePageLayout();
        VisiblePages = currentPage is { } shown && layout.Scale > 0
            ? [new PagePlacement<TPage>(renderedPageNumber, layout, shown)]
            : [];
    }

    private void UpdateContinuousLayout(Anchor? anchor)
    {
        if (pageSizes.Length == 0 || viewportWidth <= 0 || viewportHeight <= 0) return;
        ContinuousLayout strip;
        int[] wanted;
        int current;
        lock (continuousLock)
        {
            strip = ContinuousLayout.Compute(pageSizes, scrollOrientation, zoomMode, customScale,
                viewportWidth, viewportHeight, continuousScrollX, continuousScrollY);
            if (pendingRestore is { } restore)
            {
                pendingRestore = null;
                anchor = new Anchor(Math.Clamp(restore.Page, 1, pageSizes.Length), restore.PageX, restore.PageY);
            }
            if (scrollToCurrentPageStart)
            {
                scrollToCurrentPageStart = false;
                var (x, y) = strip.ScrollToStartOf(Math.Clamp(pageSelector.Page, 1, pageSizes.Length));
                strip = strip.ScrolledTo(x, y);
            }
            else if (anchor is { } a)
            {
                var (x, y) = strip.ScrollFor(a.Page, a.PageX, a.PageY);
                strip = strip.ScrolledTo(x - a.ViewportX, y - a.ViewportY);
            }
            (continuousScrollX, continuousScrollY) = (strip.ScrollX, strip.ScrollY);
            continuousLayout = strip;
            current = strip.CurrentPage;
            // Render the pages in view, then their neighbours (up to a viewport away); forget pages far away.
            var near = strip.PagesInView(margin: 1).ToList();
            wanted = [.. near.OrderBy(p => Math.Abs(p - current))];
            var keep = strip.PagesInView(margin: 3).ToHashSet();
            foreach (var far in renderedPages.Keys.Where(p => !keep.Contains(p)).ToList()) renderedPages.Remove(far);
            wantedPages = wanted;
        }
        Layout = strip.PageLayoutOf(current);
        PublishVisiblePages();
        if (pageSelector.Page != current)
        {
            syncingPageSelector = true;
            try { pageSelector.Page = current; }
            finally { syncingPageSelector = false; }
        }
        OnPropertyChanged(nameof(Continuous));
        OnPropertyChanged(nameof(ViewState));
        StartRenderPump();
    }

    private void PublishVisiblePages()
    {
        List<PagePlacement<TPage>> placements = [];
        lock (continuousLock)
        {
            if (continuousLayout is not { } strip) return;
            foreach (var page in strip.PagesInView())
                placements.Add(new PagePlacement<TPage>(page, strip.PageLayoutOf(page), renderedPages.GetValueOrDefault(page)));
        }
        VisiblePages = placements;
    }

    // Renders the wanted pages one at a time, nearest the current page first, until all are rendered. Pages are
    // re-read from wantedPages after each render, so scrolling away changes what comes next.
    private void StartRenderPump()
    {
        if (disposed || Interlocked.Exchange(ref pumping, 1) == 1) return;
        _ = PumpAsync();
    }

    private async Task PumpAsync()
    {
        IsRendering = true;
        try
        {
            while (!disposed)
            {
                int page, generation;
                lock (continuousLock)
                {
                    page = wantedPages.FirstOrDefault(p => !renderedPages.ContainsKey(p) && !failedPages.Contains(p));
                    generation = continuousGeneration;
                }
                if (page == 0 || displayMode != DisplayMode.Continuous) break;
                await RenderContinuousPageAsync(page, generation).ConfigureAwait(false);
            }
        }
        finally
        {
            Volatile.Write(ref pumping, 0);
            IsRendering = false;
        }
        // A layout change can have asked for more while the last render finished.
        lock (continuousLock)
            if (displayMode != DisplayMode.Continuous || !wantedPages.Any(p => !renderedPages.ContainsKey(p) && !failedPages.Contains(p))) return;
        StartRenderPump();
    }

    private async Task RenderContinuousPageAsync(int page, int generation)
    {
        await renderGate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (disposed) return;
            var rendered = await renderer.RenderAsync(document, page, CancellationToken.None).ConfigureAwait(false);
            lock (continuousLock)
            {
                if (generation != continuousGeneration) return; // layers changed meanwhile: render it again
                renderedPages[page] = rendered;
            }
            RenderError = null;
        }
        catch (Exception e)
        {
            lock (continuousLock) failedPages.Add(page);
            RenderError = e.Message;
        }
        finally
        {
            renderGate.Release();
        }
        PublishVisiblePages();
    }

    // Layers changed: every cached page is stale.
    private Task RerenderContinuousAsync()
    {
        lock (continuousLock)
        {
            continuousGeneration++;
            renderedPages.Clear();
            failedPages.Clear();
        }
        UpdateLayout();
        return Task.CompletedTask;
    }

    private void UpdateSinglePageLayout()
    {
        if (currentPage is not { } page)
        {
            Layout = default;
            return;
        }
        // A newly shown page starts at its top-left corner. This is the page that rendered, not the one selected:
        // until the new page arrives, the old one is still on screen and keeps its scroll position.
        if (renderedPageNumber != layoutPageNumber)
        {
            layoutPageNumber = renderedPageNumber;
            scrollX = scrollY = 0;
        }
        if (pendingRestore is { } restore && restore.Page == renderedPageNumber && viewportWidth > 0 && viewportHeight > 0)
        {
            pendingRestore = null;
            var scale = PageLayout.Compute(viewportWidth, viewportHeight, page.Width, page.Height, zoomMode, customScale).Scale;
            (scrollX, scrollY) = (restore.PageX * scale, restore.PageY * scale);
        }
        var computed = PageLayout.Compute(viewportWidth, viewportHeight, page.Width, page.Height,
            zoomMode, customScale, scrollX, scrollY);
        (scrollX, scrollY) = (computed.ScrollX, computed.ScrollY); // keep the clamped values
        Layout = computed;
    }

    // ---- Links ------------------------------------------------------------------------------------------------

    private readonly Dictionary<int, IReadOnlyList<PageLink>> links = [];

    /// <summary>
    /// The links on a page, in page points from its top-left corner (the coordinates of
    /// <see cref="PageLayout.ToPage"/>). Read once per page; reading waits for a render in progress, because the
    /// document is not read from two threads at once.
    /// </summary>
    public async Task<IReadOnlyList<PageLink>> GetLinksAsync(int page)
    {
        lock (links)
            if (links.TryGetValue(page, out var known)) return known;
        if (page < 1 || page > pageBoxes.Length) return [];
        IReadOnlyList<PdfLink> read;
        await renderGate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (disposed) return [];
            read = await document.GetPageLinksAsync(page).ConfigureAwait(false);
        }
        finally
        {
            renderGate.Release();
        }
        var box = pageBoxes[page - 1];
        IReadOnlyList<PageLink> result = [.. read.Select(link => new PageLink(ToPageRect(box, link.Area), link.Target))];
        lock (links) links[page] = result;
        return result;
    }

    /// <summary>
    /// The link under a viewport point, on whichever page is drawn there, or null. When links overlap, the last
    /// one in the page's annotation list (the one drawn on top) wins.
    /// </summary>
    public async Task<PdfLinkTarget?> LinkAtAsync(double viewportX, double viewportY)
    {
        foreach (var placement in visiblePages)
        {
            var (x, y) = placement.Layout.ToPage(viewportX, viewportY);
            var size = pageSizes.ElementAtOrDefault(placement.Page - 1);
            if (x < 0 || y < 0 || x > size.Width || y > size.Height) continue;
            var onPage = await GetLinksAsync(placement.Page).ConfigureAwait(false);
            for (int i = onPage.Count - 1; i >= 0; i--)
                if (onPage[i].Area.Contains(x, y)) return onPage[i].Target;
            return null;
        }
        return null;
    }

    /// <summary>
    /// Show the page a link goes to, at the link's position when it has one, keeping the zoom. A missing
    /// horizontal position keeps the current one; a missing vertical position shows the top of the page.
    /// </summary>
    public void GoTo(PdfPageLinkTarget target)
    {
        var page = Math.Clamp(target.OneBasedPageNumber, 1, Math.Max(1, pageBoxes.Length));
        if (pageBoxes.Length == 0) return;
        var box = pageBoxes[page - 1];
        var current = ViewState;
        var x = target.Left is { } left ? ToPageX(box, left) : current.PageX;
        var y = target.Top is { } top ? ToPageY(box, top) : 0;
        Restore(current with { Page = page, PageX = Math.Max(0, x), PageY = Math.Max(0, y) });
    }

    // PDF coordinates in the page's visible box (y up, corners in either order after a rotation) to page points
    // from the top-left corner, as the page is drawn (see RenderTargetBase.MapUserSpaceToBitmapSpace).
    private static double ToPageX(PdfRect box, double x) => (x - box.Left) * Math.Sign(box.Right - box.Left);
    private static double ToPageY(PdfRect box, double y) => (box.Top - y) * Math.Sign(box.Top - box.Bottom);

    private static PageRect ToPageRect(PdfRect box, PdfRect area)
    {
        double x1 = ToPageX(box, area.Left), x2 = ToPageX(box, area.Right);
        double y1 = ToPageY(box, area.Top), y2 = ToPageY(box, area.Bottom);
        return new PageRect(Math.Min(x1, x2), Math.Min(y1, y2), Math.Abs(x2 - x1), Math.Abs(y2 - y1));
    }

    // ---- Text ---------------------------------------------------------------------------------------------------

    private const int CachedTextPages = 16;
    private readonly Dictionary<int, PageText> texts = [];
    private readonly LinkedList<int> textUse = []; // most recently used first

    /// <summary>
    /// The text of a page with every glyph's position, in page points from its top-left corner (the coordinates
    /// of <see cref="PageLayout.ToPage"/> and <see cref="PageLink"/>). The most recently used pages are kept.
    /// Reading waits for a render in progress, because the document is not read from two threads at once.
    /// </summary>
    public async Task<PageText> GetTextAsync(int page)
    {
        lock (texts)
            if (texts.TryGetValue(page, out var known))
            {
                textUse.Remove(page);
                textUse.AddFirst(page);
                return known;
            }
        if (page < 1 || page > document.TotalPages) return PageText.Empty;
        PageText text;
        await renderGate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (disposed) return PageText.Empty;
            text = await document.PageTextLayerAsync(page).ConfigureAwait(false);
        }
        finally
        {
            renderGate.Release();
        }
        lock (plainTexts) plainTexts[page] = text.Text;
        lock (texts)
        {
            if (texts.TryAdd(page, text)) textUse.AddFirst(page);
            while (textUse.Count > CachedTextPages)
            {
                texts.Remove(textUse.Last!.Value);
                textUse.RemoveLast();
            }
        }
        return text;
    }

    // The plain text of every page read so far (a few kilobytes a page, against hundreds for the glyphs): a search
    // matches it without reading the page again, and reads glyph positions only for pages that match.
    private readonly Dictionary<int, string> plainTexts = [];

    // A page's text for a search: from the cache when it is there, otherwise read without filling the cache, so a
    // search through a long document does not push out the pages being looked at.
    private async Task<PageText> SearchTextAsync(int page)
    {
        lock (texts)
            if (texts.TryGetValue(page, out var known)) return known;
        PageText text;
        await renderGate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (disposed) return PageText.Empty;
            text = await document.PageTextLayerAsync(page).ConfigureAwait(false);
        }
        finally
        {
            renderGate.Release();
        }
        lock (plainTexts) plainTexts[page] = text.Text;
        return text;
    }

    // The matches on a page: pages whose text is known are matched first, and read only when they match.
    private async Task<IReadOnlyList<SearchMatch>> SearchPageAsync(int page, string query, bool matchCase)
    {
        string? known;
        lock (plainTexts) plainTexts.TryGetValue(page, out known);
        if (known is not null && TextSearch.FindRanges(known, query, matchCase).Count == 0) return [];
        return TextSearch.Find(await SearchTextAsync(page).ConfigureAwait(false), page, query, matchCase);
    }

    // ---- Search -------------------------------------------------------------------------------------------------

    private const int SearchPublishMs = 100;
    private readonly object searchLock = new(); // a search's check that it is current and its publish are one step
    private CancellationTokenSource? searching;
    private string searchQuery = "";
    private IReadOnlyList<SearchMatch> searchMatches = [];
    private IReadOnlyDictionary<int, SearchMatch[]> matchesByPage = new Dictionary<int, SearchMatch[]>();
    private int currentMatchIndex = -1;
    private int searchedPages;
    private bool isSearching;
    private ICommand? nextMatchCommand, previousMatchCommand;

    /// <summary>The text of the last search, normalized (see <see cref="TextSearch.Normalize"/>); empty when there is none.</summary>
    public string SearchQuery
    {
        get => searchQuery;
        private set => Set(ref searchQuery, value);
    }

    /// <summary>
    /// The matches of the last search in document order. It grows while <see cref="IsSearching"/>: pages are searched
    /// from the current page to the end, then from the start, and each change publishes a new list.
    /// </summary>
    public IReadOnlyList<SearchMatch> SearchMatches
    {
        get => searchMatches;
        private set => Set(ref searchMatches, value);
    }

    /// <summary>The index in <see cref="SearchMatches"/> of the match being shown, or -1.</summary>
    public int CurrentMatchIndex
    {
        get => currentMatchIndex;
        private set
        {
            if (Set(ref currentMatchIndex, value)) OnPropertyChanged(nameof(CurrentMatch));
        }
    }

    /// <summary>The match being shown, or null.</summary>
    public SearchMatch? CurrentMatch => currentMatchIndex >= 0 && currentMatchIndex < searchMatches.Count
        ? searchMatches[currentMatchIndex] : null;

    /// <summary>How many pages the current search has read, for progress (out of <see cref="PageCount"/>).</summary>
    public int SearchedPages
    {
        get => searchedPages;
        private set => Set(ref searchedPages, value);
    }

    /// <summary>True while a search is reading pages.</summary>
    public bool IsSearching
    {
        get => isSearching;
        private set => Set(ref isSearching, value);
    }

    /// <summary>The matches on one page, for highlighting.</summary>
    public IReadOnlyList<SearchMatch> MatchesOn(int page) =>
        matchesByPage.TryGetValue(page, out var onPage) ? onPage : [];

    /// <summary>Show the next match, after the last one wrapping to the first.</summary>
    public ICommand NextMatchCommand => nextMatchCommand ??=
        new RelayCommand(() => ShowMatch((currentMatchIndex + 1) % searchMatches.Count), () => searchMatches.Count > 0, this);

    /// <summary>Show the previous match, before the first one wrapping to the last.</summary>
    public ICommand PreviousMatchCommand => previousMatchCommand ??=
        new RelayCommand(() => ShowMatch(currentMatchIndex <= 0 ? searchMatches.Count - 1 : currentMatchIndex - 1),
            () => searchMatches.Count > 0, this);

    /// <summary>
    /// Search every page for <paramref name="query"/> (see <see cref="TextSearch"/> for how text matches), replacing
    /// the previous search. The first match on or after the current page is shown as soon as it is found. The task
    /// completes when every page has been searched, or the search was replaced, cleared or cancelled; it never
    /// throws for those. Pages are read between renders, so the viewer stays responsive.
    /// </summary>
    /// <returns>The number of matches; for a search that was replaced or cancelled, those found until then.</returns>
    public async Task<int> SearchAsync(string query, bool matchCase = false, CancellationToken cancellation = default)
    {
        var mine = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        var wanted = TextSearch.Normalize(query);
        lock (searchLock)
        {
            searching?.Cancel();
            searching = mine;
            PublishMatches([], -1, wanted);
            SearchedPages = 0;
            if (wanted.Length == 0 || disposed) return 0;
            IsSearching = true; // inside the lock, so a newer search cannot finish first and leave it set
        }

        var token = mine.Token;
        List<SearchMatch> before = [], after = [];
        int current = -1;           // the match to show: an index in before or after
        var currentInBefore = false;
        var start = Math.Clamp(pageSelector.Page, 1, Math.Max(1, document.TotalPages));
        var lastPublish = Environment.TickCount64;
        var searched = 0;
        try
        {
            for (int i = 0; i < document.TotalPages && !token.IsCancellationRequested; i++)
            {
                var page = (start - 1 + i) % document.TotalPages + 1;
                var found = await SearchPageAsync(page, wanted, matchCase).ConfigureAwait(false);
                if (token.IsCancellationRequested || disposed) break;
                searched = i + 1;
                if (found.Count == 0)
                {
                    if (Environment.TickCount64 - lastPublish >= SearchPublishMs)
                    {
                        lastPublish = Environment.TickCount64;
                        if (!Publish()) break;
                    }
                    continue;
                }
                var wrapped = page < start;
                (wrapped ? before : after).AddRange(found);
                var show = current < 0;
                if (show) (current, currentInBefore) = wrapped ? (before.Count - found.Count, true) : (after.Count - found.Count, false);
                var now = Environment.TickCount64;
                if (show || now - lastPublish >= SearchPublishMs)
                {
                    lastPublish = now;
                    if (!Publish()) break;
                    if (show) ShowMatch(currentMatchIndex);
                }
            }
            if (!token.IsCancellationRequested) Publish();
            return before.Count + after.Count;
        }
        finally
        {
            lock (searchLock)
                if (ReferenceEquals(searching, mine)) IsSearching = false;
        }

        // Matches on pages before the start come first; the current match keeps pointing at the same match.
        bool Publish()
        {
            var index = current < 0 ? -1 : currentInBefore ? current : before.Count + current;
            lock (searchLock)
            {
                if (!ReferenceEquals(searching, mine) || token.IsCancellationRequested) return false;
                if (before.Count + after.Count != searchMatches.Count || index != currentMatchIndex)
                    PublishMatches([.. before, .. after], index, wanted);
                SearchedPages = searched;
            }
            return true;
        }
    }

    private void PublishMatches(IReadOnlyList<SearchMatch> matches, int current, string query)
    {
        matchesByPage = matches.GroupBy(m => m.Page).ToDictionary(g => g.Key, g => g.ToArray());
        SearchQuery = query;
        SearchMatches = matches;
        CurrentMatchIndex = current;
        OnPropertyChanged(nameof(CurrentMatch));
    }

    /// <summary>Forget the search: no matches, nothing highlighted. A search still reading pages stops.</summary>
    public void ClearSearch()
    {
        lock (searchLock)
        {
            searching?.Cancel();
            searching = null;
            PublishMatches([], -1, "");
            SearchedPages = 0;
            IsSearching = false;
        }
    }

    /// <summary>
    /// Make a match the current one and scroll it into view, keeping the zoom. A match already in view does not
    /// move the page; otherwise it is placed a third of the way down the view, and centred across it when the view
    /// would cut it off sideways.
    /// </summary>
    public void ShowMatch(int index)
    {
        if (index < 0 || index >= searchMatches.Count) return;
        CurrentMatchIndex = index;
        var match = searchMatches[index];
        var area = match.Bounds;
        var (scale, shown) = displayMode == DisplayMode.Continuous && continuousLayout is { } strip
            ? (strip.Scale, (PageLayout?)strip.PageLayoutOf(match.Page))
            : (layout.Scale, renderedPageNumber == match.Page && layout.Scale > 0 ? layout : (PageLayout?)null);
        if (scale <= 0 || viewportWidth <= 0 || viewportHeight <= 0)
        {
            Restore(ViewState with { Page = match.Page, PageY = Math.Max(0, area.Y - 36) });
            return;
        }
        if (shown is { } on && InView(on, area)) return;

        var current = ViewState;
        double viewWidth = viewportWidth / scale, viewHeight = viewportHeight / scale;
        var x = current.PageX;
        // The horizontal position only carries over on the same page; it is the left of the view in page points.
        if (shown is null || area.X < x || area.Right > x + viewWidth)
            x = area.Width >= viewWidth ? area.X : area.X - (viewWidth - area.Width) / 2;
        var y = area.Y - viewHeight / 3;
        Restore(current with { Page = match.Page, PageX = x, PageY = Math.Max(0, y) });
    }

    private bool InView(PageLayout on, PageRect area)
    {
        double left = on.OffsetX + area.X * on.Scale, top = on.OffsetY + area.Y * on.Scale;
        return left >= 0 && top >= 0 && left + area.Width * on.Scale <= viewportWidth &&
               top + area.Height * on.Scale <= viewportHeight;
    }

    // ---- Selection ----------------------------------------------------------------------------------------------

    private TextSelection? selection;
    private (int Page, int Start, int End) selectionAnchor; // the word the selection grows from
    private int selectionVersion;
    private ICommand? selectAllCommand;

    /// <summary>The selected text, on one page, or null. Changes raise PropertyChanged, possibly off the UI thread.</summary>
    public TextSelection? Selection
    {
        get => selection;
        private set => Set(ref selection, value);
    }

    /// <summary>Select all the text of the current page.</summary>
    public ICommand SelectAllCommand => selectAllCommand ??=
        new RelayCommand(() => _ = SelectAllAsync(pageSelector.Page), () => document.TotalPages > 0, this);

    /// <summary>
    /// Select the word under a viewport point, or the character there when it is not part of a word; with no text
    /// near the point, clear the selection. Returns whether something was selected.
    /// </summary>
    public async Task<bool> SelectWordAtAsync(double viewportX, double viewportY)
    {
        var version = Interlocked.Increment(ref selectionVersion);
        if (PagePointAt(viewportX, viewportY) is not var (page, x, y))
        {
            ClearSelection();
            return false;
        }
        var text = await GetTextAsync(page).ConfigureAwait(false);
        if (version != Volatile.Read(ref selectionVersion)) return false;
        var index = TextHitTest.IndexAt(text, x, y);
        if (index < 0 || !NearGlyph(text, index, x, y))
        {
            ClearSelection();
            return false;
        }
        var (start, end) = TextHitTest.WordAt(text.Text, index);
        selectionAnchor = (page, start, end);
        Selection = TextSelection.Of(text, page, start, end);
        return true;
    }

    /// <summary>
    /// Grow or shrink the selection from the word first selected to the character nearest a viewport point, on the
    /// same page (a selection does not cross pages). Does nothing without a selection.
    /// </summary>
    public async Task ExtendSelectionToAsync(double viewportX, double viewportY)
    {
        if (selection is null) return;
        var version = Interlocked.Increment(ref selectionVersion);
        var (page, anchorStart, anchorEnd) = selectionAnchor;
        if (PlacementOf(page) is not { } placed) return;
        var (x, y) = placed.ToPage(viewportX, viewportY);
        var text = await GetTextAsync(page).ConfigureAwait(false);
        if (version != Volatile.Read(ref selectionVersion) || selection is null) return;
        var index = TextHitTest.IndexAt(text, x, y);
        if (index < 0) return;
        var (start, end) = index >= anchorStart
            ? (anchorStart, Math.Max(anchorEnd, CharacterEnd(text, index)))
            : (index, anchorEnd);
        Selection = TextSelection.Of(text, page, start, end);
    }

    /// <summary>Select all the text of a page; a page without text clears the selection.</summary>
    public async Task SelectAllAsync(int page)
    {
        var version = Interlocked.Increment(ref selectionVersion);
        var text = await GetTextAsync(page).ConfigureAwait(false);
        if (version != Volatile.Read(ref selectionVersion)) return;
        if (text.Text.Length == 0)
        {
            ClearSelection();
            return;
        }
        selectionAnchor = (page, 0, text.Text.Length);
        Selection = TextSelection.Of(text, page, 0, text.Text.Length);
    }

    /// <summary>Select nothing.</summary>
    public void ClearSelection()
    {
        Interlocked.Increment(ref selectionVersion);
        Selection = null;
    }

    // Past the last character the glyph at index produced (a ligature produces several).
    private static int CharacterEnd(PageText text, int index)
    {
        var glyph = text.GlyphAt(index);
        var end = index + 1;
        while (end < text.Text.Length && text.GlyphAt(end) == glyph) end++;
        return end;
    }

    // A press must be on or close to a glyph (half a line height) to start a selection: not in a margin.
    private static bool NearGlyph(PageText text, int index, double x, double y)
    {
        var glyph = text.Glyphs[text.GlyphAt(index)];
        var (left, top, right, bottom) = glyph.Bounds;
        var slack = glyph.LineHeight / 2;
        return x >= left - slack && x <= right + slack && y >= top - slack && y <= bottom + slack;
    }

    // The page drawn under a viewport point and the point on it, or null over the background.
    private (int Page, double X, double Y)? PagePointAt(double viewportX, double viewportY)
    {
        foreach (var placement in visiblePages)
        {
            var (x, y) = placement.Layout.ToPage(viewportX, viewportY);
            var size = pageSizes.ElementAtOrDefault(placement.Page - 1);
            if (x >= 0 && y >= 0 && x <= size.Width && y <= size.Height) return (placement.Page, x, y);
        }
        return null;
    }

    // Where a page is drawn now, if it is in view.
    private PageLayout? PlacementOf(int page)
    {
        foreach (var placement in visiblePages)
            if (placement.Page == page) return placement.Layout;
        return null;
    }

    /// <summary>
    /// Stop rendering and release the document. Waits for a render in progress to finish first.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (disposed) return;
        disposed = true;
        pendingRender?.Cancel();
        Interlocked.Exchange(ref searching, null)?.Cancel();
        pageSelector.PropertyChanged -= OnPageSelectorChanged;
        document.OptionalContentState.SelectedContentChanged -= OnSelectedContentChanged;
        if (document.OptionalContentState is INotifyPropertyChanged notifying)
            notifying.PropertyChanged -= OnOptionalContentStateChanged;
        await renderGate.WaitAsync().ConfigureAwait(false);
        document.Dispose();
        renderGate.Release();
    }

    /// <inheritdoc />
    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    private bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        return true;
    }
}
