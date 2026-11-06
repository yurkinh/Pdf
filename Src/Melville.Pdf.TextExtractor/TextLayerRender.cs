using System.Numerics;
using System.Text;
using Melville.INPC;
using Melville.Pdf.LowLevel.Model.Objects;
using Melville.Pdf.LowLevel.Model.Wrappers;
using Melville.Pdf.Model.Renderers;
using Melville.Pdf.Model.Renderers.Bitmaps;
using Melville.Pdf.Model.Renderers.FontRenderings;
using Melville.Pdf.Model.Renderers.FontRenderings.CharacterReaders;
using Melville.Pdf.Model.Renderers.FontRenderings.GlyphMappings;
using Melville.Pdf.Model.Renderers.FontRenderings.Type3;
using Melville.Pdf.Model.Renderers.GraphicsStates;
using Melville.StreamInterfaces;

namespace Melville.Pdf.TextExtractor;

/// <summary>
/// A render target that draws nothing but records every glyph: its Unicode text and where it is, through the
/// target's transform (which maps the page to top-left page points). Unlike <see cref="ExtractTextRender"/>,
/// fonts keep reading their own character codes, so glyph widths, and so positions, are those of a real rendering;
/// the ToUnicode map is read alongside, from the same bytes.
/// </summary>
[FromConstructor]
internal partial class TextLayerRender : RenderTargetBase<TextLayerBuilder, UncoloredGraphicsState>
{
    public override IDrawTarget CreateDrawTarget() => NullDrawTarget.Instance;

    public override ValueTask RenderBitmapAsync(IPdfBitmap bitmap) => ValueTask.CompletedTask;

    public override void SetBackgroundRect(in PdfRect rect, double width, double height, in Matrix3x2 transform)
    {
    }

    public override async ValueTask<IRealizedFont> WrapRealizedFontAsync(
        IRealizedFont font, PdfDictionary? fontDeclaration)
    {
        var unicode = fontDeclaration is null
            ? null
            : await new ReadUnicodeCharactersFactory(font, fontDeclaration).ParseTextMappingAsync().CA();
        return new RecordingFont(font, ReferenceEquals(unicode, font.ReadCharacter) ? null : unicode);
    }

    internal TextLayerBuilder Builder => Target;
}

/// <summary>
/// Wraps a font so its glyphs are drawn as usual (onto a target that draws nothing) and recorded. The renderer
/// caches wrapped fonts per document, for every page and every TextLayerRender, so the recording goes to the
/// builder of the target each string is written to, never to one captured here.
/// </summary>
internal sealed class RecordingFont : IRealizedFont, IReadCharacter
{
    private readonly IRealizedFont inner;
    private readonly IReadCharacter? unicode;
    private readonly Dictionary<uint, string> textOfCode = [];
    private readonly uint[] unicodeScratch = new uint[8];

    public RecordingFont(IRealizedFont inner, IReadCharacter? unicode)
    {
        this.inner = inner;
        this.unicode = unicode;
    }

    public IReadCharacter ReadCharacter => this;
    public IMapCharacterToGlyph MapCharacterToGlyph => inner.MapCharacterToGlyph;
    public double? CharacterWidth(uint character) => inner.CharacterWidth(character);
    public int GlyphCount => inner.GlyphCount;
    public string FamilyName => inner.FamilyName;
    public string Description => inner.Description;
    public bool IsCachableFont => false; // the recording differs per target

    // Read one character code as the font does, and its Unicode text from the same bytes.
    public Span<uint> GetCharacters(in ReadOnlySpan<byte> input, in Span<uint> scratchBuffer, out int bytesConsumed)
    {
        var codes = inner.ReadCharacter.GetCharacters(input, scratchBuffer, out bytesConsumed);
        if (unicode is not null && codes.Length == 1 && bytesConsumed > 0)
        {
            lock (textOfCode)
                if (!textOfCode.ContainsKey(codes[0])) textOfCode[codes[0]] = Decode(input[..bytesConsumed]);
        }
        return codes;
    }

    private string Decode(ReadOnlySpan<byte> bytes)
    {
        var text = new StringBuilder();
        while (bytes.Length > 0)
        {
            var chars = unicode!.GetCharacters(bytes, unicodeScratch, out var consumed);
            if (consumed <= 0) break;
            foreach (var c in chars) AppendCodePoint(text, c);
            bytes = bytes[consumed..];
        }
        return text.ToString();
    }

    private static void AppendCodePoint(StringBuilder text, uint codePoint)
    {
        if (codePoint is 0 or > 0x10FFFF || codePoint is >= 0xD800 and <= 0xDFFF) return;
        text.Append(char.ConvertFromUtf32((int)codePoint));
    }

    // Without a ToUnicode map, the code is taken as the character, as the plain text extractor does.
    private string TextOf(uint code)
    {
        lock (textOfCode)
            if (textOfCode.TryGetValue(code, out var text)) return text;
        return unicode is null && code is > 0 and <= 0x10FFFF and not (>= 0xD800 and <= 0xDFFF) ? char.ConvertFromUtf32((int)code)
            : "";
    }

    public IFontWriteOperation BeginFontWrite(IFontTarget target)
    {
        // Text inside a Type 3 glyph or a tiling pattern is written to another target: it is not page text.
        var output = (target.RenderTarget as TextLayerRender)?.Builder ?? TextLayerBuilder.Discard;
        output.BeginString();
        return new WriteOperation(this, inner.BeginFontWrite(target), target, output);
    }

    private sealed class WriteOperation(
        RecordingFont font, IFontWriteOperation inner, IFontTarget target, TextLayerBuilder output)
        : IFontWriteOperation
    {
        private uint lastCode;
        private Matrix3x2 lastGlyphMatrix;

        public async ValueTask AddGlyphToCurrentStringAsync(uint character, uint glyph, Matrix3x2 textMatrix)
        {
            await inner.AddGlyphToCurrentStringAsync(character, glyph, textMatrix).CA();
            (lastCode, lastGlyphMatrix) = (character, textMatrix * target.RenderTarget.GraphicsState.CurrentState().TransformMatrix);
            // The width is known now unless the font has no widths; then NativeWidthOfLastGlyphAsync comes next.
            if (font.inner.CharacterWidth(character) is { } width) Record(width);
        }

        public async ValueTask<double> NativeWidthOfLastGlyphAsync(uint glyph)
        {
            var width = await inner.NativeWidthOfLastGlyphAsync(glyph).CA();
            Record(width);
            return width;
        }

        // Glyph space is one em per unit (the glyph matrix includes the font size), so the advance is the width.
        private void Record(double width)
        {
            var text = font.TextOf(lastCode);
            if (text.Length == 0) return;
            var start = Vector2.Transform(Vector2.Zero, lastGlyphMatrix);
            var end = Vector2.Transform(new Vector2((float)width, 0), lastGlyphMatrix);
            var up = Vector2.TransformNormal(new Vector2(0, 0.8f), lastGlyphMatrix);
            output.Add(new TextGlyph(text, start, end, up));
        }

        public void DeltaInsideWrite(double value) => inner.DeltaInsideWrite(value);

        public void RenderCurrentString(bool stroke, bool fill, bool clip, in Matrix3x2 finalTextMatrix) =>
            inner.RenderCurrentString(stroke, fill, clip, finalTextMatrix);

        public IFontWriteOperation CreatePeerWriteOperation(IFontTarget peerTarget) =>
            new WriteOperation(font, inner.CreatePeerWriteOperation(peerTarget), peerTarget, output);
    }
}
