using FluentAssertions;
using Melville.Pdf.LowLevel.Model.Conventions;
using Melville.Pdf.LowLevel.Model.Objects;
using Melville.Pdf.Model.Documents;
using Melville.Pdf.Model.Renderers.FontRenderings.DefaultFonts;
using Xunit;

namespace Melville.Pdf.DataModelTests.Fonts;

public class SubstituteDefaultFontTest
{
    public static TheoryData<string, string> Names => new()
    {
        { "Times New Roman,Bold", "Times-Bold" },
        { "TimesNewRomanPS-BoldItalicMT", "Times-BoldOblique" },
        { "ABCDEE+Georgia", "Times-Roman" },
        { "Arial", "Helvetica" },
        { "ABCDEE+Segoe UI,Bold", "Helvetica-Bold" },
        { "Calibri-Italic", "Helvetica-Oblique" },
        { "AAAAAB+AvenirNext-DemiBold", "Helvetica-Bold" },
        { "MicrosoftSansSerif", "Helvetica" },
        { "CourierNewPS-BoldMT", "Courier-Bold" },
        { "Consolas", "Courier" },
        { "Symbol", "Symbol" },
        { "Wingdings-Regular", "ZapfDingbats" },
    };

    [Theory]
    [MemberData(nameof(Names))]
    public void MapsWellKnownFamiliesAndStylesByName(string name, string expected)
    {
        var mapped = SubstituteDefaultFont.MapFontName(PdfDirectObject.CreateName(name), FontFlags.None);

        mapped.Should().NotBeNull();
        mapped!.Value.ToString().Should().Be(expected);
    }

    [Fact]
    public void TheFlagsAddBoldAndItalicTheNameDoesNotMention()
    {
        var mapped = SubstituteDefaultFont.MapFontName(PdfDirectObject.CreateName("Times New Roman"),
            FontFlags.ForceBold | FontFlags.Italic);

        mapped.Should().Be(KnownNames.TimesBoldOblique);
    }

    [Fact]
    public void AnUnknownNameIsLeftToTheFlags()
    {
        SubstituteDefaultFont.MapFontName(PdfDirectObject.CreateName("XYZ123"), FontFlags.Serif).Should().BeNull();
    }
}
