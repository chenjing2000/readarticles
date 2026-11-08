# Testing and acceptance

Run from the repository root on Windows with the .NET 10 SDK. Scripts support Windows PowerShell 5.1 and PowerShell 7. If unsigned scripts are blocked by the machine policy, use a process-scoped bypass; no permanent execution-policy change is required.

## Unified entry points

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\\tools\\Test.ps1 -Suite Core
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\\tools\\Test.ps1 -Suite UI
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\\tools\\Test.ps1 -Suite Full
```

All fixed test inputs come from `tests/TestData/`. The runner copies the article and vocabulary JSON into a unique temporary workspace, generates one shared pool of deterministic PCM WAV files, and sets `READARTICLES_TEST_LIBRARY` and `READARTICLES_TEST_ARTICLE` for child test processes. The source fixtures are never modified.

## Suites

- **Core** runs content, matching, rendering, tree-navigation, and vocabulary-editing regression categories.
- **UI** runs Core plus icons, reader windows, and reader-control interaction checks. It requires an interactive Windows desktop session.
- **Full** runs the complete application regression suite, media decoding/playback checks, and a read-only source-fixture integrity check.

A failed native command or assertion makes the entry point return a nonzero exit code. Individual test cases and grouped checks are reported separately. A skipped check is not a pass.

## Test data layout

```text
tests/TestData/
├── Library/                 # Fixed article and vocabulary JSON
│   └── The Economist/
└── Audio/
    └── README.md             # Generated audio-pool rules
```

The main article contains 13 paragraphs, 62 stable SIDs, and 120 vocabulary entries. The test runner generates UK and US WAV files for all sentence SIDs except `s011`, and for every distinct vocabulary filename stem. The missing `s011_uk` and `s011_us` files are intentional: loading an article must not scan or report missing sentence recordings; only attempting to play `s011` should report the selected sentence's missing recording.

Generated WAVs are short synthetic tones, not speech and not pronunciation content. They exercise filename resolution, media decoding, position, pause/resume, and completion. Tests that specifically exercise MP3 priority or invalid MP3 handling create their own temporary files. No separate WAV-only or missing-audio fixture tree is maintained.

## Outputs and cleanup

Screenshots, reports, and transcript logs are written under `artifacts/`: `screenshots/`, `reports/`, and `logs/`. Audio copies and scratch files are kept under the OS temporary directory. The runner restores environment variables and removes only the unique temporary directory it created. The source test data is never modified.

## Published application acceptance

After publishing, run:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\\tools\\Test-PublishedApp.ps1
```

This check validates the published EXE in an isolated path. It does not replace the Core, UI, or Full regression suites.
