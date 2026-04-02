# Implementation Plan: SQL Prompt Core Feature Parity

**Branch**: `010-sql-prompt-core-parity` | **Date**: 2026-04-01 | **Spec**: [spec.md](./spec.md)
**Input**: Feature specification from `/specs/010-sql-prompt-core-parity/spec.md`

## Summary

Fill 8 feature gaps between AKML SQL and Redgate SQL Prompt's core (non-AI) features. Research revealed that most features have substantial backend infrastructure already built (engine, IPC, models). The primary remaining work is shell-side UI, SSMS hookup, and connecting existing components. Priority order: Execution Guard (P1), Snippet Manager (P1), Settings UI (P2), Safe Rename (P2), Actions List (P3), Grid Enhancements (P3), Object Definition Box (P3), Navigation Polish (P4).

## Technical Context

**Language/Version**: C# / .NET Framework 4.7.2 (Shell), .NET 10 (Engine)
**Primary Dependencies**: VS SDK 17.14.x (SSMS 21/22, VS 2022/2026), VS SDK 15.9.3 (SSMS 20), VS SDK 16.0.208 (VS 2019)
**Storage**: JSON config files (%AppData%/AKML SQL/), SQLite (history), .akmlsnippet files (snippets)
**Testing**: xunit 2.x, Microsoft.NET.Test.Sdk 17.x
**Target Platform**: SSMS 20/21/22, Visual Studio 2019/2022/2026, Windows x86/x64
**Project Type**: VS/SSMS extension (desktop IDE plugin)
**Performance Goals**: Dialog response <200ms, grid sort <500ms for 10K rows, aggregate stats <300ms
**Constraints**: No XAML in SharedProject (programmatic WPF only), must build each shell project individually with MSBuild
**Scale/Scope**: 8 feature areas, ~15 files to create, ~20 files to modify

## Constitution Check

*No constitution file found. Proceeding without constitution gates.*

## Project Structure

### Documentation (this feature)

```text
specs/010-sql-prompt-core-parity/
├── plan.md              # This file
├── spec.md              # Feature specification
├── research.md          # Phase 0 research findings
├── data-model.md        # Entity models
├── quickstart.md        # Build and test instructions
├── contracts/
│   └── ipc-messages.md  # IPC message contracts
├── checklists/
│   └── requirements.md  # Spec quality checklist
└── tasks.md             # Phase 2 output (created by /speckit.tasks)
```

### Source Code (files to create/modify)

```text
src/AkmlSql.Shell.Shared/
├── Safety/
│   ├── ExecutionInterceptor.cs          # MODIFY: Wire pre-execution hook + audit logging
│   └── SafetyWarningDialog.cs           # MODIFY: Add environment color styling per clarification
├── Snippets/
│   ├── SnippetManagerDialog.cs          # CREATE: WPF dialog for snippet CRUD
│   ├── SnippetManagerViewModel.cs       # CREATE: ViewModel for snippet manager
│   └── SnippetManagerCommand.cs         # CREATE: Menu command to open snippet manager
├── Dialogs/
│   └── SettingsWindow.cs                # MODIFY: Add category pages for all 15 AppSettings sections
├── Refactoring/
│   ├── SafeRenameCommand.cs             # MODIFY: Complete stub with input dialog + IPC + script output
│   └── RefactoringPreviewDialog.cs      # MODIFY: Complete stub with diff tree view
├── Analysis/
│   ├── LightbulbProvider.cs             # MODIFY: Add refactoring actions to suggested actions
│   └── RefactoringAction.cs             # CREATE: ISuggestedAction for refactoring operations
├── Productivity/Grid/
│   ├── GridSortHandler.cs               # CREATE: Column header click sorting
│   ├── GridFilterPopup.cs               # CREATE: Column filter UI
│   └── GridFeatureInitializer.cs        # MODIFY: Wire sort + filter handlers
├── Editor/Completion/
│   ├── AkmlCompletionPopup.cs           # MODIFY: Add secondary definition panel
│   └── ObjectDefinitionPanel.cs         # CREATE: Summary/Script tabbed panel
├── Navigation/
│   ├── BookmarkManager.cs               # CREATE: Session-scoped bookmark store
│   ├── BookmarkGlyphFactory.cs          # CREATE: Margin glyph for bookmarks
│   ├── BookmarkCommands.cs              # CREATE: Toggle/Next/Previous commands
│   └── DocumentOutlineCommand.cs        # MODIFY: Complete stub
├── Commands/
│   └── AkmlSqlPackage.vsct              # MODIFY: Add menu entries for new commands

src/AkmlSql.Engine/
├── Navigation/
│   └── DocumentOutlineHandler.cs        # CREATE: Parse SQL into outline tree
├── Snippets/
│   └── SnippetRequestHandler.cs         # MODIFY: Implement HandleImport()

tests/AkmlSql.Core.Tests/
├── Safety/
│   └── SafetyCheckHandlerTests.cs       # MODIFY: Add audit logging tests
├── Snippets/
│   └── SnippetImportTests.cs            # CREATE: Import handler tests
└── Navigation/
    └── DocumentOutlineTests.cs          # CREATE: Outline parser tests
```

