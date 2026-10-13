# Contract: IPC changes (web ↔ engine)

**Feature**: 041-web-ssms-ux-parity

**Transport:** the existing WebSocket bridge (and named pipe) with MessagePack frames, 16 MB
inbound cap, 15 MB reply budget. The 200+ band numbers request and reply **adjacently**
(`ExecuteQuery 212 / ExecuteQueryResult 213`); the next free pair is 217/218.

**Rules for this contract:**
- Every change is an additive key, a new adjacent type pair, or a new capability id.
- Keys are appended in order and never renumbered. Optional fields are nullable or carry a
  sentinel (`-1`, `0`).
- An **older web bundle** ignores unknown trailing keys: it still gets `Status`, `ErrorMessage`
  (first error), `ResultSets` (always returned now) and `Messages string[]` (SSMS-formatted
  lines), so its banner and grids keep working.
- A **newer web bundle on an older engine** sees empty `MessageDetails`/`Batches`, null
  `CurrentDatabase`, no host kind, and the capabilities absent — it falls back to the legacy
  strings, hides Parse and the database selector, and labels styles "On this engine".
- A request type the engine does not know gets **no reply** (`RpcRouter` returns null), so
  every new request is sent only when its capability is advertised.
- The web never sends the new keys to the SSMS shell's pipe; the shell is unaffected.

**Tests:** `tests/AkmlSql.Core.Tests/Ipc/ExecuteQueryMessagesCompatTests.cs` (legacy 8-element
`ExecuteQueryResult` and 11-element `ExecuteResultSet` payloads deserialise; a 13-element
payload deserialises into a copy of the old class), `AllMessageTypesInProcessTests` (217 and
219 registered; 218 and 220 not; factory entries for both), `HandshakeHandlerTests`
(capabilities contain both ids; `HostKind` echoed).

---

## Capabilities (`HandshakeResponse.EngineCapabilities`, key 3)

| Id | Gates |
|---|---|
| `session.database.v1` | `ListDatabasesRequest.SessionId`, `ChangeDatabase` (217/218), `ExecuteQueryResult.CurrentDatabase` |
| `execute.v2` | `ExecuteQueryResult.MessageDetails/Batches/CompletedAtUnixMs/MessagesOmitted`, `ExecuteResultSet.BatchIndex`, `ExecuteParse` (219/220) |

---

## `HandshakeResponse` (201)

| Key | Field | Type | Meaning |
|---|---|---|---|
| 0–7 | *(unchanged)* | | Status, EngineVersion (raw, with `+hash`), ChosenProtocolVersion, EngineCapabilities, NewBearerToken, ServerCanonicalIdentity, ErrorMessage, ServerTlsThumbprint |
| **8** | `HostKind` | `string?` | `"ide"` = started by SSMS over the named pipe (runs as the signed-in user); `"service"` = Windows service (`--web`); `"console"` = interactive `--web`; null = older engine |
| **9** | `RunsAs` | `string?` | `WindowsIdentity.GetCurrent().Name`; shown only on hover and in Diagnostics |

Web: `IEngineBridge.EngineHostKind` / `EngineRunsAs` (default-interface members, null when
absent; cleared on disconnect). `IProfileStore.EngineStylesGroupName` = `HostKind == "ide"`
? "Shared with SSMS" : "On this engine" (FR-063).

---

## `ListDatabasesRequest` (94) → `ListDatabasesResult` (194)

| Key | Field | Type | Meaning |
|---|---|---|---|
| 0 | `ConnectionString` | string | *(unchanged)* full connection string (pre-connect path) |
| **1** | `SessionId` | `string?` | When set and `ConnectionString` is empty: use the session's stored connection string (engine-held credentials), after alias resolution and `BridgeSqlTargetGuard`; `Ok=false` with an error when the session is unknown or disconnected |

Result unchanged (`Ok`, `Databases`, `Error`); same `sys.databases` + `HAS_DBACCESS` query.

---

