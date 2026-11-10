using ReadArticles.Infrastructure;

internal static class NotificationChecks
{
    internal static void Run()
    {
        Program.Run("notifications classify severity and display newest at top", () =>
        {
            var service = new NotificationService();
            service.Publish("info", "Connected", NotificationSeverity.Info);
            service.Publish("warning", "Missing sentence recording", NotificationSeverity.Warning);
            service.Publish("error", "Invalid passage", NotificationSeverity.Error);
            Program.Check(service.Visible.Count == 3);
            Program.Check(service.Visible[0].Severity == NotificationSeverity.Error);
            Program.Check(service.Visible[2].Severity == NotificationSeverity.Info);
        });
        Program.Run("visible notifications are newest-first while history remains chronological", () =>
        {
            var service = new NotificationService();
            var first = service.Publish("one", "First", NotificationSeverity.Info)!;
            var second = service.Publish("two", "Second", NotificationSeverity.Warning)!;
            var third = service.Publish("three", "Third", NotificationSeverity.Info)!;
            var fourth = service.Publish("four", "Fourth", NotificationSeverity.Info)!;
            var fifth = service.Publish("five", "Fifth", NotificationSeverity.Info)!;
            Program.Check(service.Messages.Select(n => n.Id).SequenceEqual(
                new[] { first.Id, second.Id, third.Id, fourth.Id, fifth.Id }));
            Program.Check(service.Visible.Select(n => n.Id).SequenceEqual(
                new[] { fifth.Id, fourth.Id, third.Id, second.Id }));
        });
        Program.Run("duplicate transient warnings coalesce", () =>
        {
            var service = new NotificationService();
            var first = service.Publish("missing-sentence", "No recording", NotificationSeverity.Warning);
            var second = service.Publish("missing-sentence", "No recording", NotificationSeverity.Warning);
            Program.Check(first?.Id == second?.Id && service.Messages.Count == 1);
        });
        Program.Run("overflow never discards an unacknowledged error", () =>
        {
            var service = new NotificationService();
            for (int i = 0; i < 9; i++)
                service.Publish("error-" + i, "Failure " + i, NotificationSeverity.Error);
            Program.Check(service.Messages.Count == 9);
            Program.Check(service.Visible.Count == 4 && service.OverflowErrors.Count == 5);
            var oldest = service.Messages[0];
            Program.Check(service.OverflowErrors.Any(n => n.Id == oldest.Id));
            Program.Check(service.Dismiss(oldest.Id));
            Program.Check(service.Messages.Count == 8 && service.OverflowErrors.Count == 4);
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
        Program.Run("transient deduplication requires identical key message and severity", () =>
        {
            var service = new NotificationService();
            var first = service.Publish("audio", "Missing UK audio", NotificationSeverity.Warning);
            var duplicate = service.Publish("audio", "Missing UK audio", NotificationSeverity.Warning);
            var differentMessage = service.Publish("audio", "Missing US audio", NotificationSeverity.Warning);
            var differentSeverity = service.Publish("audio", "Missing UK audio", NotificationSeverity.Info);
            Program.Check(first?.Id == duplicate?.Id);
            Program.Check(differentMessage?.Id != first?.Id && differentSeverity?.Id != first?.Id);
            Program.Check(service.Messages.Count == 3);
        });
        Program.Run("transient queue eviction prefers oldest information", () =>
        {
            var service = new NotificationService();
            service.Publish("warn", "Warning", NotificationSeverity.Warning);
            for (int i = 0; i < 5; i++) service.Publish("info" + i, "Info", NotificationSeverity.Info);
            Program.Check(service.Messages.Count == 4);
            Program.Check(service.Messages.Any(m => m.Key == "warn"));
            Program.Check(!service.Messages.Any(m => m.Key == "info0"));
        });
    }
}
