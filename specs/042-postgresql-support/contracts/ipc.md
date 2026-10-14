# Contract: engine IPC changes for PostgreSQL

All existing `[MessagePackObject]` types evolve append-only (new keys after the last existing
key, non-null defaults). Message type numbers continue from 221. The SQL Server paths read none of
the new keys, so an older client and a newer engine (or the reverse) keep working.

## Capability and configuration

| Item | Value |
| --- | --- |
| Capability string | `dialect.postgresql.v1` (added to `Capabilities.Current`; omitted when `postgreSql.enabled` is false) |
| Config section | `"postgreSql": { "enabled": true }` in `config.json` (`PostgreSqlSettings`) |
| Error code for unsupported surfaces | `ErrorInfo.Code = "dialect.unsupported"`, message `Not available for PostgreSQL` |

## Existing messages: appended keys

| Message | New key | Type | Default | Meaning |
| --- | --- | --- | --- | --- |
| `ConnectionInfo` (ConnectionChanged) | 5 `Kind` | int | 0 | `DatabaseKind` |
| `ConnectionInfo` | 6 `RememberPassword` | bool | false | save the supplied password after a successful open (FR-104) |
| `TestSqlConnectionRequest` | 1 `Kind` | int | 0 | which provider to test with |
| `TestSqlConnectionResponse` | 2 `ServerVersion` | string? | null | e.g. `PostgreSQL 18.1` / SQL Server product version (FR-002) |
| `ListDatabasesRequest` | 2 `Kind` | int | 0 | used only when `SessionId` is absent |
| `ExecuteMessageDto` | 10 `SqlState` | string? | null | PostgreSQL SQLSTATE (`42P01`) |
| `ExecuteMessageDto` | 11 `Detail` | string? | null | `PostgresException.Detail` / notice detail |
| `ExecuteMessageDto` | 12 `Hint` | string? | null | server hint, or the engine's own hint (`GO` is SQL Server-only) |
| `ExecuteMessageDto` | 13 `Column` | int | 0 | 1-based column of the error position in the document (0 = unknown) |
| `ExecuteQueryResult` | 13 `TransactionState` | int | 0 | `TransactionState` after the run (0 None, 1 Open, 2 Aborted) |
| `ChangeDatabaseResponse` | 4 `TransactionState` | int | 0 | always 0 after a PostgreSQL reconnect |
| `CrudCellDto` | 6 `TypeName` | string? | null | `format_type` text used for the `::type` cast (PostgreSQL only) |
| `AnalysisRuleInfoDto` | 9 `Dialect` | string | "sqlserver" | `sqlserver` / `postgresql` |
| `SafetyCheckRequest` | 3 `Dialect` | string | "sqlserver" | no session on this request |
| `HistoryRecordRequest` | 13 `Kind` | int | 0 | stored in `history.kind` |
| `HistoryEntryDto` | 18 `Kind` | int | 0 | |
| `HistorySearchRequest` | 15 `Kind` | int? | null | filter; null = both kinds |
| `HandshakeResponse` | none | | | the capability list carries the feature |

`ExecuteMessageDto.Kind`, `Severity`, `Line` keep their meaning: notices are Info with
`Severity = 10` and `Text` prefixed `NOTICE:` / `WARNING:` / `INFO:`; errors are Error with
`Severity = 16` so `ExecuteOutcome`'s "error when severity ≥ 11" rule holds; `Number` is 0 for
PostgreSQL (the SQLSTATE is the identifier).

`ExecuteBatchDto` is reused as **one record per statement** for PostgreSQL (Index = statement
ordinal, StartLine / StartOffset / EndOffset = the statement's span in the document, Status,
ElapsedMs, ErrorCount). `ExecuteResultSet.BatchIndex` = statement ordinal.

`ExecuteParse` (219 → 220) for a PostgreSQL session parses each statement with the product's
parser and never contacts the server; errors arrive as `ExecuteMessageDto` with `Line`,
`Column`, `SqlState = "42601"`, `Hint = "Object names and permissions were not checked."`.

## Session-scoped requests: dispatch by kind, no new keys

`Format`, `FormatAction`, `FormatPreview`, `FormatSelection`, `CodeAnalysis`, `Completion`,
`QuickInfo`, `Signature`, `WildcardExpansion`, `StatementBoundary`, `GetObjectDefinition`,
`ScriptAs`, `ExecuteQuery`, `ExecuteParse`, `ApplyChanges`, `ChangeDatabase`,
`ListDatabases` (with `SessionId`), `DocumentOutline`, `SnippetExpand`: the engine reads
`SessionState.Kind` and routes to the PostgreSQL implementation. For `RefactorPreview`,
`RefactorApply`, `FindInvalidObjects`, `FindReferences`, `FindUnusedVariables`,
`CrudGeneration`, `EncryptedObjectDecryption` on a PostgreSQL session the engine replies with the
`dialect.unsupported` error.

## New messages

### 221 `CredentialRequest` → 222 `CredentialResponse`

```text
CredentialRequest
  [0] Action   int     0 = Has, 1 = Forget
  [1] Kind     int     DatabaseKind (only PostgreSql is served today)
  [2] Server   string  host (lower-cased)
  [3] Port     int     0 → 5432
  [4] Login    string  user name
CredentialResponse
  [0] Ok       bool
  [1] Has      bool    Action Has: a remembered password exists; Action Forget: it existed
  [2] Error    string?
```

Never carries a password. Store key: `pg://{server}:{port}` + login in `SqlCredentialStore`.

### 223 `RedactRequest` → 224 `RedactResponse`

```text
RedactRequest
  [0] Dialect  string  "postgresql" (SQL Server redaction stays in-process in the client)
  [1] Text     string
RedactResponse
  [0] Ok       bool    false when the text could not be scanned (the client must not send the original)
  [1] Text     string  literals replaced by placeholders, structure kept
  [2] Error    string?
```

## Connection strings (web → engine)

PostgreSQL `ConnectionInfo.ConnectionString` is an Npgsql string built with
`DbConnectionStringBuilder`:
`Host=<host>;Port=<port>;Database=<db>;Username=<user>;Password=<pwd>;SSL Mode=<Disable|Prefer|Require|VerifyCA|VerifyFull>;Root Certificate=<path>;Application Name=AKML SQL;Timeout=15;Command Timeout=0`.
`Password` is omitted when the user did not type one and `RememberPassword` was on for that saved
connection; the engine then injects the remembered one (or fails the open with
`dialect.password-required`, which the web turns into the prompt).

## Engine-side guard for bridge clients

`PgTargetGuard.Check(connectionString)` on `ConnectionChanged`, `TestSqlConnection` and
`ListDatabases` from the WebSocket bridge: rejects `Integrated Security=true`, `Kerberos Service
Name`, `GSS Encryption Mode=Require`, and a missing `Username`; the named-pipe path (SSMS) is
exempt as today. Error code `dialect.credentials-required`.
