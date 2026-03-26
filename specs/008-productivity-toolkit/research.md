# Research: Productivity Toolkit

**Feature**: 008-productivity-toolkit | **Date**: 2026-03-24

## R1: Excel Export Library

**Decision**: Use ClosedXML for .xlsx export in the engine (.NET 10).

**Rationale**: ClosedXML is a mature, MIT-licensed library for creating .xlsx files without requiring Excel installation. It supports auto-column widths, styled headers, and streaming for large datasets. It runs on .NET 10 (engine-side) so it doesn't affect shell compatibility.

**Alternatives considered**:
- **EPPlus**: Powerful but has a commercial license (Polyform Noncommercial) for recent versions. Rejected for licensing concerns.
- **NPOI**: Apache-licensed but heavier and less ergonomic API. Rejected for complexity.
- **OpenXML SDK**: Microsoft's low-level library. Too verbose for simple export use cases.

## R2: Results Grid Access in SSMS

**Decision**: Hook into the SSMS results grid via the DTE automation model and SSMS-specific COM interop. The grid control is a standard .NET DataGridView accessible through the SSMS window hierarchy.

**Rationale**: SSMS 20/21/22 expose query results in a DataGridView control within the results pane. Access is via WPF visual tree walking from the active document window, similar to the tab coloring approach in Phase 7. The DataGridView provides DataSource access for reading cell values and selection events for aggregates.

**Alternatives considered**:
- **IVsOutputWindow**: Only for text output, not grid data. Rejected.
- **Results-to-text interception**: Parsing text output is fragile. Rejected.
- **Custom results pane replacement**: Too invasive. Rejected.

## R3: Statement Boundary Detection for Execute Current Statement

**Decision**: Use the existing TSql170Parser to parse the full script, then locate the statement containing the cursor offset using AST node positions.

**Rationale**: The TSql170Parser already produces a full AST with StartOffset and FragmentLength for each statement. The engine can accept a cursor offset and walk the AST to find the enclosing statement. This is more reliable than regex-based boundary detection and handles all SQL constructs correctly (nested blocks, CTEs, multi-line statements).

**Alternatives considered**:
- **Regex-based splitting**: Unreliable for complex SQL (nested blocks, strings containing keywords). Rejected.
- **GO-separator only**: Misses semicolon-delimited statements within a batch. Rejected.
- **Shell-side text scanning**: Would duplicate parsing logic. Rejected — keep in engine.

## R4: Go to Definition — Retrieving CREATE Scripts

**Decision**: Query `sys.sql_modules` for programmable objects (procedures, functions, views, triggers) and construct CREATE TABLE scripts from schema cache metadata for tables.

**Rationale**: `sys.sql_modules.definition` contains the full CREATE script for programmable objects. For tables, the schema cache already has column, index, FK, and constraint metadata that can be assembled into a CREATE TABLE script. This avoids the unreliable `sp_helptext` and works for all object types.

**Alternatives considered**:
- **OBJECT_DEFINITION() function**: Same as sys.sql_modules but as a scalar function. Equivalent, but sys.sql_modules allows batch retrieval.
- **sp_helptext**: Splits output into 255-char lines, harder to reassemble. Rejected.
- **SMO (SQL Server Management Objects)**: Heavy dependency, slow for single-object scripting. Rejected.

## R5: Command Palette Architecture

**Decision**: Implement as a shell-side WPF popup overlay (not a VS tool window) with a static command registry. No IPC needed — all command metadata is registered in-process.

