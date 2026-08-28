# Contract: Docs Content Source & Search Index

How repo documentation becomes site pages, with zero per-document registration (FR-005). Data shapes in [../data-model.md](../data-model.md); decisions in [../research.md](../research.md) R2–R4.

## Content source

- **Included**: `doc/**/*.md` from the repo root, ingested via csproj glob into the site's `Content/docs/` at publish (default-include).
- **Excluded** (configurable list in `appsettings.json`, e.g. `Docs:Exclusions`):
  - Folders: `_Prompt-Gap/`, `Phase-One/`, `superpowers/`, `WEB/` (internal process/milestone docs)
  - Files: `progress.md`, `bugs.md`, `manual-test-plan.md`, `codebase-audit-*.md` (internal state)
  - `specs/**` is never included (process artifacts, not product documentation)
- **Adding a new doc**: drop a `.md` file anywhere under `doc/` (not excluded) → it appears in nav and search on next publish. No code, config, or front-matter change required.

## Title, slug, section, ordering

| Property | Rule |
|----------|------|
| Title | First `# H1` text. Fallback: filename without extension, separators → spaces, title-cased (e.g. `m3-security.md` → "M3 Security") |
| Slug | Relative path without extension, lowercase, spaces/`_` → `-` (e.g. `doc/WEB/M4-iis-installer.md` → `web/m4-iis-installer`); duplicates get `-2`, `-3` suffixes |
| Section | First path segment mapped via `Docs:SectionTitles` (default: top-level files → `"Guides"`; subfolder name title-cased otherwise) |
| Order | Ordinal-ignore-case by title within a section; leading `NN-` filename prefix forces position |

## Rendering

- Markdig `UseAdvancedExtensions()` (tables, task lists, auto-links, etc.); code fences highlighted server-side via ColorCode.Universal (C#, SQL, PowerShell, JSON, XML).
- Relative links between included docs are rewritten to site routes (`./ipc-api.md` → `/docs/ipc-api`); links to excluded/external files are left as-is; images resolve to copied content assets.
- Output HTML is sanitized before insertion.
- Malformed files render what parses; a missing title never blocks listing (fallback title) — spec edge cases.
- All rendering happens once at startup into an in-memory cache (nav manifest + HTML + plain text); no per-request parsing.

## `search-index.json`

Generated at startup, served as a static-computed asset, consumed by `wwwroot/js/docs-search.js` (MiniSearch):

```json
{
  "generatedAt": "2026-08-27T00:00:00Z",
  "documents": [
    { "title": "Architecture Overview", "headings": "Components; Startup sequence", "body": "…plain text…", "url": "/docs/architecture" }
  ]
}
```

- One entry per rendered Document; `body` is whitespace-normalized plain text.
- FR-007 baseline (sidebar title filter) works with JS disabled; MiniSearch adds full-text + typo tolerance when JS is available.

## Empty & scale behavior

- Empty content source → `/docs` shows an empty-state message, not an error (spec edge case).
- Deep/long trees: sections collapsible, sidebar scrollable, pages stay performant (spec edge case) — rendering is cached HTML, so long documents cost nothing per request.
