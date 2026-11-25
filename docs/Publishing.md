# Publishing

From the repository root on Windows with a .NET 10 SDK, close ReadArticles and run:

```powershell
.\tools\Publish.ps1
```

The script uses `ReadArticles/Properties/PublishProfiles/Portable.pubxml` (Release, win-x64, framework-dependent single-file, no trimming). The installed portable layout is:

```text
dist/
  ReadArticles/
    ReadArticles.exe
    README.txt
    userdata/
      config/settings.json
      backups/
```

`userdata/config/settings.json` is created on first launch if absent; `userdata/` also holds backups, WebView2 data and logs and is never cleared during publishing. The application still uses the external library directory selected by the user. Keep the entire portable `ReadArticles/` folder together when moving it, and place it in a directory where the current Windows user has write permission. The machine must have **.NET 10 Windows Desktop Runtime x64**, patch 10.0.11 or later, not just the standard .NET runtime, and Microsoft Edge WebView2 Evergreen Runtime.

## Safe publishing

1. Reject reparse points in the destination and staging paths or their existing ancestors; do not traverse userdata. Publish the new EXE into a unique `dist/.publish-*` staging directory.
2. Strip PDB/XML build artifacts and reject unexpected packaged files.
3. Check the existing application EXE for incompatible file locks before making changes.
4. Keep existing `dist/ReadArticles/userdata/` untouched. Old directory structures and sibling settings are not migrated.

A failed build, unexpected dependency, or locked EXE does not replace the installed EXE. Updating preserves existing `userdata/`, README.txt and the user's external library; the prior EXE is retained as a recovery copy. If publication fails because the EXE is in use, close the application and retry.

Legacy `dist/ReadArticles.exe`, `dist/userdata/`, and sibling settings are neither imported nor deleted. Archive old files separately once the new installation is verified. Previous EXE copies may also be removed manually **only after** the new version has passed both `Test-PublishedApp.ps1` and the interactive final-EXE checklist in [Testing](Testing.md). Keep at least the newest verified recovery EXE; archive or delete older `.previous-*` copies as needed (a practical manual target is one or two recovery versions). The publish script deliberately does **not** auto-purge unverified recovery files or touch `userdata/`.

The publish script validates the package structure and safe EXE replacement, **not** a full interactive WebView2 reading-session check. Always run the published-app check on an interactive local Windows desktop before declaring the new version usable.

## Tests and runtime verification

Use the process-only execution-policy override if required:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tools\Publish.ps1
```

Run the filesystem-level publication tests (the script mocks only `dotnet publish`, not filesystem changes):

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tools\Test-Publish.ps1
```

After a real publish:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tools\Test-PublishedApp.ps1
```

`Test-PublishedApp.ps1` tests a copied executable from a directory with spaces and a different working directory, checks that `userdata/config/settings.json` is created inside the renamed executable directory, and checks the native missing-.NET instructions. Run `tools/Test.ps1 -Suite Core` in CI, and local `-Suite UI` and `-Suite Full` for interactive and media behavior.

The Portable profile puts intermediate compiled output under `ReadArticles/obj/publish-build/`. Normal build/test commands can also create bin/obj caches. The `dist/` directory is local build output and is not committed; never remove its userdata or only verified recovery EXE when cleaning caches. The project does not automatically install .NET, create an installer, or update itself online.
