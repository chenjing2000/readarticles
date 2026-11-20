using System.IO;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using ReadArticles.ViewModels;
using ReadArticles.Models;
using ReadArticles.Services;
using System.Text;
using System.Text.Json;
using System.Windows;
using Microsoft.Web.WebView2.Core;
using ReadArticles.Infrastructure;

namespace ReadArticles;

public partial class MainWindow
{
    private const string WebPilotOrigin = "https://readarticles.invalid";
    private const string WebPilotDocument = WebPilotOrigin + "/reader.html";
    private string webPilotSessionId = Guid.NewGuid().ToString("N");
    private string? webPilotPageId;
    private bool webPilotReady;
    private bool webPilotClosed;
    private bool webPilotFailureShown;
    private int webPilotVocabularyRevision;
    private (double Left, double Right)? webPilotRightDragStart;
    private ScriptArticle? webPilotScriptArticle;
    private bool webPilotScriptMode;
    private string? webPilotPendingModeRequest;
    private bool webPilotPendingModeIsScript;
    private ScriptArticle? webPilotPendingScriptArticle;
    private string? webPilotPendingSnapshotId;
    private readonly HashSet<string> webPilotSeenRequests = new(StringComparer.Ordinal);

    // The single WebView2 is the sole reading and vocabulary workspace.
    private void InitializeWebPilot()
    {
        ViewModel.ArticleChanged += WebPilotArticleChanged;
        ViewModel.VocabularyChanged += WebPilotVocabularyChanged;
        ViewModel.PlaybackChanged += SendWebPilotPlayback;
        ViewModel.Notifications.Changed += SendWebPilotNotifications;
        ViewModel.PropertyChanged += WebPilotPropertyChanged;
        Loaded += WebPilotLoaded;
    }

