# ReadArticles Standardization Implementation Plan

Status: approved plan; implementation proceeds in independently tested phases.

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

- Settings: automatic backups with a bounded history (initial target: five valid versions).
- Vocabulary: exactly one latest pre-save backup per source file. Identify a source by a stable SHA-256 derived from its normalized full path so same-named files in different folders cannot collide.
- Before replacing a vocabulary source, verify that its backup is complete and matches the current source. If backup creation or verification fails, abort the source replacement.
- Restore must preserve the current file before replacement and must never modify passage JSON.
- Cache is disposable and is not backed up. Logs are retained by age and size policy.

## Phases

### Phase 1 — User-data paths and settings migration (P0)

- Introduce one path service for application, userdata, config, backups, cache, and logs.
- Store settings at `userdata/config/settings.json`.
- Migrate the legacy executable-sibling `<process-name>.settings.json` without deleting the source.
- If both legacy and current settings exist and differ, prefer the current file, preserve both, and warn; never silently discard either file.
- On access/permission failures, clearly instruct the user to move the whole folder to a writable location. Do not use `%LOCALAPPDATA%` as a fallback.
- Add migration, conflict, corruption, and path tests.

### Phase 2 — Backup and recovery (P0)

- Add settings backup history and recovery.
- Add one-version-per-source vocabulary backup and recovery.
- Test failed backup, external edits, interrupted writes, collisions, and recovery.

### Phase 3 — Portable publishing (P0)

- Publish to `dist/ReadArticles/` with the executable and userdata kept separate.
- Replace only application binaries; preserve userdata and arbitrary user files.
- Retain staging validation and old-version protection.
- Test first publish, update, locked executable, failed build, interruption, and portable-folder relocation.

### Phase 4 — Runtime logs (P1)

- Add lightweight daily logs under `userdata/logs/`.
- Record operational events and actionable errors without recording article text or vocabulary meanings.
- Apply file-size and retention limits. Logging failures must not crash core reading or playback.

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

Phase 1 is the first implementation target. Later-phase directories and behaviors must not be documented as already implemented until their code and tests have landed.