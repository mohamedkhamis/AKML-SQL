# M0 Engine Transport — Closure Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Close the four PRD success metrics from spec 021 M0 (Engine Dispatcher Refactor & Transport Abstraction) that were explicitly deferred when PR #236 merged to master on 2026-05-15.

**Architecture:** Incremental, additive finishing work on top of the already-merged spec 021 M0 transport abstraction. The architectural pieces (`IRpcTransport`, `IRpcRequestHandler<,>`, `RpcRouter`, `RpcContext`, three transports, ~50 handlers in `Handlers/*` folders, in-process test matrix) all exist and ship today. This plan closes the deferred polish: dual-owned `_cachedSettings` (real smell), `PipeRpcServer.cs` rename + LOC budget (file shape), `AiRequestHandler` monolith split into base + subclasses (interpretive), and perf gate tightening (measurement).

**Tech Stack:** .NET 10 engine (`net10.0`, win-x64, self-contained single-file), MSBuild, xUnit, MessagePack. Shell extensions (`net472`, 6 hosts) are out of scope — frame format does not change.

**Not in scope:**
- Modifying any file under `src/AkmlSql.Shell.Shared/` or `src/AkmlSql.{Ssms,VS}*/`
- Changing the wire format `[length][CRC][MessagePack(RpcMessage)]`
- Any integer message-type code
- M3+ work (LAN-mode TLS, pairing UI, etc.)

**Git policy:** This repository's `CLAUDE.md` is explicit: NEVER run `git add`, `git commit`, `git push`, or `gh pr create` unless directly instructed. Every task ends at "ready to commit" — the user drives git. No step in this plan stages or commits.

---

## File Structure

### New files
| Path | Responsibility |
|---|---|
| `src/AkmlSql.Engine/Transports/NamedPipeTransport.cs` | Frame I/O + pipe accept loop only; ≤150 LOC. Replaces `Server/PipeRpcServer.cs`. |
| `src/AkmlSql.Engine/EngineComposition.cs` | Composition root: builds all engine services, `RpcContext`, and registers handlers with an `RpcRouter`. Extracted from the current `PipeRpcServer` constructor. |
| `src/AkmlSql.Engine/Handlers/Ai/AiHandlerBase.cs` | Abstract base lifting privacy-consent check, retry-with-backoff, settings-refresh, error-envelope construction. |
| `src/AkmlSql.Engine/Handlers/Ai/AiTextToSqlHandler.cs` | Per-message subclass for `MessageTypes.AiTextToSql`; ≤80 LOC. |
| `src/AkmlSql.Engine/Handlers/Ai/AiExplainHandler.cs` | Per-message subclass for `MessageTypes.AiExplain`; ≤80 LOC. |
| `src/AkmlSql.Engine/Handlers/Ai/AiFixHandler.cs` | Per-message subclass for `MessageTypes.AiFix`; ≤80 LOC. |
| `src/AkmlSql.Engine/Handlers/Ai/AiOptimizeHandler.cs` | Per-message subclass for `MessageTypes.AiOptimize`; ≤80 LOC. |
| `src/AkmlSql.Engine/Handlers/Ai/AiIndexAnalysisHandler.cs` | Per-message subclass for `MessageTypes.AiIndexAnalysis`; ≤80 LOC. |
| `src/AkmlSql.Engine/Handlers/Ai/AiChatHandler.cs` | Per-message subclass for `MessageTypes.AiChat`; ≤80 LOC. |
| `src/AkmlSql.Engine/Handlers/Ai/AiGhostTextHandler.cs` | Per-message subclass for `MessageTypes.AiGhostText`; ≤80 LOC. |
| `src/AkmlSql.Engine/Ai/AiPipelineServices.cs` | Per-pipeline collaborator types (schema-context builder, privacy transformer, provider router, prompt builder) that the subclasses share via constructor injection. Carved out of the current 1896-LOC `AiRequestHandler.cs`. |
| `tests/AkmlSql.Engine.Tests/Handlers/Ai/AiHandlerBaseTests.cs` | xUnit tests for the abstract base via a test-only concrete subclass. |
| `tests/AkmlSql.Engine.Tests/Handlers/Ai/AiTextToSqlHandlerTests.cs` | xUnit test smoke per handler — direct dispatch + InProcessTransport round-trip. |

### Modified files
| Path | Change |
|---|---|
| `src/AkmlSql.Engine/RpcContext.cs` | Add `EnsureSettings()` + `InvalidateSettings()`; carry the `ConfigManager.Load()` callback that today lives on `PipeRpcServer`. |
| `src/AkmlSql.Engine/Server/PipeRpcServer.cs` | DELETE (replaced by `Transports/NamedPipeTransport.cs`). |
| `src/AkmlSql.Engine/Server/PipeRpcServer.Handlers.cs` | DELETE (logic moves to `EngineComposition.cs`). |
| `src/AkmlSql.Engine/EngineHost.cs` | Replace `new PipeRpcServer(pipeName)` with `EngineComposition.Build(...)` → `NamedPipeTransport(...)` + start. |
| `src/AkmlSql.Engine/Ai/AiRequestHandler.cs` | DELETE after subclass extraction. Shared collaborators move to `AiPipelineServices.cs`. |
| `src/AkmlSql.Engine/Handlers/Ai/AiMessageHandlers.cs` | DELETE (bridge no longer needed once concrete handlers register directly). |
| `src/AkmlSql.Engine/RpcRouter.cs` | No code change required, but used as the single dispatch surface in the new composition root. |
| `tests/AkmlSql.Engine.Tests/PerformanceBaselineTests.cs` | Add heavier-workload corpus (10× scale), measure `BulkFormat` p50 instead of single-format p50 (or keep both), set `MaxRegressionFraction = 0.05`. |
| `doc/architecture.md` | Update § 9b "Spec 021 — M0 Transport Abstraction" to reflect the final shape (`EngineComposition` as composition root, `NamedPipeTransport` replaces `PipeRpcServer`). |
| `doc/ipc-api.md` | Update § "Transport Plurality" with final transport names. |
| `specs/021-web-edition/tasks.md` | Add closure-task table at the bottom referencing this plan; tick T020 / T009 / T019 / T006-T025 follow-ups. |

---

## Phase 1 — Pre-flight: establish green baseline

### Task 1: Verify current state builds and tests pass

**Files:** none (verification only)

- [ ] **Step 1: Build the engine**

  Run from repo root:

  ```bash
  dotnet build src/AkmlSql.Engine/AkmlSql.Engine.csproj -c Release
  ```

  Expected: build succeeds with `0 Error(s)`.

- [ ] **Step 2: Run engine test suite**

  ```bash
  dotnet test tests/AkmlSql.Engine.Tests/AkmlSql.Engine.Tests.csproj -c Release
  ```

  Expected: all tests pass. Note any pre-existing skipped/flaky tests so they're not blamed on later steps.

- [ ] **Step 3: Capture a fresh perf baseline locally**

  Delete the existing baseline file so the test re-captures it on this machine:

  ```bash
  rm -f tests/AkmlSql.Engine.Tests/baselines/m0-baseline.json
  dotnet test tests/AkmlSql.Engine.Tests/AkmlSql.Engine.Tests.csproj --filter "Capture_or_compare_M0_baseline" -c Release
  ```

  Expected: test passes, `tests/AkmlSql.Engine.Tests/baselines/m0-baseline.json` is recreated with non-zero `p50Ms` for both CompletionRequest and FormatRequest. **Do NOT commit this file** — it is a per-machine reference.

