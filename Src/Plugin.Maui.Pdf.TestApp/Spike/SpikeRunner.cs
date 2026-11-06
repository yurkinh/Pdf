using System.Diagnostics;
using MauiGraphicsSpike;
using Melville.Parsing.Streams;
using Melville.Pdf.FontLibrary;
using Melville.Pdf.LowLevel.Parsing.ParserContext;
using Melville.Pdf.Model;
using Melville.Pdf.ReferenceDocuments.Infrastructure;
using Melville.Pdf.SkiaSharp;

namespace Plugin.Maui.Pdf.TestApp.Spike;

public sealed record SpikeLine(string Question, string Answer);

/// <summary>
/// Phase 1 device checks. Each result is also written to the console with a [SPIKE] marker so it can be
/// collected from simulator/emulator logs.
/// </summary>
public static class SpikeRunner
{
    // The same documents the runtime benchmarks use: text, images, patterns and shadings.
    public static readonly string[] Documents =
    [
        "-StandardFonts", "-EmbeddedTrueType", "-JpegImage", "-JBigSampleBitStream1",
        "-Jpeg2000ColorImage", "-ColoredTilePattern", "-Type2LinearShader"
    ];

    public static IReadOnlyList<SpikeLine> RunCanvasChecks()
    {
        var rasterizer = new NativeRasterizer();
        var lines = new List<SpikeLine> { new("Backend", rasterizer.Name) };
        using (var probe = new Microsoft.Maui.Graphics.Platform.PlatformBitmapExportService().CreateContext(4, 4, 1))
        {
            var type = probe.Canvas.GetType();
            var chain = type.FullName + " : " + type.BaseType?.FullName;
            lines.Add(new("Export context canvas type", chain));
        }
        lines.AddRange(CanvasChecks.Run(rasterizer).Select(r => new SpikeLine(r.Question, r.Answer)));
#if ANDROID
        // Same checks with the native-canvas transform workaround.
        lines.AddRange(CanvasChecks.Run(rasterizer, AndroidTransformFix.Concat)
            .Where(r => r.Question.Contains("Transform") || r.Question.Contains("PictureCanvas"))
            .Select(r => new SpikeLine("[android fix] " + r.Question, r.Answer)));
#endif
        Log(lines);
        return lines;
    }

    /// <summary>Renders page 1 of each document with the existing Skia renderer: proves the net10.0 core runs here.</summary>
    public static async Task<(IReadOnlyList<SpikeLine> Lines, byte[]? FirstPng)> RunCoreChecksAsync()
    {
        var lines = new List<SpikeLine> { new("Runtime", $"{System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription}, {DeviceInfo.Platform} {DeviceInfo.VersionString}") };
        byte[]? firstPng = null;
        var generators = KnownGenerators.All.ToDictionary(g => g.Prefix);
        foreach (var name in Documents)
        {
            try
            {
                var generator = generators[name];
                var source = WritableBuffer.Create();
                await using (var writer = source.WritingStream()) await generator.WritePdfAsync(writer);
                var watch = Stopwatch.StartNew();
                using var doc = await new PdfReader(new ConstantPasswordSource(PasswordType.User, generator.Password),
                    SelfContainedDefaultFonts.Instance).ReadFromAsync(source);
                using var png = new MemoryStream();
                await RenderWithSkia.ToPngStreamAsync(doc, 1, png, -1, 512);
                lines.Add(new($"Render {name}", $"ok, {watch.ElapsedMilliseconds} ms, {png.Length} bytes"));
                firstPng ??= png.ToArray();
            }
            catch (Exception e)
            {
                lines.Add(new($"Render {name}", $"THREW {e.GetType().Name}: {e.Message}"));
            }
        }
        Log(lines);
        return (lines, firstPng);
    }

    private static void Log(IEnumerable<SpikeLine> lines)
    {
        foreach (var line in lines) Console.WriteLine($"[SPIKE] {line.Question} => {line.Answer}");
    }
}
