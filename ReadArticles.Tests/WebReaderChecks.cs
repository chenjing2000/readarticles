using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Web.WebView2.Wpf;
using ReadArticles.Infrastructure;
using ReadArticles.Services;

internal static class WebReaderChecks
{
    internal static void Run()
    {
        Program.Run("WebView2 reader assets embedded in application assembly", () =>
        {
            Assembly assembly = typeof(ReadArticles.MainWindow).Assembly;
            foreach (string name in new[]
            {
                "ReadArticles.web.reader.html",
                "ReadArticles.web.reader.css",
                "ReadArticles.web.reader.js",
                "ReadArticles.web.dom.js",
                "ReadArticles.web.bridge.js",
                "ReadArticles.web.reading.js",
                "ReadArticles.web.vocabulary.js",
                "ReadArticles.web.playback.js",
                "ReadArticles.web.layout.js",
                "ReadArticles.web.settings.js",
                "ReadArticles.web.notifications.js",
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
                if (name.EndsWith(".svg", StringComparison.Ordinal))
                {
                    var svg = System.Xml.Linq.XDocument.Load(stream!).Root!;
                    var ns = svg.Name.Namespace;
                    var circle = svg.Elements(ns + "circle").First();
                    Program.Check((string?)svg.Attribute("viewBox") == "0 0 1024 1024" &&
                        (string?)circle.Attribute("cx") == "512" &&
                        (string?)circle.Attribute("cy") == "512" &&
                        (string?)circle.Attribute("r") == "480" &&
                        (string?)circle.Attribute("fill") == "#D65B45" &&
                        svg.Descendants().Any(element =>
                            (string?)element.Attribute("fill") == "#FFFFFF" ||
                            (string?)element.Attribute("stroke") == "#FFFFFF"));
                }
            }
        });
        Program.Run("WebView2 failure shows native terminal screen instead of WPF fallback", () =>
        {
            var window = new ReadArticles.MainWindow(persistSettings: false);
            try
            {
                var host = (ReadArticles.Presentation.WebReaderHost)typeof(ReadArticles.MainWindow)
                    .GetField("webReaderHost", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
                host.Fail("WebView2 initialization failed");
                Program.Check(host.IsUnavailable);
                var failurePanel = (Grid)window.FindName("WebFailurePanel");
                var legacyOverlay = (Grid)window.FindName("ReaderOverlay");
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
                      }], [{
                        "text": "Alpha, stays beta.",
                        "timed_units": [["Alpha",4,5],[",",null,null],["stays",null,null],["missing",4,4.5],["beta",3,4.5]]
                      }]]
                    }
                    """);

                window.Show();
                Program.Check(window.ViewModel.OpenArticle(article));
                var browser = (WebView2)window.FindName("ReaderWebView");
                var overlay = (System.Windows.Controls.Grid)window.FindName("ReaderOverlay");
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
                JsonElement seekKeys = EvaluateModule(browser, """
                    (() => {
                      const progress = document.getElementById('progress');
                      const previousPlayback = getPlayback();
                      const previousDragging = isSeekDragging();
                      const originalSend = window.chrome.webview.postMessage;
                      const calls = [];
                      try {
                        window.chrome.webview.postMessage = ({ type, payload }) => {
                          calls.push({ type, direction: payload?.direction });
                          return 'captured';
                        };
                        progress.dispatchEvent(new Event('blur'));
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
                        renderPlayback({ ...getPlayback(), canPlay: false });
                        const noArticleSpace = fire(button, ' ');
                        return {
                          focused, outline, left, right, global, stopped,
                          nativeStep, count, calls, space, repeatSpace, up,
                          repeatArrow, modified, retainedFocus, noArticleSpace
                        };
                      } finally {
                        window.chrome.webview.postMessage = originalSend;
                        renderPlayback(previousPlayback);
                        if (previousDragging) progress.dispatchEvent(new Event('pointerdown', { bubbles: true }));
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
                JsonElement exclusiveKeys = EvaluateModule(browser, """
                    (() => {
                      const originalSend = window.chrome.webview.postMessage;
                      const previousPlayback = getPlayback();
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
                        window.chrome.webview.postMessage = ({ type, payload }) => {
                          calls.push(type + (payload?.direction ?? ''));
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
                        window.chrome.webview.postMessage = originalSend;
                        renderPlayback(previousPlayback);
                      }
                    })()
                    """);
                Program.Check(exclusiveKeys.GetProperty("all").GetBoolean() &&
                    exclusiveKeys.GetProperty("delivered").GetInt32() == 0 &&
                    exclusiveKeys.GetProperty("count").GetInt32() == 12);

                // Time labels preserve 60:00 at one hour, then switch to hh:mm:ss.
                JsonElement timeFormats = EvaluateModule(browser, """
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
                JsonElement dragState = EvaluateModule(browser, """
                    (() => {
                      const progress = document.getElementById('progress');
                      const previousPlayback = getPlayback();
                      const previousDragging = isSeekDragging();
                      try {
                        const active = { position: 42, duration: 200, canSeek: true,
                          canPlay: true, isArticleActive: true, state: 'paused' };
                        renderPlayback(active);
                        progress.dispatchEvent(new Event('pointerdown', { bubbles: true }));
                        const held = isSeekDragging();
                        document.dispatchEvent(new Event('pointerup'));
                        const released = !isSeekDragging();
                        progress.dispatchEvent(new Event('pointerdown', { bubbles: true }));
                        document.dispatchEvent(new Event('pointercancel'));
                        const cancelled = !isSeekDragging();
                        progress.dispatchEvent(new Event('pointerdown', { bubbles: true }));
                        progress.dispatchEvent(new Event('blur'));
                        const blurred = !isSeekDragging();
                        progress.dispatchEvent(new Event('pointerdown', { bubbles: true }));
                        renderPlayback({ ...active, position: 0, canSeek: false, state: 'stopped',
                          isArticleActive: false });
                        const stopped = !isSeekDragging() && progress.disabled && progress.value === '0';
                        return { held, released, cancelled, blurred, stopped };
                      } finally {
                        renderPlayback(previousPlayback);
                        if (previousDragging) progress.dispatchEvent(new Event('pointerdown', { bubbles: true }));
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
                JsonElement selectionAction = EvaluateModule(browser, """
                    (() => {
                      const button = document.getElementById('selection-add');
                      const hidden = button.hidden;
                      button.hidden = false;
                      const rect = (left, top, right, bottom) => ({
                        left, top, right, bottom,
                        width: right - left, height: bottom - top
                      });
                      positionSelectionAddButton(rect(90, 110, 130, 130), button);
                      const afterLastLine = {
                        x: Number.parseInt(button.style.left),
                        y: Number.parseInt(button.style.top)
                      };
                      positionSelectionAddButton(rect(innerWidth - 30, innerHeight - 30,
                        innerWidth - 10, innerHeight - 10), button);
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

                Program.Check(EvaluateModule(browser, """
                    (() => {
                      const button = document.getElementById('selection-add');
                      const root = document.documentElement.style;
                      const oldSize = root.getPropertyValue('--body-size');
                      const oldAdd = root.getPropertyValue('--selection-add-size');
                      const hidden = button.hidden, oldLeft = button.style.left, oldTop = button.style.top;
                      try {
                        root.setProperty('--body-size', '40px');
                        button.hidden = false;
                        for (const [size, expected] of [[.4, 24], [.6, 24], [1.2, 48]]) {
                          root.setProperty('--selection-add-size', size + 'em');
                          const box = button.getBoundingClientRect();
                          const glyph = button.querySelector('img').getBoundingClientRect();
                          if (box.width !== expected || box.height !== expected ||
                              Math.abs(glyph.width - size * 40) > .02) return false;
                        }
                        const rect = { left: innerWidth - 30, top: innerHeight - 30,
                          right: innerWidth - 10, bottom: innerHeight - 10, width: 20, height: 20 };
                        positionSelectionAddButton(rect, button);
                        const hit = button.getBoundingClientRect();
                        const icon = button.querySelector('img').getBoundingClientRect();
                        return hit.width === 48 && hit.height === 48 && icon.width === 48 && icon.height === 48 &&
                          hit.left === innerWidth - 79 && hit.top === innerHeight - 78 &&
                          hit.right <= innerWidth && hit.bottom <= innerHeight &&
                          button.contains(document.elementFromPoint(hit.right - 1, hit.bottom - 1));
                      } finally {
                        root.setProperty('--body-size', oldSize);
                        root.setProperty('--selection-add-size', oldAdd);
                        button.style.left = oldLeft;
                        button.style.top = oldTop;
                        button.hidden = hidden;
                      }
                    })()
                    """).GetBoolean());

                // The frame-paced loop checks geometry at up to 60Hz, even
                // without another selectionchange, and clears when selection ends.
                JsonElement selectionTracking = EvaluateModule(browser, """
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
                      const started = !button.hidden && selectionTracking().tracking &&
                        selectionTracking().refreshHz === 60;
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
                WaitFor(() => EvaluateModule(browser, """
                    !selectionTracking().tracking &&
                      document.getElementById('selection-add').hidden &&
                      !selectionTracking().hasSelection
                    """).GetBoolean(), "selection-add disappears after selection ends without manual cleanup");

                // Test actual browser bounds and the two word-divider drag limits.
                JsonElement widths = EvaluateModule(browser, """
                    (() => {
                      const article = document.getElementById('article-scroll');
                      const vocabulary = document.getElementById('vocabulary-scroll');
                      const splitter = document.getElementById('reader-splitter');
                      const original = getRightWidth();
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
                    var layoutField = typeof(ReadArticles.MainWindow).GetField("paneLayout",
                        BindingFlags.Instance | BindingFlags.NonPublic)!;
                    var paneLayout = (ReadArticles.Layout.PaneLayout)layoutField.GetValue(window)!;
                    var resize = typeof(ReadArticles.MainWindow).GetMethod("ResizeLibraryPane",
                        BindingFlags.Instance | BindingFlags.NonPublic)!;
                    double initialRight = paneLayout.RightWidth;
                    resize.Invoke(window, [400d]);
                    WaitFor(() => Math.Abs(Evaluate(browser, """
                        document.getElementById('vocabulary-scroll').getBoundingClientRect().width
                        """).GetDouble() - paneLayout.RightWidth) < 2,
                        "coordinated vocabulary width after library resize");
                    Program.Check(paneLayout.RightWidth <= initialRight);
                    resize.Invoke(window, [200d]);
                    WaitFor(() => Math.Abs(Evaluate(browser, """
                        document.getElementById('vocabulary-scroll').getBoundingClientRect().width
                        """).GetDouble() - paneLayout.RightWidth) < 2,
                        "vocabulary stays steady when library shrinks");

                    // Reverse direction: a right-divider intent may compress
                    // the native library, but the browser paints only C#'s result.
                    var bridge = (ReadArticles.Presentation.WebReaderBridge)typeof(ReadArticles.MainWindow)
                        .GetField("webReaderBridge", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
                    using var widthIntent = JsonDocument.Parse("""{"payload":{"width":400}}""");
                    bridge.HandlePaneWidth(widthIntent.RootElement, Guid.NewGuid().ToString("D"));
                    WaitFor(() => Math.Abs(Evaluate(browser, """
                        document.getElementById('vocabulary-scroll').getBoundingClientRect().width
                        """).GetDouble() - paneLayout.RightWidth) < 2,
                        "confirmed vocabulary width after right-divider resize");
                    var nativeLeft = (ColumnDefinition)window.FindName("LeftColumn");
                    Program.Check(Math.Abs(nativeLeft.ActualWidth - paneLayout.LeftWidth) < 2);
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
                JsonElement browserMatches = EvaluateModule(browser,
                    "matchVocabulary(" + JsonSerializer.Serialize(phrase) + "," +
                    JsonSerializer.Serialize(words) + ")");
                Program.Check(browserMatches.GetArrayLength() == 1 &&
                    browserMatches[0].GetProperty("start").GetInt32() == 0 &&
                    browserMatches[0].GetProperty("length").GetInt32() == 11 &&
                    browserMatches[0].GetProperty("key").GetString() == "public life");
                JsonElement boundary = EvaluateModule(browser, "matchVocabulary(" +
                    JsonSerializer.Serialize("politician's politician’s 2public public-life") + "," +
                    JsonSerializer.Serialize(new[] { "politician", "public" }) + ")");
                Program.Check(boundary.GetArrayLength() == 1 &&
                    boundary[0].GetProperty("key").GetString() == "public" &&
                    boundary[0].GetProperty("start").GetInt32() == 27);

                // Preserve Python-compatible numeric boundaries (digits are not letters).
                JsonElement repeated = EvaluateModule(browser,
                    "matchVocabulary('GO went Go goes İ go', [{word:'go',family:['went','goes']}])");
                Program.Check(repeated.EnumerateArray().Select(hit => hit.GetProperty("start").GetInt32())
                    .SequenceEqual(new[] { 0, 3, 8, 11, 18 }) &&
                    repeated.EnumerateArray().All(hit => hit.GetProperty("key").GetString() == "go"));
                JsonElement numericBoundary = EvaluateModule(browser,
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
                JsonElement wordKeys = EvaluateModule(browser, """
                    (() => {
                      const rows = [...document.querySelectorAll('#vocabulary-list .word-row')];
                      const originalSend = window.chrome.webview.postMessage;
                      const calls = [];
                      try {
                        window.chrome.webview.postMessage = ({type}) => { calls.push(type); };
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
                      } finally { window.chrome.webview.postMessage = originalSend; }
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
                draft.Appearance.Title.Size = 21;
                draft.Appearance.Body.Size = 19;
                draft.Appearance.Vocabulary.Size = 20;
                draft.Appearance.Phonetics.Size = 18;
                draft.Appearance.Meanings.Size = 22;
                draft.Appearance.Body.Background = "#F7F1E6";
                draft.Appearance.Title.Font = "Newsreader";
                draft.Appearance.Body.Font = "Source Serif 4";
                draft.Appearance.Vocabulary.Font = "Nunito";
                draft.Appearance.Meanings.Font = "Source Han Serif SC";
                draft.Appearance.Title.LineHeight = 1.85;
                draft.Appearance.Body.LineHeight = 1.65;
                draft.Appearance.Meanings.LineHeight = 1.55;
                draft.Appearance.Title.ParagraphAfter = 13;
                draft.Appearance.Body.ParagraphAfter = 19;
                draft.Appearance.Body.FirstLineIndent = 1;
                draft.Appearance.Body.SelectionAddSize = 0.95;
                draft.Appearance.Body.SelectionBackground = "#E5EDE0";
                draft.Appearance.Body.ScriptColor = "#ECE6F3";
                var applyDialog = typeof(ReadArticles.MainWindow).GetMethod(
                    "ApplyDialogSettings", BindingFlags.Instance | BindingFlags.NonPublic)!;
                object?[] settingsArguments = [draft, null];
                Program.Check((bool)applyDialog.Invoke(window, settingsArguments)!);
                var settingsRefresh = (Task)settingsArguments[1]!;
                WpfTestHelpers.Wait(() => settingsRefresh.IsCompleted);
                settingsRefresh.GetAwaiter().GetResult();
                WaitFor(() => Evaluate(browser,
                    "document.documentElement.style.getPropertyValue('--body-size')")
                    .GetString() == "19px", "native font-size setting roundtrip");
                Program.Check(Evaluate(browser, """
                    (() => {
                      const style = document.documentElement.style;
                      return ['--title-size', '--body-size', '--word-size', '--phonetic-size', '--meaning-size']
                        .map(name => style.getPropertyValue(name)).join(',') === '21px,19px,20px,18px,22px' &&
                        getComputedStyle(document.querySelector('#article-title')).fontSize === '21px' &&
                        getComputedStyle(document.querySelector('#article-body')).fontSize === '19px' &&
                        ['--title-line', '--body-line', '--meaning-line', '--title-after', '--body-after', '--body-indent']
                          .map(name => style.getPropertyValue(name)).join(',') === '1.85,1.65,1.55,13px,19px,1em' &&
                        Math.abs(parseFloat(getComputedStyle(document.querySelector('#article-body')).lineHeight) - 19 * 1.65) < .02 &&
                        getComputedStyle(document.querySelector('#article-body .article-paragraph')).marginBottom === '19px' &&
                        getComputedStyle(document.querySelector('#article-body .article-paragraph')).textIndent === '19px';
                    })()
                    """).GetBoolean());
                Program.Check(Evaluate(browser,
                    "document.documentElement.style.getPropertyValue('--reading-bg')")
                    .GetString() == "#F7F1E6");
                Program.Check(Evaluate(browser,
                    "document.documentElement.style.getPropertyValue('--selection-add-size')")
                    .GetString() == "0.95em" &&
                    Evaluate(browser,
                        "document.documentElement.style.getPropertyValue('--title-font')")
                        .GetString() == "\"Newsreader\", \"Source Serif 4\", \"Georgia\", \"Nunito\", \"Microsoft YaHei UI\", sans-serif" &&
                    Evaluate(browser,
                        "document.documentElement.style.getPropertyValue('--body-font')")
                        .GetString() == "\"Source Serif 4\", \"Newsreader\", \"Georgia\", \"Nunito\", \"Microsoft YaHei UI\", sans-serif" &&
                    Evaluate(browser,
                        "document.documentElement.style.getPropertyValue('--word-font')")
                        .GetString() == "\"Nunito\", \"Newsreader\", \"Source Serif 4\", \"Georgia\", \"Microsoft YaHei UI\", sans-serif" &&
                    Evaluate(browser,
                        "document.documentElement.style.getPropertyValue('--meaning-font')")
                        .GetString() == "\"Source Han Serif SC\", \"Microsoft YaHei\", sans-serif" &&
                    Evaluate(browser,
                        "document.documentElement.style.getPropertyValue('--script-color')")
                        .GetString() == "#ECE6F3" &&
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
                      document.querySelectorAll('#article-body .script-paragraph > .script-sentence .script-timed-unit').length === 5)()
                    """).GetBoolean(), "Script DOM switch");
                Program.Check(Evaluate(browser,
                    "document.getElementById('article-title').textContent").GetString() ==
                    "Script DOM fixture");
                // One original text layer; SVG rectangles sit behind it.
                WaitFor(() => Evaluate(browser, """
                    (() => {
                      const row = document.querySelector('#article-body .script-paragraph');
                      const layer = row?.querySelector(':scope > svg.script-background');
                      const rect = layer?.querySelector('rect');
                      const unit = row?.querySelector('.script-timed-unit');
                      return Boolean(rect && unit && row.textContent === 'It is a beautiful day.' &&
                        !row.querySelector('.script-paint, clipPath') &&
                        row.querySelectorAll('.script-timed-unit').length === 3 &&
                        layer.getAttribute('aria-hidden') === 'true' &&
                        getComputedStyle(row).isolation === 'isolate' &&
                        getComputedStyle(layer).zIndex === '-1' &&
                        getComputedStyle(layer).pointerEvents === 'none' &&
                        getComputedStyle(unit).color === getComputedStyle(row).color &&
                        getComputedStyle(rect).fill === 'rgb(236, 230, 243)' &&
                        getComputedStyle(rect).shapeRendering.toLowerCase() === 'crispedges');
                    })()
                    """).GetBoolean(), "Script SVG background beneath unchanged text");
                // Exercise the actual SVG background at 0%, 50%, 100%, and stop.
                JsonElement sweep = EvaluateModule(browser, """
                    (() => {
                      const rect = document.querySelector(
                        '#article-body .script-paragraph .script-background rect');
                      if (!rect || typeof paintScriptProgress !== 'function') return null;
                      const widthAt = (seconds, active = true) => {
                        paintScriptProgress({ isArticleActive: active,
                          canSeek: active, position: seconds });
                        return Number(rect.getAttribute('width'));
                      };
                      const unit = document.querySelector('.script-paragraph .script-timed-unit');
                      const next = unit.nextElementSibling;
                      const range = document.createRange();
                      range.selectNodeContents(unit);
                      const original = range.getBoundingClientRect();
                      const color = getComputedStyle(unit).color;
                      const gap = next.getBoundingClientRect().left - original.right;
                      const start = widthAt(1.0), middle = widthAt(1.4), end = widthAt(1.8);
                      const exact = gap > 0 && Math.abs(middle - original.width / 2) < 0.02 &&
                        Math.abs(end - original.width - gap) < 0.02 &&
                        getComputedStyle(unit).color === color;
                      const observer = new MutationObserver(() => {});
                      observer.observe(rect, { attributes: true });
                      widthAt(1.8);
                      const redundant = observer.takeRecords().length;
                      observer.disconnect();
                      const backward = widthAt(1.4), stopped = widthAt(1.8, false);
                      return { start, middle, end, stopped, backward, redundant, exact };
                    })()
                    """);
                if (sweep.ValueKind != JsonValueKind.Object ||
                    !sweep.GetProperty("exact").GetBoolean() ||
                    sweep.GetProperty("start").GetDouble() != 0 ||
                    sweep.GetProperty("middle").GetDouble() <= 0 ||
                    sweep.GetProperty("end").GetDouble() <=
                        sweep.GetProperty("middle").GetDouble() ||
                    sweep.GetProperty("stopped").GetDouble() != 0 ||
                    sweep.GetProperty("backward").GetDouble() != sweep.GetProperty("middle").GetDouble() ||
                    sweep.GetProperty("redundant").GetInt32() != 0)
                    throw new Exception("Unexpected Script SVG sweep: " + sweep.GetRawText());
                // Exercise real JSON admission, untimed/unaligned text, paragraph boundaries and
                // glyph-height geometry in the actual reader rather than only in synthetic DOM.
                Program.Check(EvaluateModule(browser, """
                    (() => {
                      const rows = [...document.querySelectorAll('#article-body > .script-paragraph')];
                      const near = (a, b) => Math.abs(a - b) < 0.02;
                      const width = rect => Number(rect.getAttribute('width'));
                      if (rows.length !== 2 || rows[1].textContent !== 'Alpha, stays beta.' ||
                          [...rows[1].querySelectorAll('.script-timed-unit')].map(unit => unit.textContent).join('|') !== 'Alpha|beta')
                        return false;
                      const savedPlayback = getPlayback();
                      try {
                        paintScriptProgress({ position: 5, isArticleActive: true, canSeek: true });
                        for (const [index, row] of rows.entries()) {
                          const svg = row.querySelector(':scope > svg.script-background');
                          const base = svg.getBoundingClientRect();
                          const actual = [...svg.querySelectorAll(':scope > rect')];
                          const units = [...row.querySelectorAll('.script-timed-unit')];
                          const groups = units.map(unit => {
                            const range = document.createRange();
                            range.selectNodeContents(unit);
                            return [...range.getClientRects()].filter(r => r.width > 0.25 && r.height > 0.25)
                              .sort((a,b) => a.top-b.top || a.left-b.left);
                          });
                          const expected = groups.flat();
                          if (expected.length !== actual.length || groups.some(group => !group.length)) return false;
                          if (!expected.every((r,i) =>
                            near(Number(actual[i].getAttribute('x')), r.left-base.left) &&
                            near(Number(actual[i].getAttribute('y')), r.top-base.top-1) &&
                            near(Number(actual[i].getAttribute('height')), r.height+2))) return false;
                          // Measure the trailing punctuation independently of the production gap calculation.
                          const suffixWidth = (unit, rect) => {
                            const range = document.createRange();
                            range.setStart(unit.nextSibling, 0);
                            range.setEnd(unit.nextSibling, 1);
                            const mark = range.getBoundingClientRect();
                            return Math.abs(mark.top-rect.top)<2 ? Math.max(0,mark.right-rect.right) : 0;
                          };
                          const period = suffixWidth(units.at(-1),expected.at(-1));
                          if (period<=0 || !near(width(actual.at(-1)),expected.at(-1).width+period)) return false;
                          if (index === 1) {
                            // Real null/unaligned units and an overlapping, out-of-order interval.
                            // Only the comma after Alpha and the period after beta may extend.
                            const firstEnd = groups[0].length-1;
                            const comma = suffixWidth(units[0],expected[firstEnd]);
                            if (comma<=0 || !actual.every((rect,i) => near(width(rect),expected[i].width+
                                (i===firstEnd?comma:i===actual.length-1?period:0)))) return false;
                            paintScriptProgress({position:4.5,isArticleActive:true,canSeek:true});
                            const alphaWidth = groups[0].reduce((sum,r)=>sum+r.width,0);
                            if (!near(actual.slice(0,firstEnd+1).reduce((sum,r)=>sum+width(r),0),alphaWidth/2) ||
                                !near(width(actual.at(-1)),expected.at(-1).width+period)) return false;
                            paintScriptProgress({position:5,isArticleActive:true,canSeek:true});
                          }
                        }
                        return true;
                      } finally {
                        paintScriptProgress(savedPlayback);
                      }
                    })()
                    """).GetBoolean());
                // Use actual browser geometry for same-line, cross-sentence and wrapped units.
                Program.Check(EvaluateModule(browser, """
                    (async () => {
                      const body = document.getElementById('article-body');
                      const saved = [...body.childNodes];
                      const savedPlayback = getPlayback();
                      const scroll = document.getElementById('article-scroll');
                      const savedScroll = scroll.scrollTop;
                      const word = (text, start) => '<span class="script-timed-unit" data-start="' +
                        start + '" data-end="' + (start + 1) + '">' + text + '</span>';
                      const sentence = text => '<span class="script-sentence">' + text + '</span>';
                      const paragraph = (html, style) => {
                        const row = document.createElement('p');
                        row.className = 'article-paragraph script-paragraph';
                        row.style.cssText = style;
                        row.innerHTML = html + '<svg class="script-background" aria-hidden="true"></svg>';
                        return row;
                      };
                      const near = (a, b) => Math.abs(a - b) < 0.02;
                      const measure = async () => {
                        scheduleScriptGeometry();
                        await new Promise(resolve => requestAnimationFrame(resolve));
                      };
                      const paint = (position, active = true) => paintScriptProgress({
                        position, isArticleActive: active, canSeek: active
                      });
                      const rects = row => [...row.querySelectorAll('svg rect')];
                      const width = rect => Number(rect.getAttribute('width'));
                      const x = rect => Number(rect.getAttribute('x'));
                      const y = rect => Number(rect.getAttribute('y'));
                      try {
                        const row = paragraph(sentence(word('Alpha', 0) + ' ' + word('beta', 1)) + ' ' +
                          sentence(word('gamma', 2) + ' untimed ' + word('delta', 3) + ', ' + word('epsilon', 4)),
                          'width:1000px;white-space:nowrap;text-indent:0');
                        const wrap = paragraph(sentence(word('Alpha', 5) + ' ' + word('Beta', 6)),
                          'width:4em;text-indent:0');
                        body.replaceChildren(row, wrap);
                        await measure();
                        if (rects(row).length !== 5 || rects(wrap).length !== 2) return false;
                        const [a,b,c,d,e] = rects(row);
                        const [f,g] = rects(wrap);
                        const units = [...row.querySelectorAll('.script-timed-unit'), ...wrap.querySelectorAll('.script-timed-unit')];
                        const widths = units.map(unit => unit.getBoundingClientRect().width);
                        paint(0.5);
                        if (!near(width(a), widths[0]/2) || width(b) !== 0) return false;
                        paint(1);
                        if (!near(x(a)+width(a), x(b)) || width(b) !== 0) return false;
                        paint(2.5);
                        if (!near(x(b)+width(b), x(c)) || !near(width(c), widths[2]/2)) return false;
                        paint(7);
                        if (![c,e,f,g].every((rect,i) => near(width(rect), widths[[2,4,5,6][i]])) ||
                            !near(x(d)+width(d),x(e)) || width(d)<=widths[3] || near(y(f),y(g))) return false;
                        paint(0.5);
                        if (!near(width(a),widths[0]/2) || [b,c,d,e,f,g].some(rect => width(rect)!==0)) return false;
                        // A paused playback snapshot must be restored by geometry measurement itself.
                        // No subsequent explicit paint or playback update may repair the new rectangles.
                        renderPlayback({ ...savedPlayback, position:6, duration:7, state:'paused',
                          isArticleActive:true, canSeek:true });
                        if (!near(width(f),widths[5]) || width(g)!==0) return false;
                        wrap.style.width = '1000px';
                        await measure();
                        const [left,right] = rects(wrap);
                        if (rects(wrap).length !== 2 || getPlayback().position !== 6 ||
                            getPlayback().state !== 'paused' || !near(y(left),y(right)) ||
                            !near(x(left)+width(left),x(right)) || width(left)<=widths[5] || width(right)!==0)
                          return false;
                        wrap.style.cssText = 'font-family:monospace;width:8ch;text-indent:0';
                        wrap.replaceChildren(...paragraph(sentence(word('ABCDEFGHIJK',5)+' '+word('xy',6)), '').childNodes);
                        await measure();
                        const [first,last,next] = rects(wrap);
                        if (rects(wrap).length !== 3 || near(y(first),y(last)) || !near(y(last),y(next))) return false;
                        const range = document.createRange();
                        range.selectNodeContents(wrap.querySelector('.script-timed-unit'));
                        const measured = [...range.getClientRects()].filter(r=>r.width>0.25&&r.height>0.25);
                        const [w1,w2] = measured.map(r=>r.width);
                        const total = w1+w2;
                        paint(5+w1/(2*total));
                        if (!near(width(first),w1/2) || width(last)!==0) return false;
                        paint(5+(w1+w2/2)/total);
                        if (!near(width(first),w1) || !near(width(last),w2/2)) return false;
                        paint(6);
                        if (!near(width(first),w1) || !near(x(last)+width(last),x(next)) || width(last)<=w2 || width(next)!==0) return false;
                        paint(5+w1/(2*total));
                        if (!near(width(first),w1/2) || width(last)!==0 || width(next)!==0) return false;
                        paint(7); paint(0,false);
                        if (![...rects(row),...rects(wrap)].every(rect=>width(rect)===0)) return false;

                        // Reuse the actual reader's geometry and paint entry points for punctuation.
                        const arrange = async (html, style='width:1000px;white-space:nowrap;text-indent:0') => {
                          const p = paragraph(html,style);
                          body.replaceChildren(p);
                          await measure();
                          paint(0,false);
                          return p;
                        };
                        const unitRect = unit => {
                          const range = document.createRange();
                          range.selectNodeContents(unit);
                          return range.getBoundingClientRect();
                        };
                        const markRight = (node,length) => {
                          const range = document.createRange();
                          range.setStart(node,0);
                          range.setEnd(node,length);
                          return Math.max(...[...range.getClientRects()].map(r=>r.right));
                        };
                        for (const marks of [',','.',';',':','?','!','…','...','?!','!!']) {
                          const p = await arrange(sentence(word('Alpha',0)+marks+' '+word('Beta',1)));
                          const [a,b] = rects(p);
                          const original = unitRect(p.querySelector('.script-timed-unit')).width;
                          if (rects(p).length!==2) throw Error('rect count '+marks);
                          paint(0.99);
                          if (!near(width(a),original*0.99) || width(b)!==0) throw Error('partial '+marks);
                          paint(1);
                          if (!near(x(a)+width(a),x(b)) || width(a)<=original || width(b)!==0)
                            throw Error('bridge '+marks);
                          const observer = new MutationObserver(()=>{});
                          observer.observe(a,{attributes:true});
                          paint(1);
                          const writes = observer.takeRecords().length;
                          observer.disconnect();
                          paint(0.5);
                          if (writes!==0 || !near(width(a),original/2) || width(b)!==0)
                            throw Error('rewind '+marks);
                        }
                        for (const [suffix,count,next] of [
                          ['...',3,false],['?! ',2,false],[',\"',1,true],[', untimed ',1,true],
                          ['— ',0,true],[' (',0,true],['\"!',0,false],['，',0,false],
                          ['?!—',2,true],[' ',0,false]
                        ]) {
                          const p = await arrange(sentence(word('Alpha',0)+suffix+(next?word('Beta',1):'')));
                          const unit = p.querySelector('.script-timed-unit'), original = unitRect(unit);
                          const expected = count?markRight(unit.nextSibling,count)-original.right:0;
                          paint(1);
                          if ((count && expected<=0) || !near(width(rects(p)[0]),original.width+expected))
                            throw Error('prefix '+suffix);
                        }
                        // An actual wrap between words must leave only the comma on the first line.
                        let p = await arrange(sentence(word('Alpha',0)+', '+word('Beta',1)),
                          'font-family:monospace;width:8ch;text-indent:0');
                        let [leftMark,rightWord] = rects(p);
                        let unit = p.querySelector('.script-timed-unit'), original = unitRect(unit);
                        let expected = markRight(unit.nextSibling,1)-original.right;
                        paint(1);
                        if (near(y(leftMark),y(rightWord)) || expected<=0 ||
                            !near(width(leftMark),original.width+expected)) throw Error('line end');
                        renderPlayback({...savedPlayback,position:1,duration:2,state:'paused',
                          isArticleActive:true,canSeek:true});
                        p.style.width='1000px';
                        await measure();
                        [leftMark,rightWord] = rects(p);
                        if (!near(y(leftMark),y(rightWord)) || !near(x(leftMark)+width(leftMark),x(rightWord)) ||
                            width(rightWord)!==0 || getPlayback().position!==1 || getPlayback().state!=='paused')
                          throw Error('paused reflow');
                        // Use explicit newlines for punctuation wraps, but natural wrapping for a long word.
                        for (const [text,marks,size,expectedCount] of [
                          ['Alpha','?\n!...','1000px',1],['Alpha','\n?!...','1000px',0],
                          ['Alpha','? \n!...','1000px',1],['ABCDEFGHIJK','?!','8ch',2]
                        ]) {
                          p = await arrange(sentence(word(text,0)+marks),
                            'font-family:monospace;white-space:pre-line;text-indent:0;width:'+size);
                          unit = p.querySelector('.script-timed-unit');
                          const range = document.createRange();
                          range.selectNodeContents(unit);
                          const fragments = [...range.getClientRects()].filter(r=>r.width>0.25&&r.height>0.25);
                          const lastRect = fragments.at(-1), node = unit.nextSibling;
                          let right = lastRect.right, count = 0, offLine = 0;
                          for (let i=0;i<marks.length;i++) {
                            if (/\s/u.test(marks[i])) continue;
                            range.setStart(node,i); range.setEnd(node,i+1);
                            const mark = range.getBoundingClientRect();
                            if (mark.width<=0) continue;
                            if (Math.abs(mark.top-lastRect.top)<2) { count++; right=Math.max(right,mark.right); }
                            else offLine++;
                          }
                          if (count!==expectedCount || count+offLine!==marks.replace(/\s/gu,'').length ||
                              (marks.includes('\n')?offLine===0:fragments.length<2))
                            throw Error('wrap fixture '+text+' '+count+'/'+offLine);
                          paint(1);
                          const actual = rects(p);
                          if (actual.length!==fragments.length || !actual.every((r,i)=>
                              near(width(r),fragments[i].width+(i===actual.length-1?right-lastRect.right:0))))
                            throw Error('wrap paint '+text);
                          p.style.width='1000px'; p.style.whiteSpace='nowrap';
                          await measure();
                          paint(1);
                          original = unitRect(unit);
                          if (rects(p).length!==1 ||
                              !near(width(rects(p)[0]),markRight(node,marks.length)-original.left))
                            throw Error('wrap reflow '+text);
                        }
                        for (const [parts,count] of [[['?','!'],2],[['...',' '],3],[[',','"'],1]]) {
                          p = await arrange(sentence(word('Alpha',0)));
                          unit = p.querySelector('.script-timed-unit');
                          for (const part of parts) unit.parentNode.append(document.createTextNode(part));
                          await measure();
                          const range = document.createRange();
                          range.setStartAfter(unit);
                          let node=unit.nextSibling, remaining=count;
                          while (remaining>node.length) { remaining-=node.length; node=node.nextSibling; }
                          range.setEnd(node,remaining);
                          expected = Math.max(...[...range.getClientRects()].map(r=>r.right))-unitRect(unit).left;
                          paint(1);
                          if (!near(width(rects(p)[0]),expected)) throw Error('split prefix '+parts);
                        }
                        p = await arrange(sentence(word('Alpha',0)+',')+' '+sentence(word('Beta',1)));
                        paint(1);
                        if (!near(x(rects(p)[0])+width(rects(p)[0]),x(rects(p)[1])))
                          throw Error('sentence separator');
                        p = await arrange(sentence(word('Alpha',0))+sentence('?! "untimed"'));
                        unit = p.querySelector('.script-timed-unit');
                        expected = markRight(p.querySelectorAll('.script-sentence')[1].firstChild,2)-unitRect(unit).left;
                        paint(1);
                        if (!near(width(rects(p)[0]),expected)) throw Error('sentence prefix');
                        p = await arrange(sentence(word('Alpha',0)+' '));
                        body.append(paragraph(sentence('! '+word('Beta',1)),'width:1000px;white-space:nowrap;text-indent:0'));
                        await measure(); paint(1);
                        if (!near(width(rects(p)[0]),unitRect(p.querySelector('.script-timed-unit')).width))
                          throw Error('paragraph boundary');
                        p = await arrange(sentence(word('Alpha',0)+word(',',1)+' '+word('Beta!',2)));
                        const [firstTimed,commaTimed,lastTimed] = rects(p);
                        const [firstWidth,commaWidth,lastWidth] = [...p.querySelectorAll('.script-timed-unit')]
                          .map(u=>unitRect(u).width);
                        paint(1);
                        if (!near(width(firstTimed),firstWidth) || width(commaTimed)!==0) throw Error('timed mark');
                        paint(1.5);
                        if (!near(width(commaTimed),commaWidth/2)) throw Error('timed mark partial');
                        paint(2.5);
                        if (!near(width(lastTimed),lastWidth/2)) throw Error('included mark partial');
                        paint(3);
                        if (!near(width(lastTimed),lastWidth)) throw Error('included mark complete');
                        paint(0,false);
                        return rects(p).every(r=>width(r)===0);
                      } finally {
                        body.replaceChildren(...saved);
                        refreshHighlights(true);
                        renderPlayback(savedPlayback);
                        await measure();
                        scroll.scrollTop = savedScroll;
                      }
                    })()
                    """).GetBoolean());
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
                JsonElement familyPassage = EvaluateModule(browser, """
                    (() => ({
                      forms: matchedTexts('go'),
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
                JsonElement collision = EvaluateModule(browser, """
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
                JsonElement scriptForms = EvaluateModule(browser, """
                    matchedTexts('go')
                    """);
                Program.Check(scriptForms.EnumerateArray()
                    .Select(x => x.GetString()).SequenceEqual(["went", "goes"]));
                WaitFor(() => Evaluate(browser, """
                    Boolean(document.querySelector('#word-highlight-layer .word-highlight-rect') &&
                      document.querySelector('.script-background rect'))
                    """).GetBoolean(), "Script vocabulary and playback layers coexist");
                Program.Check(EvaluateModule(browser, """
                    (() => {
                      const body = document.getElementById('article-body');
                      const layer = document.getElementById('word-highlight-layer');
                      const row = body.querySelector('.script-paragraph');
                      const background = row.querySelector('.script-background');
                      const capsules = [...layer.children];
                      const color = capsules.map(node => node.style.backgroundColor);
                      const rectangles = [...background.querySelectorAll('rect')];
                      if (getComputedStyle(body).isolation !== 'isolate' ||
                          getComputedStyle(layer).zIndex !== '-1' ||
                          getComputedStyle(row).isolation !== 'isolate' ||
                          getComputedStyle(background).zIndex !== '-1') return false;
                      const unchanged = () => capsules.length === layer.children.length &&
                        capsules.every((node,i) => node === layer.children[i] && node.style.backgroundColor === color[i]);
                      const paint = (position, active = true) => paintScriptProgress({
                        position, isArticleActive: active, canSeek: active
                      });
                      paint(3);
                      if (!rectangles.every(rect => Number(rect.getAttribute('width')) > 0) || !unchanged()) return false;
                      paint(0.5);
                      if (!unchanged() || Number(rectangles.at(-1).getAttribute('width')) !== 0) return false;
                      paint(0,false);
                      return unchanged() && rectangles.every(rect => Number(rect.getAttribute('width')) === 0);
                    })()
                    """).GetBoolean());
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

    // Execute real module exports in the live page without publishing application
    // state on window. WebView2 ExecuteScriptAsync does not await JS promises.
    private static JsonElement EvaluateModule(WebView2 browser, string script)
    {
        string attribute = "data-test-" + Guid.NewGuid().ToString("N");
        string body = """
            (async () => {
              const { getPlayback, isSeekDragging, renderPlayback, formatSeconds } = await import('https://readarticles.invalid/playback.js');
              const { matchVocabulary, positionSelectionAddButton, updateSelectionAdd,
                selectionTracking, matchedTexts, refreshHighlights } = await import('https://readarticles.invalid/vocabulary.js');
              const { applyPaneWidth, getRightWidth } = await import('https://readarticles.invalid/layout.js');
              const { paintScriptProgress, scheduleScriptGeometry } = await import('https://readarticles.invalid/reading.js');
              return (
            """ + script + """
              );
            })()
            """;
        string name = JsonSerializer.Serialize(attribute);
        Evaluate(browser, body + ".then(value => document.documentElement.setAttribute(" + name +
            ", JSON.stringify({value})), error => document.documentElement.setAttribute(" + name +
            ", JSON.stringify({error:String(error)}))); true");
        WaitFor(() => Evaluate(browser, "document.documentElement.hasAttribute(" + name + ")")
            .GetBoolean(), "module evaluation");
        string result = Evaluate(browser, "document.documentElement.getAttribute(" + name + ")").GetString()!;
        Evaluate(browser, "document.documentElement.removeAttribute(" + name + "); true");
        using var json = JsonDocument.Parse(result);
        if (json.RootElement.TryGetProperty("error", out var error))
            throw new Exception("JavaScript module evaluation failed: " + error.GetString());
        return json.RootElement.GetProperty("value").Clone();
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
