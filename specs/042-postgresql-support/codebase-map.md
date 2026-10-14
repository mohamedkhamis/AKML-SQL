# Codebase map: where AKML SQL is tied to SQL Server (plan-phase input)

Captured 2026-10-14 while writing the spec. This is implementation context for `/speckit.plan`;
the spec itself stays implementation-free. Paths are repo-relative.

**Headline**: there is no dialect / provider / database-kind abstraction anywhere in `src/` or
`tests/`. The only PostgreSQL mention is the AI prompt line "not MySQL, PostgreSQL"
(`src/AkmlSql.AI/Prompts/PromptTemplates.cs:28`). The SSMS extension and the web edition share the
same engine process (SSMS over the named pipe, web over the WebSocket bridge), so every engine
change is on the SSMS path too. The web app also runs language code in the browser: it references
`AkmlSql.AI`, `Analysis`, `Formatting` and `IntelliSense` directly.

## Connections

- `src/AkmlSql.Core/Ipc/Messages/ConnectionInfo.cs`: `SessionId`, raw `ConnectionString`,
  `ServerVersion` (13-17), `EngineEdition`, `DatabaseName`. No auth-mode or provider field.
- `src/AkmlSql.Engine/Server/SessionManager.cs` (`SessionState`) mirrors those fields per session.
- `src/AkmlSql.Engine/Handlers/Control/ConnectionChangedHandler.cs` runs `SqlAliasResolution`,
  `BridgeSqlTargetGuard.Check`, `ParserService.SetServerVersion`, disposes the old persistent
  connection, `DatabaseProvider.SchedulePrefetch`, then schema Phase A and Phase B.
- `src/AkmlSql.Engine/Execution/SessionConnection.cs` + `SessionConnectionRegistry.cs`: one
  long-lived `Microsoft.Data.SqlClient.SqlConnection` per session (`RunExclusiveAsync` is typed
  to `SqlConnection`).
- `src/AkmlSql.Engine/Handlers/Control/TestSqlConnectionHandler.cs`: `new SqlConnection`,
  catches `SqlException`. Messages in `src/AkmlSql.Core/Ipc/Messages/TestSqlConnectionMessages.cs`.
- `ListDatabasesHandler.cs` (`sys.databases ... HAS_DBACCESS`, query in
  `SchemaMetadataService.cs:86-98`); `ChangeDatabaseHandler.cs` (spec 041 selector) uses
  `SqlConnectionStringBuilder.InitialCatalog` + `SqlConnection.ChangeDatabase`.
- `src/AkmlSql.Engine/Pairing/BridgeSqlTargetGuard.cs` parses SqlClient strings
  (Windows / Entra / named-pipe rules; named-pipe clients exempt).
- `src/AkmlSql.Engine/Schema/SqlAliasResolution.cs`: SQL Server client aliases.
- `src/AkmlSql.Core/Config/SqlCredentialStore.cs`: DPAPI SQL-auth passwords; used only by
  `Shell.Shared` / `Ssms22`. The web app never persists passwords.
- Capabilities: `src/AkmlSql.Engine/Capabilities.cs` (`core.format.v1`, `core.analysis.v1`,
  `schema.v2`, `schema.cache.v1`, `diagnostics.engine-log-tail.v1`, `snippets.write`,
  `styles.sqlprompt.v1`, `execute.v2`, `session.database.v1`); sent in
  `HandshakeResponse.EngineCapabilities` (key 3) with `ServerCanonicalIdentity` (key 5). Web
  checks: `ISqlConnectionService.cs:122`, `IQueryExecutionService.cs:39`, `IProfileStore.cs:130`,
  `Editor.razor:986`. Adding a capability string needs no protocol change.
