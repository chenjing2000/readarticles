# ReadArticles — Development handoff (2026-11-21)

## Verified baseline and repository

- GitHub: https://github.com/chenjing2000/readarticles; Windows workspace: `C:\Projects\ReadArticles`.
- **Last verified application baseline:** `main` at `388f57c7aaaf1e0fbdbe8a25c2d7f3c1e30135b4`, the Squash Merge of [PR #24](https://github.com/chenjing2000/readarticles/pull/24). The [post-merge Windows CI](https://github.com/chenjing2000/readarticles/actions/runs/44877177907) passed (build, Core regression, portable publish and release layout). Check the actual `main` HEAD on resumption; a subsequent documentation-only merge may move it.
- Recent merged work: [PR #21](https://github.com/chenjing2000/readarticles/pull/21) established the Passage/Script, vocabulary and whole-article audio contracts; [PR #22](https://github.com/chenjing2000/readarticles/pull/22) fixed failed TreeView passage selection rollback; [PR #23](https://github.com/chenjing2000/readarticles/pull/23) made the vocabulary-highlight toggle gray when off and magenta when on; [PR #24](https://github.com/chenjing2000/readarticles/pull/24) removed unused C# Script progress and aggregate playback-state forwarding.
- PR #24: [feature CI](https://github.com/chenjing2000/readarticles/actions/runs/44874841134) succeeded; local Release build and UI **13 groups passed**, and a rerun of local Full **97 checks passed, 0 failed, 0 skipped** (plus two unchanged-source checks). An earlier Full run had one WebView2 notification-dismiss timeout while the user reported accidentally closing test windows; the rerun passed. The user confirmed Script timing and switching, coordinated audio playback, vocabulary reorder/delete and the settings **Restore Defaults** button on a Windows desktop.
- At this handoff the remote branches were `main`, `refactor/remove-unused-reader-code` (merged PR #24; branch not yet deleted), and `feature/exercise` (two historical commits not merged). Do not assume a local branch, working-tree state or subsequent remote cleanup without checking Git.

## Current behavior and non-negotiable boundaries

- Passage JSON: `filetype: "passage"`, required string `title` (empty allowed) and `paragraphs: string[][]`. No SID/old Passage migration. The matching Script and Passage files are read-only; vocabulary saves preserve metadata and protect against concurrent/external edits with atomic replacement and verified backups.
- Whole-article playback tries safe direct sibling audio candidates in M4A, MP3, WAV priority and filename order until one **opens**. Word pronunciation resolves `audio/<stem>_uk/us.mp3` or WAV, with MP3 preferred by existence and no fallback after an MP3 decoding failure. There is no sentence playback. Separate article/pronunciation controllers coordinate pause/stop; stale media callbacks do not overwrite new state.
- WebView2 is the sole Passage/Script reader; WPF owns the native window, left navigation and settings dialog. Script foreground progress is painted by `web/reader.js`; the old C# `ScriptTimedUnit.Progress()` and aggregate `ReaderPlayback.State/Owner` APIs have been deleted. **Keep `UiIcons.Refresh`**: the settings dialog uses it for the Restore Defaults button.
- Protect selected source libraries, `userdata/`, backups, recovery copies, media files, failure rollback and browser session/request guards. Prefer deleting demonstrably unused code to adding abstractions; allow small local duplication when clearer.

## Next round — proposed, not authorized

- **Best next candidate:** review `tools/Test.ps1` and `ReadArticles.Tests/Program.cs` for duplicate execution of the same test methods across Core/UI category combinations. Preserve every unique assertion, the individual targeted-test entry points and Windows Core/UI/Full coverage; quantify coverage before editing.
- **Optional follow-up reviews:** `ReaderWorkspace.OpenArticle()` (test-only convenience), `AudioItem` (single-field wrapper), duplicated settings option definitions, and the small native SVG loader. Each needs its own impact/benefit check; do not refactor merely to reduce line counts.
- No implementation, branch deletion or merge is authorized by this handoff. Confirm the next change with the user before altering application behavior or creating a large refactor.

## Resume checklist

1. Read [project contract](project-contract.md), [architecture](Application-Overview.md), [library/JSON layout](Library-Layout.md), [testing](Testing.md) and [review skill](skills/project-code-review/SKILL.md).
2. Check remote `main`, current PRs and local status; start any new code change from the actual latest `main` in a separate branch.
3. Discuss a minimal per-file proposal first. Keep PRs Draft until checks and user acceptance; merge only after separate authorization.

```powershell
cd C:\Projects\ReadArticles
git status -sb
git fetch --prune origin
git switch main
git pull --ff-only origin main

.\tools\Test.ps1 -Suite Core
.\tools\Test.ps1 -Suite UI
.\tools\Test.ps1 -Suite Full
# Close any running EXE before publishing.
.\tools\Publish.ps1
.\tools\Test-PublishedApp.ps1
```

Use an isolated copy of the article library for destructive tests. The GitHub Core check does not substitute for local UI, Full or published-EXE acceptance. Do not claim tests were run unless supported by logs.
