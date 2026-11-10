# Testing and acceptance

Run from the repository root on Windows with the .NET 10 SDK. The unified PowerShell entry point supports Windows PowerShell 5.1 and PowerShell 7. If the machine's execution policy blocks unsigned local scripts, invoke it with a process-scoped bypass:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tools\Test.ps1 -Suite Core
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tools\Test.ps1 -Suite UI
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tools\Test.ps1 -Suite Full
```

`tests/TestData/` is the only checked-in source for fixed test inputs. The runner validates the main article and its audio pool, copies the required files into a unique temporary workspace, and sets `READARTICLES_TEST_LIBRARY` and `READARTICLES_TEST_ARTICLE` for the child test processes. The source test data is never modified. All temporary test workspaces are created under the operating system temporary directory and removed after the run.

## Suites

- **Core** runs publishing safeguards and the core regression categories: content and unified-audio contracts, matching/rendering, tree navigation, and vocabulary editing.
- **UI** runs Core plus icons, reader windows, and keyboard/control interaction checks. Run this suite locally on the Windows desktop where the application is used; it is the focused diagnostic suite for WPF interaction issues.
- **Full** runs the complete application regression suite, real MP3/WAV playback checks, and read-only checks against the unified test article. Run this locally before merging; it does not require a developer-specific article directory.

The hosted GitHub Actions runner runs **Core** only. WPF focus, popup, media-device, and input timing can differ between hosted virtual desktops and a user's interactive Windows session. A hosted UI timeout is therefore recorded as an environment-specific UI failure, not silently treated as a passing UI test. The local **UI** and **Full** results remain required for acceptance of changes affecting the reader interface or playback.

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

## Workflow and publishing regression checks

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tools\Test-Workflow.ps1
```

`Test-Workflow.ps1` verifies command failure propagation and that Full uses the unified test data without external paths. `Test-Publish.ps1` separately exercises staged replacement against temporary files.

## Ownership and maintenance

Never edit `tests/TestData/` during a test run. When intentionally changing a fixture, update `tests/TestData/README.md` and the corresponding regression assertions in the same change. Do not commit generated screenshots, reports, logs, personal settings, or temporary media. Run tests sequentially because native WPF input and playback checks require exclusive desktop interaction.

## Phase 5 targeted checks

Run `dotnet run --project ReadArticles.Tests -c Release -- --notifications-only` for queue ordering, severity, deduplication and error overflow. Run `dotnet run --project ReadArticles.Tests -c Release -- --lazy-tree-only` for root-only and one-level scan behavior. The existing `--tree-only`, `--window-only`, `--backup-only` and `--content-only` suites remain relevant. Native WPF animation timing, hover, keyboard close and published EXE behavior still require an interactive Windows desktop in addition to hosted Core CI. Never test recovery fault cases against a real library.

## Phase 5 reliability and interactive regression additions

- `--window-only`: includes a real WPF ToastHost timing check. A transient message is hovered near its expiry threshold, must remain visible while hovered, then expires after the remaining active time. An Error must remain after the transient timeout and disappear only after its close action.
- `--tree-only`: includes rapid collapse during a pending directory scan, successful retry after cancellation, library-session replacement while an old scan is pending, and failed article selection restoring the previously committed leaf.
- `--backup-only`: includes a Windows file-sharing fault injection. The current source is held without delete sharing so the restore replacement fails after backup rotation; the test verifies the original source and previous valid backup are both restored byte-for-byte and temporary files are cleaned.
- The fault-injection test uses an isolated temporary directory and never touches the user's library. The WPF timing test uses the test window and closes it in a finally block.

## Toast layout and motion regression

- `--notifications-only` verifies that message history remains chronological while the visible list is newest-first (newest at top, oldest at bottom), including the four-item visible limit.
- `--window-only` verifies that new cards enter from above, dismissed cards exit downward, the default card width is 360 DIP, visible card gaps are 2 DIP, and the oldest visible card is anchored to the bottom of the host.
- The host retains a 16 DIP bottom margin from the reading pane. Info, Warning and Error cards use distinct icon glyphs, foreground colors, backgrounds and font weights.
- Info and Warning expire after their active display interval; hovering pauses the interval. Error notifications do not expire automatically. Exiting cards remain in the layout until their exit animation completes.
- Animation sampling is performed on a real WPF window in the Windows UI suite. Hosted Core CI does not replace interactive desktop verification of perceived motion and rendering.
