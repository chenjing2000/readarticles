using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using ReadArticles.Models;
using ReadArticles.ViewModels;

namespace ReadArticles;

public partial class MainWindow
{
    public async void RestoreContent(string? library = null) =>
        await RestoreContentAsync(library);

    // The task-returning entrypoint makes startup recovery deterministically
    // observable without changing the fire-and-forget application startup API.
    public Task<bool> RestoreContentAsync(string? library = null)
    {
        library ??= settings.Library;
        return library is null ? Task.FromResult(false) : ViewModel.OpenLibraryAsync(library);
    }

    public bool SelectPassage(string path)
    {
        string full = Path.GetFullPath(path);
        var node = MainViewModel.AllLeaves(ViewModel.Books).FirstOrDefault(n => string.Equals(n.PassagePath, full, StringComparison.OrdinalIgnoreCase));
        if (node is null)
        {
            // Programmatic selection can target a valid passage whose ancestors
            // have not been expanded. Do not scan the entire library for it.
            bool insideBook = ViewModel.Books.Any(book =>
            {
                if (book.DirectoryPath is null) return false;
                string relative = Path.GetRelativePath(book.DirectoryPath, full);
                return relative != "." && !Path.IsPathRooted(relative) &&
                    relative != ".." &&
                    !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal);
            });
            if (insideBook && File.Exists(full))
                return ViewModel.OpenArticle(full);
            ViewModel.Notify("PassageNotInLibrary", "文章不在当前图书馆中。", ReadArticles.Infrastructure.NotificationSeverity.Warning);
            return false;
        }
        bool opened = ViewModel.OpenArticle(node.PassagePath!);
        if (opened) Dispatcher.BeginInvoke(() => SelectTreeNode(LibraryTree, node));
        return opened;
    }

    private static bool SelectTreeNode(ItemsControl parent, NavigationNode target)
    {
        parent.UpdateLayout();
        foreach (var item in parent.Items)
        {
            if (parent.ItemContainerGenerator.ContainerFromItem(item) is not TreeViewItem container) continue;
            if (ReferenceEquals(item, target))
            {
                container.IsSelected = true;
                container.BringIntoView();
                return true;
            }
            if (container.IsExpanded && SelectTreeNode(container, target)) return true;
        }
        return false;
    }

    private bool restoringTreeSelection;
    private NavigationNode? pendingTreeRollback;

    private void TreeSelectionChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (restoringTreeSelection || e.NewValue is not NavigationNode { PassagePath: not null } node ||
            ReferenceEquals(node, pendingTreeRollback))
            return;
        if (SamePassage(node.PassagePath, ViewModel.Article?.FilePath)) return;
        if (ViewModel.OpenArticle(node.PassagePath))
        {
            pendingTreeRollback = null;
            return;
        }

        // Finish the current mouse/keyboard selection before rolling back.
        // A rollback performed inside SelectedItemChanged can reselect the bad leaf.
        string? committed = ViewModel.Article?.FilePath;
        pendingTreeRollback = node;
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, new Action(() =>
        {
            if (!ReferenceEquals(pendingTreeRollback, node)) return;
            try
            {
                // A later user selection (or successful article switch) wins.
                if (!ReferenceEquals(LibraryTree.SelectedItem, node) ||
                    !string.Equals(ViewModel.Article?.FilePath, committed, StringComparison.OrdinalIgnoreCase))
                    return;
                restoringTreeSelection = true;
                try
                {
                    var previous = MainViewModel.AllLeaves(ViewModel.Books)
                        .FirstOrDefault(item => SamePassage(item.PassagePath, committed));
                    if (previous is not null && SelectTreeNode(LibraryTree, previous))
                        return;
                    var selected = FindTreeContainer(LibraryTree, node);
                    if (selected is not null) selected.IsSelected = false;
                }
                finally { restoringTreeSelection = false; }
            }
            finally
            {
                if (ReferenceEquals(pendingTreeRollback, node)) pendingTreeRollback = null;
            }
        }));
    }

    private static bool SamePassage(string? a, string? b) =>
        a is not null && b is not null &&
        string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);

    private static TreeViewItem? FindTreeContainer(ItemsControl parent, NavigationNode target)
    {
        foreach (var item in parent.Items)
        {
            if (parent.ItemContainerGenerator.ContainerFromItem(item) is not TreeViewItem container) continue;
            if (ReferenceEquals(item, target)) return container;
            if (!container.IsExpanded) continue;
            var nested = FindTreeContainer(container, target);
            if (nested is not null) return nested;
        }
        return null;
    }

    private async void TreeExpanded(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is TreeViewItem { DataContext: NavigationNode node })
        {
            node.IsExpanded = true;
            await ViewModel.ExpandDirectoryAsync(node);
        }
    }

    private void TreeCollapsed(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is TreeViewItem { DataContext: NavigationNode node })
        {
            node.IsExpanded = false;
            ViewModel.CollapseDirectory(node);
        }
    }

    private async void LibraryClicked(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "选择包含 Book 文件夹的图书馆", InitialDirectory = ViewModel.LibraryRoot ?? "" };
        if (dialog.ShowDialog(this) == true) await ViewModel.OpenLibraryAsync(dialog.FolderName);
    }
}
