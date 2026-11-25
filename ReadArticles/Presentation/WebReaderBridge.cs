using System.ComponentModel;
using System.Text.Json;
using System.Windows.Threading;
using ReadArticles.Infrastructure;
using ReadArticles.Layout;
using ReadArticles.Models;
using ReadArticles.ViewModels;

namespace ReadArticles.Presentation;

// Owns protocol identity, validated intents and snapshots, not native controls.
public sealed class WebReaderBridge : IDisposable
{
    private readonly WebReaderHost host;
    private readonly MainViewModel ViewModel;
    private readonly ReaderSettings settings;
    private readonly PaneLayout paneLayout;
    private readonly Func<double> availablePaneWidth;
    private readonly Action<bool> applyPaneWidths;
    private readonly Func<string?> chooseImportFile;
    private readonly Action<bool> setRendered;
    private readonly DispatcherTimer progressTimer = new() { Interval = TimeSpan.FromMilliseconds(100) };
    private bool disposed;
    private string currentSessionId = Guid.NewGuid().ToString("N");
    private string? currentPageId;
    private bool ready;
    private int vocabularyRevision;
    public int VocabularyRevision => vocabularyRevision;
    private sealed record PendingReadingMode(string RequestId, bool IsScript, ScriptArticle? Script);
    private PendingReadingMode? pendingMode;
    private string? pendingSnapshotId;
    private readonly HashSet<string> seenRequests = new(StringComparer.Ordinal);

    public WebReaderBridge(WebReaderHost host, MainViewModel viewModel, ReaderSettings settings,
        PaneLayout paneLayout, Func<double> availablePaneWidth, Action<bool> applyPaneWidths,
        Func<string?> chooseImportFile, Action<bool> setRendered)
    {
        this.host = host;
        ViewModel = viewModel;
        this.settings = settings;
        this.paneLayout = paneLayout;
        this.availablePaneWidth = availablePaneWidth;
        this.applyPaneWidths = applyPaneWidths;
        this.chooseImportFile = chooseImportFile;
        this.setRendered = setRendered;
        host.NavigationReset += ResetPage;
        host.MessageReceived += Receive;
        host.Failed += BrowserFailed;
        ViewModel.ArticleChanged += ArticleChanged;
        ViewModel.VocabularyChanged += VocabularyChanged;
        ViewModel.PlaybackChanged += RefreshPlayback;
        ViewModel.PlaybackChanged += SendPlayback;
        ViewModel.Notifications.Changed += SendNotifications;
        ViewModel.PropertyChanged += PropertyChanged;
        progressTimer.Tick += ProgressTimerTick;
    }

    private void ResetPage()
    {
        ready = false;
        currentPageId = null;
        pendingSnapshotId = null;
        seenRequests.Clear();
        pendingMode = null;
        setRendered(false);
    }

    private void BrowserFailed(string message)
    {
        ResetPage();
        progressTimer.Stop();
        ViewModel.Stop();
    }

    public void SendPaneWidth() => Post("setPaneWidth", new { rightWidth = paneLayout.RightWidth });

    private void ProgressTimerTick(object? sender, EventArgs e)
    {
        if (!host.IsUnavailable && ViewModel.Playback.IsArticleActive) SendPlayback();
    }

    private void RefreshPlayback()
    {
        progressTimer.Interval = ViewModel.IsScriptMode && ViewModel.Playback.IsArticlePlaying
            ? TimeSpan.FromMilliseconds(33) : TimeSpan.FromMilliseconds(100);
        if (!disposed && !host.IsUnavailable && ViewModel.Playback.IsArticleActive) progressTimer.Start();
        else progressTimer.Stop();
    }

