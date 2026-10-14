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

**SPIKE S2 result (2026-10-14, task T143, `PgTokenAnchorsSpikeTests`).** The parse tree carries a `location` (a start offset, the first
token of the node) on expressions and names only: `ColumnRef`, `ResTarget`, `A_Expr`, `FuncCall`, `RangeVar` and the like. The nodes that
hold the clauses - `SelectStmt`, `InsertStmt`, `UpdateStmt`, `DeleteStmt`, `JoinExpr` - have none, no node has an end, and nothing places a
clause keyword (`FROM`, `WHERE`, `GROUP BY`, `ON CONFLICT`) or an alias's `AS`. So the "AST-anchored" branch is not available for any
clause, and the plan's fallback applies to all of them.

**As built.**
- The structure is read off the **tokens**: the lexical scanner's tokens (comments attached to the token they lead or trail), bracket and
  `CASE ... END` pairs matched, clause keywords found at the top level of each statement. The parse tree is used where it is exact: the script
  must parse before anything is done, the result must parse to the same statements (`TreesEquivalent`), and the statement kinds decide
  nothing else.
- The layout is built as a **document** (`PgDoc`: text, soft breaks, groups that stay flat when they fit, indentation, alignment, scopes for
  "the start of the line the parenthesis was on"), then rendered. The printers decide what goes where from the SQL Prompt style; the renderer
  decides which groups fit. The text is a function of the tokens and the style alone, which is what makes it stable.
- One more check than planned: the formatted script must hold the same tokens (a word may differ in case) and the same comments, in order,
  as the original. A comment that ended a line before a comma is written after the comma, so comments are compared apart from tokens.
- Option semantics follow the T-SQL SQL Prompt layout option by option (clause alignment, lists, parentheses, CTE, join, CASE, AND / OR,
  BETWEEN, IN, INSERT, function calls). Statements it does not restructure (most utility and DDL statements) keep their own line breaks and
  indentation, get keyword case, and a statement written on one line is wrapped after its top-level commas when it is too long.
- `PgStyleApplicability` is `SqlPromptApplicability` in `AkmlSql.Formatting`, next to the catalog, because the browser's style editor
  reads it and cannot reference the engine-only PostgreSQL library. A test proves it both ways: an option listed as no effect changes
  nothing for any script of the corpus, and every other option changes the output of some script.

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

**SPIKE S6 result (2026-10-14, task T132, Npgsql with PostgreSQL 18, `PgColumnSchemaSpikeTests`).**

| Query | Without `KeyInfo` | With `KeyInfo` |
|---|---|---|
| `SELECT id, name FROM people` | `TableOID` set; base table, base schema, base column and `IsKey` empty | base table `people`, schema `public`, base column, `IsKey` true for the primary key only |
| `SELECT id AS person_id ...` | - | `BaseColumnName` is the real column (`id`) |
| `SELECT upper(name) AS shout, id + 1 ...` | - | no table, no base column, `TableOID` 0, `IsKey` null |
| a join of two tables | - | each column carries its own table, so one set names two |
| a view | - | the view is the base relation, `IsKey` false |
| a table with no primary key | - | the table is named, `IsKey` false everywhere |
| a composite primary key | - | `IsKey` true on each of its columns that is in the set |

Decisions that follow:
- **As built (T138):** the engine does not use `KeyInfo` at all. The row description already carries `TableOID` and the column's
  attribute number for every kind of relation, and the driver's own schema query skips partitioned and foreign tables (relkind `p`
  and `f`: `A_partitioned_table_is_editable` found a set with no base table and a zero OID there). So the names, the key, nullability,
  identity, generation and sequence defaults all come from one catalog read by `TableOID` once the reader is closed
  (`PgProvenanceCollector`), which also opens no second connection. Needs PostgreSQL 12 or later (generated columns).
- Provenance needs `CommandBehavior.KeyInfo` for the driver's names; without it only `TableOID` is known (kept for the record; the
  engine does not rely on it, see above).
- `IsIdentity` was false for a `serial` column; `IsAutoIncrement` was true. A generated column is "auto" if either is set.
- `IsKey` marks the key columns that are present, so a composite key with one column missing still shows one `IsKey`. A set is
  editable only when **every** primary key column of the table is present, so the engine reads the table's primary key
  (`pg_index.indisprimary` by `TableOID`) and compares; the keyed write would otherwise match several rows (the affected-row
  check would catch it, but the grid should say read-only up front).
- A set is read-only, with a reason, when it names more than one table, a view, a table with no primary key, an expression, or
  leaves out part of the primary key.

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

**SPIKE S7 result (2026-10-14, task T114) - partly confirmed, one assumption corrected, one check
left for an elevated shell.**

- *Confirmed*: on the development machine `AkmlSqlWebEngine` is installed with `StartName:
  LocalSystem` and the command line `AkmlSql.Engine.exe --web --config "C:\ProgramData\AKML SQL
  Web\config.json"`. DPAPI `CurrentUser` in that process is the SYSTEM profile, as stated above.
