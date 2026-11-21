# ReadArticles — Development handoff (2026-11-21)

## Verified baseline

- GitHub: https://github.com/chenjing2000/readarticles; Windows workspace: `C:\Projects\ReadArticles`.
- **Latest verified application baseline before this documentation PR:** `main` at `43adc495d5a0c9b27faa4007e74f882da0c35f4d`, the Squash Merge of [PR #27](https://github.com/chenjing2000/readarticles/pull/27). [Post-merge Windows CI](https://github.com/chenjing2000/readarticles/actions/runs/44880549914) passed build, Core regression, portable publish and release layout. A documentation merge will change the HEAD without changing application behavior; always recheck the actual `main`.
- [PR #21](https://github.com/chenjing2000/readarticles/pull/21) established the current Passage/Script, vocabulary and whole-article audio behavior; [PR #22](https://github.com/chenjing2000/readarticles/pull/22) fixed failed TreeView passage-selection rollback; [PR #23](https://github.com/chenjing2000/readarticles/pull/23) corrected the vocabulary highlight toggle (gray off, magenta on).
- **Three cleanup batches completed:** [PR #24](https://github.com/chenjing2000/readarticles/pull/24) removed unused C# Script progress and aggregate playback state forwarding; [PR #26](https://github.com/chenjing2000/readarticles/pull/26) deduplicated Core/UI test execution while retaining all targeted test entry points; [PR #27](https://github.com/chenjing2000/readarticles/pull/27) removed the test-only `ReaderWorkspace.OpenArticle()`, obsolete SID test preparation and 122 unused sentence MP3 fixtures (4,651,344 bytes). All 240 active vocabulary MP3s remain.
- For PR #27, [feature CI](https://github.com/chenjing2000/readarticles/actions/runs/44879919382) passed. User-provided local Windows logs confirm Release build, `Test-Workflow.ps1` (3 checks), UI (72/72), Full (97/97 plus 2 read-only source checks), `Publish.ps1` and `Test-PublishedApp.ps1` (4/4). Intermittent WebView2 `Error 1412` lines caused no test failure; do not assume they prove a product defect. Earlier PR #24 also passed manual Script switching/progress, audio coordination, word reorder/delete and settings Restore Defaults acceptance.
- This documentation update is [PR #25](https://github.com/chenjing2000/readarticles/pull/25). Check its actual merge status before starting a new development round. Do not assume any old feature/cleanup branches have been removed or a local workspace is clean.

## Current behavior and protected boundaries

- Passage JSON has `filetype: "passage"`, required string `title` (empty allowed), and `paragraphs: string[][]`. There is no old SID format migration or sentence-level playback. Passage/Script files and audio are read-only; vocabulary saving preserves metadata and protects concurrent/external changes with atomic replacement and verified backups.
- Whole-article playback tries safe directly adjacent M4A, then MP3, then WAV candidates in filename order until one **opens**. Word pronunciation looks under `audio/` for derived `<stem>_uk/us.mp3` or WAV files: MP3 wins by existence, and MP3 decode failure does not retry WAV. Article and pronunciation use separate coordinated controllers; stale callbacks must not change the newer target.
- WPF owns the native window, tree and settings dialog; a single WebView2 hosts the Passage/Script reader. `web/reader.js` paints Script progress; the deleted C# `ScriptTimedUnit.Progress()` must not be revived. `ReaderWorkspace` now commits prepared articles; actual loading is through `ArticleRepository.Load()` and the view model.
- **Keep `UiIcons.Refresh`**, used by Restore Defaults. The one-field `AudioItem` and small setting-value duplication were inspected and intentionally **retained**, because replacing them with string-only APIs or metadata frameworks does not demonstrably simplify the program.
- Test setup copies only the 240 active vocabulary MP3 fixtures; it still validates their completeness and removes obsolete `audio` properties from a **temporary copy** of the legacy vocabulary JSON. It does not copy sentence recordings or rewrite Passage JSON. Core/UI invoke unique test groups once; Full includes logger checks without a duplicate process.
- Protect real user libraries, `userdata/`, backups and recovery copies, directory-scan/session guards, audio cancellation and browser failure handling. Prefer minimal deletion of proven dead code, not speculative rewrites.

## Next development round

- **No fourth batch is approved by this handoff.** If additional cleanup is requested, first inspect current source and establish concrete callers and risks. A small SVG icon loader was an unverified earlier review candidate; evaluate whether a change really reduces complexity. Do not reopen `AudioItem`, setting definitions or deleted test paths merely to reduce line counts.
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
