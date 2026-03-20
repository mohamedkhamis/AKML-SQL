# Quickstart: SQL Formatter & Code Beautifier

**Branch**: `003-sql-formatter` | **Date**: 2026-03-20

## Prerequisites

- Windows 11 (development machine)
- Visual Studio 2022 Enterprise (for MSBuild and VS SDK)
- .NET 10 SDK (for formatting library, engine, CLI, and tests)
- SSMS 20 and/or SSMS 22 (for integration testing)
- SQL Server instance with a sample database (for identifier casing sync and wildcard expansion testing)

## Project Structure (New in Phase 3)

```text
src/
  AkmlSql.Core/                    # Extended: formatter IPC message types, formatter settings
  AkmlSql.Formatting/              # NEW: Shared formatting engine library (.NET 10)
  AkmlSql.Formatter/               # NEW: Standalone CLI formatter (.NET 10, self-contained)
  AkmlSql.Engine/                  # Extended: format request handlers
  AkmlSql.Shell.Shared/            # Extended: format commands, profile editor UI
  AkmlSql.Ssms20/                  # Unchanged (imports Shell.Shared)
  AkmlSql.Ssms22/                  # Unchanged
  (other shell projects...)
tests/
  AkmlSql.Core.Tests/              # Extended: formatter message serialization tests
  AkmlSql.Engine.Tests/            # Unchanged (Phase 2)
  AkmlSql.Formatting.Tests/        # NEW: Formatting pipeline, rules, profiles, CLI tests
```

## Key New Dependencies

| Package | Project | Purpose |
|---|---|---|
| Microsoft.SqlServer.TransactSql.ScriptDom 170.191.0 | AkmlSql.Formatting | T-SQL parsing and AST (shared with Phase 2 via Engine) |
| System.Text.Json 8.x | AkmlSql.Formatting | Profile JSON serialization with source generators |
| DiffPlex 1.7.x | AkmlSql.Formatter (CLI) | Diff computation for CLI diff mode and profile comparison |

## Build Commands

```bash
MSBUILD="/c/Program Files/Microsoft Visual Studio/2022/Enterprise/MSBuild/Current/Bin/MSBuild.exe"

# Formatting library (dotnet — .NET 10 class library)
dotnet build src/AkmlSql.Formatting/AkmlSql.Formatting.csproj -c Release

# Engine (references Formatting library)
dotnet build src/AkmlSql.Engine/AkmlSql.Engine.csproj -c Release
dotnet publish src/AkmlSql.Engine/AkmlSql.Engine.csproj -c Release -r win-x64

# CLI formatter (self-contained, trimmed)
dotnet build src/AkmlSql.Formatter/AkmlSql.Formatter.csproj -c Release
dotnet publish src/AkmlSql.Formatter/AkmlSql.Formatter.csproj -c Release -r win-x64

# Shell projects (still MSBuild — same as Phase 1/2)
"$MSBUILD" "src/AkmlSql.Ssms20/AkmlSql.Ssms20.csproj" -t:Restore -p:Configuration=Release -v:quiet
"$MSBUILD" "src/AkmlSql.Ssms20/AkmlSql.Ssms20.csproj" -t:Build -p:Configuration=Release -v:minimal

# Tests
dotnet test tests/AkmlSql.Formatting.Tests/AkmlSql.Formatting.Tests.csproj
dotnet test tests/AkmlSql.Core.Tests/AkmlSql.Core.Tests.csproj
```

## Development Workflow

### 1. Start with the formatting library (testable without IDE or engine)

The formatting library is a standalone .NET 10 class library. You can develop and test it independently:

```bash
# Run all formatting tests
dotnet test tests/AkmlSql.Formatting.Tests

# Run tests by category
dotnet test tests/AkmlSql.Formatting.Tests --filter "Category=Pipeline"
dotnet test tests/AkmlSql.Formatting.Tests --filter "Category=Rules"
dotnet test tests/AkmlSql.Formatting.Tests --filter "Category=Profiles"
dotnet test tests/AkmlSql.Formatting.Tests --filter "Category=Actions"
```

### 2. Test the CLI formatter independently

