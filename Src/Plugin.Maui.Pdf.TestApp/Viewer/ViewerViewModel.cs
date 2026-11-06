using Melville.Pdf.LowLevel.Parsing.ParserContext;
using Plugin.Maui.Pdf;
using Plugin.Maui.Pdf.TestApp.Documents;
using Melville.Pdf.ViewerModel;

namespace Plugin.Maui.Pdf.TestApp.Viewer;

/// <summary>
/// The viewer page's state: which document, and where the user is in it. The reading position is restored when
/// a document is opened again and saved by <see cref="SavePosition"/> (the page calls it when the window stops,
/// when it is destroyed, and when the user leaves the viewer).
/// </summary>
public sealed class ViewerViewModel(ReadingPositions positions) : ObservableObject, IQueryAttributable
{
    private CatalogDocument? document;
    private PdfSource? source;
    private IPasswordSource? passwordSource;
    private PdfViewState? viewState;
    private string title = "No document";

    public PdfSource? Source { get => source; private set => Set(ref source, value); }

    public IPasswordSource? PasswordSource { get => passwordSource; private set => Set(ref passwordSource, value); }

    /// <summary>Two-way bound to the viewer: set to restore a position, updated as the user moves around.</summary>
    public PdfViewState? ViewState { get => viewState; set => Set(ref viewState, value); }

    public string Title { get => title; private set => Set(ref title, value); }

    /// <summary>Shell passes the document chosen on another page (see <see cref="AppNavigation"/>).</summary>
    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (!query.TryGetValue(AppNavigation.DocumentKey, out var value) || value is not CatalogDocument chosen) return;
        Show(chosen);
    }

    /// <summary>
    /// Show a document at its saved position, if any. Password source and position are set before the source:
    /// the viewer reads both when the source changes.
    /// </summary>
    public void Show(CatalogDocument chosen)
    {
        SavePosition();
        document = chosen;
        Title = chosen.Title;
        PasswordSource = new PasswordPrompt(chosen.Title, chosen.AskForPassword ? null : chosen.Password);
        ViewState = chosen.Key is { } key ? positions.Find(key)?.State : null;
        Source = chosen.Source;
    }

    /// <summary>Remember where the user is in the current document (if it can be reopened later).</summary>
    public void SavePosition()
    {
        if (document?.Key is { } key && ViewState is { } state) positions.Save(key, document.Title, state);
    }

    /// <summary>Save the position and close the document, releasing it.</summary>
    public void Close()
    {
        SavePosition();
        document = null;
        Source = null;
    }
}
