# Contract: Release Visibility and Download-Page Availability

**Feature**: `038-site-downloads-admin-portal` | **Covers**: FR-001…FR-017, SC-003, SC-005

Supersedes nothing in `specs/034-blazor-product-site/contracts/releases-json.md` — the manifest
schema and its failure behaviour are unchanged. This contract governs **which** valid releases the
page advertises and **how availability is decided**.

---

## 1. Availability

A release is **downloadable** when either condition holds:

| Condition | Probe cost |
|-----------|-----------|
| It carries a `cdnUrl` | None — no filesystem access, no network access |
| Its local installer resolves under the downloads folder | One canonical path resolution + `File.Exists` |

**Change from current behaviour**: today `IsDownloadable` requires the *local* file whenever
`downloadUrl` starts with `downloads/`, which is true for all 16 current releases even though every
one also has a CDN mirror and `/dl/{file}` redirects to that mirror without touching the local
folder. The page's "can I offer this?" and the endpoint's "will I serve this?" had drifted apart.

**Rules**:

- **R1.1** A release with a `cdnUrl` MUST be offered regardless of local file presence.
- **R1.2** A release with neither a `cdnUrl` nor a present local file MUST NOT be offered (FR-006).
- **R1.3** Availability MUST NOT perform any network request. Reachability of the CDN is not probed.
- **R1.4** Download size MUST be read from the local file when present and omitted otherwise. A
  CDN-only release shows no size rather than a guessed one.

---

## 2. Visibility setting

| Value | Advertised on `/download` |
|-------|---------------------------|
| `LatestOnly` | The newest downloadable release only. The "Previous releases" section is **not rendered at all** — not rendered empty. |
| `LatestN` | The newest N downloadable releases (N ∈ 1…50). The newest is the primary card; the rest form the secondary list. |
| `All` | Every downloadable release. |

**Default when unset**: `LatestN` with N = 3 (FR-016 — the default is explicitly not `All`).

**Rules**:

- **R2.1** The setting MUST be applied **before** any availability probe, so probe count follows what
  is shown, not what is published (SC-003).
- **R2.2** Ordering is newest-first, from `ReleasesManifest`'s existing ordering. The setting selects
  a prefix of that order; it never reorders.
- **R2.3** A change MUST take effect on the next public request, with no restart (FR-013).
- **R2.4** The setting MUST NOT be consulted by `/dl/{file}` or `/dl-count/{file}`. A previously
  published direct link keeps working and keeps counting (FR-015).
- **R2.5** When N exceeds the number of downloadable releases, all of them are shown. Not an error.
- **R2.6** When **no** release is downloadable, the existing friendly fallback renders unchanged —
  never an error page (FR-007).
- **R2.7** The render path MUST NOT read the settings store. The value is resolved into memory at
  startup and refreshed on save, so a settings failure can neither break nor slow the page (FR-016a).
- **R2.8** If the setting cannot be read at all, the page serves the documented default
  (`LatestN` = 3). The failure is logged, reported by `/health`, and shown in the portal — loud, but
  never fatal to the public page. Failing startup was considered and rejected: turning a narrow read
  failure into total site downtime is a worse outcome than showing three releases.

---

## 3. Page structure

- **R3.1** Exactly one release is the primary card, carrying version, released date, supported hosts,
  minimum OS, size (when known), SHA-256, and the primary download button (FR-001).
- **R3.2** History, when shown, is visually secondary — collapsed or clearly de-emphasised (FR-010).
- **R3.3** The primary download action MUST work with JavaScript disabled (FR-009). The existing
  progressive enhancement is preserved: no-JS follows `/dl/{file}` and is counted server-side;
  `download-track.js` rewrites the href to the CDN and beacons `/dl-count/{file}`.
- **R3.4** At phone width the current version and its button MUST be reachable without scrolling past
  any other version (SC-004).

---

## 4. Render cost

- **R4.1** The release list MUST be resolved **once** per render. The current expression-bodied
  `PreviousReleases` property is evaluated twice (`.Count > 0`, then the `foreach`), doubling every
  probe.
- **R4.2** Per render, filesystem probes MUST NOT exceed the number of **local-only** releases
  actually rendered, plus one size stat for the primary card when it is local.
- **R4.3** With `LatestOnly`, a 100-entry manifest MUST cost at most one probe.

**Test hook**: availability probing is substitutable so a test can count invocations — this is how
SC-003 is gated deterministically rather than by wall-clock timing.

---

## 5. Admin-side preview

- **R5.1** The settings page MUST show which releases the public currently sees, before and after a
  change (FR-017).
- **R5.2** The Releases section MUST list every manifest entry with: advertised?, CDN mirror?, local
  file present?, size, and whether it is currently offered — including **present-but-unadvertised**
  installer files found in the folder (FR-034).
