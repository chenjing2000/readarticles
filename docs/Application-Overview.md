# ReadArticles Application Overview

## Design goals

ReadArticles is a local Windows reading and vocabulary application. WPF hosts the left library tree and one WebView2 hosts the passage and vocabulary panes. Content remains in ordinary JSON and M4A/MP3/WAV files so it can be prepared outside the application and inspected without a database.

The design favors explicit responsibilities, concrete services, and small functions. It avoids a dependency-injection container, generic repository framework, event bus, and plugin architecture. A single audio-player interface is retained because it separates native media playback from deterministic playback tests.

## Framework and execution

The application uses C# and WPF, targeting `net10.0-windows` with runtime version `10.0.11` or a later compatible patch. The build SDK is selected by `global.json`, currently `10.0.400`. WPF XAML defines the native window and directory tree. A single embedded WebView2 renders Passage/Script content, vocabulary, controls, text selection, highlighting and notifications using static HTML/CSS/JavaScript. WPF media playback supplies system-supported M4A/MP3/WAV decoding and progress through the C# playback service. WebView2 Evergreen Runtime is a separate system requirement.

JSON handling uses `System.Text.Json`; files remain the storage layer. Neither Python nor a second WPF reader is a runtime dependency.

## Module responsibilities

| Module | Responsibility |
| --- | --- |
| `App.xaml` / `App.xaml.cs` | Shared brushes and button styles; startup, command-line library selection, and main-window creation |
| `MainWindow.xaml` | Native left navigation and one WebView2 reader host, with a native failure/exit overlay |
| `MainWindow.xaml.cs` | Construct the state model and two independent media players; initialize modules and coordinate shutdown |
| `MainWindow.Library.cs` | Folder selection, explicit library refresh, tree selection, library restoration and article selection |
| `MainWindow.Playback.cs` | Native playback key interception and player cleanup |
| `MainWindow.Settings.cs`, `Settings/SettingsDialog.cs` | Apply native pane widths, native modal settings and preference persistence |
| `Layout/PaneLayout.cs` | Session-only pane widths, shared width solver and right-divider drag rollback |
| `Application/MainViewModel.cs` | Shared state, notifications, and operation errors |
| `Application/MainViewModel.Library.cs` | Coordinate library/article preparation and commits |
| `Library/LibraryNavigation.cs` | Own library root, exclusion snapshot, tree projection, async scans, cancellation and stale-result guards |
| `Application/MainViewModel.Reading.cs` | Prepare reading-mode changes, coordinate render-confirmed commits and selection validation |
| `Application/MainViewModel.Vocabulary.cs` | Prepare word commands and coordinate vocabulary edits and commits |
| `Application/MainViewModel.Playback.cs` | Forward reader commands with the current article and accent |
| `Vocabulary/VocabularyEntryViewModel.cs` | A word's display fields and pronunciation availability; edits are coordinated by the main view model |
| `Library/LibraryContent.cs`, `Reading/LoadedArticle.cs`, `Reading/ScriptContent.cs`, `Vocabulary/VocabularyContent.cs` | Feature-owned navigation nodes and loaded content records |
| `Library/LibraryRepository.cs` | Discover immediate book directories; scan one level and parse JSON for exact passage tags; defer full schema validation |
| `Reading/ArticleRepository.cs` | `ReadParagraphs` parses and validates passage content; `Load` prepares the matching vocabulary and article snapshot; `LoadScript` reads the optional Script on demand |
| `Vocabulary/VocabularyEditor.cs` | Prepare vocabulary edits, preserve metadata and return a saved snapshot |
| `Vocabulary/VocabularyRepository.cs` | Validate vocabulary and save with external-change detection |
| `Reading/ReaderWorkspace.cs` | Own committed article, Script cache and mode; validate selected text and commit the saved vocabulary snapshot |
| `Audio/AudioResources.cs` | Derive audio stems, determine pronunciation ownership, list safe sibling M4A/MP3/WAV article candidates in priority/name order and resolve word MP3/WAV under `audio/` |
| `Audio/ReaderPlayback.cs` | Select audio targets, enforce article-only controls/progress, cancel pronunciation, and report audio errors |
| `Audio/PlaybackController.cs` / `Audio/IAudioPlayer.cs` | Playback state, pause/resume, completion, progress access, and stale-event rejection; player boundary |
| `Infrastructure/JsonFiles.cs` | JSON helpers, SHA256 fingerprints, and temporary-file replacement |
| `Infrastructure/ResourcePaths.cs` | Relative-path and reparse-point checks |
| `Settings/ReaderSettings.cs` | Load and save preferences under the portable `userdata/config/` directory |
| `Infrastructure/UiIcons.cs` | Load native window/tree/refresh/settings icons, including Restore Defaults |
| `Infrastructure/AppPaths.cs`, `BackupFiles.cs`, `AppLogger.cs` | Portable data paths, verified backup/recovery and bounded diagnostics |
| `Notifications/NotificationService.cs` | Authoritative in-memory FIFO notification queue |
| `Audio/WpfAudioPlayer.cs` | Native audio playback adapter |
| `MainWindow.WebView.cs` | Compose the browser modules, native import confirmation and terminal failure UI |
| `Presentation/WebReaderHost.cs` | WebView2 lifecycle, restricted resource serving and transport |
| `Presentation/WebReaderBridge.cs` | Protocol identity, validated commands, snapshots and progress timer |
| `web/reader.html`, `reader.css`, `reader.js`, `web/icons/*.svg` | Single local document, styles, module composition and embedded icons |
| `web/bridge.js`, `dom.js` | Protocol identity and shared DOM helpers |
| `web/reading.js`, `vocabulary.js` | Passage/Script rendering, matching, selection and vocabulary edit presentation |
| `web/playback.js`, `layout.js`, `settings.js`, `notifications.js` | Playback controls, pane-width intents, appearance and Toast presentation |

