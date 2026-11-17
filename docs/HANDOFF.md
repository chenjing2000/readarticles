# ReadArticles — Handoff (2026-11-17)

## Current baseline

- Repository: `chenjing2000/readarticles`; local Windows checkout: `C:\Projects\ReadArticles`.
- Verified `main` at handoff creation: `a317f45afe179e8dad25330eee5a9545a945b2db` (PR #13). Always fetch the latest `main` before starting new work.
- Remote branches after cleanup: `main` and `feature/exercise`. The latter is an **unmerged historical EnglishBench exercise implementation**, not a branch to merge directly into the current reader. The obsolete `feature/unified-testdata` branch was removed; PR #1 was closed without merging.
- The recent reliability/code-cleanup and documentation pass is complete (PRs #9–#13 merged). Do not reopen it without a concrete issue or requirement.

## Application contract and workflow

- Windows x64, C#/WPF, .NET 10. Read [`AGENTS.md`](../AGENTS.md) and [`project-contract.md`](project-contract.md) first; use [`project-code-review`](skills/project-code-review/SKILL.md) and WindowsAppPlaybook when available.
- Reader opens a lazy, one-level-at-a-time library tree; passage JSON and media are read-only. Only the companion vocabulary JSON is edited, with external-change checks, verified backup and atomic replacement.
- Article audio and sentence/word pronunciation use separate coordinated players. MP3 takes precedence over WAV; decoding failure does not fall back. Missing sentence audio is checked at playback time.
- Notifications use `NotificationService` / floating `ToastHost`; legacy `MainViewModel.Status` and bottom status row were removed. Errors do not auto-expire.
- Portable output: `dist/ReadArticles/ReadArticles.exe`; settings and logs under adjacent `userdata/`. Preserve user data, source files and recovery copies during upgrades.

## Validation and delivery

Run from the repository root on a Windows desktop:

```powershell
dotnet build ReadArticles -c Release
.\tools\Test.ps1 -Suite Core
.\tools\Test.ps1 -Suite UI
.\tools\Test.ps1 -Suite Full
```

For a release, also run `.\tools\Publish.ps1` and `.\tools\Test-PublishedApp.ps1` (see [Publishing](Publishing.md)). The last locally supplied PR #12 validation passed Release build, Core (10 groups), UI (13 groups) and Full (6 groups; 114 internal checks), all with zero failures; this **does not validate future changes**. The PR #13 documentation-only change was not independently retested locally.

- Discuss proposed changes before implementation when requested; prefer smallest necessary edit, deletion over needless new abstractions, and preservation of proven boundaries.
- Use a feature branch and English commit message; create a PR. Do **not** merge into `main` without explicit approval. Core CI is not a substitute for local UI/Full acceptance for interactive changes.
- Do not experiment against real libraries, alter fixed `tests/TestData/` casually, or commit `dist/`, `artifacts/`, or `userdata/`.

## Open considerations (not authorized work)

- `feature/exercise` is kept for possible future reference only; its older architecture needs fresh design review before reuse.
- The current `main` has no automatic GitHub Release creation workflow. A removed older branch had an incompatible workflow; only add a new one if explicitly requested.
- Future release acceptance may include actual audio listening, DPI/multimonitor checks, published EXE startup, and synchronous warning on final settings-save failure.
- `Publish.ps1` retains previous EXE copies without automatic pruning; this is a deferred maintenance choice, not an established bug.

Start any new task by checking the current branch and `git status`, reading the relevant contract, and defining the specific change and acceptance criteria.
