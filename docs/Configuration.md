# Configuration

The authoritative, executable-adjacent configuration file is **ReadArticles.config.json**. The filename does not change when the EXE is renamed. It uses structured Appearance (title, body, vocabulary, phonetics, meanings) and Audio (accent, volume, seek-step) sections plus the selected library. The file is created with defaults on first launch if no valid prior configuration is available.

## Migration and recovery

- When the new file is missing, the app first imports the previous userdata/config/settings.json, then the older executable-adjacent ReadArticles.settings.json. Both originals remain unchanged.
- A new configuration always takes precedence if present. A malformed new file is moved to a timestamped .corrupt sibling, and the app tries the last verified settings backup before creating defaults. IO/access failures show a warning and run with in-memory settings rather than silently changing location.
- Saves compare serialized contents and skip no-op writes. Changed saves atomically replace the JSON and maintain one verified most-recent backup at userdata/backups/settings/settings.json.bak.
- Left/right pane widths are session-only (250/300 DIP defaults), never saved in the configuration. Window size, active article, and highlights are not persisted either.

## Native settings dialog

The single native WPF modal dialog opens from the library header next to 选择图书馆. All appearance and audio choices are staged: Cancel/Escape discards them, 恢复默认 resets all controls in the temporary draft, and 保存 validates six-digit hexadecimal colors, saves the JSON once, then applies the C# settings snapshot to WebView2. On failure the dialog remains open. The former bottom-left native context menu and browser HTML settings panel are removed.

Fonts prefer Nunito Sans → Microsoft YaHei → Arial for English; meanings use Microsoft YaHei (with SimSun and KaiTi fallback). Defaults: title 18px bold, body 15px with 1.3 line height/12px paragraph after/2em indent, word head 16px semibold #B7394D, IPA 15px #0A7785, meaning 15px 1.3 line height, reading background #F8F8F6. A color field displays six hex digits plus an unbordered live swatch. Accent is UK by default, with forward steps 4/7/10/15s, backward 5/8/11/16s, and volume at 65%. Playback speed remains unchanged: no variable-speed option or additional playback engine.

UK and US pronunciations each keep their transcription and sound.svg button in one wrapping unit. The two units may wrap between them, without explicit newline or horizontal scrollbar. All SVG controls are embedded; WPF uses the existing native SVG resource renderer, and WebView2 loads the embedded browser icons through its restricted local resource host.

## Packaging

The published EXE remains in dist/ReadArticles/ and must be placed in a writable directory. Preserve **both ReadArticles.config.json and userdata/** when replacing or moving the executable; publish replaces only the EXE and does not erase user files. Existing vocabulary backups, logs and WebView2 profile remain under userdata/. For testing and recovery see [Testing](Testing.md) and [Publishing](Publishing.md). For the application data contract see [Library Layout](Library-Layout.md).

## Runtime requirements

The portable application needs Windows x64 .NET 10 Windows Desktop Runtime (10.0.11 or a compatible later patch) and WebView2 Evergreen. If the required runtime is missing, the native .NET host must report it before any WPF window can open.
