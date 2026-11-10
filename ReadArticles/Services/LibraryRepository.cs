using System.IO;
using ReadArticles.Models;

namespace ReadArticles.Services;

/// <summary>Discovers navigation entries without opening passage or audio content.</summary>
public sealed class LibraryRepository
{
    public LibrarySnapshot Load(string root)
    {
        string absolute = Path.GetFullPath(root);
        if (!Directory.Exists(absolute))
            throw new DirectoryNotFoundException("图书馆目录不存在。");
        var warnings = new List<string>();
        var books = new List<NavigationNode>();
        try
        {
            foreach (var child in Directory.EnumerateDirectories(absolute))
            {
                if (ShouldSkip(child, warnings)) continue;
                try
                {
                    if (!File.Exists(Path.Combine(child, "book.json"))) continue;
                    books.Add(new NavigationNode(Path.GetFileName(child), child, null,
                        Array.Empty<NavigationNode>(), child));
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                    warnings.Add("某个书籍目录无法读取。");
                }
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // The root must be usable; an incomplete initial book list is not a
            // successful library open.
            throw new IOException("图书馆根目录无法完整读取。", e);
        }
        return new LibrarySnapshot(absolute,
            books.OrderBy(n => n.Name, StringComparer.OrdinalIgnoreCase).ToList(), warnings);
    }

    public DirectoryScanResult ScanDirectoryLevel(string directory, string bookRoot)
    {
        var nodes = new List<NavigationNode>();
        var warnings = new List<string>();
        bool complete = true;
        // Separate enumerators allow sibling directories to survive a file
        // enumeration failure (and vice versa). A faulted enumerator is never resumed.
        try
        {
            foreach (var file in Directory.EnumerateFiles(directory, "*.json"))
            {
                if (ShouldSkip(file, warnings)) continue;
                string name = Path.GetFileName(file);
                if (name.Equals("book.json", StringComparison.OrdinalIgnoreCase) ||
                    name.EndsWith(".vocabulary.json", StringComparison.OrdinalIgnoreCase) ||
                    name.EndsWith(".exercise.json", StringComparison.OrdinalIgnoreCase)) continue;
                try
                {
                    nodes.Add(new NavigationNode(Path.GetFileNameWithoutExtension(file), bookRoot,
                        Path.GetFullPath(file), Array.Empty<NavigationNode>()));
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                    warnings.Add("某个正文文件无法读取。");
                }
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            complete = false;
            warnings.Add("当前目录的正文列表未能完整读取。");
        }

        try
        {
            foreach (var child in Directory.EnumerateDirectories(directory))
            {
                if (ShouldSkip(child, warnings)) continue;
                try
                {
                    nodes.Add(new NavigationNode(Path.GetFileName(child), bookRoot,
                        null, Array.Empty<NavigationNode>(), Path.GetFullPath(child)));
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                    warnings.Add("某个子目录无法读取。");
                }
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            complete = false;
            warnings.Add("当前目录的子目录列表未能完整读取。");
        }
        var ordered = nodes.OrderBy(n => n.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(n => n.IsPassage).ToList();
        return new DirectoryScanResult(ordered, warnings, complete);
    }

    private static bool ShouldSkip(string path, List<string> warnings)
    {
        string name = Path.GetFileName(path);
        if (name.StartsWith('.') ||
            name.Equals("userdata", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("__pycache__", StringComparison.OrdinalIgnoreCase))
            return true;
        try
        {
            var attributes = File.GetAttributes(path);
            return (attributes & (FileAttributes.ReparsePoint | FileAttributes.Hidden)) != 0;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            warnings.Add("某个目录项无法读取，已跳过。");
            return true;
        }
    }
}
