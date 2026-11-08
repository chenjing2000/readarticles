using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ReadArticles.Models;
using ReadArticles.ViewModels;

internal static class ReaderWindowChecks
{
    internal static void Run()
    {
        Program.Run("three-column real content: tree, 13 readers and 120 full vocabulary rows", () =>
        {
            var window = CreateWindow();
            try
            {
                window.Show();
                Program.Check(window.ViewModel.OpenLibrary(TestData.LibraryRoot));
                var initialTree = (TreeView)window.FindName("LibraryTree");
                var initialLeaf = MainViewModel.AllLeaves(window.ViewModel.Books).Single(n => n.PassagePath == TestData.ArticlePath);
                Program.Check(Select(initialTree, initialLeaf));
                Program.Check(window.SelectPassage(TestData.ArticlePath));
                Layout(window);
                Program.Check(window.Readers.Count == 13 && window.ViewModel.Words.Count == 120);
                Program.Check(((TreeView)window.FindName("LibraryTree")).SelectedItem is NavigationNode { Name: "When America walks away" });
                Program.Check(((TextBlock)window.FindName("ArticleTitleText")).Text == "When America walks away");
                var row = window.ViewModel.Words[0];
                Program.Check(row.Meanings.Count > 0 && row.CanPlayUk && row.CanPlayUs && row.PhoneticUk == "—");
                Program.Check(window.Readers[0].FontFamily.Source == "Segoe UI");
            }
            finally { window.Close(); WpfTestHelpers.Pump(); }
        });
        Program.Run("actual tree leaf selection switches article and word list", () =>
        {
            var window = CreateWindow();
            try
            {
                window.Show();
                LoadArticle(window);
                var tree = (TreeView)window.FindName("LibraryTree");
                var alternate = MainViewModel.AllLeaves(window.ViewModel.Books).First(n => n.PassagePath != TestData.ArticlePath);
                Program.Check(Select(tree, alternate));
                Layout(window);
                Program.Check(window.ViewModel.ArticleTitle == alternate.Name && window.ViewModel.Article!.FilePath == alternate.PassagePath);
                Program.Check(Select(tree, MainViewModel.AllLeaves(window.ViewModel.Books).Single(n => n.PassagePath == TestData.ArticlePath)));
                Layout(window);
                Program.Check(window.ViewModel.ArticleTitle == "When America walks away");
            }
            finally { window.Close(); WpfTestHelpers.Pump(); }
        });
        Program.Run("real reader: every SID is owned and missing audio is reported only on playback", () =>
        {
            var window = CreateWindow();
            try
            {
                window.Show();
                LoadArticle(window);
                foreach (var reader in window.Readers)
                    foreach (var range in reader.Ranges)
                        Program.Check(reader.FindSegment(reader.PositionAt(range, 2))?.Segment.Sid == range.Segment.Sid);
                Program.Check(window.ViewModel.CanPlaySegment("s011") && window.ViewModel.CanPlaySegment("s001"));
                Program.Check(!window.ViewModel.Status.Contains("缺失"));
                window.ViewModel.PlaySegment("s011");
                Program.Check(window.ViewModel.Status == "句子 s011 的英音录音文件缺失。");
            }
            finally { window.Close(); WpfTestHelpers.Pump(); }
        });
        Program.Run("real vocabulary selection focuses an exact passage phrase", () =>
        {
            var window = CreateWindow();
            try
            {
                window.Show();
                LoadArticle(window);
                window.ViewModel.HighlightsVisible = true;
                var list = (ListBox)window.FindName("VocabularyList");
                var matcher = new ReadArticles.Services.VocabularyMatcher();
                var words = window.ViewModel.Words.Select(w => w.Word).ToArray();
                foreach (var reader in window.Readers)
                {
                    foreach (var range in reader.Ranges)
                    {
                        var hits = matcher.Match(range.Segment.Text, words);
                        if (hits.Count == 0) continue;
                        var hit = hits[0];
                        Program.Check(Equals(reader.GetTextBackground(range.Segment.Sid, hit.Start), window.FindResource("VocabularyHighlightBrush")));
                        list.SelectedItem = window.ViewModel.Words.First(w => w.Word.Equals(hit.Key, StringComparison.OrdinalIgnoreCase));
                        Layout(window);
                        Program.Check(Equals(reader.GetTextBackground(range.Segment.Sid, hit.Start), window.FindResource("SelectedVocabularyBrush")));
                        list.SelectedItem = null;
                        Layout(window);
                        Program.Check(Equals(reader.GetTextBackground(range.Segment.Sid, hit.Start), window.FindResource("VocabularyHighlightBrush")));
                        return;
                    }
                }
                throw new Exception("Fixture article has no vocabulary match");
            }
            finally { window.Close(); WpfTestHelpers.Pump(); }
        });
        Program.Run("independent scrolling: article movement does not move vocabulary", () =>
        {
            var window = CreateWindow();
            try
            {
                window.Show();
                LoadArticle(window);
                var passage = (ScrollViewer)window.FindName("PassageScroll");
                var list = (ListBox)window.FindName("VocabularyList");
                var wordsScroll = WpfTestHelpers.Descendants(list).OfType<ScrollViewer>().First();
                double offset = wordsScroll.VerticalOffset;
                passage.ScrollToEnd(); Layout(window);
                Program.Check(passage.VerticalOffset > 0 && wordsScroll.VerticalOffset == offset);
                string last = window.ViewModel.Article!.Segments.Last().Text;
                Program.Check(new TextRange(window.Readers[^1].Document.ContentStart, window.Readers[^1].Document.ContentEnd).Text.Contains(last));
                Capture(window, "reader-article-end.png");
                passage.ScrollToTop(); Layout(window);
                wordsScroll.ScrollToEnd(); Layout(window);
                Program.Check(passage.VerticalOffset == 0 && wordsScroll.VerticalOffset > 0);
                Program.Check(wordsScroll.ScrollableHeight - wordsScroll.VerticalOffset < 1);
                Capture(window, "reader-vocabulary-end.png");
                wordsScroll.ScrollToTop(); Layout(window);
            }
            finally { window.Close(); WpfTestHelpers.Pump(); }
        });
        Program.Run("resizable panels preserve content and full vocabulary layout", () =>
        {
            var window = CreateWindow();
            try
            {
                window.Show();
                LoadArticle(window);
                var left = (ColumnDefinition)window.FindName("LeftColumn");
                var right = (ColumnDefinition)window.FindName("RightColumn");
                left.Width = new GridLength(375); right.Width = new GridLength(470); Layout(window);
                double readerWidth = window.Readers[0].ActualWidth;
                double vocabularyWidth = ((ListBox)window.FindName("VocabularyList")).ActualWidth;
                Console.WriteLine($"  Resized content widths: reader={readerWidth:F1}, vocabulary={vocabularyWidth:F1}");
                Program.Check(readerWidth > 200 && vocabularyWidth > 400);
                Program.Check(window.Readers.Count == 13 && window.ViewModel.Words.Count == 120);
                left.Width = new GridLength(310); right.Width = new GridLength(390); Layout(window);
                window.ViewModel.HighlightsVisible = false;
                Capture(window, "reader-real-content.png");
                window.ViewModel.HighlightsVisible = true; Layout(window); Capture(window, "reader-highlight.png");
            }
            finally { window.Close(); WpfTestHelpers.Pump(); }
        });
    }
    private static ReadArticles.MainWindow CreateWindow() => new ReadArticles.MainWindow(false)
    {
        Left = -10000, Top = -10000, ShowActivated = false,
        WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize, Width = 1792, Height = 1068
    };
    private static void LoadArticle(ReadArticles.MainWindow window)
    {
        Program.Check(window.ViewModel.OpenLibrary(TestData.LibraryRoot));
        Program.Check(window.SelectPassage(TestData.ArticlePath));
        Layout(window);
    }
    private static void Layout(ReadArticles.MainWindow window)
    {
        window.UpdateLayout(); WpfTestHelpers.Pump(); window.UpdateLayout();
    }
    private static void Capture(ReadArticles.MainWindow window, string filename)
    {
        Layout(window);
        var bitmap = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(window);
        var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap));
        string path = Path.GetFullPath(Path.Combine("artifacts", filename));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var stream = File.Create(path); png.Save(stream);
        Console.WriteLine("  Screenshot: " + path);
    }
    private static bool Select(ItemsControl control, NavigationNode target)
    {
        control.UpdateLayout();
        foreach (var item in control.Items)
        {
            if (control.ItemContainerGenerator.ContainerFromItem(item) is not TreeViewItem child) continue;
            if (ReferenceEquals(item, target)) { child.IsSelected = true; return true; }
            if (item is NavigationNode node && Contains(node.Children, target))
            {
                child.IsExpanded = true; child.UpdateLayout();
                return Select(child, target);
            }
        }
        return false;
    }
    private static bool Contains(IEnumerable<NavigationNode> nodes, NavigationNode target) => nodes.Any(n => ReferenceEquals(n, target) || Contains(n.Children, target));
}
