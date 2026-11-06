using System.Collections.Generic;
using System.Threading.Tasks;
using Melville.StreamInterfaces;
using Melville.Pdf.LowLevel.Model.Objects;
using Melville.Pdf.Model.Documents;
using Melville.Pdf.Model.OptionalContent;
using Melville.Pdf.Model.Renderers.DocumentPartCaches;
using Melville.Pdf.Model.Renderers.FontRenderings.DefaultFonts;

namespace Melville.Pdf.Model.Renderers.DocumentRenderers;

internal class PageTreeDocumentRenderer : DocumentRenderer
{
    private readonly PageTree tree;

    public PageTreeDocumentRenderer(int totalPages, IDefaultFontMapper fontMapper, IDocumentPartCache cache, 
        PageTree tree, IOptionalContentState ocs) :
        base(totalPages, fontMapper, cache, ocs)
    {
        this.tree = tree;
    }

    protected override ValueTask<HasRenderableContentStream> GetPageContentAsync(int oneBasedPageNumber) =>
        tree.GetPageAsync(oneBasedPageNumber);

    // Page dictionaries are loaded once per document, so the same page is always the same instance.
    private Dictionary<PdfDictionary, int>? pageNumbers;

    internal override async ValueTask<int> PageNumberOfAsync(PdfDictionary page)
    {
        if (pageNumbers is null)
        {
            var numbers = new Dictionary<PdfDictionary, int>(ReferenceEqualityComparer.Instance);
            int number = 0;
            await foreach (var each in tree.CA()) numbers.TryAdd(each.LowLevel, ++number);
            pageNumbers = numbers;
        }
        return pageNumbers.GetValueOrDefault(page);
    }
}