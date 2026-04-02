# Implementation Plan: SQL Prompt Core Parity — Remaining Gaps

**Branch**: `011-core-parity-remaining-gaps` | **Date**: 2026-04-02 | **Spec**: [spec.md](./spec.md)
**Input**: Feature specification from `/specs/011-core-parity-remaining-gaps/spec.md`

## Summary

Fill 6 remaining gaps (reduced from 7 — Copy with Headers already implemented) to achieve 100% SQL Prompt Core feature parity. Most gaps are low-effort enhancements to existing infrastructure. Only Split Table (P4) is a new heavyweight operation.

## Technical Context

**Language/Version**: C# / .NET Framework 4.7.2 (Shell), .NET 10 (Engine)
**Primary Dependencies**: VS SDK 17.14.x, ClosedXML (Excel export), TSql170Parser (AST)
**Testing**: xunit 2.x
**Target Platform**: SSMS 20/21/22, VS 2019/2022/2026
**Project Type**: VS/SSMS extension
**Constraints**: No XAML in SharedProject, build each shell project individually

## Constitution Check

*No constitution file found. Proceeding without gates.*

## Project Structure

### Source Code (files to create/modify)

```text
src/AkmlSql.Engine/
├── Refactoring/Operations/Lightweight/
│   ├── ExpandInsertColumnsOperation.cs    # MODIFY: add metadata comments
│   └── ConvertSpExecutesqlOperation.cs    # CREATE: sp_executesql → static SQL
├── Export/
│   └── GridExportService.cs               # MODIFY: 15+ digit precision option

src/AkmlSql.Core/
├── Config/AppSettings.cs                  # MODIFY: add new settings
├── Ipc/Messages/FormatActionRequest.cs    # MODIFY: add enum value

src/AkmlSql.Shell.Shared/
├── Editor/Completion/
│   ├── AkmlCompletionPopup.cs             # MODIFY: Ctrl opacity toggle
│   └── CompletionController.cs            # MODIFY: detect Ctrl key state
├── Analysis/
│   ├── LightbulbProvider.cs               # MODIFY: add sp_executesql action
│   └── RefactoringAction.cs               # MODIFY: add new action type
├── Tabs/
│   └── TabColoringManager.cs              # MODIFY: gradient option
├── Refactoring/
│   └── SplitTableCommand.cs               # CREATE: Split Table shell command

src/AkmlSql.Engine/
├── Refactoring/Operations/Heavyweight/
│   └── SplitTableOperation.cs             # CREATE: Split Table operation

tests/AkmlSql.Core.Tests/
├── Refactoring/
│   ├── InsertMetadataTests.cs             # CREATE
│   └── SpExecutesqlConversionTests.cs     # CREATE
```

## Implementation Phases

### Phase 1: P1 — INSERT Metadata + sp_executesql (highest value)

**Task 1.1: INSERT Metadata Comments**
- Modify `ExpandInsertColumnsOperation.cs` to append `-- {TypeDisplay}, {nullable}` after each column
- Use existing `Column.TypeDisplay`, `IsNullable`, `DefaultValue`, `IsIdentity` from schema cache
- Add `InsertColumnsIncludeTypes` bool setting to `FormatterSettings`
- Skip identity/computed columns (or mark with `-- IDENTITY`)

**Task 1.2: Convert sp_executesql to Static SQL**
- Add `ConvertSpExecutesql = 16` to `FormatActionType` enum
- Create `ConvertSpExecutesqlOperation.cs` as new lightweight operation
- Parse `EXEC sp_executesql @template, @paramDefs, @param1=val1...`
- Extract template string, parse parameter definitions, substitute values
- Add to LightbulbProvider as a contextual action (detect sp_executesql on current line)

### Phase 2: P3 — Polish Features (low effort)

**Task 2.1: Ctrl Transparency**
- In `CompletionController.cs`, detect Ctrl key down/up in the Exec handler
- When Ctrl is held and popup is visible, set `_adornment.Popup.Opacity = 0.3`
- On release, restore to `1.0`
- Guard: don't trigger on Ctrl+Space chord

**Task 2.2: Tab Gradient**
- Add `GradientColors` bool to `TabSettings` (default false)
- In `TabColoringManager.cs`, when enabled, replace `SolidColorBrush` with `LinearGradientBrush` (top=lighter, bottom=base)

**Task 2.3: Excel 15+ Digit Precision**
- Add `ExcelLargeNumberAsText` bool to `GridSettings` (default true)
- In `GridExportService.cs`, when formatting int/bigint cells, check if value length > 15 digits
- If so, set cell DataType to Text instead of Number

### Phase 3: P4 — Split Table (high effort, deferred)

**Task 3.1: Split Table Operation**
- Create `SplitTableOperation.cs` extending `HeavyweightOperationBase`
- Input: table name, columns to move
- Preview: generates CREATE TABLE (new), ALTER TABLE (FK), INSERT INTO (data migration)
- Uses schema cache for FK dependency analysis
- Generates script opened in new tab (same pattern as Safe Rename)

**Task 3.2: Split Table Command**
- Create `SplitTableCommand.cs` in Shell
- Dialog to select columns to move
- Calls engine via RefactorPreview IPC
- Opens generated script in new tab

## Dependency Graph

```
Phase 1 (P1 — independent tasks):
  Task 1.1 (INSERT Metadata) ─── independent
  Task 1.2 (sp_executesql)  ─── independent

Phase 2 (P3 — independent tasks, can parallel with Phase 1):
  Task 2.1 (Ctrl Transparency) ─── independent
  Task 2.2 (Tab Gradient) ─────── independent
  Task 2.3 (Excel Precision) ──── independent

Phase 3 (P4 — depends on Phase 1 for pattern familiarity):
  Task 3.1 (Split Table Op) ───── independent
  Task 3.2 (Split Table Cmd) ──── depends on 3.1
```
