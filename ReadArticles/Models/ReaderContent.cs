using System.Text.Json.Serialization;
namespace ReadArticles.Models;

public sealed record Segment(
    [property: JsonPropertyName("sid")] string Sid,
    [property: JsonPropertyName("text")] string Text,
    [property: JsonIgnore] bool HasAudio = true);
