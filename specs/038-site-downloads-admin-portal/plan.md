# Implementation Plan: Site Download Experience and Full Admin Portal

**Branch**: `038-site-downloads-admin-portal` | **Date**: 2026-09-12 | **Spec**: [spec.md](./spec.md)
**Input**: Feature specification from `/specs/038-site-downloads-admin-portal/spec.md`

## Summary

Three deliverables against one existing ASP.NET Core static-SSR site (`src/AkmlSql.Site`):

1. **Fix the download path (US1)**. Two independent causes, both confirmed by reading the code: the page probes the filesystem twice per release for 16 releases and refuses to offer a release whose *local* file is absent even when a working CDN mirror exists; and the IIS deploy sets `startMode=AlwaysRunning` without `idleTimeout=0`, `preloadEnabled` or `applicationInitialization`, so the app re-initialises its whole docs corpus on the first request after an idle recycle.
2. **Put release visibility under an owner setting (US2)**. A new `site_settings` table in the existing `analytics.db`, read through a cached singleton, applied to the download page *before* any availability probe runs — so the setting also bounds the work in (1).
3. **Grow `/admin` into a portal with per-person metrics (US3/US4/US5)**. Adds full-IP and persistent-visitor columns to the existing SQLite schema via the migration mechanism already in `AnalyticsStore`, a consent gate that governs whether either is written, an individuals list and detail view, country/version download grouping, and a shared admin layout with persistent navigation.

The privacy decisions taken on 2026-09-12 (full addresses, cookie identity, 365-day retention) reverse a design the existing code states explicitly in comments, in the dashboard's own privacy paragraph, and in `IpAnonymizer`'s summary. Those statements become false the moment this ships, so correcting them is in scope, not cleanup.

## Post-plan clarifications (2026-09-12)

Three decisions were taken after this plan was first written; all are folded into the artifacts
below and none changed the architecture:

| Decision | Effect on the plan |
|----------|--------------------|
| Consent bar shown to **every** visitor, no geo-gating (FR-043a) | `ConsentMiddleware` must not consult `GeoLookup`. Removes geo from the consent path entirely — one fewer dependency, not more. |
| Consent bar is **non-blocking**; explicit dismissal records Decline; silence stays unresolved (FR-043b) | Pure presentation + state rule. No new component. Adds quickstart scenarios S5.1a–S5.1c. |
| Settings unreadable → serve the **documented default**, log loudly, never fail the page (FR-016a) | Confirms the R4 caching design and adds an explicit fallback path plus scenario S2.7. Startup-fail was considered and rejected. |

The first two together mean individual-level coverage will be a **minority of traffic** by design.
That is an accepted trade in favour of US1, recorded in contract C2.7 so it is not later "fixed" by
making the prompt blocking or inferring consent from silence.

## Technical Context

**Language/Version**: C# / .NET 10 (`net10.0`)
**Primary Dependencies**: ASP.NET Core static-SSR Razor Components (no interactive render mode), `Microsoft.Data.Sqlite` 10.x (plain ADO.NET, no EF Core), `MaxMind.GeoIP2` 6.x, Markdig + Markdown.ColorCode (docs only)
**Storage**: SQLite at `%ProgramData%\AKML SQL Site\analytics.db` (WAL), plus `salt.bin` beside it; installer files in a folder outside the app root
**Testing**: xunit + bunit (`tests/AkmlSql.Site.Tests`), existing suites under `Admin/`, `Analytics/`, `Components/`, `Releases/`, `Telemetry/`
**Target Platform**: Windows Server / IIS in-process (`scripts/deploy-site-iis.ps1`), app pool `AkmlSqlSite`
**Project Type**: Web application — one deployable, server-rendered, no client framework
**Performance Goals**: download page usable < 3 s cold / < 1 s warm p95 (SC-001); transfer starts < 2 s of click (SC-002); render cost flat from 1 to 100 releases (SC-003); metrics add < 10 ms per request (SC-012)
**Constraints**: strict CSP (`script-src 'self'`, no inline script or style — CSS bar charts use precomputed bucket classes); static SSR only, every feature must work with JavaScript disabled (FR-009); metrics are fire-and-forget and must never fail a request (FR-041); theme CSS is generated from `docs/theme-tokens.json` and must not be hand-edited
**Scale/Scope**: single-administrator portal; low-thousands of rows/day; 16 releases today; ~400 KB of docs markdown parsed at startup

No `NEEDS CLARIFICATION` items remain — the three spec-level unknowns were resolved in the spec's Clarifications section on 2026-09-12, and every technical unknown is resolved in [research.md](./research.md).

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-checked after Phase 1 design.*

