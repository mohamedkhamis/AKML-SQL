# Contract — `RpcContext` Settings Access

**Status**: New surface added in P1 of spec 022 (M0 closure).
**Backwards compatibility**: This work removes one public property (`Settings { get; set; }`) and adds two methods (`EnsureSettings()`, `InvalidateSettings()`) plus one required-init property (`SettingsLoader`). Callers of the old `Settings` property must migrate. All existing callers live inside the engine project — no shell-side or external consumer is affected.

## Public surface

```csharp
namespace AkmlSql.Engine;

public sealed class RpcContext
{
    public required SessionManager Sessions { get; init; }
    public required SchemaCacheManager SchemaCache { get; init; }
    public required Serilog.ILogger Logger { get; init; }

    /// <summary>
    /// On-disk settings loader. Wired by EngineComposition to ConfigManager.Load.
    /// </summary>
    public required Func<AppSettings> SettingsLoader { get; init; }

    public TsqlParserService? ParserService { get; init; }
    public SchemaMetadataService? SchemaMetadata { get; init; }

    /// <summary>
    /// Returns the cached AppSettings. Calls <see cref="SettingsLoader"/> on first
    /// invocation; subsequent calls return the same instance until
    /// <see cref="InvalidateSettings"/> is called. Thread-safe.
    /// </summary>
    public AppSettings EnsureSettings();

    /// <summary>
    /// Drops the cached reference. The next <see cref="EnsureSettings"/> call
    /// re-invokes the loader. Thread-safe; may be called concurrently with
    /// <see cref="EnsureSettings"/>.
    /// </summary>
    public void InvalidateSettings();
}
```

## Behavioural contract

### `EnsureSettings()`

- MUST be idempotent across calls until `InvalidateSettings()` is called.
- MUST be thread-safe: concurrent callers MUST see the same `AppSettings` instance, and the `SettingsLoader` MUST be invoked at most once between invalidations (no double-loading on race).
- MUST NOT block longer than the underlying loader requires. Default loader (`ConfigManager.Load`) is bound by one synchronous on-disk read.
- MUST NOT throw transient errors — if the loader throws, the exception propagates and the cache remains empty (next call re-attempts).

### `InvalidateSettings()`

- MUST be safe to call when the cache is already empty (no-op).
- MUST be safe to call from a different thread than the one currently inside `EnsureSettings()`. The contract guarantees the next `EnsureSettings()` call (after `InvalidateSettings()` returns) re-invokes the loader, but does not guarantee any specific behaviour for an `EnsureSettings()` call already in progress at the moment of invalidation (it returns whichever value it has already computed; the call right after invalidation observes the fresh value).

### `SettingsLoader`

- MUST be non-null at construction time. `required init` enforces this on a C# 11+ compiler.
- MUST be callable repeatedly. The implementation may have side effects (e.g. reading disk), but the contract does not promise to call it any specific number of times.
- MUST be deterministic enough that two calls without an intervening config change return semantically equivalent values. The closure pins the canonical implementation to `ConfigManager.Load`, which returns a fresh `AppSettings` reading the same on-disk JSON file.

## Caller migration

Existing callers that read settings:

| Pre-closure call site | Migration |
|---|---|
| `PipeRpcServer.cs:35` (field declaration) | DELETE |
| `PipeRpcServer.Handlers.cs:35` (`Settings = _cachedSettings`) | DELETE — set `SettingsLoader = ConfigManager.Load` instead |
| `PipeRpcServer.Handlers.cs:46-49` (in CompletionHandler closure) | Replace closure body with `ctx.EnsureSettings()` |
| `PipeRpcServer.Handlers.cs:90-93` (in AnalysisHandler closure) | Replace closure body with `ctx.EnsureSettings()` |
| `PipeRpcServer.Handlers.cs:100-102` (AnalysisSettingsChanged callback) | Replace `_cachedSettings = null; _rpcContext.Settings = null;` with `ctx.InvalidateSettings()` |

Future callers (in tests, in new handlers): use `ctx.EnsureSettings()` for reads, `ctx.InvalidateSettings()` for explicit invalidation. Do NOT add a new property bypassing this API.

## Invariants

1. The cached `AppSettings` field appears in exactly one source file (`RpcContext.cs`). Verified by:
   ```bash
   grep -rln "_cachedSettings\b" src/AkmlSql.Engine/
   ```
   MUST return exactly one path.
2. No code outside `EngineComposition` and the `AnalysisSettingsChanged` handler calls `InvalidateSettings()`.
3. Settings reads in handler code MUST go through the context, not through `ConfigManager.Load()` directly. Tests verify this via a search for stray `ConfigManager.Load` calls inside `src/AkmlSql.Engine/Handlers/`.
