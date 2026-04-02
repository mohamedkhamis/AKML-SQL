# Tasks: Productivity Toolkit

**Input**: Design documents from `/specs/008-productivity-toolkit/`
**Prerequisites**: plan.md (required), spec.md (required), research.md, data-model.md, contracts/

**Organization**: Tasks are grouped by user story to enable independent implementation and testing of each story.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (e.g., US1, US2, US3)
- Include exact file paths in descriptions

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Add new dependencies and create directory structure for Phase 8 components

- [x] T001 Add `ClosedXML` package reference to `src/AkmlSql.Engine/AkmlSql.Engine.csproj`
- [x] T002 [P] Create directory structure: `src/AkmlSql.Core/Models/Productivity/`, `src/AkmlSql.Core/Models/Navigation/` for new model classes
- [x] T003 [P] Create directory structure: `src/AkmlSql.Engine/Navigation/`, `src/AkmlSql.Engine/Productivity/`, `src/AkmlSql.Engine/Export/` for engine handlers
- [x] T004 [P] Create directory structure: `src/AkmlSql.Shell.Shared/Productivity/CommandPalette/`, `src/AkmlSql.Shell.Shared/Productivity/DocumentOutline/`, `src/AkmlSql.Shell.Shared/Productivity/Grid/`, `src/AkmlSql.Shell.Shared/Productivity/Navigation/`, `src/AkmlSql.Shell.Shared/Execution/` for shell components
- [x] T005 [P] Create directory structure: `src/AkmlSql.Shell.Shared/Editor/` already exists — verify ready for new tagger files

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Core infrastructure that MUST be complete before ANY user story can be implemented

**⚠️ CRITICAL**: No user story work can begin until this phase is complete

- [x] T006 Add new IPC message type constants to `src/AkmlSql.Core/Ipc/RpcMessage.cs`: Navigation (60-62, 160-162), Editor/Productivity (64-68, 164-168) per contracts
- [x] T007 Add `GridSettings` nested class to `src/AkmlSql.Core/Config/AppSettings.cs` with properties: FindShortcut ("Ctrl+F"), Aggregates (true), NullHighlight (true), RowNumbers (false), FreezeHeaders (true)
- [x] T008 [P] Add `EditorProductivitySettings` nested class to `src/AkmlSql.Core/Config/AppSettings.cs` with: CommandPaletteShortcut ("Ctrl+Shift+P"), HighlightOccurrences (true), BracketMatching (true), NamedRegions (true), StickyScroll (true), Minimap (false), DocumentOutline (true)
- [x] T009 [P] Add `ExecutionProductivitySettings` nested class to `src/AkmlSql.Core/Config/AppSettings.cs` with: CurrentStatementShortcut ("Alt+Enter"), NotificationThreshold (30), ShowExecutionTimer (true), MultiDatabase (true)
- [x] T010 [P] Add `NavigationSettings` nested class to `src/AkmlSql.Core/Config/AppSettings.cs` with: GoToDefinition (true), PeekDefinition (true), FindReferences (true), ObjectSearch (true), ConnectionAliases (List<ConnectionAliasEntry>)
- [x] T011 [P] Add `CommandPaletteSettings` nested class with UsageCounts (Dictionary<string, int>) to `src/AkmlSql.Core/Config/AppSettings.cs`
- [x] T012 Add `Grid`, `EditorProductivity`, `ExecutionProductivity`, `Navigation`, `CommandPalette` properties to root `AppSettings` class in `src/AkmlSql.Core/Config/AppSettings.cs`
- [x] T013 [P] Create `GridExportFormat` enum (Csv=0, Tsv=1, Json=2, Xml=3, Xlsx=4, Html=5, SqlInsert=6, Markdown=7) in `src/AkmlSql.Core/Models/Productivity/GridExportFormat.cs`
- [x] T014 [P] Create `CommandEntry` model in `src/AkmlSql.Core/Models/Productivity/CommandEntry.cs` with: Id (string), Name (string), Category (string), KeyboardShortcut (string?), UsageCount (int), LastUsed (DateTime?)
- [x] T015 [P] Create `DocumentOutlineNode` model in `src/AkmlSql.Core/Models/Productivity/DocumentOutlineNode.cs` with: Name, NodeType (enum: Procedure, Function, CTE, TempTable, Statement, Region, Block, Trigger, View), StartLine, StartOffset, EndOffset, NestingLevel, Children (List)
- [x] T016 [P] Create `StatementRange` model in `src/AkmlSql.Core/Models/Productivity/StatementRange.cs` with: StartOffset, EndOffset, StartLine, EndLine, StatementType (string)
- [x] T017 [P] Create `ConnectionAlias` model in `src/AkmlSql.Core/Models/Productivity/ConnectionAlias.cs` with: ServerName (string), Alias (string)
- [x] T018 [P] Create `ObjectReference` model in `src/AkmlSql.Core/Models/Navigation/ObjectReference.cs` with: ReferencingObjectSchema, ReferencingObjectName, ReferencingObjectType, ReferenceLine
- [x] T019 Register new command IDs in `src/AkmlSql.Shell.Shared/PackageGuids.cs` for: CommandPalette, ExecuteCurrentStatement, ExecuteToCursor, GoToDefinition, PeekDefinition, FindReferences, ObjectSearch, NavigateNextStatement, NavigatePrevStatement, NavigateMatchingPair, GridFind, GridExport, CrudGeneration
- [x] T020 Add VSCT entries (Buttons, Groups, KeyBindings) to all 6 target VSCT files for: Ctrl+Shift+P (Command Palette), Alt+Enter (Execute Current Statement), F12 (Go to Definition), Alt+F12 (Peek Definition), Shift+F12 (Find References), Ctrl+T (Object Search), Ctrl+PageUp/Down (Navigate Statements), Ctrl+] (Navigate Matching Pair)

