---
description: "Task list for feature 038 — Site Download Experience and Full Admin Portal"
---

# Tasks: Site Download Experience and Full Admin Portal

**Feature branch**: `038-site-downloads-admin-portal`
**Input**: Design documents in `C:\Repos\AKML\AKML-SQL\specs\038-site-downloads-admin-portal\`
**Prerequisites**: [plan.md](./plan.md), [spec.md](./spec.md), [research.md](./research.md), [data-model.md](./data-model.md), [contracts/](./contracts/), [quickstart.md](./quickstart.md)

**Tests**: **REQUIRED**, not optional. The project constitution (Principle III) states *"Every `src/`
project MUST have a matching xunit test project under `tests/`. New behavior lands with tests covering
it."* Test tasks below are therefore mandatory deliverables, not a TDD preference.

**Organization**: Grouped by user story so each can be implemented, tested and deployed independently.

---

## READ THIS FIRST — implementer orientation

You are modifying **one existing ASP.NET Core application**: `src/AkmlSql.Site`. There is no new
project. Everything below extends code that already exists and works.

### Non-negotiable constraints (violating any of these breaks the build or the site)

| Constraint | Detail |
|---|---|
| **Static SSR only** | No interactive render mode is registered. Do **not** add `@rendermode`, Blazor Server circuits, or WebAssembly. Every interaction is a plain form POST or a link. |
| **Strict CSP** | `Program.cs` sets `script-src 'self'; style-src 'self'`. **No inline `<script>`, no inline `style=` attributes, no `onclick=`.** External JS goes in `wwwroot/js/*.js` and is referenced via `@Assets["js/name.js"]`. Charts use the precomputed `.bar-h-*` bucket classes in `wwwroot/css/site.css`. |
| **No-JS first** | Every feature must work with JavaScript disabled (FR-009). JS is progressive enhancement only. |
| **Theme CSS is generated** | `wwwroot/css/themes/light.css` and `dark.css` are generated from `docs/theme-tokens.json` by `scripts/generate-theme-css.ps1`. **Never hand-edit them** — `build.ps1` step 1 runs `-CheckOnly` and fails on drift. Put new styles in `wwwroot/css/site.css`. |
| **No EF Core** | Storage is plain ADO.NET over `Microsoft.Data.Sqlite`. Parameterised commands only (`$name` placeholders, `AddWithValue`). Follow the existing style in `Analytics/AnalyticsStore.cs`. |
| **Metrics never break a request** | All analytics writes go through `IAnalyticsSink` (fire-and-forget, `ChannelAnalyticsSink`). Every tracking call site is wrapped in `try/catch` that swallows. Never `await` a metrics write on the request path. |
| **Git** | Do **not** run `git add`, `git commit`, `git push`, or create PRs. Deliver changes uncommitted. |

### Build and test commands

```bash
# Build the site
dotnet build src/AkmlSql.Site/AkmlSql.Site.csproj -c Debug

# Run the site's tests (the suite you will be extending)
dotnet test tests/AkmlSql.Site.Tests/AkmlSql.Site.Tests.csproj

# Run one test class while iterating
dotnet test tests/AkmlSql.Site.Tests/AkmlSql.Site.Tests.csproj --filter "FullyQualifiedName~ReleaseAvailabilityTests"

# Run locally — use https, or Secure cookies are silently dropped and consent will look broken
dotnet run --project src/AkmlSql.Site/AkmlSql.Site.csproj
```

### Key existing symbols you will interact with

| File | Symbols |
|---|---|
| `Analytics/AnalyticsStore.cs` | `InitializeSchema()`, `AddColumnIfMissing((string Name, string Type))`, `EnrichmentColumns`, `DownloadEnrichmentColumns`, `LogVisit(VisitInfo)`, `LogDownload(DownloadInfo)`, `GetSummary(int days, DateTimeOffset now)`, `Prune(int, DateTimeOffset)`, `ComputeIpHash(string?, DateOnly)`, `ResolveSessionId(...)`, `QueryCountRows(...)`, `QueryDailySeries(...)`, `HumanOnly`, `RealDownloadOnly`, `FormatUtc(...)`, `FormatDay(...)`, `_gate`, `_connection`, `TopRowLimit` |
| `Analytics/AnalyticsModels.cs` | `VisitInfo`, `DownloadInfo`, `NotFoundInfo`, `IAnalyticsSink`, `CountRow`, `DailyCount`, `AnalyticsSummary` |
| `Analytics/HttpRequestFacts.cs` | `ClientIp(HttpContext)`, `ReferrerHost(HttpRequest)`, `ReferrerUrl(HttpRequest)`, `Language(HttpRequest)`, `Campaign(HttpRequest)` |
| `Analytics/IpAnonymizer.cs` | `ToPrefix(string?)`, `IPv4PrefixBits` (24), `IPv6PrefixBits` (48) |
| `Analytics/GeoLookup.cs` | `Locate(string?)` → `GeoLocation(CountryCode, CountryName)`, `GeoLocation.Unknown` |
| `Analytics/DownloadEndpoint.cs` | `Map`, `MapCount`, `Handle`, `HandleCount`, `ResolveFilePath(string, string?)`, `ResolveCdnUrl(ReleasesManifest?, string?)`, `LogDownload(...)`, `DownloadsOptions.Folder` |
| `Analytics/VisitTrackingMiddleware.cs` | `InvokeAsync`, `ShouldTrack`, `ShouldTrackNotFound`, `ExcludedPaths`, `ExcludedPrefixes` |
| `Releases/ReleaseAvailability.cs` | `LocalPrefix` (`"downloads/"`), `IsLocal(Release)`, `IsDownloadable(Release)`, `SizeBytes(Release)`, `DisplaySize(Release)`, `TrackedUrl(Release)`, `FormatSize(long)`, `ResolveFile(Release)` |
| `Releases/ReleasesManifest.cs` | `Load(IWebHostEnvironment)`, `Create(...)`, `Releases`, `Latest`, `IsAvailable`, `Unavailable` |
| `Admin/AdminBranchMiddleware.cs` | `RequiresChallenge(HttpContext)` |
| `Admin/AdminDashboardOptions.cs` | `Ranges` (`[7,30,90,365]`), `DefaultDays` (30), `MaxDays` (3650), `NormalizeDays(int?)`, `Label(int)` |
| `Admin/AdminEndpoints.cs` | `Map(IEndpointRouteBuilder)`, `AuditLoggerName` |
| `Admin/AdminAuth.cs` | `Scheme`, `Verify(string, string)` |
| `Analytics/MetricsExport.cs` | `ToCsv(AnalyticsSummary)`, `FileName(AnalyticsSummary, DateTimeOffset)` |
| `tests/AkmlSql.Site.Tests/TempDirectory.cs` | Disposable temp folder helper — use it for every store test |

---

## Phase 1: Setup — baseline and fact-finding

**Purpose**: Record the "before" state so the US1 fix can be proven to have worked, and settle the two
open questions from research.md that need a live server.

- [x] T001 Record the pre-change performance baseline for the download page and write it into `specs/038-site-downloads-admin-portal/baseline.md`.
  - Recycle the `AkmlSqlSite` IIS app pool (or `iisreset`) to force a genuinely cold app, then request `/download` and record time-to-usable-page.
  - Reload `/download` five more times; discard the first; record the warm p95.
  - Click the primary download button and record the time until bytes start arriving, once with JS enabled and once with JS disabled.
  - Record the current release count in `src/AkmlSql.Site/wwwroot/releases.json` (expected: 16).
  - **Why**: SC-001/SC-002 are measured, not asserted in CI. Without a recorded "before", the IIS and render changes cannot be shown to have moved anything.

- [x] T002 Establish the green baseline: run `dotnet build src/AkmlSql.Site/AkmlSql.Site.csproj -c Debug` and `dotnet test tests/AkmlSql.Site.Tests/AkmlSql.Site.Tests.csproj`, and record the pass/fail counts in `specs/038-site-downloads-admin-portal/baseline.md`.
  - Any test already failing before your changes must be noted as pre-existing, so it is never mistaken later for a regression you introduced.

- [x] T003 [P] Settle research open item 1: list the contents of the installer folder on the server (`C:\inetpub\akml.khamis.work-downloads`, per `Downloads:Folder` in `src/AkmlSql.Site/appsettings.json`) and compare it against the 16 `downloadUrl` entries in `src/AkmlSql.Site/wwwroot/releases.json`. Record the result in `specs/038-site-downloads-admin-portal/baseline.md`.
  - **Why**: if installers are missing locally, the CDN-availability bug (T019/T020) is currently causing a *visible* failure — the page hides working releases, or shows "No public release available yet" while the GitHub links work fine. If they are all present, the bug is latent. Either way T019 fixes it; this tells you the severity.

- [x] T004 [P] Settle research open item 2: confirm nothing is fronting the site (no Cloudflare, ARR, or load balancer). Check that `Analytics:KnownProxies` in `src/AkmlSql.Site/appsettings.json` is empty and that IIS is serving in-process. Record in `specs/038-site-downloads-admin-portal/baseline.md`.
  - **Why**: FR-038 stores the client address from `HttpRequestFacts.ClientIp`, which reads `Connection.RemoteIpAddress`. Behind an unconfigured proxy every visitor collapses to one address and US3 becomes meaningless while appearing to work.

- [x] T005 [P] Confirm the geo database is present at the path resolved by `AnalyticsOptions.GeoDatabasePath` (empty → `%ProgramData%\AKML SQL Site\GeoLite2-Country.mmdb`). If absent, run `scripts/update-geoip.ps1`. Record in `specs/038-site-downloads-admin-portal/baseline.md`.
  - Without it every country figure reports "unknown" — correct degraded behaviour, but it makes US3's country scenarios unverifiable.

**Checkpoint**: Baseline recorded. You now know whether the download bug is live or latent.

---

## Phase 2: Foundational — blocking prerequisites

**Purpose**: Storage schema, the settings store, and the admin shell. More than one user story depends
on each of these, so they are built once here rather than rebuilt or re-parented later.

**⚠️ CRITICAL**: No user story phase may begin until this phase is complete.

### Storage schema

- [x] T006 Extend the schema migration in `src/AkmlSql.Site/Analytics/AnalyticsStore.cs` → `InitializeSchema()` to add the new columns.
  - Add to the `EnrichmentColumns` array (applied to the `visits` table): `("ip", "TEXT")`, `("visitor_id", "TEXT")`, `("consent", "TEXT")`.
  - Add to the `DownloadEnrichmentColumns` array (applied to the `downloads` table): `("ip", "TEXT")`, `("visitor_id", "TEXT")`, `("consent", "TEXT")`, `("release_version", "TEXT")`.
  - **Do not** write a new migration mechanism. The existing `AddColumnIfMissing` loop already runs these idempotently against installed databases — that is exactly why it exists ("the deployed site has months of history and must not be reset").
  - All new columns are `NULL`-able. Pre-038 rows keep `NULL` for all of them and that is correct, not a gap to backfill.
  - **Do not** remove `ip_hash` or `ip_prefix`. `ip_hash` still drives `ResolveSessionId` and per-day unique counting for non-consenting visitors; `ip_prefix` is the network grouping key.
  - Update the class-level XML summary, which currently claims *"the raw client IP is never persisted"* — that becomes false here. Replace it with an accurate description: full address stored only with consent, truncated prefix and daily hash always.

- [x] T007 Add the new indexes to the index-creation SQL block near the end of `InitializeSchema()` in `src/AkmlSql.Site/Analytics/AnalyticsStore.cs`:
  ```sql
  CREATE INDEX IF NOT EXISTS ix_visits_visitor        ON visits    (visitor_id, utc);
  CREATE INDEX IF NOT EXISTS ix_downloads_visitor     ON downloads (visitor_id, utc);
  CREATE INDEX IF NOT EXISTS ix_downloads_day_country ON downloads (day, country);
  CREATE INDEX IF NOT EXISTS ix_visits_day_country    ON visits    (day, country);
  ```
  - Keep every existing index. Add these to the same `command.CommandText` string that already creates `ix_visits_day` etc.

- [x] T008 [P] Write `tests/AkmlSql.Site.Tests/Analytics/SchemaMigrationTests.cs` proving the migration is safe on an existing database.
  - Create a `TempDirectory`, construct an `AnalyticsStore`, insert a visit and a download, dispose it.
  - Re-open a new `AnalyticsStore` on the same path (simulating a deploy) and assert: the new columns exist, the previously inserted rows still return their original values, and `ip` / `visitor_id` / `consent` are `NULL` on them.
  - Assert opening a third time changes nothing (idempotence).
  - Covers quickstart scenario **SX.3**.

### Settings

- [x] T009 [P] Create `src/AkmlSql.Site/Settings/ReleaseVisibility.cs`.
  - `public enum ReleaseVisibilityMode { LatestOnly, LatestN, All }`.
  - `public static class ReleaseVisibilityBounds { public const int MinCount = 1; public const int MaxCount = 50; public const int DefaultCount = 3; public const ReleaseVisibilityMode DefaultMode = ReleaseVisibilityMode.LatestN; }`
  - A static helper `public static IReadOnlyList<Release> Apply(ReleaseVisibilityMode mode, int count, IReadOnlyList<Release> newestFirst)` returning the visible prefix: `LatestOnly` → at most 1; `LatestN` → at most `count`; `All` → all. Never reorders. When `count` exceeds the list length, return the whole list (not an error).
  - Add a `Label(ReleaseVisibilityMode, int)` returning plain-language text for the settings UI ("Latest release only", "Latest 3 releases", "All releases").
  - Contract reference: `contracts/release-visibility.md` §2.

- [x] T010 [P] Create `src/AkmlSql.Site/Settings/SiteSettings.cs` — the immutable POCO the rest of the app reads.
  - Properties: `ReleaseVisibilityMode Visibility`, `int VisibilityCount`, `int IdentifiableRetentionDays`.
  - `public static SiteSettings Defaults { get; }` = `LatestN`, 3, 365. Per FR-016 the default MUST NOT be `All`.
  - A `Validate()` returning a list of human-readable error strings: `VisibilityCount` outside 1–50, `IdentifiableRetentionDays` outside 1–3650.

- [x] T011 Create `src/AkmlSql.Site/Settings/SiteSettingsStore.cs` — SQLite-backed, cached, fail-soft.
  - Constructor takes `AnalyticsOptions` (or the resolved database path) and an `ILogger<SiteSettingsStore>`. Opens its **own** `SqliteConnection` to the same `analytics.db`. Do **not** share `AnalyticsStore._connection` or its `_gate` — a settings read must never wait behind a metrics write. WAL is already enabled, so concurrent access is safe.
  - `CreateTableIfMissing()`:
    ```sql
    CREATE TABLE IF NOT EXISTS site_settings (
        key TEXT PRIMARY KEY NOT NULL,
        value TEXT NOT NULL,
        updated_utc TEXT NOT NULL,
        updated_by TEXT NULL
    );
    ```
  - Keys: `release_visibility`, `release_visibility_count`, `identifiable_retention_days`.
  - `public SiteSettings Current { get; private set; }` — the in-memory cached value. **The render path reads only this property; it must never touch SQLite.** (FR-016a, contract R2.7.)
  - `public bool LoadFailed { get; private set; }` and `public string? LoadError { get; private set; }` — set when the initial load throws.
  - `Load()`: read all rows, parse into a `SiteSettings`, fall back to `SiteSettings.Defaults` for any missing or unparseable key. On any exception (locked, corrupt, missing table), log at **Warning** with the exception, set `LoadFailed = true`, and set `Current = SiteSettings.Defaults`. **Never rethrow** — an unreadable settings store must not be able to break the public download page (FR-016a, contract R2.8).
  - `Save(SiteSettings candidate, string? updatedBy)`: call `candidate.Validate()` first and return the error list without writing if non-empty. Otherwise `INSERT ... ON CONFLICT(key) DO UPDATE` all three keys inside a single transaction, set `updated_utc` via the same ISO-8601 round-trip format `AnalyticsStore.FormatUtc` uses, then replace `Current`. Last save wins.
  - `public (DateTimeOffset? UpdatedUtc, string? UpdatedBy) LastChange()` for the settings page's confirmation display (FR-035).

- [x] T012 [P] Write `tests/AkmlSql.Site.Tests/Settings/SiteSettingsStoreTests.cs`.
  - Empty table → `Current` equals `SiteSettings.Defaults` (`LatestN`, 3) — proves FR-016 / quickstart **S2.5**.
  - Save then re-open on the same file → value persists (quickstart **S2.6**).
  - `Save` with count 0 and with count 999 → returns errors naming the 1–50 bound, writes nothing, `Current` unchanged. **Assert there is no silent clamping** (FR-012, quickstart **S2.3**).
  - `Save` with `IdentifiableRetentionDays` 0 and 99999 → rejected.
  - Corrupt/unreadable database file → `Load()` does not throw, `LoadFailed` is true, `Current` equals defaults (FR-016a, quickstart **S2.7**).
  - Two sequential saves → last wins, `updated_utc` and `updated_by` reflect the second.

- [x] T013 Register the settings store in `src/AkmlSql.Site/Program.cs`.
  - `builder.Services.AddSingleton<SiteSettingsStore>(...)` next to the existing `AnalyticsStore` registration.
  - In the eager-resolution block after `app.Build()` (where `ReleasesManifest`, `DocsContentService` and `AnalyticsStore` are already resolved), resolve `SiteSettingsStore` and call `CreateTableIfMissing()` + `Load()`. **Unlike the other singletons this must not fail the deploy** — `Load()` already swallows, so this is simply the point at which the cache is warmed.
  - If `LoadFailed`, log at Warning: `"Site settings could not be loaded; serving documented defaults. {Error}"`.

- [x] T014 Extend the `/health` endpoint in `src/AkmlSql.Site/Program.cs` to report settings state.
  - Add `settingsLoaded = !settings.LoadFailed` and `releaseVisibility = settings.Current.Visibility.ToString()` to the anonymous `report` object.
  - Keep the existing "degraded is still a 200" behaviour — a settings problem must not fail a deploy smoke test.
  - Required by FR-016a and quickstart **S2.7**.

### Admin shell

- [x] T015 [P] Create `src/AkmlSql.Site/Admin/AdminNav.cs` — the single source of truth for portal sections.
  - `public sealed record AdminSection(string Route, string Title, string Description)`.
  - `public static readonly IReadOnlyList<AdminSection> Sections` in display order: `/admin` "Overview", `/admin/downloads` "Downloads", `/admin/people` "People", `/admin/pages` "Pages", `/admin/errors` "Errors", `/admin/releases` "Releases", `/admin/settings` "Settings".
  - `public static bool IsActive(AdminSection section, string currentPath)` — exact match for `/admin`, `StartsWithSegments` for the others, so `/admin/people/abc123` marks "People" active.
  - `public static string WithRange(string route, int days)` → `$"{route}?days={days}"`. This one helper is how the reporting window survives navigation (FR-032); every nav link and every export link must be built with it.
  - Contract reference: `contracts/admin-portal-surface.md` §1, §3, §4.

- [x] T016 Create `src/AkmlSql.Site/Components/Layout/AdminLayout.razor` — the portal shell.
  - `@inherits LayoutComponentBase`, `@layout` target for every `/admin/*` page **except** `/admin/login` (a sign-in page must not show navigation to sections the visitor cannot reach — contract A3.5).
  - Renders: an "Admin" eyebrow + site title; a `<nav>` listing `AdminNav.Sections` as links built with `AdminNav.WithRange(section.Route, days)`, with `aria-current="page"` on the active one; the shared range selector (`AdminDashboardOptions.Ranges`, using `AdminDashboardOptions.Label`); a visible statement of the active window; a sign-out form posting to `/admin/logout`; then `@Body`.
  - `<HeadContent><meta name="robots" content="noindex, nofollow" /></HeadContent>` on every portal page (contract A2.5).
  - Read the current `days` from the query string via `NavigationManager` and normalise with `AdminDashboardOptions.NormalizeDays`. An unparseable value falls back to the default rather than erroring (contract A4.4).
  - Styling goes in `wwwroot/css/site.css` only. No inline styles (CSP).

- [x] T017 Re-parent the two existing admin pages onto the new shell.
  - `src/AkmlSql.Site/Components/Pages/Admin/AdminDashboard.razor`: add `@layout AdminLayout`, and remove the now-duplicated header/sign-out/range markup that the layout provides.
  - `src/AkmlSql.Site/Components/Pages/Admin/AdminErrors.razor`: add `@layout AdminLayout`, remove its duplicated chrome.
  - `src/AkmlSql.Site/Components/Pages/Admin/AdminLogin.razor`: **leave alone** — it must not use the portal shell.
  - Do not change what either page *reports* in this task; this is chrome only.

- [x] T018 [P] Write `tests/AkmlSql.Site.Tests/Admin/AdminNavTests.cs`.
  - `IsActive` returns true for `/admin` only on exact `/admin`, and true for `/admin/people` on both `/admin/people` and `/admin/people/abc123`.
  - `WithRange` produces `?days=7`.
  - Every route in `AdminNav.Sections` starts with `/admin` — so it is automatically covered by `AdminBranchMiddleware`.

**Checkpoint**: Schema migrated, settings readable and fail-soft, portal shell exists. User story work can begin.

---

## Phase 3: User Story 1 — A visitor downloads AKML SQL without waiting (Priority: P1) 🎯 MVP

**Goal**: The download page renders fast and correctly, and clicking the button starts the transfer
immediately. This is the site's conversion path and the defect reported first.

**Independent Test**: Request `/download` cold and warm and measure; click the primary button with and
without JS; confirm one clearly-dominant current release; confirm a CDN-backed release is offered even
when its local file is absent.

**Delivers value alone**: yes — this is the MVP. Ship and deploy it before anything else.

### Correctness fix — CDN-aware availability

- [x] T019 [US1] Fix availability resolution in `src/AkmlSql.Site/Releases/ReleaseAvailability.cs`.
  - **The bug**: `IsLocal(release)` returns true whenever `downloadUrl` starts with `"downloads/"` — true for **all 16** current releases — so `IsDownloadable` demands the local file exists. But `DownloadEndpoint.Handle` checks `ResolveCdnUrl` **first** and 302s to GitHub without ever touching the local folder, and `download-track.js` rewrites the href straight to the CDN. The page's "can I offer this?" and the endpoint's "will I serve this?" have drifted apart — the exact disagreement this class's own summary says it exists to prevent.
  - Change `IsDownloadable(Release release)` to:
    1. `false` when `release is null`;
    2. `true` when `release.CdnUrl` is non-empty — **no filesystem probe, no network probe**;
    3. otherwise `ResolveFile(release) is not null` for local releases;
    4. `true` for a non-local absolute `downloadUrl` (existing behaviour — we cannot check it and need not).
  - **Never** issue an HTTP request to verify the CDN URL. A per-render call to GitHub would put a third-party network hop on the site's primary page — precisely what `GeoLookup` is offline to avoid.
  - Leave `SizeBytes` / `DisplaySize` as they are: a CDN-only release has no local file, so size returns `null` and the page omits the row rather than guessing (contract R1.4).
  - Update the class summary to describe the new rule.
  - Contract reference: `contracts/release-visibility.md` §1.

- [x] T020 [P] [US1] Write `tests/AkmlSql.Site.Tests/Releases/ReleaseAvailabilityTests.cs`.
  - Release with `CdnUrl` set and **no** local file → `IsDownloadable` is `true`.
  - Release with neither `CdnUrl` nor a local file → `false`.
  - Release with no `CdnUrl` but a present local file → `true`.
  - `null` release → `false`.
  - A release with `CdnUrl` causes **zero** filesystem access (inject or count probes; see T022's seam).
  - `DisplaySize` returns `null` for a CDN-only release and a formatted size for a local one.
  - Covers quickstart **S1.4**, FR-006.

### Performance fix — single-pass rendering

- [x] T021 [US1] Eliminate the double evaluation in `src/AkmlSql.Site/Components/Pages/Download.razor`.
  - **The bug**: `PreviousReleases` at line ~159 is an **expression-bodied property**, not a cached field:
    ```csharp
    private List<Release> PreviousReleases =>
        Manifest.Releases.Skip(1).Where(Availability.IsDownloadable).ToList();
    ```
    The page evaluates it twice — at `@if (PreviousReleases.Count > 0)` and again at `@foreach (var release in PreviousReleases)` — so every release is probed twice. With 16 releases that is ~32 existence probes per render.
  - Replace it with a field populated once in `OnInitialized()`: `private IReadOnlyList<Release> _previousReleases = [];` and `private Release? _latest;` and `private string? _latestSize;`.
  - Resolve the latest release, its downloadability, and its display size **once** into fields; render from the fields.
  - Keep every existing behaviour: the friendly fallback when nothing is downloadable (FR-007), `ReleaseAvailability.TrackedUrl` for hrefs, `data-file` / `data-cdn-url` attributes, the SmartScreen guidance paragraph, the SHA-256 block with its copy button, and both `<script>` tags (`copy-hash.js`, `download-track.js`).
  - Contract reference: `contracts/release-visibility.md` §4 (R4.1, R4.2).

- [x] T022 [US1] Introduce a countable seam for availability probing so probe counts can be asserted deterministically.
  - Extract the filesystem check in `ReleaseAvailability` behind an overridable/injectable delegate — e.g. an internal `Func<string, bool> _fileExists` defaulting to `File.Exists`, or make `ResolveFile` `protected virtual`. Keep the production path identical.
  - This exists purely so T023 can count probes without timing. Do not over-build it — one seam, no abstraction layer, no interface (constitution Principle V).

- [x] T023 [P] [US1] Extend `tests/AkmlSql.Site.Tests/Components/DownloadPageTests.cs` with probe-count assertions (bunit).
  - Build a manifest with 100 local-only releases; render the page; assert the probe count equals the number of releases actually rendered — **not twice that**. This is the regression guard for T021.
  - Build a manifest of 100 CDN-backed releases; assert **zero** filesystem probes.
  - Assert the release list resolves exactly once per render.
  - Covers SC-003 and quickstart **S1.3**. (The `LatestOnly` half of S1.3 is added in T037, once the setting exists.)

- [x] T024 [P] [US1] Extend `tests/AkmlSql.Site.Tests/Components/DownloadPageTests.cs` with rendering assertions.
  - Exactly one primary release card, carrying version, released date, supported hosts, minimum OS, SHA-256 and the primary button (FR-001).
  - The primary card appears in the markup **before** any history section (FR-010, SC-004).
  - With no downloadable release, the friendly fallback renders and no error page is produced (FR-007).
  - The primary download link's `href` is `ReleaseAvailability.TrackedUrl(latest)` so the no-JS path works (FR-009).

### Download counting integrity

- [x] T025 [P] [US1] Extend `tests/AkmlSql.Site.Tests/Analytics/DownloadEndpointTests.cs` for exactly-once counting.
  - A request carrying a `Range` header does **not** enqueue a download (the existing guard in `LogDownload`).
  - A `POST /dl-count/{file}` beacon for a file that was also streamed does not produce a second count for the same click — assert the endpoint's counting contract is one-per-click by construction.
  - `/dl-count/{file}` for a filename that is neither in the manifest with a CDN mirror nor resolvable locally returns 404 and enqueues nothing (the counter cannot be inflated with invented filenames).
  - Covers FR-005, SC-009, quickstart **S1.6**.

### Cold start

- [x] T026 [US1] Move startup maintenance off the request-blocking path in `src/AkmlSql.Site/Program.cs`.
  - Today `analyticsStore.Prune(...)` and `analyticsStore.ClearSameOriginReferrers(...)` run **inline** after `app.Build()`, before the first request can be served.
  - Create `src/AkmlSql.Site/Analytics/MaintenanceHostedService.cs` implementing `IHostedService` (or `BackgroundService`). In `StartAsync`, kick off a `Task.Run` that performs, in order: `Prune(RetentionDays)`, `ClearSameOriginReferrers(siteHost)`, and (added later in T078) the identifiable de-identification pass. Log the same informational messages that are logged today, with the same message templates.
  - **Keep** the eager resolution of `ReleasesManifest`, `DocsContentService` and `AnalyticsStore` — that is deliberate fail-fast for broken deploys (the comment in `Program.cs` says so) and must not be weakened. Only the *maintenance* moves.
  - Wrap the background work in `try/catch` that logs and swallows; maintenance failing must never take the site down.

- [x] T027 [US1] Configure IIS against cold start in `scripts/deploy-site-iis.ps1`.
  - The script sets `startMode = AlwaysRunning` (line ~240) and nothing else. `AlwaysRunning` starts the worker process, but the default `idleTimeout` of 20 minutes still shuts it down, and without preload the ASP.NET Core app is not initialised until a real request arrives — so a visitor from a search result pays for the restart.
  - Add, next to the existing `Set-ItemProperty` calls on the app pool:
    - `Set-ItemProperty "IIS:\AppPools\$AppPoolName" -Name processModel.idleTimeout -Value ([TimeSpan]::Zero)`
    - `Set-ItemProperty "IIS:\Sites\$SiteName" -Name applicationDefaults.preloadEnabled -Value $true` (or set `preloadEnabled` on the application)
  - Add an `applicationInitialization` entry that warms a cheap route (`/health`) on start.
  - Add a comment explaining why all three are needed together, in the style of the script's existing comments.
  - Keep the change idempotent — the script is re-run on every deploy.

- [x] T028 [US1] **BLOCKED — needs a production deploy (not performed without approval).** Re-measure and record. Repeat T001's cold and warm measurements after T021/T026/T027 and append the "after" numbers to `specs/038-site-downloads-admin-portal/baseline.md`.
  - **Pass**: cold < 3 s, warm < 1 s p95 (SC-001); transfer starts < 2 s (SC-002).
  - If the numbers did **not** move, say so plainly in the record and investigate before moving on — a fix that does not move the number is not a fix. Do not assume; measure.
  - Covers quickstart **S1.1**, **S1.2**, **S1.5**, **SX.6**.

- [x] T029 [P] [US1] **BLOCKED — needs a browser.** Manually verify phone width: at 400 px the current version and its download button are reachable without scrolling past any other version, and the page does not scroll horizontally. Record in `specs/038-site-downloads-admin-portal/baseline.md`. Covers SC-004, quickstart **S1.7**.

- [x] T030 [P] [US1] **BLOCKED — needs a browser.** Manually verify the no-JS path: disable JavaScript, load `/download`, click the primary button. The transfer must start via `/dl/{file}` (which 302s to the CDN) and the download must be counted. Covers FR-009, quickstart **S1.5**.

- [x] T031 [P] [US1] Add a test to `tests/AkmlSql.Site.Tests/Analytics/AnalyticsStoreTests.cs` asserting metrics failure cannot break the page: with a sink that throws on every enqueue, rendering `/download` still succeeds and `/dl/{file}` still streams. Covers FR-041, quickstart **SX.2**.

- [x] T032 [US1] Run the full site test suite and confirm green, plus no new failures against the T002 baseline.

**Checkpoint**: 🎯 **MVP complete.** The download page is fast and correct. This is independently
deployable — stop here and validate before continuing if you want an early win.

---

## Phase 4: User Story 2 — The owner controls which versions the public sees (Priority: P2)

**Goal**: A setting in the portal decides how much release history `/download` advertises, effective
immediately, with no redeploy — which also permanently bounds the render work fixed in US1.

**Independent Test**: Sign in, set "latest only", load `/download` in a signed-out browser, confirm one
release; change to show more and confirm the page follows — all without restarting the site.

**Depends on**: Phase 2 (settings store, admin shell). Does **not** depend on US1, but is far more
valuable after it.

- [x] T033 [US2] Apply the visibility setting in `src/AkmlSql.Site/Components/Pages/Download.razor`.
  - Inject `SiteSettingsStore` and read `Store.Current` — **only the cached property**, never a database call, so the render path stays clean (FR-016a, contract R2.7).
  - In `OnInitialized()`, apply `ReleaseVisibility.Apply(mode, count, Manifest.Releases)` **before** filtering by `IsDownloadable`, so probes follow what is shown, not what is published (contract R2.1 — this is what makes SC-003 hold for a 100-release manifest).
  - `LatestOnly` → render the primary card and **do not render the history section at all** (not an empty section) (contract R2.2).
  - Ordering comes from `ReleasesManifest`'s existing newest-first order; the setting selects a prefix and never reorders.
  - When no release survives, the existing friendly fallback renders unchanged.

- [x] T034 [US2] Create `src/AkmlSql.Site/Components/Pages/Admin/AdminSettings.razor` at route `/admin/settings`.
  - `@layout AdminLayout`, `@inject SiteSettingsStore Settings`, `@inject ReleasesManifest Manifest`, `@inject ReleaseAvailability Availability`.
  - Render a plain `<form method="post" action="/admin/settings">` with the antiforgery token — no JavaScript (contract A5.6).
  - Controls: radio group for `ReleaseVisibilityMode` (each option with a plain-language description of what it publishes — FR-011/A5.1); a number input for the count, enabled for `LatestN`; a number input for `identifiable_retention_days`.
  - Show the current value of each setting, and `LastChange()` ("Last changed {utc} by {session}").
  - **Preview (FR-017 / A5.2)**: list which releases the public currently sees under the saved setting, and — after a save — which they see now. Reuse `ReleaseVisibility.Apply` so the preview cannot disagree with the page.
  - Show validation errors returned from the save, inline, naming the bound (never silently clamp — A5.3).
  - After a successful save, show explicit confirmation of **what** changed and **when it takes effect** ("live on the next public request") (FR-035, A5.4).
  - If `Settings.LoadFailed`, show a prominent warning that defaults are being served and why (FR-016a).

- [x] T035 [US2] Add the settings POST endpoint in `src/AkmlSql.Site/Admin/AdminEndpoints.cs` → `Map`.
  - `endpoints.MapPost("/admin/settings", HandleSettings);` taking `IFormCollection` so antiforgery is enforced, exactly as `HandleLogin` does.
  - Parse the form into a `SiteSettings`, call `SiteSettingsStore.Save(candidate, updatedBy)` where `updatedBy` comes from the authenticated principal (`ClaimsPrincipal.Identity?.Name` or the admin scheme's equivalent).
  - On validation failure, redirect back to `/admin/settings?error=...` carrying enough detail for the page to render the message; on success redirect to `/admin/settings?saved=1`.
  - Log the change at Information with what changed — FR-035's audit trail, in the style of the existing `AuditLoggerName` sign-in logging.
  - It sits under `/admin`, so `AdminBranchMiddleware` already guards it — do not add a second authorization path (A2.1).

- [x] T036 [P] [US2] Write `tests/AkmlSql.Site.Tests/Settings/ReleaseVisibilityTests.cs`.
  - `Apply(LatestOnly, _, 16 releases)` → 1 release, and it is the newest.
  - `Apply(LatestN, 3, 16)` → 3 newest, in original order.
  - `Apply(LatestN, 99, 16)` → all 16, no error (contract R2.5).
  - `Apply(All, _, 16)` → all 16.
  - `Apply(..., empty list)` → empty, no throw.
  - Ordering is never changed by any mode (contract R2.2).

- [x] T037 [P] [US2] Extend `tests/AkmlSql.Site.Tests/Components/DownloadPageTests.cs` for visibility.
  - With `LatestOnly` and a 100-release manifest: exactly one release rendered, **no history section element present at all**, and at most **one** availability probe (contract R4.3, completing quickstart **S1.3**).
  - With `LatestN`(3): three releases, at most three probes.
  - With `All`: all downloadable releases rendered.
  - With an empty `site_settings` table: the default `LatestN`(3) applies (FR-016).

- [x] T038 [P] [US2] Write `tests/AkmlSql.Site.Tests/Components/AdminSettingsPageTests.cs` (bunit).
  - The page renders every mode option with its description and marks the saved one selected.
  - The preview lists exactly the releases `ReleaseVisibility.Apply` returns for the saved setting.
  - A validation error passed to the page renders inline and names the bound.
  - When `LoadFailed` is true, the warning banner renders.

- [x] T039 [P] [US2] Add a test to `tests/AkmlSql.Site.Tests/Analytics/DownloadEndpointTests.cs`: with `LatestOnly` saved, `GET /dl/{older-file}` still serves (or 302s to its CDN) and still enqueues a download.
  - **This is the FR-015 guarantee**: visibility governs advertising, not reachability. `DownloadEndpoint` must have **no reference at all** to `SiteSettingsStore` (contract R2.4) — assert this by construction, not just behaviour.
  - Covers quickstart **S2.4**.

- [x] T040 [P] [US2] Extend `tests/AkmlSql.Site.Tests/Components/AdminPagesTests.cs`: `/admin/settings` requires authentication via `AdminBranchMiddleware.RequiresChallenge`.

- [x] T041 [US2] **BLOCKED — needs a browser + production deploy.** Manually verify quickstart **S2.1** (set "latest only", confirm in a signed-out browser, whole loop under 1 minute — SC-005), **S2.2** (latest N), **S2.6** (survives an app-pool recycle) and **S2.7** (settings store made unreadable → page still serves defaults). Record results in `specs/038-site-downloads-admin-portal/baseline.md`.

- [x] T042 [US2] Run the full site test suite and confirm green.

**Checkpoint**: Release visibility is owner-controlled and the download page's cost is permanently bounded.

---

## Phase 5: User Story 5 — A visitor decides whether to be tracked (Priority: P3, ships with US3)

**Goal**: A non-blocking consent request, shown to every visitor, that gates all identifiable
collection. **This must ship before US3**, or there would be a window in which identifiable data is
collected with no consent path — which is the thing the spec exists to prevent.

**Independent Test**: In a fresh private window, decline: the download still works, no identity cookie
is issued, nothing identifiable is stored, and the bar does not return. Accept in another fresh
window and confirm the inverse. Then withdraw and confirm recording stops.

**⚠️ Ordering**: Phase 5 **MUST** complete before Phase 6.

- [x] T043 [P] [US5] Create `src/AkmlSql.Site/Consent/ConsentState.cs`.
  - `public enum ConsentState { Unknown, Granted, Denied }`.
  - `Unknown` is a first-time visitor and is **not** implied consent (FR-043, contract C2.1).

- [x] T044 [US5] Create `src/AkmlSql.Site/Consent/ConsentCookies.cs` — all cookie mechanics in one place.
  - Constants: `public const string ConsentCookieName = "akml.consent";`, `public const string VisitorCookieName = "akml.vid";`, `public const int LifetimeDays = 365;`.
  - `CookieOptions Build(DateTimeOffset now)` → `HttpOnly = true`, `Secure = true`, `SameSite = SameSiteMode.Lax`, `Path = "/"`, `Expires = now.AddDays(LifetimeDays)`, `IsEssential = false`.
  - `ConsentState Read(HttpRequest)` → parse `akml.consent` (`"granted"` / `"denied"`), anything else → `Unknown`.
  - `string? ReadVisitorId(HttpRequest)` → `akml.vid`, validated as a 32-char hex GUID `N` format; anything malformed is treated as absent (never trust a client-supplied identifier verbatim).
  - `void Grant(HttpResponse, DateTimeOffset now, out string visitorId)` → sets `akml.consent=granted` **and** issues a fresh `Guid.NewGuid().ToString("N")` as `akml.vid`.
  - `void Deny(HttpResponse, DateTimeOffset now)` → sets `akml.consent=denied` **and deletes** `akml.vid`.
  - **The two cookies must stay independent** (contract C1.1): a refusal is remembered *without* issuing an identifier — storing the refusal inside the identity cookie would mean issuing the very thing the visitor refused.
  - Both are `HttpOnly` because no client script reads them and the CSP forbids inline script anyway (C1.2).
  - The identity cookie's lifetime must not exceed the identifiable retention period — a cookie that outlives the data it points at is a dangling identifier (C1.3).

- [x] T045 [US5] Create `src/AkmlSql.Site/Consent/ConsentMiddleware.cs`.
  - Resolves `ConsentState` and the visitor id from cookies and stores both in `HttpContext.Items` under stable keys (e.g. `ConsentMiddleware.StateKey`, `ConsentMiddleware.VisitorIdKey`).
  - Registered in `Program.cs` **before** `app.UseMiddleware<VisitTrackingMiddleware>()`, so tracking can read the resolved state (contract C2.3).
  - When state is `Granted` but `akml.vid` is missing or malformed, issue a fresh one and set the cookie — a consenting visitor whose id cookie was lost becomes a **new** individual, never re-linked to the old one.
  - **Must not consult `GeoLookup`** — consent is requested of everyone regardless of country, and a missing geo database must not be able to affect whether consent is asked for (FR-043a, contract C2.4).
  - Never throws into the pipeline.

- [x] T046 [US5] Create `src/AkmlSql.Site/Consent/ConsentEndpoints.cs`.
  - `POST /consent`: takes `IFormCollection` (antiforgery enforced, as `/admin/login` does), reads `choice` (`granted` | `denied`) and `returnUrl`.
  - `granted` → `ConsentCookies.Grant`; `denied` → `ConsentCookies.Deny`; anything else → no change.
  - Redirect to `returnUrl` **only if** it passes a local-path check (`Url.IsLocalUrl` equivalent: starts with a single `/`, not `//`, not a scheme). Otherwise redirect to `/`. **An open redirect here would be a real vulnerability** — this endpoint takes a redirect target from a form rendered on every page (contract C4.1).
  - `POST /privacy/forget`: reads `akml.vid`; calls `ConsentCookies.Deny`; calls the store's delete-by-visitor (T077). No authentication — it is the visitor's own identifier presented by their own cookie. With no `akml.vid` present it **succeeds idempotently** and reports that nothing was stored — never an error, and never a probe that reveals whether an id exists (contract C4.4).
  - Register both in `Program.cs` next to `ClientErrorEndpoint.Map(app)`.

- [x] T047 [US5] Create the consent bar UI as a component, e.g. `src/AkmlSql.Site/Components/ConsentBar.razor`, and render it from `src/AkmlSql.Site/Components/Layout/MainLayout.razor`.
  - Renders **only** when the resolved state is `Unknown` (contract C2.2).
  - **Non-blocking (FR-043b, contract C2.5)**: a bar fixed to the bottom of the viewport that obscures nothing, delays nothing and disables nothing. **Not a modal. No overlay. No focus trap. No scroll lock.** The visitor must be able to complete the entire download path without touching it.
  - Plain-language text naming what is collected (a persistent identifier and the full IP address), why, and how long it is kept; a link to `/privacy`.
  - A `<form method="post" action="/consent">` with the antiforgery token, a hidden `returnUrl` set to the current path, and **two buttons of equal prominence**: Accept (`choice=granted`) and Decline (`choice=denied`).
  - A dismiss control that submits `choice=denied` — **an explicit dismissal records Decline** (contract C2.6).
  - Silence, scrolling and navigating away leave the state `Unknown`; they are **never** treated as consent.
  - Keyboard reachable in DOM order, `role="region"` with an `aria-label`, and it must not steal focus on load.
  - Styles in `wwwroot/css/site.css` using existing theme tokens. No inline styles.

- [x] T048 [US5] Create `src/AkmlSql.Site/Components/Pages/Privacy.razor` at route `/privacy`.
  - States, in plain language: that a persistent first-party identifier and the **full IP address** are stored for consenting visitors; the country, network, device and browser recorded for everyone; why; the **configured** retention period; and how to withdraw, get a copy, or request deletion (FR-039).
  - **Read the retention number from the same configuration the store enforces** (`SiteSettingsStore.Current.IdentifiableRetentionDays`) — never hard-code it. This is what makes a drift between the notice and actual behaviour fail a test rather than ship (SC-013, contract C7.3).
  - A `<form method="post" action="/privacy/forget">` with antiforgery, offering withdrawal + deletion, and a confirmation state after submission.
  - Link it from the site footer in `src/AkmlSql.Site/Components/Layout/MainLayout.razor`.

- [x] T049 [US5] Wire consent into the visit write path in `src/AkmlSql.Site/Analytics/VisitTrackingMiddleware.cs`.
  - Read the resolved state and visitor id from `HttpContext.Items`.
  - Pass them onto `VisitInfo` (new init properties added in T050).
  - Behaviour is unchanged for everything else — the same `ShouldTrack` rules, the same fire-and-forget enqueue, the same `try/catch` that swallows.

- [x] T050 [US5] Extend the models in `src/AkmlSql.Site/Analytics/AnalyticsModels.cs`.
  - Add to both `VisitInfo` and `DownloadInfo`: `public string? VisitorId { get; init; }` and `public ConsentState Consent { get; init; } = ConsentState.Unknown;`.
  - Add `public string? ReleaseVersion { get; init; }` to `DownloadInfo` only.
  - **Correct the XML docs** on `VisitInfo.IpAddress` and `DownloadInfo.IpAddress`, which currently say the address is *"NEVER persisted"*. That becomes false here. State the new rule: persisted in full only when consent is granted; otherwise only the truncated prefix and the daily salted hash.

- [x] T051 [US5] Enforce the consent gate at the storage layer in `src/AkmlSql.Site/Analytics/AnalyticsStore.cs` → `LogVisit` and `LogDownload`.
  - Write `ip` and `visitor_id` **if and only if** `Consent == ConsentState.Granted`; otherwise bind `DBNull.Value` for both.
  - Always write the `consent` column (`"granted"` / `"denied"` / `"unknown"`) so the unattributed share is a query, not an inference (FR-047).
  - Continue writing `ip_hash` and `ip_prefix` for **all** states — unchanged behaviour, and it is what lets a non-consenting visitor still be counted once per day without storing anything identifying about them (contract C3.3).
  - **Enforce this in the store, not only at the call site** (contract C3.1). A bug in a page or middleware must not be able to cause collection. Add an inline comment saying exactly that, so a later refactor does not "simplify" the guard away.

- [x] T052 [P] [US5] Write `tests/AkmlSql.Site.Tests/Consent/ConsentCookiesTests.cs`.
  - `Grant` sets both cookies; `akml.vid` is a valid 32-char hex GUID; both are `HttpOnly`, `Secure`, `SameSite=Lax`, expire in 365 days.
  - `Deny` sets `akml.consent=denied` and **deletes** `akml.vid`.
  - `Read` maps `"granted"`/`"denied"` correctly and everything else (absent, empty, `"true"`, garbage) to `Unknown`.
  - `ReadVisitorId` rejects a malformed value and returns null.

- [x] T053 [P] [US5] Write `tests/AkmlSql.Site.Tests/Consent/ConsentMiddlewareTests.cs`.
  - `Unknown` → no identifier issued.
  - `Granted` with no `akml.vid` → a fresh one is issued.
  - `Granted` with a malformed `akml.vid` → treated as absent, a fresh one issued, **not** re-linked to anything.
  - **The consent path never calls `GeoLookup`** — assert with a `GeoLookup` substitute that fails the test if touched. Assert the bar renders for `Unknown` visitors resolved to an EU country, a non-EU country, **and with no geo database present** (FR-043a, quickstart **S5.1a**).

- [x] T054 [P] [US5] Write `tests/AkmlSql.Site.Tests/Consent/ConsentEndpointsTests.cs`.
  - `POST /consent` with `returnUrl=https://evil.example` redirects to `/`, not to the external host (quickstart **S5.7**).
  - `returnUrl=//evil.example` and `returnUrl=/download` — the first falls back to `/`, the second is honoured.
  - `choice=granted` issues both cookies; `choice=denied` issues only the consent cookie.
  - A missing antiforgery token is rejected.
  - `POST /privacy/forget` with no `akml.vid` succeeds idempotently and reveals nothing about whether an id exists.

- [x] T055 [P] [US5] Write `tests/AkmlSql.Site.Tests/Consent/ConsentStorageGateTests.cs` — **the most important test in this phase**.
  - After a `Denied` visit: the stored row has `ip IS NULL`, `visitor_id IS NULL`, `consent = 'denied'`, and `ip_hash` / `ip_prefix` still populated (SC-014, quickstart **S5.2**).
  - After an `Unknown` visit: same, with `consent = 'unknown'` (quickstart **S5.1b**).
  - After a `Granted` visit: `ip` holds the full address, `visitor_id` matches the cookie, `consent = 'granted'`.
  - A `Denied` **download** is still recorded and still appears in download totals (contract C3.4, FR-044, quickstart **S5.3**).
  - Call `LogVisit` directly with `Consent = Denied` but a non-null `VisitorId` and assert the store still writes `NULL` — proving the gate is in the store, not just the caller (contract C3.1).

- [x] T056 [P] [US5] Write `tests/AkmlSql.Site.Tests/Components/ConsentBarTests.cs` (bunit).
  - Renders only for `Unknown`; absent for `Granted` and `Denied` (contract C2.2).
  - Accept and Decline are both present as submit buttons with equal prominence.
  - The dismiss control submits `choice=denied` (contract C2.6, quickstart **S5.1c**).
  - The form has `method="post"`, `action="/consent"`, an antiforgery token and a `returnUrl` — it works with JS disabled.
  - **No overlay, modal, `aria-modal`, or scroll-locking markup is emitted** (FR-043b).
  - The bar is not rendered before the page's main content in a way that could obscure the download button.

- [x] T057 [P] [US5] Write `tests/AkmlSql.Site.Tests/Components/PrivacyPageTests.cs`.
  - The notice mentions that full IP addresses and a persistent identifier are stored.
  - **The stated retention number equals `SiteSettingsStore.Current.IdentifiableRetentionDays`** — change the setting in the test and assert the page text follows. This is the drift gate (SC-013, quickstart **S5.6**).
  - The withdrawal form is present and posts to `/privacy/forget`.

- [x] T058 [US5] **BLOCKED — needs a browser over https.** Manually verify quickstart **S5.1** (bar appears, blocks nothing, download clickable without touching it), **S5.4** (accept, return later, recognised as the same individual) and **S5.5** (withdraw → rows deleted, cookie expired). Use a fresh private window per scenario over **https** — a leftover cookie or plain http invalidates the result. Record in `specs/038-site-downloads-admin-portal/baseline.md`.

- [x] T059 [US5] Run the full site test suite and confirm green.

**Checkpoint**: Consent gates all identifiable collection. US3 may now safely begin.

---

## Phase 6: User Story 3 — The owner sees who downloaded and from where (Priority: P3)

**Goal**: Downloads grouped by country and version, an individuals list with filters, and a per-person
history — plus honest reporting of what the data cannot see.

**Independent Test**: Generate visits and downloads from several distinct consenting clients, then
confirm per-country download counts, the individuals list, the detail view, and that filtering by
country and by "downloaded" narrows every figure on screen.

**Depends on**: Phase 2 (schema) and **Phase 5 (consent)** — both mandatory.

### Write path

- [x] T060 [US3] Resolve and store the release version in `src/AkmlSql.Site/Analytics/DownloadEndpoint.cs` → `LogDownload`.
  - Add a helper `ResolveReleaseVersion(ReleasesManifest?, string fileName)` matching `Path.GetFileName(release.DownloadUrl)` case-insensitively against the requested file, returning `release.Version` or `null`.
  - Pass it onto `DownloadInfo.ReleaseVersion`, and pass consent state + visitor id from `HttpContext.Items` exactly as T049 does for visits.
  - **Resolve at write time, not read time**: the manifest is mutable, and a release later removed from it would retroactively orphan its historical downloads (research R6).
  - Do this in **both** the CDN-redirect branch and the local-stream branch, and in `HandleCount` — all three are real downloads.

- [x] T061 [US3] Persist the new download fields in `src/AkmlSql.Site/Analytics/AnalyticsStore.cs` → `LogDownload`: add `release_version` to the INSERT column list and parameters, alongside the consent-gated `ip` / `visitor_id` / `consent` from T051.

### Read models

- [x] T062 [P] [US3] Create `src/AkmlSql.Site/Analytics/Individuals.cs` with the read-model records.
  - `IndividualRow(string VisitorId, DateTimeOffset FirstSeen, DateTimeOffset LastSeen, string? Country, string? CountryCode, string? IpAddress, string? NetworkPrefix, string? Device, string? Os, string? Browser, long VisitCount, long DownloadCount, bool IsReturning)` with `Downloaded => DownloadCount > 0`.
  - `IndividualActivity(DateTimeOffset Utc, string Kind, string Target, string? ReleaseVersion, string? ReferrerUrl, string? Campaign)` where `Kind` is `"visit"` or `"download"`.
  - `IndividualDetail(IndividualRow Summary, IReadOnlyList<IndividualActivity> Activity)`.
  - `IndividualFilter(int Days, string? CountryCode, bool? Downloaded, int Page, int PageSize)`.
  - `CoverageSummary(long AttributedVisits, long UnattributedVisits, double UnattributedSharePercent, long AutomatedVisits, long DistinctIndividuals, long NewIndividuals, long ReturningIndividuals)`.
  - `DownloadCountryRow(string? Country, string? CountryCode, long Count, double SharePercent)` and `DownloadVersionRow(string? ReleaseVersion, string? File, long Count, double SharePercent)`.
  - Contract reference: `contracts/metrics-read-model.md` §1–§3.

- [x] T063 [US3] Add `GetDownloadsByCountry(int days, DateTimeOffset now)` to `src/AkmlSql.Site/Analytics/AnalyticsStore.cs`.
  - `GROUP BY country` over `downloads` within the window, ordered by count descending.
  - **A `NULL` country becomes an explicit "Unknown" bucket** — never dropped, never hidden (FR-028, contract M5.1). Rows must sum to the window total.
  - Compute `SharePercent` to one decimal.
  - Apply `RealDownloadOnly` (bots dropped, scripted clients kept) — a scripted installer fetch is a real acquisition (contract M5.5).

- [x] T064 [US3] Add `GetDownloadsByVersion(int days, DateTimeOffset now)` to `AnalyticsStore`.
  - `GROUP BY release_version, file`; `NULL` version → explicit "Unattributed" bucket; counts must sum to the window total (contract M5.2).

- [x] T065 [US3] Add `GetDailyDownloads(int days, DateTimeOffset now)` to `AnalyticsStore` if not already covered by the existing `DailyDownloads` on `AnalyticsSummary`.
  - Reuse `QueryDailySeries`. **Zero-fill every day** — a gap that renders as "no bar" rather than "a bar of zero" reads as missing data.

- [x] T066 [US3] Add `GetIndividuals(IndividualFilter filter, DateTimeOffset now)` to `AnalyticsStore`.
  - Aggregate over `visitor_id` across `visits` and `downloads` where `visitor_id IS NOT NULL` and the row is within the window.
  - `FirstSeen`/`LastSeen` = `MIN`/`MAX` of `utc` across **both** tables; country/address/network/device/os/browser = most recent non-null value.
  - `IsReturning` = first seen **before** the window start (FR-022a).
  - Apply the `CountryCode` and `Downloaded` filters in SQL, not in memory.
  - Server-side paging via `LIMIT`/`OFFSET` from `filter.Page` / `filter.PageSize`.
  - Exclude automated clients using the existing `HumanOnly` predicate (FR-027).
  - Order by last seen descending by default.

- [x] T067 [US3] Add `GetIndividual(string visitorId, int days, DateTimeOffset now)` to `AnalyticsStore` returning an `IndividualDetail`.
  - The activity stream is a `UNION ALL` of visits and downloads for that `visitor_id`, ordered by `utc`, projected into `IndividualActivity`.
  - **One interleaved stream, not two tables** — the question is "what did this person do", and interleaving is the only shape that answers it (FR-024, contract §2).
  - Return `null` when the id is unknown, so the page can render a clean not-found rather than an empty shell.

- [x] T068 [US3] Add `GetCoverage(int days, DateTimeOffset now)` to `AnalyticsStore` returning a `CoverageSummary`.
  - `AttributedVisits` = visits with non-null `visitor_id`; `UnattributedVisits` = the rest; share to one decimal (FR-047, SC-016).
  - `AutomatedVisits` reuses the existing automation predicate — reported separately, never silently discarded.
  - `DistinctIndividuals`, `NewIndividuals`, `ReturningIndividuals` per T066's definition.

### Portal views

- [x] T069 [US3] Create `src/AkmlSql.Site/Components/Pages/Admin/AdminDownloads.razor` at `/admin/downloads`.
  - `@layout AdminLayout`. Headline: total downloads in window, trend vs previous window.
  - Sections: downloads by country (ranked, count + share, Unknown shown); downloads by version; daily downloads chart.
  - Charts use the existing pure-CSS `.bar-h-*` bucket classes — **no chart library, no inline styles** (contract A7.2). Reuse `Components/Chart.razor` and `Components/MetricTable.razor` if they fit.
  - Export link to `/admin/downloads.csv` built with `AdminNav.WithRange`.
  - Must be usable at phone width (FR-036) — wide tables go in their own `overflow-x: auto` container.

- [x] T070 [US3] Create `src/AkmlSql.Site/Components/Pages/Admin/AdminPeople.razor` at `/admin/people`.
  - `@layout AdminLayout`. Filter controls as a plain GET form (country select, downloaded yes/no/any) so filters travel in the query string and the view stays bookmarkable (contract A4.5).
  - Table of `IndividualRow`: visitor id (shortened, linking to the detail page), first seen, last seen, country, visits, downloads, returning?
  - **Every summary figure on the page honours the active filters** — a filtered list above unfiltered totals is a misreading waiting to happen (FR-025).
  - **Render the coverage summary prominently**: distinct individuals **with** the unattributed share beside it. Never present "individuals" as "visitors" or "users" without it (contract M3.1, FR-047).
  - Add a short standing caveat in the UI: one identifier is one **browser**, not one human; a cleared cookie creates a new individual (contract C5.1).
  - Server-side paging controls.

- [x] T071 [US3] Create `src/AkmlSql.Site/Components/Pages/Admin/AdminPerson.razor` at `/admin/people/{visitorId}`.
  - `@layout AdminLayout`. Shows the full `IndividualRow` including the **full IP address** and derived network and country (FR-029), then the interleaved activity stream in time order.
  - A "Delete this individual" form posting to `/admin/people/{visitorId}/delete` with antiforgery and a confirmation step (FR-040).
  - Clean not-found state for an unknown id.

- [x] T072 [US3] Add the per-view CSV exports to `src/AkmlSql.Site/Admin/AdminEndpoints.cs` → `Map`: `/admin/downloads.csv`, `/admin/people.csv`, `/admin/pages.csv`.
  - Follow the existing `/admin/metrics.csv` pattern exactly: bind `days` as a `string?` and parse it (an unparseable int must not silently fall back), `Cache-Control: no-store`, `Results.File(...)` with a descriptive file name.
  - **Export the filtered rows currently displayed**, not the unfiltered table (FR-026, contract M6.1).
  - Put the active window and filters in **both** the file name and a header comment row, so a file found later is self-describing (M6.2).
  - Exports containing `ip` or `visitor_id` carry a header line labelling them as containing personal data (FR-049, M6.3).
  - Extend `Analytics/MetricsExport.cs` with the new `ToCsv` overloads rather than writing CSV inline in the endpoint.

- [x] T073 [US3] Add `POST /admin/people/{visitorId}/delete` to `src/AkmlSql.Site/Admin/AdminEndpoints.cs`, taking `IFormCollection` for antiforgery, calling the store's delete-by-visitor (T077), logging the deletion, and redirecting to `/admin/people` with a confirmation flag.

- [x] T074 [US3] Demote page-visit reporting: create `src/AkmlSql.Site/Components/Pages/Admin/AdminPages.razor` at `/admin/pages` and move the page-centric sections (top pages, entry/exit pages, bounce rate, sessions, pages per session, referrers, slow pages, 404s) out of `AdminDashboard.razor` into it.
  - **Retain every metric in full** — the owner called these "fine but not important", so they move, they do not disappear (FR-030).
  - Reuse the existing `AnalyticsSummary` shape unchanged; do not rebuild page-visit reporting.
  - Leave `AdminDashboard.razor` as a genuine overview: headline downloads, headline individuals with coverage, a small trend, and links into the detailed sections.

### Retention and deletion

- [x] T075 [US3] Add `DeIdentify(int identifiableRetentionDays, DateTimeOffset now)` to `src/AkmlSql.Site/Analytics/AnalyticsStore.cs`.
  - `UPDATE visits SET ip = NULL, visitor_id = NULL WHERE day < $boundary AND (ip IS NOT NULL OR visitor_id IS NOT NULL);` and the same for `downloads`. Return the number of rows affected.
  - **Null the columns; do not delete the rows.** That is what keeps country and version totals for an old period reconciling after the personal detail is gone (SC-008, contract M5.4/C6.2).
  - Idempotent: running it twice affects zero rows the second time.

- [x] T076 [US3] Call `DeIdentify` from `MaintenanceHostedService` (T026), using `SiteSettingsStore.Current.IdentifiableRetentionDays`, before the existing `Prune(RetentionDays)` call. Log the row count at Information when non-zero, matching the existing log style.

- [x] T077 [US3] Add `DeleteVisitor(string visitorId)` to `AnalyticsStore`, deleting every matching row from **both** `visits` and `downloads` in one transaction and returning the total deleted.
  - Must leave nothing that could re-link the visitor (FR-040, contract C4.3). Used by both `POST /privacy/forget` (T046) and the admin delete (T073).

### Tests

- [x] T078 [P] [US3] Write `tests/AkmlSql.Site.Tests/Analytics/DownloadGroupingTests.cs`.
  - Country grouping: counts correct, ranked descending, Unknown bucket present for `NULL` country, **shares sum to 100%**, and per-country counts sum to the headline total (SC-008, M5.1).
  - Version grouping: correct per version, Unattributed bucket for `NULL`, sums reconcile (M5.2).
  - A bot download is excluded; a `curl` download is **included** (M5.5, quickstart **S3.7**).
  - Daily series is zero-filled across the window.

- [x] T079 [P] [US3] Write `tests/AkmlSql.Site.Tests/Analytics/IndividualsQueryTests.cs`.
  - Two consenting visitors with distinct ids → two individuals with correct visit/download counts.
  - A visitor active across two days → **one** individual, first/last seen spanning both (SC-019, quickstart **S5.4**).
  - `IsReturning` true when first seen precedes the window.
  - Filtering by country and by `Downloaded` narrows the list **and** the coverage figures (FR-025, quickstart **S3.4**).
  - Non-consenting rows never produce an individual but **do** appear in headline totals (FR-044).
  - A bot never appears as an individual (FR-027).
  - **Same IP and user-agent, different visitor id → two individuals, never merged** (FR-022b, quickstart **S3.8**). This is the anti-re-linking guarantee; assert it explicitly.
  - `GetIndividual` returns an interleaved, time-ordered stream mixing visits and downloads.

- [x] T080 [P] [US3] Write `tests/AkmlSql.Site.Tests/Analytics/IdentifiableRetentionTests.cs`.
  - Seed rows older and newer than the boundary; run `DeIdentify`.
  - Assert old rows have `ip` and `visitor_id` null while their `country`, `release_version`, `day` and counts are untouched.
  - Assert per-country and per-version totals for that window are **identical before and after** (SC-008, M5.4, quickstart **S3.10**).
  - Assert newer rows are untouched, and a second run affects zero rows.
  - Assert `DeleteVisitor` removes rows from both tables and returns the correct count.

- [x] T081 [P] [US3] Write `tests/AkmlSql.Site.Tests/Analytics/CoverageSummaryTests.cs`: with a mix of granted, denied and unknown traffic, `UnattributedSharePercent` is correct and attributed + unattributed equals the headline total (FR-047, SC-016, contract M5.3, quickstart **S3.6**).

- [x] T082 [P] [US3] Write `tests/AkmlSql.Site.Tests/Components/AdminPeoplePageTests.cs` (bunit): the list renders rows, the coverage summary with the unattributed share is present, filters are reflected in every figure, and the per-browser caveat text is rendered.

- [x] T083 [P] [US3] Write `tests/AkmlSql.Site.Tests/Admin/MetricsExportTests.cs`: each export contains only the filtered rows, the window and filters appear in the file name and a header row, exports with personal data carry the label, and `Cache-Control: no-store` is set (FR-026, FR-049, M6.1–M6.4, quickstart **S3.9**).

- [ ] T084 **BLOCKED — needs `AKML_SITE_ADMIN_PASSWORD`.** Playwright is wired; `AkmlSql.Site.E2E.Tests.AdminPortalTests` covers this and skips without the password. [US3] Manually verify quickstart **S3.1** (country grouping answerable in under 15 s — SC-006) and **S3.3** (one individual's full history reachable in under 30 s — SC-007). Record in `specs/038-site-downloads-admin-portal/baseline.md`.

- [x] T085 [US3] Run the full site test suite and confirm green.

**Checkpoint**: The owner can answer "who downloaded, from where, of which version" — with honest coverage reporting.

---

## Phase 7: User Story 4 — The owner runs the whole site from one portal (Priority: P4)

**Goal**: One portal with persistent navigation, a shared range, and visibility into what is actually
on disk.

**Independent Test**: Sign in and reach every section from navigation without typing a URL; confirm the
range chosen in one section still applies in another; confirm signing out blocks every section.

**Depends on**: Phase 2 (shell) and the pages created in Phases 4–6.

- [x] T086 [US4] Complete navigation in `src/AkmlSql.Site/Components/Layout/AdminLayout.razor`: every section from `AdminNav.Sections` is present, the active one carries `aria-current="page"`, and each is reachable in one click from any other (FR-031, SC-010, contract A3.1–A3.4).

- [x] T087 [US4] Ensure range propagation everywhere: every nav link, every export link and every in-page link inside the portal is built with `AdminNav.WithRange(route, days)`, and each section displays the active window visibly (FR-032, contract A4.2/A4.3).
  - Audit all portal pages for hard-coded `/admin/...` hrefs that drop the range.

- [x] T088 [US4] Create `src/AkmlSql.Site/Components/Pages/Admin/AdminReleases.razor` at `/admin/releases`.
  - `@layout AdminLayout`. For every manifest entry: version, released date, CDN mirror present?, local file present?, size, currently advertised?
  - **Flag advertised-but-missing** (in the manifest, no CDN, no local file) — the case that turns the primary call to action into a dead link (A6.2).
  - **Flag present-but-unadvertised** — enumerate the downloads folder (`DownloadsOptions.Folder`) and list files no manifest entry references (A6.3). This is what permanently answers research open item 1 from T003.
  - Read-only. **No editing of release metadata and no installer upload** — explicitly out of scope (A6.4).
  - Use `ReleaseAvailability.FormatSize` for sizes so the display matches the public page.

- [x] T089 [US4] Verify `AdminPages.razor` (T074) is wired into navigation and honours the shared range.

- [x] T090 [P] [US4] Extend `tests/AkmlSql.Site.Tests/Components/AdminPagesTests.cs` with the **auth enumeration test**.
  - Enumerate every route and export in `contracts/admin-portal-surface.md` §1 — `/admin`, `/admin/downloads`, `/admin/people`, `/admin/people/{id}`, `/admin/pages`, `/admin/errors`, `/admin/releases`, `/admin/settings`, and all four `.csv` exports — and assert `AdminBranchMiddleware.RequiresChallenge` is `true` for each without an authenticated cookie, and that `/admin/login` is `false`.
  - **Drive the list from `AdminNav.Sections`** where possible, so a section added later without a guard fails this test (FR-033, SC-011, quickstart **S4.3**, contract A2.1).

- [x] T091 [P] [US4] Write `tests/AkmlSql.Site.Tests/Components/AdminRangePropagationTests.cs`: rendering any portal page with `?days=7` produces nav links and export links that all carry `days=7`, and the active window is visible in the markup (quickstart **S4.2**).

- [x] T092 [P] [US4] Write `tests/AkmlSql.Site.Tests/Components/AdminReleasesPageTests.cs`: advertised-but-missing and present-but-unadvertised are both flagged; a CDN-backed release with no local file shows as available; sizes render via `FormatSize`.

- [x] T093 [P] [US4] Add a test asserting `/admin/login` does **not** render the portal shell — a sign-in page must not display navigation to sections the visitor cannot reach (contract A3.5).

- [ ] T094 **BLOCKED — needs `AKML_SITE_ADMIN_PASSWORD`.** Playwright is wired; `AkmlSql.Site.E2E.Tests.AdminPortalTests` covers this and skips without the password. [US4] Manually verify quickstart **S4.1** (every section from nav), **S4.4** (releases flags), **S4.5** (settings confirmation) and **S4.6** (overview and downloads at 400 px, no horizontal page scroll). Record in `specs/038-site-downloads-admin-portal/baseline.md`.

- [x] T095 [US4] Run the full site test suite and confirm green.

**Checkpoint**: All five user stories are independently functional.

---

## Phase 8: Polish, documentation currency and cross-cutting concerns

**Purpose**: Correct every statement the privacy reversal made false, close the two deferred items from
`/speckit.clarify`, and run the acceptance gate.

**⚠️ The documentation tasks are not optional cleanup.** The constitution's Additional Constraints
require documentation currency, and these files currently assert the *opposite* of what the code now
does. Leaving them is shipping a false privacy claim to users.

- [x] T096 [P] Correct the false privacy statement in `src/AkmlSql.Site/Components/Pages/Admin/AdminDashboard.razor`.
  - The visible `admin-privacy` paragraph currently reads: *"full IP addresses are never stored … No cookies are set for visitors."* **Both halves are now false.**
  - Replace with an accurate statement: full addresses and a persistent identifier are stored **for consenting visitors**; non-consenting visitors keep only the truncated prefix and the daily salted hash; two cookies are set (consent and, with consent, identity); identifiable data is removed after the configured retention period. Read the retention number from `SiteSettingsStore`, not a literal.

- [x] T097 [P] Correct the class summary in `src/AkmlSql.Site/Analytics/IpAnonymizer.cs`, which states *"The full address … is never persisted"*. Describe the class's actual remaining role: deriving the truncated network prefix used for grouping and for non-consenting rows. Keep the class — it is still used.

- [x] T098 [P] Correct the rationale in `src/AkmlSql.Site/Analytics/GeoLookup.cs`, whose summary is built on "the least risky way to hold data you do not need is not to hold it". Country-only collection is still true and still deliberate; the surrounding framing about minimal retention is not. Adjust precisely — do not delete the country-only rationale, which still holds.

- [x] T099 [P] Rewrite the `Analytics._note` in `src/AkmlSql.Site/appsettings.json` to describe the new model, and add a `Consent` section note plus the new settings' storage location (the `site_settings` table in `analytics.db`, owner-editable at `/admin/settings`).

- [x] T100 [P] Update `doc/configuration.md` with the new settings, the two cookies, the two retention boundaries, and the consent model.

- [x] T101 Add a spec-038 entry to `doc/progress.md` following the existing per-spec format: what shipped, the root causes found (double-evaluated release list, CDN-blind availability, unconfigured IIS cold start), the privacy reversal and why, the measured before/after numbers from `baseline.md`, and any deferred items.

- [x] T102 [P] Update `CLAUDE.md` if the site's structure section needs the new `Settings/` and `Consent/` folders listed. Keep the edit minimal and in the file's existing style.

- [x] T103 [P] Accessibility pass on the consent bar and the portal (deferred item from `/speckit.clarify`).
  - Consent bar: keyboard reachable in DOM order, both buttons focusable and operable by keyboard, `role="region"` with an `aria-label`, does not steal focus on load, does not trap focus, and is announced sensibly by a screen reader.
  - Portal: `aria-current="page"` on the active nav item, tables have proper `<th scope>`, filter forms have associated `<label>`s, charts have accessible text alternatives (the existing dashboard's patterns are the reference).
  - Add assertions for the markup-level items to the relevant bunit tests.

- [x] T104 [P] Add throttling to the new public POST endpoints (deferred item from `/speckit.clarify`).
  - `POST /consent` and `POST /privacy/forget` are new unauthenticated public endpoints. Reuse the existing `AdminLoginThrottle` pattern rather than inventing a second mechanism (constitution Principle V).
  - Keep it light — the abuse surface is small (a visitor can only delete their own id) — but unbounded public POSTs should not go out unthrottled.

- [x] T105 [P] Write `tests/AkmlSql.Site.Tests/Analytics/DisclosureBoundaryTests.cs` (SC-015, contract C7.1).
  - Render the public pages, `/sitemap.xml`, `/robots.txt`, `/search-index.json` and an error response, and assert none contains a full IP address or a visitor id.
  - Review every log message template touching visits or downloads and assert none interpolates `ip` or `visitor_id`.
  - Assert the `/health` payload exposes no personal data.

- [x] T106 [P] Normalise terminology across the new UI (deferred item from `/speckit.clarify`): **"individual"** is the canonical term in the spec. Use it consistently in page headings, table captions and export headers rather than mixing "person", "visitor" and "user" — and keep "visitor" only where it genuinely means any site visitor, consenting or not.

- [x] T107 Verify the theme drift gate is green: run `scripts/generate-theme-css.ps1 -CheckOnly` (or `build.ps1` step 1) and confirm no hand-edited theme CSS. All new styling must live in `wwwroot/css/site.css` (constitution II, quickstart **SX.4**).

- [x] T108 Run the complete `quickstart.md` validation pass — every `[AUTO]` scenario via `dotnet test`, every `[MANUAL]` scenario by hand — and record each result in `specs/038-site-downloads-admin-portal/baseline.md`. **This is the acceptance gate for "done"** (constitution: Development Workflow).

- [x] T109 Run the full solution build in one pass to confirm nothing outside the site broke:
  ```bash
  MSBUILD="/c/Program Files/Microsoft Visual Studio/18/Insiders/MSBuild/Current/Bin/MSBuild.exe"
  "$MSBUILD" AKML-SQL.slnx -t:Restore -v:quiet
  "$MSBUILD" AKML-SQL.slnx -t:Build -p:Configuration=Release -m -v:minimal
  ```
  - Compare failures against the T002 baseline. Pre-existing reds noted there are not regressions; anything new is.

- [x] T110 Final report: summarise what shipped, the measured before/after performance numbers, any scenario that did not pass and why, and any deferred work — recorded in `doc/progress.md`. **Do not commit anything** — deliver the changes uncommitted and wait for explicit instruction (constitution Principle IV).

---

## Dependencies & Execution Order

### Phase dependencies

```
Phase 1 (Setup)
    ↓
Phase 2 (Foundational) ──── BLOCKS EVERYTHING BELOW
    ↓
    ├──→ Phase 3 (US1, P1) 🎯 MVP ── independently deployable
    ├──→ Phase 4 (US2, P2) ── needs Phase 2 settings store
    ├──→ Phase 5 (US5, P3) ── MUST precede Phase 6
    │        ↓
    │    Phase 6 (US3, P3) ── needs Phase 2 schema + Phase 5 consent
    └──→ Phase 7 (US4, P4) ── needs pages from Phases 4–6 to navigate to
             ↓
         Phase 8 (Polish)
```

### The one hard ordering constraint

**Phase 5 (consent) MUST complete before Phase 6 (metrics).** Building them in the other order ships a
window in which identifiable data is collected with no consent path — the precise thing the spec
exists to prevent. Everything else is flexible.

### Story independence

| Story | Can start after | Independently testable? | Independently deployable? |
|---|---|---|---|
| US1 (P1) | Phase 2 | Yes | **Yes — this is the MVP** |
| US2 (P2) | Phase 2 | Yes | Yes |
| US5 (P3) | Phase 2 | Yes | Yes (bar + privacy page, collecting nothing new) |
| US3 (P3) | Phase 5 | Yes | Yes |
| US4 (P4) | Phases 4–6 | Yes | Yes |

### Within each story

Models → store/query methods → endpoints/middleware → pages → tests → manual verification.

---

## Parallel Execution Opportunities

Tasks marked **[P]** touch different files and have no incomplete dependencies.

**Phase 1** — T003, T004, T005 in parallel (independent fact-finding).

**Phase 2** — T009, T010 in parallel (different new files); T008, T012, T018 in parallel (three
independent test files).

**Phase 3 (US1)** — T020, T023, T024, T025 in parallel once T019/T021/T022 land; T029, T030, T031 in
parallel.

**Phase 4 (US2)** — T036, T037, T038, T039, T040 in parallel once T033–T035 land.

**Phase 5 (US5)** — T043 alone first; then T052–T057 all in parallel once T044–T051 land.

**Phase 6 (US3)** — T062 first; then T078–T083 in parallel once the queries and pages land.

**Phase 7 (US4)** — T090, T091, T092, T093 in parallel.

**Phase 8** — T096–T100, T102–T106 nearly all in parallel (different files). T107–T110 are sequential
and last.

### Example parallel batch — Phase 5 tests

```text
Task: "Write tests/AkmlSql.Site.Tests/Consent/ConsentCookiesTests.cs"
Task: "Write tests/AkmlSql.Site.Tests/Consent/ConsentMiddlewareTests.cs"
Task: "Write tests/AkmlSql.Site.Tests/Consent/ConsentEndpointsTests.cs"
Task: "Write tests/AkmlSql.Site.Tests/Consent/ConsentStorageGateTests.cs"
Task: "Write tests/AkmlSql.Site.Tests/Components/ConsentBarTests.cs"
Task: "Write tests/AkmlSql.Site.Tests/Components/PrivacyPageTests.cs"
```

---

## Implementation Strategy

### MVP first — stop after Phase 3

1. Phase 1 (Setup) — baseline recorded.
2. Phase 2 (Foundational) — schema, settings, shell.
3. Phase 3 (US1) — the download fix.
4. **STOP. Deploy. Measure.** The reported defect is fixed and the site is faster. Everything after
   this is additive.

### Incremental delivery

| Increment | Phases | Delivers |
|---|---|---|
| 1 (MVP) | 1 → 3 | Fast, correct download page |
| 2 | 4 | Owner controls published versions |
| 3 | 5 | Consent, privacy notice, deletion |
| 4 | 6 | Country + per-individual metrics |
| 5 | 7 | Full portal navigation |
| 6 | 8 | Documentation currency, accessibility, acceptance gate |

Each increment is deployable and adds value without breaking the previous one.

---

## Task Summary

| Phase | Story | Tasks | Count |
|---|---|---|---|
| 1 — Setup | — | T001–T005 | 5 |
| 2 — Foundational | — | T006–T018 | 13 |
| 3 — Download experience 🎯 | US1 (P1) | T019–T032 | 14 |
| 4 — Release visibility | US2 (P2) | T033–T042 | 10 |
| 5 — Consent | US5 (P3) | T043–T059 | 17 |
| 6 — Metrics | US3 (P3) | T060–T085 | 26 |
| 7 — Portal | US4 (P4) | T086–T095 | 10 |
| 8 — Polish | — | T096–T110 | 15 |
| **Total** | | | **110** |

**Parallelisable**: 49 tasks marked [P].
**MVP scope**: Phases 1–3 (T001–T032, 32 tasks).

---

## Notes for the implementer

- **Measure, don't assume.** T001 and T028 exist because a performance fix that does not move the
  number is not a fix. If the after-numbers don't improve, say so and investigate rather than
  declaring success.
- **The consent gate lives in the store** (T051), not only in the UI. If you find yourself relying on
  a page or middleware to prevent collection, the gate is in the wrong place.
- **Do not "fix" low individual coverage.** Asking everyone and never blocking will produce a small
  individuals list *by design* (spec Clarifications, contract C2.7). Making the prompt blocking, or
  inferring consent from silence, would violate FR-043b and FR-043a respectively.
- **Do not re-link visitors** by IP or user-agent when the cookie is gone (FR-022b). It would
  reconstruct exactly what a visitor clearing their cookies asked to break.
- **The privacy text is load-bearing.** T096–T099 correct statements that become false the moment
  T051 ships. Do not defer them past the deploy.
- **Never commit.** Deliver uncommitted and wait for explicit instruction (constitution IV).
- Stop at any checkpoint to validate a story independently.
