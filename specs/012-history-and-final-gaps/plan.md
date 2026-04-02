# Implementation Plan: SQL History Enhancements & Final Parity Gaps

**Branch**: `012-history-and-final-gaps` | **Date**: 2026-04-02 | **Spec**: [spec.md](./spec.md)
**Input**: 7 remaining gaps from `doc/AKML_SQL_Gap_Analysis_1.md`

## Summary

Fill the final 7 gaps to achieve absolute 100% SQL Prompt v11 parity. Research revealed that Starring and Rename are 50-80% done (schema + UI exist). Advanced Search needs a query parser. Copy as IN and Unformat are straightforward additions. Version History and Search Highlighting require new components.

## Technical Context

**Language/Version**: C# / .NET Framework 4.7.2 (Shell), .NET 10 (Engine)
**Storage**: SQLite (History database, WAL mode, FTS5 virtual table)
**Testing**: xunit 2.x
**Target Platform**: SSMS 20/21/22, VS 2019/2022/2026
**Constraints**: No XAML in SharedProject, WPF programmatic layout

## Constitution Check

*No constitution file found.*

## Project Structure

```text
src/AkmlSql.Shell.Shared/
├── History/
│   ├── HistoryToolWindowControl.cs    # MODIFY: rename context menu, highlighting, version panel
│   ├── HistoryViewModel.cs            # MODIFY: advanced search parser integration
│   └── HistorySearchParser.cs         # CREATE: prefix/wildcard/boolean search parser
├── Productivity/Grid/
│   └── GridCopyAsMenu.cs              # MODIFY: add FormatAsInClause + menu item

src/AkmlSql.Engine/
├── History/
│   ├── HistoryDatabase.cs             # MODIFY: add history_versions table, retention exemption
│   └── HistoryRetentionService.cs     # MODIFY: skip is_favorite=1 rows
├── Refactoring/Operations/Lightweight/
│   └── UnformatOperation.cs           # CREATE: whitespace stripper
├── Formatter/
│   └── FormatRequestHandler.cs        # MODIFY: add Unformat=17 case

src/AkmlSql.Core/
├── Ipc/Messages/
│   └── FormatActionRequest.cs         # MODIFY: add Unformat=17 enum value
```

## Implementation Phases

### Phase 1: Quick Wins (4 tasks, all independent)

**Task 1.1: Starring Retention Exemption** — Modify `HistoryRetentionService` to skip `is_favorite=1` during cleanup. Star UI already exists.

**Task 1.2: Rename Closed Queries** — Add "Rename" to History context menu, update `tab_title` column.

**Task 1.3: Copy as IN Clause** — Add `FormatAsInClause()` to `GridCopyAsMenu.cs` with proper quoting.

**Task 1.4: Unformat Action** — Add `Unformat=17` enum, create `UnformatOperation.cs`, wire into dispatcher + lightbulb.

### Phase 2: Advanced Search (2 tasks)

**Task 2.1: Search Query Parser** — Create `HistorySearchParser.cs` with prefix, wildcard, phrase, boolean parsing.

**Task 2.2: Wire into ViewModel** — Replace direct `SearchText` usage with parsed query.

### Phase 3: Polish Features (2 tasks)

**Task 3.1: Search Match Highlighting** — TextBlock Run-based highlighting in code preview.

**Task 3.2: Version History Timeline** — New SQLite table, version tracking, timeline panel UI.

## Dependency Graph

```
Phase 1 (all parallel):
  1.1 (Starring retention) ── Very Low
  1.2 (Rename) ───────────── Very Low
  1.3 (Copy as IN) ─────── Low
  1.4 (Unformat) ──────── Low

Phase 2 (sequential):
  2.1 (Search parser) ──── Medium
  2.2 (Wire into VM) ───── depends on 2.1

Phase 3 (independent of Phase 2):
  3.1 (Highlighting) ───── Low-Medium
  3.2 (Version history) ── Medium-High
```
