# ReadArticles — Development handoff (2026-11-21)

## Repository and verified baseline

- GitHub: https://github.com/chenjing2000/readarticles; Windows workspace: `C:\Projects\ReadArticles`.
- **Current production baseline:** `main` incorporates [PR #21](https://github.com/chenjing2000/readarticles/pull/21) via Squash Merge `6e45aed52d2aa9844fb6742cfa6a1ef378f7c5e9` (2026-11-21). PR #21 is closed/merged, not a pending Draft.
- Before merge, the final feature head `f029f28` passed [Windows CI #222](https://github.com/chenjing2000/readarticles/actions/runs/44847542023): build, Core regression, portable publish and release layout. The user separately confirmed local `Test.ps1 -Suite Full` **97 passed, 0 failed, 0 skipped** at `f029f28`, and earlier local UI (13 groups passed), publish and published-EXE checks. The user approved all six real-machine functional acceptance categories, then authorized the merge.
- The final merged PR contains the breaking Passage schema, Script-synchronized coloring, vocabulary selection, M4A/MP3/WAV whole-article audio fallback, coordinated word/audio playback and removal of sentence audio. Final audit addressed unsafe candidate paths and pause intent across asynchronous media opening.
- Branch cleanup was requested after PR #21. The user reported remote tracking branches `origin/main` and `origin/feature/exercise`; three historical local branches were still present at that moment. **Do not assume they have since been deleted**. Inspect current local/remote Git status in the new session.

## Current contracts and exclusions

- Passage uses precisely `filetype: "passage"`, string `title` (required, empty permitted; no filename fallback) and `paragraphs: string[][]`. The application **does not** read or convert the old SID/segment format; the user will standardize article sources independently. Some older secondary test/source article material may still have the old format and is intentionally not converted or used as a new-format loading fixture.
- Script JSON and Vocabulary JSON retain their separate formats. Passage and Script are read-only; writing vocabulary must protect external changes, atomically save, and maintain verified backups.
- Whole-article audio searches files directly beside the selected Passage JSON, ordered M4A then MP3 then WAV, and filename within each type. Individual unsafe or unplayable files do not prevent trying later valid candidates. A successful open ends candidate search. Stop/switch invalidates old events; a pause request while opening persists through retries. Word pronunciation uses separate MP3/WAV lookup under `audio/`, MP3 preferred by existence, with no WAV retry after MP3 decode failure.
- No legacy reader, sentence-audio paths, migrations, unnecessary new layers or old-format compatibility. For a new request, follow first principles and the minimum necessary code/doc changes.

## Starting the next development round

1. Read [project contract](project-contract.md), [architecture](Application-Overview.md), [library format](Library-Layout.md), [test and acceptance](Testing.md), and [review skill](skills/project-code-review/SKILL.md). Refer to [documentation index](README.md) for other focused guides.
2. Verify GitHub `main` HEAD and current PRs; check local branch and working tree before editing. Do not assume this handoff's historical commit remains the latest after subsequent documentation-only changes.
3. For new changes, discuss requirements and minimal implementation plan first. Work on a new feature branch/PR, preserve the `main` baseline, and keep merging subject to the user's separate approval.
4. When necessary, run these existing commands from an interactive Windows desktop:

```powershell
cd C:\Projects\ReadArticles
git status --short
git fetch --prune origin
git switch main
git pull --ff-only origin main

.\tools\Test.ps1 -Suite Core
.\tools\Test.ps1 -Suite UI
.\tools\Test.ps1 -Suite Full
# Close the app before publishing.
.\tools\Publish.ps1
.\tools\Test-PublishedApp.ps1
```

Do not claim local GUI/audio or new-head validation was performed unless corresponding logs are provided. Preserve the selected real article library, `userdata/`, recovery files, and backups. No new feature is authorized by this handoff alone.
