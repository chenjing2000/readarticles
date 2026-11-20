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

        Program.Run("structured reader settings normalize invalid choices and preserve session-only pane widths", () =>
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
                    Volume = 3, Accent = "invalid",
                    ForwardStep = 1, BackwardStep = 1
                };
                invalid.Appearance.Body.Size = 99;
                invalid.Appearance.Body.ParagraphAfter = -5;
                invalid.Appearance.Vocabulary.Color = "#BAD";
                JsonFiles.WriteAtomic(path, invalid);
                var loaded = ReaderSettings.Load(out var warning);
                Program.Check(warning == null && loaded.LeftWidth == 250 && loaded.RightWidth == 300);
                Program.Check(loaded.FontSize == 15 && loaded.Volume == 1 && loaded.Accent == "uk");
                Program.Check(loaded.ForwardStep == 4 && loaded.BackwardStep == 5);
                Program.Check(loaded.Appearance.Body.ParagraphAfter == 12 &&
                    loaded.Appearance.Vocabulary.Color == "#B7394D");
                loaded.FontSize = double.NaN;
                loaded.Volume = double.NaN;
                loaded.Save();
                loaded = ReaderSettings.Load(out warning);
                Program.Check(warning == null && loaded.FontSize == 15 && loaded.Volume == 0.65);
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

        Program.Run("existing userdata config migrates beside the executable without deleting the original", () =>
        {
            string current = ReaderSettings.PathName;
            string previous = AppPaths.PreviousSettingsFile;
            string legacy = AppPaths.LegacySettingsFile;
            byte[]? currentBefore = File.Exists(current) ? File.ReadAllBytes(current) : null;
            byte[]? previousBefore = File.Exists(previous) ? File.ReadAllBytes(previous) : null;
            byte[]? legacyBefore = File.Exists(legacy) ? File.ReadAllBytes(legacy) : null;
            try
            {
                if (File.Exists(current)) File.Delete(current);
                if (File.Exists(legacy)) File.Delete(legacy);
                JsonFiles.WriteAtomic(previous, new
                {
                    LeftWidth = 244, RightWidth = 355, FontSize = 16,
                    Volume = 0.4, Accent = "us", ForwardStep = 7,
                    Library = TestData.LibraryRoot
                });
                var migrated = ReaderSettings.Load(out var warning);
                Program.Check(warning == null && migrated.LeftWidth == 250 && migrated.RightWidth == 300);
                Program.Check(migrated.Accent == "us" && migrated.FontSize == 16 &&
                    migrated.Volume == 0.4 && migrated.ForwardStep == 7 &&
                    migrated.Library == TestData.LibraryRoot);
                Program.Check(File.Exists(current) && File.Exists(previous));
                string oldFingerprint = JsonFiles.Fingerprint(previous);
                migrated.Appearance.Body.Background = "#FFF8E7";
                migrated.Save();
                var reopened = ReaderSettings.Load(out warning);
                Program.Check(warning == null && reopened.Appearance.Body.Background == "#FFF8E7" &&
                    reopened.Accent == "us" && JsonFiles.Fingerprint(previous) == oldFingerprint);
            }
            finally
            {
                Restore(current, currentBefore);
                Restore(previous, previousBefore);
                Restore(legacy, legacyBefore);
            }
        });

        Program.Run("native settings save JSON beside exe without storing splitters", () =>
        {
            string path = ReaderSettings.PathName;
            string previous = AppPaths.PreviousSettingsFile;
            string legacy = AppPaths.LegacySettingsFile;
            byte[]? old = File.Exists(path) ? File.ReadAllBytes(path) : null;
            byte[]? previousBefore = File.Exists(previous) ? File.ReadAllBytes(previous) : null;
            byte[]? legacyBefore = File.Exists(legacy) ? File.ReadAllBytes(legacy) : null;
            ReadArticles.MainWindow? window = null;
            try
            {
                if (File.Exists(path)) File.Delete(path);
                if (File.Exists(previous)) File.Delete(previous);
                if (File.Exists(legacy)) File.Delete(legacy);
                window = new ReadArticles.MainWindow { ShowActivated = false, Left = -10000, Top = -10000 };
                window.Show();
                Program.Check(File.Exists(path) &&
                    Path.GetRelativePath(AppPaths.ApplicationDirectory, path) == "ReadArticles.config.json");
                var defaults = ReaderSettings.Load(out var warning);
                Program.Check(warning == null && defaults.FontSize == 15 &&
                    defaults.Appearance.Title.Size == 18 &&
                    defaults.Appearance.Body.ParagraphAfter == 12 &&
                    defaults.Appearance.Phonetics.Color == "#0A7785");
                Program.Check(((Button)window.FindName("SettingsButton")).ContextMenu is null);
                Program.Check(window.ViewModel.OpenLibrary(TestData.LibraryRoot));
                var draft = defaults.Copy();
                draft.ForwardStep = 7;
                draft.Accent = "us";
                draft.Appearance.Body.Size = 17;
                var apply = typeof(ReadArticles.MainWindow).GetMethod("ApplyDialogSettings",
                    BindingFlags.Instance | BindingFlags.NonPublic)!;
                Program.Check((bool)apply.Invoke(window, [draft])!);
                var saved = ReaderSettings.Load(out warning);
                Program.Check(warning == null && saved.FontSize == 17 && saved.ForwardStep == 7 &&
                    saved.Accent == "us" && saved.Library == TestData.LibraryRoot);
                ((ColumnDefinition)window.FindName("LeftColumn")).Width = new GridLength(275);
                window.UpdateLayout();
                window.Close();
                window = null;
                string raw = File.ReadAllText(path);
                Program.Check(!raw.Contains("LeftWidth", StringComparison.Ordinal) &&
                    !raw.Contains("RightWidth", StringComparison.Ordinal) &&
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
                Restore(previous, previousBefore);
                Restore(legacy, legacyBefore);
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
