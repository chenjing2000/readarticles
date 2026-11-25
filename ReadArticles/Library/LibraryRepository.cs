using System.IO;
using System.Text.Json;
using ReadArticles.Models;
using ReadArticles.Infrastructure;

namespace ReadArticles.Services;

/// <summary>Discovers navigation entries without opening passage or audio content.</summary>
public sealed class LibraryRepository
{
    private static readonly IReadOnlySet<string> DefaultExclusions =
        ReaderSettings.ParseExcludedFolders(ReaderSettings.DefaultExcludedFolders);

    public LibrarySnapshot Load(string root, IReadOnlySet<string>? excludedFolders = null)
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
                if ((excludedFolders ?? DefaultExclusions).Contains(Path.GetFileName(child)) || ShouldSkip(child, warnings)) continue;
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

    public DirectoryScanResult ScanDirectoryLevel(string directory, string bookRoot,
        IReadOnlySet<string>? excludedFolders = null)
    {
        var nodes = new List<NavigationNode>();
        var warnings = new List<string>();
        bool complete = true;
        // Enumerate direct files once; only exact passage JSON becomes a leaf.
        try
        {
            foreach (var file in Directory.EnumerateFiles(directory))
            {
                if (!IsJsonFile(file)) continue;
                if (ShouldSkip(file, warnings)) continue;
                // Never traverse descendants here. Only direct JSON files whose
                // top-level filetype is exactly "passage" become tree leaves.
                try
                {
                    using var stream = File.OpenRead(file);
                    using var json = JsonDocument.Parse(stream);
                    if (json.RootElement.ValueKind != JsonValueKind.Object ||
                        !json.RootElement.TryGetProperty("filetype", out var type) ||
                        type.ValueKind != JsonValueKind.String ||
                        !string.Equals(type.GetString(), "passage", StringComparison.Ordinal))
                        continue;
                    nodes.Add(new NavigationNode(Path.GetFileNameWithoutExtension(file), bookRoot,
                        Path.GetFullPath(file), Array.Empty<NavigationNode>()));
                }
                catch (JsonException)
                {
                    // Invalid JSON cannot be a discoverable passage.
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                    complete = false;
                    warnings.Add("某个 JSON 文件无法读取。");
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
                if ((excludedFolders ?? DefaultExclusions).Contains(Path.GetFileName(child)) || ShouldSkip(child, warnings)) continue;
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

    private static bool IsJsonFile(string path) =>
        Path.GetExtension(path).Equals(".json", StringComparison.OrdinalIgnoreCase);

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
