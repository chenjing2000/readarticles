# ReadArticles Standardization Implementation Plan

Status: phases 1–3 merged; phase 4 runtime logging is implemented on `feature/runtime-logging` and awaiting acceptance.

## Goals

- Keep the application framework-dependent, single-file, Windows x64, and WPF.
- Separate application binaries from user data in a portable deployment folder.
- Protect user settings and vocabulary data with explicit backup and recovery.
- Preserve the external article library as user-owned data.
- Keep publishing staged and recoverable, and add lightweight diagnostics.

## Agreed deployment layout

```text
ReadArticles/
  ReadArticles.exe
  README.txt
  userdata/
    config/settings.json
    backups/settings/
    backups/vocabulary/
    cache/
    logs/
```

`userdata` is beside the executable. The whole application folder must be placed in a user-writable location. Do not silently redirect data to `%LOCALAPPDATA%`. Create subdirectories on demand. Do not create `runtimes/` unless an app-private runtime is actually introduced.

## Data ownership

- Passage/article JSON and audio files are read-only to ReadArticles.
- The companion vocabulary JSON is application-managed and must retain its current format, metadata handling, external-change detection, and atomic-save behavior.
- The selected article library remains external and is never moved into the application folder.

## Backup policy

- Settings: exactly one latest valid pre-save backup.
- Vocabulary: exactly one latest pre-save backup per source file. Identify a source by a stable SHA-256 derived from its normalized full path so same-named files in different folders cannot collide.
- Before replacing a vocabulary source, verify that its backup is complete and matches the current source. If backup creation or verification fails, abort the source replacement.
- Restore must preserve the current file before replacement and must never modify passage JSON.
- Cache is disposable and is not backed up. Logs are retained by age and size policy.

## Phases

### Phase 1 — User-data paths and settings migration (P0) — Complete

- Introduce one path service for application, userdata, config, backups, cache, and logs.
- Store settings at `userdata/config/settings.json`.
- Migrate the legacy executable-sibling `<process-name>.settings.json` without deleting the source.
- If both legacy and current settings exist and differ, prefer the current file, preserve both, and warn; never silently discard either file.
- On access/permission failures, clearly instruct the user to move the whole folder to a writable location. Do not use `%LOCALAPPDATA%` as a fallback.
- Add migration, conflict, corruption, and path tests.

### Phase 2 — Backup and recovery (P0) — Complete

- Settings: retain exactly one latest valid pre-save backup.
- Vocabulary: retain exactly one latest valid pre-save backup per source file.
- Use verified temporary copies and atomic replacement; abort source replacement if backup creation or verification fails.
- Validate JSON before restoring settings or vocabulary.
- During restore, preserve the current file as the new latest backup before replacing it with the previous backup.
- Test failed backup, external edits, interrupted writes, same-name path collisions, and restore behavior.

### Phase 3 — Portable publishing (P0) — Complete

- Publish to `dist/ReadArticles/` with the executable and userdata kept separate. Preserve legacy `dist/userdata/` when copying it to the new layout; do not delete user originals automatically.
- Replace only application binaries; preserve userdata and arbitrary user files.
- Retain staging validation and old-version protection.
- Test first publish, update, locked executable, failed build, interruption, and portable-folder relocation.

### Phase 4 — Runtime logs (P1) — Implemented; acceptance pending

- Add lightweight daily JSON-lines logs under `userdata/logs/`.
- Record application start/exit, handled operation failures, library/article warnings, playback failures, settings-save failures, and unhandled exceptions.
- Do not record exception messages, article text, vocabulary words or meanings, or absolute media paths. Exception type, HRESULT, and method-only stack frames are retained for diagnosis.
- Keep each daily log file at or below 2 MiB, retain at most 14 days, and cap the log directory at 20 MiB. A single rollover file per day is allowed.
- Logging failures are swallowed and must not crash core reading or playback.
- No application cache currently requires persistence; `userdata/cache/` remains reserved and is not created until a real cache feature needs it.

### Phase 5 — Documentation and UI standardization (P1/P2)

- Complete the project contract, README, publishing and recovery guidance, and repository agent instructions.
- Remove machine-specific absolute references from `AGENTS.md`.
- Gradually consolidate UI design resources and manually verify DPI, multiple monitors, high contrast, and theme behavior.

## Workflow and acceptance

- Implement one phase per feature branch from the latest `main`.
- Do not start a dependent phase until the current phase passes its acceptance checks and is merged.
- Preserve the existing Core, UI, Full, publishing, and source-integrity tests; add tests rather than weakening assertions.
- Run Core in hosted Windows CI. Run UI and Full on an interactive local Windows desktop.
- Use English commit messages and squash-merge reviewed pull requests.
- Any conflict between this plan and the actual code must be reported before broadening the change.

## Current implementation note

Phases 1–3 are merged into `main`. Phase 4 is implemented on `feature/runtime-logging` and requires hosted Core CI plus local UI/Full acceptance before merge. Phase 5 remains pending.