    public void Receive(string raw)
    {
        if (disposed || host.IsUnavailable) return;
        try
        {
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
                if (ready) return;
                currentPageId = pageId;
                ready = true;
                seenRequests.Clear();
                SendInitialize();
                return;
            }

            if (!ready || pageId != currentPageId) return;
            if (type is "modeRendered" or "modeRenderFailed")
            {
                if (!TryMessageString(root, "sessionId", out string? modeSession) ||
                    modeSession != currentSessionId ||
                    !TryMessageString(root, "requestId", out string? modeRequest) ||
                    pendingMode is not { } pending || modeRequest != pending.RequestId)
                    return;
                bool succeeded = type == "modeRendered";
                if (succeeded)
                {
                    ViewModel.CommitReadingMode(pending.IsScript, pending.Script);
                    RefreshPlayback();
                }
                else
                {
                    ViewModel.Notify("WebPilotScriptRenderFailed",
                        "Script 切换失败或不可用，已保留原阅读模式",
                        NotificationSeverity.Error);
                }
                pendingMode = null;
                Post("actionResult", new { status = succeeded ? "success" : "failed" }, modeRequest);
                return;
            }
            if (type is "rendered" or "renderFailed")
            {
                if (!TryMessageString(root, "sessionId", out string? sessionId) ||
                    sessionId != currentSessionId ||
                    !TryMessageString(root, "requestId", out string? snapshotId) ||
                    snapshotId != pendingSnapshotId)
                    return;

                pendingSnapshotId = null;
                if (type == "renderFailed")
                    host.Fail("WebView2 正文渲染失败。请重新启动 ReadArticles。");
                else
                    setRendered(true);
                return;
            }

            // Only explicitly supported, validated user actions are accepted.
            if (type is not ("addWord" or "moveWord" or "deleteWord" or "importVocabulary" or
                "playArticle" or "stopArticle" or "seekArticle" or "playWord" or
                "switchReadingMode" or "setHighlight" or
                "setPanelWidth" or "dragPanelWidth" or "cancelPanelDrag" or "notificationDismiss"))
                return;

            if (!TryMessageString(root, "requestId", out string? requestId) ||
                !Guid.TryParse(requestId, out _))
                return;
            if (!seenRequests.Add(requestId)) return;

            bool currentSession = TryMessageString(root, "sessionId", out string? actionSession) &&
                actionSession == currentSessionId;
            bool currentVocabulary = root.TryGetProperty("vocabularyRevision", out var revision) &&
                revision.ValueKind == JsonValueKind.Number &&
                revision.TryGetInt32(out int value) && value == vocabularyRevision;
            if (!currentSession || !currentVocabulary)
            {
                Post("actionResult", new { status = "stale" }, requestId);
                return;
            }
            if (type == "dragPanelWidth")
            {
                if (TryPaneWidth(root, out double desired))
                {
                    applyPaneWidths(paneLayout.DragVocabulary(desired, availablePaneWidth()));
                }
                return;
            }
            if (type == "cancelPanelDrag")
            {
                applyPaneWidths(paneLayout.CancelVocabularyDrag(availablePaneWidth()));
                return;
            }
            if (type == "setHighlight")
            {
                if (!root.TryGetProperty("payload", out var payload) ||
                    payload.ValueKind != JsonValueKind.Object ||
                    !payload.TryGetProperty("enabled", out var enabled) ||
                    enabled.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                {
                    Post("actionResult", new { status = "invalid" }, requestId);
                    return;
                }
                ViewModel.HighlightsVisible = enabled.GetBoolean();
                Post("actionResult", new { status = "success" }, requestId);
                return;
            }
            if (type == "setPanelWidth")
            {
                HandlePaneWidth(root, requestId);
                return;
            }
            if (type == "notificationDismiss")
            {
                DismissNotification(root, requestId);
                return;
            }
            if (type == "switchReadingMode")
            {
                SwitchReadingMode(requestId);
                return;
            }
            if (type is "playArticle" or "stopArticle" or "seekArticle" or
                "playWord")
            {
                AudioIntent(type, root, requestId);
                return;
            }
            if (type == "addWord")
            {
                if (!root.TryGetProperty("payload", out var selected) ||
                    !ValidateSelection(selected, out string? text))
                {
                    EditResult(requestId, "invalid");
                    return;
                }
                var result = ViewModel.TryAddWord(text!);
                EditResult(requestId, ResultName(result),
                    result == VocabularyEditResult.Success ? ViewModel.Words.Count - 1 : -1);
                return;
            }
            if (type is "moveWord" or "deleteWord")
            {
                if (!root.TryGetProperty("payload", out var target) ||
                    !TryWordTarget(target, out int index))
                {
                    EditResult(requestId, "stale");
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
                        EditResult(requestId, "invalid");
                        return;
                    }
                    result = ViewModel.TryMoveWord(word, direction);
                    if (result == VocabularyEditResult.Success) nextIndex = index + direction;
                }
                else result = ViewModel.TryDeleteWord(word);

                EditResult(requestId, ResultName(result), nextIndex);
                return;
            }
            if (type == "importVocabulary")
            {
                if (!ViewModel.CanWriteVocabulary)
                {
                    EditResult(requestId, "unavailable");
                    return;
                }
                string? expectedPage = currentPageId;
                string expectedSession = currentSessionId;
                int expectedRevision = vocabularyRevision;
                string? path = chooseImportFile();
                if (disposed || host.IsUnavailable) return;
                if (path is null)
                {
                    EditResult(requestId, "cancelled");
                    return;
                }
                // A modal dialog can allow a context change; revalidate.
                if (expectedSession != currentSessionId ||
                    expectedRevision != vocabularyRevision || !ready ||
                    expectedPage != currentPageId)
                {
                    EditResult(requestId, "stale");
                    return;
                }
                var result = ViewModel.TryImportVocabulary(path);
                EditResult(requestId, ResultName(result));
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
    private void EditResult(string requestId, string status, int index = -1)
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
        Post("actionResult", new { status, index }, requestId);
    }

    private static bool TryPaneWidth(JsonElement root, out double value)
    {
        value = 0;
        return root.ValueKind == JsonValueKind.Object &&
            root.TryGetProperty("payload", out var payload) &&
            payload.ValueKind == JsonValueKind.Object &&
            payload.TryGetProperty("width", out var width) &&
            width.ValueKind == JsonValueKind.Number &&
            width.TryGetDouble(out value) && double.IsFinite(value) &&
            value >= 200 && value <= 400;
    }

    public void HandlePaneWidth(JsonElement root, string requestId)
    {
        if (!TryPaneWidth(root, out double value))
        {
            Post("actionResult", new { status = "invalid" }, requestId);
            return;
        }
        paneLayout.EndVocabularyDrag();
        applyPaneWidths(paneLayout.ResizeVocabulary(value, availablePaneWidth()));
        Post("actionResult", new { status = "success" }, requestId);
    }

    public void DismissNotification(JsonElement root, string requestId)
    {
        if (!root.TryGetProperty("payload", out var payload) ||
            payload.ValueKind != JsonValueKind.Object ||
            !payload.TryGetProperty("id", out var id) ||
            id.ValueKind != JsonValueKind.Number ||
            !id.TryGetInt64(out long notificationId) ||
            notificationId <= 0)
        {
            Post("actionResult", new { status = "invalid" }, requestId);
            return;
        }
        bool dismissed = ViewModel.Notifications.Dismiss(notificationId);
        Post("actionResult", new { status = dismissed ? "success" : "stale" }, requestId);
    }

    private object? PassageSnapshot()
    {
        var article = ViewModel.Article;
        return article is null ? null : new
        {
            title = article.Title,
            paragraphs = article.Paragraphs
        };
    }

    public static object ScriptSnapshot(ScriptArticle article) => new
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

    private void SwitchReadingMode(string requestId)
    {
        if (pendingMode is not null)
        {
            Post("actionResult", new { status = "unavailable" }, requestId);
            return;
        }
        var result = ViewModel.PrepareReadingMode(out bool nextScript, out var script);
        if (result != ReadingModePreparation.Ready)
        {
            Post("actionResult", new
            {
                status = result == ReadingModePreparation.Unavailable ? "unavailable" : "failed"
            }, requestId);
            return;
        }
        pendingMode = new PendingReadingMode(requestId, nextScript, script);
        Post("setReadingMode", new
        {
            mode = nextScript ? "script" : "passage",
            article = nextScript ? ScriptSnapshot(script!) : PassageSnapshot()
        }, requestId);
    }

    private void AudioIntent(string type, JsonElement root, string requestId)
    {
        var article = ViewModel.Article;
        if (article is null)
        {
            Post("actionResult", new { status = "unavailable" }, requestId);
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
                Post("actionResult", new { status = "invalid" }, requestId);
                return;
            }
            if (data.TryGetProperty("direction", out var direction))
            {
                if (direction.ValueKind != JsonValueKind.String ||
                    direction.GetString() is not ("backward" or "forward"))
                {
                    Post("actionResult", new { status = "invalid" }, requestId);
                    return;
                }
                ViewModel.SeekArticleRelative(direction.GetString() == "backward");
            }
            else if (!data.TryGetProperty("position", out var time) ||
                     time.ValueKind != JsonValueKind.Number ||
                     !time.TryGetDouble(out double seconds) || !double.IsFinite(seconds) ||
                     seconds < 0 || seconds > ViewModel.Playback.ArticleDuration)
            {
                Post("actionResult", new { status = "invalid" }, requestId);
                return;
            }
            else
                ViewModel.Playback.SeekArticle(seconds);
        }
        else if (type == "playWord")
        {
            if (!root.TryGetProperty("payload", out var target) ||
                !TryWordTarget(target, out int index) ||
                !TryMessageString(target, "accent", out string? accent) ||
                accent is not ("uk" or "us") ||
                (accent == "uk" ? !ViewModel.Words[index].CanPlayUk : !ViewModel.Words[index].CanPlayUs))
            {
                Post("actionResult", new { status = "invalid" }, requestId);
                return;
            }
            ViewModel.PlayWord(ViewModel.Words[index].Entry, accent);
        }
        // Playback failures continue to be surfaced by ReaderPlayback notifications.
        // "accepted" is not a false claim that a media file played successfully.
        Post("actionResult", new { status = "accepted" }, requestId);
        SendPlayback();
    }

