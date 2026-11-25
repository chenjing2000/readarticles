using System.Reflection;
using System.Text.Json;
using System.Windows.Controls;
using ReadArticles.Infrastructure;
using ReadArticles.Layout;
using ReadArticles.Presentation;

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

        Program.Run("reader bridge rejects stale identities, repeated requests and actions after failure", () =>
        {
            var window = new ReadArticles.MainWindow(false);
            try
            {
                var bridge = Bridge(window);
                var host = (WebReaderHost)typeof(ReadArticles.MainWindow)
                    .GetField("webReaderHost", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
                string page = Guid.NewGuid().ToString("D");
                string otherPage = Guid.NewGuid().ToString("D");
                string session = (string)typeof(WebReaderBridge)
                    .GetField("currentSessionId", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(bridge)!;
                string request = Guid.NewGuid().ToString("D");
                void Highlight(bool enabled, string pageId, string sessionId, string requestId, int version = 1, int revision = 0) =>
                    bridge.Receive(JsonSerializer.Serialize(new
                    {
                        version, type = "setHighlight", pageId, sessionId, requestId,
                        vocabularyRevision = revision, payload = new { enabled }
                    }));

                Highlight(true, page, session, Guid.NewGuid().ToString("D"));
                Program.Check(!window.ViewModel.HighlightsVisible); // not ready
                bridge.Receive(JsonSerializer.Serialize(new { version = 1, type = "ready", pageId = page }));
                Highlight(true, page, session, Guid.NewGuid().ToString("D"), version: 2);
                Highlight(true, otherPage, session, Guid.NewGuid().ToString("D"));
                Highlight(true, page, "stale-session", Guid.NewGuid().ToString("D"));
                Highlight(true, page, session, Guid.NewGuid().ToString("D"), revision: 1);
                Program.Check(!window.ViewModel.HighlightsVisible);
                Highlight(true, page, session, request);
                Program.Check(window.ViewModel.HighlightsVisible);
                Highlight(false, page, session, request);
                Program.Check(window.ViewModel.HighlightsVisible); // duplicate request
                Highlight(false, page, session, Guid.NewGuid().ToString("D"));
                Program.Check(!window.ViewModel.HighlightsVisible);
                host.Fail("test failure");
                Highlight(true, page, session, Guid.NewGuid().ToString("D"));
                Program.Check(!window.ViewModel.HighlightsVisible && host.IsUnavailable);
            }
            finally { window.Close(); }
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
                Program.Check(vocabularyEvents == 0 && Bridge(window).VocabularyRevision == 0);
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
                var bridge = Bridge(window);
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
                using var snap = JsonDocument.Parse(JsonSerializer.Serialize(bridge.SettingsSnapshot()));
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
                    bridge.HandlePaneWidth(data.RootElement, "00000000-0000-4000-8000-000000000001");
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
                var bridge = Bridge(window);
                JsonElement Snapshot()
                {
                    using var json = JsonDocument.Parse(JsonSerializer.Serialize(
                        bridge.NotificationSnapshot()));
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
                var bridge = Bridge(window);
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
                    bridge.DismissNotification(data.RootElement, "00000000-0000-4000-8000-000000000002");
                }
            }
            finally { window.Close(); }
        });
    }
    private static WebReaderBridge Bridge(ReadArticles.MainWindow window) =>
        (WebReaderBridge)typeof(ReadArticles.MainWindow)
            .GetField("webReaderBridge", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
}
