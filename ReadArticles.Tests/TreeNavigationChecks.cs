using System.Windows.Controls;
using ReadArticles.Models;

internal static class TreeNavigationChecks
{
    internal static void Run()
    {
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
