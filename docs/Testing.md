# Testing and acceptance

Run from the repository root on Windows with the .NET 10 SDK. The unified PowerShell entry point supports Windows PowerShell 5.1 and PowerShell 7. If the machine's execution policy blocks unsigned local scripts, invoke it with a process-scoped bypass:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tools\Test.ps1 -Suite Core
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tools\Test.ps1 -Suite UI
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tools\Test.ps1 -Suite Full
```

`tests/TestData/` is the only checked-in source for fixed test inputs. The runner validates the main article and its audio pool, copies the required files into a unique temporary workspace, and sets `READARTICLES_TEST_LIBRARY` and `READARTICLES_TEST_ARTICLE` for the child test processes. The source test data is never modified. All temporary test workspaces are created under the operating system temporary directory and removed after the run.

## Suites

- **Core** runs publishing safeguards, isolated logger checks and one `--core-suite` process for data/audio/Script contracts, vocabulary `family` validation/persistence, WebView2 snapshots and real DOM matching in Passage/Script, tree navigation, vocabulary commits and notifications. Each test method runs once.
- **UI** uses one `--ui-suite` process for the same Core cases plus native window/tree, settings, controls, icons and backups, with no duplicate test methods. The isolated logger check still runs once. Run UI on an interactive Windows desktop.
- **Full** runs the complete application regression suite (including logger checks), real M4A/MP3/WAV playback checks, and separate read-only checks against the unified test article. It does not re-run logger checks in a separate process. Run this locally before merging; it does not require a developer-specific article directory.

Targeted `--*-only` entry points remain available for focused development tests. The standard suites avoid duplicate invocations.

The single Windows CI workflow runs on pushes and pull requests to `main` (and manual dispatch). Its Core suite already includes real WebView2 DOM tests. Browser/Windows focus and media-device behavior can differ between hosted and interactive sessions. Local UI/Full and manual browser/audio checks remain required.

The summary distinguishes Passed, Failed, and Skipped groups. A skipped group is not counted as verified. A failed native command or assertion makes the entry point return a nonzero exit code.

## Test data layout

```text
tests/TestData/
├── Library/                 # Fixed article and vocabulary JSON
├── Audio/
│   └── audio/               # Shared vocabulary UK/US recordings
└── README.md                # Inventory and fixture rules
```

The main Passage contains 13 paragraphs, 62 sentence strings, a JSON title, and 120 vocabulary entries. Only whole-article and vocabulary audio playback are supported; no sentence-recording fixtures are needed. Test-specific WAV/M4A candidates, corrupt media, filename ordering and asynchronous fallback are created under isolated temporary directories. Do not maintain duplicate permanent media fixtures.

## Outputs

Test screenshots, Markdown reports, and transcripts are written under `artifacts/`:

- `artifacts/screenshots/` for screenshots;
- `artifacts/reports/` for per-run summaries;
- `artifacts/logs/` for full console transcripts.

Scratch directories and copied audio are not test outputs; they belong in the OS temporary directory and are removed after execution. The published application does not use `artifacts/`.

## Runtime logging checks

Core and UI run the isolated logger checks through `--logging-only`; Full runs the same `AppLoggerChecks` within its complete application regression suite, without a duplicate invocation. These checks verify that exception messages and learning content are omitted, daily files rotate within their size bound, expired files and excess total storage are pruned, and log-directory failures never escape into application code. The tests use temporary directories and do not write into the real `userdata/logs/` folder.

After launching the actual portable application, inspect `dist/ReadArticles/userdata/logs/` to confirm that a daily log is created. The application does not log article or vocabulary content.

## Published EXE acceptance

After publishing, run:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tools\Test-PublishedApp.ps1
```

The published-app check copies only the EXE into a temporary path with spaces, renames it, and launches it from another working directory. It checks the main window, configuration placement, clean closure, and missing-.NET installation instructions. It does not modify installed runtimes or user configuration.

### Final portable EXE: interactive acceptance (manual, isolated data)

After `Publish.ps1` and `Test-PublishedApp.ps1` succeed, start **the exact** `dist/ReadArticles/ReadArticles.exe` in a writable portable directory on an interactive Windows desktop, using a **copy** of the test library. Verify the following before releasing:

