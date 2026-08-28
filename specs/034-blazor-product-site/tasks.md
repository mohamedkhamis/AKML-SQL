---
description: "Task list for 034-blazor-product-site implementation"
---

# Tasks: AKML SQL Product Website (Blazor)

**Input**: Design documents from `/specs/034-blazor-product-site/`
**Prerequisites**: [plan.md](plan.md), [spec.md](spec.md), [research.md](research.md), [data-model.md](data-model.md), [contracts/](contracts/), [quickstart.md](quickstart.md)

**Tests**: Included — the approved plan structures `tests/AkmlSql.Site.Tests/` (xunit + bunit, mirroring `AkmlSql.Web.Tests`) and the repo convention is one test project per src project.

**Organization**: Tasks are grouped by user story to enable independent implementation and testing of each story.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (US1, US2, US3)
- All file paths are repo-relative

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Project initialization and basic structure

- [X] T001 Create `src/AkmlSql.Site/` Blazor Web App project (`net10.0`, static SSR only — no Interactive Server/Auto/Client project; `LangVersion=latest`, `Nullable=enable`, `ImplicitUsings=enable` matching `src/AkmlSql.Web/AkmlSql.Web.csproj`), add Markdig `1.3.*` and Markdown.ColorCode package references, and add the project to `AKML-SQL.slnx`
- [X] T002 [P] Create `tests/AkmlSql.Site.Tests/` test project (`Sdk.Razor`-style, `net10.0`, xunit `2.*` + bunit `2.9` + coverlet) mirroring `tests/AkmlSql.Web.Tests/AkmlSql.Web.Tests.csproj` structure, and add it to `AKML-SQL.slnx`
- [X] T003 [P] Add docs content ingestion to `src/AkmlSql.Site/AkmlSql.Site.csproj` (MSBuild glob copying `../../doc/**/*.md` into `Content/docs/` at publish, with the exclusion list from `specs/034-blazor-product-site/contracts/docs-content.md`) and create `src/AkmlSql.Site/appsettings.json` with a `Docs` section (`Exclusions`, `SectionTitles`)
- [X] T004 [P] Extend `scripts/generate-theme-css.ps1` with an output-folder parameter and generate `dark.css`/`light.css` from `docs/theme-tokens.json` into `src/AkmlSql.Site/wwwroot/css/themes/`, keeping the `build.ps1` theme-drift gate (`-CheckOnly`) green

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: App shell that MUST be complete before ANY user story can be implemented

**⚠️ CRITICAL**: No user story work can begin until this phase is complete

- [X] T005 Configure `src/AkmlSql.Site/Program.cs` for static SSR: `MapRazorComponents`, `MapStaticAssets`, antiforgery, status-code/NotFound support, and the composition root where story services will be registered
- [X] T006 [P] Create `src/AkmlSql.Site/Components/Layout/MainLayout.razor`: header nav (Home · Features · Docs · Download + repository link per FR-011) with a commented, reserved Support slot per FR-010, footer (product name, MIT license link, repo link), skip link, semantic `header/main/nav/footer` landmarks
- [X] T007 [P] Create `src/AkmlSql.Site/Components/App.razor` + `Routes.razor` with head wiring: theme CSS link order and an inline no-flash theme boot script defaulting to dark (pattern: `src/AkmlSql.Web/wwwroot/js/akml-theme-boot.js`)
- [X] T008 [P] Create base stylesheet `src/AkmlSql.Site/wwwroot/css/site.css` consuming `--akml-*` tokens for the layout skeleton (header, footer, nav, docs sidebar grid) — structural styles only, no marketing polish
- [X] T009 [P] Create `src/AkmlSql.Site/Components/Pages/NotFound.razor`: friendly 404 with links back to `/` and `/docs`

**Checkpoint**: `dotnet run --project src/AkmlSql.Site` serves a dark-themed shell with working nav and 404 — user story implementation can now begin

---

