using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using ReadArticles.Infrastructure;

namespace ReadArticles.Presentation;

// Native browser lifetime and restricted local transport; no business state.
public sealed class WebReaderHost : IDisposable
{
    private const string Origin = "https://readarticles.invalid";
    public const string Document = Origin + "/reader.html";
    private readonly WebView2 browser;
    private CoreWebView2? core;
    private bool closed;
    private bool failed;
    public bool IsUnavailable => closed || failed;
    public Dispatcher Dispatcher => browser.Dispatcher;
    public event Action? NavigationReset;
    public event Action<string>? MessageReceived;
    public event Action<string>? Failed;

    public WebReaderHost(WebView2 browser) => this.browser = browser;

    public async Task InitializeAsync()
    {
        try
        {
            string dataDirectory = Path.Combine(AppPaths.CacheDirectory, "webview2");
            var environment = await CoreWebView2Environment.CreateAsync(userDataFolder: dataDirectory);
            if (IsUnavailable) return;
            await browser.EnsureCoreWebView2Async(environment);
            if (IsUnavailable) return;
            core = browser.CoreWebView2;
            core.Settings.AreHostObjectsAllowed = false;
            core.Settings.AreDefaultContextMenusEnabled = false;
            core.Settings.AreDevToolsEnabled = false;
            core.Settings.IsStatusBarEnabled = false;
            core.AddWebResourceRequestedFilter(Origin + "/*", CoreWebView2WebResourceContext.All);
            core.WebResourceRequested += ResourceRequested;
            core.WebMessageReceived += ReceiveMessage;
            core.NavigationStarting += NavigationStarting;
            core.NewWindowRequested += NewWindowRequested;
            core.NavigationCompleted += NavigationCompleted;
            core.ProcessFailed += ProcessFailed;
            core.Navigate(Document);
        }
        catch (Exception error)
        {
            AppLogger.Default.Warning("WebViewPilotInitializationFailed", error);
            Fail("WebView2 无法初始化。请检查 Evergreen Runtime 安装或用户数据目录权限，然后重新启动程序。");
        }
    }

    private void NavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs args)
    {
        if (IsUnavailable || !string.Equals(args.Uri, Document, StringComparison.OrdinalIgnoreCase))
        {
            args.Cancel = true;
            return;
        }
        NavigationReset?.Invoke();
    }

    private void NewWindowRequested(object? sender, CoreWebView2NewWindowRequestedEventArgs args) =>
        args.Handled = true;

    private void NavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs args)
    {
        if (!args.IsSuccess) Fail("WebView2 本地页面加载失败。请重新启动 ReadArticles。");
    }

    private void ProcessFailed(object? sender, CoreWebView2ProcessFailedEventArgs args) =>
        Fail("WebView2 进程异常。请重新启动 ReadArticles。");

    private void ResourceRequested(object? sender, CoreWebView2WebResourceRequestedEventArgs args)
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
            typeof(WebReaderHost).Assembly.GetManifestResourceStream(resource);
        int status = stream is null ? 404 : 200;
        stream ??= new MemoryStream(Encoding.UTF8.GetBytes("Not found"));
        string headers = $"Content-Type: {mime}; charset=utf-8\r\n" +
            "Cache-Control: no-store\r\nX-Content-Type-Options: nosniff\r\n" +
            "Content-Security-Policy: default-src 'none'; script-src 'self'; style-src 'self'; img-src 'self' data:; connect-src 'none'; frame-src 'none'; base-uri 'none'; form-action 'none'\r\n";
        args.Response = core!.Environment.CreateWebResourceResponse(
            stream, status, status == 200 ? "OK" : "Not Found", headers);
    }

    private void ReceiveMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs args)
    {
        if (IsUnavailable || !string.Equals(args.Source, Document, StringComparison.OrdinalIgnoreCase))
            return;
        try { MessageReceived?.Invoke(args.WebMessageAsJson); }
        catch (Exception error) when (error is InvalidOperationException or FormatException)
        {
            AppLogger.Default.Warning("WebViewPilotInvalidMessage", error);
        }
    }

    public void SendJson(string json)
    {
        if (IsUnavailable || core is null) return;
        core.PostWebMessageAsJson(json);
    }

    public void Fail(string message)
    {
        if (IsUnavailable) return;
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(new Action(() => Fail(message)));
            return;
        }
        failed = true;
        Failed?.Invoke(message);
        AppLogger.Default.Warning("WebViewPilotUnavailable");
    }

    public void Dispose()
    {
        if (closed) return;
        closed = true;
        if (core is not null)
        {
            core.WebResourceRequested -= ResourceRequested;
            core.WebMessageReceived -= ReceiveMessage;
            core.NavigationStarting -= NavigationStarting;
            core.NewWindowRequested -= NewWindowRequested;
            core.NavigationCompleted -= NavigationCompleted;
            core.ProcessFailed -= ProcessFailed;
            core = null;
        }
        browser.Dispose();
    }
}
