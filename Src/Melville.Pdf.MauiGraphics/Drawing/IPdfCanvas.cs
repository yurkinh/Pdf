using System.Numerics;
using Microsoft.Maui.Graphics;

namespace Melville.Pdf.MauiGraphics.Drawing;

/// <summary>
/// The drawing operations a PDF page renders to. The renderer records them into a
/// <see cref="PdfDisplayList"/>; <see cref="MauiCanvasAdapter"/> plays them onto a Maui.Graphics ICanvas.
/// Paths passed in are not modified afterwards.
/// </summary>
public interface IPdfCanvas
{
    /// <summary>Save the transform and clip.</summary>
    void SaveState();

    /// <summary>Restore the last saved transform and clip. A restore without a matching save is ignored.</summary>
    void RestoreState();

    /// <summary>Apply a transform in the current (user) space.</summary>
    void ConcatenateTransform(in Matrix3x2 transform);

    /// <summary>Fill a path.</summary>
    void FillPath(PathF path, WindingMode winding, PdfFill fill);

    /// <summary>Stroke a path.</summary>
    void StrokePath(PathF path, PdfStroke stroke);

    /// <summary>Intersect the clip with a path.</summary>
    void ClipPath(PathF path, WindingMode winding);

    /// <summary>Draw an image stretched over a rectangle in the current space.</summary>
    void DrawImage(PdfImage image, RectF destination);
}
