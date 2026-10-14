# Implementation Plan: PostgreSQL support in the engine and the web edition

**Branch**: `042-postgresql-support` | **Date**: 2026-10-14 | **Spec**: [spec.md](./spec.md)
**Input**: Feature specification from `/specs/042-postgresql-support/spec.md`; plan-phase codebase
map in [codebase-map.md](./codebase-map.md)

## Summary

Add PostgreSQL as a second database kind in the engine and the web edition: connect and switch
between SQL Server and PostgreSQL saved connections, run scripts as PostgreSQL runs them (one
statement at a time, autocommit, stop at the first error, notices and SQLSTATE errors with
positions, transaction state with one-click rollback), browse schemas through the existing cache
model, and get highlighting, completion, quick info and signature help from a reference derived
from the official PostgreSQL 18 documentation. The user chose the full tier: editable results,
Format SQL with the existing styles, Parse, and a PostgreSQL code analysis rule set (52 rules
planned, floor 40). Refactorings stay out. The two dialects are isolated: SQL Server paths are
byte-for-byte unchanged and the existing suites are the regression gate; SSMS, which shares the
engine, sees no change.

**Approach** ([research.md](./research.md) R1–R27): the product understands PostgreSQL syntax
through the real PostgreSQL parser (`pgsqlparser` 2.1.0 over libpg_query 18.1.0), engine-only;
Npgsql 10 for the data path; everything PostgreSQL runs in the engine and the web routes to it
(PostgreSQL mode is connected-only, so an engine is always paired). One new library
`AkmlSql.PostgreSql` (language, reference, completion, formatting, analysis, snippets) plus a
`PostgreSql/` folder in the engine (connections, catalog, execution, CRUD, credentials). The
engine dispatches by the session's kind; IPC evolves append-only; a capability string and a
config flag make the feature switchable as one unit.

**Spec amendment made at plan time**: FR-110 (settings export) was reworded; the export
envelope never includes saved connections (spec 041 design), so the kind travels with the
saved-connection record and export/import are unchanged (R22).

## Technical Context

**Language/Version**: C# 14 / .NET 10 (engine, new library, tests); Blazor WebAssembly .NET 10
(web); JavaScript (CodeMirror 6 bundle, `akml-editor.js`)
**Primary Dependencies**: Npgsql 10.0.3 (engine only); pgsqlparser 2.1.0 (libpg_query 18.1.0,
PostgreSQL 18.6 parser; MIT; Google.Protobuf ≥ 3.36.2; engine only); existing MessagePack 3.1.8,
Microsoft.Data.Sqlite, Microsoft.SqlServer.TransactSql.ScriptDom (unchanged); CodeMirror
`@codemirror/lang-sql` PostgreSQL dialect (already vendored)
**Storage**: SQLite history (schema v3 → v4: `kind` column); IndexedDB `savedSqlConnections`
(schemaless, new fields default on read); DPAPI `sql-credentials.json` (reused with `pg://`
keys); `config.json` (`postgreSql.enabled`); embedded `postgresql-reference.json`
**Testing**: xunit 2.x (new `tests/AkmlSql.PostgreSql.Tests`), engine handler tests with mocked
connections, live tests behind `WithPgOrSkipAsync`, Playwright browser tests
(`PostgresWorkspaceTests`), corpus gates (completion, format goldens, rules)
**Target Platform**: Windows x64 engine (self-contained, native library allowed; single-file and
trimming stay off); browser (no PostgreSQL code in the WASM bundle)
**Project Type**: desktop-hosted engine + web application (existing structure)
**Performance Goals**: Phase A object names within 2 s and details within 10 s for 1,000 objects
(SC-008); connection switch reflected within 3 s (SC-002); parse of a 2,000-line script in
single-digit ms; formatter within the existing 500 ms budget
**Constraints**: SQL Server behaviour unchanged with identical gate results (FR-091, SC-003);
SSMS untouched; IPC append-only; no password at rest in the browser; no new theme colour (kind
shown by label and icon); 10 MB document limit; 16 MB frame limit
**Scale/Scope**: one new library, one engine folder, one reference generator tool, ~16 IPC key
additions + 2 new message pairs, 52 rules, 3 corpora, 8 user stories

## Constitution Check

*GATE: passed before Phase 0; re-checked after Phase 1 (see end of file).*

