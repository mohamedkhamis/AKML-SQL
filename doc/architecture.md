# AKML SQL — Architecture Overview

## 1. High-Level Component Map

```
┌─────────────────────────────────────────────────────────────────────────┐
│  HOST IDEs  (SSMS 20/21/22 · VS 2019/2022/2026)                        │
│                                                                          │
│  ┌──────────────────────────────────────────────────────────────────┐   │
│  │  Shell Extension  (.NET Framework 4.7.2 VSIX)                    │   │
│  │                                                                  │   │
│  │  AkmlSql.Shell.Shared (.projitems — shared source)               │   │
│  │  ├── Commands/        Menu commands (Format, Analyze, …)         │   │
│  │  ├── Dialogs/         WinForms UI (Settings, LogViewer, …)       │   │
│  │  ├── Formatting/      Format-on-type/paste/save triggers          │   │
│  │  ├── Analysis/        Squiggle / error-list integration           │   │
│  │  ├── IntelliSense/    Native IntelliSense conflict manager        │   │
│  │  ├── Update/          Update launcher & result reader             │   │
│  │  └── Ipc/             PipeRpcClient + EngineProcessManager        │   │
│  │                                                                  │   │
│  │  Compiled six times against different VS SDK versions:           │   │
│  │    AkmlSql.Ssms20   (VS SDK 15.9.3, x86)                        │   │
│  │    AkmlSql.Ssms21   (VS SDK 17.14, x64)                         │   │
│  │    AkmlSql.Ssms22   (VS SDK 17.14, x64)                         │   │
│  │    AkmlSql.VS2019   (VS SDK 16.0, x86)                          │   │
│  │    AkmlSql.VS2022   (VS SDK 17.14, x64)                         │   │
│  │    AkmlSql.VS2026   (VS SDK 17.14, x64)                         │   │
│  └──────────────────────────────────────────────────────────────────┘   │
│                │ Named Pipe (owner-SID ACL, MessagePack frames)          │
└───────────────────────────────────────────────────────────────────────── ┘
                 │
                 ▼
┌────────────────────────────────────────────────────────────────────────┐
│  AkmlSql.Engine  (.NET 10, self-contained, single-file, win-x64)       │
│                                                                         │
│  PipeRpcServer                                                          │
│  ├── SessionManager         Active editor sessions + document text      │
│  ├── TsqlParserService      Thread-safe TSql170Parser wrapper           │
│  ├── CompletionEngine       IntelliSense completions                    │
│  │   ├── KeywordProvider                                                │
│  │   ├── SchemaProvider     Uses SchemaCacheManager                     │
│  │   ├── SnippetProvider                                                │
│  │   └── FunctionProvider                                               │
│  ├── SignatureProvider       Parameter info                             │
│  ├── QuickInfoProvider       Hover tooltips                             │
│  ├── FormatRequestHandler   Delegates to FormatterPipeline              │
│  │   └── BulkFormatter      Parallel file-level formatting              │
│  ├── SnippetRequestHandler  Expand / list / save / delete               │
│  │   ├── SnippetLoader      Reads .akmlsnippet JSON files               │
│  │   └── SnippetIndex       Shortcode / category / search index         │
│  ├── AnalysisEngine         Static code analysis (70+ rules)            │
│  │   ├── RuleRegistry       Discovers all IAnalysisRule implementations  │
│  │   ├── CaSettingsLoader   Per-project .casettings overrides           │
│  │   └── SuppressionMap     -- akml-disable/enable comment parsing       │
│  ├── RefactoringEngine      Preview + apply refactoring operations       │
│  │   ├── Lightweight ops    Pure-text transforms (no schema needed)      │
│  │   └── Heavyweight ops    Schema-aware transforms                      │
│  └── SchemaCacheManager     Server:database → DatabaseCache              │
│      ├── SchemaMetadataService  Phase A/B population via sys.* views    │
│      └── ChangeDetector     Periodic schema-change detection             │
│                                                                         │
│  AkmlSql.Formatting  (referenced by Engine)                             │
│  ├── FormatterPipeline      7-stage formatting pipeline                 │
│  ├── NoformatScanner        -- noformat / -- endnoformat regions         │
│  ├── SqlcmdPreprocessor     :r / :setvar directive handling              │
│  ├── AstAnnotator           Attaches comments to AST nodes              │
│  ├── LayoutEngine           Builds whitespace/newline IR                 │
│  ├── CasingEngine           Keyword & identifier casing                  │
│  ├── TextEmitter            IR → final string                            │
│  ├── SemanticValidator      Round-trips parse to verify equivalence      │
│  └── ProfileManager         Load / save / list .akmlstyle profiles       │
└────────────────────────────────────────────────────────────────────────┘

┌─────────────────────────────────────────────────────────────────────────┐
│  AkmlSql.Core  (netstandard2.0 + net10.0)  — shared by all             │
│  ├── IPC types              RpcMessage, FrameProtocol, MessageTypes     │
│  ├── Message models         All request/response POCOs (MessagePack)     │
│  ├── Config                 AppSettings, ConfigManager                   │
│  ├── Update models          UpdateManifest, UpdateResult                │
│  └── Logging                LoggerFactory (Serilog wrapper)              │
└─────────────────────────────────────────────────────────────────────────┘

┌─────────────────────────────────────────────────────────────────────────┐
│  AkmlSql.Updater  (.NET 10, self-contained)                             │
│  Launched by shell → fetches manifest JSON → writes update-available.json│
└─────────────────────────────────────────────────────────────────────────┘

┌─────────────────────────────────────────────────────────────────────────┐
│  AkmlSql.Installer  (Inno Setup 7)                                      │
│  Detects installed IDEs → copies extension files → clears MEF caches    │
└─────────────────────────────────────────────────────────────────────────┘
```

