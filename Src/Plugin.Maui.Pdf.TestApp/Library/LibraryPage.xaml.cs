namespace Plugin.Maui.Pdf.TestApp.Library;

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
        viewModel.StartWatching();
        await viewModel.LoadAsync();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        viewModel.StopWatching();
    }
}
