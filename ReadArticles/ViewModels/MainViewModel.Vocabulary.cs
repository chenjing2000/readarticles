using System.IO;
using ReadArticles.Models;
using ReadArticles.Services;

namespace ReadArticles.ViewModels;

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
            words.Add(new VocabularyEntryViewModel(entry, this, uk, us));
        }
        return words;
    }
    public void AddSelection(PassageSelection selection) => Handle(() =>
    {
        var article = Article ?? throw new InvalidOperationException("请先打开文章。");
        var entry = VocabularyRepository.NewEntry(selection.Text);
        if (article.Vocabulary.Data.Words.Any(w => w.Word.Equals(entry.Word, StringComparison.OrdinalIgnoreCase)))
        {
            Status = "词表中已存在：" + entry.Word;
            return;
        }
        var entries = new List<VocabularyEntry>(article.Vocabulary.Data.Words);
        entries.Add(entry);
        CommitWords(entries);
    });
    public bool CanMoveWord(VocabularyEntry entry, int direction)
    {
        if (!CanWriteVocabulary || (direction != -1 && direction != 1)) return false;
        var entries = Article!.Vocabulary.Data.Words;
        int index = entries.IndexOf(entry);
        return index >= 0 && index + direction >= 0 && index + direction < entries.Count;
    }
    public void MoveWord(VocabularyEntry entry, int direction) => Handle(() =>
    {
        if (!CanMoveWord(entry, direction)) return;
        var entries = Article!.Vocabulary.Data.Words.ToList();
        int index = entries.IndexOf(entry);
        int destination = index + direction;
        entries.RemoveAt(index);
        entries.Insert(destination, entry);
        CommitWords(entries);
    });
    public void DeleteWord(VocabularyEntry entry) => Handle(() =>
    {
        var article = Article;
        if (article == null || !CanWriteVocabulary || !article.Vocabulary.Data.Words.Contains(entry)) return;
        CommitWords(article.Vocabulary.Data.Words.Where(w => !ReferenceEquals(w, entry)).ToList());
        playback.StopWord(entry);
    });
    public void ImportVocabulary(string path) => Handle(() =>
    {
        if (!File.Exists(path)) throw new FileNotFoundException("导入文件不存在。");
        var incoming = new VocabularyRepository().Load(path);
        CommitVocabulary(incoming.Data);
    });
    private void CommitWords(List<VocabularyEntry> entries)
    {
        CommitVocabulary(new VocabularyData { Words = entries, Extra = Article!.Vocabulary.Data.Extra });
    }
    private void CommitVocabulary(VocabularyData vocabulary)
    {
        workspace.ReplaceVocabulary(vocabulary);
        RefreshWords();
        VocabularyChanged?.Invoke();
    }
}
