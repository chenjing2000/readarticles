# ReadArticles Application Overview

## Design goals

ReadArticles is a local Windows reading and vocabulary application. It displays a directory tree, a rendered English passage, and an editable vocabulary list in three separate panes. Content remains in ordinary JSON and MP3 files so it can be prepared outside the application and inspected without a database.

The design favors explicit responsibilities, concrete services, and small functions. It avoids a dependency-injection container, generic repository framework, event bus, and plugin architecture. A single audio-player interface is retained because it separates native media playback from deterministic playback tests.

## Framework and execution

The application uses C# and WPF, targeting `net10.0-windows` with runtime version `10.0.11` or a later compatible patch. The build SDK is selected by `global.json`, currently `10.0.400`. WPF XAML defines the window, templates, and bindings. Native `RichTextBox`, `FlowDocument`, and `TextPointer` APIs implement passage rendering and selection. A WPF `Adorner` hosts the selection-add button in the same visual tree as the passage. WPF media playback supplies MP3 decoding and progress.

JSON handling uses `System.Text.Json`; files remain the storage layer. The original Python application is a migration reference, not a runtime dependency or required repository file. The WPF reader does not use an embedded browser to render the passage.

## Module responsibilities

| Module | Responsibility |
| --- | --- |
| `App.xaml` / `App.xaml.cs` | Shared brushes and button styles; startup, command-line library selection, and main-window creation |
| `MainWindow.xaml` | Three-pane layout, controls, templates, bindings, and conditional status-row visibility |
| `MainWindow.xaml.cs` | Construct the state model and two independent media players; initialize modules and coordinate shutdown |
| `MainWindow.Library.cs` | Folder selection, tree selection, library restoration, and passage-control assembly |
| `MainWindow.Vocabulary.cs` | Import confirmation, list selection, highlighting, and move/delete UI actions |
| `MainWindow.Playback.cs` | Playback controls and icons, progress timer, drag interaction, and media cleanup |
| `MainWindow.Settings.cs` | Startup size, column widths, font/accent/progress-step menus, and preference persistence |
| `MainWindow.Status.cs` | Conditional warning/error status and six-second expiry |
| `ViewModels/MainViewModel.cs` | Shared state, notifications, and operation errors |
| `ViewModels/MainViewModel.Library.cs` | Prepare and commit library/article changes; resolve account-display context |
| `ViewModels/MainViewModel.Vocabulary.cs` | Prepare word commands and coordinate vocabulary edits and commits |
| `ViewModels/MainViewModel.Playback.cs` | Forward reader commands with the current article and accent |
| `ViewModels/VocabularyEntryViewModel.cs` | A word's display fields, available pronunciation actions, and move/delete commands |
| `Models/` | Passage segments, SIDs, selection ranges, vocabulary entries, navigation nodes, and loaded snapshots |
| `Services/LibraryRepository.cs` | Discover marked book directories and recursively build the directory tree without parsing content |
| `Services/ArticleRepository.cs` | Parse and validate passages; load the matching vocabulary; report missing sentence audio |
| `Services/VocabularyRepository.cs` | Validate vocabulary, derive audio stems, and save with external-change detection |
| `Services/UserRepository.cs` | Read the limited account-display data from the book's existing answer sheet |
| `Services/ReaderWorkspace.cs` | Commit prepared library/article state and replace vocabulary after successful persistence |
| `Services/VocabularyMatcher.cs` | Find non-overlapping literal vocabulary matches with explicit English-word boundaries |
| `Services/AudioResources.cs` | Resolve sentence/word audio and select the first sibling MP3 for whole-article playback |
| `Services/ReaderPlayback.cs` | Select audio targets, enforce article-only controls/progress, cancel pronunciation, and report audio errors |
| `Services/PlaybackController.cs` / `IAudioPlayer.cs` | Playback state, pause/resume, completion, progress access, and stale-event rejection; player boundary |
| `Rendering/ParagraphReader.cs` | Build paragraph documents; maintain SID-to-text ranges, selections, styles, and playback requests |
| `Rendering/SelectionAddAdorner.cs` | Position and show/hide the native circle-plus selection action |
| `Infrastructure/JsonFiles.cs` | JSON helpers, SHA256 fingerprints, and temporary-file replacement |
| `Infrastructure/ResourcePaths.cs` | Relative-path and reparse-point checks |
| `Infrastructure/ReaderSettings.cs` | Load and save application preferences beside the executable |
| `Infrastructure/UiIcons.cs` | Load embedded icons and create vector drawings, including disabled variants |
| `Infrastructure/WpfAudioPlayer.cs` / `RelayCommand.cs` | Native media adapter and small WPF command implementation |