**Checkpoint**: Foundation ready — user story implementation can now begin in parallel

---

## Phase 3: User Story 1 — Results Grid Search and Aggregates (Priority: P1) 🎯 MVP

**Goal**: Ctrl+F in results grid for search/highlight; cell selection shows SUM/AVG/COUNT/MIN/MAX in status bar

**Independent Test**: Execute query, Ctrl+F in grid, search text, verify highlights. Select numeric cells, verify aggregates in status bar.

### Implementation for User Story 1

- [x] T021 [US1] Create `src/AkmlSql.Shell.Shared/Productivity/Grid/GridAccessHelper.cs` — utility to locate and access the SSMS DataGridView from the active document window via WPF visual tree walking, provide methods to read cell values, column headers, and selection state
- [x] T022 [US1] Create `src/AkmlSql.Shell.Shared/Productivity/Grid/GridFindBar.cs` — WPF UserControl overlaid on the results grid: search TextBox, regex toggle, match count label, next/previous buttons; on text change: scan all DataGridView cells for matches, highlight matching cells with background color, update match count; F3 for next, Shift+F3 for previous
- [x] T023 [US1] Create `src/AkmlSql.Shell.Shared/Productivity/Grid/GridAggregatesProvider.cs` — subscribe to DataGridView.SelectionChanged event, compute SUM/AVG/COUNT/MIN/MAX for selected cells, display in status bar via StatusBarManager; show COUNT only for non-numeric selections
- [x] T024 [US1] Create `src/AkmlSql.Shell.Shared/Commands/GridCommands.cs` — OleMenuCommand for CmdGridFind bound to Ctrl+F (in grid context), toggles GridFindBar visibility; BeforeQueryStatus enables only when results grid has focus
- [x] T025 [US1] Wire GridAggregatesProvider.Initialize() and GridFindBar setup in AkmlSqlPackage initialization, guarded by GridSettings.Aggregates and GridSettings.FindShortcut

**Checkpoint**: Grid search and aggregates functional. MVP delivers immediate value.

---

## Phase 4: User Story 2 — Copy and Export Results Data (Priority: P1)

**Goal**: Right-click Copy As (CSV/JSON/XML/INSERT/HTML), Export to Excel/file, Generate Script from rows

**Independent Test**: Right-click grid rows, Copy As > JSON, verify valid JSON. Export to Excel, verify .xlsx.

### Implementation for User Story 2

- [x] T026 [P] [US2] Create all IPC message POCOs for grid export in `src/AkmlSql.Core/Ipc/Messages/GridExportRequest.cs` and `GridExportResponse.cs` per contracts/grid-ipc.md (optional engine-side .xlsx)
- [x] T027 [US2] Create `src/AkmlSql.Shell.Shared/Productivity/Grid/GridCopyAsMenu.cs` — register context menu items on the DataGridView: "Copy As > CSV", "Copy As > TSV", "Copy As > JSON", "Copy As > XML", "Copy As > HTML Table", "Copy As > INSERT Statements"; each formatter reads selected rows and formats to clipboard
- [x] T028 [US2] Create `src/AkmlSql.Shell.Shared/Productivity/Grid/GridExportManager.cs` — handle "Export to File" commands: show SaveFileDialog with format filter, stream grid data to file in CSV/JSON/XML/Markdown/SQL INSERT formats; for XLSX: send GridExportRequest to engine or use shell-side ClosedXML if available
- [x] T029 [US2] Create `src/AkmlSql.Engine/Export/GridExportService.cs` — engine-side .xlsx generation using ClosedXML: accept column headers, rows, and types; create workbook with auto-column widths, styled headers, proper data types; stream to output path
- [x] T030 [US2] Register GridExport (68) handler in `src/AkmlSql.Engine/Server/PipeRpcServer.cs` routing to GridExportService
- [x] T031 [US2] Create `src/AkmlSql.Shell.Shared/Productivity/Grid/GridScriptGenerator.cs` — "Generate Script > INSERT/UPDATE/DELETE" from selected rows: read row data + column metadata + primary key info from schema cache, generate parameterized SQL scripts, open in new editor tab
- [x] T032 [US2] Wire context menu and export commands in AkmlSqlPackage initialization

