# Phase 1 Data Model: Site Download Experience and Full Admin Portal

**Feature**: `038-site-downloads-admin-portal` | **Date**: 2026-09-12
**Storage**: SQLite (WAL) at `%ProgramData%\AKML SQL Site\analytics.db`, plain ADO.NET via
`Microsoft.Data.Sqlite`. No EF Core, no migration framework — schema changes go through
`AnalyticsStore.InitializeSchema`'s existing additive `AddColumnIfMissing` loop, which is how every
enrichment column so far reached installed databases without resetting history.

---

## 1. Schema changes to existing tables

### 1.1 `visits` — added columns

| Column | Type | Null | Written when | Notes |
|--------|------|------|--------------|-------|
| `ip` | TEXT | YES | Consent granted only | Full client address (FR-038). Null for every non-consenting row, and for all pre-038 history. |
| `visitor_id` | TEXT | YES | Consent granted only | Persistent individual id from the `akml.vid` cookie (FR-022). |
| `consent` | TEXT | YES | Always | `granted` \| `denied` \| `unknown`. Present so FR-047's unattributed share is a query, not an inference. |

**Retained unchanged and deliberately**: `ip_hash` (per-day salted; still drives
`ResolveSessionId` and per-day unique counting for non-consenting visitors), `ip_prefix` (truncated
/24 or /48; the honest network grouping key and the only network value available for
non-consenting rows), and every existing enrichment column.

### 1.2 `downloads` — added columns

| Column | Type | Null | Written when | Notes |
|--------|------|------|--------------|-------|
| `ip` | TEXT | YES | Consent granted only | As above. |
| `visitor_id` | TEXT | YES | Consent granted only | As above. |
| `consent` | TEXT | YES | Always | As above. |
| `release_version` | TEXT | YES | Always, when resolvable | Resolved **at write time** from `ReleasesManifest` by filename. Resolving at read time was rejected: a release later removed from the manifest would retroactively orphan its historical downloads. |

### 1.3 Untouched

`not_found` and `client_errors` gain nothing. Neither has ever held an IP-derived column, and
nothing in this feature changes that — a missing page is a content problem, and client error reports
carry a client-generated install GUID.

---

## 2. New table: `site_settings`

One row per setting, so a new setting is an insert rather than a migration.

| Column | Type | Null | Notes |
|--------|------|------|-------|
| `key` | TEXT | NO | PRIMARY KEY. `release_visibility`, `release_visibility_count`, `identifiable_retention_days`. |
| `value` | TEXT | NO | Serialized scalar. Validated on write against the setting's allowed range (FR-042). |
| `updated_utc` | TEXT | NO | ISO-8601 round-trip, matching `FormatUtc` elsewhere in the store. |
| `updated_by` | TEXT | YES | Admin session identifier — satisfies FR-035's "from which session". |

**Concurrency**: last save wins, inside a transaction. `updated_utc` and `updated_by` make the
outcome of a concurrent edit visible rather than silent, which is what the edge case asks for.

**Defaults when a key is absent, and when the store cannot be read at all** (FR-016 — the default
must not be "show everything"; FR-016a — an unreadable store falls back to these same values rather
than failing the public page, with the failure logged, reported by `/health`, and shown in the
portal):

| Key | Default | Bounds |
|-----|---------|--------|
| `release_visibility` | `LatestN` | one of `LatestOnly`, `LatestN`, `All` |
| `release_visibility_count` | `3` | 1–50 |
| `identifiable_retention_days` | `365` | 1–3650 |

---

## 3. New indexes

```sql
CREATE INDEX IF NOT EXISTS ix_visits_visitor        ON visits    (visitor_id, utc);
CREATE INDEX IF NOT EXISTS ix_downloads_visitor     ON downloads (visitor_id, utc);
CREATE INDEX IF NOT EXISTS ix_downloads_day_country ON downloads (day, country);
CREATE INDEX IF NOT EXISTS ix_visits_day_country    ON visits    (day, country);
```

Existing day indexes are kept. At low-thousands of rows per day these are precautionary; adding them
alongside the columns they serve is cheaper than diagnosing a slow portal later.

---

## 4. Entities (domain view)

### Release *(existing, unchanged shape — availability semantics change)*

Version, released-at, supported hosts, minimum OS, SHA-256, notes, `downloadUrl`, `cdnUrl`.

**Changed rule**: a release is *downloadable* when it has a CDN mirror **or** its local installer is
present — not, as today, only when the local file exists. See `contracts/release-visibility.md` and
research R2; this is the correctness fix, and it also removes most per-render filesystem work.

### ReleaseVisibility *(new)*

| Value | Public page shows |
|-------|-------------------|
| `LatestOnly` | Exactly the current release; no history section rendered at all |
| `LatestN` | The newest N downloadable releases, N from `release_visibility_count` |
| `All` | Every downloadable release |

