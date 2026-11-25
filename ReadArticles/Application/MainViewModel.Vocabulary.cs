using ReadArticles.Infrastructure;
using ReadArticles.Vocabulary;
using ReadArticles.Models;
using ReadArticles.Services;

namespace ReadArticles.ViewModels;

public enum VocabularyEditResult { Success, Duplicate, Unavailable, Failed }

public sealed partial class MainViewModel
{
    private void RefreshWords()
    {
        Words.Clear();
        if (Article is null) return;
        foreach (var word in PrepareWords(Article)) Words.Add(word);
    }

    private List<VocabularyEntryViewModel> PrepareWords(LoadedArticle article)
    {
        var words = new List<VocabularyEntryViewModel>();
        var stems = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in article.Vocabulary.Data.Words)
        {
            string stem = AudioResources.AudioStem(entry.Word);
            bool ownsAudio = stem.Length > 0 && stems.Add(stem);
            bool uk = ownsAudio && playback.CanPlayStem(article.Directory, stem, "uk");
            bool us = ownsAudio && playback.CanPlayStem(article.Directory, stem, "us");
            words.Add(new VocabularyEntryViewModel(entry, uk, us));
        }
        return words;
    }

    private readonly VocabularyEditor vocabularyEditor = new();

    public VocabularyEditResult TryAddWord(string text)
    {
        if (!CanWriteVocabulary) return VocabularyEditResult.Unavailable;
        bool duplicate = false;
        bool completed = Handle(() =>
        {
            var article = Article!;
            var saved = vocabularyEditor.Add(article.VocabularyPath, article.Vocabulary, text, out string word);
            if (saved is null)
            {
                duplicate = true;
                Notify("DuplicateVocabulary", "词表中已存在：" + word, NotificationSeverity.Warning);
                return;
            }
            CommitVocabulary(saved);
        });
        return !completed ? VocabularyEditResult.Failed :
            duplicate ? VocabularyEditResult.Duplicate : VocabularyEditResult.Success;
    }

    public bool CanMoveWord(VocabularyEntry entry, int direction) =>
        VocabularyEditor.CanMove(Article?.Vocabulary, entry, direction);

    public VocabularyEditResult TryMoveWord(VocabularyEntry entry, int direction)
    {
        if (!CanMoveWord(entry, direction)) return VocabularyEditResult.Unavailable;
        bool completed = Handle(() =>
        {
            var article = Article!;
            CommitVocabulary(vocabularyEditor.Move(article.VocabularyPath, article.Vocabulary, entry, direction));
        });
        return completed ? VocabularyEditResult.Success : VocabularyEditResult.Failed;
    }

    public VocabularyEditResult TryDeleteWord(VocabularyEntry entry)
    {
        var article = Article;
        if (article is null || !VocabularyEditor.CanDelete(article.Vocabulary, entry))
            return VocabularyEditResult.Unavailable;
        bool completed = Handle(() =>
        {
            CommitVocabulary(vocabularyEditor.Delete(article.VocabularyPath, article.Vocabulary, entry));
            playback.StopWord(entry);
        });
        return completed ? VocabularyEditResult.Success : VocabularyEditResult.Failed;
    }

    public VocabularyEditResult TryImportVocabulary(string path)
    {
        if (!CanWriteVocabulary) return VocabularyEditResult.Unavailable;
        bool completed = Handle(() =>
        {
            var article = Article!;
            CommitVocabulary(vocabularyEditor.Import(article.VocabularyPath, article.Vocabulary, path));
        });
        return completed ? VocabularyEditResult.Success : VocabularyEditResult.Failed;
    }

    private void CommitVocabulary(VocabularySnapshot saved)
    {
        workspace.CommitVocabulary(saved);
        playback.RefreshVocabulary(saved.Data.Words);
        RefreshWords();
        VocabularyChanged?.Invoke();
    }
}
