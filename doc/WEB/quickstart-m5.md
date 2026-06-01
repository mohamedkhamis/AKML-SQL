# Quickstart — Web edition M5 (User Story 4)

This walks a developer through the offline IntelliSense flow: paired engine + cached schema, then engine unpaired -> IntelliSense still works from the cache.

## Prerequisites

- Completed M2 quickstart (`doc/WEB/quickstart-m2.md`).
- A paired engine (see `quickstart-m3.md` for the pairing flow) with at least one SQL Server connection.

## Try the flow

1. With the engine paired and a database selected, type in the editor. IntelliSense
   completions resolve against the live engine.
2. Open **Settings -> Schema cache**. The database you just used appears in the
   table with a `Last used` timestamp and a size in KB.
3. Stop the engine (close the tray icon or kill the process).
4. Reload the editor. The status bar shows **Disconnected**.
5. Type — IntelliSense still resolves from the cached snapshot.
   *(Full cache-backed completion replaces the current "empty response on disconnect"
   path when T109 wires the fallback; the schema-cache store + sync timer are in
   place today.)*

## Snippets

1. Type `ssf` in the editor and accept the suggestion. The built-in
   `SELECT * FROM` snippet expands. Same with `cte`.
2. Open **Settings -> Schema cache** and use the "Clear all" button.
   Built-in snippets do NOT clear — they're shipped with the bundle.

## Light refactoring

1. Select a block of SQL.
2. Right-click — "Format selection" runs the formatter on the selection only.
   This is the local-only refactoring path (no engine round-trip).
3. With the engine paired, additional heavyweight refactorings appear (smart
   rename across a schema, schema-aware extract-procedure). These require the
   `refactoring.heavy` capability — the menu items render with a
   `<CapabilityNotice>` when missing.

## Closed by spec 027 (M5 offline closure)

The items below were follow-ups when M5's substrate first landed under spec 021 Phase 6; spec 027 closed them (PR #245):

- **Cache-backed completion fallback** — `CompletionService` / `QuickInfoService` /
  `SignatureHelpService` resolve from the cached IndexedDB snapshot when the bridge
  is unreachable (T109). The cache-aware **status indicator** (Live / Cached /
  Offline / Disconnected) surfaces this.
- **Snippets** — full browser library: an engine-native (`$Name$` / `$CURSOR$` /
  `$SELECTEDTEXT$`) built-in set, in-editor expansion with tab-stops, a surround-with
  chord (Ctrl+K, Ctrl+S), a `/snippets` management page, and `.akmlsnippet`
  import/export.
- **Lightweight refactoring** — all ten parser-only ops run offline via a Refactor
  menu + before/after preview (the ops were relocated into `AkmlSql.IntelliSense` so
  the browser runs the same code as the engine).
- **Heavyweight refactoring UI** — Smart Rename / Parameterize Values / Extract
  Procedure with an input dialog + change-list preview, gated on `refactoring.heavy`
  (shown disabled with an "engine" badge when offline).
- **Inline suppression editing** — per-finding "suppress on this line"
  (`-- noqa: RuleId`, cross-surface) and "suppress globally" (browser-local override).

## What is *not* in M5

- **Engine-side schema-cache message types.** `SchemaChecksumRequest`,
  `SchemaPhaseAResponse`, and `SchemaPhaseBResponse` are reserved in
  contracts/schema-cache-shape.md but the engine handler that serves them is a
  follow-up. The browser polling timer is running; the cache touches `LastUsedAt`
  to keep LRU warm until the engine wires its half.
- **Heavyweight refactoring against a *cached* schema** (offline). The UI runs
  heavyweight ops over the live bridge only; running them against a cached schema
  while the engine is down is a named follow-up (research.md Decision 3).
- **File-scope-per-rule suppression** — line + global ship; a `-- noqa-file:` style
  directive is a named follow-up (research.md Decision 4).
- **The offline-IntelliSense E2E + visual parity audit** run developer-side (a real
  engine + two GUIs); the scaffolds are checked in (`UserStory4Tests`,
  `M5-PARITY-AUDIT.md`) but the runs are interactive.

## LRU eviction

The browser's IndexedDB has a per-origin storage quota. When a write throws
`QuotaExceededError`, `ISchemaCacheEvictor` evicts the oldest entries by
`LastUsedAt` ascending until the write succeeds, fires a single
`EvictionOccurred` event with the count, and writes a diagnostics row.

To verify (when the engine wires Phase A responses): cache 10+ databases, then
populate one with a synthetic 50 MB payload — observe the older entries drain
out and the **non-blocking notice** appear once.

## Where to look in the code

| Concern | Path |
|---------|------|
| Schema cache store | `src/AkmlSql.Web/Services/ISchemaCacheStore.cs` |
| Schema sync timer | `src/AkmlSql.Web/Services/ISchemaSync.cs` |
| LRU evictor | `src/AkmlSql.Web/Services/ISchemaCacheEvictor.cs` |
| Snippet store | `src/AkmlSql.Web/Services/ISnippetStore.cs` |
| Refactoring service | `src/AkmlSql.Web/Services/IRefactoringService.cs` |
| Schema-cache settings | `src/AkmlSql.Web/Pages/SchemaCacheSettings.razor` |
| Tests | `tests/AkmlSql.Web.Tests/Cache/` + `Snippets/` + `Refactoring/` (30 tests) |