The window and main view model each use partial files to group responsibilities while sharing their existing state. `ReaderPlayback` is a concrete service with no window or view-model dependency. `PlaybackController` operates through `IAudioPlayer`, which includes media-open events and progress. The window reads article progress and sends seek requests through `ReaderPlayback`; native media objects remain behind the player boundary. Repositories own file validation and persistence, the workspace owns committed content, and renderers own text/selection display. No additional service interfaces or generic frameworks are introduced.

## Important data flows

### Library and article loading

```text
Folder selection -> LibraryRepository -> navigation nodes -> collapsed tree
Passage selection -> ArticleRepository -> prepared article and words
                  -> commit workspace -> render paragraphs and vocabulary
```

Library selection clears the active passage and words. Startup restores only a library directory, when available. Selecting an article loads its matching vocabulary. Preparation occurs before committing the article, so a failed switch retains the previous readable article.

### Vocabulary editing

```text
Selection/import/move/delete -> MainViewModel -> ReaderWorkspace
                            -> VocabularyRepository -> checked file replacement
                            -> refresh words -> update passage styles
```

All successful vocabulary commits use one refresh/notification path. Import keeps incoming metadata; later edits retain the current vocabulary metadata. Recognized extension metadata at the vocabulary root, entry, meaning, and audio levels survives serialization.

The JSON `words` array determines list order. Moving swaps neighboring positions; the first and last items disable impossible moves. Delete removes the selected entry. The selection-add button creates a word with declared pronunciation paths, but does not generate definitions or MP3 files.

Delete requires an active, writable vocabulary and an entry belonging to that vocabulary. Requests with no article or a stale entry do nothing. The matching pronunciation is stopped only after the deletion has been saved successfully.

Paragraph documents are rebuilt when the case-insensitive vocabulary set changes. A reorder, capitalization-only change, or duplicate-only set change updates styles without rebuilding the document. Valid text selections and SID mappings are preserved through document updates.

### Playback

```text
UI request -> ReaderPlayback -> audio resource lookup -> PlaybackController -> IAudioPlayer
Media event -> request-token check -> ReaderPlayback state/progress -> UI update
```

The bottom play/pause and stop actions own only the selected sibling MP3. Stop is enabled only while that file is playing or paused; it does not interrupt sentence or word playback. The sibling MP3 has a dedicated player and controller; sentence and word requests share a second independent player and controller. ReaderPlayback coordinates them: starting or resuming the sibling MP3 stops pronunciation, while starting pronunciation pauses the sibling MP3 without losing its position. Finishing pronunciation leaves the sibling MP3 paused. Only one target plays at a time. Old completion/failure callbacks cannot advance or stop a newer target. The bottom progress bar is shown and accepts seeking only for the sibling MP3 while playing or paused. Starting a sentence or word keeps the paused sibling MP3 progress and time label visible. Seeking and stopping affect only the sibling MP3, even while pronunciation is playing. When the sibling MP3 is stopped or has not been started, its progress bar stays hidden.

### Status and lifecycle

Normal startup, loading, and successful vocabulary edits produce no routine status message. Missing resources, invalid content, duplicate additions, and failed saves remain visible for six seconds. Empty status collapses the entire status row, returning its height to all three panes. A new message restarts expiry.