| Principle | Status | Evidence |
| --- | --- | --- |
| I. Process isolation & host safety | PASS | All PostgreSQL parsing, formatting, analysis, schema, execution and AI redaction run in the engine (R18); the shell projects are untouched; the web's in-browser T-SQL services are kind-gated so they never receive PostgreSQL text (`IFormatterService`, `IAnalyserService`, `OfflineSqlScanner` behind quick info and signature help, `IRefactoringService`, the in-browser `SplitBatches` in `IQueryExecutionService`, built-in snippets, `AiChatPanel`'s prompt, `LiteralRedactor`). |
| II. Build integrity | PASS | New projects added to `AKML-SQL.slnx` and built in the one-pass solution build; the engine publish carries the native parser library (spike S1 verifies the published folder); no hand-edited CSS; the kind badge uses label + existing icon sprite, so the theme-token gate is untouched. |
| III. Tests & corpora non-regressible | PASS | `tests/AkmlSql.PostgreSql.Tests` is created with the library; new ratchets: PostgreSQL completion corpus (≥ 300 cases), format corpus (≥ 100 scripts), per-rule positive/negative tests; existing goldens and corpus gates must produce identical results (FR-091, SC-003, `compare-test-results.ps1`). |
| IV. Git consent | PASS | No git mutations in this plan; implementation delivers uncommitted changes and asks. |
| V. Simplicity & convention fidelity | PASS with justification | Reuse first: `SessionConnection` generalised to `DbConnection` + kind (no second registry); `AnalysisDiagnostic`, `ResolvedAnalysisSettings`, `SuppressionMap`, `CodeIssueInfo`, `CompletionItem`, `DatabaseCache` reused; `SqlCredentialStore` reused with a prefixed key; `ExecuteBatchDto` reused one-per-statement so `ExecuteLineMapper` is unchanged; existing message types reused with dispatch by kind. Two additions need justification (Complexity Tracking): the new library and the `ISchemaMetadataProvider` seam. |

**Additional constraints**: paths over IPC absolute (`Root Certificate` path validated with
`Path.GetFullPath`); secrets DPAPI-wrapped and never logged (FR-100, FR-103); IPC local-only
unchanged; technology pins unchanged (two packages added, both net10-only, engine-only);
documentation updated in the same change (quickstart §3).

## Project Structure

### Documentation (this feature)

```text
specs/042-postgresql-support/
├── plan.md              # this file
├── spec.md              # feature spec (FR-110 amended at plan time)
├── codebase-map.md      # where the product is tied to SQL Server (plan input)
├── research.md          # Phase 0: decisions R1–R27, spikes S1–S7
├── data-model.md        # Phase 1: entities, migrations, state machine
├── quickstart.md        # Phase 1: acceptance gate
├── contracts/
│   ├── ipc.md               # appended keys, messages 221–224, capability, config, guard
│   ├── reference-schema.md  # reference JSON schema + generator inputs
│   ├── rules-catalog.md     # 52 PostgreSQL rules with ids, severities, doc sections
│   ├── catalog-queries.md   # pg_catalog queries for schema, checksum, databases, CRUD
│   └── web-ui.md            # components, test ids, routing by kind
├── checklists/requirements.md
└── tasks.md             # Phase 2 (/speckit.tasks), not created here
```

### Source Code (repository root)

