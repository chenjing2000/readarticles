using System.IO;

namespace ReadArticles.Models;

public sealed record LoadedArticle(string FilePath, string Title, IReadOnlyList<string[]> Paragraphs,
    string VocabularyPath, VocabularySnapshot Vocabulary, IReadOnlyList<string> Warnings)
{
    public string Directory => Path.GetDirectoryName(FilePath)!;
}
