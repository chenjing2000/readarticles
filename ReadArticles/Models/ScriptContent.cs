namespace ReadArticles.Models;

// Script is a companion view, not another playable article or a Passage SID source.
public sealed record ScriptArticle(string Title, IReadOnlyList<ScriptParagraph> Paragraphs);
public sealed record ScriptParagraph(IReadOnlyList<ScriptSentence> Sentences);
public sealed record ScriptSentence(string Text, IReadOnlyList<ScriptTimedUnit> TimedUnits);

// Offset is a best-effort rendering position within the original sentence.
// Failed alignment does not invalidate an otherwise well-formed Script JSON.
public sealed record ScriptTimedUnit(string Text, double? Start, double? End, int Offset)
{
    public int Length => Text.Length;
    public bool CanColor => Offset >= 0 && Start.HasValue && End.HasValue;

    public double Progress(double seconds)
    {
        if (!CanColor || !double.IsFinite(seconds)) return 0;
        double start = Start.GetValueOrDefault(), end = End.GetValueOrDefault();
        return Math.Clamp((seconds - start) / (end - start), 0, 1);
    }
}
