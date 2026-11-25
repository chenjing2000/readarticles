# ReadArticles — Development handoff (2026-11-25)

## Verified baseline

- Repository: https://github.com/chenjing2000/readarticles; Windows workspace: `C:\Projects\ReadArticles`.
- Application baseline: `951e9bfc47a86f644c1d5f744a2784dae691ad8b` (squash merge of [PR #31](https://github.com/chenjing2000/readarticles/pull/31)). [Post-merge Windows CI](https://github.com/chenjing2000/readarticles/actions/runs/45529294370) passed.
- Previous documentation baseline: `eea5ee55d88dd2196d503deb4048bddbfd3e5185`; [CI](https://github.com/chenjing2000/readarticles/actions/runs/45529466703) passed. Always inspect actual main HEAD before new work.
- PR #31 feature [CI](https://github.com/chenjing2000/readarticles/actions/runs/45528774435) passed on `4a8b1da`. User-provided Windows acceptance: Workflow 3/3 earlier in the round; UI 73/73; Full 98/98 plus 2 read-only source checks; Publish and published EXE checks 4/4. Physical keyboard acceptance confirmed exclusive shortcuts, repeated arrows, single Space toggle, paused seeking, dialog isolation, and all six focused-control visual checks. After merging, local main at `eea5ee5` passed Full 98/98 plus 2 source checks; first publish encountered an in-use EXE, smoke checks passed on the existing EXE, and the second publish succeeded. No fresh Windows run is claimed for documentation changes.
- The user synchronized main and deleted the merged playback feature branch. Remote branches at this verified baseline: `main` and `feature/exercise` at `fbca9d26d7c02e8edfba984256a8543897772429`. Preserve the divergent exercise branch until separately reviewed. Local state is established only by the supplied logs, not direct filesystem access.

## Current development boundaries

- Read [project contract](project-contract.md), [architecture](Application-Overview.md), [library format](Library-Layout.md), [testing](Testing.md) and [project review skill](skills/project-code-review/SKILL.md). The [documentation index](README.md) identifies the owner of each detail.
- PR #28 added explicit vocabulary family matching; PR #31 added exclusive whole-article Space/Left/Right shortcuts and removed conflicting keyboard/focus visuals. Both are accepted. No pending implementation or cleanup batch is approved. Historical implementation records remain in merged PRs and Git history.
- Retain `UiIcons.Refresh` for Restore Defaults, the small `AudioItem` type and intentional settings-value duplication. Do not reintroduce removed SID, sentence playback, WPF reader or C# Script progress/matching implementations, or remove code merely to reduce lines.
- Protect real libraries, userdata, backup/recovery copies, navigation guards, media cancellation and browser failure handling. New work requires a minimal scoped plan, isolated branch, tests and separate merge authorization.
- Follow the commands and interactive gates in [Testing](Testing.md) and [Publishing](Publishing.md). Check actual main HEAD, PRs and local status; distinguish hosted CI from local acceptance. The intermittent Chromium Error 1412 shutdown warning did not fail the recorded tests.
