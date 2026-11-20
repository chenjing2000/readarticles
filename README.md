# ReadArticles

ReadArticles is a local C# / WPF application for English reading, vocabulary editing, and playback of existing MP3 and WAV files. It uses a directory tree, passage pane, and vocabulary pane backed by ordinary JSON files.

## Run

On Windows with the .NET 10 Windows Desktop Runtime (10.0.11 or a later compatible patch) and the Microsoft Edge WebView2 Evergreen Runtime, launch `dist/ReadArticles/ReadArticles.exe`. User settings are stored in `dist/ReadArticles/userdata/config/settings.json` (with backups under `userdata/`); keep the entire application folder together and place it in a location where the current user has write permission. Use the folder-selection button to open a library, or pass `--library` on the command line. Publish first if the runtime folder is absent; generated binaries are not tracked by Git.

Startup loads only a collapsed library tree. It does not restore a passage or vocabulary. Routine successful actions are quiet; informational and warning notifications expire automatically, while unacknowledged errors remain accessible until dismissed. The former bottom status row is removed.

The bottom play/pause, stop, and progress controls own only the first sorted MP3 directly beside the passage JSON, or the first sorted WAV if no MP3 exists. Sentence audio uses `audio_sentences/`; word audio uses `audio/`. Both use derived filenames, with MP3 preferred by existence and WAV used only when MP3 is absent; decoding failure does not retry WAV. JSON audio path fields are ignored on load. Vocabulary saves remove its old fields; passage JSON is not saved by the application, so external preparation tools remove its fields when regenerating content. No old-directory, naming, or index fallback or resource migration is provided. These pronunciations share a second player: starting pronunciation pauses the article, and starting or resuming the article stops pronunciation. Vocabulary editing writes the active companion JSON, so use working copies for experiments.

## Script reading mode

A passage `<name>.json` can optionally have a read-only `<name>.script.json` in the same directory. The music button to the left of the vocabulary-highlighting button loads that companion only when clicked, then toggles between Passage and Script views. Script files do not appear separately in the library tree. Missing or invalid Script companions leave Passage and article audio untouched.

Script JSON requires `"filetype": "script"`, a string `"title"` (empty is allowed), and `"paragraphs"` of sentences containing `"text"` and `"timed_units"`. Each timed unit is `[text, startSeconds, endSeconds]`; a pair of null times means no coloring. No cross-file text matching is required. The script view has no sentence pronunciation, but retains word selection, vocabulary highlights, and whole-article audio. Its `#2475A8` foreground follows the absolute article playback position; Seek/rewind updates it, and stop/end clears it. Source Script and Passage JSON are never rewritten.

## Documentation

Start with the [documentation index](docs/README.md):

- [Library layout and file requirements](docs/Library-Layout.md)
- [Design goals, framework, and modules](docs/Application-Overview.md)
- [Image-to-passage conversion skill](docs/skills/image_to_passage/SKILL.md)
- [Passage organization and segmentation skill](docs/skills/passage_segment/SKILL.md)
- [Vocabulary preparation from structured or plain input](docs/Vocabulary-Preparation.md)
- [Running, publishing, and troubleshooting](docs/Getting-Started.md)
- [Regression categories and manual acceptance](docs/Testing.md)

All maintained documentation is in English. The conversion skills prepare content externally; they are not built-in OCR, enrichment, or speech-generation features.

## Repository structure

| Path | Purpose |
| --- | --- |
| `ReadArticles/` | Published application's source and embedded UI resources |
| `ReadArticles.Tests/` | Categorized checks of production services and controls |
| `tests/TestData/` | Single source of fixed article, vocabulary, and audio test resources |
| `artifacts/` | Test screenshots, reports, and logs |
| `dist/` | Portable application output, including the executable and its `userdata/` directory |
| `docs/` | Current documentation and conversion skills |

## Build and test

Run from the repository root with an SDK compatible with `global.json`:

```powershell
dotnet build ReadArticles -c Release
.\tools\Test.ps1 -Suite Full
```

See the testing guide for the unified test-data layout and Core/UI/Full suites. The current main branch does not implement account registration/login or exercise answering. The supplied authoring skills may still produce exercise companions; the reader excludes those files from navigation and does not render them.

## Unified publishing

Run `.\tools\Publish.ps1` from this repository root. The application uses Portable.pubxml and staged validation. The single EXE and its `userdata/` directory are under `dist/ReadArticles/`. Publication preserves existing userdata without importing obsolete `dist/userdata/` or sibling settings. See [Publishing](docs/Publishing.md), [health check](docs/Health-Check-2026-10-13.md), and [Release notes](docs/Release-Notes.md).


## Unified testing and acceptance

Use `.\tools\Test.ps1 -Suite Core`, `-Suite UI`, or `-Suite Full`. All fixed test inputs come from `tests/TestData/`; temporary copies are isolated under the OS temporary directory. After publishing, run `.\tools\Test-PublishedApp.ps1`. See the [documentation index](docs/README.md) and [test guide](docs/Testing.md). PowerShell source is English-only.


- [Runtime logging](docs/Logging.md)

## Phase 5 reader navigation and notifications

- Reader messages appear as floating notifications at the bottom of the middle reading pane instead of a bottom status row. Errors remain until dismissed, and old errors can be opened through the overflow indicator.
- The left book tree is lazy: opening a library discovers only its immediate book folders; expanding a folder reads only its direct children. Empty intermediary folders remain navigable.
- Collapsing and re-expanding a folder refreshes that level without stopping current reading or playback.
