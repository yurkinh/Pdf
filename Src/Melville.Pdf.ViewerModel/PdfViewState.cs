namespace Melville.Pdf.ViewerModel;

/// <summary>
/// Where the user is in a document, for saving when the app is backgrounded and restoring later.
/// The scroll position is the page point (in PDF points) at the viewport's top-left corner, so it survives a
/// different viewport size or zoom.
/// </summary>
/// <param name="Page">The page, first page 1.</param>
/// <param name="ZoomMode">How the page is sized.</param>
/// <param name="Zoom">The scale in <see cref="ViewerModel.ZoomMode.Custom"/> mode (otherwise the scale in use, for information).</param>
/// <param name="PageX">Page x at the viewport's left edge when the page is wider than the viewport, else 0.</param>
/// <param name="PageY">Page y at the viewport's top edge when the page is taller than the viewport, else 0.</param>
public readonly record struct PdfViewState(int Page, ZoomMode ZoomMode, double Zoom, double PageX = 0, double PageY = 0);
