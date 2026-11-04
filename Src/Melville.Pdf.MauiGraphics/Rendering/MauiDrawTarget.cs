using System.Numerics;
using Melville.Pdf.MauiGraphics.Drawing;
using Melville.Pdf.Model.Renderers;
using Melville.Pdf.Model.Renderers.GraphicsStates;
using Microsoft.Maui.Graphics;

namespace Melville.Pdf.MauiGraphics.Rendering;

/// <summary>Builds one path from PDF path operators (and glyph outlines), then fills, strokes or clips it.</summary>
internal sealed class MauiDrawTarget : IDrawTarget
{
    private readonly IPdfCanvas target;
    private readonly GraphicsStateStack<MauiGraphicsState> state;

    // Adobe's interpreter ignores drawing operations before the first MoveTo; null means no MoveTo yet.
    private PathF? path;

    // True once the path has been painted or clipped: it is recorded and must not change. It stays available,
    // because text in a clipping render mode paints and then clips the same glyph path; the next MoveTo starts
    // a new path.
    private bool recorded;

    public MauiDrawTarget(IPdfCanvas target, GraphicsStateStack<MauiGraphicsState> state)
    {
        this.target = target;
        this.state = state;
    }

    public void MoveTo(Vector2 startPoint)
    {
        if (path is null || recorded)
        {
            path = new PathF();
            recorded = false;
        }
        path.MoveTo(startPoint.X, startPoint.Y);
    }

    public void LineTo(Vector2 endPoint) => OpenPath()?.LineTo(endPoint.X, endPoint.Y);

    public void CurveTo(Vector2 control, Vector2 endPoint) =>
        OpenPath()?.QuadTo(control.X, control.Y, endPoint.X, endPoint.Y);

    public void CurveTo(Vector2 control1, Vector2 control2, Vector2 endPoint) =>
        OpenPath()?.CurveTo(control1.X, control1.Y, control2.X, control2.Y, endPoint.X, endPoint.Y);

    public void ClosePath() => OpenPath()?.Close();

    // Segments after a paint have no current point (no MoveTo yet), so they are ignored like before the first MoveTo.
    private PathF? OpenPath() => recorded ? null : path;

    public void EndGlyph()
    {
    }

    public void PaintPath(bool stroke, bool fill, bool evenOddFillRule)
    {
        if (path is null) return;
        recorded = true;
        var top = state.StronglyTypedCurrentState();
        if (fill) target.FillPath(path, Winding(evenOddFillRule), top.Fill());
        if (stroke) target.StrokePath(path, top.Stroke());
    }

    public void ClipToPath(bool evenOddRule)
    {
        if (path is null) return;
        recorded = true;
        target.ClipPath(path, Winding(evenOddRule));
    }

    private static WindingMode Winding(bool evenOdd) => evenOdd ? WindingMode.EvenOdd : WindingMode.NonZero;

    public void Dispose()
    {
    }
}
