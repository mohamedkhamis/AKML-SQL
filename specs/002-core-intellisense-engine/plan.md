# Implementation Plan: Core IntelliSense Engine

**Branch**: `002-core-intellisense-engine` | **Date**: 2026-03-19 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/002-core-intellisense-engine/spec.md`

## Summary

Deliver a schema-aware IntelliSense engine for SSMS 20/21/22 and VS 2019/2022/2026 that replaces SSMS's unreliable built-in IntelliSense. The engine runs out-of-process (.NET 10) communicating with the in-process VS extension shell (.NET Fx 4.7.2) via named pipes with MessagePack serialization. It uses Microsoft ScriptDom for T-SQL parsing (two-tier: fast tokenization per keystroke + full AST on debounce) and maintains an in-memory schema cache populated from SQL Server system catalogs. Completion providers handle keywords, objects, columns, JOINs, function signatures, Quick Info tooltips, aliases, snippets, and variables.

## Technical Context

**Language/Version**: C# / .NET Framework 4.7.2 (shell extensions) + .NET 10 (engine, tests)
**Primary Dependencies**: Microsoft.SqlServer.TransactSql.ScriptDom 170.x, MessagePack-CSharp 2.x, VS SDK 15.9.3-17.14.x (per target), System.IO.Pipes
**Storage**: In-memory schema cache + disk persistence (`%LocalAppData%/AKML SQL/cache/`), config in `%AppData%/AKML SQL/config.json`
**Testing**: xunit 2.x, Microsoft.NET.Test.Sdk 17.x (same as Phase 1)
**Target Platform**: Windows 10/11, SSMS 20 (x86) / SSMS 21-22 (x64) / VS 2019-2026
**Project Type**: VS extension (desktop-app + out-of-process service)
**Performance Goals**: <100ms p95 completion latency, <3s schema cache initial load, <50ms incremental parse
**Constraints**: <200MB engine memory (500 tables), <500MB (5000+ tables), zero IDE crashes, 2s crash recovery
**Scale/Scope**: Support databases with 10,000+ objects, documents with 10,000+ lines, 6 IDE targets, SQL Server 2016-2025 + Azure SQL

## Constitution Check

*No constitution file found. Gates skipped.*

## Project Structure

### Documentation (this feature)

```text
specs/002-core-intellisense-engine/
├── plan.md                 # This file
├── spec.md                 # Feature specification
├── research.md             # Phase 0: ScriptDom, named pipes, VS SDK, schema queries
├── data-model.md           # Phase 1: Entity model
├── quickstart.md           # Phase 1: Development setup guide
├── contracts/
│   ├── named-pipe-protocol.md      # IPC message format and lifecycle
│   └── completion-provider-interface.md  # Provider routing and interfaces
├── checklists/
│   └── requirements.md    # Spec quality checklist
└── tasks.md               # Phase 2 output (created by /speckit.tasks)
```

### Source Code (repository root)

```text
src/
├── AkmlSql.Core/                          # EXTENDED: IPC messages, IntelliSense settings
│   ├── Config/
│   │   ├── AppSettings.cs                 # Extended: IntelliSenseSettings, CacheSettings
│   │   └── ConfigManager.cs              # Unchanged
│   ├── Ipc/                              # NEW: Shared IPC types
│   │   ├── RpcMessage.cs                 # Envelope: MessageType, RequestId, Payload
│   │   ├── Messages/                     # MessagePack message contracts
│   │   │   ├── ConnectionInfo.cs
│   │   │   ├── DocumentChange.cs
│   │   │   ├── CompletionRequest.cs
│   │   │   ├── CompletionResponse.cs
│   │   │   ├── SignatureRequest.cs
│   │   │   ├── SignatureResponse.cs
│   │   │   ├── QuickInfoRequest.cs
│   │   │   ├── QuickInfoResponse.cs
│   │   │   ├── RefreshRequest.cs
│   │   │   ├── RefreshResponse.cs
│   │   │   ├── EngineStatusInfo.cs
│   │   │   └── ErrorInfo.cs
│   │   └── FrameProtocol.cs             # Length-prefix read/write helpers
│   ├── Logging/
│   └── Update/
│
├── AkmlSql.Engine/                        # NEW: Out-of-process IntelliSense engine (.NET 10)
│   ├── Program.cs                        # Entry point: parse args, start pipe server
│   ├── Server/
│   │   ├── PipeRpcServer.cs              # Named pipe listener + message dispatch
│   │   └── SessionManager.cs            # Per-connection session state
│   ├── Schema/
│   │   ├── SchemaMetadataService.cs      # SQL catalog queries (4-level degradation)
│   │   ├── SchemaCacheManager.cs         # In-memory cache + disk persistence
│   │   ├── DatabaseCache.cs              # Per-database cache with phased population
│   │   ├── ChangeDetector.cs             # CHECKSUM_AGG polling + modify_date diff
│   │   └── Models/                       # DatabaseObject, Column, ForeignKey, etc.
│   ├── Parser/
│   │   ├── TsqlParserService.cs          # Two-tier: tokenize (fast) + parse (debounced)
│   │   ├── CursorContextAnalyzer.cs      # Determine clause type, dot prefix, scope
│   │   ├── AliasResolver.cs              # Walk AST for alias→table mappings
│   │   ├── CteResolver.cs               # Resolve CTE column definitions
│   │   ├── TempTableTracker.cs           # Track #temp definitions in batch
│   │   ├── VariableTracker.cs            # Track @variable declarations
│   │   └── SuffixCompletionHelper.cs     # Append dummy tokens for partial SQL
│   ├── Completion/
│   │   ├── CompletionEngine.cs           # Provider chain router
│   │   ├── FuzzyMatcher.cs              # Prefix, CamelCase, substring matching
│   │   ├── Providers/
│   │   │   ├── KeywordProvider.cs        # Context-aware keyword suggestions
│   │   │   ├── ObjectProvider.cs         # Tables, views, procs, functions
│   │   │   ├── ColumnProvider.cs         # Columns with PK/FK ranking
│   │   │   ├── JoinProvider.cs           # FK-based JOIN + ON clause generation
│   │   │   ├── SignatureProvider.cs      # Function/proc parameter signatures
│   │   │   ├── QuickInfoProvider.cs      # Hover tooltip metadata
│   │   │   ├── AliasProvider.cs          # Auto-suggest aliases from table names
│   │   │   ├── SnippetProvider.cs        # Basic built-in snippets
│   │   │   └── VariableProvider.cs       # @variable completions
│   │   └── Dictionaries/
│   │       ├── KeywordDictionary.cs      # Version-aware T-SQL keywords
│   │       ├── BuiltinFunctionDictionary.cs  # Built-in function signatures
│   │       └── SystemProcDictionary.cs   # sp_, xp_, fn_ prefixed procs
│   └── AkmlSql.Engine.csproj            # .NET 10, self-contained
│
├── AkmlSql.Shell.Shared/                 # EXTENDED: Editor hooks, completion UI, IPC client
│   ├── Editor/                           # NEW: VS SDK editor integration
│   │   ├── TextViewCreationListener.cs   # MEF: IWpfTextViewCreationListener
│   │   ├── CompletionSource.cs           # MEF: ICompletionSource + ICompletionSourceProvider
│   │   ├── SignatureHelpSource.cs        # MEF: ISignatureHelpSource
│   │   ├── QuickInfoSource.cs            # MEF: IQuickInfoSource
│   │   ├── CompletionCommandHandler.cs   # IOleCommandTarget for keystroke interception
│   │   └── ContentTypeDetector.cs        # Runtime T-SQL content type discovery
│   ├── Ipc/                             # NEW: Client-side IPC
│   │   ├── PipeRpcClient.cs             # Named pipe client + request multiplexing
│   │   └── EngineProcessManager.cs      # Launch, monitor, restart engine process
│   ├── Ui/                              # NEW: Completion popup
│   │   ├── CompletionPopup.xaml(.cs)    # WPF popup with fuzzy filter
│   │   ├── CompletionItemViewModel.cs   # Item display model
│   │   ├── ThemeManager.cs             # VSColorTheme integration
│   │   └── DpiHelper.cs               # Multi-monitor DPI handling
│   ├── IntelliSense/                    # NEW: Native IntelliSense handling
│   │   └── NativeIntelliSenseManager.cs # Detect, disable, restore SSMS IntelliSense
│   ├── Commands/                        # Existing Phase 1 commands
│   ├── Dialogs/
│   ├── StatusBar/
│   ├── Update/
│   └── Validation/
│
├── AkmlSql.Ssms20/                       # Unchanged structure, imports Shell.Shared
├── AkmlSql.Ssms22/                       # Unchanged structure, imports Shell.Shared
├── (other shell projects...)
├── AkmlSql.Updater/                      # Unchanged
└── AkmlSql.Installer/                    # Extended: deploy engine binary

