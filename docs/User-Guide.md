# User Guide

## Requirements and launch

Run ReadArticles on Windows with the .NET 10 Windows Desktop Runtime, version `10.0.11` or a later compatible patch. Building also requires a .NET SDK compatible with `global.json`, currently SDK `10.0.400` with `latestPatch` roll-forward. Both the application and its tests target `net10.0-windows`.

The published application lives in `dist/`. Distribute only `ReadArticles.exe`. It contains the application assemblies, UI assets, and runtime configuration, but not the .NET runtime. On first startup it creates `ReadArticles.settings.json` beside the executable.

- Launch `dist/ReadArticles.exe` to restore the last selected library, if one has been saved.
- For another library, use the folder-selection button or pass a library explicitly:

```powershell
.\dist\ReadArticles.exe --library "C:\Libraries\English"
```

Select the parent directory of marked book directories. Startup and folder selection show only a collapsed tree, not a previous passage or vocabulary. The interface currently mixes Chinese and English labels; this guide names controls by their function in English.

## Basic use

1. Open a library and expand its tree one level at a time.
2. Select a passage leaf to load its text and matching vocabulary.
3. Select passage text within one sentence to reveal the small circle-plus action. Click it to save that selection to the word list. Cross-sentence selections are not addable.
4. Select a vocabulary entry to show move-up, move-down, and delete. These actions write the active companion JSON immediately.
5. Use the highlight toggle to display or hide literal vocabulary matches. Highlighting begins off.
6. Use the import button beside the highlight toggle to replace the active vocabulary with a selected structured JSON file after confirmation.
7. Open the bottom-left settings circle for the shared body/vocabulary font size (14, 15, or 16 DIP; default 15) and sentence-playback accent.

Right-clicking anywhere in the vocabulary pane has no action or popup menu. Use the bottom buttons to move or delete the selected word.

The bottom play/pause and stop buttons control only the first MP3 directly beside the passage. Stop is enabled while the sibling MP3 is playing or paused, and does not interrupt sentence or word playback. Right-click text for sentence playback; use the word's pronunciation buttons for word playback. The bottom progress bar appears only while the sibling MP3 is playing or paused and seeks only in that file. The sibling MP3 uses its own player; sentence and word audio share a second player. Starting sentence or word playback pauses the sibling MP3 and preserves its position, progress bar, and time label. Starting or resuming the sibling MP3 stops the second player. Finishing pronunciation does not automatically resume the sibling MP3. If the sibling MP3 has not been started or has been stopped, its progress bar remains hidden. Left and Right seek in the sibling MP3 from any control or application popup menu, regardless of focus. Settings > Progress Bar provides Forward (4, 7, 10 seconds) and Backward (5, 8, 15 seconds), with the first option selected by default. Left moves backward by the selected Backward value and Right moves forward by the selected Forward value. Each numeric option explicitly displays its unit in seconds. All Settings menus share a light background and fit their text. Selected font, accent, and step options use a #87c0ca background with no checkmark. The two selections are saved between sessions. Space takes priority over other controls and toggles only the sibling MP3 playback/pause; holding Space does not repeatedly toggle playback. Other controls do not handle these two keys. When no sibling MP3 is active, Left and Right do nothing and cannot seek sentence or word audio. Dotted keyboard focus outlines are hidden for all controls, including popup menus.

A time label between the progress bar and play button displays current time/total duration. Each value uses mm:ss through one hour (including 60:00 at exactly one hour) and hh:mm:ss beyond one hour. The label updates during playback and seeking, stays visible while paused, and appears or disappears with the progress bar.

Successful ordinary actions are quiet. Warning/error status appears for six seconds and then disappears with its layout row. A new warning restarts that interval. Missing physical audio does not prevent passage reading.

Use a separate working copy of a library for interactive editing experiments. Do not alter `ReadArticles.Tests/Fixtures/Library` directly; deterministic tests depend on that fixed data.

## Maintenance and configuration

Use [Testing](Testing.md) for test and acceptance commands, [Publishing](Publishing.md) for release commands, and [Configuration](Configuration.md) for settings behavior and recovery.

## Troubleshooting

| Symptom | Check |
| --- | --- |
| Library has no books | Select the parent library; confirm `book.json` exists in each immediate child book directory |
| A leaf fails to open | Discovery checks filenames only; confirm that JSON is a valid passage, not unrelated metadata or standalone vocabulary |
| Words do not load automatically | Confirm the exact sibling `<title>.vocabulary.json` name, JSON structure, and audio-path declarations |
| Vocabulary is unreadable and cannot be overwritten | Repair the companion, then reopen the article; the reader protects damaged vocabulary from replacement |
| Saving reports an external edit | Reopen the article to load the current fingerprint before editing again |
| Bottom play reports no MP3 | Place a real whole-article MP3 directly beside the passage JSON; sentence audio is not a fallback |
| A different whole-article file plays | The first case-insensitively sorted sibling MP3 wins; keep one intended file or rename deliberately |
| A sentence or word cannot play | Check the exact declared file, nonzero size, valid MP3 encoding, and safe relative path |
| A canonical word is not highlighted | Matching is literal, not inflection-aware; see the vocabulary preparation guide's surface-form discussion |
| Blank text has no right-click audio | Blank passages intentionally omit sentence audio; exercise answering is not implemented |
| Settings do not restore a passage or old window size | This is intentional: library-only startup and 70% centered window size are the current behavior |

For content authoring, use [Image to Passage](skills/skills-python/image_to_passage/SKILL.md), [Passage Segmentation](skills/skills-python/passage_segment/SKILL.md), and [Vocabulary Preparation](Vocabulary-Preparation.md), then follow [Library Layout](Library-Layout.md).

The Portable profile places temporary compiled output inside obj/publish-build instead of creating a separate release copy under bin. Only dist is the release directory; artifacts and old staging releases can be removed. Building or running tests can still create bin and obj caches. Do not delete dist or its settings while cleaning caches. LeftWidth and RightWidth are rounded in configuration, and FontSize is stored directly as an integer DIP value.
