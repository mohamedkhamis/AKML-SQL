# AKML SQL — Architecture Overview

## 1. High-Level Component Map

```
┌─────────────────────────────────────────────────────────────────────────┐
│  HOST IDEs  (SSMS 22 · VS 2026)                                        │
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
│  │  Compiled twice against each host's VS SDK:                      │   │
│  │    AkmlSql.Ssms22   (VS SDK 17.14, x64)                         │   │
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
│  │   ├── SuppressionMap     -- akml-disable/enable comment parsing       │
│  │   └── SessionSuppression Rules muted until the engine process exits   │
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

The editor is launched via `FormatStylesEditorWindow.Launch()` from: Tools → AKML SQL → "Format Styles..." (both hosts, `cmdFormatStyles` 0x0916), the SSMS DTE-injected fallback menu, the Command Palette (`akml.formatStyles`), and the Options Format › Styles page's "Edit formatting styles…" button (spec 033). Spec 033 also promoted the window from a preview-only browser to a full editor: load-on-select via `ProfileGet` (34) raw reads, dirty-tracked merge-saves through `ProfileSave` (15) preserving metadata/extension data, rename via `ProfileRename` (35), delete guarded against the active style, and a schema-v2 settings tree (5 SQL Prompt categories, enum dropdowns, per-setting descriptions).

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

Spec 021 (web edition) introduced an `IRpcTransport` + `IRpcRequestHandler<TRequest, TResponse>` abstraction so the same engine handlers can serve named pipes (IDE plugins, today), in-process calls (Blazor WASM running engine logic in the browser tab; engine unit tests with zero serialisation), and WebSocket (future browser ↔ engine, M3+) without per-transport handler duplication. The spec 022 closure finished the deferred polish: a single composition root, the named-pipe transport trimmed to a reference-shape file, and the cached settings single-owned by `RpcContext`.

**Wire format and message-type integer codes are unchanged** — existing SSMS/VS shell extensions need zero updates after the M0 refactor.

### New types (under `src/AkmlSql.Engine/`)

| Type | Path | Role |
|------|------|------|
| `IRpcTransport` | `Transports/IRpcTransport.cs` | Frame I/O + lifecycle. One impl per medium. |
| `NamedPipeTransport` | `Transports/NamedPipeTransport.cs` | Named-pipe accept loop, pipe ACL, framed read/write; implements `IRpcTransport`, raising `RequestReceived` for each decoded `RpcMessage`. 147 LOC — the M0 reference shape. |
| `InProcessTransport` | `Transports/InProcessTransport.cs` | Method-call dispatch, no serialisation. Used by Blazor WASM and engine unit tests. |
| `WebSocketTransport` | `Transports/WebSocketTransport.cs` | Localhost-by-default WebSocket bridge — see § 9d. |
| `IRpcRequestHandler<TRequest, TResponse>` | `Transports/IRpcRequestHandler.cs` | One impl per message-type integer code. Two opt-in DIM properties: `AllowsEmptyPayload` (for messages with no payload, e.g. `ProfileList`) and `SwallowCancellation` (for handlers where OCE → null response is preferable to tearing down the pipe loop, e.g. `AnalysisHandler`). |
| `RpcRouter` | `RpcRouter.cs` | Per-process dispatch surface. `Register<,>(handler)` wires a typed handler; `RegisterRaw(code, func)` wires a delegating handler that consumes/produces a raw `RpcMessage` itself. Resolves the message-type code, deserialises the payload, dispatches. Replaces the pre-closure `_pluggableHandlers` dictionary. |
| `RpcContext` | `RpcContext.cs` | Per-process shared state passed to every handler: `Sessions`, `SchemaCache`, `Logger`, `ParserService`, `SchemaMetadata`, and — after the spec 022 closure — the **sole** owner of the cached `AppSettings`. Handlers read settings through `EnsureSettings()` (idempotent lazy load); the `AnalysisSettingsChanged` handler drops the cache via `InvalidateSettings()`. No transport holds a settings field. |
| `EngineComposition` | `EngineComposition.cs` | The single composition root — see below. |
| `EngineHandlerRegistry` | `EngineHandlerRegistry.cs` | Static handler-registration surface — see below. |

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
└── Ai/                 AiHandlerBase + 7 per-message handlers (TextToSql, Explain, Fix, Optimize, IndexAnalysis, Chat, GhostText)
```

