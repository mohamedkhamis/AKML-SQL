# Phase 1 — Data Model: M0 Engine Transport Closure

The closure is an internal refactor; it does not introduce persistent data. Every "entity" below is an in-memory object whose shape this work pins down or changes. The model captures the post-closure shape: the contract a future maintainer (or a future transport author) sees when they read the engine source.

---

## Entity 1: `RpcContext` (modified)

**Type**: `sealed class`, public, namespace `AkmlSql.Engine`

**Responsibility**: Per-process shared state injected into every handler invocation. Sole owner of the cached `AppSettings` after this closure.

**Fields / properties** (post-closure):

| Name | Type | Init mode | Notes |
|---|---|---|---|
| `Sessions` | `SessionManager` | `required init` | Active editor sessions; unchanged from spec 021 |
| `SchemaCache` | `SchemaCacheManager` | `required init` | Per-database schema caches; unchanged |
| `Logger` | `Serilog.ILogger` | `required init` | Engine logger; unchanged |
| `SettingsLoader` | `Func<AppSettings>` | `required init` | **NEW (P1)** — on-disk loader callback supplied by `EngineComposition` |
| `ParserService` | `TsqlParserService?` | `init` (optional) | Unchanged; needed by `ConnectionChangedHandler` |
| `SchemaMetadata` | `SchemaMetadataService?` | `init` (optional) | Unchanged; needed by Phase A/B population |
| `_cachedSettings` | `AppSettings?` | private field | **NEW (P1)** — sole cached copy; populated by `EnsureSettings()`, cleared by `InvalidateSettings()` |
| `_settingsLock` | `object` | private field, `new()` | **NEW (P1)** — guards `_cachedSettings` |

**Removed**: `Settings { get; set; }` public property (replaced by `EnsureSettings`)

**Methods** (post-closure):

| Signature | Behaviour |
|---|---|
| `AppSettings EnsureSettings()` | **NEW (P1)** Returns the cached settings; calls `SettingsLoader()` exactly once on first call. Thread-safe via the lock. |
| `void InvalidateSettings()` | **NEW (P1)** Drops the cached reference; next `EnsureSettings()` call re-invokes the loader. Thread-safe. |

**Validation rules**:
- `SettingsLoader` MUST be non-null (`required init` enforces this at construction).
- `EnsureSettings()` MUST be idempotent and MUST return the same instance across calls until `InvalidateSettings()` is called.
- `InvalidateSettings()` MUST be safe to call concurrently with `EnsureSettings()`; the next call returns a fresh instance, not a torn read.

**State transitions** (`_cachedSettings`):

```text
null ──EnsureSettings()──▶ {AppSettings instance} ──InvalidateSettings()──▶ null
                                    ▲                                          │
                                    └─────────────EnsureSettings()─────────────┘
```

---

## Entity 2: `NamedPipeTransport` (renamed; modified)

**Type**: `sealed class`, public, namespace `AkmlSql.Engine.Transports`

**Replaces**: `PipeRpcServer` in `Server/PipeRpcServer.cs` + `Server/PipeRpcServer.Handlers.cs`

**Responsibility**: Named-pipe lifecycle and frame I/O only. Pipe ACL configuration, accept loop, framed read/write, dispatch hand-off to `RpcRouter`. No service construction, no handler registration.

**Fields**:

| Name | Type | Init mode |
|---|---|---|
| `_pipeName` | `string` | `readonly`, constructor |
| `_ctx` | `RpcContext` | `readonly`, constructor |
| `_router` | `RpcRouter` | `readonly`, constructor |

**Removed (was on `PipeRpcServer`)**:
- `_cachedSettings` field — moved to `RpcContext` (Entity 1)
- `_sessionManager`, `_parserService`, `_completionEngine`, ... all ~20 service fields — moved to `EngineComposition` (Entity 3)
- `_pluggableHandlers` dictionary — replaced by `RpcRouter` dispatch
- `_rpcContext` field — superseded by `_ctx` (same role, cleaner name)
- `RegisterPluggableHandlers()` partial method — moved to `EngineHandlerRegistry` (Entity 4)
- `LookupSession()` private method — moved to a closure inside `EngineHandlerRegistry` so AI/navigation handlers can call it

**Methods** (post-closure, target ≤ 150 LOC total):