**Checkpoint**: Full data export pipeline working — CSV, JSON, XML, Excel, Markdown, SQL INSERT, and script generation.

---

## Phase 5: User Story 3 — Command Palette (Priority: P1)

**Goal**: Ctrl+Shift+P opens searchable command launcher with fuzzy match, shortcut hints, and frequency ranking

**Independent Test**: Press Ctrl+Shift+P, type "form", verify "Format SQL" appears. Select and verify execution.

### Implementation for User Story 3

- [x] T033 [US3] Create `src/AkmlSql.Shell.Shared/Productivity/CommandPalette/CommandRegistry.cs` — static class that scans all registered OleMenuCommand instances from Phases 1–8 and common DTE commands, builds a List<CommandEntry> with names, categories, shortcuts; provides Refresh() to rebuild on command registration changes
- [x] T034 [US3] Create `src/AkmlSql.Shell.Shared/Productivity/CommandPalette/FuzzyMatcher.cs` — character-subsequence fuzzy matching algorithm: score based on consecutive matches, word boundary bonuses, prefix match bonuses; return sorted results
- [x] T035 [US3] Create `src/AkmlSql.Shell.Shared/Productivity/CommandPalette/CommandPaletteViewModel.cs` — INotifyPropertyChanged ViewModel: SearchText property, FilteredCommands (ObservableCollection<CommandEntry>), SelectedCommand; on SearchText change: fuzzy match + sort by (0.7 * usageFrequency + 0.3 * matchScore); on execute: invoke command, increment UsageCount, persist to config
- [x] T036 [US3] Create `src/AkmlSql.Shell.Shared/Productivity/CommandPalette/CommandPaletteWindow.cs` — WPF Popup overlay centered on SSMS main window: TextBox at top, ListView below with command name (left) and shortcut hint (right), keyboard navigation (Up/Down/Enter/Escape), dismiss on Escape or focus loss
- [x] T037 [US3] Create `src/AkmlSql.Shell.Shared/Commands/CommandPaletteCommand.cs` — OleMenuCommand for CmdCommandPalette bound to Ctrl+Shift+P, shows/hides CommandPaletteWindow
- [x] T038 [US3] Wire CommandPaletteCommand.Initialize() and CommandRegistry.Refresh() in AkmlSqlPackage initialization across all 6 targets

**Checkpoint**: Command Palette fully functional — fuzzy search, frequency ranking, shortcut hints. All 3 P1 stories complete.

---

## Phase 6: User Story 4 — Execute Current Statement (Priority: P2)

**Goal**: Alt+Enter executes only the SQL statement at cursor position

**Independent Test**: Script with 3 SELECTs, cursor in 2nd, Alt+Enter, verify only 2nd executes.

### Implementation for User Story 4

- [x] T039 [P] [US4] Create `StatementBoundaryRequest`/`StatementBoundaryResponse` IPC POCOs in `src/AkmlSql.Core/Ipc/Messages/StatementBoundaryRequest.cs` and `StatementBoundaryResponse.cs` per contracts/editor-ipc.md
- [x] T040 [US4] Create `src/AkmlSql.Engine/Productivity/StatementBoundaryDetector.cs` — parse SQL with TsqlParserService, walk the TSqlScript.Batches[].Statements[] AST, return StatementRange for the statement enclosing the given cursor offset; also support AllStatements mode returning all statement ranges for Ctrl+PageUp/Down
- [x] T041 [US4] Create `src/AkmlSql.Engine/Productivity/ProductivityRequestHandler.cs` — handle StatementBoundary (65): deserialize request, call StatementBoundaryDetector, return response
- [x] T042 [US4] Register StatementBoundary (65) handler in `src/AkmlSql.Engine/Server/PipeRpcServer.cs`
- [x] T043 [US4] Create `src/AkmlSql.Shell.Shared/Commands/ExecuteCurrentStatementCommand.cs` — OleMenuCommand for Alt+Enter: get cursor offset from IVsTextView, send StatementBoundaryRequest to engine, receive statement range, select that text range in the editor, invoke SSMS Execute (F5) on the selection
- [x] T044 [US4] Create `src/AkmlSql.Shell.Shared/Commands/ExecuteToCursorCommand.cs` — OleMenuCommand: get cursor offset, send StatementBoundaryRequest (AllStatements=true), select from offset 0 to cursor statement's EndOffset, invoke SSMS Execute
- [x] T045 [US4] Wire ExecuteCurrentStatementCommand.Initialize() and ExecuteToCursorCommand.Initialize() in AkmlSqlPackage

**Checkpoint**: Alt+Enter correctly identifies and executes the single statement at cursor position.

---

## Phase 7: User Story 5 — Document Outline (Priority: P2)

**Goal**: Dockable panel showing script structure (procedures, CTEs, temp tables) with click-to-navigate

**Independent Test**: Open large script, open outline, verify tree structure, click to navigate.

### Implementation for User Story 5

