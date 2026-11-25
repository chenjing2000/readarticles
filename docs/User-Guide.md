# User Guide

## Requirements and launch

Run ReadArticles on Windows with the .NET 10 Windows Desktop Runtime, x64, version `10.0.11` or a later compatible patch, and Microsoft Edge WebView2 Evergreen Runtime. Building also requires a .NET SDK compatible with `global.json`, currently SDK `10.0.400` with `latestPatch` roll-forward. Both the application and its tests target `net10.0-windows`.

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
3. Select Passage or Script text within one sentence to reveal the small circle-plus action. Click it to save that selection to the word list. Cross-sentence selections are not addable.
4. Select a vocabulary entry to enable the applicable move-up, move-down and delete buttons. These actions write the active companion JSON immediately.
5. Use the highlight toggle to display or hide case-insensitive literal matches for each `word` and its explicitly listed `family` forms. Highlighting begins off.
6. Use the import button beside the highlight toggle to replace the active vocabulary with a selected structured JSON file after confirmation.
7. Click the refresh icon between 选择图书馆 and the cog to reopen the current library; this stops audio and clears reading/selection. If the root no longer exists, select a new library. Click the cog icon to open the centered 设置 dialog. Adjust title, body, word, IPA, meanings, background colors, default accent and seek steps; click 保存 to commit all changes, 取消 to discard, or 恢复默认 to reset the entire draft.

## Passage and Script

Use the document/code icon immediately left of the highlight toggle to switch to an optional same-stem `<name>.script.json` and back. The companion is loaded on demand, not at startup. Missing or invalid Script leaves Passage and article playback unchanged. The Script may have different text and title from Passage; its timestamped foreground follows article audio, including seeks, and clears on Stop or natural completion. Untimed or unaligned units remain readable without progress coloring. Vocabulary and article-audio lookup remain tied to the original Passage. See [Library Layout](Library-Layout.md) for the format.

## Vocabulary and playback

A vocabulary entry may have an optional `family` list containing inflected spellings actually found in the article (for example, `"word": "go", "family": ["went", "goes"]`). Selecting `go` also highlights and locates `went` and `goes` when present. The right pane displays only the canonical `word`, not its family forms; pronunciation also continues to use the canonical word. The reader does not automatically infer missing forms. See [Vocabulary Preparation](Vocabulary-Preparation.md) for authoring rules.

Right-clicking anywhere in the vocabulary pane has no action or popup menu. Use the bottom buttons to move or delete the selected word.

The bottom play/pause and stop buttons control only the whole-article recording, selected as described in [Library Layout](Library-Layout.md). The progress slider is always visible, enabled only when article audio is seekable. Starting pronunciation pauses article playback without losing position; resuming article playback stops pronunciation. Finishing pronunciation leaves the article paused. Stop affects only the article channel and resets its progress.

### Main-window keyboard shortcuts

| Key | Action |
| --- | --- |
| Space | Start, pause or resume the article; holding the key toggles only once |
| Left | Seek backward by the configured interval |
| Right | Seek forward by the configured interval |

These unmodified keys are reserved throughout the active main window, regardless of focus in the native tree or WebView2. They are consumed even when the action is unavailable. Repeated arrows accumulate; seeking while paused leaves audio paused. Other controls do not activate or resize, and browser focus outlines and keyboard-only splitter coloring are suppressed. Focus stays in place, the selected-word marker remains, and mouse operations still work. Tab and non-reserved input remain available; Enter activates a vocabulary row. Separate dialogs and modified key combinations keep their normal behavior. Set intervals in [Configuration](Configuration.md).

A time label between the progress bar and play button displays current time/total duration; no 文章音频 caption is shown. Each value uses mm:ss through one hour (including 60:00 at exactly one hour) and hh:mm:ss beyond one hour. The label updates during playback and seeking, stays visible while paused, and resets to zero on Stop.

Successful ordinary actions are quiet. Missing physical audio does not prevent passage reading. See Floating messages below for notification behavior.

Use a separate working copy of a library for interactive editing experiments. Do not alter `tests/TestData/` directly while tests are running; deterministic tests use this fixed source data and operate on temporary copies.

## Maintenance and configuration

Use [Testing](Testing.md) for test and acceptance commands, [Publishing](Publishing.md) for release commands, and [Configuration](Configuration.md) for settings behavior and recovery.

## Troubleshooting

| Symptom | Check |
| --- | --- |
| Library has no books | Select the parent library; confirm `book.json` exists in each immediate child book directory |
| An article is missing from the tree | Confirm readable, syntactically valid JSON with a top-level exact `filetype: "passage"`; collapse and re-expand its folder |
| A leaf fails to open | Check the complete Passage schema, including title, sentence arrays and placeholders; a passage-tagged JSON can still fail full validation |
| Script cannot be opened | Check the same-stem Script companion and timestamp format; correct the file and retry the music toggle |
| Reader shows a native failure screen | Check WebView2 Evergreen Runtime and portable directory write permission; restart |
| Words do not load automatically | Confirm the exact sibling `<title>.vocabulary.json` name, JSON structure; audio paths are inferred and old declarations are ignored |
| Vocabulary is unreadable and cannot be overwritten | Repair the companion, then reopen the article; the reader protects damaged vocabulary from replacement |
| Saving reports an external edit | Reopen the article to load the current fingerprint before editing again |
| Bottom play reports no audio | Place a decodable whole-article M4A, MP3 or WAV directly beside the Passage JSON; the player tries sorted candidates until one opens |
| A different whole-article file plays | The player tries M4A, then MP3, then WAV; within each format it sorts filenames case-insensitively (ordinal tie-break) and uses the first file that opens. Keep one intended recording or rename deliberately |
| A word cannot play | Check files under audio/ and the first-entry word stem, nonzero size and valid MP3/WAV encoding; vocabulary MP3 decode failure does not retry WAV |
| A canonical word or its inflected form is not highlighted | Enable highlights and verify that the exact headword is listed as `word`, or that the observed inflection is explicitly included in its `family` array. Matching is case-insensitive but does not infer additional forms; see [Vocabulary Preparation](Vocabulary-Preparation.md) |
| Blank text display | Blank passages retain valid [[n]] placeholders and render as blanks; exercise answering is not implemented |
| Settings do not restore a passage or old window size | This is intentional: library-only startup and a centered window (70% work-area height; width at least 796 DIP when it fits) are the current behavior |

For content authoring, use [Image to Passage](skills/image_to_passage/SKILL.md), [Passage Segmentation](skills/passage_segment/SKILL.md), and [Vocabulary Preparation](Vocabulary-Preparation.md), then follow [Library Layout](Library-Layout.md).

For generated build folders, recovery EXEs and safe cleanup, see [Publishing](Publishing.md). Pane-width and typography options are described in [Configuration](Configuration.md).

## Floating messages and lazy navigation

Messages appear above the middle pane's playback bar. Informational and warning notices fade away after approximately five seconds, with the timer paused while hovered. Errors do not auto-expire. Each message has a close button; long messages can be expanded. At most three messages are visible, with additional messages queued until slots free up. A successful article or library switch clears the old context's notifications.

Opening a library lists only books. Use the plus button to expand one level at a time; clicking folder text merely selects it. A folder may be expandable even when it contains no direct JSON file. Collapse and re-expand to refresh added, deleted or renamed files. Collapsing a directory does not unload the current passage or stop audio.
