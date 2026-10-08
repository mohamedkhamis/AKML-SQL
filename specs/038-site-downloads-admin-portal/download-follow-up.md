# Download click follow-up — 2026-10-07

The owner authorized fixing the download button and previously specified direct
GitHub EXE downloads, cached latest-release lookup, nonblocking statistics, a brief
duplicate-click guard, version/size display and browser verification.

Root cause reproduced before editing: Home → Download through Blazor enhanced
navigation leaves `/dl/{file}` in the anchor because the page-local script does
not run after the DOM swap. Blazor fetches that URL as a page; the browser does
not receive a native download navigation. A local Chromium trace recorded a
`fetch` request and zero download events. Opening a new window bypasses interception.

Implementation decisions:

- Render the asset URL in SSR and explicitly disable enhanced navigation on every
  installer link. Load a single delegated handler from the application shell.
- Allow native link activation. Queue one beacon before navigation, without
  waiting for its response. Use keepalive fetch only if beacon enqueue fails.
- Suppress repeat activation of the same filename for two seconds, with busy state.
- Fetch the GitHub latest-release API in a background service every 15 minutes;
  never fetch it while handling a click. Keep the last valid asset metadata in
  memory and an atomic cache file beside the analytics DB. Seed from the deployed
  manifest if no persisted cache exists. Require the expected installer filename
  and a direct HTTPS asset URL in this repository; reject draft/prerelease data.
- Show the asset's version and size, and only its own checksum when supplied.
  Preserve admin release visibility and previous-release links.
- Accept statistics for API-discovered assets even before the checked-in manifest
  contains them; attribute their version from trusted cached metadata.

Constitution check: Site-only behavior and matching tests, existing analytics and
consent policy, no desktop changes, no Git mutations or deployment. Required gates:
Site tests, full solution Release MSBuild, theme drift and local browser download
checks. A direct GitHub link still works without JS; statistics require JS on that
path. Browser beacons/analytics remain best-effort, not proof of completed installation.

API reference: [GitHub latest release and asset schema](https://docs.github.com/en/rest/releases/releases#get-the-latest-release).

Completed: 900 Site tests, full solution Release build and theme drift checks passed.
33 online browser checks and 8 after a restart with GitHub API access unavailable
passed. Actual installer filename, byte count and SHA-256 matched API metadata.
See [results and limitations](../../reports/site-audit-2026-10-06/04-download-fix.md).
