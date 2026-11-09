using System.IO;
using System.Text.Json;

namespace ReadArticles.Infrastructure;

public sealed class ReaderSettings
{
    public double LeftWidth { get; set; } = 310;
    public double RightWidth { get; set; } = 390;
    public double FontSize { get; set; } = 15;
    public double Volume { get; set; } = 0.65;
    public string Accent { get; set; } = "uk";
    public double ForwardStep { get; set; } = 4.0;
    public double BackwardStep { get; set; } = 5.0;
    public string? Library { get; set; }

    public static string PathName => AppPaths.SettingsFile;

    public static ReaderSettings Load(out string? warning)
    {
        warning = null;
        try
        {
            AppPaths.EnsureConfigDirectory();

            if (File.Exists(PathName))
            {
                var current = ReadCurrentSettings(out warning);
                string legacyPath = AppPaths.LegacySettingsFile;
                if (File.Exists(legacyPath) && !File.Exists(AppPaths.LegacySettingsMigrationMarker))
                {
                    string legacyFingerprint = JsonFiles.Fingerprint(legacyPath);
                    if (!string.Equals(JsonFiles.Fingerprint(PathName), legacyFingerprint, StringComparison.Ordinal))
                    {
                        warning = "检测到新版和旧版配置文件同时存在且内容不同。当前使用 userdata/config/settings.json，旧版配置文件已保留；如需使用旧配置，请先备份两份文件后再处理。";
                    }
                    else
                    {
                        WriteMigrationMarker(legacyFingerprint);
                    }
                }
                return current;
            }

            string migrationSourcePath = AppPaths.LegacySettingsFile;
            if (File.Exists(migrationSourcePath))
            {
                try
                {
                    var migrated = Deserialize(migrationSourcePath);
                    migrated.Save();
                    try
                    {
                        WriteMigrationMarker(JsonFiles.Fingerprint(migrationSourcePath));
                    }
                    catch (Exception markerError) when (markerError is IOException or UnauthorizedAccessException)
                    {
                        warning = "设置已迁移，但无法写入迁移标记；旧版配置文件已保留：" + markerError.Message;
                    }
                    return migrated;
                }
                catch (JsonException error)
                {
                    warning = "旧版配置文件损坏，原文件已保留；本次使用默认设置。请检查旧配置文件：" + error.Message;
                    return new ReaderSettings();
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException)
                {
                    warning = "无法迁移旧版配置文件，原文件已保留；本次设置暂存于内存：" + error.Message;
                    return new ReaderSettings();
                }
            }

            var defaults = new ReaderSettings();
            defaults.Save();
            return defaults;
        }
        catch (JsonException error)
        {
            warning = "配置文件损坏，使用默认设置：" + error.Message;
            try
            {
                string corruptPath = PathName + "." + DateTime.Now.ToString("yyyyMMddHHmmssfff") + ".corrupt";
                if (File.Exists(PathName)) File.Move(PathName, corruptPath);
                var defaults = new ReaderSettings();
                defaults.Save();
                return defaults;
            }
            catch (Exception saveError) when (saveError is IOException or UnauthorizedAccessException)
            {
                warning += " 无法初始化配置：" + saveError.Message;
            }
            return new ReaderSettings();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            warning = "无法创建或访问用户数据目录，设置暂存于内存。请将整个 ReadArticles 文件夹移动到具有写入权限的位置。详细信息：" + error.Message;
            return new ReaderSettings();
        }
    }

    private static void WriteMigrationMarker(string fingerprint)
    {
        File.WriteAllText(AppPaths.LegacySettingsMigrationMarker, fingerprint);
    }

    private static ReaderSettings ReadCurrentSettings(out string? warning)
    {
        warning = null;
        var settings = Deserialize(PathName);
        settings.Normalize();
        return settings;
    }

    private static ReaderSettings Deserialize(string path)
    {
        var settings = JsonSerializer.Deserialize<ReaderSettings>(File.ReadAllText(path))
            ?? throw new JsonException("配置内容不能为空。");
        settings.Normalize();
        return settings;
    }

    public void Save()
    {
        Normalize();
        if (File.Exists(PathName))
        {
            _ = Deserialize(PathName);
            BackupFiles.CreateVerifiedBackup(PathName, AppPaths.SettingsBackupFile);
        }
        JsonFiles.WriteAtomic(PathName, this);
    }

    public static void RestoreLatestBackup()
    {
        _ = Deserialize(AppPaths.SettingsBackupFile);
        BackupFiles.RestoreLatestBackup(PathName, AppPaths.SettingsBackupFile);
    }

    private void Normalize()
    {
        LeftWidth = double.IsFinite(LeftWidth) ? Math.Round(LeftWidth) : 310;
        RightWidth = double.IsFinite(RightWidth) ? Math.Round(RightWidth) : 390;
        FontSize = double.IsFinite(FontSize) ? Math.Clamp(Math.Round(FontSize), 14, 16) : 15;
        Volume = double.IsFinite(Volume) ? Math.Clamp(Volume, 0, 1) : 0.65;
        if (Accent != "uk" && Accent != "us") Accent = "uk";
        if (ForwardStep != 4 && ForwardStep != 7 && ForwardStep != 10) ForwardStep = 4;
        if (BackwardStep != 5 && BackwardStep != 8 && BackwardStep != 15) BackwardStep = 5;
    }
}
