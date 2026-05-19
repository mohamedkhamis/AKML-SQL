# Contract — `AiHandlerBase<TRequest, TResponse>`

**Status**: New surface added in P3 of spec 022 (M0 closure).
**Replaces**: `AkmlSql.Engine.Ai.AiRequestHandler` (1896-LOC monolith) and `AkmlSql.Engine.Handlers.Ai.AiMessageHandler` (46-LOC bridge), both deleted at the end of P3.

## Why this base exists

Every AI message handler today inlines the same prologue and epilogue:

1. Load `AiSettings` from the config (~ 3 lines)
2. Run `CheckPrivacyConsent(settings)` — local-provider allowlist, throws `PrivacyConsentRequiredException` for cloud providers without consent (~ 6 lines including the throw)
3. Deserialise the request payload (~ 2 lines)
4. Wrap the per-message logic in a `try { ... } catch (Exception ex) { return error envelope; }` block (~ 8 lines including the try/catch and the envelope)
5. The per-message logic itself (~ 100–400 lines depending on the handler)

The closure lifts steps 1, 2, 4 (and the response-shape concern) into an abstract base so each concrete handler only carries step 5 (the per-message logic). Result: 7 concrete handlers, each ≤ 80 lines, sharing one ~ 60-line base.

## Public surface

```csharp
namespace AkmlSql.Engine.Handlers.Ai;

public abstract class AiHandlerBase<TRequest, TResponse> : IRpcRequestHandler<TRequest, TResponse>
    where TResponse : new()
{
    protected AiPipelineServices Services { get; }
    protected Serilog.ILogger Log { get; }

    protected AiHandlerBase(AiPipelineServices services);

    public abstract int RequestMessageType { get; }
    public abstract int ResponseMessageType { get; }

    public virtual bool SwallowCancellation => true;
    public virtual bool AllowsEmptyPayload => false;

    protected abstract Task<TResponse> InvokeAsync(
        TRequest request,
        RpcContext ctx,
        CancellationToken ct);

    // Sealed so subclasses cannot bypass the consent gate.
    public sealed Task<TResponse> HandleAsync(
        TRequest request,
        RpcContext ctx,
        CancellationToken ct);
}
```

## Behavioural contract

### `HandleAsync` (sealed)

The base's `HandleAsync` runs in this order:

1. Call `CheckPrivacyConsent(Services.SettingsProvider())`.
   - If the configured `Provider` is in `LocalProviders = { "ollama", "lmstudio" }`, skip.
   - If `PrivacyConsentRequired == false`, skip.
   - Otherwise throw `PrivacyConsentRequiredException("CONSENT_REQUIRED:Data will be sent to {provider}. Please confirm in settings.")`.
2. `try { return await InvokeAsync(request, ctx, ct); }`
3. `catch (OperationCanceledException) when (SwallowCancellation) { return new TResponse(); }`

Note: the base does NOT catch generic `Exception`. The contract relies on `RpcRouter` / the transport's outer-catch to turn exceptions into error envelopes. This matches the post-spec-021 pattern where typed handlers do NOT catch their own exceptions; the dispatcher does.

### `InvokeAsync` (abstract)

Subclasses MUST:
- Read the live `AiSettings` via `Services.SettingsProvider()` rather than caching them at class construction time. This guarantees FR-013: settings invalidation flows through immediately.
- Use `Services.SchemaContext`, `Services.Privacy`, and `Services.Parser` for cross-handler concerns.
- Wrap any provider invocation that should retry on rate-limit responses with `AiPipelineServices.ExecuteWithBackoffAsync(...)`. Subclasses that should *not* retry (e.g. `AiGhostTextHandler`) call the provider directly.
- Return a non-null `TResponse`. The base does not enforce non-null; subclasses that need a "no result" shape define a default-constructed instance and return that.

Subclasses MUST NOT:
- Override `HandleAsync` (sealed).
- Catch `OperationCanceledException` themselves unless they intentionally want to override `SwallowCancellation = true`. If they need to swallow cancellation for sub-operations but rethrow at the message level, they should set `SwallowCancellation = false` and handle OCE inside `InvokeAsync`.
- Read settings via `ConfigManager.Load()` directly. Use `Services.SettingsProvider()`.

### `SwallowCancellation`

The default is `true`. Concrete subclasses override to `false` if cancellation should surface as an error envelope rather than an empty response. Today, every AI handler treats cancellation as "no result" — the user typed again, the in-flight request superseded — so `true` matches existing behaviour.