- Web: `src/AkmlSql.Web/Services/ISqlConnectionService.cs` (`ConnectAsync` / `TestAsync` /
  `ListDatabasesAsync` take server, database, windowsAuth, user, password; `ValidateTarget` /
  `IsRemoteTcpServer` know SQL Server address forms; `BuildConnectionString` line 410 uses
  `DbConnectionStringBuilder` with SqlClient keys; sends `ServerVersion = 0`).
  `ISavedSqlConnectionStore.cs` (`SavedSqlConnection`: Id, Name, Server, Database, WindowsAuth,
  Login, LastConnectedAt; no kind, no password). IndexedDB store names in
  `JsIndexedDbAdapter.cs` (`savedSqlConnections`, `connections`, `pairingTokens`, `schemaEntries`).
  UI: `Shared/ConnectionManagerModal.razor` ("Connect to SQL Server"), `ConnectionPickerComponent`,
  `DatabaseSelectorComponent`, `Settings/ConnectionsSection.razor`, `StatusBar.razor`,
  `CommandPalette.razor:558`; controller `IConnectionManagerController.cs`;
  `wwwroot/js/akml-connection-manager.js`.

## Schema

- `src/AkmlSql.Engine/Schema/SchemaMetadataService.cs`: concrete, no interface, every method
  takes `SqlConnection`. Phase A: `sys.objects`, `sys.schemas`, `sys.partitions`, `@@SERVERNAME`,
  `sys.servers`. Phase B: `sys.columns`, `sys.types`, `sys.computed_columns`,
  `sys.default_constraints`, `sys.indexes`, `sys.index_columns`, `sys.foreign_keys`,
  `sys.foreign_key_columns`, `sys.parameters`, `sys.extended_properties`.
  `ProbePermissionsAsync` keys off error numbers 4060/18456/18452/916; `DetectEngineEditionAsync`
  / `DetectFeaturesAsync` use `SERVERPROPERTY`. Dead code: `PopulateFromInformationSchemaAsync`
  (ANSI `INFORMATION_SCHEMA`, line 621), `DetectFeaturesAsync`, `ProbePermissionsAsync`.
- `ChangeDetector.cs`: `CHECKSUM_AGG(BINARY_CHECKSUM(...))` over `sys.objects` + DDL regex.
- `SchemaCacheManager.cs`: key `"{server}:{database}"` (actually `(SessionId, DatabaseName)`),
  LRU, disk persistence, periodic refresh.
- Model (`src/AkmlSql.IntelliSense/Schema/DatabaseCache.cs`, `Models/DatabaseObject.cs`):
  `DbObjectType` = Table, View, Procedure, ScalarFunction, TableFunction, InlineFunction,
  Synonym, Sequence; `PermissionDeniedErrorNumber`; `Column.TypeDisplay` has nvarchar/(MAX)
  logic; `IndexType` Clustered/Nonclustered/Heap/Columnstore. Otherwise mostly neutral.
- Other catalog users: `Completion/Providers/DatabaseProvider.cs` (`sys.databases`),
  `Navigation/ObjectDefinitionService.cs` (`sys.sql_modules`), `Navigation/ReferenceCollector.cs`
  + `Handlers/Refactoring/FindInvalidObjectsHandler.cs` (`sys.sql_expression_dependencies`),
  `Productivity/ScriptAsHandler.cs` / `ScriptAsGenerator.cs` (`sp_helptext`), heavyweight
  refactorings (`sp_rename`, `sp_executesql`).
- `Handlers/Schema/SchemaIdentifyHandler.cs` takes `databaseLookup` / `identityResolver`
  callbacks (neutral, injectable); `EngineHost.cs:353` wires `() => null`.
- Web tree: `Shared/SchemaTreeComponent.razor` (`ServerCanonicalIdentity` + `DatabaseName`;
  fixed headers Tables / Views / Stored Procedures / Functions by `DbObjectType`);
  `Services/ISchemaCacheStore.cs` (`SchemaSnapshot`, key `MakeKey(identity, database)`);
  `ISchemaSync.cs`, `ISchemaCacheEvictor.cs`, `Shared/Settings/SchemaCacheSection.razor`.

## Language surfaces (ScriptDom 180.102.0; 165 files)

