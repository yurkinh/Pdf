using System.Threading.Tasks;
using Melville.Pdf.LowLevel.Model.Objects;
using Melville.Pdf.Model.Documents;
using Melville.Pdf.Model.OptionalContent;
using Melville.Pdf.Model.Renderers.DocumentPartCaches;
using Melville.Pdf.Model.Renderers.FontRenderings.DefaultFonts;

namespace Melville.Pdf.Model.Renderers.DocumentRenderers;

internal class OwnedPageTreeDocumentRenderer : PageTreeDocumentRenderer
{
    private readonly PdfDocument document;
    public OwnedPageTreeDocumentRenderer(
        int totalPages, IDefaultFontMapper fontMapper, IDocumentPartCache cache, PageTree tree, 
        IOptionalContentState ocs, PdfDocument document) : base(totalPages, fontMapper, cache, tree, ocs)
    {
        this.document = document;
    }

    internal override ValueTask<PdfDirectObject> NamedDestinationAsync(PdfDirectObject name) =>
        document.NamedDestinationAsync(name);

    public override void Dispose()
    {
        document.Dispose();
        base.Dispose();
    }
}