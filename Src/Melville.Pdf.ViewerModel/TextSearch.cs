using System.Globalization;
using Melville.Pdf.TextExtractor;

namespace Melville.Pdf.ViewerModel;

/// <summary>
/// One place a search found its text: the page, the characters of that page's <see cref="PageText.Text"/>, and the
/// areas to highlight, one per line the match spans, in page points from the page's top-left corner (the
/// coordinates of <see cref="PageLayout.ToPage"/>).
/// </summary>
public sealed record SearchMatch(int Page, int Start, int Length, IReadOnlyList<PageRect> Areas)
{
    /// <summary>The smallest rectangle around all of <see cref="Areas"/>.</summary>
    public PageRect Bounds
    {
        get
        {
            if (Areas.Count == 0) return default;
            double left = Areas.Min(a => a.X), top = Areas.Min(a => a.Y);
            return new PageRect(left, top, Areas.Max(a => a.Right) - left, Areas.Max(a => a.Bottom) - top);
        }
    }
}

/// <summary>
/// Finds text in a page's text layer. Matching ignores case (unless asked not to), width and kana type; any run of
/// white space in the query matches one space or line break of the page, so a phrase is found across a line break.
/// A word hyphenated at the end of a line is not joined.
/// </summary>
public static class TextSearch
{
    /// <summary>The query as it is matched: trimmed, with each run of white space made one space.</summary>
    public static string Normalize(string query) =>
        string.Join(' ', query.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    /// <summary>Every match of <paramref name="query"/> on a page, in text order, not overlapping.</summary>
    public static IReadOnlyList<SearchMatch> Find(PageText text, int page, string query, bool matchCase = false)
    {
        List<SearchMatch> found = [];
        foreach (var (start, length) in FindRanges(text.Text, query, matchCase))
        {
            var areas = AreasOf(text, start, length);
            if (areas.Count > 0) found.Add(new SearchMatch(page, start, length, areas));
        }
        return found;
    }

    /// <summary>
    /// Where <paramref name="query"/> occurs in a page's <see cref="PageText.Text"/>: (start, length) pairs in text
    /// order, not overlapping. Needs only the text, so a viewer can keep the text of every page and read the glyph
    /// positions again only for pages that match.
    /// </summary>
    public static IReadOnlyList<(int Start, int Length)> FindRanges(string text, string query, bool matchCase = false)
    {
        var wanted = Normalize(query);
        if (wanted.Length == 0 || text.Length == 0) return [];
        // The same length as the text, so positions carry over; line breaks become the spaces they stand for.
        var haystack = string.Create(text.Length, text,
            static (span, source) => { for (int i = 0; i < span.Length; i++) span[i] = char.IsWhiteSpace(source[i]) ? ' ' : source[i]; });
        var options = CompareOptions.IgnoreWidth | CompareOptions.IgnoreKanaType |
                      (matchCase ? CompareOptions.None : CompareOptions.IgnoreCase);
        var compare = CultureInfo.InvariantCulture.CompareInfo;
        List<(int, int)> found = [];
        for (int from = 0; from < haystack.Length;)
        {
            var at = compare.IndexOf(haystack.AsSpan(from), wanted, options, out var length);
            if (at < 0) break;
            var start = from + at;
            if (length <= 0) { from = start + 1; continue; } // only ignorable characters matched
            found.Add((start, length));
            from = start + length;
        }
        return found;
    }

    /// <summary>
    /// The areas covered by Text[start..(start + length)]: one rectangle per line, around the line's glyph boxes.
    /// A line ends at an inserted line break.
    /// </summary>
    public static IReadOnlyList<PageRect> AreasOf(PageText text, int start, int length)
    {
        List<PageRect> areas = [];
        PageRect? line = null;
        int lastGlyph = -1;
        for (int i = start; i < start + length && i < text.Text.Length; i++)
        {
            var glyph = text.GlyphAt(i);
            if (glyph < 0)
            {
                if (text.Text[i] == '\n' && line is { } ended)
                {
                    areas.Add(ended);
                    line = null;
                }
                continue;
            }
            if (glyph == lastGlyph) continue; // a ligature's characters share one glyph
            lastGlyph = glyph;
            var (left, top, right, bottom) = text.Glyphs[glyph].Bounds;
            var box = new PageRect(left, top, right - left, bottom - top);
            line = line is { } current ? Union(current, box) : box;
        }
        if (line is { } last) areas.Add(last);
        return areas;
    }

    private static PageRect Union(PageRect a, PageRect b)
    {
        double left = Math.Min(a.X, b.X), top = Math.Min(a.Y, b.Y);
        return new PageRect(left, top, Math.Max(a.Right, b.Right) - left, Math.Max(a.Bottom, b.Bottom) - top);
    }
}