| Signature | Behaviour |
|---|---|
| `NamedPipeTransport(string pipeName, RpcContext ctx, RpcRouter router)` | Constructor — stores fields, no work. |
| `Task StartAsync(CancellationToken ct)` | Implements `IRpcTransport.StartAsync`. Begins the accept loop. |
| `Task RunAsync(CancellationToken ct)` | Existing public entry point retained for `EngineHost`. Drives `StartAsync` and awaits the accept loop. |
| `private async Task HandleClientAsync(NamedPipeServerStream pipe, CancellationToken ct)` | Framed read/dispatch/write loop. |
| `private async Task<RpcMessage?> DispatchAsync(RpcMessage message, CancellationToken ct)` | Thin wrapper around `_router.RouteAsync(message, _ctx, ct)` with outer try/catch for error envelopes. |
| `private static PipeSecurity CreatePipeSecurity()` | Named-pipe ACL (owner-allow, NetworkSid-deny); unchanged from current implementation. |
| `event Func<RpcMessage, CancellationToken, Task<RpcMessage?>>? RequestReceived` | Implements `IRpcTransport.RequestReceived` — fired internally from `DispatchAsync`; subscribers (e.g. `RpcRouter`) write the response back. |
| `ValueTask DisposeAsync()` | Implements `IAsyncDisposable`; tears down the accept loop. |

**LOC budget**: ≤ 150 lines, verified by `wc -l src/AkmlSql.Engine/Transports/NamedPipeTransport.cs`.

---

## Entity 3: `EngineComposition` (new)

**Type**: `sealed class`, public, namespace `AkmlSql.Engine`

**Responsibility**: Composition root. Single place that builds every engine service, constructs the shared `RpcContext`, creates the `RpcRouter`, runs handler registration via `EngineHandlerRegistry`, and returns the built objects to the caller.

**Properties** (immutable post-build):

| Name | Type | Notes |
|---|---|---|
| `Context` | `RpcContext` | The shared context every transport uses |
| `Router` | `RpcRouter` | The router every transport routes through |
| `HistoryRetention` | `HistoryRetentionService` | Started by the host after composition; not owned by any transport |

**Removed** (compared to old `PipeRpcServer` constructor body):
- All ~20 engine service fields move into `EngineHandlerRegistry` as constructor locals — they survive only as long as the registry call.

**Static factory**:

| Signature | Behaviour |
|---|---|
| `static EngineComposition Build()` | Idempotent within a process (called once by `EngineHost`). Constructs `SessionManager`, `TsqlParserService`, `SchemaCacheManager`, `SchemaMetadataService`; builds `RpcContext` with `SettingsLoader = ConfigManager.Load`; constructs `RpcRouter`; instantiates `EngineHandlerRegistry(ctx)` and calls `RegisterAllHandlers(router)`; constructs the `HistoryRetentionService` for the host to start. |

---

## Entity 4: `EngineHandlerRegistry` (new)

**Type**: `internal sealed class`, namespace `AkmlSql.Engine`

**Responsibility**: Hold the verbatim content of the current `PipeRpcServer.Handlers.cs` registration block — one place where every shell-to-engine message type is wired to its handler.

**Constructor**:

| Signature | Behaviour |
|---|---|
| `EngineHandlerRegistry(RpcContext ctx)` | Stores the context (handlers receive it at invocation time, not registration time). |

**Methods**:

| Signature | Behaviour |
|---|---|
| `void RegisterAllHandlers(RpcRouter router)` | Instantiates every engine service the handlers need (parser, schema cache, completion engine, format handler, refactoring engine, AI pipeline services, ...), then calls `router.Register<TReq, TResp>(...)` for every typed handler and `router.RegisterRaw(...)` for every delegating handler. ~ 350 LOC overall — sized identically to the current `PipeRpcServer.Handlers.cs`. |

**Identity to current code**: the post-closure body of `RegisterAllHandlers` is a near-identity transformation of `PipeRpcServer.Handlers.cs:19–352`, with three mechanical changes documented in the closure plan (Task 4 step 2):
- partial-class qualifier removed; class becomes `internal sealed class EngineHandlerRegistry`
- `_pluggableHandlers[X] = TypedHandlerAdapter(...)` becomes `router.Register(handler)`
- `_pluggableHandlers[X] = new DelegatingMessageHandler(...)` becomes `router.RegisterRaw(X, lambda)`

---

## Entity 5: `AiHandlerBase<TRequest, TResponse>` (new)

**Type**: `public abstract class`, namespace `AkmlSql.Engine.Handlers.Ai`

**Implements**: `IRpcRequestHandler<TRequest, TResponse>` from spec 021

**Responsibility**: Template-method base for all AI message handlers. Lifts cross-handler boilerplate (privacy-consent check, settings retrieval through the shared context, error-envelope-on-throw, `SwallowCancellation = true` default) out of every concrete handler.

**Generic constraints**:
- `TResponse : new()` — base needs a zero-arg constructor to return an empty response when `SwallowCancellation` swallows an OCE.

**Fields**:

| Name | Type | Visibility |
|---|---|---|
| `Services` | `AiPipelineServices` | `protected` |
| `Log` | `Serilog.ILogger` | `protected` |

