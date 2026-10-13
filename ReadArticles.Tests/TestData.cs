using System.IO;
using ReadArticles.Models;
using ReadArticles.Rendering;

internal static class TestData
{
    internal static string LibraryRoot => Path.GetFullPath(Environment.GetEnvironmentVariable("READARTICLES_TEST_LIBRARY") ?? "ReadArticles.Tests/Fixtures/Library");
    internal static string ArticlePath => Path.Combine(LibraryRoot, "The Economist", "2026-09-26", "When America walks away", "When America walks away.json");

    internal static ParagraphReader Reader() => new ParagraphReader(
        new[] { new Segment("s001", "public life."), new Segment("s002", "public life.") },
        new[] { "public", "public life" });

    internal static IEnumerable<NavigationNode> Leaves(IEnumerable<NavigationNode> nodes)
    {
        foreach (var node in nodes)
        {
            if (node.IsPassage) yield return node;
            else
                foreach (var leaf in Leaves(node.Children)) yield return leaf;
        }
    }
}
