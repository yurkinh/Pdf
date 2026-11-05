namespace Melville.Pdf.Maui.Controls;

/// <summary>
/// Where a <see cref="PdfViewer"/> reads its document from. The document is opened when the viewer needs it,
/// and the stream is read to the end: PDF parsing needs random access.
/// </summary>
public abstract class PdfSource
{
    /// <summary>Open the document's bytes. The caller disposes the stream.</summary>
    public abstract Task<Stream> OpenAsync(CancellationToken cancellation);

    /// <summary>A document produced by a stream factory, called each time the document is opened.</summary>
    public static PdfSource FromStream(Func<CancellationToken, Task<Stream>> open) => new StreamSource(open);

    /// <summary>A document in memory.</summary>
    public static PdfSource FromBytes(byte[] bytes) =>
        new StreamSource(_ => Task.FromResult<Stream>(new MemoryStream(bytes, writable: false)));

    /// <summary>A document in the file system.</summary>
    public static PdfSource FromFile(string path) =>
        new StreamSource(_ => Task.FromResult<Stream>(File.OpenRead(path)));

    /// <summary>
    /// A document chosen with FilePicker. Opened through <see cref="FileBase.OpenReadAsync"/>, never by path:
    /// on Android the result can be a content URI.
    /// </summary>
    public static PdfSource FromFileResult(FileResult file) => new StreamSource(_ => file.OpenReadAsync());

    /// <summary>A file bundled with the app (Resources/Raw).</summary>
    public static PdfSource FromAppPackage(string fileName) =>
        new StreamSource(_ => FileSystem.Current.OpenAppPackageFileAsync(fileName));

    private sealed class StreamSource(Func<CancellationToken, Task<Stream>> open) : PdfSource
    {
        public override Task<Stream> OpenAsync(CancellationToken cancellation) => open(cancellation);
    }
}
