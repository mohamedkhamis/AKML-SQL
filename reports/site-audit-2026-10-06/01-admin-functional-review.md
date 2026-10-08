# Task 1 — Admin portal functional review

Reviewed 2026-10-06. Source baseline: `5d2c6f4`. Target: `akml.khamis.work`, IIS site `AkmlSqlSite`, deployed at `C:\inetpub\akml.khamis.work`.

## Scope and evidence

- Read the portal pages, endpoint handlers, stores, consent pipeline, download tracking, desktop error sender, and specifications 034/038. The deployed `AkmlSql.Site.dll` SHA-256 matched the current Release build.
- **877 Site unit/component tests passed.** Separately exercised a copy of the published production application on local HTTPS, in Production mode, using a new database with 520 synthetic visitors, 205 synthetic feedback messages, a local download fixture, and a loopback SMTP sink. No real email was sent.
- Tested all ten protected admin page routes, login, all four CSV exports, 24 page/range combinations, settings writes, visitor deletion, feedback actions, consent/withdrawal, download endpoints, and valid client-error ingestion. This exposed defects not caught by the unit suite.
- Production database inspection used SQLite `mode=ro` plus `query_only`. Only aggregate counts/schema/configuration were collected. Production admin credentials were unavailable; **authenticated production UI actions were not exercised**. Destructive actions and submissions were exercised only against synthetic data.
- Evidence is in ignored `artifacts/site-audit-2026-10-06/`: `functional-results.json`, `functional-supplement.json`, `production-aggregates.json`, and the local harnesses. The initial source-hosted trial had static-asset/test-harness failures; the final review used the copied published application. Do not treat that initial trial as production evidence.

## Feature inventory

Paths below are relative to the repository. `Pages/` in the location column means `src/AkmlSql.Site/Components/Pages/Admin/`; other shortened paths are relative to `src/AkmlSql.Site/`.

