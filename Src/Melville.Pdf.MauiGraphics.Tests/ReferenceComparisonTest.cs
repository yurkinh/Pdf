using System.Text;
using Melville.Pdf.ReferenceDocuments.Infrastructure;
using Xunit;

namespace Melville.Pdf.MauiGraphics.Tests;

/// <summary>
/// Renders every reference document with the Maui.Graphics renderer (replayed on Skia) and with the existing
/// SkiaSharp renderer, and reports how much they differ. Both rasterize with Skia, so differences come from the
/// port, not from the rasterizer.
/// </summary>
public class ReferenceComparisonTest(ITestOutputHelper output)
{
    private const int Width = 400;

    public static TheoryData<string> Documents() =>
        new(GeneratorFactory.AllGenerators.Select(g => g.Prefix).OrderBy(p => p, StringComparer.Ordinal));

    /// <summary>Supersampled differences above this fail; phase 3 measured at most 1.2% outside the list below.</summary>
    private const double Tolerance = 0.02;

    // Documents where the reference is known to be wrong, with the expected difference.
    private static readonly Dictionary<string, string> KnownReferenceErrors = new()
    {
        // Text render modes that add glyphs to the clip: the SkiaSharp renderer clips to an empty path
        // (its path builder is detached by the paint before the clip), so the clipped fill vanishes.
        ["-StrokeAndClip"] = "SkiaSharp text clip is empty",
        ["-StrokeFillAndClip"] = "SkiaSharp text clip is empty",
        ["-FillAndClip"] = "SkiaSharp text clip is empty",
    };

    [Theory]
    [MemberData(nameof(Documents))]
    public async Task MatchesTheSkiaSharpRenderer(string prefix)
    {
        var generator = GeneratorFactory.AllGenerators.Single(g => g.Prefix == prefix);
        var difference = await CompareWithRetryForEncryptedAsync(generator);
        if (KnownReferenceErrors.TryGetValue(prefix, out var reason))
        {
            Assert.True(difference > 0.001, $"{prefix} now matches the reference; remove it from the known list ({reason})");
            return;
        }
        Assert.True(difference <= Tolerance, $"{prefix}: {difference:P2} of pixels differ (tolerance {Tolerance:P0})");
    }

    // Upstream issue, not the renderer: encrypted reference documents use random keys, and about 0.3-1.3% of them
    // cannot be read back (PdfParseException "Unknown token" or a PostScript stack overflow while parsing the
    // encryption dictionary). Regenerate those few; any other failure, or a renderer exception, still fails.
    private static async Task<double> CompareWithRetryForEncryptedAsync(IPdfGenerator generator)
    {
        for (int attempt = 1; ; attempt++)
        {
            try
            {
                return (await Renders.CompareAsync(generator, Width)).Fraction;
            }
            catch (Exception e) when (attempt < 3 && generator.Prefix.StartsWith("-Encrypted") &&
                                      e.GetType().Name is "PdfParseException" or "PostscriptNamedErrorException")
            {
            }
        }
    }

    [Fact(Explicit = true)]
    public async Task ReportDifferencesForAllDocuments()
    {
        var report = new StringBuilder();
        var rows = new List<(string Name, string Result, double Diff)>();
        foreach (var generator in GeneratorFactory.AllGenerators.OrderBy(g => g.Prefix, StringComparer.Ordinal))
        {
            try
            {
                var (diff, _, _) = await Renders.CompareAsync(generator, Width);
                rows.Add((generator.Prefix, $"{diff:P2}", diff));
            }
            catch (Exception e)
            {
                rows.Add((generator.Prefix, $"EXCEPTION {e.GetType().Name}: {e.Message.ReplaceLineEndings(" ")}", 2));
            }
        }
        foreach (var (name, result, _) in rows.OrderByDescending(r => r.Diff))
            report.AppendLine($"{name}\t{result}");
        var path = Path.Combine(Path.GetTempPath(), "maui-vs-skia.tsv");
        await File.WriteAllTextAsync(path, report.ToString(), TestContext.Current.CancellationToken);
        output.WriteLine($"Report: {path}");
        output.WriteLine(report.ToString());
    }
}
