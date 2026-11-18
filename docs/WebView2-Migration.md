# WebView2 reader migration — implementation log

Date: 2026-11-18  
Branch: `feature/webview2-reader` (based on `feature/script-reading-mode`, not `main`).

## Approved end state

One WebView2 renders the middle reader, title, right vocabulary pane, both footers, selection affordances, and notifications. C#/WPF keeps the left navigation tree, all file and audio business logic, settings and durable state. No React, Node, HTML audio, or second WebView2.

## 2026-11-18 follow-up: Core test expectations updated

The Windows build at `54c8d80` passed without errors, but `Test.ps1 -Suite Core` reported **9 groups passed, 1 failed**: content-only had two failing assertions, while rendering, tree, editing, notifications and lazy-tree groups passed.

The failures were **outdated test expectations** from before the user's approved changes, not evidence of an application build failure:

- `ContentChecks` expected an old error to survive a successful article/library switch; now it verifies that successful switches clear all visible and queued notifications, while a later failed article open still reports an error.
- `ScriptChecks` expected `Another.json` containing `{}` to be listed as a passage; now it verifies that only `Example.json` with `filetype: "passage"` is visible and Script/untyped JSON are excluded.

Only these two test files were changed. The corrected test source is committed, but **the user has not yet provided a Windows build/Core result for the new commit**. Retest with `dotnet build ReadArticles.Tests -c Release` followed by `.\tools\Test.ps1 -Suite Core`.

## Latest revision: simple notification clearing and strict lazy directory discovery (2026-11-18)

The user confirmed on Windows at commit `384e970` that the Toast hover fix works (messages remain while hovered); Release build and `Test.ps1 -Suite Core` passed 10/10 groups, including 34 content checks.

### Article changes and notifications

The user rejected per-article notification metadata as unnecessary. The intermediate `NotificationItem.ArticlePath`, `NotificationService.ClearArticle`, and `MainViewModel.NotifyGlobal` changes have therefore been **reverted**. The implemented rule is simpler: after a **successful** `OpenArticle` commit, stop the old audio, call the existing `Notifications.Clear()`, and only then display the new article. This clears **all** visible and queued Info/Warning/Error messages in both WPF and WebView2. Failed article opens do not clear the current messages (and may report a new error). Successfully reopening/changing the library also clears prior notifications before any new library warnings.

### Tree appearance and discovery

- The WPF `TreeViewItem` has a fixed 20-DIP expander column. Header content and its TextBlock are explicitly left-aligned, with a 2-DIP text left margin. Dragging the left pane wider or narrower should no longer separate folder/file names from their expander glyphs.
- Existing `ExpandDirectoryAsync` remains lazy and scans only the **immediate** directory of the expanded node; no recursive descent or eager opening of descendant files. Child folders remain as unloaded navigation nodes until clicked.
- Direct files become visible leaves **only** if they have a `.json` extension (case-insensitive) and are parseable JSON objects with a **top-level** string property `"filetype": "passage"` (exact spelling and value). Other extensions, missing/incorrect types, non-passage JSON, nested-only filetype, and malformed JSON remain hidden.
- Existing hidden/cache/audio directory filtering is preserved. Initial library-root discovery still recognizes Book folders using the existing `book.json` marker; this change concerns scanning expanded directory nodes.
- Tests now exercise a successful article switch clearing both visible and queued notifications, while a failed open leaves previous notifications intact; valid/invalid JSON filetype filtering; no recursive child scan; failed-open selection rollback; and expander-to-label gap under two different left pane widths.

**Windows acceptance still needed for these new commits:**

```powershell
cd C:\Projects\ReadArticles
git pull --ff-only origin feature/webview2-reader
dotnet build ReadArticles.Tests -c Release
.\tools\Test.ps1 -Suite Core
dotnet run --project ReadArticles -c Release -- --webview-pilot
```

Confirm an old Error plus queued notifications immediately vanish after successfully switching articles; switching to an invalid article does not erase the old error. Resize the left library column and verify symbols remain attached to names. Expand folders individually and verify only direct child folders and correctly typed Passage JSON files appear. Do not merge Draft PR #16 before Windows acceptance.

## Windows follow-up: transient Toast hover fixed (2026-11-18)

At commit `1e757fd`, the user's Windows Release build and Core suite passed (**10/10 groups, 34 content checks**) with two nonfatal `CS8602` nullable warnings in `NotificationChecks.cs`. The user accepted the three-notification limit, Error manual-dismiss behavior, and FIFO refill, but reported that **Info/Warning still disappeared while the mouse was hovering**. The user also asked for current default widths: left library **310 DIP**, right vocabulary **390 DIP**; the middle pane consumes remaining width. These are startup defaults, adjusted by available window width and layout constraints.

**Root cause and changes submitted (Windows retest pending):** The browser's `renderNotifications` previously unconditionally used `host.append(entry.card)` for every notification snapshot, even when the DOM order did not change. Moving an existing hovered card may generate `mouseleave` and restart a paused timer. The renderer now calls `insertBefore` only when a node is at the wrong position. Each card uses both pointer and mouse enter/leave handlers to pause/resume, and its expiry callback additionally checks the actual CSS `:hover` state before initiating the fade. Manual close and Error's no-auto-fade rule remain unchanged.

