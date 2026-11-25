using ReadArticles.Models;
using ReadArticles.Infrastructure;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
namespace ReadArticles.Services;
public sealed class ArticleRepository
{
    public LoadedArticle Load(string path)
    {
        string absolute = ResourcePaths.RejectReparsePoints(path);
        
        using var json = JsonDocument.Parse(File.ReadAllText(absolute));
        var (title, paragraphs) = ReadParagraphs(json.RootElement);
        string vocabularyPath = Path.ChangeExtension(absolute, ".vocabulary.json");
        var warnings = new List<string>();
        VocabularySnapshot vocabulary;
        try { vocabulary = new VocabularyRepository().Load(vocabularyPath); }
        catch (Exception error) when (error is JsonException or InvalidDataException or IOException or UnauthorizedAccessException or ArgumentException)
        {
            warnings.Add("词汇表无法加载：" + error.Message);
            vocabulary = new VocabularySnapshot(new VocabularyData(), null, false, error.Message);
        }
        return new LoadedArticle(absolute, title, paragraphs, vocabularyPath, vocabulary, warnings);
    }

    public static string ScriptPathFor(string passagePath) =>
        Path.ChangeExtension(Path.GetFullPath(passagePath), ".script.json");

    // This is called only after the reader explicitly requests the Script view.
    // It does not read vocabulary, find recordings or mutate the active Passage.
    public ScriptArticle LoadScript(string passagePath)
    {
        using var json = JsonDocument.Parse(File.ReadAllText(ResourcePaths.RejectReparsePoints(ScriptPathFor(passagePath))));
        JsonElement root = json.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("Script 根节点必须为对象。");

        var missing = new List<string>();
        if (!root.TryGetProperty("filetype", out var type)) missing.Add("filetype");
        if (!root.TryGetProperty("title", out var title)) missing.Add("title");
        if (missing.Count > 0)
            throw new InvalidDataException("Script 缺少必需字段：" + string.Join("、", missing) + "。");
        if (type.ValueKind != JsonValueKind.String || type.GetString() != "script")
            throw new InvalidDataException("Script 的 filetype 必须为 \"script\"。");
        if (title.ValueKind != JsonValueKind.String)
            throw new InvalidDataException("Script 的 title 必须为字符串（可以为空）。");

        if (!root.TryGetProperty("paragraphs", out var source) ||
            source.ValueKind != JsonValueKind.Array || source.GetArrayLength() == 0)
            throw new InvalidDataException("Script 的 paragraphs 必须为非空二维数组。");

        var paragraphs = new List<ScriptParagraph>();
        foreach (var paragraph in source.EnumerateArray())
        {
            if (paragraph.ValueKind != JsonValueKind.Array || paragraph.GetArrayLength() == 0)
                throw new InvalidDataException("Script 每个段落必须包含句子数组。");
            var sentences = new List<ScriptSentence>();
            foreach (var sentence in paragraph.EnumerateArray())
            {
                if (sentence.ValueKind != JsonValueKind.Object ||
                    !sentence.TryGetProperty("text", out var textValue) ||
                    textValue.ValueKind != JsonValueKind.String ||
                    string.IsNullOrWhiteSpace(textValue.GetString()))
                    throw new InvalidDataException("Script 句子缺少有效的 text 字符串。");
                if (!sentence.TryGetProperty("timed_units", out var unitsValue) ||
                    unitsValue.ValueKind != JsonValueKind.Array)
                    throw new InvalidDataException("Script 句子缺少 timed_units 数组。");

                string text = textValue.GetString()!;
                int cursor = 0;
                var units = new List<ScriptTimedUnit>();
                foreach (var unit in unitsValue.EnumerateArray())
                {
                    if (unit.ValueKind != JsonValueKind.Array || unit.GetArrayLength() != 3)
                        throw new InvalidDataException("Script 的 timed_units 项必须为三元数组。");
                    JsonElement[] fields = unit.EnumerateArray().ToArray();
                    if (fields[0].ValueKind != JsonValueKind.String ||
                        string.IsNullOrEmpty(fields[0].GetString()))
                        throw new InvalidDataException("Script 时间单元的显示文本必须为非空字符串。");

                    double? start = ReadTime(fields[1]);
                    double? end = ReadTime(fields[2]);
                    if (start.HasValue != end.HasValue ||
                        (start.HasValue && end!.Value <= start.Value))
                        throw new InvalidDataException("Script 时间区间必须同时为 null，或满足 0 <= start < end。");

                    string display = fields[0].GetString()!;
                    int offset = text.IndexOf(display, cursor, StringComparison.Ordinal);
                    // The producer guarantees alignment. A mismatch must not
                    // reject the JSON or invent a replacement substring.
                    if (offset >= 0) cursor = offset + display.Length;
                    units.Add(new ScriptTimedUnit(display, start, end, offset));
                }
                sentences.Add(new ScriptSentence(text, units));
            }
            paragraphs.Add(new ScriptParagraph(sentences));
        }
        return new ScriptArticle(title.GetString()!, paragraphs);
    }

    private static double? ReadTime(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Null) return null;
        if (element.ValueKind != JsonValueKind.Number ||
            !element.TryGetDouble(out double time) || !double.IsFinite(time) || time < 0)
            throw new InvalidDataException("Script 时间必须是有限非负数字或 null。");
        return time;
    }

    private static (string Title, List<string[]> Paragraphs) ReadParagraphs(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object || JsonFiles.Text(root, "filetype") != "passage")
            throw new InvalidDataException("filetype 必须为 passage。");
        if (!root.TryGetProperty("title", out var title) || title.ValueKind != JsonValueKind.String)
            throw new InvalidDataException("title 必须存在且为字符串。");

        var paragraphs = new List<string[]>();
        var placeholders = new List<int>();
        foreach (var paragraph in JsonFiles.Array(root, "paragraphs", true).EnumerateArray())
        {
            if (paragraph.ValueKind != JsonValueKind.Array || paragraph.GetArrayLength() == 0)
                throw new InvalidDataException("每个段落必须包含非空句子数组。");
            var sentences = new List<string>();
            foreach (var item in paragraph.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.String)
                    throw new InvalidDataException("句子必须为字符串。");
                string text = item.GetString()!;
                if (string.IsNullOrWhiteSpace(text) || text != text.Trim())
                    throw new InvalidDataException("句子正文为空或含首尾空白。");
                foreach (Match match in Regex.Matches(text, @"\[\[([0-9]+)\]\]"))
                {
                    if (!int.TryParse(match.Groups[1].Value, out int number))
                        throw new InvalidDataException("占位符编号超出有效范围。");
                    placeholders.Add(number);
                }
                sentences.Add(text);
            }
            paragraphs.Add(sentences.ToArray());
        }
        bool blank = placeholders.Any(n => n >= 1);
        foreach (string sentence in paragraphs.SelectMany(p => p))
        {
            string scrubbed = blank ? Regex.Replace(sentence, @"\[\[([0-9]+)\]\]", "") : sentence;
            if (scrubbed.Contains("[[") || scrubbed.Contains("]]"))
                throw new InvalidDataException("文章占位符结构无效。");
        }
        if (blank && !placeholders.Order().SequenceEqual(Enumerable.Range(1, placeholders.Count)))
            throw new InvalidDataException("占位符编号必须唯一、从 1 连续。");
        return (title.GetString()!, paragraphs);
    }
}
