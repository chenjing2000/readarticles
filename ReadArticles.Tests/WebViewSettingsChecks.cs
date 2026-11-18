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
