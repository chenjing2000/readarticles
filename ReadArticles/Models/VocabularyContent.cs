using System.Text.Json;
using System.Text.Json.Serialization;

namespace ReadArticles.Models;

public sealed record Meaning([property: JsonPropertyName("pos")] string Pos, [property: JsonPropertyName("meaning")] string Text)
{
    [JsonExtensionData] public Dictionary<string, JsonElement>? Extra { get; init; }
}

public sealed class VocabularyEntry
{
    [JsonPropertyName("word")] public string Word { get; init; } = "";
    [JsonPropertyName("phonetic_uk")] public string PhoneticUk { get; init; } = "";
    [JsonPropertyName("phonetic_us")] public string PhoneticUs { get; init; } = "";
    [JsonPropertyName("meanings")] public List<Meaning> Meanings { get; init; } = new List<Meaning>();
    [JsonExtensionData] public Dictionary<string, JsonElement>? Extra { get; init; }
}

public sealed class VocabularyData
{
    [JsonPropertyName("filetype")] public string FileType { get; init; } = "vocabulary";
    [JsonPropertyName("words")] public List<VocabularyEntry> Words { get; init; } = new List<VocabularyEntry>();
    [JsonExtensionData] public Dictionary<string, JsonElement>? Extra { get; init; }
}

public sealed record VocabularySnapshot(VocabularyData Data, string? Fingerprint, bool CanWrite, string? Warning = null);
