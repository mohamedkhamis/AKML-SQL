# Implementation Plan: Snippet Manager

**Branch**: `004-snippet-manager` | **Date**: 2026-03-20 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/004-snippet-manager/spec.md`

## Summary

Deliver a schema-aware snippet system for SSMS 20/21/22 and VS 2019/2022/2026 with 75+ built-in snippets, custom snippet creation, tab-stop placeholder navigation with linked placeholders, surround-with templates, IntelliSense integration, multi-source library (personal/team/built-in), format-on-expand via the Phase 3 formatter, and import from SQL Prompt and SSMS native formats. The snippet engine runs as a module within the Phase 2 out-of-process engine, while tab-stop navigation runs in-process in the shell using ITrackingSpan + IOleCommandTarget. Snippets are stored as `.akmlsnippet` JSON files with a separate variables section supporting schema-aware types.

## Technical Context

**Language/Version**: C# / .NET Framework 4.7.2 (shell extensions) + .NET 10 (engine, tests)
**Primary Dependencies**: Phase 2 engine (schema cache, completion popup, named pipes, CursorContextAnalyzer), Phase 3 formatter (format-on-expand), System.Text.Json 8.x (snippet serialization), FileSystemWatcher (hot-reload)
**Storage**: `.akmlsnippet` JSON files in `%AppData%/AKML SQL/snippets/` (personal), `<install>/snippets/` (built-in), configurable path (team). Usage stats in `%AppData%/AKML SQL/cache/snippet-usage.json`
**Testing**: xunit 2.x, Microsoft.NET.Test.Sdk 17.x
**Target Platform**: Windows 10/11, SSMS 20 (x86) / SSMS 21-22 (x64) / VS 2019-2026
**Project Type**: VS extension module (within existing engine + shell)
**Performance Goals**: <20ms expansion, <50ms search (500 snippets), <10ms tab-stop navigation, <100ms hot-reload, <100ms schema-aware placeholder suggestion
**Constraints**: No additional processes, reuse Phase 2 completion popup, custom tab-stop implementation (not VS SDK IVsExpansionSession)
**Scale/Scope**: 75+ built-in snippets, 500+ total snippets supported, 14 built-in variables, 9 schema-aware types, 3 import formats, 6 IDE targets

## Constitution Check

*No constitution file found. Gates skipped.*

## Project Structure

### Documentation (this feature)

```text
specs/004-snippet-manager/
├── plan.md                 # This file
├── spec.md                 # Feature specification
├── research.md             # Phase 0: Expansion patterns, import formats
├── data-model.md           # Phase 1: Entity model
├── quickstart.md           # Phase 1: Development setup guide
├── contracts/
│   ├── snippet-file-format.md       # .akmlsnippet JSON schema
│   ├── snippet-protocol-extension.md # Named pipe protocol extensions
│   └── import-mapping.md            # SQL Prompt and SSMS import mapping
├── checklists/
│   └── requirements.md    # Spec quality checklist
└── tasks.md               # Phase 2 output (created by /speckit.tasks)
```

### Source Code (repository root)

```text
src/
├── AkmlSql.Core/                          # EXTENDED: Snippet IPC messages, snippet settings
│   ├── Config/
│   │   ├── AppSettings.cs                 # Extended: SnippetSettings
│   │   └── ConfigManager.cs              # Unchanged
│   ├── Ipc/
│   │   ├── Messages/
│   │   │   ├── SnippetExpandRequest.cs    # NEW: Expand snippet by shortcode
│   │   │   ├── SnippetExpandResponse.cs   # NEW: Expanded text with placeholder info
│   │   │   ├── SnippetListRequest.cs      # NEW: List/search snippets
│   │   │   ├── SnippetListResponse.cs     # NEW: Snippet metadata array
│   │   │   ├── SnippetSaveRequest.cs      # NEW: Save/update snippet
│   │   │   ├── SnippetSaveResponse.cs     # NEW: Save confirmation
│   │   │   ├── SnippetDeleteRequest.cs    # NEW: Delete snippet
│   │   │   ├── SnippetDeleteResponse.cs   # NEW: Delete confirmation
│   │   │   ├── SnippetImportRequest.cs    # NEW: Import from external format
│   │   │   ├── SnippetImportResponse.cs   # NEW: Import result with mapping report
│   │   │   ├── CompletionRequest.cs       # EXTENDED: add HasSelection field
│   │   │   └── (existing messages unchanged)
│   │   └── FrameProtocol.cs             # Unchanged
│   └── Logging/
│
├── AkmlSql.Engine/                        # EXTENDED: Snippet engine module
│   ├── Snippets/                          # NEW: Snippet engine
│   │   ├── SnippetLoader.cs              # Load .akmlsnippet files from all sources
│   │   ├── SnippetIndex.cs              # In-memory searchable index with context filtering
│   │   ├── SnippetExpander.cs           # Resolve built-in variables, prepare expansion text
│   │   ├── SnippetFileWatcher.cs        # FileSystemWatcher per source + debounce + polling fallback
│   │   ├── SnippetUsageTracker.cs       # Track usage counts, persist to JSON
│   │   ├── PlaceholderParser.cs         # Parse $VarName$ markers in body text
│   │   ├── BuiltInVariableResolver.cs   # Resolve $DATE$, $USER$, $DATABASE$, etc.
│   │   └── Import/
│   │       ├── SqlPromptXmlImporter.cs   # Import .sqlpromptsnippet XML files
│   │       ├── SqlPromptJsonImporter.cs  # Import SQL Prompt v10.5+ JSON files
│   │       ├── SsmsSnippetImporter.cs    # Import .snippet VS CodeSnippet XML files
│   │       └── ImportVariableMapper.cs   # Variable name mapping across formats
│   ├── Completion/
│   │   └── Providers/
│   │       └── SnippetProvider.cs        # EXTENDED: Load from SnippetIndex, context filter
│   ├── Server/
│   │   └── PipeRpcServer.cs              # EXTENDED: Route snippet message types
│   └── AkmlSql.Engine.csproj            # Unchanged (no new dependencies)
│
├── AkmlSql.Shell.Shared/                 # EXTENDED: Snippet expansion UI, manager dialog
│   ├── Snippets/                          # NEW: Shell-side snippet integration
│   │   ├── SnippetExpansionSession.cs    # Tab-stop state: tracking spans, linked groups, undo
│   │   ├── SnippetExpansionManager.cs    # Manage active session per text view
│   │   ├── SnippetTriggerHandler.cs      # Detect shortcode + Tab, initiate expansion
│   │   ├── PlaceholderAdornment.cs       # Visual highlight for active placeholder
│   │   └── SurroundWithCommand.cs        # Ctrl+K, Ctrl+S — show surround-with list
│   ├── Ui/
│   │   ├── SnippetManagerDialog.cs       # NEW: WPF DialogWindow — snippet browser + editor
│   │   ├── SnippetManagerViewModel.cs    # NEW: Tree view, search, CRUD, preview state
│   │   ├── SnippetEditorPanel.cs         # NEW: Editor for snippet metadata + body + variables
│   │   ├── SnippetPreviewRenderer.cs     # NEW: Live preview of expanded snippet
│   │   └── (existing UI files unchanged)
│   ├── Editor/
│   │   └── CompletionCommandHandler.cs   # EXTENDED: Integrate snippet trigger on Tab
│   ├── Commands/
│   │   └── CreateSnippetFromSelectionCommand.cs  # NEW: Right-click → Create Snippet
│   └── (existing files unchanged)
│
├── AkmlSql.Ssms20/ through AkmlSql.VS2026/  # Unchanged, import Shell.Shared
├── AkmlSql.Formatting/                       # Unchanged (consumed for format-on-expand)
├── AkmlSql.Formatter/                        # Unchanged
├── AkmlSql.Updater/                          # Unchanged
└── AkmlSql.Installer/                        # EXTENDED: deploy built-in snippets

