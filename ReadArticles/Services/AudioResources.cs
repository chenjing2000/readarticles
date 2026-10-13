using ReadArticles.Models;
using ReadArticles.Infrastructure;
using System.IO;
namespace ReadArticles.Services;
public sealed class AudioResources
{
    public string ArticleFile(string root)
    {
        foreach (string extension in new[] { ".mp3", ".wav" })
        {
            string? first = Directory.EnumerateFiles(root, "*", SearchOption.TopDirectoryOnly)
                .Where(path => string.Equals(Path.GetExtension(path), extension, StringComparison.OrdinalIgnoreCase))
                .OrderBy(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase)
                .ThenBy(path => Path.GetFileName(path), StringComparer.Ordinal)
                .FirstOrDefault();
            if (first != null) return ResourcePaths.Resolve(root, Path.GetFileName(first));
        }
        throw new FileNotFoundException("文章同级目录没有 MP3/WAV 音频文件。");
    }
    public string Segment(string root, Segment segment, string accent)
    {
        if (!segment.HasAudio) throw new InvalidDataException("ArticleBlank 没有正文音频。");
        return FindFile(root, "audio_sentences", segment.Sid, accent);
    }
    public string Vocabulary(string root, VocabularyEntry entry, string accent)
    {
        string stem = VocabularyRepository.AudioStem(entry.Word);
        if (stem.Length == 0) throw new InvalidDataException("词条没有可用的音频文件名。");
        return FindFile(root, "audio", stem, accent);
    }
    public static bool OwnsWordAudio(LoadedArticle article, VocabularyEntry entry)
    {
        string stem = VocabularyRepository.AudioStem(entry.Word);
        return stem.Length > 0 && ReferenceEquals(entry,
            article.Vocabulary.Data.Words.FirstOrDefault(w => VocabularyRepository.AudioStem(w.Word) == stem));
    }
    private static string FindFile(string root, string folder, string stem, string accent)
    {
        if (accent != "uk" && accent != "us") throw new ArgumentException("Accent must be uk or us.");
        string mp3 = ResourcePaths.Resolve(root, $"{folder}/{stem}_{accent}.mp3");
        if (File.Exists(mp3)) return mp3;
        string wav = ResourcePaths.Resolve(root, $"{folder}/{stem}_{accent}.wav");
        return File.Exists(wav) ? wav : mp3;
    }
    public static bool IsAvailable(string path)
    {
        try { return File.Exists(path) && new FileInfo(path).Length > 0; }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }
}