- *Corrected*: the sentence "it lands beside that config" was wrong. The `--config` file is the
  engine's bridge settings. `SqlCredentialStore` takes its folder from `Constants.ConfigFilePath`,
  which is the account's own `%AppData%\AKML SQL\config.json`, so for the service the file is
  `C:\Windows\System32\config\systemprofile\AppData\Roaming\AKML SQL\sql-credentials.json`, in the
  same folder as that account's `config.json`, and not in `C:\ProgramData\AKML SQL Web`.
- *Confirmed for an interactive account*: in the user's profile `sql-credentials.json` and
  `config.json` carry the same access list (SYSTEM, Administrators, the user; all inherited), so
  the store is no more open than the settings file beside it.
- *Not verified*: the access list of the SYSTEM profile's `AKML SQL` folder. It cannot be read from a
  non-elevated shell (access denied), and the check needs an elevated one:
  `icacls "C:\Windows\System32\config\systemprofile\AppData\Roaming\AKML SQL"`. The default for that
  profile is SYSTEM and Administrators only, which is what the consent text already says ("any
  administrator of the engine machine"), so the text stays as it is in `contracts/web-ui.md`.

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
`Kind` key. `SessionConnection` is generalised to hold a `DbConnection` plus a `Kind`; the
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

## Spike outcomes (recorded during implementation)

### S1 - pgsqlparser in a self-contained win-x64 publish (recorded 2026-10-14, T011)

**Outcome: loads and works; part of the original question (running as the web service account) is
deferred to deployment.** Evidence: a throwaway self-contained win-x64 console app (outside the
repository) parsed, scanned and split text; `libpg_query.dll` was copied next to the executable by
the package; `dumpbin /dependents` shows it imports only `KERNEL32.dll` (the C runtime is linked in),
so no VC++ redistributable is needed on a clean machine. `tests/AkmlSql.PostgreSql.Tests/Language/PgParserSmokeTests.cs`
pins the behaviour below (9 tests, all pass). Running the engine as `AkmlSqlWebEngine`
(LocalSystem) needs an elevated install; it is verified at the deployment task (T181), and there is
no reason to expect a difference because the DLL depends on nothing but the OS.

API facts the design now relies on (replacing guesses in R1, R2, R7):

| Area | Fact |
|---|---|
| Parse | `Parser.Parse(sql)` returns `Result<ParseResult>`; `ParseResult.Stmts[i]` is a `RawStmt` (`Stmt`, `StmtLocation`, `StmtLen`). |
| Error position | `Error.CursorPos` is **1-based, in Unicode code points** (not bytes, not UTF-16). `Error.GetCursorCharOffset(sql)` converts to a 0-based UTF-16 offset; `Error.GetLineAndColumn(sql)` gives line and column. This answers S3 for the parser. The server's `PostgresException.Position` follows the same rule (code points), so the engine converts it with the same helper (`PgPositions`), and S3's live check only confirms it. |
| Tree locations | Node `location` and `RawStmt.StmtLocation` are **UTF-8 byte offsets**; `Utf8OffsetMapper.ToCharOffset(int)` converts to UTF-16. |
| Scan | `Parser.Scan(sql)` returns `ScanResult.Tokens` (raw scanner tokens); `Parser.Tokenize(sql)` returns `SqlToken` (`Start`/`End` UTF-16, `Kind` in Keyword/Identifier/StringLiteral/NumericLiteral/Parameter/Operator/Punctuation/Comment/Other, `Token`, `KeywordKind`). Whitespace is not a token. |
| Split | `Parser.SplitWithScanner(sql)` returns `SplitResult.Statements` (`SplitStmt`: `Location`, `Length` in UTF-16, `Text`). `Text` is the **raw slice, leading white space included**, so a statement starts at the first non-white-space character after `Location`; error positions must be added to that, not to `Location`. Dollar quoting is respected. |
| Equivalence | `ParseTreeExtensions.EqualsIgnoringLocations(IMessage, IMessage)` compares two trees ignoring every location field. It replaces the hand-written location stripper planned in R2 for the formatter's meaning-preserved check (FR-141). |
| Classification | `Parser.Classify(sql)` returns `StatementInfo` (`Kind`: Select, Insert, Update, Delete, Merge, Truncate, Copy, Ddl, Transaction, Session, Cursor, Prepared, Explain, Call, Maintenance, Lock; `IsReadOnly`, `IsUtility`, `HasDataModifyingCte`, `HasLockingClause`). It drives the transaction tracker (R9) and several rules. |
| Tree walking | `ParseTreeExtensions.Walk`/`Descendants`/`GetLocation`/`GetText`, and `Deparse(IMessage)`, exist and are used by the rules and the formatter's token anchoring (S2). |
| PL/pgSQL | `Parser.ParsePlpgsql` is available for the dynamic-SQL rule (PGSE001). |

Consequence for tasks: T038 wraps these members (`PgParser`, `PgStatementSplitter`, `PgPositions`)
rather than re-implementing them; T022's `StripLocations`/`TreesEquivalent` test becomes a thin test
of `EqualsIgnoringLocations`.

### S3 - the server's error position counts characters (recorded 2026-10-14, T071)

**Outcome: confirmed.** `PostgresException.Position` is 1-based and counts Unicode code points, as the parser's
cursor position does. `PgExecuteRunnerTests.A_character_outside_the_basic_plane_before_the_error_still_puts_the_caret_in_the_right_place`
runs `SELECT 'a<U+1F642>' || nope;` on the second line: the server reports `nope` at character 16, the engine
converts it with `PgPositions.ToUtf16Offset` to editor column 17, and the test pins line 2, column 17. An error
without a position (for example `RAISE EXCEPTION`, SQLSTATE `P0001`) is placed at the start of its statement. The
engine sends each statement on its own, so the position is relative to the statement text and is shifted by the
statement's start in the script (`PgPositions.ToDocumentOffset`) before the line and column are read.

### S4 - a cancelled or timed-out statement keeps the physical connection (recorded 2026-10-14, T071)

**Outcome: the connection and its backend survive; no Disconnected path is needed for timeouts.** Probed against
PostgreSQL 18 with Npgsql 10.0.3 and pinned by `PgExecuteRunnerTests`:

| Event | What the driver does | Result in the engine |
|---|---|---|
| `CommandTimeout` expires | sends a cancel request, throws `NpgsqlException` with an inner `TimeoutException` | `TimedOut`; the backend process id before and after is the same; the next run works |
| user cancel (token) | sends a cancel request, throws `OperationCanceledException` | `Cancelled`; the next run works |
| server `statement_timeout` | `PostgresException` with SQLSTATE `57014` | an ordinary error |
| backend terminated (`pg_terminate_backend`) | `NpgsqlException` with an inner `IOException`; connection state Closed (Broken) | `Disconnected`; later statements Skipped; the transaction state resets to None |

The same cancel request that stops a statement inside a transaction aborts the transaction (SQLSTATE `57014`), so
the transaction tracker treats a timeout and a cancel as failures. No password or connection string appears in any
of the messages.

### S5 - `AllResultTypesAreUnknown` returns the server's text for every type (recorded 2026-10-14, T071)

**Outcome: confirmed, and the driver's own type names are good enough.** With the flag set on each command,
`reader.GetString` returns exactly what `psql` prints: `{1,2,3}` for an integer array, `1 day 02:00:00` for an
interval, `{"a": 1}` for json (spacing as the server writes it), `Þadbeef` for bytea, the label of an enum, and
`1.50` for `numeric(10,2)`. `GetDataTypeName` already carries the declared modifier (`character varying(5)`,
`numeric(10, 2)`); the engine removes the space after the comma to match `format_type` (`numeric(10,2)`), and a type
outside `pg_catalog` arrives schema-qualified (`public.mood`). That makes the column-schema call (a catalog query on a
cloned connection per result set) unnecessary for display; the editing story will fetch column provenance itself, once
per table, not per set.

Two driver facts used by the runner: `NpgsqlDataReader.Statements[0].StatementType` and `.Rows` give the counted
command tags (`SELECT n`, `INSERT 0 n`, `UPDATE n`, `DELETE n`, `MERGE n`, `COPY n`) while every definition or control
command reports `Other`, whose tag the engine builds from the statement's leading words (`PgCommandTag`); `Statements`
is marked obsolete in favour of the batch API, which has no `AllResultTypesAreUnknown`, so the property is used with
the warning suppressed in one place.

### Connection failures by cause (recorded 2026-10-14, T055)

Probed against the private server, a closed port, an unknown host and the local SQL Server on port 1433:

| Cause | Driver exception | Engine message |
|---|---|---|
| wrong password / unknown user | `PostgresException` 28P01 | server text + "Check the user name and password." |
| unknown database | `PostgresException` 3D000 | server text + hint to connect to `postgres` |
| password missing, server wants one | `NpgsqlException` "No password has been provided ..." | `dialect.password-required` |
| TLS verification fails | `NpgsqlException` "Exception while performing SSL handshake", inner `AuthenticationException` | TLS text + hint about the mode and root certificate |
| port closed / host unknown | `NpgsqlException` with inner `SocketException` | "Could not reach host:port ..." |
| port answers in another protocol (SQL Server on 1433) | `NpgsqlException` "Exception while reading from stream", inner `EndOfStreamException` | "The server at host:port did not answer as a PostgreSQL server. Check the database kind." |
| SQL Server driver against 5432 | `SqlException` 258 (a plain timeout; the driver gives no protocol signal) | reworded only because the port is 5432 (FR-008) |
