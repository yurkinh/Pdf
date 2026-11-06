namespace Plugin.Maui.Pdf.TestApp.Gallery;

public partial class GalleryPage : ContentPage
{
    public GalleryPage(GalleryViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}
