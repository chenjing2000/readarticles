using System.Diagnostics;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Automation;

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
        Program.Run("toast hover pauses expiry, transient fades, and errors persist until closed", () =>
        {
            var window = new ReadArticles.MainWindow(false)
            {
                Left = -10000, Top = -10000, ShowActivated = false
            };
            try
            {
                window.Show();
                WpfTestHelpers.Pump();
                var service = window.ViewModel.Notifications;
                var host = (ReadArticles.Controls.ToastHost)window.FindName("NotificationHost");
                var transient = service.Publish("toast-hover-test", "Hover pause test",
                    ReadArticles.Infrastructure.NotificationSeverity.Info)!;
                WpfTestHelpers.Pump(4000);
                var card = FindToastCard(host, "Hover pause test");
                Program.Check(card is not null);
                card!.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, Environment.TickCount)
                    { RoutedEvent = UIElement.MouseEnterEvent });
                WpfTestHelpers.Pump(1200);
                Program.Check(service.Messages.Any(n => n.Id == transient.Id));
                card.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, Environment.TickCount)
                    { RoutedEvent = UIElement.MouseLeaveEvent });
                WpfTestHelpers.Pump(300);
                Program.Check(service.Messages.Any(n => n.Id == transient.Id));
                WpfTestHelpers.Pump(1000);
                Program.Check(!service.Messages.Any(n => n.Id == transient.Id));

                var error = service.Publish("toast-error-test", "Persistent error test",
                    ReadArticles.Infrastructure.NotificationSeverity.Error)!;
                WpfTestHelpers.Pump(5000);
                Program.Check(service.Messages.Any(n => n.Id == error.Id));
                var errorCard = FindToastCard(host, "Persistent error test");
                Program.Check(errorCard is not null);
                var close = WpfTestHelpers.Descendants(errorCard!)
                    .OfType<Button>().Single(button =>
                        AutomationProperties.GetName(button) == "关闭消息");
                close.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                WpfTestHelpers.Wait(() => !service.Messages.Any(n => n.Id == error.Id));
            }
            finally { window.Close(); WpfTestHelpers.Pump(); }
        });

        Program.Run("toast cards use newest-first order fixed two-DIP gaps and 360-DIP width", () =>
        {
            var window = new ReadArticles.MainWindow(false)
                { Left = -10000, Top = -10000, ShowActivated = false };
            try
            {
                window.Show();
                WpfTestHelpers.Pump();
                var service = window.ViewModel.Notifications;
                var host = (ReadArticles.Controls.ToastHost)window.FindName("NotificationHost");
                service.Publish("layout-1", "First toast", ReadArticles.Infrastructure.NotificationSeverity.Info);
                service.Publish("layout-2", "Second toast", ReadArticles.Infrastructure.NotificationSeverity.Warning);
                service.Publish("layout-3", "Third toast", ReadArticles.Infrastructure.NotificationSeverity.Error);
                WpfTestHelpers.Pump(350);
                host.UpdateLayout();
                var first = FindToastCard(host, "First toast")!;
                var second = FindToastCard(host, "Second toast")!;
                var third = FindToastCard(host, "Third toast")!;
                Program.Check(first is not null && second is not null && third is not null);
                Program.Check(Math.Abs(first.Width - 360) < 0.01 &&
                    Math.Abs(second.Width - 360) < 0.01 && Math.Abs(third.Width - 360) < 0.01);
                Program.Check(Math.Abs(third.Margin.Bottom - 2) < 0.01);
                Program.Check(Math.Abs(second.Margin.Bottom - 2) < 0.01);
                Program.Check(Math.Abs(first.Margin.Bottom) < 0.01);
                double firstY = first.TranslatePoint(new Point(), host).Y;
                double secondY = second.TranslatePoint(new Point(), host).Y;
                double thirdY = third.TranslatePoint(new Point(), host).Y;
                Program.Check(thirdY < secondY && secondY < firstY);
                Program.Check(Math.Abs((secondY - thirdY) - (third.ActualHeight + 2)) < 1.5);
                Program.Check(Math.Abs((firstY - secondY) - (second.ActualHeight + 2)) < 1.5);
                Program.Check(Math.Abs(firstY + first.ActualHeight - host.ActualHeight) < 1.5);
            }
            finally { window.Close(); WpfTestHelpers.Pump(); }
        });

        Program.Run("toast enters from above and exits downward", () =>
        {
            var window = new ReadArticles.MainWindow(false)
                { Left = -10000, Top = -10000, ShowActivated = false };
            try
            {
                window.Show();
                WpfTestHelpers.Pump();
                var service = window.ViewModel.Notifications;
                var host = (ReadArticles.Controls.ToastHost)window.FindName("NotificationHost");
                var item = service.Publish("motion-test", "Motion direction test",
                    ReadArticles.Infrastructure.NotificationSeverity.Info)!;
                WpfTestHelpers.Pump(60);
                var card = FindToastCard(host, "Motion direction test")!;
                Program.Check(card is not null);
                var transform = (TranslateTransform)card.RenderTransform;
                Program.Check(transform.Y < 0);
                Program.Check(card.Opacity < 1);
                WpfTestHelpers.Pump(300);
                Program.Check(Math.Abs(transform.Y) < 0.1 && Math.Abs(card.Opacity - 1) < 0.01);
                var close = WpfTestHelpers.Descendants(card)
                    .OfType<Button>().Single(button => AutomationProperties.GetName(button) == "关闭消息");
                close.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                WpfTestHelpers.Pump(60);
                Program.Check(transform.Y > 0 && card.Opacity < 1);
                WpfTestHelpers.Wait(() => !service.Messages.Any(n => n.Id == item.Id));
            }
            finally { window.Close(); WpfTestHelpers.Pump(); }
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

    private static Border? FindToastCard(DependencyObject host, string message)
    {
        var text = WpfTestHelpers.Descendants(host).OfType<TextBlock>()
            .FirstOrDefault(item => item.Text == message);
        if (text is null) return null;
        DependencyObject? current = text;
        while (current is not null && current is not Border)
            current = VisualTreeHelper.GetParent(current);
        return current as Border;
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
