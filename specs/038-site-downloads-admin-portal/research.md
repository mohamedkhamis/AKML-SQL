# Phase 0 Research: Site Download Experience and Full Admin Portal

**Feature**: `038-site-downloads-admin-portal` | **Date**: 2026-09-12

All findings come from reading the current code on `master` (merge `53c39ba`). Nothing here is
inferred from the symptom report alone; each root-cause claim names the file and the mechanism, and
each carries a stated confirmation step, because a plausible cause that turns out to be wrong is
worse than an open question.

---

## R1. Why the download page is slow — cause 1: the page probes the filesystem per release, twice

**Decision**: Resolve the release list once per render, after the visibility setting has bounded it,
with availability and size resolved in the same single pass.

**Finding**:

`Components/Pages/Download.razor:159` declares

```csharp
private List<Release> PreviousReleases =>
    Manifest.Releases.Skip(1).Where(Availability.IsDownloadable).ToList();
```

as an **expression-bodied property**, not a cached field. The page evaluates it twice — once at
`@if (PreviousReleases.Count > 0)` and again at `@foreach (var release in PreviousReleases)`. Each
evaluation calls `ReleaseAvailability.IsDownloadable` per release, which reaches
`DownloadEndpoint.ResolveFilePath` → `Path.GetFullPath` ×2, `Directory.Exists`, `File.Exists`.

`wwwroot/releases.json` currently holds **16 releases**. The latest one additionally costs an
`IsDownloadable` call plus `DisplaySize` → `new FileInfo(path).Length`. So a single render performs
roughly **32 existence probes and one size stat**, on a page whose entire job is to show one button.

**Rationale**: The fix is not a cache with an invalidation problem — it is to stop doing the work
twice and to stop doing it for releases the page will not show. Applying the US2 visibility setting
*before* the probe loop makes the cost O(N shown), not O(N published), which is what SC-003 asks for.
`ReleaseAvailability` stays scoped (per request) so the existing DL-001 contract — "files are dropped
into the folder between deploys, so presence is resolved per render" — is preserved exactly.

**Alternatives considered**:

- *Singleton cache with a file watcher*: rejected. Introduces invalidation and a watcher for a
  problem that disappears once the list is bounded and evaluated once.
