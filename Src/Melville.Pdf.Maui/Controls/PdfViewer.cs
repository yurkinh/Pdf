using System.Windows.Input;
using System.ComponentModel;
using System.Globalization;
using Melville.Pdf.LowLevel.Parsing.ParserContext;
using Melville.Pdf.FontLibrary;
using Melville.Pdf.MauiGraphics;
using Melville.Pdf.MauiGraphics.Drawing;
using Melville.Pdf.Model;
using Melville.Pdf.Model.Renderers.FontRenderings.DefaultFonts;
using Melville.Pdf.ViewerModel;
using Microsoft.Extensions.DependencyInjection;
using ViewModel = Melville.Pdf.ViewerModel.PdfViewerViewModel<Melville.Pdf.MauiGraphics.Drawing.PdfDisplayList>;

namespace Melville.Pdf.Maui.Controls;

/// <summary>
/// Displays a PDF document one page at a time, with page navigation, zoom (buttons, pinch, double tap), panning
/// and an optional layers list. Set <see cref="Source"/> to open a document.
/// </summary>
public partial class PdfViewer : ContentView
{
    // ---- Public API -------------------------------------------------------------------------------------------

    /// <summary>The document to show. Setting it closes the current document.</summary>
    public static readonly BindableProperty SourceProperty = BindableProperty.Create(
        nameof(Source), typeof(PdfSource), typeof(PdfViewer), propertyChanged: (b, _, _) => ((PdfViewer)b).OnSourceChanged());

    /// <summary>Supplies passwords for encrypted documents. Without one, encrypted documents fail to open.</summary>
    public static readonly BindableProperty PasswordSourceProperty = BindableProperty.Create(
        nameof(PasswordSource), typeof(IPasswordSource), typeof(PdfViewer));

    /// <summary>The displayed page, first page 1. Two-way by default.</summary>
    public static readonly BindableProperty PageNumberProperty = BindableProperty.Create(
        nameof(PageNumber), typeof(int), typeof(PdfViewer), 1, BindingMode.TwoWay,
        propertyChanged: (b, _, n) => ((PdfViewer)b).OnPageNumberChanged((int)n));

    private static readonly BindablePropertyKey PageCountKey = BindableProperty.CreateReadOnly(
        nameof(PageCount), typeof(int), typeof(PdfViewer), 0);

    /// <summary>Number of pages in the open document, 0 when none is open.</summary>
    public static readonly BindableProperty PageCountProperty = PageCountKey.BindableProperty;

    /// <summary>How the page is sized: fit page (default), fit width, or the zoom the user chose.</summary>
    public static readonly BindableProperty ZoomModeProperty = BindableProperty.Create(
        nameof(ZoomMode), typeof(ZoomMode), typeof(PdfViewer), ZoomMode.FitPage, BindingMode.TwoWay,
        propertyChanged: (b, _, n) => { if (((PdfViewer)b).viewModel is { } vm) vm.ZoomMode = (ZoomMode)n; });

    private static readonly BindablePropertyKey IsLoadingKey = BindableProperty.CreateReadOnly(
        nameof(IsLoading), typeof(bool), typeof(PdfViewer), false);

    /// <summary>True while a document is being opened.</summary>
    public static readonly BindableProperty IsLoadingProperty = IsLoadingKey.BindableProperty;

    private static readonly BindablePropertyKey ErrorMessageKey = BindableProperty.CreateReadOnly(
        nameof(ErrorMessage), typeof(string), typeof(PdfViewer), null,
        propertyChanged: (b, _, n) => ((PdfViewer)b).ShowError((string?)n));

    /// <summary>Why the document or the current page could not be shown, or null.</summary>
    public static readonly BindableProperty ErrorMessageProperty = ErrorMessageKey.BindableProperty;

    /// <summary>Whether the navigation and zoom toolbar is shown.</summary>
    public static readonly BindableProperty ShowToolbarProperty = BindableProperty.Create(
        nameof(ShowToolbar), typeof(bool), typeof(PdfViewer), true);

