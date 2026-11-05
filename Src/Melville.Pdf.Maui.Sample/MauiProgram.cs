using Melville.Pdf.Maui.Hosting;
using Microsoft.Extensions.Logging;
#if DEBUG
using Microsoft.Maui.DevFlow.Agent;
#endif

namespace Melville.Pdf.Maui.Sample;

public static class MauiProgram
{
	public static MauiApp CreateMauiApp()
	{
		var builder = MauiApp.CreateBuilder();
		builder
			.UseMauiApp<App>()
			.ConfigureFonts(fonts =>
			{
				fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
				fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
			});

		builder.UseMelvillePdf();
		builder.Services.AddSingleton<Documents.DocumentCatalog>();
		builder.Services.AddTransient<Library.LibraryPage>();
		builder.Services.AddTransient<Library.LibraryViewModel>();
		builder.Services.AddTransient<Gallery.GalleryPage>();
		builder.Services.AddTransient<Gallery.GalleryViewModel>();
		builder.Services.AddTransient<Viewer.ViewerPage>();
		builder.Services.AddTransient<Viewer.ViewerViewModel>();
		builder.Services.AddTransient<Diagnostics.DiagnosticsPage>();
		builder.Services.AddTransient<Diagnostics.DiagnosticsViewModel>();
		builder.Services.AddTransient<Spike.SpikePage>();
		builder.Services.AddTransient<Spike.SpikeViewModel>();

#if DEBUG
		builder.Logging.AddDebug();
		builder.AddMauiDevFlowAgent();
#endif

		return builder.Build();
	}
}
