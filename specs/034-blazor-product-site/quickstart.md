# Quickstart — 034-blazor-product-site

Runnable validation scenarios proving the feature end-to-end, mapped to the spec's Success Criteria. For data shapes and rules see [data-model.md](data-model.md) and [contracts/](contracts/).

## Prerequisites

- .NET 10 SDK (repo standard — same as `src/AkmlSql.Web/`)
- Repo checkout with `doc/` populated (58+ Markdown files)
- Optional: `scripts/generate-theme-css.ps1` run (or verify the checked-in generated CSS is fresh — `build.ps1` step 1 gates drift)

## Setup & run

```bash
# Restore + build the site project (added to AKML-SQL.slnx by implementation)
dotnet build src/AkmlSql.Site/AkmlSql.Site.csproj -c Debug

# Run the site (static SSR; note the launched URL, e.g. http://localhost:5xxx)
dotnet run --project src/AkmlSql.Site/AkmlSql.Site.csproj

# Unit/component tests
dotnet test tests/AkmlSql.Site.Tests/AkmlSql.Site.Tests.csproj
```

## Validation scenarios

### VS-1 — Discover & download (US1, SC-001)

1. Open `/` → product name, value proposition, feature highlights, and a prominent download CTA are visible in the first paint (no JS required).
2. Click the CTA (1 click) → `/download` shows latest version, release date, supported hosts (SSMS 22 / VS 2026), SHA-256, and the installer link (click 2 starts the download).
3. Older releases remain listed below the latest (FR-003).
4. Rename `wwwroot/releases.json` temporarily → reload `/download`: friendly fallback message + repo releases link, no error page. Restore the file.

**Expected**: download reachable in ≤2 clicks, well under 1 minute; broken feed degrades gracefully.

### VS-2 — Automatic documentation (US2, FR-004–FR-007, SC-002, SC-005)

1. Open `/docs` → section tree lists documents from `doc/` with correct titles; excluded internal files (`bugs.md`, `progress.md`, `_Prompt-Gap/…`, `WEB/…`) are absent.
2. Add `doc/test-quickstart-demo.md` with an `# H1` and a fenced ```sql code block → rebuild/restart → the page appears in nav with no configuration change.
3. Open it → headings, lists, tables, links, and the highlighted SQL block render readably.
4. Add `doc/no-h1-demo.md` with **no** H1 → it still appears, titled from its filename (edge case).
5. Use the sidebar filter, then the full-text search box, to locate a known feature doc (e.g. "analysis rules") on the first attempt.
6. Delete both demo files.

**Expected**: 100% of eligible docs appear automatically; new file visible after rebuild with zero manual steps; locate-by-search succeeds first try.

### VS-3 — Responsive & branded (US3, FR-008, FR-009, SC-003, SC-004, SC-006)

1. View `/`, `/features`, `/docs`, `/download` at 360 px, 768 px, 1280 px, 1920 px → no horizontal scrolling, no unreadable text.
2. Site boots dark by default (Developer Dark: dark canvas, accent glow, monospace touches); toggle switches to the light palette and persists across reload; no flash of the wrong theme on load.
3. Throttle network (DevTools "Slow 4G") → text and navigation appear first; screenshots/heavy assets load later; first content < 3s on normal broadband.
4. Keyboard-only pass: Tab through header nav, docs tree, and download CTA (visible focus); run a contrast spot-check on body text and links.

**Expected**: consistent Developer Dark branding on every page and viewport; accessible baseline holds.

### VS-4 — Fund-me absence (FR-010)

1. Inspect header/footer on all pages → **no** payment, donation, or fund-me UI; the reserved Support slot is a structural placeholder only (commented nav position or empty footer region per contract).

**Expected**: nothing to click that asks for money; an obvious insertion point exists.

## Notes

- Playwright E2E automation is intentionally deferred (research.md R7); these scenarios are the manual gate until pages stabilize.
- Implementation details (service bodies, components, tests) belong to `tasks.md` via `/skill:speckit-tasks`, not this guide.
