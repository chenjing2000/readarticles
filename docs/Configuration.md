# Configuration

ReadArticles stores its current settings in `userdata/config/settings.json`, inside the portable directory beside the executable. All app-owned config, backups, logs and WebView2 data live under `userdata/`, never in the selected article library.

## Breaking upgrade and recovery

The setting format is structured `Appearance` and `Audio` JSON. Older flat formats and old executable-sibling files are not imported. An obsolete file at the current location is replaced with defaults; no account-specific data is read. Normal current-schema saves are atomic and rotate one verified pre-save backup at `userdata/backups/settings/settings.json.bak`. Malformed current JSON is preserved as a timestamped `.corrupt` file, with verified-backup recovery attempted before default initialization. On I/O failure, settings remain in memory and a warning is reported.

Pane widths are session-only (250/300 DIP default). Window size, active article and highlighting are not saved.

## Native settings dialog

The cog next to 选择图书馆 opens the modal window titled 设置. It uses the built-in WPF Fluent theme, fixed 440 x 690 DIP window size, four 72/120/72/120 DIP columns and close-only window chrome. Esc/Cancel discards the draft. Restore Defaults edits the draft; Save applies it once. Six color properties are each chosen from five uniformly distributed 20-DIP balls, without hexadecimal text entry. The first color of each row is the default:

- Title, body and meaning text: #283747, #344054, #3F4750, #315247, #513D4D
- Word head: #B7394D, #9D3E68, #7D50A1, #1F6795, #8B593B
- Phonetics: #0A7785, #256CA5, #38735A, #7657A0, #995431
- Background: #F8F8F6, #F0F3F7, #F7F1E6, #EFF5F0, #F1F0F7

Title defaults: 18px bold, 1.6 line height, 16px after. Body defaults: 15px, 1.5 line height, 16px after, 2em first-line indent. Word head: 16px semibold with choices 15/16/17. IPA: 14px with choices 13/14/15. Meanings: 15px, 1.4 line height. Default accent UK, volume 65%, forward steps 4/7/10/15s, backward 5/8/11/16s. There is no speed control.

## Reading UI and files

The three bottom rails are 48 DIP high. Icon toolbar buttons have circular borderless surfaces. UK and US IPA plus speaker each wrap as a single unit. Rounded vocabulary highlighting is painted behind immutable text using `Range.getClientRects()`; it does not add elements to article text. Script progress is #2475A8 blue via its existing SVG clip system. Stop disables seeking and clears Script color.

All article/Script/vocabulary JSON, MP3 and WAV remain in the user-selected external library. Their app-generated backups may be saved under `userdata/backups/vocabulary/` without altering source media locations. Portable publication preserves existing `userdata/` but does not migrate old directory layouts.
