# Quickstart: Core IntelliSense Engine

**Branch**: `002-core-intellisense-engine` | **Date**: 2026-03-19

## Prerequisites

- Windows 11 (development machine)
- Visual Studio 2022 Enterprise (for MSBuild and VS SDK)
- .NET 10 SDK (for engine and tests)
- SSMS 20 and/or SSMS 22 (for integration testing)
- SQL Server instance (LocalDB, Developer, or Azure SQL) with a sample database (AdventureWorks recommended)

## Project Structure (New in Phase 2)

```text
src/
  AkmlSql.Core/                    # Extended: IntelliSense settings, IPC message types
  AkmlSql.Engine/                  # NEW: Out-of-process IntelliSense engine (.NET 10)
  AkmlSql.Shell.Shared/            # Extended: Editor hooks, completion UI, IPC client
  AkmlSql.Ssms20/                  # Extended: Engine lifecycle management
  AkmlSql.Ssms22/                  # Extended: Engine lifecycle management
  (other shell projects...)
tests/
  AkmlSql.Core.Tests/              # Extended: Message serialization tests
  AkmlSql.Engine.Tests/            # NEW: Parser, cache, provider tests (.NET 10)
```

## Key New Dependencies

| Package | Project | Purpose |
|---|---|---|
| Microsoft.SqlServer.TransactSql.ScriptDom | AkmlSql.Engine | T-SQL parsing and AST |
| MessagePack | AkmlSql.Core | IPC message serialization (netstandard2.0) |
| System.IO.Pipes.AccessControl | AkmlSql.Engine | Named pipe ACL (security) |

## Build Commands

```bash
MSBUILD="/c/Program Files/Microsoft Visual Studio/2022/Enterprise/MSBuild/Current/Bin/MSBuild.exe"

# Engine (uses dotnet — .NET 10 project)
dotnet build src/AkmlSql.Engine/AkmlSql.Engine.csproj -c Release
dotnet publish src/AkmlSql.Engine/AkmlSql.Engine.csproj -c Release

# Shell projects (still MSBuild — same as Phase 1)
"$MSBUILD" "src/AkmlSql.Ssms20/AkmlSql.Ssms20.csproj" -t:Restore -p:Configuration=Release -v:quiet
"$MSBUILD" "src/AkmlSql.Ssms20/AkmlSql.Ssms20.csproj" -t:Build -p:Configuration=Release -v:minimal

# Tests
dotnet test tests/AkmlSql.Engine.Tests/AkmlSql.Engine.Tests.csproj
dotnet test tests/AkmlSql.Core.Tests/AkmlSql.Core.Tests.csproj
```

## Development Workflow

### 1. Start with the engine (testable without IDE)

The engine is a standalone .NET 10 console app. You can develop and test it independently:

```bash
# Run engine directly for debugging
dotnet run --project src/AkmlSql.Engine -- --pipe test-pipe-1 --parent-pid 0
```

### 2. Test parser and completion logic with unit tests

Most development is test-driven against the parser and completion providers:

```bash
dotnet test tests/AkmlSql.Engine.Tests --filter "Category=Parser"
dotnet test tests/AkmlSql.Engine.Tests --filter "Category=Completion"
dotnet test tests/AkmlSql.Engine.Tests --filter "Category=SchemaCache"
```

### 3. Integration test with SSMS

Deploy the engine alongside the SSMS extension:

```
Extension dir: <SSMS root>/Common7/IDE/Extensions/AkmlSql/
Engine binary: <SSMS root>/Common7/IDE/Extensions/AkmlSql/Engine/AkmlSql.Engine.exe
```

Launch SSMS with `/log` for diagnostics, check `%AppData%/AKML SQL/logs/` for engine logs.

## Architecture Quick Reference

```
SSMS (UI thread, .NET Fx 4.7.2)
  └─ AkmlSql Shell Extension
       ├─ IOleCommandTarget (keystroke interception)
       ├─ ICompletionSource (provides items to VS completion broker)
       ├─ ISignatureHelpSource (parameter tooltips)
       ├─ IQuickInfoSource (hover tooltips)
       ├─ PipeRpcClient (named pipe to engine)
       └─ EngineProcessManager (launch, monitor, restart)

Engine (separate process, .NET 10)
  └─ AkmlSql.Engine.exe
       ├─ PipeRpcServer (named pipe listener)
       ├─ SessionManager (per-connection state)
       ├─ SchemaMetadataService (SQL catalog queries)
       ├─ SchemaCacheManager (in-memory cache, disk persistence)
       ├─ TsqlParserService (ScriptDom two-tier: tokens + AST)
       └─ CompletionProviderChain (keyword, object, column, join, signature, quickinfo, snippet, alias, variable)

Communication: Named pipe with MessagePack + length-prefix framing
```

## Key Design Decisions

1. **Two-tier parsing**: Tokenize per keystroke (~60ms), full AST on debounce (~300ms)
2. **Legacy VS SDK APIs**: ICompletionSource (not async) for SSMS 20 compatibility
3. **Static ranking heuristics**: PK first, FK second, ordinal position. No usage tracking
4. **4-level permission degradation**: Full → NoDmv → InformationSchema → PublicOnly
5. **Silent engine startup**: No UI indicator while engine initializes
6. **Content type discovery**: Must verify SSMS T-SQL content type string at runtime