```text
src/
├── AkmlSql.Core/
│   ├── DatabaseKind.cs                       # enum (SqlServer = 0, PostgreSql = 1)
│   ├── Config/AppSettings.cs                 # + PostgreSqlSettings { Enabled }
│   ├── Config/SqlCredentialStore.cs          # unchanged API; pg:// keys by convention
│   └── Ipc/Messages/                         # appended keys (ipc.md); CredentialMessages.cs, RedactMessages.cs
├── AkmlSql.PostgreSql/                       # NEW library (net10.0): pgsqlparser, Google.Protobuf
│   ├── Language/      PgParser (wrapper + location-stripped tree compare), PgScanner (tokens),
│   │                  PgStatementSplitter, PgLexicalScanner (managed, tolerant), PgTransactionTracker
│   ├── Reference/     postgresql-reference.json (embedded), omissions.json, PgReference (lookup)
│   ├── Completion/    PgCursorContextAnalyzer, PgCompletionEngine, Providers/ (Keyword, Command,
│   │                  Function, Type, Schema, Object, Column, Alias, Cte, JoinOnFk, Snippet),
│   │                  PgQuickInfoService, PgSignatureService, PgIdentifierQuoter, PgWildcardExpander
│   ├── Formatting/    PgFormatter, PgLayoutTree, PgTokenAnchors, PgStyleApplicability, PgCommentAttacher
│   ├── Analysis/      PgAnalysisEngine, PgRuleRegistry, IPgAnalysisRule, PgAnalysisContext,
│   │                  Rules/{Performance,BestPractices,Security,Style,Design,Deprecated,Execution,Naming}/
│   ├── Snippets/      *.akmlsnippet (PostgreSQL built-ins, embedded)
│   └── Safety/        PgDestructiveStatementCheck
├── AkmlSql.Engine/
│   ├── Capabilities.cs                       # + DialectPostgreSqlV1 (conditional on config)
│   ├── Server/SessionManager.cs              # SessionState.Kind, SearchPath, TransactionState
│   ├── Execution/SessionConnection.cs        # DbConnection + Kind
│   ├── Schema/ISchemaMetadataProvider.cs     # seam; SchemaMetadataService implements it unchanged
│   ├── Handlers/…                            # dispatch by kind at the top of each session-scoped handler
│   └── PostgreSql/                           # NEW folder (Npgsql)
│       ├── PgConnectionFactory, PgTargetGuard, PgCredentialResolver, CredentialHandler
│       ├── PgSchemaMetadataService, PgChangeChecksum, PgDatabaseLister
│       ├── PgExecuteRunner (per-statement, notices, positions, tags), PgParseRunner, PgResultSetReader
│       ├── PgCrudWriteGenerator, PgApplyRunner
│       └── RedactHandler
├── AkmlSql.AI/Prompts/                       # SqlDialect parameter on BuildSystemPrompt + builders
└── AkmlSql.Web/
    ├── Services/ISqlConnectionService.cs     # kind-aware connect/test/list; credential Has/Forget
    ├── Services/ISavedSqlConnectionStore.cs  # Kind, Port, SslMode, RootCertificatePath, RememberPassword
    ├── Services/IFormatterService.cs, IAnalyserService.cs, IQueryExecutionService.cs,
    │   IQuickInfoService.cs, ISignatureHelpService.cs, ISnippetStore.cs, IHistoryService.cs,
    │   IAiPromptService.cs, IAiGhostTextService.cs   # route / gate by kind
    ├── Shared/ConnectionManagerModal.razor, ConnectionPickerComponent.razor, StatusBar.razor,
    │   SchemaTreeComponent.razor (Materialized Views), ResultsPaneComponent.razor (txn state),
    │   AiChatPanel.razor, Settings/ConnectionsSection.razor, Settings/DiagnosticsSection.razor
    ├── Pages/History.razor (kind badge/filter/search term), Pages/Editor.razor (setDialect, guards)
    └── wwwroot/js/akml-editor.js             # setDialect(kind) via a language Compartment

tools/pg-reference/AkmlSql.PostgreSql.ReferenceGen/   # NEW console tool (not in the product build)

tests/
├── AkmlSql.PostgreSql.Tests/                 # NEW: Language, Reference, Completion (corpus gate),
│                                             #   Formatting (corpus), Rules (per rule), Isolation, Parse
├── pg-completion-corpus/                     # NEW: p01-*.json … + gate.json (ratchet)
├── pg-format-corpus/                         # NEW: corpus/*.sql, golden/*__<style>.sql
├── AkmlSql.Engine.Tests/PostgreSql/          # handlers with mocked connections; live tests via WithPgOrSkipAsync
├── AkmlSql.Core.Tests/                       # message round-trips for the appended keys
├── AkmlSql.Web.Tests/                        # bUnit: dialog kind switch, status kind, history filter, routing by kind
└── AkmlSql.Web.E2E.Tests/PostgresWorkspaceTests.cs   # browser scenarios (skips without a server)

doc/postgresql-reference.md, doc/ipc-api.md, doc/configuration.md, doc/analysis-rules.md,
doc/progress.md, THIRD-PARTY-NOTICES.txt, CLAUDE.md
```

**Structure Decision**: one new shared library for everything that is pure language and
reference work (testable without a server, reusable by a future pgAdmin client through the
engine), and a folder inside the engine for everything that opens a connection, so the engine
remains the only process that talks to databases (constitution I). The web edition gains routing
and UI only.

## Complexity Tracking

| Violation | Why Needed | Simpler Alternative Rejected Because |
| --- | --- | --- |
| New project `AkmlSql.PostgreSql` (+ its test project) | FR-093 asks for PostgreSQL support switchable as one unit; the parser dependency (native) and the rule set must not leak into `AkmlSql.Analysis` / `AkmlSql.Formatting`, which the web bundle references | Folding into the existing libraries would pull `pgsqlparser` + Google.Protobuf into the browser bundle and put PostgreSQL rules inside the T-SQL registry's reflection scope |
| `ISchemaMetadataProvider` seam over the concrete `SchemaMetadataService` | `ConnectionChangedHandler` and `SchemaCacheManager` must call one of two catalog implementations by kind | Branching inside `SchemaMetadataService` (every method takes `SqlConnection`) would mix `pg_catalog` and `sys.*` code in one class and touch the SQL Server path the gates protect |
| `PgLexicalScanner` beside the native `Scan` (R5) | Tolerant scanning of in-progress text, psql meta-command and `COPY FROM STDIN` detection before the native scanner would reject the text, and literal redaction | The native scanner errors on unterminated input and knows no meta-commands; routing those cases through it would need error recovery it does not offer |

