using System.IO;
using System.Text.RegularExpressions;
using ReadArticles.Models;
using ReadArticles.Infrastructure;
namespace ReadArticles.Services;
public sealed class ReaderWorkspace
{
    public LoadedArticle? Article { get; private set; }
    public ScriptArticle? Script { get; private set; }
    public bool IsScriptMode { get; private set; }

    public void ClearArticle()
    {
        Article = null;
        ResetReadingMode();
    }

    public void CommitArticle(LoadedArticle prepared)
    {
        Article = prepared;
        ResetReadingMode();
    }

    private void ResetReadingMode()
    {
        Script = null;
        IsScriptMode = false;
    }

    // Preparation does not change the committed mode or cache. A missing
    // companion returns null; malformed/unreadable data still throws.
    public ScriptArticle? PrepareScript()
    {
        var article = Article ?? throw new InvalidOperationException("请先打开文章。");
        if (Script is not null) return Script;
        return File.Exists(ResourcePaths.RejectReparsePoints(ArticleRepository.ScriptPathFor(article.FilePath)))
            ? new ArticleRepository().LoadScript(article.FilePath)
            : null;
    }

    public void CommitReadingMode(bool isScript, ScriptArticle? script)
    {
        if (Article is null) throw new InvalidOperationException("请先打开文章。");
        if (isScript) Script = script ?? throw new ArgumentNullException(nameof(script));
        IsScriptMode = isScript;
    }

    public bool ValidateSelection(bool isScript, int paragraphIndex, int sentenceIndex,
        int offset, int length, string? candidate)
    {
        if (isScript != IsScriptMode || paragraphIndex < 0 || sentenceIndex < 0 ||
            offset < 0 || length <= 0 || length > 2048 ||
            string.IsNullOrWhiteSpace(candidate) || candidate.Length > 2048)
            return false;
        string original;
        if (isScript)
        {
            if (Script is null || paragraphIndex >= Script.Paragraphs.Count ||
                sentenceIndex >= Script.Paragraphs[paragraphIndex].Sentences.Count)
                return false;
            original = Script.Paragraphs[paragraphIndex].Sentences[sentenceIndex].Text;
        }
        else
        {
            if (Article is null || paragraphIndex >= Article.Paragraphs.Count ||
                sentenceIndex >= Article.Paragraphs[paragraphIndex].Length)
                return false;
            original = Regex.Replace(Article.Paragraphs[paragraphIndex][sentenceIndex], @"\[\[(\d+)\]\]",
                match => "____" + match.Groups[1].Value + "____");
        }
        if (offset > original.Length || length > original.Length - offset) return false;
        string normalized = Regex.Replace(original.Substring(offset, length), @"\s+", " ").Trim();
        return string.Equals(candidate, normalized, StringComparison.Ordinal);
    }
    public void CommitVocabulary(VocabularySnapshot saved)
    {
        var article = Article ?? throw new InvalidOperationException("请先打开文章。");
        Article = article with { Vocabulary = saved };
    }
}