- [x] T046 [P] [US5] Create `DocumentOutlineRequest`/`DocumentOutlineResponse` and `OutlineNodeDto` IPC POCOs in `src/AkmlSql.Core/Ipc/Messages/DocumentOutlineRequest.cs` and `DocumentOutlineResponse.cs` per contracts/editor-ipc.md
- [x] T047 [US5] Create `src/AkmlSql.Engine/Productivity/DocumentOutlineBuilder.cs` — parse SQL with TsqlParserService, walk AST to build tree of OutlineNodeDto: detect CREATE PROCEDURE/FUNCTION/VIEW/TRIGGER (top-level), CTEs (WITH clause), temp tables (#table CREATE), BEGIN...END blocks, --region/--endregion markers, major statement types
- [x] T048 [US5] Add DocumentOutline (64) handler to ProductivityRequestHandler in `src/AkmlSql.Engine/Productivity/ProductivityRequestHandler.cs`
- [x] T049 [US5] Register DocumentOutline (64) handler in `src/AkmlSql.Engine/Server/PipeRpcServer.cs`
- [x] T050 [US5] Create `src/AkmlSql.Shell.Shared/Productivity/DocumentOutline/DocumentOutlineViewModel.cs` — INotifyPropertyChanged ViewModel with RootNodes (ObservableCollection<OutlineNodeDto>), SelectedNode; on text buffer change (debounced 300ms): send DocumentOutlineRequest, update tree
- [x] T051 [US5] Create `src/AkmlSql.Shell.Shared/Productivity/DocumentOutline/DocumentOutlineControl.cs` — programmatic WPF UserControl with TreeView bound to ViewModel, node icons by type, click handler that scrolls editor to StartLine
- [x] T052 [US5] Create `src/AkmlSql.Shell.Shared/Productivity/DocumentOutline/DocumentOutlineToolWindow.cs` — ToolWindowPane hosting DocumentOutlineControl, Caption="Document Outline"
- [x] T053 [US5] Wire DocumentOutlineToolWindow in AkmlSqlPackage with ProvideToolWindow attribute and menu command

**Checkpoint**: Document Outline panel shows script structure and enables click-to-navigate.

---

## Phase 8: User Story 6 — Highlight Occurrences and Bracket Matching (Priority: P2)

**Goal**: Click identifier → all occurrences highlighted; cursor on BEGIN → matching END highlighted

**Independent Test**: Click @variable, verify all occurrences highlight. Cursor on BEGIN, verify matching END.

### Implementation for User Story 6

- [x] T054 [P] [US6] Create `src/AkmlSql.Shell.Shared/Editor/OccurrenceHighlightTagger.cs` — MEF ITagger<ITextMarkerTag> export with [ContentType("T-SQL")]: on caret position change, extract word at caret, find all occurrences in buffer text (case-insensitive whole-word match), emit TextMarkerTag spans for each occurrence; debounce 150ms
- [x] T055 [P] [US6] Create `src/AkmlSql.Shell.Shared/Editor/OccurrenceHighlightTaggerProvider.cs` — MEF IViewTaggerProvider export that creates OccurrenceHighlightTagger per view, guarded by EditorProductivitySettings.HighlightOccurrences
- [x] T056 [US6] Create `src/AkmlSql.Shell.Shared/Editor/BracketMatchingTagger.cs` — MEF ITagger<ITextMarkerTag> export with [ContentType("T-SQL")]: on caret position change, check if caret is on a bracket keyword (BEGIN/END, CASE/END, TRY/CATCH, parentheses, IF/ELSE); find matching pair(s) using a stack-based scanner; emit highlight tags for all matched positions
- [x] T057 [US6] Create `src/AkmlSql.Shell.Shared/Editor/BracketMatchingTaggerProvider.cs` — MEF IViewTaggerProvider export, guarded by EditorProductivitySettings.BracketMatching
- [x] T058 [US6] Register new tagger files in `src/AkmlSql.Shell.Shared/AkmlSql.Shell.Shared.projitems`

**Checkpoint**: Identifier occurrences and bracket pairs visually highlighted in the editor.

---

## Phase 9: User Story 7 — Go to Definition and Peek Definition (Priority: P2)

**Goal**: F12 navigates to CREATE script; Alt+F12 shows inline preview

**Independent Test**: F12 on table name → CREATE script in new tab. Alt+F12 on procedure → inline panel.

### Implementation for User Story 7

- [x] T059 [P] [US7] Create `GetObjectDefinitionRequest`/`GetObjectDefinitionResponse` IPC POCOs in `src/AkmlSql.Core/Ipc/Messages/GetObjectDefinitionRequest.cs` and `GetObjectDefinitionResponse.cs` per contracts/navigation-ipc.md
- [x] T060 [US7] Create `src/AkmlSql.Engine/Navigation/ObjectDefinitionService.cs` — for programmable objects: query `sys.sql_modules WHERE object_id = OBJECT_ID(@name)` to get definition; for tables: build CREATE TABLE script from schema cache metadata (columns, indexes, FKs, constraints); for views: query sys.sql_modules
- [x] T061 [US7] Create `src/AkmlSql.Engine/Navigation/NavigationRequestHandler.cs` — handle GetObjectDefinition (60): deserialize request, call ObjectDefinitionService, return response with CREATE script and object type
- [x] T062 [US7] Register GetObjectDefinition (60) handler in `src/AkmlSql.Engine/Server/PipeRpcServer.cs`
- [x] T063 [US7] Create `src/AkmlSql.Shell.Shared/Commands/GoToDefinitionCommand.cs` — OleMenuCommand for F12: extract identifier at cursor (word under caret), send GetObjectDefinitionRequest, open result in new editor tab with SQL syntax
- [x] T064 [US7] Create `src/AkmlSql.Shell.Shared/Productivity/Navigation/PeekDefinitionControl.cs` — WPF UserControl for inline peek: scrollable text view showing CREATE script, dismiss on Escape, resize handle; displayed as editor adornment below the current line
- [x] T065 [US7] Create `src/AkmlSql.Shell.Shared/Commands/PeekDefinitionCommand.cs` — OleMenuCommand for Alt+F12: extract identifier, send GetObjectDefinitionRequest(PeekOnly=true), show PeekDefinitionControl inline
- [x] T066 [US7] Wire GoToDefinitionCommand.Initialize() and PeekDefinitionCommand.Initialize() in AkmlSqlPackage

**Checkpoint**: F12 and Alt+F12 work for tables, views, procedures, and functions.

---

## Phase 10: User Story 8 — Named Regions and Navigate Between Queries (Priority: P3)

**Goal**: --region/--endregion collapsible; Ctrl+PageUp/Down jumps between statements; Ctrl+] jumps to matching pair