A direct JavaScript fake-DOM/timer simulation verified: three active cards (only two non-Error timers), a pointer entering Info after 1500 ms cancels its timer, twenty identical state snapshots do not move the node, fifteen further seconds of hovering do not dismiss Info, and leaving resumes exactly the previous remaining time before the 1000 ms fade. Warning continues to expire normally and Error only closes manually. The C# notification test references that emitted CS8602 now use explicit non-null return contracts.

**Required Windows test:**

```powershell
cd C:\Projects\ReadArticles
git pull --ff-only origin feature/webview2-reader
dotnet build ReadArticles.Tests -c Release
.\tools\Test.ps1 -Suite Core
dotnet run --project ReadArticles -c Release -- --webview-pilot
```

Trigger a normal or warning notification. Hover over **the message text** before its expiration and hold the pointer there for at least 10 seconds: it must not fade. Move to its `...>>展开` or close-button area while remaining inside the card: it must still not fade. Then move the pointer outside: the **remaining** display time should resume, followed by the original one-second fade. Error stays visible until closed. Confirm startup pane widths still reset to 310/390 DIP (subject to layout constraints).

## Current acceptance decision: three-slot FIFO and resettable divider widths

**2026-11-18 — new user requirements override earlier Toast/archive and width-persistence designs.** Stage 6 has *not* yet passed Windows validation for these changes.

### Notifications

- At most **three** notifications are visible at the bottom-center of the article pane. Every published message enters one FIFO queue in the C# `NotificationService`; only the earliest three pending messages occupy slots. The newest *within those admitted three* is displayed at the top. Further notifications wait without any overflow-count button, archive panel, silent eviction, or automatic dismissal while queued.
- **Error** messages never auto-fade. They retain the visible slot until manually closed, with the normal 1-second exit fade. **Info** and **Warning** begin a ~4.65-second visual countdown only when first displayed, then exit with a 1-second downward fade. Hover pauses and resumes the countdown for transient notifications; hovering an Error leaves it visible because it has no countdown.
- All three types retain their colored, borderless, equal-width (responsive maximum 520px) cards, `普通：/警告：/错误：` prefixes, centered layout, two-line truncation and `...>>展开` / `<<收起` controls.
- User-visible vocabulary edit results are now published by C# to that same queue. Duplicate and failed-save actions continue to use their existing authoritative ViewModel notifications, so no duplicate result is synthesized in JavaScript. Repeatedly published messages remain distinct and queued in arrival order.
- The old `还有 N 条错误` UI and its browser/WPF fallback handlers have been deleted. The existing `AppLogger` continues diagnostic logging; no new raw article or vocabulary content is copied to persistent notification-history files.

### Pane widths

- **Left and right pane widths are session-only.** Every fresh launch uses the existing defaults (`LeftWidth = 310`, `RightWidth = 390`), subject to minimum sizes/available window width. The user may resize them freely while that session remains running.
- `ReaderSettings.LeftWidth` and `RightWidth` are now excluded from JSON serialization and deserialization via `[JsonIgnore]`. Historical width keys in an existing `settings.json` are ignored; future saves omit them. The persisted font size, accent, volume, skip intervals and library path are unchanged.
- New C# tests verify FIFO ordering, exactly three admitted messages, lack of silent eviction even when keys repeat, and the width JSON round-trip defaults. A JavaScript host simulation verified three active cards, a two-item waiting queue, no timer on Error, hover pause on Info, delayed admission after a full 1-second fade, and manual Error dismissal.

### Windows acceptance (required)

```powershell
cd C:\Projects\ReadArticles
git pull --ff-only origin feature/webview2-reader
dotnet build ReadArticles.Tests -c Release
.\tools\Test.ps1 -Suite Core
dotnet run --project ReadArticles -c Release -- --webview-pilot
```

Generate **five** messages of mixed types. Only three should appear, oldest three admitted first; close an Error or let a transient finish and observe one queued message enter from above. An Error must not disappear by itself even after a long wait. Hover over a transient before its expiry to confirm it remains visible. Verify the archive/count button has disappeared entirely. Move left and right dividers; close and reopen the same build and verify they return to defaults (font and seek-step preferences may remain persisted).

**Do not treat older notes below about automatic Error fading, error archive counts or divider-width persistence as active requirements.** They record earlier implementation checkpoints that have now been superseded.

## First checkpoint: opt-in skeleton

The first code checkpoint introduces a **read-only pilot**, not a replacement of the production UI:

