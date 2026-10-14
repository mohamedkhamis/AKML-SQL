---
description: "Task list for 042-postgresql-support"
---

# Tasks: PostgreSQL support in the engine and the web edition

**Input**: Design documents from `/specs/042-postgresql-support/`
**Prerequisites**: [plan.md](./plan.md), [spec.md](./spec.md), [research.md](./research.md), [data-model.md](./data-model.md), [contracts/ipc.md](./contracts/ipc.md), [contracts/reference-schema.md](./contracts/reference-schema.md), [contracts/rules-catalog.md](./contracts/rules-catalog.md), [contracts/catalog-queries.md](./contracts/catalog-queries.md), [contracts/web-ui.md](./contracts/web-ui.md), [quickstart.md](./quickstart.md), [codebase-map.md](./codebase-map.md)

**Tests are not optional here.** Constitution III and FR-091 / SC-003 require every new behaviour
to land with automated checks and the existing gates to stay identical. In every story, write the
test tasks first, see them fail, then implement.

**Spikes are measurement tasks, not TDD.** S1–S7 (research.md R27) sit before the design that
depends on them. A spike ends by recording its outcome and the chosen branch in research.md; it
is done when the record exists, not when something "passes".

**Organization**: one phase per user story, in spec priority order (US1–US3 are P1, US4–US7 are
P2, US8 is P3). Phase 2 holds the shared pieces every story needs: appended IPC keys, the kind
on the session, the generalised session connection, the catalog seam, the dispatch-by-kind
scaffold, the web dialect gate, the history migration and the language layer of the new library.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: can run in parallel. The task touches different files and does not depend on an unfinished task.
- **[Story]**: `[US1]`…`[US8]`, mapping to the user stories in spec.md.
- `R12`, `S3` refer to the decisions and spikes in research.md. Read the referenced entry before
  starting a task; it names the alternatives rejected and the traps.

## Rules for every task

- Repository root: `D:\Repo\01-Khamis-Projects\AKML-SQL`. Sources under `src/`, tests under
  `tests/`, the reference generator under `tools/pg-reference/`.
- **New library**: `src/AkmlSql.PostgreSql` (net10.0; `pgsqlparser` + Google.Protobuf; engine-only).
  **Data path**: `src/AkmlSql.Engine/PostgreSql/` (Npgsql). Nothing PostgreSQL-specific goes into
  `AkmlSql.Analysis`, `AkmlSql.Formatting` or `AkmlSql.IntelliSense`, which the browser bundle
  references, and nothing goes into the shell projects.
- **Core stays netstandard2.0-safe**: `DatabaseKind`, `TransactionState`, `PgIdentifierQuoting`
  and every message DTO use plain enums and `{ get; set; }`; no records, no `init`, no LINQ
  `Contains` on strings.
- **MessagePack DTOs**: append `[Key(n)]` exactly as `contracts/ipc.md` numbers them. Never
  renumber. New request types (221–224) are sent only when `dialect.postgresql.v1` is advertised.
- **IndexedDB records are schemaless**: new saved-connection fields default on read; no DB
  version bump.
- **No new theme colour**: the kind is shown by label and icon. `scripts/generate-theme-css.ps1
  -CheckOnly` and `WebCssTokenTests` must stay green.
- **Test ids** only from `contracts/web-ui.md`; existing SQL Server ids and labels never change.
- **No browser pop-ups**: use `IDialogService` / `INotificationService`.
- **Browser tests need Debug builds** of `src/AkmlSql.Web` and `src/AkmlSql.Engine`
  (`WebAppFixture` runs `dotnet run --no-build -c Debug`); run `tests/AkmlSql.Web.E2E.Tests`
  serially (`-- xUnit.ParallelizeTestCollections=false`); rebuilding the engine while a sandbox
  engine runs locks the DLLs.
- **Live PostgreSQL tests** read `AKML_PG_TEST_CONNECTION` and skip when it is unset or the
  server is unreachable (`WithPgOrSkipAsync`, T012). A local PostgreSQL 18 with the Northwind
  port from quickstart.md §0 is the expected target.
- **Test categories**: tag every new test class with `[Trait("Category", "<name>")]` using the
  names quickstart.md §1 filters on: `PostgreSql` (everything under
  `tests/AkmlSql.Engine.Tests/PostgreSql/` and the `PostgresWorkspaceTests` class), `Isolation`,
  `CompletionCorpus`, `Reference`, `FormatCorpus`, `Rules`, `Parse`.
- **Engine redeploy is a full publish** (CLAUDE.md); the installer already ships `publish\*`
  recursively, so the native parser library needs no installer change beyond S1's check.
- **One command per statement** for PostgreSQL scripts (R7). Do not batch: the extended protocol
  wraps a batch in one implicit transaction and an error rolls back the earlier statements.
- **Git**: deliver changes uncommitted and ask "Ready to commit?" (constitution IV). Never
  commit, push or amend on your own.

---

## Phase 1: Setup (projects, packages, baseline)

**Purpose**: the new projects exist, the packages resolve, the pre-feature baseline is captured,
and spike S1 proves the native parser loads in the published engine.

