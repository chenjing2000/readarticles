using System.IO;
using ReadArticles.Models;
using ReadArticles.Services;

namespace ReadArticles.Vocabulary;

// No reading-session state: a returned snapshot is already persisted.
public sealed class VocabularyEditor
{
    private readonly VocabularyRepository repository = new();

    public static VocabularyEntry NewEntry(string text)
    {
        string normalized = VocabularyRepository.NormalizeWord(text);
        if (normalized.Length == 0) throw new InvalidDataException("选词不能为空。");
        return new VocabularyEntry { Word = normalized };
    }

    public VocabularySnapshot? Add(string path, VocabularySnapshot current, string text, out string word)
    {
        EnsureWritable(current);
        var entry = NewEntry(text);
        word = entry.Word;
        if (current.Data.Words.Any(w => VocabularyRepository.NormalizeWord(w.Word).Equals(entry.Word, StringComparison.OrdinalIgnoreCase)))
            return null;
        return SaveWords(path, current, new List<VocabularyEntry>(current.Data.Words) { entry });
    }

    public static bool CanMove(VocabularySnapshot? current, VocabularyEntry entry, int direction)
    {
        if (current?.CanWrite != true || direction is not (-1 or 1)) return false;
        var entries = current.Data.Words;
        int index = entries.IndexOf(entry);
        return index >= 0 && index + direction >= 0 && index + direction < entries.Count;
    }

    public VocabularySnapshot Move(string path, VocabularySnapshot current, VocabularyEntry entry, int direction)
    {
        if (!CanMove(current, entry, direction)) throw new InvalidOperationException("词条无法移动。");
        var entries = current.Data.Words.ToList();
        int index = entries.IndexOf(entry);
        entries.RemoveAt(index);
        entries.Insert(index + direction, entry);
        return SaveWords(path, current, entries);
    }

    public static bool CanDelete(VocabularySnapshot? current, VocabularyEntry entry) =>
        current?.CanWrite == true && current.Data.Words.Contains(entry);

    public VocabularySnapshot Delete(string path, VocabularySnapshot current, VocabularyEntry entry)
    {
        if (!CanDelete(current, entry)) throw new InvalidOperationException("词条无法删除。");
        return SaveWords(path, current, current.Data.Words.Where(w => !ReferenceEquals(w, entry)).ToList());
    }

    public VocabularySnapshot Import(string path, VocabularySnapshot current, string incomingPath)
    {
        EnsureWritable(current);
        if (!File.Exists(incomingPath)) throw new FileNotFoundException("导入文件不存在。");
        return Save(path, current, repository.Load(incomingPath).Data);
    }

    private VocabularySnapshot SaveWords(string path, VocabularySnapshot current, List<VocabularyEntry> entries) =>
        Save(path, current, new VocabularyData { Words = entries, Extra = current.Data.Extra });

    private VocabularySnapshot Save(string path, VocabularySnapshot current, VocabularyData data)
    {
        EnsureWritable(current);
        string fingerprint = repository.Save(path, data, current.Fingerprint);
        return new VocabularySnapshot(data, fingerprint, true);
    }

    private static void EnsureWritable(VocabularySnapshot current)
    {
        if (!current.CanWrite) throw new InvalidOperationException("原词汇表损坏，不能覆盖。请先修复并重新打开。");
    }
}