- [ ] **Step 4: Build one shell host as a smoke check**

  ```bash
  MSBUILD="/c/Program Files/Microsoft Visual Studio/2022/Enterprise/MSBuild/Current/Bin/MSBuild.exe"
  "$MSBUILD" "src/AkmlSql.Ssms22/AkmlSql.Ssms22.csproj" -t:Restore -p:Configuration=Release -v:quiet
  "$MSBUILD" "src/AkmlSql.Ssms22/AkmlSql.Ssms22.csproj" -t:Build -p:Configuration=Release -v:minimal
  ```

  Expected: `Build succeeded.` with zero errors. The shell extension must keep building throughout this plan.

---

## Phase 2 — Gap 1: Move `_cachedSettings` from `PipeRpcServer` to `RpcContext`

The field is dual-owned today: it lives on `PipeRpcServer.cs:35` AND is mirrored into `RpcContext.Settings` from two places in `PipeRpcServer.Handlers.cs` (lines 35, 48, 92). The `AnalysisSettingsChanged` handler nulls both (`PipeRpcServer.Handlers.cs:101–102`). This is the half-finished tail of T009.

### Task 2: Add settings provider to RpcContext

**Files:**
- Modify: `src/AkmlSql.Engine/RpcContext.cs`
- Test: `tests/AkmlSql.Engine.Tests/RpcContextTests.cs` (new file)

- [ ] **Step 1: Write the failing test**

  Create `tests/AkmlSql.Engine.Tests/RpcContextTests.cs`:

  ```csharp
  using AkmlSql.Core.Config;
  using AkmlSql.Engine;
  using AkmlSql.Engine.Parser;
  using AkmlSql.Engine.Schema;
  using AkmlSql.Engine.Server;
  using Serilog;
  using Xunit;

  namespace AkmlSql.Engine.Tests;

  public class RpcContextTests
  {
      private static RpcContext NewContext(Func<AppSettings> loader)
      {
          return new RpcContext
          {
              Sessions = new SessionManager(),
              SchemaCache = new SchemaCacheManager(),
              Logger = Log.Logger,
              SettingsLoader = loader,
          };
      }

      [Fact]
      public void EnsureSettings_loads_once_and_caches()
      {
          int loadCount = 0;
          var ctx = NewContext(() => { loadCount++; return new AppSettings(); });

          var s1 = ctx.EnsureSettings();
          var s2 = ctx.EnsureSettings();

          Assert.Same(s1, s2);
          Assert.Equal(1, loadCount);
      }

      [Fact]
      public void InvalidateSettings_forces_reload_on_next_call()
      {
          int loadCount = 0;
          var ctx = NewContext(() => { loadCount++; return new AppSettings(); });

          ctx.EnsureSettings();
          ctx.InvalidateSettings();
          ctx.EnsureSettings();

          Assert.Equal(2, loadCount);
      }
  }
  ```

- [ ] **Step 2: Run test to verify it fails**

  ```bash
  dotnet test tests/AkmlSql.Engine.Tests/AkmlSql.Engine.Tests.csproj --filter "RpcContextTests" -c Release
  ```

  Expected: FAIL — `RpcContext` does not yet have `SettingsLoader`, `EnsureSettings()`, or `InvalidateSettings()`.

- [ ] **Step 3: Add the settings-provider methods to RpcContext**

  Replace the body of `src/AkmlSql.Engine/RpcContext.cs` with:

  ```csharp
  using AkmlSql.Core.Config;
  using AkmlSql.Engine.Parser;
  using AkmlSql.Engine.Schema;
  using AkmlSql.Engine.Server;
  using Serilog;

  namespace AkmlSql.Engine
  {
      /// <summary>
      /// Spec 021 (web edition) — M0 transport abstraction.
      /// Per-process shared state passed to every <see cref="Transports.IRpcRequestHandler{TReq,TResp}"/>
      /// invocation by <see cref="RpcRouter"/>. Sole owner of <c>_cachedSettings</c> after the
      /// M0 closure plan (Phase 2): callers go through <see cref="EnsureSettings"/> and
      /// <see cref="InvalidateSettings"/> instead of a per-transport field.
      /// </summary>
      public sealed class RpcContext
      {
          private AppSettings? _cachedSettings;
          private readonly object _settingsLock = new();

          public required SessionManager Sessions { get; init; }
          public required SchemaCacheManager SchemaCache { get; init; }
          public required ILogger Logger { get; init; }

          /// <summary>Loader callback supplied by the composition root (typically <c>ConfigManager.Load</c>).</summary>
          public required Func<AppSettings> SettingsLoader { get; init; }

          public TsqlParserService? ParserService { get; init; }
          public SchemaMetadataService? SchemaMetadata { get; init; }

          /// <summary>Idempotent: loads once, caches for the lifetime of this context.</summary>
          public AppSettings EnsureSettings()
          {
              if (_cachedSettings != null) return _cachedSettings;
              lock (_settingsLock)
              {
                  return _cachedSettings ??= SettingsLoader();
              }
          }

          /// <summary>Drops the cache. Next <see cref="EnsureSettings"/> call re-runs the loader.</summary>
          public void InvalidateSettings()
          {
              lock (_settingsLock) { _cachedSettings = null; }
          }
      }
  }
  ```

- [ ] **Step 4: Run test to verify it passes**

  ```bash
  dotnet test tests/AkmlSql.Engine.Tests/AkmlSql.Engine.Tests.csproj --filter "RpcContextTests" -c Release
  ```

  Expected: PASS.

- [ ] **Step 5: Compile-check the whole engine**

  ```bash
  dotnet build src/AkmlSql.Engine/AkmlSql.Engine.csproj -c Release
  ```

  Expected: BUILD FAILS — existing call sites (`PipeRpcServer.Handlers.cs` lines 35, 48, 92, 101) reference the old shape. This is the trigger for Task 3.

### Task 3: Migrate `PipeRpcServer.Handlers.cs` consumers to use RpcContext

**Files:**
- Modify: `src/AkmlSql.Engine/Server/PipeRpcServer.cs` (remove `_cachedSettings` field)
- Modify: `src/AkmlSql.Engine/Server/PipeRpcServer.Handlers.cs` (route through ctx)
- Modify: `src/AkmlSql.Engine/Handlers/Completion/CompletionHandler.cs` (verify the settings-provider callback signature change is compatible)
- Modify: `src/AkmlSql.Engine/Handlers/Analysis/AnalysisHandlers.cs` (same)
- Modify: `src/AkmlSql.Engine/Ai/AiRequestHandler.cs:63` — `RefreshSettings()` calls are still needed but now via the ctx

- [ ] **Step 1: Delete the `_cachedSettings` field from `PipeRpcServer.cs`**

  Remove line 35 (the `_cachedSettings` field). Also remove the field declaration `private RpcContext _rpcContext = null!;` on line 82 (it stays — different concern; leave it for now).

- [ ] **Step 2: Rewrite the `RpcContext` initialiser in `PipeRpcServer.Handlers.cs`**

  At `PipeRpcServer.Handlers.cs:33–41`, replace:

  ```csharp
  _rpcContext = new RpcContext
  {
      Settings = _cachedSettings,
      Sessions = _sessionManager,
      SchemaCache = _schemaCacheManager,
      Logger = Log.Logger,
      ParserService = _parserService,
      SchemaMetadata = _schemaMetadataService,
  };
  ```

  With:

  ```csharp
  _rpcContext = new RpcContext
  {
      Sessions = _sessionManager,
      SchemaCache = _schemaCacheManager,
      Logger = Log.Logger,
      ParserService = _parserService,
      SchemaMetadata = _schemaMetadataService,
      SettingsLoader = Core.Config.ConfigManager.Load,
  };
  ```

