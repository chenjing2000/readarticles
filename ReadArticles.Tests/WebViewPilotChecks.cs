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
                      sentences: document.querySelectorAll('#article-body .sentence[data-sentence-index]').length,
                      words: document.querySelectorAll('#vocabulary-list .word-row').length,
                      playable: !document.getElementById('article-play').disabled
                    }))()
                    """);
                // Passage titles come exclusively from JSON, never from the test copy's filename.
                string expectedTitle = "When America walks away";
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
                      bottomHeight: Math.round(document.querySelector('.article-pane .toolbar').getBoundingClientRect().height),
                      wordBottomHeight: Math.round(document.querySelector('.vocabulary-toolbar').getBoundingClientRect().height),
                      stoppedLocked: document.getElementById('progress').disabled,
                      icon: document.querySelector('#article-play img')?.getAttribute('src'),
                      grouped: document.querySelectorAll('.phonetic-line .phonetic-unit').length >= 2,
                      images: [...document.querySelectorAll('.toolbar button img')].every(img => img.complete && img.naturalWidth > 0)
                    }))()
                    """);
                Program.Check(visual.GetProperty("popupGone").GetBoolean() &&
                    visual.GetProperty("defaultSize").GetString() == "18px" &&
                    visual.GetProperty("icon").GetString() == "icons/play.svg" &&
                    visual.GetProperty("bottomHeight").GetInt32() == 48 &&
                    visual.GetProperty("wordBottomHeight").GetInt32() == 48 &&
                    visual.GetProperty("stoppedLocked").GetBoolean() &&
                    visual.GetProperty("grouped").GetBoolean() &&
                    visual.GetProperty("images").GetBoolean());

                // A focused progress slider must share the document's article-seek
                // shortcuts, not Chromium's native 0.05-second arrow increments.
                JsonElement seekKeys = Evaluate(browser, """
                    (() => {
                      const progress = document.getElementById('progress');
                      const previousPlayback = currentPlayback;
                      const previousSettings = currentSettings;
                      const previousDragging = seekDragging;
                      const originalSend = send;
                      const calls = [];
                      try {
                        send = (type, options = {}) => {
                          calls.push({ type, direction: options.payload?.direction });
                          return 'captured';
                        };
                        currentSettings = { ...currentSettings, backwardStep: 11, forwardStep: 7 };
                        seekDragging = false;
                        renderPlayback({
                          position: 120, duration: 300, canSeek: true,
                          canPlay: true, isArticleActive: true, state: 'paused'
                        });
                        progress.focus();
                        const focused = document.activeElement === progress;
                        const outline = getComputedStyle(progress).outlineStyle;
                        const fire = (target, key, extra = {}, type = "keydown") => {
                          const event = new KeyboardEvent(type, {
                            key, bubbles: true, cancelable: true, ...extra
                          });
                          target.dispatchEvent(event);
                          return event.defaultPrevented;
                        };
                        const left = fire(progress, 'ArrowLeft');
                        const right = fire(progress, 'ArrowRight');
                        const global = fire(document.getElementById('article-scroll'), 'ArrowRight');
                        const nativeStep = progress.step;
                        const button = document.getElementById('article-stop');
                        button.focus();
                        const space = fire(button, ' ');
                        const repeatSpace = fire(button, ' ', { repeat: true });
                        const up = fire(button, ' ', {}, 'keyup');
                        const repeatArrow = fire(button, 'ArrowLeft', { repeat: true });
                        const modified = fire(button, 'ArrowRight', { shiftKey: true });
                        const retainedFocus = document.activeElement === button;
                        renderPlayback({
                          position: 120, duration: 300, canSeek: false,
                          canPlay: true, isArticleActive: false, state: 'stopped'
                        });
                        const count = calls.length;
                        const stopped = fire(progress, 'ArrowRight');
                        currentPlayback = { ...currentPlayback, canPlay: false };
                        const noArticleSpace = fire(button, ' ');
                        return {
                          focused, outline, left, right, global, stopped,
                          nativeStep, count, calls, space, repeatSpace, up,
                          repeatArrow, modified, retainedFocus, noArticleSpace
                        };
                      } finally {
                        send = originalSend;
                        currentSettings = previousSettings;
                        seekDragging = previousDragging;
                        renderPlayback(previousPlayback);
                      }
                    })()
                    """);
                bool validKeys = seekKeys.GetProperty("focused").GetBoolean() &&
                    seekKeys.GetProperty("outline").GetString() == "none" &&
                    seekKeys.GetProperty("left").GetBoolean() &&
                    seekKeys.GetProperty("right").GetBoolean() &&
                    seekKeys.GetProperty("global").GetBoolean() &&
                    seekKeys.GetProperty("stopped").GetBoolean() &&
                    seekKeys.GetProperty("nativeStep").GetString() == "0.05" &&
                    seekKeys.GetProperty("space").GetBoolean() &&
                    seekKeys.GetProperty("repeatSpace").GetBoolean() &&
                    seekKeys.GetProperty("up").GetBoolean() &&
                    seekKeys.GetProperty("repeatArrow").GetBoolean() &&
                    !seekKeys.GetProperty("modified").GetBoolean() &&
                    seekKeys.GetProperty("retainedFocus").GetBoolean() &&
                    seekKeys.GetProperty("noArticleSpace").GetBoolean() &&
                    seekKeys.GetProperty("count").GetInt32() == 5 &&
                    seekKeys.GetProperty("calls").EnumerateArray().Select(call => (
                        Type: call.GetProperty("type").GetString(),
                        Direction: call.TryGetProperty("direction", out var direction) ? direction.GetString() : null))
                    .SequenceEqual(new (string? Type, string? Direction)[] {
                        (Type: "seekArticle", Direction: "backward"),
                        (Type: "seekArticle", Direction: "forward"),
                        (Type: "seekArticle", Direction: "forward"),
                        (Type: "playArticle", Direction: null),
                        (Type: "seekArticle", Direction: "backward")
                    });
                if (!validKeys)
                    throw new Exception("Unexpected progress keyboard outcome: " + seekKeys.GetRawText());

                // Reserved keys must not reach any focused DOM control or reveal a focus outline.
                JsonElement exclusiveKeys = Evaluate(browser, """
                    (() => {
                      const originalSend = send;
                      const previousPlayback = currentPlayback;
                      const targets = [
                        document.querySelector('.word-row'),
                        document.getElementById('highlight-toggle'),
                        document.getElementById('reader-splitter'),
                        document.getElementById('article-play')
                      ];
                      const calls = [];
                      let delivered = 0;
                      const listener = () => delivered++;
                      try {
                        send = (type, options = {}) => {
                          calls.push(type + (options.payload?.direction ?? ''));
                          return 'captured';
                        };
                        renderPlayback({ position: 120, duration: 300, canSeek: true,
                          canPlay: true, isArticleActive: true, state: 'paused' });
                        const results = targets.map(target => {
                          target.focus();
                          const focused = document.activeElement === target;
                          target.addEventListener('keydown', listener);
                          target.addEventListener('keyup', listener);
                          const before = calls.length;
                          const consumed = [' ', 'ArrowLeft', 'ArrowRight'].every(key =>
                            ['keydown', 'keyup'].every(type => {
                              const event = new KeyboardEvent(type, { key, bubbles: true, cancelable: true });
                              target.dispatchEvent(event);
                              return event.defaultPrevented;
                            }));
                          target.removeEventListener('keydown', listener);
                          target.removeEventListener('keyup', listener);
                          return focused && document.activeElement === target && consumed &&
                            getComputedStyle(target).outlineStyle === 'none' &&
                            calls.slice(before).join(',') === 'playArticle,seekArticlebackward,seekArticleforward';
                        });
                        return { all: results.every(Boolean), delivered, count: calls.length };
                      } finally {
                        send = originalSend;
                        renderPlayback(previousPlayback);
                      }
                    })()
                    """);
                Program.Check(exclusiveKeys.GetProperty("all").GetBoolean() &&
                    exclusiveKeys.GetProperty("delivered").GetInt32() == 0 &&
                    exclusiveKeys.GetProperty("count").GetInt32() == 12);

                // Time labels preserve 60:00 at one hour, then switch to hh:mm:ss.
                JsonElement timeFormats = Evaluate(browser, """
                    (() => ({
                      short: formatSeconds(625), exactHour: formatSeconds(3600),
                      overHour: formatSeconds(3601), long: formatSeconds(8100)
                    }))()
                    """);
                Program.Check(timeFormats.GetProperty("short").GetString() == "10:25" &&
                    timeFormats.GetProperty("exactHour").GetString() == "60:00" &&
                    timeFormats.GetProperty("overHour").GetString() == "01:00:01" &&
                    timeFormats.GetProperty("long").GetString() == "02:15:00");

                // A pointer click without change, cancellation, blur or stop
                // must not leave the range slider frozen in dragging mode.
                JsonElement dragState = Evaluate(browser, """
                    (() => {
                      const progress = document.getElementById('progress');
                      const previousPlayback = currentPlayback;
                      const previousDragging = seekDragging;
                      try {
                        const active = { position: 42, duration: 200, canSeek: true,
                          canPlay: true, isArticleActive: true, state: 'paused' };
                        renderPlayback(active);
                        progress.dispatchEvent(new Event('pointerdown', { bubbles: true }));
                        const held = seekDragging;
                        document.dispatchEvent(new Event('pointerup'));
                        const released = !seekDragging;
                        progress.dispatchEvent(new Event('pointerdown', { bubbles: true }));
                        document.dispatchEvent(new Event('pointercancel'));
                        const cancelled = !seekDragging;
                        progress.dispatchEvent(new Event('pointerdown', { bubbles: true }));
                        progress.dispatchEvent(new Event('blur'));
                        const blurred = !seekDragging;
                        progress.dispatchEvent(new Event('pointerdown', { bubbles: true }));
                        renderPlayback({ ...active, position: 0, canSeek: false, state: 'stopped',
                          isArticleActive: false });
                        const stopped = !seekDragging && progress.disabled && progress.value === '0';
                        return { held, released, cancelled, blurred, stopped };
                      } finally {
                        seekDragging = previousDragging;
                        renderPlayback(previousPlayback);
                      }
                    })()
                    """);
                Program.Check(dragState.GetProperty("held").GetBoolean() &&
                    dragState.GetProperty("released").GetBoolean() &&
                    dragState.GetProperty("cancelled").GetBoolean() &&
                    dragState.GetProperty("blurred").GetBoolean() &&
                    dragState.GetProperty("stopped").GetBoolean());

                // Selection-add anchors to the final fragment at +1px horizontally
                // and +0px vertically, with edge-safe left/above fallbacks.
                JsonElement selectionAction = Evaluate(browser, """
                    (() => {
                      const button = document.getElementById('selection-add');
                      const hidden = button.hidden;
                      button.hidden = false;
                      const rect = (left, top, right, bottom) => ({
                        left, top, right, bottom,
                        width: right - left, height: bottom - top
                      });
                      positionSelectionAddButton({
                        getClientRects: () => [
                          rect(90, 80, 150, 100),
                          rect(90, 110, 130, 130)
                        ]
                      }, button);
                      const afterLastLine = {
                        x: Number.parseInt(button.style.left),
                        y: Number.parseInt(button.style.top)
                      };
                      positionSelectionAddButton({
                        getClientRects: () => [
                          rect(innerWidth - 30, innerHeight - 30,
                            innerWidth - 10, innerHeight - 10)
                        ]
                      }, button);
                      const nearEdge = {
                        x: Number.parseInt(button.style.left),
                        y: Number.parseInt(button.style.top)
                      };
                      const style = getComputedStyle(button);
                      const result = {
                        width: style.width, height: style.height,
                        iconWidth: Number.parseFloat(getComputedStyle(button.querySelector('img')).width),
                        fontSize: Number.parseFloat(getComputedStyle(button).fontSize),
                        iconCornerAligned: (() => {
                          const box = button.getBoundingClientRect();
                          const icon = button.querySelector('img').getBoundingClientRect();
                          return Math.abs(box.left - icon.left) < 0.5 &&
                            Math.abs(box.top - icon.top) < 0.5;
                        })(),
                        afterLastLine, nearEdge
                      };
                      button.hidden = hidden;
                      return result;
                    })()
                    """);
                Program.Check(selectionAction.GetProperty("width").GetString() == "24px" &&
                    selectionAction.GetProperty("height").GetString() == "24px" &&
                    Math.Abs(selectionAction.GetProperty("iconWidth").GetDouble() -
                        selectionAction.GetProperty("fontSize").GetDouble() * 0.65) < 0.1 &&
                    selectionAction.GetProperty("iconCornerAligned").GetBoolean() &&
                    selectionAction.GetProperty("afterLastLine").GetProperty("x").GetInt32() == 131 &&
                    selectionAction.GetProperty("afterLastLine").GetProperty("y").GetInt32() == 130 &&
                    selectionAction.GetProperty("nearEdge").GetProperty("x").GetInt32() ==
                        Evaluate(browser, "innerWidth").GetInt32() - 55 &&
                    selectionAction.GetProperty("nearEdge").GetProperty("y").GetInt32() ==
                        Evaluate(browser, "innerHeight").GetInt32() - 54);

                // The frame-paced loop checks geometry at up to 60Hz, even
                // without another selectionchange, and clears when selection ends.
                JsonElement selectionTracking = Evaluate(browser, """
                    (() => {
                      const sentence = document.querySelector('#article-body .sentence[data-sentence-index]');
                      sentence.scrollIntoView({ block: 'center' });
                      const walker = document.createTreeWalker(sentence, NodeFilter.SHOW_TEXT);
                      let text;
                      while ((text = walker.nextNode()) && text.textContent.trim().length < 6) {}
                      if (!text) return { started: false, previousX: 0 };
                      const range = document.createRange();
                      range.setStart(text, 0);
                      range.setEnd(text, 6);
                      const selection = window.getSelection();
                      selection.removeAllRanges();
                      selection.addRange(range);
                      updateSelectionAdd();
                      const button = document.getElementById('selection-add');
                      const started = !button.hidden && selectionAddPositionFrame !== 0 &&
                        selectionAddRefreshHz === 60;
                      const previousX = Number.parseInt(button.style.left);
                      sentence.parentElement.style.transform = 'translateX(12px)';
                      return { started, previousX };
                    })()
                    """);
                Program.Check(selectionTracking.GetProperty("started").GetBoolean());
                int originalX = selectionTracking.GetProperty("previousX").GetInt32();
                WaitFor(() => Evaluate(browser,
                    "Number.parseInt(document.getElementById('selection-add').style.left)")
                    .GetInt32() >= originalX + 10, "repaint-paced selection tracking after layout shift");
                Program.Check(Evaluate(browser, """
                    (() => {
                      document.querySelector('#article-body .sentence[data-sentence-index]').parentElement.style.transform = '';
                      window.getSelection().removeAllRanges();
                      return true;
                    })()
                    """).GetBoolean());
                WaitFor(() => Evaluate(browser, """
                    selectionAddPositionFrame === 0 &&
                      document.getElementById('selection-add').hidden &&
                      selectedPassage === null
                    """).GetBoolean(), "selection-add disappears after selection ends without manual cleanup");

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

                // The icon reflects whether highlighting is active, not the action
                // available after clicking: gray for off, magenta for on.
                bool HighlightIconMatches(bool enabled)
                {
                    JsonElement state = Evaluate(browser, """
                        (() => {
                          const button = document.getElementById('highlight-toggle');
                          return {
                            pressed: button.getAttribute('aria-pressed'),
                            icon: button.querySelector('img').getAttribute('src')
                          };
                        })()
                        """);
                    return state.GetProperty("pressed").GetString() == (enabled ? "true" : "false") &&
                        state.GetProperty("icon").GetString() == (enabled
                            ? "icons/show-vocabulary.svg" : "icons/not-show-vocabulary.svg");
                }
                Program.Check(!window.ViewModel.HighlightsVisible && HighlightIconMatches(false));
                Evaluate(browser, "document.getElementById('highlight-toggle').click(); true");
                WaitFor(() => window.ViewModel.HighlightsVisible && HighlightIconMatches(true),
                    "browser vocabulary highlighting icon on");
                WaitFor(() => Evaluate(browser,
                    "document.querySelectorAll('#word-highlight-layer .word-highlight-rect').length > 0")
                    .GetBoolean(), "rounded vocabulary highlights");
                Program.Check(Evaluate(browser, """
                    (() => {
                      const box = document.querySelector('#word-highlight-layer .word-highlight-rect');
                      return box && getComputedStyle(box).borderRadius === '4px' &&
                        !CSS.highlights?.has('reader-vocabulary');
                    })()
                    """).GetBoolean());
                Evaluate(browser, "document.getElementById('highlight-toggle').click(); true");
                WaitFor(() => !window.ViewModel.HighlightsVisible && HighlightIconMatches(false) &&
                    Evaluate(browser, "document.querySelector('#word-highlight-layer') === null").GetBoolean(),
                    "browser vocabulary highlighting icon off");
                Evaluate(browser, "document.getElementById('highlight-toggle').click(); true");
                WaitFor(() => window.ViewModel.HighlightsVisible && HighlightIconMatches(true),
                    "browser vocabulary highlighting icon restored");
                Evaluate(browser, "document.querySelector('#vocabulary-list .word-row').click(); true");
                Program.Check(Evaluate(browser,
                    "document.querySelectorAll('#vocabulary-list .word-row.selected').length")
                    .GetInt32() == 1);

                // Space is reserved for article playback; Enter still activates a word row.
                JsonElement wordKeys = Evaluate(browser, """
                    (() => {
                      const rows = [...document.querySelectorAll('#vocabulary-list .word-row')];
                      const originalSend = send;
                      const calls = [];
                      try {
                        send = type => { calls.push(type); return 'captured'; };
                        const press = (row, key) => {
                          row.focus();
                          const event = new KeyboardEvent('keydown',
                            { key, bubbles: true, cancelable: true });
                          row.dispatchEvent(event);
                          return event.defaultPrevented;
                        };
                        const space = press(rows[1], ' ') && !rows[1].classList.contains('selected');
                        const enter = press(rows[1], 'Enter') && rows[1].classList.contains('selected');
                        press(rows[0], 'Enter');
                        return { space, enter, playback: calls.filter(type => type === 'playArticle').length };
                      } finally { send = originalSend; }
                    })()
                    """);
                Program.Check(wordKeys.GetProperty("space").GetBoolean() &&
                    wordKeys.GetProperty("enter").GetBoolean() &&
                    wordKeys.GetProperty("playback").GetInt32() == 1 &&
                    !window.ViewModel.Playback.IsArticleActive);

                // The native modal is the sole settings entry. Its validated
                // draft must update the browser without a second HTML form.
                var readSettings = (ReaderSettings)typeof(ReadArticles.MainWindow)
                    .GetField("settings", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .GetValue(window)!;
                var draft = readSettings.Copy();
                draft.Appearance.Body.Size = 16;
                draft.Appearance.Body.Background = "#F7F1E6";
                draft.Appearance.Title.Font = "Nunito";
                draft.Appearance.Body.Font = "Nunito";
                draft.Appearance.Vocabulary.Font = "Nunito";
                draft.Appearance.Body.SelectionAddSize = 0.80;
                draft.Appearance.Body.SelectionBackground = "#E5EDE0";
                draft.Appearance.Body.ScriptColor = "#856300";
                var applyDialog = typeof(ReadArticles.MainWindow).GetMethod(
                    "ApplyDialogSettings", BindingFlags.Instance | BindingFlags.NonPublic)!;
                Program.Check((bool)applyDialog.Invoke(window, [draft])!);
                WaitFor(() => Evaluate(browser,
                    "document.documentElement.style.getPropertyValue('--body-size')")
                    .GetString() == "16px", "native font-size setting roundtrip");
                Program.Check(Evaluate(browser,
                    "document.documentElement.style.getPropertyValue('--reading-bg')")
                    .GetString() == "#F7F1E6");
                Program.Check(Evaluate(browser,
                    "document.documentElement.style.getPropertyValue('--selection-add-size')")
                    .GetString() == "0.8em" &&
                    Evaluate(browser,
                        "document.documentElement.style.getPropertyValue('--title-font')")
                        .GetString()!.StartsWith("\"Nunito\"", StringComparison.Ordinal) &&
                    Evaluate(browser,
                        "document.documentElement.style.getPropertyValue('--body-font')")
                        .GetString()!.StartsWith("\"Nunito\"", StringComparison.Ordinal) &&
                    Evaluate(browser,
                        "document.documentElement.style.getPropertyValue('--word-font')")
                        .GetString()!.StartsWith("\"Nunito\"", StringComparison.Ordinal) &&
                    Evaluate(browser,
                        "document.documentElement.style.getPropertyValue('--script-color')")
                        .GetString() == "#856300" &&
                    Evaluate(browser,
                        "document.documentElement.style.getPropertyValue('--selection-background')")
                        .GetString() == "#E5EDE0" &&
                    Evaluate(browser,
                        "getComputedStyle(document.querySelector('#article-body .sentence'), '::selection').backgroundColor")
                        .GetString() == "rgb(229, 237, 224)");

                var notice = window.ViewModel.Notifications.Publish("dom-test",
                    "WebView2 DOM notification", NotificationSeverity.Error);
                Program.Check(notice is not null);
                WaitFor(() => Evaluate(browser,
                    "document.querySelectorAll('#notification-cards .notice.error').length")
                    .GetInt32() == 1, "browser error notification");
                Evaluate(browser, "document.querySelector('#notification-cards .notice-close').click(); true");
                WaitFor(() => window.ViewModel.Notifications.Messages.All(n => n.Id != notice!.Id),
                    "browser close button acknowledges notification");
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
                      const coloredUnit = paint?.querySelector('.script-timed-unit');
                      // Chromium serializes CSS url(#id) as url("#id"). Compare the
                      // referenced ID, not the browser-specific quoting style.
                      const clipReference = paint?.style.clipPath.replace(/["']/g, '');
                      return Boolean(paint && clip && clip.id.startsWith('script-clip-') &&
                        clip.querySelectorAll('rect').length >= 3 &&
                        clipReference === 'url(#' + clip.id + ')' &&
                        coloredUnit && getComputedStyle(coloredUnit).color === 'rgb(133, 99, 0)');
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
                // Real DOM: a single headword owns inflected hits in both modes.
                string familyArticle = Path.Combine(directory, "FamilyFixture.json");
                File.WriteAllText(familyArticle, """
                    {"filetype":"passage","title":"Family fixture",
                     "paragraphs":[["He went home.", "She goes away."]]}
                    """);
                File.WriteAllText(Path.ChangeExtension(familyArticle, ".vocabulary.json"), """
                    {"filetype":"vocabulary","words":[
                      {"word":"go","family":["went","goes"],"meanings":[]}
                    ]}
                    """);
                File.WriteAllText(ArticleRepository.ScriptPathFor(familyArticle), """
                    {"filetype":"script","title":"Family script",
                     "paragraphs":[[{"text":"We went home. She goes away.",
                       "timed_units":[["We we",0,1],["nt home. She goes away.",1,3]]}]]}
                    """);
                Program.Check(window.ViewModel.OpenArticle(familyArticle));
                WaitFor(() => Evaluate(browser, """
                    document.getElementById('article-title').textContent === 'Family fixture' &&
                    document.querySelectorAll('#vocabulary-list .word-row').length === 1
                    """).GetBoolean(), "family Passage rendering");
                JsonElement familyPassage = Evaluate(browser, """
                    (() => ({
                      forms: matches.filter(m => m.word === 'go').map(m => m.range.toString()),
                      headings: [...document.querySelectorAll('#vocabulary-list .word-head')]
                        .map(row => row.textContent)
                    }))()
                    """);
                Program.Check(familyPassage.GetProperty("forms").EnumerateArray()
                    .Select(x => x.GetString()).SequenceEqual(["went", "goes"]) &&
                    familyPassage.GetProperty("headings").EnumerateArray()
                    .Select(x => x.GetString()).SequenceEqual(["go"]));
                Evaluate(browser, "document.querySelector('#vocabulary-list .word-row').click(); true");
                WaitFor(() => Evaluate(browser, """
                    document.querySelectorAll('#vocabulary-list .word-row.selected').length === 1 &&
                    document.querySelectorAll('#word-highlight-layer .word-highlight-rect').length >= 2
                    """).GetBoolean(), "family forms highlighted and selected");
                JsonElement collision = Evaluate(browser, """
                    matchVocabulary('went goes', [
                      {word:'go',family:['went','goes']}, {word:'went',family:[]}
                    ])
                    """);
                Program.Check(collision.GetArrayLength() == 2 &&
                    collision[0].GetProperty("key").GetString() == "went" &&
                    collision[1].GetProperty("key").GetString() == "go");
                Evaluate(browser, "document.getElementById('script-toggle').click(); true");
                WaitFor(() => Evaluate(browser, """
                    document.getElementById('article-title').textContent === 'Family script' &&
                    document.querySelectorAll('#article-body .script-paragraph > .script-sentence').length === 1
                    """).GetBoolean(), "family Script rendering");
                JsonElement scriptForms = Evaluate(browser, """
                    matches.filter(m => m.word === 'go').map(m => m.range.toString())
                    """);
                Program.Check(scriptForms.EnumerateArray()
                    .Select(x => x.GetString()).SequenceEqual(["went", "goes"]));
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
