using System.Numerics;
using FluentAssertions;
using Melville.Pdf.MauiGraphics.Drawing;
using Microsoft.Maui.Graphics;
using Xunit;

namespace Melville.Pdf.MauiGraphics.Tests;

public class DashPatternTest
{
    [Fact]
    public void PatternsAreInMultiplesOfTheStrokeWidth() =>
        MauiCanvasAdapter.DashPatternFor([10, 5], 5, DashBehavior.Exact).Should().Equal(2, 1);

    [Fact]
    public void NativeBackendsGetOneWidthAddedToEachDash() =>
        // CoreGraphics and Android draw dashes one width short (phase 1 spike), gaps as given.
        MauiCanvasAdapter.DashPatternFor([10, 5, 20, 5], 5, DashBehavior.NativeShortened).Should().Equal(3, 1, 5, 1);

    [Fact]
    public void ZeroPhaseKeepsThePattern() =>
        MauiCanvasAdapter.StartAtPhase([50, 20, 10, 20], 0).Should().Equal(50, 20, 10, 20);

    [Fact]
    public void PhaseInsideADashShortensItAndAppendsTheSkippedPart() =>
        // 20 into [50 dash, 20 gap, 10 dash, 20 gap]: 30 of the first dash remain; the 20 skipped close the period.
        MauiCanvasAdapter.StartAtPhase([50, 20, 10, 20], 20).Should().Equal(30, 20, 10, 20, 20, 0);

    [Fact]
    public void PhaseInsideAGapStartsWithAnEmptyDash() =>
        // 60 lands 10 into the first gap.
        MauiCanvasAdapter.StartAtPhase([50, 20, 10, 20], 60).Should().Equal(0, 10, 10, 20, 50, 10);

    [Theory]
    [InlineData(20)]
    [InlineData(60)]
    [InlineData(95)]
    [InlineData(-40)]
    [InlineData(1000)]
    public void RotationKeepsThePeriodAndAnEvenLength(float phase)
    {
        var rotated = MauiCanvasAdapter.StartAtPhase([50, 20, 10, 20], phase);
        rotated.Sum().Should().BeApproximately(100, 0.001f);
        (rotated.Length % 2).Should().Be(0);
    }

    [Theory]
    [InlineData(20)]
    [InlineData(60)]
    [InlineData(95)]
    public void RotationDrawsTheSameLineAsThePhase(float phase)
    {
        float[] dashes = [50, 20, 10, 20];
        var rotated = MauiCanvasAdapter.StartAtPhase(dashes, phase);
        for (float x = 0.5f; x < 300; x += 1)
            IsOn(rotated, x, 0).Should().Be(IsOn(dashes, x, phase), $"at {x}");
    }

    private static bool IsOn(float[] dashes, float x, float phase)
    {
        var position = (x + phase) % dashes.Sum();
        for (int i = 0; i < dashes.Length; i++)
        {
            if (position < dashes[i]) return i % 2 == 0;
            position -= dashes[i];
        }
        return false;
    }
}

public class DisplayListTest
{
    [Fact]
    public void ExtraRestoresAreIgnoredAndOpenSavesClosed()
    {
        var recorder = new DisplayListRecorder();
        recorder.RestoreState();           // the graphics state stack pops its base state on dispose
        recorder.SaveState();
        recorder.SaveState();
        var list = recorder.ToDisplayList(10, 10, 1);

        var probe = new CountingCanvas();
        list.Replay(probe);
        probe.Saves.Should().Be(2);
        probe.Restores.Should().Be(2);
    }

    [Fact]
    public void ReplayRepeatsTheRecordingExactly()
    {
        var recorder = new DisplayListRecorder();
        var path = new PathF();
        path.AppendRectangle(1, 2, 3, 4);
        recorder.SaveState();
        recorder.ConcatenateTransform(Matrix3x2.CreateScale(2));
        recorder.FillPath(path, WindingMode.EvenOdd, new SolidFill(Colors.Red));
        recorder.RestoreState();
        var list = recorder.ToDisplayList(10, 20, 0.5f);

        list.Count.Should().Be(4);
        list.FillCount.Should().Be(1);
        (list.PageWidth, list.PageHeight).Should().Be((5, 10));
        var first = new CountingCanvas();
        var second = new CountingCanvas();
        list.Replay(first);
        list.Replay(second);
        second.Log.Should().Equal(first.Log);
        first.Log.Should().Equal("save", "concat", "fill EvenOdd", "restore");
    }

    private sealed class CountingCanvas : IPdfCanvas
    {
        public int Saves;
        public int Restores;
        public List<string> Log { get; } = [];
        public void SaveState() { Saves++; Log.Add("save"); }
        public void RestoreState() { Restores++; Log.Add("restore"); }
        public void ConcatenateTransform(in Matrix3x2 transform) => Log.Add("concat");
        public void FillPath(PathF path, WindingMode winding, PdfFill fill) => Log.Add($"fill {winding}");
        public void StrokePath(PathF path, PdfStroke stroke) => Log.Add("stroke");
        public void ClipPath(PathF path, WindingMode winding) => Log.Add($"clip {winding}");
        public void DrawImage(PdfImage image, RectF destination) => Log.Add("image");
    }
}

public class PdfImageTest
{
    private static PdfImage TwoByOne() => new(2, 1, [0, 0, 0, 255, 255, 255, 255, 255]); // black, white

    [Fact]
    public void NearestResizeKeepsHardEdges()
    {
        var resized = TwoByOne().Resize(4, 1, bilinear: false);
        resized.Pbgra.Where((_, i) => i % 4 == 0).Should().Equal(0, 0, 255, 255);
    }

    [Fact]
    public void BilinearResizeBlends()
    {
        var resized = TwoByOne().Resize(4, 1, bilinear: true);
        var blue = resized.Pbgra.Where((_, i) => i % 4 == 0).ToArray();
        blue.First().Should().Be(0);
        blue.Last().Should().Be(255);
        blue[1].Should().BeInRange(1, 254, "the pixels next to the edge are blended");
    }

    [Fact]
    public void ShrinkAveragesBlocks()
    {
        var shrunk = TwoByOne().Shrink(1, 1);
        shrunk.Pbgra.Should().Equal(127, 127, 127, 255);
    }

    [Fact]
    public void AverageColorIgnoresTransparentPixels()
    {
        var image = new PdfImage(2, 1, [0, 0, 255, 255, 0, 0, 0, 0]); // opaque red, transparent
        var average = image.AverageColor();
        (average.Red, average.Green, average.Blue).Should().Be((1f, 0f, 0f));
        average.Alpha.Should().BeApproximately(0.5f, 0.01f);
    }
}
