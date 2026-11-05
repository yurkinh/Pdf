namespace Melville.Pdf.Maui.Sample.Library;

public partial class LibraryPage : ContentPage
{
    private readonly LibraryViewModel viewModel;

    public LibraryPage(LibraryViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = this.viewModel = viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await viewModel.LoadAsync();
    }
}
