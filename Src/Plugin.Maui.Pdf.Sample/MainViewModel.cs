using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace Plugin.Maui.Pdf.Sample;

/// <summary>Which document the page shows, and the status line under the viewer.</summary>
public sealed class MainViewModel : INotifyPropertyChanged
{
    private PdfSource? source;
    private int pageNumber = 1;
    private int pageCount;
    private string title = "welcome.pdf";

    public MainViewModel()
    {
        // Bundled PDFs live in Resources/Raw; a picked file is read through FilePicker (works with Android content
        // URIs and the iOS/Mac sandbox, where there may be no usable path).
        OpenWelcomeCommand = new Command(() => Show("welcome.pdf", PdfSource.FromAppPackage("welcome.pdf")));
        OpenLayersCommand = new Command(() => Show("layers.pdf", PdfSource.FromAppPackage("layers.pdf")));
        OpenFileCommand = new Command(async () => await OpenFileAsync());
        Source = PdfSource.FromAppPackage("welcome.pdf");
    }

    public PdfSource? Source { get => source; private set => Set(ref source, value); }

    /// <summary>Two-way bound to the viewer.</summary>
    public int PageNumber
    {
        get => pageNumber;
        set { if (Set(ref pageNumber, value)) OnPropertyChanged(nameof(Status)); }
    }

    /// <summary>Bound from the viewer (read-only there).</summary>
    public int PageCount
    {
        get => pageCount;
        set { if (Set(ref pageCount, value)) OnPropertyChanged(nameof(Status)); }
    }

    public string Title { get => title; private set { if (Set(ref title, value)) OnPropertyChanged(nameof(Status)); } }

    public string Status => PageCount == 0 ? Title : $"{Title}: page {PageNumber} of {PageCount}";

    public ICommand OpenWelcomeCommand { get; }
    public ICommand OpenLayersCommand { get; }
    public ICommand OpenFileCommand { get; }

    private void Show(string name, PdfSource document)
    {
        Title = name;
        Source = document;
    }

    private async Task OpenFileAsync()
    {
        var file = await FilePicker.Default.PickAsync(new PickOptions
        {
            PickerTitle = "Open a PDF",
            FileTypes = FilePickerFileType.Pdf,
        });
        if (file is not null) Show(file.FileName, PdfSource.FromFileResult(file)); // null: the user cancelled
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    private bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(name);
        return true;
    }
}
