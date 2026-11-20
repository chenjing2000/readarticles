using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

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

    // A deliberate breaking configuration upgrade: old flat settings are
    // never migrated. Only the structured Appearance/Audio format is accepted.
    public static ReaderSettings Load(out string? warning)
    {
        warning = null;
        try
        {
            AppPaths.EnsureConfigDirectory();
            if (File.Exists(PathName))
            {
                try
                {
                    using var document = JsonDocument.Parse(File.ReadAllText(PathName));
                    var root = document.RootElement;
                    if (root.ValueKind == JsonValueKind.Object &&
                        root.TryGetProperty("Appearance", out var appearance) &&
                        appearance.ValueKind == JsonValueKind.Object &&
                        root.TryGetProperty("Audio", out var audio) &&
                        audio.ValueKind == JsonValueKind.Object)
                        return Deserialize(PathName);
                    // Valid JSON but obsolete schema: deliberate breaking change.
                    warning = "检测到旧格式配置，已重新创建默认设置。";
                }
                catch (JsonException error)
                {
                    // This is corruption, not an old schema. Retain the
                    // original and try a verified backup of the new format.
                    warning = "配置文件损坏：" + error.Message;
                    string corrupt = PathName + "." +
                        DateTime.Now.ToString("yyyyMMddHHmmssfff") + ".corrupt";
                    File.Move(PathName, corrupt);
                    if (File.Exists(AppPaths.SettingsBackupFile))
                    {
                        try
                        {
                            var backup = Deserialize(AppPaths.SettingsBackupFile);
                            BackupFiles.RestoreLatestBackup(PathName, AppPaths.SettingsBackupFile);
                            warning += " 已从最近一次有效备份恢复。";
                            return backup;
                        }
                        catch (Exception restoreError) when
                            (restoreError is JsonException or IOException or UnauthorizedAccessException)
                        {
                            warning += " 备份不可恢复。";
                        }
                    }
                }
                // Obsolete formats are not migrated, just replaced with
                // defaults. An obsolete source never rotates to the backup.
                var clean = new ReaderSettings();
                JsonFiles.WriteAtomic(PathName, clean);
                return clean;
            }

            var defaults = new ReaderSettings();
            defaults.Save();
            return defaults;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            warning = "配置无法读写，暂时使用内存设置。请检查程序目录权限：" + error.Message;
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
    private static string ColorCode(string? value, string fallback, params string[] palette) =>
        value != null && palette.Contains(value, StringComparer.OrdinalIgnoreCase)
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
        a.Title.Font = Font(a.Title.Font, "Georgia", "Georgia", "Microsoft YaHei UI", "Nunito", "Segoe UI");
        a.Title.Size = Allowed(a.Title.Size, 18, 16, 18, 20, 22, 24);
        a.Title.LineHeight = Allowed(a.Title.LineHeight, 1.7, 1.6, 1.7, 1.8);
        a.Title.ParagraphAfter = Allowed(a.Title.ParagraphAfter, 16, 16, 18, 20);
        a.Title.Color = ColorCode(a.Title.Color, "#283747", "#283747", "#344054", "#3F4750", "#315247", "#513D4D");
        a.Body.Font = Font(a.Body.Font, "Georgia", "Georgia", "Microsoft YaHei UI", "Nunito", "Segoe UI");
        a.Body.Size = Allowed(a.Body.Size, 15, 14, 15, 16, 17);
        a.Body.LineHeight = Allowed(a.Body.LineHeight, 1.7, 1.6, 1.7, 1.8);
        a.Body.ParagraphAfter = Allowed(a.Body.ParagraphAfter, 16, 14, 16, 18);
        a.Body.FirstLineIndent = Allowed(a.Body.FirstLineIndent, 2, 0, 1, 2, 3);
        a.Body.Color = ColorCode(a.Body.Color, "#283747", "#283747", "#344054", "#3F4750", "#315247", "#513D4D");
        a.Body.Background = ColorCode(a.Body.Background, "#F8F8F6", "#F8F8F6", "#F0F3F7", "#F7F1E6", "#EFF5F0", "#F1F0F7");
        a.Body.ScriptColor = ColorCode(a.Body.ScriptColor, "#B64550", "#B64550", "#A33D30", "#A45A2C", "#856300", "#53713C");
        a.Body.SelectionAddSize = Allowed(a.Body.SelectionAddSize, 0.65, 0.65, 0.70, 0.75, 0.80);
        a.Body.SelectionBackground = ColorCode(a.Body.SelectionBackground, "#DDE8F2",
            "#DDE8F2", "#E5EDE0", "#F3E9DC", "#EBE5F2", "#F2E5E7");
        a.Vocabulary.Font = Font(a.Vocabulary.Font, "Georgia", "Georgia", "Microsoft YaHei UI", "Nunito", "Segoe UI");
        a.Vocabulary.Size = Allowed(a.Vocabulary.Size, 16, 15, 16, 17);
        a.Vocabulary.Color = ColorCode(a.Vocabulary.Color, "#B7394D", "#B7394D", "#9D3E68", "#7D50A1", "#1F6795", "#8B593B");
        a.Phonetics.Size = Allowed(a.Phonetics.Size, 14, 13, 14, 15);
        a.Phonetics.Color = ColorCode(a.Phonetics.Color, "#0A7785", "#0A7785", "#256CA5", "#38735A", "#7657A0", "#995431");
        a.Meanings.Font = Font(a.Meanings.Font, "Microsoft YaHei", "Microsoft YaHei", "SimSun", "KaiTi");
        a.Meanings.Size = Allowed(a.Meanings.Size, 15, 14, 15, 16);
        a.Meanings.LineHeight = Allowed(a.Meanings.LineHeight, 1.5, 1.5, 1.6, 1.7);
        a.Meanings.Color = ColorCode(a.Meanings.Color, "#283747", "#283747", "#344054", "#3F4750", "#315247", "#513D4D");
        if (Audio.Accent is not ("uk" or "us")) Audio.Accent = "uk";
        Audio.ForwardStep = Allowed(Audio.ForwardStep, 4, 4, 7, 10, 15);
        Audio.BackwardStep = Allowed(Audio.BackwardStep, 5, 5, 8, 11, 16);
    }
}