tests/
├── AkmlSql.Core.Tests/                   # Extended: IPC message serialization tests
└── AkmlSql.Engine.Tests/                 # NEW: Engine test project (.NET 10)
    ├── Parser/
    │   ├── CursorContextAnalyzerTests.cs
    │   ├── AliasResolverTests.cs
    │   ├── CteResolverTests.cs
    │   ├── TempTableTrackerTests.cs
    │   └── SuffixCompletionHelperTests.cs
    ├── Completion/
    │   ├── KeywordProviderTests.cs
    │   ├── ObjectProviderTests.cs
    │   ├── ColumnProviderTests.cs
    │   ├── JoinProviderTests.cs
    │   ├── FuzzyMatcherTests.cs
    │   └── CompletionEngineTests.cs
    ├── Schema/
    │   ├── SchemaCacheManagerTests.cs
    │   ├── ChangeDetectorTests.cs
    │   └── PermissionDegradationTests.cs
    └── Ipc/
        ├── FrameProtocolTests.cs
        └── MessageSerializationTests.cs
```

**Structure Decision**: The engine is a new standalone .NET 10 project (`AkmlSql.Engine`) — the largest addition. Shared IPC types go in `AkmlSql.Core` (netstandard2.0 + net10.0 dual-target, accessible from both shell and engine). Editor hooks and completion UI go in `AkmlSql.Shell.Shared` (compiled into each shell extension). This extends the existing Phase 1 structure without modifying its architecture.

## Complexity Tracking

| Aspect | Justification | Simpler Alternative Rejected Because |
|---|---|---|
| Out-of-process engine | Required by spec (FR-018): non-blocking UI, crash isolation, independent .NET runtime | In-process would block SSMS UI thread during schema queries and heavy parsing |
| Two-tier parsing | ScriptDom full parse ~300ms on large docs exceeds 100ms keystroke budget | Full parse only would miss the performance target; tokenize-only would lack alias/CTE resolution |
| Named pipe + MessagePack | Binary protocol needed for sub-ms IPC; MessagePack 2-4x faster than JSON | JSON-over-pipes would add serialization overhead; gRPC adds heavy dependencies for .NET Fx 4.7.2 |
| Legacy VS SDK APIs | SSMS 20 (VS SDK 15.x) only supports synchronous ICompletionSource | Async APIs would require separate code paths per target or dropping SSMS 20 support |
| 4-level permission degradation | Enterprise environments have varied permission models | Single permission path would fail silently for restricted users |