- *Move size into `releases.json`*: rejected explicitly by the existing design ("read from the file
  rather than added to the manifest schema so it cannot disagree with what visitors actually
  download") and that reasoning still holds.

**Confirmation step**: a test that counts probe invocations through a substitutable availability
probe and asserts the count equals the number of releases rendered, once.

---

## R2. Why the download page is slow — cause 2 (and the reported "bug"): local-file presence gates CDN-served releases

**Decision**: A release is downloadable when it has a CDN mirror **or** its local file is present.
Only local-only releases need a filesystem probe.

**Finding**: This is the most likely explanation for the "have bug" half of the report.

Every one of the 16 entries in `releases.json` carries **both** a local `downloadUrl`
(`downloads/AKMLSQLSetup-*.exe`) **and** a `cdnUrl` (a GitHub release asset). The runtime paths
disagree about which one matters:

- `ReleaseAvailability.IsLocal` returns true for anything whose `downloadUrl` starts with
  `downloads/` — true for all 16, regardless of the CDN mirror.
- `IsDownloadable` therefore returns `!IsLocal || ResolveFile(...) is not null`, i.e. **it demands
  the local file exists** for all 16.
- But `DownloadEndpoint.Handle` checks `ResolveCdnUrl` *first* and 302s to GitHub without ever
  touching the local folder, and `download-track.js` rewrites the link straight to the CDN.

So the page's "can I offer this?" answer and the endpoint's "will I serve this?" answer have drifted
apart — precisely the disagreement `ReleaseAvailability`'s own summary says it exists to prevent.
If the downloads folder on the server (`C:\inetpub\akml.khamis.work-downloads`) does not hold all 16
installers — likely, at ~66 MB each — then the page either hides working releases or, if the latest
is among the missing, falls through to **"No public release available yet"** while the CDN link works
perfectly.

**Rationale**: Fixing this removes a class of silent failure *and* removes most of the probe work in
R1, since a CDN-backed release needs no filesystem check at all.

**Alternatives considered**:

- *Probe the CDN URL over the network*: rejected outright. A per-render HTTP call to GitHub puts a
  third-party network hop on the site's primary page — the exact thing `GeoLookup` is offline to
  avoid.
- *Treat `cdnUrl` as authoritative and drop local serving*: rejected. Local serving is the fallback
  when GitHub is unreachable, and `/dl` counting depends on the request reaching this server first.

**Confirmation step**: on the server, list the downloads folder and compare against the manifest;
the new admin Releases section (FR-034) makes this permanently visible rather than a one-off check.

---

## R3. Why the site is slow to start — cold start is not configured against

**Decision**: Set `idleTimeout=0` and `preloadEnabled=true` with an `applicationInitialization` warm
request in `scripts/deploy-site-iis.ps1`, and move startup *maintenance* off the critical path.

**Finding**: Two compounding issues.

*IIS side*: `scripts/deploy-site-iis.ps1:240` sets `startMode = AlwaysRunning`, and that is the only
app-pool tuning present — grep finds no `idleTimeout`, no `preloadEnabled`, no
`applicationInitialization`. `AlwaysRunning` starts the **worker process** on IIS start, but the
default `idleTimeout` of 20 minutes still shuts it down after idleness, and without `preloadEnabled`
the **ASP.NET Core application** is not initialised until a real request arrives. On a low-traffic
product site, the visitor arriving from a search result is very often the one paying for the restart.

*Application side*: `Program.cs` deliberately does heavy work eagerly at boot (a good decision — it
fails a broken deploy fast rather than on the first request):

- `DocsContentService.Build` parses **17 markdown files, ~400 KB**, renders them through Markdig +
  Markdown.ColorCode, and builds the full-text search index.
- `ReleasesManifest.Load` parses `releases.json`.
- `AnalyticsStore` opens SQLite, runs the schema migration (per-column `ALTER TABLE` probes, `UPDATE`
  backfills, index creation), then `Prune(400)` and `ClearSameOriginReferrers(...)` run **inline
  before the first request can be served**.

**Rationale**: Keep the fail-fast *validation* (resolve the singletons — that is Principle II
thinking and it catches broken deploys), but the two **maintenance** operations are not validation.
`Prune` deletes old rows and `ClearSameOriginReferrers` is a one-time historical repair that is
idempotent and corrects nothing after its first run. Neither needs to block the first page view.
Moving them into a hosted service that runs after startup removes them from cold start without
weakening the deploy check. The IIS settings then mean cold start is rarely paid at all.

**Alternatives considered**:

- *Lazy docs parsing*: rejected. It would move the cost to the first `/docs` request and break the
  `/health` probe's ability to report a broken corpus at deploy time.
- *ReadyToRun / AOT publish*: not pursued now. It is a real lever, but it is a build-pipeline change
  with its own risks, and it should be measured only after the free wins above — measuring first is
  cheaper than guessing.
- *Deleting `ClearSameOriginReferrers`*: tempting (it is a spent one-time repair) but out of scope
  for this feature and not this plan's call to make.

**Confirmation step**: quickstart measures cold and warm page time before and after; `/health`
continues to report singleton state.

---

## R4. Where owner-editable settings live

**Decision**: A `site_settings` table in the existing `analytics.db`, fronted by a
`SiteSettingsStore` singleton that caches the current value in memory and refreshes it on save.

**Rationale**:

- The database file already exists, is created on demand, and the deploy script already ACLs its
  folder for the app pool identity — a new store inherits all of that with no deploy change.
- Writes are transactional, so FR-042's "validated before applied" and the concurrent-edit edge case
  ("last save wins, and is recorded") fall out naturally.
- It is backed up together with the metrics it governs.
- WAL mode is already enabled, so a second connection for settings reads alongside the analytics
  writer without contention.

**Alternatives considered**:

- *Write to `appsettings.json`*: rejected. Writing to the deployed config file triggers an IIS app
  restart on every settings change — turning a one-second toggle into a cold start, i.e. directly
  re-creating the US1 defect.
- *A separate `site-settings.json` with temp-file+rename*: workable and matches the desktop
  `ConfigManager` idiom, but it adds a second storage mechanism, a second path to ACL, and a second
  thing to back up, for data that is one row. Principle V favours extending the existing store.
- *Reusing `AnalyticsStore`'s connection*: rejected. Settings and metrics have different lifetimes
  and lock profiles; sharing the `_gate` would make a settings read wait behind a metrics write.

**Cache invalidation**: the download page reads the setting on every render, so an uncached SQLite
read would sit on the hot path. The singleton holds the current value and replaces it on save —
single-process, single-administrator, so no cross-process invalidation problem exists.

**Failure behaviour** *(clarified 2026-09-12, FR-016a)*: because the value is resolved into memory,
the render path never touches the store, so a runtime read failure cannot reach the public page at
all. If the value cannot be loaded at startup, the site serves the documented default (`LatestN` = 3)
and records the failure in the logs, the `/health` report and the portal. Failing startup — which
would be consistent with the existing analytics-folder fail-fast — was considered and **rejected**:
that check guards a folder at deploy time, whereas this would turn a narrow, unlikely read failure
into total site downtime, a strictly worse outcome than advertising three releases.

---

## R5. Consent model and identity cookie

**Decision**: Two independent cookies. A consent cookie recording the choice, and an identity cookie
issued **only** after consent is granted.

| Cookie | Purpose | Set when | Lifetime | Flags |
|--------|---------|----------|----------|-------|
| `akml.consent` | Remembers granted/denied | On the visitor's choice | 365 days | `HttpOnly`, `Secure`, `SameSite=Lax` |
| `akml.vid` | Persistent individual id (GUID) | Only when consent is granted | 365 days (matches FR-037) | `HttpOnly`, `Secure`, `SameSite=Lax` |

**Rationale**:

- Separating them is what satisfies FR-046: a visitor who declines still gets a remembered decision
  and is never asked again, without being given a tracking identifier to carry it. Storing the
  refusal *in* the identity cookie would mean issuing the thing they refused.
- `HttpOnly` because no client script needs either value — the CSP already forbids inline script, and
  the banner is a form POST, so nothing in the browser reads these.
- The lifetime matches the retention period deliberately: a cookie that outlives the data it points
  at is a dangling identifier.

**Flow**: `ConsentMiddleware` resolves state onto `HttpContext.Items` before `VisitTrackingMiddleware`
reads it. `POST /consent` sets the choice and redirects back to the originating path (validated as a
local path — an open redirect here would be a real vulnerability). The banner renders from the
resolved state, so it appears only for `Unknown`.

**Presentation** *(clarified 2026-09-12, FR-043a/FR-043b)*: shown to **every** `Unknown` visitor with
no geo-gating — so the consent path must not consult `GeoLookup`, and a missing geo database cannot
affect whether consent is asked for. The bar is **non-blocking**: it obscures and delays nothing, and
the download completes without touching it. Accept and Decline are equally prominent; an explicit
dismissal records `Denied`; silence leaves `Unknown`.

**Accepted consequence**: those two choices together produce the smallest individuals list of any
combination considered — visitors who ignore the bar are never tracked and are asked again next
visit. This is a deliberate trade in favour of US1's download conversion. It is written into the
contract (C2.7) specifically so a future reader does not "fix" the low coverage by making the prompt
blocking or by inferring consent from silence.

**Alternatives considered**:

- *JavaScript-set cookie*: rejected. The site is no-JS-first (FR-009) and the strict CSP means any
  inline approach would need a policy relaxation — paying a security cost for a page reload.
- *Server-side consent record keyed by IP*: rejected as self-defeating — it would require storing the
  address of people who refused to have their address stored.
- *Treat "no choice yet" as consent until they decline*: rejected. FR-043 requires affirmative
  consent before the identifier is issued.

---

## R6. Storing full addresses without losing what already works

**Decision**: Add `ip` (TEXT NULL) and `visitor_id` (TEXT NULL) columns to `visits` and `downloads`
through the existing `AddColumnIfMissing` migration. **Keep** `ip_hash` and `ip_prefix`.

**Rationale**:

- `AnalyticsStore.InitializeSchema` already performs additive, idempotent column migration precisely
  so "an installed database gains them in place — the deployed site has months of history and must
  not be reset". The new columns use that same path; no migration framework is introduced.
- `ip_prefix` stays because it is the honest grouping key for "one noisy network", and it is the only
  network-level value available for non-consenting rows.
- `ip_hash` stays because `ResolveSessionId` depends on it *and* because it is what allows a
  non-consenting visitor to be counted once per day without storing anything about them. Removing it
  would degrade aggregate accuracy for exactly the people who refused.
- Both new columns are nullable and written **only when consent is granted**, which is what makes
  FR-044 enforceable at the storage layer rather than only in UI logic.

**Release version attribution**: add `release_version` to `downloads`, resolved at write time from
`ReleasesManifest` by filename. Resolving at read time was rejected — the manifest is mutable and a
release removed from it would retroactively orphan its historical downloads.

**De-identification at the retention boundary**: `Prune` currently deletes whole rows past
`RetentionDays` (default 400). The spec wants identifiable detail gone at 365 days while aggregate
totals still reconcile (SC-008, and the "aggregate may outlive identifiable detail" assumption). So
two boundaries: `IdentifiableRetentionDays` (365) nulls `ip` and `visitor_id` in place; the existing
`RetentionDays` continues to delete rows outright. Nulling rather than deleting is what keeps the
country and version totals for an old period intact after the personal detail is gone.

---

## R7. Individuals: query shape and the honesty problem

**Decision**: Aggregate over `visitor_id` with a left join from visits to downloads, plus explicit
reporting of what the aggregate cannot see.

**Rationale**: The straightforward query is easy; the trap is presenting it as complete. Three facts
must be surfaced alongside the list, or the owner will read it wrongly:

1. **Non-consenting traffic is invisible at person level** (FR-047). Reported as a stated share, not
   omitted.
2. **A cleared cookie creates a new individual.** The portal must not read that as growth, and
   FR-022b forbids re-linking by address or user-agent to "fix" it — that would reconstruct the
   tracking the visitor cleared.
3. **Address grouping and person grouping legitimately disagree.** Several people behind one
   carrier-grade NAT address are several individuals and one address. Both numbers are correct; the
   portal shows them as separate figures and never as a single "users" number.

**Indexes**: `ix_visits_visitor (visitor_id, utc)`, `ix_downloads_visitor (visitor_id, utc)`,
`ix_downloads_day_country (day, country)`. Existing day indexes are retained. At low-thousands of
rows per day these are precautionary rather than necessary, which is the right time to add them.

---

## R8. Portal shell, navigation and range propagation

**Decision**: A new `AdminLayout.razor` with persistent navigation; the reporting range continues to
travel as the `?days=` query-string value, propagated onto every navigation link.

**Rationale**: `AdminDashboardOptions` already documents why the window is a query-string value —
"keeps the dashboard static-SSR (no interactive render mode) and makes a chosen range a shareable,
bookmarkable URL". That reasoning extends unchanged to a multi-section portal: each nav link carries
the current range, so FR-032 is satisfied with no session state, no cookie, and no interactivity.

**Alternatives considered**:

- *Range in a cookie or session*: rejected. It would break bookmarkability, add a cookie for an
  administrator convenience, and make a screenshot ambiguous about its own range — which FR-032's
  second clause explicitly guards against.
- *Interactive Server render mode for the portal*: rejected. It would introduce a circuit, a
  websocket and a CSP change to the one part of the site that least needs live updates; the spec's
  Out of Scope section already rules out live dashboards.

**Charts**: reuse the existing pure-CSS bar mechanism (precomputed `.bar-h-*` bucket classes in
`site.css`) that the current dashboard uses to satisfy the strict CSP. No chart library.

---

## R9. Testing strategy for criteria that resist unit testing

**Decision**: Gate structural causes with deterministic tests; gate wall-clock criteria in the
quickstart.

| Criterion | How it is gated |
|-----------|-----------------|
| SC-001 / SC-002 (cold/warm timing) | Manual measurement in `quickstart.md`, plus the deploy smoke test. Timing assertions in CI are flaky and would get muted. |
| SC-003 (flat cost vs release count) | **Deterministic test**: substitutable availability probe, counted; assert probes == releases rendered, and that a 100-entry manifest with "latest only" probes at most once. |
| SC-011 (no unauthenticated disclosure) | Test enumerating every portal route + export through `AdminBranchMiddleware.RequiresChallenge`. |
| SC-014 (refusal leaves no trace) | Store-level test: after a denied-consent visit, assert `ip` and `visitor_id` are null for that row. |
| SC-015 (no address on unauthenticated surfaces) | Test asserting the public pages, sitemap and error responses contain no address; plus a log-template review. |
| SC-013 (notice matches behaviour) | Test asserting the privacy page states the configured retention number and the actual storage mode, read from the same options the store uses — so drift fails the build. |

**Rationale**: This mirrors how the repo already treats non-deterministic gates: the completion
corpus and formatter goldens are hard ratchets, while environment-dependent performance checks are
acknowledged as drift-prone (`PerformanceBaselineTests` is a known environmental red). Adding
timing assertions to CI here would repeat a mistake the repo has already paid for.

---

## Open items carried into `/speckit.tasks`

None blocking. Two require a live server to settle, and both are observations for the implementer
rather than design gaps:

1. **Does the downloads folder actually hold all 16 installers?** Determines whether R2 is currently
   producing a visible failure or only latent risk. The new admin Releases section answers it
   permanently.
2. **Is anything fronting the site?** `Analytics:KnownProxies` is empty, which is correct for IIS
   in-process. If a CDN is ever placed in front, FR-038's stored address silently becomes the
   proxy's and US3 becomes meaningless — worth an explicit check during deployment verification.
