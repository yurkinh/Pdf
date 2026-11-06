namespace Plugin.Maui.Pdf.TestApp.Spike;

public partial class SpikePage : ContentPage
{
    private readonly SpikeViewModel viewModel;

    public SpikePage(SpikeViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = this.viewModel = viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (viewModel.Results.Count == 0) await viewModel.RunAsync();
    }
}
