using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using ReadArticles.Infrastructure;
using ReadArticles.Layout;
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
                var layout = (PaneLayout)typeof(ReadArticles.MainWindow)
                    .GetField("paneLayout", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .GetValue(window)!;

                window.Width = Math.Min(window.Width, Math.Max(window.MinWidth, 820));
                WpfTestHelpers.Pump();
                var type = typeof(ReadArticles.MainWindow);
                var dragLeft = type.GetMethod("ResizeLibraryPane",
                    BindingFlags.Instance | BindingFlags.NonPublic)!;
                var bridge = (ReadArticles.Presentation.WebReaderBridge)type
                    .GetField("webReaderBridge", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
                void DragRight(double width)
                {
                    using var data = System.Text.Json.JsonDocument.Parse(
                        System.Text.Json.JsonSerializer.Serialize(new { payload = new { width } }));
                    bridge.HandlePaneWidth(data.RootElement, Guid.NewGuid().ToString("D"));
                }
                double available = Math.Floor(Math.Max(606, grid.ActualWidth) - 6);
                double initialRight = layout.RightWidth;
                dragLeft.Invoke(window, [400d]);
                double expectedLeft = Math.Min(400, available - 400);
                double expectedRight = Math.Min(initialRight, available - expectedLeft - 200);
                Program.Check(layout.LeftWidth == expectedLeft &&
                    layout.RightWidth == expectedRight &&
                    center.MinWidth == 403);
                double compressedRight = layout.RightWidth;
                dragLeft.Invoke(window, [200d]);
                // Reversing this divider grows the article; right stays unchanged.
                Program.Check(layout.LeftWidth == 200 &&
                    layout.RightWidth == compressedRight);
                DragRight(400);
                double maximumRight = Math.Min(400, available - 400);
                double expectedRightAfterDrag = maximumRight;
                Program.Check(layout.RightWidth == expectedRightAfterDrag &&
                    layout.LeftWidth >= 200 &&
                    available - layout.LeftWidth - layout.RightWidth >= 200);
                double compressedLeft = layout.LeftWidth;
                DragRight(200);
                // Reversing the right divider grows only the article.
                Program.Check(layout.RightWidth == 200 &&
                    layout.LeftWidth == compressedLeft);
            }
            finally { window.Close(); WpfTestHelpers.Pump(); }
        });

        Program.Run("native main-window playback keys are consumed without an article", () =>
        {
            var window = new ReadArticles.MainWindow(false);
            try
            {
                window.Show();
                var source = PresentationSource.FromVisual(window);
                Program.Check(source is not null);
                var splitter = (Thumb)window.FindName("LibrarySplitter");
                var left = (ColumnDefinition)window.FindName("LeftColumn");
                var controls = new UIElement[] { window, splitter,
                    (TreeView)window.FindName("LibraryTree"),
                    (Button)window.FindName("SettingsButton") };
                double before = left.Width.Value;
                foreach (var control in controls)
                {
                    foreach (Key key in new[] { Key.Space, Key.Left, Key.Right })
                    {
                        foreach (var routedEvent in new[] { Keyboard.PreviewKeyDownEvent, Keyboard.PreviewKeyUpEvent })
                        {
                            var args = new KeyEventArgs(Keyboard.PrimaryDevice, source!, 0, key)
                            {
                                RoutedEvent = routedEvent
                            };
                            control.RaiseEvent(args);
                            Program.Check(args.Handled);
                        }
                    }
                }
                Program.Check(left.Width.Value == before && !window.ViewModel.Playback.IsArticleActive);
                var enter = new KeyEventArgs(Keyboard.PrimaryDevice, source!, 0, Key.Enter)
                {
                    RoutedEvent = Keyboard.PreviewKeyDownEvent
                };
                splitter.RaiseEvent(enter);
                Program.Check(!enter.Handled);
            }
            finally { window.Close(); WpfTestHelpers.Pump(); }
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
                    fresh.Appearance.Body.Size == 15 &&
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
                    defaults.Appearance.Body.Size == 15 &&
                    defaults.Appearance.Title.Size == 18 &&
                    defaults.Appearance.Title.LineHeight == 1.7 &&
                    defaults.Appearance.Body.LineHeight == 1.7 &&
                    defaults.Appearance.Meanings.LineHeight == 1.5 &&
                    defaults.Appearance.Body.SelectionAddSize == 0.65 &&
                    defaults.Appearance.Body.SelectionBackground == "#DDE8F2" &&
                    defaults.Appearance.Body.ScriptColor == "#F6EDC2" &&
                    defaults.Appearance.Title.Font == "Newsreader" &&
                    defaults.Appearance.Body.Font == "Newsreader" &&
                    defaults.Appearance.Vocabulary.Font == "Newsreader" &&
                    defaults.Appearance.Meanings.Font == "Source Han Serif SC" &&
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
                draft.ForwardStep = 23;
                draft.BackwardStep = 31;
                draft.Accent = "us";
                draft.Appearance.Body.Size = 17;
                draft.Appearance.Body.Background = "#F7F1E6";
                var apply = typeof(ReadArticles.MainWindow).GetMethod("ApplyDialogSettings",
                    BindingFlags.Instance | BindingFlags.NonPublic)!;
                object?[] arguments = [draft, null];
                Program.Check((bool)apply.Invoke(window, arguments)!);
                var refresh = (Task)arguments[1]!;
                WpfTestHelpers.Wait(() => refresh.IsCompleted);
                refresh.GetAwaiter().GetResult();
                Program.Check(window.ViewModel.Accent == "us");
                var saved = ReaderSettings.Load(out warning);
                Program.Check(warning == null && saved.Appearance.Body.Size == 17 &&
                    saved.ForwardStep == 23 && saved.BackwardStep == 31 && saved.Accent == "us" &&
                    saved.Library == TestData.LibraryRoot &&
                    saved.Appearance.Body.Background == "#F7F1E6");
                string missingRoot = Path.Combine(Path.GetTempPath(), "ReadArticles-refresh-settings-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(missingRoot);
                try
                {
                    Program.Check(window.ViewModel.OpenLibrary(missingRoot));
                    Directory.Delete(missingRoot);
                    Task<bool> missing = window.ViewModel.RefreshLibraryAsync();
                    WpfTestHelpers.Wait(() => missing.IsCompleted);
                    Program.Check(!missing.GetAwaiter().GetResult() &&
                        window.ViewModel.LibraryRoot is null && ReaderSettings.Load(out _).Library is null);
                }
                finally { if (Directory.Exists(missingRoot)) Directory.Delete(missingRoot); }
                window.Close();
                window = null;
                string raw = File.ReadAllText(path);
                Program.Check(!raw.Contains("LeftWidth", StringComparison.Ordinal) &&
                    !raw.Contains("RightWidth", StringComparison.Ordinal) &&
                    !raw.Contains("\"Volume\"", StringComparison.Ordinal) &&
                    raw.Contains("\"Appearance\"", StringComparison.Ordinal) &&
                    raw.Contains("\"Audio\"", StringComparison.Ordinal));
                var closed = ReaderSettings.Load(out warning);
                var freshLayout = new PaneLayout();
                Program.Check(warning == null && freshLayout.LeftWidth == 250 &&
                    freshLayout.RightWidth == 300 && closed.Appearance.Body.Size == 17);
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
            var dialog = (Window)ctor.Invoke([new ReaderSettings(), new Func<ReaderSettings, bool>(_ => false)]);
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
                    if (heading.Text == "其它")
                    {
                        var row = section.Children.OfType<Grid>().Single();
                        Program.Check(row.ColumnDefinitions.Select(c => c.Width.Value)
                            .SequenceEqual(new[] { 112d, 332d }));
                        Program.Check(row.Children.OfType<TextBlock>().Single().Text == "需排除的文件夹");
                        var input = row.Children.OfType<TextBox>().Single();
                        Program.Check(input.Text == ReaderSettings.DefaultExcludedFolders &&
                            input.Width == 332 && input.Height == 30 && !input.AcceptsReturn);
                        // Exercise the actual loaded Fluent template, not a synthetic Loaded event.
                        dialog.Show();
                        input.BringIntoView();
                        input.Focus();
                        WpfTestHelpers.Pump();
                        dialog.UpdateLayout();
                        Program.Check(input.IsLoaded);
                        var template = input.Template ?? throw new Exception("Fluent TextBox template was not loaded.");
                        var clearButton = template.FindName("DeleteButton", input) as UIElement;
                        Program.Check(clearButton is not null &&
                            clearButton.Visibility == Visibility.Collapsed &&
                            Equals(clearButton.ReadLocalValue(UIElement.VisibilityProperty), Visibility.Collapsed));
                        input.Text = "video, 结果";
                        var edited = (ReaderSettings)type.GetField("draft",
                            BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(dialog)!;
                        Program.Check(edited.ExcludedFolders == "video, 结果");
                        continue;
                    }
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
                                // The 100-DIP editor + 20-DIP unit label
                                // retains the established compact layout.
                                Program.Check(group.ColumnDefinitions.Select(c => c.Width.Value)
                                    .SequenceEqual(new[] { 100d, 20d }));
                                Program.Check(group.Children.OfType<TextBox>().Single().Width == 100 &&
                                    !group.Children.OfType<ComboBox>().Any());
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
                    row.Children.OfType<TextBlock>().Any(label => label.Text == "播放颜色")));
                var playbackRow = body.Children.OfType<Grid>().Single(row =>
                    row.Children.OfType<TextBlock>().Any(label => label.Text == "播放颜色"));
                var playbackLabel = playbackRow.Children.OfType<TextBlock>().Single(label => label.Text == "播放颜色");
                var playbackPalette = playbackRow.Children.OfType<Grid>().Single(group =>
                    Grid.GetColumn(group) == Grid.GetColumn(playbackLabel) + 1);
                var playbackSwatches = playbackPalette.Children.OfType<Button>().Select(button => (Grid)button.Content).ToArray();
                Program.Check(playbackSwatches.Select(swatch =>
                    ((System.Windows.Media.SolidColorBrush)swatch.Children.OfType<System.Windows.Shapes.Ellipse>()
                        .Single().Fill).Color.ToString()).SequenceEqual(
                    new[] { "#FFF6EDC2", "#FFF5E4D6", "#FFE1EEDB", "#FFE0ECF4", "#FFECE6F3" }) &&
                    playbackSwatches.All(swatch =>
                        ((System.Windows.Media.SolidColorBrush)swatch.Children.OfType<TextBlock>()
                            .Single().Foreground).Color.ToString() == "#FF283747"));
                var fontDropdowns = sections.Children.OfType<StackPanel>()
                    .SelectMany(section => section.Children.OfType<Grid>())
                    .SelectMany(row => row.Children.OfType<ComboBox>())
                    .ToArray();
                var englishSelectors = fontDropdowns.Where(combo =>
                    combo.Items.Count == 5 &&
                    combo.Items.OfType<ComboBoxItem>().All(item => item.Tag is string)).ToArray();
                Program.Check(englishSelectors.Length == 3 && fontDropdowns.Length == 5);
                foreach (var selector in englishSelectors)
                    Program.Check(selector.Items.OfType<ComboBoxItem>()
                        .Select(item => item.Tag?.ToString())
                        .SequenceEqual(new[] { "Newsreader", "Source Serif 4", "Georgia",
                            "Nunito", "Microsoft YaHei UI" }));
                var meanings = sections.Children.OfType<StackPanel>().Single(section =>
                    section.Children.OfType<TextBlock>().Single().Text == "释义");
                var meaningSelector = meanings.Children.OfType<Grid>().SelectMany(row =>
                    row.Children.OfType<ComboBox>()).Single();
                Program.Check(meaningSelector.Items.OfType<ComboBoxItem>().Select(item => (string)item.Tag)
                    .SequenceEqual(new[] { "Source Han Serif SC", "Microsoft YaHei" }) &&
                    meaningSelector.Items.OfType<ComboBoxItem>().Select(item => (string)item.Content)
                    .SequenceEqual(new[] { "思源宋体", "微软雅黑" }) && meaningSelector.SelectedIndex == 0);
                var audio = sections.Children.OfType<StackPanel>().Single(section =>
                    section.Children.OfType<TextBlock>().Single().Text == "音频");
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

        Program.Run("numeric editors validate typing, paste, save and reset", () =>
        {
            var type = typeof(ReadArticles.MainWindow).Assembly
                .GetType("ReadArticles.SettingsDialog", throwOnError: true)!;
            var ctor = type.GetConstructor(BindingFlags.Instance | BindingFlags.NonPublic,
                binder: null, types: [typeof(ReaderSettings), typeof(Func<ReaderSettings, bool>)],
                modifiers: null)!;
            var originalSettings = new ReaderSettings();
            ReaderSettings? savedDraft = null;
            int commitCalls = 0;
            var dialog = (Window)ctor.Invoke([originalSettings,
                new Func<ReaderSettings, bool>(value =>
                {
                    commitCalls++;
                    savedDraft = value.Copy();
                    return false;
                })]);
            var previousCulture = System.Globalization.CultureInfo.CurrentCulture;
            try
            {
                System.Globalization.CultureInfo.CurrentCulture = System.Globalization.CultureInfo.GetCultureInfo("de-DE");
                var sections = (StackPanel)type.GetField("sections",
                    BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(dialog)!;
                dialog.Show();
                WpfTestHelpers.Pump();
                TextBox[] NumberBoxes() => sections.Children.OfType<StackPanel>()
                    .SelectMany(section => section.Children.OfType<Grid>())
                    .SelectMany(row => row.Children.OfType<Grid>())
                    .SelectMany(group => group.Children.OfType<TextBox>()).ToArray();
                var defaults = new[] { "18", "1.7", "16", "15", "1.7", "16", "2", "0.65",
                    "16", "14", "15", "1.5", "4", "5" };
                var boxes = NumberBoxes();
                Program.Check(boxes.Select(box => box.Text).SequenceEqual(defaults) &&
                    boxes.Select(box => box.MaxLength).SequenceEqual(new[] { 2, 4, 2, 2, 4, 2, 1, 4, 2, 2, 2, 4, 2, 2 }) &&
                    boxes.All(box => box.Width == 100 && box.Height == 30 && box.FontSize == 13 &&
                        box.TextAlignment == TextAlignment.Center &&
                        box.VerticalContentAlignment == VerticalAlignment.Center && !box.AcceptsReturn &&
                        !InputMethod.GetIsInputMethodEnabled(box)));
                foreach (var box in boxes)
                {
                    box.BringIntoView();
                    WpfTestHelpers.Pump();
                    box.ApplyTemplate();
                    Program.Check(box.Template?.FindName("DeleteButton", box) is UIElement clear &&
                        clear.Visibility == Visibility.Collapsed);
                }
                bool RejectsTyping(TextBox box, string text)
                {
                    var args = new TextCompositionEventArgs(Keyboard.PrimaryDevice,
                        new TextComposition(InputManager.Current, box, text))
                        { RoutedEvent = TextCompositionManager.PreviewTextInputEvent };
                    box.RaiseEvent(args);
                    return args.Handled;
                }
                bool RejectsPaste(TextBox box, string text)
                {
                    var args = new DataObjectPastingEventArgs(
                        new DataObject(DataFormats.UnicodeText, text), false, DataFormats.UnicodeText)
                        { RoutedEvent = DataObject.PastingEvent };
                    box.RaiseEvent(args);
                    return args.CommandCancelled;
                }
                foreach (var box in boxes)
                {
                    box.SelectAll();
                    Program.Check(RejectsTyping(box, "abc") && RejectsTyping(box, "-") &&
                        RejectsTyping(box, "１") && RejectsPaste(box, "2a") && RejectsPaste(box, " 2"));
                    if (box.MaxLength == 4)
                        Program.Check(!RejectsPaste(box, "1.75") && !RejectsPaste(box, ".65") &&
                            !RejectsPaste(box, "1.") && RejectsPaste(box, "1.755") && RejectsPaste(box, "1..7") &&
                            RejectsPaste(box, "12.3"));
                    else
                        Program.Check(RejectsTyping(box, ".") && RejectsPaste(box, "100") &&
                            RejectsPaste(box, "1.5") && RejectsPaste(box, box.MaxLength == 1 ? "10" : "123") &&
                            !RejectsPaste(box, box.MaxLength == 1 ? "2" : "25"));
                }
                var sizeBox = boxes[0];
                sizeBox.Select(2, 0);
                Program.Check(RejectsTyping(sizeBox, "3") && RejectsPaste(sizeBox, "3"));
                sizeBox.SelectAll();
                sizeBox.SelectedText = "25";
                Program.Check(sizeBox.Text == "25");
                var lineBox = boxes[1];
                lineBox.Text = "1.7";
                lineBox.SelectAll();
                lineBox.SelectedText = "1.75";
                Program.Check(lineBox.Text == "1.75" && lineBox.CanUndo);
                lineBox.Select(1, 1);
                Program.Check(!ApplicationCommands.Cut.CanExecute(null, lineBox) || lineBox.SelectionLength == 1);
                ApplicationCommands.Cut.Execute(null, lineBox);
                Program.Check(lineBox.Text == "1.75");
                lineBox.Undo();
                Program.Check(lineBox.Text == "1.7" && lineBox.CanRedo);
                lineBox.Redo();
                Program.Check(lineBox.Text == "1.75");
                lineBox.Select(1, 1);
                lineBox.SelectedText = "";
                Program.Check(lineBox.Text == "1.75");
                lineBox.SelectAll();
                lineBox.SelectedText = "";
                Program.Check(lineBox.Text == "");
                lineBox.Text = defaults[1];
                var saveButton = ((Grid)dialog.Content).Children.OfType<Grid>().Single()
                    .Children.OfType<Button>().Single(button => Equals(button.Content, "保存"));
                foreach (var (index, invalid) in new[] { (0, ""), (0, "0"), (0, "09"), (0, "41"), (0, "00"),
                    (1, "."), (1, "0.99"), (1, "3.01"), (2, "41"), (4, "0"), (5, "99"),
                    (6, "6"), (7, "0.39"), (7, "1.21"), (11, "3.1"), (12, "0"), (13, "61") })
                {
                    boxes[index].Text = invalid;
                    saveButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                    var notice = (Popup)type.GetField("notice", BindingFlags.Instance | BindingFlags.NonPublic)!
                        .GetValue(dialog)!;
                    Program.Check(commitCalls == 0 && notice.IsOpen &&
                        ReferenceEquals(notice.PlacementTarget, boxes[index]));
                    boxes[index].Text = defaults[index];
                }
                var custom = new[] { "10", "1.", "0", "40", "3", "40", "5", ".65",
                    "21", "22", "23", "1.55", "1", "60" };
                for (int i = 0; i < boxes.Length; i++) boxes[i].Text = custom[i];
                saveButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                Program.Check(commitCalls == 1 && savedDraft is not null &&
                    savedDraft.Appearance.Title.Size == 10 && savedDraft.Appearance.Body.Size == 40 &&
                    savedDraft.Appearance.Vocabulary.Size == 21 && savedDraft.Appearance.Phonetics.Size == 22 &&
                    savedDraft.Appearance.Meanings.Size == 23 && savedDraft.Appearance.Title.LineHeight == 1 &&
                    savedDraft.Appearance.Body.LineHeight == 3 && savedDraft.Appearance.Meanings.LineHeight == 1.55 &&
                    savedDraft.Appearance.Title.ParagraphAfter == 0 && savedDraft.Appearance.Body.ParagraphAfter == 40 &&
                    savedDraft.Appearance.Body.FirstLineIndent == 5 && savedDraft.Appearance.Body.SelectionAddSize == 0.65 &&
                    savedDraft.ForwardStep == 1 && savedDraft.BackwardStep == 60 &&
                    originalSettings.Appearance.Title.Size == 18 && originalSettings.Appearance.Body.Size == 15 &&
                    originalSettings.ForwardStep == 4 && originalSettings.BackwardStep == 5);

                // Reset must not reintroduce an unavailable default font or
                // retain selectors from the previous draft visual tree.
                var installed = (HashSet<string>)type.GetField("installedFontNames",
                    BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(dialog)!;
                installed.Add("Newsreader");
                installed.Add("Source Serif 4");
                var footer = ((Grid)dialog.Content).Children.OfType<Grid>().Single();
                var resetButton = footer.Children.OfType<Button>().First();
                resetButton.RaiseEvent(new RoutedEventArgs(
                    System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                var resetDraft = (ReaderSettings)type.GetField("draft",
                    BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(dialog)!;
                var selectors = (List<ComboBox>)type.GetField("englishFontSelectors",
                    BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(dialog)!;
                Program.Check(resetDraft.ExcludedFolders == ReaderSettings.DefaultExcludedFolders &&
                    resetDraft.Appearance.Body.ScriptColor == "#F6EDC2" &&
                    resetDraft.Appearance.Meanings.Font == "Source Han Serif SC" &&
                    resetDraft.Appearance.Title.Font == "Newsreader" &&
                    resetDraft.Appearance.Body.Font == "Newsreader" &&
                    resetDraft.Appearance.Vocabulary.Font == "Newsreader" &&
                    selectors.Count == 3 && selectors.All(combo => combo.SelectedIndex == 0));
                Program.Check(NumberBoxes().Select(box => box.Text).SequenceEqual(defaults));
                installed.Remove("Newsreader");
                resetButton.RaiseEvent(new RoutedEventArgs(
                    System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                resetDraft = (ReaderSettings)type.GetField("draft",
                    BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(dialog)!;
                Program.Check(resetDraft.Appearance.Title.Font == "Source Serif 4" &&
                    resetDraft.Appearance.Body.Font == "Source Serif 4" &&
                    resetDraft.Appearance.Vocabulary.Font == "Source Serif 4" &&
                    selectors.Count == 3 && selectors.All(combo => combo.SelectedIndex == 1));
                installed.Remove("Source Serif 4");
                installed.Remove("Georgia");
                installed.Remove("Nunito");
                resetButton.RaiseEvent(new RoutedEventArgs(
                    System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                resetDraft = (ReaderSettings)type.GetField("draft",
                    BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(dialog)!;
                Program.Check(resetDraft.Appearance.Title.Font == "Microsoft YaHei UI" &&
                    resetDraft.Appearance.Body.Font == "Microsoft YaHei UI" &&
                    resetDraft.Appearance.Vocabulary.Font == "Microsoft YaHei UI" &&
                    selectors.Count == 3 && selectors.All(combo => combo.SelectedIndex == 4));
            }
            finally
            {
                System.Globalization.CultureInfo.CurrentCulture = previousCulture;
                dialog.Close();
            }
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
