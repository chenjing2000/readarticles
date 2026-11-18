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
    private bool webPilotEnabled;
    private bool webPilotReady;
    private bool webPilotClosed;
    private bool webPilotFailureShown;
    private int webPilotVocabularyRevision;
    private ScriptArticle? webPilotScriptArticle;
    private bool webPilotScriptMode;
    private string? webPilotPendingModeRequest;
    private bool webPilotPendingModeIsScript;
    private ScriptArticle? webPilotPendingScriptArticle;
    private object? webPilotVocabularyIdentity;
    private string? webPilotPendingSnapshotId;
    private readonly HashSet<string> webPilotSeenRequests = new(StringComparer.Ordinal);

    // Migration-only pilot. All original WPF controls and business commands remain intact.
    private void InitializeWebPilot()
    {
        if (!webPilotEnabled) return;
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
                    WebPilotFailure("WebView2 本地页面加载失败，已切回 WPF 阅读界面。");
            };
            core.ProcessFailed += (_, args) =>
            {
                // A failed browser process never commits a business-state change.
                WebPilotFailure("WebView2 进程异常，已切回 WPF 阅读界面。");
            };
            core.Navigate(WebPilotDocument);
        }
        catch (Exception error)
        {
            AppLogger.Default.Warning("WebViewPilotInitializationFailed", error);
            WebPilotFailure("WebView2 无法初始化。请检查 Evergreen Runtime 安装或用户数据目录权限。现已使用 WPF 阅读界面。");
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
                    WebPilotFailure("WebView2 正文渲染失败，已切回 WPF 阅读界面。");
                else
                    WebPilotOverlay.Visibility = Visibility.Visible;
                return;
            }

            // The pilot deliberately rejects all user actions until the existing
            // persistence and playback services have validated request handlers.
            if (type is not ("addWord" or "moveWord" or "deleteWord" or "importVocabulary" or
                "playArticle" or "stopArticle" or "seekArticle" or "playWord" or
                "playSegment" or "switchReadingMode" or "setHighlight" or
                "setFontSize" or "setAccent" or "setPanelWidth" or "setForwardStep" or
                "setBackwardStep" or "notificationDismiss"))
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
            if (type is ("setFontSize" or "setAccent" or "setPanelWidth" or
                "setForwardStep" or "setBackwardStep"))
            {
                WebPilotSettingIntent(type, root, requestId);
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
                "playWord" or "playSegment")
            {
                WebPilotAudioIntent(type, root, requestId);
                return;
            }
            if (type == "addWord")
            {
                if (!root.TryGetProperty("payload", out var selected) ||
                    !WebPilotValidateSelection(selected, out string? text))
                {
                    PostWebPilot("actionResult", new { status = "invalid" }, requestId);
                    return;
                }
                var result = ViewModel.TryAddWord(text!);
                PostWebPilot("actionResult", new
                {
                    status = WebPilotResultName(result),
                    index = result == VocabularyEditResult.Success ? ViewModel.Words.Count - 1 : -1
                }, requestId);
                return;
            }
            if (type is "moveWord" or "deleteWord")
            {
                if (!root.TryGetProperty("payload", out var target) ||
                    !WebPilotTryWordTarget(target, out int index))
                {
                    PostWebPilot("actionResult", new { status = "stale" }, requestId);
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
                        PostWebPilot("actionResult", new { status = "invalid" }, requestId);
                        return;
                    }
                    result = ViewModel.TryMoveWord(word, direction);
                    if (result == VocabularyEditResult.Success) nextIndex = index + direction;
                }
                else result = ViewModel.TryDeleteWord(word);

                PostWebPilot("actionResult", new
                {
                    status = WebPilotResultName(result),
                    index = nextIndex
                }, requestId);
                return;
            }
            if (type == "importVocabulary")
            {
                if (!ViewModel.CanWriteVocabulary)
                {
                    PostWebPilot("actionResult", new { status = "unavailable" }, requestId);
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
                    PostWebPilot("actionResult", new { status = "cancelled" }, requestId);
                    return;
                }
                // A modal dialog may allow a library/article switch. Require
                // the original committed context after it closes.
                if (expectedSession != webPilotSessionId ||
                    expectedRevision != webPilotVocabularyRevision)
                {
                    PostWebPilot("actionResult", new { status = "stale" }, requestId);
                    return;
                }
                var result = ViewModel.TryImportVocabulary(dialog.FileName);
                PostWebPilot("actionResult", new { status = WebPilotResultName(result) }, requestId);
                return;
            }
            // Audio, Script and remaining settings commands are stage 5/6.
            PostWebPilot("actionResult", new { status = "unavailable" }, requestId);
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException or FormatException)
        {
            AppLogger.Default.Warning("WebViewPilotInvalidMessage", error);
        }
    }

    private void WebPilotSettingIntent(string type, JsonElement root, string requestId)
    {
        if (!root.TryGetProperty("payload", out var payload) ||
            payload.ValueKind != JsonValueKind.Object)
        {
            PostWebPilot("actionResult", new { status = "invalid" }, requestId);
            return;
        }

        bool valid = false;
        switch (type)
        {
            case "setFontSize":
                if (payload.TryGetProperty("size", out var font) &&
                    font.ValueKind == JsonValueKind.Number &&
                    font.TryGetInt32(out int size) && size is 14 or 15 or 16)
                {
                    ApplyReadingFont(size);
                    valid = true;
                }
                break;
            case "setAccent":
                if (TryMessageString(payload, "accent", out string? accent) &&
                    accent is "uk" or "us")
                {
                    ViewModel.Accent = accent;
                    RefreshSettingsChecks();
                    valid = true;
                }
                break;
            case "setPanelWidth":
                if (payload.TryGetProperty("width", out var width) &&
                    width.ValueKind == JsonValueKind.Number &&
                    width.TryGetDouble(out double px) && double.IsFinite(px) &&
                    px >= 220 && px <= 1000)
                {
                    settings.RightWidth = Math.Round(px);
                    valid = true;
                }
                break;
            case "setForwardStep":
            case "setBackwardStep":
                if (payload.TryGetProperty("seconds", out var step) &&
                    step.ValueKind == JsonValueKind.Number &&
                    step.TryGetDouble(out double seconds) &&
                    (type == "setForwardStep"
                        ? seconds == 4 || seconds == 7 || seconds == 10
                        : seconds == 5 || seconds == 8 || seconds == 15))
                {
                    if (type == "setForwardStep") settings.ForwardStep = seconds;
                    else settings.BackwardStep = seconds;
                    RefreshSettingsChecks();
                    valid = true;
                }
                break;
        }
        if (!valid)
        {
            PostWebPilot("actionResult", new { status = "invalid" }, requestId);
            return;
        }
        bool saved = SaveSettings(player.Volume);
        SendWebPilotSettings();
        PostWebPilot("actionResult", new { status = saved ? "success" : "failed" }, requestId);
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
            paragraphs = article.Paragraphs.Select(p => p.Select(s => new
            {
                sid = s.Sid, text = s.Text, hasAudio = s.HasAudio
            }).ToArray()).ToArray()
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
                data.ValueKind != JsonValueKind.Object ||
                !data.TryGetProperty("position", out var time) ||
                time.ValueKind != JsonValueKind.Number ||
                !time.TryGetDouble(out double seconds) || !double.IsFinite(seconds) ||
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
        else if (type == "playSegment")
        {
            if (webPilotScriptMode ||
                !root.TryGetProperty("payload", out var target) ||
                !TryMessageString(target, "sid", out string? sid) ||
                !ViewModel.CanPlaySegment(sid!))
            {
                PostWebPilot("actionResult", new { status = "invalid" }, requestId);
                return;
            }
            ViewModel.PlaySegment(sid!);
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
            if (!TryMessageString(payload, "sid", out string? sid)) return false;
            var segment = ViewModel.Article?.Segments.FirstOrDefault(s => s.Sid == sid);
            if (segment is null) return false;
            // Passage blank markers alter the *visible* UTF-16 offsets.
            original = Regex.Replace(segment.Text, @"\[\[(\d+)\]\]",
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
        fontSize = settings.FontSize,
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
            isArticleActive = playback.IsArticleActive,
            playingSid = ViewModel.PlayingSid
        };
    }

    private object WebPilotNotificationSnapshot() => new
    {
        visible = ViewModel.Notifications.Visible.Select(n =>
            new { id = n.Id, message = n.Message, severity = n.Severity.ToString() }).ToArray(),
        overflowErrorCount = ViewModel.Notifications.OverflowErrors.Count,
        overflowErrors = ViewModel.Notifications.OverflowErrors.Select(n =>
            new { id = n.Id, message = n.Message, severity = n.Severity.ToString() }).ToArray(),
        // Error popups also fade visually. Their underlying records remain
        // accessible after fading until the user explicitly acknowledges them.
        errors = ViewModel.Notifications.Messages.Where(n => n.Severity == NotificationSeverity.Error)
            .Select(n => new { id = n.Id, message = n.Message, severity = n.Severity.ToString() }).ToArray()
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
        webPilotVocabularyIdentity = ViewModel.Article?.Vocabulary;
        webPilotSeenRequests.Clear();
        if (webPilotReady)
        {
            WebPilotOverlay.Visibility = Visibility.Collapsed;
            SendWebPilotInitialize();
        }
    }

    private void WebPilotVocabularyChanged()
    {
        var identity = ViewModel.Article?.Vocabulary;
        if (ReferenceEquals(webPilotVocabularyIdentity, identity))
        {
            SendWebPilotSettings();
            return;
        }
        webPilotVocabularyIdentity = identity;
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
        if (args.PropertyName == nameof(ViewModel.Accent))
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
            WebPilotFailure("WebView2 通信失败，已切回 WPF 阅读界面。");
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
        WebPilotOverlay.Visibility = Visibility.Collapsed;
        // WPF is the browser-failure fallback, including its Toast timing.
        NotificationHost.Attach(ViewModel.Notifications);
        ViewModel.Notify("WebViewPilotUnavailable", message, NotificationSeverity.Error);
        MessageBox.Show(this, message, "ReadArticles", MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    private void CloseWebPilot()
    {
        if (!webPilotEnabled) return;
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
