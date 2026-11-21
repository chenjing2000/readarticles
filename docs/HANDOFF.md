# ReadArticles — Development handoff (2026-11-21)

## Current baseline

- Repository: [chenjing2000/readarticles](https://github.com/chenjing2000/readarticles); local Windows workspace: `C:\Projects\ReadArticles`.
- Stable `main` baseline: `9f6cdc1`. Current development: `feature/simplify-passage-audio-selection`, [Draft PR #21](https://github.com/chenjing2000/readarticles/pull/21). The PR must remain unmerged until the user separately authorizes Squash Merge.
- [Windows CI #218](https://github.com/chenjing2000/readarticles/actions/runs/44768028098) passed for `af0c66a` (Windows build, Core regression, portable publish and release-layout verification). Later final-review fixes require fresh verification; do **not** treat this earlier CI result as verifying later commits.
- The user ran `Test.ps1 -Suite UI` successfully (13 groups, 0 failures); then `Test.ps1 -Suite Full` successfully (94 individual regression checks, 0 failures; 6 outer checks, 0 failures) after the test-owner correction at `e473afd`. `Publish.ps1` and `Test-PublishedApp.ps1` also passed on Windows (4 published-app checks, 0 failures). The logs were explicitly supplied in chat.
- **User-confirmed real-machine functionality:** all six categories passed: new Passage format, Script playback-synchronized foreground color, vocabulary highlighting/selection, M4A/MP3/WAV article audio and fallback, playback controls/word-audio coordination, and removed sentence-audio feature. The user selected **continue acceptance**, not final merge approval.

## Scope and retained contracts

- Passage JSON is **breaking**: exactly `filetype: "passage"`, a required string `title` (empty is allowed, with no filename fallback), and `paragraphs: string[][]`. There are no sentence SIDs, `next_sid`, sentence objects, `HasAudio` or sentence playback. Preserve paragraph/sentence boundaries, valid `[[n]]` markers, and within-sentence selection using paragraph/sentence indices.
- The optional Script companion and Vocabulary JSON schemas are unchanged. Script foreground follows whole-article playback and seek. Passage and Script source JSON are read-only; Vocabulary saves retain transactional writes, conflict detection and backups.
- Whole-article audio candidates are taken once from the Passage JSON's direct sibling directory and ordered by extension priority **M4A → MP3 → WAV**, then filename (ordinal ignore-case, with ordinal tie-break). Attempt candidates in order until successful media opening. Opening failure continues to the next candidate, including within the same format; successful opening ends the fallback search. Stop/article switch/window close cancel pending attempts. Once successfully opened, a playback-time error does not switch recordings. Vocabulary audio remains MP3-existence-first, WAV second, without decoding fallback.
- **No old Passage migration or compatibility logic.** The user will independently normalize their article collection. One secondary source article under `tests/TestData/Library/` is still in the *old* Passage format; the user explicitly requested **not** to convert it. Do not misrepresent it as a new-format loadable fixture or add code to accept it. Existing first-article fixed tests use the converted test fixture.
- `docs/skills/` contains the current image-to-passage, passage segmentation, vocabulary enrichment and project-code-review contracts.

## Final delivery review (in progress)

- Static review confirmed the principal format simplification, WebView2 selection boundaries, word/audio separation and removal of SID-based calls.
- Approved narrow fixes after the review: reject unsafe individual audio candidates without aborting later candidates; preserve a user pause while media is opening and across a candidate failure; cover both with regression tests. These fixes were applied to `AudioResources.cs`, `PlaybackController.cs`, and `AudioChecks.cs` on PR #21.
- The second old-format source fixture must remain untouched by this review. No compatibility layer or article migration is authorized.
- Check the latest PR head and **its own** Windows CI before reporting the final status. Distinguish automatic Core CI from local interactive UI/Full and the user's already accepted six real-machine behaviors. In previous runs, hosted WebView2 sometimes timed out on rendering handshake but succeeded on rerun of unchanged commits; do not suppress or weaken the test to hide this.
- Do not merge PR #21, close PRs, delete development branches, or alter user article files without separate explicit authorization.

## Verification and handoff commands

```powershell
cd C:\Projects\ReadArticles
git status --short
git fetch origin
git switch feature/simplify-passage-audio-selection
git pull --ff-only origin feature/simplify-passage-audio-selection

.\tools\Test.ps1 -Suite Core
.\tools\Test.ps1 -Suite UI
.\tools\Test.ps1 -Suite Full
# Close the running application before publishing.
.\tools\Publish.ps1
.\tools\Test-PublishedApp.ps1
```

Start from [project contract](project-contract.md), [library layout](Library-Layout.md), [testing](Testing.md), and [project code review skill](skills/project-code-review/SKILL.md). Preserve the first-principles/minimal-change rule: avoid new models, compatibility branches, or unrelated cleanup. The next gate is reviewing fresh CI and presenting final acceptance options; merging remains separately authorized.
