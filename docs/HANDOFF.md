# ReadArticles — Development handoff (2026-11-25)

## Verified main baseline

- Repository: https://github.com/chenjing2000/readarticles; Windows workspace: `C:\Projects\ReadArticles`.
- Main `70eca4a9f1afe1b8e6c045075c060f583969f80e` is a documentation-only follow-up to accepted application `891cba0c409c8d7a083b6de4dace23990b15b86f`, squash merge of [PR #36](https://github.com/chenjing2000/readarticles/pull/36). [Main CI](https://github.com/chenjing2000/readarticles/actions/runs/45559977645) passed.
- PR #36 post-merge Windows Full 111/111, publishing safeguards 10/10, read-only source 2/2, all five groups passed, actual publication and PublishedApp 4/4 passed. Report: `artifacts/reports/Full-20261125-141940.md`. Historical PR #36 scope, review and acceptance evidence remain in the [main baseline handoff](https://github.com/chenjing2000/readarticles/blob/70eca4a/docs/HANDOFF.md) and merged PR.
- Preserve divergent `feature/exercise` at `fbca9d26d7c02e8edfba984256a8543897772429`. Former remote `feature/refresh-and-icons` no longer exists; local cleanup status unverified. Do not restore reverted PR #34.

## Current delivery: PR #37

- Branch `feature/playback-background`; [PR #37](https://github.com/chenjing2000/readarticles/pull/37) targets main `70eca4a`. User approved continuous Script playback backgrounds, including playback-over-vocabulary priority. AudioPlayer reference: final application `d8458d6`, handoff `275e9bf`.
- One original text layer, direct SVG rectangles beneath unchanged text, same-visual-line pure-whitespace trailingGap only upon completion, crispEdges, five light colors, ten historical ReadArticles color mappings, same settings field. Playback and vocabulary state remain independent. No pagination, translation or export functionality is added.
- Initial application/test candidate `a4d6d6d52dfba2f174e14325fbf2194d7fbacac6`: six existing application files (+55/-51, net +4), three existing test files and seven existing documents. No new application files, classes, methods, persistent settings fields, timers or dependencies.
- User approved pre-merge review corrections R1–R5: test automatic paused reflow recovery without a new playback message; verify actual JSON null/unaligned units, paragraph boundaries and exact rectangle geometry; rebuild vocabulary Range matches and restore playback/scroll state after temporary DOM; remove unused paragraph dataset and duplicate success-path geometry scheduling; consolidate handoff and test descriptions. No change to core background algorithm, settings or data format. The corrected application/test candidate requires fresh CI and Windows regression evidence; do not transfer previous counts to the new commit.

## Verified evidence for the initial candidate

- [Application PR CI](https://github.com/chenjing2000/readarticles/actions/runs/45625878895) and [documentation-only follow-up CI at 61ed00b](https://github.com/chenjing2000/readarticles/actions/runs/45627254402) both succeeded (windows-core).
- User's synchronized Windows checkout at `a4d6d6d` was clean before testing. Full 111/111 application checks (including four logging checks), 10/10 publishing safeguards, 2/2 source read-only checks and five groups passed, zero failures/skips. Report: `artifacts/reports/Full-20261125-214110.md`. Actual portable publication succeeded first attempt; PublishedApp 4/4, zero failures/skips. Actual published EXE launched.
- User confirmed 15/15 manual items: unchanged text color, proportional sweep, cumulative background, continuous seams and smooth moving edge, five colors, correct height/indent/line ends, untimed text, wrapped units, resize/splitter alignment, pause/resume/seek/stop, playback priority and vocabulary restoration, Passage/Script switching, vocabulary matching/selection/positioning/editing, settings save/cancel/reset/restart.
- Two Chromium shutdown diagnostics (Chrome_WidgetWin_0, error 1412) were nonfatal. Mixed-DPI comparisons, quantified performance, separate Workflow run and post-test git status were not reported. Previous deferred risks remain in the main baseline handoff; do not expand scope without evidence.

## Pending verification and release

- Confirm review-corrected commit's GitHub CI and Windows Full, Workflow, actual publication, PublishedApp and focused manual regression (especially paused resize, vocabulary selection and mode switching). Record actual results before requesting separate merge authorization.
- No merge authorized. After authorized squash merge, verify main CI and Windows post-merge regression, then update handoff and clean up the feature branch only after ref verification and authorization.

## Development boundaries

- Begin subsequent work from verified main, checking [project contract](project-contract.md), [architecture](Application-Overview.md), [library format](Library-Layout.md), [testing](Testing.md), [publishing](Publishing.md) and [project review skill](skills/project-code-review/SKILL.md). [Documentation index](README.md) identifies the owner of each detail.
- Retain one WPF application project, one test project, one WebView2 reader, concrete feature modules, explicit MainViewModel coordination and native JavaScript ES modules. Preserve keyboard, audio, matching, settings, navigation and persistence invariants. Do not introduce new frameworks, data formats or dependencies without need.
- Retain `UiIcons.Refresh` for library refresh and Restore Defaults, the small `AudioItem` type and intentional settings-value duplication. Do not reintroduce SID, sentence playback, WPF reader or C# Script progress/matching.
- Protect real libraries, userdata, backups/recovery copies, navigation guards, media cancellation and browser failure handling. New development requires agreed minimal scope, isolated branch, tests and separate merge authorization.
