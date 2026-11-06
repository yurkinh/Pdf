using System.Text.Json;
using System.Text.Json.Serialization;
using Melville.Pdf.ViewerModel;

namespace Melville.Pdf.Maui.Sample.Documents;

/// <summary>Where the user was in a document, as saved.</summary>
public sealed record ReadingPosition(string Key, string Title, PdfViewState State, DateTimeOffset SavedAt);

/// <summary>
/// The last reading position of recently opened documents, in <see cref="IPreferences"/>. Saving is a small
/// synchronous write, fast enough for a lifecycle handler (Stopped, Destroying).
/// </summary>
public sealed class ReadingPositions(IPreferences preferences)
{
    private const string PreferenceKey = "reading-positions";
    private const int MaxDocuments = 50;
    private List<ReadingPosition>? positions;

    /// <summary>
    /// Raised after a save. On a pop the page underneath appears before the viewer has saved, so pages that show
    /// positions listen for this rather than reading only when they appear.
    /// </summary>
    public event EventHandler? Changed;

    /// <summary>The most recently saved position, if any.</summary>
    public ReadingPosition? Latest => Load().FirstOrDefault();

    /// <summary>The saved position for a document, if any.</summary>
    public ReadingPosition? Find(string key) => Load().FirstOrDefault(p => p.Key == key);

    /// <summary>Save a position; it becomes <see cref="Latest"/>.</summary>
    public void Save(string key, string title, PdfViewState state)
    {
        var list = Load();
        list.RemoveAll(p => p.Key == key);
        list.Insert(0, new ReadingPosition(key, title, state, DateTimeOffset.UtcNow));
        if (list.Count > MaxDocuments) list.RemoveRange(MaxDocuments, list.Count - MaxDocuments);
        preferences.Set(PreferenceKey, JsonSerializer.Serialize(list, ReadingPositionContext.Default.ListReadingPosition));
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private List<ReadingPosition> Load()
    {
        if (positions is not null) return positions;
        try
        {
            var json = preferences.Get(PreferenceKey, "");
            positions = json.Length == 0 ? [] : JsonSerializer.Deserialize(json, ReadingPositionContext.Default.ListReadingPosition) ?? [];
        }
        catch (JsonException)
        {
            positions = []; // written by an older build: start again rather than fail
        }
        return positions;
    }
}

[JsonSourceGenerationOptions(UseStringEnumConverter = true)]
[JsonSerializable(typeof(List<ReadingPosition>))]
internal sealed partial class ReadingPositionContext : JsonSerializerContext;
