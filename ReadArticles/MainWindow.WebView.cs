using System.IO;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using ReadArticles.ViewModels;
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
                "setFontSize" or "setAccent" or "setPanelWidth" or "notificationDismiss"))
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
                if (actionSession != webPilotSessionId ||
                    value != webPilotVocabularyRevision)
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
            !TryMessageString(payload, "sid", out string? sid) ||
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
        var segment = ViewModel.Article?.Segments.FirstOrDefault(s => s.Sid == sid);
        if (segment is null) return false;

        // Match the exact Passage display text. Blank markers change visible
        // UTF-16 offsets and must not be compared against source JSON offsets.
        string visible = Regex.Replace(segment.Text, @"\[\[(\d+)\]\]",
            match => "____" + match.Groups[1].Value + "____");
        if (offset > visible.Length || length > visible.Length - offset)
            return false;
        string normalized = Regex.Replace(visible.Substring(offset, length), @"\s+", " ").Trim();
        if (!string.Equals(candidate, normalized, StringComparison.Ordinal))
            return false;
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
        var article = ViewModel.Article;
        var passage = article is null ? null : new
        {
            title = article.Title,
            paragraphs = article.Paragraphs.Select(p => p.Select(s => new
            {
                sid = s.Sid,
                text = s.Text,
                hasAudio = s.HasAudio
            }).ToArray()).ToArray()
        };
        PostWebPilot("initialize", new
        {
            article = passage,
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
        rightWidth = settings.RightWidth
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
            playingSid = ViewModel.PlayingSid
        };
    }

    private object WebPilotNotificationSnapshot() => new
    {
        visible = ViewModel.Notifications.Visible.Select(n =>
            new { id = n.Id, message = n.Message, severity = n.Severity.ToString() }).ToArray(),
        overflowErrorCount = ViewModel.Notifications.OverflowErrors.Count
    };

    private void WebPilotArticleChanged()
    {
        // A previous display is never presented as the newly committed article.
        webPilotSessionId = Guid.NewGuid().ToString("N");
        webPilotVocabularyRevision = 0;
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
