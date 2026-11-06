using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows.Input;
using Melville.Pdf.FontLibrary;
using Melville.Pdf.LowLevel.Parsing.ParserContext;
using Melville.Pdf.Maui.Rendering;
using Melville.Pdf.Maui.Sample.Documents;
using Melville.Pdf.MauiGraphics;
using Melville.Pdf.MauiGraphics.Drawing;
using Melville.Pdf.Model;
using Melville.Pdf.Model.Renderers.DocumentRenderers;
using Melville.Pdf.SkiaSharp;
using Microsoft.Maui.Graphics.Platform;
using SkiaSharp;

namespace Melville.Pdf.Maui.Sample.Diagnostics;

/// <summary>One measurement or result line.</summary>
public sealed record Measurement(string Name, string Value);

/// <summary>
/// Times one page through the pipeline (open, record, replay on this platform's canvas, and the SkiaSharp renderer
/// for comparison), shows both renders, and runs a smoke test that records every page of every document.
/// Results are also written to the console as [DIAG] and [SMOKE] lines.
/// </summary>
public sealed class DiagnosticsViewModel : ObservableObject
{
    private const int RenderWidth = 800;
    private const int Replays = 5;

    private readonly DocumentCatalog catalog;
    private IReadOnlyList<CatalogDocument> documents;
    private CatalogDocument? selected;
    private string pageText = "1";
    private bool isRunning;
    private ImageSource? nativeImage, skiaImage;
    private string status = "Choose a document and measure a page.";

    public DiagnosticsViewModel(DocumentCatalog catalog)
    {
        this.catalog = catalog;
        documents = catalog.References;
        selected = documents.FirstOrDefault();
        MeasureCommand = new Command(async () => await RunAsync(MeasureAsync), () => !IsRunning);
        SmokeTestCommand = new Command(async () => await RunAsync(SmokeTestAsync), () => !IsRunning);
    }

    public IReadOnlyList<CatalogDocument> Documents { get => documents; private set => Set(ref documents, value); }

    public CatalogDocument? Selected { get => selected; set => Set(ref selected, value); }

    /// <summary>Page to measure, first page 1.</summary>
    public string PageText { get => pageText; set => Set(ref pageText, value); }

    public bool IsRunning
    {
        get => isRunning;
        private set
        {
            if (!Set(ref isRunning, value)) return;
            ((Command)MeasureCommand).ChangeCanExecute();
            ((Command)SmokeTestCommand).ChangeCanExecute();
        }
    }

    /// <summary>What the page is doing; announced to screen readers when it changes.</summary>
    public string Status
    {
        get => status;
        private set
        {
            if (Set(ref status, value)) SemanticScreenReader.Default.Announce(value);
        }
    }

    // Progress is shown but not announced: one announcement per document would drown the screen reader.
    private void SetProgress(string text)
    {
        status = text;
        OnPropertyChanged(nameof(Status));
    }

    public ObservableCollection<Measurement> Results { get; } = [];

    /// <summary>The Maui.Graphics renderer on this platform's native canvas.</summary>
    public ImageSource? NativeImage { get => nativeImage; private set => Set(ref nativeImage, value); }

    /// <summary>The existing SkiaSharp renderer, for comparison.</summary>
    public ImageSource? SkiaImage { get => skiaImage; private set => Set(ref skiaImage, value); }

    public ICommand MeasureCommand { get; }

    public ICommand SmokeTestCommand { get; }

    /// <summary>Add the bundled samples ahead of the reference documents.</summary>
    public async Task LoadAsync()
    {
        if (Documents.Count != catalog.References.Count) return;
        try
        {
            var samples = await catalog.GetSamplesAsync();
            Documents = [.. samples, .. catalog.References];
            Selected = Documents[0];
        }
        catch (Exception e)
        {
            Status = $"The bundled samples could not be read: {e.Message}";
        }
    }

    private async Task RunAsync(Func<Task> work)
    {
        IsRunning = true;
        Results.Clear();
        try
        {
            await work();
        }
        catch (Exception e)
        {
            Status = $"Failed: {e.GetType().Name}: {e.Message}";
            Console.WriteLine($"[DIAG] failed {e}");
        }
        finally
        {
            IsRunning = false;
        }
    }

    private void Add(string name, string value)
    {
        Results.Add(new Measurement(name, value));
        Console.WriteLine($"[DIAG] {name}: {value}");
    }

