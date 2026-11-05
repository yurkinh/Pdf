using System.Windows.Input;
using Melville.Pdf.Maui.Sample.Documents;

namespace Melville.Pdf.Maui.Sample.Library;

/// <summary>The bundled sample documents, plus opening a PDF from the file system.</summary>
public sealed class LibraryViewModel(DocumentCatalog catalog) : ObservableObject
{
    private IReadOnlyList<CatalogDocument> documents = [];
    private string? error;

    public IReadOnlyList<CatalogDocument> Documents { get => documents; private set => Set(ref documents, value); }

    /// <summary>Open the tapped row in the viewer.</summary>
    public ICommand OpenCommand { get; } = new Command<CatalogDocument>(async d => await AppNavigation.OpenAsync(d));

    public string? Error
    {
        get => error;
        private set { if (Set(ref error, value)) OnPropertyChanged(nameof(HasError)); }
    }

    public bool HasError => error is not null;

    public ICommand OpenFileCommand { get; } = new Command(async () => await AppNavigation.PickAndOpenAsync());

    public async Task LoadAsync()
    {
        if (Documents.Count > 0) return;
        try
        {
            Documents = await catalog.GetSamplesAsync();
        }
        catch (Exception e)
        {
            Error = $"The bundled samples could not be read: {e.Message}";
        }
    }
}
