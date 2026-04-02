# Tasks: Core IntelliSense Engine

**Input**: Design documents from `/specs/002-core-intellisense-engine/`
**Prerequisites**: plan.md (required), spec.md (required), research.md, data-model.md, contracts/

**Tests**: Not included — tests were not explicitly requested. Add test tasks via follow-up if TDD approach desired.

**Organization**: Tasks grouped by user story for independent implementation and testing.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (e.g., US1, US2)
- Exact file paths included in descriptions

---

## Phase 1: Setup (Project Initialization)

**Purpose**: Create new projects, add dependencies, establish project structure

- [x] T001 Create AkmlSql.Engine .NET 10 console project in src/AkmlSql.Engine/AkmlSql.Engine.csproj with ScriptDom and MessagePack dependencies
- [x] T002 Create AkmlSql.Engine.Tests xunit project in tests/AkmlSql.Engine.Tests/AkmlSql.Engine.Tests.csproj referencing AkmlSql.Engine
- [x] T003 Add MessagePack 2.x NuGet dependency to src/AkmlSql.Core/AkmlSql.Core.csproj (netstandard2.0 + net10.0)
- [x] T004 [P] Add VS SDK editor extensibility package references (Microsoft.VisualStudio.Language.Intellisense, Microsoft.VisualStudio.Text.UI.Wpf) to each shell project .csproj
- [x] T005 [P] Add IntelliSenseSettings and CacheSettings classes to src/AkmlSql.Core/Config/AppSettings.cs extending existing AppSettings
- [x] T006 Update src/AkmlSql.Installer/AkmlSqlSetup.iss to deploy AkmlSql.Engine.exe alongside extension

**Checkpoint**: All projects compile, dependencies resolve, solution builds

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Core infrastructure that MUST be complete before ANY user story can begin

**Warning**: No user story work can begin until this phase is complete

### IPC Layer (Shared)

- [x] T007 [P] Implement RpcMessage envelope with MessageType, RequestId, Payload in src/AkmlSql.Core/Ipc/RpcMessage.cs
- [x] T008 [P] Implement length-prefix FrameProtocol (ReadFramedAsync, WriteFramedAsync) in src/AkmlSql.Core/Ipc/FrameProtocol.cs
- [x] T009 [P] Implement ConnectionInfo message contract in src/AkmlSql.Core/Ipc/Messages/ConnectionInfo.cs
- [x] T010 [P] Implement DocumentChange message contract in src/AkmlSql.Core/Ipc/Messages/DocumentChange.cs
- [x] T011 [P] Implement CompletionRequest and CompletionResponse message contracts in src/AkmlSql.Core/Ipc/Messages/CompletionRequest.cs and src/AkmlSql.Core/Ipc/Messages/CompletionResponse.cs
- [x] T012 [P] Implement SignatureRequest, SignatureResponse, QuickInfoRequest, QuickInfoResponse message contracts in src/AkmlSql.Core/Ipc/Messages/
- [x] T013 [P] Implement RefreshRequest, RefreshResponse, EngineStatusInfo, ErrorInfo message contracts in src/AkmlSql.Core/Ipc/Messages/
- [x] T014 Implement CompletionItem model with DisplayText, InsertText, ObjectType, SecondaryText, SortPriority in src/AkmlSql.Core/Ipc/Messages/CompletionResponse.cs

### Engine Core

- [x] T015 Implement engine entry point with --pipe and --parent-pid args, parent PID monitoring for orphan protection in src/AkmlSql.Engine/Program.cs
- [x] T016 Implement PipeRpcServer with NamedPipeServerStream, ACL security (current user only), message dispatch loop in src/AkmlSql.Engine/Server/PipeRpcServer.cs
- [x] T017 Implement SessionManager for per-connection state tracking (SessionId, connection info, cache ref) in src/AkmlSql.Engine/Server/SessionManager.cs

### Schema Models

- [x] T018 [P] Implement DatabaseObject, Column, Parameter, Index models in src/AkmlSql.Engine/Schema/Models/
- [x] T019 [P] Implement ForeignKey model with parent/referenced schema, table, columns in src/AkmlSql.Engine/Schema/Models/ForeignKey.cs
- [x] T020 [P] Implement DatabaseCache with CacheKey, PopulationPhase state machine, SchemaEntry collections in src/AkmlSql.Engine/Schema/DatabaseCache.cs
- [x] T021 Implement SchemaCacheManager for cache lookup by connection key, LRU eviction (max 10 databases) in src/AkmlSql.Engine/Schema/SchemaCacheManager.cs

