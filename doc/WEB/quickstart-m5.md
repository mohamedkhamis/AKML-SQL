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

## What is *not* in M5

- **Engine-side schema-cache message types.** `SchemaChecksumRequest`,
  `SchemaPhaseAResponse`, and `SchemaPhaseBResponse` are reserved in
  contracts/schema-cache-shape.md but the engine handler that serves them is a
  follow-up. The browser polling timer is running; the cache touches `LastUsedAt`
  to keep LRU warm until the engine wires its half.
- **Cache-backed completion fallback.** T109 wires `CompletionService` /
  `QuickInfoService` / `SignatureHelpService` to consult the cached snapshot
  when the bridge is unreachable. The plumbing is in place
  (`ISchemaCacheStore`); the fallback path itself lands when an interactive
  session can verify completion accuracy.
- **Heavyweight refactoring UI.** The service is wired (`IRefactoringService`)
  and gated on the `refactoring.heavy` capability via `<CapabilityNotice>`. The
  UI surface (rename dialog, conflict resolution) is M5.5 work that hasn't
  landed yet.

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
