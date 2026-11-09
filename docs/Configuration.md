# Configuration

The current configuration file is `userdata/config/settings.json` beside the executable's application directory, independent of the working directory and executable name. A legacy executable-sibling `ReadArticles.settings.json` is migrated on first launch without deleting the original. If both files exist and differ before migration has been recorded, the application prefers the new file and warns.

Settings saves maintain one verified most-recent backup under `userdata/backups/settings/settings.json.bak`. Damaged settings are preserved as timestamped `.corrupt` files; the application attempts recovery from the latest valid backup. Vocabulary edits use atomic writes, a single pre-save backup per source, and external-change detection.

## Ownership and updates

The application owns its configuration format. Publishing updates only the EXE and preserves configuration and other user files. Git and Release assets exclude personal settings and media. Do not delete dist to update a program. Use a writable application folder.

Acceptance tests launch a renamed EXE in a temporary directory, check configuration placement, and use an isolated runtime root to verify the missing-.NET instructions. Application-specific configuration recovery remains covered by the existing service/UI checks.

## Application-specific details

See [Library-Layout.md](Library-Layout.md) and [Testing](Testing.md).
## Detailed settings behavior

The configuration file remains `userdata/config/settings.json` even if the EXE is renamed. The application uses the EXE directory, not the current working directory. If the configuration is absent, it writes defaults during startup. Font, accent, seek-step, library selection, and completed column resizing save immediately; normal exit also saves column widths and volume. Existing unknown fields are ignored. Startup stays centered at rounded 70% dimensions of the work area, with no program-defined maximum width or height and no restored passage.

A damaged JSON configuration is preserved as a timestamped `.corrupt` file. The application attempts to restore the latest valid settings backup before falling back to defaults, with a warning. Read/write failures are reported; the application uses in-memory settings and does not silently move the configuration elsewhere. Put the EXE in a writable folder.

Install the Windows x64 .NET 10 **Windows Desktop Runtime**, 10.0.11 or a later compatible patch. The SDK is not required to run the release. If the required runtime is missing, the native .NET application host presents the installation prompt before WPF starts; the application cannot show its own WPF dialog at that stage. This missing-runtime prompt should be checked on a clean Windows machine.
