# Library Layout and File Requirements

## 1. Select the library root

Select the directory containing your book directories, rather than a book directory itself. Only immediate child directories containing `book.json` are treated as books. Book contents are discovered one directory level at a time as users expand the tree.

Recommended layout:

```text
Library/                              <- select this directory
  The Economist/                      <- a book directory
    book.json                         <- required marker
    2026-09-26/                        <- optional grouping directory
      When America walks away/        <- optional article directory
        When America walks away.json
        When America walks away.vocabulary.json
        article.m4a
        audio/
          be_stuck_in_uk.mp3
          be_stuck_in_us.mp3
    userdata/                         <- optional; excluded from navigation
      xiaoxin/
        answer_sheet.json
```

Date and article directories are conventions, not requirements. A passage may sit directly in the book directory or deeper within it. Separate article directories are useful because all passages sharing a directory also share its whole-article audio selection and relative audio locations.

Opening a library discovers only immediate book directories and checks for `book.json`; it does not enumerate passages inside books, parse passage or vocabulary JSON, open an article, or start audio. The tree starts collapsed. Each expansion enumerates one directory level, and collapsing a node also collapses its descendants.

## 2. Required and optional files

| File or directory | Requirement | Location | Current behavior |
| --- | --- | --- | --- |
| Book directory | Required for normal library navigation | Immediate child of the selected root | Recognized only when it contains `book.json` |
| `book.json` | Required marker for each book | Book directory itself | Only its existence is checked; its contents are not parsed |
| `<title>.json` | Required to read a passage | Anywhere inside a recognized book, outside excluded branches | Parsed only when selected; must have `filetype: "passage"` |
| `<title>.vocabulary.json` | Optional | Same directory and same filename stem as the passage | Automatically loaded with the passage; missing file gives an empty, writable word list |
| `audio/` and its MP3/WAV files | Optional physical files | Passage directory | Names derived from word stem; missing or empty files disable pronunciation buttons; later stem collisions have no audio eligibility |
| One whole-article `.m4a`, `.mp3` or `.wav` | Optional | Directly beside the passage JSON | Used only by the central bottom play/pause button |
| `userdata/xiaoxin/answer_sheet.json` | Optional | Relative to the book directory | Only `username` is read for the account label; authentication and exercise answering are not implemented |
| `<title>.exercise.json` | Optional authoring companion | Beside the passage | Excluded from navigation and not read or displayed by the current reader |
| Images, PDFs, text, and other non-JSON source files | Optional stored material | Any convenient location | Not opened by the reader and not shown as passage leaves |

A minimal marker can contain:

```json
{}
```

A book is listed whenever its immediate directory contains `book.json`, even if it currently has no passages. JSON validity is not checked during discovery, so a malformed potential passage can appear in the tree and fail only when selected.

## 3. Passage and vocabulary naming

The Passage JSON `title` field supplies the center-pane title, including an empty string to hide it. The filename only identifies the passage and its matching companion files; it does not supply a title fallback.

For a passage named `Example.json`, the automatically loaded companion is exactly `Example.vocabulary.json`. The application does not search for an arbitrary vocabulary file in the same folder.

A standalone file named `vocabulary.json` can be selected through the vocabulary import button, but it is not automatically paired with a passage. Keep such input files outside scanned book branches, or rename the final file to the matching companion name. Otherwise `vocabulary.json` may appear as a potential passage and fail the passage `filetype` check.

The optional `family` array on each vocabulary entry holds article-attested surface forms differing from its `word`. It may be omitted or empty; invalid or duplicate forms are rejected. Both Passage and Script match explicitly listed forms to the canonical headword without changing pronunciation filenames or the right-side vocabulary display.

Import replaces the active article's word list and saves the result to its matching companion file; it is not a merge operation. Moving or deleting words also saves that companion. If the file has changed externally since loading, the save is rejected: reopen the article before editing again. Malformed vocabulary does not block passage reading, but overwriting that malformed companion is disabled until it is repaired and reopened.

See the [passage skill](skills/passage_segment/SKILL.md) and [vocabulary skill](Vocabulary-Preparation.md) for the exact JSON structures.

## 4. Whole-article and vocabulary audio

### Whole-article playback

The bottom play/pause button searches only the directory containing the Passage JSON. The filename need not match the Passage filename or JSON title. Extensions are compared case-insensitively. Candidate files are sorted by **extension priority** (`.m4a`, then `.mp3`, then `.wav`), then by filename using ordinal case-insensitive ordering with ordinal comparison as a tie-breaker. Each candidate is attempted in this single order. If opening/decoding fails, continue with the next file, including other files of the same extension. Stop searching once one file opens successfully. Report one failure only after all candidates fail.