---

## 2. Process Boundary: Shell ↔ Engine

The shell extension and the engine run in **separate processes**. The shell is hosted inside a .NET Framework 4.7.2 VS/SSMS process; the engine is a .NET 10 self-contained executable.

### Why out-of-process?

| Concern | Solution |
|---------|----------|
| .NET Framework ↔ .NET 10 incompatibility | Separate processes, shared message protocol |
| Engine crash isolation | VS/SSMS never sees engine crashes |
| Native AOT / trimming | Engine can be trimmed; shell cannot |
| Dependency conflicts | Engine uses modern libs freely |

### Pipe naming

```
akmlsql-engine-{user-SID}-{shell-PID}
```

Each shell process gets its own named pipe. The pipe ACL allows only the current user's SID and explicitly denies the Network SID.

### Frame protocol

```
┌─────────┬─────────┬──────────────────────────────────┐
│ 4 bytes │ 4 bytes │ N bytes                           │
│ Length  │ XOR CRC │ MessagePack(RpcMessage)           │
└─────────┴─────────┴──────────────────────────────────┘
```

`RpcMessage` carries three fields:
- `MessageType` (int) — identifies the operation
- `RequestId` (int) — correlates request to response (0 for notifications)
- `Payload` (byte[]) — MessagePack-serialized request or response object

Max frame size: **16 MB**.

---

## 3. Shell Extension Startup Sequence

```
IDE loads VSIX
    → Package.InitializeAsync()
        → LoggerFactory.Initialize()
        → ConfigManager.Load()
        → LoadValidator.Validate()           (extension files present?)
        → NativeIntelliSenseManager.CheckAndPromptOnFirstLoad()
        → EngineProcessManager.LaunchAsync() → spawns AkmlSql.Engine.exe
        → PipeRpcClient.ConnectAsync()       (retry up to 5×, 200ms apart)
        → Register menu commands
        → Register text-view event handlers  (format-on-type, analysis triggers)
        → SchemaCacheManager.StartPeriodicRefresh()
```

Engine crashes trigger automatic restart via `Process.Exited` event, up to 5 times.

---

## 4. IntelliSense Data Flow

```
User types SQL
    → AnalysisController.OnTextChanged() (debounced 300ms)
        → PipeRpcClient.SendAsync(RequestAnalyze)
            → Engine: AnalysisEngine.AnalyzeAsync()
                → Parse AST (TsqlParserService)
                → Run enabled rules against AST
                → Resolve suppressions
                → Return CodeAnalysisResponse
        → Shell: render squiggles + error list entries

User requests completion (Ctrl+Space / auto)
    → PipeRpcClient.SendAsync(RequestCompletion)
        → Engine: CompletionEngine.GetCompletions()
            → Parse tokens at cursor
            → Merge: keywords + schema objects + snippets + functions
            → Return CompletionResponse (ranked list)
        → Shell: show VS completion list
```

