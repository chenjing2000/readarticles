using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ReadArticles.Infrastructure;

internal static class IconChecks
{
    internal static void Run()
    {
        Program.Run("native window and tree icons remain embedded", () =>
        {
            Program.Check(UiIcons.AppIcon is ImageSource &&
                UiIcons.Plus is ImageSource && UiIcons.Minus is ImageSource &&
                UiIcons.Settings is ImageSource && UiIcons.Refresh is ImageSource);
        });
        Program.Run("native window starts centered with the tree and one browser host", () =>
        {
            var window = new ReadArticles.MainWindow(false);
            try
            {
                var work = SystemParameters.WorkArea;
                double workWidth = Math.Round(work.Width);
                double expectedWidth = Math.Min(workWidth,
                    Math.Max(Math.Round(workWidth * 0.7), 250 + 3 + 200 + 3 + 300 + 40));
                Program.Check(Math.Abs(window.Width - expectedWidth) < 1);
                Program.Check(Math.Abs(window.MinWidth -
                    Math.Min(200 + 3 + 200 + 3 + 200 + 40, expectedWidth)) < 1);
                Program.Check(Math.Abs(window.Left -
                    Math.Round(Math.Round(work.Left) + (workWidth - expectedWidth) / 2)) < 1);
                Program.Check(Math.Abs(window.Height - Math.Round(Math.Round(work.Height) * 0.7)) < 1);
                Program.Check(window.FindName("LibraryTree") is TreeView &&
                    window.FindName("ReaderWebView") is not null &&
                    window.FindName("VocabularyList") is null);
            }
            finally { window.Close(); }
        });
    }
}
