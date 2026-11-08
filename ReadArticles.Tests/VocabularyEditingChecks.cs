using System.IO;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Input;
using ReadArticles.Services;

internal static class VocabularyEditingChecks
{
    internal static void Run()
    {
        Program.Run("SVG vocabulary actions swap and delete with matching JSON and selection", () =>
        {
            string root = Path.Combine(Path.GetTempPath(), "ReadArticles-editing-tests-" + Guid.NewGuid().ToString("N"));
            ReadArticles.MainWindow? window = null;
            try
            {
                Directory.CreateDirectory(root);
                window = new ReadArticles.MainWindow(false) { Left = -10000, Top = -10000, ShowActivated = false };
                window.Show();
                string article = Path.Combine(root, "Copy.json");
                string vocabulary = Path.ChangeExtension(article, ".vocabulary.json");
                File.Copy(TestData.ArticlePath, article);
                var data = JsonNode.Parse(File.ReadAllText(Path.ChangeExtension(TestData.ArticlePath, ".vocabulary.json")))!;
                var original = data["words"]!.AsArray();
                data["words"] = new JsonArray(original.Take(3).Select(word => word!.DeepClone()).ToArray());
                File.WriteAllText(vocabulary, data.ToJsonString());
                Program.Check(window.ViewModel.OpenArticle(article));
                var list = (ListBox)window.FindName("VocabularyList");
                var actions = (StackPanel)window.FindName("VocabularyActions");
                var up = (Button)window.FindName("MoveUpButton");
                var down = (Button)window.FindName("MoveDownButton");
                var delete = (Button)window.FindName("DeleteWordButton");
                Program.Check(actions.Visibility == Visibility.Collapsed);
                Program.Check(new[] { up, down, delete }.All(button => ((Image)button.Content).Source is DrawingImage));
                string[] expected = window.ViewModel.Words.Select(w => w.Word).ToArray();
                list.SelectedIndex = 0;
                Program.Check(list.ContextMenu == null);
                WpfTestHelpers.Pump(30);
                var otherRow = (ListBoxItem)list.ItemContainerGenerator.ContainerFromIndex(1);
                string beforeRightClick = File.ReadAllText(vocabulary);
                foreach (var routedEvent in new[] { Mouse.PreviewMouseDownEvent, Mouse.PreviewMouseUpEvent })
                {
                    var rightClick = new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Right) { RoutedEvent = routedEvent };
                    otherRow.RaiseEvent(rightClick);
                    Program.Check(rightClick.Handled && list.SelectedIndex == 0);
                }
                Program.Check(File.ReadAllText(vocabulary) == beforeRightClick && window.ViewModel.PlaybackState == PlaybackState.Stopped);
                Program.Check(actions.Visibility == Visibility.Visible && !up.IsEnabled && down.IsEnabled && delete.IsEnabled);
                Click(down);
                (expected[0], expected[1]) = (expected[1], expected[0]);
                CheckOrder();
                Program.Check(list.SelectedIndex == 1 && up.IsEnabled && down.IsEnabled);
                Click(up);
                (expected[0], expected[1]) = (expected[1], expected[0]);
                CheckOrder();
                Program.Check(list.SelectedIndex == 0 && !up.IsEnabled);
                list.SelectedIndex = 2;
                Program.Check(up.IsEnabled && !down.IsEnabled);
                Click(up);
                (expected[1], expected[2]) = (expected[2], expected[1]);
                CheckOrder();
                Program.Check(list.SelectedIndex == 1 && up.IsEnabled && down.IsEnabled);
                WpfTestHelpers.Pump();
                window.UpdateLayout();
                var image = new System.Windows.Media.Imaging.RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32);
                image.Render(window);
                var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
                encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(image));
                Directory.CreateDirectory(Path.GetFullPath(Path.Combine("artifacts", "screenshots")));
                using (var screenshot = File.Create(Path.GetFullPath("artifacts/screenshots/vocabulary-actions.png"))) encoder.Save(screenshot);
                string unchanged = File.ReadAllText(vocabulary);
                File.AppendAllText(vocabulary, "\n");
                Click(up);
                Program.Check(File.ReadAllText(vocabulary) == unchanged + "\n");
                Program.Check(list.SelectedIndex == 1 && window.ViewModel.Words.Select(word => word.Word).SequenceEqual(expected));
                File.WriteAllText(vocabulary, unchanged);
                string removed = ((ReadArticles.ViewModels.VocabularyEntryViewModel)list.SelectedItem).Word;
                Click(delete);
                expected = expected.Where(word => word != removed).ToArray();
                CheckOrder();
                Program.Check(list.SelectedIndex == -1 && actions.Visibility == Visibility.Collapsed);
                list.SelectedIndex = 0;
                Click(delete);
                expected = expected.Skip(1).ToArray();
                CheckOrder();
                list.SelectedIndex = 0;
                Program.Check(!up.IsEnabled && !down.IsEnabled);
                Click(delete);
                expected = [];
                CheckOrder();
                Program.Check(actions.Visibility == Visibility.Collapsed);

                void CheckOrder()
                {
                    Program.Check(window.ViewModel.Words.Select(word => word.Word).SequenceEqual(expected));
                    var saved = new VocabularyRepository().Load(vocabulary);
                    Program.Check(saved.CanWrite && saved.Data.Words.Select(word => word.Word).SequenceEqual(expected));
                }
            }
            finally
            {
                window?.Close();
                if (Directory.Exists(root)) Directory.Delete(root, true);
            }
        });
    }
    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
}
