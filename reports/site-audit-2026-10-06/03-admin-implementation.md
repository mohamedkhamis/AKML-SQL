# Admin fixes and collection health — implementation

Date: 2026-10-06 (Africa/Cairo). Implements the owner's approved first increment:
F01–F10 plus collection health. The two audit reports remain dated baselines.
Source changes only; no production deployment or Git mutation performed by this agent.

## Changes

Paths below are relative to `src/AkmlSql.Site/`.

| Finding | Implemented behavior | Main source |
|---|---|---|
| F01 | Latest non-null dimensions by timestamp, event kind and row ID. Country selects a person's latest known country while keeping all their events in the window. First seen/returning still use whole history. | `Analytics/AnalyticsStore.Individuals.cs:418` |
| F02 | People country choices include consenting visitors with no download. | `Analytics/AnalyticsStore.Individuals.cs:367`, `Components/Pages/Admin/AdminPeople.razor` |
| F03 | Matching people/returning totals follow filters; unrelated coverage and bot figures explicitly say whole window. | `Components/Pages/Admin/AdminPeople.razor` |
| F04 | People CSV streams every matching person from a read snapshot, without the old 500-row cap. Metadata identifies filters, consent coverage limits and personal data. | `Analytics/AnalyticsStore.Individuals.cs:348`, `Admin/AdminReportExports.cs`, `Admin/AdminCsv.cs` |
| F05 | Feedback has 50-row pages, text search, matching counts and full filtered export. Handle/reopen/delete preserve search/status/page; pages clamp after removals. | `Feedback/FeedbackStore.cs`, `Feedback/FeedbackEndpoints.cs`, `Components/Pages/Admin/AdminFeedback.razor` |
| F06 | Malformed numeric settings return field-specific errors and save nothing; existing bounds remain enforced. | `Admin/AdminEndpoints.cs` |
| F07 | Maintenance runs after startup, hourly and after retention changes. Failed passes retry after one minute. Settings shows last attempt/success/failure, affected counts and current overdue identifiable-row count using cleanup's UTC-day boundary. | `Analytics/MaintenanceHostedService.cs`, `Analytics/AnalyticsStore.cs:933`, `Settings/SiteSettingsStore.cs`, `Components/CollectionHealthPanel.razor` |
| F08 | Non-GET/API status responses no longer re-enter Razor status pages. Production exception handling returns a generic response directly. Missing/invalid form tokens retain their 400 response. | `Program.cs:228` |
| F09 | Reporting uses separate read-only WAL snapshots instead of holding the ingestion connection's lock. Nonblocking queue intake now measures accepted, persisted, pending, dropped and failed items, last persistence and processing delay. | `Analytics/AnalyticsStore.cs:96`, `Analytics/ChannelAnalyticsSink.cs`, `Analytics/CollectionHealth.cs` |
| F10 | Errors has 50-row paging, message/exception search, version/severity filters and full filtered CSV. Insights exports its displayed aggregates and states its top-10 and visitor-day attribution limits. Window links retain filters and reset page. | `Analytics/AnalyticsStore.ClientErrorPages.cs`, `Admin/AdminReportExports.cs`, `Components/Pages/Admin/AdminErrors.razor`, `Components/Pages/Admin/AdminInsights.razor`, `Components/Layout/AdminLayout.razor` |

New and replaced CSV streams quote cells, use invariant numeric formatting and
preserve formula-like strings as text. This does not remediate the older aggregate
CSV endpoints covered by the separate security report. Error UI/export wording now
acknowledges persistent installation IDs and potentially sensitive diagnostic text.
No new personal identifiers or desktop data collection were introduced.

The export contract now explicitly means all matching rows across pages. Feedback
is an all-time inbox filtered by status/search. Error and People exports use the
selected window. Insights is an aggregate export, not raw events. Exact search
filters stay in CSV metadata rather than exposing search text in filenames.

## Verification

- **890/890 Site tests passed**, including 13 new cases covering latest dimensions,
  null address fallback, deterministic ties, visit-only countries, all 521 exported
  people, feedback/error paging beyond 200 records, literal search parameters,
  queue overflow/write failure, retention changes/failure/backlog, CSV escaping,
  and a stable read snapshot while a writer completes within five seconds.
- **Full solution Release build passed** using Visual Studio MSBuild; existing
  warnings remain. **Theme-token drift check passed** for all three themes.
- Published the changed Site to an isolated local directory and ran it in
  **Production mode on local HTTPS**, with synthetic SQLite data, generated local
  admin credentials and loopback SMTP. No live inbox or production database used.
- **12 local Chromium check groups passed**: unauthorized admin redirects; login
  token failure; rendering admin sections; latest People dimensions/filter/counts;
  all 520 people and one filtered person exported; 205 feedback entries reachable
  and exported with action context preserved; 235 errors paged/exported with
  search/version/severity; Insights export; malformed/out-of-bounds settings;
  valid retention save and cleanup/backlog display; affected POST/API/GET error
  statuses; and 1440/390-pixel layouts for People, Feedback, Errors and Settings.
- Missing tokens returned 400 for login, settings, person deletion, feedback
  handle/reopen/delete/test-email, privacy forget and feedback submission. Keyless
  client-error ingestion retained 404; ordinary GET not-found retained 404.
- Evidence: ignored `artifacts/admin-fixes/`: `test-results/admin-follow-up.trx`,
  `solution-build.log`, `publish.log`, `browser-results.json`, `verify.cjs`, and
  desktop/mobile PNGs. The browser script uses only the isolated fixture and is
  not the repository's production-targeted E2E suite.

## Limits and remaining work

- Queue counters are **process-lifetime queue items**; one error batch is one item.
  They reset on restart. Accepted means queued, not durable delivery; restart can
  lose pending items. Rejected requests and duplicate events are not measured.
  Durable intake/deduplication and historical health metrics remain future work.
- Snapshots remove the shared application read/write lock, not all SQLite or disk
  contention. Long-running exports can retain WAL history until readers finish.
  Query duration and database/WAL size still need monitoring at production scale;
  the fixture checks are not a production load-capacity benchmark. Page counts and
  page rows are separate read operations and can change during concurrent intake.
- Retention still erases the existing `ip`/`visitor_id` fields under the existing
  UTC-day policy. This increment does not claim all diagnostic/free-text/network
  data is anonymous or solve the broader retention findings in the security report.
- Scheduled maintenance is implemented; startup/change/failure paths were exercised.
  No dedicated hour-long timer soak test or production IIS execution was performed.
- New installer/SSMS telemetry and the report's broader data-gap recommendations
  remain deferred, as requested. Broader Task 2 security remediation and Task 3's
  GitHub download fix/browser matrix remain pending separate authorization.
- Browser verification here used Chromium with desktop/mobile viewport emulation;
  it does not establish Firefox, Edge, real-mobile or production behavior.
