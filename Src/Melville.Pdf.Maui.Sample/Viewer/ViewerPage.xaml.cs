namespace Melville.Pdf.Maui.Sample.Viewer;

/// <summary>
/// Shows one document. Saves the reading position when the window stops (backgrounded), when it is destroyed
/// (Android's back button can finish the activity without Stopped), and when the user leaves the page; closes the
/// document when the page is popped so it does not stay in memory.
/// </summary>
public partial class ViewerPage : ContentPage
{
    private readonly ViewerViewModel viewModel;
    private Window? window;

    public ViewerPage(ViewerViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = this.viewModel = viewModel;
        Loaded += (_, _) => Attach(Window);
        Unloaded += (_, _) => Attach(null);
    }

    private void Attach(Window? newWindow)
    {
        if (window is not null)
        {
            window.Stopped -= OnWindowStopped;
            window.Destroying -= OnWindowStopped;
        }
        window = newWindow;
        if (window is not null)
        {
            window.Stopped += OnWindowStopped;
            window.Destroying += OnWindowStopped;
        }
    }

    private void OnWindowStopped(object? sender, EventArgs e) => viewModel.SavePosition();

    protected override void OnNavigatedFrom(NavigatedFromEventArgs args)
    {
        base.OnNavigatedFrom(args);
        // Popped (back button or gesture): this page will not be shown again, so release the document.
        // Pushed over (for example the password prompt, which is modal and does not navigate): keep it.
        if (args.NavigationType is NavigationType.Pop or NavigationType.PopToRoot) viewModel.Close();
        else viewModel.SavePosition();
    }
}
