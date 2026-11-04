using System.Numerics;
using Melville.Pdf.MauiGraphics.Drawing;
using Melville.Pdf.MauiGraphics.Rendering;
using Melville.Pdf.Model.Renderers.DocumentRenderers;
using Melville.Pdf.ViewerModel;
using Microsoft.Maui.Graphics;

namespace Melville.Pdf.MauiGraphics;

/// <summary>
/// Renders PDF pages with Microsoft.Maui.Graphics. A page is recorded once into a <see cref="PdfDisplayList"/>
/// (off the UI thread) and then drawn onto any ICanvas, at any scale, as often as needed.
/// </summary>
public static class RenderWithMauiGraphics
{
    /// <summary>
    /// Record one page. Vector content stays resolution independent; <paramref name="pixelsPerPoint"/> sets the
    /// resolution of images the renderer rasterizes itself (shadings).
    /// </summary>
    /// <param name="document">The document to render.</param>
    /// <param name="oneBasedPageNumber">The page; the first page is 1.</param>
    /// <param name="pixelsPerPoint">Recording resolution, in units per PDF point.</param>
    public static async ValueTask<PdfDisplayList> RecordPageAsync(
        DocumentRenderer document, int oneBasedPageNumber, float pixelsPerPoint = 2)
    {
        var recorder = new DisplayListRecorder();
        float width = 0, height = 0;
        await document.RenderPageToAsync(oneBasedPageNumber, (rect, pageRotation) =>
        {
            var (w, h) = document.ScalePageToRequestedSize(rect,
                new Vector2((float)(rect.Width * pixelsPerPoint), (float)(rect.Height * pixelsPerPoint)));
            (width, height) = (w, h);
            var target = new MauiRenderTarget(recorder);
            document.InitializeRenderTarget(target, rect, w, h, pageRotation);
            return target;
        }).ConfigureAwait(false);
        return recorder.ToDisplayList(width, height, 1 / pixelsPerPoint);
    }

    /// <summary>
    /// Draw a recorded page onto a canvas at <paramref name="scale"/> canvas units per PDF point, with its top-left
    /// corner at the canvas origin. The canvas state is unchanged afterwards.
    /// </summary>
    public static void Draw(this PdfDisplayList page, ICanvas canvas, IPdfImageFactory images,
        CanvasQuirks quirks, float scale = 1)
    {
        var adapter = new MauiCanvasAdapter(canvas, images, quirks);
        adapter.SaveState();
        adapter.ConcatenateTransform(Matrix3x2.CreateScale(scale * page.PointsPerUnit));
        page.Replay(adapter);
        adapter.RestoreState();
    }
}

/// <summary>
/// Page renderer for <see cref="PdfViewerViewModel{TPage}"/>: records pages into display lists on a worker thread.
/// </summary>
/// <param name="pixelsPerPoint">Recording resolution for rasterized content (shadings).</param>
public sealed class MauiGraphicsPageRenderer(float pixelsPerPoint = 2) : IPageRenderer<PdfDisplayList>
{
    /// <inheritdoc />
    public async ValueTask<RenderedPage<PdfDisplayList>> RenderAsync(
        DocumentRenderer document, int oneBasedPageNumber, CancellationToken cancellation)
    {
        var page = await Task.Run(
            () => RenderWithMauiGraphics.RecordPageAsync(document, oneBasedPageNumber, pixelsPerPoint).AsTask(),
            cancellation).ConfigureAwait(false);
        return new RenderedPage<PdfDisplayList>(page, page.PageWidth, page.PageHeight);
    }
}
