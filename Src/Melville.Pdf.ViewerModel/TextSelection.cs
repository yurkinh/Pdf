using Melville.Pdf.TextExtractor;

namespace Melville.Pdf.ViewerModel;

/// <summary>
/// Selected text: characters of one page's <see cref="PageText.Text"/>, the text itself (with the spaces and line
/// breaks between words and lines) and the areas to highlight, in page points from the page's top-left corner.
/// </summary>
public sealed record TextSelection(int Page, int Start, int Length, string Text, IReadOnlyList<PageRect> Areas)
{
    internal static TextSelection Of(PageText text, int page, int start, int end) =>
        new(page, start, end - start, text.Text[start..end], TextSearch.AreasOf(text, start, end - start));
}

/// <summary>Finds the text under a point of a page, for selecting it.</summary>
public static class TextHitTest
{
    /// <summary>
    /// The index in <see cref="PageText.Text"/> of the character drawn nearest to (x, y), in page points from the
    /// top-left corner; -1 when the page has no text. A glyph whose box contains the point wins; otherwise the
    /// nearest box, where vertical distance counts double so that the same line is preferred.
    /// </summary>
    public static int IndexAt(PageText text, double x, double y)
    {
        if (text.Glyphs.Count == 0) return -1;
        int best = -1;
        double bestDistance = double.MaxValue;
        for (int i = 0; i < text.Glyphs.Count; i++)
        {
            var (left, top, right, bottom) = text.Glyphs[i].Bounds;
            var dx = x < left ? left - x : x > right ? x - right : 0;
            var dy = y < top ? top - y : y > bottom ? y - bottom : 0;
            var distance = dx * dx + 4 * dy * dy;
            if (distance < bestDistance)
            {
                (best, bestDistance) = (i, distance);
                if (distance == 0) break;
            }
        }
        return FirstIndexOf(text, best);
    }

    /// <summary>The first index in <see cref="PageText.Text"/> that a glyph produced (a ligature produces several).</summary>
    public static int FirstIndexOf(PageText text, int glyph)
    {
        for (int i = 0; i < text.Text.Length; i++)
            if (text.GlyphAt(i) == glyph) return i;
        return -1;
    }

    /// <summary>
    /// The word around Text[index], as (start, end) with end exclusive: letters, digits and marks, joined by an
    /// apostrophe or hyphen between them. A character that is not part of a word is a word of its own.
    /// </summary>
    public static (int Start, int End) WordAt(string text, int index)
    {
        if (index < 0 || index >= text.Length) return (0, 0);
        if (!IsWordChar(text, index)) return (index, ClusterEnd(text, index));
        int start = index, end = index + 1;
        while (start > 0 && IsWordChar(text, start - 1)) start--;
        while (end < text.Length && IsWordChar(text, end)) end++;
        return (start, end);
    }

    private static int ClusterEnd(string text, int index) =>
        char.IsHighSurrogate(text[index]) && index + 1 < text.Length ? index + 2 : index + 1;

    private static bool IsWordChar(string text, int i)
    {
        var c = text[i];
        if (char.IsLetterOrDigit(c) || char.IsSurrogate(c) || char.GetUnicodeCategory(c) is
                System.Globalization.UnicodeCategory.NonSpacingMark or System.Globalization.UnicodeCategory.SpacingCombiningMark)
            return true;
        // "don't", "well-known": joiners between word characters.
        return c is '\'' or '’' or '-' && i > 0 && i + 1 < text.Length &&
               char.IsLetterOrDigit(text[i - 1]) && char.IsLetterOrDigit(text[i + 1]);
    }
}
