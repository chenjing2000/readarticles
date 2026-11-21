using System.IO;
using System.Text.Json;
using ReadArticles.Services;
using ReadArticles.Models;
using ReadArticles.ViewModels;
using ReadArticles.Infrastructure;

internal static class ContentChecks
{
    internal static void Run()
    {
        RunCopyTest("concurrent vocabulary saves reject stale writers instead of losing edits", (root, passage) =>
        {
            foreach (bool existing in new[] { false, true })
            {
                string path = Path.Combine(root, existing ? "existing.json" : "new.json");
                var repository = new VocabularyRepository();
                string? fingerprint = existing ? repository.Save(path, new VocabularyData(), null) : null;
                using var start = new Barrier(8);
                var tasks = Enumerable.Range(0, 8).Select(writer => Task.Factory.StartNew(() =>
                {
                    var data = new VocabularyData
                    {
                        Words = Enumerable.Range(0, 500).Select(index => new VocabularyEntry
                        {
                            Word = $"writer {writer} word {index}",
                            Meanings = [new Meaning("n.", new string('x', 2000))]
                        }).ToList()
                    };
                    start.SignalAndWait();
                    string target = writer % 2 == 0 ? path.ToUpperInvariant() : Path.GetRelativePath(Environment.CurrentDirectory, path);
                    try { repository.Save(target, data, fingerprint); return true; }
                    catch (InvalidOperationException) { return false; }
                }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default)).ToArray();
                Task.WaitAll(tasks);
                int committed = tasks.Count(task => task.Result);
                if (committed != 1) throw new Exception($"Expected one committed writer; got {committed} (existing={existing}).");
                Program.Check(repository.Load(path).Data.Words.Count == 500);
                Program.Check(!Directory.EnumerateFiles(root, "*.tmp").Any());
            }
        });
        Program.Run("delete without an article or with a stale word leaves workspace unchanged", () =>
        {
            using var first = new WpfAudioPlayer();
            using var second = new WpfAudioPlayer();
            var model = new MainViewModel(new PlaybackController(first), new PlaybackController(second));
            var stale = VocabularyRepository.NewEntry("stale word");
            Program.Check(model.TryDeleteWord(stale) == VocabularyEditResult.Unavailable);
            Program.Check(model.Article == null && model.Notifications.Messages.Count == 0);
            Program.Check(model.OpenArticle(TestData.ArticlePath));
            string before = JsonFiles.Fingerprint(model.Article!.VocabularyPath)!;
            Program.Check(model.TryDeleteWord(stale) == VocabularyEditResult.Unavailable);
            Program.Check(JsonFiles.Fingerprint(model.Article.VocabularyPath) == before);
        });
        Program.Run("real magazine: 13 paragraphs, 62 sentences and 120 complete words", () =>
        {
            var article = new ArticleRepository().Load(TestData.ArticlePath);
            Program.Check(article.Title == "When America walks away" && article.Paragraphs.Count == 13 && article.Paragraphs.Sum(p => p.Length) == 62);
            Program.Check(article.Vocabulary.Data.Words.Count == 120 && article.Paragraphs.Last().Last().Length > 0);
            Program.Check(article.Vocabulary.Data.Words[0].Word == "be stuck in" && article.Vocabulary.Data.Words[0].Meanings[0].Text.Contains("困在"));
        });
        Program.Run("lazy root discovery and explicit one-level traversal", () =>
        {
            var repo = new LibraryRepository();
            var library = repo.Load(TestData.LibraryRoot);
            Program.Check(library.Books.Count == 1 && library.Books[0].Name == "The Economist");
            Program.Check(!TestData.Leaves(library.Books).Any());
            var dates = repo.ScanDirectoryLevel(library.Books[0].DirectoryPath!, library.Books[0].BookRoot);
            Program.Check(dates.IsComplete && dates.Entries.Any(n => n.Name == "2026-09-26"));
            var date = dates.Entries.Single(n => n.Name == "2026-09-26");
            var folders = repo.ScanDirectoryLevel(date.DirectoryPath!, date.BookRoot);
            var folder = folders.Entries.Single(n => n.Name == "When America walks away");
            var article = repo.ScanDirectoryLevel(folder.DirectoryPath!, folder.BookRoot);
            Program.Check(article.Entries.Any(n => n.PassagePath == TestData.ArticlePath));
        });
        RunCopyTest("missing title rejects passage without losing current article", (root, passage) =>
        {
            var json = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(passage))!;
            json.AsObject().Remove("title");
            File.WriteAllText(passage, json.ToJsonString());
            var workspace = new ReaderWorkspace();
            workspace.OpenArticle(TestData.ArticlePath);
            var previous = workspace.Article;
            ExpectInvalid(() => workspace.OpenArticle(passage));
            Program.Check(ReferenceEquals(previous, workspace.Article));
        });
        RunCopyTest("empty title is valid and file name does not override it", (root, passage) =>
        {
            var json = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(passage))!;
            json["title"] = "";
            File.WriteAllText(passage, json.ToJsonString());
            var article = new ArticleRepository().Load(passage);
            Program.Check(article.Title == "" && article.Paragraphs.Sum(p => p.Length) == 62);
        });
        RunCopyTest("empty passage segments are rejected with a data error and preserve the current article", (root, passage) =>
        {
            var json = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(passage))!;
            foreach (var paragraph in json["paragraphs"]!.AsArray())
                paragraph!.AsArray().Clear();
            File.WriteAllText(passage, json.ToJsonString());
            var workspace = new ReaderWorkspace();
            workspace.OpenArticle(TestData.ArticlePath);
            var previous = workspace.Article;
            try
            {
                workspace.OpenArticle(passage);
                throw new Exception("Empty passage was accepted.");
            }
            catch (InvalidDataException error)
            {
                Program.Check(error.Message.Contains("非空句子数组", StringComparison.Ordinal));
            }
            Program.Check(ReferenceEquals(previous, workspace.Article));
        });
        RunCopyTest("bad optional vocabulary preserves readable article and blocks overwrite", (root, passage) =>
        {
            File.WriteAllText(Path.Combine(root, "Test.vocabulary.json"), "broken");
            var article = new ArticleRepository().Load(passage);
            Program.Check(article.Paragraphs.Count == 13 && article.Warnings.Count > 0 && !article.Vocabulary.CanWrite);
            File.Delete(Path.Combine(root, "Test.vocabulary.json"));
        });
        RunCopyTest("null vocabulary item degrades companion without hiding passage", (root, passage) =>
        {
            string path = Path.Combine(root, "Test.vocabulary.json");
            foreach (string invalid in new[] { "{\"filetype\":\"vocabulary\",\"words\":[null]}", "{\"filetype\":\"vocabulary\",\"words\":[{\"word\":\"test\",\"meanings\":[null]}]}" })
            {
                File.WriteAllText(path, invalid);
                var article = new ArticleRepository().Load(passage);
                Program.Check(article.Paragraphs.Count == 13 && !article.Vocabulary.CanWrite);
            }
            File.Delete(path);
        });
        RunCopyTest("oversized placeholder is rejected without crashing article switch", (root, passage) =>
        {
            string invalid = Path.Combine(root, "Overflow.json");
            var json = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(passage))!;
            json["paragraphs"]![0]![0] = "Invalid [[2147483648]] placeholder";
            File.WriteAllText(invalid, json.ToJsonString());
            var workspace = new ReaderWorkspace();
            workspace.OpenArticle(TestData.ArticlePath);
            var previous = workspace.Article;
            ExpectInvalid(() => workspace.OpenArticle(invalid));
            Program.Check(ReferenceEquals(previous, workspace.Article));
        });
        RunCopyTest("nested vocabulary metadata survives atomic roundtrip", (root, passage) =>
        {
            string path = Path.Combine(root, "metadata.json");
            var json = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(Path.ChangeExtension(TestData.ArticlePath, ".vocabulary.json")))!;
            json["words"]![0]!["meanings"]![0]!["source"] = "kept";
            json["words"]![0]!["audio"] = new System.Text.Json.Nodes.JsonObject { ["provider"] = "discarded" };
            File.WriteAllText(path, json.ToJsonString());
            var repository = new VocabularyRepository();
            var snapshot = repository.Load(path);
            repository.Save(path, snapshot.Data, snapshot.Fingerprint);
            var result = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(path))!;
            Program.Check((string?)result["words"]![0]!["meanings"]![0]!["source"] == "kept" && result["words"]![0]!["audio"] == null);
        });
        RunCopyTest("vocabulary copy-only roundtrip and external edit detection", (root, passage) =>
        {
            var repository = new VocabularyRepository();
            string path = Path.Combine(root, "words.json");
            File.Copy(Path.ChangeExtension(TestData.ArticlePath, ".vocabulary.json"), path);
            var snapshot = repository.Load(path);
            var added = VocabularyRepository.NewEntry("  testing   phrase  ");
            var data = new VocabularyData { Words = [.. snapshot.Data.Words, added], Extra = snapshot.Data.Extra };
            var fingerprint = repository.Save(path, data, snapshot.Fingerprint);
            Program.Check(repository.Load(path).Data.Words[^1].Word == "testing phrase" && !File.ReadAllText(path).Contains("\"audio\"" ));
            File.AppendAllText(path, " ");
            ExpectInvalid(() => repository.Save(path, data, fingerprint));
        });
        RunCopyTest("marker JSON is never parsed, hidden and userdata branches pruned", (root, passage) =>
        {
            string book = Path.Combine(root, "library", "Book");
            Directory.CreateDirectory(book);
            File.WriteAllText(Path.Combine(book, "book.json"), "not-json");
            File.Copy(passage, Path.Combine(book, "Reading.json"));
            foreach (string ignored in new[] { "UserData", ".hidden", "__PYCACHE__" })
            {
                Directory.CreateDirectory(Path.Combine(book, ignored));
                File.Copy(passage, Path.Combine(book, ignored, "Ignored.json"));
            }
            var result = new LibraryRepository().Load(Path.GetDirectoryName(book)!);
            Program.Check(result.Books.Count == 1 && !TestData.Leaves(result.Books).Any());
            var direct = new LibraryRepository().ScanDirectoryLevel(book, book);
            Program.Check(direct.Entries.Count(n => n.IsPassage) == 1);
            Program.Check(File.ReadAllText(Path.Combine(book, "book.json")) == "not-json");
            using var player = new ReadArticles.Infrastructure.WpfAudioPlayer();
            using var pronunciationPlayer = new WpfAudioPlayer();
            var model = new ReadArticles.ViewModels.MainViewModel(new PlaybackController(player), new PlaybackController(pronunciationPlayer));
            Program.Check(model.OpenLibrary(Path.GetDirectoryName(book)!));
            Program.Check(model.OpenArticle(TestData.ArticlePath));
            int notifications = 0;
            model.ArticleChanged += () => notifications++;
            Program.Check(model.OpenArticle(Path.Combine(book, "Reading.json")));
            Program.Check(model.ArticleTitle == "When America walks away" && notifications == 1);
        });
        RunCopyTest("normal edits stay quiet and article switches clear previous notifications", (root, passage) =>
        {
            using var player = new WpfAudioPlayer();
            using var pronunciationPlayer = new WpfAudioPlayer();
            var model = new MainViewModel(new PlaybackController(player), new PlaybackController(pronunciationPlayer));
            Program.Check(model.Notifications.Messages.Count == 0);
            Program.Check(model.OpenLibrary(TestData.LibraryRoot) && model.Notifications.Messages.Count == 0);
            File.WriteAllText(passage, """{"filetype":"passage","title":"","paragraphs":[["Character [[1]] matters."]]}""");
            Program.Check(model.OpenArticle(passage) && model.Notifications.Messages.Count == 0);
            Program.Check(model.TryAddWord("alpha") == VocabularyEditResult.Success);
            Program.Check(model.Words.Count == 1 && model.Notifications.Messages.Count == 0);
            Program.Check(model.TryAddWord("beta") == VocabularyEditResult.Success);
            Program.Check(model.Words.Count == 2 && model.Notifications.Messages.Count == 0);
            Program.Check(model.TryMoveWord(model.Words[1].Entry, -1) == VocabularyEditResult.Success);
            Program.Check(model.Words[0].Word == "beta" && model.Notifications.Messages.Count == 0);
            Program.Check(model.TryDeleteWord(model.Words[0].Entry) == VocabularyEditResult.Success);
            Program.Check(model.Words.Count == 1 && model.Notifications.Messages.Count == 0);
            Program.Check(model.TryAddWord("alpha") == VocabularyEditResult.Duplicate);
            Program.Check(model.Words.Count == 1 && model.Notifications.Messages.Any(n =>
                n.Key == "DuplicateVocabulary" && n.Message.Contains("已存在") &&
                n.Severity == NotificationSeverity.Warning));
            var duplicateWarning = model.Notifications.Messages.Single(n => n.Key == "DuplicateVocabulary");
            Program.Check(model.TryImportVocabulary(Path.ChangeExtension(TestData.ArticlePath, ".vocabulary.json")) ==
                VocabularyEditResult.Success);
            Program.Check(model.Words.Count == 120 &&
                model.Notifications.Messages.Any(n => n.Id == duplicateWarning.Id));
            Program.Check(model.TryAddWord("gamma") == VocabularyEditResult.Success);
            Program.Check(model.Words.Count == 121 &&
                model.Notifications.Messages.Any(n => n.Id == duplicateWarning.Id));
            File.AppendAllText(Path.ChangeExtension(passage, ".vocabulary.json"), " ");
            Program.Check(model.TryDeleteWord(model.Words[0].Entry) == VocabularyEditResult.Failed);
            Program.Check(model.Words.Count == 121 && model.Notifications.Messages.Any(n =>
                n.Key == "UserOperationFailed" && n.Severity == NotificationSeverity.Error));
            // A successful article switch clears both displayed and queued
            // notifications, including old errors.
            Program.Check(model.OpenArticle(TestData.ArticlePath) &&
                model.Notifications.Messages.Count == 0);
            // Reopening a library also clears messages from the old reading
            // session. A subsequently failed article open reports one error.
            Program.Check(model.OpenLibrary(TestData.LibraryRoot) &&
                model.Notifications.Messages.Count == 0);
            Program.Check(!model.OpenArticle(Path.Combine(root, "Missing.json")) &&
                model.Notifications.Messages.Count(n => n.Key == "UserOperationFailed" &&
                    n.Severity == NotificationSeverity.Error) == 1);
        });
        RunCopyTest("import and later edits preserve vocabulary metadata and notify once per commit", (root, passage) =>
        {
            string incoming = Path.Combine(root, "incoming.json");
            var json = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(Path.ChangeExtension(TestData.ArticlePath, ".vocabulary.json")))!;
            json["provider"] = "imported metadata";
            File.WriteAllText(incoming, json.ToJsonString());
            using var player = new WpfAudioPlayer();
            using var pronunciationPlayer = new WpfAudioPlayer();
            var model = new MainViewModel(new PlaybackController(player), new PlaybackController(pronunciationPlayer));
            Program.Check(model.OpenArticle(passage));
            int notifications = 0;
            model.VocabularyChanged += () => notifications++;
            Program.Check(model.TryImportVocabulary(incoming) == VocabularyEditResult.Success);
            Program.Check(notifications == 1 && model.Words.Count == 120);
            Program.Check(model.TryAddWord("fresh phrase") == VocabularyEditResult.Success);
            Program.Check(notifications == 2 && model.Words.Count == 121);
            var saved = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(Path.ChangeExtension(passage, ".vocabulary.json")))!;
            Program.Check((string?)saved["provider"] == "imported metadata");
            Program.Check((string?)saved["words"]![0]!["word"] == "be stuck in");
        });
    }
    private static void RunCopyTest(string name, Action<string, string> test)
    {
        Program.Run(name, () =>
        {
            string root = Path.Combine(Path.GetTempPath(), "ReadArticles-content-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                string passage = Path.Combine(root, "Test.json");
                File.Copy(TestData.ArticlePath, passage);
                test(root, passage);
            }
            finally { Directory.Delete(root, true); }
        });
    }
    private static void ExpectInvalid(Action action)
    {
        try { action(); }
        catch (Exception e) when (e is InvalidDataException or JsonException or InvalidOperationException) { return; }
        throw new Exception("Invalid input was accepted");
    }
}
