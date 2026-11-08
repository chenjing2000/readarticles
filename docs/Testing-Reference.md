# Testing ReadArticles

## Runner and data ownership

Run the unified test entry point from the repository root on Windows:

```powershell
.\tools\Test.ps1 -Suite Core
.\tools\Test.ps1 -Suite UI
.\tools\Test.ps1 -Suite Full
```

All fixed input data lives under `tests/TestData/`. `Test.ps1` validates that source, creates an isolated working copy under the operating system temporary directory, and supplies the library/article paths to child test processes through environment variables. Test code must not modify checked-in source data. Scratch directories are created under the OS temporary directory and cleaned after use. Screenshots, reports, and transcripts are the only test outputs retained under `artifacts/`.

The STA console runner tests production services and real WPF controls. Fake audio adapters make state transitions deterministic; the shared MP3 fixtures exercise actual decoding. WAV-only and corrupt-media scenarios are generated in temporary directories. Exit code 0 means all selected checks passed, 1 means a test failed, and 2 means an unknown category. Run categories sequentially because WPF input and media checks share the desktop session.

The main test article has 13 paragraphs, 62 stable sentence IDs, and 120 vocabulary entries. UK/US audio is supplied for all vocabulary entries and all sentence IDs except `s011`. The missing `s011` recordings are intentional and verify that missing sentence audio is reported only when that sentence is played. A second article supports navigation and switching tests.

The main branch has no exercise module or exercise test category. No test restores exercise behavior from another branch. Case-insensitive exclusion of UserData and cache directories is part of navigation regression coverage.

## Functional categories

| Category | Responsibility |
| --- | --- |
| `--content-only` | Passage JSON contracts, library discovery, optional account label, atomic vocabulary writes, metadata, external edits, and harmless deletion requests without an active entry |
| `--rendering-only` | Literal matching, SID ownership, valid and invalid selections, blank passages, and styles |
| `--window-only` | Loaded-control hit-testing, selection action, three-pane rendering, independent scrolling, and status lifecycle |
| `--tree-only` | One-level expansion and recursive collapse |
| `--audio-only` | Single-file playback, two-player coordination, stale callbacks, invalid resources, and real MP3 decoding |
| `--controls-only` | Library-only startup, configuration initialization and value normalization, immediate settings saves, keyboard shortcuts, progress/time, and sibling article-audio controls |
| `--icons-only` | Embedded icons, circle-plus movement, footer alignment, startup size, and highlighting |
| `--editing-only` | Vocabulary move/delete UI and corresponding persisted order |
| `--source-only` | Read-only integrity and rendering checks against the unified test article |

The category switches are intended for the unified runner, which prepares the environment first. For ordinary use, run `tools/Test.ps1`; do not launch the test executable directly unless `READARTICLES_TEST_LIBRARY` and `READARTICLES_TEST_ARTICLE` have been prepared.

Each category has a clear behavioral purpose. Write cases own their data, and separate cases do not depend on previous answers or edits. A small amount of setup repetition keeps that ownership explicit.

`--source-only` reads the temporary article selected by `tools/Test.ps1` through `READARTICLES_TEST_ARTICLE`. It checks the copied unified fixture and verifies that source files remain unchanged.

## Manual acceptance

Automated checks cover state, controls, layout, and event routing. Native dialogs and physical input should also be checked:

1. Open a copied library. Confirm startup is collapsed with empty passage/vocabulary panes and visible disabled footer controls. Expand and select a passage.
2. Select text, scroll both ways, and move the window. Confirm the circle-plus follows the selection, does not cover text, and disappears when the selection clears.
3. Play the sibling article recording (MP3, or WAV when MP3 is absent), then word or sentence audio. Confirm pronunciation pauses the article and preserves its position. Resume the article and confirm pronunciation stops. Stop and seek must affect only the article file.
4. Change Forward/Backward settings. Check Left, Right, and Space with the physical keyboard from each pane and settings popup; inspect progress and the time label.
5. Move, delete, and import vocabulary in a copied library. Reopen and confirm persisted order and contents. Try externally editing the companion before saving; the app must warn rather than overwrite that change.

Check the unified audio contract in copied data: WAV-only word/sentence/article targets play; adding the corresponding MP3 makes it win even when corrupt (report failure without WAV retry). Confirm old directories and JSON path fields cannot redirect lookup, saving vocabulary removes legacy audio fields, and later stem collisions and empty stems preserve text but disable all audio. Check that an ordinary article without audio fields still offers sentence playback, while valid [[n]] blank articles do not.

Do not manually edit checked-in fixtures. Screenshots support inspection but do not themselves prove a test passed.

## Generated files

Screenshots are written to `artifacts/screenshots/`. Per-run Markdown summaries are written to `artifacts/reports/`, and PowerShell transcripts to `artifacts/logs/`. Temporary copied libraries and audio are created under the OS temporary directory and removed after each run. The checked-in `tests/TestData/` directory is the only fixed test-input source.

Build outputs and screenshots can be removed after review. The published application does not use `artifacts/`. The font checks use 14/15/16 DIP and verify that direct pt declarations stay unchanged. Size checks use rounded window and column calculations, and settings checks verify integer persisted widths. Keep the formal `dist/` release and its settings. See [Getting Started](Getting-Started.md) for publishing and settings-preserving replacement.
