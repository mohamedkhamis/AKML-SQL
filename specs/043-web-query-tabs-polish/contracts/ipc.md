# Contract: IPC and engine transport changes

**Branch**: `043-web-query-tabs-polish` | **Date**: 2026-10-21

IPC evolves append-only (CLAUDE.md). This feature adds **no message type and no capability
string**. Two behaviours change, both backwards compatible; `doc/ipc-api.md` is updated in the
same change (constitution, documentation currency).

## 1. `SessionDisconnect` gains `Release`

Existing request (capability `session.disconnect.v1`), MessagePack keys append-only:

| Key | Field | Type | Default | Semantics |
| --- | --- | --- | --- | --- |
| 0 | SessionId | string | | unchanged |
| next free | Release | bool | false | **new**. `true`: after today's disconnect (close the session's connection with a 15 s budget, rolling back an open transaction; mark the session disconnected; invalidate the database provider), the engine also removes the session entry and its document from `SessionManager`, drops every schema cache keyed by this session (memory and disk) and the session's prefetch state. The session id may be reused later by a new `ConnectionChanged`, which starts it afresh. |

Reply: unchanged. An engine that predates the key ignores it and behaves as today (disconnect
only). The web sends `Release=true` when a tab is closed and `Release=false` (or omits it) on
Disconnect from the status bar / Settings and on a restored tab's reattach (R2). On reattach the
web **awaits this reply before sending `ConnectionChanged`**; under §2 a notification is not
ordered after an earlier request, so the browser must sequence the two.

## 2. The WebSocket transport answers requests concurrently

Applies to `src/AkmlSql.Engine/Transports/WebSocketTransport.cs` only (the named-pipe transport
is unchanged; SSMS is unaffected).

| Frame | Handling |
| --- | --- |
| Notification (`RequestId == 0`: `DocumentChanged`, `ConnectionChanged`, settings changed, …) | handled **inline on the read loop, in arrival order**, as today |
| Request (`RequestId != 0`) | dispatched to the thread pool; the read loop continues with the next frame; the reply is written under the socket's send lock |
| Handshake / authentication frames | unchanged (inline, before anything else) |

Guarantees:

- Frames on one socket are **read** in order; a notification read before a request has been
  applied before that request is dispatched. So `DocumentChanged` then `Completion` for one
  session still completes against the new text.
- Two requests on **different** sessions may run at the same time (overlapping executes,
  completion during another tab's run, cancel during another tab's run).
- Two executes on the **same** session still serialise on that session's `SessionConnection`
  gate (unchanged).
- Replies may be written out of request order; the browser correlates by request id (it already
  does).
- A notification is **not** ordered after an earlier request on the same socket (the request may
  still be running when the notification is applied). The browser sequences them where it
  matters: it awaits the `SessionDisconnect` reply before a reattach's `ConnectionChanged`, and it
  does not change or disconnect a tab's connection while that tab's run is in flight.
- A handler exception still produces the error reply for that request id; cancellation of the
  socket cancels every in-flight handler (the existing token).

No capability string: a browser cannot tell a serial engine from a concurrent one, and does not
need to; with an older engine the behaviour is today's one-at-a-time answering.

Spike S1 (research.md) audits the shared state before this is enabled: `RpcContext` settings
cache, the SQLite history store, `DatabaseProvider` / `TryClaimPopulation`, `SchemaCacheManager`,
and the send path.

## 3. Unchanged messages the feature relies on

| Message | Use by this feature |
| --- | --- |
| `ConnectionChanged` (notification) | sent per tab with the tab's engine session id; re-sent on a restored tab's first activation or run, after the `SessionDisconnect` reply has been awaited |
| `TestSqlConnection`, `ListDatabases`, credential requests | shared, no session id, unchanged |
| `ChangeDatabase` (`session.database.v1`) | per tab |
| `ExecuteQuery`, `ExecuteCancel`, `ExecuteParse`, `ApplyChanges` (`execute.v2`) | per tab; the request carries the tab's session id captured before the first await |
| `DocumentChanged` | per tab, before completion / quick info / signature requests, as today |
| `Completion`, `QuickInfo`, `SignatureHelp`, `GoToDefinition`, `WildcardExpansion` | stamped with the owning tab's session id |
| `Format`, `FormatPreview`, `CodeAnalysis` (PostgreSQL paths) | stamped with the owning tab's session id |
| `SchemaChecksum`, `SchemaPhaseA`, `SchemaPhaseB`, `SchemaRefresh` | sent by the single `SchemaSync` for the **active** tab's session (stop / start on tab switch) |
| History record request | `SessionKey` = tab key, `TabTitle` = tab name |