- The existing WPF reader is still the default, including existing tests and user-data paths.
- `--webview-pilot` initializes one WebView2 overlay spanning the center/right columns.
- HTML, CSS and JS are embedded in the application assembly and served from a strict, fixed URL whitelist through `WebResourceRequested`. Nothing is loaded from the user library or an external CDN.
- The WPF host initializes WebView2 with `userdata/cache/webview2/` as the browser user data folder. No cache directory is created for normal, non-pilot startup.
- JavaScript sends `{version:1,type:"ready",pageId}`; C# responds with a snapshot (`initialize`), and JavaScript acknowledges `rendered`. The overlay is made visible only after acknowledgement.
- C# sends only trusted article, vocabulary, playback and notification snapshots. The pilot's buttons are visibly disabled. All mutation messages are deliberately rejected until the later business-bridge stage.
- Article changes rotate the session ID. The host rejects unrecognized message types and wrong page IDs for follow-up messages, and refuses messages from other document URLs.
- Initialization failure or WebView2 process failure falls back to the original WPF reader and reports a native error.

**Not yet implemented:** Script mode in WebView2, DOM vocabulary matching and highlights, live selection/add, user actions, continuous 33 ms playback coloring, notification animations, production settings and input. The pilot intentionally renders a first-pass, non-interactive Passage preview only. The existing WPF implementation remains authoritative.

## Windows acceptance results supplied on 2026-11-18

On commit `ec41182`, the user ran:

- `dotnet build ReadArticles.Tests -c Release`: **passed**.
- `--webview-pilot-only`: **1 passed / 0 failed**, verifying bundled assets.
- `Test.ps1 -Suite Core`: **10 groups passed / 0 failed**.
- `Test.ps1 -Suite UI`: **12 groups passed / 1 group failed**, with two WPF assertions:
  1. `WindowChecks.cs`: the old test counted the hidden WebView2 pilot as a fourth root-grid pane. Commit `3a16b73` filters out collapsed elements; rerun required.
  2. `ScriptChecks.cs`: `Script vocabulary highlight changed pixels: 0`. This is a pre-existing failure on the old WPF Script renderer, unchanged by the WebView2 pilot. The failing assertion is preserved until the new WebView2 renderer has a real visual test.
- `Test.ps1 -Suite Full`: **120 checks passed / 2 failed** (the same two UI assertions).

The earlier `Publish.ps1` and `Test-PublishedApp.ps1` passed, but **only for the default WPF launch path**. There is no submitted run log yet for `--webview-pilot`, including the published EXE.

**Do not claim complete UI/Full acceptance.** The collapsed-element test fix has been committed but not yet retested on the user's Windows desktop. The original Script pixel test remains failing.

## Direct `--window-only` invocation caveat

On the later Windows run at commit `21c43e6`, the previously failing floating-notification layout check **passed** after excluding collapsed pilot elements. The direct command

`dotnet run --project ReadArticles.Tests -c Release -- --window-only`

then reported an unrelated failure at `ReaderWindowChecks.cs:31`: the fixed article's first vocabulary entry requires both UK and US audio eligibility. The standalone invocation uses `tests/TestData/Library` without the sibling recordings from `tests/TestData/Audio`. The canonical `tools/Test.ps1 -Suite UI` stages both sources under a temporary library using `Prepare-TestWorkspace` and sets `READARTICLES_TEST_LIBRARY`; the same assertion passed in the earlier full UI run with that isolated setup. **Do not weaken the audio eligibility assertion** or edit source fixtures to address this.

The old WPF Script visual assertion continues to report `Script vocabulary highlight changed pixels: 0`; the new browser rendering has not yet undergone its own pixel acceptance. Re-run the canonical UI and Full suites after code changes, and test `--webview-pilot` in an interactive Windows process.

## Pilot visual acceptance and stage 2 protocol checkpoint (2026-11-18)

The user launched `dotnet run --project ReadArticles -c Release -- --webview-pilot` on Windows and supplied a screenshot showing the left WPF tree, the Passage title/body, and the right WebView2 vocabulary list (81 entries). No browser initialization error occurred. **The read-only pilot's initial WebView2 loading/rendering milestone is confirmed.**

The screenshot also revealed a horizontal page scrollbar at the bottom and partially clipped right toolbar buttons. The CSS has since been changed to constrain the root/workspace overflow, allow grid tracks to shrink, and wrap the vocabulary footer buttons. **These CSS changes have not yet been verified by a new Windows screenshot.**

Stage 2 groundwork now includes:

- Each host `initialize` carries a unique `requestId`; only a matching `rendered` acknowledgment for the active page and article session can show the overlay. `renderFailed` falls back to WPF.
- On trusted navigation/reload, pending acknowledgments and page identity are invalidated.
- Incoming messages are length-limited and checked for protocol version, string types, valid page/request UUIDs, known operations, current article session and vocabulary revision.
- Pilot operations are explicitly **read-only**: recognized action attempts receive `actionResult` with `unavailable` or `stale`. No vocabulary or audio command is executed.
- Vocabulary revisions advance only on committed vocabulary snapshot replacement, not merely on a highlight-visibility toggle. C# emits incremental `setVocabulary` and `setSettings` changes without reconstructing the Passage DOM.
- JS rejects older vocabulary revisions and messages for a prior article session, and acknowledges the latest full snapshot ID. Frontend test simulation confirmed `ready`, `rendered` ID, incremental vocabulary refresh, stale revision/session filtering and settings refresh.

