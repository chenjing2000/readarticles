using System.Reflection;
using System.Text.Json;
using ReadArticles.Infrastructure;

internal static class WebViewSettingsChecks
{
    internal static void Run()
    {
        Program.Run("WebView2 settings intents accept only supported values", () =>
        {
            var window = new ReadArticles.MainWindow(false);
            try
            {
                var type = typeof(ReadArticles.MainWindow);
                var settings = (ReaderSettings)type.GetField("settings",
                    BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
                var handle = type.GetMethod("WebPilotSettingIntent",
                    BindingFlags.Instance | BindingFlags.NonPublic)!;
                Apply("setFontSize", """{"size":16}""");
                Program.Check(settings.FontSize == 16);
                Apply("setFontSize", """{"size":40}""");
                Program.Check(settings.FontSize == 16);
                Apply("setAccent", """{"accent":"us"}""");
                Program.Check(window.ViewModel.Accent == "us");
                Apply("setAccent", """{"accent":"invalid"}""");
                Program.Check(window.ViewModel.Accent == "us");
                Apply("setPanelWidth", """{"width":360}""");
                Program.Check(settings.RightWidth == 360);
                Apply("setPanelWidth", """{"width":-42}""");
                Program.Check(settings.RightWidth == 360);
                var getSnapshot = type.GetMethod("WebPilotSettingsSnapshot",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                Program.Check(getSnapshot is not null);
                using (var settingsJson = JsonDocument.Parse(JsonSerializer.Serialize(
                    getSnapshot!.Invoke(window, null))))
                    Program.Check(settingsJson.RootElement.GetProperty("rightWidth").GetDouble() == 360);
                Apply("setForwardStep", """{"seconds":7}""");
                Apply("setBackwardStep", """{"seconds":8}""");
                Program.Check(settings.ForwardStep == 7 && settings.BackwardStep == 8);
                Apply("setForwardStep", """{"seconds":99}""");
                Program.Check(settings.ForwardStep == 7);

                void Apply(string command, string payload)
                {
                    using var data = JsonDocument.Parse("{\"payload\":" + payload + "}");
                    handle.Invoke(window, [command, data.RootElement,
                        "00000000-0000-4000-8000-000000000001"]);
                }
            }
            finally { window.Close(); }
        });

        Program.Run("WebView2 faded errors remain accessible in notification snapshots", () =>
        {
            var window = new ReadArticles.MainWindow(false);
            try
            {
                for (int index = 0; index < 5; index++)
                    window.ViewModel.Notifications.Publish("error-" + index,
                        "Retained error " + index, NotificationSeverity.Error);
                var method = typeof(ReadArticles.MainWindow).GetMethod(
                    "WebPilotNotificationSnapshot", BindingFlags.Instance | BindingFlags.NonPublic);
                Program.Check(method is not null);
                using var snapshot = JsonDocument.Parse(JsonSerializer.Serialize(
                    method!.Invoke(window, null)));
                Program.Check(snapshot.RootElement.GetProperty("visible").GetArrayLength() == 4);
                Program.Check(snapshot.RootElement.GetProperty("errors").GetArrayLength() == 5);
                Program.Check(snapshot.RootElement.GetProperty("overflowErrors").GetArrayLength() == 1);
            }
            finally { window.Close(); }
        });

        Program.Run("WebView2 notification dismiss retains errors until acknowledged", () =>
        {
            var window = new ReadArticles.MainWindow(false);
            try
            {
                var service = window.ViewModel.Notifications;
                var warning = service.Publish("warn", "Temporary warning",
                    NotificationSeverity.Warning)!;
                var error = service.Publish("error", "Persistent error",
                    NotificationSeverity.Error)!;
                var dismiss = typeof(ReadArticles.MainWindow).GetMethod(
                    "WebPilotDismissNotification", BindingFlags.Instance | BindingFlags.NonPublic)!;
                Program.Check(service.Messages.Count == 2);
                Apply(warning.Id);
                Program.Check(service.Messages.Count == 1 &&
                    service.Messages[0].Id == error.Id);
                Apply(error.Id);
                Program.Check(service.Messages.Count == 0);

                void Apply(long id)
                {
                    using var data = JsonDocument.Parse("{\"payload\":{\"id\":" +
                        id.ToString(System.Globalization.CultureInfo.InvariantCulture) + "}}");
                    dismiss.Invoke(window, [data.RootElement,
                        "00000000-0000-4000-8000-000000000002"]);
                }
            }
            finally { window.Close(); }
        });
    }
}
