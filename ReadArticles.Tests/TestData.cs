using System.IO;
using ReadArticles.Models;
using ReadArticles.Rendering;

internal static class TestData
{
    internal static string LibraryRoot => Path.GetFullPath(Environment.GetEnvironmentVariable("READARTICLES_TEST_LIBRARY") ?? "ReadArticles.Tests/Fixtures/Library");
    internal static string ArticlePath => Path.Combine(LibraryRoot, "The Economist", "2026-09-26", "When America walks away", "When America walks away.json");

    internal static AudioPaths Paths(string sid) => new AudioPaths($"audio_segments/{sid}_uk.mp3", $"audio_segments/{sid}_us.mp3");

    internal static ParagraphReader Reader() => new ParagraphReader(
        new[] { new Segment("s001", "public life.", Paths("s001")), new Segment("s002", "public life.", Paths("s002")) },
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