    private static string ResultName(VocabularyEditResult result) => result switch
    {
        VocabularyEditResult.Success => "success",
        VocabularyEditResult.Duplicate => "duplicate",
        VocabularyEditResult.Unavailable => "unavailable",
        _ => "failed"
    };

    private bool TryWordTarget(JsonElement payload, out int index)
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

    public bool ValidateSelection(JsonElement payload, out string? text)
    {
        text = null;
        if (payload.ValueKind != JsonValueKind.Object ||
            !TryMessageString(payload, "mode", out string? mode) ||
            mode is not ("passage" or "script") ||
            !payload.TryGetProperty("text", out var selectedText) ||
            selectedText.ValueKind != JsonValueKind.String ||
            !payload.TryGetProperty("paragraphIndex", out var paragraph) ||
            paragraph.ValueKind != JsonValueKind.Number || !paragraph.TryGetInt32(out int paragraphIndex) ||
            !payload.TryGetProperty("sentenceIndex", out var sentence) ||
            sentence.ValueKind != JsonValueKind.Number || !sentence.TryGetInt32(out int sentenceIndex) ||
            !payload.TryGetProperty("offset", out var offsetValue) ||
            offsetValue.ValueKind != JsonValueKind.Number || !offsetValue.TryGetInt32(out int offset) ||
            !payload.TryGetProperty("length", out var lengthValue) ||
            lengthValue.ValueKind != JsonValueKind.Number || !lengthValue.TryGetInt32(out int length))
            return false;

        string? candidate = selectedText.GetString();
        if (!ViewModel.ValidateReadingSelection(mode == "script", paragraphIndex, sentenceIndex,
                offset, length, candidate))
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

    private void SendInitialize()
    {
        if (!ready) return;
        pendingSnapshotId = Guid.NewGuid().ToString("D");
        setRendered(false);
        object? passage = ViewModel.IsScriptMode && ViewModel.Script is { } script
            ? ScriptSnapshot(script) : PassageSnapshot();
        Post("initialize", new
        {
            article = passage,
            mode = ViewModel.IsScriptMode ? "script" : "passage",
            canWriteVocabulary = ViewModel.CanWriteVocabulary,
            vocabulary = VocabularyRows(),
            settings = SettingsSnapshot(),
            playback = PlaybackSnapshot(),
            notifications = NotificationSnapshot()
        }, pendingSnapshotId);
    }

    private object[] VocabularyRows() => ViewModel.Words.Select(w => (object)new
    {
        word = w.Word,
        family = w.Entry.Family ?? new List<string>(),
        phoneticUk = w.PhoneticUk,
        phoneticUs = w.PhoneticUs,
        meanings = w.Meanings.Select(m => new { pos = m.Pos, text = m.Text }).ToArray(),
        canPlayUk = w.CanPlayUk,
        canPlayUs = w.CanPlayUs
    }).ToArray();

    public object SettingsSnapshot() => new
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
        rightWidth = paneLayout.RightWidth,
        forwardStep = settings.ForwardStep,
        backwardStep = settings.BackwardStep
    };