tests/
├── AkmlSql.Core.Tests/                   # Extended: snippet message serialization tests
├── AkmlSql.Engine.Tests/                 # Extended: snippet tests
│   └── Snippets/
│       ├── SnippetLoaderTests.cs         # Loading from multiple sources
│       ├── SnippetIndexTests.cs          # Search, context filtering
│       ├── SnippetExpanderTests.cs       # Variable resolution, expansion
│       ├── PlaceholderParserTests.cs     # Placeholder detection in body
│       ├── BuiltInVariableResolverTests.cs # All 14 built-in variables
│       ├── SqlPromptImporterTests.cs     # XML + JSON import with variable mapping
│       ├── SsmsImporterTests.cs          # VS CodeSnippet import
│       └── SnippetUsageTrackerTests.cs   # Usage count persistence
└── AkmlSql.Formatting.Tests/             # Unchanged
```

**Structure Decision**: Phase 4 adds snippet functionality as a new module within the existing engine (`AkmlSql.Engine/Snippets/`) and shell (`AkmlSql.Shell.Shared/Snippets/`). No new projects are needed — snippets are a module, not a standalone library. The engine handles snippet loading, indexing, expansion, and import. The shell handles tab-stop navigation, visual adornments, and the manager UI. This follows the same split as Phase 2 (engine = logic, shell = UI) and Phase 3 (engine = formatting, shell = commands).

## Complexity Tracking

| Aspect | Justification | Simpler Alternative Rejected Because |
|---|---|---|
| Custom tab-stop implementation (not VS SDK IVsExpansionSession) | Full control over linked placeholders, schema-aware IntelliSense during navigation, undo integration | VS SDK expansion API is coupled to .snippet XML format, prevents schema-aware placeholders, fragile across SSMS versions |
| FileSystemWatcher + polling fallback for team folders | Hot-reload required (FR-030); network shares can silently drop FSW notifications | Polling only would be too slow for local folders; FSW only would miss changes on network shares |
| Three import parsers (SQL Prompt XML, SQL Prompt JSON, SSMS native) | SQL Prompt changed format at v10.5; SSMS uses a different schema; all three are common migration sources | Single parser would miss a significant portion of existing snippets users want to migrate |