| Principle | Applies? | Assessment |
|-----------|----------|------------|
| **I. Process Isolation & Host Safety** | No | Site-only feature. No shell extension, engine, or IPC surface is touched. No `.projitems` change. |
| **II. Build Integrity (Gates Are Law)** | Yes | **PASS.** `AkmlSql.Site` builds with `dotnet`; the MSBuild-only rule covers shell projects, not this one. New admin styling extends `wwwroot/css/site.css`; the generated `wwwroot/css/themes/*.css` files stay untouched, so the `generate-theme-css.ps1 -CheckOnly` drift gate stays green. |
| **III. Tests Are Non-Regressible** | Yes | **PASS.** `tests/AkmlSql.Site.Tests` exists and is extended, not replaced. No formatter or completion corpus is involved, so neither ratchet moves. New behaviour lands with tests; the performance criteria that cannot be asserted without timing flake (SC-001/SC-002) are gated by a probe-count test plus a manual quickstart measurement instead. |
| **IV. Git Consent (NON-NEGOTIABLE)** | Yes | **PASS.** Planning artifacts are written to the working tree only. Nothing staged, committed, or pushed. |
| **V. Simplicity & Convention Fidelity** | Yes | **PASS with one recorded tension** — see below and Complexity Tracking. |

### Principle V in detail

The design deliberately reuses rather than parallels:

- Settings live in a new table inside the **existing** `analytics.db` — already created, ACL'd for the app pool by the deploy script, and backed up with the metrics — rather than a second file, a second connection string, or `appsettings.json` (which IIS would recycle the app on writing).
- New columns arrive through `AnalyticsStore`'s **existing** `AddColumnIfMissing` migration loop, which is how every enrichment column so far was added to an installed database without resetting months of history.
- The portal reuses the **existing** admin cookie scheme, `AdminBranchMiddleware`, `AdminLoginThrottle`, and the query-string range mechanism from `AdminDashboardOptions` — no new auth path, and range state stays in the URL so pages remain static-SSR and bookmarkable.
- The consent banner is a plain form POST, matching the site's no-JS-first posture and requiring **no CSP change**.

**The tension**: this feature is large for one spec — five user stories touching the public page, the storage schema, the request pipeline, and the admin surface. It is not speculative generality (every part is explicitly requested), but it is a lot at once. Mitigation is sequencing: US1 ships and is verifiable alone; US2 adds one setting; US3/US5 ship together because consent gates the data collection; US4 is the shell that ties them together. Recorded in Complexity Tracking.

**Documentation currency (Additional Constraints)**: the privacy reversal invalidates statements in `AnalyticsStore`'s class summary, `IpAnonymizer`'s summary, `GeoLookup`'s rationale, `AdminDashboard.razor`'s visible privacy paragraph, and `appsettings.json`'s `Analytics._note`. All five are in scope. `doc/progress.md` gets a spec-038 entry.

### Post-Phase-1 re-check

Re-evaluated after producing the data model and contracts: **no new violations.** The design added no new project, no new storage engine, no new auth mechanism, and no new client-side framework. The one new middleware (consent resolution) sits beside the two that already exist and follows their shape. Contracts are documents, not code surface.

## Project Structure

### Documentation (this feature)

```text
specs/038-site-downloads-admin-portal/
├── plan.md                              # This file
├── spec.md                              # Feature specification
├── research.md                          # Phase 0 output
├── data-model.md                        # Phase 1 output
├── quickstart.md                        # Phase 1 output — the acceptance gate
├── contracts/
│   ├── release-visibility.md            # Setting semantics + public-page effect
│   ├── consent-and-identity.md          # Cookies, consent states, what is written when
│   ├── metrics-read-model.md            # Portal query shapes and reconciliation rules
│   └── admin-portal-surface.md          # Routes, navigation, range propagation, auth
├── checklists/
│   └── requirements.md                  # Spec quality checklist (16/16)
└── tasks.md                             # Phase 2 output — NOT created by /speckit.plan
```

### Source Code (repository root)

