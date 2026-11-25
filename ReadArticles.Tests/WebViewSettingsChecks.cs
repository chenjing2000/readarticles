using System.Reflection;
using System.Text.Json;
using System.Windows.Controls;
using ReadArticles.Infrastructure;
using ReadArticles.Layout;

internal static class WebViewSettingsChecks
{
    internal static void Run()
    {
        Program.Run("pane layout absorbs changes in article first and restores a cancelled drag", () =>
        {
            var layout = new PaneLayout();
            Program.Check(layout.LeftWidth == 250 && layout.RightWidth == 300);
            Program.Check(layout.ResizeLibrary(400, 750));
            Program.Check(layout.LeftWidth == 350 && layout.RightWidth == 200);
            Program.Check(!layout.ResizeLibrary(200, 750));
            Program.Check(layout.LeftWidth == 200 && layout.RightWidth == 200);
            Program.Check(layout.DragVocabulary(400, 750));
            Program.Check(layout.LeftWidth == 200 && layout.RightWidth == 350);
            layout.DragVocabulary(300, 750);
            Program.Check(layout.CancelVocabularyDrag(750));
            Program.Check(layout.LeftWidth == 200 && layout.RightWidth == 200);
            layout.DragVocabulary(350, 750);
            layout.EndVocabularyDrag();
            Program.Check(!layout.CancelVocabularyDrag(750) && layout.RightWidth == 350);
            layout.Fit(600);
            Program.Check(layout.LeftWidth == 200 && layout.RightWidth == 200);
            layout.Fit(1000);
            Program.Check(layout.LeftWidth == 200 && layout.RightWidth == 200);
        });

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

        Program.Run("browser accepts only session pane-width intent and receives typed appearance snapshot", () =>
        {
            var window = new ReadArticles.MainWindow(false);
            try
            {
                var type = typeof(ReadArticles.MainWindow);
                var selected = (PaneLayout)type.GetField("paneLayout",
                    BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
                var handler = type.GetMethod("WebPilotSettingIntent",
                    BindingFlags.Instance | BindingFlags.NonPublic)!;
                var center = (ColumnDefinition)window.FindName("CenterColumn");
                Program.Check(center.MinWidth == 403);
                Apply(300);
                Program.Check(selected.RightWidth == 300);
                Apply(199);
                Program.Check(selected.RightWidth == 300);
                Apply(401);
                Program.Check(selected.RightWidth == 300);
                Apply(200);
                Program.Check(selected.RightWidth == 200);
                Apply(400);
                Program.Check(selected.RightWidth >= 200 && selected.RightWidth <= 400);
                var method = type.GetMethod("WebPilotSettingsSnapshot",
                    BindingFlags.Instance | BindingFlags.NonPublic)!;
                using var snap = JsonDocument.Parse(JsonSerializer.Serialize(method.Invoke(window, null)));
                var root = snap.RootElement;
                Program.Check(root.GetProperty("rightWidth").GetDouble() == selected.RightWidth &&
                    root.GetProperty("appearance").GetProperty("title").GetProperty("size").GetDouble() == 18 &&
                    root.GetProperty("appearance").GetProperty("title").GetProperty("lineHeight").GetDouble() == 1.7 &&
                    root.GetProperty("appearance").GetProperty("body").GetProperty("lineHeight").GetDouble() == 1.7 &&
                    root.GetProperty("appearance").GetProperty("body").GetProperty("scriptColor").GetString() == "#B64550" &&
                    root.GetProperty("appearance").GetProperty("body").GetProperty("selectionAddSize").GetDouble() == 0.65 &&
                    root.GetProperty("appearance").GetProperty("body").GetProperty("selectionBackground").GetString() == "#DDE8F2" &&
                    root.GetProperty("appearance").GetProperty("meanings").GetProperty("lineHeight").GetDouble() == 1.5 &&
                    root.GetProperty("appearance").GetProperty("body").GetProperty("paragraphAfter").GetDouble() == 16 &&
                    root.GetProperty("appearance").GetProperty("phonetics").GetProperty("size").GetDouble() == 14 &&
                    root.GetProperty("appearance").GetProperty("phonetics").GetProperty("color").GetString() == "#0A7785");
                Program.Check(!root.TryGetProperty("fontSize", out _));

                void Apply(double width)
                {
                    using var data = JsonDocument.Parse("{\"payload\":{\"width\":" +
                        width.ToString(System.Globalization.CultureInfo.InvariantCulture) + "}}");
                    handler.Invoke(window, [data.RootElement,
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

        Program.Run("structured appearance persists while session widths reset after serialization", () =>
        {
            var initial = new ReaderSettings { Accent = "us", ForwardStep = 7 };
            initial.Appearance.Body.Size = 17;
            initial.Appearance.Body.Background = "#F7F1E6";
            var layout = new PaneLayout();
            layout.ResizeLibrary(320, 1000);
            layout.ResizeVocabulary(390, 1000);
            Program.Check(layout.LeftWidth == 320 && layout.RightWidth == 390);
            string serialized = JsonSerializer.Serialize(initial);
            Program.Check(!serialized.Contains("LeftWidth", StringComparison.Ordinal) &&
                !serialized.Contains("RightWidth", StringComparison.Ordinal) &&
                !serialized.Contains("\"FontSize\"", StringComparison.Ordinal));
            // Legacy width fields must remain ignored on deserialization.
            var legacy = serialized.Insert(1, "\"LeftWidth\":320,\"RightWidth\":390,");
            var loaded = JsonSerializer.Deserialize<ReaderSettings>(legacy)!;
            var fresh = new PaneLayout();
            Program.Check(fresh.LeftWidth == 250 && fresh.RightWidth == 300 &&
                loaded.Appearance.Body.Size == 17 && loaded.Accent == "us" &&
                loaded.ForwardStep == 7 && loaded.Appearance.Body.Background == "#F7F1E6");
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
