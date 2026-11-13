namespace Melville.Pdf.ViewerModel;

/// <summary>
/// Space on each side of a page: a margin around it (view units) or a crop that hides part of it (PDF points).
/// </summary>
public readonly record struct PageInsets(double Left, double Top, double Right, double Bottom)
{
    /// <summary>The same inset on every side.</summary>
    public static PageInsets Uniform(double inset) => new(inset, inset, inset, inset);

    /// <summary>No inset.</summary>
    public static PageInsets Zero => default;

    /// <summary>Whether every side is zero.</summary>
    public bool IsZero => Left == 0 && Top == 0 && Right == 0 && Bottom == 0;

    /// <summary>A page size with this crop taken off, at least one point each way.</summary>
    public PageSize Crop(PageSize size) =>
        new(Math.Max(1, size.Width - Left - Right), Math.Max(1, size.Height - Top - Bottom));
}
