using System.IO;
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
    private readonly string webPilotSessionId = Guid.NewGuid().ToString("N");
    private string? webPilotPageId;
    private bool webPilotEnabled;
    private bool webPilotReady;
    private bool webPilotClosed;

    // Migration-only pilot. All original WPF controls and business commands remain intact.
    private void InitializeWebPilot()
    {
        if (!webPilotEnabled) return;
        ViewModel.ArticleChanged += WebPilotArticleChanged;
        ViewModel.VocabularyChanged += WebPilotVocabularyChanged;
        ViewModel.PlaybackChanged += SendWebPilotPlayback;
        ViewModel.Notifications.Changed += SendWebPilotNotifications;
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
                    args.Cancel = true;
            };
            core.NewWindowRequested += (_, args) => args.Handled = true;
            core.ProcessFailed += (_, args) =>
            {
                // A failed browser process never commits a business-state change.
                WebPilotFailure("WebView2 进程异常，已切回 WPF 阅读界面。");
            };
            core.Navigate(WebPilotDocument);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException
            or InvalidOperationException or ArgumentException or COMException or
            CoreWebView2RuntimeNotFoundException)
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
        if (webPilotClosed || !string.Equals(args.Source, WebPilotDocument, StringComparison.OrdinalIgnoreCase))
            return;
        try
        {
            using var json = JsonDocument.Parse(args.WebMessageAsJson);
            JsonElement root = json.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("version", out var version) || version.GetInt32() != 1 ||
                !root.TryGetProperty("type", out var type) ||
                !root.TryGetProperty("pageId", out var page) ||
                page.ValueKind != JsonValueKind.String)
                return;

            string? pageId = page.GetString();
            if (pageId is null || pageId.Length is < 16 or > 100) return;
            switch (type.GetString())
            {
                case "ready" when !webPilotReady:
                    webPilotPageId = pageId;
                    webPilotReady = true;
                    SendWebPilotInitialize();
                    break;
                case "rendered" when webPilotReady && pageId == webPilotPageId:
                    if (root.TryGetProperty("sessionId", out var session) &&
                        session.GetString() == webPilotSessionId)
                        WebPilotOverlay.Visibility = Visibility.Visible;
                    break;
                // No mutation commands are accepted by the pilot. Stage 4 adds
                // validated action handlers and explicit success/failure replies.
            }
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException or FormatException)
        {
            AppLogger.Default.Warning("WebViewPilotInvalidMessage", error);
        }
    }

    private void SendWebPilotInitialize()
    {
        if (!webPilotReady) return;
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
        var words = ViewModel.Words.Select(w => new
        {
            word = w.Word,
            phoneticUk = w.PhoneticUk,
            phoneticUs = w.PhoneticUs,
            meanings = w.Meanings.Select(m => new { pos = m.Pos, text = m.Text }).ToArray(),
            canPlayUk = w.CanPlayUk,
            canPlayUs = w.CanPlayUs
        }).ToArray();
        PostWebPilot("initialize", new
        {
            article = passage,
            vocabulary = words,
            settings = new
            {
                fontSize = settings.FontSize,
                accent = ViewModel.Accent,
                highlightsVisible = ViewModel.HighlightsVisible,
                rightWidth = settings.RightWidth
            },
            playback = WebPilotPlaybackSnapshot(),
            notifications = WebPilotNotificationSnapshot()
        });
    }

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
        if (webPilotReady) SendWebPilotInitialize();
    }

    private void WebPilotVocabularyChanged()
    {
        if (webPilotReady) SendWebPilotInitialize();
    }

    private void SendWebPilotPlayback()
    {
        if (webPilotReady) PostWebPilot("setPlayback", WebPilotPlaybackSnapshot());
    }

    private void SendWebPilotNotifications()
    {
        if (webPilotReady) PostWebPilot("setNotifications", WebPilotNotificationSnapshot());
    }

    private void PostWebPilot(string type, object payload)
    {
        if (!webPilotReady || webPilotClosed || webPilotPageId is null ||
            ReaderWebView.CoreWebView2 is null) return;
        string json = JsonSerializer.Serialize(new
        {
            version = 1,
            type,
            pageId = webPilotPageId,
            sessionId = webPilotSessionId,
            payload
        });
        ReaderWebView.CoreWebView2.PostWebMessageAsJson(json);
    }

    private void WebPilotFailure(string message)
    {
        if (webPilotClosed) return;
        webPilotReady = false;
        webPilotPageId = null;
        WebPilotOverlay.Visibility = Visibility.Collapsed;
        ViewModel.Notify("WebViewPilotUnavailable", message, NotificationSeverity.Error);
        MessageBox.Show(this, message, "ReadArticles", MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    private void CloseWebPilot()
    {
        webPilotClosed = true;
        ViewModel.ArticleChanged -= WebPilotArticleChanged;
        ViewModel.VocabularyChanged -= WebPilotVocabularyChanged;
        ViewModel.PlaybackChanged -= SendWebPilotPlayback;
        ViewModel.Notifications.Changed -= SendWebPilotNotifications;
        Loaded -= WebPilotLoaded;
        ReaderWebView.Dispose();
    }
}