On close, timers stop, playback stops, and both media players are disposed. Normal application windows persist settings; test windows can disable persistence.

## Display and settings

- Startup width and height are the rounded 70% of the primary screen's working area, centered to whole DIP coordinates; previous window dimensions are not restored.
- The tree and title use 11 pt and 12 pt respectively. The shared body/vocabulary setting directly uses 14, 15, or 16 DIP, defaulting to 15 DIP. Settings menus use 10 pt.
- Each paragraph has a two-em first-line indent. All panes scroll independently and column splitters allow resizing.
- The three bottom toolbars share their height and alignment. Playback controls remain present when disabled.
- Highlighting starts off. The selection-add action is a 14 by 14 DIP native vector circle-plus; it follows selection scrolling and layout and hides outside the viewport or when selection is cleared.
- Progress settings provide independent Forward (4/7/10 seconds) and Backward (5/8/15 seconds) selections, defaulting to their first options. Left uses the Backward value to move backward; Right uses the Forward value to move forward. Each option displays seconds explicitly. All Settings menus fit their text and share a light background; selected font, accent, and step options use #87c0ca instead of a checkmark. Space has priority across the window and popup menus and toggles only the sibling MP3 playback/pause.
- Font size, accent, progress steps, library, column widths, and player volume are retained as applicable. Window dimensions, active passage, and highlighting are not saved. Old settings containing these fields remain readable; the unused fields are ignored.

## Current boundaries

The application reads passages and vocabulary and plays existing audio. OCR, vocabulary enrichment, dictionary lookup, and audio generation are external preparation workflows. There is no paragraph-play button, vocabulary-export UI, account registration/login workflow, or exercise-answering interface.

Matching is literal and case-insensitive, not morphological: `convict` does not automatically match `convicted`, and straight/curly apostrophes are not normalized. ASCII letters, apostrophes, and hyphens participate in word boundaries; digits deliberately do not. Among matches starting at the same position, the longest wins. These details matter when authoring canonical vocabulary entries.

Each playback controller owns one audio file at a time. It has no paragraph queue or multi-file playlist. Playback state belongs to the controller, coordination between the two players belongs to ReaderPlayback, and timers and controls belong to MainWindow.Playback. The existing concrete repositories and window partial classes remain together; small repeated UI operations do not require another abstraction layer.

Tests exercise these production modules directly; the obsolete prototype application has been removed. See [Testing](Testing.md) for categories and their data ownership. Exercise authoring remains in the supplied skills, but there is no exercise runtime module on main.

Keyboard focus remains available, but the global focus visual uses an empty template so no control displays a dotted focus outline.

Focusable buttons use a pale teal (#D7EFEB) background while they have keyboard focus, including the settings circle. Focus outlines remain hidden. Local MP3s and personal resource directories are excluded from Git; embedded UI icons remain source dependencies.

The Portable publish profile creates one framework-dependent Windows x64 executable in dist/. Its native host asks users to install the matching Windows Desktop Runtime when missing. Configuration uses the executable stem plus .settings.json in the executable directory; missing settings initialize at startup, recoverable JSON corruption keeps a backup, and settings changes save without waiting for exit. The window has no explicit maximum size. See Getting-Started.md for publishing commands and verification limits.

ReaderSettings owns configuration defaults, recovery, and value normalization on load and save. It rounds stored column widths, bounds the font and volume, and validates accent and progress choices. MainWindow.Settings applies those values to controls and constrains column widths to the current viewport; it does not duplicate configuration validation.

Program-computed window dimensions, column widths and limits, scrollbar width, and paragraph line heights are rounded before they feed subsequent size calculations. Saved LeftWidth and RightWidth are integers. Direct XAML pt font declarations for the title, tree, and settings menus remain unchanged. SVG geometry and text-selection positions are not rounded, preserving drawing and text alignment.
