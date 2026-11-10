using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;
using ReadArticles.Models;
using ReadArticles.Rendering;
using ReadArticles.ViewModels;

namespace ReadArticles;

public partial class MainWindow
{
    private readonly List<ParagraphReader> readers = new List<ParagraphReader>();

    public IReadOnlyList<ParagraphReader> Readers => readers;

    public void RestoreContent(string? library = null)
    {
        library ??= settings.Library;
        if (library is not null) ViewModel.OpenLibrary(library);
    }

    public bool SelectPassage(string path)
    {
        string full = Path.GetFullPath(path);
        var node = MainViewModel.AllLeaves(ViewModel.Books).FirstOrDefault(n => string.Equals(n.PassagePath, full, StringComparison.OrdinalIgnoreCase));
        if (node is null)
        {
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

    private void RenderArticle()
    {
        ParagraphHost.Children.Clear();
        readers.Clear();
        var article = ViewModel.Article;
        EmptyMessage.Visibility = article is null ? Visibility.Visible : Visibility.Collapsed;
        ArticleTitleText.Visibility = article is null ? Visibility.Collapsed : Visibility.Visible;
        RefreshVocabularyHighlights();
        if (article is null)
        {
            RefreshPlayback();
            return;
        }
        var words = ViewModel.Words.Select(w => w.Word).ToArray();
        foreach (var paragraph in article.Paragraphs)
        {
            var reader = new ParagraphReader(paragraph, words, ViewModel.HighlightsVisible)
            {
                FontSize = settings.FontSize,
                FontFamily = new FontFamily("Segoe UI"),
                Margin = new Thickness(0, 3, 0, 15)
            };
            reader.SelectionRequested += ViewModel.AddSelection;
            reader.SegmentPlaybackRequested += ViewModel.PlaySegment;
            readers.Add(reader);
            ParagraphHost.Children.Add(reader);
        }
        PassageScroll.ScrollToTop();
        RefreshPlayback();
    }

    private bool restoringTreeSelection;

    private void TreeSelectionChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (restoringTreeSelection || e.NewValue is not NavigationNode { PassagePath: not null } node)
            return;
        if (SamePassage(node.PassagePath, ViewModel.Article?.FilePath)) return;
        if (ViewModel.OpenArticle(node.PassagePath)) return;

        // Restore the committed article only if its tree container already exists.
        restoringTreeSelection = true;
        try
        {
            string? committed = ViewModel.Article?.FilePath;
            var previous = MainViewModel.AllLeaves(ViewModel.Books)
                .FirstOrDefault(item => SamePassage(item.PassagePath, committed));
            if (previous is not null && SelectTreeNode(LibraryTree, previous))
                return;
            var selected = FindTreeContainer(LibraryTree, node);
            if (selected is not null) selected.IsSelected = false;
        }
        finally { restoringTreeSelection = false; }
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

    private void LibraryClicked(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "选择包含 Book 文件夹的图书馆", InitialDirectory = ViewModel.LibraryRoot ?? "" };
        if (dialog.ShowDialog(this) == true) ViewModel.OpenLibrary(dialog.FolderName);
    }
}
