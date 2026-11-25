# ReadArticles — Development handoff (2026-11-25)

## Verified main baseline

- Repository: https://github.com/chenjing2000/readarticles; Windows workspace: `C:\Projects\ReadArticles`.
- Accepted main application: `cc3f525d2f1d48284561fdbaaf33de09a0d0f19a`, authorized squash merge of [PR #37](https://github.com/chenjing2000/readarticles/pull/37). Git tree `4708446e5877314cf2743a1dbf2931be9dfc0d4a` exactly matches accepted PR head `d1fb8a2c0cf301a00b0f4cdc7e9b8be8a175bb25`. [Main CI](https://github.com/chenjing2000/readarticles/actions/runs/45631297017) passed on its first run. This handoff follow-up changes documentation only.
- Post-merge Windows main at cc3f525: fast-forward alignment, empty diff against d1fb8a2, Full 111/111 application checks (including four logging checks), publishing safeguards 10/10, source read-only 2/2, five groups passed with zero failures/skips. Workflow 3/3. Report: `artifacts/reports/Full-20261125-221205.md`. First portable publication built successfully but refused replacement of an occupied EXE; retry succeeded. PublishedApp 4/4 both before retry and again on the final actual published EXE, zero failures/skips; clean worktree and launch command completed. No separate post-merge interactive playback result was reported; the identical application already passed pre-merge manual acceptance.
- Previous accepted main application `891cba0c409c8d7a083b6de4dace23990b15b86f` (PR #36), documentation baseline `70eca4a`; historical scope and evidence are in the [previous handoff](https://github.com/chenjing2000/readarticles/blob/70eca4a/docs/HANDOFF.md).

## Completed delivery: PR #37

- Continuous Script playback backgrounds, including playback-over-vocabulary priority, based on AudioPlayer final application `d8458d6` and handoff `275e9bf`.
- One original text layer, direct SVG rectangles beneath unchanged text, same-visual-line pure-whitespace trailingGap only upon completion, crispEdges, five light colors, ten historical ReadArticles color mappings, same settings field. Playback and vocabulary state remain independent. No pagination, translation or export functionality.
- R1–R5 review corrections: verify automatic paused reflow without a new playback message, real JSON null/unaligned units, paragraph boundaries and exact rectangle geometry; restore vocabulary Range matches, playback and scroll after temporary DOM; remove unused paragraph dataset and duplicate success-path geometry scheduling; consolidate handoff and test descriptions.
- Final application diff against 70eca4a: six existing application files, +55/-53 lines (net +2); three existing test files and seven existing documents. No new application files, classes, methods, persistent settings fields, timers or dependencies.

## Pre-merge verification

- Initial candidate `a4d6d6d`: [PR CI](https://github.com/chenjing2000/readarticles/actions/runs/45625878895) and [documentation CI at 61ed00b](https://github.com/chenjing2000/readarticles/actions/runs/45627254402) passed. Windows Full 111/111, publishing safeguards 10/10, source read-only 2/2, five groups passed, actual publication and PublishedApp 4/4, zero failures/skips. Report: `artifacts/reports/Full-20261125-214110.md`. User confirmed 15/15 manual checks including five colors, continuity, wrapped units, resize, playback, vocabulary layering and settings.
- Review-corrected candidate `d1fb8a2`: [PR CI](https://github.com/chenjing2000/readarticles/actions/runs/45629280175) passed first attempt. Windows Full 111/111, publishing safeguards 10/10, source read-only 2/2, five groups passed, Workflow 3/3, actual publication and PublishedApp 4/4, zero failures/skips, clean worktree. Report: `artifacts/reports/Full-20261125-220518.md`. User confirmed 6/6 focused manual regression: normal playback, paused window resize and splitter, rewind/stop vocabulary restoration, vocabulary navigation/selection/add, repeated Passage/Script switching. Initial and corrected candidates have 21 successful manual check instances, not a fresh post-merge manual run.
- Intermittent Chromium shutdown diagnostic 1412 appeared in Windows logs without failing tests. Mixed-DPI comparisons and quantified rendering performance were not performed. Prior deferred risks remain in the previous handoff; no evidence warrants expanding this batch.

## Release status and cleanup

- PR #37 is merged; post-merge main CI, Windows Full, Workflow, portable publication and final PublishedApp verification passed. This final handoff is documentation only. No further implementation batch is approved.
- Remote `feature/playback-background` remains at `d1fb8a2`; local branch last reported at the same accepted commit. Cleanup awaits separate authorization and ref verification. Preserve divergent `feature/exercise` at `fbca9d26d7c02e8edfba984256a8543897772429`, all user data, and the previously reverted PR #34 decision.

## Development boundaries

- Begin subsequent work from verified main, checking [project contract](project-contract.md), [architecture](Application-Overview.md), [library format](Library-Layout.md), [testing](Testing.md), [publishing](Publishing.md) and [project review skill](skills/project-code-review/SKILL.md). [Documentation index](README.md) identifies the owner of each detail.
- Retain one WPF application project, one test project, one WebView2 reader, concrete feature modules, explicit MainViewModel coordination and native JavaScript ES modules. Preserve keyboard, audio, matching, settings, navigation and persistence invariants. Do not introduce new frameworks, data formats or dependencies without need.
- Retain `UiIcons.Refresh` for library refresh and Restore Defaults, the small `AudioItem` type and intentional settings-value duplication. Do not reintroduce SID, sentence playback, WPF reader or C# Script progress/matching.
- Protect real libraries, userdata, backups/recovery copies, navigation guards, media cancellation and browser failure handling. New development requires agreed minimal scope, isolated branch, tests and separate merge authorization.