**Independent Test**: Add --region, verify collapsible. Ctrl+PageDown jumps to next statement.

### Implementation for User Story 8

- [x] T067 [P] [US8] Create `src/AkmlSql.Shell.Shared/Editor/RegionTagger.cs` — MEF ITagger<IOutliningRegionTag> export with [ContentType("T-SQL")]: scan buffer for `--region\s+(.+)` / `--endregion` comment patterns, emit IOutliningRegionTag for each matched pair with the region name as collapsed text; handle nested regions
- [x] T068 [P] [US8] Create `src/AkmlSql.Shell.Shared/Editor/RegionTaggerProvider.cs` — MEF ITaggerProvider export, guarded by EditorProductivitySettings.NamedRegions
- [x] T069 [US8] Create `src/AkmlSql.Shell.Shared/Commands/NavigateStatementCommand.cs` — two OleMenuCommands for Ctrl+PageDown (next statement) and Ctrl+PageUp (previous statement): send StatementBoundaryRequest(AllStatements=true) to engine, find the statement boundary after/before current cursor offset, move caret to that position
- [x] T070 [US8] Create `src/AkmlSql.Shell.Shared/Commands/NavigateMatchingPairCommand.cs` — OleMenuCommand for Ctrl+]: read word at caret, if it's a bracket keyword (BEGIN, END, CASE, TRY, CATCH, open/close paren), use BracketMatchingTagger's matching logic to find the pair, move caret to the matched position
- [x] T071 [US8] Wire NavigateStatementCommand.Initialize() and NavigateMatchingPairCommand.Initialize() in AkmlSqlPackage; register region tagger in projitems

**Checkpoint**: Named regions collapse, statement navigation works, Ctrl+] jumps to matching pair.

---

## Phase 11: User Story 9 — Grid Advanced Features (Priority: P3)

**Goal**: Column statistics, transpose results, NULL highlighting, row numbers, cell editing

**Independent Test**: Right-click column header → Column Statistics popup. NULL cells visually distinct.

### Implementation for User Story 9

- [x] T072 [US9] Create `src/AkmlSql.Shell.Shared/Productivity/Grid/ColumnStatisticsPopup.cs` — WinForms popup shown on column header right-click: compute min, max, average, distinct count, null count from DataGridView column data; display in a compact formatted panel
- [x] T073 [US9] Create `src/AkmlSql.Shell.Shared/Productivity/Grid/TransposeResultsView.cs` — WinForms dialog showing single-row result as label-value pairs (column name → value) in a two-column grid, useful for inspecting wide single-row results
- [x] T074 [US9] Create `src/AkmlSql.Shell.Shared/Productivity/Grid/NullHighlighter.cs` — hook into DataGridView.CellFormatting event: when cell value is DBNull, display italic "NULL" text with distinct background color (configurable); distinguish from empty string ("")
- [x] T075 [US9] Create `src/AkmlSql.Shell.Shared/Productivity/Grid/RowNumberProvider.cs` — add a virtual "Row #" column as the first column in the DataGridView with sequential numbers (1-based); toggled by GridSettings.RowNumbers
- [x] T076 [US9] Create `src/AkmlSql.Shell.Shared/Productivity/Grid/CellEditDialog.cs` — WinForms dialog for inline cell editing: show current value, allow modification, on confirm: generate UPDATE statement using table name and primary key from schema cache, show generated SQL for user confirmation, execute if confirmed
- [x] T077 [US9] Wire all grid advanced features in AkmlSqlPackage initialization, guarded by individual settings flags

