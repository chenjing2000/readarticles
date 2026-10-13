using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ReadArticles.Models;
using System.IO;

internal static class IconChecks
{
    internal static void Run()
    {
        Program.Run("selection add uses a square vector icon and keeps add behavior", () =>
        {
            var reader = new ReadArticles.Rendering.ParagraphReader([new Segment("s001", "selected word", true)], []);
            var host = new Window { Content = reader, Width = 360, Height = 150, Left = -10000, Top = -10000, ShowActivated = false };
            try
            {
                host.Show(); WpfTestHelpers.Pump();
                var range = reader.Ranges[0];
                reader.Selection.Select(reader.PositionAt(range, 0), reader.PositionAt(range, 8));
                WpfTestHelpers.Pump();
                var adorner = System.Windows.Documents.AdornerLayer.GetAdornerLayer(reader)!.GetAdorners(reader)!.Single();
                var button = (Button)VisualTreeHelper.GetChild(adorner, 0);
                Program.Check(reader.IsSelectionAddVisible && button.Content is Image);
                var icon = (Image)button.Content;
                Program.Check(icon.Source is DrawingImage);
                Program.Check(icon.ActualWidth == 14 && icon.ActualHeight == 14);
                Program.Check(button.ActualWidth == 14 && button.ActualHeight == 14);
                foreach (int points in new[] { 14, 15, 16 })
                {
                    reader.FontSize = points;
                    WpfTestHelpers.Pump();
                    var rectangle = reader.Selection.End.GetCharacterRect(System.Windows.Documents.LogicalDirection.Backward);
                    Program.Check((button.PointToScreen(new Point(0, 0)) - reader.PointToScreen(rectangle.BottomRight)).Length < 2);
                }
                var buttonCenter = button.TranslatePoint(new Point(7, 7), host);
                DependencyObject? hit = host.InputHitTest(buttonCenter) as DependencyObject;
                while (hit != null && hit != button) hit = VisualTreeHelper.GetParent(hit);
                Program.Check(hit == button);
                reader.RaiseEvent(new System.Windows.Input.MouseButtonEventArgs(System.Windows.Input.Mouse.PrimaryDevice, 0, System.Windows.Input.MouseButton.Left) { RoutedEvent = System.Windows.Input.Mouse.PreviewMouseUpEvent });
                reader.Selection.Select(range.Start, range.Start);
                Program.Check(!reader.IsSelectionAddVisible);
                WpfTestHelpers.Pump(); Program.Check(!reader.IsSelectionAddVisible);
                reader.Selection.Select(reader.PositionAt(range, 0), reader.PositionAt(range, 8)); WpfTestHelpers.Pump();
                reader.RaiseEvent(new System.Windows.Input.KeyboardFocusChangedEventArgs(System.Windows.Input.Keyboard.PrimaryDevice, 0, reader, button) { RoutedEvent = System.Windows.Input.Keyboard.LostKeyboardFocusEvent });
                Program.Check(!reader.IsSelectionAddVisible);
                reader.Selection.Select(range.Start, range.Start);
                reader.Selection.Select(reader.PositionAt(range, 0), reader.PositionAt(range, 8)); WpfTestHelpers.Pump();
                PassageSelection? appended = null; reader.SelectionRequested += selection => appended = selection;
                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Program.Check(appended?.Text == "selected" && !reader.IsSelectionAddVisible && reader.Selection.IsEmpty);
            }
            finally { host.Close(); WpfTestHelpers.Pump(); }
        });
        Program.Run("plain plus follows window movement, scrolling and viewport clipping", () =>
        {
            var reader = new ReadArticles.Rendering.ParagraphReader([new Segment("s001", "selected word " + string.Concat(Enumerable.Repeat("more words ", 100)), true)], [])
                { Margin = new Thickness(150, 160, 20, 20) };
            var content = new Grid { Width = 1000, Height = 1400 }; content.Children.Add(reader);
            var scroll = new ScrollViewer { Content = content, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            var host = new Window { Content = scroll, Width = 400, Height = 250, Left = SystemParameters.WorkArea.Left + 80, Top = SystemParameters.WorkArea.Top + 80, ShowActivated = false };
            try
            {
                host.Show(); WpfTestHelpers.Pump(); scroll.ScrollToVerticalOffset(100); WpfTestHelpers.Pump();
                var range = reader.Ranges[0]; reader.Selection.Select(reader.PositionAt(range, 0), reader.PositionAt(range, 8)); WpfTestHelpers.Pump();
                var adorner = System.Windows.Documents.AdornerLayer.GetAdornerLayer(reader)!.GetAdorners(reader)!.Single();
                var button = (Button)VisualTreeHelper.GetChild(adorner, 0);
                Program.Check(reader.IsSelectionAddVisible);
                CheckScreenCorner();
                foreach (var shift in new[] { new Point(25, 20), new Point(-35, -10), new Point(20, -15), new Point(-10, 5) })
                {
                    var before = button.PointToScreen(new Point(0, 0));
                    host.Left += shift.X; host.Top += shift.Y; WpfTestHelpers.Pump();
                    CheckScreenCorner();
                    var screenShift = PresentationSource.FromVisual(host)!.CompositionTarget!.TransformToDevice.Transform((Vector)shift);
                    Program.Check((button.PointToScreen(new Point(0, 0)) - before - screenShift).Length < 2);
                }
                var original = button.PointToScreen(new Point(0, 0));
                var screenScale = PresentationSource.FromVisual(host)!.CompositionTarget!.TransformToDevice;
                scroll.ScrollToVerticalOffset(140); WpfTestHelpers.Pump();
                Program.Check(reader.IsSelectionAddVisible && Math.Abs(button.PointToScreen(new Point(0, 0)).Y - (original.Y - 40 * screenScale.M22)) < 1);
                CheckScreenCorner();
                scroll.ScrollToHorizontalOffset(40); WpfTestHelpers.Pump();
                Program.Check(reader.IsSelectionAddVisible && Math.Abs(button.PointToScreen(new Point(0, 0)).X - (original.X - 40 * screenScale.M11)) < 1);
                CheckScreenCorner();
                reader.Margin = new Thickness(170, 160, 20, 20); WpfTestHelpers.Pump(); CheckScreenCorner();
                scroll.ScrollToVerticalOffset(400); WpfTestHelpers.Pump(); Program.Check(!reader.IsSelectionAddVisible);
                scroll.ScrollToVerticalOffset(100); WpfTestHelpers.Pump(); Program.Check(reader.IsSelectionAddVisible);
                reader.Selection.Select(range.Start, range.Start); Program.Check(!reader.IsSelectionAddVisible);
                scroll.ScrollToHorizontalOffset(0); WpfTestHelpers.Pump(); Program.Check(!reader.IsSelectionAddVisible);
                void CheckScreenCorner()
                {
                    var rectangle = reader.Selection.End.GetCharacterRect(System.Windows.Documents.LogicalDirection.Backward);
                    var expected = reader.PointToScreen(rectangle.BottomRight);
                    var actual = button.PointToScreen(new Point(0, 0));
                    if ((actual - expected).Length > 2) throw new Exception($"Add button corner {actual}, selected corner {expected}");
                }
            }
            finally { host.Close(); WpfTestHelpers.Pump(); }
        });
        Program.Run("startup window is 70 percent of work area and centered", () =>
        {
            var initial = new ReadArticles.MainWindow(false) { ShowActivated = false };
            try
            {
                var area = SystemParameters.WorkArea;
                Program.Check(Math.Abs(initial.Width - Math.Round(Math.Round(area.Width) * 0.7)) < 0.01 && Math.Abs(initial.Height - Math.Round(Math.Round(area.Height) * 0.7)) < 0.01);
                Program.Check(initial.Left == Math.Round(Math.Round(area.Left) + (Math.Round(area.Width) - initial.Width) / 2));
                Program.Check(initial.Top == Math.Round(Math.Round(area.Top) + (Math.Round(area.Height) - initial.Height) / 2));
                foreach (var name in new[] { "LeftColumn", "CenterColumn", "RightColumn" })
                {
                    var column = (ColumnDefinition)initial.FindName(name);
                    Program.Check(column.MinWidth == Math.Round(column.MinWidth));
                    if (!column.Width.IsStar) Program.Check(column.Width.Value == Math.Round(column.Width.Value));
                }
                initial.Show(); WpfTestHelpers.Pump(); initial.UpdateLayout();
                Program.Check(Math.Abs(initial.ActualWidth - Math.Round(Math.Round(area.Width) * 0.7)) < 1 && Math.Abs(initial.ActualHeight - Math.Round(Math.Round(area.Height) * 0.7)) < 1);
                var lastColumn = (ColumnDefinition)initial.FindName("RightColumn");
                Program.Check(lastColumn.Offset + lastColumn.ActualWidth <= initial.ActualWidth);
            }
            finally { initial.Close(); WpfTestHelpers.Pump(); }
        });
        Program.Run("startup footer stays visible with disabled controls and aligned columns", () =>
        {
            var window = CreateWindow();
            try
            {
                window.Show();
                Directory.CreateDirectory(Path.GetFullPath("artifacts"));
                WpfTestHelpers.Pump(); window.UpdateLayout();
                var controls = (Grid)window.FindName("PassageControls");
                var play = (Button)window.FindName("ReadAllButton");
                var stop = (Button)window.FindName("StopButton");
                var highlight = (Button)window.FindName("HighlightButton");
                Program.Check(controls.IsVisible && play.IsVisible && stop.IsVisible);
                Program.Check(!play.IsEnabled && !stop.IsEnabled && !highlight.IsEnabled);
                double center = play.TranslatePoint(new Point(0, play.ActualHeight / 2), window).Y;
                foreach (string name in new[] { "SettingsButton", "StopButton", "ImportButton", "HighlightButton" })
                {
                    var button = (Button)window.FindName(name);
                    Program.Check(Math.Abs(button.TranslatePoint(new Point(0, button.ActualHeight / 2), window).Y - center) < 1);
                }
                double bottom = ((ScrollViewer)window.FindName("PassageScroll")).TranslatePoint(new Point(0, ((ScrollViewer)window.FindName("PassageScroll")).ActualHeight), window).Y;
                foreach (string name in new[] { "LibraryTree", "VocabularyList" })
                {
                    var content = (FrameworkElement)window.FindName(name);
                    Program.Check(Math.Abs(content.TranslatePoint(new Point(0, content.ActualHeight), window).Y - bottom) < 1);
                }
                var toolbar = (Border)window.FindName("PassageToolbar");
                Program.Check(((SolidColorBrush)toolbar.Background).Color == ((SolidColorBrush)window.Background).Color);
                var snapshot = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32);
                snapshot.Render(window);
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(new CroppedBitmap(snapshot, new Int32Rect(0, snapshot.PixelHeight - 130, snapshot.PixelWidth, 130))));
                using (var screenshot = File.Create(Path.GetFullPath("artifacts/reader-startup-footer.png"))) encoder.Save(screenshot);
            }
            finally { window.Close(); WpfTestHelpers.Pump(); }
        });
        Program.Run("asset-backed window, playback and import icons load from resources", () =>
        {
            var window = CreateWindow();
            try
            {
                window.Show();
                Program.Check(window.Icon is BitmapSource);
                Program.Check(new ImageSource[] { ReadArticles.Infrastructure.UiIcons.Play, ReadArticles.Infrastructure.UiIcons.Pause,
                    ReadArticles.Infrastructure.UiIcons.Stop, ReadArticles.Infrastructure.UiIcons.Import,
                    ReadArticles.Infrastructure.UiIcons.HighlightOn, ReadArticles.Infrastructure.UiIcons.HighlightOff }.All(icon => icon is DrawingImage));
                Program.Check(WpfTestHelpers.IsIcon(((Image)((Button)window.FindName("ReadAllButton")).Content).Source, ReadArticles.Infrastructure.UiIcons.DisabledPlay));
                Program.Check(WpfTestHelpers.IsIcon(((Image)((Button)window.FindName("StopButton")).Content).Source, ReadArticles.Infrastructure.UiIcons.DisabledStop));
                Program.Check(WpfTestHelpers.IsIcon(((Image)((Button)window.FindName("ImportButton")).Content).Source, ReadArticles.Infrastructure.UiIcons.Import));
                Program.Check(window.ViewModel.OpenLibrary(TestData.LibraryRoot));
                Program.Check(window.SelectPassage(TestData.ArticlePath));
                WpfTestHelpers.Pump(); window.UpdateLayout();
                foreach (string name in new[] { "ReadAllButton", "StopButton", "ImportButton" })
                {
                    var image = (Image)((Button)window.FindName(name)).Content;
                    if (Math.Abs(image.ActualWidth - 26) >= 0.01 || Math.Abs(image.ActualHeight - 26) >= 0.01)
                        throw new Exception($"{name} icon measures {image.ActualWidth} x {image.ActualHeight}, expected 26 x 26");
                }
                var highlight = (Image)((Button)window.FindName("HighlightButton")).Content;
                if (Math.Abs(highlight.ActualWidth - 24) >= 0.01 || Math.Abs(highlight.ActualHeight - 24) >= 0.01)
                    throw new Exception($"Highlight icon measures {highlight.ActualWidth} x {highlight.ActualHeight}, expected 24 x 24");
            }
            finally { window.Close(); WpfTestHelpers.Pump(); }
        });
        Program.Run("startup restores only collapsed library, ignoring saved passage and highlights", () =>
        {
            string path = ReadArticles.Infrastructure.ReaderSettings.PathName;
            byte[]? previous = File.Exists(path) ? File.ReadAllBytes(path) : null;
            ReadArticles.MainWindow? session = null;
            try
            {
                ReadArticles.Infrastructure.JsonFiles.WriteAtomic(ReadArticles.Infrastructure.ReaderSettings.PathName,
                    new { HighlightsVisible = true, Library = TestData.LibraryRoot, Passage = TestData.ArticlePath });
                session = new ReadArticles.MainWindow { Left = -10000, Top = -10000, ShowActivated = false };
                session.Show();
                WpfTestHelpers.Pump();
                session.RestoreContent();
                Program.Check(session.ViewModel.Books.Count > 0 && session.ViewModel.Books.All(book => !book.IsExpanded));
                Program.Check(session.ViewModel.Article is null && session.ViewModel.Words.Count == 0 && session.Readers.Count == 0);
                Program.Check(!session.ViewModel.HighlightsVisible);
                Program.Check(WpfTestHelpers.IsIcon(((Image)((Button)session.FindName("HighlightButton")).Content).Source, ReadArticles.Infrastructure.UiIcons.DisabledHighlightOn));
            }
            finally
            {
                try { session?.Close(); }
                finally { if (previous is null) File.Delete(path); else File.WriteAllBytes(path, previous); }
            }
        });
        Program.Run("highlight icon toggles off-on-off with matching passage backgrounds", () =>
        {
            var window = CreateWindow();
            try
            {
                window.Show();
                Program.Check(window.ViewModel.OpenLibrary(TestData.LibraryRoot));
                Program.Check(window.SelectPassage(TestData.ArticlePath));
                WpfTestHelpers.Pump(); window.UpdateLayout();
                var toggle = (Button)window.FindName("HighlightButton");
                var icon = (Image)toggle.Content;
                Program.Check(!window.ViewModel.HighlightsVisible && WpfTestHelpers.IsIcon(icon.Source, ReadArticles.Infrastructure.UiIcons.HighlightOn));
                var matcher = new ReadArticles.Services.VocabularyMatcher();
                var reader = window.Readers.First(r => r.Ranges.Any(range => matcher.Match(range.Segment.Text, window.ViewModel.Words.Select(w => w.Word)).Count > 0));
                var range = reader.Ranges.First(range => matcher.Match(range.Segment.Text, window.ViewModel.Words.Select(w => w.Word)).Count > 0);
                var hit = matcher.Match(range.Segment.Text, window.ViewModel.Words.Select(w => w.Word))[0];
                Program.Check(Equals(reader.GetTextBackground(range.Segment.Sid, hit.Start), Brushes.Transparent));
                toggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Program.Check(window.ViewModel.HighlightsVisible && WpfTestHelpers.IsIcon(icon.Source, ReadArticles.Infrastructure.UiIcons.HighlightOff));
                Program.Check(Equals(reader.GetTextBackground(range.Segment.Sid, hit.Start), window.FindResource("VocabularyHighlightBrush")));
                reader.FocusVocabulary(hit.Key);
                toggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Program.Check(!window.ViewModel.HighlightsVisible && WpfTestHelpers.IsIcon(icon.Source, ReadArticles.Infrastructure.UiIcons.HighlightOn));
                Program.Check(Equals(reader.GetTextBackground(range.Segment.Sid, hit.Start), Brushes.Transparent));
            }
            finally { window.Close(); WpfTestHelpers.Pump(); }
        });
        Program.Run("plus/minus SVG expanders retain recursive collapse and leaf behavior", () =>
        {
            var window = CreateWindow();
            try
            {
                window.Show();
                Program.Check(window.ViewModel.OpenLibrary(TestData.LibraryRoot));
                WpfTestHelpers.Pump(); window.UpdateLayout();
                var tree = (TreeView)window.FindName("LibraryTree");
                tree.UpdateLayout();
                var book = (TreeViewItem)tree.ItemContainerGenerator.ContainerFromIndex(0);
                var button = (ToggleButton)book.Template.FindName("Expander", book);
                Program.Check(!book.IsExpanded);
                var icon = WpfTestHelpers.Descendants(button).OfType<Image>().Single();
                var plus = icon.Source;
                Program.Check(plus is DrawingImage);
                button.IsChecked = true; window.UpdateLayout();
                Program.Check(book.IsExpanded && icon.Source is DrawingImage && !ReferenceEquals(icon.Source, plus));
                var date = (TreeViewItem)book.ItemContainerGenerator.ContainerFromIndex(0);
                Program.Check(!date.IsExpanded);
                date.IsExpanded = true; window.UpdateLayout();
                var folder = (TreeViewItem)date.ItemContainerGenerator.ContainerFromIndex(0);
                folder.IsExpanded = true; window.UpdateLayout();
                var leaf = (TreeViewItem)folder.ItemContainerGenerator.ContainerFromIndex(0);
                Program.Check(((ToggleButton)leaf.Template.FindName("Expander", leaf)).Visibility == Visibility.Hidden);
                button.IsChecked = false; window.UpdateLayout();
                Program.Check(!book.IsExpanded && !date.IsExpanded && !folder.IsExpanded && ReferenceEquals(icon.Source, plus));
            }
            finally { window.Close(); WpfTestHelpers.Pump(); }
        });
    }
    private static ReadArticles.MainWindow CreateWindow() => new ReadArticles.MainWindow(false)
    {
        Left = -10000, Top = -10000, ShowActivated = false
    };
}
