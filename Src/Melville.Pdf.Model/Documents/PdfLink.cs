using Melville.Pdf.LowLevel.Model.Wrappers;

namespace Melville.Pdf.Model.Documents;

/// <summary>
/// A link on a page: the area that responds to a click or tap, and where it goes.
/// </summary>
/// <param name="Area">The link's rectangle, in the coordinates of
/// <see cref="Renderers.DocumentRenderers.DocumentRenderer.GetPageBoxAsync"/>: PDF points after the page's
/// default rotation. Like the page box, its corners can be in either order after a rotation.</param>
/// <param name="Target">Where the link goes.</param>
public sealed record PdfLink(PdfRect Area, PdfLinkTarget Target);

/// <summary>
/// Where a <see cref="PdfLink"/> goes.
/// </summary>
public abstract record PdfLinkTarget;

/// <summary>
/// A page in the same document, optionally a position on it.
/// </summary>
/// <param name="OneBasedPageNumber">The page to show.</param>
/// <param name="Left">The x coordinate to put at the left of the view, in the coordinates of that page's
/// <see cref="Renderers.DocumentRenderers.DocumentRenderer.GetPageBoxAsync"/>; null to keep the
/// current horizontal position.</param>
/// <param name="Top">The y coordinate to put at the top of the view, in the same coordinates; null to keep the
/// current vertical position.</param>
public sealed record PdfPageLinkTarget(int OneBasedPageNumber, double? Left = null, double? Top = null)
    : PdfLinkTarget;

/// <summary>
/// A web address or other URI, from a URI action.
/// </summary>
/// <param name="Uri">The URI, as written in the document.</param>
public sealed record PdfUriLinkTarget(string Uri) : PdfLinkTarget;