**Structure Decision**: All new code goes into existing project directories following established patterns. No new projects needed. Shell code in Shell.Shared (compiled into all 6 targets), Engine code in AkmlSql.Engine, tests in AkmlSql.Core.Tests.

## Implementation Phases

### Phase 1: P1 — Execution Guard + Snippet Manager (highest impact)

#### Task 1.1: Execution Guard — SSMS Pre-Execution Hook

**What exists**: `ExecutionInterceptor.cs` (complete logic), `SafetyCheckHandler.cs` (engine analysis), `SafetyWarningDialog.cs` (3 dialog modes), IPC messages (type 55/155), `EnvironmentDetector.cs` (production matching), `SafetySettings` (7 config flags), package initialization already calls `ExecutionInterceptor.Initialize()`.

**What to build**:
1. Implement `IOleCommandTarget` command filter in `ExecutionInterceptor.Initialize()` to intercept `ECMD_EXECUTE` / `ECMD_EXECUTESQL` before SSMS processes them
2. Extract SQL text from active editor buffer via `IVsTextView.GetBuffer()`
3. Extract server name from active connection (via SSMS connection info API)
4. Call existing `OnBeforeExecute(sqlText, serverName)` — returns true/false
5. If false (blocked), suppress the command; if true, pass through to SSMS
6. Add Serilog structured logging after dialog result for audit trail (FR-007a)

**Files**: `ExecutionInterceptor.cs` (modify), `SafetyWarningDialog.cs` (minor — add environment color to dialog background per clarification)

**Tests**: Unit test `SafetyCheckHandler` with various SQL patterns (DELETE without WHERE, DROP TABLE, TRUNCATE, DELETE with WHERE, safe SELECT). Integration test requires SSMS.

**Risk**: SSMS pre-execution hook mechanism varies by version. SSMS 20 (IsolatedShell) may use different COM interfaces than SSMS 21/22. Fallback: use `DTE.Events.CommandEvents` with pre-execute callback.

---

#### Task 1.2: Snippet Manager — WPF Dialog

**What exists**: Full engine backend (`SnippetLoader`, `SnippetIndex`, `SnippetRequestHandler`), IPC messages (types 20-24, 120-124), rich data model (metadata, variables, context, surround-with), `ProfileEditorDialog.cs` as WPF template, `ThemeManager` for theming.

**What to build**:
1. `SnippetManagerDialog.cs` — WPF `DialogWindow` with split-pane layout:
   - Left: SearchBox + TreeView (Personal/Team/BuiltIn → Categories → Snippets)
   - Right: Editor panel (Shortcode, Name, Description, Tags, Category fields + Body editor with syntax coloring)
   - Bottom: Buttons (New, Duplicate, Delete, Import, Export, Close)
2. `SnippetManagerViewModel.cs` — INotifyPropertyChanged, communicates with engine via IPC
3. `SnippetManagerCommand.cs` — VS menu command (add to AKML SQL menu in .vsct)
4. Implement `SnippetRequestHandler.HandleImport()` in engine (currently stubbed)

**Files**: Create 3 new files in Shell.Shared/Snippets/, modify SnippetRequestHandler.cs, modify .vsct for menu entry

**Pattern**: Follow `ProfileEditorDialog.cs` exactly — programmatic WPF, ThemeManager colors, no XAML, DialogWindow base class, ~1100x750 size.

---

### Phase 2: P2 — Settings UI + Safe Rename

#### Task 2.1: Settings UI Completeness

**What exists**: `SettingsWindow.cs` (WPF, theme-aware), `SettingsDialog.cs` (WinForms, 150+ controls), `AppSettings.cs` (15+ sections all defined), `OptionCategoryTreeBuilder.cs` (tree navigation builder), `ConfigManager.cs` (load/save).

