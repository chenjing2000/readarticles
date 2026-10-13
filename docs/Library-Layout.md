# Library Layout and File Requirements

## 1. Select the library root

Select the directory containing your book directories, rather than a book directory itself. Only immediate child directories containing `book.json` are treated as books. Each recognized book is scanned recursively.

Recommended layout:

```text
Library/                              <- select this directory
  The Economist/                      <- a book directory
    book.json                         <- required marker
    2026-09-26/                        <- optional grouping directory
      When America walks away/        <- optional article directory
        When America walks away.json
        When America walks away.vocabulary.json
        article.mp3
        audio_segments/
          s001_uk.mp3
          s001_us.mp3
        audio_vocabulary/
          be_stuck_in_uk.mp3
          be_stuck_in_us.mp3
    userdata/                         <- optional; excluded from navigation
      xiaoxin/
        answer_sheet.json
```

Date and article directories are conventions, not requirements. A passage may sit directly in the book directory or deeper within it. Separate article directories are useful because all passages sharing a directory also share its whole-article MP3 selection and relative audio locations.

Opening a library reads directory names and discovers potential passage filenames. It does not parse passage or vocabulary JSON, open an article, or start audio. The tree starts collapsed. Expanding opens one level; collapsing a node recursively collapses its descendants.

## 2. Required and optional files

| File or directory | Requirement | Location | Current behavior |
| --- | --- | --- | --- |
| Book directory | Required for normal library navigation | Immediate child of the selected root | Recognized only when it contains `book.json` |
| `book.json` | Required marker for each book | Book directory itself | Only its existence is checked; its contents are not parsed |
| `<title>.json` | Required to read a passage | Anywhere inside a recognized book, outside excluded branches | Parsed only when selected; must have `filetype: "passage"` |
| `<title>.vocabulary.json` | Optional | Same directory and same filename stem as the passage | Automatically loaded with the passage; missing file gives an empty, writable word list |
| `audio_segments/` and its MP3 files | Optional physical files | Passage directory | Complete passages must declare these paths even when the files do not exist; missing files produce a warning without blocking reading |
| `audio_vocabulary/` and its MP3 files | Optional physical files | Passage directory | Vocabulary entries must declare the correct paths; missing or empty files disable the corresponding pronunciation buttons |
| One whole-article `.mp3` | Optional | Directly beside the passage JSON | Used only by the central bottom play/pause button |
| `userdata/xiaoxin/answer_sheet.json` | Optional | Relative to the book directory | Only `username` is read for the account label; authentication and exercise answering are not implemented |
| `<title>.exercise.json` | Optional authoring companion | Beside the passage | Excluded from navigation and not read or displayed by the main-branch reader |
| Images, PDFs, text, and other non-JSON source files | Optional stored material | Any convenient location | Not opened by the reader and not shown as passage leaves |

A minimal marker can contain:

```json
{}
```

An empty book with no discoverable passage filenames is omitted and reported as a warning. JSON validity is not checked during discovery, so a malformed potential passage can appear in the tree and fail only when selected.

## 3. Passage and vocabulary naming

The passage filename supplies the title displayed in the center pane. A JSON `title` field is not used.

For a passage named `Example.json`, the automatically loaded companion is exactly `Example.vocabulary.json`. The application does not search for an arbitrary vocabulary file in the same folder.

A standalone file named `vocabulary.json` can be selected through the vocabulary import button, but it is not automatically paired with a passage. Keep such input files outside scanned book branches, or rename the final file to the matching companion name. Otherwise `vocabulary.json` may appear as a potential passage and fail the passage `filetype` check.

Import replaces the active article's word list and saves the result to its matching companion file; it is not a merge operation. Moving or deleting words also saves that companion. If the file has changed externally since loading, the save is rejected: reopen the article before editing again. Malformed vocabulary does not block passage reading, but overwriting that malformed companion is disabled until it is repaired and reopened.

See the [passage skill](skills/skills-python/passage_segment/SKILL.md) and [vocabulary skill](Vocabulary-Preparation.md) for the exact JSON structures.

## 4. Three independent audio lookups

### Whole-article playback

The central bottom play/pause button searches only the passage JSON's own directory for files whose extension is `.mp3`, case-insensitively. It sorts filenames using ordinal, case-insensitive comparison, with ordinal comparison as a tie-breaker, and plays the first file. A matching title is not required.

For example, `a-reading.MP3` is selected before `z-reading.mp3`. Nested MP3 files are ignored. There is no fallback to `audio_segments/`, no concatenation, and no automatic move to the next sibling MP3. Accent selection does not change this file.

If no sibling MP3 exists, an error is shown. Existing word or sentence playback is left running. If an MP3 exists but is empty or cannot be decoded, playback reports the failure; the reader does not search for the next file.

### Sentence playback

Right-clicking passage text requests the sentence identified by its SID, using:

```text
audio_segments/<sid>_uk.mp3
audio_segments/<sid>_us.mp3
```

The selected British or American accent determines the path. The old `audio/` directory is not a fallback. Blank passages have no sentence audio declarations and do not offer sentence playback.

### Vocabulary playback

Each word has its own British and American pronunciation buttons. Paths must use:

```text
audio_vocabulary/<stem>_uk.mp3
audio_vocabulary/<stem>_us.mp3
```

The exact stem algorithm is defined in the vocabulary skill. Whole-article audio has its own player; sentence and vocabulary audio share a second player. Starting pronunciation pauses the article while preserving its position. Starting or resuming the article stops pronunciation. The bottom stop button stops only the article audio; closing the window or opening another article stops both players.

## 5. Excluded branches and safe paths

The scanner excludes hidden files/directories, names beginning with `.`, directories named `userdata` or `__pycache__` regardless of letter case, and reparse points such as junctions or symbolic links. Inside a book, it excludes `book.json`, `*.vocabulary.json`, and `*.exercise.json` from passage leaves.

Other JSON files are treated as potential passages without inspecting their contents. Keep unrelated JSON outside scanned book branches.

Audio references are relative to the passage directory. Absolute paths, drive-qualified references, colon-containing references, paths escaping that directory, and paths traversing reparse points are rejected. Use the exact forward-slash paths specified by the format; do not substitute `audio/`, absolute paths, or arbitrary audio filenames.

## 6. Files outside the library

`dist/ReadArticles.settings.json` belongs beside the executable, not in a book. It stores application preferences and is initialized on first startup. The single-file release contains all application assemblies; distribute only `dist/ReadArticles.exe`, without the .NET runtime. Keep the existing settings file during updates.

Source images and the original Python ZIP are preparation/reference material. ReadArticles does not convert them when a library is opened.