**Checkpoint**: Grid is now a full data analysis tool with stats, transpose, null highlighting, and cell editing.

---

## Phase 12: User Story 10 — Multi-Database Execution (Priority: P3)

**Goal**: Execute same script against multiple databases simultaneously with comparison view

**Independent Test**: Select 3 databases, execute query, verify 3 sets of results labeled by database.

### Implementation for User Story 10

- [x] T078 [US10] Create `src/AkmlSql.Shell.Shared/Execution/MultiDatabaseSelector.cs` — WinForms dialog listing all databases on the current server (via schema cache or sys.databases query) with checkboxes for selection; return selected database names
- [x] T079 [US10] Create `src/AkmlSql.Shell.Shared/Execution/MultiDatabaseExecutor.cs` — open parallel SqlConnection instances to each selected database (same server, different InitialCatalog), execute script on all in parallel using Task.WhenAll, collect results (DataTable or error per database)
- [x] T080 [US10] Create `src/AkmlSql.Shell.Shared/Execution/MultiDatabaseResultsView.cs` — WinForms tabbed panel showing results per database: one tab per database with its DataGridView, plus a summary tab showing row counts and status per database
- [x] T081 [US10] Wire multi-database execution command in AkmlSqlPackage, guarded by ExecutionProductivitySettings.MultiDatabase

**Checkpoint**: Multi-database execution with per-database result tabs.

---

## Phase 13: User Story 11 — Execution Notifications and Timer (Priority: P3)

**Goal**: Live elapsed time in status bar during execution; Windows toast on long-query completion

**Independent Test**: Execute query, verify live timer. Set threshold to 5s, run 6s query, verify toast.

### Implementation for User Story 11

- [x] T082 [US11] Create `src/AkmlSql.Shell.Shared/Execution/ExecutionTimerManager.cs` — subscribe to query execution start/end events (share hooks with ExecutionCapture from Phase 7), on start: begin 1-second timer updating status bar with "Executing... HH:MM:SS"; on end: show final duration; clear on new execution
- [x] T083 [US11] Create `src/AkmlSql.Shell.Shared/Execution/CompletionNotifier.cs` — on query completion: if duration exceeds ExecutionProductivitySettings.NotificationThreshold, send Windows toast notification via ToastNotificationManager COM interop showing "Query completed in Xs — N rows returned" or error summary
- [x] T084 [US11] Wire ExecutionTimerManager.Initialize() and CompletionNotifier.Initialize() in AkmlSqlPackage

**Checkpoint**: Live timer visible during execution; toast notifications for long queries.

---

## Phase 14: User Story 12 — Object Search and Find All References (Priority: P3)

**Goal**: Ctrl+T quick object search; Shift+F12 lists all referencing objects

**Independent Test**: Ctrl+T, type "Ord", verify matching objects. Shift+F12 on table, verify references.

### Implementation for User Story 12

- [x] T085 [P] [US12] Create `FindReferencesRequest`/`FindReferencesResponse` and `ObjectReferenceDto` IPC POCOs in `src/AkmlSql.Core/Ipc/Messages/FindReferencesRequest.cs` and `FindReferencesResponse.cs` per contracts/navigation-ipc.md
- [x] T086 [P] [US12] Create `ObjectSearchRequest`/`ObjectSearchResponse` and `ObjectSearchResultDto` IPC POCOs in `src/AkmlSql.Core/Ipc/Messages/ObjectSearchRequest.cs` and `ObjectSearchResponse.cs` per contracts/navigation-ipc.md
- [x] T087 [US12] Create `src/AkmlSql.Engine/Navigation/ReferenceCollector.cs` — query `sys.sql_expression_dependencies` and `sys.dm_sql_referencing_entities` to find all objects referencing a given entity; return list of ObjectReferenceDto
- [x] T088 [US12] Add FindReferences (61) and ObjectSearch (62) handlers to `src/AkmlSql.Engine/Navigation/NavigationRequestHandler.cs` — FindReferences calls ReferenceCollector; ObjectSearch queries schema cache with fuzzy name matching
- [x] T089 [US12] Register FindReferences (61) and ObjectSearch (62) in `src/AkmlSql.Engine/Server/PipeRpcServer.cs`
- [x] T090 [US12] Create `src/AkmlSql.Shell.Shared/Productivity/Navigation/ObjectSearchWindow.cs` — WPF Popup overlay (similar to Command Palette): text input, fuzzy search results showing object name + type icon, Enter navigates to definition (reuses GoToDefinitionCommand logic)
- [x] T091 [US12] Create `src/AkmlSql.Shell.Shared/Productivity/Navigation/ReferencesPanel.cs` — VS tool window showing references list: object name, type, schema; click navigates to definition at reference line
- [x] T092 [US12] Create `src/AkmlSql.Shell.Shared/Commands/ObjectSearchCommand.cs` (Ctrl+T) and `FindReferencesCommand.cs` (Shift+F12) — wire to ObjectSearchWindow and ReferencesPanel
- [x] T093 [US12] Wire all navigation commands in AkmlSqlPackage

