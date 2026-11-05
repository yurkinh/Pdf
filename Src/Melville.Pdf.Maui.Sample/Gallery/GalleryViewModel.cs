using System.Windows.Input;
using Melville.Pdf.Maui.Sample.Documents;

namespace Melville.Pdf.Maui.Sample.Gallery;

/// <summary>Every Melville.Pdf reference document, filtered by name or description.</summary>
public sealed class GalleryViewModel : ObservableObject
{
    private readonly IReadOnlyList<CatalogDocument> all;
    private IReadOnlyList<CatalogDocument> documents;
    private string filter = "";

    public GalleryViewModel(DocumentCatalog catalog)
    {
        all = catalog.References;
        documents = all;
    }

    public IReadOnlyList<CatalogDocument> Documents { get => documents; private set => Set(ref documents, value); }

    public string Filter
    {
        get => filter;
        set
        {
            if (!Set(ref filter, value ?? "")) return;
            Documents = filter.Length == 0
                ? all
                : all.Where(d => d.Title.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                                 d.Description.Contains(filter, StringComparison.OrdinalIgnoreCase)).ToList();
            OnPropertyChanged(nameof(Summary));
        }
    }

    public string Summary => Documents.Count == all.Count
        ? $"{all.Count} reference documents"
        : $"{Documents.Count} of {all.Count} reference documents";

    /// <summary>Open the tapped row in the viewer.</summary>
    public ICommand OpenCommand { get; } = new Command<CatalogDocument>(async d => await AppNavigation.OpenAsync(d));
}