    private async Task MeasureAsync()
    {
        if (Selected is not { } document) return;
        Status = $"Measuring {document.Title}…";
        NativeImage = SkiaImage = null;

        var watch = Stopwatch.StartNew();
        var bytes = await Task.Run(() => ReadAllAsync(document));
        using var renderer = await Task.Run(() => OpenAsync(bytes, document.Password));
        Add("Open", $"{watch.Elapsed.TotalMilliseconds:F0} ms ({bytes.Length / 1024.0:F0} KB, {renderer.TotalPages} pages)");

        var page = int.TryParse(PageText, out var p) ? Math.Clamp(p, 1, renderer.TotalPages) : 1;
        PageText = page.ToString();

        watch.Restart();
        var list = await Task.Run(() => RenderWithMauiGraphics.RecordPageAsync(renderer, page).AsTask());
        Add($"Record page {page}", $"{watch.Elapsed.TotalMilliseconds:F1} ms ({list.Count} operations, {list.FillCount} fills, {list.ImageCount} images)");

        // Replay on the UI thread, where the viewer replays.
        var height = (int)(list.PageHeight * (RenderWidth / list.PageWidth));
        var (first, average, png) = ReplayNative(list, RenderWidth, height);
        Add($"Replay {RenderWidth}×{height} px", $"first {first:F1} ms, then {average:F1} ms average of {Replays - 1}");
        NativeImage = ImageSource.FromStream(() => new MemoryStream(png));

        watch.Restart();
        var skiaPng = await Task.Run(async () =>
        {
            using var surface = await RenderWithSkia.ToSurfaceAsync(renderer, page, RenderWidth, height);
            var elapsed = watch.Elapsed;
            using var image = surface.Snapshot();
            using var data = image.Encode(SKEncodedImageFormat.Png, 100);
            return (Elapsed: elapsed, Png: data.ToArray());
        });
        Add("SkiaSharp renderer (for comparison)", $"{skiaPng.Elapsed.TotalMilliseconds:F1} ms");
        SkiaImage = ImageSource.FromStream(() => new MemoryStream(skiaPng.Png));

        Add("Managed heap", $"{GC.GetTotalMemory(false) / 1048576.0:F1} MB");
        Add("Process working set", $"{Environment.WorkingSet / 1048576.0:F1} MB");
        Status = $"{document.Title}, page {page} of {renderer.TotalPages}";
    }

    private static (double First, double Average, byte[] Png) ReplayNative(PdfDisplayList list, int width, int height)
    {
        using var context = new PlatformBitmapExportService().CreateContext(width, height, 1);
        var scale = width / list.PageWidth;
        var times = new double[Replays];
        for (int i = 0; i < Replays; i++)
        {
            var watch = Stopwatch.StartNew();
            context.Canvas.FillColor = Colors.White;
            context.Canvas.FillRectangle(0, 0, width, height);
            PdfPageDrawing.Draw(list, context.Canvas, scale);
            times[i] = watch.Elapsed.TotalMilliseconds;
        }
        using var png = new MemoryStream();
        context.Image.Save(png, ImageFormat.Png);
        return (times[0], times.Skip(1).Average(), png.ToArray());
    }

    private async Task SmokeTestAsync()
    {
        var all = Documents;
        int pages = 0, failures = 0;
        var watch = Stopwatch.StartNew();
        for (int i = 0; i < all.Count; i++)
        {
            var document = all[i];
            SetProgress($"Smoke test {i + 1} of {all.Count}: {document.Title}");
            var result = await Task.Run(() => RecordEveryPageAsync(document));
            pages += result.Pages;
            if (result.Error is null) continue;
            failures++;
            Add(document.Title, result.Error);
            Console.WriteLine($"[SMOKE] FAIL {document.Title}: {result.Error}");
        }
        var summary = $"{all.Count} documents, {pages} pages, {failures} failures, {watch.Elapsed.TotalSeconds:F0} s";
        Results.Insert(0, new Measurement("Smoke test", summary));
        Console.WriteLine($"[SMOKE] {summary}");
        Status = failures == 0 ? "Smoke test passed" : "Smoke test found failures";
    }

    private static async Task<(int Pages, string? Error)> RecordEveryPageAsync(CatalogDocument document)
    {
        int pages = 0;
        try
        {
            using var renderer = await OpenAsync(await ReadAllAsync(document), document.Password);
            for (int page = 1; page <= renderer.TotalPages; page++, pages++)
                await RenderWithMauiGraphics.RecordPageAsync(renderer, page);
            return (pages, null);
        }
        catch (Exception e)
        {
            return (pages, $"page {pages + 1}: {e.GetType().Name}: {e.Message.ReplaceLineEndings(" ")}");
        }
    }

    private static async Task<byte[]> ReadAllAsync(CatalogDocument document)
    {
        await using var stream = await document.Source.OpenAsync(CancellationToken.None);
        using var copy = new MemoryStream();
        await stream.CopyToAsync(copy);
        return copy.ToArray();
    }

    private static async Task<DocumentRenderer> OpenAsync(byte[] bytes, string? password) =>
        await new PdfReader(new ConstantPasswordSource(PasswordType.User, password ?? ""), SelfContainedDefaultFonts.Instance)
            .ReadFromAsync(bytes);
}
