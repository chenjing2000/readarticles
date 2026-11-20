# Testing and acceptance

Run from the repository root on Windows with the .NET 10 SDK. The unified PowerShell entry point supports Windows PowerShell 5.1 and PowerShell 7. If the machine's execution policy blocks unsigned local scripts, invoke it with a process-scoped bypass:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tools\Test.ps1 -Suite Core
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tools\Test.ps1 -Suite UI
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tools\Test.ps1 -Suite Full
```

`tests/TestData/` is the only checked-in source for fixed test inputs. The runner validates the main article and its audio pool, copies the required files into a unique temporary workspace, and sets `READARTICLES_TEST_LIBRARY` and `READARTICLES_TEST_ARTICLE` for the child test processes. The source test data is never modified. All temporary test workspaces are created under the operating system temporary directory and removed after the run.

## Suites

- **Core** runs publishing safeguards and data/audio/Script contracts, WebView2 snapshot checks, real browser DOM rendering, tree navigation, vocabulary commit and notification tests.
- **UI** runs Core plus native window/tree checks, WebView2 browser rendering, settings and control checks. Run it on an interactive Windows desktop.
- **Full** runs the complete application regression suite, real MP3/WAV playback checks, and read-only checks against the unified test article. Run this locally before merging; it does not require a developer-specific article directory.

The single Windows CI workflow runs on pushes and pull requests to `main` (and manual dispatch). Its Core suite already includes real WebView2 DOM tests; the retired feature-branch pilot workflow has been removed. Browser/Windows focus and media-device behavior can differ between hosted and interactive sessions. Local UI/Full and manual browser/audio checks remain required.

The summary distinguishes Passed, Failed, and Skipped groups. A skipped group is not counted as verified. A failed native command or assertion makes the entry point return a nonzero exit code.

## Test data layout

```text
tests/TestData/
├── Library/                 # Fixed article and vocabulary JSON
├── Audio/
│   ├── audio/               # Shared vocabulary UK/US recordings
│   └── audio_sentences/     # Shared sentence UK/US recordings
└── README.md                # Inventory and fixture rules
```

The main article contains 13 paragraphs, 62 stable sentence IDs, and 120 vocabulary entries. The shared audio pool contains UK and US MP3 recordings for every vocabulary entry and every sentence except `s011`. The missing `s011_uk` and `s011_us` files are intentional: opening or switching an article and rendering sentence controls must not probe or report missing sentence recordings. The sentence control remains available for a valid SID; attempting to play `s011` checks only the selected accent and reports that sentence's missing recording.

WAV-only, MP3-priority, corrupt-MP3, and other exceptional media cases are generated in isolated temporary directories by the tests. Do not maintain duplicate WAV-only or missing-audio fixture trees.

## Outputs

Test screenshots, Markdown reports, and transcripts are written under `artifacts/`:

- `artifacts/screenshots/` for screenshots;
- `artifacts/reports/` for per-run summaries;
- `artifacts/logs/` for full console transcripts.

Scratch directories and copied audio are not test outputs; they belong in the OS temporary directory and are removed after execution. The published application does not use `artifacts/`.

## Runtime logging checks

Every suite runs the isolated logger checks through `--logging-only`. They verify that exception messages and learning content are omitted, daily files rotate within their size bound, expired files and excess total storage are pruned, and log-directory failures never escape into application code. The tests use temporary directories and do not write into the real `userdata/logs/` folder.

After launching the actual portable application, inspect `dist/ReadArticles/userdata/logs/` to confirm that a daily log is created. The application does not log article or vocabulary content.

## Published EXE acceptance

After publishing, run:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tools\Test-PublishedApp.ps1
```

The published-app check copies only the EXE into a temporary path with spaces, renames it, and launches it from another working directory. It checks the main window, configuration placement, clean closure, and missing-.NET installation instructions. It does not modify installed runtimes or user configuration.

### Final portable EXE: interactive acceptance (manual, isolated data)

After `Publish.ps1` and `Test-PublishedApp.ps1` succeed, start **the exact** `dist/ReadArticles/ReadArticles.exe` in a writable portable directory on an interactive Windows desktop, using a **copy** of the test library. Verify the following before releasing:

