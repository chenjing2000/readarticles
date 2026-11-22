# ReadArticles Application Overview

## Design goals

ReadArticles is a local Windows reading and vocabulary application. WPF hosts the left library tree and one WebView2 hosts the passage and vocabulary panes. Content remains in ordinary JSON and M4A/MP3/WAV files so it can be prepared outside the application and inspected without a database.

The design favors explicit responsibilities, concrete services, and small functions. It avoids a dependency-injection container, generic repository framework, event bus, and plugin architecture. A single audio-player interface is retained because it separates native media playback from deterministic playback tests.

## Framework and execution

The application uses C# and WPF, targeting `net10.0-windows` with runtime version `10.0.11` or a later compatible patch. The build SDK is selected by `global.json`, currently `10.0.400`. WPF XAML defines the native window and directory tree. A single embedded WebView2 renders Passage/Script content, vocabulary, controls, text selection, highlighting and notifications using static HTML/CSS/JavaScript. WPF media playback supplies system-supported M4A/MP3/WAV decoding and progress through the C# playback service. WebView2 Evergreen Runtime is a separate system requirement.

JSON handling uses `System.Text.Json`; files remain the storage layer. The original Python application is a migration reference, not a runtime dependency or required repository file. The former WPF passage renderer was removed after WebView2 migration.

## Module responsibilities

| Module | Responsibility |
| --- | --- |
| `App.xaml` / `App.xaml.cs` | Shared brushes and button styles; startup, command-line library selection, and main-window creation |
| `MainWindow.xaml` | Native left navigation and one WebView2 reader host, with a native failure/exit overlay |
| `MainWindow.xaml.cs` | Construct the state model and two independent media players; initialize modules and coordinate shutdown |
| `MainWindow.Library.cs` | Folder selection, tree selection, library restoration and article selection |
| `MainWindow.Playback.cs` | C# audio playback snapshots, timing and resource cleanup |
| `MainWindow.Settings.cs`, `SettingsDialog.cs` | Shared pane-width solver, native modal settings and preference persistence |

| `ViewModels/MainViewModel.cs` | Shared state, notifications, and operation errors |
| `ViewModels/MainViewModel.Library.cs` | Prepare and commit library/article changes |
| `ViewModels/MainViewModel.Vocabulary.cs` | Prepare word commands and coordinate vocabulary edits and commits |
| `ViewModels/MainViewModel.Playback.cs` | Forward reader commands with the current article and accent |
| `ViewModels/VocabularyEntryViewModel.cs` | A word's display fields, available pronunciation actions, and move/delete commands |
| `Models/` | Passage sentence arrays, selection ranges, vocabulary entries, navigation nodes, and loaded snapshots |
| `Services/LibraryRepository.cs` | Discover immediate book directories and scan each expanded folder one level at a time without parsing passage content |
| `Services/ArticleRepository.cs` | `ReadParagraphs` parses and validates passage content; `Load` coordinates the matching vocabulary, missing-audio warnings, and the loaded snapshot |
| `Services/VocabularyRepository.cs` | Validate vocabulary, derive audio stems, and save with external-change detection |
| `Services/ReaderWorkspace.cs` | Commit prepared library/article state and replace vocabulary after successful persistence |
| `Services/AudioResources.cs` | List safe sibling M4A/MP3/WAV article candidates in priority/name order and resolve word MP3/WAV under `audio/` |
| `Services/ReaderPlayback.cs` | Select audio targets, enforce article-only controls/progress, cancel pronunciation, and report audio errors |
| `Services/PlaybackController.cs` / `IAudioPlayer.cs` | Playback state, pause/resume, completion, progress access, and stale-event rejection; player boundary |
| `Infrastructure/JsonFiles.cs` | JSON helpers, SHA256 fingerprints, and temporary-file replacement |
| `Infrastructure/ResourcePaths.cs` | Relative-path and reparse-point checks |
| `Infrastructure/ReaderSettings.cs` | Load and save preferences under the portable `userdata/config/` directory |
| `Infrastructure/UiIcons.cs` | Load remaining native window/tree icons |
| `Infrastructure/WpfAudioPlayer.cs` | Native audio playback adapter |
| `MainWindow.WebView.cs` | Browser initialization, verified bridge messages and native terminal failure handling |
| `web/reader.html`, `reader.css`, `reader.js`, `web/icons/*.svg` | Article/Script DOM, matching, selection, playback visuals, embedded icon states, styles and notifications |