- [ ] **Step 3: Rewrite the CompletionHandler registration to use the ctx**

  At `PipeRpcServer.Handlers.cs:43–52`, replace the closure that captures `_cachedSettings` with one that asks the ctx:

  ```csharp
  var completionHandler = new Handlers.Completion.CompletionHandler(
      _completionEngine,
      () => _rpcContext.EnsureSettings());
  _pluggableHandlers[MessageTypes.RequestCompletion] =
      new TypedHandlerAdapter<CompletionRequest, CompletionResponse>(completionHandler, _rpcContext);
  ```

- [ ] **Step 4: Rewrite the AnalysisHandler registration the same way**

  At `PipeRpcServer.Handlers.cs:87–94`, replace:

  ```csharp
  var analysisHandler = new Handlers.Analysis.AnalysisHandler(
      _analysisEngine,
      () => _rpcContext.EnsureSettings());
  ```

- [ ] **Step 5: Rewrite the `AnalysisSettingsChanged` invalidation callback**

  At `PipeRpcServer.Handlers.cs:98–104`, replace:

  ```csharp
  var analysisSettingsChangedHandler = new Handlers.Analysis.AnalysisSettingsChangedHandler(() =>
  {
      _caSettingsLoader.InvalidateCache();
      _cachedSettings = null;
      _rpcContext.Settings = null;
      _aiHandler.RefreshSettings();
  });
  ```

  With:

  ```csharp
  var analysisSettingsChangedHandler = new Handlers.Analysis.AnalysisSettingsChangedHandler(() =>
  {
      _caSettingsLoader.InvalidateCache();
      _rpcContext.InvalidateSettings();
      _aiHandler.RefreshSettings();
  });
  ```

- [ ] **Step 6: Build the engine**

  ```bash
  dotnet build src/AkmlSql.Engine/AkmlSql.Engine.csproj -c Release
  ```

  Expected: BUILD SUCCEEDS. If errors remain, they will be in adapter or handler files referencing the old `Settings` property setter — fix those by removing direct `_rpcContext.Settings = ...` assignments (the loader is the source of truth now).

- [ ] **Step 7: Run the full test suite**

  ```bash
  dotnet test tests/AkmlSql.Engine.Tests/AkmlSql.Engine.Tests.csproj -c Release
  ```

  Expected: all tests pass, including the existing `AnalysisSettingsChangedHandlerTests` (if any) — confirming invalidation semantics are preserved.

- [ ] **Step 8: Stop. Ready to commit.** Inform the user: "Gap 1 (`_cachedSettings` move) complete. Ready to commit?"

---

## Phase 3 — Gap 2: Rename `PipeRpcServer` → `NamedPipeTransport`, ≤150 LOC

`PipeRpcServer.cs` is 354 LOC; `PipeRpcServer.Handlers.cs` is 353 LOC. To hit the ≤150 LOC budget, the constructor service-init + handler registration moves into a new `EngineComposition` class. The transport file ends up owning only: pipe ACL, accept loop, frame I/O, dispatch via `RpcRouter`.

### Task 4: Extract `EngineComposition` (composition root)

**Files:**
- Create: `src/AkmlSql.Engine/EngineComposition.cs`
- Modify: `src/AkmlSql.Engine/Server/PipeRpcServer.cs` (strip constructor body)
- Modify: `src/AkmlSql.Engine/EngineHost.cs` (call `EngineComposition.Build(...)`)

- [ ] **Step 1: Create `EngineComposition.cs`**

  Create `src/AkmlSql.Engine/EngineComposition.cs`:

  ```csharp
  using AkmlSql.Core.Config;
  using AkmlSql.Engine.Ai;
  using AkmlSql.Engine.Analysis;
  using AkmlSql.Engine.Completion;
  using AkmlSql.Engine.Export;
  using AkmlSql.Engine.Formatter;
  using AkmlSql.Engine.History;
  using AkmlSql.Engine.Navigation;
  using AkmlSql.Engine.Parser;
  using AkmlSql.Engine.Productivity;
  using AkmlSql.Engine.Refactoring;
  using AkmlSql.Engine.Safety;
  using AkmlSql.Engine.Schema;
  using AkmlSql.Engine.Server;
  using AkmlSql.Engine.Sessions;
  using AkmlSql.Engine.Snippets;
  using AkmlSql.Formatting.Profiles;
  using Serilog;

  namespace AkmlSql.Engine
  {
      /// <summary>
      /// Spec 021 (web edition) — M0 closure (Phase 3). Composition root for the engine. Owns
      /// service construction, builds the <see cref="RpcContext"/>, and registers every handler
      /// with the supplied <see cref="RpcRouter"/>. Replaces the constructor block + the partial
      /// <c>PipeRpcServer.Handlers.cs</c> that previously held this code. Transports stay focused
      /// on frame I/O.
      /// </summary>
      public sealed class EngineComposition
      {
          public required RpcContext Context { get; init; }
          public required RpcRouter Router { get; init; }
          public required AiRequestHandler AiHandler { get; init; }
          public required HistoryRetentionService HistoryRetention { get; init; }

          /// <summary>Builds everything the engine needs. Idempotent: call once per process.</summary>
          public static EngineComposition Build()
          {
              var sessions = new SessionManager();
              var parser = new TsqlParserService();
              var schemaCache = new SchemaCacheManager();
              var schemaMeta = new SchemaMetadataService();

              var ctx = new RpcContext
              {
                  Sessions = sessions,
                  SchemaCache = schemaCache,
                  Logger = Log.Logger,
                  ParserService = parser,
                  SchemaMetadata = schemaMeta,
                  SettingsLoader = ConfigManager.Load,
              };

              var router = new RpcRouter();
              var registry = new EngineHandlerRegistry(ctx);
              var ai = registry.RegisterAllHandlers(router);

              // History retention starts after construction so the registry has its handler refs.
              var historyDb = new HistoryDatabase();
              var settings = ConfigManager.Load();
              var retention = new HistoryRetentionService(historyDb, settings.History);

              return new EngineComposition
              {
                  Context = ctx,
                  Router = router,
                  AiHandler = ai,
                  HistoryRetention = retention,
              };
          }
      }
  }
  ```

  (The `EngineHandlerRegistry` class is created in Task 5 — it owns the registration block that today lives in `PipeRpcServer.Handlers.cs`.)

