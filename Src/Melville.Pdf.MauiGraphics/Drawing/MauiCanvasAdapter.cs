using System.Numerics;
using Microsoft.Maui.Graphics;

namespace Melville.Pdf.MauiGraphics.Drawing;

/// <summary>
/// The differences between Maui.Graphics backends that the PDF renderer has to compensate for.
/// </summary>
/// <param name="Concatenate">Applies a transform in user space. Defaults to ICanvas.ConcatenateTransform,
/// which is wrong on Android and throws in Maui.Graphics.Skia 10.0.x; those hosts pass a working one.</param>
public sealed record CanvasQuirks(Action<ICanvas, Matrix3x2> Concatenate)
{
    /// <summary>A backend whose ConcatenateTransform behaves as documented (CoreGraphics).</summary>
    public static CanvasQuirks Default { get; } = new((c, m) => c.ConcatenateTransform(m));
}

/// <summary>
/// Plays <see cref="IPdfCanvas"/> operations onto a Maui.Graphics ICanvas, compensating for backend
/// differences. Leaves the canvas state as it found it once every save is restored.
/// </summary>
public sealed class MauiCanvasAdapter : IPdfCanvas
{
    private const int MaxTilesPerFill = 4096;

    private readonly ICanvas canvas;
    private readonly IPdfImageFactory images;
    private readonly CanvasQuirks quirks;
    private int depth;

    /// <summary>Create an adapter for one replay onto a canvas.</summary>
    public MauiCanvasAdapter(ICanvas canvas, IPdfImageFactory images, CanvasQuirks quirks)
    {
        this.canvas = canvas;
        this.images = images;
        this.quirks = quirks;
    }

    /// <inheritdoc />
    public void SaveState()
    {
        depth++;
        canvas.SaveState();
    }

    /// <inheritdoc />
    public void RestoreState()
    {
        if (depth == 0) return;
        depth--;
        canvas.RestoreState();
    }

    /// <inheritdoc />
    public void ConcatenateTransform(in Matrix3x2 transform) => quirks.Concatenate(canvas, transform);

    /// <inheritdoc />
    public void FillPath(PathF path, WindingMode winding, PdfFill fill)
    {
        switch (fill)
        {
            case SolidFill solid:
                canvas.FillColor = solid.Color;
                canvas.FillPath(path, winding);
                break;
            case ImageFill image:
                SaveState();
                canvas.ClipPath(path, winding);
                ConcatenateTransform(image.ImageToUser);
                canvas.DrawImage(image.Image.PlatformImage(images), 0, 0, image.Image.Width, image.Image.Height);
                RestoreState();
                break;
            case TileFill tile:
                FillWithTiles(path, winding, tile);
                break;
        }
    }

    private void FillWithTiles(PathF path, WindingMode winding, TileFill tile)
    {
        if (!Matrix3x2.Invert(tile.TileToUser, out var userToTile)) return;
        var stepX = Math.Abs(tile.Step.Width);
        var stepY = Math.Abs(tile.Step.Height);
        if (stepX <= 0 || stepY <= 0) return;

        // The cells that can touch the path: its bounds mapped into tile space.
        var bounds = BoundsIn(path.Bounds, userToTile);
        var firstX = (int)Math.Floor(bounds.Left / stepX);
        var lastX = (int)Math.Ceiling(bounds.Right / stepX);
        var firstY = (int)Math.Floor(bounds.Top / stepY);
        var lastY = (int)Math.Ceiling(bounds.Bottom / stepY);
        if ((long)(lastX - firstX + 1) * (lastY - firstY + 1) > MaxTilesPerFill)
        {
            FillPath(path, winding, new SolidFill(tile.FallbackColor));
            return;
        }

        SaveState();
        canvas.ClipPath(path, winding);
        ConcatenateTransform(tile.TileToUser);
        for (int y = firstY; y <= lastY; y++)
        for (int x = firstX; x <= lastX; x++)
        {
            SaveState();
            ConcatenateTransform(Matrix3x2.CreateTranslation(x * stepX, y * stepY));
            canvas.ClipRectangle(0, 0, stepX, stepY);
            tile.Tile.Replay(this);
            RestoreState();
        }
        RestoreState();
    }