## Phase 3: User Story 1 - Discover the Product and Download It (Priority: P1) 🎯 MVP

**Goal**: Landing page with value proposition + download CTA, features page, and a download page rendering the latest (and older) releases from a checked-in `releases.json` manifest with graceful fallback

**Independent Test**: quickstart.md VS-1 — from `/`, reach a working download of the latest release in ≤2 clicks; rename `wwwroot/releases.json` and confirm the friendly fallback appears with no error page

### Tests for User Story 1

- [X] T010 [P] [US1] Manifest parse/validation tests in `tests/AkmlSql.Site.Tests/Releases/ReleasesManifestTests.cs`: valid file, missing file, invalid JSON, schema-violating entries skipped, empty array — each producing the contract behavior in `specs/034-blazor-product-site/contracts/releases-json.md`
- [X] T011 [P] [US1] bunit tests in `tests/AkmlSql.Site.Tests/Components/DownloadPageTests.cs`: latest release renders (version, date, hosts, SHA-256), older releases listed, fallback message + repo `/releases/latest` link when the manifest is broken

### Implementation for User Story 1

- [X] T012 [P] [US1] Create `Release` model + `ReleasesManifest` loader in `src/AkmlSql.Site/Releases/ReleasesManifest.cs` per `contracts/releases-json.md` (newest-first ordering, `IsLatest` derived, failure states per contract) and register it in `src/AkmlSql.Site/Program.cs`
- [X] T013 [P] [US1] Seed `src/AkmlSql.Site/wwwroot/releases.json` with the current product release entry using the repo's `1.YY.MMDD.HHmm` versioning (schema per contract; `downloadUrl` placeholder to host downloads folder)
- [X] T014 [P] [US1] Create `src/AkmlSql.Site/Components/Pages/Home.razor`: product name, one-line value proposition, feature highlights, prominent download CTA (FR-001, SC-001)
- [X] T015 [P] [US1] Create `src/AkmlSql.Site/Components/Pages/Features.razor`: the FR-002 capability areas (IntelliSense, formatting, static analysis, refactoring, snippets, SQL history, AI assistance) with short descriptions and lazy-loaded screenshots where available
- [X] T016 [US1] Create `src/AkmlSql.Site/Components/Pages/Download.razor`: latest release card (version, date, supported hosts, SHA-256, installer link), older-releases list, and friendly fallback per contract (depends on T012, T013)

**Checkpoint**: US1 fully functional and independently testable — a visitor can discover the product and download it with docs/theme absent or unfinished

---

## Phase 4: User Story 2 - Browse Automatically Maintained Documentation (Priority: P2)

**Goal**: Docs section that renders `doc/` Markdown with zero per-document registration, sidebar tree navigation, title filter, and full-text search

**Independent Test**: quickstart.md VS-2 — add a new `.md` file under `doc/`, rebuild, and confirm it appears in nav and renders (H1 title and filename-fallback cases), with excluded internal files absent

### Tests for User Story 2

- [X] T017 [P] [US2] Discovery tests in `tests/AkmlSql.Site.Tests/Docs/DocsCatalogTests.cs`: glob discovery, exclusion list, H1 title + filename fallback, slug rules/dedup, section mapping, ordering, empty source — per `contracts/docs-content.md`
- [X] T018 [P] [US2] Rendering tests in `tests/AkmlSql.Site.Tests/Docs/MarkdownRendererTests.cs`: headings/tables/links/images, fenced SQL/C# highlighting, relative-link rewriting to site routes, HTML sanitization
- [X] T019 [P] [US2] bunit tests in `tests/AkmlSql.Site.Tests/Components/DocsPagesTests.cs`: docs index tree, sidebar filter, empty-state message, unknown-slug 404

### Implementation for User Story 2

