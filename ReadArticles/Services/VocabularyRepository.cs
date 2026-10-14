using ReadArticles.Models;
using ReadArticles.Infrastructure;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Text;
using System.Threading;
namespace ReadArticles.Services;
public sealed class VocabularyRepository
{
    public VocabularySnapshot Load(string path)
    {
        if (!File.Exists(path)) return new VocabularySnapshot(new VocabularyData(), null, true);
        byte[] bytes = File.ReadAllBytes(path);
        string content = System.Text.Encoding.UTF8.GetString(bytes).TrimStart('\uFEFF');
        using var json = JsonDocument.Parse(content);
        var root = json.RootElement;
        if (root.ValueKind != JsonValueKind.Object || JsonFiles.Text(root, "filetype") != "vocabulary")
            throw new InvalidDataException("filetype 必须为 vocabulary。");
        foreach (var entry in JsonFiles.Array(root, "words").EnumerateArray())
        {
            JsonFiles.Text(entry, "word");
            JsonFiles.Text(entry, "phonetic_uk", false);
            JsonFiles.Text(entry, "phonetic_us", false);
            foreach (var meaning in JsonFiles.Array(entry, "meanings").EnumerateArray())
            { JsonFiles.Text(meaning, "pos", false); JsonFiles.Text(meaning, "meaning", false); }
        }
        var data = JsonSerializer.Deserialize<VocabularyData>(content)!;
        Validate(data);
        foreach (var entry in data.Words) entry.Extra?.Remove("audio");
        return new VocabularySnapshot(data, JsonFiles.Fingerprint(bytes), true);
    }

    public string Save(string path, VocabularyData data, string? expectedFingerprint)
    {
        string absolute = Path.GetFullPath(path);
        // Serialize this application's writers before checking the expected file version.
        string lockName = @"Local\ReadArticles.Vocabulary." + JsonFiles.Fingerprint(Encoding.UTF8.GetBytes(absolute.ToUpperInvariant()));
        using var mutex = new Mutex(false, lockName);
        try { mutex.WaitOne(); }
        catch (AbandonedMutexException) { } // Ownership is acquired; recheck the file after the previous writer exited.
        try
        {
            Validate(data);
            foreach (var entry in data.Words) entry.Extra?.Remove("audio");
            // Deny ordinary in-place writers while preparing the replacement. Release the
            // read handle immediately before rename, which Windows requires for overwrite.
            using var guard = File.Exists(absolute) ? new FileStream(absolute, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete) : null;
            void CheckCurrent()
            {
                string? actual = File.Exists(absolute) ? JsonFiles.Fingerprint(absolute) : null;
                if (actual != expectedFingerprint) throw new InvalidOperationException("词汇表已被其他程序修改，请重新打开文章后再编辑。");
            }
            CheckCurrent();
            return JsonFiles.WriteAtomic(absolute, data, () => { CheckCurrent(); guard?.Dispose(); });
        }
        finally { mutex.ReleaseMutex(); }
    }

    public static VocabularyEntry NewEntry(string text)
    {
        string normalized = Regex.Replace(text, @"\s+", " ").Trim();
        if (normalized.Length == 0) throw new InvalidDataException("选词不能为空。");
        return new VocabularyEntry { Word = normalized };
    }

    public static string AudioStem(string word)
    {
        return Regex.Replace(word.Trim().ToLowerInvariant(), "[^a-z0-9]+", "_").Trim('_');
    }

    public static void Validate(VocabularyData data)
    {
        if (data.FileType != "vocabulary" || data.Words is null) throw new InvalidDataException("词汇表结构无效。");
        var words = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in data.Words)
        {
            if (entry is null || string.IsNullOrWhiteSpace(entry.Word) || !words.Add(Regex.Replace(entry.Word, @"\s+", " ").Trim()))
                throw new InvalidDataException("词条为空或重复。");
            if (entry.Meanings is null || entry.PhoneticUk is null || entry.PhoneticUs is null || entry.Meanings.Any(m => m is null || m.Pos is null || m.Text is null))
                throw new InvalidDataException($"词条音标或释义无效：{entry.Word}");
        }
    }
}
