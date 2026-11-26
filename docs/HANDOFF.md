# ReadArticles — Development handoff (2026-11-26)

## Verified main baseline

- Repository: https://github.com/chenjing2000/readarticles; Windows workspace: `C:\Projects\ReadArticles`.
- Accepted main application: `c9f8b6c1175eca826f737c5acae629aa38a04a81`, authorized squash merge of [PR #38](https://github.com/chenjing2000/readarticles/pull/38). Git tree `19ce0891ea2069357fe10a7c5b015b12e1002867` exactly matches accepted PR head `2165aadc0202f54387911821e8073f2315ffcfe9`. [Main CI](https://github.com/chenjing2000/readarticles/actions/runs/45772466029) passed. This handoff follow-up changes documentation only.
- Post-merge Windows main at c9f8b6c: fast-forward alignment, empty diff against 2165aad, Full 113/113 application checks, publishing safeguards 10/10, source read-only 2/2, five groups passed with zero failures/skips. Workflow 3/3. Report: `artifacts/reports/Full-20261126-151317.md`. Actual portable publication and PublishedApp 4/4 passed, zero failures/skips; clean worktree and launch command completed. No new post-merge interactive acceptance was reported; the identical application already passed pre-merge manual checks.
- Previous accepted application `cc3f525d2f1d48284561fdbaaf33de09a0d0f19a` (PR #37), documentation baseline `f2214ed81a580d8dd15c9204dbeecaba5b0299f3`. Historical Script playback background delivery and acceptance evidence remain in the [previous handoff](https://github.com/chenjing2000/readarticles/blob/f2214ed/docs/HANDOFF.md).

## Completed delivery: PR #38

- English title/body/headword font order: Newsreader (default), Source Serif 4, Georgia, Nunito, Microsoft YaHei UI. Meaning font order: Source Han Serif SC (default), Microsoft YaHei. Retain valid saved selections; normalize retired names; use existing installed-English-font detection and ordered CSS fallbacks. User confirmed installed variable Newsreader and Source Serif 4 display correctly. No font bundling or native UI font change.
- All fourteen numeric settings use shared single-line TextBoxes. Integer ranges: text size 10–40px, paragraph-after 0–40px, first-line indent 0–5em (one digit), seek forward/backward 1–60s. Decimal ranges: line height 1–3 and selection-add glyph 0.40–1.20em, one integer digit and at most two fractional digits. ASCII-only editing, whole invalid paste rejection, save-time range validation and focused nonmodal notice; retain existing defaults, layout, draft, cancel, reset, JSON schema and backup behavior. Decimal point deletion/cut must not create illegal integer text or disrupt ordinary legal undo/redo.
- Selection-add click target remains at least 24 CSS px square and expands to fit its glyph; use measured size for edge positioning. Preserve the existing frame-paced selection tracking and Script/vocabulary rendering behavior.
- R1–R8 review: correct missing title paragraph-after default to 16px and remove duplicate initializers (explicit 6px remains valid); remove unused browser seek-step snapshot fields, retain native audio authority; reuse the final selected rectangle; generalize notice names and protect invalid numeric deletion/cut; move structured settings tests to Core, split native layout and numeric editor tests, add missing-field, range, audio seek, geometry and editing-history checks; synchronize Configuration and Testing.
- Final PR diff against f2214ed: 12 existing files, +673/-266 lines total. Six application files; application code net +69 lines across the entire batch. No new files, application classes, persisted fields, dependencies, or runtime components.

## Pre-merge verification

- Font-only `f298608` [CI](https://github.com/chenjing2000/readarticles/actions/runs/45757312728) passed; user confirmed variable fonts display correctly.
- Font-size candidate `0e0da74` [CI](https://github.com/chenjing2000/readarticles/actions/runs/45764914951) passed and 10/10 manual checks passed.
- Unified numeric candidate `833c4de` [CI](https://github.com/chenjing2000/readarticles/actions/runs/45767172027) passed and 14/14 manual checks passed.
- Review commits `0265656` and `2165aad`; final [PR CI](https://github.com/chenjing2000/readarticles/actions/runs/45768611598) passed. Windows Full 113/113, publishing safeguards 10/10, source read-only 2/2, five groups passed with zero failures/skips; Workflow 3/3, actual publication and PublishedApp 4/4, clean worktree and launch command completed. Report: `artifacts/reports/Full-20261126-145638.md`.
- Final candidate passed 11/11 focused manual checks: default spacing, invalid Backspace/Delete/Ctrl+X, legal Ctrl+Z/Ctrl+Y, selection replacement and paste, save/cancel/reset, multiline selection tracking and resize, 40px/1.2em glyph target and edge placement, custom paused seeking, Script backgrounds and vocabulary highlights. Total 35 successful manual check instances across three candidates, not 35 separate checks repeated on main.
- Chromium shutdown diagnostic 1412 appeared in both final Windows Full logs without failing tests. Mixed-DPI comparisons and quantified rendering performance remain unverified. No separate post-merge interactive results were reported.

## Release status and cleanup

- PR #38 is merged; main application CI, Windows Full, Workflow, actual portable publication and PublishedApp passed. No further implementation batch is approved.
- At handoff preparation, remote `feature/reading-fonts` remained at the accepted head `2165aad`; local last reported main was clean and synchronized at c9f8b6c. Cleanup is authorized for this exact merged branch only; verify refs before deletion. The GitHub connection lacks a branch-deletion action, so complete remote/local deletion using Git on Windows. Previous `feature/playback-background` is absent from the verified remote branch list.
- Preserve divergent `feature/exercise` at `fbca9d26d7c02e8edfba984256a8543897772429`, all user data, and the previously reverted PR #34 decision.

## Development boundaries

- Begin subsequent work from verified main, checking [project contract](project-contract.md), [architecture](Application-Overview.md), [library format](Library-Layout.md), [testing](Testing.md), [publishing](Publishing.md) and [project review skill](skills/project-code-review/SKILL.md). [Documentation index](README.md) identifies the owner of each detail.
- Retain one WPF application project, one test project, one WebView2 reader, concrete feature modules, explicit MainViewModel coordination and native JavaScript ES modules. Preserve keyboard, audio, matching, settings, navigation and persistence invariants. Do not introduce new frameworks, data formats or dependencies without need.
- Retain `UiIcons.Refresh` for library refresh and Restore Defaults, the small `AudioItem` type and intentional settings-value duplication. Do not reintroduce SID, sentence playback, WPF reader or C# Script progress/matching.
- Protect real libraries, userdata, backups/recovery copies, navigation guards, media cancellation and browser failure handling. New development requires agreed minimal scope, isolated branch, tests and separate merge authorization.