    private async void WebPilotLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= WebPilotLoaded;
        try
        {
            string dataDirectory = Path.Combine(AppPaths.CacheDirectory, "webview2");
            var environment = await CoreWebView2Environment.CreateAsync(userDataFolder: dataDirectory);
            if (webPilotClosed) return;
            await ReaderWebView.EnsureCoreWebView2Async(environment);
            if (webPilotClosed) return;

            var core = ReaderWebView.CoreWebView2;
            core.Settings.AreHostObjectsAllowed = false;
            core.Settings.AreDefaultContextMenusEnabled = false;
            core.Settings.AreDevToolsEnabled = false;
            core.Settings.IsStatusBarEnabled = false;
            core.AddWebResourceRequestedFilter(WebPilotOrigin + "/*", CoreWebView2WebResourceContext.All);
            core.WebResourceRequested += WebPilotResourceRequested;
            core.WebMessageReceived += WebPilotMessageReceived;
            core.NavigationStarting += (_, args) =>
            {
                if (!string.Equals(args.Uri, WebPilotDocument, StringComparison.OrdinalIgnoreCase))
                {
                    args.Cancel = true;
                    return;
                }
                // Reload creates a new JS page identity. Do not accept the old page.
                webPilotReady = false;
                webPilotPageId = null;
                webPilotPendingSnapshotId = null;
                webPilotSeenRequests.Clear();
                webPilotPendingModeRequest = null;
                webPilotPendingScriptArticle = null;
                WebPilotOverlay.Visibility = Visibility.Collapsed;
            };
            core.NewWindowRequested += (_, args) => args.Handled = true;
            core.NavigationCompleted += (_, args) =>
            {
                if (!args.IsSuccess)
                    WebPilotFailure("WebView2 本地页面加载失败。请重新启动 ReadArticles。");
            };
            core.ProcessFailed += (_, args) =>
            {
                // A failed browser process never commits a business-state change.
                WebPilotFailure("WebView2 进程异常。请重新启动 ReadArticles。");
            };
            core.Navigate(WebPilotDocument);
        }
        catch (Exception error)
        {
            AppLogger.Default.Warning("WebViewPilotInitializationFailed", error);
            WebPilotFailure("WebView2 无法初始化。请检查 Evergreen Runtime 安装或用户数据目录权限，然后重新启动程序。");
        }
    }

    private void WebPilotResourceRequested(object? sender, CoreWebView2WebResourceRequestedEventArgs args)
    {
        var path = Uri.TryCreate(args.Request.Uri, UriKind.Absolute, out var uri) &&
                   uri.Scheme == Uri.UriSchemeHttps &&
                   uri.Host.Equals("readarticles.invalid", StringComparison.OrdinalIgnoreCase) &&
                   string.IsNullOrEmpty(uri.Query) ? uri.AbsolutePath : "";
        (string? resource, string mime) = path switch
        {
            "/reader.html" => ("ReadArticles.web.reader.html", "text/html"),
            "/reader.css" => ("ReadArticles.web.reader.css", "text/css"),
            "/reader.js" => ("ReadArticles.web.reader.js", "text/javascript"),
            _ when Regex.IsMatch(path, @"^/icons/[a-z-]+\.svg$") =>
                ("ReadArticles.web.icons." + Path.GetFileName(path), "image/svg+xml"),
            _ => (null, "text/plain")
        };
        Stream? stream = resource is null ? null :
            typeof(MainWindow).Assembly.GetManifestResourceStream(resource);
        int status = stream is null ? 404 : 200;
        stream ??= new MemoryStream(Encoding.UTF8.GetBytes("Not found"));
        string headers = $"Content-Type: {mime}; charset=utf-8\r\n" +
            "Cache-Control: no-store\r\nX-Content-Type-Options: nosniff\r\n" +
            "Content-Security-Policy: default-src 'none'; script-src 'self'; style-src 'self'; img-src 'self' data:; connect-src 'none'; frame-src 'none'; base-uri 'none'; form-action 'none'\r\n";
        args.Response = ReaderWebView.CoreWebView2.Environment.CreateWebResourceResponse(
            stream, status, status == 200 ? "OK" : "Not Found", headers);
    }

    private void WebPilotMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs args)
    {
        if (webPilotClosed || webPilotFailureShown ||
            !string.Equals(args.Source, WebPilotDocument, StringComparison.OrdinalIgnoreCase))
            return;
        try
        {
            string raw = args.WebMessageAsJson;
            if (raw.Length > 32768) return;
            using var json = JsonDocument.Parse(raw);
            JsonElement root = json.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("version", out var version) ||
                version.ValueKind != JsonValueKind.Number || !version.TryGetInt32(out int protocolVersion) ||
                protocolVersion != 1 ||
                !TryMessageString(root, "type", out string? type) ||
                !TryMessageString(root, "pageId", out string? pageId) ||
                !Guid.TryParse(pageId, out _))
                return;

            if (type == "ready")
            {
                if (webPilotReady) return;
                webPilotPageId = pageId;
                webPilotReady = true;
                webPilotSeenRequests.Clear();
                SendWebPilotInitialize();
                return;
            }

            if (!webPilotReady || pageId != webPilotPageId) return;
            if (type is "modeRendered" or "modeRenderFailed")
            {
                if (!TryMessageString(root, "sessionId", out string? modeSession) ||
                    modeSession != webPilotSessionId ||
                    !TryMessageString(root, "requestId", out string? modeRequest) ||
                    modeRequest != webPilotPendingModeRequest)
                    return;
                bool succeeded = type == "modeRendered";
                if (succeeded)
                {
                    webPilotScriptMode = webPilotPendingModeIsScript;
                    if (webPilotScriptMode) webPilotScriptArticle = webPilotPendingScriptArticle;
                    RefreshPlayback();
                }
                else
                {
                    ViewModel.Notify("WebPilotScriptRenderFailed",
                        "Script 切换失败或不可用，已保留原阅读模式",
                        NotificationSeverity.Error);
                }
                webPilotPendingModeRequest = null;
                webPilotPendingScriptArticle = null;
                PostWebPilot("actionResult", new { status = succeeded ? "success" : "failed" }, modeRequest);
                return;
            }
            if (type is "rendered" or "renderFailed")
            {
                if (!TryMessageString(root, "sessionId", out string? sessionId) ||
                    sessionId != webPilotSessionId ||
                    !TryMessageString(root, "requestId", out string? snapshotId) ||
                    snapshotId != webPilotPendingSnapshotId)
                    return;

                webPilotPendingSnapshotId = null;
                if (type == "renderFailed")
                    WebPilotFailure("WebView2 正文渲染失败。请重新启动 ReadArticles。");
                else
                    WebPilotOverlay.Visibility = Visibility.Visible;
                return;
            }

            // The pilot deliberately rejects all user actions until the existing
            // persistence and playback services have validated request handlers.
            if (type is not ("addWord" or "moveWord" or "deleteWord" or "importVocabulary" or
                "playArticle" or "stopArticle" or "seekArticle" or "playWord" or
                "switchReadingMode" or "setHighlight" or
                "setPanelWidth" or "dragPanelWidth" or "cancelPanelDrag" or "notificationDismiss"))
                return;

            if (!TryMessageString(root, "requestId", out string? requestId) ||
                !Guid.TryParse(requestId, out _))
                return;
            if (!webPilotSeenRequests.Add(requestId)) return;

            bool currentSession = TryMessageString(root, "sessionId", out string? actionSession) &&
                actionSession == webPilotSessionId;
            bool currentVocabulary = root.TryGetProperty("vocabularyRevision", out var revision) &&
                revision.ValueKind == JsonValueKind.Number &&
                revision.TryGetInt32(out int value) && value == webPilotVocabularyRevision;
            if (!currentSession || !currentVocabulary)
            {
                PostWebPilot("actionResult", new { status = "stale" }, requestId);
                return;
            }
            if (type == "dragPanelWidth")
            {
                if (root.TryGetProperty("payload", out var drag) &&
                    drag.ValueKind == JsonValueKind.Object &&
                    drag.TryGetProperty("width", out var candidate) &&
                    candidate.ValueKind == JsonValueKind.Number &&
                    candidate.TryGetDouble(out double desired) && double.IsFinite(desired) &&
                    desired >= 200 && desired <= 400)
                {
                    webPilotRightDragStart ??= (settings.LeftWidth, settings.RightWidth);
                    ResizeVocabularyPane(desired);
                }
                return;
            }
            if (type == "cancelPanelDrag")
            {
                if (webPilotRightDragStart is { } original)
                {
                    webPilotRightDragStart = null;
                    SetPaneWidths(original.Left, original.Right, notifyBrowser: true);
                }
                return;
            }
            if (type == "setHighlight")
            {
                if (!root.TryGetProperty("payload", out var payload) ||
                    payload.ValueKind != JsonValueKind.Object ||
                    !payload.TryGetProperty("enabled", out var enabled) ||
                    enabled.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                {
                    PostWebPilot("actionResult", new { status = "invalid" }, requestId);
                    return;
                }
                ViewModel.HighlightsVisible = enabled.GetBoolean();
                PostWebPilot("actionResult", new { status = "success" }, requestId);
                return;
            }
            if (type == "setPanelWidth")
            {
                WebPilotSettingIntent(root, requestId);
                return;
            }
            if (type == "notificationDismiss")
            {
                WebPilotDismissNotification(root, requestId);
                return;
            }
            if (type == "switchReadingMode")
            {
                WebPilotSwitchReadingMode(requestId);
                return;
            }
            if (type is "playArticle" or "stopArticle" or "seekArticle" or
                "playWord")
            {
                WebPilotAudioIntent(type, root, requestId);
                return;
            }
            if (type == "addWord")
            {
                if (!root.TryGetProperty("payload", out var selected) ||
                    !WebPilotValidateSelection(selected, out string? text))
                {
                    WebPilotEditResult(requestId, "invalid");
                    return;
                }
                var result = ViewModel.TryAddWord(text!);
                WebPilotEditResult(requestId, WebPilotResultName(result),
                    result == VocabularyEditResult.Success ? ViewModel.Words.Count - 1 : -1);
                return;
            }
            if (type is "moveWord" or "deleteWord")
            {
                if (!root.TryGetProperty("payload", out var target) ||
                    !WebPilotTryWordTarget(target, out int index))
                {
                    WebPilotEditResult(requestId, "stale");
                    return;
                }
                var word = ViewModel.Words[index].Entry;
                VocabularyEditResult result;
                int nextIndex = -1;
                if (type == "moveWord")
                {
                    if (!target.TryGetProperty("direction", out var directionValue) ||
                        directionValue.ValueKind != JsonValueKind.Number ||
                        !directionValue.TryGetInt32(out int direction) ||
                        (direction != -1 && direction != 1))
                    {
                        WebPilotEditResult(requestId, "invalid");
                        return;
                    }
                    result = ViewModel.TryMoveWord(word, direction);
                    if (result == VocabularyEditResult.Success) nextIndex = index + direction;
                }
                else result = ViewModel.TryDeleteWord(word);

                WebPilotEditResult(requestId, WebPilotResultName(result), nextIndex);
                return;
            }
            if (type == "importVocabulary")
            {
                if (!ViewModel.CanWriteVocabulary)
                {
                    WebPilotEditResult(requestId, "unavailable");
                    return;
                }
                string expectedSession = webPilotSessionId;
                int expectedRevision = webPilotVocabularyRevision;
                var dialog = new OpenFileDialog
                {
                    Filter = "Vocabulary JSON|*.json",
                    CheckFileExists = true
                };
                if (dialog.ShowDialog(this) != true ||
                    MessageBox.Show(this, "用导入词表替换当前词表？",
                        "导入词汇表", MessageBoxButton.YesNo) != MessageBoxResult.Yes)
                {
                    WebPilotEditResult(requestId, "cancelled");
                    return;
                }
                // A modal dialog may allow a library/article switch. Require
                // the original committed context after it closes.
                if (expectedSession != webPilotSessionId ||
                    expectedRevision != webPilotVocabularyRevision)
                {
                    WebPilotEditResult(requestId, "stale");
                    return;
                }
                var result = ViewModel.TryImportVocabulary(dialog.FileName);
                WebPilotEditResult(requestId, WebPilotResultName(result));
                return;
            }
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException or FormatException)
        {
            AppLogger.Default.Warning("WebViewPilotInvalidMessage", error);
        }
    }

    // Only the C# notification service owns queued user-visible feedback.
    // The ViewModel already publishes duplicate and failed-save messages.
    private void WebPilotEditResult(string requestId, string status, int index = -1)
    {
        if (status is not ("duplicate" or "failed"))
        {
            string message = status switch
            {
                "success" => "词汇表已保存",
                "cancelled" => "已取消导入",
                "stale" => "文章或词表已变化，请重新操作",
                "invalid" => "选区或词条无效，请重新选择",
                _ => "当前词表不可编辑"
            };
            var severity = status is "success" or "cancelled"
                ? NotificationSeverity.Info : NotificationSeverity.Warning;
            ViewModel.Notify("WebPilotVocabulary" + status, message, severity);
        }
        PostWebPilot("actionResult", new { status, index }, requestId);
    }

    private void WebPilotSettingIntent(JsonElement root, string requestId)
    {
        if (!root.TryGetProperty("payload", out var payload) ||
            payload.ValueKind != JsonValueKind.Object ||
            !payload.TryGetProperty("width", out var width) ||
            width.ValueKind != JsonValueKind.Number ||
            !width.TryGetDouble(out double value) || !double.IsFinite(value) ||
            value < 200 || value > 400)
        {
            PostWebPilot("actionResult", new { status = "invalid" }, requestId);
            return;
        }
        webPilotRightDragStart = null;
        ResizeVocabularyPane(value);
        PostWebPilot("actionResult", new { status = "success" }, requestId);
    }

    private void WebPilotDismissNotification(JsonElement root, string requestId)
    {
        if (!root.TryGetProperty("payload", out var payload) ||
            payload.ValueKind != JsonValueKind.Object ||
            !payload.TryGetProperty("id", out var id) ||
            id.ValueKind != JsonValueKind.Number ||
            !id.TryGetInt64(out long notificationId) ||
            notificationId <= 0)
        {
            PostWebPilot("actionResult", new { status = "invalid" }, requestId);
            return;
        }
        bool dismissed = ViewModel.Notifications.Dismiss(notificationId);
        PostWebPilot("actionResult", new { status = dismissed ? "success" : "stale" }, requestId);
    }

    private object? WebPilotPassageSnapshot()
    {
        var article = ViewModel.Article;
        return article is null ? null : new
        {
            title = article.Title,
            paragraphs = article.Paragraphs
        };
    }

    private static object WebPilotScriptSnapshot(ScriptArticle article) => new
    {
        title = article.Title,
        paragraphs = article.Paragraphs.Select(p => p.Sentences.Select(s => new
        {
            text = s.Text,
            timedUnits = s.TimedUnits.Select(u => new
            {
                text = u.Text, offset = u.Offset, length = u.Length,
                start = u.Start, end = u.End, canColor = u.CanColor
            }).ToArray()
        }).ToArray()).ToArray()
    };

    private void WebPilotSwitchReadingMode(string requestId)
    {
        var article = ViewModel.Article;
        if (article is null)
        {
            PostWebPilot("actionResult", new { status = "unavailable" }, requestId);
            return;
        }
        if (webPilotPendingModeRequest is not null)
        {
            PostWebPilot("actionResult", new { status = "unavailable" }, requestId);
            return;
        }
        bool nextScript = !webPilotScriptMode;
        ScriptArticle? script = null;
        if (nextScript)
        {
            script = webPilotScriptArticle;
            if (script is null)
            {
                string path = ArticleRepository.ScriptPathFor(article.FilePath);
                if (!File.Exists(path))
                {
                    ViewModel.Notify("ScriptNotFound",
                        "Script 切换失败或不可用，已保留原阅读模式。未找到同名 Script JSON：" + Path.GetFileName(path),
                        NotificationSeverity.Error);
                    PostWebPilot("actionResult", new { status = "unavailable" }, requestId);
                    return;
                }
                try
                {
                    script = new ArticleRepository().LoadScript(article.FilePath);
                }
                catch (Exception error) when (error is InvalidDataException or
                    JsonException or IOException or UnauthorizedAccessException or ArgumentException)
                {
                    AppLogger.Default.Warning("WebPilotScriptLoadFailed", error);
                    ViewModel.Notify("ScriptLoadFailed",
                        "Script 切换失败或不可用，已保留原阅读模式。Script JSON 加载失败：" + error.Message,
                        NotificationSeverity.Error);
                    PostWebPilot("actionResult", new { status = "failed" }, requestId);
                    return;
                }
            }
        }
        webPilotPendingModeRequest = requestId;
        webPilotPendingModeIsScript = nextScript;
        webPilotPendingScriptArticle = script;
        PostWebPilot("setReadingMode", new
        {
            mode = nextScript ? "script" : "passage",
            article = nextScript ? WebPilotScriptSnapshot(script!) : WebPilotPassageSnapshot()
        }, requestId);
    }

    private void WebPilotAudioIntent(string type, JsonElement root, string requestId)
    {
        var article = ViewModel.Article;
        if (article is null)
        {
            PostWebPilot("actionResult", new { status = "unavailable" }, requestId);
            return;
        }

        if (type == "playArticle")
            ViewModel.ToggleArticleAudio();
        else if (type == "stopArticle")
            ViewModel.StopArticleAudio();
        else if (type == "seekArticle")
        {
            if (!ViewModel.Playback.CanSeekArticle ||
                !root.TryGetProperty("payload", out var data) ||
                data.ValueKind != JsonValueKind.Object)
            {
                PostWebPilot("actionResult", new { status = "invalid" }, requestId);
                return;
            }
            double seconds;
            if (data.TryGetProperty("direction", out var direction))
            {
                if (direction.ValueKind != JsonValueKind.String ||
                    direction.GetString() is not ("backward" or "forward"))
                {
                    PostWebPilot("actionResult", new { status = "invalid" }, requestId);
                    return;
                }
                double step = direction.GetString() == "backward"
                    ? -settings.BackwardStep : settings.ForwardStep;
                seconds = Math.Clamp(ViewModel.Playback.ArticlePosition + step,
                    0, ViewModel.Playback.ArticleDuration);
            }
            else if (!data.TryGetProperty("position", out var time) ||
                     time.ValueKind != JsonValueKind.Number ||
                     !time.TryGetDouble(out seconds) || !double.IsFinite(seconds) ||
                     seconds < 0 || seconds > ViewModel.Playback.ArticleDuration)
            {
                PostWebPilot("actionResult", new { status = "invalid" }, requestId);
                return;
            }
            ViewModel.Playback.SeekArticle(seconds);
        }
        else if (type == "playWord")
        {
            if (!root.TryGetProperty("payload", out var target) ||
                !WebPilotTryWordTarget(target, out int index) ||
                !TryMessageString(target, "accent", out string? accent) ||
                accent is not ("uk" or "us") ||
                (accent == "uk" ? !ViewModel.Words[index].CanPlayUk : !ViewModel.Words[index].CanPlayUs))
            {
                PostWebPilot("actionResult", new { status = "invalid" }, requestId);
                return;
            }
            ViewModel.PlayWord(ViewModel.Words[index].Entry, accent);
        }
        // Playback failures continue to be surfaced by ReaderPlayback notifications.
        // "accepted" is not a false claim that a media file played successfully.
        PostWebPilot("actionResult", new { status = "accepted" }, requestId);
        SendWebPilotPlayback();
    }

    private static string WebPilotResultName(VocabularyEditResult result) => result switch
    {
        VocabularyEditResult.Success => "success",
        VocabularyEditResult.Duplicate => "duplicate",
        VocabularyEditResult.Unavailable => "unavailable",
        _ => "failed"
    };

    private bool WebPilotTryWordTarget(JsonElement payload, out int index)
    {
        index = -1;
        if (payload.ValueKind != JsonValueKind.Object ||
            !payload.TryGetProperty("index", out var indexValue) ||
            indexValue.ValueKind != JsonValueKind.Number ||
            !indexValue.TryGetInt32(out index) ||
            index < 0 || index >= ViewModel.Words.Count ||
            !payload.TryGetProperty("word", out var wordValue) ||
            wordValue.ValueKind != JsonValueKind.String)
            return false;
        return string.Equals(ViewModel.Words[index].Word, wordValue.GetString(),
            StringComparison.Ordinal);
    }

    private bool WebPilotValidateSelection(JsonElement payload, out string? text)
    {
        text = null;
        if (payload.ValueKind != JsonValueKind.Object ||
            !TryMessageString(payload, "mode", out string? mode) ||
            mode != (webPilotScriptMode ? "script" : "passage") ||
            !payload.TryGetProperty("text", out var selectedText) ||
            selectedText.ValueKind != JsonValueKind.String ||
            !payload.TryGetProperty("offset", out var offsetValue) ||
            !payload.TryGetProperty("length", out var lengthValue) ||
            offsetValue.ValueKind != JsonValueKind.Number ||
            lengthValue.ValueKind != JsonValueKind.Number ||
            !offsetValue.TryGetInt32(out int offset) ||
            !lengthValue.TryGetInt32(out int length) ||
            offset < 0 || length <= 0 || length > 2048)
            return false;

        string? candidate = selectedText.GetString();
        if (string.IsNullOrWhiteSpace(candidate) || candidate.Length > 2048)
            return false;
        string original;

        if (mode == "script")
        {
            if (webPilotScriptArticle is null ||
                !payload.TryGetProperty("paragraphIndex", out var paragraph) ||
                paragraph.ValueKind != JsonValueKind.Number ||
                !paragraph.TryGetInt32(out int paragraphIndex) ||
                paragraphIndex < 0 || paragraphIndex >= webPilotScriptArticle.Paragraphs.Count ||
                !payload.TryGetProperty("sentenceIndex", out var sentence) ||
                sentence.ValueKind != JsonValueKind.Number ||
                !sentence.TryGetInt32(out int sentenceIndex) ||
                sentenceIndex < 0 ||
                sentenceIndex >= webPilotScriptArticle.Paragraphs[paragraphIndex].Sentences.Count)
                return false;
            original = webPilotScriptArticle.Paragraphs[paragraphIndex].Sentences[sentenceIndex].Text;
        }
        else
        {
            if (!payload.TryGetProperty("paragraphIndex", out var paragraph) ||
                paragraph.ValueKind != JsonValueKind.Number ||
                !paragraph.TryGetInt32(out int paragraphIndex) ||
                !payload.TryGetProperty("sentenceIndex", out var sentence) ||
                sentence.ValueKind != JsonValueKind.Number ||
                !sentence.TryGetInt32(out int sentenceIndex) ||
                paragraphIndex < 0 || sentenceIndex < 0 ||
                ViewModel.Article is not { } current ||
                paragraphIndex >= current.Paragraphs.Count ||
                sentenceIndex >= current.Paragraphs[paragraphIndex].Length)
                return false;
            original = Regex.Replace(current.Paragraphs[paragraphIndex][sentenceIndex], @"\[\[(\d+)\]\]",
                match => "____" + match.Groups[1].Value + "____");
        }
        if (offset > original.Length || length > original.Length - offset) return false;
        string normalized = Regex.Replace(original.Substring(offset, length), @"\s+", " ").Trim();
        if (!string.Equals(candidate, normalized, StringComparison.Ordinal)) return false;
        text = candidate;
        return true;
    }

    private static bool TryMessageString(JsonElement root, string property, out string? value)
    {
        value = null;
        return root.TryGetProperty(property, out var field) &&
            field.ValueKind == JsonValueKind.String &&
            (value = field.GetString()) is { Length: > 0 and <= 100 };
    }

    private void SendWebPilotInitialize()
    {
        if (!webPilotReady) return;
        webPilotPendingSnapshotId = Guid.NewGuid().ToString("D");
        WebPilotOverlay.Visibility = Visibility.Collapsed;
        object? passage = webPilotScriptMode && webPilotScriptArticle is not null
            ? WebPilotScriptSnapshot(webPilotScriptArticle) : WebPilotPassageSnapshot();
        PostWebPilot("initialize", new
        {
            article = passage,
            mode = webPilotScriptMode ? "script" : "passage",
            canWriteVocabulary = ViewModel.CanWriteVocabulary,
            vocabulary = WebPilotVocabularyRows(),
            settings = WebPilotSettingsSnapshot(),
            playback = WebPilotPlaybackSnapshot(),
            notifications = WebPilotNotificationSnapshot()
        }, webPilotPendingSnapshotId);
    }

    private object[] WebPilotVocabularyRows() => ViewModel.Words.Select(w => (object)new
    {
        word = w.Word,
        phoneticUk = w.PhoneticUk,
        phoneticUs = w.PhoneticUs,
        meanings = w.Meanings.Select(m => new { pos = m.Pos, text = m.Text }).ToArray(),
        canPlayUk = w.CanPlayUk,
        canPlayUs = w.CanPlayUs
    }).ToArray();

    private object WebPilotSettingsSnapshot() => new
    {
        appearance = new
        {
            title = new { font = settings.Appearance.Title.Font, size = settings.Appearance.Title.Size,
                lineHeight = settings.Appearance.Title.LineHeight,
                paragraphAfter = settings.Appearance.Title.ParagraphAfter, color = settings.Appearance.Title.Color },
            body = new { font = settings.Appearance.Body.Font, size = settings.Appearance.Body.Size,
                lineHeight = settings.Appearance.Body.LineHeight,
                paragraphAfter = settings.Appearance.Body.ParagraphAfter,
                firstLineIndent = settings.Appearance.Body.FirstLineIndent,
                color = settings.Appearance.Body.Color, background = settings.Appearance.Body.Background,
                scriptColor = settings.Appearance.Body.ScriptColor,
                selectionAddSize = settings.Appearance.Body.SelectionAddSize,
                selectionBackground = settings.Appearance.Body.SelectionBackground },
            vocabulary = new { font = settings.Appearance.Vocabulary.Font,
                size = settings.Appearance.Vocabulary.Size, color = settings.Appearance.Vocabulary.Color },
            phonetics = new { size = settings.Appearance.Phonetics.Size,
                color = settings.Appearance.Phonetics.Color },
            meanings = new { font = settings.Appearance.Meanings.Font,
                size = settings.Appearance.Meanings.Size, lineHeight = settings.Appearance.Meanings.LineHeight,
                color = settings.Appearance.Meanings.Color }
        },
        accent = ViewModel.Accent,
        highlightsVisible = ViewModel.HighlightsVisible,
        rightWidth = settings.RightWidth,
        forwardStep = settings.ForwardStep,
        backwardStep = settings.BackwardStep
    };

    private object WebPilotPlaybackSnapshot()
    {
        var playback = ViewModel.Playback;
        return new
        {
            state = playback.IsArticlePlaying ? "playing" :
                    playback.IsArticlePaused ? "paused" : "stopped",
            position = playback.ArticlePosition,
            duration = playback.ArticleDuration,
            canSeek = playback.CanSeekArticle,
            canPlay = ViewModel.Article is not null,
            isArticleActive = playback.IsArticleActive
        };
    }

    private object WebPilotNotificationSnapshot() => new
    {
        // Service.Visible is the first three FIFO messages, newest first
        // within the admitted group. Queued messages are not transmitted
        // until a displayed notification has completed dismissal.
        visible = ViewModel.Notifications.Visible.Select(n =>
            new { id = n.Id, message = n.Message, severity = n.Severity.ToString() }).ToArray()
    };

    private void WebPilotArticleChanged()
    {
        // A previous display is never presented as the newly committed article.
        webPilotSessionId = Guid.NewGuid().ToString("N");
        webPilotVocabularyRevision = 0;
        webPilotScriptMode = false;
        webPilotScriptArticle = null;
        webPilotPendingModeRequest = null;
        webPilotPendingScriptArticle = null;
        webPilotSeenRequests.Clear();
        if (webPilotReady)
        {
            WebPilotOverlay.Visibility = Visibility.Collapsed;
            SendWebPilotInitialize();
        }
    }

    private void WebPilotVocabularyChanged()
    {
        // Only a committed vocabulary edit emits this event.
        webPilotVocabularyRevision++;
        if (webPilotReady)
            PostWebPilot("setVocabulary", new
            {
                vocabulary = WebPilotVocabularyRows(),
                canWriteVocabulary = ViewModel.CanWriteVocabulary
            });
    }

    private void WebPilotPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
    {
        if (args.PropertyName is nameof(ViewModel.Accent) or nameof(ViewModel.HighlightsVisible))
            SendWebPilotSettings();
    }

    private void SendWebPilotSettings()
    {
        if (webPilotReady)
            PostWebPilot("setSettings", WebPilotSettingsSnapshot());
    }

    private void SendWebPilotPlayback()
    {
        if (webPilotReady) PostWebPilot("setPlayback", WebPilotPlaybackSnapshot());
    }

    private void SendWebPilotNotifications()
    {
        if (webPilotReady) PostWebPilot("setNotifications", WebPilotNotificationSnapshot());
    }

    private void PostWebPilot(string type, object payload, string? requestId = null)
    {
        if (webPilotClosed || webPilotFailureShown) return;
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(new Action(() => PostWebPilot(type, payload, requestId)));
            return;
        }
        if (!webPilotReady || webPilotPageId is null || ReaderWebView.CoreWebView2 is null)
            return;
        try
        {
            string json = JsonSerializer.Serialize(new
            {
                version = 1,
                type,
                pageId = webPilotPageId,
                sessionId = webPilotSessionId,
                vocabularyRevision = webPilotVocabularyRevision,
                requestId,
                payload
            });
            ReaderWebView.CoreWebView2.PostWebMessageAsJson(json);
        }
        catch (Exception error)
        {
            AppLogger.Default.Warning("WebViewPilotSendFailed", error);
            WebPilotFailure("WebView2 通信失败。请重新启动 ReadArticles。");
        }
    }

    private void WebPilotFailure(string message)
    {
        if (webPilotClosed || webPilotFailureShown) return;
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(new Action(() => WebPilotFailure(message)));
            return;
        }
        webPilotFailureShown = true;
        webPilotReady = false;
        webPilotPageId = null;
        webPilotPendingSnapshotId = null;
        webPilotPendingModeRequest = null;
        webPilotPendingScriptArticle = null;
        ViewModel.Stop();
        // Keep the native failure screen above every pane, including the tree.
        // The former WPF reader is not a runtime recovery path.
        WebFailureMessage.Text = message;
        WebFailurePanel.Visibility = Visibility.Visible;
        WebPilotOverlay.Visibility = Visibility.Collapsed;
        WebFailureExitButton.Focus();
        AppLogger.Default.Warning("WebViewPilotUnavailable");
    }

    private void WebFailureExitClicked(object sender, RoutedEventArgs e) => Close();

    private void CloseWebPilot()
    {
        webPilotClosed = true;
        ViewModel.ArticleChanged -= WebPilotArticleChanged;
        ViewModel.VocabularyChanged -= WebPilotVocabularyChanged;
        ViewModel.PlaybackChanged -= SendWebPilotPlayback;
        ViewModel.Notifications.Changed -= SendWebPilotNotifications;
        ViewModel.PropertyChanged -= WebPilotPropertyChanged;
        Loaded -= WebPilotLoaded;
        ReaderWebView.Dispose();
    }
}
