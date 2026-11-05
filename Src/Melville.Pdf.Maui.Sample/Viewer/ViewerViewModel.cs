using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Melville.Pdf.Maui.Controls;
using Melville.Pdf.ReferenceDocuments.Infrastructure;

namespace Melville.Pdf.Maui.Sample.Viewer;

public sealed class ViewerViewModel : INotifyPropertyChanged
{
    private readonly Dictionary<string, IPdfGenerator> generators =
        GeneratorFactory.AllGenerators.ToDictionary(g => g.Prefix.TrimStart('-'));

    private string? selectedDocument;
    private PdfSource? source;
    private int pageNumber = 1;
    private string title = "No document";

    public ViewerViewModel()
    {
        Documents = generators.Keys.Order(StringComparer.Ordinal).ToList();
        OpenFileCommand = new Command(async () => await OpenFileAsync());
    }

    /// <summary>The reference documents, generated in memory when chosen.</summary>
    public IReadOnlyList<string> Documents { get; }

    public string? SelectedDocument
    {
        get => selectedDocument;
        set
        {
            if (!Set(ref selectedDocument, value) || value is null || !generators.TryGetValue(value, out var generator)) return;
            Title = value;
            Source = PdfSource.FromStream(async _ =>
            {
                var bytes = new MemoryStream();
                await generator.WritePdfAsync(bytes);
                bytes.Position = 0;
                return bytes;
            });
        }
    }

    public PdfSource? Source
    {
        get => source;
        private set => Set(ref source, value);
    }

    public int PageNumber
    {
        get => pageNumber;
        set => Set(ref pageNumber, value);
    }

    public string Title
    {
        get => title;
        private set => Set(ref title, value);
    }

    public ICommand OpenFileCommand { get; }

    private async Task OpenFileAsync()
    {
        var file = await FilePicker.Default.PickAsync(new PickOptions
        {
            PickerTitle = "Open a PDF",
            FileTypes = FilePickerFileType.Pdf,
        });
        if (file is null) return; // cancelled
        Title = file.FileName;
        Source = PdfSource.FromFileResult(file);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        return true;
    }
}