The window and main view model each use partial files to group responsibilities while sharing their existing state. `ReaderPlayback` is a concrete service with no window or view-model dependency. `PlaybackController` operates through `IAudioPlayer`, which includes media-open events and progress. The window sends playback snapshots to the page; the page sends validated seek/playback intents to `ReaderPlayback`. Repositories own validation and persistence, the workspace owns committed content, and the browser owns text/selection display. No additional service interfaces or generic frameworks are introduced.

## Important data flows

### Library and article loading

```text
Folder selection -> LibraryRepository -> navigation nodes -> collapsed tree
Passage selection -> ArticleRepository -> prepared article and words
                  -> commit workspace -> WebView2 article/word snapshots -> DOM
```

Library selection clears the active passage and words. Startup restores only a library directory, when available. Selecting an article loads its matching vocabulary. Preparation occurs before committing the article, so a failed switch retains the previous readable article.

### Vocabulary editing

```text
Browser selection/import/move/delete intent -> MainViewModel -> ReaderWorkspace
                            -> VocabularyRepository -> checked file replacement
                            -> authoritative word snapshot -> browser DOM/highlights
```

All successful vocabulary commits use one refresh/notification path. Import keeps incoming metadata; later edits retain the current vocabulary metadata. Recognized extension metadata at the vocabulary root, entry, and meaning levels survives serialization. Legacy `audio` fields are ignored on load and removed on save.

Vocabulary saves serialize this application's writers with a named mutex derived from the absolute, case-normalized path within the current Windows session. Each writer rechecks the expected fingerprint after acquiring ownership and before replacing the file; a stale writer is rejected. This does not lock unrelated external editors or coordinate different path aliases through links or network mappings.

The JSON `words` array determines list order. Moving swaps neighboring positions; the first and last items disable impossible moves. Delete removes the selected entry. The browser selection-add action requests a C# word commit whose pronunciation filenames are inferred from its stem, but does not generate definitions or audio files.

Delete requires an active, writable vocabulary and an entry belonging to that vocabulary. Requests with no article or a stale entry do nothing. The matching pronunciation is stopped only after the deletion has been saved successfully.

After a successful word edit, C# publishes the authoritative vocabulary snapshot; the browser updates its word rows and vocabulary highlights. Failed or stale edits cannot appear as committed changes.

### Playback

```text
UI request -> ReaderPlayback -> audio resource lookup -> PlaybackController -> IAudioPlayer
Media event -> request-token check -> ReaderPlayback state/progress -> UI update
```

The bottom play/pause and stop controls own only whole-article audio, selected by trying direct sibling M4A, MP3 and WAV candidates in format/name order until one opens. An opening failure can advance to the next candidate; successful opening ends that search. Whole-article playback uses its own player and controller, while word pronunciation uses a second independent player and controller. Starting or resuming whole-article audio stops pronunciation. Starting word pronunciation pauses whole-article audio without losing its position; finishing pronunciation leaves it paused. Old completion/failure callbacks cannot advance or stop a newer target. The bottom progress bar stays visible and accepts seeking only when whole-article audio is active and seekable; stopping article audio leaves the word channel untouched. Switching articles or closing the window stops both. No sentence playback path remains. Script timed-unit foreground progress is computed and painted in `web/reader.js` using playback snapshots from C#; `ScriptTimedUnit` retains alignment and color eligibility, not a separate C# progress calculation.

### Notifications and lifecycle

Routine successful actions are quiet. Floating notifications appear over the middle reading pane without allocating a status row. Informational and warning messages expire automatically after approximately five seconds of active display time, paused while hovered; errors remain accessible until dismissed. The notification service retains unacknowledged errors, including overflow beyond the visible cards.

On close, normal application windows save final settings during `Closing`, before the window is disposed; a failed final save shows a synchronous warning. During `Closed`, notifications and navigation are detached and playback resources are released. Test windows can disable settings persistence.

## Display and settings

