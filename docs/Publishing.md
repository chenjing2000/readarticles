# Publishing

From the repository root on Windows with .NET 10 SDK, close the application and run:

```powershell
.\tools\Publish.ps1
```

The script calls ReadArticles/Properties/PublishProfiles/Portable.pubxml with PublishProfile=Portable. All four projects use Release, win-x64, framework-dependent single-file publication, bundled native loaders, no trimming or compression, and no distributed PDB/XML documentation. The result is dist/ReadArticles.exe. End users need .NET 10 Windows Desktop Runtime x64, not just the regular runtime.

The script builds in a unique staging directory under dist, excludes build symbols and XML documentation, verifies that exactly one EXE remains, and atomically replaces an existing EXE on the same volume. Failed builds, extra dependencies, and locked executables stop publication. Existing settings and arbitrary user files are retained. Temporary staging files are removed. Build caches in bin/obj are not deleted. No parallel build, test, or publish should use the same project while publication runs.

If Windows blocks scripts, use a process-only policy override:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tools\Publish.ps1
```

Test the publication safeguards without building the application:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tools\Test-Publish.ps1
```

The test substitutes only the external dotnet build and exercises the actual script against temporary files, including failed builds, unexpected DLLs, locked EXEs, and configuration preservation. Validate the real EXE separately; see [health check](Health-Check-2026-10-13.md).

For GitHub distribution, push a unique portable-* tag pointing at a reviewed and tested commit. .github/workflows/release.yml runs the same safeguards and publishing script on Windows, then creates a Release with the generated EXE and docs/Release-Notes.md. Ordinary commits do not trigger a Release. Update the release notes before publishing a new tag. EXEs are Release assets; dist, user settings, libraries, and media remain outside Git.