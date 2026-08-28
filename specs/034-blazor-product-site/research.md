# Phase 0 Research — 034-blazor-product-site

**Date**: 2026-08-27 | **Spec**: [spec.md](spec.md)

All Technical Context unknowns resolved below. Repo facts verified by codebase exploration; technology choices verified against current Microsoft/library documentation.

## R1. Blazor hosting / render mode

**Decision**: Blazor Web App (`net10.0`) with **static SSR** as the only render mode; no Interactive Server/Auto. Progressive-enhancement interactivity (theme toggle, docs search) via small vanilla JS files, not WASM islands.

**Rationale**:
- The site is content-first (landing, features, docs, download) where P1 is product *discovery* — SEO and first paint dominate. Static SSR ships full HTML; standalone WASM shows a loading shell behind a ~1 MB runtime download, which undermines SC-004 (<3s primary content) and search indexing.
- Static SSR cannot be deployed to pure static hosting (GitHub Pages/Netlify) — it needs any ASP.NET Core host. That is acceptable: the product already plans a web property (`updates.akmlsql.com` manifest endpoint, `src/AkmlSql.Core/Constants.cs:24`), so an ASP.NET Core host is aligned with the product's direction and can later serve the site, the update manifest, and download artifacts together.
- The existing `src/AkmlSql.Web/` is a standalone WASM app, but it is the *product's web edition* (in-browser formatter/analyzer + engine bridge), not a marketing site, and carries heavy dependencies (AI, Analysis, IntelliSense, IndexedDB). A separate, lean project is correct.
- Vanilla JS sprinkles instead of WASM islands keep v1 to a single project (no `.Client` project), zero WASM payload, and are sufficient for a theme toggle and fetch-and-filter search.

