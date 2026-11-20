# User Guide

## Requirements and launch

Run ReadArticles on Windows with the .NET 10 Windows Desktop Runtime, version `10.0.11` or a later compatible patch. Building also requires a .NET SDK compatible with `global.json`, currently SDK `10.0.400` with `latestPatch` roll-forward. Both the application and its tests target `net10.0-windows`.

The published application lives in `dist/ReadArticles/`. Distribute the portable folder containing `ReadArticles.exe` and retain its `userdata/` directory during updates. The EXE contains the application assemblies, UI assets, and runtime configuration, but not the .NET runtime. On first startup it creates `userdata/config/settings.json` in the portable application directory. Old settings formats are not migrated.

- Launch `dist/ReadArticles/ReadArticles.exe` to restore the last selected library, if one has been saved.
- For another library, use the folder-selection button or pass a library explicitly:

```powershell
.\dist\ReadArticles\ReadArticles.exe --library "C:\Libraries\English"
```

Select the parent directory of marked book directories. Startup and folder selection show only a collapsed tree, not a previous passage or vocabulary. The interface currently mixes Chinese and English labels; this guide names controls by their function in English.

## Basic use

1. Open a library and expand its tree one level at a time.
2. Select a passage leaf to load its text and matching vocabulary.
3. Select passage text within one sentence to reveal the small circle-plus action. Click it to save that selection to the word list. Cross-sentence selections are not addable.
4. Select a vocabulary entry to show move-up, move-down, and delete. These actions write the active companion JSON immediately.
5. Use the highlight toggle to display or hide literal vocabulary matches. Highlighting begins off.
6. Use the import button beside the highlight toggle to replace the active vocabulary with a selected structured JSON file after confirmation.
7. Click the cog icon next to 选择图书馆 to open the centered 设置 dialog. Adjust title, body, word, IPA, meanings, background colors, default accent and seek steps; click 保存 to commit all changes, 取消 to discard, or 恢复默认 to reset the entire draft.

Right-clicking anywhere in the vocabulary pane has no action or popup menu. Use the bottom buttons to move or delete the selected word.

The bottom play/pause and stop buttons control only the first sorted MP3 directly beside the passage, or first sorted WAV when no MP3 exists. Stop is enabled while the sibling audio file is playing or paused, and does not interrupt sentence or word playback. Right-click text for sentence playback; use the word's pronunciation buttons for word playback. The progress slider remains visible at all times, but permits dragging only during playback or pause, and seeks only within that audio file. The sibling audio file uses its own player; sentence and word audio share a second player. Starting sentence or word playback pauses the sibling audio file and preserves its position, progress bar, and time label. Starting or resuming the sibling audio file stops the second player. Finishing pronunciation does not automatically resume the sibling audio file. If the sibling audio file has not been started or has been stopped, the slider remains visible but disabled at zero. Left and Right seek in the sibling audio file from any control or application popup menu, regardless of focus. The native settings dialog provides Forward (4, 7, 10, 15 seconds) and Backward (5, 8, 11, 16 seconds), with the first option selected by default. Left moves backward by the selected Backward value and Right moves forward by the selected Forward value. Each numeric option explicitly displays its unit in seconds. The native settings dialog stages all changes until Save; the choices are saved between sessions. Space takes priority over other controls and toggles only the sibling audio file playback/pause; holding Space does not repeatedly toggle playback. Other controls do not handle these two keys. When no sibling audio file is active, Left and Right do nothing and cannot seek sentence or word audio. Dotted keyboard focus outlines are hidden for all controls, including popup menus.

A time label between the progress bar and play button displays current time/total duration; no 文章音频 caption is shown. Each value uses mm:ss through one hour (including 60:00 at exactly one hour) and hh:mm:ss beyond one hour. The label updates during playback and seeking, stays visible while paused, and resets to zero on Stop.

Successful ordinary actions are quiet. Floating informational and warning notifications expire after approximately five seconds of active display time, with expiry paused on hover. Errors remain until dismissed; no status row is added or removed. Missing physical audio does not prevent passage reading.

Use a separate working copy of a library for interactive editing experiments. Do not alter `tests/TestData/` directly while tests are running; deterministic tests use this fixed source data and operate on temporary copies.

## Maintenance and configuration

Use [Testing](Testing.md) for test and acceptance commands, [Publishing](Publishing.md) for release commands, and [Configuration](Configuration.md) for settings behavior and recovery.

## Troubleshooting

| Symptom | Check |
| --- | --- |
| Library has no books | Select the parent library; confirm `book.json` exists in each immediate child book directory |
| A leaf fails to open | Discovery checks filenames only; confirm that JSON is a valid passage, not unrelated metadata or standalone vocabulary |
| Words do not load automatically | Confirm the exact sibling `<title>.vocabulary.json` name, JSON structure; audio paths are inferred and old declarations are ignored |
| Vocabulary is unreadable and cannot be overwritten | Repair the companion, then reopen the article; the reader protects damaged vocabulary from replacement |
| Saving reports an external edit | Reopen the article to load the current fingerprint before editing again |
| Bottom play reports no audio | Place a real whole-article MP3 or WAV directly beside the passage JSON; sentence audio is not a fallback |
| A different whole-article file plays | The first case-insensitively sorted sibling audio file wins; keep one intended file or rename deliberately |
| A sentence or word cannot play | Check inferred files under audio/ (words) or audio_sentences/ (SIDs), nonzero size, valid MP3/WAV encoding, and first-entry stem eligibility; MP3 decode failure does not retry WAV |
| A canonical word is not highlighted | Matching is literal, not inflection-aware; see the vocabulary preparation guide's surface-form discussion |
| Blank text has no right-click audio | Blank passages are identified by valid [[n]] body placeholders and disable sentence audio; exercise answering is not implemented |
| Settings do not restore a passage or old window size | This is intentional: library-only startup and a centered window (70% work-area height; width at least 796 DIP when it fits) are the current behavior |

For content authoring, use [Image to Passage](skills/image_to_passage/SKILL.md), [Passage Segmentation](skills/passage_segment/SKILL.md), and [Vocabulary Preparation](Vocabulary-Preparation.md), then follow [Library Layout](Library-Layout.md).

The Portable profile places temporary compiled output inside obj/publish-build instead of creating a separate release copy under bin. Only dist is the release directory; artifacts and old staging releases can be removed. Building or running tests can still create bin and obj caches. Do not delete dist or its settings while cleaning caches. Left and right pane widths are session-only (defaults 250/300 DIP; limits 200–400 DIP) and are not saved in configuration; the article has a 200-DIP minimum and each splitter is 3 DIP. Typography settings are stored under the structured Appearance JSON object, using px for browser font sizes.

## Floating messages and lazy navigation

Messages appear above the middle pane's playback bar. Informational and warning notices fade away after approximately five seconds, with the timer paused while hovered. Errors remain until closed. Each message has a close button; an overflow indicator retains additional unacknowledged errors.

Opening a library lists only books. Use the plus button to expand one level at a time; clicking folder text merely selects it. A folder may be expandable even when it contains no direct JSON file. Collapse and re-expand to refresh added, deleted or renamed files. Collapsing a directory does not unload the current passage or stop audio.
