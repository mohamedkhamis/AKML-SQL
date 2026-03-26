# Implementation Plan: Productivity Toolkit

**Branch**: `008-productivity-toolkit` | **Date**: 2026-03-24 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/008-productivity-toolkit/spec.md`

## Summary

Phase 8 delivers 33 productivity features across four areas: Results Grid (find, aggregates, export, cell editing, column stats, transpose, null highlight, row numbers, frozen headers), Editor (Command Palette, Document Outline, highlight occurrences, bracket matching, named regions, sticky scroll, minimap, statement navigation), Execution (execute current statement, execute to cursor, multi-database execution, timer, notifications, CRUD generation, Script As), and Navigation (Go to Definition, Peek Definition, Find All References, Object Search, connection aliases). Features integrate via the existing shell↔engine IPC architecture, MEF-based editor adornments, and SSMS grid COM interop.

## Technical Context

**Language/Version**: C# / .NET Framework 4.7.2 (shell extensions), .NET 10 (engine)
**Primary Dependencies**: ClosedXML or EPPlus (new, for .xlsx export), VS SDK 15.9–17.14 (per target), MessagePack 2.x (IPC), Microsoft.SqlServer.TransactSql.ScriptDom 170.x (AST parsing for statement boundaries, bracket matching, document outline)
**Storage**: Command Palette usage counts in config.json; connection aliases in config.json
**Testing**: xunit 2.x with Microsoft.NET.Test.Sdk 17.x
**Target Platform**: Windows (SSMS 20/21/22, VS 2019/2022/2026)
**Project Type**: Desktop IDE extension (VS/SSMS plugin)
**Performance Goals**: Grid search < 2s for 100K rows; aggregates < 500ms; Command Palette open < 500ms; Go to Definition < 3s; Document Outline refresh < 1s for 5K+ line scripts; Excel export < 10s for 100K rows
**Constraints**: Shell runs in .NET Framework 4.7.2; all UI on VS UI thread; grid features require SSMS-specific COM interop (DataGridView access varies by SSMS version); editor features use MEF ITagger/IAdornment APIs
**Scale/Scope**: 33 features, 15 user stories, ~13 configuration settings, unlimited Command Palette entries

## Constitution Check

*No constitution file found — gate skipped. No violations to justify.*

## Project Structure

### Documentation (this feature)

```text
specs/008-productivity-toolkit/
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/           # Phase 1 output (IPC message contracts)
│   ├── grid-ipc.md
│   ├── editor-ipc.md
│   └── navigation-ipc.md
└── tasks.md             # Phase 2 output (/speckit.tasks)
```

### Source Code (repository root)

```text
src/
├── AkmlSql.Core/
│   ├── Config/
│   │   └── AppSettings.cs              # Add GridSettings, EditorProductivitySettings, ExecutionProductivitySettings, NavigationSettings
│   ├── Ipc/
│   │   ├── RpcMessage.cs               # Add message type constants (60-69, 160-169)
│   │   └── Messages/
│   │       ├── GetObjectDefinitionRequest.cs    # New: F12 Go to Definition
│   │       ├── GetObjectDefinitionResponse.cs   # New: CREATE script result
│   │       ├── DocumentOutlineRequest.cs        # New: script structure request
│   │       ├── DocumentOutlineResponse.cs       # New: outline tree
│   │       ├── FindReferencesRequest.cs         # New: Shift+F12
│   │       ├── FindReferencesResponse.cs        # New: reference list
│   │       ├── StatementBoundaryRequest.cs      # New: locate statement at offset
│   │       ├── StatementBoundaryResponse.cs     # New: statement range
│   │       ├── CrudGenerationRequest.cs         # New: table → CRUD procedures
│   │       ├── CrudGenerationResponse.cs        # New: generated SQL
│   │       ├── ScriptAsRequest.cs               # New: table → script template
│   │       └── ScriptAsResponse.cs              # New: generated script
│   └── Models/
│       ├── Productivity/
│       │   ├── CommandEntry.cs                  # New: Command Palette item
│       │   ├── DocumentOutlineNode.cs           # New: outline tree node
│       │   ├── GridExportFormat.cs              # New: export format enum
│       │   ├── ConnectionAlias.cs               # New: server alias
│       │   └── StatementRange.cs                # New: statement start/end offset
│       └── Navigation/
│           └── ObjectReference.cs               # New: reference location
├── AkmlSql.Engine/
│   ├── Navigation/
│   │   ├── ObjectDefinitionService.cs           # New: query OBJECT_DEFINITION/sys.sql_modules
│   │   ├── ReferenceCollector.cs                # New: query sys.sql_expression_dependencies
│   │   └── NavigationRequestHandler.cs          # New: IPC handler for F12/Shift+F12/Ctrl+T
│   ├── Productivity/
│   │   ├── StatementBoundaryDetector.cs         # New: AST-based statement range detection
│   │   ├── DocumentOutlineBuilder.cs            # New: AST → outline tree
│   │   ├── CrudGenerator.cs                     # New: table metadata → CRUD procedures
│   │   ├── ScriptAsGenerator.cs                 # New: table metadata → script templates
│   │   └── ProductivityRequestHandler.cs        # New: IPC handler for outline/statement/CRUD
│   └── Export/
│       └── GridExportService.cs                 # New: data → CSV/JSON/XML/XLSX/MD/SQL files
├── AkmlSql.Shell.Shared/
│   ├── Commands/
│   │   ├── CommandPaletteCommand.cs             # New: Ctrl+Shift+P handler
│   │   ├── ExecuteCurrentStatementCommand.cs    # New: Alt+Enter handler
│   │   ├── ExecuteToCursorCommand.cs            # New: execute to cursor
│   │   ├── GoToDefinitionCommand.cs             # New: F12 handler
│   │   ├── PeekDefinitionCommand.cs             # New: Alt+F12 handler
│   │   ├── FindReferencesCommand.cs             # New: Shift+F12 handler
│   │   ├── ObjectSearchCommand.cs               # New: Ctrl+T handler
│   │   ├── NavigateStatementCommand.cs          # New: Ctrl+PageUp/Down handler
│   │   ├── NavigateMatchingPairCommand.cs       # New: Ctrl+] handler
│   │   └── GridCommands.cs                      # New: grid context menu commands (Copy As, Export, Script)
│   ├── Productivity/
│   │   ├── CommandPalette/
│   │   │   ├── CommandPaletteWindow.cs          # New: WPF popup overlay
│   │   │   ├── CommandPaletteViewModel.cs       # New: fuzzy search, ranking
│   │   │   └── CommandRegistry.cs               # New: registers all commands + SSMS commands
│   │   ├── DocumentOutline/
│   │   │   ├── DocumentOutlineToolWindow.cs     # New: dockable tool window
│   │   │   ├── DocumentOutlineControl.cs        # New: WPF TreeView UI
│   │   │   └── DocumentOutlineViewModel.cs      # New: outline tree ViewModel
│   │   ├── Grid/
│   │   │   ├── GridFindBar.cs                   # New: search bar overlay on results grid
│   │   │   ├── GridAggregatesProvider.cs        # New: selection → aggregates in status bar
│   │   │   ├── GridExportManager.cs             # New: export orchestration (shell side)
│   │   │   ├── GridCopyAsMenu.cs                # New: right-click Copy As submenu
│   │   │   ├── GridScriptGenerator.cs           # New: Generate INSERT/UPDATE/DELETE from rows
│   │   │   ├── CellEditDialog.cs                # New: inline cell edit dialog
│   │   │   ├── ColumnStatisticsPopup.cs         # New: column stats popup
│   │   │   └── TransposeResultsView.cs          # New: transposed single-row view
│   │   └── Navigation/
│   │       ├── PeekDefinitionControl.cs         # New: inline peek panel
│   │       ├── ReferencesPanel.cs               # New: Find All References results panel
│   │       └── ObjectSearchWindow.cs            # New: Ctrl+T overlay
│   ├── Editor/
│   │   ├── OccurrenceHighlightTagger.cs         # New: ITagger for identifier highlighting
│   │   ├── BracketMatchingTagger.cs             # New: ITagger for pair matching
│   │   ├── RegionTagger.cs                      # New: ITagger for --region collapsing
│   │   ├── StickyScrollAdornment.cs             # New: IAdornmentLayer for sticky context
│   │   └── MinimapAdornment.cs                  # New: IAdornmentLayer for minimap
│   ├── Execution/
│   │   ├── ExecutionTimerManager.cs             # New: live elapsed time in status bar
│   │   ├── CompletionNotifier.cs                # New: Windows toast notifications
│   │   └── MultiDatabaseExecutor.cs             # New: parallel execution + comparison view
│   └── ConnectionAliasManager.cs                # New: alias storage and resolution
tests/
├── AkmlSql.Core.Tests/
│   ├── Productivity/
│   │   ├── StatementBoundaryDetectorTests.cs    # New
│   │   ├── DocumentOutlineBuilderTests.cs       # New
│   │   └── CrudGeneratorTests.cs                # New
│   └── Navigation/
│       └── ObjectDefinitionServiceTests.cs      # New
```

**Structure Decision**: Phase 8 follows the existing architecture. Grid features are shell-side (direct DataGridView interaction, no IPC for grid data). Editor features use MEF taggers/adornments. Navigation features (F12, Shift+F12, Ctrl+T) use engine-side SQL Server catalog queries via new IPC messages. The Command Palette and Document Outline are shell-side tool windows with engine-side outline parsing.

## Complexity Tracking

> No Constitution Check violations to justify.
