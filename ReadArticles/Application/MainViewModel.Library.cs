using ReadArticles.Infrastructure;
using ReadArticles.Library;
using ReadArticles.Models;
using ReadArticles.Services;

namespace ReadArticles.ViewModels;

public sealed partial class MainViewModel
{
    public bool OpenLibrary(string root) => Handle(() =>
        CommitLibrary(navigation.PrepareLibrary(root)));

    public Task<bool> OpenLibraryAsync(string root) =>
        navigation.OpenLibraryAsync(root, CommitLibrary);

    public bool CanRefreshLibrary => LibraryRoot is not null;

    public Task<bool> RefreshLibraryAsync() => LibraryRoot is { } root
        ? navigation.OpenLibraryAsync(root, CommitLibrary, isRefresh: true)
        : Task.FromResult(false);

    private void CommitLibrary(LibrarySnapshot? prepared)
    {
        navigation.CommitLibrary(prepared);
        workspace.ClearArticle();
        playback.Stop();
        // Clear after stopping the previous audio, before reporting any
        // warnings from the newly opened library.
        Notifications.Clear();
        navigation.RefreshBooks();
        Words.Clear();
        Changed(nameof(LibraryRoot));
        Changed(nameof(CanRefreshLibrary));
        Changed(nameof(ArticleTitle));
        ArticleChanged?.Invoke();
        if (prepared is { Warnings.Count: > 0 })
        {
            AppLogger.Default.Warning("LibraryLoadedWithWarnings");
            Notify("LibraryLoadedWithWarnings", "图书馆存在部分无法读取的目录。", NotificationSeverity.Warning);
        }
    }

    public Task ExpandDirectoryAsync(NavigationNode node) => navigation.ExpandDirectoryAsync(node);
    public void CollapseDirectory(NavigationNode node) => navigation.CollapseDirectory(node);
    public void CloseNavigation() => navigation.CloseNavigation();

    public bool OpenArticle(string path) => Handle(() =>
    {
        var prepared = new ArticleRepository().Load(path);
        var words = PrepareWords(prepared);
        workspace.CommitArticle(prepared);
        playback.Stop();
        // Discard the previous article's visible and queued messages after
        // the new article has committed. Failed opens do not clear anything.
        Notifications.Clear();
        Words.Clear();
        foreach (var word in words) Words.Add(word);
        Changed(nameof(ArticleTitle));
        ArticleChanged?.Invoke();
        if (Article!.Warnings.Count > 0)
        {
            AppLogger.Default.Warning("ArticleLoadedWithWarnings");
            Notify("ArticleLoadedWithWarnings", "文章已打开，但部分附属数据存在问题。",
                NotificationSeverity.Warning);
        }
    });

    public static IEnumerable<NavigationNode> AllLeaves(IEnumerable<NavigationNode> nodes) =>
        LibraryNavigation.AllLeaves(nodes);
}
