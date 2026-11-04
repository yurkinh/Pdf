// Phase 1 spike: answers the plan's open Microsoft.Maui.Graphics questions by drawing small scenes
// and checking pixels. Backend-independent: the console runs it on Skia, the MAUI sample on each
// platform's native backend (CoreGraphics, Android Canvas, Win2D).
using System.Numerics;
using Microsoft.Maui.Graphics;
using IImage = Microsoft.Maui.Graphics.IImage; // MAUI apps also import Microsoft.Maui.IImage (the Image control)

namespace MauiGraphicsSpike;

/// <summary>Draws a scene on some ICanvas backend and returns its pixels.</summary>
public interface ICanvasRasterizer
{
    string Name { get; }
    Pixels Render(int width, int height, Action<ICanvas> draw);
    /// <summary>An IImage made from premultiplied BGRA pixels (what IPdfBitmap.RenderPbgraAsync produces).</summary>
    IImage ImageFromPbgra(int width, int height, uint[] pbgra);
}

/// <summary>Straight RGBA bytes, row major.</summary>
public sealed record Pixels(int Width, int Height, byte[] Rgba)
{
    public (byte R, byte G, byte B, byte A) At(int x, int y)
    {
        var i = (y * Width + x) * 4;
        return (Rgba[i], Rgba[i + 1], Rgba[i + 2], Rgba[i + 3]);
    }

    public string Describe(int x, int y)
    {
        var (r, g, b, _) = At(x, y);
        return r > 200 && g > 200 && b > 200 ? "white"
            : r < 60 && g < 60 && b < 60 ? "black"
            : r > 200 && g < 60 && b < 60 ? "red"
            : g > 200 && r < 60 && b < 60 ? "green"
            : b > 200 && r < 60 && g < 60 ? "blue"
            : $"rgb({r},{g},{b})";
    }

    public int CountDifferent(Pixels other)
    {
        int diff = 0;
        for (int i = 0; i < Rgba.Length; i += 4)
            if (Math.Abs(Rgba[i] - other.Rgba[i]) > 1 || Math.Abs(Rgba[i + 1] - other.Rgba[i + 1]) > 1 ||
                Math.Abs(Rgba[i + 2] - other.Rgba[i + 2]) > 1 || Math.Abs(Rgba[i + 3] - other.Rgba[i + 3]) > 1)
                diff++;
        return diff;
    }
}

