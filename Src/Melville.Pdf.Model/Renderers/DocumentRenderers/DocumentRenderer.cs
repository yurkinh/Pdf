using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading.Tasks;

using Melville.Pdf.LowLevel.Model.ContentStreams;
using Melville.Pdf.LowLevel.Model.Conventions;
using Melville.Pdf.LowLevel.Model.Objects;
using Melville.Pdf.LowLevel.Model.Wrappers;
using Melville.Pdf.Model.Documents;
using Melville.Pdf.Model.OptionalContent;
using Melville.Pdf.Model.Renderers.DocumentPartCaches;
using Melville.Pdf.Model.Renderers.FontRenderings.DefaultFonts;
using Melville.Pdf.Model.Renderers.GraphicsStates;
using Melville.Pdf.Model.Renderers.OptionalContents;
using Melville.Pdf.Model.Renderers.Patterns.TilePatterns;
using Melville.StreamInterfaces;

namespace Melville.Pdf.Model.Renderers.DocumentRenderers;

/// <summary>
/// This class represents the state of the renderer, which may be refused between pages. 
/// </summary>
public abstract class DocumentRenderer : IDisposable
{
    /// <summary>
    /// Total pages available to render
    /// </summary>
    public int TotalPages { get; }

    /// <summary>
    /// Maps font names to fonts.
    /// </summary>
    public IDefaultFontMapper FontMapper { get; }

    /// <summary>
    /// Cache for partially done work that may be valuable later on.  (Es
    /// </summary>
    internal IDocumentPartCache Cache { get; }

    /// <summary>
    /// Records the visibility of various optional content group.
    /// </summary>
    public IOptionalContentState OptionalContentState { get; }

    internal DocumentRenderer(int totalPages,
        IDefaultFontMapper fontMapper, IDocumentPartCache cache, IOptionalContentState optionalContentState)
    {
        this.FontMapper = fontMapper;
        this.Cache = cache;
        OptionalContentState = optionalContentState;
        TotalPages = totalPages;
    }

    /// <summary>
    /// Create a DocumentRenderer that will render a tile pattern from this document.
    /// </summary>
    /// <param name="request">The tile brush to render.</param>
    /// <param name="priorState">The graphics state at the time of the tile brush request.</param>
    /// <returns>The DocumentReader that can render the TilePatternRequest.</returns>
    public DocumentRenderer PatternRenderer(in TileBrushRequest request, GraphicsState priorState) =>
        new PatternRenderer(FontMapper, Cache, request, priorState, OptionalContentState);

    private Func<IContentStreamOperations, IContentStreamOperations> wrapOutputWith = DoNotWrapOutput;

    private static IContentStreamOperations DoNotWrapOutput(IContentStreamOperations i) => i;

    /// <summary>
    /// Adds an output wrapper to the rendering chain.  Output wrappers can inspect and modify
    /// </summary>
    /// <param name="wrapOutput">A function that can wrap, or replace the output of the renderer</param>
    /// <returns></returns>
    public DocumentRenderer WithOutputWrapper(
        Func<IContentStreamOperations, IContentStreamOperations> wrapOutput)
    {
        var capture = wrapOutputWith;
        wrapOutputWith = i => wrapOutput(capture(i));
        return this;
    }

    /// <summary>
    /// Render a given page to a target.
    /// </summary>
    /// <param name="oneBasedPageNumber">The page to render</param>
    /// <param name="target">A factory method that creates a render target given a visible rectangle and matrix.</param>
    public async ValueTask RenderPageToAsync(int oneBasedPageNumber, Func<PdfRect, Matrix3x2, IRenderTarget> target)
    {
        var pageStruct = await GetPageContentAsync(oneBasedPageNumber).CA();
        var cropRect = await GetCropDimensionsAsync(pageStruct).CA();
        var rotation = CreateRotateMatrix(cropRect, await pageStruct.GetDefaultRotationAsync().CA());

        using var renderTarget = target(cropRect.Transform(rotation), rotation);
        await CreateRenderEngine(pageStruct, renderTarget).RunContentStreamAsync(wrapOutputWith).CA();
    }

    /// <summary>
    /// The visible box of a page (its crop box after the page's default rotation), in PDF points: the same
    /// rectangle <see cref="RenderPageToAsync"/> passes to its target factory, without rendering the page.
    /// Viewers use it to lay out many pages before any of them is drawn.
    /// </summary>
    /// <param name="oneBasedPageNumber">The page to measure.</param>
    public async ValueTask<PdfRect> GetPageBoxAsync(int oneBasedPageNumber)
    {
        var (rotation, cropRect) = await VisibleBoxTransformAsync(
            await GetPageContentAsync(oneBasedPageNumber).CA()).CA();
        return cropRect.Transform(rotation);
    }

    /// <summary>
    /// The links on a page, with their areas in the coordinates of <see cref="GetPageBoxAsync"/>. Links that go
    /// to a page of this document, to a URI, or to the next, previous, first or last page are read; other kinds
    /// of link, and links whose destination cannot be found, are left out.
    /// </summary>
    /// <param name="oneBasedPageNumber">The page whose links to read.</param>
    public ValueTask<IReadOnlyList<PdfLink>> GetPageLinksAsync(int oneBasedPageNumber) =>
        new LinkReader(this).ReadAsync(oneBasedPageNumber);

