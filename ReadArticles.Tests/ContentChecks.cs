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
        Program.Run("delete without an article or with a stale word leaves workspace unchanged", () =>
        {
            using var first = new WpfAudioPlayer();
            using var second = new WpfAudioPlayer();
            var model = new MainViewModel(new PlaybackController(first), new PlaybackController(second));
            var stale = VocabularyRepository.NewEntry("stale word");
            model.DeleteWord(stale);
            Program.Check(model.Article == null && model.Status == "");
            Program.Check(model.OpenArticle(TestData.ArticlePath));
            string before = JsonFiles.Fingerprint(model.Article!.VocabularyPath)!;
            model.DeleteWord(stale);
            Program.Check(JsonFiles.Fingerprint(model.Article.VocabularyPath) == before);
        });
        Program.Run("real magazine: 13 paragraphs, 62 stable SID and 120 complete words", () =>
        {
            var article = new ArticleRepository().Load(TestData.ArticlePath);
            Program.Check(article.Title == "When America walks away" && article.Paragraphs.Count == 13 && article.Segments.Count() == 62);
            Program.Check(article.Vocabulary.Data.Words.Count == 120 && article.Segments.Last().Sid == "s062");
            Program.Check(article.Vocabulary.Data.Words[0].Word == "be stuck in" && article.Vocabulary.Data.Words[0].Meanings[0].Text.Contains("困在"));
        });
        Program.Run("recursive real navigation and marker-only book.json", () =>
        {
            var library = new LibraryRepository().Load(TestData.LibraryRoot);
            Program.Check(library.Books.Count == 1 && library.Books[0].Name == "The Economist");
            Program.Check(TestData.Leaves(library.Books).Count() == 2 && TestData.Leaves(library.Books).Any(n => n.PassagePath == TestData.ArticlePath));
        });
        Program.Run("segment audio uses audio_segments with exact SID/accent", () =>
        {
            var article = new ArticleRepository().Load(TestData.ArticlePath);
            var resources = new AudioResources();
            var missing = new List<string>();
            foreach (var segment in article.Segments)
                foreach (var accent in new[] { "uk", "us" })
                {
                    var asset = resources.Segment(article.Directory, segment, accent);
                    if (!AudioResources.IsAvailable(asset)) { missing.Add($"{segment.Sid}_{accent}"); continue; }
                    Program.Check(Path.GetFileName(Path.GetDirectoryName(asset)) == "audio_segments");
                    Program.Check(Path.GetFileName(asset) == $"{segment.Sid}_{accent}.mp3");
                }
            Program.Check(missing.SequenceEqual(new[] { "s011_uk", "s011_us" }));
            Program.Check(article.Warnings.Any(w => w.Contains("缺失 2")));
            foreach (var word in article.Vocabulary.Data.Words)
                foreach (var accent in new[] { "uk", "us" })
                    Program.Check(AudioResources.IsAvailable(resources.Vocabulary(article.Directory, word, accent)));
        });

        RunCopyTest("duplicate SID invalid and failed switch preserves workspace", (root, passage) =>
        {
            var workspace = new ReaderWorkspace();
            workspace.OpenArticle(TestData.ArticlePath);
            var previous = workspace.Article;
            string invalid = Path.Combine(root, "Invalid.json");
            File.WriteAllText(invalid, File.ReadAllText(passage).Replace("\"s002\"", "\"s001\""));
            ExpectInvalid(() => workspace.OpenArticle(invalid));
            Program.Check(ReferenceEquals(previous, workspace.Article));
        });
        RunCopyTest("SID with trailing newline is rejected before workspace commit", (root, passage) =>
        {
            var json = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(passage))!;
            var segment = json["paragraphs"]![0]!["paragraph"]![0]!;
            segment["sid"] = "s001\n";
            segment["audio"]!["uk"] = "audio_segments/s001\n_uk.mp3";
            segment["audio"]!["us"] = "audio_segments/s001\n_us.mp3";
            File.WriteAllText(passage, json.ToJsonString());
            var workspace = new ReaderWorkspace();
            workspace.OpenArticle(TestData.ArticlePath);
            var previous = workspace.Article;
            ExpectInvalid(() => workspace.OpenArticle(passage));
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
            foreach (string invalid in new[] { "{\"filetype\":\"vocabulary\",\"words\":[null]}", "{\"filetype\":\"vocabulary\",\"words\":[{\"word\":\"test\",\"meanings\":[null]}]}", "{\"filetype\":\"vocabulary\",\"words\":[{\"word\":\"test\",\"meanings\":[],\"audio\":null}]}" })
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
            json["paragraphs"]![0]!["paragraph"]![0]!["text"] = "Invalid [[2147483648]] placeholder";
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
            json["words"]![0]!["audio"]!["provider"] = "kept";
            File.WriteAllText(path, json.ToJsonString());
            var repository = new VocabularyRepository();
            var snapshot = repository.Load(path);
            repository.Save(path, snapshot.Data, snapshot.Fingerprint);
            var result = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(path))!;
            Program.Check((string?)result["words"]![0]!["meanings"]![0]!["source"] == "kept" && (string?)result["words"]![0]!["audio"]!["provider"] == "kept");
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
            Program.Check(repository.Load(path).Data.Words[^1].Audio.Uk == "audio_vocabulary/testing_phrase_uk.mp3");
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
            Program.Check(result.Books.Count == 1 && TestData.Leaves(result.Books).Count() == 1);
            Program.Check(File.ReadAllText(Path.Combine(book, "book.json")) == "not-json");
            File.WriteAllText(Path.Combine(book, "userdata", "xiaoxin.json"), "null");
            string user = Path.Combine(book, "userdata", "xiaoxin");
            Directory.CreateDirectory(user);
            File.WriteAllText(Path.Combine(user, "answer_sheet.json"), "null");
            using var player = new ReadArticles.Infrastructure.WpfAudioPlayer();
            using var pronunciationPlayer = new WpfAudioPlayer();
            var model = new ReadArticles.ViewModels.MainViewModel(new PlaybackController(player), new PlaybackController(pronunciationPlayer));
            Program.Check(model.OpenLibrary(Path.GetDirectoryName(book)!));
            Program.Check(model.OpenArticle(TestData.ArticlePath));
            int notifications = 0;
            model.ArticleChanged += () => notifications++;
            Program.Check(model.OpenArticle(Path.Combine(book, "Reading.json")));
            Program.Check(model.ArticleTitle == "Reading" && notifications == 1 && model.CurrentUser.Contains("无法读取"));
        });
        RunCopyTest("normal operations stay quiet while warnings and errors remain visible", (root, passage) =>
        {
            using var player = new WpfAudioPlayer();
            using var pronunciationPlayer = new WpfAudioPlayer();
            var model = new MainViewModel(new PlaybackController(player), new PlaybackController(pronunciationPlayer));
            Program.Check(model.Status == "");
            Program.Check(model.OpenLibrary(TestData.LibraryRoot) && model.Status == "");
            File.WriteAllText(passage, "{\"filetype\":\"passage\",\"next_sid\":2,\"paragraphs\":[{\"paragraph\":[{\"sid\":\"s001\",\"text\":\"Character [[1]] matters.\"}]}]}");
            Program.Check(model.OpenArticle(passage) && model.Status == "");
            model.AddSelection(new PassageSelection("s001", "alpha", 0, 5));
            Program.Check(model.Words.Count == 1 && model.Status == "");
            model.AddSelection(new PassageSelection("s001", "beta", 0, 4));
            Program.Check(model.Words.Count == 2 && model.Status == "");
            model.MoveWord(model.Words[1].Entry, -1);
            Program.Check(model.Words[0].Word == "beta" && model.Status == "");
            model.DeleteWord(model.Words[0].Entry);
            Program.Check(model.Words.Count == 1 && model.Status == "");
            model.AddSelection(new PassageSelection("s001", "alpha", 0, 5));
            Program.Check(model.Words.Count == 1 && model.Status.Contains("已存在"));
            model.Status = "";
            model.ImportVocabulary(Path.ChangeExtension(TestData.ArticlePath, ".vocabulary.json"));
            Program.Check(model.Words.Count == 120 && model.Status == "");
            model.Status = "warning: earlier failure";
            model.AddSelection(new PassageSelection("s001", "gamma", 0, 5));
            Program.Check(model.Words.Count == 121 && model.Status == "warning: earlier failure");
            File.AppendAllText(Path.ChangeExtension(passage, ".vocabulary.json"), " ");
            model.DeleteWord(model.Words[0].Entry);
            Program.Check(model.Words.Count == 121 && model.Status != "" && model.Status != "warning: earlier failure");
            Program.Check(model.OpenArticle(TestData.ArticlePath) && model.Status.Contains("缺失 2"));
            Program.Check(model.OpenLibrary(TestData.LibraryRoot) && model.Status == "");
            Program.Check(!model.OpenArticle(Path.Combine(root, "Missing.json")) && model.Status != "");
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
            model.ImportVocabulary(incoming);
            Program.Check(notifications == 1 && model.Words.Count == 120);
            model.AddSelection(new PassageSelection("s001", "fresh phrase", 0, 12));
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
            string root = Path.GetFullPath(Path.Combine("artifacts", "content-tests", Guid.NewGuid().ToString("N")));
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
