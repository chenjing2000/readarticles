# ReadArticles — Development handoff (2026-11-25)

## Verified baseline and pending integration

- Repository: https://github.com/chenjing2000/readarticles; Windows workspace: `C:\Projects\ReadArticles`.
- Accepted main application baseline: `951e9bf` (PR #31). Main documentation baseline: `41c3cd8` (PR #32; [post-merge CI](https://github.com/chenjing2000/readarticles/actions/runs/45531098922) passed).
- Modular refactor: `refactor/modular-architecture`, [PR #33](https://github.com/chenjing2000/readarticles/pull/33). R1 design and R2–R6 implementation are complete. Hosted Windows CI passed at R2 `c0306e7`, R3 `286136e`, R4 `15649bf`, R5 `1fb9fb0`, and [R6 `969e399`](https://github.com/chenjing2000/readarticles/actions/runs/45534336334). R7 integrates source layout, removes redundant entry points and aligns documentation. Inspect actual PR head and CI before acceptance.
- R7 is **not accepted or merged by this document**. Local Workflow, UI, Full, read-only source checks, portable publish, published-app checks and interactive acceptance remain to be run on the actual candidate. Historical accepted PR #31 counts (UI 73/73, Full 98/98, source 2/2, published checks 4/4) are not new results. Report actual counts.
- Keep `feature/exercise` at `fbca9d26d7c02e8edfba984256a8543897772429` unchanged. Local worktree status is established only by supplied evidence.

## Current development boundaries

- Read [project contract](project-contract.md), [architecture](Application-Overview.md), [library format](Library-Layout.md), [testing](Testing.md) and [project review skill](skills/project-code-review/SKILL.md). The [documentation index](README.md) identifies the owner of each detail.
- Retain one WPF project, one WebView2 reader, concrete feature modules, explicit MainViewModel coordination and native JavaScript ES modules. No added framework, data format or runtime dependency. Preserve keyboard, audio, matching, settings, navigation and persistence invariants.
- Retain `UiIcons.Refresh` for Restore Defaults, the small `AudioItem` type and intentional settings-value duplication. Do not reintroduce SID, sentence playback, WPF reader or C# Script progress/matching, or remove code merely to reduce lines.
- Protect real libraries, userdata, backups/recovery copies, navigation guards, media cancellation and browser failure handling. Follow [Testing](Testing.md) and [Publishing](Publishing.md). Final PR requires explicit merge approval after local acceptance. Once merged, update this handoff to the accepted main baseline and clean only the completed refactoring branch.
