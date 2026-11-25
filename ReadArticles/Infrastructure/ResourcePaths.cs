using System.IO;
namespace ReadArticles.Infrastructure;

public static class ResourcePaths
{
    public static string Resolve(string root, string relative)
    {
        if (string.IsNullOrWhiteSpace(relative) || Path.IsPathRooted(relative) || relative.Contains(':'))
            throw new ArgumentException("Resource path must be a relative path.");
        string fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        string path = Path.GetFullPath(Path.Combine(fullRoot, relative.Replace('/', Path.DirectorySeparatorChar)));
        if (!path.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Resource path escapes the passage directory.");
        return RejectReparsePoints(path);
    }

    // Also used by JSON reads and writes. Missing final files are allowed,
    // but existing ancestors (including dangling links) must be checked.
    public static string RejectReparsePoints(string path)
    {
        string absolute = Path.GetFullPath(path);
        for (string? current = absolute; current is not null; current = Path.GetDirectoryName(current))
        {
            try
            {
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                    throw new ArgumentException("Resource path traverses a reparse point.");
            }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
        }
        return absolute;
    }
}