1. WebView2 reaches the actual Passage DOM, rather than merely creating a WPF window handle; ensure there is no native failure screen. Check independent article/word scrolling and vocabulary selection/highlighting.
2. Switch Passage -> Script -> Passage and confirm correct title, timed-word foreground/seek behavior and restored selection/scroll where applicable.
3. Exercise keyboard focus: TreeView Left/Right and Space on native controls must not trigger audio; focused WebView2 word rows activate with Enter **and** Space without triggering whole-article playback. Click the article audio progress slider and confirm it retains keyboard focus without an outer focus frame; Left/Right must seek by the configured backward/forward seconds rather than native slider increments. Test **rapid repeated** arrow keys (each press must accumulate), including while paused; Stop must disable seeking. Click the slider thumb without moving it, drag/release, cancel a drag, and switch focus; the progress display must resume after each interaction.
4. Verify word/segment and whole-article audio on a real output device. Test pause/resume, mouse click/drag seek, keyboard seek, stop and missing recording feedback. Verify 60:00 at exactly one hour and hh:mm:ss for longer recordings if such a test recording is available.
5. On a **disposable library copy**, edit a word, move/delete it, import a vocabulary JSON and verify only the companion vocabulary and owned backups change. Confirm settings and notifications, then close and reopen the published executable.

This list is an interactive **release gate**, not proof of automated coverage. Do not use a real article library for destructive tests, and do not claim this checklist passed unless someone actually ran it.

## Workflow and publishing regression checks

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tools\Test-Workflow.ps1
```

`Test-Workflow.ps1` verifies command failure propagation and that Full uses the unified test data without external paths. `Test-Publish.ps1` separately exercises staged replacement against temporary files.

## Ownership and maintenance

Never edit `tests/TestData/` during a test run. When intentionally changing a fixture, update `tests/TestData/README.md` and the corresponding regression assertions in the same change. Do not commit generated screenshots, reports, logs, personal settings, or temporary media. Run tests sequentially because native WPF input and playback checks require exclusive desktop interaction.

## Current targeted regression checks (single WebView2 reader)

Run the following from the repository root on Windows:

```powershell
dotnet build ReadArticles.Tests -c Release
dotnet run --project ReadArticles.Tests -c Release -- --webview-pilot-only
dotnet run --project ReadArticles.Tests -c Release -- --script-only
.\tools\Test.ps1 -Suite Core
.\tools\Test.ps1 -Suite UI
.\tools\Test.ps1 -Suite Full
.\tools\Test-PublishedApp.ps1
```

- `--webview-pilot-only` covers the embedded static assets, terminal native error screen, lack of legacy WPF reader, real Passage/word DOM and Script switching, vocabulary matching boundaries, independent scroll, font-setting roundtrip, notification dismissal and SVG clip/foreground progress.
- `--script-only` covers Script JSON validation, timed-unit offsets, null-time handling and bridge snapshot/selection contracts. Script unaligned units are allowed but not colored; single-null and zero-duration intervals must be rejected.
- `--tree-only`, `--lazy-tree-only`, `--controls-only`, `--editing-only`, `--notifications-only` and `--backup-only` keep the native tree, business states and data-safety checks. `--rendering-only` checks the browser and Script bridge, **not** the deleted WPF `ParagraphReader`.
- `--source-only` verifies that the active test article and vocabulary load without writes. It is **not** an independent browser-rendering test; real DOM assertions are in `--webview-pilot-only` / `--rendering-only`.
- The browser matches vocabulary using case-insensitive literal matches, longest match at the earliest start, and the established apostrophe/hyphen/numeric-boundary rules. Do not reintroduce the deleted C# `VocabularyMatcher` merely to test this behavior.
- Notification queue tests cover severity, FIFO order, three visible cards, overflow and persistent errors. Browser DOM tests check rendering/dismissal; deleted WPF ToastHost animation/bitmap tests are no longer applicable.
- Script foreground coloring uses SVG clip geometry driven by the C# audio clock. DOM/geometry checks verify structure and updates; **real-audio listening and visual inspection of wrapped timed units** remain separate acceptance evidence. Confirm first-line-to-last-line coloring for hyphenated/multiword units, seek, pause, stop, highlight backgrounds and mode switches.
- The published-app smoke validates launch, renamed executable, settings location and missing-.NET guidance; it does **not** independently prove every published EXE WebView2 interaction. Exercise the actual published build on an interactive desktop before release.

Do not write to a real library or its backup files in any destructive or fault-injection test. Run Core/UI/Full sequentially. Report the actual counts from each run rather than copying historical assertion totals into a new result.
