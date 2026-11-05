namespace Melville.Pdf.Maui.Sample.Diagnostics;

public partial class DiagnosticsPage : ContentPage
{
    private readonly DiagnosticsViewModel viewModel;

    public DiagnosticsPage(DiagnosticsViewModel viewModel)
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
