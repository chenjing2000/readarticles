# ReadArticles

ReadArticles is a local Windows application for English reading, vocabulary editing and listening to existing recordings. A native WPF library tree and one WebView2 reader display Passage/Script text, vocabulary, highlighting and whole-article playback from ordinary JSON and audio files.

## Run

Use Windows x64 with the .NET 10 Windows Desktop Runtime (10.0.11 or a later compatible patch) and Microsoft Edge WebView2 Evergreen Runtime. Launch `dist/ReadArticles/ReadArticles.exe` from a writable portable folder; generated binaries are not tracked by Git.

Select the parent library containing book directories marked by `book.json`, or pass `--library` with the library path. Startup restores only the library, not a previous article. Keep the executable's adjacent `userdata/` directory during updates; the external library remains in its original location.

The reader supports Passage JSON, optional same-stem Script and vocabulary companions, existing M4A/MP3/WAV whole-article recordings, and UK/US word pronunciation. Unmodified Space, Left and Right exclusively control whole-article playback throughout the main window. Vocabulary edits save the active companion; use a working copy for experiments. OCR, vocabulary enrichment and audio generation are external workflows. No account, exercise-answering or sentence-audio interface is implemented.

See the [User Guide](docs/User-Guide.md) for operation and [Library Layout](docs/Library-Layout.md) for supported files.

## Build, test and publish

From the repository root on Windows with an SDK compatible with [global.json](global.json):

```powershell
dotnet build ReadArticles -c Release
.\tools\Test.ps1 -Suite Full
# Close the running application before publishing.
.\tools\Publish.ps1
.\tools\Test-PublishedApp.ps1
```

Follow [Testing](docs/Testing.md) and [Publishing](docs/Publishing.md) for isolated fixtures, other test suites, manual acceptance and recovery safeguards.

## Repository structure

| Path | Purpose |
| --- | --- |
| `ReadArticles/` | Application source and embedded UI assets |
| `ReadArticles.Tests/` | Production-service and control regression checks |
| `tests/TestData/` | Fixed article, vocabulary and audio test resources |
| `tools/` | Build verification, test and publishing scripts |
| `artifacts/` | Local test outputs |
| `dist/` | Local portable release and its userdata |
| `docs/` | Current documentation and external content-preparation skills |

## Documentation

Start at the [documentation index](docs/README.md). Developers should read [AGENTS.md](AGENTS.md), the [project contract](docs/project-contract.md) and the [current handoff](docs/HANDOFF.md) before making changes.
