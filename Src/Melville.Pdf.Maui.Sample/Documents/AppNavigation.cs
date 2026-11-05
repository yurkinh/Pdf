namespace Melville.Pdf.Maui.Sample.Documents;

/// <summary>Shell routes and how a document is handed to the viewer.</summary>
public static class AppNavigation
{
    public const string ViewerRoute = "viewer";
    public const string DocumentKey = "document";

    /// <summary>Open <paramref name="document"/> in the viewer page.</summary>
    public static Task OpenAsync(CatalogDocument document) =>
        Shell.Current.GoToAsync(ViewerRoute, new ShellNavigationQueryParameters { [DocumentKey] = document });

    /// <summary>Let the user pick a PDF and open it; nothing happens if they cancel.</summary>
    public static async Task PickAndOpenAsync()
    {
        var file = await FilePicker.Default.PickAsync(new PickOptions
        {
            PickerTitle = "Open a PDF",
            FileTypes = FilePickerFileType.Pdf,
        });
        if (file is null) return;
        await OpenAsync(new CatalogDocument(file.FileName, "Picked from the file system", null,
            Controls.PdfSource.FromFileResult(file)));
    }
}
