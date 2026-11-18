using System.Reflection;
using System.Text.Json;
using ReadArticles.Infrastructure;

internal static class WebViewSettingsChecks
{
    internal static void Run()
    {
        Program.Run("highlight toggles do not simulate vocabulary edits or advance its revision", () =>
        {
            var window = new ReadArticles.MainWindow(false);
            try
            {
                int vocabularyEvents = 0;
                window.ViewModel.VocabularyChanged += () => vocabularyEvents++;
                window.ViewModel.HighlightsVisible = true;
                window.ViewModel.HighlightsVisible = true;
                window.ViewModel.HighlightsVisible = false;
                var revision = typeof(ReadArticles.MainWindow).GetField(
                    "webPilotVocabularyRevision", BindingFlags.Instance | BindingFlags.NonPublic)!;
                Program.Check(vocabularyEvents == 0 && (int)revision.GetValue(window)! == 0);
            }
            finally { window.Close(); }
        });

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

        Program.Run("WebView2 notification snapshot only admits three FIFO messages", () =>
        {
            var window = new ReadArticles.MainWindow(false);
            try
            {
                var notifications = window.ViewModel.Notifications;
                var posted = Enumerable.Range(0, 5)
                    .Select(index => notifications.Publish("error-" + index,
                        "Retained error " + index, NotificationSeverity.Error)!).ToArray();
                var method = typeof(ReadArticles.MainWindow).GetMethod(
                    "WebPilotNotificationSnapshot", BindingFlags.Instance | BindingFlags.NonPublic);
                Program.Check(method is not null);
                JsonElement Snapshot()
                {
                    using var json = JsonDocument.Parse(JsonSerializer.Serialize(
                        method!.Invoke(window, null)));
                    return json.RootElement.Clone();
                }
                var first = Snapshot();
                Program.Check(first.GetProperty("visible").GetArrayLength() == 3);
                Program.Check(first.GetProperty("visible")[2].GetProperty("id").GetInt64() == posted[0].Id);
                Program.Check(!first.TryGetProperty("errors", out _) &&
                    !first.TryGetProperty("overflowErrors", out _));
                Program.Check(notifications.Messages.Count == 5);
                notifications.Dismiss(posted[0].Id);
                var second = Snapshot();
                Program.Check(second.GetProperty("visible")[0].GetProperty("id").GetInt64() == posted[3].Id);
            }
            finally { window.Close(); }
        });

        Program.Run("pane widths reset on restart while unrelated reading settings persist", () =>
        {
            var restored = JsonSerializer.Deserialize<ReaderSettings>(
                """
                {"LeftWidth":720,"RightWidth":850,"FontSize":16,"Accent":"us","ForwardStep":7}
                """)!;
            Program.Check(restored.LeftWidth == 310 && restored.RightWidth == 390);
            Program.Check(restored.FontSize == 16 && restored.Accent == "us" &&
                restored.ForwardStep == 7);
            restored.LeftWidth = 540;
            restored.RightWidth = 620;
            string saved = JsonSerializer.Serialize(restored);
            Program.Check(!saved.Contains("LeftWidth", StringComparison.Ordinal) &&
                !saved.Contains("RightWidth", StringComparison.Ordinal));
            Program.Check(JsonSerializer.Deserialize<ReaderSettings>(saved)!.RightWidth == 390);
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
