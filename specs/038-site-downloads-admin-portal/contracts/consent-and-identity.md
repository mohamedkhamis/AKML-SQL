# Contract: Consent, Identity and What Gets Written

**Feature**: `038-site-downloads-admin-portal` | **Covers**: FR-022, FR-022a/b, FR-037…FR-050, SC-013…SC-019

This is the contract that makes the 2026-09-12 privacy decisions operable. It is deliberately written
as "what is stored, under which state" rather than as UI description, because the invariant that
matters is a storage invariant.

---

## 1. Cookies

| Name | Value | Set when | Lifetime | Flags | Path |
|------|-------|----------|----------|-------|------|
| `akml.consent` | `granted` \| `denied` | The visitor chooses | 365 days | `HttpOnly`, `Secure`, `SameSite=Lax` | `/` |
| `akml.vid` | GUID (`N` format) | **Only** when consent is `granted` | 365 days | `HttpOnly`, `Secure`, `SameSite=Lax` | `/` |

- **C1.1** The two cookies MUST be independent. A refusal is remembered **without** issuing
  `akml.vid` (FR-046) — storing the refusal inside the identity cookie would mean issuing the very
  thing the visitor refused.
- **C1.2** Both are `HttpOnly`: no client script reads them, and the strict CSP means none could be
  added inline anyway.
- **C1.3** `akml.vid`'s lifetime MUST NOT exceed the identifiable retention period. A cookie that
  outlives the data it points at is a dangling identifier.
- **C1.4** On withdrawal, `akml.vid` MUST be expired and `akml.consent` set to `denied`.

---

## 2. Consent states

```
Unknown ──grant──► Granted ──withdraw──► Denied
   │                                        ▲
   └──decline───────────────────────────────┘
                    (Denied ──grant──► Granted, by deliberate new choice only)
```

- **C2.1** `Unknown` is the state of a first-time visitor. It is **not** implied consent (FR-043).
- **C2.2** The banner renders only in `Unknown`. In `Granted` or `Denied` the visitor is never
  re-asked (FR-046).
- **C2.3** State is resolved once per request by `ConsentMiddleware` into `HttpContext.Items`, before
  `VisitTrackingMiddleware` runs.
- **C2.4** The banner is shown to **every** `Unknown` visitor regardless of country (FR-043a). No
  geo-gating, and the consent path MUST NOT read `GeoLookup` — a missing geo database must not be
  able to affect whether consent is requested.
- **C2.5** The banner is **non-blocking** (FR-043b): it obscures nothing, delays nothing, disables
  nothing, and the entire download path completes without interacting with it.
- **C2.6** Accept and Decline carry equal prominence. An explicit dismissal records `Denied`.
  Silence, scrolling and navigating away leave the state `Unknown` — never `Granted`.
- **C2.7** Consequence, stated so it is not later mistaken for a bug: a visitor who ignores the
  banner is asked again on every visit and is never tracked. Individual-level coverage will be a
  minority of traffic. That is the accepted outcome of C2.4 + C2.5, not a defect to fix by making
  the prompt more insistent.

---

## 3. What is written, per state — the binding table

| Column | `Unknown` | `Denied` | `Granted` |
|--------|-----------|----------|-----------|
| `utc`, `day`, `path` / `file` | ✅ | ✅ | ✅ |
| `country`, `country_code` | ✅ | ✅ | ✅ |
| `ip_prefix` (truncated) | ✅ | ✅ | ✅ |
| `ip_hash` (per-day salted) | ✅ | ✅ | ✅ |
| device / OS / browser / language / referrer / campaign | ✅ | ✅ | ✅ |
| `consent` | `unknown` | `denied` | `granted` |
| **`ip` (full address)** | ❌ | ❌ | ✅ |
| **`visitor_id`** | ❌ | ❌ | ✅ |

- **C3.1** `ip` and `visitor_id` are written **if and only if** state is `Granted`. Enforced at the
  store's insert, not only at the call site — a UI bug must not be able to cause collection.