### `NamedPipeTransport` and the composition root (spec 022 closure)

The named-pipe transport is `src/AkmlSql.Engine/Transports/NamedPipeTransport.cs` — **147 LOC**, within the M0 PRD's ≤ 150-LOC reference-shape target. It implements `IRpcTransport` (T027) like the in-process and WebSocket transports, owning only named-pipe concerns: the pipe ACL (`CreatePipeSecurity`), the accept loop (`RunAsync` / `HandleClientAsync`), framed read/write, and the dispatch hand-off — each decoded `RpcMessage` is raised via the `RequestReceived` event, with the composition root wiring `RpcRouter.RouteAsync` as the subscriber. No service-construction or handler-registration code lives in the transport.

Service construction and handler registration moved to two new files:

- **`EngineComposition`** (`src/AkmlSql.Engine/EngineComposition.cs`) — the single composition root. `EngineComposition.Build()` constructs the `RpcContext`, an `RpcRouter` with every handler registered, and the `HistoryRetentionService`, and returns the three. `EngineHost` calls `Build()` once at startup and hands `Context` + `Router` to `new NamedPipeTransport(...)`; the in-process and (future) WebSocket transports consume the same `Build()` output — no transport replicates wiring.
- **`EngineHandlerRegistry`** (`src/AkmlSql.Engine/EngineHandlerRegistry.cs`) — a static class whose `RegisterAllHandlers(router, ctx)` is the one place every message type is wired: typed handlers via `RpcRouter.Register<,>`, delegating handlers (session, history, navigation, productivity, …) via `RpcRouter.RegisterRaw`.

The pre-closure `_pluggableHandlers` dictionary and the `PipeRpcServer` partial-class pair (`PipeRpcServer.cs` + `PipeRpcServer.Handlers.cs`) are gone — `RpcRouter` is the single dispatch surface.

Three helpers extracted during M0 stay extracted:

- **`RpcResponseFactory`** (`src/AkmlSql.Engine/RpcResponseFactory.cs`) — `CreateResponse<T>` and `CreateErrorResponse`. Standalone static class.
- **`SchemaRefreshService`** (`src/AkmlSql.Engine/Schema/SchemaRefreshService.cs`) — handles the manual `Ctrl+Shift+D` schema refresh; wired into `SchemaRefreshHandler`.
- **`FindFunctionAtCursor`** — folded into `SignatureHelpHandler` as a private static helper.

### Existing IDE-plugin path is byte-for-byte compatible

The shell extensions (SSMS 22, VS 2026) send the same MessagePack frames over the same named pipe with the same ACL. No shell code was modified. The frame format `[length][CRC][MessagePack(RpcMessage)]` is unchanged.

