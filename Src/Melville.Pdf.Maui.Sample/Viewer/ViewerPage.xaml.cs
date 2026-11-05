namespace Melville.Pdf.Maui.Sample.Viewer;

public partial class ViewerPage : ContentPage
{
    public ViewerPage(ViewerViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}
