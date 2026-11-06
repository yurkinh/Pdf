using System.Numerics;
using Microsoft.Maui.Graphics.Platform;

namespace Plugin.Maui.Pdf.TestApp.Spike;

/// <summary>
/// Workaround candidate for Microsoft.Maui.Graphics on Android (10.0.x): PlatformCanvas.ConcatenateTransform
/// post-concatenates (device space instead of user space) and its Matrix3x2 conversion swaps the x and y
/// translation. Concatenate the correctly converted matrix on the native canvas instead.
/// </summary>
internal static class AndroidTransformFix
{
    public static void Concat(ICanvas canvas, Matrix3x2 m)
    {
        // Android hands out a ScalingCanvas around the PlatformCanvas. Its ConcatenateTransform both scales later
        // coordinates and forwards the transform, so scale is applied twice; go around it to the native canvas.
        // (Correct here because the export context's ScalingCanvas scale is 1; a GraphicsView's is the density.)
        var target = canvas is ScalingCanvas scaling ? scaling.ParentCanvas : canvas;
        if (target is not PlatformCanvas platform)
        {
            canvas.ConcatenateTransform(m);
            return;
        }
        // Matrix3x2 is row-vector (x' = x*M11 + y*M21 + M31); Android Matrix is column-vector.
        using var native = new Android.Graphics.Matrix();
        native.SetValues([m.M11, m.M21, m.M31, m.M12, m.M22, m.M32, 0, 0, 1]);
        platform.Canvas.Concat(native); // Canvas.concat pre-concatenates: the new matrix acts in user space
    }
}
