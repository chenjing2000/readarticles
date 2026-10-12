using System.IO;
using ReadArticles.Models;
using ReadArticles.Services;

namespace ReadArticles.ViewModels;

public sealed partial class MainViewModel
{
    public bool OpenLibrary(string root) => Handle(() =>
    {
        workspace.OpenLibrary(root);
        playback.Stop();
        Books.Clear();
        foreach (var book in workspace.Library!.Books) Books.Add(book);
        Words.Clear();
        CurrentUser = "User: —";
        Changed(nameof(LibraryRoot));
        Changed(nameof(CurrentUser));
        Changed(nameof(ArticleTitle));
        ArticleChanged?.Invoke();
        Status = string.Join("  ", workspace.Library.Warnings);
    });

    public bool OpenArticle(string path) => Handle(() =>
    {
        var prepared = new ArticleRepository().Load(path);
        var words = PrepareWords(prepared);
        string user = ReadUser(path);
        workspace.CommitArticle(prepared);
        playback.Stop();
        Words.Clear();
        foreach (var word in words) Words.Add(word);
        CurrentUser = user;
        Changed(nameof(CurrentUser));
        Changed(nameof(ArticleTitle));
        ArticleChanged?.Invoke();
        Status = string.Join("  ", Article!.Warnings);
    });

    private string ReadUser(string path)
    {
        var leaf = AllLeaves(Books).FirstOrDefault(n => string.Equals(n.PassagePath, path, StringComparison.OrdinalIgnoreCase));
        if (leaf is null) return "User: —";
        return new UserRepository().ReadUser(leaf.BookRoot);
    }

    public static IEnumerable<NavigationNode> AllLeaves(IEnumerable<NavigationNode> nodes)
    {
        foreach (var node in nodes)
        {
            if (node.IsPassage) yield return node;
            else
            {
                foreach (var leaf in AllLeaves(node.Children)) yield return leaf;
            }
        }
    }
}
