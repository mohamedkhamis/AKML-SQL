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

## 9. Spec 020 — SQL Prompt Visual Parity (Theme tokens, Format Styles editor)

Spec 020 layered on the existing theme + formatting infrastructure rather than replacing it. The new surface area:

### Theme token system (US1, builds on spec 016)

`ThemeRegistry` was extended with 25 new brush tokens and 8 invariant scalar / typography tokens, all in `src/AkmlSql.Shell.Shared/Ui/Theme/`:

| Family | Count | Source of truth for hex |
|---|---:|---|
| `IconBadge.*` (Table, View, Column, StoredProc, Function, Snippet, Keyword, Database, Schema, Trigger, Index, Synonym) | 12 | `doc/SQL-PROMPT/SQL-Prompt-Features/SQL_Prompt_Features_Core.md §1.2` |
| `TabColor.*` (Red, Amber, Green, Blue, Teal, Purple, Pink, Gray) | 8 | EnvironmentMatcher default palette |
| `History.*` (OpenIcon, ClosedIcon, StarActive, StarInactive, MatchHighlight) | 5 | `doc/SQL-PROMPT/SQL-Prompt-History/SQL_Prompt_SQL_History.md §16.2` |
| `Spacing.*` (XS / S / M / L scalars in DIU) | 4 | Theme-invariant; delegates to existing `Spacing` static class |
| `Typography.*` (Chrome, ChromeTitle, Editor, IconBadge composites) | 4 | Theme-invariant; delegates to existing `Typography` static class |

`ThemeMigrationManager.cs` writes a one-time `themeMigration.v1.json` marker at first launch; on existing-customisation detection it surfaces a notice flag.

### Format Styles editor (US3)

A new modal `DialogWindow` in `src/AkmlSql.Shell.Shared/Formatting/`:

| File | Role |
|---|---|
| `FormatStylesEditorWindow.cs` | Three-column shell: style list (left), settings tree (middle), controls + live preview (right). Programmatic WPF only (no XAML) per the `ProfileEditorDialog` pattern. |
| `FormatStylesEditorViewModel.cs` | Loads profiles via `ProfileList` IPC, schema via `RequestStyleEditorSchema` IPC, holds `_workingValues` overlaying schema defaults. Debounced `QueuePreviewAsync` (100 ms) drives the live preview. |

The editor is launched via `FormatStylesEditorWindow.Launch()`. Menu wiring (Options → Format → Styles → "Edit Formatting Styles…") is deferred to a follow-up session.

### SQL Prompt round-trip (US2)

`src/AkmlSql.Formatting/Profiles/`:

| File | Direction |
|---|---|
| `SqlPromptImporter.cs` (pre-existing) | `.sqlpromptstylev2` XML → `FormattingProfile` |
| `SqlPromptExporter.cs` (spec 020) | `FormattingProfile` → `.sqlpromptstylev2` XML |
| `FormatSettingSchema.cs` (spec 020) | Reflection-discovered descriptor; powers the editor tree |

### IPC additions

| Message | Value | Purpose |
|---|---|---|
| `RequestStyleEditorSchema` / `StyleEditorSchemaResult` | 28 / 128 | Schema descriptor request — see `doc/ipc-api.md` |

---

## 9b. Spec 021 — M0 Transport Abstraction

Spec 021 (web edition) introduced an `IRpcTransport` + `IRpcRequestHandler<TRequest, TResponse>` abstraction so the same engine handlers can serve named pipes (IDE plugins, today), in-process calls (Blazor WASM running engine logic in the browser tab; engine unit tests with zero serialisation), and WebSocket (future browser ↔ engine, M3+) without per-transport handler duplication.

**Wire format and message-type integer codes are unchanged** — existing SSMS/VS shell extensions need zero updates after the M0 refactor.

### New types (under `src/AkmlSql.Engine/`)

