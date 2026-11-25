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
    private HashSet<string> excludedFolders;
    private Task<bool>? pendingSwitch;
    private bool refreshRequired;

    private readonly Action<string, string, NotificationSeverity> notify;
    private LibrarySnapshot? library;
    public string? Root => library?.Root;
    public ObservableCollection<NavigationNode> Books { get; } = new();

    public LibraryNavigation(Action<string, string, NotificationSeverity> notify,
        string? excludedFolders = null)
    {
        this.notify = notify;
        this.excludedFolders = ReaderSettings.ParseExcludedFolders(excludedFolders);
    }

    public LibrarySnapshot PrepareLibrary(string root) => libraryRepository.Load(root, excludedFolders);

    // A filter change never supersedes a user's pending library selection.
    public Task UpdateExcludedFoldersAsync(HashSet<string> names)
    {
        if (navigationClosed || excludedFolders.SetEquals(names)) return Task.CompletedTask;
        excludedFolders = names;
        if (Root is { } root)
        {
            refreshRequired = true;
            CancelAllDirectoryScans();
            library = new LibrarySnapshot(root, Array.Empty<NavigationNode>(), Array.Empty<string>());
            RefreshBooks();
        }
        return pendingSwitch is { IsCompleted: false } switching
            ? switching : RefreshCurrentAsync();
    }

    private Task RefreshCurrentAsync() => Root is { } root
        ? LoadRootAsync(root, prepared =>
        {
            CommitLibrary(prepared);
            RefreshBooks();
            if (prepared.Warnings.Count > 0)
            {
                AppLogger.Default.Warning("LibraryLoadedWithWarnings");
                notify("LibraryLoadedWithWarnings", "图书馆存在部分无法读取的目录。", NotificationSeverity.Warning);
            }
        })
        : Task.CompletedTask;

    public Task<bool> OpenLibraryAsync(string root, Action<LibrarySnapshot> commit) =>
        pendingSwitch = LoadRootAsync(root, commit, isSwitch: true);

    private async Task<bool> LoadRootAsync(string root, Action<LibrarySnapshot> commit, bool isSwitch = false)
    {
        if (navigationClosed) return false;
        Dispatcher ui = Dispatcher.CurrentDispatcher;
        long session = ++librarySession;
        CancelAllDirectoryScans();
        try
        {
            // Only filesystem discovery runs on the worker thread. Binding and
            // collection updates always return to the owning UI Dispatcher,
            // including under the STA test runner without a SynchronizationContext.
            var filter = excludedFolders;
            while (true)
            {
                var snapshot = filter;
                var prepared = await Task.Run(() => libraryRepository.Load(root, snapshot));
                bool? result = await ui.InvokeAsync<bool?>(() =>
                {
                    if (navigationClosed || session != librarySession) return false;
                    if (!ReferenceEquals(snapshot, excludedFolders))
                    {
                        filter = excludedFolders;
                        return null;
                    }
                    commit(prepared);
                    return true;
                });
                if (result.HasValue) return result.Value;
            }
        }
        catch (Exception error) when (error is ArgumentException or IOException or
            InvalidDataException or UnauthorizedAccessException or InvalidOperationException)
        {
            Task? fallback = await ui.InvokeAsync<Task?>(() =>
            {
                if (session != librarySession || navigationClosed) return null;
                AppLogger.Default.Warning("LibraryOpenFailed", error);
                notify("LibraryOpenFailed", "无法打开图书馆，请检查文件夹访问权限。", NotificationSeverity.Error);
                // A failed switch still owes the committed library its new filter.
                return isSwitch && refreshRequired ? RefreshCurrentAsync() : null;
            });
            if (fallback is not null) await fallback;
            return false;
        }
    }

    public void CommitLibrary(LibrarySnapshot prepared)
    {
        ++librarySession;
        CancelAllDirectoryScans();
        library = prepared;
        pendingSwitch = null;
        refreshRequired = false;
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
        var filter = excludedFolders;
        long generation = node.NextGeneration();
        long session = librarySession;
        var cts = new CancellationTokenSource();
        CancellationToken token = cts.Token;
        if (directoryScans.Remove(node, out var previous)) previous.Cancel();
        directoryScans[node] = cts;
        node.LoadState = NodeLoadState.Loading;
        try
        {
            DirectoryScanResult result = await Task.Run(() =>
            {
                token.ThrowIfCancellationRequested();
                return libraryRepository.ScanDirectoryLevel(node.DirectoryPath, node.BookRoot, filter);
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
                if (!navigationClosed && node.IsExpanded && !token.IsCancellationRequested &&
                    session == librarySession && generation == node.Generation)
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
        node.IsExpanded = false;
        foreach (var current in EnumerateNodes(node))
        {
            if (directoryScans.Remove(current, out var cts)) cts.Cancel();
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
        foreach (var (node, cts) in directoryScans)
        {
            cts.Cancel();
            if (node.LoadState == NodeLoadState.Loading)
                node.LoadState = NodeLoadState.NotLoaded;
        }
        // The task that created each source disposes it in its finally block.
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
