# ReadArticles

ReadArticles is a local C# / WPF application for English reading, vocabulary editing, and playback of existing MP3 and WAV files. It uses a directory tree, passage pane, and vocabulary pane backed by ordinary JSON files.

## Run

On Windows with the .NET 10 Windows Desktop Runtime (10.0.11 or a later compatible patch), launch `dist/ReadArticles.exe`. Use the folder-selection button to open a library, or pass `--library` on the command line. Publish first if the runtime folder is absent; generated binaries are not tracked by Git.

## Documentation

Start with the [documentation index](docs/README.md). See [Testing](docs/Testing.md) for the unified test workflow.

## Repository structure

| Path | Purpose |
| --- | --- |
| `ReadArticles/` | Published application's source and embedded UI resources |
| `ReadArticles.Tests/` | Categorized regression checks |
| `tests/TestData/` | Single source of fixed test article and vocabulary JSON |
| `tools/` | Build, publishing, and test automation |
| `artifacts/` | Local test reports, logs, and screenshots; not committed |
| `dist/` | Published runtime files; not committed |

## Build and test

Run from the repository root on Windows with the .NET 10 SDK:

```powershell
dotnet build ReadArticles -c Release
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\\tools\\Test.ps1 -Suite Core
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\\tools\\Test.ps1 -Suite UI
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\\tools\\Test.ps1 -Suite Full
```

All fixed test inputs live in `tests/TestData/`. The test runner generates synthetic WAV audio in an isolated temporary workspace; no personal article directory or external recording collection is required. See [Testing and acceptance](docs/Testing.md) for suite coverage and limitations.
