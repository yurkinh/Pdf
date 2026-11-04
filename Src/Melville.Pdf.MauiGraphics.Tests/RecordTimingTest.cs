using System.Diagnostics;
using System.Numerics;
using Melville.Pdf.MauiGraphics.Drawing;
using Melville.Pdf.ReferenceDocuments.Infrastructure;
using Melville.Pdf.SkiaSharp;
using Microsoft.Maui.Graphics;
using Xunit;

namespace Melville.Pdf.MauiGraphics.Tests;

/// <summary>Where recording time goes, for one document (MELVILLE_DUMP) — writes timings to MELVILLE_DUMP_DIR.</summary>
public class RecordTimingTest
{
    [Fact(Explicit = true)]
    public async Task TimeRecording()
    {
        var name = Environment.GetEnvironmentVariable("MELVILLE_DUMP") ?? "-StandardFonts";
        var generator = GeneratorFactory.AllGenerators.Single(g => g.Prefix == name);
        using var document = await Renders.OpenAsync(generator);
        const int runs = 200;
        async Task<double> Time(Func<Task> action)
        {
            for (int i = 0; i < 10; i++) await action();
            var watch = Stopwatch.StartNew();
            for (int i = 0; i < runs; i++) await action();
            return watch.Elapsed.TotalMicroseconds / runs;
        }
        var skia = await Time(async () => { using var s = await RenderWithSkia.ToSurfaceAsync(document, 1, 1024, -1); });
        var record = await Time(async () => await RenderWithMauiGraphics.RecordPageAsync(document, 1));
        var list = await RenderWithMauiGraphics.RecordPageAsync(document, 1);
        var lines = $"{name}: skia {skia:F0} us, record {record:F0} us, ops {list.Count}, fills {list.FillCount}\n";
        var folder = Environment.GetEnvironmentVariable("MELVILLE_DUMP_DIR") ?? Path.GetTempPath();
        await File.AppendAllTextAsync(Path.Combine(folder, "timing.txt"), lines, TestContext.Current.CancellationToken);
    }
}

public class EncryptedGeneratorStabilityTest
{
    /// <summary>Generate and open each encrypted reference document many times; writes failure counts.</summary>
    [Fact(Explicit = true)]
    public async Task OpenEncryptedDocumentsRepeatedly()
    {
        var folder = Environment.GetEnvironmentVariable("MELVILLE_DUMP_DIR") ?? Path.GetTempPath();
        var lines = new List<string>();
        foreach (var generator in GeneratorFactory.AllGenerators.Where(g => g.Prefix.StartsWith("-Encrypted")).OrderBy(g => g.Prefix))
        {
            int failures = 0;
            string? sample = null;
            for (int i = 0; i < 300; i++)
            {
                try { using var _ = await Renders.OpenAsync(generator); }
                catch (Exception e) { failures++; sample ??= e.GetType().Name + ": " + e.Message[..Math.Min(40, e.Message.Length)]; }
            }
            lines.Add($"{generator.Prefix}\t{failures}/300\t{sample}");
        }
        await File.WriteAllLinesAsync(Path.Combine(folder, "encrypted.txt"), lines, TestContext.Current.CancellationToken);
    }
}
