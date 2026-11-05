namespace Melville.Pdf.Maui.Sample.Gallery;

public partial class GalleryPage : ContentPage
{
    public GalleryPage(GalleryViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}
