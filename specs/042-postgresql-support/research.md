# Research: PostgreSQL support in the engine and the web edition

**Branch**: `042-postgresql-support` | **Date**: 2026-10-14 | **Spec**: [spec.md](./spec.md)

Phase 0 output. Every decision below names its alternatives. Items marked **SPIKE** are claims the
fetched sources did not settle; each names the task that proves it before the design that depends
on it is trusted. Codebase facts come from [codebase-map.md](./codebase-map.md).

## R1. How the product understands PostgreSQL syntax (the load-bearing decision)

**Decision**: Use the real PostgreSQL parser through the `pgsqlparser` NuGet package (2.1.0, MIT),
a managed wrapper over libpg_query 18.1.0 (the PostgreSQL 18.6 parser). It runs in the engine
only. The wrapper gives: `Parser.Parse` (protobuf parse tree, `Stmts`), `Scan` / `Tokenize`
(tokens with keyword kinds), `SplitWithScanner` / `SplitWithParser` (statement boundaries that
respect dollar quoting), `Fingerprint`, `Normalize`, `Deparse`, PL/pgSQL parsing; `Result<T>` with
error message and cursor position; native binaries bundled for win-x64; trimming and AOT
compatible; dependency Google.Protobuf ≥ 3.36.2; targets net8/net9/net10.

**Rationale**: formatting, Parse (Ctrl+F5), analysis rules and the format safety check all need
an exact parse of PostgreSQL text. The server's own grammar is the only source that is complete for
PostgreSQL 18 and stays current (libpg_query tracks each major on its own branch). The engine is
already a self-contained win-x64 process that ships native code (SqlClient SNI), so a native
parser library is not a new kind of dependency. The web edition never needs it because PostgreSQL
mode is connected-only (clarification Q3): every PostgreSQL language request goes to the engine.

**Alternatives considered**:
- *Own P/Invoke binding to libpg_query* (build `pg_query.dll` with `Makefile.msvc`, generate C#
  from `pg_query.proto`): same capability, more code to own. Kept as the fallback if the package
  has a blocking defect; the API surface is identical, so a swap is contained to one adapter.