## Phase 0: Research (complete)

[research.md](./research.md): R1 parser (pgsqlparser; alternatives ANTLR, own P/Invoke, own
parser), R2 formatter design and safety check, R3 rules, R4 completion, R5 managed scanner, R6
Npgsql facts, R7 one-command-per-statement and why, R8 timeouts, R9 transaction tracking, R10
text output display, R11 editable results, R12 schema catalog, R13 databases, R14 connections,
guard and credentials (with the honest LocalSystem statement), R15 capability/config, R16
version policy (14–18, reference pinned to 18, 19 follow-up), R17 reference generator, R18 web
routing, R19 engine dispatch, R20 AI, R21 history, R22 web persistence + FR-110 amendment, R23
highlighting, R24 tests, R25 layout, R26 budgets, R27 spikes S1–S7.

## Phase 1: Design (complete)

- [data-model.md](./data-model.md): kind, saved connection, session, remembered password,
  reference, schema snapshot deltas, execution outcome, grid edit, transaction state machine,
  history v4, rule entity, config, capability.
- [contracts/](./contracts/): IPC (appended keys with numbers; messages 221–224), reference
  schema and generator inputs, 52-rule catalogue, catalog queries, web UI contract with test ids.
- [quickstart.md](./quickstart.md): environment, automated gates mapped to SC-001…SC-018, manual
  walk-through per user story, docs to update.
- Agent context: `update-agent-context.ps1 -AgentType claude` run after this plan (see the
  CLAUDE.md diff reported with the plan).

## Phase 2: Task generation approach (for `/speckit.tasks`, not executed here)

Order of work, each slice independently testable and gated:

1. **Foundations and spikes** (S1–S7): package references, `DatabaseKind`, config flag,
   capability, `SessionState.Kind`, `SessionConnection` generalisation, `ISchemaMetadataProvider`
   seam with the SQL Server implementation unchanged, dispatch-by-kind scaffolding returning
   `dialect.unsupported`, IPC key additions with round-trip tests, the reference generator skeleton.
   Gate: every existing suite identical.
2. **US1 Connect and run**: connection string, guard, credential resolver, `TestSqlConnection`
   by kind, per-statement runner with notices / positions / tags / transaction tracking, Parse
   runner, web dialog + picker + status bar + results/messages/txn UI, history kind. Gate: live
   engine tests + browser scenario US1.
3. **US2 Browse**: catalog queries, Phase A/B, checksum, databases + reconnect switch,
   Materialized Views header, inaccessible schemas. Gate: SC-008 live test.
4. **Reference**: generator over the five tags, embedded JSON, `doc/postgresql-reference.md`,
   omissions, coverage test (SC-006).
5. **US3 Write with help**: scanner-based context, providers, quick info, signature help,
   wildcard expansion, snippets, `setDialect`, completion corpus + gate, isolation cross-check
   (SC-004, SC-005).
6. **US4 Switch**: remembered password flow (Has / Forget / inject / save), history page kind,
   AI dialect + redaction, Settings/Diagnostics. Gate: SC-002, SC-009, SC-011, SC-018.
7. **US6 Edit results**: `GetColumnSchema` provenance, editability, CRUD generator, all-or-nothing
   apply, web `TypeName` plumbing. Gate: SC-014.
8. **US7 Format and Parse**: token-anchored layout (contingent on S2), style applicability,
   safety check, idempotency, Format Styles page marks, format corpus + goldens (SC-015, SC-017).
9. **US8 Analysis**: engine, registry, 52 rules with tests, rules window by kind, suppressions
   (SC-016).
10. **US5 Isolation and close-out**: unsupported-surface gating in web and engine, full gate
    comparison, docs, third-party notices, progress log.

## Post-design Constitution Check

Re-evaluated after Phase 1: unchanged, PASS on I–IV; V passes with the two justified items
above. No new violations were introduced by the contracts: no new theme colours, no new storage
mechanism (SQLite column, IndexedDB fields, existing credential store), no new transport, no new
shell code.

## Risks carried into tasks

- S2 (token-anchored layout) decides the formatter's shape; US7 tasks are written as "after S2".
- The web engine service runs as LocalSystem; the consent text is explicit (R14).
- PostgreSQL 19 GA may land during implementation; the reference stays pinned to 18 and a
  follow-up is logged in `doc/progress.md`.
- The reference generator must drive `unresolved` omissions to zero or justify them; SC-006 is
  measured against the generator's own source counts.
