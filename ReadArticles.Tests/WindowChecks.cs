using System.Diagnostics;
using System.Windows;
using System.Windows.Documents;

internal static class WindowChecks
{
    internal static void Run()
    {
        Program.Run("loaded paragraph hit-testing preserves each owning SID", () =>
        {
            WithReader(reader =>
            {
                foreach (var range in reader.Ranges)
                {
                    var character = reader.PositionAt(range, 3);
                    Rect rect = character.GetCharacterRect(LogicalDirection.Forward);
                    Program.Check(!rect.IsEmpty && rect.Height > 0);
                    var pointer = reader.GetPositionFromPoint(new Point(rect.Left + 1, rect.Top + rect.Height / 2), false);
                    Program.Check(pointer != null && reader.FindSegment(pointer)?.Segment.Sid == range.Segment.Sid);
                }
            });
        });
        Program.Run("loaded selection action emits text and hides on invalid selection", () =>
        {
            WithReader(reader =>
            {
                var range = reader.Ranges[0];
                reader.Selection.Select(reader.PositionAt(range, 0), reader.PositionAt(range, 6));
                WpfTestHelpers.Pump();
                Program.Check(reader.IsSelectionAddVisible);
                ReadArticles.Models.PassageSelection? received = null;
                reader.SelectionRequested += selection => received = selection;
                Program.Check(reader.RequestSelectionAdd() && received?.Text == "public");
                reader.Selection.Select(reader.Ranges[0].Start, reader.Ranges[1].End);
                WpfTestHelpers.Pump();
                Program.Check(!reader.IsSelectionAddVisible);
            });
        });
        Program.Run("floating notifications do not shrink reader panes or playback toolbar", () =>
        {
            var window = new ReadArticles.MainWindow(false) { Left = -10000, Top = -10000, ShowActivated = false };
            try
            {
                window.Show();
                var layout = (System.Windows.Controls.Grid)window.Content;
                var panels = layout.Children.OfType<FrameworkElement>()
                    .Where(child => System.Windows.Controls.Grid.GetRow(child) == 0 &&
                        child is not System.Windows.Controls.GridSplitter).ToArray();
                Program.Check(panels.Length == 3 && layout.RowDefinitions.Count == 1);
                WpfTestHelpers.Pump();
                window.UpdateLayout();
                var before = panels.Select(panel => panel.ActualHeight).ToArray();
                var notifications = window.ViewModel.Notifications;
                notifications.Publish("test-info", "Notification test", ReadArticles.Infrastructure.NotificationSeverity.Info);
                notifications.Publish("test-error", "Persistent test error", ReadArticles.Infrastructure.NotificationSeverity.Error);
                WpfTestHelpers.Pump();
                window.UpdateLayout();
                Program.Check(notifications.Visible.Count == 2);
                Program.Check(panels.Select((panel, index) => Math.Abs(panel.ActualHeight - before[index]) < 0.01).All(x => x));
                Program.Check(window.FindName("NotificationHost") is ReadArticles.Controls.ToastHost);
                notifications.Dismiss(notifications.Messages[0].Id);
                Program.Check(notifications.Visible.Count == 1 &&
                    notifications.Visible[0].Severity == ReadArticles.Infrastructure.NotificationSeverity.Error);
            }
            finally { window.Close(); WpfTestHelpers.Pump(); }
        });
    }

    private static void WithReader(Action<ReadArticles.Rendering.ParagraphReader> check)
    {
        var reader = TestData.Reader();
        var window = new Window
        {
            Content = reader, Width = 600, Height = 300,
            Left = -10000, Top = -10000, ShowActivated = false
        };
        try
        {
            window.Show();
            window.UpdateLayout();
            WpfTestHelpers.Pump();
            check(reader);
        }
        finally { window.Close(); WpfTestHelpers.Pump(); }
    }
}