    internal ValueTask<HasRenderableContentStream> PageAsync(int oneBasedPageNumber) =>
        GetPageContentAsync(oneBasedPageNumber);

    // The page's crop box, and the rotation that turns it into the box the page is drawn in.
    internal async ValueTask<(Matrix3x2 Rotation, PdfRect CropBox)> VisibleBoxTransformAsync(
        HasRenderableContentStream page)
    {
        var cropRect = await GetCropDimensionsAsync(page).CA();
        return (CreateRotateMatrix(cropRect, await page.GetDefaultRotationAsync().CA()), cropRect);
    }

    /// <summary>The number of a page given its dictionary, or 0 if it is not a page of this document.</summary>
    internal virtual ValueTask<int> PageNumberOfAsync(PdfDictionary page) => new(0);

    /// <summary>The destination a name (catalog /Dests) or string (the /Dests name tree) stands for, or null.</summary>
    internal virtual ValueTask<PdfDirectObject> NamedDestinationAsync(PdfDirectObject name) =>
        new(PdfDirectObject.CreateNull());

    private Matrix3x2 CreateRotateMatrix(PdfRect cropRect, long rotateBy)
    {
        var centerX = cropRect.Left + cropRect.Right / 2;
        var centerY = cropRect.Top + cropRect.Bottom / 2;
        return Matrix3x2.CreateRotation((float)(rotateBy * Math.PI / -180),
            new Vector2((float)centerX, (float)centerY));
    }

    private async ValueTask<PdfRect> GetCropDimensionsAsync(IHasPageAttributes pageStruct) =>
        await pageStruct.GetBoxAsync(BoxName.CropBox).CA() ?? new PdfRect(0, 0, 1, 1);

    private RenderEngine CreateRenderEngine(HasRenderableContentStream page, IRenderTarget target) =>
        new(page, new(target, this, OptionalContentEngine()));

    private IOptionalContentCounter OptionalContentEngine()
    {
        return OptionalContentState.AllVisible()
            ? NullOptionalContentCounter.Instance
            : new OptionalContentCounter(OptionalContentState);
    }


    /// <summary>
    /// This method is usually called inside the callback passed to REnderPageTo. In this
    /// method the client indicates the desired size and orientation of the rendered output.
    /// </summary>
    /// <param name="innerRenderer">The rendertarget that will eventually be returned to RenderPageTo</param>
    /// <param name="rect">The background rectangle for the page.</param>
    /// <param name="width">The width, in pixels of the rendered output</param>
    /// <param name="height">The height, in pixels of the rendered output.</param>
    /// <param name="PageRotationTransform">A matrix that adjusts the output for the page's default rotation</param>
    public virtual void InitializeRenderTarget(IRenderTarget innerRenderer,
        in PdfRect rect, double width, double height, in Matrix3x2 PageRotationTransform)
    {
        innerRenderer.GraphicsState.CurrentState().SetPageSize(width, height);
        innerRenderer.SetBackgroundRect(rect, width, height, PageRotationTransform);
        innerRenderer.MapUserSpaceToBitmapSpace(rect, width, height, PageRotationTransform);
    }

    /// <summary>
    /// This method takes a requested size and computes the actual final size of th rendered page.  Parameters less than
    /// 0 are considered don't care values and appropriate defaults are used.
    /// </summary>
    /// <param name="pageSize">The rectangle representing the printed page in PDF units</param>
    /// <param name="requestedSize">The requested size in pixels</param>
    /// <returns></returns>
    public virtual (int width, int height) ScalePageToRequestedSize(in PdfRect pageSize, Vector2 requestedSize) =>
        (requestedSize) switch
        {
            { X: < 0, Y: < 0 } => ((int)pageSize.Width, (int)pageSize.Height),
            { X: < 0 } => new(Scale(pageSize.Width, requestedSize.Y, pageSize.Height), (int)requestedSize.Y),
            { Y: < 0 } => new((int)requestedSize.X, Scale(pageSize.Height, requestedSize.X, pageSize.Width)),
            _ => ((int)requestedSize.X, (int)requestedSize.Y)
        };

    private static int Scale(double freeDimension, float setValue, double setDimension) =>
        (int)(freeDimension * (setValue / setDimension));

    /// <summary>
    /// Gets the nth page of a document
    /// </summary>
    /// <param name="oneBasedPageNumber">The page number, starting at 1, for the desired page</param>
    /// <returns>A HasRenderableContentStream implementation representing the page.</returns>
    protected abstract ValueTask<HasRenderableContentStream> GetPageContentAsync(int oneBasedPageNumber);

    /// <inheritdoc />
    public virtual void Dispose() => Cache.Dispose();

    /// <summary>
    /// Uncolored tile pattern renderers are required to ignore all color setting operators.  This
    /// virtual method gives the Document renderer a mechanism to opt out of color functionality.
    /// </summary>
    /// <param name="inner">A suggested IColorOperations object that will actually execute the color operations</param>
    /// <returns>The IColorOperations object to use -- typicially either the inner parameter or NullColorOperations.Instance</returns>
    public virtual IColorOperations AdjustColorOperationsModel(IColorOperations inner) => inner;
}