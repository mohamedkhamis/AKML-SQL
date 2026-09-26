# Contract: Metrics Read Model

**Feature**: `038-site-downloads-admin-portal` | **Covers**: FR-018…FR-030, SC-006…SC-009, SC-016, SC-019

Read shapes the portal consumes. All are computed over a selected window (the existing `?days=`
value), all are UTC-day bucketed, and all follow the reconciliation rules in §5.

---

## 1. Download views

### `DownloadsByCountry`

| Field | Notes |
|-------|-------|
| `Country` | Display name; `null` → the explicit **Unknown** bucket |
| `CountryCode` | ISO 3166-1 alpha-2, or null |
| `Count` | Downloads in window |
| `SharePercent` | Of window total, to one decimal |

Ranked descending by count, Unknown always shown (never dropped, never hidden when zero-filtered).

### `DownloadsByVersion`

| Field | Notes |
|-------|-------|
| `ReleaseVersion` | From `downloads.release_version`; null → **Unattributed** |
| `File` | Installer file name |
| `Count`, `SharePercent` | As above |

Answers "is the new build being taken up?" — the reason version is stored at write time rather than
resolved from the mutable manifest at read time.

### `DailyDownloads`

Zero-filled per UTC day, oldest first. Zero-filling is required: a gap that renders as "no bar"
rather than "a bar of zero" reads as missing data.

---

## 2. People views

### `IndividualRow` (list)

| Field | Source |
|-------|--------|
| `VisitorId` | `visitor_id` |
| `FirstSeen` / `LastSeen` | `MIN`/`MAX` of `utc` across visits ∪ downloads |
| `Country`, `CountryCode` | Most recent non-null |
| `IpAddress` | Most recent non-null `ip` |
| `NetworkPrefix` | Most recent non-null `ip_prefix` |
| `Device`, `Os`, `Browser` | Most recent non-null |
| `VisitCount`, `DownloadCount` | Counts in window |
| `Downloaded` | `DownloadCount > 0` |
| `IsReturning` | First seen **before** the window start |

**Filters** (FR-025): country, downloaded (yes/no/any), date range. Every headline figure on the view
honours the active filters — a filtered list above unfiltered totals is a misreading waiting to
happen.

**Sort**: last seen descending by default; downloads descending and first seen ascending also offered.

**Paging**: server-side, fixed page size. The list is unbounded in principle.

### `IndividualDetail`

Everything in `IndividualRow`, plus a **single time-ordered activity stream** merging visits and
downloads — `utc`, kind (`visit` | `download`), path or file, release version, referrer, campaign.

One stream rather than two tables: the question being asked is "what did this person do", and
interleaving is the only shape that answers it (FR-024).

---

## 3. Coverage and honesty figures

Required alongside any people view, or the list gets read as the whole audience:

| Field | Meaning |
|-------|---------|
| `AttributedVisits` | Visits with a `visitor_id` |
| `UnattributedVisits` | Visits without one (consent `unknown` or `denied`) |
| `UnattributedSharePercent` | FR-047 / SC-016 |
| `AutomatedVisits` | Bots and scripted clients, excluded from people figures, reported separately |
| `DistinctIndividuals` | Distinct `visitor_id` in window |
| `NewIndividuals` / `ReturningIndividuals` | FR-022a / SC-019 |

- **M3.1** `DistinctIndividuals` MUST NOT be presented as "visitors" or "users" without the
  unattributed share beside it.
- **M3.2** Address-based grouping and person-based grouping MUST be shown as separate figures, never
  merged. Several people behind one carrier-grade NAT address are several individuals and one
  address; both numbers are correct.

---

## 4. Pages view *(demoted, retained)*

Existing `AnalyticsSummary` page metrics — top pages, entry/exit, bounce, sessions, referrers, slow
pages, 404s — move to their own section. Retained in full (FR-030), just no longer the lead. The
existing shape is reused unchanged; nothing about page-visit reporting is rebuilt.

---

## 5. Reconciliation rules

- **M5.1** For any window, per-country counts MUST sum to the headline total, Unknown included
  (SC-008).
- **M5.2** Per-version counts MUST sum to the headline total, Unattributed included.
- **M5.3** Per-individual download counts MUST sum to attributed downloads; attributed + unattributed
  MUST equal the headline total.
- **M5.4** De-identification at the retention boundary MUST NOT change §1 figures for the same
  window — that is why `ip`/`visitor_id` are nulled rather than the rows deleted.
- **M5.5** Bot exclusion applies to visit and people figures. It MUST NOT apply to installer
  downloads: a scripted fetch of the installer is a real acquisition. Existing `HumanOnly` /
  `RealDownloadOnly` predicates already encode this split and are reused.

---

## 6. Export

- **M6.1** Every view exports to CSV, containing **the filtered rows currently displayed** — not the
  unfiltered table (FR-026).
- **M6.2** The active window and filters appear in the file name **and** a header comment row, so a
  file found later is self-describing.
- **M6.3** Exports carrying `ip` or `visitor_id` are labelled as containing personal data (FR-049)
  and require an authenticated session like every other portal surface (FR-033).
- **M6.4** `Cache-Control: no-store` on every export, as the existing `/admin/metrics.csv` already
  sets.

---

## 7. Performance

- **M7.1** Every view is served by indexed queries over the day columns plus the new visitor/country
  indexes.
- **M7.2** The portal is static SSR. No live updates, no polling, no websocket — consistent with the
  spec's Out of Scope.
- **M7.3** Read queries MUST NOT block the write path: settings and portal reads use their own
  connection, so a portal query never waits behind a metrics insert (WAL allows this).
