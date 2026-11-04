using System.Windows.Input;
using Melville.INPC;

namespace Melville.Pdf.ViewerModel;

/// <summary>
/// Lets the user see and change the current page.
/// </summary>
public partial class PageSelectorViewModel : IPageSelector
{
    /// <summary>
    /// The currently selected page number. Values outside MinPage..MaxPage are clamped.
    /// </summary>
    [AutoNotify] private int page = 1;

    /// <summary>
    /// The number of the first page.
    /// </summary>
    [AutoNotify] private int minPage = 1;

    /// <summary>
    /// The number of the last page.
    /// </summary>
    [AutoNotify] private int maxPage = 1;

    private int PageSetFilter(int newPage) => Math.Clamp(newPage, MinPage, Math.Max(MinPage, MaxPage));
    private void OnMaxPageChanged(int newValue) => Page = MinPage;

    /// <summary>
    /// Go to the next page.
    /// </summary>
    public ICommand IncrementCommand { get; }

    /// <summary>
    /// Go to the previous page.
    /// </summary>
    public ICommand DecrementCommand { get; }

    /// <summary>
    /// Go to the first page.
    /// </summary>
    public ICommand ToStartCommand { get; }

    /// <summary>
    /// Go to the last page.
    /// </summary>
    public ICommand ToEndCommand { get; }

    /// <summary>
    /// Create a page selector for a one-page document; set MaxPage for longer documents.
    /// </summary>
    public PageSelectorViewModel()
    {
        IncrementCommand = new RelayCommand(Increment, () => Page < MaxPage, this);
        DecrementCommand = new RelayCommand(Decrement, () => Page > MinPage, this);
        ToStartCommand = new RelayCommand(ToStart, () => Page != MinPage, this);
        ToEndCommand = new RelayCommand(ToEnd, () => Page < MaxPage, this);
    }

    /// <inheritdoc />
    public void Increment() => Page++;

    /// <inheritdoc />
    public void Decrement() => Page--;

    /// <inheritdoc />
    public void ToStart() => Page = MinPage;

    /// <inheritdoc />
    public void ToEnd() => Page = MaxPage;

    /// <summary>
    /// Text for the current and total number of pages, for example "3 of 12".
    /// </summary>
    public string DisplayString => $"{Page} of {MaxPage}";
}
