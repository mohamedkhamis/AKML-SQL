# Research: SQL History Enhancements & Final Parity Gaps

**Date**: 2026-04-02 | **Branch**: `012-history-and-final-gaps`

## Key Findings

### Starring / Favorites — PARTIALLY IMPLEMENTED
- `HistoryDatabase.cs` already has `is_favorite` column in SQLite schema
- `HistoryToolWindowControl.cs` already has a clickable star column and "Toggle Favorite" button
- `HistoryViewModel.cs` already has `FavoritesOnly` filter flag
- **Remaining work**: Verify starred queries survive retention auto-trim (add exemption in `HistoryRetentionService`)

### Advanced Search — PARTIALLY IMPLEMENTED
- FTS5 full-text search on `sql_text` already exists
- Server/Database/Status/Date filters already exist as dropdowns
- **Not implemented**: prefix-based search syntax (`server:`, `sql:`, etc.), wildcard matching (`*`, `?`), exact phrase (`"..."`), boolean operators (`OR`, `NOT`)
- Need: Advanced search query parser in HistoryViewModel

### Version History — NOT IMPLEMENTED
- Current schema has no `versions` table
- Each auto-save creates a new row (not linked to previous versions of the same query)
- **Need**: `history_versions` table + timeline panel UI

### Rename Closed Queries — PARTIALLY IMPLEMENTED
- `tab_title` column exists in History schema
- **Need**: Right-click context menu "Rename" option that updates `tab_title`

### Search Match Highlighting — NOT IMPLEMENTED
- Code preview is a TextBlock (text trimming, no rich formatting)
- **Need**: Replace with RichTextBox or use TextBlock Runs with background color

### Copy as IN Clause — NOT IMPLEMENTED
- `GridCopyAsMenu.cs` has 7 formats, easy pattern to add new one
- Signature: `internal static string FormatAsInClause(string[] headers, List<string[]> rows)`

### Unformat Action — NOT IMPLEMENTED
- Next `FormatActionType` enum value: 17
- No existing whitespace stripper — need new lightweight operation

## Revised Scope (adjusted by findings)

| Gap | Research Status | Actual Effort |
|-----|----------------|---------------|
| Starring | 80% done (schema + UI exist) | Very Low — just add retention exemption |
| Advanced Search | 30% done (FTS5 + filters exist) | Medium — query parser needed |
| Copy as IN | Not started | Low — add format + menu item |
| Unformat | Not started | Low — new lightweight operation |
| Search Highlighting | Not started | Low-Medium — TextBlock to Runs conversion |
| Version History | Not started | Medium-High — new schema + UI |
| Rename | 50% done (column exists) | Very Low — context menu + update query |
