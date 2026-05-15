# M0 — Engine Dispatcher Refactor & Transport Abstraction

**Status**: Draft
**Phase**: M0 (precondition for the web edition)
**Estimated effort**: 2 weeks
**Branch prefix**: `m0-engine-transport`

---

## 1. Executive summary

The engine's IPC entry point — `PipeRpcServer.cs` — is a monolithic ~50-case dispatcher hard-wired to named pipes. To unblock the Blazor WebAssembly web edition (M2+) and the local-agent WebSocket bridge (M3), the engine must serve the same request handlers over multiple transports: named pipes (current), localhost WebSocket (browser ↔ local agent), and in-process direct calls (Blazor WASM running engine logic inside the browser).

This milestone refactors the dispatcher into discrete handler classes registered against a `IRpcTransport` abstraction, with **zero behavioural change** to the SSMS and VS shell extensions. All 6 shell hosts continue to work identically after the refactor; the only externally visible change is that `AkmlSql.Engine` exposes a clean handler API that new transports can mount.

---

## 2. Why now

Three forces converge:

1. **The web edition is the next major track** and is blocked on transport flexibility. The "thick browser, thin server" architecture requires the same handler logic to run in WASM (in-process) and on a local agent (WebSocket).
2. **The dispatcher is already a known liability** — flagged in the prior codebase audit as a monolithic 50-case switch with duplicated AI-pipeline boilerplate.
3. **Spec 020 just merged.** The shell surface is stable, no parallel feature work is touching `PipeRpcServer.cs`, and the diff blast radius is contained to the engine project.

Doing the refactor before adding the Blazor project means the new project consumes a clean API from day one.

---

## 3. Current state

### 3.1 Dispatcher shape (today)

```
PipeRpcServer
├── NamedPipeServerStream lifecycle (create / accept / ACL)
├── Frame reader (4-byte length + 4-byte XOR CRC + MessagePack)
├── HandleRequestAsync(RpcMessage)
│   └── switch (msg.MessageType)
│       case 1:  CompletionRequest    → inline handler ~30 lines
│       case 2:  FormatRequest        → delegates to FormatRequestHandler
│       case 3:  AnalysisRequest      → inline handler ~40 lines
│       ... ~50 cases ...
│       case 47: AISuggestRequest     → inline handler ~60 lines (duplicated boilerplate)
│       case 48: AIExplainRequest     → inline handler ~55 lines (duplicated boilerplate)
└── Response writer (frame + send)
```

### 3.2 Problems

| # | Problem | Impact |
|---|---------|--------|
| 1 | Transport (pipe) and dispatch (switch) entangled | Cannot reuse handlers from WASM or WebSocket |
| 2 | ~15 message types have inline logic; ~35 delegate to handler classes | Inconsistent refactor never finished |
| 3 | AI pipeline boilerplate duplicated across 6 AI message types | Bug fixes must be applied 6 times |
| 4 | `_cachedSettings` is a field on `PipeRpcServer` | Coupled to pipe lifecycle |
| 5 | No handler registration model | Adding a message type requires editing one giant file |
| 6 | Tests instantiate `PipeRpcServer` with a real pipe | Slow; not unit tests |

---

## 4. Proposed architecture

### 4.1 Component diagram

