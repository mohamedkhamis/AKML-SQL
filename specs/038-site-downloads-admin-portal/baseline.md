# Baseline — feature 038, measured before any change

**Date**: 2026-09-12
**Measured on**: the live production server (this machine hosts IIS site `AkmlSqlSite` →
`C:\inetpub\akml.khamis.work`, app pool `AkmlSqlSite`, bound to `akml.khamis.work`).
**Method**: `curl` over HTTPS against `127.0.0.1` with a `--resolve` host override, so the numbers are
server-side only and exclude the public network path.

---

## T002 — Build and test baseline

| Check | Result |
|---|---|
| `dotnet build src/AkmlSql.Site -c Debug` | **Succeeded**, 0 warnings, 0 errors |
| `dotnet test tests/AkmlSql.Site.Tests` | **415 passed**, 0 failed, 0 skipped (10 s) |

No pre-existing failures in this suite. Any red after this point is a regression introduced by
feature 038.

---

## T001 — Performance baseline

### Download page (`GET /download`, 21,173 bytes)

| Scenario | Measurement | SC budget | Verdict |
|---|---|---|---|
| **True cold** — `w3wp.exe` killed (exactly what `idleTimeout` does), first request spawns the worker *and* initialises the app | **1.01 s** (ttfb 1.00 s) | < 3 s (SC-001) | ✅ **Passes** |
| Cold after overlapped recycle (`Restart-WebAppPool`) | 0.99 s | < 3 s | ✅ Passes |
| Warm, 8 consecutive requests | 11–24 ms, **p95 ≈ 12 ms** | < 1 s (SC-001) | ✅ **Passes by ~80×** |

### Download start (`GET /dl/{file}`)

| Step | Measurement | SC budget | Verdict |
|---|---|---|---|
| Site responds `302` → GitHub release asset | **15 ms** | — | ✅ |
| GitHub CDN time-to-first-byte (from this server) | **257 ms**, 3.9 MB/s sustained | < 2 s (SC-002) | ✅ **Passes** |

### ⚠️ Conclusion: the reported slowness does not reproduce server-side

Every measured value is comfortably inside its target — cold start by 3×, warm render by ~80×, and
download start by ~8×. The 32 redundant filesystem probes per render (research R1) cost
approximately nothing once the OS file cache is warm.

**The performance premise of US1 is therefore unconfirmed.** The render and cold-start work
(T021/T026/T027) remains worth doing — the code defects are real and the IIS settings are genuinely
wrong — but none of it should be presented as fixing a slowness that the server does not exhibit.
The likely remaining explanations, in order:

1. **Client-side network path.** The installer is served by redirect to GitHub's release CDN, which is
   slow or intermittently unreachable from some regions. From this datacenter it is fast; from the
   reporter's location it may not be. This would be invisible in every server-side measurement.
2. **A different "start"** than the one measured — e.g. the installer's own startup, or the browser's
   SmartScreen hold on an unsigned binary (the download page already warns about this).
3. **A condition not reproduced here**, such as the very first request after a deploy with a cold OS
   file cache.

**Needs the reporter's input to resolve.** Do not "fix" this blind.

---

## T003 — Installer folder vs manifest (research open item 1)

`C:\inetpub\akml.khamis.work-downloads` holds **all 16** installers named by
`wwwroot/releases.json`, totalling **~1.38 GB**.

**Result**: the CDN-blind availability defect (research R2) is **latent, not live** — every release
currently passes the local-file check, so nothing is being wrongly hidden today. The defect is still
real: the moment any installer is cleaned up, that release silently disappears from the page even
though its GitHub CDN link works. Fix stands (T019); severity is "latent bug + wasted probes + 1.38 GB
of disk", not "visitors are seeing a broken page".

---

## T004 — Proxy configuration (research open item 2)

`Analytics:KnownProxies` is `[]` and IIS serves the site in-process with no CDN or reverse proxy in
front. `Connection.RemoteIpAddress` is therefore the real client address.

**Result**: correct as-is. FR-038's stored address will be genuine. Re-check if anything is ever placed
in front of the site.

---

## T005 — Geo database — ⛔ **BLOCKER for the owner's primary requirement**

