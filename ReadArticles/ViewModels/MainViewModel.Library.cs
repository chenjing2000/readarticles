using System.IO;
using System.Windows.Threading;
using ReadArticles.Infrastructure;
using ReadArticles.Models;
using ReadArticles.Services;

namespace ReadArticles.ViewModels;

public sealed partial class MainViewModel
{
    private readonly LibraryRepository libraryRepository = new();
    private readonly Dictionary<NavigationNode, CancellationTokenSource> directoryScans = new();
    private long librarySession;
    private bool navigationClosed;

    public bool OpenLibrary(string root) => Handle(() =>
    {
        var prepared = libraryRepository.Load(root);
        CommitLibrary(prepared);
    });

    public async Task<bool> OpenLibraryAsync(string root)
    {
        Dispatcher ui = Dispatcher.CurrentDispatcher;
        long session = ++librarySession;
        CancelAllDirectoryScans();
        try
        {
            // Only filesystem discovery runs on the worker thread. Binding and
            // collection updates always return to the owning UI Dispatcher,
            // including under the STA test runner without a SynchronizationContext.
            var prepared = await Task.Run(() => libraryRepository.Load(root));
            return await ui.InvokeAsync(() =>
            {
                if (navigationClosed || session != librarySession) return false;
                CommitLibrary(prepared);
                return true;
            });
        }
        catch (Exception error) when (error is ArgumentException or IOException or
            InvalidDataException or UnauthorizedAccessException or InvalidOperationException)
        {
            await ui.InvokeAsync(() =>
            {
                if (session != librarySession || navigationClosed) return;
                AppLogger.Default.Warning("LibraryOpenFailed", error);
                Notify("LibraryOpenFailed", "无法打开图书馆，请检查文件夹访问权限。", NotificationSeverity.Error);
            });
            return false;
        }
    }

    private void CommitLibrary(LibrarySnapshot prepared)
    {
        ++librarySession;
        CancelAllDirectoryScans();
        workspace.CommitLibrary(prepared);
        playback.Stop();
        Books.Clear();
        foreach (var book in prepared.Books) Books.Add(book);
        Words.Clear();
        CurrentUser = "User: —";
        Changed(nameof(LibraryRoot));
        Changed(nameof(CurrentUser));
        Changed(nameof(ArticleTitle));
        ArticleChanged?.Invoke();
        if (prepared.Warnings.Count > 0)
        {
            AppLogger.Default.Warning("LibraryLoadedWithWarnings");
            Notify("LibraryLoadedWithWarnings", "图书馆存在部分无法读取的目录。", NotificationSeverity.Warning);
        }
        Status = string.Join("  ", prepared.Warnings);
    }

