using System.Windows.Controls;

internal static class TreeNavigationChecks
{
    internal static void Run()
    {
        Program.Run("tree opens collapsed and recursively resets descendants on collapse", () =>
        {
            var window = new ReadArticles.MainWindow(false);
            try
            {
                window.Show();
                Program.Check(window.ViewModel.OpenLibrary(TestData.LibraryRoot));
                WpfTestHelpers.Pump(); window.UpdateLayout();
                var tree = (TreeView)window.FindName("LibraryTree");
                var book = (TreeViewItem)tree.ItemContainerGenerator.ContainerFromIndex(0);
                Program.Check(!book.IsExpanded);
                book.IsExpanded = true; window.UpdateLayout();
                var date = (TreeViewItem)book.ItemContainerGenerator.ContainerFromIndex(0);
                Program.Check(!date.IsExpanded);
                date.IsExpanded = true; window.UpdateLayout();
                var folder = (TreeViewItem)date.ItemContainerGenerator.ContainerFromIndex(0);
                folder.IsExpanded = true; window.UpdateLayout();
                book.IsExpanded = false;
                WpfTestHelpers.Pump();
                Program.Check(!date.IsExpanded && !folder.IsExpanded);
                book.IsExpanded = true; window.UpdateLayout();
                Program.Check(!date.IsExpanded);
                date.IsExpanded = true; window.UpdateLayout();
                Program.Check(!folder.IsExpanded);
                book.IsExpanded = false;
                Program.Check(window.SelectPassage(TestData.ArticlePath));
                WpfTestHelpers.Pump(); window.UpdateLayout();
                Program.Check(!book.IsExpanded);
                var bookNode = window.ViewModel.Books[0];
                Program.Check(!bookNode.Children.First(n => n.Name == "2026-09-19").IsExpanded);
                Program.Check(!bookNode.Children.First(n => n.Name == "2026-09-26").IsExpanded);
                book.IsExpanded = true; window.UpdateLayout();
                date.IsExpanded = true; window.UpdateLayout();
                folder.IsExpanded = true; window.UpdateLayout();
                var otherDate = (TreeViewItem)book.ItemContainerGenerator.ContainerFromIndex(1);
                otherDate.IsExpanded = true; window.UpdateLayout();
                Program.Check(date.IsExpanded && folder.IsExpanded);
                Program.Check(!((TreeViewItem)otherDate.ItemContainerGenerator.ContainerFromIndex(0)).IsExpanded);
                otherDate.IsExpanded = false; window.UpdateLayout();
                Program.Check(date.IsExpanded && folder.IsExpanded);
                var scroll = WpfTestHelpers.Descendants((ListBox)window.FindName("VocabularyList"))
                    .OfType<System.Windows.Controls.Primitives.ScrollBar>().First(s => s.Orientation == Orientation.Vertical);
                Program.Check(Math.Abs(scroll.ActualWidth - Math.Round(Math.Round(System.Windows.SystemParameters.VerticalScrollBarWidth) / 2)) < 0.01);
                Program.Check(Math.Abs(tree.FontSize - 11.0 * 96 / 72) < 0.001);
                Program.Check(Math.Abs(((TextBlock)window.FindName("ArticleTitleText")).FontSize - 12.0 * 96 / 72) < 0.001);
                Program.Check(Math.Abs(((ListBox)window.FindName("VocabularyList")).FontSize - 15) < 0.001);
            }
            finally { window.Close(); }
        });
    }
}