- **C3.2** Non-consenting visitors still contribute to aggregate visit and download totals (FR-044).
  They are counted; they are not identified.
- **C3.3** `ip_hash` continues for all states. It is re-salted daily, supports per-day unique counting
  and session grouping, and is the existing accepted behaviour. Removing it would degrade aggregate
  accuracy for precisely the people who refused.
- **C3.4** Downloads are recorded regardless of consent state. Refusing tracking MUST NOT degrade or
  block the download path in any way (FR-044).

---

## 4. Endpoints

### `POST /consent`

| Field | Value |
|-------|-------|
| Body | `choice=granted\|denied`, antiforgery token, `returnUrl` |
| Success | Sets `akml.consent`; issues `akml.vid` only on `granted`; 302 to `returnUrl` |
| `returnUrl` | **MUST** be validated as a local path. A non-local value falls back to `/`. |

- **C4.1** Open-redirect protection is mandatory, not optional — this endpoint takes a redirect target
  from a form on every page.
- **C4.2** Antiforgery applies, consistent with `/admin/login`.

### `POST /privacy/forget`

| Field | Value |
|-------|-------|
| Effect | Withdraws consent, expires `akml.vid`, and deletes every row for that `visitor_id` |
| Auth | None — it is the visitor's own identifier, presented by their own cookie |
| Response | Confirmation page stating what was removed |

- **C4.3** Deletion MUST remove rows from both `visits` and `downloads`, leaving nothing that could
  re-link the visitor (FR-040).
- **C4.4** With no `akml.vid` present, the endpoint MUST succeed idempotently and say there was
  nothing stored — never an error, and never a probe that reveals whether an id exists.

---

## 5. Identity rules

- **C5.1** One `akml.vid` is one **browser**, not one human. Every "people" figure carries that
  caveat in the UI.
- **C5.2** A visitor without `akml.vid` MUST NOT be re-linked to an existing individual by address,
  user-agent, or any other signal (FR-022b). Re-linking would reconstruct exactly what a visitor
  clearing their cookies asked to break.
- **C5.3** New-versus-returning is derived from whether a `visitor_id` was first seen inside the
  selected range (FR-022a).

---

## 6. Retention

| Operation | Boundary | Effect |
|-----------|----------|--------|
| De-identify | `identifiable_retention_days` (default 365) | `ip` and `visitor_id` nulled in place; row and aggregates survive |
| Delete | `Analytics:RetentionDays` (default 400) | Whole rows removed (existing `Prune`) |

- **C6.1** Both run in the post-start maintenance service, never inline at startup (research R3).
- **C6.2** De-identification MUST NOT change any aggregate figure for the same range (SC-008).

---

## 7. Disclosure boundaries

- **C7.1** `ip` and `visitor_id` MUST NOT appear in any unauthenticated response, application log,
  error report, sitemap, or client-visible payload (FR-048, SC-015).
- **C7.2** Exports containing either MUST be produced only for an authenticated administrator and
  MUST be labelled as containing personal data (FR-049).
- **C7.3** The published privacy notice MUST state the storage mode and the **configured** retention
  number, read from the same options the store uses — so a drift between notice and behaviour fails
  a test rather than shipping (FR-039, SC-013).

---

## 8. Statements that become false on ship

These currently assert the opposite of what this contract specifies and MUST be corrected in the same
change (constitution: documentation currency):

| Location | Current claim |
|----------|---------------|
| `Analytics/AnalyticsStore.cs` class summary | "the raw client IP is never persisted" |
| `Analytics/IpAnonymizer.cs` summary | "The full address … is never persisted" |
| `Analytics/GeoLookup.cs` summary | rationale built on not holding data you do not need |
| `Components/Pages/Admin/AdminDashboard.razor` | visible text: "full IP addresses are never stored … No cookies are set for visitors." |
| `appsettings.json` → `Analytics._note` | describes the anonymised model |
| `Analytics/AnalyticsModels.cs` XML docs | "NEVER persisted" on `VisitInfo.IpAddress` / `DownloadInfo.IpAddress` |