- [ ] **Step 2: Create `EngineHandlerRegistry.cs`**

  Create `src/AkmlSql.Engine/EngineHandlerRegistry.cs`. The body is **the entire current `PipeRpcServer.Handlers.cs:19–352`** verbatim, with three mechanical changes:
  - Move from `partial class PipeRpcServer` to `internal sealed class EngineHandlerRegistry`.
  - Replace every `_pluggableHandlers[X] = Y` with `router.Register<TReq,TResp>((IRpcRequestHandler<TReq,TResp>)handler)` where `handler` is the typed handler — the `TypedHandlerAdapter` indirection goes away because `RpcRouter` already does the same job. For `DelegatingMessageHandler` entries, wrap them in a tiny `IRpcRequestHandler<RpcMessage, RpcMessage>` adapter so the same router serves them. (See Task 5 for the adapter shape.)
  - Replace every `_field` reference (`_sessionManager`, `_completionEngine`, etc.) with the local equivalent: instantiate each service inside `RegisterAllHandlers(RpcRouter router)`.

  Concrete shape:

  ```csharp
  using AkmlSql.Core.Ipc;
  using AkmlSql.Core.Ipc.Messages;
  using AkmlSql.Engine.Ai;
  // ... other usings as in PipeRpcServer.cs ...

  namespace AkmlSql.Engine;

  internal sealed class EngineHandlerRegistry
  {
      private readonly RpcContext _ctx;

      public EngineHandlerRegistry(RpcContext ctx) => _ctx = ctx;

      /// <summary>Registers every engine handler with the supplied router. Returns the
      /// <see cref="AiRequestHandler"/> instance so the host can call <c>RefreshSettings</c>.</summary>
      public AiRequestHandler RegisterAllHandlers(RpcRouter router)
      {
          var completionEngine = new CompletionEngine(_ctx.ParserService!);
          completionEngine.RegisterProvider(new Completion.Providers.DatabaseProvider());
          var formatHandler = new FormatRequestHandler(ProfileManager.CreateDefault());
          // ... (verbatim the construction block from PipeRpcServer constructor) ...

          router.Register(new Handlers.Completion.CompletionHandler(completionEngine, _ctx.EnsureSettings));
          router.Register(new Handlers.Formatting.FormatDocumentHandler(formatHandler));
          // ... (verbatim the registration block, dropping TypedHandlerAdapter wrapping) ...

          return aiHandler;
      }
  }
  ```

  This is mechanical; copy the registration block as-is and adapt the types.

- [ ] **Step 3: Run unit tests to ensure registry compiles and registers all message-type codes**

  The existing `AllMessageTypesInProcessTests.cs::AllShellToEngineMessageTypes_Are_Registered` should catch any code we forgot.

  ```bash
  dotnet test tests/AkmlSql.Engine.Tests/AkmlSql.Engine.Tests.csproj --filter "AllMessageTypesInProcess" -c Release
  ```

  Expected: PASS.

- [ ] **Step 4: Stop. Verify build is green before moving on.**

### Task 5: Replace `_pluggableHandlers` dictionary with `RpcRouter` dispatch in PipeRpcServer

The current dispatch path lives in `PipeRpcServer.DispatchAsync` (lines 219–305). It consults `_pluggableHandlers`. Once `EngineHandlerRegistry` registers everything via `router.Register(...)`, the transport's job collapses to: read frame → call `router.RouteAsync(...)` → write frame.

**Files:**
- Modify: `src/AkmlSql.Engine/Server/PipeRpcServer.cs`
- Modify: `src/AkmlSql.Engine/Server/PipeRpcServer.Handlers.cs` (delete after registry takes over)

- [ ] **Step 1: Add a `DelegatingHandlerAdapter` so legacy `IMessageHandler` entries can register with RpcRouter**

  Create `src/AkmlSql.Engine/Server/DelegatingHandlerAdapter.cs`:

  ```csharp
  using System.Threading;
  using System.Threading.Tasks;
  using AkmlSql.Core.Ipc;
  using AkmlSql.Engine.Transports;

  namespace AkmlSql.Engine.Server;

  /// <summary>
  /// Spec 021 closure — Phase 3. Bridges legacy <see cref="IMessageHandler"/> instances
  /// (which take a raw <see cref="RpcMessage"/>) into the typed
  /// <see cref="IRpcRequestHandler{TReq,TResp}"/> contract that <see cref="RpcRouter"/> registers.
  /// Used for delegating handlers (session save/restore, history, productivity, navigation, AI)
  /// that already process the raw message themselves.
  /// </summary>
  internal sealed class DelegatingHandlerAdapter : IRpcRequestHandler<RpcMessage, RpcMessage>
  {
      private readonly int _messageType;
      private readonly IMessageHandler _inner;

      public DelegatingHandlerAdapter(int messageType, IMessageHandler inner)
      {
          _messageType = messageType;
          _inner = inner;
      }

      public int RequestMessageType => _messageType;
      public int ResponseMessageType => 0;   // adapter writes a typed reply itself; router suppresses an extra envelope
      public bool AllowsEmptyPayload => true;

      public async Task<RpcMessage> HandleAsync(RpcMessage request, RpcContext ctx, CancellationToken ct)
      {
          var resp = await _inner.HandleAsync(request, ct).ConfigureAwait(false);
          // RpcRouter writes nothing when ResponseMessageType == 0, but we want the response
          // frame written verbatim. Workaround: return the response directly through the router
          // by sidestepping its serialisation. The cleanest fix is a small RpcRouter overload —
          // see Step 4 below.
          return resp ?? new RpcMessage { MessageType = 0, RequestId = request.RequestId };
      }
  }
  ```

  **Note on this adapter:** the cleanest design adds a `RpcRouter.RegisterRaw(int messageType, Func<RpcMessage, CancellationToken, Task<RpcMessage?>>)` overload that bypasses `MessagePackSerializer.Deserialize/Serialize` and returns the raw response. Implementing that overload is preferable to the placeholder shape above. The actual change to `RpcRouter.cs`:

  ```csharp
  public void RegisterRaw(int messageType, Func<RpcMessage, CancellationToken, Task<RpcMessage?>> handler)
  {
      var adapter = new RawHandlerAdapter(messageType, handler);
      if (!_adapters.TryAdd(messageType, adapter))
          throw new InvalidOperationException(
              $"RpcRouter: a handler for MessageType {messageType} is already registered.");
  }

  private sealed class RawHandlerAdapter : IHandlerAdapter
  {
      private readonly int _messageType;
      private readonly Func<RpcMessage, CancellationToken, Task<RpcMessage?>> _handler;
      public RawHandlerAdapter(int messageType, Func<RpcMessage, CancellationToken, Task<RpcMessage?>> handler)
      { _messageType = messageType; _handler = handler; }

      public Task<RpcMessage?> RouteAsync(RpcMessage msg, RpcContext ctx, CancellationToken ct)
          => _handler(msg, ct);
  }
  ```

- [ ] **Step 2: Update `EngineHandlerRegistry` to use the new `RegisterRaw` for delegating handlers**

  Replace every `_pluggableHandlers[X] = new DelegatingMessageHandler(...)` line in the registry with:

  ```csharp
  router.RegisterRaw(MessageTypes.SessionSave,
      (msg, ct) => _sessionRequestHandler.HandleAsync(msg, MessageTypes.SessionSave));
  ```

  And same for `AiMessageHandler` entries (they wrap `_aiHandler.Handle*Async`).

- [ ] **Step 3: Replace `PipeRpcServer.DispatchAsync` with a thin RpcRouter call**

  Edit `src/AkmlSql.Engine/Server/PipeRpcServer.cs`. The new `DispatchAsync` is:

  ```csharp
  private async Task<RpcMessage?> DispatchAsync(RpcMessage message, CancellationToken ct)
  {
      try
      {
          var response = await _router.RouteAsync(message, _ctx, ct).ConfigureAwait(false);
          if (response == null && !_router.IsRegistered(message.MessageType))
          {
              Log.Warning("Unknown message type: {Type}", message.MessageType);
          }
          return response;
      }
      catch (OperationCanceledException) { throw; }
      catch (Exception ex)
      {
          Log.Error(ex, "Error dispatching message type {Type}", message.MessageType);
          return RpcResponseFactory.CreateErrorResponse(ex.Message, message.RequestId);
      }
  }
  ```

  Drop the `_pluggableHandlers` field, `_rpcContext` field (replace with `_ctx` injected via constructor), `DispatchPluggableAsync`, `RegisterPluggableHandlers`, `RegisteredMessageTypeCodes`, and `LookupSession` (move to a delegate on the registry).

