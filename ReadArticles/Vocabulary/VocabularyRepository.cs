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
    private readonly Func<string, string> backupPathResolver;

    public VocabularyRepository(Func<string, string>? backupPathResolver = null)
    {
        this.backupPathResolver = backupPathResolver ?? AppPaths.VocabularyBackupFile;
    }

    public VocabularySnapshot Load(string path)
    {
        path = ResourcePaths.RejectReparsePoints(path);
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
            if (entry.TryGetProperty("family", out var family) && family.ValueKind != JsonValueKind.Array)
                throw new InvalidDataException("family 必须为字符串数组。");
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
        string absolute = ResourcePaths.RejectReparsePoints(path);
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
            string backupPath = ResourcePaths.RejectReparsePoints(backupPathResolver(absolute));
            return JsonFiles.WriteAtomic(absolute, data, () =>
            {
                CheckCurrent();
                if (File.Exists(absolute))
                    BackupFiles.CreateVerifiedBackup(absolute, backupPath);
                CheckCurrent();
                guard?.Dispose();
            });
        }
        finally { mutex.ReleaseMutex(); }
    }

    public void RestoreLatestBackup(string path, string? expectedFingerprint)
    {
        string absolute = ResourcePaths.RejectReparsePoints(path);
        string lockName = @"Local\ReadArticles.Vocabulary." + JsonFiles.Fingerprint(Encoding.UTF8.GetBytes(absolute.ToUpperInvariant()));
        using var mutex = new Mutex(false, lockName);
        try { mutex.WaitOne(); }
        catch (AbandonedMutexException) { }

        try
        {
            string backupPath = ResourcePaths.RejectReparsePoints(backupPathResolver(absolute));
            var backup = Load(backupPath);
            if (!backup.CanWrite)
                throw new InvalidDataException("备份词汇表无效，不能恢复。");

            string? actual = File.Exists(absolute) ? JsonFiles.Fingerprint(absolute) : null;
            bool invalidCurrent = false;
            if (actual != expectedFingerprint)
            {
                if (expectedFingerprint is null && actual is not null)
                {
                    try { _ = Load(absolute); }
                    catch (Exception error) when (error is JsonException or InvalidDataException) { invalidCurrent = true; }
                }
                if (!invalidCurrent)
                    throw new InvalidOperationException("词汇表已被其他程序修改，请重新打开文章后再恢复。");
            }

            if (!invalidCurrent)
            {
                BackupFiles.RestoreLatestBackup(absolute, backupPath);
                return;
            }

            // Keep the original corrupt bytes at their expected path until all
            // recovery inputs are staged; copy rather than moving the source.
            string corruptPath = absolute + "." + DateTime.Now.ToString("yyyyMMddHHmmssfff") +
                "." + Guid.NewGuid().ToString("N") + ".corrupt";
            BackupFiles.CreateVerifiedBackup(absolute, corruptPath);
            if (JsonFiles.Fingerprint(corruptPath) != actual ||
                JsonFiles.Fingerprint(absolute) != actual)
                throw new IOException("损坏文件保护副本校验失败，恢复已取消。");

            AppLogger.Default.Warning("RecoveryFilePreserved");
            // A corrupt source must never become the sole normal .bak file.
            // Restore keeps the verified previous backup while the corrupt
            // original remains available separately as a recovery artifact.
            BackupFiles.RestoreLatestBackup(absolute, backupPath, keepExistingBackup: true);
        }
        finally
        {
            mutex.ReleaseMutex();
        }
    }

    internal static string NormalizeWord(string text) => Regex.Replace(text, @"\s+", " ").Trim();

    public static void Validate(VocabularyData data)
    {
        if (data.FileType != "vocabulary" || data.Words is null) throw new InvalidDataException("词汇表结构无效。");
        var words = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in data.Words)
        {
            if (entry is null || string.IsNullOrWhiteSpace(entry.Word))
                throw new InvalidDataException("词条为空或重复。");
            string headword = NormalizeWord(entry.Word);
            if (!words.Add(headword))
                throw new InvalidDataException("词条为空或重复。");
            if (entry.Meanings is null || entry.PhoneticUk is null || entry.PhoneticUs is null || entry.Meanings.Any(m => m is null || m.Pos is null || m.Text is null))
                throw new InvalidDataException($"词条音标或释义无效：{entry.Word}");
            if (entry.Family is null) continue;
            var forms = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string? form in entry.Family)
            {
                string normalized = NormalizeWord(form ?? "");
                if (normalized.Length == 0 || form != normalized ||
                    normalized.Equals(headword, StringComparison.OrdinalIgnoreCase) || !forms.Add(normalized))
                    throw new InvalidDataException($"family 包含空值、词头或重复词形：{entry.Word}");
            }
        }
    }
}