**Rationale**: The Command Palette is purely a UI feature. All commands are registered in the shell process (OleMenuCommand instances from all phases). A WPF Popup overlay provides the centered, floating appearance (like VS Code's palette). Fuzzy search uses a simple character-subsequence matching algorithm with scoring. Usage counts are persisted in config.json.

**Alternatives considered**:
- **VS tool window**: Too heavy for a transient popup. Doesn't match the expected UX.
- **Engine-side command registry**: Unnecessary IPC round-trip for a local UI operation.
- **Windows Forms dialog**: Less flexible for the floating overlay UX needed.

## R6: Document Outline Architecture

**Decision**: Implement as a shell-side VS tool window with engine-side AST parsing. The shell sends the script text to the engine, the engine returns an outline tree, and the shell displays it in a TreeView.

**Rationale**: The engine already has TSql170Parser. Parsing the script in the engine avoids duplicating the parser in the shell (.NET Framework 4.7.2). The outline tree is a lightweight data structure (name, type, line number, children) that serializes efficiently via MessagePack. Auto-refresh uses the existing buffer change debounce pattern (300ms, like analysis).

**Alternatives considered**:
- **Shell-side parsing**: Would require referencing ScriptDom in the shell (netfx 4.7.2). Rejected — keep parsing in engine.
- **Regex-based outline**: Unreliable for nested structures. Rejected.

## R7: Editor Adornments (Highlight Occurrences, Bracket Matching, Sticky Scroll, Minimap)

**Decision**: Implement as MEF ITagger providers following the existing DiagnosticTagger pattern. Each feature gets its own tagger. Sticky scroll and minimap use IAdornmentLayer for visual overlays.

**Rationale**: The existing codebase has established MEF patterns for editor integration (DiagnosticTagger, CompletionSource, QuickInfoSource). New taggers follow the same content-type filtering (`[ContentType("T-SQL")]`) and property-based lifecycle management. Bracket matching and occurrence highlighting are purely text-analysis features that can run in the shell using simple text scanning (no AST needed for basic keyword matching).

**Alternatives considered**:
- **Engine-side highlighting**: Unnecessary IPC overhead for what is essentially a text-matching operation. Rejected.
- **Custom WPF overlay for highlights**: More complex than ITagger approach. Rejected.

## R8: Multi-Database Execution

**Decision**: Execute in the shell using parallel `SqlConnection` instances to each selected database. Results are displayed in tabbed result panes labeled by database name.

**Rationale**: Multi-database execution is a shell-side feature because each database needs its own connection. The shell opens N connections to the same server with different initial catalogs, executes the script on each in parallel, and collects results. This doesn't require engine involvement since the connections are managed by the shell.

**Alternatives considered**:
- **Engine-side execution**: Would require the engine to manage multiple SQL connections. Rejected — engine is for parsing/analysis, not query execution.
- **Sequential execution**: Too slow for multiple databases. Rejected.

## R9: Windows Toast Notifications

**Decision**: Use the Windows `ToastNotificationManager` API (available in .NET Framework 4.7.2 via Windows.UI.Notifications interop).

**Rationale**: Windows 10/11 toast notifications are the standard way to alert users of completed tasks. The API is accessible from .NET Framework 4.7.2 via COM interop or the Microsoft.Toolkit.Uwp.Notifications NuGet package. Notifications include the query duration, row count, and status.

**Alternatives considered**:
- **System tray balloon tips**: Deprecated on Windows 10+. Rejected.
- **Custom popup window**: Non-standard UX, doesn't integrate with Windows notification center. Rejected.

## R10: Find All References Implementation

**Decision**: Query `sys.sql_expression_dependencies` and `sys.dm_sql_referencing_entities` to find all objects referencing a given entity.

**Rationale**: SQL Server's dependency tracking DMVs provide comprehensive reference information. `sys.sql_expression_dependencies` tracks cross-object references. This is engine-side (requires SQL connection) and returns a list of referencing objects with their types and schema names.

**Alternatives considered**:
- **Full-text search of sys.sql_modules**: Grep-style search for object names in all module definitions. Less accurate (string matching vs. dependency tracking) but catches dynamic SQL references. Could be used as a supplement.
- **Shell-side file search**: Only searches open files, misses database-side references. Rejected as primary approach.