**Alternatives considered**: Standalone Blazor WASM on GitHub Pages (free static hosting, matches AkmlSql.Web's SDK — rejected: CSR hurts SEO/first paint, needs build-time prerender hacks); Interactive Auto (rejected: pushes a WASM download onto visitors who only read content); static site generators like Statiq/DocFX (rejected: owner mandated Blazor, FR-012).

## R2. Markdown pipeline (FR-004, FR-005, FR-006)

**Decision**: Markdig 1.3.x with `UseAdvancedExtensions()`; docs rendered **at startup, cached in memory**; server-side syntax highlighting via ColorCode.Universal (`Markdown.ColorCode`); content ingested by MSBuild glob from the repo `doc/` folder.

**Rationale**:
- Markdig is the de-facto .NET Markdown parser (DocFX, Semantic Kernel, MudBlazor all use it) and targets net10.0. No Markdown library exists in the repo today — this is a new dependency, the natural fit.
- Runtime scan-at-startup + cache directly satisfies FR-005: adding a `.md` file to the content source requires no code/config change; it appears on next build+deploy. No database, no per-document registration.
- Server-side highlighting ships highlighted HTML in the first paint (zero JS dependency, themeable via CSS classes), consistent with the SSR decision. ColorCode.Universal covers C# and SQL.
- Ingestion via csproj glob (`../../doc/**/*.md` as content with an exclusion list, copied at publish) means "new file appears automatically" is a build-system property, not app code.

**Alternatives considered**: Build-time Markdown→HTML generation (rejected: extra tooling step, runtime scan is simpler and equivalent for this size); client-side highlight.js/Prism (rejected: render-blocking JS for static content); reusing the web edition's vendored CodeMirror 6 (rejected: editor component, far too heavy for static code blocks).

## R3. Docs content source inclusion rules (spec Assumption: "decided during planning")

**Decision**: Content source = repo `doc/` folder, **default-include with an exclusion list**. Include `doc/*.md` and subfolders; exclude dev-internal content (`_Prompt-Gap/`, `Phase-One/`, `superpowers/`, `WEB/` milestone PRDs, and internal files such as `progress.md`, `bugs.md`, `codebase-audit-*`, `manual-test-plan.md`). `specs/*/spec.md` files are excluded (internal process artifacts, not product documentation). Titles derived from each file's first H1 (no front matter exists in the corpus); category = source folder; ordering = alphabetical. Malformed/title-less files fall back to a filename-derived title (spec edge case).

**Rationale**: default-include is what makes "add a file → it appears" true (FR-005) — an exclusion list only ever removes things. The corpus today is 58 Markdown files in `doc/`, all starting with an `# H1`, no YAML front matter; deriving titles from H1 requires zero changes to existing docs.

**Alternatives considered**: curated allowlist / hand-maintained TOC (rejected: violates FR-005's zero-registration rule); including `specs/*/spec.md` (rejected: internal specs expose process detail, not user documentation); adding YAML front matter to all docs (rejected: churns 58 files for metadata that H1 + folder already provide; front matter support can be added later as an optional override).

## R4. Docs search / locate (FR-007)

**Decision**: Sidebar navigation tree (folder sections → documents) + two-tier locate: (1) always-on sidebar title filter, (2) full-text search box backed by a startup-generated `search-index.json` (title, headings, plain-text body, URL) queried with MiniSearch (~few KB, BM25, typo tolerance) via a small JS file.

**Rationale**: at ~60–90 pages the index is tens of KB — trivial to generate alongside the nav manifest and to search in-browser. MiniSearch is the current consensus sweet spot for small docs sites; the title-only filter keeps FR-007 satisfied even with JS disabled.

**Alternatives considered**: Lunr.js (rejected: stalled project, current guides steer away); FlexSearch (rejected: finicky API, overkill); Pagefind (rejected: indexes built HTML via a Node post-build step — wrong fit for a Blazor-served site); hosted search (rejected: unnecessary service dependency).

## R5. Release feed for the download page (FR-003)

**Decision**: A checked-in `releases.json` manifest served as a static asset of the site, listing all releases (latest first) with version, date, supported hosts, download URL, SHA-256, and notes summary. The release script updates it at release time; the download page renders it server-side. Schema extends the existing updater manifest contract (`src/AkmlSql.Core/Update/UpdateManifest.cs`: `version`, `downloadUrl`, `releaseNotesUrl`, `sha256Hash`, `minimumOsVersion`) with `releasedAt`, `notesSummary`, `supportedHosts`. A plain `/releases/latest` repo link is the fallback when the manifest is missing/invalid (spec edge case: keep older releases listed, show friendly message).

**Rationale**: releases are produced by a *local* Inno Setup build with no CI and no GitHub Releases in use today (verified: zero `releases/download` or `api.github.com` references). A checked-in manifest is deterministic, cacheable, works when GitHub is down, and lets the site show checksums. Browser calls to `api.github.com` were rejected (60 req/hr/IP unauthenticated, shared-NAT 403s, runtime dependency on the page whose only job is delivering the installer). The same manifest can later back `updates.akmlsql.com/manifest.json` — one schema, two consumers.

**Alternatives considered**: GitHub Releases API from the browser (rejected: rate limits, CORS-dependent, runtime failure mode); build-time generation from git tags (rejected: no tag discipline/CI in repo today); installer exes checked into the repo (rejected: binary bloat — artifacts live on the host or future GitHub Releases, the manifest points at them).

## R6. Theming (FR-009, US3)

**Decision**: Reuse the existing token pipeline: `docs/theme-tokens.json` (v2, ~60 tokens, all with light/dark/high-contrast values) + `scripts/generate-theme-css.ps1` → generated `dark.css`/`light.css` custom properties (`--akml-*`). The site boots with the **dark palette as default** (Developer Dark), with an optional JS toggle following `prefers-color-scheme` for light. Site-specific "glow"/marketing accents are layered in site CSS on top of `--akml-*` tokens.

**Rationale**: one source of truth already shared between WPF and the web edition, with a build-time drift gate (`build.ps1` step 1, `-CheckOnly`). The dark palette is slate-blue (canvas `#0F172A`, accent `#2563EB`) — not literally GitHub Dark `#0d1117`, but it delivers the spec's "dark background, glowing accent, code-first aesthetic" while keeping product and site visually consistent; the generator already emits everything needed.

**Alternatives considered**: hand-rolled GitHub Dark clone palette (rejected: forks the token system, breaks the drift gate, diverges from product branding); defaulting to light like AkmlSql.Web's `index.html` (rejected: owner selected Developer Dark as the default, FR-009).

## R7. Testing approach

**Decision**: Mirror existing web test conventions: `tests/AkmlSql.Site.Tests/` — xunit 2.* + bunit 2.9 + coverlet (same shape as `tests/AkmlSql.Web.Tests/`, which uses `Sdk.Razor`, net10.0). Playwright E2E (pattern from `tests/AkmlSql.Web.E2E.Tests/`) deferred to a later phase; quickstart.md defines manual validation scenarios instead.

**Rationale**: consistency with the repo's established web test stack; docs pipeline (scan, title derivation, manifest, rendering) is plain C# and unit-testable without a browser.

**Alternatives considered**: Playwright E2E in v1 (deferred: infra cost outweighs value until pages stabilize; manual quickstart covers SC-001..006).

## Resolved Technical Context summary

| Unknown | Resolution |
|---------|------------|
| Hosting model | Blazor Web App, static SSR only, net10.0 |
| Markdown | Markdig 1.3.x + ColorCode.Universal highlighting, startup scan + cache |
| Docs source | `doc/` default-include + exclusion list; specs excluded; H1 titles |
| Search | Sidebar filter + MiniSearch over `search-index.json` |
| Release feed | Checked-in `releases.json` (extends `UpdateManifest` schema) |
| Theme | Existing `theme-tokens.json` pipeline, dark default |
| Tests | xunit + bunit (`AkmlSql.Site.Tests`) |
| Storage | None — file-based content only |
