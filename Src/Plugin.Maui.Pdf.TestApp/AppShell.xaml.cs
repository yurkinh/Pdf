using Plugin.Maui.Pdf.TestApp.Documents;
using Plugin.Maui.Pdf.TestApp.Viewer;

namespace Plugin.Maui.Pdf.TestApp;

public partial class AppShell : Shell
{
	public AppShell()
	{
		InitializeComponent();
		// Resolved through dependency injection, so ViewerPage gets a fresh ViewerViewModel each time.
		Routing.RegisterRoute(AppNavigation.ViewerRoute, typeof(ViewerPage));
		// Unattended diagnostics run (see Diagnostics.AutoRun): start on the Diagnostics tab.
		if (Diagnostics.AutoRun.Requested) Loaded += async (_, _) => await GoToAsync("//diagnostics");
	}
}
