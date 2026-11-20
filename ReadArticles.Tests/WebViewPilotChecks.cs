using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Web.WebView2.Wpf;
using ReadArticles.Infrastructure;
using ReadArticles.Services;

internal static class WebViewPilotChecks
{
    internal static void Run()
    {
        Program.Run("WebView2 pilot assets embedded in application assembly", () =>
        {
            Assembly assembly = typeof(ReadArticles.MainWindow).Assembly;
            foreach (string name in new[]
            {
                "ReadArticles.web.reader.html",
                "ReadArticles.web.reader.css",
                "ReadArticles.web.reader.js",
                "ReadArticles.web.icons.play.svg",
                "ReadArticles.web.icons.pause.svg",
                "ReadArticles.web.icons.stop.svg",
                "ReadArticles.web.icons.sound.svg",
                "ReadArticles.web.icons.import.svg",
                "ReadArticles.web.icons.delete.svg",
                "ReadArticles.web.icons.move-up.svg",
                "ReadArticles.web.icons.move-down.svg",
                "ReadArticles.web.icons.show-script.svg",
                "ReadArticles.web.icons.show-passage.svg",
                "ReadArticles.web.icons.show-vocabulary.svg",
                "ReadArticles.web.icons.not-show-vocabulary.svg",
                "ReadArticles.web.icons.selection-add.svg"
            })
            {
                using Stream? stream = assembly.GetManifestResourceStream(name);
                Program.Check(stream is not null && stream.Length > 0);
            }
        });
        Program.Run("WebView2 failure shows native terminal screen instead of WPF fallback", () =>
        {
            var window = new ReadArticles.MainWindow(persistSettings: false);
            try
            {
                var fail = typeof(ReadArticles.MainWindow).GetMethod(
                    "WebPilotFailure", BindingFlags.Instance | BindingFlags.NonPublic);
                Program.Check(fail is not null);
                fail!.Invoke(window, ["WebView2 initialization failed"]);
                var failurePanel = (Grid)window.FindName("WebFailurePanel");
                var legacyOverlay = (Grid)window.FindName("WebPilotOverlay");
                var failureMessage = (TextBlock)window.FindName("WebFailureMessage");
                var exitButton = (Button)window.FindName("WebFailureExitButton");
                Program.Check(failurePanel.Visibility == Visibility.Visible &&
                    legacyOverlay.Visibility == Visibility.Collapsed &&
                    failureMessage.Text.Contains("initialization failed") &&
                    exitButton.IsEnabled && window.ViewModel.Notifications.Messages.Count == 0);
            }
            finally { window.Close(); }
        });
        Program.Run("WebView2 article changes require no legacy WPF visual tree", () =>
        {
            var window = new ReadArticles.MainWindow(persistSettings: false);
            try
            {
                Program.Check(window.ViewModel.OpenArticle(TestData.ArticlePath));
                Program.Check(window.ViewModel.Article is not null &&
                    window.ViewModel.Words.Count > 0);
                Program.Check(window.FindName("ParagraphHost") is null &&
                    window.FindName("VocabularyList") is null &&
                    window.FindName("NotificationHost") is null);
                window.ViewModel.HighlightsVisible = true;
                Program.Check(window.FindName("ParagraphHost") is null);
            }
            finally { window.Close(); }
        });
    }

