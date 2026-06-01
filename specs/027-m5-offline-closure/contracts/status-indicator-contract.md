# Contract: Cache-aware status indicator (US5)

**Spec**: [spec.md](../spec.md) · **Research**: [research.md](../research.md) Decision 5 · **FRs**: FR-023, FR-024

Extends `Shared/StatusBar.razor` to surface **IntelliSense availability** (a function of bridge state **and** cache presence), not bridge state alone.

## Inputs

1. `IEngineBridge.State` — `Disconnected | Connecting | Open | Reconnecting | Failed` (already subscribed via `StateChanged`).
2. **Cache presence** for the active `(serverCanonicalIdentity, databaseName)` — `ISchemaCacheStore.GetAsync(server, db) != null` (and `PhaseA` non-empty). The active `(server, db)` comes from `Editor.razor`'s active connection (`_activeServerIdentity` / `_activeDatabaseName`), passed to `StatusBar` or read from a shared session service.
3. Reconnect countdown (`RetryScheduled`) — preserved from spec 025.

## Derived state (FR-023)

| Bridge state | Cache present | Indicator label | Meaning |
|---|---|---|---|
| `Open` | any | **Live** | engine-backed IntelliSense |
| `Connecting` | yes | **Cached** | completions from cache while handshaking |
| `Connecting` | no | **Connecting** | handshaking, no fallback yet |
| `Reconnecting` | yes | **Cached** | stable; completions from cache (no flicker) |
| `Reconnecting` | no | **Reconnecting · next try in Ns** | spec-025 countdown |
| `Disconnected` / `Failed` | yes | **Cached** | offline, completions from cache |
| `Disconnected` / `Failed` | no | **Offline** | keyword-only completions |

"Offline" and "Cached" both mean the bridge is down; they differ only on whether typing yields schema completions — which is the user's actual question.

## Behaviour (FR-024)

- Recompute on `IEngineBridge.StateChanged`, `ISchemaSync.ChecksumDrifted` (cache may have just been populated/cleared), and active-connection change.
- Update **in place** — no page reload.
- **No flicker** during reconnect: while `Reconnecting` with cache present, hold **Cached**; flip to **Live** only on `Open`. Never oscillate Cached↔Live mid-handshake (edge case).
- The existing engine-version / web-version labels and the reconnect countdown sub-text are preserved.

## Test contract

`tests/AkmlSql.Web.Tests/Bridge/StatusIndicatorTests.cs` (bUnit), seeding a fake bridge + in-memory cache:

- `Open` ⇒ Live (regardless of cache);
- `Disconnected` + cache present ⇒ Cached; completions resolve (cross-check with `CompletionService` offline path);
- `Disconnected` + no cache ⇒ Offline;
- `Reconnecting` + cache ⇒ Cached, stays Cached across a simulated mid-handshake tick (no flicker), flips to Live on `Open`;
- cache cleared while Disconnected ⇒ Cached → Offline in place.
