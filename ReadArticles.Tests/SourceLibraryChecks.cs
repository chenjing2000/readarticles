using System.IO;
using System.Text.Json;
using ReadArticles.Infrastructure;
using ReadArticles.Services;

// Explicit local integration checks: user-edited data is not a fixed fixture.
internal static class SourceLibraryChecks
{
    private static string ArticlePath => Environment.GetEnvironmentVariable("READARTICLES_TEST_ARTICLE")
        ?? throw new InvalidOperationException("Supply -ArticleDirectory through tools/Test.ps1 to run source checks.");

    internal static void Run()
    {
        Program.Run("current source JSON loads without changing any source files", () =>
        {
            var before = Fingerprints();
            using var passage = JsonDocument.Parse(File.ReadAllText(ArticlePath));
            using var vocabulary = JsonDocument.Parse(File.ReadAllText(Path.ChangeExtension(ArticlePath, ".vocabulary.json")));
            var article = new ArticleRepository().Load(ArticlePath);
            var paragraphs = passage.RootElement.GetProperty("paragraphs");
            int segmentCount = paragraphs.EnumerateArray().Sum(p => p.GetProperty("paragraph").GetArrayLength());
            Program.Check(article.Paragraphs.Count == paragraphs.GetArrayLength() && article.Segments.Count() == segmentCount);
            Program.Check(article.Vocabulary.Data.Words.Count == vocabulary.RootElement.GetProperty("words").GetArrayLength());
            Program.Check(Path.GetFileName(Path.GetDirectoryName(new AudioResources().Segment(article.Directory, article.Segments.First(), "uk"))) == "audio_segments");
            CheckUnchanged(before);
        });
        Program.Run("current source library renders its current article and vocabulary without writes", () =>
        {
            var before = Fingerprints();
            var article = new ArticleRepository().Load(ArticlePath);
            var window = new ReadArticles.MainWindow(false) { Left = -10000, Top = -10000, ShowActivated = false };
            try
            {
                window.Show();
                Program.Check(window.ViewModel.OpenLibrary(Path.GetDirectoryName(Path.GetDirectoryName(ArticlePath)!)!));
                Program.Check(window.SelectPassage(ArticlePath));
                WpfTestHelpers.Pump();
                Program.Check(window.Readers.Count == article.Paragraphs.Count);
                Program.Check(window.ViewModel.Words.Select(w => w.Word).SequenceEqual(article.Vocabulary.Data.Words.Select(w => w.Word)));
                Program.Check(window.Readers.SelectMany(r => r.Ranges).Select(r => r.Segment.Sid).SequenceEqual(article.Segments.Select(s => s.Sid)));
            }
            finally { window.Close(); WpfTestHelpers.Pump(); }
            CheckUnchanged(before);
        });
    }

    private static Dictionary<string, string> Fingerprints()
    {
        string directory = Path.GetDirectoryName(ArticlePath)!;
        return Directory.GetFiles(directory, "*", SearchOption.AllDirectories)
            .ToDictionary(path => path, path => JsonFiles.Fingerprint(path)!);
    }

    private static void CheckUnchanged(Dictionary<string, string> before)
    {
        var after = Fingerprints();
        Program.Check(before.Count == after.Count && before.All(pair => after.TryGetValue(pair.Key, out string? hash) && hash == pair.Value));
    }

}
