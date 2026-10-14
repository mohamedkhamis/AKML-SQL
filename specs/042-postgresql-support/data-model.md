# Data Model: PostgreSQL support in the engine and the web edition

**Branch**: `042-postgresql-support` | **Date**: 2026-10-14 | **Spec**: [spec.md](./spec.md)

Phase 1 output. Entities from the spec with their concrete shape, where they live, validation rules
and state transitions. Wire contracts are in [contracts/ipc.md](./contracts/ipc.md).

## 1. Database kind

```text
enum DatabaseKind { SqlServer = 0, PostgreSql = 1 }        // AkmlSql.Core
```

Serialised as an `int` in MessagePack (append-only keys), as the strings `sqlserver` /
`postgresql` in SQLite, JSON and search syntax. Default everywhere is SqlServer so that every
existing record, message and config file keeps its meaning.

## 2. Saved connection (web, IndexedDB `savedSqlConnections`)

| Field | Type | Default | Notes |
| --- | --- | --- | --- |
| Id | string | guid | unchanged |
| Name | string | "" | unchanged |
| Kind | DatabaseKind | SqlServer | **new**; missing on old records → SqlServer |
| Server | string | "localhost" | host for PostgreSQL |
| Port | int? | null | **new**; PostgreSQL default 5432 when null |
| Database | string | "master" | PostgreSQL default `postgres` when the kind is PostgreSql and the value is empty |
| WindowsAuth | bool | true | SQL Server only; ignored for PostgreSQL |
| Login | string? | null | PostgreSQL user name (required) |
| SslMode | string? | null | **new**; `disable` / `prefer` / `require` / `verify-ca` / `verify-full`; null → `prefer` |
| RootCertificatePath | string? | null | **new**; path on the engine machine, verifying modes only |
| RememberPassword | bool | false | **new**; opt-in per connection (FR-104) |
| LastConnectedAt | DateTimeOffset? | null | unchanged |

Never contains a password. IndexedDB values are schemaless: no DB version bump; the store's read
path fills defaults. Validation (web, before any IPC): Kind PostgreSql requires non-empty Server,
Login and a Port in 1..65535; SslMode must be one of the five values.

## 3. Session (engine, `SessionState`)

Existing fields plus:

| Field | Type | Notes |
| --- | --- | --- |
| Kind | DatabaseKind | from `ConnectionInfo.Kind` |
| ServerVersionText | string | PostgreSQL `server_version` (e.g. `18.1`) for the status bar |
| SearchPath | string[] | `current_schemas(false)` captured at connect and after a successful `SET search_path` statement |
| TransactionState | TransactionState | see §9 |

`SessionConnection` holds a `DbConnection` and the `Kind`; `RunExclusiveAsync` is unchanged in
shape. The web engine's `ServerCanonicalIdentity` for a PostgreSQL session is `pg://{host}:{port}`
lower-cased.

## 4. Remembered password (engine, `SqlCredentialStore`)

Reuses the existing entry shape `{ Server, Login, EncryptedPassword }` with `Server =
"pg://{host}:{port}"` (lower-cased host, explicit port). DPAPI CurrentUser scope with the app
entropy, atomic temp + rename, same file as SQL Server logins. Lifecycle: saved after a successful
open when `ConnectionInfo.RememberPassword` is true and a password was supplied; read when a
PostgreSQL connection string arrives without `Password`; removed by `CredentialRequest(Forget)`
or when the web deletes the saved connection (the web sends Forget first). Never returned to a
client; never logged.

## 5. PostgreSQL reference (library, embedded `postgresql-reference.json`)

```text
Reference
  version: "18"                       // documentation and parser major
  generatedFrom: { tags: ["REL_14_...", ..., "REL_18_..."], generator: "<tool version>" }
  entries: Entry[]
Entry
  kind: "command" | "keyword" | "function" | "aggregate" | "window" | "operator" | "type" | "sqlstate" | "lexical"
  name: string                        // canonical, lower-case for identifiers, upper-case for keywords
  aliases: string[]                   // e.g. int4 → integer
  category: string                    // keyword category (reserved, unreserved, col_name, type_func_name) or function section
  summary: string                     // one line (refpurpose / descr)
  synopsis: string?                   // commands: the synopsis text; operators: "left op right"
  signatures: Signature[]             // functions, aggregates, window functions
  minVersion: int                     // first supported major that has the entry (14..18)
  source: { file: string, sectionId: string }
  url: string                         // https://www.postgresql.org/docs/18/<sectionId>.html
Signature
  args: { name: string?, type: string, mode: "in" | "out" | "inout" | "variadic", hasDefault: bool }[]
  returns: string
  isSetReturning: bool
```

Loaded once per engine into dictionaries keyed by lower-cased name (functions, types, operators)
and upper-cased name (keywords, commands). `omissions.json` lists `{ name, kind, reason }` for
entries deliberately left out; SC-006 counts coverage as entries + omissions over the source set.