public static class CanvasChecks
{
    /// <param name="concat">How to concatenate a transform; defaults to ICanvas.ConcatenateTransform.
    /// Lets a backend try a workaround for a broken ConcatenateTransform.</param>
    public static IReadOnlyList<(string Question, string Answer)> Run(
        ICanvasRasterizer r, Action<ICanvas, Matrix3x2>? concat = null)
    {
        concat ??= (c, m) => c.ConcatenateTransform(m);
        var results = new List<(string, string)>();
        void Check(string question, Func<string> answer)
        {
            try { results.Add((question, answer())); }
            catch (Exception e) { results.Add((question, $"THREW {e.GetType().Name}: {e.Message}")); }
        }

        Check("StrokeDashPattern [5,5] at StrokeSize 4: first dash length", () =>
        {
            var p = r.Render(200, 20, c =>
            {
                c.StrokeColor = Colors.Black;
                c.StrokeSize = 4;
                c.StrokeLineCap = LineCap.Butt;
                c.StrokeDashPattern = [5, 5];
                c.DrawLine(0, 10, 200, 10);
            });
            var run = FirstDarkRun(p, y: 10);
            var px = run * 1.0;
            return $"{px} px -> " + (Math.Abs(px - 20) <= 2 ? "MULTIPLIED by StrokeSize"
                : Math.Abs(px - 5) <= 2 ? "absolute" : "unexpected");
        });

        // Dash and gap lengths for several widths and patterns, to find each backend's scaling rule.
        foreach (var (size, pattern) in new (float, float[])[] { (1, [5, 5]), (2, [5, 5]), (4, [5, 5]), (4, [3, 7]), (8, [2, 1]) })
        {
            Check($"Dash [{string.Join(",", pattern)}] at StrokeSize {size}: dash, gap (px)", () =>
            {
                var p = r.Render(400, 20, c =>
                {
                    c.StrokeColor = Colors.Black;
                    c.StrokeSize = size;
                    c.StrokeLineCap = LineCap.Butt;
                    c.StrokeDashPattern = pattern;
                    c.DrawLine(0, 10, 400, 10);
                });
                var (dash, gap) = FirstDashAndGap(p, y: 10);
                return $"{dash}, {gap}  (pattern x size = {pattern[0] * size}, {pattern[1] * size})";
            });
        }

        foreach (var mode in new[] { WindingMode.NonZero, WindingMode.EvenOdd })
        {
            Check($"FillPath {mode}: hole / ring", () =>
            {
                var p = r.Render(100, 100, c => { c.FillColor = Colors.Black; c.FillPath(Ring(), mode); });
                return $"{p.Describe(50, 50)} / {p.Describe(20, 20)}";
            });
            Check($"ClipPath {mode}: hole / ring / outside", () =>
            {
                var p = r.Render(100, 100, c =>
                {
                    c.ClipPath(Ring(), mode);
                    c.FillColor = Colors.Black;
                    c.FillRectangle(0, 0, 100, 100);
                });
                return $"{p.Describe(50, 50)} / {p.Describe(20, 20)} / {p.Describe(5, 5)}";
            });
        }

        // Left half blue, right half green, in premultiplied BGRA memory order.
        var pixels = new uint[16];
        for (int y = 0; y < 4; y++) for (int x = 0; x < 4; x++) pixels[y * 4 + x] = x < 2 ? 0xFF0000FFu : 0xFF00FF00u;
        IImage? image = null;
        Check("IImage from PBGRA pixels, drawn: left / right (expect blue / green)", () =>
        {
            image = r.ImageFromPbgra(4, 4, pixels);
            var p = r.Render(40, 40, c => c.DrawImage(image, 0, 0, 40, 40));
            return $"{p.Describe(5, 20)} / {p.Describe(35, 20)}";
        });

        Check("ConcatenateTransform(scale 2): fills 0..20 square to 0..40", () =>
        {
            var p = r.Render(60, 60, c =>
            {
                concat(c, Matrix3x2.CreateScale(2));
                c.FillColor = Colors.Black;
                c.FillRectangle(0, 0, 20, 20);
            });
            return $"(30,30) {p.Describe(30, 30)} / (50,50) {p.Describe(50, 50)}";
        });

        // Filled bounding box of a 0..20 square under several transforms, as (minX,minY)-(maxX,maxY).
        foreach (var (label, matrix, expect) in new (string, Matrix3x2, string)[]
                 {
                     ("identity", Matrix3x2.Identity, "(0,0)-(19,19)"),
                     ("scale 2", Matrix3x2.CreateScale(2), "(0,0)-(39,39)"),
                     ("scale 2 then translate 10,5", Matrix3x2.CreateScale(2) * Matrix3x2.CreateTranslation(10, 5), "(10,5)-(49,44)"),
                     ("rotate 90 then translate 50,0", Matrix3x2.CreateRotation(MathF.PI / 2) * Matrix3x2.CreateTranslation(50, 0), "(30,0)-(49,19)"),
                 })
        {
            Check($"ConcatenateTransform({label}): filled box [expect {expect}]", () =>
            {
                var p = r.Render(100, 100, c =>
                {
                    concat(c, matrix);
                    c.FillColor = Colors.Black;
                    c.FillRectangle(0, 0, 20, 20);
                });
                return DarkBounds(p);
            });
        }
        Check("Two ConcatenateTransform calls (scale 2, then translate 5,5): filled box [expect (10,10)-(49,49)]", () =>
        {
            var p = r.Render(100, 100, c =>
            {
                concat(c, Matrix3x2.CreateScale(2));
                concat(c, Matrix3x2.CreateTranslation(5, 5)); // applied in user space: shifts by 10 px
                c.FillColor = Colors.Black;
                c.FillRectangle(0, 0, 20, 20);
            });
            return DarkBounds(p);
        });

        Check("ConcatenateTransform(skew): non-axis-aligned matrix", () =>
        {
            var p = r.Render(60, 60, c =>
            {
                concat(c, new Matrix3x2(1, 0, 0.5f, 1, 0, 0)); // x' = x + 0.5y
                c.FillColor = Colors.Black;
                c.FillRectangle(0, 0, 20, 40);
            });
            return $"(5,35) {p.Describe(5, 35)} [expect white] / (25,35) {p.Describe(25, 35)} [expect black]";
        });

        Check("PictureCanvas replay vs direct: differing pixels (clip even-odd, transform, image, dashes, curves)", () =>
        {
            void Busy(ICanvas c) => BusyScene(c, image, concat);
            var direct = r.Render(160, 160, Busy);
            var recorder = new PictureCanvas(0, 0, 160, 160);
            Busy(recorder);
            var picture = recorder.Picture;
            var replayed = r.Render(160, 160, c => picture.Draw(c));
            return $"{direct.CountDifferent(replayed)} of {160 * 160}";
        });

        return results;
    }

