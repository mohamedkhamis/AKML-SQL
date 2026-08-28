# Implementation Plan: AKML SQL Product Website (Blazor)

**Branch**: `034-blazor-product-site` | **Date**: 2026-08-27 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/034-blazor-product-site/spec.md`

## Summary

Build a public product website for AKML SQL as a new Blazor Web App (`src/AkmlSql.Site/`, net10.0) using **static SSR**: landing page with download CTA, features page, download page driven by a checked-in `releases.json` manifest, and a documentation section that auto-discovers the repo's `doc/` Markdown files at startup (Markdig rendering + server-side syntax highlighting + cached nav manifest) with sidebar tree navigation and MiniSearch-backed search — all themed "Developer Dark" via the existing `docs/theme-tokens.json` → `generate-theme-css.ps1` pipeline, dark by default. No database, no fund-me (structural nav slot reserved), no WASM payload.

Technical approach and all hosting/library decisions: [research.md](research.md).

## Technical Context

**Language/Version**: C# (LangVersion `latest`) / .NET 10 (`net10.0`), `Nullable=enable`, `ImplicitUsings=enable` — matching `src/AkmlSql.Web/` conventions  
**Primary Dependencies**: ASP.NET Core Blazor Web App (static SSR only); Markdig 1.3.x (`UseAdvancedExtensions`); Markdown.ColorCode (ColorCode.Universal) for server-side C#/SQL highlighting; MiniSearch (small vendored JS) for full-text docs search  
**Storage**: None — file-based content only: `doc/**/*.md` ingested via MSBuild glob at publish; `releases.json` checked-in manifest served as a static asset  
**Testing**: xunit 2.* + bunit 2.9 + coverlet in `tests/AkmlSql.Site.Tests/` (mirrors `tests/AkmlSql.Web.Tests/`); Playwright E2E deferred (manual validation per [quickstart.md](quickstart.md))  
**Target Platform**: any ASP.NET Core host (IIS / Linux container / App Service); evergreen browsers, responsive 360–1920 px  
**Project Type**: web application (content/marketing + documentation site)  
**Performance Goals**: SC-004 — primary content visible < 3s on typical broadband (SSR HTML first paint, deferred heavy assets, in-memory docs cache)  
**Constraints**: FR-005 zero-registration docs (default-include glob + exclusion list); FR-009 Developer Dark default via existing token pipeline (drift gate in `build.ps1` must stay green); FR-010 no payment/donation UI (reserved nav/footer slot only); SC-006 keyboard navigable + sufficient contrast; no database  
**Scale/Scope**: 5 page types (home, features, download, docs index, doc page); ~60–90 docs pages from `doc/` (58 files today, exclusions applied); 31 internal `specs/*/spec.md` excluded; 1 new src project + 1 new test project

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

`.specify/memory/constitution.md` is the **unfilled template** (`[PROJECT_NAME]`, `[PRINCIPLE_*]` placeholders) — the project has no ratified constitution, so **no gates are defined or enforced**. Noted for transparency; consider `/skill:speckit-constitution` at some point. Pre-Phase-0: PASS (nothing to violate). Post-Phase-1 re-check: PASS — design adds exactly one src project + one test project, no storage, no new organizational layers beyond what the spec implies.

## Project Structure

### Documentation (this feature)

```text
specs/034-blazor-product-site/
├── plan.md              # This file (/speckit.plan output)
├── research.md          # Phase 0 output — all decisions + alternatives
├── data-model.md        # Phase 1 output — Document, Release, FeatureHighlight, nav/search models
├── quickstart.md        # Phase 1 output — validation scenarios mapped to SC-001..006
├── contracts/           # Phase 1 output — site routes, releases.json, docs content source
│   ├── site-routes.md
│   ├── releases-json.md
│   └── docs-content.md
├── checklists/
│   └── requirements.md  # Existing spec-quality checklist (all items pass)
└── tasks.md             # Phase 2 output (/speckit.tasks — NOT created by /speckit.plan)
```

### Source Code (repository root)

```text
src/
├── AkmlSql.Site/                    # NEW — Blazor Web App (net10.0, static SSR)
│   ├── Program.cs                   # SSR-only setup, static assets, docs services
│   ├── Components/
│   │   ├── App.razor / Routes.razor
│   │   ├── Layout/                  # MainLayout (header nav + reserved Support slot, footer), DocsLayout (sidebar tree + filter)
│   │   └── Pages/                   # Home, Features, Download, DocsIndex, DocPage, NotFound
│   ├── Docs/                        # DocsPipeline: content glob scan, H1 title/slug derivation,
│   │                                #   Markdig render + ColorCode highlight, nav manifest,
│   │                                #   search-index.json generation, in-memory cache
│   ├── Releases/                    # ReleasesManifest load/parse/validate (schema: contracts/releases-json.md)
│   ├── Content/
│   │   └── docs/                    # Populated at publish by csproj glob from ../../doc (exclusion list)
│   ├── wwwroot/
│   │   ├── css/                     # themes/dark.css + light.css (generated), site.css (Developer Dark marketing layer)
│   │   ├── js/                      # theme-toggle.js, docs-search.js (MiniSearch, vendored)
│   │   └── releases.json            # Release manifest (updated by release script)
│   └── appsettings.json             # Docs content options (exclusions, section titles)
│
├── docs/theme-tokens.json           # EXISTING — token source of truth (unchanged)
├── scripts/generate-theme-css.ps1   # EXISTING — extended with -SiteOut path or run twice (drift gate stays green)
│
tests/
└── AkmlSql.Site.Tests/              # NEW — xunit + bunit + coverlet (mirrors AkmlSql.Web.Tests shape)
    ├── Docs/                        # Pipeline tests: discovery, exclusions, H1 fallback, render, manifest, index
    ├── Releases/                    # Manifest parse/validation tests incl. broken-feed edge case
    └── Components/                  # bunit: nav renders, download CTA, docs tree, reserved Support slot absent of payment UI
```

**Structure Decision**: New standalone `src/AkmlSql.Site/` project — deliberately **not** folded into `src/AkmlSql.Web/` (that is the product's web edition with heavy engine/AI dependencies; see research.md R1). Docs content stays in the repo `doc/` folder and flows in via csproj glob (FR-005), exclusions in `appsettings.json` (research.md R3). Release data stays a checked-in manifest (research.md R5). Tests follow the established web test conventions (research.md R7).

## Complexity Tracking

No constitution gates exist (unfilled template) and no design element exceeds what the spec requires — one src project, one test project, no database, no extra services. Table intentionally empty.