| Page / feature | What exists and verification | Broken, incomplete, or important limit | Location |
|---|---|---|---|
| `/admin/login` | Single shared password; wrong password rejected; correct local password signs in. Cookie is Secure, HttpOnly, SameSite=Lax. | Missing-token POST becomes HTTP 500 through error re-execution, rather than a clean rejection. Security details in Task 2. No accounts, roles, MFA or password-change UI. | `Pages/AdminLogin.razor:46`; `Admin/AdminEndpoints.cs:126`; `Admin/AdminAuth.cs:41` |
| Shared portal shell | Ten protected routes, section navigation, range links and logout. All protected page/export GETs redirected an unauthenticated browser to login. | Shared range links discard page-specific filters; no distinct staff identities. | `Components/Layout/AdminLayout.razor:39`; `Admin/AdminNav.cs:23`; `Admin/AdminBranchMiddleware.cs:26` |
| `/admin` Overview | Visits, daily-summed unique estimates, downloads, conversion, prior-period deltas, daily/hourly charts, session metrics, browser/country/referrer/campaign/404 tables and CSV. Render/ranges/export worked. | These are website events and estimated visitor-days, **not installed users or completed downloads**. Session/unique estimates merge some users behind NAT. | `Pages/AdminDashboard.razor:67`; `Analytics/AnalyticsModels.cs:267`; `Analytics/AnalyticsStore.cs:290` |
| `/admin/downloads` | Country and release/file breakdowns, shares, trend and export. Unknown-country state explicitly distinguishes missing geo database from older unclassified records. | No completed/failed-transfer metric; no direct source/date/version drilldown. Historical source attribution is nearly absent in production. | `Pages/AdminDownloads.razor:1`; `Analytics/AnalyticsStore.Individuals.cs:27`; `Admin/AdminEndpoints.cs:56` |
| `/admin/insights` | Conversion by country, acquisition source and landing page; hourly/weekday heatmap; new/returning consenting visitors. Tested ranges rendered. | Conversion matches daily IP hashes; it is not a visit→download→install funnel. No export or install/activation/retention cohorts. | `Pages/AdminInsights.razor:72`; `Analytics/AnalyticsStore.Insights.cs:60` |
| `/admin/people` | Consent-based list; country/downloaded filters; 50-row paging; coverage caveat; CSV. Paging and filters return rows. | Missing visitor-only countries in selector; unfiltered headline above filtered rows; silently capped CSV; sorting options promised by the older contract are absent. | `Pages/AdminPeople.razor:28`; `Pages/AdminPeople.razor:193`; `Admin/AdminEndpoints.cs:73` |
| `/admin/people/{id}` | Summary, interleaved activity and deletion of all matching visit/download rows. Local deletion and missing-person state worked. | Summary dimensions use independent lexical maxima, not the latest event. Delete is immediate, with no second confirmation. | `Pages/AdminPerson.razor:47`; `Pages/AdminPerson.razor:124`; `Analytics/AnalyticsStore.Individuals.cs:353` |
| `/admin/pages` | Most visited, entry/exit pages, referrers, referring URLs, campaigns, browsers, OS, language, latency and 404s. Render/ranges/export worked. | OS means browser-derived website audience, not installed-product OS. CSV is the general metrics export, not a pages-only extract. Top lists are capped. | `Pages/AdminPages.razor:57`; `Admin/AdminEndpoints.cs:98`; `Analytics/AnalyticsStore.cs:73` |
| `/admin/releases` | Manifest inventory, CDN flag, local availability/size, public visibility, missing and orphan-file diagnostics. Rendered successfully. | Read-only. CDN “Yes” means a URL exists, not that its asset is healthy/current. No GitHub refresh, checksum verification or asset publishing action. Filesystem errors produce an empty orphan list. | `Pages/AdminReleases.razor:49`; `Pages/AdminReleases.razor:153`; `Releases/ReleaseAvailability.cs:77` |
| `/admin/errors` | Client event totals, severity filter, product version, process/host, abbreviated installation ID, messages and stack details. Valid test ingestion returned 204 and appeared in the page. | Recent list capped at 200; no paging, search, export, crash grouping, per-version rate, resolution workflow, or healthy-install denominator. OS payload is discarded. | `Pages/AdminErrors.razor:33`; `Pages/AdminErrors.razor:130`; `Telemetry/ClientErrorModels.cs:41` |
| `/admin/feedback` | Public submission→database→admin inbox→local email delivery verified. Open/handled/all; mark handled, reopen, delete, reply link, SMTP status/test. | Only newest 200 messages are reachable in each view. No search/paging/export. Reply opens an email client; sending/receipt was not tested. Production app-pool variables contain no feedback SMTP configuration; successful loopback SMTP does not prove production delivery. | `Pages/AdminFeedback.razor:64`; `Pages/AdminFeedback.razor:99`; `Feedback/FeedbackStore.cs:98`; `Feedback/FeedbackEndpoints.cs:13` |
| `/admin/settings` | Latest-only/latest-N/all visibility and identifiable-retention days; current public preview and last change. Valid saves and bounds rejection worked locally. | Malformed numeric input silently keeps old values and reports success. Retention enforcement runs only at app startup, not on save or a schedule. | `Pages/AdminSettings.razor:73`; `Admin/AdminEndpoints.cs:182`; `Analytics/MaintenanceHostedService.cs:47` |

## Action / endpoint inventory

“Verified” below means exercised on the isolated published application, not mutation of production records.

