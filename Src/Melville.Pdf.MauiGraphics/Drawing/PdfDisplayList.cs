using System.Numerics;
using Microsoft.Maui.Graphics;

namespace Melville.Pdf.MauiGraphics.Drawing;

/// <summary>
/// A recorded page (or pattern tile): the drawing operations, in order, plus the size they were recorded at.
/// Immutable once recorded, so it can be replayed from any thread, any number of times.
/// </summary>
public sealed class PdfDisplayList
{
    private readonly IReadOnlyList<Action<IPdfCanvas>> operations;

    internal PdfDisplayList(IReadOnlyList<Action<IPdfCanvas>> operations, float width, float height,
        float pointsPerUnit, int fillCount, int imageCount)
    {
        this.operations = operations;
        Width = width;
        Height = height;
        PointsPerUnit = pointsPerUnit;
        FillCount = fillCount;
        ImageCount = imageCount;
    }

    /// <summary>Width of the recording space (pixels at the recording resolution).</summary>
    public float Width { get; }

    /// <summary>Height of the recording space.</summary>
    public float Height { get; }

    /// <summary>PDF points per recording unit; replay with this scale to draw at 1 unit per point.</summary>
    public float PointsPerUnit { get; }

    /// <summary>Page width in PDF points.</summary>
    public float PageWidth => Width * PointsPerUnit;

    /// <summary>Page height in PDF points.</summary>
    public float PageHeight => Height * PointsPerUnit;

    /// <summary>Number of recorded operations.</summary>
    public int Count => operations.Count;

    /// <summary>Number of fill and stroke operations (diagnostics).</summary>
    public int FillCount { get; }

    /// <summary>Number of images drawn (diagnostics).</summary>
    public int ImageCount { get; }

    /// <summary>Play every operation onto a canvas.</summary>
    public void Replay(IPdfCanvas canvas)
    {
        foreach (var operation in operations) operation(canvas);
    }
}

/// <summary>Records drawing operations into a <see cref="PdfDisplayList"/>.</summary>
public sealed class DisplayListRecorder : IPdfCanvas
{
    private readonly List<Action<IPdfCanvas>> operations = [];
    private int depth;
    private int fills;
    private int images;

    /// <inheritdoc />
    public void SaveState()
    {
        depth++;
        operations.Add(static c => c.SaveState());
    }

    /// <inheritdoc />
    public void RestoreState()
    {
        if (depth == 0) return; // the graphics state stack pops its base state when it is disposed
        depth--;
        operations.Add(static c => c.RestoreState());
    }

    /// <inheritdoc />
    public void ConcatenateTransform(in Matrix3x2 transform)
    {
        var copy = transform;
        operations.Add(c => c.ConcatenateTransform(copy));
    }

    /// <inheritdoc />
    public void FillPath(PathF path, WindingMode winding, PdfFill fill)
    {
        fills++;
        operations.Add(c => c.FillPath(path, winding, fill));
    }

    /// <inheritdoc />
    public void StrokePath(PathF path, PdfStroke stroke)
    {
        fills++;
        operations.Add(c => c.StrokePath(path, stroke));
    }

    /// <inheritdoc />
    public void ClipPath(PathF path, WindingMode winding) =>
        operations.Add(c => c.ClipPath(path, winding));

    /// <inheritdoc />
    public void DrawImage(PdfImage image, RectF destination)
    {
        images++;
        operations.Add(c => c.DrawImage(image, destination));
    }

    /// <summary>
    /// Finish the recording. Unbalanced saves are closed so a replay always leaves the canvas as it found it.
    /// </summary>
    public PdfDisplayList ToDisplayList(float width, float height, float pointsPerUnit)
    {
        while (depth > 0) RestoreState();
        return new PdfDisplayList(operations.ToArray(), width, height, pointsPerUnit, fills, images);
    }
}
