using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.IO;
using ReadArticles.Models;
using ReadArticles.Infrastructure;

internal static class TreeNavigationChecks
{
    internal static void Run()
    {
        Program.Run("native tree and button retain non-playback keyboard input", () =>
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
                    foreach (Key key in new[] { Key.Up, Key.Down, Key.Tab, Key.Enter })
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

        Program.Run("explicit library refresh reopens current root and deselects a missing root", () =>
        {
            string temp = Path.Combine(Path.GetTempPath(), "ReadArticles-refresh-" + Guid.NewGuid().ToString("N"));
            string root = Path.Combine(temp, "Library");
            string book = Path.Combine(root, "Book");
            Directory.CreateDirectory(book);
            var window = new ReadArticles.MainWindow(false);
            try
            {
                File.WriteAllText(Path.Combine(book, "book.json"), "{}");
                string passage = Path.Combine(book, "Article.json");
                File.Copy(TestData.ArticlePath, passage);
                window.Show();
                var vm = window.ViewModel;
                var refreshButton = (Button)window.FindName("RefreshButton");
                Program.Check(!refreshButton.IsEnabled && !vm.RefreshLibraryAsync().GetAwaiter().GetResult());
                Program.Check(vm.OpenLibrary(root) && vm.OpenArticle(passage));
                WpfTestHelpers.Wait(() => refreshButton.IsEnabled);
                var oldBook = vm.Books.Single();
                oldBook.IsExpanded = true;
                Task scan = vm.ExpandDirectoryAsync(oldBook);
                string added = Path.Combine(root, "Added");
                Directory.CreateDirectory(added);
                File.WriteAllText(Path.Combine(added, "book.json"), "{}");
                refreshButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                WpfTestHelpers.Wait(() => scan.IsCompleted && vm.Article is null && vm.Books.Count == 2);
                scan.GetAwaiter().GetResult();
                Program.Check(vm.LibraryRoot == root && vm.Words.Count == 0 &&
                    !vm.Playback.IsArticleActive && vm.Books.All(n => !n.IsExpanded) &&
                    !vm.Books.Contains(oldBook));
                Program.Check(vm.OpenArticle(passage));
                Directory.Move(root, Path.Combine(temp, "Removed"));
                Task<bool> staleRefresh = vm.RefreshLibraryAsync();
                Task<bool> replacement = vm.OpenLibraryAsync(TestData.LibraryRoot);
                WpfTestHelpers.Wait(() => staleRefresh.IsCompleted && replacement.IsCompleted);
                Program.Check(!staleRefresh.GetAwaiter().GetResult() && replacement.GetAwaiter().GetResult() &&
                    vm.LibraryRoot == TestData.LibraryRoot);
                Directory.Move(Path.Combine(temp, "Removed"), root);
                Program.Check(vm.OpenLibrary(root) && vm.OpenArticle(passage));
                Directory.Move(root, Path.Combine(temp, "Removed"));
                File.WriteAllText(root, "not a directory");
                refreshButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Task filter = vm.ApplySettings(new ReaderSettings { ExcludedFolders = "video" });
                WpfTestHelpers.Wait(() => filter.IsCompleted && vm.LibraryRoot is null && !refreshButton.IsEnabled &&
                    vm.Notifications.Messages.Any(n => n.Key == "LibraryOpenFailed"));
                filter.GetAwaiter().GetResult();
                Program.Check(vm.Books.Count == 0 && vm.Article is null && vm.Words.Count == 0 &&
                    !vm.Playback.IsArticleActive);
            }
            finally
            {
                window.Close();
                WpfTestHelpers.Pump();
                Directory.Delete(temp, true);
            }
        });

        Program.Run("changing folder exclusions refreshes only navigation and invalidates old scans", () =>
        {
            string root = Path.Combine(Path.GetTempPath(), "ReadArticles-exclusions-" + Guid.NewGuid().ToString("N"));
            string book = Path.Combine(root, "Book");
            string folder = Path.Combine(book, "video");
            Directory.CreateDirectory(folder);
            var window = new ReadArticles.MainWindow(false);
            try
            {
                File.WriteAllText(Path.Combine(book, "book.json"), "{}");
                string passage = Path.Combine(folder, "Article.json");
                File.Copy(TestData.ArticlePath, passage);
                window.Show();
                var vm = window.ViewModel;
                Program.Check(vm.OpenLibrary(root) && vm.OpenArticle(passage));
                var article = vm.Article;
                var oldBook = vm.Books.Single();
                oldBook.IsExpanded = true;
                Task pending = vm.ExpandDirectoryAsync(oldBook);
                var settings = new ReaderSettings { ExcludedFolders = "video; AUDIO, cache" };
                Task refresh = vm.ApplySettings(settings);
                WpfTestHelpers.Wait(() => pending.IsCompleted && refresh.IsCompleted);
                pending.GetAwaiter().GetResult();
                refresh.GetAwaiter().GetResult();
                Program.Check(ReferenceEquals(article, vm.Article) && vm.LibraryRoot == root);
                Program.Check(oldBook.LoadState != NodeLoadState.Loaded &&
                    !ReferenceEquals(oldBook, vm.Books.Single()));
                var newBook = vm.Books.Single();
                Program.Check(!newBook.IsExpanded && newBook.LoadState == NodeLoadState.NotLoaded);
                newBook.IsExpanded = true;
                Task expand = vm.ExpandDirectoryAsync(newBook);
                WpfTestHelpers.Wait(() => expand.IsCompleted && newBook.LoadState == NodeLoadState.Loaded);
                expand.GetAwaiter().GetResult();
                Program.Check(newBook.Children.All(n => n.Name != "video"));
                Task unchanged = vm.ApplySettings(new ReaderSettings { ExcludedFolders = "CACHE, video, audio, video" });
                Program.Check(unchanged.IsCompleted && ReferenceEquals(newBook, vm.Books.Single()));
                Task restored = vm.ApplySettings(new ReaderSettings { ExcludedFolders = "" });
                WpfTestHelpers.Wait(() => restored.IsCompleted);
                restored.GetAwaiter().GetResult();
                var restoredBook = vm.Books.Single();
                restoredBook.IsExpanded = true;
                Task scan = vm.ExpandDirectoryAsync(restoredBook);
                WpfTestHelpers.Wait(() => scan.IsCompleted && restoredBook.LoadState == NodeLoadState.Loaded);
                scan.GetAwaiter().GetResult();
                Program.Check(restoredBook.Children.Any(n => n.Name == "video") &&
                    ReferenceEquals(vm.Article, article));
                Directory.Delete(root, true);
                Task failed = vm.ApplySettings(settings);
                WpfTestHelpers.Wait(() => failed.IsCompleted);
                failed.GetAwaiter().GetResult();
                Program.Check(vm.Books.Count == 0 && vm.LibraryRoot == root &&
                    ReferenceEquals(vm.Article, article));
            }
            finally
            {
                window.Close();
                WpfTestHelpers.Pump();
                if (Directory.Exists(root)) Directory.Delete(root, true);
            }
        });

        Program.Run("pending library switches preserve the latest filter and user selection", () =>
        {
            string temp = Path.Combine(Path.GetTempPath(), "ReadArticles-root-filter-" + Guid.NewGuid().ToString("N"));
            string a = Path.Combine(temp, "A"), b = Path.Combine(temp, "B");
            foreach (string root in new[] { a, b })
                foreach (string name in new[] { "Keep", "Video", "Audio" })
                {
                    string book = Path.Combine(root, name);
                    Directory.CreateDirectory(book);
                    File.WriteAllText(Path.Combine(book, "book.json"), "{}");
                }
            var window = new ReadArticles.MainWindow(false);
            try
            {
                var vm = window.ViewModel;
                Task<bool> initial = vm.OpenLibraryAsync(a);
                Task initialFilter = vm.ApplySettings(new ReaderSettings { ExcludedFolders = "video" });
                WpfTestHelpers.Wait(() => initial.IsCompleted && initialFilter.IsCompleted);
                Program.Check(initial.GetAwaiter().GetResult());
                initialFilter.GetAwaiter().GetResult();
                Program.Check(vm.LibraryRoot == a && vm.Books.Count == 2 &&
                    vm.Books.All(n => n.Name != "Video"));

                // No dispatcher pump between the requests: no earlier root result can commit.
                Task<bool> switched = vm.OpenLibraryAsync(b);
                Task first = vm.ApplySettings(new ReaderSettings { ExcludedFolders = "audio" });
                Task latest = vm.ApplySettings(new ReaderSettings { ExcludedFolders = "audio, video" });
                WpfTestHelpers.Wait(() => switched.IsCompleted && first.IsCompleted && latest.IsCompleted);
                Program.Check(switched.GetAwaiter().GetResult());
                first.GetAwaiter().GetResult();
                latest.GetAwaiter().GetResult();
                Program.Check(vm.LibraryRoot == b && vm.Books.Single().Name == "Keep");

                string passage = Path.Combine(b, "Keep", "Article.json");
                File.Copy(TestData.ArticlePath, passage);
                Program.Check(vm.OpenArticle(passage));
                var article = vm.Article;
                Task<bool> failed = vm.OpenLibraryAsync(Path.Combine(temp, "missing"));
                Task fallback = vm.ApplySettings(new ReaderSettings { ExcludedFolders = "video" });
                WpfTestHelpers.Wait(() => failed.IsCompleted && fallback.IsCompleted);
                Program.Check(!failed.GetAwaiter().GetResult());
                fallback.GetAwaiter().GetResult();
                Program.Check(vm.LibraryRoot == b && ReferenceEquals(vm.Article, article) &&
                    vm.Books.Select(n => n.Name).SequenceEqual(new[] { "Audio", "Keep" }));

                // Schedule the second edit after the failure handler starts its
                // fallback but before any Normal-priority scan result can commit.
                vm.Notifications.Clear();
                Task? lateFilter = null;
                bool scheduled = false;
                void OnFailure()
                {
                    if (scheduled || !vm.Notifications.Messages.Any(n => n.Key == "LibraryOpenFailed")) return;
                    scheduled = true;
                    window.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Send,
                        new Action(() => lateFilter = vm.ApplySettings(
                            new ReaderSettings { ExcludedFolders = "audio" })));
                }
                vm.Notifications.Changed += OnFailure;
                try
                {
                    Task<bool> failedAgain = vm.OpenLibraryAsync(Path.Combine(temp, "missing-again"));
                    Task firstFilter = vm.ApplySettings(new ReaderSettings { ExcludedFolders = "audio, video" });
                    WpfTestHelpers.Wait(() => failedAgain.IsCompleted && firstFilter.IsCompleted &&
                        lateFilter is { IsCompleted: true });
                    Program.Check(!failedAgain.GetAwaiter().GetResult());
                    firstFilter.GetAwaiter().GetResult();
                    lateFilter!.GetAwaiter().GetResult();
                    Program.Check(vm.LibraryRoot == b && ReferenceEquals(vm.Article, article) &&
                        vm.Books.Select(n => n.Name).SequenceEqual(new[] { "Keep", "Video" }));
                }
                finally { vm.Notifications.Changed -= OnFailure; }

                Task refresh = vm.ApplySettings(new ReaderSettings { ExcludedFolders = "" });
                vm.CloseNavigation();
                WpfTestHelpers.Wait(() => refresh.IsCompleted);
                refresh.GetAwaiter().GetResult();
                Program.Check(vm.Books.Count == 0 && ReferenceEquals(vm.Article, article));
            }
            finally
            {
                window.Close();
                WpfTestHelpers.Pump();
                Directory.Delete(temp, true);
            }
        });

        Program.Run("junction paths cannot bypass passage and companion safety checks", () =>
        {
            string root = Path.Combine(Path.GetTempPath(), "ReadArticles-link-" + Guid.NewGuid().ToString("N"));
            string library = Path.Combine(root, "Library");
            string book = Path.Combine(library, "Book");
            string outside = Path.Combine(root, "Outside");
            Directory.CreateDirectory(book);
            Directory.CreateDirectory(outside);
            string link = Path.Combine(book, "Linked");
            string passage = Path.Combine(book, "Reading.json");
            string externalPassage = Path.Combine(outside, "Reading.json");
            string externalVocabulary = Path.ChangeExtension(externalPassage, ".vocabulary.json");
            string companionLink = Path.ChangeExtension(passage, ".vocabulary.json");
            string scriptLink = Path.ChangeExtension(passage, ".script.json");
            void Junction(string destination)
            {
                using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = $"/d /c mklink /J \"{destination}\" \"{outside}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true
                }) ?? throw new Exception("Could not create junction fixture.");
                process.WaitForExit();
                Program.Check(process.ExitCode == 0 &&
                    (File.GetAttributes(destination) & FileAttributes.ReparsePoint) != 0);
            }
            void Reject(Action action)
            {
                try { action(); }
                catch (ArgumentException) { return; }
                throw new Exception("Reparse path was accepted.");
            }
            var window = new ReadArticles.MainWindow(false);
            try
            {
                File.WriteAllText(Path.Combine(book, "book.json"), "{}");
                File.Copy(TestData.ArticlePath, passage);
                File.Copy(TestData.ArticlePath, externalPassage);
                File.WriteAllText(externalVocabulary, """{"filetype":"vocabulary","words":[]}""");
                string original = JsonFiles.Fingerprint(externalVocabulary);
                Junction(link);
                Junction(companionLink);
                Junction(scriptLink);
                var vm = window.ViewModel;
                Program.Check(vm.OpenLibrary(library) && window.SelectPassage(passage));
                var article = vm.Article;
                Program.Check(article is not null && !article.Vocabulary.CanWrite && article.Warnings.Count > 0);
                Program.Check(!window.SelectPassage(Path.Combine(link, "Reading.json")) &&
                    ReferenceEquals(vm.Article, article));
                Program.Check(!vm.OpenArticle(Path.Combine(link, "Reading.json")));
                Program.Check(vm.PrepareReadingMode(out _, out _) == ReadArticles.ViewModels.ReadingModePreparation.Failed &&
                    !vm.IsScriptMode);
                var repository = new ReadArticles.Services.VocabularyRepository();
                string linkedVocabulary = Path.Combine(link, "Reading.vocabulary.json");
                Reject(() => repository.Load(linkedVocabulary));
                Reject(() => repository.Save(linkedVocabulary, new VocabularyData(), original));
                Reject(() => repository.RestoreLatestBackup(linkedVocabulary, original));
                Reject(() => repository.Save(companionLink, new VocabularyData(), null));
                Program.Check(JsonFiles.Fingerprint(externalVocabulary) == original);
            }
            finally
            {
                window.Close();
                WpfTestHelpers.Pump();
                // Remove links themselves before recursive fixture cleanup.
                foreach (string junction in new[] { link, companionLink, scriptLink })
                    if (Directory.Exists(junction)) Directory.Delete(junction);
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
