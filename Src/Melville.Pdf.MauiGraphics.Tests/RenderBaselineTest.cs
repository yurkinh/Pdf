using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Melville.Pdf.ReferenceDocuments.Infrastructure;
using Xunit;

namespace Melville.Pdf.MauiGraphics.Tests;

/// <summary>
/// Exact regression baselines: a hash of every reference document as the Maui.Graphics renderer draws it on Skia.
/// Any change in output fails, however small; both target frameworks must match the same baseline, so net10.0
/// and net11.0 render identically. Skia's rasterization can differ between operating systems and processors,
/// so there is one baseline file per platform; without one for this platform the test is skipped.
/// Record or update with MELVILLE_RECORD_BASELINE=1 (then review the diff of the baseline file).
/// </summary>
public class RenderBaselineTest
{
    private const int Width = 400;

    private static readonly string BaselinePath = Path.Combine(
        AppContext.BaseDirectory, "..", "..", "..", "Baselines",
        $"maui-graphics-skia-{(OperatingSystem.IsWindows() ? "windows" : OperatingSystem.IsMacOS() ? "macos" : "linux")}" +
        $"-{RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant()}.tsv");

    private static readonly Lazy<Dictionary<string, string>> Baseline = new(() =>
        File.Exists(BaselinePath)
            ? File.ReadAllLines(BaselinePath).Where(l => l.Contains('\t'))
                .Select(l => l.Split('\t')).ToDictionary(p => p[0], p => p[1])
            : []);

    private static bool Recording => Environment.GetEnvironmentVariable("MELVILLE_RECORD_BASELINE") == "1";

    public static TheoryData<string> Documents() => ReferenceComparisonTest.Documents();

    [Theory]
    [MemberData(nameof(Documents))]
    public async Task MatchesTheBaseline(string prefix)
    {
        var hash = await HashAsync(GeneratorFactory.AllGenerators.Single(g => g.Prefix == prefix));
        if (Recording)
        {
            Record(prefix, hash);
            return;
        }
        if (!File.Exists(BaselinePath))
            Assert.Skip($"No render baseline for this platform ({Path.GetFileName(BaselinePath)}); " +
                        "record one with MELVILLE_RECORD_BASELINE=1.");
        Assert.True(Baseline.Value.TryGetValue(prefix, out var expected),
            $"{prefix} has no baseline entry; record with MELVILLE_RECORD_BASELINE=1.");
        Assert.True(expected == hash,
            $"{prefix} renders differently from the baseline. If the change is intended, re-record with " +
            "MELVILLE_RECORD_BASELINE=1 and review the images with DumpSideBySide.");
    }

    private static async Task<string> HashAsync(IPdfGenerator generator)
    {
        for (int attempt = 1; ; attempt++)
        {
            try
            {
                var raster = await Renders.MauiGraphicsAsync(generator, Width);
                return Convert.ToHexString(SHA256.HashData(raster.Rgba))[..32];
            }
            // Encrypted reference documents sometimes fail to read back (upstream bug, see ReferenceComparisonTest).
            catch (Exception e) when (attempt < 3 && generator.Prefix.StartsWith("-Encrypted") &&
                                      e.GetType().Name is "PdfParseException" or "PostscriptNamedErrorException")
            {
            }
        }
    }

    private static readonly Lock RecordLock = new();

    private static void Record(string prefix, string hash)
    {
        lock (RecordLock)
        {
            var path = Path.GetFullPath(BaselinePath);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var entries = File.Exists(path)
                ? File.ReadAllLines(path).Where(l => l.Contains('\t')).Select(l => l.Split('\t'))
                    .ToDictionary(p => p[0], p => p[1])
                : new Dictionary<string, string>();
            entries[prefix] = hash;
            File.WriteAllLines(path, entries.OrderBy(e => e.Key, StringComparer.Ordinal).Select(e => $"{e.Key}\t{e.Value}"));
        }
    }
}