- Parser: `src/AkmlSql.IntelliSense/Parser/TsqlParserService.cs` picks `TSql130`-`TSql170Parser`
  by server version (web sends 0, so TSql130). `SplitBatches` (line 148) is used by
  `ExecuteQueryHandler.cs:98` and in-browser `IQueryExecutionService.cs:70`.
- Completion: `CompletionEngine.cs`; providers SmartGroupBy, Column, Object, Keyword, Join,
  JoinOnFk, Variable, Parameter, Snippet, Alias (+ engine-only `DatabaseProvider`);
  `QuickInfoProvider`, `SignatureProvider`, `FkHelpers`; context from `CursorContextAnalyzer`
  (119 ScriptDom refs), `TokenBasedAliasExtractor`, `TokenBasedCteExtractor`,
  `TempTableTracker`, `VariableTracker`. `ICompletionProvider` is neutral. On PostgreSQL text:
  tokens mostly survive; `@vars`, `#temp`, `[brackets]` are assumed throughout.
- Quick info / signature help: `Handlers/Completion/QuickInfoHandler.cs`,
  `SignatureHelpHandler.cs`; web `IQuickInfoService` / `ISignatureHelpService` with
  `OfflineSqlScanner.cs` fallback.
- Go to definition: web `IGotoDefinitionService` to `ObjectDefinitionService` (`sys.sql_modules`).
- Formatting: `src/AkmlSql.Formatting/Pipeline/FormatterPipeline.cs` hardcodes `new TSql170Parser`
  (lines 221, 268, 439); runs in browser (`IFormatterService`) and engine (`FormatRequestHandler`).
- Analysis: `src/AkmlSql.Analysis/RuleRegistry.cs`, 132 rule files; `AnalysisEngine.cs:101`
  parses with ScriptDom; runs in browser (`IAnalyserService`) and engine.
- Refactoring: 12 lightweight ops (`IntelliSense/Refactoring/Operations/Lightweight`, browser via
  `IRefactoringService`); about 12 heavyweight ops (`Engine/Refactoring/Operations/Heavyweight`).
- Wildcard expansion: `IntelliSense/Completion/WildcardExpansionHandler.cs` (tokens + cache).
- Parse check: `ExecuteQueryHandler.cs:255` `SET PARSEONLY ON/OFF`, gated by `Editor.razor:986`.
- Others: `Engine/Safety/SafetyCheckHandler.cs`, `Productivity/StatementBoundaryDetector.cs`,
  `DocumentOutlineBuilder.cs`.

## Reference data

- `src/AkmlSql.IntelliSense/Completion/Dictionaries/KeywordDictionary.cs` (991 lines, about 22
  categorised arrays incl. ScalarFunctions ~180, DataTypes 38, 2019/2022/2025 sets),
  `BuiltinFunctionDictionary.cs` (27 functions with overloads), `SystemProcDictionary.cs` (~35).
- Snippets: 18 `.akmlsnippet` files in `src/AkmlSql.Engine/SnippetPack/`; web built-ins generated
  in `Services/ISnippetStore.cs` (`SnippetStore.BuildBuiltIns`, includes `SET NOCOUNT ON`).
- Bundled but unused: `wwwroot/lib/codemirror/akml-cm.js` exports PostgreSQL, MSSQL, MySQL,
  SQLite, PLSQL, StandardSQL dialects; `akml-editor.js:687` calls `cm.langSql.sql()` with no
  dialect (StandardSQL highlighting today, not MSSQL).

## History (SQLite)

- `src/AkmlSql.Engine/History/HistoryDatabase.cs`: `history` (id, sql_text, truncated, server,
  database_name, username, executed_at, duration_ms, row_count, status, error_msg, source,
  tab_title, content_hash, is_favorite, is_open, session_id; index `IX_history_server_db`),
  `query_sessions` (session_key, local_date, ordinal, name, name_source, server, database_name,
  created_at), `history_fts` (FTS5), versions table, `metadata.schema_version`. No kind column;
  `source` = "source file path or identifier" (`HistoryRecordRequest.cs` key 9). Shared with SSMS.
  Web records via `Editor.razor:955` to `IHistoryService.RecordAsync` to `MessageTypes.HistoryRecord`.

