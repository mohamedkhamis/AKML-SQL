# Implementation Plan: Product Studio

Date: 2026-10-22. Spec: [spec.md](spec.md). Working checkout: `043-web-query-tabs-polish`; no branch operation. Use `SPECIFY_FEATURE=044-site-product-studio` for this checkout's older scripts, which ignore feature.json.

## Summary

Integrate approved concept A into the existing static SSR site. Reuse native links, release manifest, ProductScreenshot and screenshot viewer. Add a progressively enhanced screenshot selector and theme-aware marketing palette, then align adjacent public copy.

## Technical Context

C# / net10.0 Blazor static SSR, plain CSS/JavaScript. Existing bUnit/xUnit Site tests and repository Playwright Chromium. No new dependencies, storage or endpoints. Scope: Home, shared public styling/footer, Features/Download introductory copy, a public browser-setup guide, static assets, semantic theme tokens and affected tests. Reserve image sizes, eager-load hero only and retain no-JavaScript navigation. Target 320–1920 px, all existing themes.

## Constitution Check

Pre- and post-design: PASS. No shell/engine logic changes. Use generated theme tokens and full MSBuild restore/build. Preserve existing tests except expectations explicitly superseded by approved design; add support/tour/fallback coverage. No Git mutation. Reuse existing site architecture and viewer. No complexity exceptions.

## Research and decisions

See [research.md](research.md). Do not run deployed E2E fixtures: they target IIS and may mutate production analytics. Publish and run a private local preview with isolated analytics/download directories. New palette tokens are Site-only consumers; generation also updates Web theme files to preserve the existing sync gate, with no change to existing token values.

## Project Structure

- `src/AkmlSql.Site/Components/Pages/Home.razor`: benefits, database panels, tour, setup, FAQ.
- `Components/ProductScreenshot.razor`: optional explicit paths preserving Name convention.
- `wwwroot/css/product-studio.css`, `wwwroot/js/product-tour.js`: scoped layout and progressive tour.
- `docs/theme-tokens.json`: additive Marketing tokens; regenerate Site and Web themes.
- `Components/App.razor`: external CSP-compatible assets; existing theme boot stays unchanged.
- `Components/Pages/Features.razor`, `Download.razor`, `Components/HostSupportNotice.razor`, `Layout/MainLayout.razor`: truthful public copy.
- `doc/topics/browser-setup.md`: public auto-ingested setup guide.
- `tests/AkmlSql.Site.Tests/Components/HomePageTests.cs`: acceptance coverage.
- This spec folder: contract, validation guide, task ledger, asset provenance and evidence.

## Complexity Tracking

None. No new framework, service, modal or persistence.
