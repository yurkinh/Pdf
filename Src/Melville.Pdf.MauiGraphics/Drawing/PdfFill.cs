using System.Numerics;
using Microsoft.Maui.Graphics;

namespace Melville.Pdf.MauiGraphics.Drawing;

/// <summary>How a path is filled.</summary>
public abstract record PdfFill
{
    /// <summary>A colour to use where the fill cannot be drawn as is, such as a patterned stroke.</summary>
    public abstract Color FallbackColor { get; }
}

/// <summary>Fill with one colour.</summary>
public sealed record SolidFill(Color Color) : PdfFill
{
    /// <inheritdoc />
    public override Color FallbackColor => Color;
}

/// <summary>
/// Fill with an image (a rendered shading): the image's pixel rectangle is mapped to the current space by
/// <paramref name="ImageToUser"/>, then clipped to the path.
/// </summary>
public sealed record ImageFill(PdfImage Image, Matrix3x2 ImageToUser) : PdfFill
{
    /// <inheritdoc />
    public override Color FallbackColor { get; } = Image.AverageColor();
}

/// <summary>
/// Fill with a tiling pattern: <paramref name="Tile"/> is drawn in cells of <paramref name="Step"/> in tile space,
/// mapped to the current space by <paramref name="TileToUser"/>, then clipped to the path.
/// </summary>
public sealed record TileFill(PdfDisplayList Tile, Matrix3x2 TileToUser, SizeF Step) : PdfFill
{
    /// <inheritdoc />
    public override Color FallbackColor => Colors.Black;
}

/// <summary>How a path is stroked. Width and dash lengths are in the current space.</summary>
public sealed record PdfStroke(
    Color Color, float Width, LineCap Cap, LineJoin Join, float MiterLimit,
    float[]? Dashes, float DashPhase);
