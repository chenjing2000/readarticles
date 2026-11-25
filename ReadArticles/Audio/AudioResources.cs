using ReadArticles.Models;
using ReadArticles.Infrastructure;
using System.IO;
using System.Text.RegularExpressions;
namespace ReadArticles.Services;
public sealed class AudioResources
{
    private static readonly string[] priority = [".m4a", ".mp3", ".wav"];

    public IReadOnlyList<string> ArticleFiles(string root)
    {
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
    public static string AudioStem(string word)
    {
        return Regex.Replace(word.Trim().ToLowerInvariant(), "[^a-z0-9]+", "_").Trim('_');
    }

    public string Vocabulary(string root, VocabularyEntry entry, string accent)
    {
        return VocabularyByStem(root, AudioStem(entry.Word), accent);
    }

    internal string VocabularyByStem(string root, string stem, string accent)
    {
        if (stem.Length == 0) throw new InvalidDataException("词条没有可用的音频文件名。");
        return FindFile(root, "audio", stem, accent);
    }
    public static bool OwnsWordAudio(IReadOnlyList<VocabularyEntry> words, VocabularyEntry entry)
    {
        string stem = AudioStem(entry.Word);
        return stem.Length > 0 && ReferenceEquals(entry,
            words.FirstOrDefault(w => AudioStem(w.Word) == stem));
    }
    private static string FindFile(string root, string folder, string stem, string accent)
    {
        if (accent != "uk" && accent != "us") throw new ArgumentException("Accent must be uk or us.");
        string mp3 = ResourcePaths.Resolve(root, $"{folder}/{stem}_{accent}.mp3");
        if (File.Exists(mp3)) return mp3;
        string wav = ResourcePaths.Resolve(root, $"{folder}/{stem}_{accent}.wav");
        return File.Exists(wav) ? wav : mp3;
    }
    // Keep access errors observable for actual playback, while UI eligibility
    // treats unavailable files as disabled buttons.
    internal static bool IsPlayableFile(string path) =>
        Path.IsPathFullyQualified(path) &&
        priority.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase) &&
        File.Exists(path) && new FileInfo(path).Length > 0;

    public static bool IsAvailable(string path)
    {
        try { return IsPlayableFile(path); }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }
}
