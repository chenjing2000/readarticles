using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
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

        Program.Run("hyphenated and multiword Script units each retain one gradient Run", () =>
        {
            const string text = "euro-zone economic growth";
            var sentence = new ScriptSentence(text, [
                new ScriptTimedUnit("euro-zone", 29.226, 29.706, 0),
                new ScriptTimedUnit("economic growth", 29.706, 30.526, 10)
            ]);
            var reader = Reader([sentence], ["euro-zone", "growth"]);
            var paragraph = (Paragraph)reader.Document.Blocks.FirstBlock!;
            var runs = paragraph.Inlines.OfType<Span>().Single().Inlines.OfType<Run>().ToArray();
            Program.Check(runs.Count(run => run.Text == "euro-zone") == 1);
            Program.Check(runs.Count(run => run.Text == "economic growth") == 1);
            Program.Check(runs.Length == 3); // separator is its own untimed Run

            reader.SetScriptPosition(29.346); // 25% of the first complete unit
            var quarter = (LinearGradientBrush)reader.GetTextForeground(0, 0);
            Program.Check(ReferenceEquals(quarter, reader.GetTextForeground(0, 5)));
            Program.Check(ReferenceEquals(quarter, reader.GetTextForeground(0, 8)));
            Program.Check(Math.Abs(quarter.GradientStops[1].Offset - 0.25) < 0.00001);

            reader.SetScriptPosition(29.466); // 50%
            var halfway = (LinearGradientBrush)reader.GetTextForeground(0, 5);
            Program.Check(ReferenceEquals(halfway, reader.GetTextForeground(0, 0)));
            Program.Check(Math.Abs(halfway.GradientStops[1].Offset - 0.5) < 0.00001);
            reader.SetScriptPosition(29.586); // 75%
            var threeQuarters = (LinearGradientBrush)reader.GetTextForeground(0, 8);
            Program.Check(Math.Abs(threeQuarters.GradientStops[1].Offset - 0.75) < 0.00001);

            Program.Check(reader.FocusVocabulary("growth"));
            Program.Check(Equals(reader.GetScriptVocabularyBackground(0, 19),
                reader.FindResource("SelectedVocabularyBrush")));
            Program.Check(Equals(reader.GetScriptVocabularyBackground(0, 11), Brushes.Transparent));
            Program.Check(ReferenceEquals(runs[2].Foreground, reader.GetTextForeground(0, 19)));

            reader.SetScriptPosition(30.116); // 50% through multiword unit
            var multiword = (LinearGradientBrush)reader.GetTextForeground(0, 11);
            Program.Check(ReferenceEquals(multiword, reader.GetTextForeground(0, 19)));
            Program.Check(Math.Abs(multiword.GradientStops[1].Offset - 0.5) < 0.00001);

            reader.SetVocabulary(["euro-zone", "growth", "economic"], true);
            var rebuiltRuns = ((Paragraph)reader.Document.Blocks.FirstBlock!).Inlines.OfType<Span>()
                .Single().Inlines.OfType<Run>().ToArray();
            Program.Check(rebuiltRuns.Length == 3 && rebuiltRuns[0].Text == "euro-zone");
            Program.Check(reader.GetTextForeground(0, 19) is LinearGradientBrush);
            reader.SetScriptPosition(null);
            Program.Check(reader.GetTextForeground(0, 0) is not LinearGradientBrush);
            Program.Check(reader.GetTextForeground(0, 19) is not LinearGradientBrush);
        });

        Program.Run("Script gradient and vocabulary background remain independent across Run boundaries", () => InTemp((_, passage) =>
        {
            File.WriteAllText(ArticleRepository.ScriptPathFor(passage), Valid);
            var sentences = new ArticleRepository().LoadScript(passage).Paragraphs[0].Sentences;
            var reader = Reader(sentences, ["it is", "beautiful day"]);
            var original = reader.Document;
            Program.Check(reader.FocusVocabulary("beautiful day"));
            Program.Check(Equals(reader.GetScriptVocabularyBackground(0, 8),
                reader.FindResource("SelectedVocabularyBrush")));
            Program.Check(Equals(reader.GetScriptVocabularyBackground(0, 18),
                reader.FindResource("SelectedVocabularyBrush")));
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
        Program.Run("laid-out Script unit uses one absolute gradient for hyphenated text", () =>
        {
            var sentence = new ScriptSentence("euro-zone is one timed unit",
                [new ScriptTimedUnit("euro-zone", 29.226, 29.706, 0)]);
            var reader = Reader([sentence], ["euro-zone"]);
            reader.Width = 650;
            var host = new Window
            {
                Content = reader, Width = 750, Height = 220,
                Left = -10000, Top = -10000, ShowActivated = false
            };
            try
            {
                host.Show();
                WpfTestHelpers.Pump();
                reader.SetScriptPosition(29.466);
                var gradient = (LinearGradientBrush)reader.GetTextForeground(0, 0);
                var run = ((Paragraph)reader.Document.Blocks.FirstBlock!).Inlines.OfType<Span>()
                    .Single().Inlines.OfType<Run>().First();
                Rect first = run.ContentStart.GetCharacterRect(LogicalDirection.Forward);
                Rect last = run.ContentEnd.GetCharacterRect(LogicalDirection.Backward);
                Program.Check(gradient.MappingMode == BrushMappingMode.Absolute);
                Program.Check(Math.Abs(gradient.EndPoint.X - gradient.StartPoint.X -
                    (last.Left - first.Left)) < 1);
                Program.Check(ReferenceEquals(reader.GetTextForeground(0, 0),
                    reader.GetTextForeground(0, 8)));

                // Rebuilding for a vocabulary edit and reflowing a font size
                // must not leave the newly created Run on the pre-layout brush.
                reader.SetVocabulary(["euro-zone", "is"], true);
                WpfTestHelpers.Pump();
                reader.SetScriptPosition(29.466);
                Program.Check(((LinearGradientBrush)reader.GetTextForeground(0, 0)).MappingMode ==
                    BrushMappingMode.Absolute);
                reader.FontSize = 28;
                WpfTestHelpers.Pump();
                reader.SetScriptPosition(29.466);
                Program.Check(((LinearGradientBrush)reader.GetTextForeground(0, 0)).MappingMode ==
                    BrushMappingMode.Absolute);
            }
            finally { host.Close(); WpfTestHelpers.Pump(); }
        });

        Program.Run("wrapped Script unit shares one sweep and vocabulary background is visible", () =>
        {
            var sentence = new ScriptSentence("alpha beta gamma",
                [new ScriptTimedUnit("alpha beta gamma", 1.0, 2.0, 0)]);
            var reader = Reader([sentence], ["beta"]);
            reader.Width = 160;
            reader.FontSize = 26;
            var host = new Window
            {
                Content = reader, Width = 230, Height = 280,
                Left = -10000, Top = -10000, ShowActivated = false
            };
            try
            {
                host.Show();
                WpfTestHelpers.Pump();
                var run = ((Paragraph)reader.Document.Blocks.FirstBlock!).Inlines.OfType<Span>()
                    .Single().Inlines.OfType<Run>().Single();
                Rect first = run.ContentStart.GetCharacterRect(LogicalDirection.Forward);
                Rect last = run.ContentEnd.GetCharacterRect(LogicalDirection.Backward);
                Program.Check(last.Top > first.Top + 1);
                reader.SetScriptPosition(1.5);
                var paint = reader.GetTextForeground(0, 0);
                Program.Check(paint is DrawingBrush);
                Program.Check(ReferenceEquals(paint, reader.GetTextForeground(0, 7)));
                Program.Check(ReferenceEquals(paint, reader.GetTextForeground(0, 14)));
                var drawing = (DrawingBrush)paint;
                Program.Check(drawing.ViewboxUnits == BrushMappingMode.Absolute);
                Program.Check(drawing.ViewportUnits == BrushMappingMode.Absolute);
                Program.Check(drawing.Drawing is DrawingGroup { Children.Count: >= 2 });

                // Compare actual rendered pixels with and without the highlight,
                // not merely the brush returned by the lookup helper.
                reader.SetScriptPosition(null);
                byte[] withBackground = CapturePixels(reader, out int width, out int height);
                reader.SetVocabulary(["beta"], false);
                WpfTestHelpers.Pump();
                byte[] withoutBackground = CapturePixels(reader, out int sameWidth, out int sameHeight);
                Program.Check(width == sameWidth && height == sameHeight);
                var range = reader.Ranges[0];
                Rect wordStart = reader.PositionAt(range, 6).GetCharacterRect(LogicalDirection.Forward);
                Rect wordEnd = reader.PositionAt(range, 10).GetCharacterRect(LogicalDirection.Backward);
                Program.Check(Math.Abs(wordStart.Top - wordEnd.Top) < 1);
                int changes = 0;
                for (int y = Math.Max(0, (int)wordStart.Top); y < Math.Min(height, (int)wordStart.Bottom); y++)
                    for (int x = Math.Max(0, (int)wordStart.Left); x < Math.Min(width, (int)wordEnd.Left); x++)
                    {
                        int pixel = (y * width + x) * 4;
                        if (withBackground.AsSpan(pixel, 4).SequenceEqual(withoutBackground.AsSpan(pixel, 4))) continue;
                        changes++;
                    }
                Program.Check(changes > 0);
            }
            finally { host.Close(); WpfTestHelpers.Pump(); }
        });

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

    private static byte[] CapturePixels(ParagraphReader reader, out int width, out int height)
    {
        reader.UpdateLayout();
        width = (int)Math.Ceiling(reader.ActualWidth);
        height = (int)Math.Ceiling(reader.ActualHeight);
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(reader);
        var pixels = new byte[width * height * 4];
        bitmap.CopyPixels(pixels, width * 4, 0);
        return pixels;
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
