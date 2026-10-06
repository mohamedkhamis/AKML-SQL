# Admin review follow-up — 2026-10-06

The owner approved F01–F10 and collection health from
`reports/site-audit-2026-10-06/01-admin-functional-review.md`.
New installer/SSMS telemetry, broader security remediation, production deployment
and Task 3's download behavior remain outside this increment.

## Decisions and acceptance

- F01: latest non-null dimensions within the selected window, deterministic ties
  by timestamp, event kind and row ID. Country is the latest known country; filtering
  selects people by that country and retains all their events in the window.
- F02/F03: country choices cover consenting visitors and downloaders. Filtered
  person/returning totals agree with the list; unfiltered consent coverage is labelled.
- F04: People CSV exports **all matching people**, streamed from one read snapshot,
  with the window/filters and a personal-data notice. Never silently truncate.
- F05/F10: feedback and errors have bounded server paging, search and exports;
  error filters include version; Insights exports its displayed aggregate groups
  and explicitly states top-N limits. Navigation/actions preserve filters.
- F06: malformed numeric settings are rejected; nothing is saved on validation failure.
- F07: maintenance runs after startup, hourly, and promptly after retention changes;
  admin sees last attempt/success/failure and affected counts. Cancellation stops it.
- F08: invalid form tokens return 400; non-GET errors never re-enter Razor forms;
  ordinary GET not-found pages retain their existing presentation.
- F09: bounded intake remains nonblocking, with observable accepted/persisted/dropped/
  failed/pending counts and processing lag. Reporting uses separate read-only SQLite
  snapshot connections so reports do not hold the ingestion writer lock.

## Constitution check

Pass: changes remain in the Site and its matching tests; existing stores, auth,
static SSR, theme styles and consent behavior are reused. No desktop collection,
new identities, schema-content capture or Git mutations. Regression tests use
temporary databases and isolated HTTP hosting. Full solution Release build and
theme drift check are required. The dated audits remain unchanged as baselines;
implementation status is recorded separately and in the progress/memory files.

## Verification checklist

- [x] Moving visitor/latest dimension, ties and null fallback; country filter and counts.
- [x] More than 500 people exported without missing/duplicating rows.
- [x] More than 200 feedback/errors reachable; search and paging preserve filters.
- [x] Invalid numeric form values rejected without modifying settings.
- [x] Scheduled/triggered maintenance and status; queue overflow/failure/lag counters.
- [x] Read snapshot allows ingestion and remains consistent while writes continue.
- [x] Isolated production-mode HTTP: auth/CSRF, exports, settings and navigation.
- [x] Site tests, solution Release build, theme drift gate; browser smoke.

Evidence and limits: [implementation report](../../reports/site-audit-2026-10-06/03-admin-implementation.md).
890 Site tests and 12 local Chromium check groups passed. Maintenance's hourly
timer is implemented; no hour-long soak test or production load test was run.