| Type | Path | Role |
|------|------|------|
| `IRpcTransport` | `Transports/IRpcTransport.cs` | Frame I/O + lifecycle. One impl per medium. |
| `InProcessTransport` | `Transports/InProcessTransport.cs` | Method-call dispatch, no serialisation. |
| `IRpcRequestHandler<TRequest, TResponse>` | `Transports/IRpcRequestHandler.cs` | One impl per message-type integer code. Two opt-in DIM properties: `AllowsEmptyPayload` (for messages with no payload, e.g. `ProfileList`) and `SwallowCancellation` (for handlers where OCE → null response is preferable to tearing down the pipe loop, e.g. `AnalysisHandler`). |
| `RpcRouter` | `RpcRouter.cs` | Per-process router: registers typed handlers, resolves `MessageType`, deserialises payload, dispatches. |
| `RpcContext` | `RpcContext.cs` | Per-request shared state: `Settings`, `Sessions`, `SchemaCache`, `Logger`, and (for `ConnectionChanged` only) `ParserService` + `SchemaMetadata`. |
| `TypedHandlerAdapter<TRequest, TResponse>` | `Server/TypedHandlerAdapter.cs` | Bridges new typed handlers to the legacy `IMessageHandler` dict used by `PipeRpcServer._pluggableHandlers`. Honours `AllowsEmptyPayload` and `SwallowCancellation`. |
| `DelegatingMessageHandler` | `Server/DelegatingMessageHandler.cs` | Lightweight `IMessageHandler` capturing a `Func<RpcMessage, CancellationToken, Task<RpcMessage?>>`. Used by handlers whose existing surface is `RpcMessage`-typed and doesn't need typed deserialisation (AI, History, Navigation, Productivity, etc.). |

### Handler folders

All ~50 dispatch handlers now live under `src/AkmlSql.Engine/Handlers/` grouped by category:

```
Handlers/
├── Completion/         CompletionHandler, WildcardExpansionHandler, SignatureHelpHandler, QuickInfoHandler
├── Formatting/         FormattingHandlers (9 typed wrappers) + BulkFormatHandlers (2)
├── Analysis/           AnalysisHandler (+ SwallowCancellation=true), AnalysisSettingsChangedHandler
├── Snippets/           5 handlers (Expand/List/Save/Delete/Import)
├── Refactoring/        RefactorPreviewHandler, RefactorApplyHandler (both SwallowCancellation=true)
├── Schema/             SchemaRefreshHandler, SchemaStatusHandler
├── Control/            DocumentChangedHandler, PingHandler, ShutdownHandler, ConnectionChangedHandler
└── Ai/                 AiMessageHandler bridge (used by 8 AI message types)
```

### `PipeRpcServer` after M0 (= spec 021 T020)

`PipeRpcServer` is now a `partial class` split across two files:

| File | LOC | Role |
|------|-----|------|
| `Server/PipeRpcServer.cs` | ~340 | Named-pipe lifecycle (`RunAsync`, `HandleClientAsync`), frame read/write, `DispatchAsync` (which is now just a `_pluggableHandlers` lookup + default), engine-field initialisation, and the per-request `LookupSession` / `CreatePipeSecurity` helpers. |
| `Server/PipeRpcServer.Handlers.cs` | ~242 | The `RegisterPluggableHandlers()` method — every message type is wired here. |

The original 53-case `switch` in `DispatchAsync` is empty. All dispatch flows through `_pluggableHandlers` (a `Dictionary<int, IMessageHandler>`).

Three helpers that used to live on `PipeRpcServer` were extracted to separate files:

- **`RpcResponseFactory`** (`src/AkmlSql.Engine/RpcResponseFactory.cs`) — `CreateResponse<T>` and `CreateErrorResponse`. Standalone static class, no `PipeRpcServer` dependency.
- **`SchemaRefreshService`** (`src/AkmlSql.Engine/Schema/SchemaRefreshService.cs`) — handles the manual `Ctrl+Shift+D` schema refresh. Wired into `SchemaRefreshHandler` via its `Refresh(RefreshRequest)` method.
- **`FindFunctionAtCursor`** — folded into `SignatureHelpHandler` as a private static helper (its only consumer).

The strict M0 PRD target of ≤150 LOC for the named-pipe transport is **not** met (sits at ~340 LOC); the remaining ~200 LOC are engine-field declarations, the constructor's history-DB init, the pipe accept loop, and the outer dispatch try/catch — all of which legitimately belong on the transport. The rename `PipeRpcServer` → `NamedPipeTransport` remains a Phase 2 follow-up.

### Existing IDE-plugin path is byte-for-byte compatible

The shell extensions (SSMS 20/21/22, VS 2019/22/26) send the same MessagePack frames over the same named pipe with the same ACL. No shell code was modified. The frame format `[length][CRC][MessagePack(RpcMessage)]` is unchanged.

---

## 10. Key Design Decisions

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
| Spec 021 M0 `IRpcTransport` abstraction | Same engine handlers serve named-pipe (IDE plugins) + in-process (Blazor WASM) + future WebSocket transports without per-transport duplication. Wire format unchanged for backward compat. |
| Two opt-in DIM properties on `IRpcRequestHandler<,>` (`AllowsEmptyPayload`, `SwallowCancellation`) | Lets specific handlers (ProfileList, AnalysisSettingsChanged; AnalysisHandler, RefactorPreview/Apply) opt out of default error-on-null-payload and OCE-propagation behaviour without polluting the contract for the common case. |