**Next validation:** rebuild and rerun the Windows pilot, switch between two articles rapidly, check the title/word list follow the selected article, inspect whether the bottom horizontal scrollbar is absent and every disabled toolbar button remains visible. Then run Core and the portable publish checks. Real WebView2 DOM integration tests, audio/Script, mutation action success/failure and final legacy renderer deletion are *not yet accepted*.

## Stage 3 read-only Passage interactions — pending Windows acceptance

Following the successful Windows pilot screenshot/build, the browser page now includes:

- A port of the original `VocabularyMatcher` algorithm: earliest hit, longest phrase at the same start, no match overlaps, case-insensitive candidate deduplication, straight/curly apostrophe and hyphen boundary rules, and digits **excluded** from the English boundary set.
- Passage blank markers `[[N]]` displayed as `____N____` without creating sentence audio eligibility; matching is limited to the unmodified text fragments on each side of a blank.
- Browser CSS Custom Highlight API for independent vocabulary background layers and selected vocabulary focus, without inserting nested `span` tags into original sentence text. The default highlight setting is still off; click the newly enabled **高亮** button in the pilot to toggle it.
- Clicking a vocabulary row selects that entry visually and scrolls to its first matching occurrence in the article. The paragraph layout and text selection are not rebuilt by highlighting or focusing.
- One-sentence Passage text selection with its real SID, normalized selected text, rendered UTF-16 position and length. Cross-sentence or empty/whitespace-only selections are rejected. The floating `+` sends a correlated `addWord` request but, **in this pilot**, the host rejects saving and the UI explicitly reports `只读试验版：尚未保存`.
- C# validates page UUID, article session, vocabulary revision, request ID and the highlight toggle payload. Only visual `setHighlight` is enabled. All actual vocabulary mutations, audio and Script operations still return `unavailable` and cannot touch the vocabulary file.
- The simple JS matching algorithm was exercised against six functional fixtures in the development runtime: earliest/longest, apostrophe variants, digit and hyphen boundaries, deduplication and non-overlap. These are **not** a substitute for automated browser UI tests.

**Windows visual acceptance still required:** Start `--webview-pilot` and open a working-copy Passage with vocabulary entries. Toggle 高亮 on/off, select one right-hand word and confirm all its occurrences get the stronger background and the first one scrolls into view. Select a phrase inside one SID and check the floating `+`; click it and verify the UI explicitly says it was not saved, then inspect the vocabulary JSON and confirm it did not change. Try a selection across two sentences and confirm `+` disappears. Repeatedly switch articles and verify no old highlights appear. Run build, Core and publish checks before considering this checkpoint accepted.

**Known limitations:** The pilot continues to be Passage-only and read-only; timed Script render/word audio/word save/notifications animation have not been implemented. Browser UI pixel assertions and interactive copy tests are pending. The original WPF Script background pixel failure remains unsuppressed.

## Stage 3 Windows acceptance and stage 4 editing checkpoint (2026-11-18)

The user supplied a Windows WebView2 screenshot and Release/Core logs for commit `5199bb9`:

- `dotnet build ReadArticles -c Release`: **passed**.
- `tools/Test.ps1 -Suite Core`: **10 groups passed, 0 failed**.
- Toggling vocabulary highlight paints the Passage matches pink. Selecting a vocabulary row locates its occurrence in the Passage.
- Selecting text within a single SID shows the floating `+` even when the selection wraps across visual lines. Selecting across two sentences hides the `+`. This is the **intended** single-SID rule; the restriction is by sentence identity, not visual line.
- Stage 3 is accepted for the tested Passage behaviors. This does **not** assert that the old WPF Script pixel regression or final browser rendering tests pass.

### Stage 4 implementation — pending Windows acceptance

The pilot now offers *real* vocabulary edits. **Use a copy of your library for the first interactive test**:

1. `MainViewModel.Vocabulary.cs` exposes `TryAddWord`, `TryMoveWord`, `TryDeleteWord`, and `TryImportVocabulary` returning explicit `Success`, `Duplicate`, `Unavailable` or `Failed`. The WPF Action-based public methods remain compatible.
2. The WebView2 host validates the page/request IDs, committed article session, current vocabulary revision, specific vocabulary index and word name, or the selected SID/visible-text UTF-16 offset and length. For blank markers, the verification uses their **display** representation rather than the underlying JSON offset.
3. Only the existing `ReaderWorkspace.ReplaceVocabulary` → `VocabularyRepository.Save` chain can write the JSON. It retains fingerprint conflict checks, backup verification, and atomic replacement. Browser UI never writes files.
4. Following successful persistence, the host sends an authoritative `setVocabulary` snapshot; a correlated `actionResult` reports `success`, `duplicate`, `failed`, `stale`, `invalid`, `unavailable`, or `cancelled`. No optimistic additions or reordered rows are shown.
5. Browser toolbar actions enable only for a writable vocabulary and a selected valid row. Movement and deletion use index + exact word + revision; import uses the original WPF file picker and replacement confirmation. Import cancellation leaves the vocabulary unchanged.
6. A dedicated C# editing regression was added to the existing `--editing-only` suite for duplicate/no-write, successful add/reorder/delete/import persistence, and external file changes that must not be overwritten.
7. Script, article/audio controls and final Toast migration remain deferred to their scheduled stages. Default WPF mode stays the fallback and remains unmodified structurally.

