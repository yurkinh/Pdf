using System.Numerics;

namespace Melville.Pdf.TextExtractor;

/// <summary>
/// A character drawn on a page and where it is: the text a viewer searches, selects and copies, and the area to
/// highlight. Coordinates are page points from the top-left corner of the page as drawn (after the page's default
/// rotation), so they match a rendering of the page at one pixel per point.
/// </summary>
/// <param name="Text">The Unicode text of the glyph: usually one character, more for ligatures such as "fi".</param>
/// <param name="BaselineStart">Where the glyph starts on its baseline.</param>
/// <param name="BaselineEnd">Where the glyph's advance ends on its baseline: the direction from start to end is the
/// direction of the text.</param>
/// <param name="Ascent">The vector from the baseline to the top of the glyph's line (0.8 em, perpendicular to the
/// baseline). Fonts do not reliably report their ascent, so the line box is an approximation of 0.8 em above the
/// baseline and 0.2 em below it.</param>
public readonly record struct TextGlyph(string Text, Vector2 BaselineStart, Vector2 BaselineEnd, Vector2 Ascent)
{
    /// <summary>The descent is a quarter of the ascent: 0.2 em below the baseline.</summary>
    private Vector2 Descent => -Ascent / 4;

    /// <summary>The four corners of the glyph's box, starting at the bottom left (in text direction) and going
    /// round through bottom right, top right and top left.</summary>
    public (Vector2 BottomLeft, Vector2 BottomRight, Vector2 TopRight, Vector2 TopLeft) Corners =>
        (BaselineStart + Descent, BaselineEnd + Descent, BaselineEnd + Ascent, BaselineStart + Ascent);

    /// <summary>The smallest upright rectangle around the glyph: (left, top, right, bottom).</summary>
    public (float Left, float Top, float Right, float Bottom) Bounds
    {
        get
        {
            var (a, b, c, d) = Corners;
            return (Math.Min(Math.Min(a.X, b.X), Math.Min(c.X, d.X)),
                Math.Min(Math.Min(a.Y, b.Y), Math.Min(c.Y, d.Y)),
                Math.Max(Math.Max(a.X, b.X), Math.Max(c.X, d.X)),
                Math.Max(Math.Max(a.Y, b.Y), Math.Max(c.Y, d.Y)));
        }
    }

    /// <summary>The height of the glyph's line box: one em.</summary>
    public float LineHeight => Ascent.Length() * 1.25f;
}

/// <summary>
/// The text of a page, in the order it is drawn, with every glyph's position.
/// <see cref="Text"/> joins the glyphs with spaces where words are apart and line breaks where lines change;
/// <see cref="GlyphAt"/> maps a position in it back to the glyph it came from.
/// </summary>
public sealed class PageText
{
    private readonly int[] glyphOfChar;

    internal PageText(IReadOnlyList<TextGlyph> glyphs, string text, int[] glyphOfChar)
    {
        Glyphs = glyphs;
        Text = text;
        this.glyphOfChar = glyphOfChar;
    }

    /// <summary>A page with no text.</summary>
    public static PageText Empty { get; } = new([], "", []);

    /// <summary>Every glyph that has text, in drawing order. Glyphs drawn with invisible text count too: scanned
    /// pages put their recognised text there.</summary>
    public IReadOnlyList<TextGlyph> Glyphs { get; }

    /// <summary>The page's text: glyph texts in drawing order, with a space between words and a line break
    /// ("\n") between lines.</summary>
    public string Text { get; }

    /// <summary>The index in <see cref="Glyphs"/> of the glyph that produced <see cref="Text"/>[index], or -1 for
    /// a space or line break that was inserted between glyphs.</summary>
    public int GlyphAt(int index) => glyphOfChar[index];

    /// <summary>The glyphs that produced <see cref="Text"/>[start..(start + length)], in order, without the
    /// inserted spaces and line breaks.</summary>
    public IEnumerable<TextGlyph> GlyphsIn(int start, int length)
    {
        int last = -1;
        for (int i = start; i < start + length && i < glyphOfChar.Length; i++)
        {
            var glyph = glyphOfChar[i];
            if (glyph < 0 || glyph == last) continue; // a ligature's characters share one glyph
            last = glyph;
            yield return Glyphs[glyph];
        }
    }
}