public sealed class ReaderAppearance
{
    public ReaderTextStyle Title { get; set; } = new() { Size = 18, LineHeight = 1.7, ParagraphAfter = 16 };
    public ReaderBodyStyle Body { get; set; } = new();
    public ReaderWordStyle Vocabulary { get; set; } = new();
    public ReaderPhoneticStyle Phonetics { get; set; } = new();
    public ReaderMeaningStyle Meanings { get; set; } = new();
}
public class ReaderTextStyle
{
    public string Font { get; set; } = "Georgia";
    public double Size { get; set; } = 18;
    public double LineHeight { get; set; } = 1.7;
    public double ParagraphAfter { get; set; } = 6;
    public string Color { get; set; } = "#283747";
}
public sealed class ReaderBodyStyle : ReaderTextStyle
{
    public ReaderBodyStyle() { Size = 15; LineHeight = 1.7; ParagraphAfter = 16; }
    public double FirstLineIndent { get; set; } = 2;
    public string Background { get; set; } = "#F8F8F6";
    public string ScriptColor { get; set; } = "#B64550";
    public double SelectionAddSize { get; set; } = 0.65;
    public string SelectionBackground { get; set; } = "#DDE8F2";
}
public sealed class ReaderWordStyle
{
    public string Font { get; set; } = "Georgia";
    public double Size { get; set; } = 16;
    public string Color { get; set; } = "#B7394D";
}
public sealed class ReaderPhoneticStyle
{
    public double Size { get; set; } = 14;
    public string Color { get; set; } = "#0A7785";
}
public sealed class ReaderMeaningStyle
{
    public string Font { get; set; } = "Microsoft YaHei";
    public double Size { get; set; } = 15;
    public double LineHeight { get; set; } = 1.5;
    public string Color { get; set; } = "#283747";
}
public sealed class ReaderAudioPreferences
{
    public string Accent { get; set; } = "uk";
    public double ForwardStep { get; set; } = 4;
    public double BackwardStep { get; set; } = 5;
}