    /// <summary>
    /// The page, zoom and scroll position, two-way. The viewer updates it as the user moves around, so a host can
    /// save it (for example when the window is stopped). Setting it moves the viewer there; set it before
    /// <see cref="Source"/> to open the next document at a saved position. Null when no document is open.
    /// </summary>
    public static readonly BindableProperty ViewStateProperty = BindableProperty.Create(
        nameof(ViewState), typeof(PdfViewState?), typeof(PdfViewer), null, BindingMode.TwoWay,
        propertyChanged: (b, _, n) => ((PdfViewer)b).OnViewStateSet((PdfViewState?)n));

    /// <inheritdoc cref="SourceProperty"/>
    public PdfSource? Source { get => (PdfSource?)GetValue(SourceProperty); set => SetValue(SourceProperty, value); }

    /// <inheritdoc cref="PasswordSourceProperty"/>
    public IPasswordSource? PasswordSource
    {
        get => (IPasswordSource?)GetValue(PasswordSourceProperty);
        set => SetValue(PasswordSourceProperty, value);
    }

    /// <inheritdoc cref="PageNumberProperty"/>
    public int PageNumber { get => (int)GetValue(PageNumberProperty); set => SetValue(PageNumberProperty, value); }

    /// <inheritdoc cref="PageCountProperty"/>
    public int PageCount { get => (int)GetValue(PageCountProperty); private set => SetValue(PageCountKey, value); }

    /// <inheritdoc cref="ZoomModeProperty"/>
    public ZoomMode ZoomMode { get => (ZoomMode)GetValue(ZoomModeProperty); set => SetValue(ZoomModeProperty, value); }

    /// <inheritdoc cref="IsLoadingProperty"/>
    public bool IsLoading { get => (bool)GetValue(IsLoadingProperty); private set => SetValue(IsLoadingKey, value); }

    /// <inheritdoc cref="ErrorMessageProperty"/>
    public string? ErrorMessage { get => (string?)GetValue(ErrorMessageProperty); private set => SetValue(ErrorMessageKey, value); }

    /// <inheritdoc cref="ShowToolbarProperty"/>
    public bool ShowToolbar { get => (bool)GetValue(ShowToolbarProperty); set => SetValue(ShowToolbarProperty, value); }

    /// <inheritdoc cref="ViewStateProperty"/>
    public PdfViewState? ViewState { get => (PdfViewState?)GetValue(ViewStateProperty); set => SetValue(ViewStateProperty, value); }

    /// <summary>Go to the first page. Like the other commands, for menus and keyboard shortcuts.</summary>
    public ICommand FirstPageCommand { get; }

    /// <summary>Go to the previous page.</summary>
    public ICommand PreviousPageCommand { get; }

    /// <summary>Go to the next page.</summary>
    public ICommand NextPageCommand { get; }

    /// <summary>Go to the last page.</summary>
    public ICommand LastPageCommand { get; }

    /// <summary>Zoom in one step around the centre.</summary>
    public ICommand ZoomInCommand { get; }

    /// <summary>Zoom out one step around the centre.</summary>
    public ICommand ZoomOutCommand { get; }

    /// <summary>Show the whole page.</summary>
    public ICommand FitPageCommand { get; }

    /// <summary>Fit the page to the width.</summary>
    public ICommand FitWidthCommand { get; }

    /// <summary>Raised on the UI thread when a document has opened and its first page is shown.</summary>
    public event EventHandler? DocumentLoaded;

    /// <summary>Raised on the UI thread when a document could not be opened.</summary>
    public event EventHandler<Exception>? LoadFailed;

    // ---- Implementation ---------------------------------------------------------------------------------------

    private const uint BitmapDelayMs = 150;

    private readonly GraphicsView pageView;
    private readonly PdfPageDrawable drawable = new();
    private readonly Grid body;
    private readonly Label error;
    private ViewModel? viewModel;
    private CancellationTokenSource? loading;
    private int bitmapVersion;
    private Window? window;
    private readonly List<ForwardingCommand> commands = [];
    private bool updatingViewState;     // the viewer, not the host, is setting ViewState
    private PdfViewState? requestedViewState; // set by the host; applied when the next document opens