- [ ] **Step 4: Delete `PipeRpcServer.Handlers.cs`**

  ```bash
  rm src/AkmlSql.Engine/Server/PipeRpcServer.Handlers.cs
  ```

  Its content lives in `EngineHandlerRegistry.cs` now.

- [ ] **Step 5: Update `EngineHost.cs` to wire the new flow**

  Replace the `try` block at `EngineHost.cs:87–94`:

  ```csharp
  try
  {
      ProcessPendingImports();
      var composition = EngineComposition.Build();
      var transport = new PipeRpcServer(pipeName, composition.Context, composition.Router);
      await transport.RunAsync(token);
  }
  ```

- [ ] **Step 6: Update `PipeRpcServer.cs` constructor signature**

  ```csharp
  public PipeRpcServer(string pipeName, RpcContext ctx, RpcRouter router)
  {
      _pipeName = pipeName;
      _ctx = ctx;
      _router = router;
  }
  ```

  Drop every service-init line currently between lines 84 and 161.

- [ ] **Step 7: Build and test**

  ```bash
  dotnet build src/AkmlSql.Engine/AkmlSql.Engine.csproj -c Release
  dotnet test tests/AkmlSql.Engine.Tests/AkmlSql.Engine.Tests.csproj -c Release
  ```

  Expected: BUILD + ALL TESTS PASS, including `AllMessageTypesInProcessTests` and `PipeRoundTripTests`. If a test references `PipeRpcServer.RegisteredMessageTypeCodes` (which T023 used), update it to use `RpcRouter.RegisteredMessageTypes` — the router exposes the same set.

- [ ] **Step 8: Stop. Confirm LOC budget.**

  ```bash
  wc -l src/AkmlSql.Engine/Server/PipeRpcServer.cs
  ```

  Expected: ≤150 LOC. If still over, the residual is likely `LookupSession` + `CreatePipeSecurity`. `CreatePipeSecurity` stays (transport-specific). `LookupSession` should already have been moved in Step 3.

### Task 6: Rename `PipeRpcServer` → `NamedPipeTransport`

**Files:**
- Rename: `src/AkmlSql.Engine/Server/PipeRpcServer.cs` → `src/AkmlSql.Engine/Transports/NamedPipeTransport.cs`
- Modify all references

- [ ] **Step 1: Move the file**

  ```bash
  mv src/AkmlSql.Engine/Server/PipeRpcServer.cs src/AkmlSql.Engine/Transports/NamedPipeTransport.cs
  ```

- [ ] **Step 2: Rename class + namespace**

  Inside the moved file:
  - Change `namespace AkmlSql.Engine.Server` → `namespace AkmlSql.Engine.Transports`
  - Rename class `PipeRpcServer` → `NamedPipeTransport`
  - Make it implement `IRpcTransport` (`Task StartAsync(...)`, `event Func<RpcMessage, CancellationToken, Task<RpcMessage?>>? RequestReceived`). `StartAsync` does what `RunAsync` does today; `RequestReceived` is fired from the dispatch loop. (This brings it in line with the other two transports.)

- [ ] **Step 3: Update all references**

  ```bash
  grep -rln "PipeRpcServer" src/ tests/
  ```

  For each hit, replace `PipeRpcServer` with `NamedPipeTransport` and update the `using` to `AkmlSql.Engine.Transports`. Expected hits (after Tasks 4–5): `EngineHost.cs`, `EngineComposition.cs`, `tests/AkmlSql.Engine.Tests/Transports/PipeRoundTripTests.cs`, `tests/AkmlSql.Engine.Tests/InProcess/AllMessageTypesInProcessTests.cs`, plus a few doc-comment references inside other handler files (those are just comments — fine to leave or update).

- [ ] **Step 4: Build + test + LOC check**

  ```bash
  dotnet build src/AkmlSql.Engine/AkmlSql.Engine.csproj -c Release
  dotnet test tests/AkmlSql.Engine.Tests/AkmlSql.Engine.Tests.csproj -c Release
  wc -l src/AkmlSql.Engine/Transports/NamedPipeTransport.cs
  ```

  Expected: build + tests green; `wc -l` ≤ 150.

- [ ] **Step 5: Stop. Ready to commit.** Inform the user: "Gap 2 (rename + LOC ≤150) complete. Ready to commit?"

---

## Phase 4 — Gap 3: Extract `AiHandlerBase` + 7 per-message subclasses

`AiRequestHandler.cs` is 1896 LOC. The seven public `Handle*Async` methods each weigh 150–420 lines because they inline the consent-check, prompt-build, provider-route, retry-with-backoff, privacy-transform, error-envelope steps. The PRD asks for an `AiHandlerBase` lifting that boilerplate and each concrete subclass to be ≤ 80 LOC.

### Task 7: Extract pipeline collaborators into `AiPipelineServices.cs`

**Files:**
- Create: `src/AkmlSql.Engine/Ai/AiPipelineServices.cs`
- Modify: `src/AkmlSql.Engine/Ai/AiRequestHandler.cs` (delete private helpers as they move)

- [ ] **Step 1: Read AiRequestHandler.cs top to bottom**

  ```bash
  wc -l src/AkmlSql.Engine/Ai/AiRequestHandler.cs
  ```

  Identify the private/static helpers that every `Handle*Async` method uses:
  - `CheckPrivacyConsent(AiSettings)` — line ~78
  - `ExecuteWithBackoffAsync<T>(...)` — line ~100
  - `LoadAiSettings()` — somewhere mid-file
  - Schema-context building (delegated to `_schemaContextBuilder`)
  - Privacy transform (delegated to `_privacyTransformer`)
  - Provider creation (via `AiProviderFactory`)
  - Error-envelope shaping (e.g. `RpcResponseFactory.CreateErrorResponse` plus per-handler error categories)

- [ ] **Step 2: Create `AiPipelineServices.cs`**

  ```csharp
  using System.Threading;
  using System.Threading.Tasks;
  using AkmlSql.Core.Config;
  using AkmlSql.Engine.Ai.Context;
  using AkmlSql.Engine.Ai.Privacy;
  using AkmlSql.Engine.Ai.Prompts;
  using AkmlSql.Engine.Ai.Providers;
  using AkmlSql.Engine.Parser;
  using AkmlSql.Engine.Schema;

  namespace AkmlSql.Engine.Ai;

  /// <summary>
  /// Spec 021 closure — Phase 4. Shared collaborators used by every AI handler subclass.
  /// Carved out of the AiRequestHandler monolith so AiHandlerBase + concrete subclasses
  /// can share one set of injected services instead of duplicating field declarations.
  /// </summary>
  public sealed class AiPipelineServices
  {
      public required SchemaContextBuilder SchemaContext { get; init; }
      public required PrivacyTransformer Privacy { get; init; }
      public required TsqlParserService Parser { get; init; }
      public required Func<AiSettings> SettingsProvider { get; init; }   // calls ctx.EnsureSettings().Ai

      /// <summary>Builds the services around a schema-cache lookup. Mirrors the
      /// constructor of <c>AiRequestHandler</c>.</summary>
      public static AiPipelineServices Build(SchemaCacheManager schemaCache, TsqlParserService parser,
          Func<AiSettings> settingsProvider)
      {
          return new AiPipelineServices
          {
              SchemaContext = new SchemaContextBuilder(
                  (cs, db) => schemaCache.GetCache(cs, db)),
              Privacy = new PrivacyTransformer(parser),
              Parser = parser,
              SettingsProvider = settingsProvider,
          };
      }
  }
  ```