### Schema Metadata Service

- [x] T022 Implement SchemaMetadataService with batched sys catalog queries (tables/views, columns, FKs, procs/functions, extended properties) in src/AkmlSql.Engine/Schema/SchemaMetadataService.cs
- [x] T023 Implement permission probing (HAS_PERMS_BY_NAME) and 4-level degradation (Full, NoDmv, InformationSchema, PublicOnly) in src/AkmlSql.Engine/Schema/SchemaMetadataService.cs
- [x] T024 Implement phased cache population: Phase A (names, <500ms), Phase B (columns/FKs, background), Phase C (lazy on-demand) in src/AkmlSql.Engine/Schema/SchemaMetadataService.cs

### Parser Core

- [x] T025 Implement TsqlParserService with two-tier strategy: GetTokenStream (fast, per-keystroke) and Parse (debounced, full AST) with version-specific parser selection (TSql130-170) in src/AkmlSql.Engine/Parser/TsqlParserService.cs
- [x] T026 Implement SuffixCompletionHelper that appends dummy tokens to incomplete SQL for valid AST generation in src/AkmlSql.Engine/Parser/SuffixCompletionHelper.cs
- [x] T027 Implement CursorContextAnalyzer that determines ClauseType, PrecedingDot, DotPrefix, InComment, InString, InSqlcmdDirective from token stream and AST in src/AkmlSql.Engine/Parser/CursorContextAnalyzer.cs
- [x] T028 Implement AliasResolver that walks AST NamedTableReference nodes to build alias-to-table dictionary per batch in src/AkmlSql.Engine/Parser/AliasResolver.cs

### Shell IPC Client

- [x] T029 Implement PipeRpcClient with NamedPipeClientStream, request multiplexing via ConcurrentDictionary<int, TaskCompletionSource>, write serialization via SemaphoreSlim in src/AkmlSql.Shell.Shared/Ipc/PipeRpcClient.cs
- [x] T030 Implement EngineProcessManager with launch (Process.Start), crash detection, auto-restart within 2s, connect retry (10 attempts, 200ms apart) in src/AkmlSql.Shell.Shared/Ipc/EngineProcessManager.cs

### Shell Editor Hooks

- [x] T031 Implement TextViewCreationListener (IWpfTextViewCreationListener MEF export) for T-SQL content type detection and command handler attachment in src/AkmlSql.Shell.Shared/Editor/TextViewCreationListener.cs
- [x] T032 Implement CompletionCommandHandler (IOleCommandTarget) intercepting dot, parenthesis, Enter, Tab, Escape keystrokes in src/AkmlSql.Shell.Shared/Editor/CompletionCommandHandler.cs
- [x] T033 Implement CompletionSource (ICompletionSource + ICompletionSourceProvider) with [Order(Before = "default")] that bridges to PipeRpcClient for completion results in src/AkmlSql.Shell.Shared/Editor/CompletionSource.cs
- [x] T034 Implement ContentTypeDetector for runtime discovery of SSMS T-SQL content type string (log ContentType.TypeName from active editor) in src/AkmlSql.Shell.Shared/Editor/ContentTypeDetector.cs

### Completion Engine

- [x] T035 Implement CompletionEngine provider chain router: compute CursorContext, route to providers based on ClauseType/PrecedingDot, merge/sort/truncate results in src/AkmlSql.Engine/Completion/CompletionEngine.cs
- [x] T036 Implement ICompletionProvider interface with CanHandle and GetCompletions methods in src/AkmlSql.Engine/Completion/ICompletionProvider.cs

### Engine Message Dispatch

- [x] T037 Wire message dispatch in PipeRpcServer: route ConnectionChanged to SessionManager, DocumentChanged to TsqlParserService, RequestCompletion to CompletionEngine, return CompletionResult in src/AkmlSql.Engine/Server/PipeRpcServer.cs

### Shell Package Integration

- [x] T038 Integrate EngineProcessManager launch into AkmlSqlPackage.Initialize() (after existing Phase 1 init, fire-and-forget) in src/AkmlSql.Shell.Shared/ shared package init code
- [x] T039 Wire ConnectionChanged detection from SSMS active query window (ServiceCache.ScriptFactory.CurrentlyActiveWndConnectionInfo) and send to engine via PipeRpcClient in src/AkmlSql.Shell.Shared/Editor/TextViewCreationListener.cs

