using ReadArticles.Infrastructure;

internal static class NotificationChecks
{
    internal static void Run()
    {
        Program.Run("notifications classify severity and display newest at bottom", () =>
        {
            var service = new NotificationService();
            service.Publish("info", "Connected", NotificationSeverity.Info);
            service.Publish("warning", "Missing sentence recording", NotificationSeverity.Warning);
            service.Publish("error", "Invalid passage", NotificationSeverity.Error);
            Program.Check(service.Visible.Count == 3);
            Program.Check(service.Visible[0].Severity == NotificationSeverity.Info);
            Program.Check(service.Visible[2].Severity == NotificationSeverity.Error);
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
