using System.IO;

namespace ReadArticles.Models;

public sealed record LoadedArticle(string FilePath, string Title, IReadOnlyList<Segment[]> Paragraphs,
    string VocabularyPath, VocabularySnapshot Vocabulary, IReadOnlyList<string> Warnings)
{
    public string Directory => Path.GetDirectoryName(FilePath)!;
    public IEnumerable<Segment> Segments
    {
        get
        {
            foreach (var paragraph in Paragraphs)
            {
                foreach (var segment in paragraph) yield return segment;
            }
        }
    }
    public bool IsBlank => Segments.All(s => !s.HasAudio);
}