**Checkpoint**: Engine launches, pipe connects, shell can send ConnectionChanged/DocumentChanged/RequestCompletion, engine returns empty CompletionResult. End-to-end plumbing verified.

---

## Phase 3: User Story 1 - Column Completion After Alias (Priority: P1) — MVP

**Goal**: Type `o.` after `FROM dbo.Orders o` and see all columns with data types, PK/FK badges, ranked by static heuristics

**Independent Test**: Connect to any database, write `FROM dbo.Orders o`, type `SELECT o.`, verify columns appear in <100ms

### Implementation

- [x] T040 [US1] Implement ColumnProvider that resolves alias via AliasResolver, fetches columns from DatabaseCache, ranks by PK→FK→ordinal, formats SecondaryText with type/nullability/badges in src/AkmlSql.Engine/Completion/Providers/ColumnProvider.cs
- [x] T041 [US1] Register ColumnProvider in CompletionEngine routing for after-dot context when DotPrefix matches a known alias or table name in src/AkmlSql.Engine/Completion/CompletionEngine.cs
- [x] T042 [US1] Implement lazy column loading in DatabaseCache: on first reference to a table's columns, query SchemaMetadataService for that table's columns if not yet loaded in src/AkmlSql.Engine/Schema/DatabaseCache.cs
- [x] T043 [US1] Handle multi-alias resolution: ensure `c.` resolves to Customers columns and `o.` resolves to Orders columns when multiple aliases in scope in src/AkmlSql.Engine/Completion/Providers/ColumnProvider.cs
- [x] T044 [US1] Handle self-join scenario: `o1.` and `o2.` both resolve to same table but scoped to their alias in src/AkmlSql.Engine/Parser/AliasResolver.cs
- [x] T045 [US1] Ensure alias resolution works in all clause contexts (SELECT, WHERE, GROUP BY, HAVING, ORDER BY, UPDATE SET, JOIN ON) by extending CursorContextAnalyzer clause detection in src/AkmlSql.Engine/Parser/CursorContextAnalyzer.cs

**Checkpoint**: Column completion after alias works end-to-end in SSMS with correct data types and PK/FK ranking

---

## Phase 4: User Story 2 - Schema-Aware Object Completion (Priority: P1)

**Goal**: Type `FROM dbo.` and see all tables/views in dbo schema with type icons; type `FROM ` and see objects ranked by row count

**Independent Test**: Connect to database with multiple schemas, type `FROM sales.`, verify only sales schema objects appear

### Implementation

- [x] T046 [P] [US2] Implement ObjectProvider that returns tables/views/procs/functions/synonyms filtered by schema when dot-preceded, or from default schema + dbo when unqualified in src/AkmlSql.Engine/Completion/Providers/ObjectProvider.cs
- [x] T047 [P] [US2] Implement schema/database completion for multi-part names: `database.` shows schemas, `database.schema.` shows objects in src/AkmlSql.Engine/Completion/Providers/ObjectProvider.cs
- [x] T048 [US2] Register ObjectProvider in CompletionEngine routing for after-dot with schema prefix and for FROM/JOIN/EXEC contexts in src/AkmlSql.Engine/Completion/CompletionEngine.cs
- [x] T049 [US2] Implement context-aware object type filtering: only procedures after EXEC, tables/views after FROM/JOIN in src/AkmlSql.Engine/Completion/Providers/ObjectProvider.cs
- [x] T050 [US2] Implement object ranking: row count estimate descending (for tables/views), then alphabetical in src/AkmlSql.Engine/Completion/Providers/ObjectProvider.cs

**Checkpoint**: Schema-qualified and unqualified object completion works with type indicators and correct filtering

---

## Phase 5: User Story 3 - Keyword Completion in Context (Priority: P1)

**Goal**: Context-appropriate keyword suggestions at every cursor position in a SQL statement

**Independent Test**: Type `SELECT * FR` and see FROM as top suggestion; type after FROM clause and see WHERE, JOIN variants

### Implementation