    private object PlaybackSnapshot()
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

    public object NotificationSnapshot() => new
    {
        // Service.Visible is the first three FIFO messages, newest first
        // within the admitted group. Queued messages are not transmitted
        // until a displayed notification has completed dismissal.
        visible = ViewModel.Notifications.Visible.Select(n =>
            new { id = n.Id, message = n.Message, severity = n.Severity.ToString() }).ToArray()
    };

    private void ArticleChanged()
    {
        // A previous display is never presented as the newly committed article.
        currentSessionId = Guid.NewGuid().ToString("N");
        vocabularyRevision = 0;
        pendingMode = null;
        seenRequests.Clear();
        SendInitialize();
    }

    private void VocabularyChanged()
    {
        // Only a committed vocabulary edit emits this event.
        vocabularyRevision++;
        if (ready)
            Post("setVocabulary", new
            {
                vocabulary = VocabularyRows(),
                canWriteVocabulary = ViewModel.CanWriteVocabulary
            });
    }

    private void PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
    {
        // The native settings transaction publishes its complete snapshot once after apply.
        if (args.PropertyName == nameof(ViewModel.HighlightsVisible))
            SendSettings();
    }

    public void SendSettings()
    {
        if (ready)
            Post("setSettings", SettingsSnapshot());
    }

