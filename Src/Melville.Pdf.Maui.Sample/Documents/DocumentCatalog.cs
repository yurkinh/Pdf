using System.Text.Json;
using System.Text.Json.Serialization;
using Melville.Pdf.Maui.Controls;
using Melville.Pdf.ReferenceDocuments.Infrastructure;

namespace Melville.Pdf.Maui.Sample.Documents;

/// <summary>A document the sample can open: bundled, generated or picked by the user.</summary>
/// <param name="Title">Shown in lists and as the viewer title.</param>
/// <param name="Description">One line about what the document exercises.</param>
/// <param name="Password">The document's password, if known.</param>
/// <param name="Source">Where the bytes come from.</param>
/// <param name="AskForPassword">The viewer asks the user even though the password is known (the bundled
/// encrypted sample demonstrates the prompt; diagnostics still open it unattended).</param>
/// <param name="Key">Identifies the document across launches, for saving the reading position; null for files the
/// user picked, which cannot be reopened later (an Android content URI's permission does not survive a restart).</param>
public sealed record CatalogDocument(string Title, string Description, string? Password, PdfSource Source,
    bool AskForPassword = false, string? Key = null)
{
    /// <summary>Pickers show this.</summary>
    public override string ToString() => Title;

    /// <summary>
    /// What a screen reader says for a tappable row. A description on a layout makes the row one accessible element
    /// (on iOS its children become unreachable), so it carries both lines.
    /// </summary>
    public string AccessibleName => $"{Title}. {Description}";
}

/// <summary>
/// The bundled PDFs (Resources/Raw/samples, listed in index.json, written by tools/BuildSampleCorpus.cs) and the
/// Melville.Pdf reference documents, generated in memory on demand.
/// </summary>
public sealed class DocumentCatalog
{
    private const string Folder = "samples";
    private Task<IReadOnlyList<CatalogDocument>>? samples;

    /// <summary>The bundled sample documents, read once from the app package.</summary>
    public Task<IReadOnlyList<CatalogDocument>> GetSamplesAsync() => samples ??= LoadSamplesAsync();

    /// <summary>Find a bundled or reference document by its <see cref="CatalogDocument.Key"/>.</summary>
    public async Task<CatalogDocument?> FindAsync(string key) =>
        (await GetSamplesAsync()).FirstOrDefault(d => d.Key == key) ?? References.FirstOrDefault(d => d.Key == key);

    /// <summary>All reference documents, by name.</summary>
    public IReadOnlyList<CatalogDocument> References { get; } = GeneratorFactory.AllGenerators
        .OrderBy(g => g.Prefix, StringComparer.Ordinal)
        .Select(FromGenerator)
        .ToList();

    private static async Task<IReadOnlyList<CatalogDocument>> LoadSamplesAsync()
    {
        await using var stream = await FileSystem.Current.OpenAppPackageFileAsync($"{Folder}/index.json");
        var entries = await JsonSerializer.DeserializeAsync(stream, SampleIndexContext.Default.ListSampleEntry) ?? [];
        // The password is in the title for the user to type; the viewer asks for it.
        return entries.Select(e => new CatalogDocument(e.Title, e.Description, e.Password,
            PdfSource.FromAppPackage($"{Folder}/{e.File}"), AskForPassword: true, Key: $"sample:{e.File}")).ToList();
    }

    private static CatalogDocument FromGenerator(IPdfGenerator generator) => new(
        generator.Prefix.TrimStart('-'),
        generator.HelpText,
        generator.Password.Length > 0 ? generator.Password : null,
        PdfSource.FromStream(async _ =>
        {
            var bytes = new MemoryStream();
            await generator.WritePdfAsync(bytes);
            bytes.Position = 0;
            return bytes;
        }),
        Key: $"reference:{generator.Prefix}");
}

internal sealed record SampleEntry(string File, string Title, string Description, string? Password);

// Source-generated metadata: no reflection, so the index still loads in trimmed and AOT builds.
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(List<SampleEntry>))]
internal sealed partial class SampleIndexContext : JsonSerializerContext;
