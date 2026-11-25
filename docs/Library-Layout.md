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
        When America walks away.script.json
        article.m4a
        audio/
          be_stuck_in_uk.mp3
          be_stuck_in_us.mp3
```

Date and article directories are conventions, not requirements. A passage may sit directly in the book directory or deeper within it. Separate article directories are useful because all passages sharing a directory also share its whole-article audio selection and relative audio locations.

Opening a library discovers only immediate book directories and checks for `book.json`; it does not enumerate passages inside books, parse passage or vocabulary JSON, open an article, or start audio. The tree starts collapsed. Each expansion enumerates one directory level and parses direct JSON files to identify top-level objects with exact filetype `passage`; collapsing invalidates pending scans and descendant load states.

## 2. Required and optional files

| File or directory | Requirement | Location | Current behavior |
| --- | --- | --- | --- |
| Book directory | Required for normal library navigation | Immediate child of the selected root | Recognized only when it contains `book.json` |
| `book.json` | Required marker for each book | Book directory itself | Only its existence is checked; its contents are not parsed |
| `<title>.json` | Required to read a passage | Anywhere inside a recognized book, outside excluded branches | Tag checked during expansion; fully validated when selected |
| `<title>.vocabulary.json` | Optional | Same directory and same filename stem as the passage | Automatically loaded with the passage; missing file gives an empty, writable word list |
| `audio/` and its MP3/WAV files | Optional physical files | Passage directory | Names derived from word stem; missing or empty files disable pronunciation buttons; later stem collisions have no audio eligibility |
| Whole-article `.m4a`, `.mp3` or `.wav` candidates | Optional | Directly beside the passage JSON | Playback opens the first successful candidate in priority/name order |
| `<title>.script.json` | Optional | Same directory and stem as the Passage | Read-only Script companion loaded on demand; see below |
| `userdata/` | Optional external folder | Anywhere below a book | Excluded from navigation; no account or answer-sheet data is loaded |
| `<title>.exercise.json` | Optional authoring companion | Beside the passage | Excluded from navigation and not read or displayed by the current reader |
| Images, PDFs, text, and other non-JSON source files | Optional stored material | Any convenient location | Not opened by the reader and not shown as passage leaves |

A minimal marker can contain:

```json
{}
```

A book is listed whenever its immediate directory contains `book.json`, even if it currently has no passages. Book markers are not parsed. During expansion, malformed JSON and objects without exact `filetype: "passage"` are omitted. A syntactically valid passage-tagged object can still fail the full schema validation when selected.

## 3. Passage and vocabulary naming

The Passage JSON `title` field supplies the center-pane title, including an empty string to hide it. The filename only identifies the passage and its matching companion files; it does not supply a title fallback.

For a passage named `Example.json`, the automatically loaded companion is exactly `Example.vocabulary.json`. The application does not search for an arbitrary vocabulary file in the same folder.

A standalone file named `vocabulary.json` can be selected through the vocabulary import button, but it is not automatically paired with a passage. Rename the final file to the matching companion name for automatic pairing. A valid vocabulary document is not a passage leaf because its filetype is different.

Authors use the optional `family` array for article-attested surface forms differing from its `word`; the runtime validates the list structure, not attestation. It may be omitted or empty; invalid or duplicate forms are rejected. Both Passage and Script match explicitly listed forms to the canonical headword without changing pronunciation filenames or the right-side vocabulary display.

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

The scanner excludes hidden files/directories, names beginning with `.`, names equal to `userdata` or `__pycache__` (case-insensitive), and reparse points such as junctions or symbolic links. User-configured exclusions (default `audio, cache`; see [Configuration](Configuration.md)) match complete directory names case-insensitively at every level, including book discovery, regardless of JSON presence. Excluded branches are not traversed. Files with the same name as an exclusion are not hidden by that setting. These are navigation-only filters: they do not delete directories or prevent audio/cache access. Inside a book, direct JSON files become leaves only if they parse as objects with a string `filetype` exactly equal to `passage`. Normal book markers, vocabulary, Script and exercise companions therefore do not become leaves. This is a content-based filter, not a filename-suffix blacklist. Malformed JSON is skipped; unreadable files may yield an incomplete scan and a warning.

Saving a changed exclusion set cancels old scans, invalidates cached nodes and asynchronously rebuilds a collapsed tree for the same library. The current article, vocabulary and playback are retained. Equivalent sets do not refresh the tree. If root discovery fails, the tree remains empty, the library path and reading state remain committed, and an error is shown. A pending explicit library switch takes precedence: its discovery uses the latest exclusion set; on switch failure the committed library is refreshed instead. A closed navigation session never publishes pending results.

Passage reads, on-demand Script reads and vocabulary reads/writes reject paths traversing junctions or symbolic links, including existing ancestors. An unsafe vocabulary companion leaves Passage readable but disables vocabulary editing; an unsafe Script leaves the current reading mode unchanged. Programmatic passage selection cannot bypass these checks; ordinary directory exclusions remain navigation-only. Word-head duplicate checks normalize surrounding and repeated whitespace and compare case-insensitively without rewriting existing word text.

Derived audio paths stay within the passage directory. Paths escaping that directory and paths traversing reparse points are rejected. Prepare recordings under the exact new directories and inferred filenames; JSON paths cannot redirect playback.

## 6. Files outside the library

Application settings live in `dist/ReadArticles/userdata/config/settings.json`, beside the published `dist/ReadArticles/ReadArticles.exe` in its `userdata/` directory. The legacy executable-sibling settings file is ignored by the current format but is not automatically deleted. The portable release uses a framework-dependent single EXE and requires the compatible .NET 10 Windows Desktop Runtime x64 on the target machine. Keep `userdata/` and the rest of the application directory together during updates.

Images, PDFs and other source materials may be prepared externally using the skills in [the documentation index](README.md). They are not opened or converted by the reader.

## 7. Lazy navigation and refresh

The application first enumerates only the library root's immediate child directories, identifying books by the existence (not contents) of `book.json`. Each expansion enumerates only the selected directory's immediate files and subdirectories and parses direct JSON to check the passage tag. Nested folders appear even when no passage JSON exists at their level; no recursive scan is performed at startup. Full Passage schema validation and companion loading occur only when a leaf is opened; whole-article audio candidates are checked only when playback is requested.

The explicit Refresh Library button reopens the committed root using the current exclusions, resets the reading workspace and both audio channels, and starts with a collapsed tree. A missing root clears the persisted library selection and disables refresh; other root access failures clear the tree and reading workspace but retain the path for retry. Failed selection of a different library still retains the previous workspace.

Collapsing invalidates pending scans; reopening refreshes that directory. When a refresh fails, the last successful snapshot is retained where available and a warning is shown. Navigation excludes the files and folders described in [Excluded branches and safe paths](#5-excluded-branches-and-safe-paths); there is no recursive pre-scan.

## Optional Script reading companion

For an existing `<name>.json` Passage, place `<name>.script.json` in the **same directory**. The Script companion is not a separate article leaf and is not read when opening Passage. Clicking the music toggle loads it on demand; a successfully loaded Script is reused until the active article changes; failed attempts can be retried. When missing or invalid, Passage remains visible and playback continues.

The Script JSON root must have `"filetype": "script"`, `"title": ""` (or another string), and nonempty `"paragraphs"`. Every paragraph is a nonempty array of sentences; each sentence has nonblank `"text"` and `"timed_units"` array. Timed units are exactly `[displayText, startSeconds, endSeconds]`: displayText is a nonempty string; both time fields must be null or valid finite nonnegative numbers with end greater than start. Empty timed_units arrays are valid.

The application uses `text` as the displayed source; timed-unit text is located in original sentence order when rendering, but its alignment is not an admission check. Unlocatable units are not colored. Script and Passage text need not match. A nonempty Script title uses the regular Passage title styling; an empty title hides the title. Script has timed sentence units and does not interpret fill-in-the-blank placeholders; Passage also has no sentence pronunciation. The original Passage file determines the directory used for whole-article audio and the companion vocabulary file; Script never changes those lookup rules.

The music icon is dark grey for Passage and colored for Script. Vocabulary highlighting independently controls the background in both views. The audio clock controls Script foreground progress, which is cleared when article playback stops or naturally ends. Neither source JSON is modified.