**Checkpoint**: Object search and Find All References fully functional.

---

## Phase 15: User Story 13 — Sticky Scroll and Minimap (Priority: P3)

**Goal**: Procedure context stays visible at top; compact code overview in right margin

**Independent Test**: Scroll past CREATE PROCEDURE, verify name stays pinned. Enable minimap, verify overview.

### Implementation for User Story 13

- [x] T094 [US13] Create `src/AkmlSql.Shell.Shared/Editor/StickyScrollAdornment.cs` — MEF IWpfTextViewCreationListener + IAdornmentLayer: on scroll change, check if any containing scope (CREATE PROCEDURE, BEGIN...END, IF) start line has scrolled out of view; if so, render a sticky header showing the scope chain; clickable to scroll to the definition
- [x] T095 [US13] Create `src/AkmlSql.Shell.Shared/Editor/StickyScrollAdornmentProvider.cs` — MEF export with [AdornmentLayerDefinition], guarded by EditorProductivitySettings.StickyScroll
- [x] T096 [US13] Create `src/AkmlSql.Shell.Shared/Editor/MinimapAdornment.cs` — MEF IWpfTextViewCreationListener + IAdornmentLayer: render a compact ~100px wide overview of the entire script in the right margin using syntax-colored text rendering; highlight the visible viewport region; click scrolls to position
- [x] T097 [US13] Create `src/AkmlSql.Shell.Shared/Editor/MinimapAdornmentProvider.cs` — MEF export with [AdornmentLayerDefinition], guarded by EditorProductivitySettings.Minimap
- [x] T098 [US13] Register all adornment files in projitems

**Checkpoint**: Sticky scroll and minimap provide visual orientation for large scripts.

---

## Phase 16: User Story 14 — CRUD Generation and Script As (Priority: P3)

**Goal**: Right-click table → Generate CRUD Procedures; Script As CREATE/INSERT/SELECT/MERGE/BCP

**Independent Test**: Right-click table, Generate CRUD, verify 4 procedures in new tab.

### Implementation for User Story 14

- [x] T099 [P] [US14] Create `CrudGenerationRequest`/`CrudGenerationResponse` and `ScriptAsRequest`/`ScriptAsResponse` IPC POCOs in `src/AkmlSql.Core/Ipc/Messages/CrudGenerationRequest.cs`, `CrudGenerationResponse.cs`, `ScriptAsRequest.cs`, `ScriptAsResponse.cs` per contracts/editor-ipc.md
- [x] T100 [US14] Create `src/AkmlSql.Engine/Productivity/CrudGenerator.cs` — given table metadata from schema cache (columns, PK, FKs), generate 4 stored procedures: GetById (SELECT by PK), Insert (INSERT with parameters), Update (UPDATE by PK), Delete (DELETE by PK) with proper parameter declarations, error handling, and comments
- [x] T101 [US14] Create `src/AkmlSql.Engine/Productivity/ScriptAsGenerator.cs` — given table metadata, generate: CREATE TABLE (full DDL with constraints/indexes), INSERT (template with column list), SELECT (all columns), MERGE (source/target template), BCP (command-line template)
- [x] T102 [US14] Add CrudGeneration (66) and ScriptAs (67) handlers to ProductivityRequestHandler, register in PipeRpcServer
- [x] T103 [US14] Wire CRUD and Script As context menu entries in AkmlSqlPackage — right-click handling requires detecting Object Explorer context (SSMS-specific COM interop, add TODO for exact hookup)

**Checkpoint**: CRUD generation and Script As templates work for any table.

---

## Phase 17: User Story 15 — Connection Aliases (Priority: P3)

**Goal**: Assign friendly names to servers; aliases appear in tab titles, status bar, everywhere

**Independent Test**: Configure alias, connect, verify alias appears in tab title.

### Implementation for User Story 15

- [x] T104 [US15] Create `src/AkmlSql.Shell.Shared/ConnectionAliasManager.cs` — static class: load aliases from NavigationSettings.ConnectionAliases config, provide Resolve(string serverName) → string (returns alias or original name); used by TabColoringManager, WindowTitleManager, HistoryRecordRequest, status bar
- [x] T105 [US15] Integrate ConnectionAliasManager into existing Phase 7 components: update WindowTitleManager to use Resolve(), update ExecutionCapture to record alias in history, update StatusBarManager to show alias
- [x] T106 [US15] Add alias management to SettingsDialog — new "Aliases" section in the Navigation tab: DataGridView for editing server→alias pairs
- [x] T107 [US15] Wire ConnectionAliasManager.Initialize() in AkmlSqlPackage

**Checkpoint**: Aliases replace raw server names throughout the UI.

---

## Phase 18: Polish & Cross-Cutting Concerns

**Purpose**: Improvements that affect multiple user stories

