using ReadArticles.Infrastructure;

internal static class NotificationChecks
{
    internal static void Run()
    {
        Program.Run("notifications classify severity and display newest at top", () =>
        {
            var service = new NotificationService();
            service.Publish("info", "Connected", NotificationSeverity.Info);
            service.Publish("warning", "Missing recording", NotificationSeverity.Warning);
            service.Publish("error", "Invalid passage", NotificationSeverity.Error);
            Program.Check(service.Visible.Count == 3);
            Program.Check(service.Visible[0].Severity == NotificationSeverity.Error);
            Program.Check(service.Visible[2].Severity == NotificationSeverity.Info);
        });

        Program.Run("only three notifications are displayed; others wait FIFO without eviction", () =>
        {
            var service = new NotificationService();
            var items = Enumerable.Range(1, 5)
                .Select(i => service.Publish("notice-" + i, "Message " + i,
                    NotificationSeverity.Info)!).ToArray();
            Program.Check(service.Messages.Count == 5);
            Program.Check(service.Visible.Select(n => n.Id).SequenceEqual(
                new[] { items[2].Id, items[1].Id, items[0].Id }));
            Program.Check(!service.Visible.Any(n => n.Id == items[3].Id || n.Id == items[4].Id));
            Program.Check(service.Dismiss(items[0].Id));
            Program.Check(service.Visible.Select(n => n.Id).SequenceEqual(
                new[] { items[3].Id, items[2].Id, items[1].Id }));
            Program.Check(service.Dismiss(items[1].Id));
            Program.Check(service.Visible.Select(n => n.Id).SequenceEqual(
                new[] { items[4].Id, items[3].Id, items[2].Id }));
        });

        Program.Run("repeated warnings are queued as distinct notifications", () =>
        {
            var service = new NotificationService();
            var first = service.Publish("missing-sentence", "No recording", NotificationSeverity.Warning);
            var second = service.Publish("missing-sentence", "No recording", NotificationSeverity.Warning);
            Program.Check(first?.Id != second?.Id && service.Messages.Count == 2);
        });

        Program.Run("errors remain queued and no message is silently discarded", () =>
        {
            var service = new NotificationService();
            for (int i = 0; i < 9; i++)
                service.Publish("error-" + i, "Failure " + i, NotificationSeverity.Error);
            Program.Check(service.Messages.Count == 9 && service.Visible.Count == 3);
            var first = service.Messages[0];
            var fourth = service.Messages[3];
            Program.Check(!service.Visible.Any(n => n.Id == fourth.Id));
            Program.Check(service.Dismiss(first.Id));
            Program.Check(service.Messages.Count == 8 && service.Visible.Any(n => n.Id == fourth.Id));
        });

        Program.Run("errors are never coalesced and clear publishes one state change", () =>
        {
            var service = new NotificationService();
            int changes = 0;
            service.Changed += () => changes++;
            var first = service.Publish("same-error", "The same failure", NotificationSeverity.Error);
            var second = service.Publish("same-error", "The same failure", NotificationSeverity.Error);
            Program.Check(first is not null && second is not null && first.Id != second.Id);
            Program.Check(service.Messages.Count == 2 && changes == 2);
            service.Clear();
            Program.Check(service.Messages.Count == 0 && changes == 3);
        });

        Program.Run("all notice types preserve arrival order even when keys repeat", () =>
        {
            var service = new NotificationService();
            var first = service.Publish("audio", "Missing UK audio", NotificationSeverity.Warning);
            var repeated = service.Publish("audio", "Missing UK audio", NotificationSeverity.Warning);
            var differentMessage = service.Publish("audio", "Missing US audio", NotificationSeverity.Warning);
            var differentSeverity = service.Publish("audio", "Missing UK audio", NotificationSeverity.Info);
            Program.Check(first is not null && repeated is not null && first.Id != repeated.Id);
            Program.Check(differentMessage?.Id != first.Id && differentSeverity?.Id != first.Id);
            Program.Check(service.Messages.Count == 4);
            Program.Check(service.Visible.Select(n => n.Id).SequenceEqual(
                new[] { differentMessage!.Id, repeated.Id, first.Id }));
        });

        Program.Run("ordinary notifications are queued without trimming or priority eviction", () =>
        {
            var service = new NotificationService();
            var warning = service.Publish("warn", "Warning", NotificationSeverity.Warning)!;
            for (int i = 0; i < 5; i++)
                service.Publish("info" + i, "Info", NotificationSeverity.Info);
            Program.Check(service.Messages.Count == 6);
            Program.Check(service.Messages[0].Id == warning.Id);
            Program.Check(service.Visible.Count == 3);
            Program.Check(service.Visible[2].Id == warning.Id);
        });
    }
}
