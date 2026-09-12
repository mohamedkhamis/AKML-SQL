# Contract: Admin Portal Surface

**Feature**: `038-site-downloads-admin-portal` | **Covers**: FR-031…FR-036, FR-042, SC-010, SC-011

---

## 1. Routes

| Route | Section | Status |
|-------|---------|--------|
| `/admin` | Overview | Existing dashboard, re-parented and re-scoped |
| `/admin/downloads` | Downloads — country, version, trend | New |
| `/admin/people` | Individuals list + filters | New |
| `/admin/people/{visitorId}` | One individual's history | New |
| `/admin/pages` | Page visits (demoted from lead) | New page, existing data |
| `/admin/errors` | Client error reports | Existing, re-parented |
| `/admin/releases` | Advertised vs present on disk | New |
| `/admin/settings` | Release visibility, retention | New |
| `/admin/login` | Sign-in | Existing, **stays outside the portal shell** |

**Exports** (all `GET`, all under `/admin` so one guard covers them):

`/admin/metrics.csv` (existing) · `/admin/downloads.csv` · `/admin/people.csv` · `/admin/pages.csv`

**Mutations** (all `POST`, all antiforgery-protected):

`/admin/login` · `/admin/logout` (existing) · `/admin/settings` · `/admin/people/{visitorId}/delete`

---

## 2. Authentication

- **A2.1** Everything under `/admin` except `/admin/login` requires the admin cookie. Enforced by the
  **existing** `AdminBranchMiddleware` — no new authorization path is introduced, and new routes are
  covered automatically by being under `/admin`.
- **A2.2** An unauthenticated request to any section or export redirects to `/admin/login` and
  discloses nothing — no counts, no row shapes, no existence signals (FR-033, SC-011).
- **A2.3** The existing cookie scheme is unchanged: 8-hour sliding, `HttpOnly`, `Secure`,
  `SameSite=Lax`, name `akml.admin`.
- **A2.4** The login throttle (`AdminLoginThrottle`) is untouched.
- **A2.5** Every portal page keeps `<meta name="robots" content="noindex, nofollow">`, as the current
  dashboard does.

**Test hook**: one test enumerates every route and export above through
`AdminBranchMiddleware.RequiresChallenge`. A new route added later without a guard fails it.

---

## 3. Navigation

- **A3.1** `AdminLayout` renders persistent navigation listing every section, on every portal page
  (FR-031).
- **A3.2** The active section is marked with `aria-current="page"`.
- **A3.3** Every section is reachable in one click from any other (SC-010).
- **A3.4** Sign-out is present on every portal page.
- **A3.5** `/admin/login` does **not** use the portal shell — a sign-in page must not display
  navigation to sections the visitor cannot reach.

---

## 4. Range propagation

- **A4.1** The reporting window remains the `?days=` query-string value, normalised by the existing
  `AdminDashboardOptions.NormalizeDays` (defaults to 30, clamps to 3650).
- **A4.2** Every navigation link and every export link MUST carry the current window, so it survives
  navigation between sections (FR-032, SC-010) with no session state and no cookie.
- **A4.3** The active window MUST be visible on every section, so a screenshot is never ambiguous
  about its own range.
- **A4.4** An unparseable or absurd `days` value falls back to the default rather than erroring — a
  bad query string must not break the owner's portal. Existing behaviour, extended to new sections.
- **A4.5** People-view filters (country, downloaded) travel the same way, so a filtered view is
  bookmarkable and shareable.

---

## 5. Settings page

- **A5.1** Shows each setting's current value, allowed values, and a plain-language description of
  what it publishes (FR-011).
- **A5.2** Shows which releases the public currently sees, before and after a change (FR-017).
- **A5.3** Rejects out-of-range values with a message naming the bound — never silent clamping, which
  would leave the owner believing they saved something they did not (FR-012).
- **A5.4** On save, confirms **what** changed and **when it takes effect** ("live on the next public
  request") (FR-035, SC-005).
- **A5.5** Records `updated_utc` and `updated_by`.
- **A5.6** Plain form POST — works without JavaScript, consistent with the rest of the site.

---

## 6. Releases section

- **A6.1** Lists every manifest entry with: version, released date, CDN mirror present?, local file
  present?, size, and whether it is currently advertised.
- **A6.2** Flags **advertised-but-missing** (in the manifest, no CDN, no local file) — the case that
  turns the site's primary call to action into a dead link.
- **A6.3** Flags **present-but-unadvertised** (a file in the downloads folder no manifest entry
  references) — usually a stale installer taking disk space.
- **A6.4** Read-only. Editing release metadata and uploading installers are explicitly out of scope.

---

## 7. Styling and layout

- **A7.1** Reuses `wwwroot/css/site.css` and the existing admin classes. Theme CSS under
  `wwwroot/css/themes/` is **generated** from `docs/theme-tokens.json` and MUST NOT be hand-edited —
  the `generate-theme-css.ps1 -CheckOnly` drift gate is part of the build (constitution II).
- **A7.2** Charts remain pure CSS via the precomputed `.bar-h-*` bucket classes. No inline styles, no
  chart library — the strict CSP (`script-src 'self'`, `style-src 'self'`) stands unchanged.
- **A7.3** Overview and Downloads MUST be usable at phone width (FR-036). Wide tables get their own
  horizontally scrollable container rather than making the page scroll sideways.
- **A7.4** No interactive render mode is registered. The portal stays static SSR.

---

## 8. Privacy surface (public, not portal)

| Route | Purpose |
|-------|---------|
| `/privacy` | The notice: what is collected, why, retention, how to withdraw or delete (FR-039) |
| `POST /consent` | Record the visitor's choice |
| `POST /privacy/forget` | Withdraw and delete (FR-045, FR-040) |

- **A8.1** `/privacy` is publicly reachable, linked from the footer and from the consent banner.
- **A8.2** Its stated retention period is read from the **same** configuration the store enforces, so
  drift between the notice and actual behaviour fails a test rather than shipping (SC-013).