- [x] T108 [P] Add Grid, EditorProductivity, ExecutionProductivity, Navigation settings tabs/sections to `src/AkmlSql.Shell.Shared/Dialogs/SettingsDialog.cs` — Grid: find/aggregates/null highlight/row numbers toggles; Editor: occurrence highlight/bracket matching/regions/sticky scroll/minimap toggles; Execution: notification threshold, timer toggle; Navigation: Go to Def/Peek/References/Search toggles, alias editor
- [x] T109 [P] Add Serilog logging to all new Phase 8 components — GridAccessHelper, GridFindBar, CommandPalette, DocumentOutlineBuilder, StatementBoundaryDetector, ObjectDefinitionService, ReferenceCollector, all new commands
- [x] T110 Verify all Phase 8 features respect their individual enable/disable config flags
- [x] T111 Register all new shell files in `src/AkmlSql.Shell.Shared/AkmlSql.Shell.Shared.projitems`
- [x] T112 Run manual testing per quickstart.md checklist

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies — can start immediately
- **Foundational (Phase 2)**: Depends on Setup — BLOCKS all user stories
- **User Stories (Phases 3–17)**: All depend on Foundational
- **Polish (Phase 18)**: Depends on all user stories

### User Story Dependencies

- **US1 (P1)**: Can start after Foundational — No story dependencies
- **US2 (P1)**: Can start after Foundational — No story dependencies (shares GridAccessHelper with US1 but can create independently)
- **US3 (P1)**: Can start after Foundational — No story dependencies
- **US4 (P2)**: Can start after Foundational — No story dependencies
- **US5 (P2)**: Can start after Foundational — No story dependencies
- **US6 (P2)**: Can start after Foundational — No story dependencies
- **US7 (P2)**: Can start after Foundational — No story dependencies
- **US8 (P3)**: Depends on US4 (uses StatementBoundaryDetector) and US6 (uses BracketMatchingTagger)
- **US9 (P3)**: Depends on US1 (uses GridAccessHelper)
- **US10 (P3)**: Can start after Foundational — No story dependencies
- **US11 (P3)**: Can start after Foundational — No story dependencies
- **US12 (P3)**: Depends on US7 (uses ObjectDefinitionService + NavigationRequestHandler)
- **US13 (P3)**: Can start after Foundational — No story dependencies
- **US14 (P3)**: Can start after Foundational — No story dependencies
- **US15 (P3)**: Can start after Foundational — No story dependencies

### Parallel Opportunities

After Foundational completes, these story chains can run in parallel:
- **Chain A**: US1 → US9 (Grid features)
- **Chain B**: US2 (Grid export — independent)
- **Chain C**: US3 (Command Palette — independent)
- **Chain D**: US4 → US8 (Statement detection → navigation)
- **Chain E**: US5 (Document Outline — independent)
- **Chain F**: US6 (Editor highlights — independent, but US8 depends on it)
- **Chain G**: US7 → US12 (Navigation chain)
- **Chain H**: US10 + US11 + US13 + US14 + US15 (all independent)

---

## Implementation Strategy

### MVP First (User Stories 1 + 2 + 3)

1. Complete Phase 1: Setup
2. Complete Phase 2: Foundational
3. Complete Phase 3: US1 — Grid Search & Aggregates
4. Complete Phase 4: US2 — Copy & Export
5. Complete Phase 5: US3 — Command Palette
6. **STOP and VALIDATE**: Grid tools + export + command palette
7. Deploy/demo — users already have the most impactful features

### Incremental Delivery

1. Setup + Foundational → Foundation ready
2. US1 + US2 + US3 → Grid + Export + Palette (**MVP!**)
3. US4 + US6 → Execute Current Statement + Highlights
4. US5 + US7 → Document Outline + Go to Definition
5. US8 + US12 → Navigation + References (dependent on above)
6. US9–US15 → Remaining P3 features
7. Polish → Settings, logging, feature flags

### Parallel Team Strategy

With multiple developers:

1. Team completes Setup + Foundational together
2. Once Foundational is done:
   - Developer A: US1 → US9 (Grid chain)
   - Developer B: US2 (Export) + US10 + US11 (Execution)
   - Developer C: US3 (Palette) + US15 (Aliases)
   - Developer D: US4 → US8 (Statement + Navigation)
   - Developer E: US5 (Outline) + US13 (Sticky/Minimap)
   - Developer F: US6 (Highlights) + US7 → US12 (Definition + References)
   - Developer G: US14 (CRUD Generation)

---

## Notes

- [P] tasks = different files, no dependencies
- [Story] label maps task to specific user story for traceability
- No test tasks generated (not explicitly requested in spec)
- Grid features are shell-side only — no IPC for grid data access (except .xlsx export)
- Editor features use MEF ITagger/IAdornment — no IPC needed
- Navigation features (F12, Shift+F12, Ctrl+T) require engine-side IPC
- VSCT changes (T020) must be replicated across all 6 target VSCT files
- Multi-cursor and data visualizer are explicitly out of scope (deferred to future)
