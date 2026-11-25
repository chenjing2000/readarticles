# ReadArticles — Development handoff (2026-11-25)

## Verified baseline

- Repository: https://github.com/chenjing2000/readarticles; Windows workspace: `C:\Projects\ReadArticles`.
- Accepted main application baseline: `32ae70859a08daf0d2b0037e0fd67d8ef11858c2`, squash merge of [PR #35](https://github.com/chenjing2000/readarticles/pull/35). Its Git tree `6651a21e3ffdc993d989e2065bdc8aa6b20ee4b4` exactly matches the accepted PR head `e120535155b2c08a69b7191ac76f65b1ca60a845`. [PR CI](https://github.com/chenjing2000/readarticles/actions/runs/45552687966) and [post-merge main CI](https://github.com/chenjing2000/readarticles/actions/runs/45553643018) both passed. This final handoff update changes documentation only.
- Accepted pre-merge application/test candidate: `371e279de6cb8fcffdd2277f2fb2b64cf8596c89`. Windows Release build succeeded without warnings or errors; Full 110/110 (including logging 4/4), publishing safeguards 8/8, read-only source 2/2, Workflow 3/3, portable publication succeeded, PublishedApp 4/4, clean worktree. User confirmed 20/20 manual checks on the published candidate covering exclusion settings/navigation, preservation of reading and playback, Passage/Script, vocabulary matching/editing, audio, keyboard, persistence, restart and stability. Fault injection used automated isolated tests.
- Post-merge Windows main evidence: local main was safely aligned to origin/main at `32ae708`; Full 110/110 (including logging 4/4), publishing safeguards 8/8, read-only source 2/2, five Full groups passed with zero failures/skips; portable publication succeeded; PublishedApp 4/4; worktree clean. Report: `artifacts/reports/Full-20261125-132750.md`. Published executable: `dist/ReadArticles/ReadArticles.exe`. Intermittent Chromium shutdown diagnostic 1412 did not fail either run. Workflow and manual counts refer to the pre-merge candidate, not a new post-merge run.
- Preserve the divergent `feature/exercise` branch at `fbca9d26d7c02e8edfba984256a8543897772429`. The completed `feature/excluded-folders` branch is eligible for cleanup only at the verified remote tip `e120535` and local tip `371e279`; both are represented by the squash-merged main content. Never push the reverted PR #34 squash commit `3efe89f` back to main. Verify actual refs before deleting any branch.

## Completed scope

- Folder exclusions: final settings section “其它”, raw `ExcludedFolders` string (default `audio, cache`), four delimiters, trimmed case-insensitive exact names, empty disables user exclusions, invalid names rejected. Existing hidden/userdata/reparse guards remain; exclusions affect navigation only.
- A: preserve pending library selection during filter changes, invalidate old scans, await refresh, preserve the committed reading workspace, and use the latest filter during failed-switch fallback.
- B: reuse validated exclusion sets and serialized bytes, isolate invalid exclusion values from other settings, preserve valid backups, and validate actual Fluent clear-button visibility.
- C: share normalized vocabulary duplicate checks without rewriting existing entries; reject reparse paths in Passage/Script/vocabulary reads and vocabulary writes/restoration.
- D: remove redundant audio candidate flag, share file validation, compute word audio ownership in one pass, reuse folded browser matching strings, simplify selection payload, correct notification comment.
- E: validate current settings schema consistently, preserve invalid current files during explicit backup restoration without rotating good backups, and test successive filter edits during failed-switch fallback. No additional source changes approved.
- No new source files, classes, framework, runtime dependency, or Passage/Script/vocabulary format. Deferred: R11 native media-event edge cases, R14 low-frequency settings messages, R15 request deduplication growth, R18 concurrent settings writers, R20 publish destination reparse policy, R21 recovery EXE retention, R22 Fluent template dependency. Preserve current safeguards; do not treat these as proven failures.

## Release status

- PR #35 is merged and post-merge verification is complete. No further implementation batch is approved. Synchronize the documentation-only handoff commit and safely remove the completed feature branch if still present; preserve feature/exercise and all user data. Start new work from verified main with an agreed scope.

## Current development boundaries

- No further implementation batch is approved. After release verification, begin new work from the verified main baseline after checking [project contract](project-contract.md), [architecture](Application-Overview.md), [library format](Library-Layout.md), [testing](Testing.md) and [project review skill](skills/project-code-review/SKILL.md). The [documentation index](README.md) identifies the owner of each detail.
- Retain one WPF application project and one test project, one WebView2 reader, concrete feature modules, explicit MainViewModel coordination and native JavaScript ES modules. Source directories follow feature responsibilities; existing C# namespaces remain stable. No new framework, data format or runtime dependency was introduced. Preserve keyboard, audio, matching, settings, navigation and persistence invariants.
- Retain `UiIcons.Refresh` for Restore Defaults, the small `AudioItem` type and intentional settings-value duplication. Do not reintroduce SID, sentence playback, WPF reader or C# Script progress/matching, or remove code merely to reduce lines.
- Protect real libraries, userdata, backups/recovery copies, navigation guards, media cancellation and browser failure handling. Follow [Testing](Testing.md) and [Publishing](Publishing.md). New development requires an agreed minimal scope, isolated branch, tests and separate merge authorization.
