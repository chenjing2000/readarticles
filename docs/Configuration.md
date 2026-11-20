# Configuration

ReadArticles stores its current settings in `userdata/config/settings.json`, inside the portable directory beside the executable. All app-owned config, backups, logs and WebView2 data live under `userdata/`, never in the selected article library.

## Breaking upgrade and recovery

The setting format is structured `Appearance` and `Audio` JSON. Older flat formats and old executable-sibling files are not imported. An obsolete file at the current location is replaced with defaults; no account-specific data is read. Normal current-schema saves are atomic and rotate one verified pre-save backup at `userdata/backups/settings/settings.json.bak`. Malformed current JSON is preserved as a timestamped `.corrupt` file, with verified-backup recovery attempted before default initialization. On I/O failure, settings remain in memory and a warning is reported.

Pane widths are session-only (250/300 DIP default). Window size, active article and highlighting are not saved.

## Native settings dialog

The cog next to 选择图书馆 opens the modal window titled 设置. It uses the built-in WPF Fluent theme, fixed 500 x 540 DIP window size, four 72/140/72/140 DIP functional columns with a 20-DIP center gap, and close-only window chrome. Esc/Cancel discards the draft. Restore Defaults edits the draft; Save applies it once. Eight color properties are each chosen from five evenly spaced 20-DIP balls in a 140-DIP row, without hexadecimal text entry. The first color of each row is the default:

- Title, body and meaning text: #283747, #344054, #3F4750, #315247, #513D4D
- Word head: #B7394D, #9D3E68, #7D50A1, #1F6795, #8B593B
- Phonetics: #0A7785, #256CA5, #38735A, #7657A0, #995431
- Background: #F8F8F6, #F0F3F7, #F7F1E6, #EFF5F0, #F1F0F7
- Selection background (native CSS `::selection`): #DDE8F2, #E5EDE0, #F3E9DC, #EBE5F2, #F2E5E7 (default #DDE8F2)
- Script progress text: #B64550, #A33D30, #A45A2C, #856300, #53713C

Title defaults: 18px bold, 1.7 line height (choices 1.6/1.7/1.8), 16px after. Body defaults: Georgia 15px, 1.7 line height (choices 1.6/1.7/1.8), 16px after, 2em first-line indent. Word head: Georgia 16px semibold with choices 15/16/17. IPA: 14px with choices 13/14/15. Meanings: 15px, 1.5 line height (choices 1.5/1.6/1.7). English font choices are Georgia (default), Microsoft YaHei UI, Nunito, Segoe UI and Noto Sans. Chinese meaning font choices remain Microsoft YaHei, SimSun and KaiTi. Installed-font checks apply only to Georgia, Nunito, Segoe UI and Noto Sans; unavailable choices show a nonmodal "未检测到字体 {name}。" message and keep the previous usable font. Default accent UK, fixed 100% audio output, forward steps 4/7/10/15s, backward 5/8/11/16s. There is no speed control.

## Reading UI and files

The dialog keeps 13-DIP parameter labels and option text, 12-DIP unit labels, 15-DIP section titles, and left-aligned controls. The selection-add glyph size is configurable in the body settings as 0.65, 0.70, 0.75 or 0.80 em (default 0.65), relative to the body font size. It occupies the top-left of a 24 CSS-px click region and sits 1 CSS px right and 0 px below the final selected fragment. A single requestAnimationFrame loop validates the current browser selection before updating the button position at up to approximately 60 Hz while the selection is visible; there is no configurable refresh period. The three bottom rails are 48 DIP high and SVG toolbar buttons are circular and borderless. Rounded vocabulary highlights use Range rectangles without changing text nodes. Script progress tint uses the selected Script color via the existing clip system; stopped audio clears progress and disables seeking.

All article/Script/vocabulary JSON, MP3 and WAV remain in the user-selected external library. Their app-generated backups may be saved under `userdata/backups/vocabulary/` without altering source media locations. Portable publication preserves existing `userdata/` but does not migrate old directory layouts.