- *ANTLR grammars-v4 PostgreSQL grammar (C# target)*: managed, would run in the browser, but
  known to be slow on large inputs, lags the PostgreSQL release, and yields a grammar-shaped tree
  that makes layout and rules verbose. Rejected.
- *Own recursive-descent parser*: full control and error tolerance, but PostgreSQL's DDL surface
  is enormous; Parse would only catch what we implemented. Rejected for the parse; a small managed
  lexical scanner is still used where tolerance matters (R5).
- *`Parser.Format` from the wrapper*: a keyword-upper-casing deparse, not style-driven. Not used
  for FR-140.

**SPIKE S1** (first implementation task): in a published self-contained win-x64 engine, running
as the web service account, confirm the native library loads; `Parse` returns `Stmts`; `Scan` gives
start, end, token kind and keyword kind; `SplitWithScanner` gives statement offsets; a parse error
reports a 1-based cursor position.

## R2. Formatter design on top of R1

**Decision**: "AST drives structure, tokens drive emission". `PgFormatter` builds a layout tree by
walking the protobuf nodes and resolving each node's token span from the scan tokens (node
`location` is a start offset; the end is the last token before the next sibling's start). Comments
live only in the token stream and are re-attached to the nearest following token, as the T-SQL
pipeline does. The existing `SqlPromptStyle` option model is reused: an applicability table maps
each of the 38 catalogued options to "applies", "applies with PostgreSQL meaning" or "no effect
for PostgreSQL" (FR-145). PL/pgSQL and other procedural bodies inside dollar quotes are emitted
verbatim (FR-141, Out of Scope).

**Safety check (FR-141)**: parse the original and the formatted text, clear every `location`,
`stmt_location` and `stmt_len` field recursively (protobuf reflection over the message
descriptors), compare the trees structurally. Any difference returns the original text unchanged
with a message. `Fingerprint` is not used: it ignores constants and is too lossy.

**Idempotency (FR-142)**: format twice in the pipeline when the profile asks (as today).

**SPIKE S2**: prove the token-anchored tree on SELECT / INSERT ... ON CONFLICT / UPDATE ...
RETURNING / DELETE / WITH / window functions before writing layout rules. If node locations are
too sparse for a clause, fall back for that clause to a token-stream, clause-aware layout with the
AST used only for nesting depth. The plan keeps US7's layout tasks contingent on S2.

## R3. Analysis rules on top of R1

**Decision**: `PgAnalysisEngine` + `PgRuleRegistry` + `IPgAnalysisRule` in the new library,
discovering rules by reflection in their own assembly exactly as `RuleRegistry` does. The context
carries the protobuf tree, the scan tokens, the document text, the schema cache and the same
`ResolvedAnalysisSettings` and `SuppressionMap`; rules emit the existing `AnalysisDiagnostic`,
so `CodeAnalysisResponse`, `CodeIssueInfo`, the gutter, the Problems tab, severities, `.casettings`
overrides and the four disable scopes work unchanged. Suppression comments are collected from the
scan tokens' comment entries and fed to the existing `SuppressionMap` builder. Rule IDs are
`PG` + category code + number (`PGPE001`, `PGBP001`, `PGSE001`, `PGST001`, `PGDE001`, `PGDEP001`,
`PGEX001`, `PGNM001`), distinct from the SQL Server set by construction. The catalogue is in
[contracts/rules-catalog.md](./contracts/rules-catalog.md).

**Alternatives**: a shared rule interface over an abstract AST (rejected: no such AST exists and
inventing one is speculative generality); PostgreSQL rules inside `AkmlSql.Analysis` (rejected:
FR-093 wants PostgreSQL switchable as one unit and the T-SQL registry reflects over its own
assembly).

## R4. Completion, quick info and signature help

**Decision**: a `PgCompletionEngine` in the new library with its own `PgCursorContext` (built from
scan tokens: clause, dot chain, partial word, aliases, CTE names, in-string / in-comment /
in-dollar-body, join target) and providers implementing the neutral `ICompletionProvider`:
keywords and commands (reference + context), functions (reference + schema), types (reference),
schemas / objects / columns (`DatabaseCache`, search-path aware), aliases and CTEs, JOIN ON from
foreign keys (port of `FkHelpers` logic), PostgreSQL snippets. Output is the existing
`CompletionItem` / `CompletionResponse`. Quoting: double quotes only when the name is not a plain
lower-case identifier or is a reserved word. Quick info and signature help come from the reference
for built-ins and from the schema for user objects. The T-SQL `CursorContext` is not reused: it
carries `TSqlParserToken` and T-SQL-only notions (`@variables`, `#temp`, SQLCMD).

**Alternative**: reuse `CompletionEngine` with swapped providers (rejected: it is constructed with
`TsqlParserService` and T-SQL options such as `BracketMode`; its providers reference
`KeywordDictionary` and bracket quoting).

## R5. Managed lexical scanner (small, tolerant)

**Decision**: a pure C# `PgLexicalScanner` in the new library for the cases where the native
scanner's strictness or availability is wrong: psql meta-command detection, `COPY ... FROM STDIN`
pre-check, literal redaction for AI (R20), and a fallback statement splitter when `SplitWithScanner`
errors on in-progress text. It understands quoted identifiers, `'...'`, `E'...'`, `U&'...'`,
`$$`/`$tag$` quoting, `--` and nested `/* */` comments, numbers, operators, `::`, `$n`. It is not a
parser.

## R6. Npgsql

**Decision**: Npgsql 10.0.3 (NuGet; .NET 10; .NET 6 dropped). Engine-only. Facts used by the
design: `NpgsqlConnection.Notice` event (notices and warnings), `PostgreSqlVersion`,
`ChangeDatabase` (disconnect and reconnect), `NpgsqlDbColumn` with `BaseSchemaName`,
`BaseTableName`, `BaseColumnName`, `IsKey`, `IsIdentity`, `IsAutoIncrement`, `TableOID`,
`ColumnAttributeNumber`, `DataTypeName`, `PostgresType`; `PostgresException` with `SqlState`,
`MessageText`, `Detail`, `Hint`, `Position`, `Severity`; `SslMode` Disable / Allow / Prefer /
Require / VerifyCA / VerifyFull with `Root Certificate`. Npgsql exposes **no public transaction
status** (confirmed on the API page), see R9.

## R7. Execution model: one command per statement

**Decision**: split the script with `SplitWithScanner` (fallback R5), then run each statement as
its own `NpgsqlCommand` on the session's persistent connection, in order, stopping at the first
error. One `ExecuteBatchDto` per statement (Index, StartLine, offsets, Status, ElapsedMs,
ErrorCount), so the web's `ExecuteLineMapper` and the Messages tab work unchanged.

**Rationale (do not "optimise" this into a batch later)**: in the extended protocol PostgreSQL
runs every statement sent before one Sync inside an implicit transaction block; an error rolls
back all earlier statements in that batch. A multi-statement `NpgsqlCommand` or an `NpgsqlBatch`
therefore breaks FR-015 ("statements before the error stay applied"). psql behaves as the spec
wants because it sends one statement at a time.

**Error position**: `PostgresException.Position` is a 1-based index into the text sent, in
characters per the protocol documentation; document offset = statement start + Position − 1.
**SPIKE S3**: verify characters-not-bytes with a non-ASCII literal before the error.

**Command tag (FR-012)**: composed from the statement's verb (parse tree) and `RecordsAffected`
(`UPDATE 12`, `INSERT 0 1`, `CREATE TABLE`); the obsolete `Statements` API is not used.

**Notices (FR-013)**: collected from the `Notice` event per statement, mapped to
`ExecuteMessageDto` kind Info with the notice's severity text; never kind Error.

**GO (FR-010)**: not special; the server's syntax error is shown with the hint text appended by
the engine when the failing statement starts with `GO`.

**psql meta-commands and `COPY ... FROM STDIN` (FR-019)**: detected before sending (R5 for `\`
lines; the parse tree's `CopyStmt` with `is_from` and no filename); reported as status Skipped
with the hint.

## R8. Query timeout and cancellation

**Decision**: `NpgsqlCommand.CommandTimeout` = the session timeout; Npgsql sends a cancel request
on expiry and throws; mapped to `ExecuteStatus.TimedOut`; the session stays usable when the server
acknowledges the cancel. Stop mid-run stays transport-bound as for SQL Server (serial bridge).
**SPIKE S4**: confirm whether Npgsql closes the physical connection when the cancel is not
acknowledged within its cancellation timeout; if so, that case flows through the existing
`Disconnected` path and the session reconnects on the next run.

## R9. Transaction state (FR-016)

**Decision**: client-side tracking, an explicit approximation. The parse tree's `TransactionStmt`
kinds (BEGIN / START TRANSACTION → Open; COMMIT / END / ROLLBACK / ABORT → None; SAVEPOINT and
PREPARE TRANSACTION keep Open) update the session's state after each statement; an error while
Open → Aborted; any error with SQLSTATE `25P02` (in_failed_sql_transaction) → Aborted regardless
(self-healing); a new successful statement while Aborted is impossible, so the state only leaves
Aborted through ROLLBACK. The state is returned in `ExecuteQueryResult.TransactionState` and shown
in the status bar; "Rollback" sends `ROLLBACK` through the normal execute path (no new message).

**Alternatives**: Npgsql internals (`Connector.TransactionStatus`, not public); probing the server
(`pg_current_xact_id_if_assigned()` is null for read-only transactions, not reliable). Rejected.

## R10. Value display and type fidelity (FR-018)

**Decision**: run script statements with `NpgsqlCommand.AllResultTypesAreUnknown = true`, so the
server sends every column in its text output format (exactly what psql prints: arrays, ranges,
intervals, json, composites, bytea `\x...`), and read columns as strings; NULL stays NULL. Column
type names come from `GetColumnSchema()` (`DataTypeName` / `PostgresType.DisplayName`) and travel
in the existing `ExecuteResultSet.ColumnSqlTypes`. Edits are sent back as text parameters with an
explicit `::type` cast, so display and edit round-trip through the server's own I/O functions and
no client type map is needed. **SPIKE S5**: confirm `AllResultTypesAreUnknown` yields text for
every column including user-defined types, and that `GetColumnSchema()` still works with it.

## R11. Editable results and key detection (FR-130 to FR-134)

**Decision**: a result set is editable when `GetColumnSchema()` reports one `BaseTableName` /
`BaseSchemaName` for every non-expression column and the table's primary key columns are all
present (`IsKey` from the primary index; verified against `pg_index.indisprimary` through
`TableOID` when `IsKey` is ambiguous). `PgCrudWriteGenerator` emits `UPDATE "s"."t" SET "c" =
$1::type WHERE "k" = $2::type`, `INSERT ... RETURNING` (identity for `NewIdentity`), `DELETE`
by key; all edits of one Apply run in one transaction; any failure or an affected-row count ≠ 1
rolls back everything and names the row (FR-131). `CrudCellDto` carries the type name (new key 6)
for the casts. **SPIKE S6**: confirm when Npgsql populates `BaseTableName` / `IsKey` (with or
without `CommandBehavior.KeyInfo`) before freezing `IsEditable`.

## R12. Schema metadata

**Decision**: introduce `ISchemaMetadataProvider` with the four operations the engine uses
(Phase A, Phase B, list databases, change checksum) and make the existing `SchemaMetadataService`
implement it unchanged; `PgSchemaMetadataService` implements it with `pg_catalog` queries
([contracts/catalog-queries.md](./contracts/catalog-queries.md)). The `DatabaseCache` /
`DatabaseObject` model is reused: `DbObjectType.MaterializedView = 8` is appended; functions map
to ScalarFunction or TableFunction (`proretset`), procedures to Procedure (`prokind = 'p'`),
sequences to Sequence; `Column.TypeName` is `format_type()` output; `IsIdentity` from
`attidentity`; defaults from `pg_attrdef`; PK from `pg_index.indisprimary`; comments from
`pg_description`; row estimates from `pg_class.reltuples`. The session's search path
(`current_schemas(false)`) is stored on the cache for R4. System schemas (`pg_catalog`,
`information_schema`, `pg_toast`) are loaded only for name resolution and hidden in the tree
behind the toggle (FR-040).

**Change detection**: `md5(string_agg(oid || relname || relkind || relnamespace, ',' ORDER BY
oid))` over `pg_class` plus the same over `pg_proc` (user schemas only); compared on the existing
periodic timer; the DDL regex trigger stays. Event triggers rejected (superuser, server objects).

**Cache identity**: `ServerCanonicalIdentity` for PostgreSQL is `pg://{host}:{port}` lower-cased
(no superuser-only `pg_control_system()`); cache keys and disk persistence include the kind so a
SQL Server and a PostgreSQL database with the same names never share a cache (FR-044).

## R13. Databases and database switch

**Decision**: list with `pg_database WHERE datallowconn AND NOT datistemplate AND
has_database_privilege(datname, 'CONNECT')`; switch with `NpgsqlConnection.ChangeDatabase`
(a reconnect by definition). The web shows the FR-031 warning before switching and refuses while
the tracked transaction state is Open or Aborted until the user confirms rollback (FR-009).

## R14. Connections, target guard and credentials

**Decision**: the web builds an Npgsql connection string (`Host`, `Port`, `Database`, `Username`,
`Password`, `SSL Mode`, `Root Certificate`, `Application Name=AKML SQL`) with the provider-agnostic
`DbConnectionStringBuilder` it already uses; `ConnectionInfo` gains `Kind` (key 5) and
`RememberPassword` (key 6). `PgTargetGuard` replaces `BridgeSqlTargetGuard` for PostgreSQL
sessions arriving over the bridge: `Username` required, `Integrated Security` / Kerberos rejected
(FR-102); `SqlAliasResolution` is skipped. Remembered passwords reuse `SqlCredentialStore` with
the server key `pg://{host}:{port}` and the login as the second key (one store, one DPAPI
wrapper, one atomic writer); a new `CredentialRequest` (Has / Forget) lets the web skip the prompt
and forget (contracts/ipc.md). When the password is absent and the store has one, the engine
injects it before opening; when `RememberPassword` is true and a password is present, the engine
saves it after a successful open.

**Where the password really lives (honest statement for the consent text)**: the web engine
service is created by `sc.exe create AkmlSqlWebEngine` with no `obj=`, so it runs as LocalSystem;
DPAPI `CurrentUser` there is the SYSTEM profile, decryptable by any local administrator of that
machine. The SSMS-started engine is a different process under the user's account with its own
store. The store file follows `Constants.ConfigFilePath`, so under `--web --config
{commonappdata}\AKML SQL Web\config.json` it lands beside that config. The consent text says:
"stored on the engine machine for the account the engine runs as; any client paired with this
engine can use it". **SPIKE S7**: confirm the resolved path under the service and that the file
ACL matches the config's.

## R15. Capability, configuration and switch-off (FR-006, FR-093)

**Decision**: capability `dialect.postgresql.v1` in `Capabilities.Current`, omitted when
`config.json` has `"postgreSql": { "enabled": false }` (new `PostgreSqlSettings` section, default
true). With the flag off the engine does not register PostgreSQL handlers and the web disables the
PostgreSQL choice with the reason. Documented in `doc/configuration.md`.

## R16. PostgreSQL version policy

**Decision**: supported servers are the community-supported majors at planning time: 14 (EOL
2026-11-12), 15, 16, 17, 18. The reference and the parser are pinned to **18** (libpg_query
18.1.0 = PostgreSQL 18.6). PostgreSQL 19 is in release-candidate phase (beta 4 said GA "may occur
in October" 2026); it connects as a newer server and is a follow-up for the reference and the
parser branch. Servers below 14 connect with a warning; version-gated entries are withheld
(FR-052). The spec's "newest community-supported release at implementation time" is read as "at
planning time", recorded here.

## R17. The PostgreSQL reference and its generator (FR-050 to FR-054)

**Decision**: a console tool `tools/pg-reference/AkmlSql.PostgreSql.ReferenceGen` (not part of the
product build) reads a checked-out PostgreSQL source tree at the five supported tags and writes:
`src/AkmlSql.PostgreSql/Reference/postgresql-reference.json` (embedded resource),
`doc/postgresql-reference.md` (human-readable, reviewable) and
`src/AkmlSql.PostgreSql/Reference/omissions.json`. Inputs and per-entry source sections:

| Entry kind | Source | Doc link |
| --- | --- | --- |
| Keywords and categories (reserved, unreserved, column-name, type-function-name) | `src/include/parser/kwlist.h` | `sql-keywords-appendix` |
| SQL commands: name, purpose, synopsis | `doc/src/sgml/ref/*.sgml` (`refname`, `refpurpose`, `synopsis`) | the ref page id (`sql-select`, `sql-insert`, ...) |
| Built-in functions: name, argument types, return type, kind, one-line description | `src/include/catalog/pg_proc.dat` (+ `pg_type.dat` for OID → name) and the function tables in `doc/src/sgml/func/*.sgml` for the section id | `functions-<section>` |
| Operators | `src/include/catalog/pg_operator.dat` | `functions-<section>` |
| Data types and aliases | `src/include/catalog/pg_type.dat` + `doc/src/sgml/datatype.sgml` | `datatype-<section>` |
| SQLSTATE names | `src/backend/utils/errcodes.txt` | `errcodes-appendix` |
| Lexical rules (quick-info text for `::`, `$n`, dollar quoting, `E''`) | `doc/src/sgml/syntax.sgml` section ids, hand-curated list | `sql-syntax-lexical` |

`minVersion` is computed by diffing the entries across the five tags. Every entry records
`source` (file + section id) and `url` (`https://www.postgresql.org/docs/18/<id>.html`). The
generator is deterministic; its output is checked in and reviewed; `omissions.json` lists what is
left out on purpose (internal functions, deprecated aliases) so SC-006 can be measured.

**Licensing**: descriptions lifted from the `.dat` files and the DocBook sources are under the
PostgreSQL License; libpg_query is BSD-3 with vendored upb (BSD-3), utf8_range (MIT), xxHash
(BSD-2); Npgsql is PostgreSQL-licensed. The installer ships `LICENSE.txt` only; there is no
third-party notices file today. Task: add `THIRD-PARTY-NOTICES.txt` to the installer payload and
`doc/`.

## R18. Where PostgreSQL work runs for the web edition

**Decision**: everything through the engine. Because PostgreSQL mode is connected-only, an engine
is always paired when the kind is PostgreSQL; the web routes format, Parse, code analysis,
completion, quick info, signature help, wildcard expansion, safety check and AI redaction to the
engine's existing message types (which the SSMS edition already uses) and the engine dispatches by
the session's kind (R19). The in-browser T-SQL services (`IFormatterService`, `IAnalyserService`,
`OfflineSqlScanner` behind quick info and signature help, `IRefactoringService`, the in-browser
`SplitBatches` in `IQueryExecutionService`, built-in snippets, `AiChatPanel`'s prompt,
`LiteralRedactor`) are kind-gated so they never see PostgreSQL text. The browser bundle gains no
native code and no reference data. This also satisfies FR-120: a pgAdmin plugin would speak the
same messages.

## R19. Engine dispatch by kind

**Decision**: `SessionState.Kind` is set from `ConnectionInfo.Kind` by `ConnectionChangedHandler`.
Handlers that already take a `SessionId` (Format, FormatAction, FormatPreview, CodeAnalysis,
Completion, QuickInfo, Signature, WildcardExpansion, StatementBoundary, GetObjectDefinition,
ScriptAs, ExecuteQuery, ExecuteParse, ApplyChanges, ChangeDatabase, ListDatabases with session)
branch on the session's kind at the top and call the PostgreSQL implementation; the SQL Server
path is byte-for-byte the existing code. `SafetyCheckRequest` has no session, so it gains a
`Dialect` key. `SessionConnection` is generalised to hold a `DbConnection` plus a `Kind`; the
SQL Server handlers cast as before. Surfaces without a PostgreSQL implementation (refactorings,
Find invalid objects) return the "Not available for PostgreSQL" error for a PostgreSQL session,
and the web disables their entries by kind (FR-092).

## R20. AI (FR-080 to FR-082)

**Decision**: `PromptTemplates.BuildSystemPrompt` and the seven builders take a `SqlDialect`
(default SqlServer, text unchanged); the web passes the active kind; `AiChatPanel.razor`'s
hardcoded "SQL Server expert" string becomes dialect-aware; fix prompts carry SQLSTATE, detail and
hint. Privacy: for PostgreSQL text the web asks the engine to redact (new `RedactRequest`, R5
scanner); if the engine is unreachable or the redaction fails, the request is not sent and the
user sees the existing consent choice (FR-081).

## R21. History (FR-070 to FR-072)

**Decision**: SQLite schema v4 adds `history.kind TEXT NOT NULL DEFAULT 'sqlserver'` and
`query_sessions.kind` the same way (the existing `AddColumnIfMissingAsync` idiom); grouping keys
and the `IX_history_server_db` index include it; `HistoryRecordRequest` key 13, `HistoryEntryDto`
key 18, `HistorySearchRequest` key 15; search syntax gains `kind:postgresql` / `kind:sqlserver`
in the web page's search builder; the History page shows a kind badge and a filter control.
The store is owned by the one engine binary, so SSMS reads it unchanged (FR-072).

## R22. Web connection model and persistence

**Decision**: `SavedSqlConnection` gains `Kind` (default SqlServer), `Port`, `SslMode`,
`RememberPassword`; IndexedDB values are schemaless, so no database version bump: missing fields
default on read. `ConnectionManagerModal` gets a kind switch with the PostgreSQL field set;
`ConnectionPickerComponent`, `StatusBar`, Settings → Connections and the command palette show the
kind with a label and an icon (`data-testid="status-kind"`). Existing labels and test ids for SQL
Server stay unchanged (FR-091).

**FR-110 amendment**: the settings export envelope is built from preference stores only and
never includes saved connections (spec 041, by construction). Saved connections therefore do not
gain export/import behaviour; the kind travels with the saved-connection record. FR-110 is reworded
in the spec to say so; the "import without a kind → SQL Server" rule still applies to records
restored from older browser storage. Reported to the user in the plan summary.

## R23. Highlighting

**Decision**: the vendored CodeMirror bundle already exports `PostgreSQL`, `MSSQL`, `StandardSQL`
and `SQLDialect` under `langSql`; `akml-editor.js` wraps the language in a `Compartment` and
`setDialect("postgresql" | "sqlserver")` reconfigures it. SQL Server keeps `StandardSQL`, what it
has today, so highlighting for SQL Server users does not change (FR-091).

## R24. Tests, corpora and the live server

**Decision**: new xunit project `tests/AkmlSql.PostgreSql.Tests` (constitution III) holding the
PostgreSQL completion corpus gate (`tests/pg-completion-corpus/*.json`, same case format as the
T-SQL corpus, fake cache `NorthwindPgTestCacheFactory` with lower-case Northwind names in
`public` plus a `sales` schema), the format corpus (`tests/pg-format-corpus/corpus/*.sql` →
`golden/<name>__<style>.sql`, parse-equivalence and idempotency asserted for every file), and one
positive + one negative test per rule. Engine handler tests mock the connection; live tests use
`WithPgOrSkipAsync` (mirrors `WithSqlOrSkipAsync`) against `localhost:5432` with a Northwind port
(`northwind_psql`). Browser tests (`AkmlSql.Web.E2E.Tests`) add a `PostgresWorkspaceTests` class
that skips without the server. The existing suites are the regression gate for SQL Server
(FR-091, SC-003).

## R25. Project layout and isolation (FR-093, constitution V)

**Decision**: one new library `src/AkmlSql.PostgreSql` (net10.0; packages `pgsqlparser`,
Google.Protobuf; references Core, IntelliSense, Analysis, Formatting) holding Language, Reference,
Completion, Formatting, Analysis, Snippets, Safety; the data path (Npgsql: session connection,
schema metadata, execution, CRUD, credentials, target guard, databases) lives in
`src/AkmlSql.Engine/PostgreSql/` so the engine stays the only process that opens connections.
Everything PostgreSQL-specific is reachable only through the kind dispatch (R19) and the capability
flag (R15). A second data library was rejected as a parallel mechanism.

## R26. Performance budgets

- Phase A for 1,000 objects: one `pg_class` + `pg_namespace` query, < 500 ms local (SC-008
  names appear within 2 s end to end).
- Parse of a 2,000-line script with libpg_query: single-digit milliseconds; the formatter budget is
  the existing one (format a 2,000-line script under 500 ms).
- Completion: scan + context + lookup under 50 ms for a 10 MB document is not required; the
  existing 10 MB document limit applies and the scanner is linear.
- Switching connection: no page reload; dialect-aware surfaces update within 3 s (SC-002) driven
  by the Phase A completion event.

## R27. Open spikes summary

| Spike | Proves | Blocks |
| --- | --- | --- |
| S1 | pgsqlparser loads and its API shape under the published engine and the service account | everything in the new library |
| S2 | token-anchored layout tree is buildable for the core DML subset | US7 layout rules, US8 rules that need spans |
| S3 | `Position` is in characters | FR-014 offset mapping |
| S4 | Npgsql behaviour when a cancel is not acknowledged | FR-017 wording and the Disconnected path |
| S5 | `AllResultTypesAreUnknown` gives text for every type | R10 display and edit round-trip |
| S6 | when `BaseTableName` / `IsKey` are populated | FR-130 editability |
| S7 | credential store path and ACL under the service account | FR-104 consent text |