```text
src/AkmlSql.Site/
├── Program.cs                           # + consent middleware, settings singleton, deferred maintenance
├── appsettings.json                     # + Consent section; Analytics._note rewritten
├── Settings/                            # NEW — owner-editable settings
│   ├── SiteSettings.cs                  # POCO + defaults + validation
│   ├── SiteSettingsStore.cs             # SQLite-backed read/write over analytics.db
│   └── ReleaseVisibility.cs             # LatestOnly | LatestN | All + N bounds
├── Releases/
│   ├── ReleaseAvailability.cs           # CDN-aware availability; per-request memoization
│   └── ReleasesManifest.cs              # unchanged
├── Analytics/
│   ├── AnalyticsModels.cs               # + VisitorId, IpAddress retention, ReleaseVersion
│   ├── AnalyticsStore.cs                # + ip/visitor_id/release_version columns, individuals queries, de-identify prune
│   ├── VisitTrackingMiddleware.cs       # + consent-gated visitor id
│   ├── DownloadEndpoint.cs              # + release version resolution, consent-gated fields
│   ├── IpAnonymizer.cs                  # kept for derived grouping; summary corrected
│   └── Individuals.cs                   # NEW — individual row/detail read models
├── Consent/                             # NEW — US5
│   ├── ConsentState.cs                  # Unknown | Granted | Denied
│   ├── ConsentCookies.cs                # cookie names, options, issue/read/clear
│   ├── ConsentMiddleware.cs             # resolves state onto HttpContext.Items
│   └── ConsentEndpoints.cs              # POST /consent, POST /privacy/forget
├── Admin/
│   ├── AdminDashboardOptions.cs         # range propagation across sections
│   ├── AdminEndpoints.cs                # + settings POST, per-view CSV exports, delete-individual
│   └── AdminNav.cs                      # NEW — section list, active detection
└── Components/
    ├── Layout/AdminLayout.razor         # NEW — portal shell with persistent nav
    └── Pages/
        ├── Download.razor               # single-pass release list, visibility-bounded
        ├── Privacy.razor                # NEW — notice, withdraw, deletion request
        └── Admin/
            ├── AdminDashboard.razor     # becomes the overview; privacy text corrected
            ├── AdminDownloads.razor     # NEW — country + version + trend
            ├── AdminPeople.razor         # NEW — individuals list + filters
            ├── AdminPerson.razor        # NEW — one individual's history
            ├── AdminPages.razor         # NEW — page visits (demoted from lead)
            ├── AdminReleases.razor      # NEW — advertised vs present on disk
            ├── AdminSettings.razor      # NEW — release visibility + retention
            └── AdminErrors.razor        # existing, re-parented to AdminLayout

tests/AkmlSql.Site.Tests/
├── Settings/SiteSettingsStoreTests.cs           # NEW
├── Consent/ConsentMiddlewareTests.cs            # NEW
├── Consent/ConsentEndpointsTests.cs             # NEW
├── Analytics/IndividualsQueryTests.cs           # NEW
├── Analytics/IdentifiableRetentionTests.cs      # NEW
├── Analytics/AnalyticsStoreTests.cs             # extended — new columns, migration in place
├── Releases/ReleaseAvailabilityTests.cs         # NEW — CDN-aware availability
├── Components/DownloadPageTests.cs              # extended — probe count, visibility, ordering
├── Components/AdminPagesTests.cs                # extended — nav, range propagation, auth
└── Components/PrivacyPageTests.cs               # NEW — notice matches actual behaviour

scripts/deploy-site-iis.ps1                      # + idleTimeout=0, preloadEnabled, applicationInitialization
```

**Structure Decision**: Single existing web application. No new project is introduced — `AkmlSql.Site` already owns the public pages, the analytics pipeline and the admin area, and the constitution's Principle V favours extending it over standing up a parallel admin service. New folders (`Settings/`, `Consent/`) follow the established one-folder-per-concern convention already used by `Analytics/`, `Admin/`, `Releases/`, `Telemetry/`, `Seo/` and `Docs/`. Test files mirror that layout under `tests/AkmlSql.Site.Tests`, as every existing suite does.

## Implementation Sequencing

Ordered so each step is independently verifiable, matching the spec's story priorities:

| Step | Story | Ships | Verifiable by |
|------|-------|-------|---------------|
| 1 | US1 | CDN-aware availability, single-pass release list, IIS preload/idle settings | Probe-count test; quickstart cold/warm measurement |
| 2 | US2 | `site_settings` table, settings store, release-visibility setting, settings page | Set "latest only", load public page signed-out |
| 3 | US5 | Consent state, banner, cookies, privacy page, withdrawal + deletion | Decline in a fresh browser, confirm nothing identifiable is written |
| 4 | US3 | Full-IP + visitor-id columns, release version, individuals queries, downloads/people views | Generate traffic from distinct clients, check grouping and detail |
| 5 | US4 | `AdminLayout`, persistent nav, range propagation, releases + pages sections | Reach every section from nav; range survives navigation |
| 6 | — | Documentation currency: code comments, dashboard privacy copy, `appsettings` notes, `doc/progress.md` | Privacy-page test asserts notice matches configured behaviour |

Step 3 must precede step 4: the consent gate decides whether the columns added in step 4 are ever populated, and building them in the other order means shipping a window in which identifiable data is collected without a consent path.

## Complexity Tracking

> Filled because Constitution Check recorded one tension under Principle V.

| Violation | Why Needed | Simpler Alternative Rejected Because |
|-----------|------------|--------------------------------------|
| Feature spans five user stories and four subsystems in one spec | The owner's request is a single coherent goal — make the download path work and let me see who uses it — and the parts are genuinely coupled: the visibility setting is what bounds the page's probe work (US1↔US2), and consent gates the very data US3 reports (US3↔US5). Splitting them into separate specs would create ordering dependencies across branches with no reduction in total work. | Splitting into 2–3 specs was considered. Rejected because US2 without US1 leaves the page slow, US3 without US5 ships unlawful collection, and US4 alone delivers nothing. Mitigated by the sequencing table above: each step is independently testable and deployable. |
| Reversing an explicit, documented privacy design | Directly instructed by the owner on 2026-09-12 after being shown the trade-off, including that it brings consent obligations. | Keeping truncation was offered as option A and declined. The mitigation is that the reversal is *complete* — every code comment, the dashboard's visible privacy paragraph, and the published notice are corrected in the same change rather than left stating the old behaviour. |