## AI

- `src/AkmlSql.AI/Prompts/PromptTemplates.cs` `BuildSystemPrompt` ("expert SQL Server developer...
  T-SQL... not MySQL, PostgreSQL"); all seven builders (Chat, Explain, Fix, GhostText,
  IndexAnalysis, Optimize, TextToSql) route through it. Further hardcodes: `ChatSystemPrompt.cs:11`,
  `FixPrompt.cs`, `OptimizePrompt.cs`, `IndexAnalysisPrompt.cs`, `TextToSqlPrompt.cs`,
  `src/AkmlSql.Web/Shared/AiChatPanel.razor:202`. Hidden parser dependency:
  `src/AkmlSql.AI/Privacy/LiteralRedactor.cs` redacts literals via the ScriptDom AST
  (`PrivacyTransformer`); it may silently not redact PostgreSQL text.

## Web execution and edit / Apply

- `Shared/ResultSetGrid.razor` `BuildApplyRequest` (line 1668) builds `CrudEditDto`s; SQL is
  generated in `Engine/Execution/CrudWriteGenerator.cs` (bracket `QuoteName`, `[catalog]..[table]`,
  `@p0`/`@k0` `SqlParameter`, `; SELECT @@ROWCOUNT;`). `ApplyChangesHandler.cs` catches
  `SqlException`; `SqlScalarEncoder.cs` by `SqlDbType`. `ResultSetReader.cs` uses
  `CommandBehavior.KeyInfo`, `GetColumnSchema()` (`BaseCatalogName` / `IsKey` / `IsHidden`),
  `SqlDbType`; `ExecuteQueryHandler.FirstStatementObjects` works around `SET NO_BROWSETABLE`.
- `ExecuteQueryHandler.cs`: `FireInfoMessageEventOnUserErrors`, `InfoMessage` to `SqlError`
  Number/Class/State/LineNumber/Procedure; `StatementCompleted` row counts; `conn.Database`.
  `ExecuteOutcome.cs` "Error when severity >= 11". `Core/Ipc/Messages/ExecuteQueryMessages.cs`
  (`ExecuteMessageDto` Number/Severity/State/Line/Procedure/BatchIndex/RowCount, `ExecuteBatchDto`).
  `Core/Text/SqlMessageText.cs` ("Msg N, Level L, State S, Line X"), `SqlTypeName.cs`,
  `SqlValueDisplay.cs`, `Web/Services/ExecuteLineMapper.cs`.

## Tests

- Projects (file counts): Engine.Tests 251, Shell.Shared.Tests 128, Web.Tests 125,
  Formatting.Tests 84, Core.Tests 80, Web.E2E.Tests 33, E2E.Tests 19, AI.Tests 6,
  IntelliSense.Tests 4, Analysis.Tests 1.
- `tests/completion-corpus/` (f01-f21 JSON, about 1,370 completion + 100 formatting cases,
  `exclusions.json`) run by `Engine.Tests/Completion/CorpusGateTests.cs` against
  `NorthwindAutoTestCacheFactory.cs` (fake cache, Northwind shape).
- `tests/format-parity/` (corpus 33 .sql, golden 264, baselines, styles) via
  `Formatting.Tests/Parity/FormatParityTests.cs`.
- No live-database harness (no Testcontainers / LocalDB); gates use fake caches. A PostgreSQL
  equivalent needs a fake-cache factory of the same shape; live checks need a local server.

## Already neutral or reusable

Capability strings; `ICompletionProvider`; `SchemaIdentifyHandler` callbacks; the
`DatabaseCache` / `DatabaseObject` model (except `TypeDisplay`, `IndexType`); the ANSI
`INFORMATION_SCHEMA` queries (unused); the bundled CodeMirror PostgreSQL dialect; IndexedDB and
SQLite storage. Everything else (schema service, sessions, execution, CRUD, parser, dictionaries,
analysis, formatting, AI prompts) is hardwired to SqlClient, ScriptDom or `sys.*` with no seam.
