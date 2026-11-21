using ReadArticles.Models;
using ReadArticles.Infrastructure;
using System.IO;
namespace ReadArticles.Services;
public sealed class AudioResources
{
    public IReadOnlyList<string> ArticleFiles(string root)
    {
        string[] priority = [".m4a", ".mp3", ".wav"];
        return Directory.EnumerateFiles(root, "*", SearchOption.TopDirectoryOnly)
            .Where(path => priority.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
            .OrderBy(path => Array.FindIndex(priority, extension =>
                string.Equals(Path.GetExtension(path), extension, StringComparison.OrdinalIgnoreCase)))
            .ThenBy(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase)
            .ThenBy(path => Path.GetFileName(path), StringComparer.Ordinal)
            .Select(path =>
            {
                try { return ResourcePaths.Resolve(root, Path.GetFileName(path)); }
                catch (Exception error) when (error is ArgumentException or IOException or UnauthorizedAccessException)
                {
                    AppLogger.Default.Warning("AudioCandidatePathRejected", error);
                    return null;
                }
            })
            .OfType<string>()
            .ToArray();
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