**Invariant**: visibility governs only what is *advertised*. A previously published direct link keeps
working and keeps counting (FR-015) — `/dl/{file}` never consults this setting.

### ConsentState *(new)*

`Unknown` → `Granted` | `Denied`. `Granted` → `Denied` on withdrawal. `Denied` → `Granted` only by a
deliberate new choice. Resolved per request into `HttpContext.Items` by `ConsentMiddleware`, before
`VisitTrackingMiddleware` reads it.

**Invariant (the one that matters)**: `ip` and `visitor_id` are written **if and only if** state is
`Granted`. Enforced at the store's insert, not only at the call site — a UI bug must not be able to
cause collection.

### Individual *(new, derived — not a table)*

Aggregated over `visitor_id` across `visits` and `downloads`:

| Field | Source |
|-------|--------|
| Visitor id | `visitor_id` |
| First seen / last seen | `MIN(utc)` / `MAX(utc)` across both tables |
| Country | Most recent non-null `country` |
| Address | Most recent non-null `ip` |
| Network | Most recent non-null `ip_prefix` |
| Device / OS / browser | Most recent non-null values |
| Visit count | `COUNT(*)` over visits |
| Download count | `COUNT(*)` over downloads |
| Downloaded? | `download count > 0` |

**Not a stored table** on purpose: an `individuals` table would duplicate facts the event rows
already hold and would need its own retention and deletion path. Deriving it means FR-040's deletion
is a delete on two event tables, with nothing left behind.

**Honesty rules carried from the spec** — one browser, not one human; a cleared cookie is a new
individual and must not read as growth; FR-022b forbids re-linking by address or user-agent to
"repair" that.

### ConsentRecord *(new, cookie-resident)*

The choice, stored in `akml.consent` on the visitor's browser — not in the database. Storing a
server-side consent record keyed by address would mean retaining the address of people who refused
to have their address retained. The `consent` column on each event row is the per-event fact, not
the visitor's standing record.

### SiteSetting *(new)*

Key, value, allowed range, `updated_utc`, `updated_by`. See §2.

---

## 5. Validation rules (from requirements)

| Rule | Source | Enforced at |
|------|--------|-------------|
| `release_visibility_count` within 1–50; out-of-range rejected with a message | FR-012 | `SiteSettingsStore.Save` + settings form |
| `identifiable_retention_days` within 1–3650 | FR-037 | `SiteSettingsStore.Save` |
| `ip` / `visitor_id` written only when consent is `Granted` | FR-043/FR-044 | `AnalyticsStore` insert |
| A release with neither a present local file nor a CDN mirror is never offered | FR-006 | `ReleaseAvailability.IsDownloadable` |
| Visibility never affects `/dl/{file}` | FR-015 | `DownloadEndpoint` (no settings dependency) |
| Range requests and `/dl-count` beacons never double-count | FR-005 | `DownloadEndpoint.LogDownload` (existing `Range` guard) |
| Bots excluded from individuals; scripted installer fetches still counted | FR-027 | Existing `HumanOnly` / `RealDownloadOnly` predicates |
| Every grouping carries an explicit unknown bucket | FR-028 | Read-model projection |
| Redirect target of `POST /consent` must be a local path | R5 / security | `ConsentEndpoints` |

---

## 6. Retention and deletion

Two boundaries, because the spec wants identifiable detail gone while aggregate history still
reconciles (SC-008):

| Operation | Boundary | Effect |
|-----------|----------|--------|
| **De-identify** | `identifiable_retention_days` (default 365) | `UPDATE visits/downloads SET ip = NULL, visitor_id = NULL` for rows older than the boundary. Row survives; country, version and totals still reconcile. |
| **Delete** | `Analytics:RetentionDays` (existing, default 400) | Existing `Prune` behaviour — whole rows removed. |
| **Forget one individual** | On request (FR-040/FR-045) | `DELETE` every row for a `visitor_id`, and optionally for an address. Nothing is left to re-link. |

Both scheduled operations move **off the startup path** into the post-start maintenance hosted
service (research R3) — they are maintenance, not deploy validation, and they should never be
something a visitor waits behind.

---

## 7. Backward compatibility

- Pre-038 rows have `ip = NULL`, `visitor_id = NULL`, `consent = NULL`. They aggregate correctly as
  unattributed and never resolve to an individual — stated in the spec's assumptions, and treated as
  data rather than a gap to backfill.
- `ip_hash` semantics are unchanged, so existing per-day unique counts and session grouping stay
  comparable across the upgrade.
- `AddColumnIfMissing` is idempotent, so deploy and rollback are both safe. A rollback leaves the new
  columns in place, unread — which is why they are nullable and why nothing reads them without a
  null check.
