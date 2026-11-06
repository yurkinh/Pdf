using System.Windows.Input;
using Melville.Pdf.Maui.Sample.Documents;

namespace Melville.Pdf.Maui.Sample.Library;

/// <summary>The bundled sample documents, plus opening a PDF from the file system.</summary>
public sealed class LibraryViewModel : ObservableObject
{
    private readonly DocumentCatalog catalog;
    private readonly ReadingPositions positions;
    private IReadOnlyList<CatalogDocument> documents = [];
    private string? error;
    private ReadingPosition? latest;

    public LibraryViewModel(DocumentCatalog catalog, ReadingPositions positions)
    {
        this.catalog = catalog;
        this.positions = positions;
        ContinueCommand = new Command(async () => await ContinueAsync());
    }

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

    /// <summary>Whether there is a saved position to go back to.</summary>
    public bool CanContinue => latest is not null;

    /// <summary>"Continue reading …" for the most recently saved position.</summary>
    public string ContinueText => latest is { } p ? $"Continue reading {p.Title}, page {p.State.Page}" : "";

    /// <summary>Reopen the most recently read document at its saved position.</summary>
    public ICommand ContinueCommand { get; }

    /// <summary>Show the latest position and follow later saves while the page is on screen.</summary>
    public void StartWatching()
    {
        positions.Changed += OnPositionsChanged;
        RefreshLatest();
    }

    /// <summary>Stop following saves (the page is hidden).</summary>
    public void StopWatching() => positions.Changed -= OnPositionsChanged;

    private void OnPositionsChanged(object? sender, EventArgs e) => RefreshLatest();

    private void RefreshLatest()
    {
        latest = positions.Latest;
        OnPropertyChanged(nameof(CanContinue));
        OnPropertyChanged(nameof(ContinueText));
    }

    private async Task ContinueAsync()
    {
        if (latest is null) return;
        if (await catalog.FindAsync(latest.Key) is { } document) await AppNavigation.OpenAsync(document);
        else Error = $"“{latest.Title}” is no longer available.";
    }

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