The window and main view model each use partial files to group responsibilities while sharing their existing state. `ReaderPlayback` is a concrete service with no window or view-model dependency. `PlaybackController` operates through `IAudioPlayer`, which includes media-open events and progress. The bridge sends playback snapshots to the page; the page sends validated seek/playback intents to `ReaderPlayback`. Repositories own validation and persistence, the workspace owns committed content, and the browser owns text/selection display. No additional service interfaces or generic frameworks are introduced.

## Dependency direction and state ownership

One WPF project and the existing test project are retained. Source directories group concrete features, not separate assemblies; existing C# namespaces stay stable to avoid a namespace-only change. Root window partials own native controls and module composition. `Application/MainViewModel*.cs` coordinates explicit operations. Host owns restricted WebView2 transport and lifecycle; Bridge adapts verified requests and publishes snapshots. Feature code does not depend on the window or browser. Reading loads matching vocabulary through VocabularyRepository; Audio consumes vocabulary entries and a directory, never the vocabulary repository. Infrastructure provides paths, logging, JSON, backups and icons. No service locator, DI container, event bus, generic repository or extra framework is required.

| Authoritative state | Owner |
| --- | --- |
| Library root, navigation tree and scan generations | LibraryNavigation |
| Committed article, vocabulary snapshot, Script cache and mode | ReaderWorkspace |
| Candidate edits and safe save (no separate active vocabulary) | VocabularyEditor / VocabularyRepository |
| Article/word media clock and controller ownership | ReaderPlayback / PlaybackController |
| Persisted preferences including accent | Shared ReaderSettings |
| Session-only pane widths and drag rollback | PaneLayout |
| Admitted/queued notices | NotificationService |
| Browser page/session/revision, pending requests and snapshots | WebReaderBridge |
| DOM, selection, scroll, geometry and presentation copies | JavaScript feature modules |

The browser entry `reader.js` composes direct functions without global mutable application state. Imports are acyclic; reading receives its cross-feature callbacks at composition. Host serves only explicit embedded HTML, CSS, ES modules and icon paths. Preserve CSP and protocol version 1 unless a new change explicitly requires otherwise.

## Important data flows

### Startup and browser synchronization

App starts logging, loads settings and composes the window, application and browser modules. Library restoration and browser initialization can complete independently. Scan results commit on the owning dispatcher only if current. The browser sends ready with a page identity; Bridge publishes the latest committed workspace/settings/playback/notices and reveals the reader after a correlated successful render acknowledgment. Reload invalidates prior page requests without discarding committed reading state. Terminal browser failure invalidates pending requests, stops progress publication and audio, and presents the native exit screen.