- [ ] **Step 3: Compile-check**

  ```bash
  dotnet build src/AkmlSql.Engine/AkmlSql.Engine.csproj -c Release
  ```

  Expected: PASS. No call sites changed yet — this is additive.

### Task 8: Create `AiHandlerBase`

**Files:**
- Create: `src/AkmlSql.Engine/Handlers/Ai/AiHandlerBase.cs`
- Test: `tests/AkmlSql.Engine.Tests/Handlers/Ai/AiHandlerBaseTests.cs`

- [ ] **Step 1: Write the failing test**

  Create `tests/AkmlSql.Engine.Tests/Handlers/Ai/AiHandlerBaseTests.cs`:

  ```csharp
  using System.Threading;
  using System.Threading.Tasks;
  using AkmlSql.Core.Config;
  using AkmlSql.Core.Ipc;
  using AkmlSql.Core.Models.Ai;
  using AkmlSql.Engine;
  using AkmlSql.Engine.Ai;
  using AkmlSql.Engine.Handlers.Ai;
  using AkmlSql.Engine.Parser;
  using AkmlSql.Engine.Schema;
  using AkmlSql.Engine.Server;
  using Serilog;
  using Xunit;

  namespace AkmlSql.Engine.Tests.Handlers.Ai;

  public class AiHandlerBaseTests
  {
      private sealed class TestRequest { public string Text { get; set; } = ""; }
      private sealed class TestResponse { public string Echo { get; set; } = ""; }

      private sealed class EchoHandler : AiHandlerBase<TestRequest, TestResponse>
      {
          public EchoHandler(AiPipelineServices svcs) : base(svcs) { }
          public override int RequestMessageType => 9999;
          public override int ResponseMessageType => 10099;
          protected override Task<TestResponse> InvokeAsync(TestRequest req, RpcContext ctx, CancellationToken ct)
              => Task.FromResult(new TestResponse { Echo = req.Text });
      }

      [Fact]
      public async Task Local_provider_skips_consent_check_and_returns_response()
      {
          var settings = new AiSettings { Provider = "ollama", PrivacyConsentRequired = true };
          var ctx = NewContext(() => new AppSettings { Ai = settings });
          var svcs = AiPipelineServices.Build(ctx.SchemaCache, ctx.ParserService!, () => settings);
          var handler = new EchoHandler(svcs);

          var resp = await handler.HandleAsync(new TestRequest { Text = "hi" }, ctx, default);

          Assert.Equal("hi", resp.Echo);
      }

      [Fact]
      public async Task Cloud_provider_with_consent_required_throws_PrivacyConsentRequiredException()
      {
          var settings = new AiSettings { Provider = "anthropic", PrivacyConsentRequired = true };
          var ctx = NewContext(() => new AppSettings { Ai = settings });
          var svcs = AiPipelineServices.Build(ctx.SchemaCache, ctx.ParserService!, () => settings);
          var handler = new EchoHandler(svcs);

          await Assert.ThrowsAsync<PrivacyConsentRequiredException>(() =>
              handler.HandleAsync(new TestRequest { Text = "hi" }, ctx, default));
      }

      private static RpcContext NewContext(Func<AppSettings> loader) => new()
      {
          Sessions = new SessionManager(),
          SchemaCache = new SchemaCacheManager(),
          Logger = Log.Logger,
          ParserService = new TsqlParserService(),
          SettingsLoader = loader,
      };
  }
  ```

- [ ] **Step 2: Run the test to verify it fails**

  ```bash
  dotnet test tests/AkmlSql.Engine.Tests/AkmlSql.Engine.Tests.csproj --filter "AiHandlerBaseTests" -c Release
  ```

  Expected: FAIL — `AiHandlerBase` does not exist.

- [ ] **Step 3: Create `AiHandlerBase.cs`**

  ```csharp
  using System;
  using System.Threading;
  using System.Threading.Tasks;
  using AkmlSql.Core.Models.Ai;
  using AkmlSql.Engine.Ai;
  using AkmlSql.Engine.Transports;
  using Serilog;

  namespace AkmlSql.Engine.Handlers.Ai;

  /// <summary>
  /// Spec 021 closure — Phase 4. Abstract base for all AI message handlers. Lifts the boilerplate
  /// that every concrete AiSuggest/AiExplain/... handler used to inline: privacy-consent check,
  /// settings refresh, exception → typed error response, retry/backoff. Concrete subclasses
  /// override <see cref="InvokeAsync"/> with just the per-message logic.
  /// </summary>
  public abstract class AiHandlerBase<TRequest, TResponse> : IRpcRequestHandler<TRequest, TResponse>
      where TResponse : new()
  {
      protected AiPipelineServices Services { get; }
      protected ILogger Log { get; } = Serilog.Log.Logger;

      protected AiHandlerBase(AiPipelineServices services)
      {
          Services = services ?? throw new ArgumentNullException(nameof(services));
      }

      public abstract int RequestMessageType { get; }
      public abstract int ResponseMessageType { get; }
      public virtual bool SwallowCancellation => true;
      public virtual bool AllowsEmptyPayload => false;

      /// <summary>Per-message logic. Subclasses do nothing else here — base owns consent + errors.</summary>
      protected abstract Task<TResponse> InvokeAsync(TRequest request, RpcContext ctx, CancellationToken ct);

      public async Task<TResponse> HandleAsync(TRequest request, RpcContext ctx, CancellationToken ct)
      {
          CheckPrivacyConsent(Services.SettingsProvider());
          try
          {
              return await InvokeAsync(request, ctx, ct).ConfigureAwait(false);
          }
          catch (OperationCanceledException) when (SwallowCancellation)
          {
              return new TResponse();
          }
      }

      private static readonly HashSet<string> LocalProviders =
          new(StringComparer.OrdinalIgnoreCase) { "ollama", "lmstudio" };

      private static void CheckPrivacyConsent(AiSettings settings)
      {
          if (!settings.PrivacyConsentRequired) return;
          var provider = settings.Provider?.Trim() ?? string.Empty;
          if (LocalProviders.Contains(provider)) return;
          var providerDisplay = string.IsNullOrEmpty(provider) ? "your AI provider" : provider;
          throw new PrivacyConsentRequiredException(
              $"CONSENT_REQUIRED:Data will be sent to {providerDisplay}. Please confirm in settings.");
      }
  }
  ```

- [ ] **Step 4: Run the test to verify it passes**

  ```bash
  dotnet test tests/AkmlSql.Engine.Tests/AkmlSql.Engine.Tests.csproj --filter "AiHandlerBaseTests" -c Release
  ```

  Expected: PASS.

### Task 9: Migrate the 7 AI handlers one at a time

Each handler is its own task (split for atomic commits). For each, the recipe is identical:

1. Identify the existing `HandleXxxAsync(RpcMessage, sessionLookup, ct)` method body in `AiRequestHandler.cs`.
2. Strip the prologue (privacy check, deserialise, settings load) and epilogue (catch / error envelope) — `AiHandlerBase` covers them now.
3. The remaining body becomes the `InvokeAsync(TRequest, RpcContext, CancellationToken)` override.
4. The handler class file must end ≤80 LOC.
5. Register it in `EngineHandlerRegistry.RegisterAllHandlers(...)` replacing the `AiMessageHandler` bridge line.
6. Add one smoke test per handler.