### `AllowsEmptyPayload`

Most AI handlers reject empty payloads (the request has required fields). `false` is correct for the seven concrete handlers. The property is exposed as virtual so a future no-payload AI notification could opt in without changing the base.

## Subclass template

```csharp
namespace AkmlSql.Engine.Handlers.Ai;

public sealed class AiTextToSqlHandler : AiHandlerBase<AiTextToSqlRequest, AiTextToSqlResponse>
{
    public AiTextToSqlHandler(AiPipelineServices svcs) : base(svcs) { }

    public override int RequestMessageType => MessageTypes.AiTextToSql;
    public override int ResponseMessageType => MessageTypes.AiTextToSqlResponse;

    protected override async Task<AiTextToSqlResponse> InvokeAsync(
        AiTextToSqlRequest req, RpcContext ctx, CancellationToken ct)
    {
        var settings = Services.SettingsProvider();
        var schemaContext = await Services.SchemaContext.BuildAsync(req.ConnectionString, req.DatabaseName, ct);
        var prompt = BuildPrompt(req, schemaContext);
        var provider = AiProviderFactory.Create(settings);
        var result = await AiPipelineServices.ExecuteWithBackoffAsync(
            () => provider.CompleteAsync(prompt, ct), maxRetries: 3, ct);
        return new AiTextToSqlResponse { Sql = result.Text, /* ... */ };
    }

    private static string BuildPrompt(AiTextToSqlRequest req, SchemaContext ctx) => /* ... */;
}
```

LOC budget for each subclass: ≤ 80, including the `using` block and namespace. If the prompt-building helper grows large, move it to `AiPipelineServices` or a per-prompt-type helper class — do NOT inline it past the budget.

## Invariants

1. Every class under `src/AkmlSql.Engine/Handlers/Ai/` after P3 either IS `AiHandlerBase<,>` or DERIVES FROM it. Verified by source search:
   ```bash
   grep -L "AiHandlerBase" src/AkmlSql.Engine/Handlers/Ai/*.cs
   ```
   MUST return zero lines (allowing for the base class file itself, which contains the string in its declaration).
2. No call to `CheckPrivacyConsent` exists outside the base. Verified by:
   ```bash
   grep -rn "CheckPrivacyConsent" src/AkmlSql.Engine/
   ```
   MUST return exactly two lines — the definition in `AiHandlerBase.cs` and its single call site, also in `AiHandlerBase.cs`.
3. No subclass overrides `HandleAsync`. The `sealed` modifier enforces this at compile time.
4. Concrete subclasses MUST NOT reference `_aiSettings` or any privately-cached settings field. Settings come exclusively through `Services.SettingsProvider()`.
5. Bytes returned for every AI message type MUST be identical pre- and post-closure for the same input. Smoke tests for each subclass cover the local-provider happy path; the in-process matrix test covers the engine-wide round-trip.

## Migration table (Phase 3 of the closure plan)

| Old (`AiRequestHandler`) | New (`Handlers/Ai/*Handler.cs`) | Base |
|---|---|---|
| `HandleTextToSqlAsync` (line 276) | `AiTextToSqlHandler` | `AiHandlerBase<AiTextToSqlRequest, AiTextToSqlResponse>` |
| `HandleExplainAsync` (line 480) | `AiExplainHandler` | `AiHandlerBase<AiExplainRequest, AiExplainResponse>` |
| `HandleFixAsync` (line 701) | `AiFixHandler` | `AiHandlerBase<AiFixRequest, AiFixResponse>` |
| `HandleOptimizeAsync` (line 916) | `AiOptimizeHandler` | `AiHandlerBase<AiOptimizeRequest, AiOptimizeResponse>` |
| `HandleIndexAnalysisAsync` (line 1084) | `AiIndexAnalysisHandler` | `AiHandlerBase<AiIndexAnalysisRequest, AiIndexAnalysisResponse>` |
| `HandleChatAsync` (line 1259) | `AiChatHandler` | `AiHandlerBase<AiChatRequest, AiChatResponse>` |
| `HandleGhostTextAsync` (line 1476) | `AiGhostTextHandler` | `AiHandlerBase<AiGhostTextRequest, AiGhostTextResponse>` |

After all seven migrations land + smoke tests pass: `AiRequestHandler.cs` is deleted; `Handlers/Ai/AiMessageHandlers.cs` is deleted; `EngineHandlerRegistry` registers each new subclass via `router.Register(...)` with no bridge in the middle.