    private static PathF Ring()
    {
        var p = new PathF();
        p.AppendRectangle(10, 10, 80, 80);
        p.AppendRectangle(35, 35, 30, 30);
        return p;
    }

    private static void BusyScene(ICanvas c, IImage? image, Action<ICanvas, Matrix3x2> concat)
    {
        c.SaveState();
        concat(c, Matrix3x2.CreateScale(1.5f) * Matrix3x2.CreateTranslation(5, 5));
        c.ClipPath(Ring(), WindingMode.EvenOdd);
        c.FillColor = Colors.CornflowerBlue;
        c.FillRectangle(0, 0, 120, 120);
        if (image is not null) c.DrawImage(image, 20, 20, 40, 40);
        c.RestoreState();
        c.StrokeColor = Colors.DarkRed;
        c.StrokeSize = 3;
        c.StrokeDashPattern = [2, 1];
        c.DrawLine(0, 150, 160, 150);
        c.FillColor = Colors.Black;
        var tri = new PathF();
        tri.MoveTo(120, 10); tri.CurveTo(160, 10, 160, 60, 120, 60); tri.QuadTo(100, 35, 120, 10); tri.Close();
        c.FillPath(tri, WindingMode.EvenOdd);
    }

    private static string DarkBounds(Pixels p)
    {
        int minX = int.MaxValue, minY = int.MaxValue, maxX = -1, maxY = -1;
        for (int y = 0; y < p.Height; y++)
        for (int x = 0; x < p.Width; x++)
            if (p.At(x, y).R <= 128)
            {
                minX = Math.Min(minX, x); minY = Math.Min(minY, y);
                maxX = Math.Max(maxX, x); maxY = Math.Max(maxY, y);
            }
        return maxX < 0 ? "nothing filled" : $"({minX},{minY})-({maxX},{maxY})";
    }

    private static (int Dash, int Gap) FirstDashAndGap(Pixels p, int y)
    {
        int x = 0;
        while (x < p.Width && p.At(x, y).R > 128) x++;   // leading white (none expected)
        int dashStart = x;
        while (x < p.Width && p.At(x, y).R <= 128) x++;
        int gapStart = x;
        while (x < p.Width && p.At(x, y).R > 128) x++;
        return (gapStart - dashStart, x - gapStart);
    }

    private static int FirstDarkRun(Pixels p, int y)
    {
        int x = 0;
        while (x < p.Width && p.At(x, y).R > 128) x++;
        int start = x;
        while (x < p.Width && p.At(x, y).R <= 128) x++;
        return x - start;
    }
}