    /// <summary>Create a viewer with no document.</summary>
    public PdfViewer()
    {
        FirstPageCommand = Forward(vm => vm.PageSelector.ToStartCommand);
        PreviousPageCommand = Forward(vm => vm.PageSelector.DecrementCommand);
        NextPageCommand = Forward(vm => vm.PageSelector.IncrementCommand);
        LastPageCommand = Forward(vm => vm.PageSelector.ToEndCommand);
        ZoomInCommand = Forward(vm => vm.ZoomInCommand);
        ZoomOutCommand = Forward(vm => vm.ZoomOutCommand);
        FitPageCommand = Forward(vm => vm.FitPageCommand);
        FitWidthCommand = Forward(vm => vm.FitWidthCommand);

        pageView = new GraphicsView { Drawable = drawable, AutomationId = "PdfViewer.Page" };
        SemanticProperties.SetDescription(pageView, "No document");
        SemanticProperties.SetHint(pageView, "Pinch or use the zoom buttons to zoom, drag to scroll");
        pageView.SizeChanged += (_, _) => viewModel?.SetViewport(pageView.Width, pageView.Height);
        AddGestures(pageView);
        pageView.HandlerChanged += (_, _) => AttachPlatformGestures(pageView);

        var spinner = new ActivityIndicator { HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center };
        spinner.SetBinding(ActivityIndicator.IsRunningProperty, static (PdfViewer v) => v.IsLoading, source: this);
        spinner.SetBinding(IsVisibleProperty, static (PdfViewer v) => v.IsLoading, source: this);

        error = new Label
        {
            IsVisible = false,
            HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center,
            HorizontalTextAlignment = TextAlignment.Center, Margin = 24,
        };
        // Dark red is unreadable on the dark-theme background.
        error.SetAppThemeColor(Label.TextColorProperty, Colors.DarkRed, Color.FromArgb("#FF8A80"));

        var content = new Grid { Children = { pageView, spinner, error, CreateLayersPanel() } };
        var toolbar = CreateToolbar();
        toolbar.SetBinding(IsVisibleProperty, static (PdfViewer v) => v.ShowToolbar, source: this);
        body = new Grid { RowDefinitions = [new(GridLength.Auto), new(GridLength.Star)] };
        body.Add(toolbar, 0, 0);
        body.Add(content, 0, 1);
        Content = body;

        drawable.Background = BackgroundFor(Application.Current?.RequestedTheme ?? AppTheme.Light);
        if (Application.Current is { } app)
            app.RequestedThemeChanged += (_, e) => { drawable.Background = BackgroundFor(e.RequestedTheme); pageView.Invalidate(); };

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void ShowError(string? message)
    {
        error.Text = message;
        error.IsVisible = message is not null;
        if (message is null) return;
        SemanticScreenReader.Default.Announce(message);
        error.SetSemanticFocus();
    }

    private static Color BackgroundFor(AppTheme theme) =>
        theme == AppTheme.Dark ? Color.FromArgb("#1E1E1E") : Color.FromArgb("#D9D9D9");

    private async void OnSourceChanged()
    {
        var cancellation = new CancellationTokenSource();
        Interlocked.Exchange(ref loading, cancellation)?.Cancel();
        await CloseDocumentAsync();
        if (Source is not { } source) return;

        IsLoading = true;
        ErrorMessage = null;
        SemanticScreenReader.Default.Announce("Opening document");
        try
        {
            var fonts = Handler?.MauiContext?.Services.GetService<IDefaultFontMapper>() ?? SelfContainedDefaultFonts.Instance;
            var passwords = PasswordSource;
            var opened = await Task.Run(async () =>
            {
                byte[] bytes;
                await using (var stream = await source.OpenAsync(cancellation.Token))
                {
                    using var copy = new MemoryStream();
                    await stream.CopyToAsync(copy, cancellation.Token);
                    bytes = copy.ToArray();
                }
                var document = await new PdfReader(passwords, fonts).ReadFromAsync(bytes);
                return await ViewModel.CreateAsync(document, new MauiGraphicsPageRenderer());
            }, cancellation.Token);

            if (cancellation.IsCancellationRequested)
            {
                await opened.DisposeAsync();
                return;
            }
            ShowDocument(opened);
            SemanticScreenReader.Default.Announce(
                opened.PageCount == 1 ? "Document opened, 1 page" : $"Document opened, {opened.PageCount} pages");
            DocumentLoaded?.Invoke(this, EventArgs.Empty);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }
        catch (Exception e)
        {
            requestedViewState = null; // it was meant for this document
            ErrorMessage = $"This document could not be opened: {e.Message}";
            LoadFailed?.Invoke(this, e);
        }
        finally
        {
            if (ReferenceEquals(loading, cancellation)) IsLoading = false;
        }
    }

    private void ShowDocument(ViewModel opened)
    {
        viewModel = opened;
        body.BindingContext = opened;
        // A custom zoom belongs to the document it was chosen for; a new document starts fitted, unless the host
        // asked for a saved position.
        if (ZoomMode == ZoomMode.Custom) ZoomMode = ZoomMode.FitPage;
        opened.ZoomMode = ZoomMode;
        opened.PropertyChanged += OnViewModelChanged;
        opened.PageSelector.PropertyChanged += OnPageSelectorChanged;
        PageCount = opened.PageCount;
        opened.SetViewport(pageView.Width, pageView.Height);
        if (requestedViewState is { } requested)
        {
            requestedViewState = null;
            opened.Restore(requested);
            ZoomMode = opened.ZoomMode;
        }
        PageNumber = opened.PageSelector.Page;
        PublishViewState();
        foreach (var command in commands) command.Retarget();
        UpdatePage();
    }

    private async Task CloseDocumentAsync()
    {
        if (viewModel is not { } old) return;
        viewModel = null;
        old.PropertyChanged -= OnViewModelChanged;
        old.PageSelector.PropertyChanged -= OnPageSelectorChanged;
        body.BindingContext = null;
        PageCount = 0;
        foreach (var command in commands) command.Retarget();
        updatingViewState = true;
        ViewState = null;
        updatingViewState = false;
        ReplaceBitmap(null);
        drawable.Page = null;
        pageView.Invalidate();
        await old.DisposeAsync();
    }

    // View-model changes can arrive on a worker thread (rendering); the drawable is only touched on the UI thread.
    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(ViewModel.CurrentPage) or nameof(ViewModel.Layout):
                Dispatcher.Dispatch(UpdatePage);
                break;
            case nameof(ViewModel.RenderError):
                Dispatcher.Dispatch(() => ErrorMessage = viewModel?.RenderError);
                break;
            case nameof(ViewModel.ZoomMode):
                Dispatcher.Dispatch(() => { if (viewModel is { } vm) ZoomMode = vm.ZoomMode; });
                break;
            case nameof(ViewModel.ViewState):
                Dispatcher.Dispatch(PublishViewState);
                break;
        }
    }

    private void OnPageSelectorChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(PageSelectorViewModel.Page)) return;
        Dispatcher.Dispatch(() =>
        {
            if (viewModel is not { } vm) return;
            PageNumber = vm.PageSelector.Page;
            var description = $"Page {vm.PageSelector.Page} of {vm.PageCount}";
            SemanticProperties.SetDescription(pageView, description);
            SemanticScreenReader.Default.Announce(description);
        });
    }

    private void PublishViewState()
    {
        if (viewModel is not { } vm) return;
        updatingViewState = true;
        try { ViewState = vm.ViewState; }
        finally { updatingViewState = false; }
    }

    private void OnViewStateSet(PdfViewState? state)
    {
        if (updatingViewState || state is not { } requested) return;
        if (viewModel is { } vm && !IsLoading)
        {
            vm.Restore(requested);
            ZoomMode = vm.ZoomMode;
        }
        else
        {
            requestedViewState = requested; // applied by ShowDocument
        }
    }

    private ForwardingCommand Forward(Func<ViewModel, ICommand> target)
    {
        var command = new ForwardingCommand(() => viewModel is { } vm ? target(vm) : null, Dispatcher);
        commands.Add(command);
        return command;
    }

    /// <summary>
    /// A stable command (for the toolbar and the host) that runs the open document's command, if any. It raises
    /// CanExecuteChanged on the UI thread: the view model raises it from whatever thread changed a property, often the
    /// render thread, and a Button that updates IsEnabled there throws on Android ("Only the original thread that
    /// created a view hierarchy can touch its views").
    /// </summary>
    private sealed class ForwardingCommand(Func<ICommand?> target, IDispatcher dispatcher) : ICommand
    {
        private ICommand? current;

        public event EventHandler? CanExecuteChanged;

        public bool CanExecute(object? parameter) => current?.CanExecute(parameter) ?? false;

        public void Execute(object? parameter)
        {
            if (CanExecute(parameter)) current!.Execute(parameter);
        }

        public void Retarget()
        {
            if (current is not null) current.CanExecuteChanged -= OnTargetChanged;
            current = target();
            if (current is not null) current.CanExecuteChanged += OnTargetChanged;
            Raise();
        }

        private void OnTargetChanged(object? sender, EventArgs e)
        {
            if (dispatcher.IsDispatchRequired) dispatcher.Dispatch(Raise);
            else Raise();
        }

        private void Raise() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnPageNumberChanged(int page)
    {
        if (viewModel is { } vm && vm.PageSelector.Page != page) vm.PageSelector.Page = page;
    }

    private void UpdatePage()
    {
        if (viewModel is not { } vm) return;
        var page = vm.CurrentPage?.Content;
        if (!ReferenceEquals(drawable.Page, page)) ReplaceBitmap(null);
        drawable.Page = page;
        drawable.Layout = vm.Layout;
        SemanticProperties.SetDescription(pageView, $"Page {vm.PageSelector.Page} of {vm.PageCount}");
        pageView.Invalidate();
        ScheduleBitmap();
    }

    // ---- Bitmap cache -----------------------------------------------------------------------------------------

    private async void ScheduleBitmap()
    {
        var version = ++bitmapVersion;
        await Task.Delay(TimeSpan.FromMilliseconds(BitmapDelayMs));
        if (version != bitmapVersion || window is null || drawable.Page is not { } page) return;
        var scale = drawable.Layout.Scale;
        if (drawable.Bitmap is { } current && ReferenceEquals(current.Page, page) && current.Scale == scale) return;

        var density = DeviceDisplay.Current.MainDisplayInfo.Density;
        PageBitmap? bitmap = null;
        try
        {
            bitmap = await Task.Run(() => PageBitmap.Create(page, scale, density > 0 ? density : 1));
        }
        catch (Exception)
        {
            // Out of memory or a platform failure: keep drawing from the display list.
        }
        if (version != bitmapVersion || !ReferenceEquals(drawable.Page, page))
        {
            bitmap?.Dispose();
            return;
        }
        ReplaceBitmap(bitmap);
        pageView.Invalidate();
    }

    private void ReplaceBitmap(PageBitmap? bitmap)
    {
        var old = drawable.Bitmap;
        drawable.Bitmap = bitmap;
        old?.Dispose(); // safe: drawing also happens on this (UI) thread
    }

    // Free the bitmap while the app is in the background; it is rebuilt when the window is resumed.
    private void OnLoaded(object? sender, EventArgs e)
    {
        window = Window;
        if (window is null) return;
        window.Stopped += OnWindowStopped;
        window.Resumed += OnWindowResumed;
        window.Destroying += OnWindowDestroying;
        ScheduleBitmap();
    }

    private void OnUnloaded(object? sender, EventArgs e)
    {
        if (window is not null)
        {
            window.Stopped -= OnWindowStopped;
            window.Resumed -= OnWindowResumed;
            window.Destroying -= OnWindowDestroying;
        }
        window = null;
        ReplaceBitmap(null);
    }

    private void OnWindowStopped(object? sender, EventArgs e) => ReplaceBitmap(null);

    private void OnWindowResumed(object? sender, EventArgs e) => ScheduleBitmap();

    // The window is going away (closed on desktop, or the Android activity finishing): release the document now
    // rather than when the control is collected. Hosts save ViewState before this, in their own Destroying handler.
    private async void OnWindowDestroying(object? sender, EventArgs e)
    {
        Interlocked.Exchange(ref loading, null)?.Cancel();
        await CloseDocumentAsync();
    }

    // ---- Gestures ---------------------------------------------------------------------------------------------

    private double panX, panY;

    private void AddGestures(GraphicsView view)
    {
        var pinch = new PinchGestureRecognizer();
        pinch.PinchUpdated += (_, e) =>
        {
            // Scale is the change since the previous update; ScaleOrigin is relative to the view's size.
            if (e.Status == GestureStatus.Running)
                viewModel?.ZoomAt(e.Scale, e.ScaleOrigin.X * view.Width, e.ScaleOrigin.Y * view.Height);
        };

        var pan = new PanGestureRecognizer();
        pan.PanUpdated += (_, e) =>
        {
            switch (e.StatusType)
            {
                case GestureStatus.Started:
                    panX = panY = 0;
                    break;
                case GestureStatus.Running:
                    viewModel?.PanBy(e.TotalX - panX, e.TotalY - panY);
                    (panX, panY) = (e.TotalX, e.TotalY);
                    break;
            }
        };

        var doubleTap = new TapGestureRecognizer { NumberOfTapsRequired = 2 };
        doubleTap.Tapped += (_, e) =>
        {
            if (viewModel is not { } vm) return;
            if (vm.ZoomMode != ZoomMode.FitPage)
                vm.ZoomMode = ZoomMode.FitPage;
            else if (e.GetPosition(view) is { } point)
                vm.ZoomAt(2, point.X, point.Y);
        };

        view.GestureRecognizers.Add(pinch);
        view.GestureRecognizers.Add(pan);
        view.GestureRecognizers.Add(doubleTap);
    }

    /// <summary>Platform-specific input such as trackpad and mouse-wheel scrolling.</summary>
    partial void AttachPlatformGestures(GraphicsView view);

    /// <summary>Scroll the page by a wheel or trackpad delta, in view units (used by platform input).</summary>
    internal void ScrollBy(double dx, double dy) => viewModel?.PanBy(dx, dy);

    // ---- Toolbar and layers -----------------------------------------------------------------------------------

    private View CreateToolbar()
    {
        static Button MakeButton(string text, string description)
        {
            var button = new Button
            {
                // 44 x 44: the smallest comfortable touch target (Apple HIG; Android asks for 48dp).
                Text = text, Padding = new Thickness(10, 4), MinimumWidthRequest = 44, MinimumHeightRequest = 44,
                AutomationId = "PdfViewer." + description.Replace(" ", ""),
            };
            SemanticProperties.SetDescription(button, description);
            return button;
        }

        var first = MakeButton("|<", "First page");
        first.Command = FirstPageCommand;
        var previous = MakeButton("<", "Previous page");
        previous.Command = PreviousPageCommand;
        var next = MakeButton(">", "Next page");
        next.Command = NextPageCommand;
        var last = MakeButton(">|", "Last page");
        last.Command = LastPageCommand;

        var page = new Entry
        {
            // Minimum, not fixed, width so large accessibility text sizes still fit.
            Keyboard = Keyboard.Numeric, MinimumWidthRequest = 56, MinimumHeightRequest = 44,
            HorizontalTextAlignment = TextAlignment.Center, AutomationId = "PdfViewer.PageNumber",
        };
        // A hint rather than a description or placeholder: Description breaks TalkBack's edit action on Android,
        // and Placeholder would compete with Hint for the same Android attribute.
        SemanticProperties.SetHint(page, "Page number. Type a page to go to it");
        page.SetBinding(Entry.TextProperty, static (ViewModel vm) => vm.PageSelector.Page, BindingMode.TwoWay,
            converter: IntTextConverter.Instance);
        var count = new Label { VerticalOptions = LayoutOptions.Center };
        count.SetBinding(Label.TextProperty, static (ViewModel vm) => vm.PageCount, stringFormat: "of {0}");

        var zoomOut = MakeButton("−", "Zoom out");
        zoomOut.Command = ZoomOutCommand;
        var zoomIn = MakeButton("+", "Zoom in");
        zoomIn.Command = ZoomInCommand;
        var fitPage = MakeButton("Fit", "Fit page");
        fitPage.Command = FitPageCommand;
        var fitWidth = MakeButton("Width", "Fit width");
        fitWidth.Command = FitWidthCommand;

        var layers = MakeButton("Layers", "Show layers");
        layers.SetBinding(IsVisibleProperty, static (ViewModel vm) => vm.Layers, converter: NotEmptyConverter.Instance);
        layers.Clicked += (_, _) =>
        {
            layersPanel.IsVisible = !layersPanel.IsVisible;
            SemanticProperties.SetDescription(layers, layersPanel.IsVisible ? "Hide layers" : "Show layers");
            SemanticScreenReader.Default.Announce(layersPanel.IsVisible ? "Layers shown" : "Layers hidden");
        };

        return new FlexLayout
        {
            Wrap = Microsoft.Maui.Layouts.FlexWrap.Wrap,
            JustifyContent = Microsoft.Maui.Layouts.FlexJustify.Center,
            AlignItems = Microsoft.Maui.Layouts.FlexAlignItems.Center,
            Padding = new Thickness(4),
            Children = { first, previous, page, count, next, last, zoomOut, zoomIn, fitPage, fitWidth, layers },
        };
    }

    private Border layersPanel = null!;

    private View CreateLayersPanel()
    {
        var list = new CollectionView
        {
            // Not MeasureFirstItem: that gives every row the first row's width and truncates longer layer names.
            ItemsLayout = new LinearItemsLayout(ItemsLayoutOrientation.Vertical),
            ItemTemplate = new DataTemplate(() =>
            {
                var check = new CheckBox { VerticalOptions = LayoutOptions.Center };
                check.SetBinding(CheckBox.IsCheckedProperty, static (LayerRow row) => row.Visible, BindingMode.TwoWay);
                check.SetBinding(IsVisibleProperty, static (LayerRow row) => row.ShowCheck);
                // The check box carries the layer name for screen readers; the label beside it is visual only, and
                // tapping it toggles the layer too (a bigger target than the box).
                check.SetBinding(SemanticProperties.DescriptionProperty, static (LayerRow row) => row.Name);
                var name = new Label { VerticalOptions = LayoutOptions.Center, MinimumHeightRequest = 44, VerticalTextAlignment = TextAlignment.Center };
                name.SetBinding(Label.TextProperty, static (LayerRow row) => row.Name);
                AutomationProperties.SetIsInAccessibleTree(name, false);
                var toggle = new TapGestureRecognizer();
                toggle.Tapped += (_, _) => { if (name.BindingContext is LayerRow { ShowCheck: true } row) row.Visible = !row.Visible; };
                name.GestureRecognizers.Add(toggle);
                var row = new HorizontalStackLayout { Spacing = 4, Children = { check, name } };
                row.SetBinding(PaddingProperty, static (LayerRow r) => r.Depth, converter: DepthToIndentConverter.Instance);
                return row;
            }),
        };
        list.SetBinding(ItemsView.ItemsSourceProperty, static (ViewModel vm) => vm.Layers);

        layersPanel = new Border
        {
            Content = list, IsVisible = false, WidthRequest = 240, Padding = 8,
            HorizontalOptions = LayoutOptions.End, VerticalOptions = LayoutOptions.Fill,
        };
        layersPanel.SetAppThemeColor(BackgroundColorProperty, Colors.White, Color.FromArgb("#2D2D2D"));
        return layersPanel;
    }

    private sealed class NotEmptyConverter : IValueConverter
    {
        public static readonly NotEmptyConverter Instance = new();
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
            value is System.Collections.ICollection { Count: > 0 };
        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
    }

    private sealed class DepthToIndentConverter : IValueConverter
    {
        public static readonly DepthToIndentConverter Instance = new();
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
            new Thickness((value is int depth ? depth : 0) * 16, 0, 0, 0);
        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
    }

    private sealed class IntTextConverter : IValueConverter
    {
        public static readonly IntTextConverter Instance = new();
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
            value is int i ? i.ToString(CultureInfo.CurrentCulture) : "";
        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
            int.TryParse(value as string, NumberStyles.Integer, CultureInfo.CurrentCulture, out var i) ? i : Binding.DoNothing;
    }
}
