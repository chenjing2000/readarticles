using System.IO;
using ReadArticles.Models;
using ReadArticles.Services;

internal static class LazyTreeChecks
{
    internal static void Run()
    {
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
                File.WriteAllText(Path.Combine(deep, "article.json"), "not parsed");
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
                File.WriteAllText(first, "{}");
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
                    File.WriteAllText(Path.Combine(book, name), "{}");
                Directory.CreateDirectory(Path.Combine(book, "__pycache__"));
                Directory.CreateDirectory(Path.Combine(book, "userdata"));
                Directory.CreateDirectory(Path.Combine(book, ".private"));
                var found = new LibraryRepository().ScanDirectoryLevel(book, book);
                Program.Check(found.Entries.Count == 1 && found.Entries[0].Name == "Article");
            });
        });
        Program.Run("directory filtering hides cache always and audio only beside JSON files", () =>
        {
            InTemporaryLibrary(root =>
            {
                var withJson = Path.Combine(root, "WithJson");
                var withoutJson = Path.Combine(root, "WithoutJson");
                Directory.CreateDirectory(withJson);
                Directory.CreateDirectory(withoutJson);

                File.WriteAllText(Path.Combine(withJson, "metadata.JSON"), "not parsed");
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

                Program.Check(jsonEntries.Select(n => n.Name).OrderBy(n => n).SequenceEqual(
                    new[] { "cache_backup", "images", "my_cache" }.OrderBy(n => n)));
                Program.Check(nonJsonEntries.Select(n => n.Name).OrderBy(n => n).SequenceEqual(
                    new[] { "audio", "audios", "cache_backup", "images" }.OrderBy(n => n)));
            });
        });
        Program.Run("root-level cache and audio folders are filtered when applicable", () =>
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

                var result = new LibraryRepository().Load(root);
                Program.Check(result.Books.Count == 1 && result.Books[0].Name == "NormalBook");
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