- [X] T001 Capture the pre-feature baseline before any `src/` change: run `dotnet test --logger trx` for `tests/AkmlSql.Core.Tests`, `tests/AkmlSql.IntelliSense.Tests`, `tests/AkmlSql.Analysis.Tests`, `tests/AkmlSql.Formatting.Tests`, `tests/AkmlSql.Engine.Tests`, `tests/AkmlSql.AI.Tests`, `tests/AkmlSql.Web.Tests` and `tests/AkmlSql.Shell.Shared.Tests`, copy the `.trx` files plus the completion-corpus and format-parity summaries into `specs/042-postgresql-support/baseline/pre-feature/`, and write the commit hash and commands into `specs/042-postgresql-support/baseline/README.md` (SC-003 compares against this with `scripts/compare-test-results.ps1 -Before … -After …`). *(Done. The .trx files are kept outside the repository in `%LOCALAPPDATA%\AKML SQL\spec042-tests\before` (about 14 MB); the pass-lists and `baseline/README.md` (counts, the four known reds, how to compare) are in the spec folder. Known reds at the baseline: Core 1, Web 66, Shell.Shared 1, Engine 1.)*
- [X] T002 Create `src/AkmlSql.PostgreSql/AkmlSql.PostgreSql.csproj` (net10.0, `Nullable` enable, `LangVersion` latest; `PackageReference` `pgsqlparser` 2.1.0 and `Google.Protobuf` 3.36.2; `ProjectReference` `../AkmlSql.Core`, `../AkmlSql.IntelliSense`, `../AkmlSql.Analysis`, `../AkmlSql.Formatting`; `EmbeddedResource Include="Reference\*.json;Snippets\*.akmlsnippet"`) with the folders `Language/`, `Reference/`, `Completion/Providers/`, `Formatting/`, `Analysis/Rules/`, `Snippets/`, `Safety/`, and register it in `AKML-SQL.slnx` under `/src/`.
- [X] T003 Create `tests/AkmlSql.PostgreSql.Tests/AkmlSql.PostgreSql.Tests.csproj` (xunit 2.x, `Microsoft.NET.Test.Sdk` 18.x, `xunit.runner.visualstudio`; `ProjectReference` to `src/AkmlSql.PostgreSql` and `tools/pg-reference/AkmlSql.PostgreSql.ReferenceGen`; `Content` links copying `../pg-completion-corpus/**` and `../pg-format-corpus/**` to the output) with folders `Language/`, `Reference/`, `Completion/`, `Formatting/`, `Rules/`, `Isolation/`, and register it in `AKML-SQL.slnx` under `/tests/`.
- [X] T004 Add `PackageReference Include="Npgsql" Version="10.0.3"` and a `ProjectReference` to `../AkmlSql.PostgreSql/AkmlSql.PostgreSql.csproj` in `src/AkmlSql.Engine/AkmlSql.Engine.csproj`, create `src/AkmlSql.Engine/PostgreSql/`, and confirm `dotnet publish src/AkmlSql.Engine/AkmlSql.Engine.csproj -c Release -r win-x64` still succeeds with `PublishTrimmed=false`.
- [X] T005 [P] Create `tools/pg-reference/AkmlSql.PostgreSql.ReferenceGen/AkmlSql.PostgreSql.ReferenceGen.csproj` (net10.0 console, no product references) with `Program.cs` parsing `gen --source <dir> --tags <comma list> --doc-version 18 --out <repo-root>` and `tools/pg-reference/README.md` (purpose, the five tags, the shallow-clone commands from T093, how to review a diff), and register it in `AKML-SQL.slnx` under a `/tools/` folder. *(Done, README included; `Program.cs` is a placeholder until T091.)*
- [X] T006 [P] Add the `DatabaseKind` (SqlServer = 0, PostgreSql = 1) and `TransactionState` (None = 0, Open = 1, Aborted = 2) enums and the `DatabaseKindNames` helper (`"sqlserver"` / `"postgresql"`, `Parse`, `ToName`) in `src/AkmlSql.Core/DatabaseKind.cs`.
- [X] T007 [P] Add `PostgreSqlSettings { public bool Enabled { get; set; } = true; }` and `public PostgreSqlSettings PostgreSql { get; set; } = new();` to `src/AkmlSql.Core/Config/AppSettings.cs`, and the `"postgreSql": { "enabled": true }` section to `doc/configuration.md`.
- [X] T008 [P] Add `DialectPostgreSqlV1 = "dialect.postgresql.v1"` and `IReadOnlyList<string> For(AppSettings settings)` (returns `Current` plus the new string when `settings.PostgreSql.Enabled`) to `src/AkmlSql.Engine/Capabilities.cs`, and use it in `src/AkmlSql.Engine/Handlers/Handshake/HandshakeHandler.cs`.
- [X] T009 [P] Create `THIRD-PARTY-NOTICES.txt` at the repository root (libpg_query BSD-3 with vendored upb BSD-3, utf8_range MIT, xxHash BSD-2; pgsqlparser MIT; Npgsql PostgreSQL licence; PostgreSQL documentation and catalog descriptions under the PostgreSQL License) and add `Source: "..\..\THIRD-PARTY-NOTICES.txt"; DestDir: "{app}"; Flags: ignoreversion` beside the `LICENSE.txt` entries in `src/AkmlSql.Installer/AkmlSqlSetup.iss` and `src/AkmlSql.Installer/web-installer.iss`. *(Done as one entry in `AkmlSqlSetup.iss`: `web-installer.iss` is `#include`d into it, so a second entry would only duplicate the file.)*
- [X] T010 [P] Create `tests/pg-completion-corpus/README.md` (case format identical to `tests/completion-corpus/README.md`; lower-case Northwind tables in `public` plus a `sales` schema; families p01–p12), `tests/pg-completion-corpus/gate.json` (`{ "minPassRate": 0.95, "minCases": 300 }`), `tests/pg-completion-corpus/exclusions.json` (empty list), and `tests/pg-format-corpus/README.md` (naming `golden/<name>__<style>.sql`, the invariants asserted) with `corpus/.gitkeep` and `golden/.gitkeep`.
- [X] T011 Spike S1 (record, do not "pass"): add `tests/AkmlSql.PostgreSql.Tests/Language/PgParserSmokeTests.cs` calling `PgSqlParser.Parser.Parse`, `Scan`, `SplitWithScanner` and a parse error to confirm `Stmts`, token start/end/keyword kind, statement offsets and the 1-based error cursor; publish the engine and confirm the native parser library is in `src/AkmlSql.Engine/bin/Release/net10.0/win-x64/publish/`; start `AkmlSql.Engine.exe --web --config <temp config>` under the `AkmlSqlWebEngine` service account (dev VM) and complete one handshake; record the outcome and the API shapes under "R27 → S1" in `specs/042-postgresql-support/research.md`. *(Done. Outcome in research.md "Spike outcomes": the wrapper has `EqualsIgnoringLocations`, `GetCursorCharOffset`, `GetLineAndColumn` and `Utf8OffsetMapper`; `libpg_query.dll` imports only KERNEL32. The smoke tests are `PgParserSmokeTests`. Running as the service account is verified at T181.)*
- [ ] T012 [P] Add the live-server helpers: `tests/AkmlSql.Engine.Tests/PostgreSql/PgTestServer.cs` (reads `AKML_PG_TEST_CONNECTION`, opens an `NpgsqlConnection`, returns early with a skip message when unset or unreachable, exposes the database name) and `tests/AkmlSql.Web.E2E.Tests/Harness/PgSession.cs` mirroring `tests/AkmlSql.Web.E2E.Tests/Harness/SqlSession.cs` (`WebAppFixture.WithPgOrSkipAsync`). *(Engine half done: `PgTestServer`, `northwind-lite.sql`, `scripts/pg-test-server.ps1` starts a private cluster on port 55432. The E2E `PgSession` lands with T051, because it needs the connection dialog's test ids.)*

**Checkpoint**: solution builds in one pass with the new projects; baseline folder populated; S1 recorded.

---

## Phase 2: Foundational (blocking prerequisites)

**Purpose**: the engine knows a session's kind, every unsupported surface already refuses
PostgreSQL sessions, the web never sends PostgreSQL text to a T-SQL code path, the wire contract
carries the new keys, history stores the kind, and the library's language layer exists.

**⚠️ CRITICAL**: no user story work starts until T040 shows the existing suites identical to the baseline.

### Tests for Phase 2 (write first; they must fail)

- [X] T013 [P] `tests/AkmlSql.Core.Tests/Ipc/PostgreSqlMessagesCompatTests.cs`: MessagePack round-trips for every appended key in `contracts/ipc.md` (`ConnectionInfo` 5–6, `TestSqlConnectionRequest` 1, `TestSqlConnectionResponse` 2, `ListDatabasesRequest` 2, `ExecuteMessageDto` 10–13, `ExecuteQueryResult` 13, `ChangeDatabaseResponse` 4, `CrudCellDto` 6, `AnalysisRuleInfoDto` 9, `SafetyCheckRequest` 3, `HistoryRecordRequest` 13, `HistoryEntryDto` 18, `HistorySearchRequest` 15, `SchemaPhasePayload` 5–7), and that a payload serialised without them deserialises with the defaults.
- [X] T014 [P] `tests/AkmlSql.Core.Tests/Ipc/CredentialAndRedactMessagesTests.cs`: round-trips for `CredentialRequest` / `CredentialResponse` and `RedactRequest` / `RedactResponse`; `MessageTypes` constants are 221–224; `DialectErrorCodes` strings match `contracts/ipc.md`.
- [X] T015 [P] `tests/AkmlSql.Engine.Tests/Handlers/DialectDispatchTests.cs`: a session whose kind is PostgreSql gets `ErrorInfo.Code == "dialect.unsupported"` from RefactorPreview, RefactorApply, FindInvalidObjects, FindReferences, FindUnusedVariables, CrudGeneration, EncryptedObjectDecryption, ScriptAs and DocumentOutline, while a SqlServer session gets the existing replies (a spy proves the T-SQL engine was not invoked). *(Done as `DialectGuardTests`: the refusal is one router-level guard (T031), not a patch in each handler, so the test routes real messages through a real router.)*
- [X] T016 [P] `tests/AkmlSql.Engine.Tests/Execution/SessionConnectionKindTests.cs`: `SessionConnectionRegistry.GetOrCreate(sessionId, connectionString, DatabaseKind.SqlServer)` yields a `SqlConnection`, `DatabaseKind.PostgreSql` yields an unopened `NpgsqlConnection`, and `RunExclusiveAsync` hands the SQL Server callers a `SqlConnection` as before.
- [X] T017 [P] `tests/AkmlSql.Engine.Tests/Handlers/HandshakeCapabilityTests.cs`: with `postgreSql.enabled = false` the handshake capabilities lack `dialect.postgresql.v1`; the default config includes it.
- [X] T018 [P] `tests/AkmlSql.Engine.Tests/History/HistoryKindTests.cs`: schema v4 adds `kind` to `history` and `query_sessions` with default `sqlserver`; a record with `Kind = PostgreSql` round-trips through record and search; `HistorySearchRequest.Kind` filters; grouping keeps a `sqlserver` and a `postgresql` entry with equal server and database apart; an existing v3 file upgrades in place.
- [X] T019 [P] `tests/AkmlSql.Web.Tests/Services/SavedSqlConnectionKindTests.cs`: a stored record without `Kind` / `Port` / `SslMode` / `RememberPassword` reads back as SqlServer / null / null / false; a PostgreSql record validates host, user, port range 1..65535 and the five `SslMode` values.
- [X] T020 [P] `tests/AkmlSql.Web.Tests/js/akml-editor.dialect.test.mjs`: `setDialect("postgresql")` reconfigures the language compartment with `PostgreSQL`; `setDialect("sqlserver")` restores `StandardSQL`.
- [X] T021 [P] `tests/AkmlSql.Web.Tests/Services/DialectGateTests.cs`: for a PostgreSql session the in-browser `FormatterService`, `AnalyserService`, `RefactoringService`, the `OfflineSqlScanner` fallback behind quick info and signature help, and `QueryExecutionService.SplitBatches` are never invoked (fakes record calls), and the gate yields the "Not available for PostgreSQL" outcome until an engine route exists.
- [X] T022 [P] `tests/AkmlSql.PostgreSql.Tests/Language/PgLexicalScannerTests.cs`, `PgStatementSplitterTests.cs` (dollar quotes with semicolons, `E''`, `U&''`, nested comments, quoted identifiers, `$1`, `::`; a `\d` line flagged as a meta-command; `COPY … FROM STDIN` flagged), `PgTransactionTrackerTests.cs` (the state table in data-model.md §9 including `25P02`), `PgParserTests.cs` (`StripLocations` + `TreesEquivalent` ignore whitespace, comments and keyword case, and detect a changed literal or a dropped clause).
- [X] T023 [P] `tests/AkmlSql.Core.Tests/Text/PgIdentifierQuotingTests.cs`: plain lower-case names stay unquoted; upper-case letters, spaces, a leading digit and reserved words are double-quoted; embedded double quotes are doubled.

### Implementation for Phase 2

- [X] T024 Append the keys listed in `contracts/ipc.md` to `src/AkmlSql.Core/Ipc/Messages/ConnectionInfo.cs`, `TestSqlConnectionMessages.cs`, `ListDatabasesMessages.cs`, `ExecuteQueryMessages.cs` (`ExecuteMessageDto`, `ExecuteQueryResult`, `CrudCellDto`), `ChangeDatabaseMessages.cs`, `ListAnalysisRulesResponse.cs` (`AnalysisRuleInfoDto`), `SafetyCheckRequest.cs`, `HistoryRecordRequest.cs`, `HistoryEntryDto.cs`, `HistorySearchRequest.cs` and `SchemaPhasePayload.cs` (keys 5 `InaccessibleSchemas`, 6 `HiddenSchemas`, 7 `SearchPath`), each with a doc comment naming spec 042.
- [X] T025 [P] Add `src/AkmlSql.Core/Ipc/Messages/CredentialMessages.cs` and `RedactMessages.cs` per `contracts/ipc.md`, the `MessageTypes` constants 221–224 in `src/AkmlSql.Core/Ipc/RpcMessage.cs`, and `DialectErrorCodes` (`dialect.unsupported`, `dialect.password-required`, `dialect.credentials-required`) in `src/AkmlSql.Core/Ipc/Messages/ErrorInfo.cs`.
- [X] T026 [P] Add `PgIdentifierQuoting` (`Quote`, `NeedsQuoting`, `QualifiedName(schema, name)`, reserved-word set from an embedded list) in `src/AkmlSql.Core/Text/PgIdentifierQuoting.cs`; the library, the engine and the web all use it.
- [X] T027 Add `Kind`, `ServerVersionText`, `SearchPath` and `TransactionState` to `SessionState` and a `KindOf(sessionId)` helper on `SessionManager` in `src/AkmlSql.Engine/Server/SessionManager.cs`.
- [X] T028 Generalise `src/AkmlSql.Engine/Execution/SessionConnection.cs` to hold a `DbConnection` plus `DatabaseKind` (connection factory by kind; the `RunExclusiveAsync<T>` delegate receives `DbConnection`), extend `SessionConnectionRegistry.GetOrCreate` in `src/AkmlSql.Engine/Execution/SessionConnectionRegistry.cs` with the kind, and update the SQL Server callers in `ExecuteQueryHandler.cs`, `ApplyChangesHandler.cs`, `src/AkmlSql.Engine/Handlers/Control/ChangeDatabaseHandler.cs` and `ListDatabasesHandler.cs` to cast to `SqlConnection` so their behaviour is unchanged. *(Done: `RunExclusivePostgresAsync` is a separately named method, not an overload, so the existing implicitly typed SQL Server lambdas still resolve. Registry replaces the connection when the kind changes.)*
- [X] T029 Add `src/AkmlSql.Engine/Schema/ISchemaMetadataProvider.cs` (`Kind`, `PopulatePhaseAAsync`, `PopulatePhaseBAsync`, `ListDatabasesAsync`, `ComputeChecksumAsync`), make `src/AkmlSql.Engine/Schema/SchemaMetadataService.cs` implement it without behaviour change, and add `SchemaMetadataFor(DatabaseKind)` to `src/AkmlSql.Engine/RpcContext.cs` (SQL Server default; PostgreSql registered by T030/T079).
- [ ] T030 Add `src/AkmlSql.Engine/PostgreSql/PgServices.cs` (lazy composition of the parser wrapper, reference, completion, formatter, analysis engine, schema provider and credential resolver; created only when `settings.PostgreSql.Enabled`) and wire it into `src/AkmlSql.Engine/EngineHandlerRegistry.cs`; register `CredentialHandler` (221) and `RedactHandler` (223) behind the flag (their bodies land in T054 and T117). *(Moved: `PgServices` is created in US1 together with the first services it composes (T052 to T059); `RpcContext.PostgresSchemaMetadata` and `SchemaMetadataFor` are in place.)*
- [X] T031 Add `src/AkmlSql.Engine/PostgreSql/DialectDispatch.cs` (`IsPostgres(ctx, sessionId)`, `Unsupported(messageType)` reply with `dialect.unsupported`) and apply it at the top of `src/AkmlSql.Engine/Handlers/Refactoring/RefactoringHandlers.cs` (preview, apply), `Handlers/Refactoring/FindInvalidObjectsHandler.cs`, `Server/Stubs/FindUnusedVariablesHandlerStub.cs`, `Server/Stubs/EncryptedObjectDecryptionHandlerStub.cs`, `Navigation/NavigationRequestHandler.cs` (FindReferences, DocumentOutline), `Productivity/CrudGenerationHandler.cs` and `Productivity/ScriptAsHandler.cs`. *(Done differently from the list above: `RpcRouter.DialectGuard` is a pre-dispatch hook set to `PostgreSql.DialectGuard`, which answers a PostgreSQL session's request for any of nine unsupported message types with `ErrorInfo` code 4201 "dialect.unsupported: Not available for PostgreSQL". One place covers typed and raw handlers; no handler or stub was edited. `ErrorInfo.Code` is an int, so the contract was corrected: string ids travel in `ErrorCode` fields, the router envelope uses numeric codes.)*
- [X] T032 History v4 in `src/AkmlSql.Engine/History/HistoryDatabase.cs` (`SchemaVersion = 4`; `AddColumnIfMissingAsync` for `history.kind` and `query_sessions.kind` default `'sqlserver'`; rebuild `IX_history_server_db` with `kind`; `kind` in the dedup `group_key`, the search WHERE for `HistorySearchRequest.Kind`, the record insert and the `HistoryEntryDto` mapping) and in `src/AkmlSql.Engine/History/QuerySessionStore.cs` (kind in the session-key match).
- [X] T033 [P] Web `src/AkmlSql.Web/Services/ISavedSqlConnectionStore.cs`: add `Kind`, `Port`, `SslMode`, `RootCertificatePath`, `RememberPassword` to `SavedSqlConnection` with read-time defaults and the validation rules in data-model.md §2 (no IndexedDB version bump). *(Done. `RememberPassword` is stored under the JSON key `RememberOnEngine`: the store's existing tripwire test scans the whole record for the word "password", and a flag must not weaken it.)*
- [X] T034 [P] Web `src/AkmlSql.Web/Services/ISqlConnectionService.cs`: add `DatabaseKind Kind`, `string? ServerVersionText`, `TransactionState TransactionState` (changes raised through `StateChanged`) and `bool EngineSupportsPostgres` (capability `dialect.postgresql.v1` from `IEngineBridge.EngineCapabilities`).
- [X] T035 [P] Web `src/AkmlSql.Web/wwwroot/js/akml-editor.js`: wrap the `cm.langSql.sql()` language in a `Compartment`, export `setDialect(kind)` (`"postgresql"` → `cm.langSql.PostgreSQL`, anything else → the current `StandardSQL`), and expose it to `src/AkmlSql.Web/Shared/EditorComponent.razor` as `SetDialectAsync(DatabaseKind)`.
- [X] T036 [P] Web `src/AkmlSql.Web/Services/DialectGate.cs` and its use in `IFormatterService.cs`, `IAnalyserService.cs`, `IRefactoringService.cs`, `IQuickInfoService.cs` and `ISignatureHelpService.cs` (the `OfflineSqlScanner` fallback) and `IQueryExecutionService.cs` (skip `SplitBatches`, send the script untouched) so no T-SQL code path receives PostgreSQL text.
- [X] T037 [P] Add `DbObjectType.MaterializedView = 8` in `src/AkmlSql.IntelliSense/Schema/Models/DatabaseObject.cs`, `SearchPath`, `HiddenSchemas` and `InaccessibleSchemas` on `src/AkmlSql.IntelliSense/Schema/DatabaseCache.cs`, the pass-through of keys 5–7 in `src/AkmlSql.Engine/Handlers/Schema/SchemaPhaseSerializer.cs`, and a "Materialized Views" group in `src/AkmlSql.Web/Shared/SchemaTreeComponent.razor`. *(Enum, cache lists and payload mapping done and tested; the "Materialized Views" group, system-schema toggle and PostgreSQL quoting in `SchemaTreeComponent` are part of T085, because the tree builds SQL Server `[bracket]` qualifiers and must become kind-aware as a whole.)*
- [X] T038 [P] Library language layer: `src/AkmlSql.PostgreSql/Language/PgParser.cs` (`Parse` → `PgParseResult { Tree, Error { Message, Position } }`, `Scan` → `PgToken[]`, `StripLocations`, `TreesEquivalent`), `PgLexicalScanner.cs` (R5), `PgStatementSplitter.cs` (`SplitWithScanner`, lexical fallback, meta-command and `COPY FROM STDIN` flags per span) and `PgTransactionTracker.cs` (R9). *(Done. The wrapper uses the package's `EqualsIgnoringLocations` for tree equivalence and `GetCursorCharOffset` for error positions. The splitter is the managed one (scanner-based), with the server parser's own splitter as the test oracle; it keeps leading comments so a planner hint reaches the server.)*
- [X] T039 [P] `tests/AkmlSql.PostgreSql.Tests/Isolation/DictionarySnapshotTests.cs`: snapshot the T-SQL `KeywordDictionary`, `BuiltinFunctionDictionary` and `SystemProcDictionary` into `tests/AkmlSql.PostgreSql.Tests/Isolation/tsql-dictionaries.snapshot.json` so US5 can prove they never changed (FR-054, FR-091).
- [X] T040 Run every existing suite and compare with `scripts/compare-test-results.ps1 -Before specs/042-postgresql-support/baseline/pre-feature -After <run>`; Phase 2 is complete only when the comparison is identical. *(Done 2026-10-14: the Phase 2 snapshot flagged four existing tests - three pinned array lengths that an append-only key must change, one found the Redact handler unregistered; each is fixed or allow-listed in `baseline/README.md` under "Deliberate edits to existing tests". The whole-suite re-run at T072 is the final gate.)*

**Checkpoint**: a PostgreSQL session can be created in the engine, every unsupported surface refuses it, the web gate is in place, and the SQL Server suites are identical to the baseline.

---

## Phase 3: User Story 1 — Connect to PostgreSQL and run a script (Priority: P1) 🎯 MVP

**Goal**: connect from the web dialog, run scripts as PostgreSQL runs them, see result sets,
command tags, notices, SQLSTATE errors with positions, the transaction state with one-click
rollback, and the kind in the status bar (FR-001 to FR-020).

**Independent Test**: quickstart.md §2 US1 against the Northwind port; `tests/AkmlSql.Engine.Tests --filter Category=PostgreSql` with `AKML_PG_TEST_CONNECTION`; `PostgresWorkspaceTests` US1 scenarios.

### Tests for User Story 1 (write first; they must fail)

- [X] T041 [P] [US1] `tests/AkmlSql.Engine.Tests/PostgreSql/PgTargetGuardTests.cs`: bridge clients need `Username`; `Integrated Security=true`, `Kerberos Service Name` and `GSS Encryption Mode=Require` are rejected with `dialect.credentials-required`; a relative or `..`-containing `Root Certificate` path is rejected the same way; named-pipe clients are exempt.
- [X] T042 [P] [US1] `tests/AkmlSql.Engine.Tests/PostgreSql/PgCredentialResolverTests.cs` (uses `TestAppDataRoot`): a remembered password is injected into a string without `Password`; the password is saved after a successful open when `RememberPassword` is true; `Has` / `Forget` through `CredentialHandler`; keys are `pg://host:port` + login; no password reaches the log.
- [X] T043 [P] [US1] `tests/AkmlSql.Engine.Tests/PostgreSql/PgExecuteRunnerTests.cs` (live): one result set per statement; `UPDATE 1` and `CREATE TABLE` tags; `RAISE NOTICE` as Info with severity 10 in order; an error carries `SqlState`, `Detail`, `Hint`, `Line`, `Column` (spike S3: a non-ASCII literal before the error still maps the caret correctly); earlier results kept and later statements Skipped; a `GO` line yields the hint; `\dt` and `COPY … FROM STDIN` are Skipped with hints; `BEGIN; SELECT 1/0;` then `SELECT 1` → Aborted and `25P02`; `ROLLBACK` → None; a command timeout → `TimedOut` and the next run works (spike S4: record whether Npgsql dropped the physical connection); `AllResultTypesAreUnknown` returns arrays, intervals, json, bytea and an enum as server text (spike S5); a script with 20 distinct execution errors (different SQLSTATEs) where every error carries `SqlState` and maps to its statement's line (SC-007); `SSL Mode=VerifyFull` against the local server's self-signed certificate fails with the specific TLS message while `Prefer` connects (FR-101); the messages "Earlier statements stay applied unless they ran inside an explicit transaction." and "Every statement fails until ROLLBACK." are present after a stopped run and while Aborted (FR-015, FR-016); the class carries `[Trait("Category", "PostgreSql")]`.
- [X] T044 [P] [US1] `tests/AkmlSql.Engine.Tests/PostgreSql/PgParseRunnerTests.cs` (`[Trait("Category", "Parse")]`): 20 seeded syntax errors → 20 `ExecuteMessageDto`s with `SqlState 42601`, correct `Line` / `Column`, the hint "Object names and permissions were not checked."; a connection factory that throws when opened proves no server contact.
- [X] T045 [P] [US1] `tests/AkmlSql.Engine.Tests/Handlers/TestSqlConnectionHandlerPgTests.cs`: kind routing; `ServerVersion` text; the wrong-kind rephrase for a SQL Server string aimed at 5432 and a PostgreSQL string aimed at 1433 (simulated failures); no PostgreSQL failure message contains the text "SQL Server" (FR-007).
- [X] T046 [P] [US1] `tests/AkmlSql.Core.Tests/Text/SqlMessageTextPgTests.cs`: `SqlMessageText.ForPostgres` renders `SQLSTATE 42P01: relation "x" does not exist` with `DETAIL:` / `HINT:` lines and the `NOTICE:` / `WARNING:` prefixes.
- [X] T047 [P] [US1] bUnit `tests/AkmlSql.Web.Tests/Shell/ConnectionManagerModalKindTests.cs`: `conn-kind-postgresql` shows host / port / database / user / password / sslmode / rootcert / remember fields; disabled with the capability tooltip when `EngineSupportsPostgres` is false; Test shows "PostgreSQL 18.x on host:port"; the wrong-kind message; Save stores the record without a password; `conn-pg-forget` only when `Has` is true.
- [X] T048 [P] [US1] bUnit `tests/AkmlSql.Web.Tests/Shell/StatusBarKindTests.cs`: `status-kind` label and icon per kind with the version tooltip; `status-txn` and `status-txn-rollback` appear for Open / Aborted and send `ROLLBACK;`; `status-login` title is "Login" on PostgreSQL; the text "Every statement fails until ROLLBACK." is rendered while Aborted (FR-016).
- [X] T049 [P] [US1] bUnit `tests/AkmlSql.Web.Tests/Results/MessagesViewPgTests.cs` and `ProblemsListPgTests.cs`: SQLSTATE header, detail and hint lines, notice styling, click → `Line` / `Column`; the "4 of 6 statements ran" status text; Skipped reasons shown; the text "Earlier statements stay applied unless they ran inside an explicit transaction." is rendered after a stopped run (FR-015).
- [X] T050 [P] [US1] `tests/AkmlSql.Web.Tests/Services/SqlConnectionServicePgTests.cs`: the Npgsql connection string per `contracts/ipc.md` (`SSL Mode`, `Root Certificate`, `Application Name=AKML SQL`, no `Password` when remembered); `ValidateTarget` accepts `host`, `host:5433` and an IPv6 literal; `ConnectionInfo.Kind` / `RememberPassword` set; `TransactionState` updated from an execute result; `dialect.password-required` surfaces as a `PasswordRequired` result; no PostgreSQL failure message contains the text "SQL Server" (FR-007).
- [X] T051 [P] [US1] E2E `tests/AkmlSql.Web.E2E.Tests/PostgresWorkspaceTests.cs` (class skips without `AKML_PG_TEST_CONNECTION`): quickstart US1 steps 1–5 (`status-kind`, result sets, notice, error jump, the 20-error script where each Messages entry jumps to the right line, transaction aborted and rollback, GO hint, wrong-kind test message).

### Implementation for User Story 1

- [X] T052 [US1] `src/AkmlSql.Engine/PostgreSql/PgConnectionFactory.cs`: build the `NpgsqlConnection`, attach the `Notice` collector, run the connect-time facts query from `contracts/catalog-queries.md` into `SessionState` (`ServerVersionText`, `SearchPath`, database, user), default `Application Name`.
- [X] T053 [P] [US1] `src/AkmlSql.Engine/PostgreSql/PgTargetGuard.cs` per `contracts/ipc.md` (bridge clients only; named-pipe exempt like `BridgeSqlTargetGuard`); rejects a `Root Certificate` value that is not an absolute canonical path (`Path.GetFullPath(value) == value`) with `dialect.credentials-required`.
- [X] T054 [P] [US1] `src/AkmlSql.Engine/PostgreSql/PgCredentialResolver.cs` (inject / save / forget / has over `SqlCredentialStore` with `pg://{host}:{port}` keys) and `src/AkmlSql.Engine/PostgreSql/CredentialHandler.cs` (221 → 222).
- [X] T055 [US1] `src/AkmlSql.Engine/Handlers/Control/TestSqlConnectionHandler.cs`: PostgreSql branch (open + `SELECT version()` → `ServerVersion`; wrong-kind detection → rephrased message); the SQL Server branch unchanged apart from filling `ServerVersion`.
- [X] T056 [US1] `src/AkmlSql.Engine/Handlers/Control/ConnectionChangedHandler.cs`: PostgreSql branch (skip `SqlAliasResolution` and `ParserService.SetServerVersion`, which is process-wide for the T-SQL parser; run `PgTargetGuard` for bridge clients, resolve the remembered password, create the PostgreSql `SessionConnection`, capture connect-time facts, pick `ctx.SchemaMetadataFor(kind)` for Phase A/B — until T079 lands the PostgreSql provider returns an empty cache without error — set `SessionState.Kind`).
- [X] T057 [US1] `src/AkmlSql.Engine/PostgreSql/PgResultSetReader.cs`: columns as text (`AllResultTypesAreUnknown`), `ColumnSqlTypes` from `GetColumnSchema().DataTypeName`, row and size caps, `ExecuteResultSet` with `IsEditable = false` (US6 adds provenance).
- [X] T058 [US1] `src/AkmlSql.Engine/PostgreSql/PgExecuteRunner.cs`: split (`PgStatementSplitter`), one `NpgsqlCommand` per statement with `CommandTimeout`, result sets through `PgResultSetReader`, notices → Info messages, errors → `ExecuteMessageDto` (`SqlState` / `Detail` / `Hint`; `Line` / `Column` from statement start + `Position − 1`), command tags from the verb and `RecordsAffected`, the `GO` hint, meta-command and `COPY FROM STDIN` → Skipped, stop at the first error with later statements Skipped, one `ExecuteBatchDto` per statement, `PgTransactionTracker` → `TransactionState`, `CurrentDatabase`, `TimedOut` / `Disconnected` mapping; after a stop append the message "Earlier statements stay applied unless they ran inside an explicit transaction." and while Aborted append "Every statement fails until ROLLBACK." (FR-015, FR-016).
- [X] T059 [P] [US1] `src/AkmlSql.Engine/PostgreSql/PgParseRunner.cs`: per-statement `PgParser.Parse`, errors to `ExecuteMessageDto` (`42601`, line, column, hint), no connection use.
- [X] T060 [US1] `src/AkmlSql.Engine/Execution/ExecuteQueryHandler.cs`: dispatch to `PgExecuteRunner` (ExecuteQuery) and `PgParseRunner` (ExecuteParse) when `ctx.Sessions.KindOf(sessionId) == PostgreSql`; `ExecuteCancelHandler.cs` cancels the running `NpgsqlCommand` through the existing `RegisterQuery` / `TryCancel`.
- [X] T061 [P] [US1] `src/AkmlSql.Core/Text/SqlMessageText.cs`: add `ForPostgres(ExecuteMessageDto)` (header `SQLSTATE <code>: <message>`, `DETAIL:` / `HINT:` lines, notice prefixes).
- [X] T062 [P] [US1] `src/AkmlSql.Engine/Productivity/ProductivityRequestHandler.cs`: PostgreSql branch for `StatementBoundary` using `PgStatementSplitter` (execute-current-statement keeps working).
- [X] T063 [US1] Web `src/AkmlSql.Web/Services/ISqlConnectionService.cs`: `ConnectAsync` / `TestAsync` taking a `ConnectionTarget` with the PostgreSQL branch (`BuildPostgresConnectionString`, `ValidateTarget` for `host[:port]`), `HasRememberedPasswordAsync` / `ForgetPasswordAsync` (221), `Kind` / `ServerVersionText` / `TransactionState` updates, `dialect.password-required` → `PasswordRequired`.
- [X] T064 [US1] Web `src/AkmlSql.Web/Shared/ConnectionManagerModal.razor` (and `wwwroot/js/akml-connection-manager.js` if focus handling needs it): kind switch, PostgreSQL field set, TLS mode select, root certificate path, Remember password with the FR-104 / FR-106 help text, Forget, Test result text, capability-disabled state, wrong-kind rephrase; `IConnectionManagerController.Open(DatabaseKind? kind)` in `src/AkmlSql.Web/Services/IConnectionManagerController.cs`.
- [X] T065 [P] [US1] Web `src/AkmlSql.Web/Shared/ConnectionPickerComponent.razor`: kind label and icon per entry (`conn-item-kind`); add the `database` Lucide icon to `src/AkmlSql.Web/wwwroot/lib/icons/akml-icons.svg` through `src/AkmlSql.Web/tools/icons/build-sprite.mjs` if `IconSpriteCoverageTests` reports it missing. *(Done 2026-10-14 with one correction: `ConnectionPickerComponent` lists engines (Local / LAN), not database connections, so it has no kind to show. The kind label lives where database connections are listed: the saved list of the connection manager (`conn-row-kind`, title "PostgreSQL" / "SQL Server") and the status bar (`status-kind`). `database` was already in the icon sprite)*
- [X] T066 [P] [US1] Web `src/AkmlSql.Web/Shared/StatusBar.razor`: `status-kind` segment (icon + label, version tooltip), `status-txn` + `status-txn-rollback` (sends `ROLLBACK;` through `IQueryExecutionService.ExecuteAsync` and refreshes the state), `status-login` title by kind.
- [X] T067 [US1] Web `src/AkmlSql.Web/Services/IQueryExecutionService.cs` and `ExecuteLineMapper.cs`: PostgreSQL scripts sent untouched; `TransactionState` raised from results; the line mapper handles one `ExecuteBatchDto` per statement and honours `Column`. *(Done 2026-10-14: the reply deadline counts a PostgreSQL script by semicolons (the browser has no PostgreSQL splitter and a shorter deadline would report a live run as lost); `ExecuteLineMapper` maps a PostgreSQL error with the line and column the engine already computed and adds `DocumentColumn`; the not-connected text no longer names SQL Server)*
- [X] T068 [US1] Web `src/AkmlSql.Web/Shared/MessagesView.razor`, `ResultsPaneComponent.razor` and `ProblemsListComponent.razor`: PostgreSQL rendering through `SqlMessageText.ForPostgres`, notice styling, Skipped reasons, "N of M statements ran", click → `Line` / `Column`. *(Done 2026-10-14: the Problems tab lists the errors of a PostgreSQL run above the analyser's findings (`problem-execution`); clicking an error in Messages or Problems goes to its line and column (`EditorComponent.GotoPositionAsync`); notices and warnings are drawn quieter with their level in the text)*
- [X] T069 [US1] Web `src/AkmlSql.Web/Pages/Editor.razor`: `SetDialectAsync` on connect / disconnect / switch, the FR-009 transaction guard dialog before switching connection, `HistoryRecordRequest.Kind` on record; `src/AkmlSql.Web/Shared/CommandPalette.razor`: "Connect to PostgreSQL…" (`palette-connect-postgresql`) opening the dialog on the PostgreSQL kind. *(Done 2026-10-14: the editor's language follows `SqlConn.Kind` through the `Kind` parameter added in Phase 2 (connect, disconnect and switch all reconfigure in place); the FR-009 question lives in the connection manager's Connect, not in `Editor.razor`, because that is where a switch is made; the palette entry is `sql:connect-postgresql` and rows now carry `data-action-id` (the planned `palette-connect-postgresql` id would have needed a second `data-testid`); `HistoryRecordRequest.Kind` is set from `SqlConn.Kind`)*
- [X] T070 [US1] Web `src/AkmlSql.Web/Services/IHistoryService.cs`: `RecordAsync` carries `Kind`; `HistoryEntryDto.Kind` exposed to the page (badge lands in US4).
- [X] T071 [US1] `tests/AkmlSql.Engine.Tests/PostgreSql/ConnectionChangedPgTests.cs` (live): a PostgreSql `ConnectionChanged` sets `SessionState.Kind`, creates the session connection, a following `ExecuteQuery` runs, and the T-SQL parser version is unchanged after the PostgreSQL `ConnectionChanged`; record spike outcomes S3, S4 and S5 under R7, R8 and R10 in `specs/042-postgresql-support/research.md` and route a dropped connection through `Disconnected` if S4 requires it.
- [X] T072 [US1] Run `tests/AkmlSql.PostgreSql.Tests`, `tests/AkmlSql.Engine.Tests --filter Category=PostgreSql`, `tests/AkmlSql.Web.Tests` and the US1 E2E scenarios; then the full SQL Server suites compared with the baseline (identical). *(Done 2026-10-14: the full existing suites were compared with the baseline after the first story (no test that passed before stopped passing, apart from two engine host tests that lost a random port to another process running at the same time and pass alone))*

**Checkpoint**: US1 is a working MVP: connect, run, messages, errors, transaction state, status bar.

---

## Phase 4: User Story 2 — Browse a PostgreSQL database (Priority: P1)

**Goal**: the Schema explorer shows schemas, tables, views, materialized views, functions,
procedures and sequences with columns, keys, foreign keys, indexes and comments; the database
selector reconnects with a warning (FR-030 to FR-046).

**Independent Test**: quickstart.md §2 US2; `PgSchemaMetadataServiceTests` (live); `PostgresWorkspaceTests` US2 scenarios.

### Tests for User Story 2 (write first; they must fail)

- [X] T073 [P] [US2] `tests/AkmlSql.Engine.Tests/PostgreSql/PgSchemaMetadataServiceTests.cs` (live): Phase A lists schemas, tables, views, materialized views, sequences, functions (Scalar / Table by `proretset`) and procedures with row estimates and descriptions; Phase B gives columns (`format_type`, nullability, default, identity, PK), foreign keys, indexes, parameters and comments; `SearchPath` captured; an inaccessible schema marked; the databases list excludes templates; the checksum changes after `CREATE TABLE`.
- [X] T074 [P] [US2] `tests/AkmlSql.Engine.Tests/Schema/SchemaCacheManagerKindTests.cs`: cache key and persisted file name include the kind; `GetCache` for the two kinds of `northwind` are distinct objects.
- [X] T075 [P] [US2] `tests/AkmlSql.Engine.Tests/Handlers/SessionDatabaseHandlersPgTests.cs`: `ListDatabases` by session kind; `ChangeDatabase` on PostgreSql reconnects, resets `TransactionState`, updates `SessionState.DatabaseName` and replies `TransactionState = 0`.
- [X] T076 [P] [US2] bUnit `tests/AkmlSql.Web.Tests/Shell/SchemaTreeComponentPgTests.cs`: "Materialized Views" group; system schemas hidden behind `schema-system-toggle`; `schema-inaccessible` marker; insert-name and copy-name use `PgIdentifierQuoting`.
- [X] T077 [P] [US2] bUnit `tests/AkmlSql.Web.Tests/Shell/DatabaseSelectorPgTests.cs`: a PostgreSQL switch shows `db-switch-confirm` with the reconnect text; with `TransactionState != None` it shows the rollback confirmation; cancel leaves the database unchanged.
- [X] T078 [P] [US2] E2E US2 scenarios appended to `tests/AkmlSql.Web.E2E.Tests/PostgresWorkspaceTests.cs` (quickstart US2 steps 1–5).

### Implementation for User Story 2

- [X] T079 [US2] `src/AkmlSql.Engine/PostgreSql/PgSchemaMetadataService.cs` implementing `ISchemaMetadataProvider` with the Phase A and Phase B queries in `contracts/catalog-queries.md` (typmod → MaxLength / Precision / Scale, `MaterializedView`, parameters from `proarg*`, descriptions, `has_schema_privilege` → `InaccessibleSchemas`, `42501` handling, `SearchPath` on the cache, system schemas as `HiddenSchemas`), registered in `PgServices` and `RpcContext.SchemaMetadataFor`.
- [X] T080 [P] [US2] `src/AkmlSql.Engine/PostgreSql/PgChangeChecksum.cs` (the two `md5(string_agg(…))` queries) wired by kind into `src/AkmlSql.Engine/Schema/ChangeDetector.cs`, `src/AkmlSql.Engine/Schema/SchemaRefreshService.cs` and `src/AkmlSql.Engine/Handlers/Schema/SchemaChecksumHandler.cs`. *(Done 2026-10-14 with one correction: `SchemaChecksumHandler` needed no change, because the "checksum" it returns is the engine cache's phase and object count, not a server hash; the server hash feeds `ChangeDetector` (which now picks the PostgreSQL probe from the cache's kind) and the contract's `relkind` needed a `::text` cast)*
- [X] T081 [P] [US2] `src/AkmlSql.Engine/PostgreSql/PgDatabaseLister.cs` and the PostgreSql branch in `src/AkmlSql.Engine/Handlers/Control/ListDatabasesHandler.cs` (session kind, or `ListDatabasesRequest.Kind` without a session).
- [X] T082 [US2] `src/AkmlSql.Engine/Handlers/Control/ChangeDatabaseHandler.cs`: PostgreSql branch (`NpgsqlConnection.ChangeDatabase`, tracker reset, `SessionState.DatabaseName`, schema cache switch and Phase A/B kick-off, `ChangeDatabaseResponse.TransactionState = 0`).
- [X] T083 [US2] `src/AkmlSql.Engine/Schema/SchemaCacheManager.cs`: kind in `BuildCacheKey` and `GetCacheFilePath`; `ConnectionChangedHandler.cs` and `SchemaRefreshService.cs` pass the kind; `ServerCanonicalIdentity` for PostgreSql = `pg://{host}:{port}` in `src/AkmlSql.Engine/Handlers/Schema/SchemaIdentifyHandlerSupport.cs` and the identity resolver in `src/AkmlSql.Engine/EngineHost.cs`. *(Done 2026-10-14: the kind rides on the session id, which is the first argument of every cache lookup, so `SchemaCacheManager.KindOf` changes the key (`pg|session:db`) and the file name without touching the 25 callers; `DatabaseCache.Kind` is persisted; the schema identity is `pg://host:port` in the engine's identify handler and in the web's `SchemaIdentity`; reconnecting as another role starts the PostgreSQL cache over, because what a role may see is part of what was read)*
- [X] T084 [US2] `src/AkmlSql.Engine/Navigation/ObjectDefinitionService.cs` and `NavigationRequestHandler.cs`: PostgreSql branch for `GetObjectDefinition` using `pg_get_viewdef` / `pg_get_functiondef` (FR-068) with `tests/AkmlSql.Engine.Tests/PostgreSql/PgObjectDefinitionTests.cs`.
- [X] T085 [US2] Web `src/AkmlSql.Web/Shared/SchemaTreeComponent.razor`: Materialized Views group, `schema-system-toggle`, `schema-inaccessible` marker, PostgreSQL quoting in the name actions; `src/AkmlSql.Web/Services/ISchemaSync.cs` and `ISchemaCacheStore.cs` keep the `pg://` identity distinct from SQL Server keys. *(Done 2026-10-14: `ISchemaSync` and `ISchemaCacheStore` needed no change; the web keys them by `ISqlConnectionService.SchemaIdentity`, which is `pg://host:port` for PostgreSQL; a run that creates, alters or drops something refetches the schema (FR-043: engine reload from `PgCommandTags.ChangesSchema`, browser refetch after the run))*
- [X] T086 [US2] Web `src/AkmlSql.Web/Shared/DatabaseSelectorComponent.razor` and `ISqlConnectionService.ChangeDatabaseAsync`: the PostgreSQL confirm dialog (`db-switch-confirm`), the transaction guard, state reset after the reply. *(Done 2026-10-14: the confirmation is an ordinary dialog with `data-dialog-id="db-switch-confirm"` (a new `DialogRequest.DialogId`), not a second `data-testid`)*
- [X] T087 [P] [US2] Web `src/AkmlSql.Web/Shared/Settings/SchemaCacheSection.razor`: entries show the kind (identity prefix) so the two `northwind` caches are distinguishable.
- [X] T088 [US2] `tests/AkmlSql.Engine.Tests/PostgreSql/Fixtures/thousand-objects.sql` (generates 1,000 objects) and an SC-008 timing test in `PgSchemaMetadataServiceTests.cs` (Phase A names under 2 s, Phase B details under 10 s, local server).
- [X] T089 [US2] Run the US2 tests, the E2E scenarios and the baseline comparison.

**Checkpoint**: US1 + US2 give a connected, browsable PostgreSQL workspace.

---

## Phase 5: User Story 3 — Write PostgreSQL with help (Priority: P1)

**Goal**: highlighting, completion, quick info, signature help, snippets and wildcard expansion
from a documentation-derived reference and the schema; nothing T-SQL appears (FR-050 to FR-069).

**Independent Test**: the PostgreSQL completion corpus gate ≥ 95 %; `ReferenceCoverageTests`; quickstart.md §2 US3; `PostgresWorkspaceTests` US3 scenarios.

### Reference (FR-050 to FR-054)

- [X] T090 [P] [US3] `tests/AkmlSql.PostgreSql.Tests/Reference/ReferenceGenTests.cs`: readers for `kwlist.h`, `pg_proc.dat` / `pg_type.dat` / `pg_operator.dat`, a `ref/*.sgml` page, the `func/*.sgml` section index, `datatype.sgml` and `errcodes.txt` over fixture snippets in `tests/AkmlSql.PostgreSql.Tests/Reference/Fixtures/`; `VersionDiffer` yields `minVersion`; the writer's output matches `contracts/reference-schema.md`.
- [X] T091 [US3] Generator readers in `tools/pg-reference/AkmlSql.PostgreSql.ReferenceGen/`: `KwlistReader.cs`, `DatFileReader.cs`, `RefPageReader.cs`, `FuncSectionIndex.cs`, `DatatypeSectionIndex.cs`, `ErrcodesReader.cs`, `LexicalEntries.cs`, `VersionDiffer.cs`, `ReferenceWriter.cs` (json, `doc/postgresql-reference.md`, `omissions.json`, `coverage.json` with the source counts) and the `Program.cs` wiring.
- [X] T092 [P] [US3] `tests/AkmlSql.PostgreSql.Tests/Reference/ReferenceLoadTests.cs` and `ReferenceCoverageTests.cs` (SC-006: 100 % of commands and types, ≥ 95 % of functions and operators against `coverage.json`; every entry has a summary, section and url; names unique per kind; `minVersion` in 14..18); both carry `[Trait("Category", "Reference")]`.
- [X] T093 [US3] Run the generator (manual; shallow clones outside the repository: `git clone --depth 1 --branch REL_18_STABLE https://git.postgresql.org/git/postgresql.git pg18`, and the same for `REL_14_STABLE`, `REL_15_STABLE`, `REL_16_STABLE`, `REL_17_STABLE`; then `dotnet run --project tools/pg-reference/AkmlSql.PostgreSql.ReferenceGen -- gen --source <parent dir> --tags REL_14_STABLE,REL_15_STABLE,REL_16_STABLE,REL_17_STABLE,REL_18_STABLE --doc-version 18 --out .`), review `src/AkmlSql.PostgreSql/Reference/omissions.json` and drive every `unresolved` entry to zero or a written reason, and keep `src/AkmlSql.PostgreSql/Reference/postgresql-reference.json`, `coverage.json` and `doc/postgresql-reference.md` as the committed outputs.
- [X] T094 [US3] `src/AkmlSql.PostgreSql/Reference/PgReference.cs`: embedded JSON loader, dictionaries by kind, `For(serverMajor)` version filter, SQLSTATE lookup, load-time self-check.

### Tests for User Story 3 (write first; they must fail)

- [X] T095 [P] [US3] `tests/pg-completion-corpus/p01-select-columns.json` … `p12-ddl-and-types.json` (≥ 300 cases: select columns, from tables and search path, join on foreign keys, on conflict, returning, limit / offset, ilike / lateral, with … as materialized, window functions, dollar-quoted bodies, `::` casts and `$1`, json and array operators, distinct on / filter, DDL types), `tests/AkmlSql.PostgreSql.Tests/Completion/NorthwindPgTestCacheFactory.cs` (lower-case Northwind in `public` plus `sales`) and `PgCompletionCorpusGateTests.cs` (ratchet from `gate.json`, exclusions file, `[Trait("Category", "CompletionCorpus")]`). *(379 cases in 13 files; the gate passes 100 %; `gate.json` holds a per-family ratchet of 1.0 beside `minPassRate` 0.95, so any regression fails. The corpus was written from PostgreSQL semantics, and the few expectations that were wrong - for example a set-returning function is legal in a select list - were corrected in the corpus, not the engine. `exclusions.json` is empty.)*
- [X] T096 [P] [US3] `tests/AkmlSql.PostgreSql.Tests/Completion/PgCursorContextAnalyzerTests.cs`, `PgQuickInfoServiceTests.cs`, `PgSignatureServiceTests.cs`, `PgWildcardExpanderTests.cs`, `PgSnippetPackTests.cs`, `PgIdentifierInsertionTests.cs` (double quotes only when needed) and `tests/AkmlSql.PostgreSql.Tests/Language/PgDestructiveStatementCheckTests.cs` (DROP, TRUNCATE, DELETE / UPDATE without WHERE) and `tests/AkmlSql.PostgreSql.Tests/Completion/PgVersionGatingTests.cs` (with server major 14 the engine withholds entries whose `minVersion` is 15–18; with 18 it offers them; FR-052).
- [X] T097 [P] [US3] bUnit `tests/AkmlSql.Web.Tests/Completion/CompletionServicePgRoutingTests.cs` (no offline fallback for PostgreSQL; the engine path is used) and `tests/AkmlSql.Web.Tests/Snippets/SnippetStoreKindTests.cs` (built-ins by kind; user snippets filtered by `Kind`).
- [X] T098 [P] [US3] E2E US3 scenarios appended to `tests/AkmlSql.Web.E2E.Tests/PostgresWorkspaceTests.cs` (quickstart US3 steps 1–4; step 5 only when `AKML_PG14_TEST_CONNECTION` is set). *(Scenarios are in `tests/AkmlSql.Web.E2E.Tests/PostgresEditingTests.cs`, a class of its own. All run in Chromium against the private cluster; step 5 skips unless `AKML_PG14_TEST_CONNECTION` is a 14 server. They found three editor defects, fixed in `akml-editor.js`: the quoted-name label, the closing quote the editor adds, and a popup that survived typing `@`.)*

### Implementation for User Story 3

- [X] T099 [US3] `src/AkmlSql.PostgreSql/Completion/PgCursorContext.cs` and `PgCursorContextAnalyzer.cs` (with `PgAliasExtractor.cs`, `PgCteExtractor.cs`) from scan tokens: clause, dot chain, partial word, in-string / in-comment / in-dollar-body, aliases, CTEs, join target, statement verb (R4). *(`PgAliasExtractor` and `PgCteExtractor` are one file, `PgTableRefExtractor.cs`, because both read FROM items and CTE bodies from the same token view; `PgStatementTokens.cs` holds that view.)*
- [X] T100 [US3] Providers in `src/AkmlSql.PostgreSql/Completion/Providers/`: `PgKeywordProvider.cs`, `PgCommandProvider.cs`, `PgFunctionProvider.cs`, `PgTypeProvider.cs`, `PgSchemaObjectProvider.cs` (search-path ordering, qualified fallback, materialized views, sequences), `PgColumnProvider.cs`, `PgAliasCteProvider.cs`, `PgJoinOnFkProvider.cs` (port of `FkHelpers` logic), `PgSnippetProvider.cs`.
- [X] T101 [US3] `src/AkmlSql.PostgreSql/Completion/PgCompletionEngine.cs`: provider orchestration, version gating through `PgReference.For`, `FilterText`, insertion quoting via `PgIdentifierQuoting`, sort priorities matching the SQL Server engine's conventions → `CompletionResponse`.
- [X] T102 [P] [US3] `src/AkmlSql.PostgreSql/Completion/PgQuickInfoService.cs` and `PgSignatureService.cs` (reference + schema; "since PostgreSQL N"; url).
- [X] T103 [P] [US3] `src/AkmlSql.PostgreSql/Completion/PgWildcardExpander.cs` (FR-067) on the `WildcardExpansionHandler` contract.
- [X] T104 [P] [US3] PostgreSQL snippet pack `src/AkmlSql.PostgreSql/Snippets/{sel,ins,upd,del,cte,crt,cix,cfn,cvi,txn}.akmlsnippet` and `PgSnippetPack.cs`; an optional `dialect` field (default `sqlserver`) in the `.akmlsnippet` schema read by `src/AkmlSql.Engine/Snippets/SnippetLoader.cs` so user snippets are filtered by kind. *(The snippet text lives only in the embedded `Snippets/pg-*.akmlsnippet` files; `PgSnippetPack` reads them, so the completion list and the expansion share one source. The engine links the files into `snippets/` and `SnippetIndex` separates them from the SQL Server shortcodes by the new `dialect` field. The web edition has its own store: `WebSnippetMetadata.Kind`, PostgreSQL built-ins `builtin.pg.*`, `ListAsync(kind)` and `GetByShortcodeAsync(code, kind)`.)*
- [X] T105 [P] [US3] `src/AkmlSql.PostgreSql/Safety/PgDestructiveStatementCheck.cs` and the `Kind` branch in `src/AkmlSql.Engine/Safety/SafetyCheckHandler.cs` (FR-069); the web passes `SafetyCheckRequest.Kind`.
- [X] T106 [US3] Engine dispatch through `PgServices` in `src/AkmlSql.Engine/Handlers/Completion/CompletionHandler.cs`, `QuickInfoHandler.cs`, `SignatureHelpHandler.cs`, `WildcardExpansionHandler.cs` and `src/AkmlSql.Engine/Handlers/Snippets/SnippetHandlers.cs` (list and expand by kind). *(`PgServices` is `src/AkmlSql.Engine/PostgreSql/PgServices.cs`, created only when `postgreSql.enabled` is on. `SnippetListRequest` gained append-only key 5 `Kind`.)*
- [X] T107 [US3] Web `src/AkmlSql.Web/Services/ICompletionService.cs`, `IQuickInfoService.cs`, `ISignatureHelpService.cs`, `IWildcardExpansionService.cs`: PostgreSQL → engine only (replace the `DialectGate` placeholder); `src/AkmlSql.Web/Services/ISnippetStore.cs`: built-ins by kind and `Kind` on user snippets; `src/AkmlSql.Web/Pages/Snippets.razor`: kind filter. *(The services already sent PostgreSQL requests to the engine whenever the bridge is open, and the gate only governed the offline paths; what changed here is pinned by `CompletionServicePgRoutingTests`, and the snippet store and Snippets page are kind-aware. The surround-with picker lists only the connection's kind.)*
- [X] T108 [US3] Web `src/AkmlSql.Web/Shared/EditorComponent.razor` and `wwwroot/js/akml-editor.js`: completion trigger characters for PostgreSQL (`.` and `"`; no `[`, `@`, `#` triggers); hover and signature tooltips consume the engine replies unchanged. *(Word detection is `wordBefore(kind, text)`: PostgreSQL words are `[\w$]` or an open double-quoted name; `@`, `#` and `[` end an explicit session. `::` and the PostgreSQL clause words open the popup. Hover rows show the manual link, restricted to `https://www.postgresql.org/docs/<n>/<page>.html`.)*
- [X] T109 [US3] Run the completion corpus gate until ≥ 95 % (fix providers, not cases; record any exclusion with a reason in `tests/pg-completion-corpus/exclusions.json`), the US3 unit tests, the E2E scenarios and the baseline comparison.

**Checkpoint**: the P1 stories are complete; the reference, the corpus gate and the coverage test are the ratchets.

---

## Phase 6: User Story 4 — Switch between SQL Server and PostgreSQL without friction (Priority: P2)

**Goal**: saved connections of both kinds, switching in two interactions, history by kind, AI in
the active dialect, remembered passwords, and the capability-absent state (FR-070 to FR-082,
FR-104 to FR-106, FR-110, FR-111).

**Independent Test**: quickstart.md §2 US4; `PostgresWorkspaceTests` US4 scenarios including SC-018.

### Tests for User Story 4 (write first; they must fail)

- [X] T110 [P] [US4] `tests/AkmlSql.AI.Tests/PromptDialectTests.cs`: `PromptTemplates.BuildSystemPrompt(…, SqlDialect.PostgreSql)` names PostgreSQL and forbids T-SQL; the SqlServer text equals `tests/AkmlSql.AI.Tests/Fixtures/system-prompt-sqlserver.golden.txt` (captured before the change) byte for byte; all seven builders accept the dialect. *(23 tests. The eight golden files were captured from the unchanged code and are compared after line endings are normalised, so a Windows and a Linux checkout agree. `PlaceholderRestorerTests` (13) sits beside it for the answer side.)*
- [X] T111 [P] [US4] `tests/AkmlSql.Engine.Tests/PostgreSql/RedactHandlerTests.cs`: string, numeric and dollar-quoted literals replaced with placeholders; identifiers and structure kept; unscannable input → `Ok = false`. *(`PgLiteralRedactorTests` (32) in the PostgreSQL library tests, `RedactHandlerTests` (17) in the engine tests, and two contract tests. `RedactResponse` gained append-only key 3 `Placeholders`, the map the browser needs to put the literals back into the answer.)*
- [X] T112 [P] [US4] bUnit `tests/AkmlSql.Web.Tests/Shell/ConnectionSwitchTests.cs` (switch SQL Server ↔ PostgreSQL: kind, status, `setDialect` call, editor text intact, transaction guard), `tests/AkmlSql.Web.Tests/History/HistoryPageKindTests.cs` (`history-kind` badge, `history-kind-filter`, `kind:postgresql` / `kind:pg` parsing in `WebHistoryLogic`, restore suggests the matching saved connection), `tests/AkmlSql.Web.Tests/Settings/ConnectionsSectionKindTests.cs` (`settings-conn-kind`, `settings-conn-forget`), `tests/AkmlSql.Web.Tests/Settings/DiagnosticsSectionPgTests.cs` (`diag-postgresql` states) and `tests/AkmlSql.Web.Tests/Ai/AiPromptDialectTests.cs` (the prompt carries the kind; PostgreSQL redaction goes through `RedactRequest`; `Ok = false` → consent dialog and nothing sent). *(The files differ from the list: `ConnectionSwitchTests` drives the real connection service with a scripted bridge for ten rounds; the others are `HistoryPageKindTests`, `WebHistoryKindSearchTests`, `ConnectionsSectionKindTests`, `DiagnosticsSectionPgTests`, `CapabilityNoticeTests`, `AiPromptDialectTests`, `GhostTextDialectTests` and `AiChatPanelDialectTests`.)*
- [X] T113 [P] [US4] E2E US4 scenarios appended to `tests/AkmlSql.Web.E2E.Tests/PostgresWorkspaceTests.cs` (quickstart US4 steps 1–5; step 5 uses `tests/AkmlSql.Web.E2E.Tests/Harness/SandboxEngine.cs` with `postgreSql.enabled = false`), including SC-018 (no password in IndexedDB, the engine log or a settings export). *(Six scenarios appended to `PostgresWorkspaceTests.cs`. Step 5 starts the sandbox engine with `postgreSql.enabled: false` through `SandboxEngine.StartAsync(postgreSqlEnabled)`. The numbers are in `baseline.md`.)*
- [ ] T114 [P] [US4] Spike S7 (record): under the `AkmlSqlWebEngine` service account, locate the credential file (the directory of `Constants.ConfigFilePath`) and compare its ACL with `config.json`; write the result into R14 of `specs/042-postgresql-support/research.md` and adjust the consent text in `contracts/web-ui.md` if it differs. *(Partly done: the service runs as LocalSystem and the store is in the SYSTEM profile, not beside the `--config` file, which corrects R14; the access list of that folder needs an elevated shell and was not read. The result and the one command left are in R14. Left unticked on purpose.)*

### Implementation for User Story 4

- [X] T115 [US4] `src/AkmlSql.AI/SqlDialect.cs` (maps from `DatabaseKind`) and the dialect parameter on `src/AkmlSql.AI/Prompts/PromptTemplates.cs` (`BuildSystemPrompt`, default SqlServer unchanged), `ChatSystemPrompt.cs`, `ExplainPrompt.cs`, `FixPrompt.cs` (SQLSTATE / detail / hint in the error block), `GhostTextPrompt.cs`, `IndexAnalysisPrompt.cs`, `OptimizePrompt.cs`, `TextToSqlPrompt.cs`. *(`SqlDialect` is in `AkmlSql.Engine.Ai` with `SqlDialects.From(DatabaseKind)`. Every builder takes `dialect` and `serverVersion` after its existing arguments, so the SSMS call sites compile unchanged; the Fix builder also takes `sqlState`, `detail` and `hint`.)*
- [X] T116 [US4] Web `src/AkmlSql.Web/Services/IAiPromptService.cs`, `IAiGhostTextService.cs`, `IAiSchemaContextProvider.cs`, `src/AkmlSql.Web/Shared/AiChatPanel.razor` (system text by kind) and `AiPrivacyModeBadge.razor`: pass the active kind; the PostgreSQL privacy path sends `RedactRequest` (223) and routes `Ok = false` or no engine to the consent dialog text "Literals could not be removed from this script". *(`AiPromptService` and `AiGhostTextService` take the dialect context, the redactor and the dialog service as optional arguments, so a host without PostgreSQL support behaves as before. `AiOutboundGate` decides what may leave: a local provider gets the script as it is, a cloud provider gets it redacted, and a failed redaction asks (prompts) or sends nothing (ghost text). Chat is free text and is not redacted. AI Fix now receives the last run's error on PostgreSQL through `AiPanel.LastErrorProvider`.)*
- [X] T117 [US4] `src/AkmlSql.Engine/PostgreSql/RedactHandler.cs` (223 → 224) over `PgLexicalScanner`, registered behind the flag in `EngineHandlerRegistry.cs`. *(`PgLiteralRedactor` in the PostgreSQL library does the work and `RedactHandler` wraps it. A dollar-quoted body after `AS` or `DO` is redacted inside; one that does not read as PostgreSQL text is a single literal. The `AllMessageTypesInProcessTests` entry is removed.)*
- [X] T118 [US4] Web `src/AkmlSql.Web/Pages/History.razor`, `src/AkmlSql.Web/Services/WebHistoryLogic.cs` and `IHistoryService.cs`: kind badge, filter control, the `kind:` term (aliases `pg`, `mssql`) → `HistorySearchRequest.Kind`, restore → the saved connection by kind, server and database. *(The kind badge and filter appear when the engine supports PostgreSQL, a PostgreSQL entry exists, or a kind filter is on; a SQL Server-only page is unchanged. Restore never runs a script on the other kind of server: Re-execute opens it and names the connection it belongs to.)*
- [X] T119 [P] [US4] Web `src/AkmlSql.Web/Shared/Settings/ConnectionsSection.razor` (kind badge; Forget password → `ForgetPasswordAsync`; delete → Forget first) and `src/AkmlSql.Web/Shared/Settings/DiagnosticsSection.razor` ("PostgreSQL support: available / not advertised / disabled in config", active kind and server version). *(Test ids: `settings-conn-kind`, `settings-conn-connect-{id}`, `settings-conn-forget-{id}`, `diag-postgresql` with `data-state`, `diag-active-connection`. The settings catalog gained `diagnostics-postgresql`.)*
- [X] T120 [US4] Remembered-password flow end to end: `src/AkmlSql.Web/Shared/ConnectionManagerModal.razor` (remember flag persisted; connect without a password → `HasRememberedPasswordAsync` → no prompt; `PasswordRequired` → prompt with "remember again"), `src/AkmlSql.Engine/Handlers/Control/ConnectionChangedHandler.cs` and `TestSqlConnectionHandler.cs` inject-or-save through `PgCredentialResolver`. *(Most of the flow existed from US1. Added: a remembered password the server rejects is named as that, with how to replace it. The browser run shows 10 of 10 connects without a prompt, after a reload.)*
- [X] T121 [US4] Web `src/AkmlSql.Web/Shared/CapabilityNotice.razor` used by the dialog for the PostgreSQL kind (required engine version), and the capability helper in `src/AkmlSql.Web/Services/IEngineBridge.cs` if not already present. *(`CapabilityNotice.razor` already existed from spec 021 as a bridge-driven wrapper. It is extended with optional `Available`, `Feature`, `EngineConnected`, `EngineVersion` and `TestId`; the original usage is unchanged and is now tested. `EngineBridgeCapabilities.Advertises` is the helper.)*
- [X] T122 [US4] Run the US4 tests and E2E scenarios, record SC-002 (20 switches, each under 3 s, logged by the E2E test) and the SC-011 manual prompt review in `specs/042-postgresql-support/baseline.md`, then the baseline comparison. *(Done 2026-10-14. SC-002: twenty switches, the longest 371 ms. SC-018: ten of ten connects without a prompt, after a reload, and the password is in no IndexedDB store, engine log or settings export. The comparison with the pre-feature baseline shows no regression (`baseline/README.md`). Not done: the SC-011 review of real model answers, which needs a provider key and a reviewer, and the elevated check of spike S7 (T114). Results are in `baseline.md`.)*

**Checkpoint**: both kinds coexist in one workspace; history, AI and credentials follow the kind.

---

## Phase 7: User Story 5 — Nothing leaks, nothing pretends (Priority: P2)

**Goal**: SQL Server behaviour identical, no cross-dialect items, unsupported surfaces say so,
SSMS unchanged (FR-090 to FR-093).

**Independent Test**: SC-003 comparison identical; `DialectIsolationTests`; `UnsupportedSurfaceGatingTests`; the SSMS check recorded in `baseline.md`.

### Tests for User Story 5 (write first; they must fail)

- [X] T123 [P] [US5] `tests/AkmlSql.PostgreSql.Tests/Isolation/DialectIsolationTests.cs` (SC-004): no name present in both the reference and the T-SQL dictionaries with a T-SQL-only meaning (allow-list `tests/AkmlSql.PostgreSql.Tests/Isolation/shared-sql-names.json` for names such as `COUNT`); a 200-case PostgreSQL probe finds no `GO`, `@`, `#`, `[`, `sp_`, `xp_` or `NOCOUNT` item; a 200-case SQL Server probe finds no PostgreSQL-only item; the T-SQL dictionary snapshot from T039 is unchanged; the class carries `[Trait("Category", "Isolation")]`. *(Two files: the dictionary and PostgreSQL probe tests are `DialectIsolationTests` in the PostgreSQL library tests; the reverse probe over the SQL Server corpus is `tests/AkmlSql.Engine.Tests/Completion/SqlServerNoPostgresItemsTests.cs`, because that project owns the corpus and its fake cache. The allow-list is grouped, with a reason for each group and a note for each name whose meaning differs. Numbers are in `baseline.md`.)*
- [X] T124 [P] [US5] bUnit `tests/AkmlSql.Web.Tests/Shell/UnsupportedSurfaceGatingTests.cs`: on a PostgreSQL session the Refactor menu, the palette entries and Find invalid objects are disabled with `unavailable-postgresql` tooltips; shortcuts show the status message; fakes prove the in-browser formatter, analyser, offline scanner and `SplitBatches` were never called (extends T021). *(The Editor page is too heavy to render in a component test, so the policy is `UnsupportedSurfaces` / `SurfaceGate` and is tested directly; the page wiring is proven in the browser by `On_PostgreSQL_the_refactorings_are_off_and_say_why_in_the_toolbar_and_the_palette`. The in-browser formatter, analyser, offline scanner and splitter staying out of a PostgreSQL session remain proven by `DialectGateTests`. The web edition has no Find invalid objects entry, so that surface is a guard for the day it gets one.)*
- [X] T125 [P] [US5] `tests/AkmlSql.Engine.Tests/Handlers/DialectDispatchTests.cs` extension: every handler in the unsupported list replies `dialect.unsupported` on PostgreSql and the T-SQL implementation is not invoked; with `postgreSql.enabled = false` no PostgreSQL handler is registered (`EngineHandlerRegistry` test). *(`DialectGuardTests` already prove every guarded surface answers `dialect.unsupported` and that the handler is not invoked; added `PostgresFlagRegistrationTests` for the flag: with `postgreSql.enabled` false neither 221 nor 223 is registered and nothing else is removed.)*
- [X] T126 [P] [US5] `tests/AkmlSql.Engine.Tests/PostgreSql/NoTsqlOnPostgresSessionTests.cs`: a PostgreSQL session running completion, format, analysis and execute leaves no T-SQL parser log line (SC-010 evidence through the Serilog test sink). *(Evidence is a process-wide count of calls into the T-SQL parser rather than a log line, because the parser does not log. It found two leaks, fixed here: analysis and formatting of a PostgreSQL session reached the T-SQL engines. See `baseline.md`.)*

### Implementation for User Story 5

- [X] T127 [US5] Web `src/AkmlSql.Web/Pages/Editor.razor`, `src/AkmlSql.Web/Shared/CommandPalette.razor`, `src/AkmlSql.Web/Services/WorkspaceViewActions.cs` and `ICommandRegistry.cs`: disable refactoring and Find invalid objects for PostgreSQL with the tooltip, and show the status message for shortcuts through `IWorkspaceStatus`. *(`UnsupportedSurfaces` and `SurfaceGate` in `Services/UnsupportedSurfaces.cs`; the Refactor button, the palette entry (`CommandAction.DisabledReason` shows the tooltip) and the page key handler. A shortcut answers with a notification, not `IWorkspaceStatus`, which is the execution status and has no free-text channel. Refactor has no shortcut today, so the handler path is for the day it gets one.)*
- [ ] T128 [US5] Web `src/AkmlSql.Web/Services/DialectGate.cs` final form: PostgreSQL requests go to the engine where a route exists, otherwise "Not available for PostgreSQL"; remove the placeholder outcomes left by T036. *(Deferred: completion, hover and signature help already route to the engine, but formatting and analysis do not until stories 7 and 8, and their placeholder outcomes are still the right ones. Finish this task when T154 and the analysis routing land.)*
- [ ] T129 [US5] Manual SSMS check: start SSMS 22 against the same engine build, exercise completion, format, analysis and history, and record "no new UI, prompt or behaviour" plus the `tests/AkmlSql.Shell.Shared.Tests` result in `specs/042-postgresql-support/baseline.md`. *(Not done: it needs a person to start SSMS 22 and look. Structural evidence is in `baseline.md`: no file of the SSMS projects changed in any commit of this spec.)*
- [X] T130 [US5] Full-gate comparison: run every suite and store `scripts/compare-test-results.ps1 -Before specs/042-postgresql-support/baseline/pre-feature -After <run>` output as `specs/042-postgresql-support/baseline/after-us5/compare.txt` (SC-003). *(Done 2026-10-14: no test that passed before stopped passing; see `baseline/README.md`.)*
- [X] T131 [US5] Confirm no new hex / rgb colour or theme token was introduced (`scripts/generate-theme-css.ps1 -CheckOnly`, `WebCssTokenTests`) and record it in `specs/042-postgresql-support/baseline.md`. *(Done 2026-10-14: the three themes check clean and no colour was added; see `baseline.md`.)*

**Checkpoint**: US1–US5 green; isolation proven by automated gates.

---

## Phase 8: User Story 6 — Edit PostgreSQL results in place (Priority: P2)

**Goal**: single-table results with their primary key are editable; writes are keyed and
all-or-nothing; values round-trip as PostgreSQL text (FR-130 to FR-134).

**Independent Test**: quickstart.md §2 US6; `PgApplyRunnerTests` (live, SC-014); `PostgresWorkspaceTests` US6 scenarios.

### Spike and tests for User Story 6 (tests first; they must fail; the spike is recorded, not passed)

- [ ] T132 [P] [US6] Spike S6 (record): `tests/AkmlSql.Engine.Tests/PostgreSql/PgColumnSchemaSpikeTests.cs` (live) calls `GetColumnSchema()` with and without `CommandBehavior.KeyInfo` and records when `BaseTableName`, `BaseSchemaName`, `IsKey`, `IsIdentity` and `TableOID` are populated → R11 in `specs/042-postgresql-support/research.md`.
- [ ] T133 [P] [US6] `tests/AkmlSql.Engine.Tests/PostgreSql/PgResultSetProvenanceTests.cs` (live): editable for a single-table SELECT with the PK; read-only with reasons for a JOIN, an aggregate, a view and a table without a PK; `LIMIT` / `ORDER BY` still editable; `ColumnProvenanceDto`, `BaseSchema` and `BaseTable` filled.
- [ ] T134 [P] [US6] `tests/AkmlSql.Engine.Tests/PostgreSql/PgCrudWriteGeneratorTests.cs`: `UPDATE "s"."t" SET "c" = $1::type WHERE "k" = $2::type`, `INSERT … RETURNING`, `DELETE`, quoting only when needed, composite keys, NULL parameters.
- [ ] T135 [P] [US6] `tests/AkmlSql.Engine.Tests/PostgreSql/PgApplyRunnerTests.cs` (live): 30 edits, inserts and deletes across 10 column types land (SC-014, table checksum before and after); a row deleted elsewhere → nothing applied and the row named; NOT NULL refusal; invalid text → the server's message; identity returned on insert; all-or-nothing on a mid-batch failure.
- [ ] T136 [P] [US6] bUnit `tests/AkmlSql.Web.Tests/Grid/ResultSetGridPgApplyTests.cs`: `CrudCellDto.TypeName` filled from `ColumnSqlTypes`; the read-only reason texts; all-or-nothing result messages ("not applied: edit N failed"); Set to NULL and invalid-cell behaviour unchanged.
- [ ] T137 [P] [US6] E2E US6 scenarios appended to `tests/AkmlSql.Web.E2E.Tests/PostgresWorkspaceTests.cs` (quickstart US6 steps 1–3).

### Implementation for User Story 6

- [ ] T138 [US6] `src/AkmlSql.Engine/PostgreSql/PgResultSetReader.cs`: provenance from `GetColumnSchema()` (`BaseSchemaName`, `BaseTableName`, `IsKey`, `IsIdentity`, `TableOID` + the `pg_index` verification query when ambiguous), the `IsEditable` rule (one table, all PK columns present, no expressions), the read-only reason in the existing message channel; adjust per S6's record.
- [ ] T139 [P] [US6] `src/AkmlSql.Engine/PostgreSql/PgCrudWriteGenerator.cs` (quoting via `PgIdentifierQuoting`, `$n::type` casts from `CrudCellDto.TypeName`, `RETURNING` for identity).
- [ ] T140 [US6] `src/AkmlSql.Engine/PostgreSql/PgApplyRunner.cs` (one transaction per Apply, affected-rows = 1 check, rollback on any failure, `CrudEditResult` per edit naming the failing row) and the dispatch in `src/AkmlSql.Engine/Execution/ApplyChangesHandler.cs`.
- [ ] T141 [US6] Web `src/AkmlSql.Web/Shared/ResultSetGrid.razor` (`BuildApplyRequest` sets `TypeName`; read-only reason text; failure rendering) and `src/AkmlSql.Web/Services/QueryRefreshSafety.cs` (the PostgreSQL post-Apply refresh re-reads the affected rows with one read-only SELECT, never the script).
- [ ] T142 [US6] Run the US6 tests, the E2E scenarios and the baseline comparison.

**Checkpoint**: PostgreSQL results edit like SQL Server results, with all-or-nothing Apply.

---

## Phase 9: User Story 7 — Format PostgreSQL scripts (Priority: P2)

**Goal**: Format SQL with the active style, meaning preserved, idempotent, quoted identifiers and
dollar bodies verbatim, style options marked, Parse without the server (FR-140 to FR-147).

**Independent Test**: `PgFormatCorpusTests` (SC-015); `PgParseRunnerTests` (SC-017); quickstart.md §2 US7; `PostgresWorkspaceTests` US7 scenarios.

### Spike and tests for User Story 7 (write first; they must fail)

- [ ] T143 [US7] Spike S2 (record): `tests/AkmlSql.PostgreSql.Tests/Formatting/PgTokenAnchorsSpikeTests.cs` builds a token-anchored tree for SELECT, INSERT … ON CONFLICT, UPDATE … RETURNING, DELETE, WITH and window functions from node `location` plus scan tokens; record which clauses lack locations and the chosen branch per clause (AST-anchored or token-stream fallback) in R2 of `specs/042-postgresql-support/research.md`.
- [ ] T144 [P] [US7] `tests/pg-format-corpus/corpus/*.sql` (≥ 100 scripts: PostgreSQL manual examples, Northwind queries, DDL, functions with dollar bodies, regression cases) and `tests/AkmlSql.PostgreSql.Tests/Formatting/PgFormatCorpusTests.cs`: for every script and every built-in style the output equals `tests/pg-format-corpus/golden/<name>__<style>.sql`, is parse-equivalent (`TreesEquivalent`), is idempotent, and keeps quoted identifiers and dollar bodies verbatim (SC-015); `AKML_UPDATE_PG_GOLDENS=1` regenerates; the class carries `[Trait("Category", "FormatCorpus")]`.
- [ ] T145 [P] [US7] `tests/AkmlSql.PostgreSql.Tests/Formatting/PgStyleApplicabilityTests.cs` (all 38 options classified; no-effect options never change output), `PgFormatterSafetyTests.cs` (syntax error → unchanged plus message; equivalence failure → unchanged), `PgCasingTests.cs` (keywords per style; unquoted identifiers only; quoted never touched).
- [ ] T146 [P] [US7] bUnit `tests/AkmlSql.Web.Tests/Styles/StylesPagePgTests.cs` ("no effect for PostgreSQL" marks from the schema JSON; the PostgreSQL preview sample) and `tests/AkmlSql.Web.Tests/Services/FormatterServicePgRoutingTests.cs` (Format, Format actions and Parse go to the engine for PostgreSQL).
- [ ] T147 [P] [US7] E2E US7 scenarios appended to `tests/AkmlSql.Web.E2E.Tests/PostgresWorkspaceTests.cs` (quickstart US7 steps 1–4; SC-017 confirmed through the engine log).

### Implementation for User Story 7

- [ ] T148 [US7] `src/AkmlSql.PostgreSql/Formatting/PgTokenAnchors.cs` (node → token span), `PgLayoutTree.cs` and `PgCommentAttacher.cs` per S2's record.
- [ ] T149 [US7] `src/AkmlSql.PostgreSql/Formatting/PgStyleApplicability.cs`: the 38 `SqlPromptOptionCatalog` options mapped to applies / applies with PostgreSQL meaning / no effect (table in a doc comment), consumed by the formatter and exported to the style editor schema.
- [ ] T150 [US7] `src/AkmlSql.PostgreSql/Formatting/PgFormatter.cs`: layout for SELECT lists, FROM / JOIN, WHERE / AND / OR, GROUP BY / HAVING / ORDER BY / LIMIT / OFFSET, WITH, INSERT / VALUES / ON CONFLICT / RETURNING, UPDATE / SET, DELETE, CASE, subqueries, window functions, CREATE TABLE / INDEX / VIEW, a token-stream fallback for other statements; casing rules; comments; dollar bodies verbatim.
- [ ] T151 [US7] `src/AkmlSql.PostgreSql/Formatting/PgFormatPipeline.cs`: parse → layout → equivalence check (`StripLocations` compare) → idempotency pass → `FormatResult` with the diagnostic on failure (FR-141, FR-142, FR-144).
- [ ] T152 [US7] Engine dispatch by session kind in `src/AkmlSql.Engine/Handlers/Formatting/FormattingHandlers.cs` (`FormatDocumentHandler`, `FormatSelectionHandler`, `FormatPreviewHandler`, `FormatActionHandler` → expand wildcards / qualify names via `PgWildcardExpander`) and `src/AkmlSql.Engine/Formatter/FormatRequestHandler.cs`.
- [ ] T153 [US7] Style editor marks: `NoEffectForPostgreSql` per option in `src/AkmlSql.Formatting/SqlPrompt/SqlPromptOptionCatalog.cs` (from T149's table), emitted in `SchemaJson` by `StyleEditorSchemaHandler` in `src/AkmlSql.Engine/Handlers/Formatting/FormattingHandlers.cs` with the `SchemaVersion` bump recorded in `contracts/ipc.md`, and a PostgreSQL sample in `src/AkmlSql.Formatting/SqlPrompt/SqlPromptPreviewSamples.cs`.
- [ ] T154 [US7] Web `src/AkmlSql.Web/Pages/Styles.razor` and `src/AkmlSql.Web/Services/IProfileStore.cs`: show the marks and the PostgreSQL preview while the active connection is PostgreSQL; `src/AkmlSql.Web/Services/IFormatterService.cs`: engine route for PostgreSQL (replace the `DialectGate` placeholder).
- [ ] T155 [US7] Generate the goldens (`AKML_UPDATE_PG_GOLDENS=1`), review 20 of them by eye against the style definitions, and keep `tests/pg-format-corpus/golden/` as the committed ratchet.
- [ ] T156 [US7] Run the format corpus, the US7 unit tests, the E2E scenarios, the T-SQL format-parity goldens (unchanged) and the baseline comparison.

**Checkpoint**: formatting and Parse work for PostgreSQL; the T-SQL goldens are untouched.

---

## Phase 10: User Story 8 — Code analysis for PostgreSQL (Priority: P3)

**Goal**: a PostgreSQL rule set (52 planned, floor 40) with findings, suppressions, severities
and the rules window grouped by kind (FR-150 to FR-155).

**Independent Test**: `PgRuleRegistryTests`, per-rule tests, the corpus sweep (SC-016); quickstart.md §2 US8; `PostgresWorkspaceTests` US8 scenarios.

### Tests for User Story 8 (write first; they must fail)

- [ ] T157 [P] [US8] `tests/AkmlSql.PostgreSql.Tests/Rules/PgRuleRegistryTests.cs`: ≥ 40 rules; ids unique with the `PG` prefix and a category code; every rule has `ReferenceUrl`, `Description` and a default severity; `Kind = PostgreSql` in `AnalysisRuleInfoDto`; the class carries `[Trait("Category", "Rules")]`.
- [ ] T158 [P] [US8] `tests/AkmlSql.PostgreSql.Tests/Rules/PgAnalysisEngineTests.cs`: a syntax error → one finding and no rule findings; `-- akml-disable-line`, `-- akml-disable` … `-- akml-enable`, session and "everywhere" suppressions; severity overrides from `ResolvedAnalysisSettings`; "code analysis off" → nothing runs; SQL Server rules never run on PostgreSQL text and vice versa.
- [ ] T159 [P] [US8] Per-rule tests `tests/AkmlSql.PostgreSql.Tests/Rules/<Category>/<RuleId>Tests.cs` (one positive and one negative script each) for all 52 ids in `contracts/rules-catalog.md`, and `tests/AkmlSql.PostgreSql.Tests/Rules/PgRulesCorpusSweepTests.cs` running every rule over `tests/pg-format-corpus/corpus/*.sql` (0 crashes, 0 SQL Server findings; SC-016); all rule test classes carry `[Trait("Category", "Rules")]`.
- [ ] T160 [P] [US8] `tests/AkmlSql.Engine.Tests/Handlers/ListAnalysisRulesHandlerPgTests.cs` (both kinds listed with `Kind`), bUnit `tests/AkmlSql.Web.Tests/Analysis/CodeAnalysisSectionKindTests.cs` (grouped by kind) and `tests/AkmlSql.Web.Tests/Services/AnalyserServicePgRoutingTests.cs` (engine route; analysis off → no request).
- [ ] T161 [P] [US8] E2E US8 scenarios appended to `tests/AkmlSql.Web.E2E.Tests/PostgresWorkspaceTests.cs` (quickstart US8 steps 1–2).

### Implementation for User Story 8

- [ ] T162 [US8] `src/AkmlSql.PostgreSql/Analysis/IPgAnalysisRule.cs`, `PgAnalysisContext.cs` (tree, tokens, text, schema cache, settings, suppressions, cancellation), `PgRuleRegistry.cs` (reflection over the library assembly), `PgAnalysisEngine.cs` (parse; suppression map from token comments through the existing `SuppressionMap` builder; settings; severity; `AnalysisDiagnostic` output) and `PgRuleBase.cs` (node walkers, span resolution).
- [ ] T163 [P] [US8] Performance rules PGPE001–PGPE010 in `src/AkmlSql.PostgreSql/Analysis/Rules/Performance/`.
- [ ] T164 [P] [US8] Best-practice rules PGBP001–PGBP010 in `src/AkmlSql.PostgreSql/Analysis/Rules/BestPractices/`.
- [ ] T165 [P] [US8] Security rules PGSE001–PGSE005 in `src/AkmlSql.PostgreSql/Analysis/Rules/Security/` (PGSE001 uses `ParsePlpgsql` for `EXECUTE` bodies).
- [ ] T166 [P] [US8] Style rules PGST001–PGST005 in `src/AkmlSql.PostgreSql/Analysis/Rules/Style/`.
- [ ] T167 [P] [US8] Design rules PGDE001–PGDE008 in `src/AkmlSql.PostgreSql/Analysis/Rules/Design/` (PGDE006 schema-aware through the cache's foreign keys and indexes).
- [ ] T168 [P] [US8] Deprecated rules PGDEP001–PGDEP004 in `src/AkmlSql.PostgreSql/Analysis/Rules/Deprecated/`.
- [ ] T169 [P] [US8] Execution rules PGEX001–PGEX005 in `src/AkmlSql.PostgreSql/Analysis/Rules/Execution/` (PGEX001 uses `PgTransactionTracker` over the script).
- [ ] T170 [P] [US8] Naming rules PGNM001–PGNM005 in `src/AkmlSql.PostgreSql/Analysis/Rules/Naming/` (PGNM001 measures UTF-8 bytes).
- [ ] T171 [US8] Engine dispatch in `src/AkmlSql.Engine/Handlers/Analysis/AnalysisHandlers.cs` (`AnalysisHandler` by kind), `ListAnalysisRulesHandler.cs` (both registries, `Kind`) and `SessionSuppressionHandler.cs` (accepts `PG` ids).
- [ ] T172 [US8] Web `src/AkmlSql.Web/Services/IAnalyserService.cs` (engine route for PostgreSQL; off → no request) and `src/AkmlSql.Web/Shared/Settings/CodeAnalysisSection.razor` (rules grouped by kind); `doc/analysis-rules.md` gains the PostgreSQL section and `tests/AkmlSql.PostgreSql.Tests/Rules/PgRulesDocTests.cs` asserts the doc lists every rule.
- [ ] T173 [US8] Run the US8 tests, the corpus sweep, the E2E scenarios, the T-SQL analysis tests (unchanged) and the baseline comparison.

**Checkpoint**: all eight stories complete.

---

## Phase 11: Polish & cross-cutting concerns

- [ ] T174 [P] `doc/ipc-api.md`: the appended keys, messages 221–224, the `dialect.*` error codes, the capability and the kind-dispatch note.
- [ ] T175 [P] `doc/configuration.md` (`postgreSql.enabled`, the `pg://` credential keys), `doc/architecture.md` (dialect dispatch, `PgServices`, the one-command-per-statement rationale) and `doc/deployment.md` (the native parser library in the engine publish; `AKML_PG_TEST_CONNECTION`).
- [ ] T176 [P] `CLAUDE.md`: project-structure entries for `src/AkmlSql.PostgreSql`, `src/AkmlSql.Engine/PostgreSql/`, `tools/pg-reference`, `tests/AkmlSql.PostgreSql.Tests`, `tests/pg-completion-corpus`, `tests/pg-format-corpus`; the Technologies line (Npgsql 10, pgsqlparser over libpg_query 18); two Build Gotchas (the engine ships a native parser library, single-file stays off; PostgreSQL scripts run one command per statement because the extended protocol's implicit transaction would roll back earlier statements).
- [ ] T177 [P] `doc/progress.md`: the spec 042 section (per-phase tables, spike outcomes S1–S7, deferred list).
- [ ] T178 [P] Review `THIRD-PARTY-NOTICES.txt` against the licence files actually shipped by the packages in the NuGet cache (`pgsqlparser`, `Npgsql`, `Google.Protobuf`).
- [ ] T179 Quickstart.md §2 full manual walk-through on a fresh Debug build; record every scenario and success criterion (hallway SC-001 and SC-012 with the five testers listed) in `specs/042-postgresql-support/baseline.md`.
- [ ] T180 Performance check: SC-008 timings, formatting a 2,000-line PostgreSQL script under 500 ms, `tests/AkmlSql.Engine.Tests/PerformanceBaselineTests.cs` unaffected; record the numbers in `specs/042-postgresql-support/baseline.md`.
- [ ] T181 Installer: build `src/AkmlSql.Installer/AkmlSqlSetup.iss`, confirm the engine payload contains the native parser library and `THIRD-PARTY-NOTICES.txt`, and run only the installer unit classes (`ScheduledUpdateTests`, `UpdateDownloaderTests`).
- [ ] T182 Final full-gate comparison stored as `specs/042-postgresql-support/baseline/final/compare.txt`; update the notes in `specs/042-postgresql-support/checklists/requirements.md` with the outcome; deliver uncommitted and ask "Ready to commit?".

---

## Deferred (recorded with reasons; constitution Development Workflow)

- **PostgreSQL 19**: GA expected October 2026; the reference and the parser stay pinned to 18 (R16). Follow-up: regenerate the reference with `REL_19_STABLE`, move to the libpg_query 19 branch when `pgsqlparser` ships it.
- **Refactorings and Find invalid objects for PostgreSQL**: out of scope per the clarification session; they return `dialect.unsupported` (T031) and are disabled in the web (T127).
- **Offline PostgreSQL dialect**: connected-only was the user's choice (Q3); the reference lives in the engine.
- **Browser-side password storage**: rejected (Q2); remembered passwords live on the engine machine.
- **Formatting the inside of PL/pgSQL bodies**: preserved verbatim (FR-141); a PL/pgSQL layout would need `ParsePlpgsql`-driven rules.
- **Descriptions in Phase A**: `obj_description` per row may miss the Phase A budget on large catalogs; `contracts/catalog-queries.md` names the Phase B fallback. Decide from T088's numbers.
- **pgAdmin client**: a later spec; FR-120 keeps every PostgreSQL capability behind the engine protocol.

---

## Dependencies & Execution Order

### Phase dependencies

- **Setup (Phase 1)**: starts immediately; T001 before any `src/` change; T011 (S1) before T038 is relied on.
- **Foundational (Phase 2)**: after Setup; blocks every story. T040's identical comparison is the exit gate.
- **US1 (Phase 3)**: after Foundational. The MVP.
- **US2 (Phase 4)**: after Foundational; independent of US1 except T056's provider hook (an empty provider is acceptable until T079).
- **US3 (Phase 5)**: after Foundational; the reference tasks T090–T094 block T100–T102; US3 uses US2's cache when present but its corpus gate runs on a fake cache.
- **US4 (Phase 6)**: after US1 (connection switch and remembered passwords build on T063/T064) and after T032 (history kind).
- **US5 (Phase 7)**: after US1–US4 (it verifies them); its gates are the exit criteria for the P1/P2 UI stories.
- **US6 (Phase 8)**: after US1 only (reader and runner).
- **US7 (Phase 9)**: after Foundational and the S2 spike (T143); layout tasks are contingent on S2's record.
- **US8 (Phase 10)**: after US7 (rules walk the same trees and spans; the corpus sweep uses the format corpus).
- **Polish (Phase 11)**: after the stories you ship.

### Cross-story touch points (soft dependencies)

- `ConnectionManagerModal.razor` is touched by T064 (US1) and T120 (US4): finish T064 first.
- `ConnectionChangedHandler.cs` is touched by T056 (US1), T083 (US2) and T120 (US4): sequence them.
- `DialectGate.cs` is created in T036, replaced piecewise by T107 (completion), T154 (format), T172 (analysis) and finalised in T128.
- `PgResultSetReader.cs` is created in T057 (US1) and extended in T138 (US6).
- `PostgresWorkspaceTests.cs` grows per story (T051, T078, T098, T113, T137, T147, T161): append, never reorder.
- `research.md` receives spike records from T011, T071, T114, T132, T143.

### Within each story

- Tests first; they must fail before the implementation task runs.
- Engine before web for every surface (the web routes to the engine).
- Every story ends with its gate run plus the baseline comparison.

### Parallel opportunities

- Across stories: US6 and US7 need only Foundational and US1, so they may start before US5
  closes; US5 runs whenever US4 lands. Only US8 waits for US7.
- Phase 1: T005–T010 and T012 in parallel after T002–T004.
- Phase 2: T013–T023 (tests) in parallel; T025, T026, T033–T039 in parallel once T024 is done.
- US1: T041–T051 in parallel; T053, T054, T059, T061, T062, T065, T066 in parallel with the runner work.
- US2: T073–T078 in parallel; T080, T081, T087 in parallel with T079.
- US3: T090 and T092 with T095–T098; T102–T105 in parallel after T101.
- US4: T110–T114 in parallel; T119 in parallel with T115–T118.
- US6: T132–T137 in parallel; T139 in parallel with T138.
- US7: T144–T147 in parallel after T143.
- US8: T157–T161 in parallel; T163–T170 (eight rule categories) in parallel after T162.
- Polish: T174–T178 in parallel.

---

## Parallel Example: User Story 1

```bash
# Tests first (different files):
Task: "PgTargetGuardTests in tests/AkmlSql.Engine.Tests/PostgreSql/PgTargetGuardTests.cs"
Task: "PgCredentialResolverTests in tests/AkmlSql.Engine.Tests/PostgreSql/PgCredentialResolverTests.cs"
Task: "PgExecuteRunnerTests (live) in tests/AkmlSql.Engine.Tests/PostgreSql/PgExecuteRunnerTests.cs"
Task: "ConnectionManagerModalKindTests in tests/AkmlSql.Web.Tests/Shell/ConnectionManagerModalKindTests.cs"
Task: "StatusBarKindTests in tests/AkmlSql.Web.Tests/Shell/StatusBarKindTests.cs"

# Then the independent engine pieces:
Task: "PgTargetGuard in src/AkmlSql.Engine/PostgreSql/PgTargetGuard.cs"
Task: "PgCredentialResolver + CredentialHandler in src/AkmlSql.Engine/PostgreSql/"
Task: "PgParseRunner in src/AkmlSql.Engine/PostgreSql/PgParseRunner.cs"
Task: "SqlMessageText.ForPostgres in src/AkmlSql.Core/Text/SqlMessageText.cs"
```

## Parallel Example: User Story 8

```bash
# After PgAnalysisEngine (T162), the eight categories in parallel:
Task: "Performance rules PGPE001–PGPE010 in src/AkmlSql.PostgreSql/Analysis/Rules/Performance/"
Task: "Best-practice rules PGBP001–PGBP010 in src/AkmlSql.PostgreSql/Analysis/Rules/BestPractices/"
Task: "Security rules PGSE001–PGSE005 in src/AkmlSql.PostgreSql/Analysis/Rules/Security/"
Task: "Naming rules PGNM001–PGNM005 in src/AkmlSql.PostgreSql/Analysis/Rules/Naming/"
```

---

## Implementation Strategy

### MVP first (User Story 1 only)

1. Phase 1 (baseline, projects, S1) and Phase 2 (keys, kind, dispatch, gate, history, language layer).
2. Phase 3: connect, run, messages, errors, transaction state, status bar.
3. **STOP and VALIDATE**: quickstart US1 on the Northwind port; baseline comparison identical.
4. Demo: a PostgreSQL script runs in the web edition with PostgreSQL's own messages.

### Incremental delivery

1. US2 (browse) and US3 (write with help, reference) complete the P1 set.
2. US4 (switch, history, AI, credentials) then US5 (isolation gates) close the P2 UI work.
3. US6 (edit), US7 (format and Parse), US8 (rules) each ship as their own increment; each ends
   with its ratchet (SC-014, SC-015, SC-016) and the baseline comparison.
4. Polish: docs, CLAUDE.md, third-party notices, installer check, final comparison.

### Notes

- A task is done when its tests pass and the existing suites are still identical to the baseline.
- Never "fix" a spike; record it and adjust the design task that follows.
- Stop at any checkpoint; every story is independently demonstrable.