| Endpoint/action | Result and limitation | Location |
|---|---|---|
| `POST /admin/login` | Valid/invalid password paths verified. No-token rejection produces 500; authentication is still refused. | `Admin/AdminEndpoints.cs:23,126` |
| `POST /admin/logout` | Cookie sign-out and subsequent admin redirect verified. CSRF omission reviewed in Task 2. | `Admin/AdminEndpoints.cs:109,226` |
| `POST /admin/settings` | Valid update and out-of-range rejection verified; malformed strings return `saved=1`. | `Admin/AdminEndpoints.cs:29,182` |
| `POST /admin/people/{visitorId}/delete` | Synthetic visitor removed, then detail rendered not-found; underlying deletion spans both event tables. | `Admin/AdminEndpoints.cs:33`; `Analytics/AnalyticsStore.cs:941` |
| `GET /admin/metrics.csv` | 200, nonempty CSV, no-store. | `Admin/AdminEndpoints.cs:115` |
| `GET /admin/downloads.csv` | Country and version sections, 200/no-store. | `Admin/AdminEndpoints.cs:56` |
| `GET /admin/people.csv` | Filters honored, personal-data warning/no-store. Only first 500 people exported; page number ignored. | `Admin/AdminEndpoints.cs:73` |
| `GET /admin/pages.csv` | 200/no-store; reuses general metrics export. | `Admin/AdminEndpoints.cs:98` |
| `POST /admin/feedback/{id}/handled` | Changes synthetic item to handled and returns to requested allowed tab. | `Feedback/FeedbackEndpoints.cs:13` |
| `POST /admin/feedback/{id}/reopen` | Reopens synthetic item. | `Feedback/FeedbackEndpoints.cs:16` |
| `POST /admin/feedback/{id}/delete` | Deletes synthetic message; email is removed with it. | `Feedback/FeedbackEndpoints.cs:19` |
| `POST /admin/feedback/test-email` | Successful delivery to loopback SMTP sink verified; production mailbox not contacted. | `Feedback/FeedbackEndpoints.cs:22` |
| `GET/POST /feedback` | Browser form submission reached the inbox. Category/message/email/page validation, honeypot and per-IP limiter also have unit coverage. | `Components/Pages/Feedback.razor:54,174` |
| `POST /consent` | Grant issued identity cookie and returned to requested local page; refusal handled by same endpoint. | `Consent/ConsentEndpoints.cs:19,42` |
| `POST /privacy/forget` | Synthetic identity row deleted; identity cookie removed and consent set to denied. | `Consent/ConsentEndpoints.cs:24,76` |
| `GET /dl/{file}` | Local fixture streamed with correct bytes; missing file rejected. CDN branch redirects to manifest URL. Counts a request, not completed transfer. | `Analytics/DownloadEndpoint.cs:29,145` |
| `POST /dl-count/{file}` | Existing fixture accepted, 204. No event ID, deduplication, authentication or rate limit. | `Analytics/DownloadEndpoint.cs:110,123` |
| `POST /api/client-errors` | Valid keyed event accepted and shown in admin. Keyless request was rejected, but returned 400 after status-page handling rather than handler's intended 404. | `Telemetry/ClientErrorEndpoint.cs:42,54`; `Program.cs:230` |
| Public support endpoints | `/health`, `/search-index.json`, `/sitemap.xml`, `/robots.txt`, static assets, public Razor routes. None exposes a general admin JSON CRUD API. Production HTTP behavior is covered in Task 2. | `Program.cs:271,279,308,317,332` |

## Reproduced functional defects and limits

| ID | Priority | Evidence | Correction for a later approved change | File + line |
|---|---|---|---|---|
| F01 | Must-have | Seeded older Zimbabwe/tablet/iOS/Safari and newer Egypt/mobile/Android/Firefox events for one visitor. Detail showed the older facts. `MAX()` can also assemble dimensions from different events. | Select latest non-null fields with deterministic event time/id ordering; define filter semantics for people who move. | `Analytics/AnalyticsStore.Individuals.cs:353` |
| F02 | Must-have | A French visitor with no download appeared with `country=FR`, but the selector offered only All/Egypt. | Build country choices from the same population as the people list, including visitors without downloads. | `Pages/AdminPeople.razor:209` |
| F03 | Must-have | Filter returned “1 individual” but top Individuals remained “520.” | Apply filters to relevant headline figures or label them explicitly as unfiltered site-wide coverage. | `Pages/AdminPeople.razor:28,206` |
| F04 | Must-have | 520 seeded people; exported CSV contained 500 data rows, with no truncation notice. It also does not mean “current 50-row page.” | Define full-filtered-export versus current-page export, implement it consistently, stream/paginate safely. | `Admin/AdminEndpoints.cs:84`; `Analytics/IndividualsExport.cs:69` |
| F05 | Must-have | Inbox said Open (205), displayed 200 messages and offered no next page. Older entries become reachable only by removing newer ones. | Add stable paging and search, retain truthful counts. | `Feedback/FeedbackStore.cs:98`; `Pages/AdminFeedback.razor:214` |
| F06 | Must-have | Submitted `not-a-number` for both numeric settings; endpoint returned `/admin/settings?saved=1`. | Reject parse failures with field-specific errors instead of reporting a successful change. | `Admin/AdminEndpoints.cs:193,223` |
| F07 | Must-have | Retention work is scheduled once from `StartAsync`; saving a shorter retention does not start cleanup. Long uptime can retain data beyond policy. | Periodic, observable cleanup, with prompt cleanup after policy shortening; expose last success and backlog. | `Analytics/MaintenanceHostedService.cs:47,68,94` |
| F08 | Must-have | Invalid/missing CSRF token generated 500 in the copied published application. Logs show invalid-antiforgery form access during `/not-found`/error re-execution. | Preserve a clean 400/403 response without re-entering form/token processing. The guard must remain enforced. | `Program.cs:230,254`; `Admin/AdminEndpoints.cs:23` |
| F09 | Must-have | Queue capacity 1024; full queue silently drops events. Analytics reports and writes share one connection and `_gate`. | Add dropped-event counters/durable delivery as appropriate; use independent read connections and measure contention. No load test was run against production. | `Analytics/ChannelAnalyticsSink.cs:14,16,32`; `Analytics/AnalyticsStore.cs:75,225`; `Analytics/AnalyticsStore.Individuals.cs:209` |
| F10 | Nice-to-have | Errors stop at 200 recent events; Insights/Errors/Feedback have no corresponding CSV endpoints. | Add bounded paging, useful filters and exports; document top-N limits and missing data. | `Pages/AdminErrors.razor:130`; `Admin/AdminEndpoints.cs:56` |

