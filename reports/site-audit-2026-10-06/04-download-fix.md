# Download button fix — 2026-10-07

## Root cause

The download page loaded `download-track.js` inside its own body. That script
attached handlers only to links present when it executed and changed their `/dl/`
hrefs to GitHub URLs. After Home → Download through Blazor enhanced navigation,
the script did not initialize the replacement page. The remaining `/dl/{file}`
anchor was intercepted as an enhanced **fetch**, not a browser download navigation.

Reproduced against the previous locally published build: the anchor still pointed
to `/dl/AKMLSQLSetup-1.26.1005.1332.exe`, the click generated a fetch request, and
Chromium reported **zero download events**. Opening a new window bypasses enhanced
navigation, which explains the owner's working workaround. Direct page loads and
navigation between pages could therefore behave differently.

## Fix

- Installer anchors render their direct GitHub asset URLs in the server HTML and
  set `data-enhance-nav="false"`. The primary action does not navigate to a GitHub
  release webpage or serve installer bytes from IIS. Historical local-only links
  remain supported with native navigation.
- `download-track.js` loads once from `App.razor` and delegates clicks from the
  document. It survives enhanced navigation and repeated visits without attaching
  duplicate handlers. A two-second guard suppresses duplicate activation of the
  same filename and exposes “Starting download…” plus accessible busy/disabled state.
- The handler queues a statistics beacon before allowing native download navigation.
  It never awaits the response; keepalive fetch is used only if beacon enqueue fails.
  The existing analytics sink persists the event asynchronously. API-discovered
  versions are accepted by the count endpoint without waiting for a manifest deploy.
- A background service fetches the GitHub latest-release API every 15 minutes,
  validates the expected uploaded installer EXE and repository asset URL, and caches
  version, date, size, URL and checksum. It retains the last usable response on API
  failure and atomically saves it beside the analytics database as
  `latest-github-release.json`. The deployed manifest seeds a cold start.
- The button shows version and file size. Checksums come from that asset's metadata;
  an unavailable checksum is omitted rather than borrowing another release's hash.
  The admin settings preview uses the same combined latest/history list, with the
  owner's visibility rule preserved.

Main files: `Components/App.razor`, `Components/Pages/Download.razor`,
`wwwroot/js/download-track.js`, `Releases/LatestGitHubRelease.cs`,
`Analytics/DownloadEndpoint.cs`, `Components/Pages/Admin/AdminSettings.razor`
(under `src/AkmlSql.Site/`).

The API's `browser_download_url`, size and digest fields are documented in
[GitHub's release API](https://docs.github.com/en/rest/releases/releases#get-the-latest-release).

## Verification

- **900/900 Site tests passed**, including ten new cases for cache persistence,
  API failures/timeouts/malformed responses, unsafe or wrong assets, prereleases,
  direct SSR links, version/size, dynamic-version counting and history/visibility.
- **Full solution Release MSBuild passed** with existing warnings. All three theme
  drift checks passed. Published and exercised a local HTTPS app in Production mode
  using a separate synthetic analytics database; no production deployment or data changes.
- **33 online browser checks passed**: installed Chrome, installed Edge, Playwright
  Firefox and Chrome Pixel 7 emulation each passed enhanced navigation/revisit,
  direct load, rapid double-click, slow asset, slow statistics, beacon fallback,
  keyboard activation and no-JavaScript download. Each JS case verified exactly one
  asset request, one count request and one persisted download event. Double-clicks
  produced one accepted event. Slow statistics did not delay the download.
- The 33rd check downloaded the **actual GitHub installer**, without intercepting its
  response, and verified:
  - `AKMLSQLSetup-1.26.1006.2232.exe`, matching the API's latest release at verification;
  - **56,179,550 bytes** and Windows `MZ` signature;
  - SHA-256 **`ec71f6966bf9a07b0974760f03e3dab6854c8a085cfb94be2bf3db6fd7e4f4dd`**,
    matching GitHub's asset digest;
  - one persisted event in the isolated database. The installer was not executed.
- **8 additional Chrome checks passed after a server restart with GitHub API access
  deliberately unavailable**. The server log confirmed connection failure; the
  persisted URL/version/size remained usable and all click/count scenarios passed.
- Browser harness: `tests/AkmlSql.Site.E2E.Tests/download-click.local.cjs` (hardcoded
  loopback host to prevent accidental production runs). Evidence and local fixtures:
  ignored `artifacts/download-fix/`, including TRX, build/publish logs, original repro,
  API metadata, browser JSON results, screenshots and the downloaded test artifacts.

## Limits

- This is a source fix, not a production deployment. An already-open browser page
  needs a reload after deployment to receive the global script.
- Mobile checks used touch/device emulation, not a physical phone. Production IIS
  behavior was not re-tested. At 320px the primary button remains usable; an existing
  history-table overflow remains outside this click fix.
- Statistics are download requests, not completed transfers or successful installs.
  They remain best-effort when the browser/network/database is unavailable. Direct
  no-JavaScript and context-menu downloads work but do not execute the click beacon.
- On first-ever startup with both the API unavailable and no saved cache, the
  deployed manifest is the fallback. Size is shown only if asset metadata or a local
  file provides it. Cached metadata can be stale during an outage; deletion of the
  upstream asset itself cannot be repaired by a cached URL.
- Security remediation outside the previously authorized admin fixes remains pending.