- [x] T051 [P] [US3] Implement KeywordDictionary with version-aware T-SQL keyword lists (SQL Server 2016-2025) and clause-context mappings in src/AkmlSql.Engine/Completion/Dictionaries/KeywordDictionary.cs
- [x] T052 [P] [US3] Implement KeywordProvider that returns keywords valid at current ClauseType, applies casing preference (UPPER/lower/PascalCase/AsIs) from IntelliSenseSettings in src/AkmlSql.Engine/Completion/Providers/KeywordProvider.cs
- [x] T053 [US3] Implement clause-to-keyword mapping: after SELECT (TOP, DISTINCT, column-start), after FROM (JOIN variants, WHERE, GROUP BY, HAVING, ORDER BY), after WHERE (AND, OR, NOT, IN, EXISTS, BETWEEN, LIKE) in src/AkmlSql.Engine/Completion/Dictionaries/KeywordDictionary.cs
- [x] T054 [US3] Register KeywordProvider in CompletionEngine as secondary provider for most contexts and primary for general keyword positions in src/AkmlSql.Engine/Completion/CompletionEngine.cs
- [x] T055 [US3] Implement comment/string suppression: CursorContextAnalyzer sets InComment/InString from token stream, CompletionEngine returns empty when true in src/AkmlSql.Engine/Parser/CursorContextAnalyzer.cs

**Checkpoint**: Context-aware keywords appear correctly, suppressed in comments/strings, casing follows settings

---

## Phase 6: User Story 4 - FK-Based JOIN Assistance (Priority: P2)

**Goal**: After `JOIN ` with existing table references, suggest FK-related tables with auto-generated ON clauses

**Independent Test**: Write `FROM dbo.Orders o JOIN `, verify FK-related tables appear with ON clause preview

### Implementation

- [x] T056 [P] [US4] Implement JoinProvider that queries ForeignKey collection in DatabaseCache to find tables with FK relationships to already-referenced tables in src/AkmlSql.Engine/Completion/Providers/JoinProvider.cs
- [x] T057 [US4] Implement ON clause auto-generation: build `ON alias.fkCol = existingAlias.refCol` text for single and multi-column FKs in src/AkmlSql.Engine/Completion/Providers/JoinProvider.cs
- [x] T058 [US4] Implement alias suggestion for JOIN target table (PascalCase abbreviation) to use in ON clause preview in src/AkmlSql.Engine/Completion/Providers/JoinProvider.cs
- [x] T059 [US4] Register JoinProvider in CompletionEngine for JOIN context (after JOIN keyword, before table name) with ObjectProvider as fallback in src/AkmlSql.Engine/Completion/CompletionEngine.cs

**Checkpoint**: FK-based JOIN suggestions with auto-ON clauses work for single and multi-column FKs

---

## Phase 7: User Story 5 - Function Signature Help (Priority: P2)

**Goal**: Typing `(` after function/procedure name shows parameter list with current parameter highlighted

**Independent Test**: Type `CONVERT(` and verify parameter tooltip shows with correct parameter highlighted as you type

### Implementation

- [x] T060 [P] [US5] Implement BuiltinFunctionDictionary with parameter signatures for all T-SQL built-in functions (CONVERT, DATEADD, CAST, COALESCE, etc.) in src/AkmlSql.Engine/Completion/Dictionaries/BuiltinFunctionDictionary.cs
- [x] T061 [P] [US5] Implement SignatureProvider that returns signatures from BuiltinFunctionDictionary (for built-ins) and schema cache Parameters (for user-defined procs/functions) in src/AkmlSql.Engine/Completion/Providers/SignatureProvider.cs
- [x] T062 [US5] Implement active parameter tracking: count commas before cursor to determine which parameter is current in src/AkmlSql.Engine/Completion/Providers/SignatureProvider.cs
- [x] T063 [US5] Implement SignatureHelpSource (ISignatureHelpSource + ISignatureHelpSourceProvider) in shell that bridges to PipeRpcClient for SignatureRequest/SignatureResponse in src/AkmlSql.Shell.Shared/Editor/SignatureHelpSource.cs
- [x] T064 [US5] Wire parenthesis and comma keystrokes in CompletionCommandHandler to trigger/update signature help session in src/AkmlSql.Shell.Shared/Editor/CompletionCommandHandler.cs

**Checkpoint**: Signature help works for built-in functions and user-defined procedures with active parameter tracking

---

## Phase 8: User Story 6 - Quick Info Tooltips (Priority: P2)

**Goal**: Hovering over identifiers shows metadata (row counts, types, descriptions)

**Independent Test**: Hover over a table name and verify tooltip shows row count, column count, description

### Implementation

