# WebView2 reader migration — implementation log

Date: 2026-11-18  
Branch: `feature/webview2-reader` (based on `feature/script-reading-mode`, not `main`).

## Approved end state

One WebView2 renders the middle reader, title, right vocabulary pane, both footers, selection affordances, and notifications. C#/WPF keeps the left navigation tree, all file and audio business logic, settings and durable state. No React, Node, HTML audio, or second WebView2.

## First checkpoint: opt-in skeleton

The first code checkpoint introduces a **read-only pilot**, not a replacement of the production UI:

- The existing WPF reader is still the default, including existing tests and user-data paths.
- `--webview-pilot` initializes one WebView2 overlay spanning the center/right columns.
- HTML, CSS and JS are embedded in the application assembly and served from a strict, fixed URL whitelist through `WebResourceRequested`. Nothing is loaded from the user library or an external CDN.
- The WPF host initializes WebView2 with `userdata/cache/webview2/` as the browser user data folder. No cache directory is created for normal, non-pilot startup.
- JavaScript sends `{version:1,type:"ready",pageId}`; C# responds with a snapshot (`initialize`), and JavaScript acknowledges `rendered`. The overlay is made visible only after acknowledgement.
- C# sends only trusted article, vocabulary, playback and notification snapshots. The pilot's buttons are visibly disabled. All mutation messages are deliberately rejected until the later business-bridge stage.
- Article changes rotate the session ID. The host rejects unrecognized message types and wrong page IDs for follow-up messages, and refuses messages from other document URLs.
- Initialization failure or WebView2 process failure falls back to the original WPF reader and reports a native error.

**Not yet implemented:** Script mode in WebView2, DOM vocabulary matching and highlights, live selection/add, user actions, continuous 33 ms playback coloring, notification animations, production settings and input. The pilot intentionally renders a first-pass, non-interactive Passage preview only. The existing WPF implementation remains authoritative.

## Windows acceptance results supplied on 2026-11-18

On commit `ec41182`, the user ran:

- `dotnet build ReadArticles.Tests -c Release`: **passed**.
- `--webview-pilot-only`: **1 passed / 0 failed**, verifying bundled assets.
- `Test.ps1 -Suite Core`: **10 groups passed / 0 failed**.
- `Test.ps1 -Suite UI`: **12 groups passed / 1 group failed**, with two WPF assertions:
  1. `WindowChecks.cs`: the old test counted the hidden WebView2 pilot as a fourth root-grid pane. Commit `3a16b73` filters out collapsed elements; rerun required.
  2. `ScriptChecks.cs`: `Script vocabulary highlight changed pixels: 0`. This is a pre-existing failure on the old WPF Script renderer, unchanged by the WebView2 pilot. The failing assertion is preserved until the new WebView2 renderer has a real visual test.
- `Test.ps1 -Suite Full`: **120 checks passed / 2 failed** (the same two UI assertions).

The earlier `Publish.ps1` and `Test-PublishedApp.ps1` passed, but **only for the default WPF launch path**. There is no submitted run log yet for `--webview-pilot`, including the published EXE.

**Do not claim complete UI/Full acceptance.** The collapsed-element test fix has been committed but not yet retested on the user's Windows desktop. The original Script pixel test remains failing.

## Direct `--window-only` invocation caveat

On the later Windows run at commit `21c43e6`, the previously failing floating-notification layout check **passed** after excluding collapsed pilot elements. The direct command

`dotnet run --project ReadArticles.Tests -c Release -- --window-only`

then reported an unrelated failure at `ReaderWindowChecks.cs:31`: the fixed article's first vocabulary entry requires both UK and US audio eligibility. The standalone invocation uses `tests/TestData/Library` without the sibling recordings from `tests/TestData/Audio`. The canonical `tools/Test.ps1 -Suite UI` stages both sources under a temporary library using `Prepare-TestWorkspace` and sets `READARTICLES_TEST_LIBRARY`; the same assertion passed in the earlier full UI run with that isolated setup. **Do not weaken the audio eligibility assertion** or edit source fixtures to address this.

The old WPF Script visual assertion continues to report `Script vocabulary highlight changed pixels: 0`; the new browser rendering has not yet undergone its own pixel acceptance. Re-run the canonical UI and Full suites after code changes, and test `--webview-pilot` in an interactive Windows process.

## Local Windows acceptance — required

From a clean checkout of this branch:

```powershell
dotnet restore ReadArticles
dotnet build ReadArticles -c Release
dotnet run --project ReadArticles.Tests -c Release -- --webview-pilot-only
.\tools\Test.ps1 -Suite Core
.\tools\Test.ps1 -Suite UI
.\tools\Test.ps1 -Suite Full
.\tools\Publish.ps1
.\tools\Test-PublishedApp.ps1
```

Then launch the pilot explicitly:

```powershell
dotnet run --project ReadArticles -c Release -- --webview-pilot
```

Select a **working-copy** library. Confirm the WebView2 title, complete Passage text and vocabulary list appear, article navigation updates the page, and the default launch (without flag) still uses WPF. Confirm independent scrolling, DPI behavior and no unexpected file writes. The pilot buttons are intentionally not functional; use default WPF launch for editing and audio.

Next, run the **published** EXE with `--webview-pilot` from a copied directory, renamed EXE and another working directory. Verify HTML/CSS/JS are all available without loose web files. Verify missing Evergreen Runtime and read-only folder failures show WPF fallback. Check that `dist/ReadArticles/userdata/` is preserved.

The SDK addition may affect native Loader publishing. **Do not waive or silently relax the exactly-one-EXE protection in `tools/Publish.ps1`**. If publishing fails, record the exact output and resolve packaging separately.

## Pending next work

1. Check hosted Windows Core CI and the local build/publish results. Fix only verified compatibility failures.
2. Complete page lifecycle/recovery, request IDs and explicit action results with source/session/revision validation (stage 2).
3. Port Passage DOM, vocabulary matching and selection preserving old semantics (stage 3).
4. Connect the existing C# vocabulary save path with authoritative success/failure replies (stage 4).
5. Port Script timings and article/pronunciation controls (stage 5).
6. Migrate all active toolbars, CSS splitter, settings, Toast and keyboard handling (stage 6).
7. Replace old WPF structural UI tests with WebView2 rendering tests; manually test audio/visual behavior; delete old visual implementation only after complete acceptance (stage 7).

No CI, interactive UI, audio or publication result is claimed by this document. This branch must not be merged to `main` without explicit user approval.
