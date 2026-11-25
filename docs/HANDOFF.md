# ReadArticles — Development handoff (2026-11-25)

## Verified baseline

- Repository: https://github.com/chenjing2000/readarticles; Windows workspace: `C:\Projects\ReadArticles`.
- Remote main remains at `8ae55f2e2a885286daf66c7c8166a29e877add31`, following the rollback of the earlier [PR #34](https://github.com/chenjing2000/readarticles/pull/34) merge. The preceding accepted modular application baseline is `d2e9d895f074f1b115b4ed7fdff66594fc808c96` ([PR #33](https://github.com/chenjing2000/readarticles/pull/33), [post-merge CI](https://github.com/chenjing2000/readarticles/actions/runs/45536173469)).
- Accepted local release candidate: `371e279de6cb8fcffdd2277f2fb2b64cf8596c89` on `feature/excluded-folders`. Application and test sources are frozen at that commit; this handoff update is documentation-only. The candidate is not yet merged; a new PR and successful CI are required before separate user authorization to merge.
- User-provided Windows evidence for the exact candidate: Release build succeeded without warnings or errors; Full 110/110 (including logging 4/4), publishing safeguards 8/8, read-only source 2/2, five Full groups passed without failures or skips; Workflow 3/3; portable publication succeeded; published-app checks 4/4; working tree clean. Full report: `artifacts/reports/Full-20261125-131234.md`. Published executable: `dist/ReadArticles/ReadArticles.exe`. Intermittent Chromium shutdown diagnostic 1412 did not fail the tests.
- User explicitly confirmed 20/20 manual checks on the final published candidate: exclusion UI and parsing, root/nested navigation, immediate collapsed-tree refresh preserving reading/playback, cancellation/defaults/validation, rapid tree/library operations, failed article switching, Passage/Script, vocabulary highlighting/family/selection/editing, duplicate handling, article/UK/US playback, rapid playback target changes, reserved keyboard shortcuts, settings persistence and restart, normal application stability. Destructive configuration and backup recovery scenarios were covered by automated tests rather than manual edits to production settings.
- Preserve the divergent `feature/exercise` branch at `fbca9d26d7c02e8edfba984256a8543897772429`. The old local main may still point to the reverted squash commit `3efe89f`; check actual refs and a clean worktree before aligning it with remote main. Never push that stale commit back to main.

## Completed scope

- Folder exclusions: final settings section “其它”, raw `ExcludedFolders` string (default `audio, cache`), four delimiters, trimmed case-insensitive exact names, empty disables user exclusions, invalid names rejected. Existing hidden/userdata/reparse guards remain; exclusions affect navigation only.
- A: preserve pending library selection during filter changes, invalidate old scans, await refresh, preserve the committed reading workspace, and use the latest filter during failed-switch fallback.
- B: reuse validated exclusion sets and serialized bytes, isolate invalid exclusion values from other settings, preserve valid backups, and validate actual Fluent clear-button visibility.
- C: share normalized vocabulary duplicate checks without rewriting existing entries; reject reparse paths in Passage/Script/vocabulary reads and vocabulary writes/restoration.
- D: remove redundant audio candidate flag, share file validation, compute word audio ownership in one pass, reuse folded browser matching strings, simplify selection payload, correct notification comment.
- E: validate current settings schema consistently, preserve invalid current files during explicit backup restoration without rotating good backups, and test successive filter edits during failed-switch fallback. No additional source changes approved.
- No new source files, classes, framework, runtime dependency, or Passage/Script/vocabulary format. Deferred: R11 native media-event edge cases, R14 low-frequency settings messages, R15 request deduplication growth, R18 concurrent settings writers, R20 publish destination reparse policy, R21 recovery EXE retention, R22 Fluent template dependency. Preserve current safeguards; do not treat these as proven failures.

## Release gate

- Open a new PR from `feature/excluded-folders` to `main`; check the exact PR head and Windows CI conclusion. Do not merge until expressly authorized. Verify application/test tree remains identical to the accepted candidate except documentation. After authorized merge, synchronize main safely, run post-merge verification and update this handoff with actual results. Do not report pending CI or future merge as complete.

## Current development boundaries

- No further implementation batch is approved. After release verification, begin new work from the verified main baseline after checking [project contract](project-contract.md), [architecture](Application-Overview.md), [library format](Library-Layout.md), [testing](Testing.md) and [project review skill](skills/project-code-review/SKILL.md). The [documentation index](README.md) identifies the owner of each detail.
- Retain one WPF application project and one test project, one WebView2 reader, concrete feature modules, explicit MainViewModel coordination and native JavaScript ES modules. Source directories follow feature responsibilities; existing C# namespaces remain stable. No new framework, data format or runtime dependency was introduced. Preserve keyboard, audio, matching, settings, navigation and persistence invariants.
- Retain `UiIcons.Refresh` for Restore Defaults, the small `AudioItem` type and intentional settings-value duplication. Do not reintroduce SID, sentence playback, WPF reader or C# Script progress/matching, or remove code merely to reduce lines.
- Protect real libraries, userdata, backups/recovery copies, navigation guards, media cancellation and browser failure handling. Follow [Testing](Testing.md) and [Publishing](Publishing.md). New development requires an agreed minimal scope, isolated branch, tests and separate merge authorization.