- [x] T065 [P] [US6] Implement QuickInfoProvider that returns metadata for tables (row count, column count, description), columns (type, nullability, default), variables (declared type), keywords (syntax help) in src/AkmlSql.Engine/Completion/Providers/QuickInfoProvider.cs
- [x] T066 [US6] Implement identifier-at-offset resolution: determine what identifier the cursor/hover position is on and resolve it to a schema object, column, variable, or keyword in src/AkmlSql.Engine/Completion/Providers/QuickInfoProvider.cs
- [x] T067 [US6] Implement QuickInfoSource (IQuickInfoSource + IQuickInfoSourceProvider) in shell that bridges to PipeRpcClient for QuickInfoRequest/QuickInfoResponse in src/AkmlSql.Shell.Shared/Editor/QuickInfoSource.cs
- [x] T068 [US6] Wire RequestQuickInfo message dispatch in PipeRpcServer to QuickInfoProvider in src/AkmlSql.Engine/Server/PipeRpcServer.cs

**Checkpoint**: Hover tooltips show correct metadata for tables, columns, variables, and keywords

---

## Phase 9: User Story 7 - CTE and Temp Table Column Completion (Priority: P2)

**Goal**: CTE-defined columns and #temp table columns available for completion

**Independent Test**: Write `WITH cte AS (SELECT col1 FROM t)` then type `SELECT c.` after `FROM cte c`, verify col1 appears

### Implementation

- [x] T069 [P] [US7] Implement CteResolver that parses WITH clauses, extracts CTE names and column lists (explicit or inferred from SELECT), stores in BatchScope.CteDefinitions in src/AkmlSql.Engine/Parser/CteResolver.cs
- [x] T070 [P] [US7] Implement TempTableTracker that detects CREATE TABLE #name and SELECT INTO #name statements, extracts column definitions, stores in BatchScope.TempTables in src/AkmlSql.Engine/Parser/TempTableTracker.cs
- [x] T071 [US7] Extend ColumnProvider to check BatchScope.CteDefinitions and BatchScope.TempTables when resolving alias references (CTE/temp table aliases resolve to virtual column lists) in src/AkmlSql.Engine/Completion/Providers/ColumnProvider.cs
- [x] T072 [US7] Handle nested CTEs: outer CTE referencing inner CTE columns resolved through CteResolver chain in src/AkmlSql.Engine/Parser/CteResolver.cs
- [x] T073 [US7] Implement VariableTracker that detects DECLARE @name type statements, stores in BatchScope.Variables in src/AkmlSql.Engine/Parser/VariableTracker.cs
- [x] T074 [US7] Implement VariableProvider that returns @variables from BatchScope.Variables with their declared types in src/AkmlSql.Engine/Completion/Providers/VariableProvider.cs

**Checkpoint**: CTE columns, temp table columns, and variables all available for completion within batch scope

---

## Phase 10: User Story 8 - Schema Cache Management (Priority: P3)

**Goal**: Cache auto-populates on connection, refreshes on DDL and periodically, persists to disk

**Independent Test**: Connect to database, verify completion works in <3s. Execute ALTER TABLE, verify new column appears in <5s

### Implementation

- [x] T075 [P] [US8] Implement ChangeDetector with CHECKSUM_AGG polling (every 30-60s) and modify_date diff for incremental refresh of changed objects in src/AkmlSql.Engine/Schema/ChangeDetector.cs
- [x] T076 [P] [US8] Implement disk cache persistence: serialize DatabaseCache to JSON/MessagePack file in %LocalAppData%/AKML SQL/cache/{cacheKey}.bin, load on reconnect in src/AkmlSql.Engine/Schema/SchemaCacheManager.cs
- [x] T077 [US8] Implement DDL detection: after query execution, scan executed text for CREATE/ALTER/DROP keywords, trigger targeted cache refresh for affected objects in src/AkmlSql.Engine/Schema/ChangeDetector.cs
- [x] T078 [US8] Implement manual refresh command: wire Ctrl+Shift+R menu command to send SchemaRefreshRequest via PipeRpcClient, rebuild full cache for current database in src/AkmlSql.Shell.Shared/Commands/ (new RefreshCacheCommand.cs)
- [x] T079 [US8] Implement background periodic refresh timer based on CacheSettings.RefreshIntervalSeconds (default 300s) in src/AkmlSql.Engine/Schema/SchemaCacheManager.cs
- [x] T080 [US8] Implement multi-database support: maintain separate DatabaseCache per connection key, lazy-load cross-database metadata when three-part names detected in src/AkmlSql.Engine/Schema/SchemaCacheManager.cs