| Check | Result |
|---|---|
| `GeoLite2-Country.mmdb` in `C:\ProgramData\AKML SQL Site\` | **ABSENT** |
| `visits` rows in the live database | 3,123 — **0 with a country** |
| `downloads` rows in the live database | 43 — **0 with a country** |

The geo database has never been installed, so `GeoLookup` has returned `GeoLocation.Unknown` for every
request the site has ever served. **Country of download — the owner's single most-requested metric —
has zero recorded data, and no amount of portal work changes that.**

**Required action, outside this feature's code**: obtain a MaxMind licence key and run
`scripts/update-geoip.ps1`. Until then every country grouping built in US3 will correctly and
truthfully report 100% "Unknown".

**Note on history**: geo is resolved at *write* time, so the 3,123 existing visits and 43 downloads
can never be given a country retrospectively. Country data will only exist for traffic recorded after
the database is installed.

---

## IIS configuration as found (relevant to T027)

| Setting | Current | Should be | Effect |
|---|---|---|---|
| `AkmlSqlSite` app pool `startMode` | **Already `AlwaysRunning`** — the "(empty)" first reading was a PowerShell artifact: `Get-ItemProperty` renders the enum blank. Confirmed by reading `applicationHost.config` directly. | `AlwaysRunning` | No change needed |
| `processModel.idleTimeout` | **20:00** (default) | `0` | Worker is shut down after 20 min of no traffic |
| Site `preloadEnabled` | **False** | `True` | App is not initialised until a real request arrives |
| `recycling.periodicRestart.time` | 29:00:00 | — | Routine daily recycle; fine |

**Correction (made post-deploy, T028)**: the original "startMode is empty" reading was **wrong**.
`Get-ItemProperty` on an IIS enum property renders blank; `applicationHost.config` shows
`startMode="AlwaysRunning"` and always did. Only `idleTimeout` and `preloadEnabled` were genuinely
misconfigured. The deploy-script comment was corrected to match.

These two settings were genuinely wrong and worth fixing. But with a measured cold start of 1.01 s,
fixing them buys roughly one second on an infrequent request — a real improvement, not the cause of a
multi-second complaint.

---

# After — deployed to production 2026-09-12 20:38

Deployed with `scripts/deploy-site-iis.ps1 -SkipRelease`. **`Deploy-Build-Release.ps1` was deliberately
not run**: this change set contains zero product code, and the full chain would have cut installer
`1.26.0912.2038`, published it as a GitHub release, and rewritten `update-manifest.json` — pushing a
new version to every installed user's updater for a site-only change. `releases.json` and the update
manifest are untouched; the site still advertises `1.26.0910.2248`.

## Schema migration against live data

| | Before | After |
|---|---|---|
| `visits` | 3,138 | 3,140 (traffic continued during deploy) |
| `downloads` | 44 | 44 |
| `not_found` | 2,489 | 2,489 |
| `client_errors` | 2 | 2 |
| `visits` new columns | absent | `ip`, `visitor_id`, `consent` |
| `downloads` new columns | absent | `ip`, `visitor_id`, `consent`, `release_version` |
| `site_settings` table | absent | present |
| Existing rows | — | intact, `ip_hash` preserved |

Backup taken first: `C:\ProgramData\AKML SQL Site\analytics.db.pre-spec038-20260912-203825.bak`.

## Performance

| Scenario | Before | After | Budget |
|---|---|---|---|
| Cold (worker killed) | 1.01 s | **1.045 s** | < 3 s ✅ |
| Warm p95 | ~12 ms | **~11 ms** | < 1 s ✅ |
| `/download` page size | 21,173 bytes | **12,837 bytes** (−39%) | — |
| Releases advertised | 16 | **3** | — |

**The cold-start number did not move, and that is the expected result.** Forcibly killing `w3wp`
bypasses the mechanism that was fixed: `applicationInitialization` warms the app when IIS starts the
worker on its own schedule, not when a request forces a spawn after a kill. With `idleTimeout=0` the
worker no longer dies after 20 idle minutes, so **the fix removes the trigger rather than the cost** —
cold start stops happening rather than getting faster. The measurement above cannot demonstrate that;
only elapsed idle time on the live site can.

The page is 39% smaller because the default visibility setting now advertises 3 releases instead of 16.

## IIS settings, verified post-deploy

| Setting | Before | After |
|---|---|---|
| `startMode` | `AlwaysRunning` (the earlier "empty" reading was a PowerShell enum artifact) | `AlwaysRunning` |
| `processModel.idleTimeout` | 00:20:00 | **00:00:00** |
| `preloadEnabled` | False | **True** |
| `applicationInitialization` | absent | warms `/health` |

## Live functional verification

- `/download` renders **3** release cards (was 16).
- Consent bar renders with Accept and Decline; **no identity cookie before consent** (only the
  antiforgery cookie the form needs).
- `POST /consent choice=granted` → `akml.consent=granted` + `akml.vid=<32 hex>`, both
  `Secure; HttpOnly; SameSite=lax`, 365-day expiry.
- `POST /consent choice=denied` → `akml.consent=denied` and `akml.vid` **deleted**.
- `returnUrl` honoured: a local path round-trips to `Location: /docs`.
- Rate limit active: the 21st rapid POST returns **429**.
- `/privacy` returns 200.
- `/health` reports `settingsLoaded: true`, `releaseVisibility: "Latest 3 releases"`.

### A testing note worth recording

The `returnUrl` round-trip appeared broken during verification — every value redirected to `/`. It was
not broken. **Git Bash (MSYS) rewrites arguments that look like Unix paths**, so `returnUrl=/docs`
reached curl as `returnUrl=C:/Program Files/Git/docs`, and `SafeReturnUrl` correctly rejected it for
containing a colon — the open-redirect guard doing its job. Sending the value pre-encoded
(`returnUrl=%2Fdocs`) shows the correct `Location: /docs`. When testing URL-valued form fields from
Git Bash, pre-encode them or the shell will lie to you.