---

## 5. Schema Cache Lifecycle

```
ConnectionChanged notification
    → SchemaCacheManager.GetOrCreateCache(server, db)
    → If Phase == NotLoaded:
        Task.Run → SchemaMetadataService.PopulatePhaseAAsync()
            → sys.objects + sys.schemas + sys.partitions (< 500ms target)
            → DatabaseCache.Phase = PhaseA

Background (after Phase A):
    → SchemaMetadataService.PopulatePhaseBAsync()
        → LoadColumnsAsync()      (sys.columns + joins)
        → LoadForeignKeysAsync()  → RebuildFkIndex()
        → LoadParametersAsync()   (sys.parameters)
        → LoadDescriptionsAsync() (sys.extended_properties)
        → DatabaseCache.Phase = PhaseB

Periodic refresh (default: 60s):
    → ChangeDetector.CheckForChangesAsync()
        → CHECKSUM_AGG(BINARY_CHECKSUM(object_id, modify_date, create_date, type))
        → If checksum changed → cache.IsStale = true

DDL detection:
    → ChangeDetector.DetectDdlInQuery(queryText)
        → Regex: ^\s*(CREATE|ALTER|DROP)\s+(TABLE|VIEW|...)
        → If matched → trigger immediate Phase A refresh
```

---

## 6. Formatting Pipeline

```
Input SQL
    Stage 0a: NoformatScanner      → noformat regions
    Stage 0b: SqlcmdPreprocessor   → replace :r/:setvar with placeholders
    Stage 1:  TSql170Parser        → TSqlScript AST
    Stage 2:  AstAnnotator         → attach comments to AST nodes
    Stage 3:  LayoutEngine         → LayoutNode list (tokens + whitespace rules)
    Stage 4:  CasingEngine         → apply keyword/identifier case from profile
    Stage 5:  TextEmitter          → formatted string
    Stage 5b: SqlcmdPreprocessor   → restore SQLCMD placeholders
    Stage 6:  SemanticValidator    → re-parse & normalize both; compare
    Stage 7:  IdempotencyCheck     → format again; verify identical output
Output FormattedSQL
```

Failed validation (stage 6) → returns original SQL unchanged.
Failed idempotency (stage 7) → adds a diagnostic warning, still returns formatted SQL.

---

## 7. Update Flow

```
Shell startup (or user clicks "Check for Updates")
    → UpdateLauncher.LaunchUpdaterAndWait(15s timeout)
        → Spawns AkmlSql.Updater.exe --check
            → HTTPS GET https://updates.akmlsql.com/manifest.json
            → Compares manifest.Version with Constants.Version (SemVer)
            → If newer: writes %AppData%\AKML SQL\update-available.json
            → If same or older: deletes stale result file

Shell reads update-available.json
    → Shows "Update available" dialog if Available == true
    → User clicks Yes → opens DownloadUrl in default browser
```

---

## 8. Installer Flow

```
AKMLSQLSetup.exe
    → DetectInstalledIDEs()  (registry + vswhere + filesystem)
    → For each detected target:
        → Copy extension files to IDE extensions directory
        → Clear MEF / component-model cache
    → Write %AppData%\AKML SQL\config.json  (if absent)
    → Register uninstall entry

Silent mode flags:
    /VERYSILENT /ACCEPTEULA /TARGETS=20,22,2022 /NOUPDATE
```

---

## 9. Key Design Decisions

| Decision | Rationale |
|----------|-----------|
| Out-of-process engine | .NET version isolation; crash safety; trimming |
| Shared .projitems | One source compiles against 6 different VS SDKs |
| MessagePack for IPC | ~3× faster + smaller than JSON; strongly typed |
| ConcurrentDictionary for schema cache | Lock-free reads; multiple background writers |
| Phase A / Phase B schema loading | Phase A < 500ms for fast first completion; Phase B in background |
| Atomic config writes (File.Replace / File.Move overwrite) | Prevents partial-write corruption |
| Named pipe + SID ACL | Local-only IPC; no network exposure |
| CHECKSUM_AGG for change detection | Single scalar query vs full re-read |
| ProfileMetadata.SkipValidation | Allows test pipelines to bypass semantic round-trip |
| EnableIdempotencyCheck flag | Allows bulk operations to skip the expensive second parse pass |
