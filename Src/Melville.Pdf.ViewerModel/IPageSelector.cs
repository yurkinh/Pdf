namespace Melville.Pdf.ViewerModel;

/// <summary>
/// UI that can select the displayed page within a PDF document.
/// </summary>
public interface IPageSelector
{
    /// <summary>
    /// The page currently being displayed. The first page is number 1.
    /// </summary>
    int Page { get; set; }

    /// <summary>
    /// Total number of pages in the document, which is also the number of the last page.
    /// </summary>
    int MaxPage { get; set; }

    /// <summary>
    /// Go to the next page.
    /// </summary>
    void Increment();

    /// <summary>
    /// Go to the previous page.
    /// </summary>
    void Decrement();

    /// <summary>
    /// Go to the first page.
    /// </summary>
    void ToStart();

    /// <summary>
    /// Go to the last page.
    /// </summary>
    void ToEnd();
}
