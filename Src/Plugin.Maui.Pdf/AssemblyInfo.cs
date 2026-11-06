// XAML namespaces: xmlns:pdf="https://github.com/yurkinh/Pdf/maui" instead of the clr-namespace form, and the
// .NET MAUI 10 global namespace, so apps that use implicit XAML namespaces can write <PdfView /> with no prefix.
[assembly: Microsoft.Maui.Controls.XmlnsDefinition("https://github.com/yurkinh/Pdf/maui", "Plugin.Maui.Pdf")]
[assembly: Microsoft.Maui.Controls.XmlnsPrefix("https://github.com/yurkinh/Pdf/maui", "pdf")]
[assembly: Microsoft.Maui.Controls.XmlnsDefinition("http://schemas.microsoft.com/dotnet/maui/global", "Plugin.Maui.Pdf")]
