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
        Program.Run("three-pane WPF constraints match requested widths", () =>
        {
            var window = new ReadArticles.MainWindow(false);
            try
            {
                var grid = (Grid)window.Content;
                var left = (ColumnDefinition)window.FindName("LeftColumn");
                var center = (ColumnDefinition)window.FindName("CenterColumn");
                Program.Check(left.MinWidth == 200 && left.MaxWidth == 400 &&
                    left.Width.Value >= 200 && left.Width.Value <= 250 &&
                    center.MinWidth == 403 && grid.ColumnDefinitions[1].Width.Value == 3 &&
                    grid.Children.OfType<Thumb>().Single().HorizontalAlignment ==
                    HorizontalAlignment.Stretch);
                if (window.Width >= 250 + 3 + 403 + 40)
                    Program.Check(left.Width.Value == 250);
            }
            finally { window.Close(); }
        });

        Program.Run("both splitters resize center first and then the opposite side", () =>
        {
            var window = new ReadArticles.MainWindow(false);
            try
            {
                window.Show();
                var grid = (Grid)window.Content;
                var left = (ColumnDefinition)window.FindName("LeftColumn");
                var center = (ColumnDefinition)window.FindName("CenterColumn");
                var settings = (ReaderSettings)typeof(ReadArticles.MainWindow)
                    .GetField("settings", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .GetValue(window)!;

                window.Width = Math.Min(window.Width, Math.Max(window.MinWidth, 820));
                WpfTestHelpers.Pump();
                var type = typeof(ReadArticles.MainWindow);
                var dragLeft = type.GetMethod("ResizeLibraryPane",
                    BindingFlags.Instance | BindingFlags.NonPublic)!;
                var dragRight = type.GetMethod("ResizeVocabularyPane",
                    BindingFlags.Instance | BindingFlags.NonPublic)!;
                double available = Math.Floor(Math.Max(606, grid.ActualWidth) - 6);
                double initialRight = settings.RightWidth;
                dragLeft.Invoke(window, [400d]);
                double expectedLeft = Math.Min(400, available - 400);
                double expectedRight = Math.Min(initialRight, available - expectedLeft - 200);
                Program.Check(settings.LeftWidth == expectedLeft &&
                    settings.RightWidth == expectedRight &&
                    center.MinWidth == 403);
                double compressedRight = settings.RightWidth;
                dragLeft.Invoke(window, [200d]);
                // Reversing this divider grows the article; right stays unchanged.
                Program.Check(settings.LeftWidth == 200 &&
                    settings.RightWidth == compressedRight);
                dragRight.Invoke(window, [400d]);
                double maximumRight = Math.Min(400, available - 400);
                double expectedRightAfterDrag = maximumRight;
                Program.Check(settings.RightWidth == expectedRightAfterDrag &&
                    settings.LeftWidth >= 200 &&
                    available - settings.LeftWidth - settings.RightWidth >= 200);
                double compressedLeft = settings.LeftWidth;
                dragRight.Invoke(window, [200d]);
                // Reversing the right divider grows only the article.
                Program.Check(settings.RightWidth == 200 &&
                    settings.LeftWidth == compressedLeft);
            }
            finally { window.Close(); WpfTestHelpers.Pump(); }
        });

        Program.Run("left splitter keyboard width survives window resize", () =>
        {
            var window = new ReadArticles.MainWindow(false);
            try
            {
                window.Show();
                var grid = (Grid)window.Content;
                var splitter = grid.Children.OfType<Thumb>().Single();
                var left = (ColumnDefinition)window.FindName("LeftColumn");
                var source = PresentationSource.FromVisual(window);
                Program.Check(source is not null);

                var settings = (ReaderSettings)typeof(ReadArticles.MainWindow)
                    .GetField("settings", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .GetValue(window)!;
                double before = settings.LeftWidth;
                double available = Math.Floor(Math.Max(606, grid.ActualWidth) - 6);
                double target = Math.Clamp(Math.Round(before + 16), 200,
                    Math.Min(400, available - 400));
                splitter.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, source!, 0, Key.Right)
                {
                    RoutedEvent = Keyboard.PreviewKeyDownEvent
                });
                WpfTestHelpers.Pump();
                if (Math.Abs(settings.LeftWidth - target) >= 1 ||
                    Math.Abs(left.ActualWidth - target) >= 1)
                    throw new Exception($"Keyboard pane resize: expected={target}, " +
                        $"session={settings.LeftWidth}, actual={left.ActualWidth}, " +
                        $"grid={grid.ActualWidth}, initial={before}");

                window.Width += 50;
                WpfTestHelpers.Pump();
                if (Math.Abs(left.ActualWidth - target) >= 1)
                    throw new Exception($"Pane width changed after window resize: " +
                        $"expected={target}, actual={left.ActualWidth}, grid={grid.ActualWidth}");
            }
            finally { window.Close(); WpfTestHelpers.Pump(); }
        });

        Program.Run("current structured settings normalize values and keep pane widths session-only", () =>
        {
            string path = ReaderSettings.PathName;
            string backup = AppPaths.SettingsBackupFile;
            byte[]? old = File.Exists(path) ? File.ReadAllBytes(path) : null;
            byte[]? oldBackup = File.Exists(backup) ? File.ReadAllBytes(backup) : null;
            try
            {
                var invalid = new ReaderSettings
                {
                    LeftWidth = 221.4, RightWidth = 281.8,
                    Accent = "invalid", ForwardStep = 1, BackwardStep = 1
                };
                invalid.Appearance.Body.Size = 99;
                invalid.Appearance.Body.ParagraphAfter = -5;
                invalid.Appearance.Body.SelectionAddSize = 0.60;
                invalid.Appearance.Body.Font = "Nunito Sans";
                invalid.Appearance.Body.SelectionBackground = "#BAD";
                invalid.Appearance.Title.LineHeight = 1.3;
                invalid.Appearance.Vocabulary.Color = "#BAD";
                JsonFiles.WriteAtomic(path, invalid);
                var loaded = ReaderSettings.Load(out var warning);
                Program.Check(warning == null && loaded.LeftWidth == 250 && loaded.RightWidth == 300);
                Program.Check(loaded.FontSize == 15 && loaded.Accent == "uk");
                Program.Check(loaded.ForwardStep == 4 && loaded.BackwardStep == 5);
                Program.Check(loaded.Appearance.Body.ParagraphAfter == 16 &&
                    loaded.Appearance.Title.LineHeight == 1.7 &&
                    loaded.Appearance.Body.LineHeight == 1.7 &&
                    loaded.Appearance.Body.ScriptColor == "#B64550" &&
                    loaded.Appearance.Body.SelectionAddSize == 0.65 &&
                    loaded.Appearance.Body.Font == "Georgia" &&
                    loaded.Appearance.Body.SelectionBackground == "#DDE8F2" &&
                    loaded.Appearance.Vocabulary.Size == 16 &&
                    loaded.Appearance.Phonetics.Size == 14 &&
                    loaded.Appearance.Vocabulary.Color == "#B7394D");
                loaded.Appearance.Body.SelectionAddSize = 0.80;
                loaded.Appearance.Title.Font = "Noto Sans";
                loaded.Appearance.Body.Font = "Nunito";
                loaded.Appearance.Vocabulary.Font = "Noto Sans";
                loaded.Appearance.Body.SelectionBackground = "#EBE5F2";
                loaded.Save();
                loaded = ReaderSettings.Load(out warning);
                Program.Check(warning == null &&
                    loaded.Appearance.Body.SelectionAddSize == 0.80 &&
                    loaded.Appearance.Title.Font == "Noto Sans" &&
                    loaded.Appearance.Body.Font == "Nunito" &&
                    loaded.Appearance.Vocabulary.Font == "Noto Sans" &&
                    loaded.Appearance.Body.SelectionBackground == "#EBE5F2");
                loaded.FontSize = double.NaN;
                loaded.Save();
                loaded = ReaderSettings.Load(out warning);
                Program.Check(warning == null && loaded.FontSize == 15);
                byte[] persisted = File.ReadAllBytes(path);
                byte[] backupBytes = File.ReadAllBytes(backup);
                loaded.LeftWidth = 517;
                loaded.RightWidth = 645;
                loaded.Save();
                loaded.Save();
                Program.Check(File.ReadAllBytes(path).SequenceEqual(persisted) &&
                    File.ReadAllBytes(backup).SequenceEqual(backupBytes));
            }
            finally { Restore(path, old); Restore(backup, oldBackup); }
        });

        Program.Run("breaking change replaces old flat configuration without importing values", () =>
        {
            string path = ReaderSettings.PathName;
            byte[]? old = File.Exists(path) ? File.ReadAllBytes(path) : null;
            try
            {
                JsonFiles.WriteAtomic(path, new
                {
                    FontSize = 17, Accent = "us",
                    ForwardStep = 15, Library = TestData.LibraryRoot
                });
                var fresh = ReaderSettings.Load(out var warning);
                Program.Check(warning != null &&
                    fresh.FontSize == 15 &&
                    fresh.Accent == "uk" && fresh.Library == null &&
                    fresh.Appearance.Vocabulary.Size == 16);
                var reopened = ReaderSettings.Load(out warning);
                Program.Check(warning == null && reopened.Appearance.Title.LineHeight == 1.7 &&
                    reopened.Appearance.Meanings.LineHeight == 1.5);
            }
            finally { Restore(path, old); }
        });

        Program.Run("native settings persist only in exe-adjacent userdata config", () =>
        {
            string path = ReaderSettings.PathName;
            byte[]? old = File.Exists(path) ? File.ReadAllBytes(path) : null;
            ReadArticles.MainWindow? window = null;
            try
            {
                if (File.Exists(path)) File.Delete(path);
                window = new ReadArticles.MainWindow
                    { ShowActivated = false, Left = -10000, Top = -10000 };
                window.Show();
                Program.Check(File.Exists(path) &&
                    Path.GetRelativePath(AppPaths.ApplicationDirectory, path) ==
                        Path.Combine("userdata", "config", "settings.json"));
                var defaults = ReaderSettings.Load(out var warning);
                Program.Check(warning == null &&
                    typeof(ReaderSettings).GetProperty("Volume") == null &&
                    typeof(ReaderAudioPreferences).GetProperty("Volume") == null &&
                    defaults.FontSize == 15 &&
                    defaults.Appearance.Title.Size == 18 &&
                    defaults.Appearance.Title.LineHeight == 1.7 &&
                    defaults.Appearance.Body.LineHeight == 1.7 &&
                    defaults.Appearance.Meanings.LineHeight == 1.5 &&
                    defaults.Appearance.Body.SelectionAddSize == 0.65 &&
                    defaults.Appearance.Body.SelectionBackground == "#DDE8F2" &&
                    defaults.Appearance.Body.ScriptColor == "#B64550" &&
                    defaults.Appearance.Title.Font == "Georgia" &&
                    defaults.Appearance.Body.ParagraphAfter == 16 &&
                    defaults.Appearance.Phonetics.Size == 14);
                Program.Check(((Button)window.FindName("SettingsButton")).ContextMenu is null);
                var player = (WpfAudioPlayer)typeof(ReadArticles.MainWindow)
                    .GetField("player", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .GetValue(window)!;
                var pronunciationPlayer = (WpfAudioPlayer)typeof(ReadArticles.MainWindow)
                    .GetField("pronunciationPlayer", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .GetValue(window)!;
                Program.Check(player.Volume == 1.0 && pronunciationPlayer.Volume == 1.0);
                Program.Check(window.ViewModel.OpenLibrary(TestData.LibraryRoot));
                var draft = defaults.Copy();
                draft.ForwardStep = 7;
                draft.Accent = "us";
                draft.Appearance.Body.Size = 17;
                draft.Appearance.Body.Background = "#F7F1E6";
                var apply = typeof(ReadArticles.MainWindow).GetMethod("ApplyDialogSettings",
                    BindingFlags.Instance | BindingFlags.NonPublic)!;
                Program.Check((bool)apply.Invoke(window, [draft])!);
                var saved = ReaderSettings.Load(out warning);
                Program.Check(warning == null && saved.FontSize == 17 &&
                    saved.ForwardStep == 7 && saved.Accent == "us" &&
                    saved.Library == TestData.LibraryRoot &&
                    saved.Appearance.Body.Background == "#F7F1E6");
                window.Close();
                window = null;
                string raw = File.ReadAllText(path);
                Program.Check(!raw.Contains("LeftWidth", StringComparison.Ordinal) &&
                    !raw.Contains("RightWidth", StringComparison.Ordinal) &&
                    !raw.Contains("\"Volume\"", StringComparison.Ordinal) &&
                    raw.Contains("\"Appearance\"", StringComparison.Ordinal) &&
                    raw.Contains("\"Audio\"", StringComparison.Ordinal));
                var closed = ReaderSettings.Load(out warning);
                Program.Check(warning == null && closed.LeftWidth == 250 &&
                    closed.RightWidth == 300 && closed.FontSize == 17);
            }
            finally
            {
                window?.Close();
                Restore(path, old);
            }
        });
        Program.Run("settings dialog has fixed Fluent layout and eight five-color palettes", () =>
        {
            var type = typeof(ReadArticles.MainWindow).Assembly
                .GetType("ReadArticles.SettingsDialog", throwOnError: true)!;
            var ctor = type.GetConstructor(BindingFlags.Instance | BindingFlags.NonPublic,
                binder: null, types: [typeof(ReaderSettings), typeof(Func<ReaderSettings, bool>)],
                modifiers: null)!;
            var dialog = (Window)ctor.Invoke([new ReaderSettings(),
                new Func<ReaderSettings, bool>(_ => true)]);
            try
            {
                Program.Check(dialog.Title == "设置" && dialog.ResizeMode == ResizeMode.NoResize &&
                    dialog.Width == 500 && dialog.Height == 540 && !dialog.ShowInTaskbar &&
                    dialog.FontSize == 13);
                var sections = (StackPanel)type.GetField("sections",
                    BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(dialog)!;
                Program.Check(sections.Width == 444);
                var scroll = ((Grid)dialog.Content).Children.OfType<ScrollViewer>().Single();
                Program.Check(scroll.HorizontalContentAlignment == HorizontalAlignment.Left);
                int palettes = 0;
                foreach (var section in sections.Children.OfType<StackPanel>())
                {
                    var heading = section.Children.OfType<TextBlock>().Single();
                    Program.Check(heading.FontSize == 15);
                    foreach (var row in section.Children.OfType<Grid>())
                    {
                        Program.Check(row.ColumnDefinitions.Select(c => c.Width.Value)
                            .SequenceEqual(new[] { 72d, 140d, 20d, 72d, 140d }));
                        foreach (var label in row.Children.OfType<TextBlock>())
                            Program.Check(label.FontSize == 13 &&
                                label.HorizontalAlignment == HorizontalAlignment.Left &&
                                label.TextAlignment == TextAlignment.Left &&
                                Grid.GetColumn(label) is 0 or 3);
                        foreach (var editor in row.Children.OfType<FrameworkElement>()
                            .Where(child => child is not TextBlock))
                        {
                            Program.Check(editor.HorizontalAlignment == HorizontalAlignment.Left &&
                                Grid.GetColumn(editor) is 1 or 4);
                        }
                        foreach (var selector in row.Children.OfType<ComboBox>())
                            Program.Check(selector.Width == 100);
                        foreach (var group in row.Children.OfType<Grid>())
                        {
                            if (group.ColumnDefinitions.Count == 5)
                            {
                                Program.Check(group.Width == 140 &&
                                    group.ColumnDefinitions.All(c => c.Width.Value == 28));
                                var buttons = group.Children.OfType<Button>().ToArray();
                                Program.Check(buttons.Length == 5 &&
                                    buttons.All(button => button.Width == 28));
                                int selected = buttons.Count(button =>
                                    ((Grid)button.Content).Children.OfType<TextBlock>()
                                    .Single().Visibility == Visibility.Visible);
                                Program.Check(selected == 1);
                                palettes++;
                            }
                            else if (group.ColumnDefinitions.Count == 2)
                            {
                                // The 100-DIP dropdown + 20-DIP unit label
                                // retains the established compact layout.
                                Program.Check(group.ColumnDefinitions.Select(c => c.Width.Value)
                                    .SequenceEqual(new[] { 100d, 20d }));
                                Program.Check(group.Children.OfType<ComboBox>()
                                    .Single().Width == 100);
                                Program.Check(group.Children.OfType<TextBlock>()
                                    .Single().FontSize == 12);
                            }
                        }
                    }
                }
                var body = sections.Children.OfType<StackPanel>().ElementAt(1);
                Program.Check(body.Children.OfType<Grid>().Any(row =>
                    row.Children.OfType<TextBlock>().Any(label => label.Text == "选词加号")) &&
                    body.Children.OfType<Grid>().Any(row =>
                    row.Children.OfType<TextBlock>().Any(label => label.Text == "选词背景")) &&
                    body.Children.OfType<Grid>().Any(row =>
                    row.Children.OfType<TextBlock>().Any(label => label.Text == "文字颜色")) &&
                    body.Children.OfType<Grid>().Any(row =>
                    row.Children.OfType<TextBlock>().Any(label => label.Text == "台词颜色")));
                var selectionRow = body.Children.OfType<Grid>().Single(row =>
                    row.Children.OfType<TextBlock>().Any(label => label.Text == "选词加号"));
                var selectionSize = selectionRow.Children.OfType<Grid>()
                    .SelectMany(group => group.Children.OfType<ComboBox>()).Single();
                Program.Check(selectionSize.Items.OfType<ComboBoxItem>()
                    .Select(item => (double)item.Tag)
                    .SequenceEqual(new[] { 0.65, 0.70, 0.75, 0.80 }) &&
                    (double)((ComboBoxItem)selectionSize.SelectedItem).Tag == 0.65);
                var fontDropdowns = sections.Children.OfType<StackPanel>()
                    .SelectMany(section => section.Children.OfType<Grid>())
                    .SelectMany(row => row.Children.OfType<ComboBox>())
                    .ToArray();
                var englishSelectors = fontDropdowns.Where(combo =>
                    combo.Items.Count == 5 &&
                    combo.Items.OfType<ComboBoxItem>().All(item => item.Tag is string)).ToArray();
                Program.Check(englishSelectors.Length == 3);
                foreach (var selector in englishSelectors)
                    Program.Check(selector.Items.OfType<ComboBoxItem>()
                        .Select(item => item.Tag?.ToString())
                        .SequenceEqual(new[] { "Georgia", "Microsoft YaHei UI",
                            "Nunito", "Segoe UI", "Noto Sans" }));
                var audio = sections.Children.OfType<StackPanel>().Last();
                Program.Check(audio.Children.OfType<Grid>().Count() == 2 &&
                    !audio.Children.OfType<Grid>().SelectMany(row => row.Children.OfType<Grid>())
                        .Any(group => group.Children.OfType<Slider>().Any()));
                Program.Check(palettes == 8 &&
                    !sections.Children.OfType<StackPanel>()
                        .SelectMany(section => section.Children.OfType<Grid>())
                        .SelectMany(row => row.Children.OfType<TextBlock>())
                        .Any(label => label.Text == "播放音量"));
            }
            finally { dialog.Close(); }
        });
    }


    private static void Restore(string path, byte[]? bytes)
    {
        if (bytes == null)
        {
            if (File.Exists(path)) File.Delete(path);
        }
        else
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, bytes);
        }
    }
}