## 6. Schema snapshot (reuses `DatabaseCache`)

- `DbObjectType.MaterializedView = 8` appended (web tree header "Materialized Views").
- Objects: tables (`relkind` r, p, f), views (v), materialized views (m), sequences (S), functions
  (`prokind` f, a, w → ScalarFunction, or TableFunction when `proretset`), procedures (p).
- `Column`: `TypeName` = `format_type(atttypid, atttypmod)`; `MaxLength`/`Precision`/`Scale`
  derived from the typmod when meaningful; `IsIdentity` = `attidentity <> ''`; `DefaultValue` =
  `pg_get_expr(adbin, adrelid)`; `IsPrimaryKey` from `pg_index.indisprimary`; `Description` =
  `col_description`.
- `Parameter`: from `proargnames` / `proargtypes` / `proallargtypes` / `proargmodes`; `IsOutput`
  for modes `o`, `b`, `t`; `HasDefault` for the last `pronargdefaults` inputs.
- `Index`: name, `IsPrimaryKey`, `IsUnique`, columns with direction; `IndexType.Nonclustered` for
  all (no enum change).
- Cache extras: `SearchPath` (string[]), `HiddenSchemas` (system schemas present for resolution
  but hidden in the tree), `InaccessibleSchemas` (FR-046).
- Cache key and disk file name include the kind.

## 7. Execution outcome (reuses `ExecuteQueryResult`)

- One `ExecuteBatchDto` per statement; `ExecuteResultSet.BatchIndex` = statement index.
- `ExecuteMessageDto`: Info for command tags and notices (`Text` = `NOTICE: ...` / `WARNING: ...` /
  `UPDATE 12`), Error for `PostgresException` with `SqlState`, `Detail`, `Hint`, `Line`, `Column`
  (new keys 10–13; `Number` = 0; `Severity` = 16 for ERROR and above so the existing "error when
  ≥ 11" rule holds, 10 for notices).
- `TransactionState` (new key 13) after the run.
- Statuses: Ok, Error (stopped at statement N), TimedOut, Skipped (meta-command / COPY FROM
  STDIN), Disconnected, NoReply as today.

## 8. Grid edit (PostgreSQL)

`ApplyChangesRequest` unchanged in shape; `CrudCellDto.TypeName` (new key 6) carries the
`format_type` text for the `::type` cast. Outcome `CrudEditResult` per edit; all-or-nothing: on
any failure every result is `Ok = false`, the failing one carries the server's message and the
others carry "not applied: edit N failed".

## 9. Transaction state

```text
enum TransactionState { None = 0, Open = 1, Aborted = 2 }
```

Transitions (engine, per statement result):

| From | Statement / event | To |
| --- | --- | --- |
| None | BEGIN, START TRANSACTION (ok) | Open |
| Open | COMMIT, END, ROLLBACK, ABORT (ok) | None |
| Open | any error | Aborted |
| Aborted | ROLLBACK, ABORT (ok) | None |
| any | error with SQLSTATE 25P02 | Aborted |
| any | connection reset / reconnect | None |

The web keeps the last value from `ExecuteQueryResult.TransactionState` and from
`ChangeDatabaseResponse` (reconnect → None); connection switch / database switch while Open or
Aborted triggers the FR-009 prompt.

## 10. History entry (SQLite v4)

- `history.kind TEXT NOT NULL DEFAULT 'sqlserver'`, `query_sessions.kind` likewise; index
  `IX_history_server_db` rebuilt as `(kind, server, database_name, executed_at)`.
- `HistoryRecordRequest.Kind` (key 13) → column; `HistoryEntryDto.Kind` (key 18);
  `HistorySearchRequest.Kind` (key 15, nullable int).
- Dedup and session grouping queries add `kind` to their GROUP BY / match keys (FR-071).

## 11. Analysis rule (PostgreSQL)

`IPgAnalysisRule { RuleId, Name, Category (RuleCategory), DefaultSeverity, Description,
ReferenceUrl, RequiresSchema, Analyze(PgAnalysisContext) }` emitting `AnalysisDiagnostic`.
`AnalysisRuleInfoDto.Dialect` (key 9) = `postgresql` so the rules window groups by kind. Settings
and overrides use the same `codeAnalysis.ruleOverrides` map keyed by rule id; the ids never
collide with the SQL Server set.

## 12. Configuration

`config.json` → `"postgreSql": { "enabled": true }` (`PostgreSqlSettings` in `AppSettings`).
When false: capability omitted, PostgreSQL handlers not registered, web shows the choice disabled
with the reason.

## 13. Capability

`dialect.postgresql.v1` in `HandshakeResponse.EngineCapabilities`. The web checks it before
enabling the kind in the connection dialog and shows "requires engine ≥ <version>" otherwise.
