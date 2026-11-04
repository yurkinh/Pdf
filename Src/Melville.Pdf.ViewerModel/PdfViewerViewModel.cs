using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Melville.Pdf.Model.OptionalContent;
using Melville.Pdf.Model.Renderers.DocumentRenderers;

namespace Melville.Pdf.ViewerModel;

/// <summary>
/// The UI-independent state of a PDF viewer: the current page, its rendering, zoom and layout, and the
/// optional content (layers) list. Property changes may be raised from a background thread.
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
    private PageLayout layout;

    private PdfViewerViewModel(DocumentRenderer document, IPageRenderer<TPage> renderer)
    {
        this.document = document;
        this.renderer = renderer;
        pageSelector.MaxPage = document.TotalPages;
        pageSelector.PropertyChanged += OnPageSelectorChanged;
        document.OptionalContentState.SelectedContentChanged += OnSelectedContentChanged;
        if (document.OptionalContentState is INotifyPropertyChanged notifying)
            notifying.PropertyChanged += OnOptionalContentStateChanged;

        ZoomInCommand = new RelayCommand(() => SetCustomScale(layout.Scale * ZoomStep), () => CanZoom(ZoomStep), this);
        ZoomOutCommand = new RelayCommand(() => SetCustomScale(layout.Scale / ZoomStep), () => CanZoom(1 / ZoomStep), this);
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
        await viewModel.RebuildLayersAsync().ConfigureAwait(false);
        await viewModel.RenderCurrentPageAsync().ConfigureAwait(false);
        return viewModel;
    }

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
            if (Set(ref zoomMode, value)) UpdateLayout();
        }
    }

    /// <summary>The scale used in <see cref="Melville.Pdf.ViewerModel.ZoomMode.Custom"/> mode, clamped to 0.1–16.</summary>
    public double CustomScale
    {
        get => customScale;
        set
        {
            if (Set(ref customScale, Math.Clamp(value, MinScale, MaxScale)) && ZoomMode == ZoomMode.Custom)
                UpdateLayout();
        }
    }

    /// <summary>Where to draw the current page in the viewport. Updated by <see cref="SetViewport"/>, zoom and page changes.</summary>
    public PageLayout Layout
    {
        get => layout;
        private set => Set(ref layout, value);
    }

    /// <summary>Zoom in by one step from the current scale; switches to Custom mode.</summary>
    public ICommand ZoomInCommand { get; }

    /// <summary>Zoom out by one step from the current scale; switches to Custom mode.</summary>
    public ICommand ZoomOutCommand { get; }

    /// <summary>Show the whole page.</summary>
    public ICommand FitPageCommand { get; }

    /// <summary>Fit the page to the viewport width.</summary>
    public ICommand FitWidthCommand { get; }

    /// <summary>Tell the view model the size of the area the page is drawn in.</summary>
    public void SetViewport(double width, double height)
    {
        viewportWidth = width;
        viewportHeight = height;
        UpdateLayout();
    }

    /// <summary>
    /// Render the selected page again, cancelling any render still pending. The returned task completes when
    /// this request has been rendered, superseded or failed; it never throws.
    /// </summary>
    public Task RenderCurrentPageAsync()
    {
        if (disposed) return Task.CompletedTask;
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
        if (e.PropertyName == nameof(PageSelectorViewModel.Page)) _ = RenderCurrentPageAsync();
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

    private void SetCustomScale(double scale)
    {
        CustomScale = scale;
        ZoomMode = ZoomMode.Custom;
        UpdateLayout();
    }

    private void UpdateLayout()
    {
        Layout = currentPage is { } page
            ? PageLayout.Compute(viewportWidth, viewportHeight, page.Width, page.Height, zoomMode, customScale)
            : default;
    }

    /// <summary>
    /// Stop rendering and release the document. Waits for a render in progress to finish first.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (disposed) return;
        disposed = true;
        pendingRender?.Cancel();
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

    private bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        return true;
    }
}