**Suggested acceptance sequence:**

```powershell
git pull --ff-only origin feature/webview2-reader
dotnet build ReadArticles.Tests -c Release
.\tools\Test.ps1 -Suite Core
dotnet run --project ReadArticles -c Release -- --webview-pilot
```

Use a throwaway *copy* of an article and its vocabulary JSON to test the following: add a new word (confirm it appears both on screen and in the file), add it again (duplicate, file unchanged), move it up/down (order saved), delete it (entry removed), cancel import (no change), import another valid vocabulary (replacement saved), and modify the vocabulary JSON in an external editor before a subsequent browser edit (must fail without overwriting external changes). Verify the original non-pilot launch still behaves normally.

**Status:** Code submitted on the migration branch; no post-edit Windows build, UI run or manual vocabulary write verification has been received yet. Do not merge PR #16 to `main` before all stages and user approval.

## Stage 4 Windows acceptance and stage 5 Script/audio implementation (2026-11-18)

The user verified `08f70aa` on Windows:

- `dotnet build ReadArticles.Tests -c Release`: **passed**.
- `.\tools\Test.ps1 -Suite Core`: **10/10 groups passed**; the new WebView2 vocabulary edit regression also passed.
- In the WebView2 pilot, adding a selected word writes the vocabulary JSON, duplicate words are rejected without writes, move up/down/delete persist in the file, import cancellation preserves the file, valid import commits, and an externally changed vocabulary JSON blocks stale overwrites. **Stage 4 is accepted for these tested behaviors.**

### Stage 5 submitted code (not yet Windows-accepted)

- The WebView2 Script button loads `<name>.script.json` via the existing C# `ArticleRepository.LoadScript` **only on demand**. Missing/invalid Script leaves the active Passage unchanged. A new session/request-scoped `setReadingMode → modeRendered/modeRenderFailed → actionResult` handshake commits the mode only after the browser confirms rendering.
- Script uses the companion `title`, paragraph/sentence structure, and `timedUnits` with their original UTF-16 offsets and **one start/end time interval per unit**. Unaligned or untimed units stay readable without gradient. Script sentences do not fabricate Passage SID/audio eligibility. Selection within one Script sentence sends paragraph/sentence indices, not a fabricated SID, and is validated against the trusted Script model before vocabulary edits.
- Browser script rendering preserves the original text and uses a non-interactive red text copy clipped to rectangles measured by `Range.getClientRects()`. The clip widths are advanced sequentially across visual lines using the unique unit progress `clamp((t - start)/(end - start),0,1)`. This is designed to avoid repeated gradients on wrapped fragments and to allow independent CSS vocabulary backgrounds across timed units.
- C# remains the unique source of article playback time. Its existing 100 ms UI timer switches to 33 ms when WebView2 Script is playing and sends lightweight `setPlayback` position/state updates; stop/end clears red, seek recomputes it. No HTML Audio or additional audio clock is introduced.
- Article play/pause/stop/seek, vocabulary UK/US pronunciation and right-click Passage SID pronunciation send validated intents to existing C# `ReaderPlayback`. The host returns `accepted` only to indicate the command was processed, **not** to claim that the audio file actually played. Failures are still reported via existing notifications.
- A C# contract test checks the Script snapshot's title/unit offsets/nulls and Script selection validation without fabricated SIDs. A separate JavaScript host-message simulation passed initial Passage rendering, article play intent, Script switch request/ack, blank Script title and playback UI state.

**Windows acceptance (still required):**

```powershell
cd C:\Projects\ReadArticles
git pull --ff-only origin feature/webview2-reader
dotnet build ReadArticles.Tests -c Release
.\tools\Test.ps1 -Suite Core
dotnet run --project ReadArticles -c Release -- --webview-pilot
```

Use a **working-copy** library that contains a Passage, optional `<name>.script.json`, an article MP3/WAV and available UK/US word/Passage sentence recordings. Verify the following behaviors, not just whether the buttons exist:

1. Toggle Passage → Script → Passage repeatedly; Script title (including an empty title), paragraphs and independent scroll positions display correctly. Missing or invalid Script leaves Passage displayed and reports a warning/error.
2. Start article playback in Script and examine a timed unit wrapping across two or more visual lines (especially `non-identical` wrapping after the hyphen). Red foreground must advance along the first line before reaching the next, with **one** source interval, while matching vocabulary retains its pink background.
3. Pause, seek backward/forward, stop, and reach the natural end of the track. Red foreground must freeze, recompute, and clear as appropriate; the article play/stop buttons and time slider must stay synchronized with C#.
4. Play UK/US vocabulary and right-click an eligible Passage SID, including an intentionally missing sentence recording. Vocabulary/sentence sound should use the existing pronunciation channel and article interruption semantics; Script must never offer a synthetic SID.
5. Select words in one Script sentence (including wrapped text) and add to the Passage-associated vocabulary file. Cross-Script-sentence selection must hide the add button. Duplicate/add/import behavior must still respect the Stage 4 data-safety guarantees.
6. Resize the window and change reading font in Script mode. Gradient must track the **new** layout, not stale line rectangles. Verify scroll and word focus across mode toggles.

The original WPF Script background pixel regression remains known and is not suppressed. Browser-specific visual/pixel coverage and final legacy cleanup remain pending. **Do not merge PR #16** until all stages and user approval.

## Stage 5 Windows clock regression and minimal fix (2026-11-18)

The user's Windows run at commit `4ea5ad0` confirmed the Release test build and Core suite (**10 groups passed**), and confirmed correct Passage/Script switching, vocabulary highlight, UK/US and Passage pronunciation, and cross-sentence selection semantics.

**Live synchronization was not accepted.** During audio playback the WebView2 progress bar and Script red foreground remained frozen, but both jumped to the correct current position when playback was paused. Source inspection identified the exact regression: `MainWindow.Playback.cs::ProgressTimerTick` called `UpdateProgress()` (which only updates the old WPF reader) but never sent the updated position to WebView2. Only `ViewModel.PlaybackChanged` events sent `setPlayback`, explaining the correct one-time update on pause.

Commit `2350502` changes the existing timer callback to call `SendWebPilotPlayback()` on active article audio after the WPF update. It **reuses the same C# audio clock and existing 33 ms Script interval**, without a browser timer, altered Script timing, or added audio engine.

**Required retest:** Run `dotnet build ReadArticles.Tests -c Release` and `.\tools\Test.ps1 -Suite Core`; launch `--webview-pilot` with audio. Confirm the progress bar visibly advances during playback rather than only on pause; check the Script red text follows continuously, pauses in place, resumes and seeks correctly, and clears on stop/natural end. Also verify Passage play/pause and old WPF default launch. No Windows test of this new commit has yet been received.

## Stage 5 accepted and stage 6 WebView2 reading controls (2026-11-18)

The user retested the live playback fix at commit `e8ec18a` on Windows and confirmed:

- `dotnet build ReadArticles.Tests -c Release` passed.
- `Test.ps1 -Suite Core`: **10/10 groups passed** (including all 30 content checks).
- During article playback the WebView2 progress slider continuously advances and Script red foreground follows the audio. Pause/resume/seek synchronize correctly; stopping or naturally reaching the end clears the red foreground.

**Stage 5 is accepted for the reported real-world scenarios.**

### Stage 6 implementation submitted — Windows acceptance pending

- An 8px HTML divider now separates the central article and right vocabulary pane, supports pointer drag and keyboard Left/Right, and saves the final right-panel width through the existing C# `ReaderSettings` mechanism. The hidden legacy WPF column no longer overwrites the browser's saved right width on window close or left-pane resizing.
- The browser article toolbar has an in-page reading settings popover with the *existing* supported values only: 14/15/16 font, uk/us accent, forward 4/7/10 seconds, backward 5/8/15 seconds. The C# host validates each value, persists through `SaveSettings`, and sends back the authoritative `setSettings` snapshot. Left-side WPF setting changes also keep the browser synchronized.
- With WebView2 displayed, WPF does not intercept article playback keyboard shortcuts. Browser Space starts/pauses playback, Left/Right use the configured seek steps, while inputs, buttons, selects, contenteditable controls and the divider retain their normal keyboard behavior. No second playback clock was introduced.
- The browser Toast uses `NotificationService` for authoritative messages, newest-first ordering, severity, deduplication, error persistence and explicit dismiss; its own visual layer manages entry/exit animations, 4650 ms transient lifetime + 350 ms exit, and hover pause. Existing toast DOM nodes and lifetime are retained across subsequent notification snapshots. An overflow control shows all undisplayed errors with individual dismiss buttons.
- When `--webview-pilot` is active, the hidden legacy WPF ToastHost is **not** attached, avoiding duplicate expiry controllers. If WebView2 fails, WPF ToastHost attaches for native fallback. The default WPF startup remains unchanged.
- A new Core regression test covers allowed/rejected browser settings and C# notification dismissal. A browser-message simulation passed initialization, toast persistence, overflow, font/accent settings, divider persistence intents and error dismissal. This is not a Windows/WebView2 UI test.

**Windows acceptance procedure:**

```powershell
cd C:\Projects\ReadArticles
git pull --ff-only origin feature/webview2-reader
dotnet build ReadArticles.Tests -c Release
.\tools\Test.ps1 -Suite Core
dotnet run --project ReadArticles -c Release -- --webview-pilot
```

Check **in a working-copy library**:

1. Drag the divider both ways and restart. The vocabulary pane width should persist; left library divider should still work independently. Narrow and enlarge the window; no full-page horizontal scrollbar should appear.
2. Open the in-page 设置 menu and change font, default accent, forward/backward seek intervals. Restart and verify persistence. Changing an interval from the existing WPF left menu should also update the browser.
3. Click article text (not a button) then use Space and Left/Right while audio is active; verify one play/seek action per keypress, no duplicate commands. Arrow keys inside the settings selectors or the splitter should retain native/select/divider behavior.
4. Generate warning/info/error notifications. Non-errors should disappear after ~5 seconds, hovering should pause expiry, errors should persist until explicitly dismissed. Generate at least five errors to check hidden-error overflow and per-item close.
5. During the above, verify Script gradient, vocabulary highlights, selection/add and the audio clock still work. Restart without `--webview-pilot` and verify the original WPF Toast and settings still work.

**Outstanding:** The new C# test, CSS, splitter and Toast are not yet Windows-accepted. Existing old WPF Script pixel regression remains recorded and is not suppressed. Do not merge Draft PR #16 before the remaining UI/Full tests, cleanup and user approval.

## Stage 6 Windows feedback and corrected Toast design (2026-11-18)

The user rebuilt the stage-six pilot and ran `Test.ps1 -Suite Core` successfully (**10/10 groups, content 32/32 tests**). In the Windows GUI, font/accent/seek-step controls, Space/Left/Right shortcuts and Script/highlight/audio/vocabulary regressions behaved correctly. Persistence of divider/reading settings across restarts was **not yet verified**, because the executable location was unclear.

The executable entry points are:

- Development build: `ReadArticles/bin/Release/net10.0-windows/ReadArticles.exe --webview-pilot` (or run `dotnet run --project ReadArticles -c Release -- --webview-pilot` again after closing the window).
- Portable publish: run `.\tools\Publish.ps1`, then `dist/ReadArticles/ReadArticles.exe --webview-pilot`.
- Because settings live in `userdata/config/settings.json` beside the corresponding executable, compare two launches of the **same** build/publish location. Do not compare the development and published configurations.

**Toast feedback:** The user observed that ordinary notifications persisted while errors faded, contrary to expectations, and explicitly requested that **both ordinary and error cards** slide in/out at the bottom, appear newest-first as a vertically stacked group, automatically fade after some time with a **1-second exit animation**, have **no borders**, and be wider.

### Correction committed — awaiting Windows visual retest

- All severities now share the same visual lifetime: ~4650 ms before the exit starts, plus exactly 1000 ms downward fade; hover pauses the countdown. Manual close also fades over 1000 ms.
- The browser's bottom-anchored stack retains newest-first display, entry from above, exit downward, and reuses DOM/timer state across `setNotifications` updates.
- Toast width rises from 360px to 480px (responsive maximum: viewport minus 36px). Info/warning/error cards are borderless; their different backgrounds remain.
- **Data-safety distinction:** The ordinary toast is dismissed from `NotificationService` *after* visual fade, whereas the error toast **fades visually but keeps its unacknowledged C# record**. The faded error becomes accessible under `还有 N 条错误` until explicitly acknowledged. The host sends an `errors` snapshot for this purpose. This preserves the pre-existing rule that no error is silently discarded.
- A new C# test verifies all unacknowledged errors remain in the browser snapshot after the visible four-card limit; a JavaScript timer simulation passed auto-expiry for both types, hover/pause/resume, no reappearance on snapshots, and post-fade error archive access.
- This correction is **not yet Windows-accepted**. No full WebView2 UI/Full suite completion or merge is claimed.

**Retest:** `git pull --ff-only origin feature/webview2-reader`, `dotnet build ReadArticles.Tests -c Release`, `.\tools\Test.ps1 -Suite Core`, run `--webview-pilot`. Trigger informational, warning and error notifications; all cards should visibly fade after ~4.65 s with 1 s transition, retain hover pause, and stack newest-first within a 480px-wide borderless panel. Verify faded error still appears via the overflow control, and closing it there removes the record. Restart **the same** build twice to test font and pane-width persistence.

## Stage 6 follow-up: unified centered categorized Toast + exact divider persistence (2026-11-18)

The user ran `dotnet build ReadArticles.Tests -c Release` and `tools/Test.ps1 -Suite Core` successfully (**10/10 groups, including all 33 content checks**), but reported two specific GUI failures:

1. `Script 切换失败或不可用，已保留原阅读模式` still appeared outside the Toast group, and notification cards lacked the requested classification, consistent width, article-centered position and long-text expansion.
2. Dragging the HTML divider did not restore the same vocabulary width after closing and reopening `dotnet run --project ReadArticles -c Release -- --webview-pilot`.

### Fix submitted, Windows retest required