#### Task 9a: `AiTextToSqlHandler`

**Files:**
- Create: `src/AkmlSql.Engine/Handlers/Ai/AiTextToSqlHandler.cs`
- Test: `tests/AkmlSql.Engine.Tests/Handlers/Ai/AiTextToSqlHandlerTests.cs`
- Modify: `src/AkmlSql.Engine/Ai/AiRequestHandler.cs` — delete `HandleTextToSqlAsync` (lines 276 → next public method)
- Modify: `src/AkmlSql.Engine/EngineHandlerRegistry.cs` — register the new handler in place of the bridge

- [ ] **Step 1: Write the failing smoke test**

  ```csharp
  // tests/AkmlSql.Engine.Tests/Handlers/Ai/AiTextToSqlHandlerTests.cs
  [Fact]
  public async Task TextToSql_returns_response_for_local_provider()
  {
      var ctx = NewContext(...);
      var handler = new AiTextToSqlHandler(AiPipelineServices.Build(...));
      var req = new AiTextToSqlRequest { Prompt = "list customers" };
      var resp = await handler.HandleAsync(req, ctx, default);
      Assert.NotNull(resp);
  }
  ```

  Run, confirm FAIL (handler class missing).

- [ ] **Step 2: Create `AiTextToSqlHandler.cs`**

  Body = stripped-down `HandleTextToSqlAsync` from `AiRequestHandler.cs:276` onwards. Aim for ≤80 LOC including the using block.

  ```csharp
  using System.Threading;
  using System.Threading.Tasks;
  using AkmlSql.Core.Ipc.Messages;
  using AkmlSql.Engine.Ai;

  namespace AkmlSql.Engine.Handlers.Ai;

  public sealed class AiTextToSqlHandler : AiHandlerBase<AiTextToSqlRequest, AiTextToSqlResponse>
  {
      public AiTextToSqlHandler(AiPipelineServices svcs) : base(svcs) { }
      public override int RequestMessageType => MessageTypes.AiTextToSql;
      public override int ResponseMessageType => MessageTypes.AiTextToSqlResponse;

      protected override async Task<AiTextToSqlResponse> InvokeAsync(
          AiTextToSqlRequest req, RpcContext ctx, CancellationToken ct)
      {
          // verbatim body from AiRequestHandler.HandleTextToSqlAsync, but using Services.* and ctx.*
          // (no privacy-consent / settings-load / try/catch — base owns those)
          // expected final shape: ~60 LOC including provider setup, prompt build, dispatch
      }
  }
  ```

- [ ] **Step 3: Verify LOC budget**

  ```bash
  wc -l src/AkmlSql.Engine/Handlers/Ai/AiTextToSqlHandler.cs
  ```

  Expected: ≤80. If over, extract a per-message prompt-builder helper into `AiPipelineServices`.

- [ ] **Step 4: Register in `EngineHandlerRegistry`** — replace the `_aiHandler.HandleTextToSqlAsync(...)` bridge line with `router.Register(new AiTextToSqlHandler(svcs));`.

- [ ] **Step 5: Delete `HandleTextToSqlAsync` from `AiRequestHandler.cs`** — the method is fully replaced.

- [ ] **Step 6: Build + test + Ready to commit.**

#### Tasks 9b–9g: repeat for the other six handlers

Same recipe for: `AiExplainHandler`, `AiFixHandler`, `AiOptimizeHandler`, `AiIndexAnalysisHandler`, `AiChatHandler`, `AiGhostTextHandler`. Each one is its own commit stop. After all seven, verify:

- [ ] **Step 7: Confirm `AiRequestHandler.cs` is now empty (or holds only the now-unused refresh/dispose plumbing) and delete it**

  ```bash
  wc -l src/AkmlSql.Engine/Ai/AiRequestHandler.cs   # expected: low residual or 0
  rm src/AkmlSql.Engine/Ai/AiRequestHandler.cs       # only after the residual is migrated
  ```

  `RefreshSettings()` semantics should move to whatever object holds the `AiSettings` cache (likely `AiPipelineServices.SettingsProvider` already drives it via `RpcContext.EnsureSettings`).

- [ ] **Step 8: Delete `src/AkmlSql.Engine/Handlers/Ai/AiMessageHandlers.cs`** — the bridge is no longer used by any registration.

- [ ] **Step 9: Run the full test suite + `AllMessageTypesInProcessTests`**

  ```bash
  dotnet test tests/AkmlSql.Engine.Tests/AkmlSql.Engine.Tests.csproj -c Release
  ```

  Expected: every AI message type still resolves through `RpcRouter` (the matrix test catches drops).

- [ ] **Step 10: Confirm per-handler LOC budget**

  ```bash
  wc -l src/AkmlSql.Engine/Handlers/Ai/AiHandlerBase.cs \
        src/AkmlSql.Engine/Handlers/Ai/Ai*Handler.cs
  ```

  Expected: every concrete handler ≤ 80 LOC; `AiHandlerBase.cs` ≤ 100 LOC.

- [ ] **Step 11: Stop. Ready to commit.** Inform the user: "Gap 3 (AiHandlerBase + 7 subclasses) complete. Ready to commit?"

---

## Phase 5 — Gap 4: Tighten perf gate from 25% to 5%

The current threshold relaxation (`MaxRegressionFraction = 0.25`) was documented at `PerformanceBaselineTests.cs:39–48` because the measured operations are sub-2 ms — JIT/cache noise dominates the cost. Tightening the constant alone will produce a flaky test. The fix is heavier workloads that take 20–200 ms per call, where 5% is meaningful.

### Task 10: Add heavier perf workloads

**Files:**
- Modify: `tests/AkmlSql.Engine.Tests/PerformanceBaselineTests.cs`

- [ ] **Step 1: Replace the corpus with a larger one**

  The current `CorpusSql` is ~30 statements. Replace with a 300-statement corpus by repeating the four query blocks 10× with renamed identifiers to defeat the parser's identifier cache:

  ```csharp
  private static readonly string CorpusSql = BuildCorpus(repeats: 10);

  private static string BuildCorpus(int repeats)
  {
      var sb = new System.Text.StringBuilder();
      for (int i = 0; i < repeats; i++)
      {
          sb.AppendFormat(System.Globalization.CultureInfo.InvariantCulture,
              "-- block {0}\n", i);
          // Repeat the 4 representative statements from the existing corpus,
          // suffixing identifiers with the block index so the parser doesn't reuse.
          // (verbatim queries from the current file, parameterized on i)
      }
      return sb.ToString();
  }
  ```

- [ ] **Step 2: Add `BulkFormat` as a third measured workload**

  Bulk format runs the 7-stage pipeline on every statement boundary — it's the natural large-workload analog of `FormatRequest`. Add a third measurement and store/compare it:

  ```csharp
  private static (double p50Ms, double p99Ms) MeasureBulkFormat()
  {
      // same shape as MeasureFormat but invokes BulkFormatHandler.HandleAsync
      // with the full corpus split into statements
  }
  ```

  Extend `BaselineDocument` with a `BulkFormatRequest` field.

- [ ] **Step 3: Tighten the threshold**

  ```csharp
  private const double MaxRegressionFraction = 0.05;   // 5 %
  ```

  Update the surrounding comment to reflect the heavier-workload rationale.