    private static RectF BoundsIn(RectF rect, in Matrix3x2 transform)
    {
        Span<Vector2> corners =
        [
            Vector2.Transform(new Vector2(rect.Left, rect.Top), transform),
            Vector2.Transform(new Vector2(rect.Right, rect.Top), transform),
            Vector2.Transform(new Vector2(rect.Left, rect.Bottom), transform),
            Vector2.Transform(new Vector2(rect.Right, rect.Bottom), transform),
        ];
        float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
        foreach (var c in corners)
        {
            minX = Math.Min(minX, c.X); minY = Math.Min(minY, c.Y);
            maxX = Math.Max(maxX, c.X); maxY = Math.Max(maxY, c.Y);
        }
        return new RectF(minX, minY, maxX - minX, maxY - minY);
    }

    /// <inheritdoc />
    public void StrokePath(PathF path, PdfStroke stroke)
    {
        canvas.StrokeColor = stroke.Color;
        canvas.StrokeSize = stroke.Width;
        canvas.StrokeLineCap = stroke.Cap;
        canvas.StrokeLineJoin = stroke.Join;
        canvas.MiterLimit = stroke.MiterLimit;
        // Maui.Graphics 10 ignores StrokeDashOffset on Skia, so the phase is applied to the pattern instead.
        canvas.StrokeDashOffset = 0;
        canvas.StrokeDashPattern = stroke.Dashes is { Length: > 0 } dashes && stroke.Width > 0
            ? DashPatternFor(StartAtPhase(dashes, stroke.DashPhase), stroke.Width)
            : null;
        canvas.DrawPath(path);
    }

    /// <summary>
    /// Maui.Graphics dash patterns are in multiples of the stroke width; PDF dashes are lengths.
    /// Even entries are dashes, odd entries gaps.
    /// </summary>
    public static float[] DashPatternFor(float[] dashes, float width) =>
        dashes.Select(d => d / width).ToArray();

    /// <summary>
    /// The dash pattern that starts <paramref name="phase"/> into <paramref name="dashes"/> (an even-length
    /// dash, gap, ... list): a pattern with phase 0 that draws the same line. Same period; a zero-length dash
    /// or gap keeps the dash/gap alternation where the rotation splits a segment.
    /// </summary>
    public static float[] StartAtPhase(float[] dashes, float phase)
    {
        var period = dashes.Sum();
        if (phase == 0 || period <= 0) return dashes;
        phase %= period;
        if (phase < 0) phase += period;
        if (phase == 0) return dashes;

        var index = 0;
        while (phase >= dashes[index])
        {
            phase -= dashes[index];
            index = (index + 1) % dashes.Length;
        }

        var rotated = new List<float>(dashes.Length + 2);
        if (index % 2 == 1) rotated.Add(0);            // the line starts inside a gap
        rotated.Add(dashes[index] - phase);            // the rest of the segment the phase lands in
        for (int i = index + 1; i < dashes.Length; i++) rotated.Add(dashes[i]);
        for (int i = 0; i < index; i++) rotated.Add(dashes[i]);
        rotated.Add(phase);                            // the part of that segment the phase skipped
        if (rotated.Count % 2 == 1) rotated.Add(0);    // ...joined to the next period's start by an empty gap
        return rotated.ToArray();
    }

    /// <inheritdoc />
    public void ClipPath(PathF path, WindingMode winding) => canvas.ClipPath(path, winding);

    /// <inheritdoc />
    public void DrawImage(PdfImage image, RectF destination) =>
        canvas.DrawImage(image.PlatformImage(images), destination.X, destination.Y, destination.Width, destination.Height);
}
