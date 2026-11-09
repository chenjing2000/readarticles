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

`userdata/` is created on demand when the application saves settings or backups; it is never cleared during publishing. The application still uses the external library directory selected by the user. Keep the entire portable `ReadArticles/` folder together when moving it, and place it in a directory where the current Windows user has write permission. The machine must have **.NET 10 Windows Desktop Runtime x64**, patch 10.0.11 or later, not just the standard .NET runtime.

## Safe publishing

1. Publish the new EXE into a unique `dist/.publish-*` staging directory.
2. Strip PDB/XML build artifacts and reject unexpected packaged files.
3. Check the existing application EXE for incompatible file locks before making changes.
4. If `dist/userdata/` exists and `dist/ReadArticles/userdata/` is absent, copy the legacy userdata directory to a staging folder, verify every copied file by SHA-256, and install the copied directory without deleting the original.
5. Preserve a legacy `dist/ReadArticles.settings.json` alongside the new executable if it exists, so the existing application-level settings migration can read it.
6. Create a short portable `README.txt` if absent.
7. Install the new EXE, retaining the previous executable in the portable directory under a `ReadArticles.exe.previous-*` name when replacing an existing release.
8. Remove only owned temporary staging paths after checking that they contain no reparse points.

A failed build, unexpected dependency, locked EXE, or copy-validation failure does not replace the installed EXE. Updating does not overwrite existing `userdata/`, the user's external library, or arbitrary files in `dist/`.

The earlier `dist/ReadArticles.exe` and `dist/userdata/` are deliberately **not deleted** by migration. After validating the new portable installation and verifying the user's configuration, the user may manually archive or remove obsolete root-level files. Do not delete them before checking the new installation. Previous EXE copies may also be removed manually after a version has been verified.

The publish script validates the package structure and safe EXE replacement, **not** actual WPF startup. Always run the published-app check on an interactive local Windows desktop before declaring the new version usable.

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

`Test-PublishedApp.ps1` tests a copied executable from a directory with spaces and a different working directory, checks that settings are created under `userdata/config/`, and checks the native missing-.NET instructions. Run `tools/Test.ps1 -Suite Core` in CI, and local `-Suite UI` and `-Suite Full` for interactive and media behavior.

The `dist/` directory is local build output and is not committed. The project does not automatically install .NET, create an installer, or update itself online.