1. WebView2 reaches the actual Passage DOM, rather than merely creating a WPF window handle; ensure there is no native failure screen. Check independent article/word scrolling and vocabulary selection/highlighting. With an isolated article containing `go` plus `family: ["went", "goes"]`, confirm both forms highlight and navigate as `go` without appearing as separate right-pane entries.
2. Switch Passage -> Script -> Passage and confirm correct title, timed-unit background/seek behavior and restored selection/scroll where applicable.
3. Exercise keyboard focus: Space/Left/Right on native TreeView, buttons, splitters and all WebView2 controls must exclusively control whole-article playback, with no default activation, resizing or tree navigation. Enter still activates word rows. Verify that all three reserved keys produce no focus outline on vocabulary rows or buttons and no keyboard focus color on the right splitter; retain the selected-word marker. Verify no duplicate dispatch, Space repeat suppression, key release suppression, unchanged focus, unavailable playback consumption and normal keyboard behavior in separate dialogs and with modifiers. Click the article audio progress slider and confirm it retains keyboard focus without an outer focus frame; Left/Right must seek by the configured backward/forward seconds rather than native slider increments. Test **rapid repeated** arrow keys (each press must accumulate), including while paused; Stop must disable seeking. Click the slider thumb without moving it, drag/release, cancel a drag, and switch focus; the progress display must resume after each interaction.
4. Verify word and whole-article audio on a real output device. Test pause/resume, mouse click/drag seek, keyboard seek, stop and missing recording feedback. Verify 60:00 at exactly one hour and hh:mm:ss for longer recordings if such a test recording is available.
5. On a **disposable library copy**, edit a word, move/delete it, import a vocabulary JSON and verify only the companion vocabulary and owned backups change. Confirm settings and notifications, then close and reopen the published executable.

This list is an interactive **release gate**, not proof of automated coverage. Do not use a real article library for destructive tests, and do not claim this checklist passed unless someone actually ran it.

## Workflow and publishing regression checks

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tools\Test-Workflow.ps1
```

`Test-Workflow.ps1` verifies command failure propagation and that Full uses the unified test data without external paths. `Test-Publish.ps1` separately exercises staged replacement against temporary files, including refusal of linked dist and release directories.

## Script progress color palette

Verify the five light playback backgrounds and default, dark playback swatch checkmarks, persistence and all ten historical color mappings (including lowercase hex), unknown color fallback and unchanged file bytes on normal legacy load. Other settings remain unchanged. Manually inspect all five playback backgrounds on the existing page backgrounds.

## Playback update efficiency

Live absolute divider drag frames do not accumulate in the request deduplication set; durable actions and drag completion retain request checks. Script geometry caches segment widths and skips unchanged SVG rectangle width writes, including during reverse seeks and stop. The progress timer runs only while article playback is playing; native and browser seek handlers explicitly publish the new position, even when paused. Manually verify paused forward/backward seeks, pointer seek, resume and Script progress.

## Functional icons

Automated embedded SVG checks validate the Web icon viewBox, background circle and white foreground. Native and embedded browser functional icons use maple-red `#D65B45` circles and white glyphs; preserve distinct play/pause, Passage/Script and eye/crossed-eye states. Verify actual rendering at toolbar, pronunciation, selection-add and tree sizes, hover/pressed/disabled states, and unchanged app icon and keyboard behavior.

## Explicit library refresh

Core/UI/Full exercise reopening the current root, collapsed fresh nodes, resetting the reading workspace, missing-root and same-name-file deselection, a concurrent exclusion edit, stale refresh rejection during a later library switch, and the actual refresh button. UI/Full also check immediate persistence of a null library path after missing-root refresh. Manually check button placement/disabled state and real playback interruption; use a disposable library.

## Folder exclusion regression

Core includes the pure exclusion parser checks. UI and Full also verify independent invalid-name and invalid-JSON-type field recovery, invalid-source preservation during explicit backup restoration, rejection of obsolete/invalid backups, and the loaded Fluent template's collapsed clear button. The navigation regression schedules another filter edit during failure fallback before the root result commits. Cover default and empty exclusions, exact case-insensitive matching across root and nested folders, four delimiters, whitespace and duplicates, invalid names, settings round-trip and backup, reset, equivalent-set no-op, scan invalidation and failed refresh retaining the reading workspace. On Windows, verify the new text box, cancel/save, immediate collapsed-tree refresh and continued real article/word playback.

## Numeric settings regression

Core verifies structured configuration defaults (including missing fields), retained valid values, invalid ranges/precision, all numeric settings round-trip, unchanged-file/backup behavior, custom audio seek through MainViewModel, and browser layout/selection-add geometry. UI and Full additionally verify native numeric editor layout, ASCII and decimal restrictions, selection replacement, rejected paste, invalid save and reset, and editing history. On an interactive desktop verify Backspace/Delete/Ctrl+X on the decimal point, Ctrl+Z/Ctrl+Y, clipboard behavior, and custom seek while paused/playing. Check actual variable fonts and large glyph click targets. No duplicate Core/UI/Full test invocation is needed.

