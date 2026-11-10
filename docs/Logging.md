# Runtime logging

ReadArticles writes best-effort diagnostic logs to `userdata/logs/` beside the portable executable. The directory is created only when the first log entry is written.

## Contents

Logs are UTF-8 JSON Lines. Each entry contains a local timestamp, severity, a fixed diagnostic event name, and (when available) the exception type, HRESULT, and method-only stack trace.

The logger deliberately omits exception messages and arbitrary user-provided strings. It must not record article text, vocabulary words or meanings, imported text, or absolute article/audio paths. Event names are fixed code identifiers, not user content.

Events include application start and exit, handled operation failures, article/library warnings, audio availability and playback failures, settings-save failures, and unhandled application exceptions. Unhandled exceptions are recorded but are not automatically marked as handled; the normal runtime termination behavior is preserved.

## Limits

- Active daily log: at most 2 MiB.
- One rollover file per day is retained; when the active file reaches its limit, the previous rollover is replaced.
- Files older than 14 days are removed during a successful write.
- The entire log directory is capped at 20 MiB; oldest files are removed first when necessary.
- Logging I/O and cleanup failures are swallowed. Diagnostics must never interrupt reading, playback, vocabulary editing, or shutdown.

These limits are best-effort when files are locked or the directory is not writable. If logging cannot write, the application continues without a log entry and does not fall back to another data location.

## Cache

`userdata/cache/` is reserved but is not currently created. ReadArticles has no persistent application cache requiring storage or cleanup. If a future feature introduces one, its ownership, size bound, expiry, and safe cleanup behavior must be specified and tested before use.

## Support bundle

When reporting a problem, share the relevant log file(s) from `userdata/logs/` and the application version. Review logs before sharing them. The logger avoids learning content and absolute media paths, but diagnostic method names and exception types are still included.

## Phase 5 events

Backup restore diagnostics use fixed event IDs `BackupRestoreFailed`, `BackupRollbackFailed`, and `RecoveryFilePreserved`. Directory scan failures use fixed warning IDs such as `DirectoryScanFailed` and `DirectoryScanIncomplete`. User-controlled paths and document contents must never be embedded in log event identifiers or arbitrary log messages.