**What to build**:
1. Extend `SettingsWindow.cs` with a category TreeView on the left (repurpose OptionCategoryTreeBuilder pattern)
2. Create content panels for each settings section:
   - IntelliSense (12 settings): toggles, delays, fuzzy match options
   - Cache (6 settings): auto-refresh, intervals, max databases
   - Formatter (4 settings): active profile dropdown + preview, trigger options
   - Snippets (5 settings): enabled, folders, format-on-expand, context filter
   - Code Analysis (8 settings): enabled, severity levels, rule category toggles
   - Refactoring (3 settings): preview, preserve whitespace, formatting
   - History (4 settings): enabled, max entries, cleanup interval
   - Tabs (4 settings): coloring enabled, environment rules editor with color picker
   - Safety (7 settings): execution guard toggles, environment severity map
   - Grid (5 settings): aggregates, null highlight, row numbers, freeze headers
   - Editor Productivity (4 settings): smart indent, comment format, auto-brackets
   - Execution Productivity: execution timer, post-execution actions
   - Navigation (3 settings): peek size, search case, symbol browser
   - Command Palette (2 settings): history size, shortcut
   - AI (if applicable, separate section)
3. Add Reset This Page / Reset All / Export / Import buttons
4. Wire all controls to `AppSettings` properties via `ConfigManager.Load()`/`Save()`

**Files**: `SettingsWindow.cs` (major modification), potentially extract per-section builder methods

---

#### Task 2.2: Safe Rename — Shell Command + Preview + Script Generation

**What exists**: `SafeRenameOperation.cs` (engine, fully implemented — preview + apply), `ReferenceCollector.cs` (AST visitor, finds all references), `RefactoringEngine.cs` (dispatch), IPC messages (types 30/31, 130/131), `SafeRenameCommand.cs` (shell stub), `RefactoringPreviewDialog.cs` (shell stub).

**What to build**:
1. Complete `SafeRenameCommand.cs`:
   - Show input dialog for new identifier name
   - Send `RefactorPreviewRequest` (type 30) with `OperationType = SafeRename`
   - Receive `RefactorPreviewResponse` with `RefactorChangeInfo[]` array
   - Open `RefactoringPreviewDialog` with results
2. Complete `RefactoringPreviewDialog.cs`:
   - Left panel: TreeView with file-level nodes + per-reference items
   - Right panel: Read-only diff view (- old / + new, color-coded)
   - Bottom: "Generate Script" button + "Cancel"
3. Script generation (NEW — per spec clarification):
   - Convert `RefactorChangeInfo[]` into a SQL script with ALTER statements
   - Add comments explaining each change
   - Open script in a new SSMS query editor tab via `DTE.ItemOperations.NewFile()`
   - Do NOT execute directly against database

**Files**: `SafeRenameCommand.cs` (complete stub), `RefactoringPreviewDialog.cs` (complete stub), create `RenameScriptGenerator.cs` (new, generates SQL ALTER script from change info)

---

### Phase 3: P3 — Actions List + Grid + Object Definition Box

#### Task 3.1: Actions List — Extend LightbulbProvider

**What exists**: `LightbulbProvider.cs` with `ISuggestedActionsSourceProvider` (MEF), `FixAction.cs` / `SuppressLineFixAction.cs` / `DisableRuleGloballyFixAction.cs`, `AnalysisController` events, all analysis auto-fix infrastructure.

**What to build**:
1. Create `RefactoringAction.cs` implementing `ISuggestedAction` for:
   - Qualify Object Names (uses existing `FormatAction` IPC)
   - Expand Wildcards (uses existing `WildcardExpansionHandler`)
   - Surround with BEGIN/END (uses existing `EncapsulateBeginEndOperation`)
   - Surround with TRY/CATCH
   - Comment/Uncomment selection
2. Modify `LightbulbProvider.cs` to yield refactoring actions alongside analysis fixes based on cursor context:
   - If cursor on `SELECT *` → add "Expand Wildcards"
   - If text selected → add "Surround with" options
   - If cursor on unqualified table → add "Qualify Object Names"
   - Always: "Comment/Uncomment", "Format Selection"

**Files**: Create `RefactoringAction.cs`, modify `LightbulbProvider.cs`

---

#### Task 3.2: Grid Enhancements — Sort + Filter

**What exists**: `GridFeatureInitializer.cs` (timer-based grid discovery, attaches features), `GridAccessHelper.cs` (finds DataGridView), `ColumnStatisticsPopup.cs` (column header right-click), `GridAggregatesProvider.cs` (SUM/AVG/COUNT in status bar).

**What to build**:
1. `GridSortHandler.cs`:
   - Attach to `DataGridView.ColumnHeaderMouseClick` event
   - Implement 3-click cycle: Ascending → Descending → None
   - Sort in-memory DataTable backing the grid
   - Show sort direction indicator in column header
2. `GridFilterPopup.cs`:
   - Small WinForms popup on column header right-click (alongside existing statistics)
   - Text input for contains/equals filter
   - "Clear Filter" button
   - Apply filter via `DataView.RowFilter` on backing DataTable
   - Track filtered state, reset on new query execution
