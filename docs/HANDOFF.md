# ReadArticles — Development handoff (2026-11-25)

## Verified baseline

- GitHub: https://github.com/chenjing2000/readarticles; Windows workspace: `C:\Projects\ReadArticles`.
- **Verified application baseline:** `951e9bfc47a86f644c1d5f744a2784dae691ad8b`, the Squash Merge of [PR #31](https://github.com/chenjing2000/readarticles/pull/31). The [post-merge Windows CI](https://github.com/chenjing2000/readarticles/actions/runs/45529294370) passed Release build, Core regression, portable publish and release layout. Always recheck actual branch HEAD before new work.
- **PR #31 accepted and merged:** [feature CI](https://github.com/chenjing2000/readarticles/actions/runs/45528774435) passed on `4a8b1da`. User-provided Windows logs confirm UI 73/73, Full 98/98 plus 2 read-only source checks, Publish.ps1 and Test-PublishedApp.ps1 4/4; Test-Workflow.ps1 passed 3 checks earlier in the same development round. The user confirmed physical keyboard behavior, repeat suppression, paused seeking, dialog isolation, and all six focused-control visual-response checks after the CSS correction. Intermittent Chromium Error 1412 on shutdown did not fail the tests. These are recorded results, not new local test executions.
- **PR #28 accepted and closed:** [feature CI](https://github.com/chenjing2000/readarticles/actions/runs/44968738749) passed; user-provided Windows logs confirm `Test-Workflow.ps1` (3 checks), UI (73/73), Full (98/98 plus 2 read-only source checks), `Publish.ps1`, and `Test-PublishedApp.ps1` (4/4). The user separately confirmed that `family` forms match correctly in the running application. Intermittent Chromium/WebView2 `Error 1412` shutdown messages did not fail these tests. These are recorded acceptance results, not tests re-run for this handoff.
- **Relevant history:** [PR #21](https://github.com/chenjing2000/readarticles/pull/21) established the Passage/Script, vocabulary and whole-article audio behavior; [PR #22](https://github.com/chenjing2000/readarticles/pull/22) fixed failed TreeView passage-selection rollback; [PR #23](https://github.com/chenjing2000/readarticles/pull/23) corrected the vocabulary highlight toggle. Cleanup [PR #24](https://github.com/chenjing2000/readarticles/pull/24), [PR #26](https://github.com/chenjing2000/readarticles/pull/26) and [PR #27](https://github.com/chenjing2000/readarticles/pull/27) removed proven unused Script/test code and consolidated test execution; all 240 active vocabulary MP3 fixtures remain. [PR #25](https://github.com/chenjing2000/readarticles/pull/25) merged the previous handoff refresh.
- **Remote branches at PR #31 closeout:** `main`, `feature/exercise`, and merged `feature/window-playback-shortcuts` (safe to delete after local synchronization; connector cannot delete refs). Preserve `feature/exercise` at `fbca9d26d7c02e8edfba984256a8543897772429` until separately reviewed. This does not establish the local working-tree state.

## Current behavior and protected boundaries

- Passage JSON has `filetype: "passage"`, required string `title` (empty allowed), and `paragraphs: string[][]`. There is no old SID format migration or sentence-level playback. Passage/Script files and audio are read-only; vocabulary saving preserves metadata and protects concurrent/external changes with atomic replacement and verified backups.
- Vocabulary entries accept optional `family: string[]` (missing or empty is valid; malformed, repeated, or headword-identical forms are rejected). The vocabulary-enrichment skill records only source-attested inflections when a Passage is supplied. WebView2 matches the literal `word` and listed `family` forms in both Passage and Script, attributing hits to the canonical headword; direct headword matches take precedence. No automatic morphology or contextual disambiguation is performed. `family` is not displayed in the right pane and does not alter word-audio stems. See [Vocabulary Preparation](Vocabulary-Preparation.md).
- Whole-article playback tries safe directly adjacent M4A, then MP3, then WAV candidates in filename order until one **opens**. Word pronunciation looks under `audio/` for derived `<stem>_uk/us.mp3` or WAV files: MP3 wins by existence, and MP3 decode failure does not retry WAV. Article and pronunciation use separate coordinated controllers; stale callbacks must not change the newer target.
- WPF owns the native window, tree and settings dialog; a single WebView2 hosts the Passage/Script reader. `web/reader.js` paints Script progress; the deleted C# `ScriptTimedUnit.Progress()` must not be revived. `ReaderWorkspace` now commits prepared articles; actual loading is through `ArticleRepository.Load()` and the view model.
- **Keep `UiIcons.Refresh`**, used by Restore Defaults. The one-field `AudioItem` and small setting-value duplication were inspected and intentionally **retained**, because replacing them with string-only APIs or metadata frameworks does not demonstrably simplify the program.
- Test setup copies only the 240 active vocabulary MP3 fixtures; it still validates their completeness and removes obsolete `audio` properties from a **temporary copy** of the legacy vocabulary JSON. It does not copy sentence recordings or rewrite Passage JSON. Core/UI invoke unique test groups once; Full includes logger checks without a duplicate process.
- Protect real user libraries, `userdata/`, backups and recovery copies, directory-scan/session guards, audio cancellation and browser failure handling. Prefer minimal deletion of proven dead code, not speculative rewrites.

## Next development round

- **The family and window-wide playback shortcut changes are complete; no additional cleanup batch is approved by this handoff.** If additional cleanup is requested, first inspect current source and establish concrete callers and risks. A small SVG icon loader was an unverified earlier review candidate; evaluate whether a change really reduces complexity. Do not reopen `AudioItem`, setting definitions or deleted test paths merely to reduce line counts.
- Read the [project contract](project-contract.md), [architecture](Application-Overview.md), [library format](Library-Layout.md), [test guide](Testing.md) and [review skill](skills/project-code-review/SKILL.md). Check actual `main` HEAD, PRs and local working-tree state. Discuss a minimal per-file plan before implementation, use an isolated branch and Draft PR, and merge only on separate user authorization.
- For Windows verification use the existing commands below, and report only checks actually run. Destructive tests need a disposable copy of the article library. Hosted CI is not a substitute for real GUI/audio acceptance.

```powershell
cd C:\Projects\ReadArticles
git status -sb
git fetch --prune origin
git switch main
git pull --ff-only origin main

.\tools\Test-Workflow.ps1
.\tools\Test.ps1 -Suite Core
.\tools\Test.ps1 -Suite UI
.\tools\Test.ps1 -Suite Full
# Close any running application before publishing.
.\tools\Publish.ps1
.\tools\Test-PublishedApp.ps1
```

## Main-window playback shortcuts (accepted and merged)

1. **Exclusive:** Unmodified Space, Left and Right are reserved for whole-article play/pause, backward seek and forward seek, respectively, throughout the active main window. Use the existing configured seek intervals; separate dialogs and modified key combinations retain normal behavior.
2. **Focus-independent:** The same shortcuts apply to every focused native WPF or WebView2 control; mouse behavior remains unchanged.
3. **Non-propagating:** Consume the reserved keys even when playback is unavailable; prevent default control actions and duplicate dispatch. Space toggles once per physical press; arrow keys allow normal auto-repeat.
4. **State-preserving:** Seeking retains the playing/paused state and keyboard focus. The progress bar reflects authoritative playback state; do not synthesize input or move focus to the bar.
5. **Minimal implementation:** Use one interception point in WPF and one in the WebView2 document capture phase, sharing existing C# playback commands. Remove conflicting control-specific listeners (including keyboard splitter resizing); no global hooks, new controller or shortcut settings.

The above behavior has passed automated and physical Windows acceptance. WebView2 suppresses focus-visible outlines and keyboard-only splitter coloring while retaining focus, Tab navigation, selected-word marking and mouse behavior. The current-behavior specifications and regression checks have been updated; no additional playback shortcut implementation is pending.