## `ChangeDatabaseRequest` (**217**) → `ChangeDatabaseResponse` (**218**) — new

| Key | Field | Type | Meaning |
|---|---|---|---|
| 0 | `SessionId` | string | canonical web session id |
| 1 | `DatabaseName` | string | identifier-safe (no `;` `=` `'` `"` or control characters) |

| Key | Field | Type | Meaning |
|---|---|---|---|
| 0 | `Ok` | bool | |
| 1 | `DatabaseName` | string | server-reported current database (`conn.Database`) |
| 2 | `ErrorMessage` | `string?` | SQL error text (916, 4060 …) or guard refusal |
| 3 | `ConnectionWasReset` | bool | the persistent connection had to be reopened (temp tables and SET options lost) |

Engine steps: connected session → identifier check → rewrite `Initial Catalog` → guard →
synchronous `ChangeDatabase` under the session gate (same physical connection; opens it first
if it was never opened) → `SessionManager.SetDatabase` (no dispose) → create the `(sessionId,
newDb)` cache under a claimed Phase A/B populate (as `ConnectionChangedHandler` does; never
`SchemaRefreshService.Refresh` on a missing key) → reply. Handler: typed
`IRpcRequestHandler<ChangeDatabaseRequest, ChangeDatabaseResponse>`.

---

## `SchemaRefreshRequest` (6) — unchanged, but note

It is a **notification** (`ResponseMessageType => 0`); `SchemaRefreshComplete` (104) is a dead
constant with no DTO. The web Schema panel's Refresh sends it with
`IEngineBridge.SendNotificationAsync` (a `SendAsync` would wait forever) and then fetches
Phase A/B itself. The engine's `SchemaRefreshService.Refresh` must only be called for a cache
that already exists; a database switch creates or claims the new cache first.

## `ExecuteQueryRequest` (212) — unchanged

No new keys. `GO` splitting is always on. Parse is a separate type (below), never a flag.

## `ExecuteQueryResult` (213, and 220 for Parse)