```
                       ┌─────────────────────────────────────┐
                       │         AkmlSql.Engine              │
                       │                                     │
  ┌──────────────┐     │   ┌─────────────────────────────┐   │
  │ Shell        │─────┼──→│  IRpcTransport              │   │
  │ (named pipe) │     │   │  ├─ NamedPipeTransport      │   │
  └──────────────┘     │   │  ├─ WebSocketTransport (M3) │   │
                       │   │  └─ InProcessTransport      │   │
  ┌──────────────┐     │   └────────────┬────────────────┘   │
  │ Browser      │─────┼──┐             │ frames              │
  │ (WebSocket)  │     │  │             ▼                     │
  └──────────────┘     │  │   ┌─────────────────────────────┐ │
                       │  │   │  RpcRouter                  │ │
  ┌──────────────┐     │  └──→│  - resolves MessageType     │ │
  │ Blazor WASM  │─────┼─────→│  - deserialises payload     │ │
  │ (in-process) │     │      │  - dispatches to handler    │ │
  └──────────────┘     │      └────────────┬────────────────┘ │
                       │                   │                  │
                       │      ┌────────────▼────────────────┐ │
                       │      │  IRpcRequestHandler<T,R>    │ │
                       │      │  ├─ CompletionHandler       │ │
                       │      │  ├─ FormatHandler           │ │
                       │      │  ├─ AnalysisHandler         │ │
                       │      │  ├─ SnippetHandler          │ │
                       │      │  ├─ RefactoringHandler      │ │
                       │      │  ├─ SchemaHandler           │ │
                       │      │  ├─ AiSuggestHandler ─┐     │ │
                       │      │  ├─ AiExplainHandler  ├─ AiHandlerBase │
                       │      │  ├─ AiOptimizeHandler ┘     │ │
                       │      │  └─ ... (~20 handlers)      │ │
                       │      └─────────────────────────────┘ │
                       └─────────────────────────────────────┘
```

### 4.2 Core interfaces

```csharp
public interface IRpcTransport : IAsyncDisposable
{
    Task StartAsync(CancellationToken ct);
    event Func<RpcMessage, CancellationToken, Task<RpcMessage?>> RequestReceived;
}

public interface IRpcRequestHandler<TRequest, TResponse>
{
    int MessageType { get; }
    Task<TResponse> HandleAsync(TRequest request, RpcContext ctx, CancellationToken ct);
}

public sealed class RpcRouter
{
    public void Register<TReq, TResp>(IRpcRequestHandler<TReq, TResp> handler);
    public Task<RpcMessage?> RouteAsync(RpcMessage msg, CancellationToken ct);
}

public sealed class RpcContext
{
    public AppSettings Settings { get; }
    public SessionManager Sessions { get; }
    public SchemaCacheManager SchemaCache { get; }
    public ILogger Logger { get; }
}
```

### 4.3 What changes, what doesn't

| Component | Status |
|-----------|--------|
| `PipeRpcServer` | Becomes `NamedPipeTransport` — frame I/O only, ≤ 150 lines |
| Frame format `[length][CRC][MessagePack]` | **Unchanged** — shell extensions need zero updates |
| `RpcMessage` (MessageType / RequestId / Payload) | **Unchanged** |
| All ~50 message type integer codes | **Unchanged** |
| `FormatRequestHandler`, `SnippetRequestHandler`, etc. | Refactored to implement `IRpcRequestHandler<,>` |
| `_cachedSettings` field | Moves to `RpcContext`; same invalidation semantics |
| AI handler boilerplate | Lifted into `AiHandlerBase` abstract class |
| Shell extensions (all 6 hosts) | **Unchanged** |

---

## 5. Handler taxonomy

| Group | Folder | Message types |
|-------|--------|---------------|
| **Completion** | `Engine/Handlers/Completion/` | CompletionRequest, QuickInfo, SignatureHelp |
| **Formatting** | `Engine/Handlers/Formatting/` | FormatRequest, FormatPreview, ProfileLoad, ProfileSave, ProfileList |
| **Analysis** | `Engine/Handlers/Analysis/` | AnalysisRequest, AnalysisSettingsChanged, RuleList |
| **Snippets** | `Engine/Handlers/Snippets/` | SnippetExpand, SnippetList, SnippetSave, SnippetDelete |
| **Refactoring** | `Engine/Handlers/Refactoring/` | RefactorPreview, RefactorApply, SmartRename |
| **Schema** | `Engine/Handlers/Schema/` | SchemaRefresh, SchemaQuery, SchemaProgress |
| **AI** | `Engine/Handlers/Ai/` | AiSuggest, AiExplain, AiFix, AiOptimize, AiIndex, AiChat, GhostText |
| **Session/control** | `Engine/Handlers/Control/` | SessionOpen, SessionClose, DocumentUpdate, Ping, Shutdown |

---

## 6. Transport profiles

