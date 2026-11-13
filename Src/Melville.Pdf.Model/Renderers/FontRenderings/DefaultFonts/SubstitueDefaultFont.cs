using System;
using System.Linq;
using Melville.Pdf.LowLevel.Model.Conventions;
using Melville.Pdf.LowLevel.Model.Objects;
using Melville.Pdf.Model.Documents;
using System.Text;
using Melville.Postscript.Interpreter.Values;

namespace Melville.Pdf.Model.Renderers.FontRenderings.DefaultFonts;

/// <summary>
/// Given a FontFlags value select a built in font that most closely  replaces the font.
/// This is used when a PDF file is missing a font or the font cannot be read and the
/// reader has to substitute a builtin font.
/// </summary>
public static class SubstituteDefaultFont
{
    private static readonly PdfDirectObject[] names =
    {
        KnownNames.Courier,
        KnownNames.CourierBold,
        KnownNames.CourierOblique,
        KnownNames.CourierBoldOblique,
        KnownNames.Helvetica,
        KnownNames.HelveticaBold,
        KnownNames.HelveticaOblique,
        KnownNames.HelveticaBoldOblique,
        KnownNames.TimesRoman,
        KnownNames.TimesBold,
        KnownNames.TimesOblique,
        KnownNames.TimesBoldOblique,
        KnownNames.Symbol,
        KnownNames.Symbol,
        KnownNames.Symbol,
        KnownNames.Symbol,
    };
    
    /// <summary>
    /// Get a default font PdfName that most closely resembles the font flags given
    /// </summary>
    /// <param name="flags">Fontflags for the type to immitate</param>
    /// <returns>A PdfName corresponding to a built in font.</returns>
    public static PdfDirectObject MapBuiltInFont(this FontFlags flags) => 
      names[FamilyOffset(flags) + BoldOffset(flags) + ItalicOffset(flags)];

    /// <summary>
    /// Guess the built in font that resembles a font from its name, such as "Times New Roman,Bold" or
    /// "ABCDEE+Arial-ItalicMT": the family from well known family names, bold and italic from the style words in the
    /// name or from <paramref name="flags"/>. Null when the name gives no family away.
    /// </summary>
    /// <param name="font">The font's name (BaseFont).</param>
    /// <param name="flags">Fontflags of the font, for bold and italic the name does not mention.</param>
    /// <returns>A PdfName corresponding to a built in font, or null.</returns>
    public static PdfDirectObject? MapFontName(PdfDirectObject font, FontFlags flags)
    {
        if (!font.IsName) return null;
        var text = Encoding.Latin1.GetString(font.Get<StringSpanSource>().GetSpan());
        if (text.Length > 7 && text[6] == '+') text = text[7..]; // a subset prefix, such as ABCDEE+
        var key = new string(text.Where(char.IsLetter).Select(char.ToLowerInvariant).ToArray());
        if (key.Length == 0) return null;
        if (ContainsAny(key, "dingbat", "wingding", "webding")) return KnownNames.ZapfDingbats;
        if (ContainsAny(key, "symbol")) return KnownNames.Symbol;
        int family;
        if (ContainsAny(key, "courier", "mono", "consola", "typewriter", "menlo", "fixed")) family = 0;
        else if (ContainsAny(key, "sans", "arial", "helvetica", "calibri", "segoe", "verdana", "tahoma", "roboto",
                     "gothic", "grotesk", "avenir", "futura", "frutiger", "myriad", "lucida", "trebuchet", "candara",
                     "corbel", "ubuntu", "opensans", "noto", "inter", "lato", "geneva")) family = 4;
        else if (ContainsAny(key, "times", "serif", "georgia", "garamond", "cambria", "palatino", "bookman", "antiqua",
                     "century", "minion", "libertine", "baskerville", "didot", "bodoni", "caslon", "constantia",
                     "charter", "roman")) family = 8;
        else return null;
        var bold = ContainsAny(key, "bold", "black", "heavy", "semibold", "demi") || flags.HasFlag(FontFlags.ForceBold);
        var italic = ContainsAny(key, "italic", "oblique") || flags.HasFlag(FontFlags.Italic);
        return names[family + (bold ? 1 : 0) + (italic ? 2 : 0)];
    }

    private static bool ContainsAny(string key, params ReadOnlySpan<string> words)
    {
        foreach (var word in words)
            if (key.Contains(word, StringComparison.Ordinal)) return true;
        return false;
    }

    private static int FamilyOffset(FontFlags flags)
    {
        if (flags.HasFlag(FontFlags.Symbolic)) return 12;
        if (flags.HasFlag(FontFlags.FixedPitch)) return 0;
        if (flags.HasFlag(FontFlags.Serif)) return 8;
        return 4;

    }

    private static int BoldOffset(FontFlags flags) => flags.HasFlag(FontFlags.ForceBold) ? 1 : 0;
    private static int ItalicOffset(FontFlags flags) => flags.HasFlag(FontFlags.Italic) ? 2 : 0;
}