| Key | Field | Type | Meaning |
|---|---|---|---|
| 0–7 | *(unchanged)* | | `QueryId`, `Status`, `ErrorMessage` (= first error's text, or null), `ResultSets` (always present, possibly empty), `Messages string[]` (SSMS-formatted: `Msg 208, Level 16, State 1, Line 4` + newline + text; `Procedure <name>,` before `Line` when set; `(N rows affected)`; PRINT text; System lines), `ElapsedMs` (whole script), `TotalRowsAffected` (sum of DML counts; -1 when none), `ConnectionWasReset` |
| **8** | `MessageDetails` | `ExecuteMessageDto[]` | every message in stream order |
| **9** | `Batches` | `ExecuteBatchDto[]` | one per batch (repeat loops share one entry; `RepeatCount` says how many times) |
| **10** | `CompletedAtUnixMs` | long | UTC, end of script; 0 when absent |
| **11** | `MessagesOmitted` | int | messages dropped beyond `MaxMessages = 10,000` |
| **12** | `CurrentDatabase` | `string?` | `conn.Database` captured in a `finally` after the reader is disposed, present on **every** reply including error/timeout envelopes; the engine re-keys the session when it differs; the web uses it as the `ApplyChangesRequest.BaseCatalog` fallback when a set carries no base catalog |

`ExecuteStatus` constants: 0 Ok · 1 Error · 2 Cancelled · 3 TimedOut · 4 NoConnection ·
**5 Skipped** · **6 Disconnected** (client-only) · **7 NoReply** (client-only).

Top-level `Status`: Ok when no Error message anywhere; Error when any batch has an error (the
script still ran on); TimedOut / Cancelled when the script stopped for that reason; a
separator error (`GO SELECT 2`, `GO;`, `GO 0`) → Error with one message and no batch run.

### `ExecuteResultSet`

| Key | Field | Change |
|---|---|---|
| 1 | `ColumnSqlTypes` | now the **declared** type: `nvarchar(50)`, `nvarchar(max)`, `varbinary(max)`, `decimal(10,2)`, `datetime2(3)`, `time(0)`, `datetimeoffset(7)`, `char(5)`, `binary(2)`; bare for `int bigint smallint tinyint bit date datetime smalldatetime money smallmoney float real uniqueidentifier xml text ntext image timestamp sql_variant` and UDTs (`dbo.MyType`). Old engines send bare names; `SqlTypeName.Parse` accepts both. |
| 6 | `Provenance[i].Precision` / `Scale` (keys 9/10) | **null** when SqlClient reports the 255 sentinel (fixes Apply on `date`/`money` columns) |
| **11** | `BatchIndex` | int; the batch that produced the set |

### `ExecuteMessageDto` (new)

| Key | Field | Type |
|---|---|---|
| 0 | `Kind` | int: 0 Info · 1 RowCount · 2 Error · 3 System |
| 1 | `Text` | string |
| 2 | `BatchIndex` | int |
| 3 | `Number` | int (0 for PRINT) |
| 4 | `Severity` | int (`SqlError.Class`) |
| 5 | `State` | int |
| 6 | `Line` | int, batch-relative, 0 = none |
| 7 | `Procedure` | `string?` |
| 8 | `ResultSetIndex` | int, -1 = none |
| 9 | `RowCount` | long, -1 = none |

### `ExecuteBatchDto` (new)

| Key | Field | Type |
|---|---|---|
| 0 | `Index` | int |
| 1 | `StartLine` | int, 1-based line within the submitted SQL where the batch text starts (text sent verbatim, so the server's line 1 = `StartLine`) |
| 2 | `StartOffset` | int |
| 3 | `EndOffset` | int |
| 4 | `Status` | `ExecuteStatus` |
| 5 | `ElapsedMs` | long |
| 6 | `RepeatCount` | int (≥ 1; clamped to `MaxBatchRepeat = 10,000` with a System message) |
| 7 | `ErrorCount` | int |

### Engine behaviour summary (for the record)

- Batches: token-stream split (`TsqlParserService.SplitBatches`); `GO` only at line start
  (also after a same-line block comment), never in strings/comments/`[GO]`; `GO n` repeats;
  other tokens on the GO line refuse the script.
- Per batch: command behaviour `KeyInfo` only when the first token is not
  `CREATE/ALTER` + `PROC/PROCEDURE/VIEW/FUNCTION/TRIGGER/SCHEMA/DEFAULT/RULE` and a `SELECT`
  token exists; else `Default`. Timeout per command.
- Errors ≤ 16 via `InfoMessage` (flag scoped to the gate), reader continues; connection closed
  → rest Skipped; timeout → rest Skipped; cancellation → stop.
- Counts via `StatementCompleted`; truncated SELECT → `(N rows returned; more exist)`.
- One 15 MB budget for sets + messages (text counted twice for the legacy array);
  `MaxResultSets = 1,000`.

---

## `ExecuteParse` (**219**) → `ExecuteParseResult` (**220**) — new

Payloads: `ExecuteQueryRequest` / `ExecuteQueryResult` (same classes). Registered with
`RegisterRaw` to the execute handler in parse mode. Under the session gate: `SET PARSEONLY ON`
(own command) → each batch once with `CommandBehavior.Default` (results ignored; no row counts)
→ `SET PARSEONLY OFF` (own command; if it throws, `conn.Close()` so the next execute reopens
and reports `ConnectionWasReset`). `Status` Ok → the web prints
"Commands completed successfully."; errors carry batch-relative lines mapped as for execute.

---

## Documentation

`doc/ipc-api.md` gains: constants 94/194 and 212–220 in the table; sections for
ExecuteQuery/ExecuteQueryResult, ExecuteCancel, ApplyChanges, ChangeDatabase, ExecuteParse; the
two capability ids; the handshake keys 8–9; the declared-type form of `ColumnSqlTypes`; the
null-sentinel rule for precision/scale.
