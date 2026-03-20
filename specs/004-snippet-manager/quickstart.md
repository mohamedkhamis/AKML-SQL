# Quickstart: Snippet Manager

**Branch**: `004-snippet-manager` | **Date**: 2026-03-20

## Prerequisites

- Windows 11 (development machine)
- Visual Studio 2022 Enterprise (for MSBuild and VS SDK)
- .NET 10 SDK (for engine and tests)
- SSMS 20 and/or SSMS 22 (for integration testing)
- SQL Server instance with AdventureWorks (for schema-aware placeholder testing)
- Phase 2 (IntelliSense engine) and Phase 3 (Formatter) must be complete

## Project Structure (New in Phase 4)

```text
src/
  AkmlSql.Core/                    # Extended: snippet IPC messages, snippet settings
  AkmlSql.Engine/                  # Extended: Snippets/ module (loader, index, expander, import)
  AkmlSql.Shell.Shared/            # Extended: Snippets/ (expansion session, trigger handler, manager UI)
  (other projects unchanged)
tests/
  AkmlSql.Engine.Tests/            # Extended: Snippets/ test directory
```

No new projects are created — snippets are a module within the existing engine and shell.

## Key Dependencies

No new NuGet packages. Phase 4 uses:
- Phase 2 engine (schema cache, completion popup, CursorContextAnalyzer, named pipes)
- Phase 3 formatter (format-on-expand via FormatterPipeline)
- System.Text.Json 8.x (snippet JSON serialization — already referenced)
- FileSystemWatcher (built-in .NET, for hot-reload)

## Build Commands

```bash
MSBUILD="/c/Program Files/Microsoft Visual Studio/2022/Enterprise/MSBuild/Current/Bin/MSBuild.exe"

# Engine (includes snippet module)
dotnet build src/AkmlSql.Engine/AkmlSql.Engine.csproj -c Release

# Shell projects (same as Phase 1/2/3)
"$MSBUILD" "src/AkmlSql.Ssms20/AkmlSql.Ssms20.csproj" -t:Restore -p:Configuration=Release -v:quiet
"$MSBUILD" "src/AkmlSql.Ssms20/AkmlSql.Ssms20.csproj" -t:Build -p:Configuration=Release -v:minimal

# Tests
dotnet test tests/AkmlSql.Engine.Tests --filter "Category=Snippets"
```

## Development Workflow

### 1. Start with the engine-side snippet module (testable without IDE)

```bash
# Run snippet-specific tests
dotnet test tests/AkmlSql.Engine.Tests --filter "Category=Snippets"

# Test categories
dotnet test tests/AkmlSql.Engine.Tests --filter "Category=SnippetLoader"
dotnet test tests/AkmlSql.Engine.Tests --filter "Category=SnippetExpander"
dotnet test tests/AkmlSql.Engine.Tests --filter "Category=SnippetImport"
```

### 2. Test snippet expansion via engine directly

```bash
# Run engine, send SnippetExpandRequest via test client
dotnet run --project src/AkmlSql.Engine -- --pipe test-pipe-1 --parent-pid 0
```

### 3. Integration test with SSMS

Deploy built-in snippets:
```
Built-in snippets: <SSMS root>/Common7/IDE/Extensions/AkmlSql/Snippets/*.akmlsnippet
```

Test flow: Type `ssf` + Tab → verify expansion → Tab through placeholders → verify schema-aware suggestions.

## Architecture Quick Reference

```
SSMS (UI thread, .NET Fx 4.7.2)
  └─ AkmlSql Shell Extension
       ├─ SnippetTriggerHandler (detect shortcode + Tab)
       ├─ SnippetExpansionManager (manage active session per text view)
       ├─ SnippetExpansionSession (ITrackingSpan tab-stops, linked sync, undo)
       ├─ PlaceholderAdornment (visual highlight)
       ├─ SurroundWithCommand (Ctrl+K, Ctrl+S)
       ├─ CreateSnippetFromSelectionCommand (right-click menu)
       ├─ SnippetManagerDialog (WPF DialogWindow)
       ├─ CompletionCommandHandler (EXTENDED: integrate Tab trigger)
       └─ PipeRpcClient (sends SnippetExpandRequest to engine)

Engine (separate process, .NET 10)
  └─ AkmlSql.Engine.exe
       ├─ Snippets/
       │   ├─ SnippetLoader (load from all sources)
       │   ├─ SnippetIndex (in-memory search + context filter)
       │   ├─ SnippetExpander (resolve variables, prepare expansion)
       │   ├─ SnippetFileWatcher (hot-reload with debounce)
       │   ├─ SnippetUsageTracker (usage counts)
       │   ├─ PlaceholderParser (parse $VarName$ in body)
       │   ├─ BuiltInVariableResolver (14 built-in variables)
       │   └─ Import/ (SQL Prompt XML/JSON, SSMS native)
       ├─ Completion/Providers/SnippetProvider (EXTENDED: context filtering)
       └─ (Phase 2/3 modules unchanged)
```

## Key Design Decisions

1. **Custom tab-stop implementation**: ITrackingSpan + IOleCommandTarget, not VS SDK IVsExpansionSession
2. **Engine/shell split**: Engine handles loading, indexing, expansion, import; shell handles tab-stop navigation, UI
3. **FileSystemWatcher + polling fallback**: Reliable hot-reload for local and network folders
4. **Context filtering via existing ClauseType**: Reuses Phase 2 CursorContextAnalyzer, no new parser
5. **HasSelection in CompletionRequest**: Minimal IPC change for surround-with filtering
6. **Three import parsers**: SQL Prompt XML + JSON + SSMS native to cover all migration scenarios
7. **Schema-aware not set on import**: Source formats don't support it; users add post-import
8. **Body as array of lines**: Git-friendly, human-editable, matches VS Code snippet convention
