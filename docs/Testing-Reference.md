# Testing ReadArticles

## Runner and data ownership

Run from the repository root on Windows with the .NET 10 SDK:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tools\Test.ps1 -Suite Core
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tools\Test.ps1 -Suite UI
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tools\Test.ps1 -Suite Full
```

The STA console runner tests production services and real WPF controls. A fake audio adapter makes state transitions deterministic; generated PCM WAV files exercise actual decoding and playback lifecycle. All fixed article and vocabulary JSON inputs live under `tests/TestData/Library/`. The runner creates a unique temporary workspace and a single shared audio pool for each run. It never modifies checked-in fixtures.

Run categories sequentially: native WPF input and playback checks require an interactive Windows desktop session.

## Functional categories

| Category | Responsibility |
| --- | --- |
| `--content-only` | Passage JSON contracts, library discovery, atomic vocabulary writes, metadata, external edits, and harmless deletion requests |
| `--rendering-only` | Literal matching, SID ownership, valid and invalid selections, blank passages, and styles |
| `--window-only` | Loaded-control hit-testing, selection action, three-pane rendering, independent scrolling, and status lifecycle |
| `--tree-only` | One-level expansion and recursive collapse |
| `--audio-only` | Single-file playback, two-player coordination, stale callbacks, invalid resources, and real media decoding |
| `--controls-only` | Settings, keyboard shortcuts, progress/time, and sibling article-audio controls |
| `--icons-only` | Embedded icons, circle-plus movement, footer alignment, startup size, and highlighting |
| `--editing-only` | Vocabulary move/delete UI and corresponding persisted order |
| `--source-only` | Read-only integration against the fixed test article and vocabulary JSON |

Example:

```powershell
dotnet run --project ReadArticles.Tests -c Release -- --audio-only
```

## Audio fixture behavior

The runner generates UK and US WAV files for every sentence SID except `s011`, and for each distinct vocabulary filename stem. The missing `s011_uk` and `s011_us` files are intentional. Opening or switching to the article must not probe sentence audio files or report missing recordings. The sentence remains selectable for playback; only an explicit playback request checks that sentence's selected accent and reports that single missing file.

Generated WAVs are short synthetic tones, not speech and not pronunciation content. They exercise filename resolution, media decoding, playback position, pause/resume, and completion. MP3-priority and corrupt-MP3 behavior are tested with focused temporary files.

## Manual acceptance

Automated checks cover state, controls, layout, and event routing. Native dialogs and physical input should also be checked:

1. Open the test library. Confirm startup is collapsed with empty passage/vocabulary panes.
2. Select the main article. Confirm no missing sentence-audio warning appears.
3. Play a normal sentence and confirm playback works.
4. Select sentence `s011`. Confirm the play action remains available; click it and confirm only the selected accent's missing-file message appears.
5. Switch to the other accent and repeat. Confirm the message identifies only the selected accent.
6. Play article, word, and sentence audio in turn. Confirm player ownership and pause/resume behavior.

## Generated files

Reports, transcript logs, and screenshots are written under `artifacts/`. Temporary audio and copied article data are stored under the OS temporary directory and removed after the run. Do not commit generated reports, screenshots, logs, or temporary media.
