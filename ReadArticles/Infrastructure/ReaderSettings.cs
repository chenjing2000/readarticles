using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace ReadArticles.Infrastructure;

// The executable-adjacent JSON is authoritative. Session-only pane widths
// are never serialized. Older flat settings are imported once, without
// deleting their original files.
public sealed class ReaderSettings
{
    [JsonIgnore]
    public double LeftWidth { get; set; } = 250;
    [JsonIgnore]
    public double RightWidth { get; set; } = 300;

    public ReaderAppearance Appearance { get; set; } = new();
    public ReaderAudioPreferences Audio { get; set; } = new();
    public string? Library { get; set; }

    // Compatibility aliases for existing native business and playback paths.
    [JsonIgnore] public double FontSize { get => Appearance.Body.Size; set => Appearance.Body.Size = value; }
    [JsonIgnore] public double Volume { get => Audio.Volume; set => Audio.Volume = value; }
    [JsonIgnore] public string Accent { get => Audio.Accent; set => Audio.Accent = value; }
    [JsonIgnore] public double ForwardStep { get => Audio.ForwardStep; set => Audio.ForwardStep = value; }
    [JsonIgnore] public double BackwardStep { get => Audio.BackwardStep; set => Audio.BackwardStep = value; }

    public static string PathName => AppPaths.SettingsFile;

    public ReaderSettings Copy()
    {
        var copy = JsonSerializer.Deserialize<ReaderSettings>(JsonSerializer.Serialize(this, JsonFiles.Options))!;
        copy.LeftWidth = LeftWidth;
        copy.RightWidth = RightWidth;
        return copy;
    }

    public void Apply(ReaderSettings other)
    {
        Appearance = other.Appearance;
        Audio = other.Audio;
        Library = other.Library;
    }

    private sealed class LegacyValues
    {
        public double FontSize { get; set; } = 15;
        public double Volume { get; set; } = 0.65;
        public string Accent { get; set; } = "uk";
        public double ForwardStep { get; set; } = 4;
        public double BackwardStep { get; set; } = 5;
        public string? Library { get; set; }
    }

    private static ReaderSettings ReadLegacy(string path)
    {
        var legacy = JsonSerializer.Deserialize<LegacyValues>(File.ReadAllText(path), JsonFiles.Options)
            ?? throw new JsonException("旧配置内容不能为空。");
        var settings = new ReaderSettings
        {
            FontSize = legacy.FontSize,
            Volume = legacy.Volume,
            Accent = legacy.Accent,
            ForwardStep = legacy.ForwardStep,
            BackwardStep = legacy.BackwardStep,
            Library = legacy.Library
        };
        settings.Normalize();
        return settings;
    }

