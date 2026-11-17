using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;
using ReadArticles.Models;
using ReadArticles.Rendering;
using ReadArticles.Services;
using ReadArticles.ViewModels;

namespace ReadArticles;

public partial class MainWindow
{
    private readonly List<ParagraphReader> readers = new List<ParagraphReader>();

    public IReadOnlyList<ParagraphReader> Readers => readers;

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

    private ScriptArticle? scriptArticle;
    private bool scriptMode;
    private double passageScrollOffset;
    private double scriptScrollOffset;
    private long contentRenderVersion;

    private void ScriptClicked(object sender, RoutedEventArgs e)
    {
        var article = ViewModel.Article;
        if (article is null) return;
        if (scriptMode)
        {
            double offset = passageScrollOffset;
            double leavingOffset = PassageScroll.VerticalOffset;
            try
            {
                ShowContent(null, offset);
                scriptScrollOffset = leavingOffset;
            }
            catch (Exception error) when (error is InvalidOperationException or ArgumentException)
            {
                ViewModel.Notify("ScriptViewFailed", "无法恢复普通正文显示。", ReadArticles.Infrastructure.NotificationSeverity.Error);
                ReadArticles.Infrastructure.AppLogger.Default.Warning("ScriptViewFailed", error);
            }
            return;
        }

        ScriptArticle? candidate = scriptArticle;
        if (candidate is null)
        {
            string path = ArticleRepository.ScriptPathFor(article.FilePath);
            if (!File.Exists(path))
            {
                ViewModel.Notify("ScriptNotFound", "未找到同名 Script JSON：" + Path.GetFileName(path),
                    ReadArticles.Infrastructure.NotificationSeverity.Warning);
                return;
            }
            try
            {
                candidate = new ArticleRepository().LoadScript(article.FilePath);
            }
            catch (Exception error) when (error is InvalidDataException or System.Text.Json.JsonException or
                IOException or UnauthorizedAccessException or ArgumentException)
            {
                ViewModel.Notify("ScriptLoadFailed", "Script JSON 加载失败：" + error.Message,
                    ReadArticles.Infrastructure.NotificationSeverity.Error);
                ReadArticles.Infrastructure.AppLogger.Default.Warning("ScriptLoadFailed", error);
                return;
            }
        }

        double previousOffset = PassageScroll.VerticalOffset;
        try
        {
            ShowContent(candidate, scriptScrollOffset);
            scriptArticle = candidate;
            passageScrollOffset = previousOffset;
        }
        catch (Exception error) when (error is InvalidOperationException or ArgumentException)
        {
            ViewModel.Notify("ScriptViewFailed", "Script 正文渲染失败，已保留原有正文。",
                ReadArticles.Infrastructure.NotificationSeverity.Error);
            ReadArticles.Infrastructure.AppLogger.Default.Warning("ScriptViewFailed", error);
        }
    }

    private void RenderArticle()
    {
        // An article change starts a new session. Do not retain the old Script cache.
        scriptArticle = null;
        scriptMode = false;
        passageScrollOffset = 0;
        scriptScrollOffset = 0;
        ShowContent(null, 0);
    }

    private void ShowContent(ScriptArticle? script, double targetOffset)
    {
        var article = ViewModel.Article;
        var words = ViewModel.Words.Select(w => w.Word).ToArray();
        var prepared = new List<ParagraphReader>();
        if (article is not null)
        {
            if (script is null)
            {
                foreach (var paragraph in article.Paragraphs)
                    prepared.Add(CreateReader(paragraph, words));
            }
            else
            {
                foreach (var paragraph in script.Paragraphs)
                {
                    // Empty SID is not an invented audio SID. Script sentences never
                    // trigger sentence pronunciation or Passage blank detection.
                    var segments = paragraph.Sentences
                        .Select(sentence => new Segment("", sentence.Text, false)).ToArray();
                    prepared.Add(CreateReader(segments, words, paragraph.Sentences));
                }
            }
        }

        // Commit the view only after every ParagraphReader was constructed.
        ParagraphHost.Children.Clear();
        readers.Clear();
        scriptMode = script is not null;
        readers.AddRange(prepared);
        foreach (var reader in readers) ParagraphHost.Children.Add(reader);
        EmptyMessage.Visibility = article is null ? Visibility.Visible : Visibility.Collapsed;
        string title = script?.Title ?? article?.Title ?? "";
        ArticleTitleText.SetCurrentValue(TextBlock.TextProperty, title);
        ArticleTitleText.Visibility = article is not null && title.Length > 0
            ? Visibility.Visible : Visibility.Collapsed;
        ScriptButton.IsEnabled = article is not null;
        ScriptImage.Source = scriptMode ? ReadArticles.Infrastructure.UiIcons.ScriptOn : ReadArticles.Infrastructure.UiIcons.ScriptOff;
        ScriptButton.ToolTip = scriptMode ? "切换到普通正文" : "切换到同步朗读正文";
        RefreshVocabularyHighlights();
        RefreshPlayback();
        long version = ++contentRenderVersion;
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, new Action(() =>
        {
            if (version == contentRenderVersion && ReferenceEquals(ViewModel.Article, article))
                PassageScroll.ScrollToVerticalOffset(targetOffset);
        }));
    }

    private ParagraphReader CreateReader(IEnumerable<Segment> segments, string[] words,
        IReadOnlyList<ScriptSentence>? sentences = null)
    {
        var reader = new ParagraphReader(segments, words, ViewModel.HighlightsVisible, sentences)
        {
            FontSize = settings.FontSize,
            FontFamily = new FontFamily("Segoe UI"),
            Margin = new Thickness(0, 3, 0, 15)
        };
        reader.SelectionRequested += ViewModel.AddSelection;
        if (sentences is null) reader.SegmentPlaybackRequested += ViewModel.PlaySegment;
        return reader;
    }

    private void UpdateScriptColors()
    {
        if (!scriptMode) return;
        double? position = ViewModel.Playback.IsArticleActive
            ? ViewModel.Playback.ArticlePosition : null;
        foreach (var reader in readers) reader.SetScriptPosition(position);
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
