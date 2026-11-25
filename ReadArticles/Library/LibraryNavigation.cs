using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Threading;
using ReadArticles.Infrastructure;
using ReadArticles.Models;
using ReadArticles.Services;

namespace ReadArticles.Library;

// Owns library discovery, the committed root, tree projection and scan lifetime.
// Collection changes and commit callbacks always execute on the owning dispatcher.
public sealed class LibraryNavigation
{
    private readonly LibraryRepository libraryRepository = new();
    private readonly Dictionary<NavigationNode, CancellationTokenSource> directoryScans = new();
    private long librarySession;
    private bool navigationClosed;

    private readonly Action<string, string, NotificationSeverity> notify;
    private LibrarySnapshot? library;
    public string? Root => library?.Root;
    public ObservableCollection<NavigationNode> Books { get; } = new();

    public LibraryNavigation(Action<string, string, NotificationSeverity> notify)
    {
        this.notify = notify;
    }

    public LibrarySnapshot PrepareLibrary(string root) => libraryRepository.Load(root);

    public async Task<bool> OpenLibraryAsync(string root, Action<LibrarySnapshot> commit)
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
                commit(prepared);
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
                notify("LibraryOpenFailed", "无法打开图书馆，请检查文件夹访问权限。", NotificationSeverity.Error);
            });
            return false;
        }
    }

    public void CommitLibrary(LibrarySnapshot prepared)
    {
        ++librarySession;
        CancelAllDirectoryScans();
        library = prepared;
    }

    // Publish after the application has stopped the previous playback and
    // cleared notifications, keeping the existing context-switch order.
    public void RefreshBooks()
    {
        Books.Clear();
        if (library is not null)
            foreach (var book in library.Books) Books.Add(book);
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
                    notify("DirectoryScanIncomplete", "部分目录项无法读取，可折叠后重新展开重试。",
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
                    notify("DirectoryScanFailed", "目录扫描失败，可重新展开重试。", NotificationSeverity.Warning);
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