- [X] T020 [P] [US2] Create docs models (`Document`, `DocSection`, `SearchIndexEntry`) in `src/AkmlSql.Site/Docs/Models.cs` per `specs/034-blazor-product-site/data-model.md`
- [X] T021 [US2] Implement `src/AkmlSql.Site/Docs/DocsCatalog.cs`: content-source scan, exclusions from config, title/slug/section/order derivation per `contracts/docs-content.md` (depends on T020)
- [X] T022 [US2] Implement `src/AkmlSql.Site/Docs/MarkdownRenderer.cs`: Markdig `UseAdvancedExtensions()`, ColorCode.Universal highlighting, HTML sanitization, relative-link/image rewriting per `contracts/docs-content.md` (depends on T020)
- [X] T023 [US2] Implement cached `src/AkmlSql.Site/Docs/DocsContentService.cs`: startup build of nav manifest + rendered HTML + plain text, `search-index.json` generation; register in `src/AkmlSql.Site/Program.cs` (depends on T021, T022)
- [X] T024 [P] [US2] Create `src/AkmlSql.Site/Components/Layout/DocsLayout.razor`: collapsible, scrollable sidebar section tree with always-on title filter (works with JS disabled)
- [X] T025 [P] [US2] Create `src/AkmlSql.Site/Components/Pages/DocsIndex.razor` (`/docs`): section tree of all documents + empty-state message per contract
- [X] T026 [US2] Create `src/AkmlSql.Site/Components/Pages/DocPage.razor` (`/docs/{slug}`): rendered document with headings/code/tables/images, unknown-slug NotFound (depends on T023, T024)
- [X] T027 [P] [US2] Add full-text search: vendor MiniSearch into `src/AkmlSql.Site/wwwroot/js/` and create `docs-search.js` fetching the generated `search-index.json` (deferred, progressive enhancement per `contracts/site-routes.md`)

**Checkpoint**: US1 AND US2 both work independently — docs auto-populate from `doc/` while landing/download remain intact

---

## Phase 5: User Story 3 - Consistent Branded Visual Experience (Priority: P3)

**Goal**: Developer Dark branding applied consistently across all pages, responsive 360–1920 px, optional light-mode toggle

**Independent Test**: quickstart.md VS-3 — view home/features/docs/download at 360/768/1280/1920 px with no horizontal scroll or unreadable text; dark default; toggle persists across reloads with no flash of wrong theme

### Implementation for User Story 3

- [X] T028 [P] [US3] Create `src/AkmlSql.Site/wwwroot/js/theme-toggle.js` (dark default, follows `prefers-color-scheme`, persists via localStorage) and add the toggle control to the header in `src/AkmlSql.Site/Components/Layout/MainLayout.razor`
- [X] T029 [P] [US3] Build the Developer Dark marketing layer in `src/AkmlSql.Site/wwwroot/css/site.css`: glowing accent treatment, code-first/monospace touches per FR-009, layered on `--akml-*` tokens (no hardcoded chrome colors)
- [X] T030 [US3] Responsive pass across all pages in `src/AkmlSql.Site/wwwroot/css/site.css`, `src/AkmlSql.Site/Components/Layout/MainLayout.razor`, and `src/AkmlSql.Site/Components/Layout/DocsLayout.razor`: collapsing header nav and docs sidebar, readable typography at 360–1920 px (SC-003)

**Checkpoint**: All user stories independently functional with consistent Developer Dark branding

---

## Phase 6: Polish & Cross-Cutting Concerns

**Purpose**: Improvements that affect multiple user stories

