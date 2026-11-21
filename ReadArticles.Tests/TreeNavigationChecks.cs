using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.IO;
using ReadArticles.Models;

internal static class TreeNavigationChecks
{
    internal static void Run()
    {
        Program.Run("native tree and button PreviewKeyDown are not stolen by playback shortcuts", () =>
        {
            var window = new ReadArticles.MainWindow(persistSettings: false);
            try
            {
                window.Show();
                var tree = (TreeView)window.FindName("LibraryTree");
                var settingsButton = (Button)window.FindName("SettingsButton");
                var presentationSource = PresentationSource.FromVisual(window);
                Program.Check(presentationSource is not null);
                foreach (UIElement target in new UIElement[] { tree, settingsButton })
                {
                    foreach (Key key in new[] { Key.Left, Key.Right, Key.Space })
                    {
                        var input = new KeyEventArgs(Keyboard.PrimaryDevice, presentationSource!, 0, key)
                        {
                            RoutedEvent = Keyboard.PreviewKeyDownEvent
                        };
                        target.RaiseEvent(input);
                        Program.Check(!input.Handled);
                    }
                }
            }
            finally { window.Close(); WpfTestHelpers.Pump(); }
        });

        Program.Run("collapse cancels an in-flight scan and a later expansion retries", () =>
        {
            string root = Path.Combine(Path.GetTempPath(), "ReadArticles-tree-cancel-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            var window = new ReadArticles.MainWindow(false);
            try
            {
                File.WriteAllText(Path.Combine(root, "book.json"), "marker");
                File.WriteAllText(Path.Combine(root, "Article.json"), """{"filetype":"passage"}""");
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
                File.WriteAllText(Path.Combine(root, "Article.json"), """{"filetype":"passage"}""");
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
                File.WriteAllText(badPath, """{"filetype":"passage","paragraphs":123}""");
                window.Show();
                Program.Check(window.ViewModel.OpenLibrary(root));
                WpfTestHelpers.Pump();
                var tree = (TreeView)window.FindName("LibraryTree");
                var bookNode = window.ViewModel.Books.Single();
                var bookItem = (TreeViewItem)tree.ItemContainerGenerator.ContainerFromItem(bookNode);
                bookItem.IsExpanded = true;
                WpfTestHelpers.Wait(() => bookNode.LoadState == NodeLoadState.Loaded);
                WpfTestHelpers.Pump();
                bookItem.UpdateLayout();
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

        Program.Run("failed nested passage click makes one attempt and permits explicit retry", () =>
        {
            string root = Path.Combine(Path.GetTempPath(),
                "ReadArticles-nested-selection-" + Guid.NewGuid().ToString("N"));
            string book = Path.Combine(root, "Book");
            string goodFolder = Path.Combine(book, "Good");
            string badFolder = Path.Combine(book, "Bad");
            Directory.CreateDirectory(goodFolder);
            Directory.CreateDirectory(badFolder);
            var window = new ReadArticles.MainWindow(false);
            try
            {
                File.WriteAllText(Path.Combine(book, "book.json"), "{}");
                string goodPath = Path.Combine(goodFolder, "Good.json");
                string badPath = Path.Combine(badFolder, "Bad.json");
                File.Copy(TestData.ArticlePath, goodPath);
                File.WriteAllText(badPath, """{"filetype":"passage","title":"Bad","paragraphs":123}""");
                window.Show();
                Program.Check(window.ViewModel.OpenLibrary(root));
                WpfTestHelpers.Pump();
                var tree = (TreeView)window.FindName("LibraryTree");
                WpfTestHelpers.ExpandAllDirectories(tree);
                var bookNode = window.ViewModel.Books.Single();
                var bookItem = (TreeViewItem)tree.ItemContainerGenerator.ContainerFromItem(bookNode);
                TreeViewItem Leaf(string name)
                {
                    var folderNode = bookNode.Children.Single(n => n.Name == name);
                    var folderItem = (TreeViewItem)bookItem.ItemContainerGenerator.ContainerFromItem(folderNode);
                    var leafNode = folderNode.Children.Single(n => n.IsPassage);
                    return (TreeViewItem)folderItem.ItemContainerGenerator.ContainerFromItem(leafNode);
                }
                var goodItem = Leaf("Good");
                var badItem = Leaf("Bad");
                goodItem.IsSelected = true;
                WpfTestHelpers.Pump();
                Program.Check(window.ViewModel.Article?.FilePath == goodPath);

                // Exercise TreeViewItem's mouse event path, not only direct IsSelected changes.
                var click = new MouseButtonEventArgs(Mouse.PrimaryDevice,
                    Environment.TickCount, MouseButton.Left)
                { RoutedEvent = UIElement.MouseLeftButtonDownEvent };
                badItem.RaiseEvent(click);
                WpfTestHelpers.Pump(150);
                var failures = window.ViewModel.Notifications.Messages
                    .Count(n => n.Key == "UserOperationFailed");
                Program.Check(failures == 1 &&
                    window.ViewModel.Article?.FilePath == goodPath &&
                    goodItem.IsSelected && !badItem.IsSelected);

                // A later explicit attempt is not blocked after the file has been fixed.
                File.Copy(TestData.ArticlePath, badPath, true);
                badItem.IsSelected = true;
                WpfTestHelpers.Pump();
                Program.Check(window.ViewModel.Article?.FilePath == badPath);
            }
            finally
            {
                window.Close();
                WpfTestHelpers.Pump();
                Directory.Delete(root, true);
            }
        });

        Program.Run("tree labels stay next to expanders when left pane changes width", () =>
        {
            var window = new ReadArticles.MainWindow(false);
            try
            {
                window.Show();
                Program.Check(window.ViewModel.OpenLibrary(TestData.LibraryRoot));
                WpfTestHelpers.Pump();
                var tree = (TreeView)window.FindName("LibraryTree");
                var left = (ColumnDefinition)window.FindName("LeftColumn");
                double Gap()
                {
                    window.UpdateLayout();
                    var item = (TreeViewItem)tree.ItemContainerGenerator.ContainerFromIndex(0);
                    item.ApplyTemplate();
                    var expander = (ToggleButton)item.Template.FindName("Expander", item);
                    var label = WpfTestHelpers.Descendants(item).OfType<TextBlock>()
                        .First(text => text.Text == window.ViewModel.Books[0].Name);
                    return label.TranslatePoint(new Point(), expander).X - expander.ActualWidth;
                }

                var grid = (Grid)window.Content;
                var center = (ColumnDefinition)window.FindName("CenterColumn");
                // A small work area may not fit a 390-DIP library beside the
                // WebView2 host minimum. Exercise only widths that can fit.
                double maximum = Math.Min(left.MaxWidth, Math.Floor(
                    grid.ActualWidth - grid.ColumnDefinitions[1].ActualWidth - center.MinWidth));
                Program.Check(maximum >= left.MinWidth);
                double narrowTarget = Math.Min(230, maximum);
                double wideTarget = Math.Min(390, maximum);

                left.Width = new GridLength(narrowTarget);
                double narrow = Gap();
                Program.Check(Math.Abs(left.ActualWidth - narrowTarget) < 1);
                left.Width = new GridLength(wideTarget);
                double wide = Gap();
                Program.Check(Math.Abs(left.ActualWidth - wideTarget) < 1);
                Program.Check(narrow >= 0 && narrow <= 5 && Math.Abs(narrow - wide) < 0.5);
            }
            finally { window.Close(); WpfTestHelpers.Pump(); }
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
                Program.Check(Math.Abs(tree.FontSize - 11.0 * 96 / 72) < 0.001);
                Program.Check(window.FindName("ReaderWebView") is not null);
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