**Properties** (abstract):

| Name | Type | Notes |
|---|---|---|
| `RequestMessageType` | `int` | Each concrete subclass returns its specific code (e.g. `MessageTypes.AiTextToSql`). |
| `ResponseMessageType` | `int` | Each concrete subclass returns its specific code (e.g. `MessageTypes.AiTextToSqlResponse`). |

**Properties** (virtual):

| Name | Type | Default | Notes |
|---|---|---|---|
| `SwallowCancellation` | `bool` | `true` | Override to `false` in subclasses that need OCE to bubble. |
| `AllowsEmptyPayload` | `bool` | `false` | Override per-subclass if no payload arrives. |

**Methods**:

| Signature | Behaviour |
|---|---|
| `protected abstract Task<TResponse> InvokeAsync(TRequest request, RpcContext ctx, CancellationToken ct)` | Per-message logic. Subclasses override only this and the two integer-code properties. |
| `public async Task<TResponse> HandleAsync(TRequest, RpcContext, CancellationToken)` | Implements `IRpcRequestHandler<,>.HandleAsync`. Runs the consent gate via `Services.SettingsProvider()`, then `await InvokeAsync(...)`. On OCE when `SwallowCancellation` returns `true`, returns `new TResponse()`. |
| `private static void CheckPrivacyConsent(AiSettings)` | Local-provider allowlist (`ollama`, `lmstudio`) skips. Cloud providers throw `PrivacyConsentRequiredException` when `PrivacyConsentRequired = true`. |

**Validation rules**:
- `Services` MUST be non-null (constructor argument with `ArgumentNullException` check).
- `HandleAsync` MUST NOT bypass the consent gate; subclasses cannot override `HandleAsync` directly (the method is `sealed` in code).
- Subclasses MUST return a non-null `TResponse` from `InvokeAsync` unless they explicitly want the swallow-cancellation default.

---

## Entity 6: AI handler subclasses (seven new classes)

**Common shape**: each is a `public sealed class : AiHandlerBase<XxxRequest, XxxResponse>` with:
- a constructor `(AiPipelineServices services)` forwarding to `base(services)`
- overrides for `RequestMessageType` / `ResponseMessageType`
- one override `protected override Task<XxxResponse> InvokeAsync(XxxRequest, RpcContext, CancellationToken)` containing only the per-message logic

**LOC budget**: each file ≤ 80 lines including `using`s, namespace, and the type declaration.

| Class | Message-type pair | Notes |
|---|---|---|
| `AiTextToSqlHandler` | `AiTextToSql` / `AiTextToSqlResponse` | Translates natural-language prompts into SQL using schema context. |
| `AiExplainHandler` | `AiExplain` / `AiExplainResponse` | Generates a human-readable explanation of a SQL block. |
| `AiFixHandler` | `AiFix` / `AiFixResponse` | Returns a corrected SQL block given a diagnostic. |
| `AiOptimizeHandler` | `AiOptimize` / `AiOptimizeResponse` | Suggests optimisations to a SQL block. |
| `AiIndexAnalysisHandler` | `AiIndexAnalysis` / `AiIndexAnalysisResponse` | Recommends indexes for a SQL query against schema context. |
| `AiChatHandler` | `AiChat` / `AiChatResponse` | Multi-turn SQL-aware chat. Streams partial responses today; closure preserves the streaming contract. |
| `AiGhostTextHandler` | `AiGhostText` / `AiGhostTextResponse` | Low-latency next-token suggestions. `SwallowCancellation = true` is critical here. |

**Replaces**: the seven `Handle{TextToSql,Explain,Fix,Optimize,IndexAnalysis,Chat,GhostText}Async` methods on the now-deleted `AiRequestHandler` monolith.

---

## Entity 7: `AiPipelineServices` (new)

**Type**: `public sealed class`, namespace `AkmlSql.Engine.Ai`

**Responsibility**: Shared collaborators that the seven AI handler subclasses need. Replaces the constructor block + private helpers of the old `AiRequestHandler` monolith.

**Properties** (immutable, `required init`):

| Name | Type | Notes |
|---|---|---|
| `SchemaContext` | `SchemaContextBuilder` | Existing type; builds prompt context from cached schema |
| `Privacy` | `PrivacyTransformer` | Existing type; redacts identifiers per the privacy mode |
| `Parser` | `TsqlParserService` | Existing type; used by privacy transformer and a few subclasses for AST inspection |
| `SettingsProvider` | `Func<AiSettings>` | Callback that resolves to `ctx.EnsureSettings().Ai` — i.e. fresh on every invocation, no stale copy |

**Static factory**:

