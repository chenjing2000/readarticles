using System.IO;
using ReadArticles.Models;
using ReadArticles.Infrastructure;
using ReadArticles.Services;

internal static class LazyTreeChecks
{
    internal static void Run()
    {
        Program.Run("excluded folder names parse and validate without losing internal characters", () =>
        {
            var names = ReaderSettings.ParseExcludedFolders(" audio , video ; player-result ，结果 ； AUDIO ;; my audio (2) ");
            Program.Check(names.SetEquals(new[] { "audio", "video", "player-result", "结果", "my audio (2)" }) &&
                names.Count == 5 && names.Contains("AUDIO"));
            Program.Check(ReaderSettings.ParseExcludedFolders(" ;，, ").Count == 0);
            foreach (string invalid in new[] { "audio*", @"C:\audio", @"audio\video", "..", "name.", "NUL.txt", "COM¹", "a/b" })
            {
                bool rejected = false;
                try { ReaderSettings.ParseExcludedFolders(invalid); }
                catch (ArgumentException) { rejected = true; }
                Program.Check(rejected);
            }
            var original = new ReaderSettings();
            var draft = original.Copy();
            draft.ExcludedFolders = "";
            Program.Check(original.ExcludedFolders == ReaderSettings.DefaultExcludedFolders);
            original.Apply(draft);
            Program.Check(original.ExcludedFolders == "");
            var legacy = System.Text.Json.JsonSerializer.Deserialize<ReaderSettings>("""{"Appearance":{},"Audio":{}}""")!;
            Program.Check(legacy.ExcludedFolders == ReaderSettings.DefaultExcludedFolders);
        });
        Program.Run("root discovery does not recurse into books or parse their JSON", () =>
        {
            InTemporaryLibrary(root =>
            {
                var book = Path.Combine(root, "Book");
                Directory.CreateDirectory(book);
                File.WriteAllText(Path.Combine(book, "book.json"), "not-json");
                var deep = Path.Combine(book, "date", "nested");
                Directory.CreateDirectory(deep);
                File.WriteAllText(Path.Combine(deep, "article.json"), "invalid");
                var repository = new LibraryRepository();
                var result = repository.Load(root);
                Program.Check(result.Books.Count == 1 && !result.Books[0].IsPassage);
                Program.Check(result.Books[0].LoadState == NodeLoadState.NotLoaded);
                Program.Check(!result.Books[0].Children.Any(n => n.IsPassage));
            });
        });
        Program.Run("one-level scan includes empty folders without recursing", () =>
        {
            InTemporaryLibrary(root =>
            {
                var book = Path.Combine(root, "Book");
                var empty = Path.Combine(book, "empty");
                var deep = Path.Combine(empty, "deep");
                Directory.CreateDirectory(deep);
                File.WriteAllText(Path.Combine(book, "book.json"), "marker");
                File.WriteAllText(Path.Combine(deep, "article.json"), """{"filetype":"passage"}""");
                var repository = new LibraryRepository();
                var first = repository.ScanDirectoryLevel(book, book);
                Program.Check(first.IsComplete && first.Entries.Count == 1);
                Program.Check(first.Entries[0].Name == "empty" &&
                    first.Entries[0].LoadState == NodeLoadState.NotLoaded);
                var second = repository.ScanDirectoryLevel(empty, book);
                Program.Check(second.Entries.Count == 1 && second.Entries[0].Name == "deep");
                var third = repository.ScanDirectoryLevel(deep, book);
                Program.Check(third.Entries.Count == 1 && third.Entries[0].IsPassage);
            });
        });
        Program.Run("one-level refresh observes added deleted and renamed articles", () =>
        {
            InTemporaryLibrary(root =>
            {
                var book = Path.Combine(root, "Book");
                Directory.CreateDirectory(book);
                var repository = new LibraryRepository();
                string first = Path.Combine(book, "First.json");
                string second = Path.Combine(book, "Second.json");
                File.WriteAllText(first, """{"filetype":"passage"}""");
                Program.Check(repository.ScanDirectoryLevel(book, book).Entries.Single().Name == "First");
                File.Move(first, second);
                Program.Check(repository.ScanDirectoryLevel(book, book).Entries.Single().Name == "Second");
                File.Delete(second);
                Program.Check(repository.ScanDirectoryLevel(book, book).Entries.Count == 0);
            });
        });
        Program.Run("navigation excludes companion JSON and hidden branches", () =>
        {
            InTemporaryLibrary(root =>
            {
                var book = Path.Combine(root, "Book");
                Directory.CreateDirectory(book);
                foreach (var name in new[] { "book.json", "One.vocabulary.json", "One.exercise.json", "Article.json" })
                    File.WriteAllText(Path.Combine(book, name), name == "Article.json" ? """{"filetype":"passage"}""" : "{}");
                Directory.CreateDirectory(Path.Combine(book, "__pycache__"));
                Directory.CreateDirectory(Path.Combine(book, "userdata"));
                Directory.CreateDirectory(Path.Combine(book, ".private"));
                var found = new LibraryRepository().ScanDirectoryLevel(book, book);
                Program.Check(found.Entries.Count == 1 && found.Entries[0].Name == "Article");
            });
        });
        Program.Run("default directory exclusions match complete names regardless of JSON", () =>
        {
            InTemporaryLibrary(root =>
            {
                var withJson = Path.Combine(root, "WithJson");
                var withoutJson = Path.Combine(root, "WithoutJson");
                Directory.CreateDirectory(withJson);
                Directory.CreateDirectory(withoutJson);

                File.WriteAllText(Path.Combine(withJson, "metadata.JSON"), """{"filetype":"passage"}""");
                foreach (var name in new[]
                {
                    "audio", "Audio", "audios", "audio_backup", "generated_audio",
                    "myaudio", "images", "cache", "Cache", "cache_backup", "my_cache"
                })
                    Directory.CreateDirectory(Path.Combine(withJson, name));

                foreach (var name in new[] { "audio", "audios", "images", "cache", "cache_backup" })
                    Directory.CreateDirectory(Path.Combine(withoutJson, name));

                var repository = new LibraryRepository();
                var jsonEntries = repository.ScanDirectoryLevel(withJson, withJson).Entries;
                var nonJsonEntries = repository.ScanDirectoryLevel(withoutJson, withoutJson).Entries;

                Program.Check(jsonEntries.Count(n => n.IsPassage) == 1 &&
                    jsonEntries.Single(n => n.IsPassage).Name == "metadata");
                Program.Check(jsonEntries.Where(n => !n.IsPassage).Select(n => n.Name)
                    .OrderBy(n => n).SequenceEqual(
                        new[] { "audios", "audio_backup", "generated_audio", "myaudio", "cache_backup", "images", "my_cache" }.OrderBy(n => n)));
                Program.Check(nonJsonEntries.Where(n => !n.IsPassage).Select(n => n.Name)
                    .OrderBy(n => n).SequenceEqual(
                        new[] { "audios", "cache_backup", "images" }.OrderBy(n => n)));
            });
        });
        Program.Run("only direct JSON objects tagged exactly passage become leaves", () =>
        {
            InTemporaryLibrary(root =>
            {
                string parent = Path.Combine(root, "Book");
                Directory.CreateDirectory(parent);
                Directory.CreateDirectory(Path.Combine(parent, "Nested"));
                File.WriteAllText(Path.Combine(parent, "Valid.JSON"), """{"filetype":"passage"}""");
                File.WriteAllText(Path.Combine(parent, "Script.json"), """{"filetype":"script"}""");
                File.WriteAllText(Path.Combine(parent, "Missing.json"), """{"title":"Not a passage"}""");
                File.WriteAllText(Path.Combine(parent, "WrongCase.json"), """{"filetype":"Passage"}""");
                File.WriteAllText(Path.Combine(parent, "WrongType.json"), """{"filetype":5}""");
                File.WriteAllText(Path.Combine(parent, "NestedType.json"), """{"metadata":{"filetype":"passage"}}""");
                File.WriteAllText(Path.Combine(parent, "Broken.json"), "{not valid json");
                File.WriteAllText(Path.Combine(parent, "ignored.txt"), """{"filetype":"passage"}""");
                File.WriteAllText(Path.Combine(parent, "Nested", "Deep.json"), """{"filetype":"passage"}""");

                var repository = new LibraryRepository();
                var direct = repository.ScanDirectoryLevel(parent, parent);
                Program.Check(direct.IsComplete && direct.Warnings.Count == 0);
                Program.Check(direct.Entries.Count == 2);
                Program.Check(direct.Entries.Any(n => n.IsPassage && n.Name == "Valid"));
                var folder = direct.Entries.Single(n => !n.IsPassage);
                Program.Check(folder.Name == "Nested" &&
                    folder.LoadState == NodeLoadState.NotLoaded &&
                    !folder.Children.Any(n => n.IsPassage));
                var expanded = repository.ScanDirectoryLevel(folder.DirectoryPath!, parent);
                Program.Check(expanded.Entries.Count == 1 &&
                    expanded.Entries[0].Name == "Deep" &&
                    expanded.Entries[0].IsPassage);
            });
        });
        Program.Run("custom directory exclusions apply at root and every expanded level", () =>
        {
            InTemporaryLibrary(root =>
            {
                File.WriteAllText(Path.Combine(root, "catalog.json"), "not parsed");
                foreach (var name in new[] { "AudioBook", "Cache", "NormalBook" })
                {
                    string book = Path.Combine(root, name);
                    Directory.CreateDirectory(book);
                    File.WriteAllText(Path.Combine(book, "book.json"), "marker");
                }

                var repository = new LibraryRepository();
                var filter = ReaderSettings.ParseExcludedFolders("audiobook ; 结果， video");
                var result = repository.Load(root, filter);
                Program.Check(result.Books.Select(n => n.Name).SequenceEqual(new[] { "Cache", "NormalBook" }));
                var nested = Path.Combine(root, "NormalBook", "nested");
                Directory.CreateDirectory(Path.Combine(nested, "Video"));
                Directory.CreateDirectory(Path.Combine(nested, "结果"));
                Directory.CreateDirectory(Path.Combine(nested, "video-files"));
                Directory.CreateDirectory(Path.Combine(nested, "audio"));
                Directory.CreateDirectory(Path.Combine(nested, "userdata"));
                File.WriteAllText(Path.Combine(nested, "video.json"), """{"filetype":"passage"}""");
                var entries = repository.ScanDirectoryLevel(nested, Path.Combine(root, "NormalBook"), filter).Entries;
                Program.Check(entries.Count == 3 && entries.Any(n => n.Name == "audio") &&
                    entries.Any(n => n.Name == "video-files") && entries.Any(n => n.Name == "video" && n.IsPassage));
                Program.Check(repository.Load(root, ReaderSettings.ParseExcludedFolders("")).Books.Count == 3);
            });
        });
    }

    private static void InTemporaryLibrary(Action<string> test)
    {
        string root = Path.Combine(Path.GetTempPath(), "ReadArticles-lazy-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try { test(root); }
        finally { Directory.Delete(root, true); }
    }
}