- The window starts centered. Height is 70% of the rounded work-area height; width is the larger of 70% of the rounded work-area width or 796 DIP, capped at the work-area width. Previous window dimensions are not restored.
- The native tree uses 11 pt. The fixed Fluent settings dialog edits a detached Appearance/Audio draft. Title 18px/1.7 line height/16px paragraph gap, Georgia body 15px/1.7/16px gap, word head 16px semibold, phonetics 14px, meanings 15px/1.5, background #F8F8F6. Eight configurable color areas each have five circular swatches; Script progress defaults to #B64550, with selection-add glyph size 0.65em and 60Hz repaint-paced geometry tracking.
- Each paragraph has a two-em first-line indent. All panes scroll independently. Library width defaults to 250 DIP (200–400), vocabulary width to 300 DIP (200–400), the article width has a 200-DIP minimum, and both splitters are 3 DIP. The native WebView2 host has a fixed 403-DIP minimum. One WPF width solver coordinates both splitters: moving either edge changes article width first (minimum 200 DIP), then shrinks the opposite outer pane (minimum 200 DIP), and stops when all three reach their minima. WebView2 sends live right-divider requests with screen-anchored pointer coordinates and renders only C#-confirmed width values. CSS has minimum article/host safeguards but no competing dynamic vocabulary-width clamp.
- The two reading-workspace toolbars live in WebView2. Playback controls remain present when disabled.
- Highlighting starts off (gray toggle icon); turning it on shows a magenta toggle icon and rounded word backgrounds painted from Range rectangles in a non-interactive layer, without changing article or Script text nodes. The selection-add control follows valid text selection.
- Audio settings provide Forward (4/7/10/15 seconds) and Backward (5/8/11/16 seconds) steps. Browser focus handles Space for article play/pause and Left/Right for seek; native controls retain only their own focus behavior.
- Appearance styles, accent, progress steps and library are persisted; both media players use fixed 100% volume. Pane widths can change during a session but reset to their defaults on restart; window dimensions, active passage and highlighting are not saved. Legacy width fields in settings JSON are ignored.

## Current boundaries

The application reads passages and vocabulary and plays existing audio. OCR, vocabulary enrichment, dictionary lookup, and audio generation are external preparation workflows. There is no paragraph-play button, vocabulary-export UI, account registration/login workflow, or exercise-answering interface.

Matching is literal and case-insensitive for each `word` and its explicit optional `family` forms: `convict` matches `convicted` only when `"convicted"` is listed. Every hit belongs to the canonical `word`, with direct headwords preferred over another entry's family forms. No automatic morphological inference runs, and straight/curly apostrophes are not normalized. ASCII letters, apostrophes, and hyphens participate in word boundaries; digits deliberately do not. Among matches starting at the same position, the longest wins. These details matter when authoring canonical vocabulary entries.

Each playback controller owns one active audio file at a time. Whole-article opening may advance across a fixed sorted list of candidate files; it is not a playback playlist. Playback state belongs to the controller, coordination between the two players belongs to ReaderPlayback, and timers and controls belong to MainWindow.Playback. The existing concrete repositories and window partial classes remain together; small repeated UI operations do not require another abstraction layer.

Tests exercise these production modules directly; the obsolete prototype application has been removed. See [Testing](Testing.md) for categories and their data ownership. Exercise authoring remains in the supplied skills, but there is no exercise runtime module on main.

Keyboard focus is available in both native tree controls and browser controls; focus visuals follow each renderer.

Native button focus and browser `:focus-visible` are styled independently. Local MP3s and personal resource directories are excluded from Git; required icons remain embedded.

The Portable publish profile creates one framework-dependent Windows x64 executable in `dist/ReadArticles/`. Its native host asks users to install the matching Windows Desktop Runtime when missing. Configuration uses `userdata/config/settings.json` under the executable directory. Old schemas reset without migration; corrupted current settings may recover from a verified backup. The window has no explicit maximum size. See [Publishing](Publishing.md) for publishing commands and [Testing](Testing.md) for verification limits.

ReaderSettings owns structured defaults, recovery and validation for typography, palette colors, accent and progress choices. The native left width and browser vocabulary width have separate layout controls; persisted legacy widths are ignored on restart.

Native window/column dimensions use WPF layout; browser scrollbars, text wrapping, SVG clip geometry and selection positions use Chromium layout. Saved legacy pane widths do not restore on restart.

Whole-article audio is selected from an ordered list of sibling M4A, MP3 and WAV files (extension priority, then ordinal filename ordering). The playback controller tries the next candidate only if opening fails; successful open ends selection. Sentence audio and SID-based paths are removed. Vocabulary pronunciation is still derived from `audio/<stem>_uk.mp3` or `.wav` and corresponding `_us` files; the existing MP3-before-WAV rule remains unchanged.

## Service boundaries

`NotificationService` owns the FIFO queue, severity and error persistence. WebView2 displays at most three floating notification cards and owns their animation, hover and dismissal intents. Browser startup failure is shown on the native terminal screen.

`LibraryRepository.Load` discovers only book directories at the library root, while `ScanDirectoryLevel` enumerates one level without opening passage content. `MainViewModel` coordinates background scans and marshals collection updates to the WPF Dispatcher, guarded by library-session and node-generation tokens. Closing the window or collapsing a folder invalidates pending work.