> **M0 closure (spec 022, 2026-05-20).** The four M0 PRD success metrics deferred when M0 merged (PR #236) — `NamedPipeTransport` ≤ 150 LOC, the `_cachedSettings` field moved onto `RpcContext`, `AiHandlerBase` + 7 per-message subclasses, and the perf-regression gate at 5 % — all landed via spec 022. See `specs/022-m0-engine-closure/` and the closure plan `docs/superpowers/plans/2026-05-19-m0-engine-transport-closure.md`.

---

## 9c. Spec 021 — Library extraction (IntelliSense / Analysis / AI)

To let Blazor WASM run the formatter, analyser, IntelliSense, and AI prompt building **in the browser tab** (without an engine), three previously engine-internal subsystems were extracted into standalone `net10.0` libraries:

| Library | Path | Source moved from | Notes |
|---------|------|-------------------|-------|
| `AkmlSql.IntelliSense` | `src/AkmlSql.IntelliSense/` | `src/AkmlSql.Engine/{Completion,Parser,Schema/{DatabaseCache,Models}}` | 32 files. Namespaces preserved (`AkmlSql.Engine.Completion.*`) so engine call sites need zero updates. `DatabaseProvider` stayed in the engine because it depends on SqlClient; `CompletionEngine` no longer hard-codes `RegisterProvider(new DatabaseProvider())` — the engine registers it externally on startup. |
| `AkmlSql.Analysis` | `src/AkmlSql.Analysis/` | `src/AkmlSql.Engine/{Analysis,Rules}` | 141 files including the 130+ rule classes. `AnalysisEngine.AnalyzeAsync` was refactored to take `(int serverVersion, DatabaseCache? schemaCache)` directly instead of `SessionManager + SchemaCacheManager`, so the library has no engine dependency. Callers resolve session/cache themselves. |
| `AkmlSql.AI` | `src/AkmlSql.AI/` | `src/AkmlSql.Engine/Ai/{Prompts,Context,Privacy,Providers,Streaming}` | 18 files. `AiRequestHandler` + `AiProviderTestHandler` + `Security/CredentialManager` stayed in the engine. Two decoupling refactors: `AiProviderFactory.KeyDecryptor` is a pluggable static `Func<string?, string>` delegate (engine wires `CredentialManager.Decrypt` at startup; web edition leaves the default identity since Web Crypto unwraps the key BEFORE calling the factory); `SchemaContextBuilder` takes a `Func<string, string, DatabaseCache?> cacheLookup` callback instead of `SchemaCacheManager`. |

Each library has a minimal smoke-test project under `tests/AkmlSql.{IntelliSense,Analysis,AI}.Tests/` that proves the surface is reachable from a project that references **only** the new library — no transitive engine dependency. Full functional coverage stays in `tests/AkmlSql.Engine.Tests/` via the transitive reference.

---

## 9d. Spec 021 — M3 WebSocket bridge

The web edition's browser tab talks to a local engine over a WebSocket. The bridge is **localhost-only by default** (`HttpListener` bound to `127.0.0.1`); LAN mode requires an installer-generated self-signed TLS cert and the explicit "Network exposure: LAN" installer choice.

| Component | Path | Role |
|-----------|------|------|
| `WebSocketTransport` | `src/AkmlSql.Engine/Transports/WebSocketTransport.cs` | `HttpListener` + `System.Net.WebSockets.WebSocket`. One WebSocket binary message = one `RpcMessage` MessagePack payload. Refuses non-loopback binding unless `TlsCertPath` is set. |
| `HandshakeHandler` | `src/AkmlSql.Engine/Handlers/Handshake/HandshakeHandler.cs` | First-frame dispatch (`MessageTypes.HandshakeRequest` = 200). Protocol-version overlap check; pairing PIN / bearer-token validation; capability advertisement. Parameterless ctor accepts any localhost inbound; full ctor takes callbacks for `pairingRequired`, `pinValidator`, `bearerValidator`, `bearerMinter`, and `serverCanonicalIdentityProvider`. |
| `PairingService` | `src/AkmlSql.Engine/Pairing/PairingService.cs` | 6-digit numeric PIN, 24-hour TTL, 5-attempts-per-minute sliding-window rate limit, constant-time compare. Emits a `PinChanged` event for the (deferred) tray-UI surface. |
| `BearerTokenStore` | `src/AkmlSql.Engine/Pairing/BearerTokenStore.cs` | SHA-256 hashes at rest only — raw tokens never touch disk. Atomic temp-file-plus-rename persistence. |
| `Capabilities` | `src/AkmlSql.Engine/Capabilities.cs` | Defines the stable capability identifiers (e.g. `core.format.v1`, `schema.cache.v1`) and the engine's currently-advertised list. The web client gates per-feature UI on the list — missing capability renders an inline notice, not a full-page blocker. |

Wire details (request/response shapes, status strings, capability table) are in [doc/ipc-api.md § Spec 021 — Web Edition Bridge Messages](ipc-api.md#spec-021--web-edition-bridge-messages).

**Engine-host composition (spec 025 FR-027)**: both `NamedPipeTransport` and `WebSocketTransport` are started concurrently when `config.Bridge.Enabled == true`; both share the same `RpcRouter` so the SSMS plugin and the web edition serve identical handler chains. When the bridge section is absent or disabled, only the named pipe runs — IDE-plugin-only deployments are byte-for-byte unchanged.

**LAN-mode TLS plumbing (spec 025 FR-001..FR-006)**: non-loopback bindings switch to `https://` prefixes and consume the installer-bound cert (`netsh http add sslcert ipport=…`). The transport's startup-time `ValidateCertBindingOrThrow` asserts PFX existence + thumbprint match against the active netsh binding before opening the listener; mismatch throws with both thumbprints in the message. The validated thumbprint flows into every `HandshakeResponse.ServerTlsThumbprint` so the browser can pin it on first connect and log a `Warn` diagnostic on drift.

**Threat model**: see [m3-security.md](m3-security.md) for the LAN-mode threat table, on-disk artefact audit, and the list of deferred follow-ups.

**Reconnect, schema tree, and E2E (spec 025 US3–US5)**: `EngineBridge` runs an exponential-backoff reconnect loop (500 ms / 2× / 30 s cap / ±100 ms jitter) that replays the stored bearer on every retry and exits to `Failed` on a `PinRequired` response (the bearer was revoked) — the browser-side counterpart to the engine's `BearerTokenStore.RevokeByHash`. A `SchemaTreeComponent` renders the cached `SchemaSnapshot` (Database → Schema → Object-Kind → Object → Column) with click-to-insert into the editor, refreshed on `ISchemaSync.ChecksumDrifted`. A real-engine E2E harness (`tests/AkmlSql.E2E.Tests/Harness/EngineLaunchFixture.cs`) covers wire-level handshake / restart / backoff via `BridgeHandshakeTests` under the opt-in `[Trait("Category","BridgeE2E")]`; the harness builds the engine, redirects AppData via `AKML_APP_DATA_ROOT`, and probes WebSocket readiness on a per-test free port. **Handshake registration fix**: spec 025 also wired `HandshakeHandler` into `EngineHandlerRegistry` — the handler existed since spec 021 T060 but was never registered with the router, so any WebSocket inbound that depended on it (every browser handshake) silently timed out. The named-pipe transport doesn't run handshakes, which is why the bug stayed latent until the first end-to-end E2E test fired.

---

## 9e. Spec 021 — M5 schema-cache identity

The web edition's offline IntelliSense uses an IndexedDB schema cache keyed by the composite pair `(serverCanonicalIdentity, databaseName)`. The pair is stable across host-string variations: two distinct DNS aliases pointing at the same SQL Server resolve to the same identity, so they share one cache entry (clarification 3 in spec.md).

The browser resolves the pair via two paths:

1. **Handshake response** — `HandshakeResponse.ServerCanonicalIdentity` covers the single-DB case (engine has exactly one connection at handshake time).
2. **SchemaIdentify request** (`MessageTypes.SchemaIdentifyRequest` = 202) — covers the multi-session case where the browser asks per `SessionId`.

The handler (`Handlers/Schema/SchemaIdentifyHandler`) is callback-pure (`Func<string, string?> databaseLookup`, `Func<string, string?> identityResolver`) so it unit-tests without a live SQL connection. Resolver exceptions are captured into the response's `ErrorMessage` rather than crashing the bridge.

Production wiring in `EngineHandlerRegistry` plugs `SessionManager` for `databaseLookup` and `SchemaIdentifyHandlerSupport.ParseServerFromConnectionString` (extracts `Data Source` / `Server` / `Address` from the SqlClient-style connection string) for the initial `identityResolver`. Swapping in the real `SELECT @@SERVERNAME` query is a follow-up — the handler surface does not change.

The `schema.cache.v1` capability is advertised in `Capabilities.Current` to signal availability.

Contract details (IndexedDB layout, change-detection polling, eviction policy, online/offline matrix) live in `specs/021-web-edition/contracts/schema-cache-shape.md`.

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
