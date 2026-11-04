using Melville.Pdf.Model.Renderers.DocumentRenderers;

namespace Melville.Pdf.ViewerModel;

/// <summary>
/// A rendered page in whatever form a display technology draws (a display list, a bitmap, a WPF drawing),
/// with the page's size in PDF points.
/// </summary>
public sealed record RenderedPage<TPage>(TPage Content, double Width, double Height);

/// <summary>
/// Renders one page of a document for a display technology.
/// </summary>
public interface IPageRenderer<TPage>
{
    /// <summary>
    /// Render a page. Implementations may run off the calling thread. The view model never runs two renders
    /// of one document at once, and discards a result whose request was cancelled.
    /// </summary>
    ValueTask<RenderedPage<TPage>> RenderAsync(
        DocumentRenderer document, int oneBasedPageNumber, CancellationToken cancellation);
}