3. Modify `GridFeatureInitializer.cs` to attach sort + filter handlers

**Files**: Create `GridSortHandler.cs`, `GridFilterPopup.cs`, modify `GridFeatureInitializer.cs`

**Risk**: DataGridView in SSMS may use a custom subclass that doesn't expose standard sort/filter APIs. Fallback: create a shadow DataTable from grid cell values and apply sort/filter to it, then re-bind.

---

#### Task 3.3: Object Definition Box — Secondary Completion Panel

**What exists**: `AkmlCompletionPopup.cs` (WPF popup with ListBox), `QuickInfoProvider.cs` (engine, returns rich object info), `QuickInfoRequest/Response` IPC messages, `CompletionController.cs` (keyboard handling).

**What to build**:
1. `ObjectDefinitionPanel.cs` — WPF UserControl:
   - Two tabs: "Summary" and "Script"
   - Summary: Grid of column name, type, nullable, key icon, + row count
   - Script: TextBlock with syntax-highlighted CREATE statement
   - ~300px wide, same dark theme as completion popup
2. Modify `AkmlCompletionPopup.cs`:
   - Add `ObjectDefinitionPanel` positioned to the right of the popup
   - On selection change in ListBox, send `QuickInfoRequest` to engine
   - Populate panel with response data
   - Dismiss panel when popup dismisses
3. Modify `CompletionController.cs`:
   - Debounce QuickInfo requests (300ms delay after selection change)
   - Cancel pending request on new selection

**Files**: Create `ObjectDefinitionPanel.cs`, modify `AkmlCompletionPopup.cs`, modify `CompletionController.cs`

---

### Phase 4: P4 — Navigation Polish

#### Task 4.1: Bookmarks

**What to build**:
1. `BookmarkManager.cs` — Static class with `Dictionary<string, List<int>>` (TextViewId → line numbers)
   - `Toggle(textView, line)`, `GetAll(textView)`, `NextBookmark(textView, currentLine)`, `PreviousBookmark(textView, currentLine)`, `Clear(textView)`
2. `BookmarkGlyphFactory.cs` — Implements `IGlyphFactory` (MEF export)
   - Renders blue circle icon in editor margin for bookmarked lines
   - Uses `IClassifierAggregatorService` for classification
3. `BookmarkCommands.cs` — Three VS commands:
   - Toggle Bookmark: Ctrl+K, Ctrl+K
   - Next Bookmark: Ctrl+K, Ctrl+N
   - Previous Bookmark: Ctrl+K, Ctrl+P
4. Add commands to .vsct file

**Files**: Create 3 files in Shell.Shared/Navigation/, modify .vsct

---

#### Task 4.2: Document Outline

**What exists**: `DocumentOutlineCommand.cs` (shell stub), IPC message types 64/164, `OutlineNodeDto` model.

**What to build**:
1. `DocumentOutlineHandler.cs` (Engine):
   - Parse document with TSql170Parser
   - Walk AST to build `OutlineNodeDto[]` tree:
     - CREATE PROCEDURE → node with parameters as children
     - CREATE FUNCTION → node
     - CREATE VIEW → node
     - WITH (CTE) → node per CTE name
     - SELECT INTO #temp → temp table node
     - GO batch boundaries → batch separator nodes
   - Return sorted by document offset
2. Complete `DocumentOutlineCommand.cs` (Shell):
   - Open `DocumentOutlineToolWindow` (VS tool window)
   - TreeView bound to `OutlineNodeDto[]`
   - Click node → navigate editor to node's StartOffset
   - Refresh on document change (debounced 500ms)

**Files**: Create `DocumentOutlineHandler.cs` (Engine), modify `DocumentOutlineCommand.cs` (Shell), may need to create/modify `DocumentOutlineToolWindow.cs`

---

## Dependency Graph

```
Phase 1 (P1 — no dependencies):
  Task 1.1 (Execution Guard) ─── independent
  Task 1.2 (Snippet Manager) ─── independent

Phase 2 (P2 — depends on Phase 1 for settings infrastructure):
  Task 2.1 (Settings UI) ──── depends on Task 1.1 (Safety settings page needs guard config)
  Task 2.2 (Safe Rename) ──── independent

Phase 3 (P3 — independent, can start after Phase 1):
  Task 3.1 (Actions List) ─── independent (dispatches to existing FormatAction IPC, not refactoring preview)
  Task 3.2 (Grid) ─────────── independent
  Task 3.3 (Definition Box) ── independent

Phase 4 (P4 — independent):
  Task 4.1 (Bookmarks) ────── independent
  Task 4.2 (Doc Outline) ──── independent
```

## Complexity Tracking

No constitution violations to justify. All work stays within existing project structure and patterns.