- The standalone top-left `selection-status` element was removed. Operation results that need visual confirmation use the **same** bottom Toast host as C# notifications; duplicate/failed writes continue to use existing authoritative C# warnings/errors without duplication.
- Script load/validation/render failures now publish explicit **Error** notifications through `NotificationService`, so `Script 切换失败或不可用，已保留原阅读模式` uses the same categorized bottom stack as other messages.
- All notification cards live **inside the central article pane** and are horizontally centered there, not aligned to the entire window. All cards have the same responsive 520px width (or narrower when the middle pane cannot fit it), no border, and the same timing/animation.
- Categories are `普通：` (blue/light weight), `警告：` (amber/medium), and `错误：` (red/medium). The prefix uses a stronger font. Long text is clamped to **two lines** with an `...>>展开` control and `<<收起` to collapse. The archive uses the same two-line behavior.
- New messages sort by their actual arrival across local operation feedback and server notifications, newest at the top. Both Error and other types fade visually after ~4650ms with a **1000ms downward fade**, and hovering pauses the timer. Expired errors are still available under the overflow-error archive until acknowledged by the user.
- The divider now commits its **computed target width** on pointer-up, not a potentially stale/clamped DOM rectangle. The stylesheet no longer forces a 45%-of-content hard maximum on the saved width; it honors the saved px width whenever the viewport can accommodate it, shrinking temporarily when necessary. C# still validates and persists `RightWidth` via the existing settings file.
- A C# Core regression now verifies the new `RightWidth` is included in the `WebPilotSettingsSnapshot`. A standalone JavaScript simulation confirmed categorized prefixes, two-line expansion, true newest-first ordering, error archive, one-second fade, snapshot-driven width restore and exact divider-target dispatch. **These are not proof of actual persistence on the user's Windows filesystem.**

**Required Windows acceptance:**

```powershell
cd C:\Projects\ReadArticles
git pull --ff-only origin feature/webview2-reader
dotnet build ReadArticles.Tests -c Release
.\tools\Test.ps1 -Suite Core
dotnet run --project ReadArticles -c Release -- --webview-pilot
```

Verify a missing Script companion now raises a centered red `错误：` notification, which disappears visually after the shared interval and remains under `还有 N 条错误`. Trigger a normal/duplicate/failed edit status and inspect prefix/stack. Produce long text; confirm exactly two lines until `...>>展开` is clicked. Confirm all cards have equal, borderless widths relative to the *central* article pane.

For persistence, **close the process normally**, then inspect:

```powershell
$settings = 'C:\Projects\ReadArticles\ReadArticles\bin\Release\net10.0-windows\userdata\config\settings.json'
(Get-Content -LiteralPath $settings -Raw | ConvertFrom-Json) | Select-Object LeftWidth, RightWidth, FontSize, Accent, ForwardStep, BackwardStep
```

Drag the HTML divider to an obviously different width; close the process, inspect `RightWidth`, and relaunch the **same** `dotnet run ... --webview-pilot` command without publishing first. Width and settings should restore. The temporary WPF branch remains available and PR #16 remains Draft. Do **not** infer full UI/Full completion from Core success.

## Local Windows acceptance — required

From a clean checkout of this branch:

```powershell
dotnet restore ReadArticles
dotnet build ReadArticles -c Release
dotnet run --project ReadArticles.Tests -c Release -- --webview-pilot-only
.\tools\Test.ps1 -Suite Core
.\tools\Test.ps1 -Suite UI
.\tools\Test.ps1 -Suite Full
.\tools\Publish.ps1
.\tools\Test-PublishedApp.ps1
```

Then launch the pilot explicitly:

```powershell
dotnet run --project ReadArticles -c Release -- --webview-pilot
```

Select a **working-copy** library. Confirm the WebView2 title, complete Passage text and vocabulary list appear, article navigation updates the page, and the default launch (without flag) still uses WPF. Confirm independent scrolling, DPI behavior and no unexpected file writes. The pilot buttons are intentionally not functional; use default WPF launch for editing and audio.

Next, run the **published** EXE with `--webview-pilot` from a copied directory, renamed EXE and another working directory. Verify HTML/CSS/JS are all available without loose web files. Verify missing Evergreen Runtime and read-only folder failures show WPF fallback. Check that `dist/ReadArticles/userdata/` is preserved.

The SDK addition may affect native Loader publishing. **Do not waive or silently relax the exactly-one-EXE protection in `tools/Publish.ps1`**. If publishing fails, record the exact output and resolve packaging separately.

## Pending next work

1. Check hosted Windows Core CI and the local build/publish results. Fix only verified compatibility failures.
2. Complete page lifecycle/recovery, request IDs and explicit action results with source/session/revision validation (stage 2).
3. Port Passage DOM, vocabulary matching and selection preserving old semantics (stage 3).
4. Connect the existing C# vocabulary save path with authoritative success/failure replies (stage 4).
5. Port Script timings and article/pronunciation controls (stage 5).
6. Migrate all active toolbars, CSS splitter, settings, Toast and keyboard handling (stage 6).
7. Replace old WPF structural UI tests with WebView2 rendering tests; manually test audio/visual behavior; delete old visual implementation only after complete acceptance (stage 7).

No CI, interactive UI, audio or publication result is claimed by this document. This branch must not be merged to `main` without explicit user approval.
