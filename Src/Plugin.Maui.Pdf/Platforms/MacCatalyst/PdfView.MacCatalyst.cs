using UIKit;

namespace Plugin.Maui.Pdf;

public partial class PdfView
{
    // Two-finger trackpad scrolling and mouse wheels arrive as scroll-type pans, which MAUI's
    // PanGestureRecognizer does not report on Mac Catalyst.
    partial void AttachPlatformGestures(GraphicsView view)
    {
        if (view.Handler?.PlatformView is not UIView platform) return;
        if (platform.GestureRecognizers?.OfType<ScrollRecognizer>().Any() == true) return;
        platform.AddGestureRecognizer(new ScrollRecognizer(this));
    }

    private sealed class ScrollRecognizer : UIPanGestureRecognizer
    {
        private CoreGraphics.CGPoint last;

        public ScrollRecognizer(PdfView viewer)
        {
            AllowedScrollTypesMask = UIScrollTypeMask.All;
            MaximumNumberOfTouches = 0; // scroll events only, no touch or click drags
            AddTarget(() =>
            {
                var total = TranslationInView(View);
                if (State == UIGestureRecognizerState.Began) last = new CoreGraphics.CGPoint(0, 0);
                if (State is UIGestureRecognizerState.Began or UIGestureRecognizerState.Changed)
                {
                    viewer.ScrollBy(total.X - last.X, total.Y - last.Y);
                    last = total;
                }
            });
        }
    }
}
