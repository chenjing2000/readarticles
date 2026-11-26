using System.IO;
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
                var seen = (HashSet<string>)typeof(WebReaderBridge)
                    .GetField("seenRequests", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(bridge)!;
                int count = seen.Count;
                for (int i = 0; i < 100; i++)
                    bridge.Receive(JsonSerializer.Serialize(new
                    {
                        version = 1, type = "dragPanelWidth", pageId = page, sessionId = session,
                        requestId = Guid.NewGuid().ToString("D"), vocabularyRevision = 0,
                        payload = new { width = 300 }
                    }));
                Program.Check(seen.Count == count);
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
                    root.GetProperty("appearance").GetProperty("body").GetProperty("scriptColor").GetString() == "#F6EDC2" &&
                    root.GetProperty("appearance").GetProperty("body").GetProperty("selectionAddSize").GetDouble() == 0.65 &&
                    root.GetProperty("appearance").GetProperty("body").GetProperty("selectionBackground").GetString() == "#DDE8F2" &&
                    root.GetProperty("appearance").GetProperty("meanings").GetProperty("lineHeight").GetDouble() == 1.5 &&
                    root.GetProperty("appearance").GetProperty("body").GetProperty("paragraphAfter").GetDouble() == 16 &&
                    root.GetProperty("appearance").GetProperty("phonetics").GetProperty("size").GetDouble() == 14 &&
                    root.GetProperty("appearance").GetProperty("phonetics").GetProperty("color").GetString() == "#0A7785");
                Program.Check(!root.TryGetProperty("fontSize", out _) &&
                    !root.TryGetProperty("forwardStep", out _) && !root.TryGetProperty("backwardStep", out _));

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

        Program.Run("current structured settings normalize values and keep pane widths session-only", () =>
        {
            string path = ReaderSettings.PathName;
            string backup = AppPaths.SettingsBackupFile;
            byte[]? old = File.Exists(path) ? File.ReadAllBytes(path) : null;
            byte[]? oldBackup = File.Exists(backup) ? File.ReadAllBytes(backup) : null;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, """{"Appearance":{"Title":{"Size":20},"Body":{}},"Audio":{}}""");
                byte[] missingFields = File.ReadAllBytes(path);
                var partial = ReaderSettings.Load(out var partialWarning);
                Program.Check(partialWarning == null && partial.Appearance.Title.Size == 20 &&
                    partial.Appearance.Title.ParagraphAfter == 16 && partial.Appearance.Body.ParagraphAfter == 16 &&
                    partial.Appearance.Title.LineHeight == 1.7 && partial.Appearance.Body.Size == 15 &&
                    partial.ForwardStep == 4 && partial.BackwardStep == 5 &&
                    File.ReadAllBytes(path).SequenceEqual(missingFields));
                File.WriteAllText(path, """{"Appearance":{"Title":{"ParagraphAfter":6},"Body":{"FirstLineIndent":0,"SelectionAddSize":0.4}},"Audio":{"ForwardStep":1,"BackwardStep":60}}""");
                byte[] explicitValues = File.ReadAllBytes(path);
                partial = ReaderSettings.Load(out partialWarning);
                Program.Check(partialWarning == null && partial.Appearance.Title.ParagraphAfter == 6 &&
                    partial.Appearance.Body.FirstLineIndent == 0 && partial.Appearance.Body.SelectionAddSize == 0.4 &&
                    partial.ForwardStep == 1 && partial.BackwardStep == 60 &&
                    File.ReadAllBytes(path).SequenceEqual(explicitValues));
                var invalid = new ReaderSettings
                {
                    Accent = "invalid", ForwardStep = 0, BackwardStep = 61
                };
                invalid.Appearance.Body.Size = 100;
                invalid.Appearance.Body.ParagraphAfter = -5;
                invalid.Appearance.Body.SelectionAddSize = 0.39;
                invalid.Appearance.Title.Font = "Segoe UI";
                invalid.Appearance.Body.Font = "Arial";
                invalid.Appearance.Vocabulary.Font = "Times New Roman";
                invalid.Appearance.Meanings.Font = "SimSun";
                invalid.Appearance.Body.SelectionBackground = "#BAD";
                invalid.Appearance.Title.LineHeight = 0.99;
                invalid.Appearance.Vocabulary.Color = "#BAD";
                JsonFiles.WriteAtomic(path, invalid);
                byte[] original = File.ReadAllBytes(path);
                var loaded = ReaderSettings.Load(out var warning);
                Program.Check(File.ReadAllBytes(path).SequenceEqual(original));
                var layout = new PaneLayout();
                Program.Check(warning == null && layout.LeftWidth == 250 && layout.RightWidth == 300);
                Program.Check(loaded.Appearance.Body.Size == 15 && loaded.Accent == "uk");
                Program.Check(loaded.ForwardStep == 4 && loaded.BackwardStep == 5);
                Program.Check(loaded.Appearance.Body.ParagraphAfter == 16 &&
                    loaded.Appearance.Title.LineHeight == 1.7 &&
                    loaded.Appearance.Body.LineHeight == 1.7 &&
                    loaded.Appearance.Body.ScriptColor == "#F6EDC2" &&
                    loaded.Appearance.Body.SelectionAddSize == 0.65 &&
                    loaded.Appearance.Title.Font == "Newsreader" &&
                    loaded.Appearance.Body.Font == "Newsreader" &&
                    loaded.Appearance.Vocabulary.Font == "Newsreader" &&
                    loaded.Appearance.Meanings.Font == "Source Han Serif SC" &&
                    loaded.Appearance.Body.SelectionBackground == "#DDE8F2" &&
                    loaded.Appearance.Vocabulary.Size == 16 &&
                    loaded.Appearance.Phonetics.Size == 14 &&
                    loaded.Appearance.Vocabulary.Color == "#B7394D");
                loaded.Appearance.Body.SelectionAddSize = 0.80;
                loaded.Appearance.Title.Font = "Source Serif 4";
                loaded.Appearance.Body.Font = "Nunito";
                loaded.Appearance.Vocabulary.Font = "Georgia";
                loaded.Appearance.Meanings.Font = "Microsoft YaHei";
                loaded.Appearance.Body.SelectionBackground = "#EBE5F2";
                loaded.Save();
                loaded = ReaderSettings.Load(out warning);
                Program.Check(warning == null &&
                    loaded.Appearance.Body.SelectionAddSize == 0.80 &&
                    loaded.Appearance.Title.Font == "Source Serif 4" &&
                    loaded.Appearance.Body.Font == "Nunito" &&
                    loaded.Appearance.Vocabulary.Font == "Georgia" &&
                    loaded.Appearance.Meanings.Font == "Microsoft YaHei" &&
                    loaded.Appearance.Body.SelectionBackground == "#EBE5F2");
                foreach (string retired in new[] { "KaiTi", "unknown" })
                {
                    loaded.Appearance.Meanings.Font = retired;
                    JsonFiles.WriteAtomic(path, loaded);
                    byte[] before = File.ReadAllBytes(path);
                    loaded = ReaderSettings.Load(out warning);
                    Program.Check(warning == null && loaded.Appearance.Meanings.Font == "Source Han Serif SC" &&
                        loaded.Appearance.Title.Font == "Source Serif 4" && File.ReadAllBytes(path).SequenceEqual(before));
                }
                foreach (var (oldColor, expected) in new[]
                {
                    ("#B64550", "#F6EDC2"), ("#A33D30", "#F6EDC2"), ("#E02020", "#F6EDC2"),
                    ("#A45A2C", "#F5E4D6"), ("#D65A00", "#F5E4D6"),
                    ("#53713C", "#E1EEDB"), ("#008A48", "#E1EEDB"),
                    ("#0066E6", "#E0ECF4"), ("#856300", "#ECE6F3"), ("#8a36d1", "#ECE6F3"),
                    ("#123456", "#F6EDC2")
                })
                {
                    loaded.Appearance.Body.ScriptColor = oldColor;
                    JsonFiles.WriteAtomic(path, loaded);
                    byte[] before = File.ReadAllBytes(path);
                    loaded = ReaderSettings.Load(out warning);
                    Program.Check(warning == null && loaded.Appearance.Body.ScriptColor == expected &&
                        loaded.Appearance.Body.Font == "Nunito" &&
                        loaded.Appearance.Body.SelectionBackground == "#EBE5F2" &&
                        File.ReadAllBytes(path).SequenceEqual(before));
                }
                foreach (string color in new[] { "#F6EDC2", "#F5E4D6", "#E1EEDB", "#E0ECF4", "#ECE6F3" })
                {
                    loaded.Appearance.Body.ScriptColor = color;
                    loaded.Save();
                    loaded = ReaderSettings.Load(out warning);
                    Program.Check(warning == null && loaded.Appearance.Body.ScriptColor == color);
                }
                foreach (double badSize in new[] { 0d, 9d, 41d, 100d, 15.5, double.NaN, double.PositiveInfinity })
                {
                    loaded.Appearance.Title.Size = loaded.Appearance.Body.Size =
                        loaded.Appearance.Vocabulary.Size = loaded.Appearance.Phonetics.Size =
                        loaded.Appearance.Meanings.Size = badSize;
                    loaded.Normalize();
                    Program.Check(loaded.Appearance.Title.Size == 18 && loaded.Appearance.Body.Size == 15 &&
                        loaded.Appearance.Vocabulary.Size == 16 && loaded.Appearance.Phonetics.Size == 14 &&
                        loaded.Appearance.Meanings.Size == 15);
                }
                foreach (double bad in new[] { -1d, 61d, 1.234, double.NaN, double.PositiveInfinity })
                {
                    loaded.Appearance.Title.LineHeight = loaded.Appearance.Body.LineHeight =
                        loaded.Appearance.Meanings.LineHeight = bad;
                    loaded.Appearance.Title.ParagraphAfter = loaded.Appearance.Body.ParagraphAfter = bad;
                    loaded.Appearance.Body.FirstLineIndent = loaded.Appearance.Body.SelectionAddSize = bad;
                    loaded.ForwardStep = loaded.BackwardStep = bad;
                    loaded.Normalize();
                    Program.Check(loaded.Appearance.Title.LineHeight == 1.7 && loaded.Appearance.Body.LineHeight == 1.7 &&
                        loaded.Appearance.Meanings.LineHeight == 1.5 && loaded.Appearance.Title.ParagraphAfter == 16 &&
                        loaded.Appearance.Body.ParagraphAfter == 16 && loaded.Appearance.Body.FirstLineIndent == 2 &&
                        loaded.Appearance.Body.SelectionAddSize == 0.65 && loaded.ForwardStep == 4 && loaded.BackwardStep == 5);
                }
                loaded.Appearance.Title.LineHeight = 1;
                loaded.Appearance.Body.LineHeight = 3;
                loaded.Appearance.Meanings.LineHeight = 1.55;
                loaded.Appearance.Title.ParagraphAfter = 0;
                loaded.Appearance.Body.ParagraphAfter = 40;
                loaded.Appearance.Body.FirstLineIndent = 5;
                loaded.Appearance.Body.SelectionAddSize = 1.2;
                loaded.ForwardStep = 1;
                loaded.BackwardStep = 60;
                loaded.Appearance.Title.Size = 10;
                loaded.Appearance.Body.Size = 40;
                loaded.Appearance.Vocabulary.Size = 21;
                loaded.Appearance.Phonetics.Size = 22;
                loaded.Appearance.Meanings.Size = 23;
                loaded.Save();
                loaded = ReaderSettings.Load(out warning);
                Program.Check(warning == null && loaded.Appearance.Title.Size == 10 &&
                    loaded.Appearance.Body.Size == 40 && loaded.Appearance.Vocabulary.Size == 21 &&
                    loaded.Appearance.Phonetics.Size == 22 && loaded.Appearance.Meanings.Size == 23 &&
                    loaded.Appearance.Title.LineHeight == 1 && loaded.Appearance.Body.LineHeight == 3 &&
                    loaded.Appearance.Meanings.LineHeight == 1.55 && loaded.Appearance.Title.ParagraphAfter == 0 &&
                    loaded.Appearance.Body.ParagraphAfter == 40 && loaded.Appearance.Body.FirstLineIndent == 5 &&
                    loaded.Appearance.Body.SelectionAddSize == 1.2 && loaded.ForwardStep == 1 && loaded.BackwardStep == 60);
                byte[] persisted = File.ReadAllBytes(path);
                byte[] backupBytes = File.ReadAllBytes(backup);
                layout.ResizeLibrary(400, 900);
                layout.ResizeVocabulary(400, 900);
                Program.Check(layout.LeftWidth != 250 && layout.RightWidth != 300);
                loaded.Save();
                loaded.Save();
                Program.Check(File.ReadAllBytes(path).SequenceEqual(persisted) &&
                    File.ReadAllBytes(backup).SequenceEqual(backupBytes));
            }
            finally { Restore(path, old); Restore(backup, oldBackup); }
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
    private static void Restore(string path, byte[]? bytes)
    {
        if (bytes == null)
        {
            if (File.Exists(path)) File.Delete(path);
        }
        else
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, bytes);
        }
    }

    private static WebReaderBridge Bridge(ReadArticles.MainWindow window) =>
        (WebReaderBridge)typeof(ReadArticles.MainWindow)
            .GetField("webReaderBridge", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
}
