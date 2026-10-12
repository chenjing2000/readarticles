# ReadArticles

ReadArticles is a local C# / WPF application for English reading, vocabulary editing, and playback of existing MP3 files. It uses a directory tree, passage pane, and vocabulary pane backed by ordinary JSON files.

## Run

On Windows with the .NET 10 Windows Desktop Runtime (10.0.11 or a later compatible patch), launch `dist/ReadArticles.exe`. Use the folder-selection button to open a library, or pass `--library` on the command line. Publish first if the runtime folder is absent; generated binaries are not tracked by Git.

Startup loads only a collapsed library tree. It does not restore a passage or vocabulary. Routine successful actions are quiet; warnings and errors appear for six seconds, and the empty status row takes no space.

The bottom play/pause, stop, and progress controls own only the alphabetically first MP3 directly beside the passage JSON. Sentence audio uses `audio_segments/`; word audio uses `audio_vocabulary/`. These pronunciations share a second player: starting pronunciation pauses the article, and starting or resuming the article stops pronunciation. Vocabulary editing writes the active companion JSON, so use working copies for experiments.

## Documentation

Start with the [documentation index](docs/README.md):

- [Library layout and file requirements](docs/Library-Layout.md)
- [Design goals, framework, and modules](docs/Application-Overview.md)
- [Image-to-passage conversion skill](docs/skills/skills-python/image_to_passage/SKILL.md)
- [Passage organization and segmentation skill](docs/skills/skills-python/passage_segment/SKILL.md)
- [Vocabulary preparation from structured or plain input](docs/Vocabulary-Preparation.md)
- [Running, publishing, and troubleshooting](docs/Getting-Started.md)
- [Regression categories and manual acceptance](docs/Testing.md)

All maintained documentation is in English. The conversion skills prepare content externally; they are not built-in OCR, enrichment, or speech-generation features.

## Repository structure

| Path | Purpose |
| --- | --- |
| `ReadArticles/` | Published application's source and embedded UI resources |
| `ReadArticles.Tests/` | Categorized checks of production services and controls, with fixed fixtures |
| `dist/` | Runtime files and user settings; keep these together |
| `docs/` | Current documentation and conversion skills |

## Build and test

Run from the repository root with an SDK compatible with `global.json`:

```powershell
dotnet build ReadArticles -c Release
dotnet run --project ReadArticles.Tests -c Release
```

See the testing guide for independent categories and optional machine-specific source checks. The current main branch does not implement account registration/login or exercise answering. The supplied authoring skills may still produce exercise companions; the reader excludes those files from navigation and does not render them.
