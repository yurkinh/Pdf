using System.Collections.Concurrent;
using System.ComponentModel;
using Melville.Pdf.FontLibrary;
using Melville.Pdf.LowLevel.Model.Conventions;
using Melville.Pdf.LowLevel.Model.Objects;
using Melville.Pdf.LowLevel.Writers;
using Melville.Pdf.LowLevel.Writers.Builder;
using Melville.Pdf.LowLevel.Writers.DocumentWriters;
using Melville.Pdf.Model;
using Melville.Pdf.Model.Creators;
using Melville.Pdf.Model.Renderers.DocumentRenderers;
using Melville.Pdf.ReferenceDocuments.Infrastructure;

namespace Melville.Pdf.ViewerModel.Tests;

/// <summary>A rendered page in tests: remembers its number and whether it was disposed.</summary>
public sealed class FakePage(int number) : IDisposable
{
    public int Number { get; } = number;
    public bool Disposed { get; private set; }
    public void Dispose() => Disposed = true;
}

/// <summary>
/// Page renderer whose renders can be held open per page, to reproduce overlapping requests deterministically.
/// </summary>
public sealed class FakePageRenderer : IPageRenderer<FakePage>
{
    public const double Width = 612;
    public const double Height = 792;

    private readonly ConcurrentDictionary<int, TaskCompletionSource> holds = new();
    private int active;

    public ConcurrentQueue<int> Requested { get; } = new();
    public ConcurrentQueue<FakePage> Produced { get; } = new();
    public int MaxConcurrent { get; private set; }
    public Exception? FailWith { get; set; }

    /// <summary>Renders of this page wait until <see cref="Release"/> is called.</summary>
    public void Hold(int page) => holds[page] = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

    public void Release(int page)
    {
        if (holds.TryRemove(page, out var hold)) hold.SetResult();
    }

    public async ValueTask<RenderedPage<FakePage>> RenderAsync(
        DocumentRenderer document, int oneBasedPageNumber, CancellationToken cancellation)
    {
        Requested.Enqueue(oneBasedPageNumber);
        var now = Interlocked.Increment(ref active);
        MaxConcurrent = Math.Max(MaxConcurrent, now);
        try
        {
            if (holds.TryGetValue(oneBasedPageNumber, out var hold)) await hold.Task;
            await Task.Yield();
            if (FailWith is { } failure) throw failure;
            var page = new FakePage(oneBasedPageNumber);
            Produced.Enqueue(page);
            return new RenderedPage<FakePage>(page, Width, Height);
        }
        finally
        {
            Interlocked.Decrement(ref active);
        }
    }
}

public static class TestDocuments
{
    public static async Task<DocumentRenderer> MultiPageAsync(int pages)
    {
        var builder = new PdfDocumentCreator();
        for (int i = 0; i < pages; i++) builder.Pages.CreatePage();
        using var bytes = new MemoryStream();
        await builder.CreateDocument().WriteToAsync(bytes);
        return await new PdfReader(SelfContainedDefaultFonts.Instance).ReadFromAsync(bytes.ToArray());
    }

    /// <summary>Pages that each say "Page n" in Helvetica.</summary>
    public static async Task<DocumentRenderer> WithTextAsync(int pages)
    {
        var builder = new PdfDocumentCreator();
        builder.Pages.AddStandardFont("F1"u8, BuiltInFontName.Helvetica, FontEncodingName.WinAnsiEncoding);
        for (int i = 1; i <= pages; i++)
        {
            var number = i;
            await builder.Pages.CreatePage().AddToContentStreamAsync(async csw =>
            {
                using var block = csw.StartTextBlock();
                await csw.SetFontAsync(PdfDirectObject.CreateName("F1"), 12);
                block.MovePositionBy(72, 720);
                await block.ShowStringAsync($"Page {number}");
            });
        }
        using var bytes = new MemoryStream();
        await builder.CreateDocument().WriteToAsync(bytes);
        return await new PdfReader(SelfContainedDefaultFonts.Instance).ReadFromAsync(bytes.ToArray());
    }

    /// <summary>One page with two optional content groups listed in the default configuration's /Order.</summary>
    public static async Task<DocumentRenderer> WithLayersAsync()
    {
        var builder = new PdfDocumentCreator();
        builder.Pages.CreatePage();
        PdfIndirectObject Group(string name) => builder.LowLevelCreator.Add(new DictionaryBuilder()
            .WithItem(KnownNames.Type, KnownNames.OCG)
            .WithItem(KnownNames.Name, name)
            .AsDictionary());
        var first = Group("Background");
        var second = Group("Annotations");
        builder.AddToRootDictionary(KnownNames.OCProperties, new DictionaryBuilder()
            .WithItem(KnownNames.OCGs, new PdfArray(first, second))
            .WithItem(KnownNames.D, new DictionaryBuilder()
                .WithItem(KnownNames.ON, new PdfArray(first, second))
                .WithItem(KnownNames.Order, new PdfArray(first, second))
                .AsDictionary())
            .AsDictionary());
        using var bytes = new MemoryStream();
        await builder.CreateDocument().WriteToAsync(bytes);
        return await new PdfReader(SelfContainedDefaultFonts.Instance).ReadFromAsync(bytes.ToArray());
    }

    public static async Task<DocumentRenderer> FromGeneratorAsync(string prefix)
    {
        var generator = GeneratorFactory.AllGenerators.Single(g => g.Prefix == prefix);
        using var bytes = new MemoryStream();
        await generator.WritePdfAsync(bytes);
        return await new PdfReader(SelfContainedDefaultFonts.Instance).ReadFromAsync(bytes.ToArray());
    }
}

public static class NotifyExtensions
{
    /// <summary>Wait, without polling, until a condition on a notifying object holds; fail after a timeout.</summary>
    public static async Task WaitUntilAsync(this INotifyPropertyChanged source, Func<bool> condition, int timeoutMs = 5000)
    {
        var signal = new SemaphoreSlim(0);
        void Changed(object? s, PropertyChangedEventArgs e) => signal.Release();
        source.PropertyChanged += Changed;
        try
        {
            using var timeout = new CancellationTokenSource(timeoutMs);
            while (!condition()) await signal.WaitAsync(timeout.Token);
        }
        finally
        {
            source.PropertyChanged -= Changed;
        }
    }
}
