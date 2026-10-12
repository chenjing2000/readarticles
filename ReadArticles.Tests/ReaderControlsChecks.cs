using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using ReadArticles.Infrastructure;
using ReadArticles.Services;

internal static class ReaderControlsChecks
{
    internal static void Run()
    {
        Program.Run("settings own value normalization on load and save", () =>
        {
            string path = ReaderSettings.PathName;
            byte[]? before = File.Exists(path) ? File.ReadAllBytes(path) : null;
            try
            {
                JsonFiles.WriteAtomic(path, new { LeftWidth = 221.4, RightWidth = 281.8, FontSize = 99,
                    Volume = 3, Accent = "invalid", ForwardStep = 1, BackwardStep = 1 });
                var settings = ReaderSettings.Load(out var warning);
                Program.Check(warning == null && settings.LeftWidth == 221 && settings.RightWidth == 282);
                Program.Check(settings.FontSize == 16 && settings.Volume == 1 && settings.Accent == "uk");
                Program.Check(settings.ForwardStep == 4 && settings.BackwardStep == 5);
                settings.FontSize = double.NaN;
                settings.Volume = double.NaN;
                settings.Save();
                settings = ReaderSettings.Load(out warning);
                Program.Check(warning == null && settings.FontSize == 15 && settings.Volume == 0.65);
            }
            finally { if (before == null) File.Delete(path); else File.WriteAllBytes(path, before); }
        });
        Program.Run("settings initialize beside the executable and changes save before close", () =>
        {
            string path = ReaderSettings.PathName;
            byte[]? previous = File.Exists(path) ? File.ReadAllBytes(path) : null;
            ReadArticles.MainWindow? window = null;
            try
            {
                File.Delete(path);
                window = new ReadArticles.MainWindow { ShowActivated = false, Left = -10000, Top = -10000 };
                window.Show();
                Program.Check(File.Exists(path) && Path.GetFileName(path) == Path.GetFileNameWithoutExtension(Environment.ProcessPath) + ".settings.json");
                var defaults = ReaderSettings.Load(out var warning);
                Program.Check(warning == null && defaults.ForwardStep == 4 && defaults.BackwardStep == 5 && defaults.FontSize == 15);
                var menu = ((Button)window.FindName("SettingsButton")).ContextMenu;
                var progress = (MenuItem)menu.Items[2];
                ClickStep((MenuItem)((MenuItem)progress.Items[0]).Items[1]);
                window.ViewModel.Accent = "us";
                Program.Check(window.ViewModel.OpenLibrary(TestData.LibraryRoot));
                var saved = ReaderSettings.Load(out warning);
                Program.Check(warning == null && saved.ForwardStep == 7 && saved.Accent == "us" && saved.Library == TestData.LibraryRoot);
                saved.LeftWidth = 221.4;
                saved.RightWidth = 281.8;
                saved.Save();
                saved = ReaderSettings.Load(out warning);
                Program.Check(saved.LeftWidth == 221 && saved.RightWidth == 282 && saved.FontSize == 15);
            }
            finally
            {
                window?.Close();
                if (previous == null) File.Delete(path); else File.WriteAllBytes(path, previous);
            }
        });
        Program.Run("buttons show pale teal on keyboard focus including the settings circle", () =>
        {
            var window = CreateWindow();
            try
            {
                window.ShowActivated = true;
                window.Show();
                window.Activate();
                LoadArticle(window);
                foreach (var button in WpfTestHelpers.Descendants(window).OfType<Button>().Where(button => button.IsEnabled && button.Focusable && button.IsVisible))
                {
                    Keyboard.Focus(button);
                    WpfTestHelpers.Pump(20);
                    Program.Check(button.IsKeyboardFocused);
                    button.ApplyTemplate();
                    var surface = button.Template.FindName("Surface", button) as Border;
                    var circle = button.Template.FindName("SettingsCircle", button) as System.Windows.Shapes.Ellipse;
                    var brush = surface?.Background ?? circle?.Fill;
                    Program.Check(brush is System.Windows.Media.SolidColorBrush color && color.Color == System.Windows.Media.Color.FromRgb(215, 239, 235));
                }
            }
            finally { window.Close(); WpfTestHelpers.Pump(); }
        });
        Program.Run("global keyboard focus visual is empty for controls and popup menus", () =>
        {
            var style = (Style)Application.Current.FindResource(SystemParameters.FocusVisualStyleKey);
            var focusVisual = new Control { Style = style };
            focusVisual.ApplyTemplate();
            Program.Check(System.Windows.Media.VisualTreeHelper.GetChildrenCount(focusVisual) == 0);
            var window = CreateWindow();
            try
            {
                window.Show();
                LoadArticle(window);
                foreach (var control in WpfTestHelpers.Descendants(window).OfType<Control>())
                    Program.Check(ReferenceEquals(control.FindResource(SystemParameters.FocusVisualStyleKey), style));
                var menu = ((Button)window.FindName("SettingsButton")).ContextMenu;
                menu.IsOpen = true;
                WpfTestHelpers.Pump(30);
                foreach (MenuItem item in menu.Items)
                    Program.Check(ReferenceEquals(item.FindResource(SystemParameters.FocusVisualStyleKey), style));
                menu.IsOpen = false;
            }
            finally { window.Close(); WpfTestHelpers.Pump(); }
        });
        Program.Run("playback time uses minutes through one hour and hours beyond it", () =>
        {
            var format = typeof(ReadArticles.MainWindow).GetMethod("FormatPlaybackTime", BindingFlags.NonPublic | BindingFlags.Static)!;
            foreach (var sample in new[] { (0.0, "00:00"), (190.0, "03:10"), (3599.0, "59:59"),
                (3600.0, "60:00"), (3601.0, "01:00:01"), (3790.0, "01:03:10"), (90061.0, "25:01:01") })
                Program.Check((string)format.Invoke(null, new object[] { sample.Item1 })! == sample.Item2);
        });
        Program.Run("progress menus select independent steps and preserve them across restart", () =>
        {
            string path = ReaderSettings.PathName;
            byte[]? previous = File.Exists(path) ? File.ReadAllBytes(path) : null;
            ReadArticles.MainWindow? window = null;
            try
            {
                new ReaderSettings().Save();
                window = new ReadArticles.MainWindow { ShowActivated = false, Left = -10000, Top = -10000 };
                window.Show();
                PressArrow(window, (Button)window.FindName("SettingsButton"), Key.Space);
                Program.Check(window.ViewModel.Article == null && !((Button)window.FindName("SettingsButton")).ContextMenu.IsOpen);
                var menu = (MenuItem)((Button)window.FindName("SettingsButton")).ContextMenu.Items[2];
                Program.Check((string)menu.Header == "进度条" && menu.Items.Count == 2);
                var forward = (MenuItem)menu.Items[0];
                var backward = (MenuItem)menu.Items[1];
                Program.Check((string)forward.Header == "前进" && (string)backward.Header == "后退");
                Program.Check(forward.Items.Cast<MenuItem>().Select(i => (string)i.Header).SequenceEqual(new[] { "4 秒", "7 秒", "10 秒" }));
                Program.Check(backward.Items.Cast<MenuItem>().Select(i => (string)i.Header).SequenceEqual(new[] { "5 秒", "8 秒", "15 秒" }));
                foreach (var branch in new[] { forward, backward })
                {
                    Program.Check(((MenuItem)branch.Items[0]).IsChecked);
                    foreach (MenuItem item in branch.Items)
                    {
                        ClickStep(item);
                        Program.Check(item.IsChecked && branch.Items.Cast<MenuItem>().Count(i => i.IsChecked) == 1);
                        Program.Check(Math.Abs(item.FontSize - 10.0 * 96 / 72) < 0.001);
                        foreach (MenuItem option in branch.Items)
                        {
                            option.ApplyTemplate();
                            Program.Check(option.Template.FindName("Check", option) == null);
                            var surface = (Border)option.Template.FindName("ItemSurface", option);
                            var color = ((System.Windows.Media.SolidColorBrush)surface.Background).Color;
                            if (option.IsChecked) Program.Check(color == System.Windows.Media.Color.FromRgb(135, 192, 202));
                            else Program.Check(color == System.Windows.Media.Color.FromRgb(252, 252, 253));
                        }
                    }
                }
                var context = ((Button)window.FindName("SettingsButton")).ContextMenu;
                context.IsOpen = true;
                menu.IsSubmenuOpen = true;
                forward.IsSubmenuOpen = true;
                WpfTestHelpers.Pump(30);
                var valuesPopup = (Popup)forward.Template.FindName("PART_Popup", forward);
                Program.Check(valuesPopup.IsOpen && ((Border)valuesPopup.Child).ActualWidth >= 45 && ((Border)valuesPopup.Child).ActualWidth <= 80);
                var directionPopup = (Popup)menu.Template.FindName("PART_Popup", menu);
                Program.Check(((Border)directionPopup.Child).ActualWidth >= 60 && ((Border)directionPopup.Child).ActualWidth <= 80);
                var valuesSurface = (Border)valuesPopup.Child;
                var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap((int)(valuesSurface.ActualWidth + 16), (int)(valuesSurface.ActualHeight + 16), 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
                bitmap.Render(valuesSurface);
                var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
                encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
                Directory.CreateDirectory(Path.GetFullPath("artifacts"));
                using (var file = File.Create(Path.GetFullPath("artifacts/progress-options.png"))) encoder.Save(file);
                context.IsOpen = false;
                window.Close();
                window = new ReadArticles.MainWindow { ShowActivated = false, Left = -10000, Top = -10000 };
                window.Show();
                menu = (MenuItem)((Button)window.FindName("SettingsButton")).ContextMenu.Items[2];
                Program.Check(((MenuItem)((MenuItem)menu.Items[0]).Items[2]).IsChecked);
                Program.Check(((MenuItem)((MenuItem)menu.Items[1]).Items[2]).IsChecked);
            }
            finally
            {
                try { window?.Close(); WpfTestHelpers.Pump(); }
                finally { if (previous == null) File.Delete(path); else File.WriteAllBytes(path, previous); }
            }
        });
        Program.Run("left and right arrows control article progress from every pane and popup", () =>
        {
            var window = CreateWindow();
            string root = Path.GetFullPath(Path.Combine("artifacts", "controls-tests", Guid.NewGuid().ToString("N")));
            try
            {
                Directory.CreateDirectory(root);
                string path = Path.Combine(root, "Reading.json");
                File.Copy(TestData.ArticlePath, path);
                var longest = new ArticleRepository().Load(TestData.ArticlePath).Segments.OrderByDescending(segment => segment.Text.Length).First();
                File.Copy(Path.Combine(Path.GetDirectoryName(TestData.ArticlePath)!, longest.Audio!.Uk), Path.Combine(root, "Reading.mp3"));
                window.Show();
                LoadArticle(window);
                Program.Check(window.ViewModel.OpenArticle(path));
                var player = (WpfAudioPlayer)typeof(ReadArticles.MainWindow).GetField("player", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(window)!;
                player.Volume = 0;
                var pronunciationPlayer = (WpfAudioPlayer)typeof(ReadArticles.MainWindow).GetField("pronunciationPlayer", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(window)!;
                pronunciationPlayer.Volume = 0;
                window.ViewModel.ToggleArticleAudio();
                WpfTestHelpers.Wait(() => window.ViewModel.Playback.CanSeekArticle);
                window.ViewModel.ToggleArticleAudio();
                var slider = (Slider)window.FindName("PlaybackProgress");
                var time = (TextBlock)window.FindName("PlaybackTime");
                Program.Check(time.Visibility == Visibility.Visible);
                UIElement[] targets = { (UIElement)window.FindName("LibraryTree"), window.Readers[0],
                    (UIElement)window.FindName("VocabularyList"), (UIElement)window.FindName("SettingsButton"), slider };
                foreach (var target in targets.Concat(WpfTestHelpers.Descendants(window).OfType<GridSplitter>()).Concat(WpfTestHelpers.Descendants(window).OfType<ScrollBar>()))
                {
                    slider.Value = 1;
                    PressArrow(window, target, Key.Left);
                    Program.Check(Math.Abs(player.Position.TotalSeconds) < 0.05);
                    PressArrow(window, target, Key.Right);
                    Program.Check(Math.Abs(player.Position.TotalSeconds - Math.Min(4.0, player.Duration.TotalSeconds)) < 0.05);
                    PressArrow(window, target, Key.Space);
                    Program.Check(window.ViewModel.Playback.IsArticlePlaying);
                    PressArrow(window, target, Key.Space);
                    Program.Check(window.ViewModel.Playback.IsArticlePaused);
                }
                var settings = ((Button)window.FindName("SettingsButton")).ContextMenu;
                settings.IsOpen = true;
                WpfTestHelpers.Pump(30);
                slider.Value = 1;
                PressArrow(window, (UIElement)settings.Items[0], Key.Left);
                Program.Check(Math.Abs(player.Position.TotalSeconds) < 0.05);
                var font = (MenuItem)settings.Items[0];
                font.IsSubmenuOpen = true;
                WpfTestHelpers.Pump(30);
                PressArrow(window, (UIElement)font.Items[0], Key.Right);
                Program.Check(Math.Abs(player.Position.TotalSeconds - Math.Min(4.0, player.Duration.TotalSeconds)) < 0.05);
                settings.IsOpen = false;
                var vocabulary = (ListBox)window.FindName("VocabularyList");
                Program.Check(vocabulary.ContextMenu == null);
                PressArrow(window, vocabulary, Key.Left);
                Program.Check(Math.Abs(player.Position.TotalSeconds) < 0.05);
                PressArrow(window, targets[0], Key.Space);
                Program.Check(window.ViewModel.PlaybackState == PlaybackState.Playing);
                PressArrow(window, targets[0], Key.Space);
                Program.Check(window.ViewModel.PlaybackState == PlaybackState.Paused);
                var progress = (MenuItem)settings.Items[2];
                var forward = (MenuItem)progress.Items[0];
                var backward = (MenuItem)progress.Items[1];
                double[] forwardSteps = { 4.0, 7.0, 10.0 };
                double[] backwardSteps = { 5.0, 8.0, 15.0 };
                for (int i = 0; i < forwardSteps.Length; i++)
                {
                    ClickStep((MenuItem)forward.Items[i]);
                    slider.Value = 0;
                    PressArrow(window, targets[0], Key.Right);
                    Program.Check(Math.Abs(player.Position.TotalSeconds - Math.Min(forwardSteps[i], player.Duration.TotalSeconds)) < 0.05);
                    ClickStep((MenuItem)backward.Items[i]);
                    double start = Math.Min(20, player.Duration.TotalSeconds - 0.1);
                    slider.Value = start;
                    PressArrow(window, targets[0], Key.Left);
                    Program.Check(Math.Abs(player.Position.TotalSeconds - Math.Max(0, start - backwardSteps[i])) < 0.05);
                }
                window.ViewModel.Stop();
                PressArrow(window, targets[0], Key.Left);
                Program.Check(window.ViewModel.PlaybackState == PlaybackState.Stopped);
                Program.Check(time.Visibility == Visibility.Collapsed);
                PressArrow(window, targets[0], Key.Space);
                WpfTestHelpers.Wait(() => window.ViewModel.Playback.CanSeekArticle);
                Program.Check(window.ViewModel.Playback.IsArticlePlaying);
                PressArrow(window, targets[0], Key.Space);
                Program.Check(window.ViewModel.Playback.IsArticlePaused);
                Program.Check(window.ViewModel.OpenArticle(TestData.ArticlePath));
                window.ViewModel.PlaySegment("s001");
                WpfTestHelpers.Wait(() => pronunciationPlayer.IsOpen);
                pronunciationPlayer.Pause();
                double segmentPosition = pronunciationPlayer.Position.TotalSeconds;
                Program.Check(time.Visibility == Visibility.Collapsed);
                PressArrow(window, targets[0], Key.Right);
                Program.Check(window.ViewModel.Owner == "segment:s001" && Math.Abs(pronunciationPlayer.Position.TotalSeconds - segmentPosition) < 0.05);
                window.ViewModel.PlayWord(window.ViewModel.Words[0].Entry, "uk");
                WpfTestHelpers.Wait(() => pronunciationPlayer.IsOpen);
                pronunciationPlayer.Pause();
                double wordPosition = pronunciationPlayer.Position.TotalSeconds;
                Program.Check(time.Visibility == Visibility.Collapsed);
                PressArrow(window, targets[0], Key.Left);
                Program.Check(window.ViewModel.Owner.StartsWith("word:") && Math.Abs(pronunciationPlayer.Position.TotalSeconds - wordPosition) < 0.05);
                PressArrow(window, targets[0], Key.Space);
                Program.Check(window.ViewModel.Owner.StartsWith("word:") && window.ViewModel.Status.Contains("MP3"));
            }
            finally
            {
                window.Close(); WpfTestHelpers.Pump();
                if (Directory.Exists(root)) Directory.Delete(root, true);
            }
        });
        Program.Run("folder selection discovers names without opening any JSON or content", () =>
        {
            var window = CreateWindow();
            string root = Path.GetFullPath(Path.Combine("artifacts", "controls-tests", Guid.NewGuid().ToString("N")));
            try
            {
                Directory.CreateDirectory(root);
                window.Show();
                Program.Check(window.ViewModel.OpenArticle(TestData.ArticlePath));
                string book = Path.Combine(root, "Book"); Directory.CreateDirectory(book);
                File.WriteAllText(Path.Combine(book, "book.json"), "marker");
                string path = Path.Combine(book, "Locked.json"); File.WriteAllText(path, "not JSON");
                using var locked = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
                Program.Check(window.ViewModel.OpenLibrary(root));
                Program.Check(window.ViewModel.Books.Count == 1 && MainLeaves(window).Single().Name == "Locked");
                Program.Check(window.ViewModel.Article is null && window.ViewModel.Words.Count == 0 && window.Readers.Count == 0);
            }
            finally
            {
                try { window.Close(); WpfTestHelpers.Pump(); }
                finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
            }
        });
        Program.Run("bottom circular settings offers upward font/accent menus and shared sizing", () =>
        {
            var window = CreateWindow();
            try
            {
                Directory.CreateDirectory(Path.GetFullPath("artifacts"));
                window.Show();
                Program.Check(window.ViewModel.OpenLibrary(TestData.LibraryRoot));
                Program.Check(window.SelectPassage(TestData.ArticlePath)); WpfTestHelpers.Pump(30);
                var button = (Button)window.FindName("SettingsButton");
                window.Left = SystemParameters.WorkArea.Left + 40;
                window.Top = SystemParameters.WorkArea.Top + 40;
                Program.Check(button.ContextMenu.Placement == PlacementMode.Top);
                Program.Check(Math.Abs(button.ContextMenu.FontSize - 10.0 * 96 / 72) < 0.001);
                Program.Check(button.ContextMenu.Items.Count == 3 && window.FindName("AccentSelector") is null);
                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Program.Check(button.ContextMenu.IsOpen); WpfTestHelpers.Pump(30);
                var surface = System.Windows.Media.VisualTreeHelper.GetChild(button.ContextMenu, 0) as Border;
                Program.Check(surface?.CornerRadius == new CornerRadius(10));
                var fonts = (MenuItem)button.ContextMenu.Items[0];
                Program.Check(fonts.HorizontalContentAlignment == HorizontalAlignment.Left);
                fonts.IsSubmenuOpen = true; WpfTestHelpers.Pump(30);
                var popup = (Popup)fonts.Template.FindName("PART_Popup", fonts);
                Program.Check(popup.IsOpen && popup.Child is Border { CornerRadius.TopLeft: 10 });
                var origin = surface!.PointToScreen(new Point(0, surface.ActualHeight));
                var buttonEdge = button.PointToScreen(new Point(button.ActualWidth, 0));
                var scale = System.Windows.PresentationSource.FromVisual(button)!.CompositionTarget!.TransformToDevice;
                Console.WriteLine($"  Settings panels: {surface.ActualWidth}, {((Border)popup.Child).ActualWidth}; gaps: {(origin.X - buttonEdge.X) / scale.M11}, {(buttonEdge.Y - origin.Y) / scale.M22}");
                Program.Check(surface.ActualWidth >= 75 && surface.ActualWidth <= 95);
                Program.Check(((Border)popup.Child).ActualWidth >= 35 && ((Border)popup.Child).ActualWidth <= 55);
                Program.Check(Math.Abs((origin.X - buttonEdge.X) / scale.M11 - 7) < 1);
                Program.Check(Math.Abs((buttonEdge.Y - origin.Y) / scale.M22 - 4) < 1);
                foreach (var item in button.ContextMenu.Items.Cast<MenuItem>().Concat(button.ContextMenu.Items.Cast<MenuItem>().SelectMany(item => item.Items.Cast<MenuItem>())))
                    Program.Check(Math.Abs(item.FontSize - 10.0 * 96 / 72) < 0.001);
                var menuImage = new System.Windows.Media.Imaging.RenderTargetBitmap((int)button.ContextMenu.ActualWidth, (int)button.ContextMenu.ActualHeight, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
                menuImage.Render(button.ContextMenu);
                var menuEncoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
                menuEncoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(menuImage));
                using (var menuFile = File.Create(Path.GetFullPath("artifacts/settings-menu.png"))) menuEncoder.Save(menuFile);
                var submenu = (Border)popup.Child;
                var submenuImage = new System.Windows.Media.Imaging.RenderTargetBitmap((int)(submenu.ActualWidth + submenu.Margin.Left + submenu.Margin.Right), (int)(submenu.ActualHeight + submenu.Margin.Top + submenu.Margin.Bottom), 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
                submenuImage.Render(submenu);
                var submenuEncoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
                submenuEncoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(submenuImage));
                using (var submenuFile = File.Create(Path.GetFullPath("artifacts/settings-submenu.png"))) submenuEncoder.Save(submenuFile);
                fonts.IsSubmenuOpen = false; button.ContextMenu.IsOpen = false;
                window.Left = -10000; window.Top = -10000;
                foreach (int points in new[] { 14, 15, 16 })
                {
                    var option = (MenuItem)((MenuItem)button.ContextMenu.Items[0]).Items[points - 14];
                    option.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); WpfTestHelpers.Pump(30);
                    double expected = points;
                    Program.Check(window.Readers.All(r => Math.Abs(r.FontSize - expected) < 0.001));
                    Program.Check(Math.Abs(((ListBox)window.FindName("VocabularyList")).FontSize - expected) < 0.001);
                    CheckIndentedParagraphs(window);
                    CheckSettingsSelection(button.ContextMenu);
                }
                var accents = (MenuItem)button.ContextMenu.Items[1];
                ((MenuItem)accents.Items[1]).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                Program.Check(window.ViewModel.Accent == "us");
                CheckSettingsSelection(button.ContextMenu);
                ((MenuItem)accents.Items[0]).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                Program.Check(window.ViewModel.Accent == "uk");
                CheckSettingsSelection(button.ContextMenu);
            }
            finally
            {
                window.Close(); WpfTestHelpers.Pump();
            }
        });
        Program.Run("segment and vocabulary playback never show or respond to bottom progress", () =>
        {
            var window = CreateWindow();
            try
            {
                Directory.CreateDirectory(Path.GetFullPath("artifacts"));
                window.Show();
                LoadArticle(window);
                var player = (WpfAudioPlayer)typeof(ReadArticles.MainWindow).GetField("player", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(window)!;
                player.Volume = 0;
                var pronunciationPlayer = (WpfAudioPlayer)typeof(ReadArticles.MainWindow).GetField("pronunciationPlayer", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(window)!;
                pronunciationPlayer.Volume = 0;
                var slider = (Slider)window.FindName("PlaybackProgress");
                var settingsButton = (Button)window.FindName("SettingsButton");
                ((MenuItem)((MenuItem)settingsButton.ContextMenu.Items[0]).Items[0]).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                var reader = window.Readers[0];
                Program.Check(reader.RequestSegmentPlayback(reader.PositionAt(reader.Ranges[0], 0)));
                WpfTestHelpers.Wait(() => pronunciationPlayer.IsOpen && pronunciationPlayer.Duration.TotalSeconds > 1);
                Program.Check(slider.Visibility == Visibility.Collapsed && !slider.IsEnabled && slider.Value == 0);
                var stopButton = (Button)window.FindName("StopButton");
                Program.Check(!stopButton.IsEnabled);
                stopButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Program.Check(window.ViewModel.PlaybackState == PlaybackState.Playing && window.ViewModel.Owner.StartsWith("segment:"));
                pronunciationPlayer.Pause();
                double segmentPosition = pronunciationPlayer.Position.TotalSeconds;
                slider.RaiseEvent(new DragStartedEventArgs(0, 0) { RoutedEvent = Thumb.DragStartedEvent });
                slider.RaiseEvent(new RoutedPropertyChangedEventArgs<double>(0, pronunciationPlayer.Duration.TotalSeconds / 2, Slider.ValueChangedEvent));
                WpfTestHelpers.Pump(40);
                slider.RaiseEvent(new DragCompletedEventArgs(0, 0, false) { RoutedEvent = Thumb.DragCompletedEvent });
                Program.Check(Math.Abs(pronunciationPlayer.Position.TotalSeconds - segmentPosition) < 0.2);
                window.UpdateLayout();
                var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
                bitmap.Render(window);
                var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
                encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
                using (var image = File.Create(Path.GetFullPath("artifacts/reader-controls.png"))) encoder.Save(image);
                window.ViewModel.Stop();
                Program.Check(slider.Visibility == Visibility.Collapsed && slider.Value == 0);
                window.ViewModel.PlayWord(window.ViewModel.Words[0].Entry, "uk");
                WpfTestHelpers.Wait(() => pronunciationPlayer.IsOpen);
                Program.Check(slider.Visibility == Visibility.Collapsed && !slider.IsEnabled && slider.Value == 0);
                Program.Check(!stopButton.IsEnabled);
                stopButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Program.Check(window.ViewModel.PlaybackState == PlaybackState.Playing && window.ViewModel.Owner.StartsWith("word:"));
                pronunciationPlayer.Pause();
                double wordPosition = pronunciationPlayer.Position.TotalSeconds;
                slider.RaiseEvent(new RoutedPropertyChangedEventArgs<double>(0, pronunciationPlayer.Duration.TotalSeconds / 2, Slider.ValueChangedEvent));
                WpfTestHelpers.Pump(40);
                Program.Check(Math.Abs(pronunciationPlayer.Position.TotalSeconds - wordPosition) < 0.2);
                window.ViewModel.Stop();
            }
            finally
            {
                window.Close(); WpfTestHelpers.Pump();
            }
        });
        Program.Run("bottom play and stop control only the first sibling MP3", () =>
        {
            var window = CreateWindow();
            string root = Path.GetFullPath(Path.Combine("artifacts", "controls-tests", Guid.NewGuid().ToString("N")));
            try
            {
                Directory.CreateDirectory(root);
                window.Show();
                LoadArticle(window);
                var fixtureWord = window.ViewModel.Words[0].Entry;
                string path = Path.Combine(root, "Short.json");
                File.Copy(TestData.ArticlePath, path);
                string audioRoot = Path.GetDirectoryName(TestData.ArticlePath)!;
                string sourceMp3 = Path.Combine(audioRoot, "audio_segments", "s001_uk.mp3");
                // Create the later name first so enumeration order cannot choose the file.
                File.Copy(sourceMp3, Path.Combine(root, "Z-last.mp3"));
                File.Copy(sourceMp3, Path.Combine(root, "a-first.MP3"));
                Directory.CreateDirectory(Path.Combine(root, "audio_segments"));
                File.Copy(sourceMp3, Path.Combine(root, "audio_segments", "s001_uk.mp3"));
                string wordPath = Path.Combine(root, fixtureWord.Audio.Uk);
                Directory.CreateDirectory(Path.GetDirectoryName(wordPath)!);
                File.Copy(Path.Combine(audioRoot, fixtureWord.Audio.Uk), wordPath);
                string nested = Path.Combine(root, "nested"); Directory.CreateDirectory(nested);
                File.Copy(sourceMp3, Path.Combine(nested, "00-ignored.mp3"));
                File.WriteAllText(Path.Combine(root, "00-ignored.wav"), "ignored");
                if (!window.ViewModel.OpenArticle(path)) throw new Exception(window.ViewModel.Status);
                var playButton = (Button)window.FindName("ReadAllButton");
                var slider = (Slider)window.FindName("PlaybackProgress");
                var player = (WpfAudioPlayer)typeof(ReadArticles.MainWindow).GetField("player", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(window)!;
                player.Volume = 0;
                var pronunciationPlayer = (WpfAudioPlayer)typeof(ReadArticles.MainWindow).GetField("pronunciationPlayer", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(window)!;
                pronunciationPlayer.Volume = 0;
                window.ViewModel.PlayWord(fixtureWord, "uk");
                WpfTestHelpers.Wait(() => pronunciationPlayer.IsOpen);
                playButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                WpfTestHelpers.Wait(() => slider.IsEnabled);
                Program.Check(!pronunciationPlayer.IsOpen);
                Program.Check(window.ViewModel.Owner == "passage" && window.ViewModel.PlayingSid is null);
                var time = (TextBlock)window.FindName("PlaybackTime");
                Program.Check(time.Visibility == Visibility.Visible && time.Text.Contains('/'));
                Program.Check(time.Text.EndsWith("/" + player.Duration.ToString(@"mm\:ss")));
                Program.Check(Grid.GetColumn(slider) < Grid.GetColumn(time) && Grid.GetColumn(time) < Grid.GetColumn(playButton));
                var media = (System.Windows.Media.MediaPlayer)typeof(WpfAudioPlayer).GetField("media", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(player)!;
                Program.Check(media.Source.LocalPath == Path.Combine(root, "a-first.MP3"));
                slider.Value = 1;
                window.ViewModel.PlaySegment("s001");
                WpfTestHelpers.Wait(() => pronunciationPlayer.IsOpen);
                Program.Check(window.ViewModel.Playback.IsArticlePaused && Math.Abs(player.Position.TotalSeconds - 1) < 0.2);
                Program.Check(slider.Visibility == Visibility.Visible && time.Visibility == Visibility.Visible);
                pronunciationPlayer.Pause();
                double sentencePosition = pronunciationPlayer.Position.TotalSeconds;
                slider.Value = 2;
                Program.Check(Math.Abs(pronunciationPlayer.Position.TotalSeconds - sentencePosition) < 0.1);
                playButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Program.Check(window.ViewModel.Playback.IsArticlePlaying && !pronunciationPlayer.IsOpen);

                Program.Check(WpfTestHelpers.IsIcon(((Image)playButton.Content).Source, UiIcons.Pause));
                playButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Program.Check(window.ViewModel.PlaybackState == PlaybackState.Paused);
                Program.Check(((Button)window.FindName("StopButton")).IsEnabled);
                ((Button)window.FindName("StopButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Program.Check(window.ViewModel.PlaybackState == PlaybackState.Stopped && slider.Visibility == Visibility.Collapsed);
                playButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                WpfTestHelpers.Wait(() => slider.IsEnabled);
                Program.Check(!pronunciationPlayer.IsOpen);
                playButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                slider.Value = slider.Maximum / 2; WpfTestHelpers.Pump(100);
                Program.Check(window.ViewModel.PlaybackState == PlaybackState.Paused && Math.Abs(player.Position.TotalSeconds - slider.Maximum / 2) < 0.2);
                slider.Value = 0;
                Program.Check(time.Text.StartsWith("00:00/"));
                slider.Value = 1;
                Program.Check(time.Text.StartsWith("00:01/") && time.Visibility == Visibility.Visible);
                Program.Check(slider.Focusable && slider.FocusVisualStyle == null);
                double position = slider.Value;
                Slider.DecreaseSmall.Execute(null, slider);
                Program.Check(Math.Abs(slider.Value - (position - 0.5)) < 0.001);
                WpfTestHelpers.Pump(40);
                Program.Check(Math.Abs(player.Position.TotalSeconds - slider.Value) < 0.2);
                Slider.IncreaseSmall.Execute(null, slider);
                Program.Check(Math.Abs(slider.Value - position) < 0.001);
                slider.Value = 0.1;
                Slider.DecreaseSmall.Execute(null, slider);
                Program.Check(slider.Value == 0);
                slider.Value = position;
                window.ViewModel.Accent = "us";
                Program.Check(window.ViewModel.PlaybackState == PlaybackState.Paused);
                playButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Program.Check(window.ViewModel.PlaybackState == PlaybackState.Playing && media.Source.LocalPath == Path.Combine(root, "a-first.MP3"));
                ((Button)window.FindName("StopButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Program.Check(window.ViewModel.PlaybackState == PlaybackState.Stopped && slider.Visibility == Visibility.Collapsed);
                playButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                WpfTestHelpers.Wait(() => slider.IsEnabled);
                Program.Check(!pronunciationPlayer.IsOpen);
                slider.RaiseEvent(new DragStartedEventArgs(0, 0) { RoutedEvent = Thumb.DragStartedEvent });
                window.ViewModel.PlayWord(fixtureWord, "uk");
                WpfTestHelpers.Wait(() => pronunciationPlayer.IsOpen);
                Program.Check(slider.Visibility == Visibility.Visible && slider.IsEnabled && window.ViewModel.Playback.IsArticlePaused);
                double retainedPosition = player.Position.TotalSeconds;
                Program.Check(pronunciationPlayer.IsOpen && !ReferenceEquals(player, pronunciationPlayer));
                Program.Check(time.Visibility == Visibility.Visible);
                slider.RaiseEvent(new DragCompletedEventArgs(0, 0, false) { RoutedEvent = Thumb.DragCompletedEvent });
                playButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                WpfTestHelpers.Wait(() => window.ViewModel.Playback.IsArticlePlaying);
                Program.Check(!pronunciationPlayer.IsOpen && Math.Abs(player.Position.TotalSeconds - retainedPosition) < 0.3);
                slider.Value = slider.Maximum - 0.1;
                WpfTestHelpers.Wait(() => window.ViewModel.PlaybackState == PlaybackState.Stopped);
                Program.Check(window.ViewModel.PlayingSid is null && slider.Visibility == Visibility.Collapsed);
                // A playing word is not a pause target for the bottom button.
                Program.Check(window.ViewModel.OpenArticle(TestData.ArticlePath));
                playButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Program.Check(window.ViewModel.PlaybackState == PlaybackState.Stopped &&
                    window.ViewModel.Status.Contains("同级目录") && !player.IsOpen);
                window.ViewModel.PlayWord(window.ViewModel.Words[0].Entry, "uk");
                WpfTestHelpers.Wait(() => pronunciationPlayer.IsOpen);
                playButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Program.Check(window.ViewModel.PlaybackState == PlaybackState.Playing && window.ViewModel.Owner.StartsWith("word:") && window.ViewModel.Status.Contains("MP3"));
                window.ViewModel.PlayWord(window.ViewModel.Words[0].Entry, "uk");
                WpfTestHelpers.Wait(() => pronunciationPlayer.IsOpen);
                ((Button)window.FindName("StopButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Program.Check(window.ViewModel.PlaybackState == PlaybackState.Playing && window.ViewModel.Owner.StartsWith("word:") && slider.Visibility == Visibility.Collapsed);
            }
            finally
            {
                try { window.Close(); WpfTestHelpers.Pump(); }
                finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
            }
        });
    }
    private static ReadArticles.MainWindow CreateWindow() => new ReadArticles.MainWindow(false)
    {
        ShowActivated = false, Left = -10000, Top = -10000
    };
    private static void PressArrow(ReadArticles.MainWindow window, UIElement target, Key key)
    {
        var source = PresentationSource.FromVisual(target) ?? PresentationSource.FromVisual(window)!;
        var args = new KeyEventArgs(Keyboard.PrimaryDevice, source, 0, key) { RoutedEvent = Keyboard.PreviewKeyDownEvent };
        target.RaiseEvent(args);
        Program.Check(args.Handled);
    }
    private static void CheckSettingsSelection(ItemsControl menu)
    {
        foreach (MenuItem item in menu.Items)
        {
            item.ApplyTemplate();
            Program.Check(item.Template.FindName("Check", item) == null);
            var surface = (Border)item.Template.FindName("ItemSurface", item);
            var color = ((System.Windows.Media.SolidColorBrush)surface.Background).Color;
            if (item.IsCheckable)
                Program.Check(color == (item.IsChecked
                    ? System.Windows.Media.Color.FromRgb(135, 192, 202)
                    : System.Windows.Media.Color.FromRgb(252, 252, 253)));
            CheckSettingsSelection(item);
        }
    }

    private static void ClickStep(MenuItem item)
    {
        typeof(MenuItem).GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(item, null);
        WpfTestHelpers.Pump(30);
    }
    private static void LoadArticle(ReadArticles.MainWindow window)
    {
        Program.Check(window.ViewModel.OpenLibrary(TestData.LibraryRoot));
        Program.Check(window.SelectPassage(TestData.ArticlePath));
        WpfTestHelpers.Pump(30);
    }
    private static IEnumerable<ReadArticles.Models.NavigationNode> MainLeaves(ReadArticles.MainWindow window) => ReadArticles.ViewModels.MainViewModel.AllLeaves(window.ViewModel.Books);
    private static void CheckIndentedParagraphs(ReadArticles.MainWindow window)
    {
        var host = (StackPanel)window.FindName("ParagraphHost");
        Program.Check(host.Children.Cast<object>().All(c => c is ReadArticles.Rendering.ParagraphReader));
        Program.Check(!WpfTestHelpers.Descendants(window).OfType<Button>().Any(b => Equals(b.Content, "导出")));
        foreach (var reader in window.Readers)
        {
            var paragraph = (System.Windows.Documents.Paragraph)reader.Document.Blocks.FirstBlock!;
            Program.Check(Math.Abs(paragraph.TextIndent - reader.FontSize * 2) < 0.001);
            reader.SetVocabulary(["new test term"], true);
            paragraph = (System.Windows.Documents.Paragraph)reader.Document.Blocks.FirstBlock!;
            Program.Check(Math.Abs(paragraph.TextIndent - reader.FontSize * 2) < 0.001);
        }
    }
}
