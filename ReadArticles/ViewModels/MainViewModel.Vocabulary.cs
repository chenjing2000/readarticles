using System.IO;
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
        foreach (var entry in article.Vocabulary.Data.Words)
        {
            bool uk = playback.CanPlayWord(article, entry, "uk");
            bool us = playback.CanPlayWord(article, entry, "us");
            words.Add(new VocabularyEntryViewModel(entry, uk, us));
        }
        return words;
    }

    public VocabularyEditResult TryAddWord(string text)
    {
        if (!CanWriteVocabulary) return VocabularyEditResult.Unavailable;
        bool duplicate = false;
        bool completed = Handle(() =>
        {
            var article = Article ?? throw new InvalidOperationException("请先打开文章。");
            var entry = VocabularyRepository.NewEntry(text);
            if (article.Vocabulary.Data.Words.Any(w =>
                w.Word.Equals(entry.Word, StringComparison.OrdinalIgnoreCase)))
            {
                duplicate = true;
                Notify("DuplicateVocabulary", "词表中已存在：" + entry.Word,
                    ReadArticles.Infrastructure.NotificationSeverity.Warning);
                return;
            }
            var entries = new List<VocabularyEntry>(article.Vocabulary.Data.Words) { entry };
            CommitWords(entries);
        });
        return !completed ? VocabularyEditResult.Failed :
            duplicate ? VocabularyEditResult.Duplicate : VocabularyEditResult.Success;
    }

    public bool CanMoveWord(VocabularyEntry entry, int direction)
    {
        if (!CanWriteVocabulary || (direction != -1 && direction != 1)) return false;
        var entries = Article!.Vocabulary.Data.Words;
        int index = entries.IndexOf(entry);
        return index >= 0 && index + direction >= 0 && index + direction < entries.Count;
    }

    public VocabularyEditResult TryMoveWord(VocabularyEntry entry, int direction)
    {
        if (!CanMoveWord(entry, direction)) return VocabularyEditResult.Unavailable;
        bool completed = Handle(() =>
        {
            var entries = Article!.Vocabulary.Data.Words.ToList();
            int index = entries.IndexOf(entry);
            int destination = index + direction;
            entries.RemoveAt(index);
            entries.Insert(destination, entry);
            CommitWords(entries);
        });
        return completed ? VocabularyEditResult.Success : VocabularyEditResult.Failed;
    }

    public VocabularyEditResult TryDeleteWord(VocabularyEntry entry)
    {
        var article = Article;
        if (article is null || !CanWriteVocabulary ||
            !article.Vocabulary.Data.Words.Contains(entry))
            return VocabularyEditResult.Unavailable;
        bool completed = Handle(() =>
        {
            CommitWords(article.Vocabulary.Data.Words
                .Where(w => !ReferenceEquals(w, entry)).ToList());
            playback.StopWord(entry);
        });
        return completed ? VocabularyEditResult.Success : VocabularyEditResult.Failed;
    }

    public VocabularyEditResult TryImportVocabulary(string path)
    {
        if (!CanWriteVocabulary) return VocabularyEditResult.Unavailable;
        bool completed = Handle(() =>
        {
            if (!File.Exists(path)) throw new FileNotFoundException("导入文件不存在。");
            var incoming = new VocabularyRepository().Load(path);
            CommitVocabulary(incoming.Data);
        });
        return completed ? VocabularyEditResult.Success : VocabularyEditResult.Failed;
    }

    private void CommitWords(List<VocabularyEntry> entries)
    {
        CommitVocabulary(new VocabularyData { Words = entries, Extra = Article!.Vocabulary.Data.Extra });
    }

    private void CommitVocabulary(VocabularyData vocabulary)
    {
        workspace.ReplaceVocabulary(vocabulary);
        playback.RefreshVocabulary(Article!);
        RefreshWords();
        VocabularyChanged?.Invoke();
    }
}
