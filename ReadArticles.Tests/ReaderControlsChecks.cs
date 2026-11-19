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

                double before = left.ActualWidth;
                splitter.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, source!, 0, Key.Right)
                {
                    RoutedEvent = Keyboard.PreviewKeyDownEvent
                });
                double target = Math.Min(400, Math.Min(Math.Floor(
                    Math.Max(606, grid.ActualWidth) - 6) - 400, Math.Round(before) + 16));
                var settings = (ReaderSettings)typeof(ReadArticles.MainWindow)
                    .GetField("settings", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .GetValue(window)!;
                Program.Check(settings.LeftWidth == target &&
                    Math.Abs(left.ActualWidth - target) < 1);

                window.Width += 50;
                WpfTestHelpers.Pump();
                Program.Check(Math.Abs(left.ActualWidth - target) < 1);
            }
            finally { window.Close(); WpfTestHelpers.Pump(); }
        });

        Program.Run("settings own value normalization on load and save", () =>
        {
            string path = ReaderSettings.PathName;
            string legacyPath = AppPaths.LegacySettingsFile;
            string markerPath = AppPaths.LegacySettingsMigrationMarker;
            byte[]? before = File.Exists(path) ? File.ReadAllBytes(path) : null;
            byte[]? legacyBefore = File.Exists(legacyPath) ? File.ReadAllBytes(legacyPath) : null;
            byte[]? markerBefore = File.Exists(markerPath) ? File.ReadAllBytes(markerPath) : null;
            try
            {
                if (File.Exists(legacyPath)) File.Delete(legacyPath);
                if (File.Exists(markerPath)) File.Delete(markerPath);
                JsonFiles.WriteAtomic(path, new { LeftWidth = 221.4, RightWidth = 281.8, FontSize = 99,
                    Volume = 3, Accent = "invalid", ForwardStep = 1, BackwardStep = 1 });
                var settings = ReaderSettings.Load(out var warning);
                // Persisted legacy widths are ignored: every launch starts at defaults.
                Program.Check(warning == null && settings.LeftWidth == 250 && settings.RightWidth == 300);
                Program.Check(settings.FontSize == 16 && settings.Volume == 1 && settings.Accent == "uk");
                Program.Check(settings.ForwardStep == 4 && settings.BackwardStep == 5);
                settings.FontSize = double.NaN;
                settings.Volume = double.NaN;
                settings.Save();
                settings = ReaderSettings.Load(out warning);
                Program.Check(warning == null && settings.FontSize == 15 && settings.Volume == 0.65);
                // Repeated saves and session-only width changes may not rotate the backup.
                byte[] persisted = File.ReadAllBytes(path);
                byte[] backup = File.ReadAllBytes(AppPaths.SettingsBackupFile);
                settings.LeftWidth = 517;
                settings.RightWidth = 645;
                settings.Save();
                settings.Save();
                Program.Check(File.ReadAllBytes(path).SequenceEqual(persisted) &&
                    File.ReadAllBytes(AppPaths.SettingsBackupFile).SequenceEqual(backup));
            }
            finally
            {
                if (before == null) { if (File.Exists(path)) File.Delete(path); } else File.WriteAllBytes(path, before);
                if (legacyBefore == null) { if (File.Exists(legacyPath)) File.Delete(legacyPath); } else File.WriteAllBytes(legacyPath, legacyBefore);
                if (markerBefore == null) { if (File.Exists(markerPath)) File.Delete(markerPath); } else File.WriteAllBytes(markerPath, markerBefore);
            }
        });
        Program.Run("legacy settings migrate to userdata config without deleting the source", () =>
        {
            string currentPath = ReaderSettings.PathName;
            string legacyPath = AppPaths.LegacySettingsFile;
            string markerPath = AppPaths.LegacySettingsMigrationMarker;
            byte[]? currentBefore = File.Exists(currentPath) ? File.ReadAllBytes(currentPath) : null;
            byte[]? legacyBefore = File.Exists(legacyPath) ? File.ReadAllBytes(legacyPath) : null;
            byte[]? markerBefore = File.Exists(markerPath) ? File.ReadAllBytes(markerPath) : null;
            try
            {
                if (File.Exists(currentPath)) File.Delete(currentPath);
                if (File.Exists(markerPath)) File.Delete(markerPath);
                // Simulate a genuine old JSON file that still contains widths.
                JsonFiles.WriteAtomic(legacyPath, new
                {
                    LeftWidth = 244,
                    RightWidth = 355,
                    FontSize = 16,
                    Volume = 0.4,
                    Accent = "us",
                    Library = TestData.LibraryRoot
                });
                var migrated = ReaderSettings.Load(out var warning);
                Program.Check(warning == null && migrated.LeftWidth == 250 && migrated.RightWidth == 300);
                Program.Check(migrated.Accent == "us" && migrated.Volume == 0.4 && migrated.Library == TestData.LibraryRoot);
                Program.Check(File.Exists(currentPath) && File.Exists(legacyPath));
                Program.Check(File.Exists(markerPath));
                var persisted = ReaderSettings.Load(out warning);
                Program.Check(warning == null && persisted.Accent == "us" &&
                    persisted.LeftWidth == 250 && persisted.RightWidth == 300);
                persisted.Accent = "uk";
                persisted.Save();
                var updated = ReaderSettings.Load(out warning);
                Program.Check(warning == null && updated.Accent == "uk");
            }
            finally
            {
                if (currentBefore == null) { if (File.Exists(currentPath)) File.Delete(currentPath); }
                else File.WriteAllBytes(currentPath, currentBefore);
                if (legacyBefore == null) { if (File.Exists(legacyPath)) File.Delete(legacyPath); }
                else File.WriteAllBytes(legacyPath, legacyBefore);
                if (markerBefore == null) { if (File.Exists(markerPath)) File.Delete(markerPath); }
                else File.WriteAllBytes(markerPath, markerBefore);
            }
        });
        Program.Run("settings initialize in userdata config and changes save before close", () =>
        {
            string path = ReaderSettings.PathName;
            string legacyPath = AppPaths.LegacySettingsFile;
            string markerPath = AppPaths.LegacySettingsMigrationMarker;
            byte[]? previous = File.Exists(path) ? File.ReadAllBytes(path) : null;
            byte[]? legacyBefore = File.Exists(legacyPath) ? File.ReadAllBytes(legacyPath) : null;
            byte[]? markerBefore = File.Exists(markerPath) ? File.ReadAllBytes(markerPath) : null;
            ReadArticles.MainWindow? window = null;
            try
            {
                if (File.Exists(legacyPath)) File.Delete(legacyPath);
                if (File.Exists(markerPath)) File.Delete(markerPath);
                File.Delete(path);
                window = new ReadArticles.MainWindow { ShowActivated = false, Left = -10000, Top = -10000 };
                window.Show();
                Program.Check(File.Exists(path) && path == AppPaths.SettingsFile && Path.GetRelativePath(AppPaths.ApplicationDirectory, path) == Path.Combine("userdata", "config", "settings.json"));
                var defaults = ReaderSettings.Load(out var warning);
                Program.Check(warning == null && defaults.ForwardStep == 4 && defaults.BackwardStep == 5 && defaults.FontSize == 15);
                var menu = ((Button)window.FindName("SettingsButton")).ContextMenu;
                var progress = (MenuItem)menu.Items[2];
                var step7 = (MenuItem)((MenuItem)progress.Items[0]).Items[1];
                step7.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                window.ViewModel.Accent = "us";
                Program.Check(window.ViewModel.OpenLibrary(TestData.LibraryRoot));
                var saved = ReaderSettings.Load(out warning);
                Program.Check(warning == null && saved.ForwardStep == 7 && saved.Accent == "us" && saved.Library == TestData.LibraryRoot);
                saved.LeftWidth = 221.4;
                saved.RightWidth = 281.8;
                saved.Save();
                saved = ReaderSettings.Load(out warning);
                Program.Check(saved.LeftWidth == 250 && saved.RightWidth == 300 && saved.FontSize == 15);
                // Resizing is allowed in this session; closing must not persist either width.
                ((ColumnDefinition)window.FindName("LeftColumn")).Width = new GridLength(275);
                window.UpdateLayout();
                window.Close();
                window = null;
                var closedSettings = ReaderSettings.Load(out warning);
                Program.Check(warning == null && closedSettings.LeftWidth == 250 &&
                    closedSettings.RightWidth == 300 && closedSettings.ForwardStep == 7 &&
                    closedSettings.Accent == "us");
                string serialized = File.ReadAllText(path);
                Program.Check(!serialized.Contains("LeftWidth", StringComparison.Ordinal) &&
                    !serialized.Contains("RightWidth", StringComparison.Ordinal));
            }
            finally
            {
                window?.Close();
                if (previous == null) { if (File.Exists(path)) File.Delete(path); } else File.WriteAllBytes(path, previous);
                if (legacyBefore == null) { if (File.Exists(legacyPath)) File.Delete(legacyPath); } else File.WriteAllBytes(legacyPath, legacyBefore);
                if (markerBefore == null) { if (File.Exists(markerPath)) File.Delete(markerPath); } else File.WriteAllBytes(markerPath, markerBefore);
            }
        });
    }
}