    public async Task ExpandDirectoryAsync(NavigationNode node)
    {
        if (navigationClosed || node.IsPassage || node.IsPlaceholder ||
            node.DirectoryPath is null || !node.IsExpanded) return;
        if (node.LoadState == NodeLoadState.Loading) return;
        if (node.LoadState == NodeLoadState.Loaded) return;
        // The test runner does not install a WPF SynchronizationContext.
        // Capture the actual owning dispatcher before the background scan.
        Dispatcher ui = Dispatcher.CurrentDispatcher;
        long generation = node.NextGeneration();
        long session = librarySession;
        var cts = new CancellationTokenSource();
        CancellationToken token = cts.Token;
        if (directoryScans.Remove(node, out var previous)) { previous.Cancel(); previous.Dispose(); }
        directoryScans[node] = cts;
        node.LoadState = NodeLoadState.Loading;
        try
        {
            DirectoryScanResult result = await Task.Run(() =>
            {
                token.ThrowIfCancellationRequested();
                return libraryRepository.ScanDirectoryLevel(node.DirectoryPath, node.BookRoot);
            }, token);

            await ui.InvokeAsync(() =>
            {
                if (navigationClosed || !node.IsExpanded || session != librarySession ||
                    generation != node.Generation || token.IsCancellationRequested) return;
                if (result.IsComplete)
                {
                    node.LastSuccessfulSnapshot = result.Entries.ToArray();
                    node.ReplaceChildren(result.Entries, NodeLoadState.Loaded);
                }
                else
                {
                    if (node.LastSuccessfulSnapshot is not null)
                        node.ReplaceChildren(node.LastSuccessfulSnapshot, NodeLoadState.Failed);
                    else
                        node.ReplaceChildren(result.Entries, NodeLoadState.Failed);
                }
                if (result.Warnings.Count != 0)
                {
                    AppLogger.Default.Warning("DirectoryScanIncomplete");
                    Notify("DirectoryScanIncomplete", "部分目录项无法读取，可折叠后重新展开重试。",
                        NotificationSeverity.Warning);
                }
            });
        }
        catch (OperationCanceledException) { }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            await ui.InvokeAsync(() =>
            {
                if (!navigationClosed && session == librarySession && generation == node.Generation)
                {
                    node.LoadState = NodeLoadState.Failed;
                    if (node.LastSuccessfulSnapshot is not null)
                        node.ReplaceChildren(node.LastSuccessfulSnapshot, NodeLoadState.Failed);
                    AppLogger.Default.Warning("DirectoryScanFailed", error);
                    Notify("DirectoryScanFailed", "目录扫描失败，可重新展开重试。", NotificationSeverity.Warning);
                }
            });
        }
        finally
        {
            await ui.InvokeAsync(() =>
            {
                if (directoryScans.TryGetValue(node, out var current) && ReferenceEquals(current, cts))
                    directoryScans.Remove(node);
                cts.Dispose();
            });
        }
    }

    public void CollapseDirectory(NavigationNode node)
    {
        foreach (var current in EnumerateNodes(node))
        {
            current.NextGeneration();
            if (directoryScans.Remove(current, out var cts))
            {
                cts.Cancel();
                cts.Dispose();
            }
            if (!current.IsPassage && !current.IsPlaceholder)
                current.LoadState = NodeLoadState.NotLoaded;
        }
    }

    private static IEnumerable<NavigationNode> EnumerateNodes(NavigationNode node)
    {
        yield return node;
        foreach (var child in node.Children)
            foreach (var descendant in EnumerateNodes(child))
                yield return descendant;
    }

    private void CancelAllDirectoryScans()
    {
        foreach (var cts in directoryScans.Values)
        {
            cts.Cancel();
            cts.Dispose();
        }
        directoryScans.Clear();
    }

    public void CloseNavigation()
    {
        navigationClosed = true;
        ++librarySession;
        CancelAllDirectoryScans();
    }

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
        if (Article!.Warnings.Count > 0)
        {
            AppLogger.Default.Warning("ArticleLoadedWithWarnings");
            Notify("ArticleLoadedWithWarnings", "文章已打开，但部分附属数据存在问题。",
                NotificationSeverity.Warning);
        }
        Status = string.Join("  ", Article.Warnings);
    });

    private string ReadUser(string path)
    {
        // Book ownership is available from the root-only snapshot; do not
        // recursively materialize directory nodes merely to resolve userdata.
        string full = Path.GetFullPath(path);
        var book = Books.FirstOrDefault(n =>
        {
            if (n.DirectoryPath is null) return false;
            string relative = Path.GetRelativePath(n.DirectoryPath, full);
            return relative != "." && !Path.IsPathRooted(relative) &&
                relative != ".." &&
                !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal);
        });
        return book is null ? "User: —" : new UserRepository().ReadUser(book.BookRoot);
    }

    public static IEnumerable<NavigationNode> AllLeaves(IEnumerable<NavigationNode> nodes)
    {
        foreach (var node in nodes)
        {
            if (node.IsPlaceholder) continue;
            if (node.IsPassage) yield return node;
            else
                foreach (var leaf in AllLeaves(node.Children)) yield return leaf;
        }
    }
}