    // Real WebView2 coverage must precede removal of legacy WPF visual tests.
    // This is deliberately separate from Run(), which also serves Core tests.
    internal static void RunBrowser()
    {
        Program.Run("WebView2 renders passage, vocabulary, Script and notifications in real DOM", () =>
        {
            string directory = Path.Combine(Path.GetTempPath(),
                "ReadArticles-webview-dom-" + Guid.NewGuid().ToString("N"));
            var window = new ReadArticles.MainWindow(persistSettings: false)
            {
                ShowActivated = false
            };
            try
            {
                Directory.CreateDirectory(directory);
                string article = Path.Combine(directory, "WebViewFixture.json");
                File.Copy(TestData.ArticlePath, article);
                string originalWords = Path.ChangeExtension(TestData.ArticlePath, ".vocabulary.json");
                if (File.Exists(originalWords))
                    File.Copy(originalWords, Path.ChangeExtension(article, ".vocabulary.json"));
                File.WriteAllText(ArticleRepository.ScriptPathFor(article), """
                    {
                      "filetype": "script",
                      "title": "Script DOM fixture",
                      "paragraphs": [[{
                        "text": "It is a beautiful day.",
                        "timed_units": [["It is a",1.0,1.8],["beautiful",1.8,2.4],["day",2.4,2.8]]
                      }]]
                    }
                    """);

                window.Show();
                Program.Check(window.ViewModel.OpenArticle(article));
                var browser = (WebView2)window.FindName("ReaderWebView");
                var overlay = (System.Windows.Controls.Grid)window.FindName("WebPilotOverlay");
                WaitFor(() => browser.CoreWebView2 is not null &&
                    overlay.Visibility == System.Windows.Visibility.Visible, "browser render handshake");

                JsonElement passage = Evaluate(browser, """
                    (() => ({
                      title: document.getElementById('article-title').textContent,
                      paragraphs: document.querySelectorAll('#article-body .article-paragraph').length,
                      sentences: document.querySelectorAll('#article-body .sentence[data-sid]').length,
                      words: document.querySelectorAll('#vocabulary-list .word-row').length,
                      playable: !document.getElementById('article-play').disabled
                    }))()
                    """);
                // ArticleRepository derives Passage titles from the opened filename.
                // The isolated test copy is WebViewFixture.json, not the source name.
                string expectedTitle = Path.GetFileNameWithoutExtension(article);
                bool validPassage = passage.GetProperty("title").GetString() == expectedTitle &&
                    passage.GetProperty("paragraphs").GetInt32() == 13 &&
                    passage.GetProperty("sentences").GetInt32() == 62 &&
                    passage.GetProperty("words").GetInt32() == 120 &&
                    passage.GetProperty("playable").GetBoolean() &&
                    window.FindName("ParagraphHost") is null;
                if (!validPassage)
                    throw new Exception($"Unexpected Passage DOM: {passage.GetRawText()}; " +
                        $"expectedTitle={expectedTitle}, nativeReaderHost={window.FindName("ParagraphHost")}");

                WaitFor(() => Evaluate(browser, """
                    [...document.querySelectorAll('.toolbar button img')].every(img =>
                        img.complete && img.naturalWidth > 0)
                    """).GetBoolean(), "embedded toolbar icons loaded");
                JsonElement visual = Evaluate(browser, """
                    (() => ({
                      popupGone: !document.getElementById('reader-settings-panel') &&
                                 !document.getElementById('reader-settings-toggle'),
                      defaultSize: getComputedStyle(document.getElementById('article-title')).fontSize,
                      icon: document.querySelector('#article-play img')?.getAttribute('src'),
                      grouped: document.querySelectorAll('.phonetic-line .phonetic-unit').length >= 2,
                      images: [...document.querySelectorAll('.toolbar button img')].every(img => img.complete && img.naturalWidth > 0)
                    }))()
                    """);
                Program.Check(visual.GetProperty("popupGone").GetBoolean() &&
                    visual.GetProperty("defaultSize").GetString() == "18px" &&
                    visual.GetProperty("icon").GetString() == "icons/play.svg" &&
                    visual.GetProperty("grouped").GetBoolean() &&
                    visual.GetProperty("images").GetBoolean());

                // Test actual browser bounds and the two word-divider drag limits.
                JsonElement widths = Evaluate(browser, """
                    (() => {
                      const article = document.getElementById('article-scroll');
                      const vocabulary = document.getElementById('vocabulary-scroll');
                      const splitter = document.getElementById('reader-splitter');
                      const original = currentSettings.rightWidth;
                      applyPaneWidth(200);
                      const minimum = vocabulary.getBoundingClientRect().width;
                      const maximumAllowed = Math.max(200, Math.min(400,
                        Math.floor(document.querySelector('.workspace').clientWidth - 203)));
                      applyPaneWidth(maximumAllowed);
                      const maximum = vocabulary.getBoundingClientRect().width;
                      applyPaneWidth(original);
                      return {
                        article: article.getBoundingClientRect().width,
                        vocabulary: vocabulary.getBoundingClientRect().width,
                        splitter: splitter.getBoundingClientRect().width,
                        minimum, maximum
                      };
                    })()
                    """);
                Program.Check(widths.GetProperty("article").GetDouble() >= 200 &&
                    widths.GetProperty("vocabulary").GetDouble() >= 200 &&
                    widths.GetProperty("vocabulary").GetDouble() <= 400 &&
                    widths.GetProperty("splitter").GetDouble() == 3 &&
                    widths.GetProperty("minimum").GetDouble() == 200 &&
                    widths.GetProperty("maximum").GetDouble() <= 400);

                // Native and browser widths are coordinated by one layout solver.
                // Expanding left consumes article space before vocabulary space.
                double previousWindowWidth = window.Width;
                try
                {
                    window.Width = Math.Min(window.Width, Math.Max(window.MinWidth, 820));
                    WpfTestHelpers.Pump();
                    var settingsField = typeof(ReadArticles.MainWindow).GetField("settings",
                        BindingFlags.Instance | BindingFlags.NonPublic)!;
                    var paneSettings = (ReaderSettings)settingsField.GetValue(window)!;
                    var resize = typeof(ReadArticles.MainWindow).GetMethod("ResizeLibraryPane",
                        BindingFlags.Instance | BindingFlags.NonPublic)!;
                    double initialRight = paneSettings.RightWidth;
                    resize.Invoke(window, [400d]);
                    WaitFor(() => Math.Abs(Evaluate(browser, """
                        document.getElementById('vocabulary-scroll').getBoundingClientRect().width
                        """).GetDouble() - paneSettings.RightWidth) < 2,
                        "coordinated vocabulary width after library resize");
                    Program.Check(paneSettings.RightWidth <= initialRight);
                    resize.Invoke(window, [200d]);
                    WaitFor(() => Math.Abs(Evaluate(browser, """
                        document.getElementById('vocabulary-scroll').getBoundingClientRect().width
                        """).GetDouble() - paneSettings.RightWidth) < 2,
                        "vocabulary stays steady when library shrinks");

                    // Reverse direction: a right-divider intent may compress
                    // the native library, but the browser paints only C#'s result.
                    var resizeRight = typeof(ReadArticles.MainWindow).GetMethod(
                        "ResizeVocabularyPane", BindingFlags.Instance | BindingFlags.NonPublic)!;
                    resizeRight.Invoke(window, [400d]);
                    WaitFor(() => Math.Abs(Evaluate(browser, """
                        document.getElementById('vocabulary-scroll').getBoundingClientRect().width
                        """).GetDouble() - paneSettings.RightWidth) < 2,
                        "confirmed vocabulary width after right-divider resize");
                    var nativeLeft = (ColumnDefinition)window.FindName("LeftColumn");
                    Program.Check(Math.Abs(nativeLeft.ActualWidth - paneSettings.LeftWidth) < 2);
                }
                finally
                {
                    window.Width = previousWindowWidth;
                    WpfTestHelpers.Pump();
                }

                // Scrolling the long Passage and its 120-word list must be independent.
                JsonElement scroll = Evaluate(browser, """
                    (() => {
                      const articlePane = document.getElementById('article-scroll');
                      const wordsPane = document.getElementById('vocabulary-scroll');
                      articlePane.scrollTop = 0;
                      wordsPane.scrollTop = 0;
                      articlePane.scrollTop = articlePane.scrollHeight;
                      const articleEnd = articlePane.scrollTop;
                      const wordsAfterArticle = wordsPane.scrollTop;
                      articlePane.scrollTop = 0;
                      wordsPane.scrollTop = wordsPane.scrollHeight;
                      return {
                        articleEnd,
                        wordsAfterArticle,
                        articleAfterWords: articlePane.scrollTop,
                        wordsEnd: wordsPane.scrollTop
                      };
                    })()
                    """);
                if (!(scroll.GetProperty("articleEnd").GetDouble() > 0 &&
                    scroll.GetProperty("wordsAfterArticle").GetDouble() == 0 &&
                    scroll.GetProperty("articleAfterWords").GetDouble() == 0 &&
                    scroll.GetProperty("wordsEnd").GetDouble() > 0))
                    throw new Exception("Browser panes did not scroll independently: " +
                        scroll.GetRawText());

                // Check browser matching boundaries and longest-phrase preference.
                string phrase = "public life. public's public-life2";
                string[] words = ["public", "public life"];
                JsonElement browserMatches = Evaluate(browser,
                    "matchVocabulary(" + JsonSerializer.Serialize(phrase) + "," +
                    JsonSerializer.Serialize(words) + ")");
                Program.Check(browserMatches.GetArrayLength() == 1 &&
                    browserMatches[0].GetProperty("start").GetInt32() == 0 &&
                    browserMatches[0].GetProperty("length").GetInt32() == 11 &&
                    browserMatches[0].GetProperty("key").GetString() == "public life");
                JsonElement boundary = Evaluate(browser, "matchVocabulary(" +
                    JsonSerializer.Serialize("politician's politician’s 2public public-life") + "," +
                    JsonSerializer.Serialize(new[] { "politician", "public" }) + ")");
                Program.Check(boundary.GetArrayLength() == 1 &&
                    boundary[0].GetProperty("key").GetString() == "public" &&
                    boundary[0].GetProperty("start").GetInt32() == 27);

                // Preserve Python-compatible numeric boundaries (digits are not letters).
                JsonElement numericBoundary = Evaluate(browser,
                    "matchVocabulary('public2 2public', ['public'])");
                Program.Check(numericBoundary.GetArrayLength() == 2 &&
                    numericBoundary[0].GetProperty("start").GetInt32() == 0 &&
                    numericBoundary[1].GetProperty("start").GetInt32() == 9);

                Evaluate(browser, "document.getElementById('highlight-toggle').click(); true");
                WaitFor(() => window.ViewModel.HighlightsVisible &&
                    Evaluate(browser, "document.getElementById('highlight-toggle').getAttribute('aria-pressed')")
                        .GetString() == "true", "browser vocabulary highlighting");
                Program.Check(Evaluate(browser,
                    "Boolean(CSS.highlights && CSS.highlights.has('reader-vocabulary'))")
                    .GetBoolean());
                Evaluate(browser, "document.querySelector('#vocabulary-list .word-row').click(); true");
                Program.Check(Evaluate(browser,
                    "document.querySelectorAll('#vocabulary-list .word-row.selected').length")
                    .GetInt32() == 1);

                // Keyboard activation of ARIA word buttons must never trigger article playback.
                JsonElement wordKeys = Evaluate(browser, """
                    (() => {
                      const rows = [...document.querySelectorAll('#vocabulary-list .word-row')];
                      const press = (row, key) => {
                        row.focus();
                        const event = new KeyboardEvent('keydown',
                          { key, bubbles: true, cancelable: true });
                        row.dispatchEvent(event);
                        return event.defaultPrevented && row.classList.contains('selected');
                      };
                      return { space: press(rows[1], ' '), enter: press(rows[0], 'Enter') };
                    })()
                    """);
                Program.Check(wordKeys.GetProperty("space").GetBoolean() &&
                    wordKeys.GetProperty("enter").GetBoolean() &&
                    !window.ViewModel.Playback.IsArticleActive);

                // The native modal is the sole settings entry. Its validated
                // draft must update the browser without a second HTML form.
                var readSettings = (ReaderSettings)typeof(ReadArticles.MainWindow)
                    .GetField("settings", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .GetValue(window)!;
                var draft = readSettings.Copy();
                draft.Appearance.Body.Size = 16;
                draft.Appearance.Body.Background = "#FFF8E7";
                var applyDialog = typeof(ReadArticles.MainWindow).GetMethod(
                    "ApplyDialogSettings", BindingFlags.Instance | BindingFlags.NonPublic)!;
                Program.Check((bool)applyDialog.Invoke(window, [draft])!);
                WaitFor(() => Evaluate(browser,
                    "document.documentElement.style.getPropertyValue('--body-size')")
                    .GetString() == "16px", "native font-size setting roundtrip");
                Program.Check(Evaluate(browser,
                    "document.documentElement.style.getPropertyValue('--reading-bg')")
                    .GetString() == "#FFF8E7");

                var notice = window.ViewModel.Notifications.Publish("dom-test",
                    "WebView2 DOM notification", NotificationSeverity.Error);
                Program.Check(notice is not null);
                WaitFor(() => Evaluate(browser,
                    "document.querySelectorAll('#notification-cards .notice.error').length")
                    .GetInt32() == 1, "browser error notification");
                window.ViewModel.Notifications.Dismiss(notice!.Id);
                WaitFor(() => Evaluate(browser,
                    "document.querySelectorAll('#notification-cards .notice.error').length")
                    .GetInt32() == 0, "browser notification dismissal");

                Evaluate(browser, "document.getElementById('script-toggle').click(); true");
                WaitFor(() => Evaluate(browser, """
                    (() => document.getElementById('script-toggle').getAttribute('aria-pressed') === 'true' &&
                      !document.getElementById('script-toggle').disabled &&
                      document.querySelectorAll('#article-body .script-paragraph > .script-sentence .script-timed-unit').length === 3)()
                    """).GetBoolean(), "Script DOM switch");
                Program.Check(Evaluate(browser,
                    "document.getElementById('article-title').textContent").GetString() ==
                    "Script DOM fixture");
                // Script timing has a real SVG clip geometry rather than only DOM text.
                WaitFor(() => Evaluate(browser, """
                    (() => {
                      const row = document.querySelector('#article-body .script-paragraph');
                      const paint = row?.querySelector(':scope > .script-paint');
                      const clip = row?.querySelector(':scope > .script-clips clipPath');
                      const redUnit = paint?.querySelector('.script-timed-unit');
                      // Chromium serializes CSS url(#id) as url("#id"). Compare the
                      // referenced ID, not the browser-specific quoting style.
                      const clipReference = paint?.style.clipPath.replace(/["']/g, '');
                      return Boolean(paint && clip && clip.id.startsWith('script-clip-') &&
                        clip.querySelectorAll('rect').length >= 3 &&
                        clipReference === 'url(#' + clip.id + ')' &&
                        redUnit && getComputedStyle(redUnit).color === 'rgb(184, 26, 53)');
                    })()
                    """).GetBoolean(), "Script SVG clipping and foreground");
                // Exercise the actual SVG clipping update at 0%, 50%, 100%, and stop.
                JsonElement sweep = Evaluate(browser, """
                    (() => {
                      const rect = document.querySelector(
                        '#article-body .script-paragraph .script-clips clipPath rect');
                      if (!rect || typeof paintScriptProgress !== 'function') return null;
                      const widthAt = (seconds, active = true) => {
                        paintScriptProgress({ isArticleActive: active,
                          canSeek: active, position: seconds });
                        return Number(rect.getAttribute('width'));
                      };
                      return { start: widthAt(1.0), middle: widthAt(1.4),
                        end: widthAt(1.8), stopped: widthAt(1.8, false) };
                    })()
                    """);
                if (sweep.ValueKind != JsonValueKind.Object ||
                    sweep.GetProperty("start").GetDouble() != 0 ||
                    sweep.GetProperty("middle").GetDouble() <= 0 ||
                    sweep.GetProperty("end").GetDouble() <=
                        sweep.GetProperty("middle").GetDouble() ||
                    sweep.GetProperty("stopped").GetDouble() != 0)
                    throw new Exception("Unexpected Script SVG sweep: " + sweep.GetRawText());
                Evaluate(browser, "document.getElementById('script-toggle').click(); true");
                WaitFor(() => Evaluate(browser, """
                    (() => document.getElementById('script-toggle').getAttribute('aria-pressed') === 'false' &&
                      !document.getElementById('script-toggle').disabled &&
                      document.querySelectorAll('#article-body .article-paragraph').length === 13)()
                    """).GetBoolean(), "return to Passage DOM");
                Program.Check(window.FindName("ParagraphHost") is null);
            }
            finally
            {
                window.Close();
                WpfTestHelpers.Pump(100);
                if (Directory.Exists(directory)) Directory.Delete(directory, true);
            }
        });
    }

    private static JsonElement Evaluate(WebView2 browser, string script)
    {
        Task<string> evaluation = browser.CoreWebView2.ExecuteScriptAsync(script);
        WaitFor(() => evaluation.IsCompleted, "WebView2 JavaScript result");
        using var document = JsonDocument.Parse(evaluation.GetAwaiter().GetResult());
        return document.RootElement.Clone();
    }

    private static void WaitFor(Func<bool> condition, string operation)
    {
        var watch = Stopwatch.StartNew();
        while (!condition() && watch.Elapsed < TimeSpan.FromSeconds(20))
            WpfTestHelpers.Pump(30);
        if (!condition()) throw new Exception("Timed out waiting for " + operation);
    }
}