### Library and article loading

```text
Folder selection -> LibraryNavigation -> LibraryRepository -> navigation nodes -> collapsed tree
Passage selection -> ArticleRepository -> prepared article and words
                  -> commit workspace -> WebView2 article/word snapshots -> DOM
```

Library selection or explicit refresh clears the active passage and words and stops both audio channels. Missing-root refresh clears the persisted library path; other refresh failures retain it for retry. Exclusion changes still refresh navigation only. Startup restores only a library directory, when available. Selecting an article loads its matching vocabulary. Preparation occurs before committing the article, so a failed switch retains the previous readable article.

### Reading-mode switching

Prepare the target mode and load the optional Script on demand without altering committed state. Bridge correlates the candidate with a request. Only a matching page/session/request success acknowledgment commits the mode and Script cache; render failure restores the previous DOM and mode, leaving the audio clock unchanged. Library/article switches invalidate pending mode requests. The browser paints timed units from C# playback snapshots.

### Vocabulary editing

```text
Browser selection/import/move/delete intent -> MainViewModel -> VocabularyEditor
                            -> VocabularyRepository -> checked file replacement
                            -> ReaderWorkspace commit -> browser DOM/highlights
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

The bottom play/pause and stop controls own only whole-article audio, selected by trying direct sibling M4A, MP3 and WAV candidates in format/name order until one opens. An opening failure can advance to the next candidate; successful opening ends that search. Whole-article playback uses its own player and controller, while word pronunciation uses a second independent player and controller. Starting or resuming whole-article audio stops pronunciation. Starting word pronunciation pauses whole-article audio without losing its position; finishing pronunciation leaves it paused. Old completion/failure callbacks cannot advance or stop a newer target. The bottom progress bar stays visible and accepts seeking only when whole-article audio is active and seekable; stopping article audio leaves the word channel untouched. Switching articles or closing the window stops both. No sentence playback path remains. Script timed-unit foreground progress is computed and painted in `web/reading.js` using playback snapshots from C#; `ScriptTimedUnit` retains alignment and color eligibility, not a separate C# progress calculation.

### Settings and layout

The native dialog edits a detached settings draft. Successful save precedes application to the shared ReaderSettings. Changed accent stops pronunciation; the complete settings snapshot is published once. Library root changes persist separately. PaneLayout computes both sides and owns the drag baseline; MainWindow applies native width and Bridge publishes right width. Browser pointer input requests width rather than owning final layout.

### Notifications and lifecycle

Routine successful actions are quiet. Floating notifications appear over the middle reading pane without allocating a status row. Informational and warning messages expire automatically after approximately five seconds of active display time, paused while hovered; errors do not auto-expire. The service admits the first three queued messages, displays them newest-first within that group, and admits waiting messages FIFO when a card is dismissed. Switching to a successfully committed library or article clears the previous context's queue.

On close, normal application windows save final settings during `Closing`, before the window is disposed; a failed final save shows a synchronous warning. During `Closed`, Bridge stops the publication timer and detaches subscriptions, Host releases browser resources, navigation invalidates and cancels scans, then both playback resources are stopped and disposed. Test windows can disable settings persistence.

## Display and settings

- The window starts centered. Height is 70% of the rounded work-area height; width is the larger of 70% of the rounded work-area width or 796 DIP, capped at the work-area width. Previous window dimensions are not restored.
- The native tree uses 11 pt. The native modal edits a detached settings draft; all typography, palettes, seek options and persistence rules are specified in [Configuration](Configuration.md).
- Paragraph indentation follows the body setting (default two em). All panes scroll independently. Library width defaults to 250 DIP (200–400), vocabulary width to 300 DIP (200–400), the article width has a 200-DIP minimum, and both splitters are 3 DIP. The native WebView2 host has a fixed 403-DIP minimum. One C# PaneLayout solver coordinates both splitters: moving either edge changes article width first (minimum 200 DIP), then shrinks the opposite outer pane (minimum 200 DIP), and stops when all three reach their minima. WebView2 sends live right-divider requests with screen-anchored pointer coordinates and renders only C#-confirmed width values. CSS has minimum article/host safeguards but no competing dynamic vocabulary-width clamp.
- The two reading-workspace toolbars live in WebView2. Playback controls remain present when disabled.
- Functional SVG icons share the AudioPlayer-style flat circular design: 1024-square viewBox, radius 480, maple-red `#D65B45` background, white foreground, transparent outer margin. Native and browser display sizes remain unchanged; toolbar hover/pressed colors are `#FBECE8`/`#F4D8D0`. Application maple-leaf PNG/ICO are unchanged.
- Highlighting starts off (crossed-eye toggle icon); turning it on shows an eye icon and rounded word backgrounds painted from Range rectangles in a non-interactive layer, without changing article or Script text nodes. The selection-add control follows valid text selection.
- Unmodified Space, Left and Right are exclusive whole-article playback shortcuts throughout the active main window, including WPF and WebView2 controls. They are consumed even when playback is unavailable; Space ignores auto-repeat, arrows repeat, focus and paused state are preserved, and separate dialogs retain normal keyboard behavior. WPF intercepts PreviewKeyDown/PreviewKeyUp; WebView2 intercepts keydown/keyup in the document capture phase, before target controls. Both use existing C# play/pause and relative-seek commands. CSS suppresses browser focus-visible outlines and keyboard-only splitter coloring without moving focus.
- Appearance styles, accent, progress steps and library are persisted; both media players use fixed 100% volume. Pane widths can change during a session but reset to their defaults on restart; window dimensions, active passage and highlighting are not saved. Legacy width fields in settings JSON are ignored.