| Signature | Behaviour |
|---|---|
| `static AiPipelineServices Build(SchemaCacheManager schemaCache, TsqlParserService parser, Func<AiSettings> settingsProvider)` | Constructs the existing `SchemaContextBuilder` over a `(connectionString, databaseName) → DatabaseCache?` lookup that captures `schemaCache`. Constructs `PrivacyTransformer` over the parser. Wires `SettingsProvider`. |

**Static helper**:

| Signature | Behaviour |
|---|---|
| `static Task<T> ExecuteWithBackoffAsync<T>(Func<Task<T>> action, int maxRetries = 3, CancellationToken ct = default)` | Existing retry-with-exponential-backoff (T061 from Phase 9) — moved out of the monolith. Subclasses opt in per-call. |

**Removed**: the field-storage role of `AiRequestHandler` (private `_aiSettings`, `_schemaContextBuilder`, etc.). Those instances live as immutable properties on `AiPipelineServices` now.

---

## Entity 8: `RpcRouter` (modified)

**Type**: `sealed class`, public, namespace `AkmlSql.Engine`

**Existing surface** (unchanged):
- `Register<TReq, TResp>(IRpcRequestHandler<TReq, TResp>)` — typed registration
- `IsRegistered(int)` — query
- `RegisteredMessageTypes` — enumeration
- `RegisterAllInAssembly(Assembly?)` — reflection discovery
- `RouteAsync(RpcMessage, RpcContext, CancellationToken)` — dispatch surface
- Private `IHandlerAdapter` + `TypedHandlerAdapter<TReq, TResp>` — unchanged

**New surface** (P2):

| Signature | Behaviour |
|---|---|
| `void RegisterRaw(int messageType, Func<RpcMessage, CancellationToken, Task<RpcMessage?>> handler)` | Adds a `RawHandlerAdapter` to the dispatch map. Throws `InvalidOperationException` on duplicate `messageType` (same semantics as `Register<,>`). |
| `private sealed class RawHandlerAdapter : IHandlerAdapter` | Wraps the function. `RouteAsync` invokes the function directly, bypassing MessagePack on both sides. |

**Validation rules**:
- `RegisterRaw` and `Register<,>` MUST NOT both register the same `messageType` — second call throws.
- The `RawHandlerAdapter` returns the function's response verbatim (no envelope rewrap). Function returning `null` means "notification, no reply" — same semantics as today's `null` response from typed handlers.

---

## Entity 9: `PerformanceBaselineTests` (modified)

**Type**: existing test class, namespace `AkmlSql.Engine.Tests`

**Fields** (modified, P4):

| Name | Old value | New value | Notes |
|---|---|---|---|
| `MaxRegressionFraction` | `0.25` | `0.05` | The PRD-target threshold |
| `MeasureIterations` | `50` | `50` (initial) | May rise to `200` if 5 % threshold flakes |
| `Trials` | `5` | `5` | Unchanged; minimum-p50-across-trials remains the gating reading |
| `CorpusSql` | ~30 statements | ~300 statements via `BuildCorpus(repeats: 10)` | Identifiers suffixed per block |

**Records** (`BaselineDocument`):

| Field | Old | New |
|---|---|---|
| `CompletionRequest` | `BaselineSample` | unchanged |
| `FormatRequest` | `BaselineSample` | unchanged |
| `BulkFormatRequest` | not present | **NEW** `BaselineSample` — bulk-format pipeline run |

**New method**:

| Signature | Behaviour |
|---|---|
| `private static (double p50Ms, double p99Ms) MeasureBulkFormat()` | Same trial / iteration shape as `MeasureFormat`, but invokes `BulkFormatHandler.HandleAsync` on the full corpus split into statements. |

**Validation rule**: after the corpus change, captured p50 for every workload MUST be ≥ 20 ms (FR-015). The test asserts this on capture.

---

## Cross-entity invariants

1. **Single source of settings**: `_cachedSettings` exists in exactly one source file (`RpcContext.cs`). Verified by source search (SC-001).
2. **Single dispatch surface**: `RpcRouter` is the only object that maps an integer message type to its handler. `NamedPipeTransport`, `InProcessTransport`, and `WebSocketTransport` all dispatch through it.
3. **Composition root invariant**: `EngineComposition.Build()` is the only public composition entry point. No code outside `EngineComposition` constructs an `RpcContext`, an `RpcRouter`, or registers a handler — except the test harness, which constructs its own context for unit tests.
4. **AI base / subclass shape**: every class under `Handlers/Ai/` is either `AiHandlerBase<,>` or a concrete subclass of it. No standalone AI handler classes remain.
5. **Wire-format identity**: no entity in this model changes the bytes on the wire. The seven AI subclasses produce the same `XxxResponse` MessagePack payloads as the old monolith for the same inputs.
