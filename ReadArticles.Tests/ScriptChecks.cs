using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using ReadArticles.Infrastructure;
using ReadArticles.Models;
using ReadArticles.Rendering;
using ReadArticles.Services;

internal static class ScriptChecks
{
    private const string Valid = """
        {
          "filetype": "script",
          "title": "",
          "paragraphs": [[{
            "text": "It is a beautiful day.",
            "timed_units": [
              ["It is a", 1.0, 1.8],
              ["beautiful", 1.8, 2.4],
              ["day", 2.4, 2.8]
            ]
          }]]
        }
        """;

    internal static void Run()
    {
        Program.Run("Script companion name is exact and is never a library leaf", () => InTemp((directory, passage) =>
        {
            var repo = new LibraryRepository();
            string companion = ArticleRepository.ScriptPathFor(passage);
            Program.Check(companion == Path.Combine(directory, "Example.script.json"));
            File.WriteAllText(companion, Valid);
            File.WriteAllText(Path.Combine(directory, "Other.SCRIPT.JSON"), Valid);
            File.WriteAllText(Path.Combine(directory, "Another.json"), "{}");
            var snapshot = repo.ScanDirectoryLevel(directory, directory);
            Program.Check(snapshot.Entries.Count(e => e.IsPassage) == 2);
            Program.Check(snapshot.Entries.All(e => !e.Name.EndsWith(".script", StringComparison.OrdinalIgnoreCase)));
        }));

        Program.Run("Script requires filetype and title, but accepts empty title", () => InTemp((_, passage) =>
        {
            var repo = new ArticleRepository();
            string path = ArticleRepository.ScriptPathFor(passage);
            File.WriteAllText(path, """{"paragraphs":[]}""");
            try
            {
                repo.LoadScript(passage);
                throw new Exception("Missing metadata accepted.");
            }
            catch (InvalidDataException error)
            {
                Program.Check(error.Message.Contains("filetype") && error.Message.Contains("title"));
            }

            foreach (string invalid in new[]
            {
                """{"filetype":"passage","title":"","paragraphs":[[{"text":"x","timed_units":[]}]]}""",
                """{"filetype":"script","title":null,"paragraphs":[[{"text":"x","timed_units":[]}]]}""",
                """{"filetype":"script","title":"","paragraphs":[[{"text":"x","timed_units":[["x",1.0,null]]}]]}""",
                """{"filetype":"script","title":"","paragraphs":[[{"text":"x","timed_units":[["x",2.0,1.0]]}]]}""",
                """{"filetype":"script","title":"","paragraphs":[[{"text":"x","timed_units":[["x",0,0]]}]]}"""
            })
            {
                File.WriteAllText(path, invalid);
                try { repo.LoadScript(passage); throw new Exception("Invalid Script accepted."); }
                catch (InvalidDataException) { }
            }

            File.WriteAllText(path, Valid);
            var loaded = repo.LoadScript(passage);
            Program.Check(loaded.Title == "" && loaded.Paragraphs.Count == 1);
            Program.Check(loaded.Paragraphs[0].Sentences[0].TimedUnits[0].Offset == 0);
            Program.Check(loaded.Paragraphs[0].Sentences[0].TimedUnits[1].Offset == 8);
        }));

        Program.Run("Script skips unaligned units without validating against Passage", () => InTemp((_, passage) =>
        {
            string json = """
                {"filetype":"script","title":"New title","paragraphs":[[{
                  "text":"the the [[1]] stays unchanged",
                  "timed_units":[["the",0,1],["the",1,2],["not in text",2,3],["stays",null,null]]
                }]]}
                """;
            File.WriteAllText(ArticleRepository.ScriptPathFor(passage), json);
            var article = new ArticleRepository().LoadScript(passage);
            var units = article.Paragraphs[0].Sentences[0].TimedUnits;
            Program.Check(units[0].Offset == 0 && units[1].Offset == 4);
            Program.Check(units[2].Offset == -1 && !units[2].CanColor);
            Program.Check(units[3].Offset >= 0 && !units[3].CanColor);
            var sentences = article.Paragraphs[0].Sentences;
            var reader = Reader(sentences, []);
            Program.Check(new TextRange(reader.Document.ContentStart, reader.Document.ContentEnd).Text.Contains("[[1]]"));
            Program.Check(!reader.RequestSegmentPlayback(reader.Ranges[0].Start));
            reader.SetScriptPosition(1.5);
            Program.Check(reader.GetTextForeground(0, 4) is LinearGradientBrush);
        }));

        Program.Run("Script gradient and vocabulary background remain independent across Run boundaries", () => InTemp((_, passage) =>
        {
            File.WriteAllText(ArticleRepository.ScriptPathFor(passage), Valid);
            var sentences = new ArticleRepository().LoadScript(passage).Paragraphs[0].Sentences;
            var reader = Reader(sentences, ["it is", "beautiful day"]);
            var original = reader.Document;
            Program.Check(reader.FocusVocabulary("beautiful day"));
            reader.SetScriptPosition(1.4);
            Program.Check(ReferenceEquals(original, reader.Document));
            Program.Check(reader.GetTextForeground(0, 0) is LinearGradientBrush);
            reader.SetScriptPosition(2.5);
            Program.Check(reader.GetTextForeground(0, 0) is SolidColorBrush complete &&
                complete.Color == Color.FromRgb(0xB8, 0x1A, 0x35));
            Program.Check(reader.GetTextForeground(0, 18) is LinearGradientBrush);
            reader.SetVocabulary(["it is", "beautiful day"], false);
            Program.Check(ReferenceEquals(original, reader.Document));
            reader.SetScriptPosition(1.0);
            Program.Check(reader.GetTextForeground(0, 0) is not LinearGradientBrush);
            reader.SetScriptPosition(null);
            Program.Check(reader.GetTextForeground(0, 0) is not LinearGradientBrush);
            Program.Check(!reader.RequestSegmentPlayback(reader.Ranges[0].Start));
        }));

    }

