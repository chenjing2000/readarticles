# Testing ReadArticles

## Runner and data ownership

Run from the repository root on Windows with the SDK specified by `global.json`:

```powershell
dotnet run --project ReadArticles.Tests -c Release
```

The STA console runner tests production services and real WPF controls. A fake audio adapter makes state transitions deterministic; local fixture MP3 files exercise actual decoding. Exit code 0 means all selected checks passed, 1 means failure, and 2 means an unknown category.

Default checks use fixed fixtures under `ReadArticles.Tests/Fixtures/`. Write tests create temporary copies and clean them afterward. Test windows normally disable preference persistence; settings-restoration checks back up and restore settings beside the test executable. Tests share immutable fixture definitions and small WPF helpers rather than a separate prototype application. Run categories sequentially: generated test directories are shared by the runner.

The main branch has no exercise module or exercise test category. No test restores exercise behavior from another branch. Case-insensitive exclusion of UserData and cache directories is part of navigation regression coverage.

## Functional categories

| Category | Responsibility |
| --- | --- |
| `--content-only` | Passage JSON contracts, library discovery, optional account label, atomic vocabulary writes, metadata, external edits, and harmless deletion requests without an active entry |
| `--rendering-only` | Literal matching, SID ownership, valid and invalid selections, blank passages, and styles |
| `--window-only` | Loaded-control hit-testing, selection action, three-pane rendering, independent scrolling, and status lifecycle |
| `--tree-only` | One-level expansion and recursive collapse |
| `--audio-only` | Single-file playback, two-player coordination, stale callbacks, invalid resources, and real MP3 decoding |
| `--controls-only` | Library-only startup, configuration initialization and value normalization, immediate settings saves, keyboard shortcuts, progress/time, and sibling MP3 controls |
| `--icons-only` | Embedded icons, circle-plus movement, footer alignment, startup size, and highlighting |
| `--editing-only` | Vocabulary move/delete UI and corresponding persisted order |
| `--source-only` | Optional read-only integration against a local Economist article; excluded from default checks |

Example:

```powershell
dotnet run --project ReadArticles.Tests -c Release -- --audio-only
```

Each category has a clear behavioral purpose. Write cases own their data, and separate cases do not depend on previous answers or edits. A small amount of setup repetition keeps that ownership explicit.

`--source-only` reads the temporary article selected by tools/Test.ps1 -ArticleDirectory through READARTICLES_TEST_ARTICLE. It checks current content rather than a fixed word count. The original article is not modified.

## Manual acceptance

Automated checks cover state, controls, layout, and event routing. Native dialogs and physical input should also be checked:

1. Open a copied library. Confirm startup is collapsed with empty passage/vocabulary panes and visible disabled footer controls. Expand and select a passage.
2. Select text, scroll both ways, and move the window. Confirm the circle-plus follows the selection, does not cover text, and disappears when the selection clears.
3. Play the sibling MP3, then word or sentence audio. Confirm pronunciation pauses the article and preserves its position. Resume the article and confirm pronunciation stops. Stop and seek must affect only the article file.
4. Change Forward/Backward settings. Check Left, Right, and Space with the physical keyboard from each pane and settings popup; inspect progress and the time label.
5. Move, delete, and import vocabulary in a copied library. Reopen and confirm persisted order and contents. Try externally editing the companion before saving; the app must warn rather than overwrite that change.

Do not manually edit checked-in fixtures. Screenshots support inspection but do not themselves prove a test passed.

## Generated files

MP3 files are local resources and are ignored by Git. Keep the local fixture audio when cleaning generated files. A fresh checkout needs the corresponding MP3 files restored under the fixture library before running audio and playback integration checks; these checks intentionally verify real decoding rather than silently skipping missing resources.

Build outputs and screenshots are ignored by Git and can be removed after review. Tests may recreate artifacts; the published application does not use it. The font checks use 14/15/16 DIP and verify that direct pt declarations stay unchanged. Size checks use rounded window and column calculations, and settings checks verify integer persisted widths. Keep the formal `dist/` release and its settings. See [Getting Started](Getting-Started.md) for publishing and settings-preserving replacement.

For a release check, publish with the Portable profile and confirm dist contains only the EXE before first run. Run from another working directory and confirm the .settings.json is created beside the EXE. Copy the EXE under another filename and confirm the settings stem follows that filename. Verify the missing-runtime installation prompt on a clean Windows machine; the development machine already has .NET installed.