| Transport | Process model | Use case | Phase |
|-----------|---------------|----------|-------|
| **NamedPipeTransport** | Engine process serves pipe; shell process connects | Current SSMS / VS shells | M0 |
| **InProcessTransport** | Handlers invoked directly via method call; no serialization | Engine unit tests; Blazor WASM running engine logic in-browser | M0 |
| **WebSocketTransport** | Engine process serves localhost or LAN WebSocket | Browser ↔ local engine | M3 |

---

## 7. Milestones

### M0.1 — Skeleton (week 1, days 1–2)

Add `IRpcTransport`, `IRpcRequestHandler<,>`, `RpcRouter`, `RpcContext`. No handler moved yet. `PipeRpcServer` continues to work via its old switch; the new types live alongside, unused.

### M0.2 — InProcessTransport + first handler (week 1, days 3–4)

Move `CompletionRequest` handling out of the switch into `CompletionHandler`. Add `InProcessTransport`. Add a unit test that exercises the completion handler via `InProcessTransport` with zero serialization.

### M0.3 — Migrate remaining inline handlers (week 1, day 5 – week 2, day 2)

Migrate the ~15 message types with inline logic in the switch. Group by folder. Run full test suite after each group.

### M0.4 — Migrate delegating handlers (week 2, days 3–4)

Wrap existing handler classes in `IRpcRequestHandler<,>` adapters. `PipeRpcServer` renamed to `NamedPipeTransport`, ≤ 150 lines.

### M0.5 — AiHandlerBase consolidation (week 2, day 5)

Lift duplicated prompt-construction / provider-routing / streaming boilerplate from AI handlers into `AiHandlerBase`.

### M0.6 — Documentation + IPC contract review (week 2, end)

Update `doc/architecture.md` and `doc/ipc-api.md`. The wire format is unchanged so `ipc-api.md` only gets a section on transport plurality.

---

## 8. Risks and mitigations

| Risk | Likelihood | Impact | Mitigation |
|------|------------|--------|------------|
| Regression in shell extensions (frame compatibility) | Low | High | Frame format unchanged; integration test suite runs all 50 messages over a real pipe before and after each milestone |
| Performance regression in completion hot path | Medium | Medium | Baseline `CompletionRequest` p50/p99 before M0.2; assert no >5% regression |
| `_cachedSettings` semantics drift | Medium | Medium | Literal field move; same invalidation handler; one targeted test |
| Scope creep — "while we're in here, let's fix X" | High | High | Two-week budget is hard; defer all non-refactor work |

---

## 9. Success metrics

- `NamedPipeTransport.cs` ≤ 150 LOC
- No handler class > 200 LOC; AI handler classes ≤ 80 LOC each
- All ~50 message types exercised via `InProcessTransport` in `AkmlSql.Engine.Tests`
- `CompletionRequest` p50 within 5% of baseline; `FormatRequest` p50 within 5% of baseline
- `IRpcRequestHandler<,>`, `IRpcTransport`, `RpcRouter`, `RpcContext` exposed as public API
- Zero files under `src/AkmlSql.Shell.Shared/` modified

---

## 10. Out of scope

- `WebSocketTransport` implementation — M3
- Streaming responses (AI tokens) — separate spec after M3
- Authentication on transports — M3 covers LAN-pairing-token model
- Engine process supervision changes
- Source generators for handler registration

---

## 11. Open questions

1. `IRpcTransport` event vs `Func` property — decide in M0.1
2. Handler registration: manual or reflection scan? — lean reflection (matches `RuleRegistry`)
3. Cancellation token contract for in-process callers — document explicitly
4. `RpcMessage.RequestId` for in-process — leave field; transport ignores

---

## 12. Definition of done

- [ ] All 6 sub-milestones merged
- [ ] `doc/architecture.md` and `doc/ipc-api.md` updated
- [ ] All 6 shell hosts (SSMS 20/21/22, VS 2019/22/26) deploy and smoke-test green
- [ ] Engine test suite green; in-process tests cover all message types
- [ ] Performance regression within 5% on completion + formatting
- [ ] CHANGELOG entry written
- [ ] Branch `m0-engine-transport` merged to master via PR