    public static ReaderSettings Load(out string? warning)
    {
        warning = null;
        try
        {
            AppPaths.EnsureConfigDirectory();
            if (File.Exists(PathName)) return Deserialize(PathName);

            // Preserve old sources for rollback. Never silently discard a
            // valid legacy preference or overwrite an invalid source.
            foreach (string source in new[] { AppPaths.PreviousSettingsFile, AppPaths.LegacySettingsFile })
            {
                if (!File.Exists(source)) continue;
                try
                {
                    var migrated = ReadLegacy(source);
                    migrated.Save();
                    return migrated;
                }
                catch (JsonException error)
                {
                    warning = "旧配置损坏，原文件已保留：" + error.Message;
                    // Do not replace an invalid existing configuration with
                    // defaults until all migration candidates are examined.
                }
            }

            var defaults = new ReaderSettings();
            defaults.Save();
            return defaults;
        }
        catch (JsonException error)
        {
            warning = "配置文件损坏：" + error.Message;
            try
            {
                string corruptPath = PathName + "." + DateTime.Now.ToString("yyyyMMddHHmmssfff") + ".corrupt";
                if (File.Exists(PathName)) File.Move(PathName, corruptPath);
                if (File.Exists(AppPaths.SettingsBackupFile))
                {
                    try
                    {
                        _ = Deserialize(AppPaths.SettingsBackupFile);
                        BackupFiles.RestoreLatestBackup(PathName, AppPaths.SettingsBackupFile);
                        warning += " 已从最近一次有效备份恢复，损坏文件保留为：" + Path.GetFileName(corruptPath);
                        return Deserialize(PathName);
                    }
                    catch (Exception restoreError) when (restoreError is JsonException or IOException or UnauthorizedAccessException)
                    {
                        warning += " 最近一次备份无法恢复：" + restoreError.Message;
                    }
                }
                var defaults = new ReaderSettings();
                defaults.Save();
                warning += " 当前使用默认设置。";
                return defaults;
            }
            catch (Exception saveError) when (saveError is IOException or UnauthorizedAccessException)
            {
                warning += " 无法初始化配置：" + saveError.Message;
                return new ReaderSettings();
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            warning = "无法读取或保存配置，设置暂存于内存。请将程序放在有写入权限的目录：" + error.Message;
            return new ReaderSettings();
        }
    }

    private static ReaderSettings Deserialize(string path)
    {
        var settings = JsonSerializer.Deserialize<ReaderSettings>(File.ReadAllText(path), JsonFiles.Options)
            ?? throw new JsonException("配置内容不能为空。");
        settings.Normalize();
        return settings;
    }

    public void Save()
    {
        Normalize();
        byte[] desired = JsonSerializer.SerializeToUtf8Bytes(this, JsonFiles.Options);
        if (File.Exists(PathName))
        {
            byte[] existing = File.ReadAllBytes(PathName);
            if (existing.AsSpan().SequenceEqual(desired)) return;
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

    private static double Allowed(double value, double fallback, params double[] choices) =>
        double.IsFinite(value) && choices.Contains(value) ? value : fallback;
    private static string Font(string? value, string fallback, params string[] choices) =>
        choices.Contains(value, StringComparer.Ordinal) ? value! : fallback;
    private static string ColorCode(string? value, string fallback) =>
        value != null && Regex.IsMatch(value, "^#[0-9a-fA-F]{6}$")
            ? value.ToUpperInvariant() : fallback;

    public void Normalize()
    {
        LeftWidth = double.IsFinite(LeftWidth) ? Math.Clamp(Math.Round(LeftWidth), 200, 400) : 250;
        RightWidth = double.IsFinite(RightWidth) ? Math.Clamp(Math.Round(RightWidth), 200, 400) : 300;
        Appearance ??= new();
        Audio ??= new();
        Appearance.Title ??= new();
        Appearance.Body ??= new();
        Appearance.Vocabulary ??= new();
        Appearance.Phonetics ??= new();
        Appearance.Meanings ??= new();
        var a = Appearance;
        a.Title.Font = Font(a.Title.Font, "Nunito Sans", "Nunito Sans", "Microsoft YaHei", "Arial");
        a.Title.Size = Allowed(a.Title.Size, 18, 16, 18, 20, 22, 24);
        a.Title.LineHeight = Allowed(a.Title.LineHeight, 1.3, 1.2, 1.3, 1.4, 1.5);
        a.Title.ParagraphAfter = Allowed(a.Title.ParagraphAfter, 6, 3, 6, 12);
        a.Title.Color = ColorCode(a.Title.Color, "#283747");
        a.Body.Font = Font(a.Body.Font, "Nunito Sans", "Nunito Sans", "Microsoft YaHei", "Arial");
        a.Body.Size = Allowed(a.Body.Size, 15, 14, 15, 16, 17);
        a.Body.LineHeight = Allowed(a.Body.LineHeight, 1.3, 1.2, 1.3, 1.4, 1.5);
        a.Body.ParagraphAfter = Allowed(a.Body.ParagraphAfter, 12, 8, 12, 16, 20);
        a.Body.FirstLineIndent = Allowed(a.Body.FirstLineIndent, 2, 0, 1, 2, 3);
        a.Body.Color = ColorCode(a.Body.Color, "#283747");
        a.Body.Background = ColorCode(a.Body.Background, "#F8F8F6");
        a.Vocabulary.Font = Font(a.Vocabulary.Font, "Nunito Sans", "Nunito Sans", "Microsoft YaHei", "Arial");
        a.Vocabulary.Size = Allowed(a.Vocabulary.Size, 16, 14, 15, 16, 17, 18, 20);
        a.Vocabulary.Color = ColorCode(a.Vocabulary.Color, "#B7394D");
        a.Phonetics.Size = Allowed(a.Phonetics.Size, 15, 12, 13, 14, 15, 16, 18);
        a.Phonetics.Color = ColorCode(a.Phonetics.Color, "#0A7785");
        a.Meanings.Font = Font(a.Meanings.Font, "Microsoft YaHei", "Microsoft YaHei", "SimSun", "KaiTi");
        a.Meanings.Size = Allowed(a.Meanings.Size, 15, 14, 15, 16, 17);
        a.Meanings.LineHeight = Allowed(a.Meanings.LineHeight, 1.3, 1.2, 1.3, 1.4, 1.5);
        a.Meanings.Color = ColorCode(a.Meanings.Color, "#283747");
        Audio.Volume = double.IsFinite(Audio.Volume) ? Math.Clamp(Audio.Volume, 0, 1) : 0.65;
        if (Audio.Accent is not ("uk" or "us")) Audio.Accent = "uk";
        Audio.ForwardStep = Allowed(Audio.ForwardStep, 4, 4, 7, 10, 15);
        Audio.BackwardStep = Allowed(Audio.BackwardStep, 5, 5, 8, 11, 16);
    }
}

public sealed class ReaderAppearance
{
    public ReaderTextStyle Title { get; set; } = new() { Size = 18, LineHeight = 1.3, ParagraphAfter = 6 };
    public ReaderBodyStyle Body { get; set; } = new();
    public ReaderWordStyle Vocabulary { get; set; } = new();
    public ReaderPhoneticStyle Phonetics { get; set; } = new();
    public ReaderMeaningStyle Meanings { get; set; } = new();
}
public class ReaderTextStyle
{
    public string Font { get; set; } = "Nunito Sans";
    public double Size { get; set; } = 18;
    public double LineHeight { get; set; } = 1.3;
    public double ParagraphAfter { get; set; } = 6;
    public string Color { get; set; } = "#283747";
}
public sealed class ReaderBodyStyle : ReaderTextStyle
{
    public ReaderBodyStyle() { Size = 15; ParagraphAfter = 12; }
    public double FirstLineIndent { get; set; } = 2;
    public string Background { get; set; } = "#F8F8F6";
}
public sealed class ReaderWordStyle
{
    public string Font { get; set; } = "Nunito Sans";
    public double Size { get; set; } = 16;
    public string Color { get; set; } = "#B7394D";
}
public sealed class ReaderPhoneticStyle
{
    public double Size { get; set; } = 15;
    public string Color { get; set; } = "#0A7785";
}
public sealed class ReaderMeaningStyle
{
    public string Font { get; set; } = "Microsoft YaHei";
    public double Size { get; set; } = 15;
    public double LineHeight { get; set; } = 1.3;
    public string Color { get; set; } = "#283747";
}
public sealed class ReaderAudioPreferences
{
    public string Accent { get; set; } = "uk";
    public double Volume { get; set; } = 0.65;
    public double ForwardStep { get; set; } = 4;
    public double BackwardStep { get; set; } = 5;
}
