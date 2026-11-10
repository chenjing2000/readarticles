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
    }

    private static void InTemporaryLibrary(Action<string> test)
    {
        string root = Path.Combine(Path.GetTempPath(), "ReadArticles-lazy-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try { test(root); }
        finally { Directory.Delete(root, true); }
    }
}