- [X] T031 [P] SEO infrastructure in `src/AkmlSql.Site/`: per-page `<title>`/meta description/Open Graph tags, `sitemap.xml` covering all doc routes, `robots.txt` (per `contracts/site-routes.md`)
- [X] T032 [P] Accessibility hardening: focus-visible styles, aria labels on nav/search/toggle, contrast verification against theme tokens (SC-006) across `src/AkmlSql.Site/wwwroot/css/site.css` and layout components
- [X] T033 [P] Performance pass: lazy-load screenshots, defer `docs-search.js`, static-asset cache headers, verify <3s primary content (SC-004)
- [X] T034 Wire the site into `build.ps1` (build/publish step) and verify the full solution build (`AKML-SQL.slnx` via MSBuild) stays green
- [X] T035 [P] Update `README.md` (site section + docs link) and `CLAUDE.md` project-structure/test listings for `AkmlSql.Site` and `AkmlSql.Site.Tests`
- [X] T036 Run all quickstart.md validation scenarios VS-1..VS-4 (including VS-4: no payment/donation UI anywhere, Support slot reserved structurally) and fix findings

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies — can start immediately
- **Foundational (Phase 2)**: Depends on T001–T004 — BLOCKS all user stories
- **User Stories (Phases 3–5)**: All depend on Foundational completion; then proceed in priority order (P1 → P2 → P3) or in parallel
- **Polish (Phase 6)**: Depends on all desired user stories being complete

### User Story Dependencies

- **US1 (P1)**: Starts after Foundational — no dependency on other stories (download page reads only `releases.json`)
- **US2 (P2)**: Starts after Foundational — independent of US1 (docs pipeline is self-contained); shares only MainLayout/theme from Foundational
- **US3 (P3)**: Starts after Foundational — styles all pages, so practically lands best after US1/US2 pages exist, but remains independently testable on the shell alone

### Within Each User Story

- Tests (T010–T011, T017–T019) written FIRST and confirmed failing before implementation
- Models before services; services before pages (T012 → T016; T020 → T021/T022 → T023 → T026)
- Story checkpoint validated before moving to the next priority

### Parallel Opportunities

- Setup: T002, T003, T004 in parallel after T001
- Foundational: T006–T009 in parallel after T005
- US1: T010, T011 in parallel; then T012–T015 in parallel; T016 last
- US2: T017–T019 in parallel; T021/T022 in parallel; T024/T025/T027 in parallel after T023
- US3: T028, T029 in parallel; T030 last
- Polish: T031, T032, T033, T035 in parallel

---

## Parallel Example: User Story 2

```bash
# Launch all US2 tests together (write-first, expect failures):
Task: "Discovery tests in tests/AkmlSql.Site.Tests/Docs/DocsCatalogTests.cs"
Task: "Rendering tests in tests/AkmlSql.Site.Tests/Docs/MarkdownRendererTests.cs"
Task: "bunit tests in tests/AkmlSql.Site.Tests/Components/DocsPagesTests.cs"

# After models (T020), launch pipeline services together:
Task: "Implement src/AkmlSql.Site/Docs/DocsCatalog.cs"
Task: "Implement src/AkmlSql.Site/Docs/MarkdownRenderer.cs"

# After DocsContentService (T023), launch UI pieces together:
Task: "DocsLayout.razor sidebar tree + filter"
Task: "DocsIndex.razor + empty state"
Task: "docs-search.js + vendored MiniSearch"
```

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Complete Phase 1: Setup (T001–T004)
2. Complete Phase 2: Foundational (T005–T009) — CRITICAL, blocks all stories
3. Complete Phase 3: User Story 1 (T010–T016)
4. **STOP and VALIDATE**: run quickstart.md VS-1
5. Deploy/demo if ready — a working product + download site

### Incremental Delivery

1. Setup + Foundational → dark shell with nav/404
2. + US1 → validate VS-1 → deployable MVP (discover + download)
3. + US2 → validate VS-2 → docs live, auto-maintained
4. + US3 → validate VS-3 → branded, responsive, toggle
5. Polish → VS-1..VS-4 full pass, SEO/a11y/perf gates

---

## Notes

- [P] tasks = different files, no dependencies on incomplete tasks
- [USn] label maps each story-phase task to its spec.md user story for traceability
- Verify story tests fail before implementing (red-green)
- Contracts are binding: `contracts/releases-json.md` failure behavior, `contracts/docs-content.md` inclusion rules, `contracts/site-routes.md` route/FR-010 requirements
- Repo git rules apply: no commits without the user's explicit instruction