    internal static void RunUi()
    {
        Program.Run("Script toggle keeps Passage on failure and restores it after switching", () => InTemp((_, passage) =>
        {
            using var window = new ScriptWindow(passage);
            var button = (Button)window.Instance.FindName("ScriptButton");
            var icon = (Image)button.Content;
            Program.Check(button.IsEnabled && ReferenceEquals(icon.Source, UiIcons.ScriptOff));
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Program.Check(ReferenceEquals(icon.Source, UiIcons.ScriptOff) &&
                window.Instance.ViewModel.Notifications.Messages.Last().Severity == NotificationSeverity.Warning);

            File.WriteAllText(ArticleRepository.ScriptPathFor(passage), """{"filetype":"script","paragraphs":[]}""");
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Program.Check(ReferenceEquals(icon.Source, UiIcons.ScriptOff) &&
                window.Instance.ViewModel.Notifications.Messages.Last().Message.Contains("title"));

            File.WriteAllText(ArticleRepository.ScriptPathFor(passage), Valid);
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Program.Check(ReferenceEquals(icon.Source, UiIcons.ScriptOn));
            Program.Check(window.Instance.Readers.Count == 1 &&
                !window.Instance.Readers[0].Ranges[0].Segment.HasAudio);
            Program.Check(((System.Windows.Controls.TextBlock)window.Instance.FindName("ArticleTitleText")).Visibility == Visibility.Collapsed);

            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Program.Check(ReferenceEquals(icon.Source, UiIcons.ScriptOff));
            Program.Check(window.Instance.Readers[0].Ranges[0].Segment.Sid == "s001");
        }));
    }

    private static ParagraphReader Reader(IReadOnlyList<ScriptSentence> sentences, IEnumerable<string> words) =>
        new ParagraphReader(sentences.Select(s => new Segment("", s.Text, false)), words, true, sentences);

    private static void InTemp(Action<string, string> check)
    {
        string directory = Path.Combine(Path.GetTempPath(), "ReadArticles-Script-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string passage = Path.Combine(directory, "Example.json");
        File.WriteAllText(passage, """
            {"filetype":"passage","next_sid":2,"paragraphs":[{"paragraph":[{"sid":"s001","text":"Original paragraph."}]}]}
            """);
        try { check(directory, passage); }
        finally { Directory.Delete(directory, true); }
    }

    private sealed class ScriptWindow : IDisposable
    {
        public ReadArticles.MainWindow Instance { get; }
        public ScriptWindow(string passage)
        {
            Instance = new ReadArticles.MainWindow(false)
            {
                Left = -10000, Top = -10000, ShowActivated = false
            };
            Instance.Show();
            WpfTestHelpers.Pump();
            Program.Check(Instance.ViewModel.OpenArticle(passage));
            WpfTestHelpers.Pump();
        }
        public void Dispose()
        {
            Instance.Close();
            WpfTestHelpers.Pump();
        }
    }
}
