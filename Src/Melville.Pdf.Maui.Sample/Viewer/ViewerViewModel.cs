using Melville.Pdf.LowLevel.Parsing.ParserContext;
using Melville.Pdf.Maui.Controls;
using Melville.Pdf.Maui.Sample.Documents;

namespace Melville.Pdf.Maui.Sample.Viewer;

/// <summary>The viewer page's state: which document, and where the user is in it.</summary>
public sealed class ViewerViewModel : ObservableObject, IQueryAttributable
{
    private PdfSource? source;
    private IPasswordSource? passwordSource;
    private int pageNumber = 1;
    private string title = "No document";

    public PdfSource? Source { get => source; private set => Set(ref source, value); }

    public IPasswordSource? PasswordSource { get => passwordSource; private set => Set(ref passwordSource, value); }

    public int PageNumber { get => pageNumber; set => Set(ref pageNumber, value); }

    public string Title { get => title; private set => Set(ref title, value); }

    /// <summary>Shell passes the document chosen on another page (see <see cref="AppNavigation"/>).</summary>
    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (!query.TryGetValue(AppNavigation.DocumentKey, out var value) || value is not CatalogDocument document) return;
        Show(document);
    }

    /// <summary>Show a document. The password source is set first: the viewer reads it when the source changes.</summary>
    public void Show(CatalogDocument document)
    {
        Title = document.Title;
        PageNumber = 1;
        PasswordSource = new PasswordPrompt(document.Title, document.AskForPassword ? null : document.Password);
        Source = document.Source;
    }
}
