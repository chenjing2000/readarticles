# ReadArticles — Development handoff (2026-11-20)

## Current GitHub baseline

- Repository: [chenjing2000/readarticles](https://github.com/chenjing2000/readarticles); local project directory is typically `C:\Projects\ReadArticles`.
- `main` baseline: `9565fafea6e0f3a956e26cf87c4437355bdd1472` (PR [#19](https://github.com/chenjing2000/readarticles/pull/19) **merged** with squash). The merged commit passed [Windows CI #194](https://github.com/chenjing2000/readarticles/actions/runs/44717115970).
- Previous settings, Nunito, selection and audio progress controls, including four code-review edge cases, passed the user's Windows real-machine acceptance on 2026-11-20.
- Active cleanup branch: `refactor/simplify-reader-code`, created from that `main` baseline. **Do not merge this cleanup into `main` without separate explicit approval.** Confirm the actual HEAD, PR and CI status on GitHub before working.
- GitHub remote state does not establish the local checkout or working-tree status; check `git status --short` first.

## Current cleanup scope

- Remove unreachable WebView2 handler fallback, unused message parameter and legacy CSS Highlight cleanup.
- Remove obsolete WPF-style vocabulary action wrappers and the unused synchronous workspace library opener. Keep the existing authoritative `Try*` vocabulary operations and their result checks.
- Remove an unused segment-availability parameter, the test-only `FontSize` alias and unused SVG recoloring options.
- Keep asynchronous navigation/session invalidation, C#-owned audio timing, word edit safety, backup/restore, and native WPF settings behavior unchanged.
- Update affected tests; verify build, CI and Windows UI/Full/manual acceptance before requesting a merge. Do not delete other branches.

## Project contracts and validation

- Read [AGENTS.md](../AGENTS.md), [project-contract.md](project-contract.md), [project-code-review/SKILL.md](skills/project-code-review/SKILL.md), [Configuration.md](Configuration.md), [Application-Overview.md](Application-Overview.md), [Testing.md](Testing.md) and [Publishing.md](Publishing.md). Historical PR #19 specifics are documented in its merged commits and these project specifications; do not mistake the old feature branch for the current development branch.
- Application: WPF/.NET 10 Windows x64, one WebView2 reading host; Passage and Script are separate modes. Article and audio sources are read-only; vocabulary edits preserve unknown JSON metadata, version checks and verified backups. Settings are EXE-adjacent `userdata/config/settings.json`; pane widths are session-only.
- Run the existing Windows checks from the repository root (against isolated test data):

```powershell
cd C:\Projects\ReadArticles
git status --short
git fetch origin
git switch refactor/simplify-reader-code
git pull --ff-only origin refactor/simplify-reader-code

.\tools\Test.ps1 -Suite UI
.\tools\Test.ps1 -Suite Full
.\tools\Publish.ps1
.\tools\Test-PublishedApp.ps1
```

- Always distinguish passed GitHub CI from local Windows UI, real-media, and published-app checks that have not actually been executed for the new commit.
