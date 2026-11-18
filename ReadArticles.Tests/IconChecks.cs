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
                UiIcons.Plus is ImageSource && UiIcons.Minus is ImageSource);
        });
        Program.Run("native window starts centered with the tree and one browser host", () =>
        {
            var window = new ReadArticles.MainWindow(false);
            try
            {
                var work = SystemParameters.WorkArea;
                Program.Check(Math.Abs(window.Width - Math.Round(Math.Round(work.Width) * 0.7)) < 1);
                Program.Check(Math.Abs(window.Height - Math.Round(Math.Round(work.Height) * 0.7)) < 1);
                Program.Check(window.FindName("LibraryTree") is TreeView &&
                    window.FindName("ReaderWebView") is not null &&
                    window.FindName("VocabularyList") is null);
            }
            finally { window.Close(); }
        });
    }
}