## Production data coverage snapshot

Read-only snapshot; counts can change after the review. These totals include recorded automated traffic and are **not** the portal's human-only headline figures.

| Stored data | Observation | Meaning |
|---|---:|---|
| Visits | 4,915 | Raw stored page events. |
| Visits with consent-based visitor ID | 134 | Longitudinal People views cover only a small subset; do not extrapolate blindly. |
| Visits with country / campaign source / external referrer | 251 / 62 / 12 | Strong missingness; historic unknowns are not evidence of users having no country/source. |
| Downloads | 58 | Recorded requests/clicks, not installations. |
| Downloads with visitor ID / country | 5 / 6 | Individual/location download analytics are sparse. |
| Downloads with campaign source / external referrer | 0 / 0 | Source fields exist, but this pipeline has not populated them for these events. Insights instead estimates source using same-day matching to visits. |
| Client errors | 21 events, 4 distinct reported install IDs, 6 reported product versions, 4 host strings | An error-producing sample, not 4 active installations. |
| Feedback / 404 records | 1 / 5,419 | Feedback works as a store; large 404 volume warrants classification of scans versus broken product links. |
| Ungranted visit/download rows with full IP or visitor ID | 0 / 0 | The explicit column consent gate held in this snapshot. This does not establish that URLs, message text or infrastructure logs are PII-free; see Task 2. |
| Current settings | LatestOnly; stored latest-N count 3; identifiable retention 365 days | Latest-N count is inactive while LatestOnly is selected. |

## Data gaps and next-version collection design

Website analytics consent and product diagnostics/usage consent are different choices. Use separate, explicit scopes. A random persistent installation ID is pseudonymous, not a guarantee of anonymity. Do not collect SQL text, query results, connection strings, server/database names, usernames, machine names, raw file paths or email addresses for product metrics. Use allowlisted event fields, short retention and an opt-out/delete path. Never reconstruct identity from IP after refusal.