Only direct sibling files qualify; nested files are ignored. Do not concatenate audio or start another file after normal playback completes. A playback failure *after successful opening* is reported rather than restarting another recording. User stop, article switch and window shutdown cancel pending candidate attempts. Selecting UK/US pronunciation does not change the whole-article recording.

### Vocabulary playback

Word pronunciation uses names derived from the word stem:

```text
audio/<stem>_uk.mp3 or .wav
audio/<stem>_us.mp3 or .wav
```

The exact ASCII-only stem algorithm is defined in [Vocabulary Preparation](Vocabulary-Preparation.md). Reserve each nonempty stem for the first entry in `words` order before checking files. Later collisions and empty stems keep their entries but have no audio eligibility. For each accent, an existing MP3 wins; WAV is considered only if MP3 is absent. Decoding failure does not retry WAV. No M4A vocabulary support is added.

Passage and Vocabulary JSON never declare media paths. Passage sentences have no SID and no sentence recordings: the old `audio_sentences/` convention and segment player are removed. Whole-article audio has its own player; vocabulary pronunciation uses a second player. Pronunciation pauses article playback at the current position. Resuming article playback stops pronunciation; the bottom stop control affects only article audio. Closing the window or opening another article stops both.

## 5. Excluded branches and safe paths

The scanner excludes hidden files/directories, names beginning with `.`, directories named `userdata`, `__pycache__` or `cache` (case-insensitive exact names), and reparse points such as junctions or symbolic links. If a directory directly contains any `.json` file (case-insensitive extension), its immediate child directories whose names contain `audio` (case-insensitive substring) are also hidden. Without a directly contained JSON file, audio-named child directories remain visible. These are navigation-only filters: they do not delete directories or prevent audio/cache access. Inside a book, `book.json`, `*.vocabulary.json` and `*.exercise.json` are excluded from passage leaves.

Other JSON files are treated as potential passages without inspecting their contents. Keep unrelated JSON outside scanned book branches.

Derived audio paths stay within the passage directory. Paths escaping that directory and paths traversing reparse points are rejected. Prepare recordings under the exact new directories and inferred filenames; JSON paths cannot redirect playback.

## 6. Files outside the library

Application settings live in `dist/ReadArticles/userdata/config/settings.json`, beside the published `dist/ReadArticles/ReadArticles.exe` in its `userdata/` directory. The legacy executable-sibling settings file is ignored by the current format but is not automatically deleted. The portable release uses a framework-dependent single EXE and requires the compatible .NET 10 Windows Desktop Runtime x64 on the target machine. Keep `userdata/` and the rest of the application directory together during updates.

Source images and the original Python ZIP are preparation/reference material. ReadArticles does not convert them when a library is opened.

## 7. Lazy navigation and refresh

The application first enumerates only the library root's immediate child directories, identifying books by the existence (not contents) of `book.json`. Each expansion enumerates only the selected directory's direct JSON passage candidates and immediate subdirectories. Nested folders appear even when no passage JSON exists at their level; no recursive scan is performed at startup. Passage JSON is parsed only when opened, and whole-article audio candidates are checked only when playback is requested.

Collapsing invalidates pending scans; reopening refreshes that directory. When a refresh fails, the last successful snapshot is retained where available and a warning is shown. Navigation excludes the files and folders described in [Excluded branches and safe paths](#5-excluded-branches-and-safe-paths); there is no recursive pre-scan.

## Optional Script reading companion

For an existing `<name>.json` Passage, place `<name>.script.json` in the **same directory**. The Script companion is not a separate article leaf and is not read when opening Passage. Clicking the music toggle attempts to load it once; when missing or invalid, Passage remains visible and playback continues.

The Script JSON root must have `"filetype": "script"`, `"title": ""` (or another string), and nonempty `"paragraphs"`. Every paragraph is a nonempty array of sentences; each sentence has nonempty `"text"` and `"timed_units"` array. Timed units are exactly `[displayText, startSeconds, endSeconds]`: displayText is a nonempty string; both time fields must be null or valid finite nonnegative numbers with end greater than start. Empty timed_units arrays are valid.

The application uses `text` as the displayed source; timed-unit text is located in original sentence order when rendering, but its alignment is not an admission check. Unlocatable units are not colored. Script and Passage text need not match. A nonempty Script title uses the regular Passage title styling; an empty title hides the title. Script has timed sentence units and no fill-in-the-blank placeholders; Passage also has no sentence pronunciation. The original Passage file determines the directory used for whole-article audio and the companion vocabulary file; Script never changes those lookup rules.

The music icon is dark grey for Passage and colored for Script. Vocabulary highlighting independently controls the background in both views. The audio clock controls Script foreground progress, which is cleared when article playback stops or naturally ends. Neither source JSON is modified.

To convert an original WhisperX transcript JSON directly into this companion format, use the [Script generation skill](skills/script_generation/SKILL.md).
