using System.IO;

namespace ReadArticles.Infrastructure;

public static class AppPaths
{
    public static string ApplicationDirectory => Path.GetFullPath(AppContext.BaseDirectory);
    public static string UserDataDirectory => Path.Combine(ApplicationDirectory, "userdata");
    public static string ConfigDirectory => Path.Combine(UserDataDirectory, "config");
    public static string SettingsFile => Path.Combine(ConfigDirectory, "settings.json");
    public static string LegacySettingsMigrationMarker => Path.Combine(ConfigDirectory, "legacy-settings-migrated.sha256");
    public static string BackupDirectory => Path.Combine(UserDataDirectory, "backups");
    public static string SettingsBackupDirectory => Path.Combine(BackupDirectory, "settings");
    public static string SettingsBackupFile => Path.Combine(SettingsBackupDirectory, "settings.json.bak");
    public static string VocabularyBackupDirectory => Path.Combine(BackupDirectory, "vocabulary");
    public static string CacheDirectory => Path.Combine(UserDataDirectory, "cache");
    public static string LogDirectory => Path.Combine(UserDataDirectory, "logs");

    public static void EnsureConfigDirectory() => Directory.CreateDirectory(ConfigDirectory);

    public static string VocabularyBackupFile(string sourcePath)
    {
        string normalized = Path.GetFullPath(sourcePath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).ToUpperInvariant();
        string id = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(normalized)));
        return Path.Combine(VocabularyBackupDirectory, id + ".vocabulary.json.bak");
    }

    public static string LegacySettingsFile
    {
        get
        {
            string? executableName = Path.GetFileNameWithoutExtension(Environment.ProcessPath);
            if (string.IsNullOrWhiteSpace(executableName))
                executableName = "ReadArticles";
            return Path.Combine(ApplicationDirectory, executableName + ".settings.json");
        }
    }
}
