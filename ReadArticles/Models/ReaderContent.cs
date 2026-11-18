using System.Text.Json.Serialization;
namespace ReadArticles.Models;

public sealed record Segment(
    [property: JsonPropertyName("sid")] string Sid,
    [property: JsonPropertyName("text")] string Text,
    [property: JsonIgnore] bool HasAudio = true);
// This offset refers to rendered text; blank placeholders have a different display length.
public sealed record PassageSelection(string Sid, string Text, int RenderedOffset, int Length);
