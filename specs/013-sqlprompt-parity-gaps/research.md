# Research: SQL Prompt Parity — Remaining Gaps

**Date**: 2026-04-03  
**Feature**: `013-sqlprompt-parity-gaps`

## Decision 1: Formatting Region Directives — Reuse Existing NoformatScanner

**Decision**: Extend the existing `NoformatScanner` to recognize `-- AKML formatting off/on` and `-- SQL Prompt formatting off/on` as aliases for the existing `-- noformat`/`-- endnoformat` syntax.

**Rationale**: A fully production-ready `NoformatScanner` already exists in `src/AkmlSql.Formatting/Pipeline/NoformatScanner.cs`. It supports:
- Line comments: `-- noformat` / `-- endnoformat`
- Block comments: `/* noformat */` / `/* endnoformat */`
- Nested region merging
- Unmatched open tags extend to EOF (fail-safe)
- 2-second regex timeout safety
- Full pipeline integration (LayoutNode.IsInNoformatRegion respected by all stages)

Adding alias patterns to the existing regex is a trivial change (~5 lines). No architectural work needed.

**Alternatives considered**:
- New pre-processing stage before NoformatScanner → Rejected: unnecessary duplication
- Token-level detection in FormatterPipeline → Rejected: NoformatScanner already handles this perfectly

## Decision 2: Unformat Command — Wire Existing Operation to Shell

**Decision**: Create a shell command (`UnformatCommand.cs`) that invokes the existing `UnformatOperation` via the `FormatAction` IPC message type.

**Rationale**: `UnformatOperation` already exists at `src/AkmlSql.Engine/Refactoring/Operations/Lightweight/UnformatOperation.cs`. It:
- Implements `ILightweightOperation`
- Collapses whitespace to single-line SQL
- Handles string literals, comments, SQLCMD directives
- Is already wired to `FormatRequestHandler.HandleFormatAction()` via `FormatActionType.Unformat = 17`

Shell needs: new command ID in `PackageGuids.cs`, new `UnformatCommand.cs` file, VSCT button registration, keyboard shortcut binding.

**Alternatives considered**:
- New formatting profile with minimal whitespace → Rejected: UnformatOperation already exists and is better suited
- Client-side regex stripping → Rejected: engine already handles edge cases (string literals, comments)

## Decision 3: History Advanced Search — Leverage FTS5 Native Syntax

**Decision**: Extend `HistorySearchParser` to translate user search syntax into FTS5 query syntax, and add post-query CamelCase filtering.

**Rationale**: SQLite FTS5 natively supports:
- `*` prefix/suffix wildcards (e.g., `Product*`)
- Boolean: `term1 OR term2`, `NOT term`
- Exact phrase: `"exact phrase"`
- AND is implicit (space-separated terms)

Current implementation wraps all search text in literal quotes, defeating FTS5 features. The fix is in `HistoryDatabase.SearchAsync` — pass the parsed query directly to FTS5 MATCH instead of wrapping in quotes.

CamelCase matching (`PC` → `ProductCategory`) requires post-FTS5 filtering since FTS5 doesn't support this natively. Apply `MatchesCamelCase()` (already exists in `CompletionItemModel`) as a post-filter on result SQL text.

**Alternatives considered**:
- Custom SQLite tokenizer for CamelCase → Rejected: high complexity, marginal benefit
- Full regex search outside FTS5 → Rejected: loses FTS5 performance on large history

## Decision 4: IntelliSense Icon Colors — SQL Prompt One Dark Palette

**Decision**: Update `CompletionItemModel.GetColor()` to use the SQL Prompt reference palette from `doc/SQL-PROMPT/SQL-Prompt-Features/SQL_Prompt_Features_Core.md`.

**Rationale**: Current colors use Material Design palette. SQL Prompt uses One Dark/Atom-inspired palette:

