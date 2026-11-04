using System.Numerics;
using Melville.Pdf.LowLevel.Model.Conventions;
using Melville.Pdf.LowLevel.Model.Objects;
using Melville.Pdf.MauiGraphics.Drawing;
using Melville.Pdf.Model.Renderers.Colors;
using Melville.Pdf.Model.Renderers.DocumentRenderers;
using Melville.Pdf.Model.Renderers.GraphicsStates;
using Melville.Pdf.Model.Renderers.Patterns.ShaderPatterns;
using Melville.Pdf.Model.Renderers.Patterns.TilePatterns;
using Microsoft.Maui.Graphics;
using PdfLineCap = Melville.Pdf.LowLevel.Model.ContentStreams.LineCap;
using PdfLineJoin = Melville.Pdf.LowLevel.Model.ContentStreams.LineJoinStyle;

namespace Melville.Pdf.MauiGraphics.Rendering;

/// <summary>Where a fill's colour or pattern comes from; turned into a PdfFill when a path is painted.</summary>
internal interface IFillSource
{
    PdfFill CreateFill(MauiGraphicsState topState);
}

internal sealed class SolidFillSource(Color color) : IFillSource
{
    private readonly SolidFill fill = new(color);
    public PdfFill CreateFill(MauiGraphicsState topState) => fill;
}

/// <summary>A shading rendered into page-pixel space; mapped back through the state in use when painted.</summary>
internal sealed class ShadingFillSource(PdfImage image) : IFillSource
{
    public PdfFill CreateFill(MauiGraphicsState topState) => new ImageFill(image, topState.RevertToPixelsMatrix());
}

/// <summary>A tile recorded in pattern space, repeated every RepeatSize.</summary>
internal sealed class TileFillSource(PdfDisplayList tile, Matrix3x2 patternTransform, Vector2 repeatSize) : IFillSource
{
    public PdfFill CreateFill(MauiGraphicsState topState) =>
        new TileFill(tile, patternTransform * topState.RevertToPixelsMatrix(), new SizeF(repeatSize.X, repeatSize.Y));
}

internal sealed class MauiNativeBrush : INativeBrush
{
    private DeviceColor color = new(0, 0, 0, 255);
    private double alpha = 1;
    private IFillSource? source;

    public void SetSolidColor(DeviceColor color)
    {
        this.color = color;
        source = null;
    }

    public void SetAlpha(double alpha)
    {
        this.alpha = alpha;
        if (source is SolidFillSource) source = null;
    }

    public async ValueTask SetPatternAsync(PdfDictionary pattern, DocumentRenderer parentRenderer, GraphicsState prior)
    {
        source = await pattern.GetOrDefaultAsync(KnownNames.PatternType, 0).ConfigureAwait(false) switch
        {
            1 => await CreateTileSourceAsync(pattern, parentRenderer, prior).ConfigureAwait(false),
            2 => await CreateShadingSourceAsync(pattern, prior).ConfigureAwait(false),
            _ => new SolidFillSource(Colors.Transparent),
        };
    }

    private static async Task<IFillSource> CreateTileSourceAsync(
        PdfDictionary pattern, DocumentRenderer parentRenderer, GraphicsState prior)
    {
        var request = await TileBrushRequest.ParseAsync(pattern).ConfigureAwait(false);
        // Tiles are recorded at one unit per pattern-space unit; they stay vector, so no resolution is needed.
        var tile = await RenderWithMauiGraphics.RecordPageAsync(
            parentRenderer.PatternRenderer(request, prior), 0, pixelsPerPoint: 1).ConfigureAwait(false);
        return new TileFillSource(tile, request.PatternTransform, request.RepeatSize);
    }

    private static async Task<IFillSource> CreateShadingSourceAsync(PdfDictionary pattern, GraphicsState prior)
    {
        var shader = await ShaderParser.ParseShaderAsync(pattern).ConfigureAwait(false);
        int width = Math.Max(1, (int)prior.PageWidth), height = Math.Max(1, (int)prior.PageHeight);
        var image = PdfImage.Create(width, height, pixels => Render(shader, pixels, width, height));
        return new ShadingFillSource(image);

        static unsafe void Render(IShaderWriter shader, nint pixels, int width, int height) =>
            shader.RenderBits((uint*)pixels, width, height);
    }

    public void WriteColorTo(INativeBrush target)
    {
        if (target is not MauiNativeBrush other) return;
        other.color = color;
        other.alpha = alpha;
        other.source = source;
    }

    public T TryGetNativeBrush<T>() => (T)(source ??= new SolidFillSource(ToColor(color.WithAlpha(alpha))));

    private static Color ToColor(DeviceColor c) => Color.FromRgba(c.RedByte, c.GreenByte, c.BlueByte, c.Alpha);
}

internal sealed class MauiGraphicsState() : GraphicsState(new MauiNativeBrush(), new MauiNativeBrush())
{
    public PdfFill Fill() => NonstrokeBrush.TryGetNativeBrush<IFillSource>().CreateFill(this);

    public PdfStroke Stroke()
    {
        var paint = StrokeBrush.TryGetNativeBrush<IFillSource>().CreateFill(this);
        return new PdfStroke(
            paint.FallbackColor,
            (float)this.EffectiveLineWidth(),
            Cap(LineCap),
            Join(LineJoinStyle),
            (float)MiterLimit,
            this.IsDashedStroke() ? DashArrayFor(DashArray) : null,
            (float)DashPhase);
    }

    // A dash array with an odd count repeats to make dash/gap pairs, as Skia does.
    private static float[] DashArrayFor(IReadOnlyList<double> dashes) =>
        (dashes.Count % 2 == 0 ? dashes : dashes.Concat(dashes)).Select(d => (float)d).ToArray();

    private static Microsoft.Maui.Graphics.LineCap Cap(PdfLineCap cap) => cap switch
    {
        PdfLineCap.Round => Microsoft.Maui.Graphics.LineCap.Round,
        PdfLineCap.Square => Microsoft.Maui.Graphics.LineCap.Square,
        _ => Microsoft.Maui.Graphics.LineCap.Butt,
    };

    private static LineJoin Join(PdfLineJoin join) => join switch
    {
        PdfLineJoin.Round => LineJoin.Round,
        PdfLineJoin.Bevel => LineJoin.Bevel,
        _ => LineJoin.Miter,
    };
}
