# Data Model — 034-blazor-product-site

Entities derived from the spec's "Key Entities" section and the research decisions. The site is file-backed; there is **no database** — these are in-memory/serialized models.

## Document (docs page)

A documentation entry discovered automatically from the docs content source (`doc/` glob, see [contracts/docs-content.md](contracts/docs-content.md)).

| Field | Type | Rules |
|-------|------|-------|
| `Title` | string | First H1 of the file; fallback: filename-derived title (kebab/snake → words) — spec edge case |
| `Slug` | string | URL-safe, derived from relative path (`doc/WEB/x.md` → `web/x`); unique; lowercase |
| `Route` | string | `/docs/{Slug}` |
| `SourcePath` | string | Relative path of the source `.md` file (diagnostics only) |
| `Section` | string | Derived from source folder via section mapping; top-level `doc/*.md` → `"Guides"` |
| `Order` | int | Alphabetical within section (ordinal-ignore-case); optional numeric filename prefix (`01-…`) honored |
| `HtmlContent` | string | Markdig-rendered, ColorCode-highlighted, sanitized; cached at startup |
| `PlainText` | string | Stripped text for the search index |
| `Headings` | list&lt;string&gt; | H2/H3 text for search index + potential on-page TOC |
| `LastUpdated` | DateTimeOffset? | Source file last-write time at publish; nullable |

**Validation**: missing/duplicate H1 → fallback title, page still renders (spec edge case); empty docs source → empty-state UI, never an error page (spec edge case); invalid Markdown → render what parses (Markdig is non-throwing).

## DocSection (nav tree node)

| Field | Type | Rules |
|-------|------|-------|
| `Name` | string | Display name (from section mapping) |
| `Key` | string | Folder key (`web`, `guides`, …) |
| `Documents` | list&lt;Document&gt; | Ordered per Document.Order |
| `Collapsed` | bool | UI state only — deep trees must remain scrollable/collapsible (spec edge case) |

## SearchIndexEntry (serialized to `search-index.json`)

| Field | Type | Rules |
|-------|------|-------|
| `title` | string | Document.Title |
| `headings` | string | Concatenated H2/H3 |
| `body` | string | Document.PlainText (truncated, e.g. first ~20k chars) |
| `url` | string | Document.Route |

Generated once at startup alongside the nav manifest; consumed by `docs-search.js` (MiniSearch). Title-only sidebar filter works without it.

## Release (download page)

Backed by the checked-in `releases.json` manifest; schema contract in [contracts/releases-json.md](contracts/releases-json.md). Extends the updater's `UpdateManifest` fields.

| Field | Type | Rules |
|-------|------|-------|
| `Version` | string | SemVer-ish `1.YY.MMDD.HHmm` (matches repo build versioning) |
| `ReleasedAt` | DateOnly | Displayed as release date (FR-003) |
| `SupportedHosts` | list&lt;string&gt; | e.g. `SSMS 22`, `VS 2026` (FR-003) |
| `DownloadUrl` | string (uri) | Installer artifact location (host content folder or future GitHub Release) |
| `Sha256Hash` | string | Displayed for verification; hex, 64 chars |
| `ReleaseNotesUrl` | string (uri)? | Optional |
| `NotesSummary` | string? | Short markdown-lite summary |
| `MinimumOsVersion` | string? | Carried from updater schema |
| `IsLatest` | bool | Derived: first entry / highest version — not stored |

**State transitions**: `latest → archived` (implicit: a newer entry is prepended; previous releases remain listed — FR-003 "previous releases SHOULD remain accessible").

**Validation**: manifest missing/invalid → friendly fallback message + repo `/releases/latest` link, older entries still listed if parseable (spec edge case); entries sorted newest-first; `Version` required, others nullable.

## FeatureHighlight (landing/features pages)

Static content authored in components (not a data source).

| Field | Type | Rules |
|-------|------|-------|
| `Title` | string | Capability name (FR-002 list: IntelliSense, formatting, static analysis, refactoring, snippets, SQL history, AI assistance) |
| `Description` | string | 1–2 sentences |
| `Icon` | string | Icon key or placeholder (no logo assets confirmed — spec assumption) |
| `ScreenshotUrl` | string? | Optional; deferred loading (slow-network edge case) |

## Relationships

```text
DocSection 1──* Document
Document  1──1 SearchIndexEntry   (projected at startup)
Release   *    (flat ordered list; no relations)
FeatureHighlight * (static, unrelated)
```
