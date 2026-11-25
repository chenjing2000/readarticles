# ReadArticles — Development handoff (2026-11-25)

## Verified baseline

- Repository: https://github.com/chenjing2000/readarticles; Windows workspace: `C:\Projects\ReadArticles`.
- Main before this delivery: `edee4e46811f419bc923aecbb43c0d2399adb0ff` (documentation only after accepted PR #35 squash `32ae70859a08daf0d2b0037e0fd67d8ef11858c2`). [Previous main CI](https://github.com/chenjing2000/readarticles/actions/runs/45553643018) passed; local post-merge Full 110/110 and PublishedApp 4/4 passed.
- Accepted main application baseline: `891cba0c409c8d7a083b6de4dace23990b15b86f`, squash merge of [PR #36](https://github.com/chenjing2000/readarticles/pull/36). Git tree `cb5265cda55035c5f384f921ace2ddcb0700a90c` exactly matches PR head `6668f2539bc0d38416f545c99ccc919d999c3a28`. [PR CI #282](https://github.com/chenjing2000/readarticles/actions/runs/45559003032) and [main CI #283](https://github.com/chenjing2000/readarticles/actions/runs/45559437311) completed successfully.
- Post-merge local main verification at 891cba0: fast-forward alignment, Full 111/111 including logging 4/4, publishing safeguards 10/10, read-only source 2/2, five groups passed, zero failures/skips, actual portable publication and PublishedApp 4/4 passed, clean worktree. Report: `artifacts/reports/Full-20261125-141940.md`. Workflow and manual acceptance counts below refer to pre-merge candidates, not fresh post-merge runs.
- Candidate branch: `feature/refresh-and-icons`. Verified application source `4f2fb469cce77b1f9ff834a0ecdefe598a148f7b`; accepted application/test candidate `84c9ac9345bb19035a48422ac4438dd535fdfb79` changes only one DOM color assertion relative to that source. This handoff update changes documentation only.
- Windows evidence: Release build succeeded at 4f2fb46; initial Full 110/111 failed on a stale RGB expectation for Script purple. Corrected only the assertion at 84c9ac9; Full 111/111 (including logging 4/4), publishing safeguards 10/10, source read-only 2/2, all five groups passed with zero failures/skips, clean worktree. Report: `artifacts/reports/Full-20261125-140854.md`. Workflow 3/3, actual portable publish and PublishedApp 4/4 passed at 4f2fb46; the executable `dist/ReadArticles/ReadArticles.exe` is unchanged by the test-only fix. Intermittent Chromium shutdown diagnostic 1412 was non-fatal.
- User confirmed initial 18/18 manual checks at 914a2e2 (refresh and icons) and final 17/17 manual checks on the application corresponding to 84c9ac9 (five Script colors and persistence, playback/paused seek, Script progress, splitter, refresh/exclusions, vocabulary and settings). No independent manual ACL-denial or deletion-during-scan verification or quantified performance benchmark was reported.
- Preserve divergent `feature/exercise` at `fbca9d26d7c02e8edfba984256a8543897772429`. Do not restore reverted PR #34. Verify refs before branch cleanup.

## Completed scope

- Explicit refresh button left of settings: reopen current library, reset article/vocabulary/mode and both audio channels, rebuild collapsed navigation with current exclusions. Missing root clears selection and persisted path; other access failures retain path for retry. Failed selection of another root retains the prior workspace. Exclusion edits refresh navigation only.
- Replace all 17 functional SVGs with AudioPlayer-style circles (#D65B45 background, white foreground); keep existing resource names, display sizes, application maple-leaf PNG/ICO and data formats. Warm hover/pressed colors.
- A (bb88530): isolate root discovery exceptions from commit callbacks, reject missing vocabulary import fingerprints, independently recover invalid exclusion JSON types, expand existing regression assertions.
- B (a4d5c57): reuse verified backup copying and unique corrupt settings names; retain fingerprint checks and rescue copies.
- C (ba81124): skip durable dedup tracking for absolute live drag frames, cache Script geometry widths and skip unchanged SVG writes, stop periodic progress updates when paused; existing native/browser seeks publish immediately.
- D (a8d4865): reject reparse points in publish target/staging and ancestors, verify embedded SVGs, test linked dist and release directories.
- E (4f2fb46): replace Script progress palette with #E02020 (default), #0066E6, #008A48, #8A36D1, #D65A00. Old palette values normalize to default. 84c9ac9 corrects remaining RGB assertion.
- No new source files, classes, framework, runtime dependency or data format. Deferred risks: native media-event identity, concurrent settings writers, remaining low-frequency request dedup growth, repeated native stops, root scan workload, recovery EXE retention and Fluent template reliance. Do not treat these as proven failures or expand scope without evidence.

## Release status

- PR #36 was explicitly authorized and squash-merged. Main CI and local post-merge verification passed. This final handoff update is documentation only.
- Branch deletion remains unauthorized. Remote `feature/refresh-and-icons` at `6668f25` and reported local tip `84c9ac9` are retained; both are represented in merged main. Only clean up after separate authorization and ref verification. Preserve `feature/exercise` and all user data.

## Current development boundaries
- No further implementation batch is approved. After authorized merge and release verification, begin new work from the verified main baseline after checking [project contract](project-contract.md), [architecture](Application-Overview.md), [library format](Library-Layout.md), [testing](Testing.md) and [project review skill](skills/project-code-review/SKILL.md). The [documentation index](README.md) identifies the owner of each detail.
- Retain one WPF application project and one test project, one WebView2 reader, concrete feature modules, explicit MainViewModel coordination and native JavaScript ES modules. Source directories follow feature responsibilities; existing C# namespaces remain stable. No new framework, data format or runtime dependency was introduced. Preserve keyboard, audio, matching, settings, navigation and persistence invariants.
- Retain `UiIcons.Refresh` for library refresh and Restore Defaults, the small `AudioItem` type and intentional settings-value duplication. Do not reintroduce SID, sentence playback, WPF reader or C# Script progress/matching, or remove code merely to reduce lines.
- Protect real libraries, userdata, backups/recovery copies, navigation guards, media cancellation and browser failure handling. Follow [Testing](Testing.md) and [Publishing](Publishing.md). New development requires an agreed minimal scope, isolated branch, tests and separate merge authorization.
