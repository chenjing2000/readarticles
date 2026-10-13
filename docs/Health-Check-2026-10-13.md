# Health check - 2026-10-13

Scope: publication, settings and resource paths, existing service/WPF checks, and the actual single-file EXE. Environment: Windows x64, .NET SDK 10.0.401, runtime 10.0.12. Commands must run from the repository root.

## Verified

- Publishing safeguards: 5 passed, 0 failed. Tests cover user-file preservation, PDB/XML exclusion, failed builds, unexpected DLLs, and locked executables.
- Full application suite: 62 passed, 12 failed. The failures below require local MP3 fixtures that are absent; the full suite is NOT reported as passing.
- Separately rerun categories: rendering 13/13, tree 1/1, editing 1/1, icons 8/8 passed.
- Isolated EXE copied into a spaced path, renamed, and started from another working directory: main window opens and valid settings follow the EXE name and directory.
- An isolated runtime root confirms the native installation instructions when .NET is absent. The graphical runtime-error dialog was disabled and not visually verified.
- NuGet vulnerability audit, including transitive packages, reported no known vulnerable packages from the configured sources.
- Published EXE: 321,729 bytes, approximately 0.31 MiB. Application assets are embedded; user libraries, settings, and media are external.

## Incomplete audio validation

Local MP3 files are deliberately ignored by Git. The checked-in JSON fixtures are present, but their audio_segments and audio_vocabulary MP3 resources are absent. The documented original local article directory did not supply replacement fixtures during this check. No original library was edited and no checks were disabled to hide failures.

The 12 failed checks are:

- segment audio uses audio_segments with exact SID/accent
- normal operations stay quiet while warnings and errors remain visible
- three-column real content: tree, 13 readers and 120 full vocabulary rows
- real reader: every SID is owned and missing audio reported
- real MP3 segment uk: decode, position, pause/resume and end
- real MP3 vocabulary uk: decode, position and end
- real MP3 segment us: decode, position, pause/resume and end
- real MP3 vocabulary us: decode, position and end
- real MP3 controller clears SID and ownership after completion
- left and right arrows control article progress from every pane and popup
- segment and vocabulary playback never show or respond to bottom progress
- bottom play and stop control only the first sibling MP3

Restore the matching original local MP3 fixture set, then rerun the full suite. These failures do not establish a business-code defect; they leave playback and audio-dependent layout/status behavior unverified. Actual audio was not listened to.

## Code inspection

Settings derive their location from AppContext.BaseDirectory and the actual process name; malformed JSON is backed up before defaults are written. JSON writes use a temporary file, flush, and final replacement; vocabulary external-edit detection and resource traversal rejection are covered by existing passing checks. No business-code or file-format changes were made for publication.

## Reproduction

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tools\Test-Publish.ps1
dotnet run --project ReadArticles.Tests -c Release
dotnet run --project ReadArticles.Tests -c Release -- --rendering-only
dotnet run --project ReadArticles.Tests -c Release -- --tree-only
dotnet run --project ReadArticles.Tests -c Release -- --editing-only
dotnet run --project ReadArticles.Tests -c Release -- --icons-only
dotnet list ReadArticles/ReadArticles.csproj package --vulnerable --include-transitive
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tools\Publish.ps1
```

See [Publishing](Publishing.md) and [Testing](Testing.md). The GitHub Release workflow validates publication and builds the EXE; it does not certify the missing local-media checks.
## Unified testing and acceptance verification

Full: 74 fixed regression checks and 2 current-source checks passed using the supplied article recordings. This supersedes the earlier 12 failures caused by unavailable MP3 fixtures. The fixed 120-word fixture and the current 121-word vocabulary were verified separately; legacy audio paths were normalized only in temporary copies.

Published EXE acceptance passed: isolated EXE-only folder with spaces, renamed executable, a different working directory, correct configuration placement, clean shutdown, and native missing-.NET installation instructions under an isolated runtime root. Installed runtimes and original media were not modified. This verifies automated behavior; human listening and a fresh-machine graphical runtime prompt were not performed.

The orchestration regression passed success, failure rejection, and optional-data handling checks. PowerShell source was verified as English ASCII with no syntax errors. Use [Testing](Testing.md) for the shared entry points; stage counts and individual case counts are reported separately.