The CLI formatter is a standalone executable that consumes the formatting library:

```bash
# Build and run the CLI
dotnet run --project src/AkmlSql.Formatter -- --file "test.sql" --profile "Default"

# Check mode (exit code 0/1)
dotnet run --project src/AkmlSql.Formatter -- --check "test.sql"

# Diff mode
dotnet run --project src/AkmlSql.Formatter -- --diff "test.sql"

# Pipe mode
cat test.sql | dotnet run --project src/AkmlSql.Formatter -- --stdin --stdout
```

### 3. Integration test with engine

The engine hosts the formatting library and handles format requests via named pipes:

```bash
# Run engine directly for debugging
dotnet run --project src/AkmlSql.Engine -- --pipe test-pipe-1 --parent-pid 0
```

### 4. Integration test with SSMS

Deploy the extension with formatting commands:

```
Extension dir: <SSMS root>/Common7/IDE/Extensions/AkmlSql/
Engine binary: <SSMS root>/Common7/IDE/Extensions/AkmlSql/Engine/AkmlSql.Engine.exe
CLI binary:    <SSMS root>/Common7/IDE/Extensions/AkmlSql/Formatter/akmlsql-format.exe
Built-in profiles: <SSMS root>/Common7/IDE/Extensions/AkmlSql/Profiles/*.akmlstyle
```

Launch SSMS with `/log` for diagnostics, check `%AppData%/AKML SQL/logs/` for engine logs.

## Architecture Quick Reference

```
SSMS (UI thread, .NET Fx 4.7.2)
  └─ AkmlSql Shell Extension
       ├─ FormatDocumentCommand (Ctrl+K, Y)
       ├─ FormatSelectionCommand (Ctrl+K, F)
       ├─ Standalone Action Commands (Ctrl+B, Ctrl+*)
       ├─ Format-on-Paste/Save/Delimiter handlers
       ├─ ProfileEditorDialog (WPF DialogWindow)
       ├─ ProfileSelectorDropdown (toolbar)
       ├─ BulkFormatWizard (dialog)
       ├─ PipeRpcClient (sends FormatRequest to engine)
       └─ (Phase 2: IntelliSense commands unchanged)

Engine (separate process, .NET 10)
  └─ AkmlSql.Engine.exe
       ├─ PipeRpcServer (extended: dispatches format messages)
       ├─ FormatRequestHandler → AkmlSql.Formatting
       │   ├─ FormatterPipeline (6-stage: Parse → Annotate → Layout → Casing → Emit → Validate)
       │   ├─ ProfileManager (load/save/list profiles)
       │   ├─ Standalone Actions (casing, semicolons, wildcards, etc.)
       │   └─ Selection Formatter
       └─ (Phase 2: Completion, Schema, Parser unchanged)

CLI (standalone process, .NET 10)
  └─ akmlsql-format.exe
       ├─ FormatCommand / CheckCommand / DiffCommand / ProfileCommand
       └─ → AkmlSql.Formatting (same library as engine)

Communication: Named pipe with MessagePack + length-prefix framing (extended with format messages)
```

## Key Design Decisions

1. **Hybrid AST + token stream emit**: AST for structural decisions, token stream for exact comment preservation
2. **Pre-scan noformat regions before parsing**: Noformat can span arbitrary AST boundaries
3. **SQLCMD sentinel replacement**: Pre/post-process since ScriptDom has no SQLCMD mode
4. **Separate `AkmlSql.Formatting` library**: Shared between engine and CLI without coupling
5. **Programmatic WPF (no XAML)**: Cross-SDK compatibility for profile editor in Shell.Shared
6. **Modal `DialogWindow` for profile editor**: Save/Cancel semantics, available across all VS SDK versions
7. **`RichTextBox` for preview**: Lightweight, cross-SDK safe, read-only syntax-colored preview
8. **System.Text.Json source generators**: AOT/trim compatible profile serialization
9. **`[JsonExtensionData]` for profile forward compatibility**: Unknown fields preserved during round-trip
10. **Semantic validation via normalized AST comparison**: Re-parse output, compare `SqlScriptGenerator` output