    public void SendPlayback()
    {
        if (ready) Post("setPlayback", PlaybackSnapshot());
    }

    private void SendNotifications()
    {
        if (ready) Post("setNotifications", NotificationSnapshot());
    }

    private void Post(string type, object payload, string? requestId = null)
    {
        if (disposed || host.IsUnavailable) return;
        if (!host.Dispatcher.CheckAccess())
        {
            host.Dispatcher.BeginInvoke(new Action(() => Post(type, payload, requestId)));
            return;
        }
        if (!ready || currentPageId is null)
            return;
        try
        {
            string json = JsonSerializer.Serialize(new
            {
                version = 1,
                type,
                pageId = currentPageId,
                sessionId = currentSessionId,
                vocabularyRevision = vocabularyRevision,
                requestId,
                payload
            });
            host.SendJson(json);
        }
        catch (Exception error)
        {
            AppLogger.Default.Warning("WebViewPilotSendFailed", error);
            host.Fail("WebView2 通信失败。请重新启动 ReadArticles。");
        }
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        pendingMode = null;
        progressTimer.Stop();
        progressTimer.Tick -= ProgressTimerTick;
        ViewModel.ArticleChanged -= ArticleChanged;
        ViewModel.VocabularyChanged -= VocabularyChanged;
        ViewModel.PlaybackChanged -= RefreshPlayback;
        ViewModel.PlaybackChanged -= SendPlayback;
        ViewModel.Notifications.Changed -= SendNotifications;
        ViewModel.PropertyChanged -= PropertyChanged;
        host.NavigationReset -= ResetPage;
        host.MessageReceived -= Receive;
        host.Failed -= BrowserFailed;
    }
}
