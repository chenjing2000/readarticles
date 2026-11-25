# Modular refactoring — temporary implementation plan

## Baseline and scope

Approved architecture discussion: R1, 2026-11-25. Main `41c3cd8`; accepted application baseline `951e9bf` (PR #31); PR #32 documentation only. Preserve `feature/exercise`. One isolated branch and draft PR; separate merge authorization required. Do not modify user libraries, userdata, backups or release behavior. Existing user-visible functionality, file formats, UI, keyboard/focus behavior, media coordination and protocol safeguards remain unchanged.

## Architecture and state

Single application project and existing test project. Concrete feature modules; no DI container, generic repository, event bus, new framework or parallel state store. MainWindow owns native visual behavior and composition. MainViewModel remains the application coordination entry; ReaderWorkspace owns committed article context. LibraryNavigation owns tree scan lifecycle, session and node generations. Reading owns committed Passage/Script mode and cached Script; preserve render-ack transaction. VocabularyEditor applies rules and persists via VocabularyRepository; workspace commits only after success. Audio owns media state and clock, uses necessary vocabulary data, not WebView or settings. Settings owns persisted preferences; Layout owns transient widths and drag baseline. Notifications owns one FIFO. WebReaderHost owns browser lifetime/resources; WebReaderBridge owns validated protocol and snapshots. JS owns DOM, selection, scroll, geometry and presentation copies, not canonical business state.

Use explicit calls for prepare/commit and cross-feature ordering; events report committed changes, not implicit transactions. Preserve page/session/revision/request guards, modal revalidation, source restrictions, stale callbacks, cancellation, backups, atomic persistence and error/recovery semantics. Keep existing repositories, playback controllers, IAudioPlayer and platform adapter unless concrete evidence warrants change.

## Stages and gates

| Stage | Work | Exit evidence |
| --- | --- | --- |
| R1 | State ownership, dependency direction and workflow review; this temporary plan | Approved design, fixed baseline |
| R2 | Extract PaneLayout; remove transient widths from ReaderSettings; keep one C# layout authority; test resize, cancel, settings serialization and actual WPF/browser agreement (Windows CI passed at c0306e7; final local acceptance pending) | Build and targeted layout/settings checks; Core/UI as applicable |
| R3 | Extract LibraryNavigation, separate prepare/commit, move committed Script/mode to workspace; preserve scan guards and failed selection rollback (Windows CI passed at 286136e; final local acceptance pending) | Navigation, content, Script and bridge checks |
| R4 | Consolidate vocabulary edit/save/commit; narrow audio inputs; unify relative seek and accent ownership without changing playback semantics (Windows CI passed at 15649bf; final local acceptance pending) | Content, backups, audio, vocabulary and coordination checks |
| R5 | Extract WebReaderHost and WebReaderBridge; separate native dialogs and protocol from application rules; preserve message schema and resource/CSP restrictions (Windows CI passed at 1fb9fb0; final local acceptance pending) | Bridge, real DOM, failure and lifecycle checks |
| R6 | Native JS ES modules for bridge, reading, vocabulary, playback, settings, layout and notices; update resource allowlist and packaging; migrate tests without exposing global state (code and regression updates prepared; execution pending) | Embedded resource, real DOM and shortcut tests |
| R7 | Remove obsolete code/aliases, organize source by responsibility, align documentation; full validation and portable publish | Full regression, source integrity, published artifact checks, Windows manual acceptance, approved merge |

R2–R7 implementation is incremental. Each stage has a coherent English commit; unexecuted checks remain explicitly pending. No artificial LOC target. Existing UI 73/73, Full 98/98 plus source checks 2/2 and publish checks 4/4 are historical baseline, not new executions. CI Core includes browser DOM checks, not physical keyboard/audio acceptance. Use existing Test-Workflow, Test.ps1 Core/UI/Full, Publish and Test-PublishedApp scripts and isolated fixtures. Retain all meaningful assertions when moving tests to module boundaries.

## Main workflows

- Startup: load settings, compose modules, start browser and restore library independently; send current committed snapshot when ready.
- Open article: prepare passage/companion data, commit, stop previous playback, clear old notifications, refresh projection and publish snapshot; failed preparation keeps old state and tree selection.
- Reading mode: prepare target, render with correlated request, commit on success only; failure keeps previous view and playback.
- Vocabulary: validate current context, prepare candidate, save with fingerprint/backup, commit, refresh pronunciation eligibility and publish snapshot.
- Playback: native and browser inputs share commands; audio supplies authoritative snapshots; browser paints progress.
- Settings: edit detached draft, save before apply, coordinate affected features.
- Exit: save in Closing; invalidate pending requests and scans, detach subscriptions and release media/browser resources.

Consolidate durable architecture into Application-Overview and relevant existing specs in R7, then delete this temporary plan.