- [ ] **Step 4: Recapture the baseline**

  ```bash
  rm -f tests/AkmlSql.Engine.Tests/baselines/m0-baseline.json
  AKML_UPDATE_BASELINE=1 dotnet test tests/AkmlSql.Engine.Tests/AkmlSql.Engine.Tests.csproj \
      --filter "Capture_or_compare_M0_baseline" -c Release
  ```

  Expected: baseline file recreated with 3 measurements (CompletionRequest, FormatRequest, BulkFormatRequest), each p50 in the 10–200 ms range. Do not commit this file — it's per-machine.

- [ ] **Step 5: Confirm the gate fires correctly**

  Re-run the test 3 times consecutively without code changes:

  ```bash
  for i in 1 2 3; do
      dotnet test tests/AkmlSql.Engine.Tests/AkmlSql.Engine.Tests.csproj \
          --filter "Capture_or_compare_M0_baseline" -c Release
  done
  ```

  Expected: all 3 runs PASS. If any flakes at the 5% boundary, increase `MeasureIterations` from 50 → 200 to reduce variance further (cheap — only the perf test loop pays this).

- [ ] **Step 6: Stop. Ready to commit.** Inform the user: "Gap 4 (perf gate tightened to 5%) complete. Ready to commit?"

---

## Phase 6 — Documentation + verification

### Task 11: Update `doc/architecture.md`

**Files:**
- Modify: `doc/architecture.md` § 9b (Spec 021 — M0 Transport Abstraction)

- [ ] **Step 1: Refresh the diagram + prose**

  Replace § 9b's references to `PipeRpcServer` + `_pluggableHandlers` with the final shape: `EngineComposition.Build()` builds the `RpcContext`, `RpcRouter`, and registers all handlers. Three transports (`NamedPipeTransport`, `InProcessTransport`, `WebSocketTransport`) share the router. Quote new file paths.

- [ ] **Step 2: Add a "Closure" footnote**

  ```markdown
  > Closure (2026-05-19): the M0 PRD success metrics that were deferred in PR #236 —
  > `NamedPipeTransport ≤ 150 LOC`, `_cachedSettings` field move, `AiHandlerBase` + 7
  > subclasses, perf gate at 5% — landed via `docs/superpowers/plans/2026-05-19-m0-engine-transport-closure.md`.
  ```

### Task 12: Update `doc/ipc-api.md` "Transport Plurality" section

**Files:**
- Modify: `doc/ipc-api.md`

- [ ] **Step 1: Rename mentions**

  Replace any remaining `PipeRpcServer` references with `NamedPipeTransport`. Wire format unchanged — don't touch the frame description.

### Task 13: Update `specs/021-web-edition/tasks.md` follow-ups section

**Files:**
- Modify: `specs/021-web-edition/tasks.md`

- [ ] **Step 1: Add a "M0 Closure" section near the deferred-followups summary**

  Append:

  ```markdown
  ## M0 Closure (2026-05-19)

  All four M0 success metrics that were deferred when PR #236 merged have now landed via
  `docs/superpowers/plans/2026-05-19-m0-engine-transport-closure.md`:

  - [X] `_cachedSettings` moved from `PipeRpcServer` to `RpcContext.EnsureSettings/InvalidateSettings` (T009 tail)
  - [X] `PipeRpcServer` → `NamedPipeTransport.cs` rename + ≤150 LOC (T020 tail)
  - [X] `AiHandlerBase` + 7 per-message subclasses, each ≤80 LOC (T019 reversal)
  - [X] Perf gate tightened from 25% to 5% via heavier-workload corpus + `BulkFormat` measurement (T025 tail)
  ```

### Task 14: Six-host smoke verification

**Files:** none (build verification)

- [ ] **Step 1: Build every shell host**

  ```bash
  MSBUILD="/c/Program Files/Microsoft Visual Studio/2022/Enterprise/MSBuild/Current/Bin/MSBuild.exe"
  for proj in Ssms20 Ssms21 Ssms22 VS2019 VS2022 VS2026; do
      "$MSBUILD" "src/AkmlSql.${proj}/AkmlSql.${proj}.csproj" -t:Restore -p:Configuration=Release -v:quiet
      "$MSBUILD" "src/AkmlSql.${proj}/AkmlSql.${proj}.csproj" -t:Build -p:Configuration=Release -v:minimal || echo "FAIL: ${proj}"
  done
  ```

  Expected: all 6 succeed. Any failure must be diagnosed (likely a stale reference to `PipeRpcServer` in a shared file — but shell projects are not supposed to reference the engine internals, so this should not happen).

- [ ] **Step 2: Publish the engine + run the installer build**

  ```bash
  dotnet publish src/AkmlSql.Engine/AkmlSql.Engine.csproj -c Release -r win-x64
  ```

  Expected: publish succeeds. Single-file output present in `bin/Release/net10.0/win-x64/publish/`.

- [ ] **Step 3: Stop. Ready to commit + open PR.** Inform the user: "All four PRD gaps closed; 6 hosts build; docs updated. Ready to commit + push?"

---

## Self-review

**Spec coverage** — every PRD success metric is now tied to a task:

| PRD metric | Task |
|---|---|
| `NamedPipeTransport.cs` ≤ 150 LOC | Task 4, 5, 6 |
| No handler class > 200 LOC | Task 9a–9g (LOC checks per file) |
| AI handler classes ≤ 80 LOC each | Task 9a–9g step 3 |
| All ~50 message types via InProcessTransport | Already passing (T023); regression check in Task 5 step 7 |
| Completion p50 / Format p50 within 5% | Task 10 |
| `IRpcRequestHandler<,>`, `IRpcTransport`, `RpcRouter`, `RpcContext` public | Already met; preserved by Task 6 |
| Zero files under `src/AkmlSql.Shell.Shared/` modified | Verified in Task 14 |

**Placeholder scan** — sites with `// verbatim from ...` are intentional (the engineer copies the existing block verbatim); they are not lazy-coding placeholders, they point at the source and the LOC target.

**Type consistency** — `AiHandlerBase<TRequest, TResponse>` is consistent across Tasks 8, 9a–9g. `RpcContext.SettingsLoader`, `EnsureSettings()`, `InvalidateSettings()` consistent across Tasks 2 + 3.

**Risk callouts:**
1. **The current `AiRequestHandler.RefreshSettings()` is called from the `AnalysisSettingsChanged` handler.** When `AiRequestHandler` is deleted (Task 9 step 7), the registry's invalidation closure must also be updated. Cover this by routing AI settings through `AiPipelineServices.SettingsProvider` — that provider reads from `ctx.EnsureSettings().Ai` and is implicitly fresh after `ctx.InvalidateSettings()`.
2. **The `DelegatingHandlerAdapter` raw-response path needs to bypass MessagePack double-serialisation.** Task 5 step 1 specifies the `RpcRouter.RegisterRaw` overload. If skipped, error envelopes for delegating handlers will be wrapped twice.
3. **Perf gate flakiness at 5%.** Task 10 step 5 explicitly tests 3 consecutive runs; if a single run flakes, increase iterations rather than relaxing the threshold.

---

## Execution handoff

**Plan complete and saved to `docs/superpowers/plans/2026-05-19-m0-engine-transport-closure.md`. Two execution options:**

1. **Subagent-Driven (recommended)** — I dispatch a fresh subagent per task, review between tasks, fast iteration. Best for the AI-extraction phase (Tasks 9a–9g) which is 7 atomic units of mechanical work.

2. **Inline Execution** — Execute tasks in this session using executing-plans, batch execution with checkpoints. Best if you want to review file-by-file as we go.

**Which approach?**
