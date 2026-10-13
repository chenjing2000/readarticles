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
        string absolute = Path.GetFullPath(path);
        string title = Path.GetFileNameWithoutExtension(absolute);
        using var json = JsonDocument.Parse(File.ReadAllText(absolute));
        var paragraphs = ReadParagraphs(json.RootElement);
        string vocabularyPath = Path.ChangeExtension(absolute, ".vocabulary.json");
        var warnings = new List<string>();
        VocabularySnapshot vocabulary;
        try { vocabulary = new VocabularyRepository().Load(vocabularyPath); }
        catch (Exception error) when (error is JsonException or InvalidDataException or IOException or UnauthorizedAccessException)
        {
            warnings.Add("词汇表无法加载：" + error.Message);
            vocabulary = new VocabularySnapshot(new VocabularyData(), null, false, error.Message);
        }
        var resources = new AudioResources();
        int missing = 0;
        foreach (var segment in paragraphs.SelectMany(p => p).Where(s => s.HasAudio))
            foreach (string accent in new[] { "uk", "us" })
            {
                var asset = resources.Segment(Path.GetDirectoryName(absolute)!, segment, accent);
                if (!AudioResources.IsAvailable(asset)) missing++;
            }
        if (missing > 0) warnings.Add($"缺失 {missing} 个正文音频；正文仍可阅读。");
        return new LoadedArticle(absolute, title, paragraphs, vocabularyPath, vocabulary, warnings);
    }

    private static List<Segment[]> ReadParagraphs(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object || JsonFiles.Text(root, "filetype") != "passage")
            throw new InvalidDataException("filetype 必须为 passage。");
        if (!root.TryGetProperty("next_sid", out var next) || next.ValueKind != JsonValueKind.Number || !next.TryGetInt32(out int nextSid) || nextSid < 1 || nextSid > 1000)
            throw new InvalidDataException("next_sid 必须是 1–1000 的整数。");
        var paragraphs = new List<Segment[]>();
        var ids = new HashSet<string>();
        var placeholders = new List<int>();
        foreach (var paragraph in JsonFiles.Array(root, "paragraphs", true).EnumerateArray())
        {
            var segments = new List<Segment>();
            foreach (var item in JsonFiles.Array(paragraph, "paragraph", true).EnumerateArray())
            {
                string sid = JsonFiles.Text(item, "sid"), text = JsonFiles.Text(item, "text");
                if (!Regex.IsMatch(sid, @"\As[0-9]{3}\z") || int.Parse(sid[1..]) == 0 || !ids.Add(sid))
                    throw new InvalidDataException($"非法或重复 SID：{sid}");
                if (string.IsNullOrEmpty(text) || text != text.Trim()) throw new InvalidDataException($"{sid} 正文为空或含首尾空白。");
                foreach (Match match in Regex.Matches(text, @"\[\[([0-9]+)\]\]"))
                {
                    if (!int.TryParse(match.Groups[1].Value, out int number)) throw new InvalidDataException("占位符编号超出有效范围。");
                    placeholders.Add(number);
                }
                segments.Add(new Segment(sid, text));
            }
            paragraphs.Add(segments.ToArray());
        }
        var all = paragraphs.SelectMany(p => p).ToArray();
        if (nextSid <= all.Max(s => int.Parse(s.Sid[1..]))) throw new InvalidDataException("next_sid 不大于已有 SID。");
        bool blank = placeholders.Any(n => n >= 1);
        foreach (var segment in all)
        {
            if (blank)
            {
                string scrubbed = Regex.Replace(segment.Text, @"\[\[([0-9]+)\]\]", "");
                if (scrubbed.Contains("[[") || scrubbed.Contains("]]")) throw new InvalidDataException("ArticleBlank 结构无效。");
            }
            else if (segment.Text.Contains("[[") || segment.Text.Contains("]]"))
                throw new InvalidDataException($"{segment.Sid} 完整文章正文结构无效。");
        }
        if (blank && !placeholders.Order().SequenceEqual(Enumerable.Range(1, placeholders.Count))) throw new InvalidDataException("占位符编号必须唯一、从 1 连续。");
        return paragraphs.Select(p => p.Select(s => s with { HasAudio = !blank }).ToArray()).ToList();
    }
}
