# ReadArticles — Development handoff (2026-11-20)

## Verified baseline and PR status

- Repository: [chenjing2000/readarticles](https://github.com/chenjing2000/readarticles); local Windows directory: `C:\Projects\ReadArticles`.
- Stable `main`: `9565fafea6e0f3a956e26cf87c4437355bdd1472` (PR [#19](https://github.com/chenjing2000/readarticles/pull/19) merged). [Windows CI #194](https://github.com/chenjing2000/readarticles/actions/runs/44717115970) passed.
- Current work: `refactor/simplify-reader-code` and [Draft PR #20](https://github.com/chenjing2000/readarticles/pull/20). The code cleanup was committed through `66e0739`; follow-up `6018db1` removes the CS8620 nullable-type warning and updates GitHub Actions to Node.js 24-compatible v6 actions.
- [Windows CI #199](https://github.com/chenjing2000/readarticles/actions/runs/44723293426) passed at `6018db1`: Windows build, Core regression, protected portable publish and release-layout verification. Zero GitHub check annotations. Subsequent documentation-only changes require their own CI verification.
- **Windows real-machine acceptance confirmed by the user:** settings dialog and SVG icons; vocabulary add/delete/move/import; article and word audio; Passage/Script switching. This acceptance covers the cleanup application code through `66e0739`. Later changes through `6018db1` were test/workflow-only; no application behavior changed.
- Local `Suite UI`, `Suite Full`, and direct `Test-PublishedApp.ps1` execution for this cleanup are **not independently confirmed** by the remote CI. Do not report them as executed unless local logs are provided.
- **Do not merge PR #20 or delete branches without separate, explicit user approval.** The branch HEAD and PR state must be checked again before any change.

## Changes in the current cleanup

- Removed obsolete vocabulary action wrappers, unused `ReaderWorkspace.OpenLibrary` and `PassageSelection`, unused WebView2 fallback/setting handler argument, old CSS Highlight cleanup, unused segment availability argument, legacy `ReaderSettings.FontSize` alias, and unused SVG recoloring branches.
- Updated corresponding tests to check `Try*` vocabulary outcomes and to use the current message/appearance APIs.
- Preserved native WPF tree async cancellation, C# authoritative audio state, backup/restore and vocabulary transactional writes. Deliberately **did not** refactor the duplicate tree-generation invalidation or introduce a new abstraction.
- This documentation cleanup removes completed migration logs, dated audits/release notes, a superseded implementation plan and redundant testing-reference content. Their history remains in Git.

## Next actions and verification

1. Start with [project-contract.md](project-contract.md), [Application-Overview.md](Application-Overview.md), [Testing.md](Testing.md), [Publishing.md](Publishing.md), and [project-code-review skill](skills/project-code-review/SKILL.md). The [documentation index](README.md) lists the retained material.
2. Confirm GitHub PR #20, current `refactor/simplify-reader-code` HEAD and its latest CI. The last code-bearing HEAD before this documentation cleanup is `6018db1`. Remote status does **not** prove the local working tree is clean.
3. If additional Windows testing is required, verify `git status --short` before switching or pulling, and use the project's existing checks:

```powershell
cd C:\Projects\ReadArticles
git status --short
git fetch origin
git switch refactor/simplify-reader-code
git pull --ff-only origin refactor/simplify-reader-code

.\tools\Test.ps1 -Suite UI
.\tools\Test.ps1 -Suite Full
# Close the running application before publishing.
.\tools\Publish.ps1
.\tools\Test-PublishedApp.ps1
```

4. No further code cleanup is required solely to close this documentation task. Keep PR #20 Draft pending explicit merge authorization.