| Type | Current (Material) | Target (SQL Prompt) | Letter |
|------|-------------------|--------------------| ------|
| Table | #1565C0 (blue) | #E5C04B (yellow) | T |
| View | #2E7D32 (green) | #56B6C2 (teal) | V |
| Column | #F9A825 (gold) | #61AFEF (blue) | C |
| Keyword | #546E7A (blue-gray) | #ABB2BF (silver) | K |
| Snippet | #E65100 (orange) | #3DD68C (green) | S |
| Function | #AD1457 (magenta) | #D19A66 (orange) | F |
| Procedure | #6A1B9A (purple) | #C678DD (purple) | P |
| Schema | #616161 (gray) | #98C379 (green) | Sc |
| Database | #00695C (teal) | #E06C75 (red) | D |
| Variable | #00838F (cyan) | #56B6C2 (teal) | @ |
| Alias | #283593 (indigo) | #61AFEF (blue) | A |
| Parameter | #4E342E (brown) | #C678DD (purple) | P |

Additionally, badge rendering needs a semi-transparent background layer (20% opacity of the text color).

**Alternatives considered**:
- Keep Material Design for types not in SQL Prompt docs → Rejected: user chose full palette update (Clarification Q2)
- Theme-dependent icon colors → Rejected: SQL Prompt uses fixed colors on dark popup background

## Decision 5: Options Dialog Colors — Targeted Hex Value Updates

**Decision**: Update `ThemeBrushSet.Dark` and `ThemeBrushSet.Light` in `SettingsWindow.cs` to match the SQL Prompt specification.

**Rationale**: Current light theme uses #F5F5F5 main bg (spec says #F0F0F0), #CCE8FF selected (spec says #0078D4). Current dark uses #1E1E1E main bg (spec says #2D2D3B), #094771 selected. These are small hex value changes — no structural modification needed.

**Changes needed**:
- Light: Main #F5F5F5→#F0F0F0, Panel stays #FFFFFF, Selected #CCE8FF→#0078D4 (with white text), Border #E0E0E0→#CCCCCC
- Dark: Main #1E1E1E→#2D2D3B, Panel #2D2D30→#1E1E2E, Text secondary #888888→#8892A8, Border #3C3C3C→#3A3F4E

## Decision 6: History Search Highlighting — Extend Existing Implementation

**Decision**: Enhance `UpdatePreviewWithHighlighting()` in `HistoryToolWindowControl.cs` to support multi-term highlighting and use Yellow Ochre (#F9A825 at 30% opacity).

**Rationale**: Highlighting already exists using TextBlock.Inlines with Run objects. Current implementation does simple substring matching. Enhancement needed:
- Parse search text into individual terms (respecting quotes)
- Highlight each term independently
- Use #F9A825 at 30% opacity instead of current ThemeManager color

## Decision 7: Query Rename — Already Implemented

**Decision**: No new work needed. Rename is already functional.

**Rationale**: The History context menu already includes "Rename" (Action=6 in HistoryActionRequest). It shows an input dialog, sends rename via IPC, and persists to SQLite `tab_title` column. The only potential enhancement is making renamed queries searchable via the `name:` prefix — verify this works.

## Decision 8: Tab Color Propagation — DTE + Visual Tree Walking

**Decision**: Implement status bar coloring via DTE status bar API, and floating window borders via IVsWindowFrame visual tree walking.

**Rationale**: `TabColoringManager` already hooks `WindowActivated` events and has color extraction logic. Missing: actual visual application. The TODO stubs indicate the approach was planned but not implemented. WPF visual tree walking is needed to locate and modify the SSMS status bar and floating window chrome.

**Risk**: SSMS internal visual tree structure may vary between versions. Defensive coding with try/catch needed.

## Decision 9: Installer — AppMutex + Log Flag + Import Step

**Decision**: Add AppMutex for SSMS processes, enhance logging, and add post-install SQL Prompt import step.

**Rationale**:
- AppMutex: Inno Setup's `AppMutex` directive can detect running `Ssms.exe` processes
- Logging: Inno Setup already supports `/LOG` parameter natively — just needs documentation
- Import: `SqlPromptImporter` already exists with 99-option mapping. Add a Pascal Script function to detect SQL Prompt config directory and invoke the importer
