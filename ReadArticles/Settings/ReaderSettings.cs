using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ReadArticles.Infrastructure;

// The executable-adjacent JSON is authoritative for persisted preferences.
// Obsolete flat settings are reset, not imported.
public sealed class ReaderSettings
{
    public ReaderAppearance Appearance { get; set; } = new();
    public ReaderAudioPreferences Audio { get; set; } = new();
    public string? Library { get; set; }
    public const string DefaultExcludedFolders = "audio, cache";
    public string ExcludedFolders { get; set; } = DefaultExcludedFolders;

    public static HashSet<string> ParseExcludedFolders(string? text)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string name in (text ?? DefaultExcludedFolders)
            .Split([',', ';', '，', '；'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            string stem = name.Split('.')[0].TrimEnd(' ').ToUpperInvariant();
            bool reserved = stem is "CON" or "PRN" or "AUX" or "NUL" or "CONIN$" or "CONOUT$" ||
                (stem.Length == 4 && (stem.StartsWith("COM", StringComparison.Ordinal) ||
                    stem.StartsWith("LPT", StringComparison.Ordinal)) && "123456789¹²³".Contains(stem[3]));
            if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
                name.EndsWith('.') || reserved)
                throw new ArgumentException($"无效的文件夹名称：{name}。请输入文件夹名称，而不是路径。");
            names.Add(name);
        }
        return names;
    }

    // Convenience accessors for native playback and setting updates.
    [JsonIgnore] public string Accent { get => Audio.Accent; set => Audio.Accent = value; }
    [JsonIgnore] public double ForwardStep { get => Audio.ForwardStep; set => Audio.ForwardStep = value; }
    [JsonIgnore] public double BackwardStep { get => Audio.BackwardStep; set => Audio.BackwardStep = value; }

    public static string PathName => AppPaths.SettingsFile;

    public ReaderSettings Copy() =>
        JsonSerializer.Deserialize<ReaderSettings>(JsonSerializer.Serialize(this, JsonFiles.Options))!;

    public void Apply(ReaderSettings other)
    {
        Appearance = other.Appearance;
        Audio = other.Audio;
        Library = other.Library;
        ExcludedFolders = other.ExcludedFolders;
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
                    if (IsCurrentSchema(root))
                        return Deserialize(root, out warning);
                    // Valid JSON but obsolete schema: deliberate breaking change.
                    warning = "检测到旧格式配置，已重新创建默认设置。";
                }
                catch (JsonException error)
                {
                    // This is corruption, not an old schema. Retain the
                    // original and try a verified backup of the new format.
                    warning = "配置文件损坏：" + error.Message;
                    string corrupt = RecoveryCopyPath();
                    File.Move(PathName, corrupt);
                    if (File.Exists(AppPaths.SettingsBackupFile))
                    {
                        try
                        {
                            var backup = DeserializeBackup(AppPaths.SettingsBackupFile);
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

    private static bool IsCurrentSchema(JsonElement root) =>
        root.ValueKind == JsonValueKind.Object &&
        root.TryGetProperty("Appearance", out var appearance) &&
        appearance.ValueKind == JsonValueKind.Object &&
        root.TryGetProperty("Audio", out var audio) &&
        audio.ValueKind == JsonValueKind.Object;

    // A malformed field value is not structural JSON corruption. On load,
    // recover only that value; the original file remains untouched until Save.
    private static ReaderSettings Deserialize(JsonElement root, out string? warning)
    {
        warning = null;
        if (!IsCurrentSchema(root)) throw new JsonException("配置不是当前结构。");
        // Only this optional field is independently recoverable. Do not
        // conceal structural errors in the other settings.
        bool invalidType = root.TryGetProperty(nameof(ExcludedFolders), out var field) &&
            field.ValueKind is not (JsonValueKind.String or JsonValueKind.Null);
        ReaderSettings settings;
        if (invalidType)
        {
            var copy = System.Text.Json.Nodes.JsonNode.Parse(root.GetRawText())!.AsObject();
            copy.Remove(nameof(ExcludedFolders));
            settings = copy.Deserialize<ReaderSettings>(JsonFiles.Options)
                ?? throw new JsonException("配置内容不能为空。");
        }
        else
            settings = root.Deserialize<ReaderSettings>(JsonFiles.Options)
                ?? throw new JsonException("配置内容不能为空。");
        try { _ = ParseExcludedFolders(settings.ExcludedFolders); }
        catch (ArgumentException) { invalidType = true; }
        if (invalidType)
        {
            settings.ExcludedFolders = DefaultExcludedFolders;
            warning = "排除文件夹设置无效，已恢复该项默认设置；其他设置保持不变。";
        }
        settings.NormalizeAppearanceAndAudio();
        return settings;
    }

    private static ReaderSettings DeserializeBackup(string path)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var settings = Deserialize(document.RootElement, out var warning);
        if (warning is not null) throw new JsonException(warning);
        return settings;
    }

    // Return the validated set for the caller to apply without parsing again.
    public HashSet<string> Save()
    {
        var names = Normalize();
        byte[] desired = JsonSerializer.SerializeToUtf8Bytes(this, JsonFiles.Options);
        if (File.Exists(PathName))
        {
            byte[] existing = File.ReadAllBytes(PathName);
            if (existing.AsSpan().SequenceEqual(desired)) return names;
            using var document = JsonDocument.Parse(System.Text.Encoding.UTF8.GetString(existing).TrimStart('\uFEFF'));
            _ = Deserialize(document.RootElement, out var warning);
            // Preserve the original invalid field separately. Do not replace
            // the last valid normal backup with a configuration needing repair.
            string backup = warning is null ? AppPaths.SettingsBackupFile :
                RecoveryCopyPath();
            BackupFiles.CreateVerifiedBackup(PathName, backup);
        }
        JsonFiles.WriteAtomicBytes(PathName, desired);
        return names;
    }

    private static string RecoveryCopyPath() =>
        PathName + "." + Guid.NewGuid().ToString("N") + ".corrupt";

    public static void RestoreLatestBackup()
    {
        _ = DeserializeBackup(AppPaths.SettingsBackupFile);
        bool keepBackup = false;
        if (File.Exists(PathName))
        {
            try { _ = DeserializeBackup(PathName); }
            catch (JsonException)
            {
                BackupFiles.CreateVerifiedBackup(PathName, RecoveryCopyPath());
                keepBackup = true;
            }
        }
        BackupFiles.RestoreLatestBackup(PathName, AppPaths.SettingsBackupFile,
            keepExistingBackup: keepBackup);
    }

    internal static readonly (double Min, double Max, int Decimals) FontSizeRange = (10, 40, 0);
    internal static readonly (double Min, double Max, int Decimals) LineHeightRange = (1, 3, 2);
    internal static readonly (double Min, double Max, int Decimals) ParagraphAfterRange = (0, 40, 0);
    internal static readonly (double Min, double Max, int Decimals) FirstLineIndentRange = (0, 5, 0);
    internal static readonly (double Min, double Max, int Decimals) SelectionAddSizeRange = (0.40, 1.20, 2);
    internal static readonly (double Min, double Max, int Decimals) SeekStepRange = (1, 60, 0);

    internal static bool IsValidNumber(double value, (double Min, double Max, int Decimals) range) =>
        value >= range.Min && value <= range.Max && value == Math.Round(value, range.Decimals);
    private static double ValidNumber(double value, double fallback, (double Min, double Max, int Decimals) range) =>
        IsValidNumber(value, range) ? value : fallback;
    private static string Font(string? value, string fallback, params string[] choices) =>
        choices.Contains(value, StringComparer.Ordinal) ? value! : fallback;
    private static string ColorCode(string? value, string fallback, params string[] palette) =>
        value != null && palette.Contains(value, StringComparer.OrdinalIgnoreCase)
            ? value.ToUpperInvariant() : fallback;

    public HashSet<string> Normalize()
    {
        ExcludedFolders ??= DefaultExcludedFolders;
        HashSet<string> names;
        try { names = ParseExcludedFolders(ExcludedFolders); }
        catch (ArgumentException error) { throw new JsonException(error.Message, error); }
        NormalizeAppearanceAndAudio();
        return names;
    }

    private void NormalizeAppearanceAndAudio()
    {
        ExcludedFolders ??= DefaultExcludedFolders;
        Appearance ??= new();
        Audio ??= new();
        Appearance.Title ??= new();
        Appearance.Body ??= new();
        Appearance.Vocabulary ??= new();
        Appearance.Phonetics ??= new();
        Appearance.Meanings ??= new();
        var a = Appearance;
        a.Title.Font = Font(a.Title.Font, "Newsreader", "Newsreader", "Source Serif 4", "Georgia", "Nunito", "Microsoft YaHei UI");
        a.Title.Size = ValidNumber(a.Title.Size, 18, FontSizeRange);
        a.Title.LineHeight = ValidNumber(a.Title.LineHeight, 1.7, LineHeightRange);
        a.Title.ParagraphAfter = ValidNumber(a.Title.ParagraphAfter, 16, ParagraphAfterRange);
        a.Title.Color = ColorCode(a.Title.Color, "#283747", "#283747", "#344054", "#3F4750", "#315247", "#513D4D");
        a.Body.Font = Font(a.Body.Font, "Newsreader", "Newsreader", "Source Serif 4", "Georgia", "Nunito", "Microsoft YaHei UI");
        a.Body.Size = ValidNumber(a.Body.Size, 15, FontSizeRange);
        a.Body.LineHeight = ValidNumber(a.Body.LineHeight, 1.7, LineHeightRange);
        a.Body.ParagraphAfter = ValidNumber(a.Body.ParagraphAfter, 16, ParagraphAfterRange);
        a.Body.FirstLineIndent = ValidNumber(a.Body.FirstLineIndent, 2, FirstLineIndentRange);
        a.Body.Color = ColorCode(a.Body.Color, "#283747", "#283747", "#344054", "#3F4750", "#315247", "#513D4D");
        a.Body.Background = ColorCode(a.Body.Background, "#F8F8F6", "#F8F8F6", "#F0F3F7", "#F7F1E6", "#EFF5F0", "#F1F0F7");
        a.Body.ScriptColor = a.Body.ScriptColor?.ToUpperInvariant() switch
        {
            "#B64550" or "#A33D30" or "#E02020" => "#F6EDC2",
            "#A45A2C" or "#D65A00" => "#F5E4D6",
            "#53713C" or "#008A48" => "#E1EEDB",
            "#0066E6" => "#E0ECF4",
            "#856300" or "#8A36D1" => "#ECE6F3",
            _ => ColorCode(a.Body.ScriptColor, "#F6EDC2",
                "#F6EDC2", "#F5E4D6", "#E1EEDB", "#E0ECF4", "#ECE6F3")
        };
        a.Body.SelectionAddSize = ValidNumber(a.Body.SelectionAddSize, 0.65, SelectionAddSizeRange);
        a.Body.SelectionBackground = ColorCode(a.Body.SelectionBackground, "#DDE8F2",
            "#DDE8F2", "#E5EDE0", "#F3E9DC", "#EBE5F2", "#F2E5E7");
        a.Vocabulary.Font = Font(a.Vocabulary.Font, "Newsreader", "Newsreader", "Source Serif 4", "Georgia", "Nunito", "Microsoft YaHei UI");
        a.Vocabulary.Size = ValidNumber(a.Vocabulary.Size, 16, FontSizeRange);
        a.Vocabulary.Color = ColorCode(a.Vocabulary.Color, "#B7394D", "#B7394D", "#9D3E68", "#7D50A1", "#1F6795", "#8B593B");
        a.Phonetics.Size = ValidNumber(a.Phonetics.Size, 14, FontSizeRange);
        a.Phonetics.Color = ColorCode(a.Phonetics.Color, "#0A7785", "#0A7785", "#256CA5", "#38735A", "#7657A0", "#995431");
        a.Meanings.Font = Font(a.Meanings.Font, "Source Han Serif SC", "Source Han Serif SC", "Microsoft YaHei");
        a.Meanings.Size = ValidNumber(a.Meanings.Size, 15, FontSizeRange);
        a.Meanings.LineHeight = ValidNumber(a.Meanings.LineHeight, 1.5, LineHeightRange);
        a.Meanings.Color = ColorCode(a.Meanings.Color, "#283747", "#283747", "#344054", "#3F4750", "#315247", "#513D4D");
        if (Audio.Accent is not ("uk" or "us")) Audio.Accent = "uk";
        Audio.ForwardStep = ValidNumber(Audio.ForwardStep, 4, SeekStepRange);
        Audio.BackwardStep = ValidNumber(Audio.BackwardStep, 5, SeekStepRange);
    }
}

public sealed class ReaderAppearance
{
    public ReaderTextStyle Title { get; set; } = new();
    public ReaderBodyStyle Body { get; set; } = new();
    public ReaderWordStyle Vocabulary { get; set; } = new();
    public ReaderPhoneticStyle Phonetics { get; set; } = new();
    public ReaderMeaningStyle Meanings { get; set; } = new();
}
public class ReaderTextStyle
{
    public string Font { get; set; } = "Newsreader";
    public double Size { get; set; } = 18;
    public double LineHeight { get; set; } = 1.7;
    public double ParagraphAfter { get; set; } = 16;
    public string Color { get; set; } = "#283747";
}
public sealed class ReaderBodyStyle : ReaderTextStyle
{
    public ReaderBodyStyle() { Size = 15; }
    public double FirstLineIndent { get; set; } = 2;
    public string Background { get; set; } = "#F8F8F6";
    public string ScriptColor { get; set; } = "#F6EDC2";
    public double SelectionAddSize { get; set; } = 0.65;
    public string SelectionBackground { get; set; } = "#DDE8F2";
}
public sealed class ReaderWordStyle
{
    public string Font { get; set; } = "Newsreader";
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
    public string Font { get; set; } = "Source Han Serif SC";
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
