using System.IO;
using System.Security.Cryptography;
using System.Text;
using ReadArticles.Infrastructure;
using ReadArticles.Models;
using ReadArticles.Services;

internal static class BackupChecks
{
    internal static void Run()
    {
        Program.Run("settings keep exactly one verified pre-save backup and can restore it", () =>
        {
            string settingsPath = ReaderSettings.PathName;
            string backupPath = AppPaths.SettingsBackupFile;
            string markerPath = AppPaths.LegacySettingsMigrationMarker;
            string legacyPath = AppPaths.LegacySettingsFile;
            byte[]? settingsBefore = File.Exists(settingsPath) ? File.ReadAllBytes(settingsPath) : null;
            byte[]? backupBefore = File.Exists(backupPath) ? File.ReadAllBytes(backupPath) : null;
            byte[]? markerBefore = File.Exists(markerPath) ? File.ReadAllBytes(markerPath) : null;
            byte[]? legacyBefore = File.Exists(legacyPath) ? File.ReadAllBytes(legacyPath) : null;
            try
            {
                if (File.Exists(settingsPath)) File.Delete(settingsPath);
                if (File.Exists(backupPath)) File.Delete(backupPath);
                if (File.Exists(legacyPath)) File.Delete(legacyPath);
                if (File.Exists(markerPath)) File.Delete(markerPath);
                var first = new ReaderSettings { Volume = 0.2, Accent = "uk" };
                first.Save();
                Program.Check(!File.Exists(backupPath));
                var second = new ReaderSettings { Volume = 0.4, Accent = "us" };
                second.Save();
                Program.Check(File.Exists(backupPath));
                Program.Check(ReaderSettings.Load(out _).Volume == 0.4);
                Program.Check(JsonFiles.Fingerprint(backupPath) == JsonFiles.Fingerprint(Encoding.UTF8.GetBytes(File.ReadAllText(backupPath))));
                var third = new ReaderSettings { Volume = 0.8, Accent = "uk" };
                third.Save();
                Program.Check(Directory.GetFiles(AppPaths.SettingsBackupDirectory, "*.bak").Length == 1);
                Program.Check(JsonFiles.Fingerprint(backupPath) != JsonFiles.Fingerprint(settingsPath));
                ReaderSettings.RestoreLatestBackup();
                var restored = ReaderSettings.Load(out var warning);
                Program.Check(warning == null && restored.Volume == 0.4 && restored.Accent == "us");
                Program.Check(Directory.GetFiles(AppPaths.SettingsBackupDirectory, "*.bak").Length == 1);
            }
            finally
            {
                RestoreFile(settingsPath, settingsBefore);
                RestoreFile(backupPath, backupBefore);
                RestoreFile(markerPath, markerBefore);
                RestoreFile(legacyPath, legacyBefore);
            }
        });

        Program.Run("vocabulary keeps one verified backup per source and restores safely", () =>
        {
            string root = Path.Combine(Path.GetTempPath(), "ReadArticles-backup-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                string source = Path.Combine(root, "same-name.vocabulary.json");
                string backupRoot = Path.Combine(root, "backups");
                string BackupPath(string path)
                {
                    string normalized = Path.GetFullPath(path).ToUpperInvariant();
                    string id = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized)));
                    return Path.Combine(backupRoot, id + ".bak");
                }

                var initial = new VocabularyData
                {
                    Words = new List<VocabularyEntry>
                    {
                        new() { Word = "alpha", Meanings = new List<Meaning> { new("n.", "first") } }
                    }
                };
                JsonFiles.WriteAtomic(source, initial);
                var repository = new VocabularyRepository(BackupPath);
                var loaded = repository.Load(source);
                var versionTwo = new VocabularyData
                {
                    Words = new List<VocabularyEntry>
                    {
                        new() { Word = "beta", Meanings = new List<Meaning> { new("n.", "second") } }
                    }
                };
                string fingerprintTwo = repository.Save(source, versionTwo, loaded.Fingerprint);
                string backup = BackupPath(source);
                Program.Check(File.Exists(backup));
                Program.Check(repository.Load(backup).Data.Words[0].Word == "alpha");

                var versionThree = new VocabularyData
                {
                    Words = new List<VocabularyEntry>
                    {
                        new() { Word = "gamma", Meanings = new List<Meaning> { new("n.", "third") } }
                    }
                };
                string fingerprintThree = repository.Save(source, versionThree, fingerprintTwo);
                Program.Check(repository.Load(backup).Data.Words[0].Word == "beta");
                Program.Check(Directory.GetFiles(backupRoot, "*.bak").Length == 1);

                repository.RestoreLatestBackup(source, fingerprintThree);
                Program.Check(repository.Load(source).Data.Words[0].Word == "beta");
                Program.Check(repository.Load(backup).Data.Words[0].Word == "gamma");
                Program.Check(Directory.GetFiles(backupRoot, "*.bak").Length == 1);
            }
            finally
            {
                Directory.Delete(root, true);
            }
        });

        Program.Run("vocabulary save aborts when its backup cannot be created", () =>
        {
            string root = Path.Combine(Path.GetTempPath(), "ReadArticles-vocabulary-backup-failure-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                string source = Path.Combine(root, "article.vocabulary.json");
                string blocked = Path.Combine(root, "blocked");
                File.WriteAllText(blocked, "not a directory");
                var original = new VocabularyData
                {
                    Words = new List<VocabularyEntry>
                    {
                        new() { Word = "original", Meanings = new List<Meaning> { new("n.", "original") } }
                    }
                };
                JsonFiles.WriteAtomic(source, original);
                string before = JsonFiles.Fingerprint(source);
                var repository = new VocabularyRepository(_ => Path.Combine(blocked, "backup.bak"));
                var changed = new VocabularyData
                {
                    Words = new List<VocabularyEntry>
                    {
                        new() { Word = "changed", Meanings = new List<Meaning> { new("n.", "changed") } }
                    }
                };
                bool failed = false;
                try { repository.Save(source, changed, before); }
                catch (IOException) { failed = true; }
                catch (UnauthorizedAccessException) { failed = true; }
                Program.Check(failed && JsonFiles.Fingerprint(source) == before);
                Program.Check(repository.Load(source).Data.Words[0].Word == "original");
            }
            finally
            {
                Directory.Delete(root, true);
            }
        });

        Program.Run("same-named vocabulary files in different folders receive separate backup identities", () =>
        {
            string root = Path.Combine(Path.GetTempPath(), "ReadArticles-backup-identity-" + Guid.NewGuid().ToString("N"));
            try
            {
                string first = Path.Combine(root, "one", "vocabulary.json");
                string second = Path.Combine(root, "two", "vocabulary.json");
                string firstBackup = AppPaths.VocabularyBackupFile(first);
                string secondBackup = AppPaths.VocabularyBackupFile(second);
                Program.Check(!string.Equals(firstBackup, secondBackup, StringComparison.OrdinalIgnoreCase));
            }
            finally
            {
                if (Directory.Exists(root)) Directory.Delete(root, true);
            }
        });

        Program.Run("failed backup leaves the source file unchanged", () =>
        {
            string root = Path.Combine(Path.GetTempPath(), "ReadArticles-backup-failure-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                string source = Path.Combine(root, "source.json");
                string blockedDirectory = Path.Combine(root, "blocked");
                File.WriteAllText(source, "{\"version\":1}");
                File.WriteAllText(blockedDirectory, "not a directory");
                string before = File.ReadAllText(source);
                bool failed = false;
                try { BackupFiles.CreateVerifiedBackup(source, Path.Combine(blockedDirectory, "source.bak")); }
                catch (IOException) { failed = true; }
                catch (UnauthorizedAccessException) { failed = true; }
                Program.Check(failed && File.ReadAllText(source) == before);
            }
            finally
            {
                Directory.Delete(root, true);
            }
        });
    }

    private static void RestoreFile(string path, byte[]? content)
    {
        if (content == null)
        {
            if (File.Exists(path)) File.Delete(path);
        }
        else
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, content);
        }
    }
}
