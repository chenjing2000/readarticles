# Configuration

The current configuration file is ReadArticles.settings.json beside the actual executable. It follows the EXE directory rather than the working directory. For AudioPlayer, ReciteWords, and ReadArticles, renaming the EXE changes the .settings.json stem; MdictViewer always uses dictionaries.json.

Malformed settings are backed up with a timestamp before defaults are saved. Vocabulary edits use atomic writes and detect external changes.

## Ownership and updates

The application owns its configuration format. Publishing updates only the EXE and preserves configuration and other user files. Git and Release assets exclude personal settings and media. Do not delete dist to update a program. Use a writable application folder.

Acceptance tests launch a renamed EXE in a temporary directory, check configuration placement, and use an isolated runtime root to verify the missing-.NET instructions. Application-specific configuration recovery remains covered by the existing service/UI checks.

## Application-specific details

See [Library-Layout.md](Library-Layout.md) and [Testing](Testing.md).
## Detailed settings behavior

The settings name follows the executable: renaming the EXE to `Reader.exe` makes its settings path `Reader.settings.json`. The application uses the EXE directory, not the current working directory. If the configuration is absent, it writes defaults during startup. Font, accent, seek-step, library selection, and completed column resizing save immediately; normal exit also saves column widths and volume. Existing unknown fields are ignored. Startup stays centered at rounded 70% dimensions of the work area, with no program-defined maximum width or height and no restored passage.

A damaged JSON configuration is preserved as a timestamped `.bak` file and replaced with defaults, with a warning. Read/write failures are reported; the application uses in-memory settings and does not silently move the configuration elsewhere. Put the EXE in a writable folder.

Install the Windows x64 .NET 10 **Windows Desktop Runtime**, 10.0.11 or a later compatible patch. The SDK is not required to run the release. If the required runtime is missing, the native .NET application host presents the installation prompt before WPF starts; the application cannot show its own WPF dialog at that stage. This missing-runtime prompt should be checked on a clean Windows machine.