## Ownership and maintenance

Feature tests cover PaneLayout geometry/drag rollback, ReaderWorkspace committed modes, VocabularyEditor persistence before workspace commit, and WebReaderBridge request identity/revision guards. Browser tests exercise actual embedded ES modules via dynamic import and DOM events without exposing mutable application globals. Retain real DOM, native focus and media assertions when reorganizing source files. Hosted Core does not run all local UI/Full cases.

Never edit `tests/TestData/` during a test run. When intentionally changing a fixture, update `tests/TestData/README.md` and the corresponding regression assertions in the same change. Do not commit generated screenshots, reports, logs, personal settings, or temporary media. Run tests sequentially because native WPF input and playback checks require exclusive desktop interaction.

## Targeted regression checks

Run the following from the repository root on Windows:

```powershell
dotnet build ReadArticles.Tests -c Release
# Targeted commands assume an isolated fixture environment if the case needs TestData.
dotnet run --project ReadArticles.Tests -c Release -- --webview-pilot-only
dotnet run --project ReadArticles.Tests -c Release -- --script-only
```

- `--webview-pilot-only` (retained command name) covers the embedded HTML/CSS/ES module/icon assets, terminal native error screen, lack of legacy WPF reader, real Passage/word DOM and Script switching, exclusive reserved-key dispatch and focus-outline suppression, vocabulary matching boundaries and `family` mapping across Passage/Script timed units, independent scroll, font-setting roundtrip, notification dismissal and SVG cumulative background progress.
- `--script-only` covers Script JSON validation, timed-unit offsets, null-time handling and bridge snapshot/selection contracts. Script unaligned units are allowed but not colored; single-null and zero-duration intervals must be rejected.
- `--tree-only`, `--lazy-tree-only`, `--controls-only`, `--editing-only`, `--notifications-only` and `--backup-only` keep the native tree, business states and data-safety checks. `--rendering-only` checks the browser and Script bridge, **not** the deleted WPF `ParagraphReader`.
- `--source-only` verifies that the active test article and vocabulary load without writes. It is **not** an independent browser-rendering test; real DOM assertions are in `--webview-pilot-only` / `--rendering-only`.
- The browser matches the headword `word` and explicit `family` strings using case-insensitive literal matches, longest match at the earliest start, and the established apostrophe/hyphen/numeric-boundary rules. Hits map back to their canonical headword; a direct headword match takes precedence over another entry's family alias. The repository checks optional family-array structure, uniqueness, and save round-trips. Do not reintroduce the deleted C# `VocabularyMatcher` merely to test this behavior.
- Notification queue tests cover severity, FIFO order, three visible cards, waiting messages and persistent errors. Browser DOM tests check rendering/dismissal; deleted WPF ToastHost animation/bitmap tests are no longer applicable.
- Script playback uses one unchanged text layer and SVG background rectangles driven by the C# audio clock. DOM/geometry checks cover exact text, no duplicate layer or ClipPath, inherited crispEdges, same-line whitespace including cross-sentence gaps, untimed text/punctuation, actual JSON null and unaligned units, paragraph boundaries, measured rectangle coordinates and height, proportional progress through a wrapped timed unit, last-fragment-only gap, automatic reflow restoration from a paused snapshot without a new playback update, rewind, reset and unchanged-width writes. Temporary DOM fixtures restore the prior playback, scroll and vocabulary Range matches. **Real-audio listening and visual inspection** remain separate acceptance evidence: confirm no obvious fractional-pixel seams, smooth moving edge, glyph-height backgrounds (+1 CSS px above/below), unchanged indent/line/paragraph spacing, all five colors, narrow/wide reflow and divider/font changes. Verify paused/playing seeks, stop/natural completion, Passage/Script mode switches, word highlight toggling/selection/family/positioning and selection-add. Playback background takes precedence while overlapping vocabulary highlights, which reappear on rewind/reset; check the existing capsule edge padding visually. No pagination, translation or export features are added.
- The published-app smoke validates launch, renamed executable, settings location and missing-.NET guidance; it does **not** independently prove every published EXE WebView2 interaction. Exercise the actual published build on an interactive desktop before release.

Do not write to a real library or its backup files in any destructive or fault-injection test. Run Core/UI/Full sequentially. Report the actual counts from each run rather than copying historical assertion totals into a new result.