| Data point | Current state / why it matters | Where and how to collect it | Priority |
|---|---|---|---|
| Downloads by version/date/source | Version/date exist. Source fields exist but were empty for all 58 downloads. Needed to understand release adoption and acquisition. | Extend the click event with allowlisted release/asset ID, page placement and sanitized campaign codes. For consenting users, carry permitted first-touch attribution. For others retain aggregate campaign counts without persistent identity. `DownloadEndpoint.cs:240`; `HttpRequestFacts.cs:120`; `AnalyticsStore.Insights.cs:60`. | Must-have |
| Click intent versus transfer success/failure | Current counts happen before redirect/stream and beacons have no completion acknowledgement. Needed to distinguish UI failure from real acquisition. | Label site events “download requested.” Give each user gesture an event ID and enforce unique ingestion. Separately ingest GitHub asset aggregate counts on a schedule. A cross-origin GitHub download cannot prove browser completion to this site; do not invent that signal. | Must-have |
| Installer started/completed/failed/cancelled | No dedicated server schema or installer lifecycle endpoint. Error logs do not supply an installation funnel. | After explicit product telemetry consent, installer posts structured stage/result/exit-code/duration, release/asset hash, upgrade versus fresh install. Queue locally and retry with event-ID deduplication. Instrument Inno Setup lifecycle/error handlers; never upload the full installer log automatically. `src/AkmlSql.Installer/AkmlSqlSetup.iss:789` currently initializes the existing error-report preference. | Must-have |
| First successful SSMS activation | No first-use event. An installer returning success does not prove package load/engine handshake succeeded. | One consented `activation_completed` event after SSMS package initialization and successful engine handshake, with product version and coarse host/runtime versions. Add to package/engine lifecycle after success, not at process launch. | Must-have |
| Active installs / returning product users | Absent. Four installation IDs in error reports count only installations that emitted retained errors. | Optional low-frequency, consented heartbeat/first use per day, random resettable install ID, product version and coarse platform fields; compute DAU/WAU/MAU and report consent coverage. No device fingerprint or background “active” event when software was not used. | Must-have |
| SSMS / legacy VS version distribution | Error envelope sends process name, not host product version. VS is no longer a supported target. | Send allowlisted host family and actual product major/minor version during consented activation/heartbeat. Keep legacy VS as an explicitly unsupported bucket rather than advertising support. `src/AkmlSql.Core/Logging/TelemetryClient.cs:193,226`. | Must-have |
| Installed OS / architecture / .NET runtime | Browser OS exists; desktop error DTO accepts OS but drops it; no actual runtime version. Needed to reproduce compatibility failures. | Coarse OS version/build family, process architecture and runtime major/minor from the product, under diagnostic/usage consent. Store/aggregate these fields; do not confuse bundled runtime with system-installed SDK. `Telemetry/ClientErrorModels.cs:41`; `src/AkmlSql.Core/Logging/TelemetryClient.cs:194`. | Must-have |
| Error/crash quality and rates | Event receipt and severity/stack display exist. No fingerprint grouping, crash sessions, resolution state, version filters or denominator. Abrupt process termination may prevent queue flush. | Structured exception type + normalized stack fingerprint, safe error code, host/version and event ID; durable local retry; fatal crash marker uploaded only after consent on next run. Rates use consented active sessions/installs, not raw errors alone. `Telemetry/ClientErrorEndpoint.cs:99`; `Pages/AdminErrors.razor:130`. | Must-have |
| Visit→download→install→activate conversion | Only same-day website hash-based visit→download conversion exists. IP sharing and cross-day limits make this an estimate. | Publish separate stages first. Cross-context attribution requires explicit, disclosed consent and a short-lived opaque acquisition token that reaches the installer through a supported mechanism; a fixed GitHub EXE URL does not itself carry the website identity into the installed app. Show unmatched stages and coverage instead of silently joining by IP. | Must-have |
| Referrers / unique versus repeat visitors | Already present, with daily-IP estimates and consented cookie-based repeat views. | Fix dimensions/filters, retain precise “visitor-day” labels, show missingness, strip query/fragment/credentials from referring URLs, and prefer allowlisted campaign codes. No additional raw-IP collection is needed. `AnalyticsModels.cs:275`; `HttpRequestFacts.cs:39`. | Must-have |
| Metric quality / delivery / retention | No visible drop count, processing lag, deduplication count, last cleanup status or reconciliation report. | Server counters for accepted/persisted/dropped/rejected/duplicate events, source coverage and maintenance age. No personal data required. `ChannelAnalyticsSink.cs:14`; `MaintenanceHostedService.cs:47`. | Must-have |
| Feature usage | No general product feature-use events; editor UI interfaces named “telemetry” are not proof of collection. Needed to prioritize enhancements. | Separate optional usage consent; coarse command ID, success/failure and duration bucket, sampled or aggregated locally. Never SQL, object names, AI prompts, schema or document paths. Attach at shared command/service completion points. | Nice-to-have |
| Performance by feature / host | Site server time exists; product perceived latency and reliability trends do not. | Consent-based histograms for startup, completion, formatting and engine reconnect; coarse input-size buckets, no content. | Nice-to-have |
| Upgrade/uninstall outcomes | No dedicated cohort or rollback measurement. | Optional lifecycle result event, version from/to and coarse reason enum. An uninstall event must respect existing opt-out, avoid sending after deletion of consent state, and not block uninstall. | Nice-to-have |
| Feedback workflow | Category/message/optional reply email and handled state exist. No searchable history, assignment, release link or user-visible status. | Add paging/search and optional version/category metadata; explicit optional reply address remains limited to support. Never repurpose feedback email for analytics identity. | Nice-to-have |

## Recommended order for the next version

1. **Must-have: truthful and safe collection.** Address Task 2 security/privacy findings, establish event and consent contracts, implement event IDs and monitor drops. Separate download requests, installs and activations in names and charts.
2. **Must-have: repair current reports.** F01–F09, especially silent data truncation, wrong latest dimensions, misleading filters and retention timing. Add regression checks at the HTTP/database level alongside component tests.
3. **Must-have: acquisition and reliability.** The separately requested Task 3 download fix; installer/activation outcomes; host/runtime distribution; crash grouping and quality indicators. Show missing attribution explicitly.
4. **Must-have before publishing adoption claims:** consent-based active-install denominator and transparent funnel coverage. Do not infer healthy installations from error reports.
5. **Nice-to-have:** feature usage/performance aggregates, release/cohort drilldowns, exports for remaining views, improved feedback workflow.

This is a review, not an implementation. Task 3 remains deferred until the Task 2 approval checkpoint, as instructed.
