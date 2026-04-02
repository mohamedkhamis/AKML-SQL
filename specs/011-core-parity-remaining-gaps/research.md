# Research: SQL Prompt Core Parity — Remaining Gaps

**Date**: 2026-04-02 | **Branch**: `011-core-parity-remaining-gaps`

## Key Findings

### Copy with Headers — ALREADY IMPLEMENTED
Research revealed that `GridCopyAsMenu.cs` already includes column headers as the first row in ALL copy formats (CSV, TSV, JSON, XML, HTML, INSERT, Markdown). The `ExtractSelectedData()` method returns `(string[] Headers, List<string[]> Rows)` — headers are always included. **No work needed for FR-009/FR-010/FR-011.** US3 can be removed from scope.

### INSERT Metadata Comments
- `ExpandInsertColumnsOperation.cs` already uses schema cache via `context.SchemaCache.FindObject()`
- `Column` model has all needed fields: `TypeDisplay`, `IsNullable`, `IsIdentity`, `IsComputed`, `DefaultValue`
- Just need to append `-- {TypeDisplay}, {nullable}, {default}` comment after each column name

### sp_executesql Conversion
- No existing `FormatActionType` for this. Need new enum value (16).
- `ExpandExecParametersOperation` handles `ExecuteStatement` AST nodes — good reference pattern
- T-SQL ScriptDom parses `EXEC sp_executesql` into `ExecuteStatement` with parameters
- Need new lightweight operation to extract template string and substitute parameter values

### Excel 15+ Digit Precision
- `GridExportService.cs` already uses ClosedXML with type-aware cell formatting
- For 15+ digit numbers, need to set cell `DataType = XLDataType.Text` instead of numeric
- Add a `ExcelPrecisionAsText` bool to `GridSettings`

### Completion Popup Transparency
- `AkmlCompletionPopup.cs` is a WPF `Border` managed by `CompletionPopupAdornment`
- WPF `UIElement.Opacity` property can be toggled on Ctrl key state
- Need to detect Ctrl key state in `CompletionController` key handler

### Tab Gradient
- `TabColoringManager.cs` applies colors via tab header bar
- WPF `LinearGradientBrush` can replace `SolidColorBrush` for gradient effect
- Add `GradientColors` bool to `TabSettings`

### Split Table
- Heavyweight refactoring following existing pattern (`HeavyweightOperationBase`)
- Needs `SplitTableOperation.cs` in Engine + `SplitTableCommand.cs` in Shell
- Must query FK dependencies via `sys.foreign_keys` / schema cache
- High complexity — generates CREATE TABLE, ALTER TABLE (FK), INSERT INTO, ALTER dependent objects

## Revised Scope (6 gaps, not 7)

| Gap | Status | Effort |
|-----|--------|--------|
| INSERT metadata comments | Enhancement to existing operation | Low |
| Convert sp_executesql | New lightweight operation + enum value | Medium |
| ~~Copy with Headers~~ | **Already implemented** | None |
| Ctrl transparency | WPF opacity toggle | Low |
| Tab gradient | LinearGradientBrush swap | Low |
| Excel 15+ digit precision | Cell format override in export | Low |
| Split Table | New heavyweight operation | High |
