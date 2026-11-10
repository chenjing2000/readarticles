using System.Windows.Controls;
using System.IO;
using ReadArticles.Models;

internal static class TreeNavigationChecks
{
    internal static void Run()
    {
        Program.Run("collapse cancels an in-flight scan and a later expansion retries", () =>
        {
            string root = Path.Combine(Path.GetTempPath(), "ReadArticles-tree-cancel-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            var window = new ReadArticles.MainWindow(false);
            try
            {
                File.WriteAllText(Path.Combine(root, "book.json"), "marker");
                File.WriteAllText(Path.Combine(root, "Article.json"), "{}");
                window.Show();
                var node = new NavigationNode("Book", root, null, Array.Empty<NavigationNode>(), root);
                node.IsExpanded = true;
                Task pending = window.ViewModel.ExpandDirectoryAsync(node);
                node.IsExpanded = false;
                window.ViewModel.CollapseDirectory(node);
                WpfTestHelpers.Wait(() => pending.IsCompleted);
                pending.GetAwaiter().GetResult();
                Program.Check(node.LoadState == NodeLoadState.NotLoaded);
                Program.Check(!node.Children.Any(child => child.IsPassage));

                node.IsExpanded = true;
                Task retry = window.ViewModel.ExpandDirectoryAsync(node);
                WpfTestHelpers.Wait(() => retry.IsCompleted);
                retry.GetAwaiter().GetResult();
                Program.Check(node.LoadState == NodeLoadState.Loaded);
                Program.Check(node.Children.Any(child => child.Name == "Article" && child.IsPassage));
            }
            finally
            {
                window.Close();
                WpfTestHelpers.Pump();
                Directory.Delete(root, true);
            }
        });

        Program.Run("switching libraries invalidates results from the previous library session", () =>
        {
            string root = Path.Combine(Path.GetTempPath(), "ReadArticles-tree-session-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            var window = new ReadArticles.MainWindow(false);
            try
            {
                File.WriteAllText(Path.Combine(root, "book.json"), "marker");
                File.WriteAllText(Path.Combine(root, "Article.json"), "{}");
                window.Show();
                var stale = new NavigationNode("Old book", root, null, Array.Empty<NavigationNode>(), root)
                    { IsExpanded = true };
                Task pending = window.ViewModel.ExpandDirectoryAsync(stale);
                Task<bool> replacement = window.ViewModel.OpenLibraryAsync(TestData.LibraryRoot);
                WpfTestHelpers.Wait(() => pending.IsCompleted && replacement.IsCompleted);
                pending.GetAwaiter().GetResult();
                Program.Check(replacement.GetAwaiter().GetResult());
                Program.Check(window.ViewModel.Books.Count == 1);
                Program.Check(stale.LoadState != NodeLoadState.Loaded);
                Program.Check(!stale.Children.Any(child => child.IsPassage));
            }
            finally
            {
                window.Close();
                WpfTestHelpers.Pump();
                Directory.Delete(root, true);
            }
        });

        Program.Run("failed article selection restores the previously committed tree leaf", () =>
        {
            string root = Path.Combine(Path.GetTempPath(), "ReadArticles-selection-rollback-" + Guid.NewGuid().ToString("N"));
            string book = Path.Combine(root, "Book");
            Directory.CreateDirectory(book);
            var window = new ReadArticles.MainWindow(false);
            try
            {
                File.WriteAllText(Path.Combine(book, "book.json"), "marker");
                string goodPath = Path.Combine(book, "Good.json");
                string badPath = Path.Combine(book, "Bad.json");
                File.Copy(TestData.ArticlePath, goodPath);
                File.WriteAllText(badPath, "{ invalid article");
                window.Show();
                Program.Check(window.ViewModel.OpenLibrary(root));
                WpfTestHelpers.Pump();
                var tree = (TreeView)window.FindName("LibraryTree");
                var bookNode = window.ViewModel.Books.Single();
                var bookItem = (TreeViewItem)tree.ItemContainerGenerator.ContainerFromItem(bookNode);
                bookItem.IsExpanded = true;
                WpfTestHelpers.Wait(() => bookNode.LoadState == NodeLoadState.Loaded);
                WpfTestHelpers.Pump();
                var goodNode = bookNode.Children.Single(n => n.PassagePath == goodPath);
                var badNode = bookNode.Children.Single(n => n.PassagePath == badPath);
                var goodItem = (TreeViewItem)bookItem.ItemContainerGenerator.ContainerFromItem(goodNode);
                var badItem = (TreeViewItem)bookItem.ItemContainerGenerator.ContainerFromItem(badNode);
                goodItem.IsSelected = true;
                WpfTestHelpers.Pump();
                Program.Check(window.ViewModel.Article?.FilePath == goodPath);
                badItem.IsSelected = true;
                WpfTestHelpers.Pump();
                Program.Check(window.ViewModel.Article?.FilePath == goodPath);
                Program.Check(ReferenceEquals(tree.SelectedItem, goodNode));
                Program.Check(goodItem.IsSelected && !badItem.IsSelected);
            }
            finally
            {
                window.Close();
                WpfTestHelpers.Pump();
                Directory.Delete(root, true);
            }
        });

        Program.Run("tree expands one level at a time and collapse invalidates descendants", () =>
        {
            var window = new ReadArticles.MainWindow(false);
            try
            {
                window.Show();
                Program.Check(window.ViewModel.OpenLibrary(TestData.LibraryRoot));
                WpfTestHelpers.Pump();
                var bookNode = window.ViewModel.Books.Single();
                Program.Check(bookNode.LoadState == NodeLoadState.NotLoaded);
                Program.Check(!bookNode.Children.Any(n => n.IsPassage));
                var tree = (TreeView)window.FindName("LibraryTree");
                window.UpdateLayout();
                var book = (TreeViewItem)tree.ItemContainerGenerator.ContainerFromIndex(0);
                book.IsExpanded = true;
                PumpUntil(() => bookNode.LoadState == NodeLoadState.Loaded);
                Program.Check(bookNode.Children.Any(n => n.Name == "2026-09-26"));
                Program.Check(!bookNode.Children.SelectMany(n => n.Children).Any(n => n.IsPassage));
                var dateNode = bookNode.Children.First(n => n.Name == "2026-09-26");
                var date = (TreeViewItem)book.ItemContainerGenerator.ContainerFromItem(dateNode);
                date.IsExpanded = true;
                PumpUntil(() => dateNode.LoadState == NodeLoadState.Loaded);
                Program.Check(dateNode.Children.Any(n => n.Name == "When America walks away"));
                var folderNode = dateNode.Children.First(n => n.Name == "When America walks away");
                var folder = (TreeViewItem)date.ItemContainerGenerator.ContainerFromItem(folderNode);
                folder.IsExpanded = true;
                PumpUntil(() => folderNode.LoadState == NodeLoadState.Loaded);
                Program.Check(folderNode.Children.Any(n => n.PassagePath == TestData.ArticlePath));

                book.IsExpanded = false;
                WpfTestHelpers.Pump();
                Program.Check(!bookNode.IsExpanded && !dateNode.IsExpanded &&
                    !folderNode.IsExpanded && bookNode.LoadState == NodeLoadState.NotLoaded);
                book.IsExpanded = true;
                PumpUntil(() => bookNode.LoadState == NodeLoadState.Loaded);
                Program.Check(bookNode.Children.Any(n => n.Name == "2026-09-26"));
                Program.Check(!bookNode.Children.Any(n => n.LoadState == NodeLoadState.Loaded));

                Program.Check(window.SelectPassage(TestData.ArticlePath));
                WpfTestHelpers.Pump(); window.UpdateLayout();
                var scroll = WpfTestHelpers.Descendants((ListBox)window.FindName("VocabularyList"))
                    .OfType<System.Windows.Controls.Primitives.ScrollBar>()
                    .First(s => s.Orientation == Orientation.Vertical);
                Program.Check(Math.Abs(scroll.ActualWidth -
                    Math.Round(Math.Round(System.Windows.SystemParameters.VerticalScrollBarWidth) / 2)) < 0.01);
                Program.Check(Math.Abs(tree.FontSize - 11.0 * 96 / 72) < 0.001);
                Program.Check(Math.Abs(((TextBlock)window.FindName("ArticleTitleText")).FontSize - 12.0 * 96 / 72) < 0.001);
                Program.Check(Math.Abs(((ListBox)window.FindName("VocabularyList")).FontSize - 15) < 0.001);
            }
            finally { window.Close(); WpfTestHelpers.Pump(); }
        });
    }

    private static void PumpUntil(Func<bool> completed)
    {
        for (int i = 0; i < 100; i++)
        {
            WpfTestHelpers.Pump(20);
            if (completed()) return;
        }
        throw new Exception("Timed out waiting for lazy directory scan.");
    }
}
