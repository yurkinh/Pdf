using Melville.Pdf.FontLibrary;
using Melville.Pdf.Model.Renderers.FontRenderings.DefaultFonts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Melville.Pdf.Maui.Hosting;

/// <summary>Options for <see cref="MelvillePdfHostingExtensions.UseMelvillePdf"/>.</summary>
public sealed class MelvillePdfOptions
{
    /// <summary>
    /// Fonts for PDF standard fonts and fonts a document does not embed. Defaults to the self-contained Latin
    /// fonts, which need no system font folder (there is none on iOS or Android). For Chinese, Japanese and
    /// Korean documents use SelfContainedCjkFonts from Melville.Pdf.FontLibrary.Cjk, which adds about 90 MB.
    /// </summary>
    public IDefaultFontMapper FontMapper { get; set; } = SelfContainedDefaultFonts.Instance;
}

/// <summary>Registers the Melville.Pdf viewer's services.</summary>
public static class MelvillePdfHostingExtensions
{
    /// <summary>Register the services PdfViewer uses (the font mapper).</summary>
    public static MauiAppBuilder UseMelvillePdf(this MauiAppBuilder builder, Action<MelvillePdfOptions>? configure = null)
    {
        var options = new MelvillePdfOptions();
        configure?.Invoke(options);
        builder.Services.TryAddSingleton(options.FontMapper);
        return builder;
    }
}