**Checkpoint**: Schema cache auto-populates, persists, refreshes on DDL/timer/manual, supports multi-database

---

## Phase 11: User Story 9 - Completion UI with Fuzzy Matching (Priority: P3)

**Goal**: Popup with fuzzy/CamelCase matching, theme support, keyboard/mouse navigation, DPI awareness

**Independent Test**: Trigger completion, type `custid`, verify `CustomerID` matches. Switch to Dark theme, verify popup follows

### Implementation

- [x] T081 [P] [US9] Implement FuzzyMatcher with 5-level scoring: exact prefix, case-insensitive prefix, CamelCase, substring, non-contiguous character matching in src/AkmlSql.Engine/Completion/FuzzyMatcher.cs
- [x] T082 [P] [US9] Implement ThemeManager using VSColorTheme.GetThemedColor() and EnvironmentColors for Light/Dark/Blue theme colors in src/AkmlSql.Shell.Shared/Ui/ThemeManager.cs
- [x] T083 [P] [US9] Implement DpiHelper for multi-monitor DPI-aware popup positioning in src/AkmlSql.Shell.Shared/Ui/DpiHelper.cs
- [x] T084 [US9] Implement CompletionPopup WPF control with item list, filter textbox, type icons, secondary text column, status bar (source table + match count) in src/AkmlSql.Shell.Shared/Ui/CompletionPopup.xaml and src/AkmlSql.Shell.Shared/Ui/CompletionPopup.xaml.cs
- [x] T085 [US9] Implement CompletionItemViewModel for data binding: DisplayText, InsertText, IconSource, SecondaryText, IsSelected in src/AkmlSql.Shell.Shared/Ui/CompletionItemViewModel.cs
- [x] T086 [US9] Wire FuzzyMatcher into CompletionEngine: apply filter to all provider results before returning CompletionResponse in src/AkmlSql.Engine/Completion/CompletionEngine.cs
- [x] T087 [US9] Implement keyboard navigation (Up/Down/Enter/Tab/Escape) and mouse support (click select, double-click accept) in CompletionPopup in src/AkmlSql.Shell.Shared/Ui/CompletionPopup.xaml.cs

**Checkpoint**: Fuzzy matching works, popup follows theme, DPI correct, keyboard/mouse navigation functional

---

## Phase 12: User Story 10 - Out-of-Process Engine Resilience (Priority: P3)

**Goal**: Engine crash doesn't affect IDE; auto-restart within 2s; silent when unavailable

**Independent Test**: Kill engine process, verify SSMS doesn't freeze, engine restarts in <2s

### Implementation

- [x] T088 [US10] Implement crash detection in EngineProcessManager: monitor Process.Exited event, detect IOException on pipe read, fail all pending TCS with EngineDisconnectedException in src/AkmlSql.Shell.Shared/Ipc/EngineProcessManager.cs
- [x] T089 [US10] Implement auto-restart logic: on crash detection, wait 500ms backoff, relaunch engine process, reconnect pipe, re-send ConnectionChanged for all active sessions in src/AkmlSql.Shell.Shared/Ipc/EngineProcessManager.cs
- [x] T090 [US10] Implement silent degradation in CompletionSource: if PipeRpcClient is disconnected, return empty completions (no error, no popup) — completion resumes when engine reconnects in src/AkmlSql.Shell.Shared/Editor/CompletionSource.cs
- [x] T091 [US10] Implement heartbeat: shell sends Ping every 15s, engine responds with Pong (EngineStatusInfo). If no response in 5s, treat as crash in src/AkmlSql.Shell.Shared/Ipc/PipeRpcClient.cs
- [x] T092 [US10] Implement graceful shutdown: on IDE exit, send Shutdown message to engine, engine closes pipe and exits in src/AkmlSql.Shell.Shared/Ipc/EngineProcessManager.cs

**Checkpoint**: Engine crash recovery within 2s, IDE never freezes, silent when engine unavailable

---

## Phase 13: User Story 11 - Native IntelliSense Conflict Resolution (Priority: P3)

**Goal**: Detect and offer to disable SSMS IntelliSense on first load; restore on uninstall

**Independent Test**: Install extension in SSMS with native IntelliSense enabled, verify disable dialog appears and works

### Implementation

