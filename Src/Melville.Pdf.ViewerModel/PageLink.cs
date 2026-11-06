using Melville.Pdf.Model.Documents;

namespace Melville.Pdf.ViewerModel;

/// <summary>
/// A link on a page, laid out for the viewer.
/// </summary>
/// <param name="Area">The area that follows the link, in page points from the page's top-left corner.</param>
/// <param name="Target">Where the link goes: a <see cref="PdfPageLinkTarget"/> to show with
/// <see cref="PdfViewerViewModel{TPage}.GoTo"/>, or a <see cref="PdfUriLinkTarget"/> for the app to open.</param>
public sealed record PageLink(PageRect Area, PdfLinkTarget Target);
