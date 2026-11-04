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

public class RenderDeterminismTest
{
    /// <summary>Render MELVILLE_DUMP 300 times; write the distinct output hashes and save one odd render.</summary>
    [Fact(Explicit = true)]
    public async Task CountDistinctRenders()
    {
        var name = Environment.GetEnvironmentVariable("MELVILLE_DUMP") ?? "-FiltersGenerator";
        var folder = Environment.GetEnvironmentVariable("MELVILLE_DUMP_DIR") ?? Path.GetTempPath();
        var generator = GeneratorFactory.AllGenerators.Single(g => g.Prefix == name);
        var counts = new System.Collections.Concurrent.ConcurrentDictionary<string, int>();
        var samples = new System.Collections.Concurrent.ConcurrentDictionary<string, Raster>();
        var parallel = int.TryParse(Environment.GetEnvironmentVariable("MELVILLE_PARALLEL"), out var p) ? p : 1;
        // Each round starts `parallel` renders of the same document at once (fresh documents, shared statics).
        for (int round = 0; round < 300 / parallel; round++)
        {
            await Task.WhenAll(Enumerable.Range(0, parallel).Select(_ => Task.Run(async () =>
            {
                string key;
                try
                {
                    var raster = Environment.GetEnvironmentVariable("MELVILLE_RENDERER") == "skiasharp"
                        ? await Renders.SkiaSharpAsync(generator, 400, 240)
                        : await Renders.MauiGraphicsAsync(generator, 400);
                    key = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(raster.Rgba))[..16];
                    samples.TryAdd(key, raster);
                }
                catch (Exception e) { key = "EXCEPTION " + e.GetType().Name + ": " + e.Message[..Math.Min(60, e.Message.Length)]; }
                counts.AddOrUpdate(key, 1, (_, c) => c + 1);
            })));
        }
        await File.WriteAllLinesAsync(Path.Combine(folder, "determinism.txt"),
            counts.OrderByDescending(c => c.Value).Select(c => $"{c.Value}\t{c.Key}"), TestContext.Current.CancellationToken);
        var ordered = samples.OrderByDescending(s => counts[s.Key]).Select(s => s.Value).ToList();
        if (ordered.Count > 1) DumpRendersTest.WritePair(Path.Combine(folder, "determinism-usual-vs-odd.png"), ordered[0], ordered[1]);
    }
}

public class ConcurrentRenderingTest
{
    /// <summary>
    /// Render every document serially, then 3 more times each, 12 at a time in shuffled order (different documents
    /// at once, sharing static caches), and write every concurrent result that differs from its serial one.
    /// </summary>
    [Fact(Explicit = true)]
    public async Task AllDocumentsConcurrently()
    {
        var folder = Environment.GetEnvironmentVariable("MELVILLE_DUMP_DIR") ?? Path.GetTempPath();
        var skiaSharp = Environment.GetEnvironmentVariable("MELVILLE_RENDERER") == "skiasharp";
        var generators = GeneratorFactory.AllGenerators.ToList();
        async Task<string> Hash(IPdfGenerator g)
        {
            for (int attempt = 1; ; attempt++)
            {
                try
                {
                    var raster = skiaSharp ? await Renders.SkiaSharpAsync(g, 400, 300) : await Renders.MauiGraphicsAsync(g, 400);
                    return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(raster.Rgba))[..16];
                }
                catch (Exception) when (attempt < 3 && g.Prefix.StartsWith("-Encrypted")) { }
                catch (Exception e) { return "EXCEPTION " + e.GetType().Name; }
            }
        }
        var serial = new Dictionary<string, string>();
        foreach (var g in generators) serial[g.Prefix] = await Hash(g);

        var work = Enumerable.Range(0, 3).SelectMany(_ => generators).OrderBy(_ => Random.Shared.Next()).ToList();
        var differences = new System.Collections.Concurrent.ConcurrentBag<string>();
        await Parallel.ForEachAsync(work, new ParallelOptions { MaxDegreeOfParallelism = 12 }, async (g, _) =>
        {
            var hash = await Hash(g);
            if (hash != serial[g.Prefix]) differences.Add($"{g.Prefix}\t{serial[g.Prefix]} -> {hash}");
        });
        await File.WriteAllLinesAsync(Path.Combine(folder, "concurrent.txt"),
            [$"renders: {work.Count}, differing: {differences.Count}", .. differences.Order()], TestContext.Current.CancellationToken);
    }
}
