using Melville.Pdf.Maui.Sample.Documents;
using Melville.Pdf.Maui.Sample.Viewer;

namespace Melville.Pdf.Maui.Sample;

public partial class AppShell : Shell
{
	public AppShell()
	{
		InitializeComponent();
		// Resolved through dependency injection, so ViewerPage gets a fresh ViewerViewModel each time.
		Routing.RegisterRoute(AppNavigation.ViewerRoute, typeof(ViewerPage));
	}
}