- [x] T093 [US11] Implement NativeIntelliSenseManager: detect SSMS IntelliSense state via DTE.Properties["TextEditor", "SQL"] and command filter suppression in src/AkmlSql.Shell.Shared/IntelliSense/NativeIntelliSenseManager.cs
- [x] T094 [US11] Implement first-time dialog: check config flag (intellisense.nativeDisableAsked), show dialog with [Yes] [No] [Don't ask again], persist choice in src/AkmlSql.Shell.Shared/IntelliSense/NativeIntelliSenseManager.cs
- [x] T095 [US11] Implement disable logic: set DTE option Auto List Members = false for T-SQL, and/or suppress commands via IOleCommandTarget filter not passing to next handler in src/AkmlSql.Shell.Shared/IntelliSense/NativeIntelliSenseManager.cs
- [x] T096 [US11] Implement restore logic: on uninstall (via installer), re-enable SSMS IntelliSense setting if it was disabled by AKML SQL (track in config flag) in src/AkmlSql.Installer/AkmlSqlSetup.iss

**Checkpoint**: Native IntelliSense disable/restore workflow works cleanly across install/uninstall

---

## Phase 14: Additional Completion Providers

**Purpose**: Snippet and alias providers that enhance the core completion experience

- [x] T097 [P] Implement SnippetProvider with 6 built-in snippets (ssf, sel, ins, upd, del, cte) with tab-stop placeholders in src/AkmlSql.Engine/Completion/Providers/SnippetProvider.cs
- [x] T098 [P] Implement AliasProvider that suggests alias abbreviations (PascalCase first letters) after table references in FROM/JOIN, checking for conflicts in src/AkmlSql.Engine/Completion/Providers/AliasProvider.cs
- [x] T099 [P] Implement SystemProcDictionary with sp_, xp_, fn_ prefixed system stored procedures and their signatures in src/AkmlSql.Engine/Completion/Dictionaries/SystemProcDictionary.cs
- [x] T100 Register SnippetProvider, AliasProvider in CompletionEngine routing for appropriate contexts in src/AkmlSql.Engine/Completion/CompletionEngine.cs
- [x] T101 Implement tab-stop navigation for accepted snippets: after snippet insertion, place cursor at $1 position, Tab advances to $2 in src/AkmlSql.Shell.Shared/Editor/CompletionCommandHandler.cs

---

## Phase 15: Settings & Configuration UI

**Purpose**: Make all IntelliSense and cache settings configurable via Options dialog

- [x] T102 Implement IntelliSense options page reading/writing IntelliSenseSettings and CacheSettings via ConfigManager in src/AkmlSql.Shell.Shared/Commands/OptionsCommand.cs (replace existing placeholder)
- [x] T103 Implement settings propagation: on settings change, send updated settings to engine via pipe message in src/AkmlSql.Shell.Shared/Ipc/PipeRpcClient.cs
- [x] T104 Add Refresh Cache menu command (Ctrl+Shift+R) to VSCT files and wire to RefreshCacheCommand in src/AkmlSql.Shell.Shared/ and each shell project's .vsct

---

## Phase 16: Polish & Cross-Cutting Concerns

**Purpose**: Improvements that affect multiple user stories

- [x] T105 [P] Implement batch separator (GO) detection and per-batch scope isolation in TsqlParserService and CursorContextAnalyzer in src/AkmlSql.Engine/Parser/TsqlParserService.cs
- [x] T106 [P] Implement Azure SQL Database detection (EngineEdition=5) and skip cross-database/linked server queries in SchemaMetadataService in src/AkmlSql.Engine/Schema/SchemaMetadataService.cs
- [x] T107 [P] Implement INFORMATION_SCHEMA fallback queries for permission level 3 (no sys access) in src/AkmlSql.Engine/Schema/SchemaMetadataService.cs
- [x] T108 [P] Implement version-aware feature detection: temporal_type (2016+), is_node/is_edge (2017+), ledger_type (2022+) in schema queries in src/AkmlSql.Engine/Schema/SchemaMetadataService.cs
- [x] T109 Add engine process deployment to installer: package AkmlSql.Engine.exe publish output into extension directory under Engine/ subfolder in src/AkmlSql.Installer/AkmlSqlSetup.iss
- [x] T110 Verify all completion features work across SSMS 20, SSMS 22 builds by building each shell project individually with MSBuild
- [x] T111 Run performance validation: measure completion latency p95 against AdventureWorks database (target <100ms)

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies — start immediately
- **Foundational (Phase 2)**: Depends on Setup — BLOCKS all user stories
- **US1-US3 (Phase 3-5, P1)**: All depend on Foundational. Execute sequentially (US1 is MVP)
- **US4-US7 (Phase 6-9, P2)**: Depend on Foundational. Can run in parallel after US1 validates the stack
- **US8-US11 (Phase 10-13, P3)**: Depend on Foundational. Can run in parallel with P2 stories
- **Additional Providers (Phase 14)**: Can run after Foundational, parallel with any user story
- **Settings (Phase 15)**: Can run after Foundational, parallel with user stories
- **Polish (Phase 16)**: Depends on all desired user stories being complete

### User Story Dependencies

- **US1 (Column Completion)**: Foundational only — no other story dependency. **MVP target**
- **US2 (Object Completion)**: Foundational only — independent of US1
- **US3 (Keyword Completion)**: Foundational only — independent of US1/US2
- **US4 (JOIN Assistance)**: Requires ForeignKey data in cache (loaded in Foundational Phase B)
- **US5 (Signature Help)**: Foundational only — independent of other stories
- **US6 (Quick Info)**: Foundational only — independent of other stories
- **US7 (CTE/Temp Table)**: Foundational only — extends ColumnProvider from US1
- **US8 (Cache Management)**: Enhances Foundational cache — extends SchemaCacheManager
- **US9 (Fuzzy Matching UI)**: Enhances Foundational CompletionEngine — extends popup
- **US10 (Engine Resilience)**: Enhances Foundational EngineProcessManager
- **US11 (Native IntelliSense)**: Foundational only — independent

### Parallel Opportunities

**Within Phase 2 (Foundational)**:
- T007-T014 (IPC messages) all parallel
- T018-T020 (schema models) all parallel
- T025-T028 (parser components) partially parallel

**Across User Stories (after Foundational)**:
- US1, US2, US3 can potentially run in parallel (different providers, different files)
- US4-US7 can all run in parallel (different providers, different files)
- US8-US11 can all run in parallel (different subsystems)
- Phase 14-15 can run in parallel with any user story phase

---

## Parallel Example: Foundational Phase

```text
# Launch all IPC message contracts in parallel:
T007: RpcMessage.cs
T008: FrameProtocol.cs
T009: ConnectionInfo.cs
T010: DocumentChange.cs
T011: CompletionRequest.cs + CompletionResponse.cs
T012: SignatureRequest.cs + SignatureResponse.cs + QuickInfoRequest.cs + QuickInfoResponse.cs
T013: RefreshRequest.cs + RefreshResponse.cs + EngineStatusInfo.cs + ErrorInfo.cs

# Launch all schema models in parallel:
T018: DatabaseObject, Column, Parameter, Index models
T019: ForeignKey model
T020: DatabaseCache model
```

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Complete Phase 1: Setup (T001-T006)
2. Complete Phase 2: Foundational (T007-T039) — CRITICAL, blocks all stories
3. Complete Phase 3: US1 Column Completion (T040-T045)
4. **STOP and VALIDATE**: Test alias.column completion end-to-end in SSMS
5. Deploy/demo — this single feature already differentiates from SSMS IntelliSense

### Incremental Delivery

1. Setup + Foundational → Plumbing works (engine launches, pipe connects)
2. + US1 → Column completion after alias (MVP!)
3. + US2 + US3 → Full P1 completion (objects + keywords)
4. + US4-US7 → Advanced completion (JOINs, signatures, quick info, CTEs)
5. + US8-US11 → Production hardening (cache management, resilience, conflict resolution)
6. + Phase 14-16 → Polish (snippets, aliases, settings, cross-cutting)

### Parallel Team Strategy

With multiple developers after Foundational is complete:
- Developer A: US1 (MVP) → US4 (JOINs) → US8 (Cache)
- Developer B: US2 (Objects) → US5 (Signatures) → US9 (Fuzzy UI)
- Developer C: US3 (Keywords) → US7 (CTE/Temp) → US10 (Resilience)
- Developer D: US6 (Quick Info) → US11 (Native IntelliSense) → Phase 14-15

---

## Notes

- [P] tasks = different files, no dependencies between them
- [Story] label maps task to specific user story for traceability
- Each user story should be independently completable and testable
- Commit after each task or logical group
- Stop at any checkpoint to validate story independently
- Shell projects MUST be built with MSBuild individually (not dotnet build, not solution build)
- Engine project uses dotnet build/publish
- ContentType string ("T-SQL") must be verified at runtime in SSMS — T034 is critical early validation
