using System.Numerics;
using Melville.Pdf.LowLevel.Model.Wrappers;
using Melville.Pdf.MauiGraphics.Drawing;
using Melville.Pdf.Model.Renderers;
using Melville.Pdf.Model.Renderers.Bitmaps;
using Microsoft.Maui.Graphics;

namespace Melville.Pdf.MauiGraphics.Rendering;

/// <summary>
/// Render target that emits IPdfCanvas operations. Graphics state saves, restores and transform changes map
/// to SaveState, RestoreState and ConcatenateTransform, as in the WPF target.
/// </summary>
internal sealed class MauiRenderTarget : RenderTargetBase<IPdfCanvas, MauiGraphicsState>
{
    // IPdfBitmap draws into the unit square with y pointing up; images are stored top row first.
    private static readonly Matrix3x2 FlipUnitSquare = new(1, 0, 0, -1, 0, 1);
    private static readonly RectF UnitSquare = new(0, 0, 1, 1);

    public MauiRenderTarget(IPdfCanvas target) : base(target)
    {
        State.ContextPushed += (_, _) => Target.SaveState();
        State.BeforeContextPopped += (_, _) => Target.RestoreState();
        State.TransformPushed += (_, e) => Target.ConcatenateTransform(e.NewMatrix);
    }

    public override void SetBackgroundRect(in PdfRect rect, double width, double height, in Matrix3x2 transform)
    {
        var page = new PathF();
        page.AppendRectangle(0, 0, (float)width, (float)height);
        Target.FillPath(page, WindingMode.NonZero, new SolidFill(Colors.White));
        Target.ClipPath(page, WindingMode.NonZero);
    }

    public override IDrawTarget CreateDrawTarget() => new MauiDrawTarget(Target, State);

    public override async ValueTask RenderBitmapAsync(IPdfBitmap bitmap)
    {
        var decoded = await PdfImage.CreateAsync(bitmap.Width, bitmap.Height, RenderInto).ConfigureAwait(false);
        // The transform maps the unit square onto the page: its column lengths are the drawn size.
        var transform = State.StronglyTypedCurrentState().TransformMatrix;
        var image = ImageSizing.Prepare(decoded,
            new Vector2(transform.M11, transform.M12).Length(),
            new Vector2(transform.M21, transform.M22).Length(),
            bitmap.ShouldInterpolate(transform));
        Target.SaveState();
        Target.ConcatenateTransform(FlipUnitSquare);
        Target.DrawImage(image, UnitSquare);
        Target.RestoreState();

        unsafe ValueTask RenderInto(nint pixels) => bitmap.RenderPbgraAsync((byte*)pixels);
    }
}
