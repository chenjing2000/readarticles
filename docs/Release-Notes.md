# Portable release - 2026-10-13

Windows x64, framework-dependent single EXE. Install .NET 10 Windows Desktop Runtime x64 before running.

- Unified Portable.pubxml and tools/Publish.ps1; output: dist/ReadArticles.exe.
- Staged publication validates a single EXE and preserves existing user configuration and other user files.
- No bundled .NET runtime, trimming, single-file compression, separate PDBs, or XML documentation.
- Source, documentation, and publishing regression tests are included in this tag; user data and local media are excluded.

See [health check](Health-Check-2026-10-13.md) for the actual local validation results and limitations. Release builds also run the publishing safeguard tests; they do not replace the local UI/audio checks.