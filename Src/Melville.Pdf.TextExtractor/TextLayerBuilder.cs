using System.Numerics;
using System.Text;

namespace Melville.Pdf.TextExtractor;

/// <summary>
/// Collects the glyphs of a page and joins their text: a space where the gap to the previous glyph on the same
/// line is wide enough to be a word break, and a line break where the next glyph is not on the same line.
/// </summary>
internal sealed class TextLayerBuilder
{
    /// <summary>Gap between glyphs on a line, in em, from which a space is inserted.</summary>
    private const float WordGap = 0.15f;

    /// <summary>Distance across the baseline, in em, beyond which the next glyph is on another line.</summary>
    private const float LineShift = 0.5f;

    /// <summary>How far, in em, the next glyph may start before the end of the previous one and still continue
    /// the line (kerning and overlapping glyphs).</summary>
    private const float Overlap = 0.5f;

    /// <summary>A builder whose glyphs are thrown away.</summary>
    public static TextLayerBuilder Discard { get; } = new(discard: true);

    private readonly bool discard;
    private readonly List<TextGlyph> glyphs = [];

    public TextLayerBuilder() : this(discard: false)
    {
    }

    private TextLayerBuilder(bool discard) => this.discard = discard;

    public void BeginString()
    {
    }

    public void Add(in TextGlyph glyph)
    {
        if (!discard) glyphs.Add(glyph);
    }

    public PageText Build()
    {
        if (glyphs.Count == 0) return PageText.Empty;
        var text = new StringBuilder();
        var owners = new List<int>();
        for (int i = 0; i < glyphs.Count; i++)
        {
            if (i > 0 && Separator(glyphs[i - 1], glyphs[i]) is { } separator &&
                !char.IsWhiteSpace(text[^1]) && !char.IsWhiteSpace(glyphs[i].Text[0]))
            {
                text.Append(separator);
                owners.Add(-1);
            }
            text.Append(glyphs[i].Text);
            for (int j = 0; j < glyphs[i].Text.Length; j++) owners.Add(i);
        }
        return new PageText([.. glyphs], text.ToString(), [.. owners]);
    }

    // Null when the glyphs are adjacent on one line; ' ' for a word gap; '\n' for a new line.
    private static char? Separator(in TextGlyph previous, in TextGlyph next)
    {
        var em = Math.Max(previous.LineHeight, 0.01f);
        var direction = previous.BaselineEnd - previous.BaselineStart;
        direction = direction.LengthSquared() > 1e-6f ? Vector2.Normalize(direction) : Vector2.UnitX;
        var delta = next.BaselineStart - previous.BaselineEnd;
        var along = Vector2.Dot(delta, direction) / em;
        var across = Math.Abs(direction.X * delta.Y - direction.Y * delta.X) / em;
        if (across > LineShift || along < -Overlap) return '\n';
        return along > WordGap ? ' ' : null;
    }
}