## Current boundaries

The application reads passages and vocabulary and plays existing audio. OCR, vocabulary enrichment, dictionary lookup, and audio generation are external preparation workflows. There is no paragraph-play button, vocabulary-export UI, account registration/login workflow, or exercise-answering interface.

Matching is literal and case-insensitive for each `word` and its explicit optional `family` forms: `convict` matches `convicted` only when `"convicted"` is listed. Every hit belongs to the canonical `word`, with direct headwords preferred over another entry's family forms. No automatic morphological inference runs, and straight/curly apostrophes are not normalized. ASCII letters, apostrophes, and hyphens participate in word boundaries; digits deliberately do not. Among matches starting at the same position, the longest wins. These details matter when authoring canonical vocabulary entries.

Each playback controller owns one active audio file at a time. Whole-article opening may advance across a fixed sorted list of candidate files; it is not a playback playlist. Playback state belongs to the controller, coordination and relative seeking belong to ReaderPlayback, and the publication timer belongs to WebReaderBridge and native input belongs to MainWindow.Playback. MainViewModel and MainWindow share one ReaderSettings instance; the accent is not separately stored in the view model. Concrete feature repositories and their models are grouped by responsibility; window partials remain with XAML. Small repeated UI operations do not require another abstraction layer.

Tests exercise these production modules directly; the obsolete prototype application has been removed. See [Testing](Testing.md) for categories and their data ownership. Exercise authoring remains in the supplied skills, but there is no exercise runtime module on main.

Keyboard focus remains available for Tab and non-reserved input; the browser intentionally does not display focus-visible outlines. Personal libraries and generated media are not tracked; the fixed test pronunciation pool and required UI assets remain in Git.

The Portable publish profile creates one framework-dependent Windows x64 executable in `dist/ReadArticles/`. Its native host asks users to install the matching Windows Desktop Runtime when missing. Configuration uses `userdata/config/settings.json` under the executable directory. Old schemas reset without migration; corrupted current settings may recover from a verified backup. The window has no explicit maximum size. See [Publishing](Publishing.md) for publishing commands and [Testing](Testing.md) for verification limits.

See [Configuration](Configuration.md) for persisted defaults and [Library Layout](Library-Layout.md) for file naming and audio candidate details.

## Service boundaries

`NotificationService` owns the FIFO queue, severity and error persistence. WebView2 displays at most three floating notification cards and owns their animation, hover and dismissal intents. Browser startup failure is shown on the native terminal screen.

`LibraryRepository.Load` discovers only book directories at the library root, while `ScanDirectoryLevel` enumerates one level and parses direct JSON to identify passage-tagged objects. `LibraryNavigation` coordinates background scans and marshals collection updates to the WPF Dispatcher, guarded by library-session and node-generation tokens. Closing the window or collapsing a folder invalidates pending work.
