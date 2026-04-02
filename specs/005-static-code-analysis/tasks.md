# Tasks: Static Code Analysis Engine

**Input**: Design documents from `/specs/005-static-code-analysis/`
**Prerequisites**: plan.md ✅ spec.md ✅ research.md ✅ data-model.md ✅ contracts/ ✅ quickstart.md ✅

**Organization**: Tasks grouped by user story for independent implementation and delivery.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependency on incomplete tasks)
- **[Story]**: Which user story this task belongs to (US1–US5)

---

## Phase 1: Setup (Project Structure)

**Purpose**: Create all new files, directories, and projects required before any feature code can be written.

- [X] T001 Create directory `src/AkmlSql.Engine/Analysis/Rules/Performance/` (empty — structure only)
- [X] T002 [P] Create directory `src/AkmlSql.Engine/Analysis/Rules/BestPractices/`
- [X] T003 [P] Create directory `src/AkmlSql.Engine/Analysis/Rules/Security/`
- [X] T004 [P] Create directory `src/AkmlSql.Engine/Analysis/Rules/Style/`
- [X] T005 [P] Create directory `src/AkmlSql.Engine/Analysis/Rules/Deprecated/`
- [X] T006 [P] Create directory `src/AkmlSql.Engine/Analysis/Rules/Design/`
- [X] T007 [P] Create directory `src/AkmlSql.Engine/Analysis/Rules/Execution/`
- [X] T008 [P] Create directory `src/AkmlSql.Engine/Analysis/Rules/Naming/`
- [X] T009 [P] Create directory `src/AkmlSql.Shell.Shared/Analysis/`
- [X] T010 [P] Create directory `src/AkmlSql.Core/Models/Analysis/`
- [X] T011 [P] Create directory `tests/AkmlSql.Engine.Tests/Analysis/Rules/Performance/`
- [X] T012 [P] Create directory `tests/AkmlSql.Engine.Tests/Analysis/Rules/BestPractices/`
- [X] T013 [P] Create directory `tests/AkmlSql.Engine.Tests/Analysis/`
- [X] T014 Create new project file `src/AkmlSql.Analyzer/AkmlSql.Analyzer.csproj` (net10.0, win-x64, self-contained, references AkmlSql.Engine and AkmlSql.Core)
- [X] T015 Add `AkmlSql.Analyzer` project to `AKML-SQL.slnx`

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Core infrastructure that ALL user stories depend on. Nothing in Phase 3+ can start until this is complete.

**⚠️ CRITICAL**: All user story phases block on this phase.

### IPC Models (Core project)

- [X] T016 [P] Add `DiagnosticSeverity` enum (Error=3, Warning=2, Information=1, Hint=0) to `src/AkmlSql.Core/Models/Analysis/DiagnosticSeverity.cs`
- [X] T017 [P] Add `FixType` enum (Transform=0, Insert=1, Remove=2, Suppress=3) to `src/AkmlSql.Core/Models/Analysis/FixType.cs`
- [X] T018 [P] Add `SuppressScope` enum (Line=0, File=1, Global=2) to `src/AkmlSql.Core/Models/Analysis/SuppressScope.cs`
- [X] T019 [P] Create `FixActionInfo` MessagePack model in `src/AkmlSql.Core/Ipc/Messages/FixActionInfo.cs` (fields per data-model.md: Label, FixType, ReplacementStart, ReplacementEnd, ReplacementText, SuppressRuleId?, SuppressScopeCode?)
- [X] T020 [P] Create `CodeIssueInfo` MessagePack model in `src/AkmlSql.Core/Ipc/Messages/CodeIssueInfo.cs` (fields: RuleId, Severity, Message, StartOffset, EndOffset, Line, Column, FixActions)
- [X] T021 [P] Create `CodeAnalysisRequest` MessagePack model in `src/AkmlSql.Core/Ipc/Messages/CodeAnalysisRequest.cs` (SessionId, RequestId, DocumentText, DocumentVersion)
- [X] T022 [P] Create `CodeAnalysisResponse` MessagePack model in `src/AkmlSql.Core/Ipc/Messages/CodeAnalysisResponse.cs` (RequestId, Issues, AnalyzedVersion)
- [X] T023 Add new message type constants `RequestAnalyze = 25`, `AnalysisSettingsChanged = 26`, `AnalysisResult = 125` to `src/AkmlSql.Core/Ipc/MessageTypes.cs`

### Settings (Core project)

- [X] T024 Add `CodeAnalysisSettings` class to `src/AkmlSql.Core/Config/AppSettings.cs` with properties: `Enabled` (bool, default true), `RunOnType` (bool, default true), `RunOnSave` (bool, default true), `AutoFixOnFormat` (bool, default false), `SquiggleStyle` (string, default "underline"), `ShowInErrorList` (bool, default true)
- [X] T025 [P] Create `CaSettings` model in `src/AkmlSql.Core/Models/Analysis/CaSettings.cs` (Metadata, Rules dictionary, GlobalSuppressions — per data-model.md)
- [X] T026 [P] Create `RuleConfig` model in `src/AkmlSql.Core/Models/Analysis/RuleConfig.cs` (Enabled, Severity string)
- [X] T027 [P] Create `GlobalSuppression` model in `src/AkmlSql.Core/Models/Analysis/GlobalSuppression.cs` (Rule, Reason)

### Engine Analysis Infrastructure

- [X] T028 Create `IAnalysisRule` interface in `src/AkmlSql.Engine/Analysis/IAnalysisRule.cs` (RuleId, Category, DefaultSeverity, RequiresSchema, Analyze(AnalysisContext) → IEnumerable<AnalysisDiagnostic>)
- [X] T029 Create `AnalysisFixAction` class in `src/AkmlSql.Engine/Analysis/AnalysisFixAction.cs` (Label, FixType, ReplacementStart, ReplacementEnd, ReplacementText, SuppressRuleId?, SuppressScope?)
- [X] T030 Create `AnalysisDiagnostic` class in `src/AkmlSql.Engine/Analysis/AnalysisDiagnostic.cs` (RuleId, CategoryCode, Severity, Message, StartOffset, EndOffset, Line, Column, FixActions array — per data-model.md)
- [X] T031 Create `ResolvedRuleConfig` class and `ResolvedAnalysisSettings` class in `src/AkmlSql.Engine/Analysis/ResolvedAnalysisSettings.cs` (Enabled, RunOnType, RunOnSave, AutoFixOnFormat, EffectiveRules dictionary with GetSeverity helper)
- [X] T032 Create `SuppressionMap` class in `src/AkmlSql.Engine/Analysis/SuppressionMap.cs` (SuppressedLines dict, SuppressedBlocks list, IsSuppressed(line, ruleId) method)
- [X] T033 Create `AnalysisContext` class in `src/AkmlSql.Engine/Analysis/AnalysisContext.cs` (Script, CurrentBatch, Tokens, DocumentText, SessionId, SchemaCache?, Settings, Suppressions, CancellationToken — per data-model.md)
- [X] T034 Create `RuleRegistry` class in `src/AkmlSql.Engine/Analysis/RuleRegistry.cs` — reflects all `IAnalysisRule` implementations in the current assembly, instantiates them, and exposes `IReadOnlyList<IAnalysisRule> GetRules(ResolvedAnalysisSettings settings)` filtered to enabled rules
- [X] T035 Create `CaSettingsLoader` class in `src/AkmlSql.Engine/Analysis/CaSettingsLoader.cs` — loads `.casettings` JSON from nearest ancestor directory (walk-up logic per research.md R-006), merges with global `AppSettings.CodeAnalysis`, returns `ResolvedAnalysisSettings`; cache results by directory path
- [X] T036 Create `AnalysisEngine` class in `src/AkmlSql.Engine/Analysis/AnalysisEngine.cs` — `AnalyzeAsync(CodeAnalysisRequest, SessionManager, SchemaCacheManager, CaSettingsLoader, CancellationToken)` → splits text by GO batches → hashes each batch → parallel rule execution (SemaphoreSlim(8)) → returns `CodeAnalysisResponse`
- [X] T037 Wire `RequestAnalyze = 25` into `PipeRpcServer.DispatchAsync` in `src/AkmlSql.Engine/Server/PipeRpcServer.cs` — deserialize `CodeAnalysisRequest`, call `AnalysisEngine.AnalyzeAsync`, return `AnalysisResult` message; wire `AnalysisSettingsChanged = 26` to call `CaSettingsLoader.InvalidateCache()`
- [X] T038 Create `AnalysisEngineTestHelper` static class in `tests/AkmlSql.Engine.Tests/Analysis/AnalysisEngineTestHelper.cs` — creates minimal `AnalysisContext` (no schema cache, default settings, TSql160 parser) for a given SQL string and optional rule ID filter; used by all rule tests

**Checkpoint**: Foundation complete — engine can receive an analyze request, run rules, and return diagnostics. Shell integration can begin.

---

## Phase 3: User Story 1 — Real-Time Issue Detection (Priority: P1) 🎯 MVP

**Goal**: Violations appear as colored squiggles in the SSMS/VS SQL editor within one second of typing, backed by at least 10 working rules.

**Independent Test**: Open any SQL editor in SSMS 22. Type `SELECT * FROM dbo.Orders`. A green warning squiggle appears under `SELECT *` within one second with tooltip "Avoid SELECT * in stored procedures". Edit the line to `SELECT Id FROM dbo.Orders`. Squiggle disappears.

### Reference Rule Implementations (PE001–PE010)

- [X] T039 [P] [US1] Implement `PE001_AvoidSelectStar` in `src/AkmlSql.Engine/Analysis/Rules/Performance/PE001_AvoidSelectStar.cs` — fires on `SELECT *` in stored procedure body; fix: expand to explicit columns (degrade gracefully when no schema cache by emitting warning without expansion)
- [X] T040 [P] [US1] Implement `PE002_UnqualifiedObjectName` in `src/AkmlSql.Engine/Analysis/Rules/Performance/PE002_UnqualifiedObjectName.cs` — fires when a table/view reference has no schema prefix; fix: prepend `dbo.`
- [X] T041 [P] [US1] Implement `PE003_MissingWhereOnDeleteUpdate` in `src/AkmlSql.Engine/Analysis/Rules/Performance/PE003_MissingWhereOnDeleteUpdate.cs` — fires on DELETE or UPDATE statement with no WHERE clause; severity: Error; no auto-fix (too destructive)
- [X] T042 [P] [US1] Implement `PE004_LikeWithLeadingWildcard` in `src/AkmlSql.Engine/Analysis/Rules/Performance/PE004_LikeWithLeadingWildcard.cs` — fires on `LIKE '%value'` patterns; no auto-fix
- [X] T043 [P] [US1] Implement `PE009_MissingSetNoCountOn` in `src/AkmlSql.Engine/Analysis/Rules/Performance/PE009_MissingSetNoCountOn.cs` — fires on CREATE/ALTER PROCEDURE body that does not begin with `SET NOCOUNT ON`; fix: insert `SET NOCOUNT ON` at start of procedure body
- [X] T044 [P] [US1] Implement `PE010_SelectStarInExists` in `src/AkmlSql.Engine/Analysis/Rules/Performance/PE010_SelectStarInExists.cs` — fires on `EXISTS (SELECT * ...)` pattern; fix: replace `SELECT *` with `SELECT 1`
- [X] T045 [P] [US1] Implement `BP004_NullComparison` in `src/AkmlSql.Engine/Analysis/Rules/BestPractices/BP004_NullComparison.cs` — fires on `= NULL` or `<> NULL` comparisons; severity: Error; fix: replace with `IS NULL` / `IS NOT NULL`
- [X] T046 [P] [US1] Implement `BP001_UseScope_Identity` in `src/AkmlSql.Engine/Analysis/Rules/BestPractices/BP001_UseScopeIdentity.cs` — fires on `@@IDENTITY` usage; fix: replace with `SCOPE_IDENTITY()`
- [X] T047 [P] [US1] Implement `SE001_SqlInjectionRisk` in `src/AkmlSql.Engine/Analysis/Rules/Security/SE001_SqlInjectionRisk.cs` — fires when dynamic SQL string concatenation contains a variable/column reference; severity: Error; no auto-fix
- [X] T048 [P] [US1] Implement `DEP001_DeprecatedDataType` in `src/AkmlSql.Engine/Analysis/Rules/Deprecated/DEP001_DeprecatedDataType.cs` — fires on `text`, `ntext`, `image` column type declarations; fix: replace with `varchar(max)`, `nvarchar(max)`, `varbinary(max)` respectively

### Shell Integration (Squiggles + Error List)

- [X] T049 [US1] Create `AnalysisController` in `src/AkmlSql.Shell.Shared/Analysis/AnalysisController.cs` — holds `CancellationTokenSource` per session; on `DocumentChanged` event, cancels prior token, debounces 300ms, fires `RequestAnalyze` RPC to Engine, receives `CodeAnalysisResponse`, raises `DiagnosticsUpdated` event with `CodeIssueInfo[]`
- [X] T050 [US1] Create `DiagnosticTagger` in `src/AkmlSql.Shell.Shared/Analysis/DiagnosticTagger.cs` — implements `ITagger<IErrorTag>`; exported as `[Export(typeof(IViewTaggerProvider))]` MEF component; maps `CodeIssueInfo.Severity` to `PredefinedErrorTypeNames` (Error=SyntaxError, Warning=Warning, Information=OtherError, Hint=OtherError with lighter color); raises `TagsChanged` when `AnalysisController.DiagnosticsUpdated` fires
- [X] T051 [US1] Create `ErrorListReporter` in `src/AkmlSql.Shell.Shared/Analysis/ErrorListReporter.cs` — listens to `AnalysisController.DiagnosticsUpdated`; pushes `Severity >= Warning` issues to the VS/SSMS Error List via `ITableDataSink`; clears previous entries for the same document before pushing new ones
- [X] T052 [US1] Wire `AnalysisController`, `DiagnosticTagger`, and `ErrorListReporter` into `AkmlSqlPackage` initialization (inside `AkmlSql.Shell.Shared`) — instantiate after commands are registered; pass the existing `PipeClient` instance to `AnalysisController`

### Rule Tests (US1)

- [X] T053 [P] [US1] Create `PE001_AvoidSelectStarTests` in `tests/AkmlSql.Engine.Tests/Analysis/Rules/Performance/PE001_AvoidSelectStarTests.cs` — test: fires on `SELECT *` in proc body; does NOT fire on `SELECT *` in ad-hoc query; does NOT fire in comment; has fix action
- [X] T054 [P] [US1] Create `PE003_MissingWhereTests` in `tests/AkmlSql.Engine.Tests/Analysis/Rules/Performance/PE003_MissingWhereTests.cs` — test: fires on `DELETE FROM t` (no WHERE); does NOT fire on `DELETE FROM t WHERE id = 1`; severity is Error
- [X] T055 [P] [US1] Create `BP004_NullComparisonTests` in `tests/AkmlSql.Engine.Tests/Analysis/Rules/BestPractices/BP004_NullComparisonTests.cs` — test: fires on `= NULL`; does NOT fire on `IS NULL`; fix produces `IS NULL`; in-comment does not fire
- [X] T056 [US1] Create `AnalysisEngineTests` in `tests/AkmlSql.Engine.Tests/Analysis/AnalysisEngineTests.cs` — integration tests: engine returns correct diagnostics for 3+ rules on known SQL; empty document returns empty issues; cancellation token aborts analysis

**Checkpoint**: SSMS 22 shows real-time squiggles for 10 rules. Error List panel populated. US1 fully testable.

---

## Phase 4: User Story 2 — One-Click Auto-Fix (Priority: P2)

**Goal**: A lightbulb appears on any fixable squiggle; clicking it applies the fix instantly in the editor.

**Independent Test**: Trigger rule BP004 by typing `WHERE col = NULL`. Hover over squiggle. Lightbulb icon appears. Click → menu shows "Replace with IS NULL". Select it → editor text changes to `WHERE col IS NULL`. Squiggle disappears. Ctrl+Z undoes the change.

### Fix Infrastructure

- [X] T057 [US2] Create `FixAction` class in `src/AkmlSql.Shell.Shared/Analysis/FixAction.cs` — implements `ISuggestedAction`; holds `ITextBuffer`, span, `FixActionInfo`; `Invoke()` applies `ITextBuffer.Replace(span, replacementText)` on the UI thread; `TryGetTelemetryId()` returns the rule ID
- [X] T058 [US2] Create `SuppressLineFixAction` class in `src/AkmlSql.Shell.Shared/Analysis/SuppressLineFixAction.cs` — implements `ISuggestedAction`; inserts `-- noqa: RULEID\n` on the line before the violation; handles file-scope suppression as a header comment
- [X] T059 [US2] Create `DisableRuleGloballyFixAction` class in `src/AkmlSql.Shell.Shared/Analysis/DisableRuleGloballyFixAction.cs` — implements `ISuggestedAction`; calls `ConfigManager` to set the rule's `enabled: false` in global `CodeAnalysisSettings`; fires `AnalysisSettingsChanged` notification to Engine
- [X] T060 [US2] Create `LightbulbProvider` class in `src/AkmlSql.Shell.Shared/Analysis/LightbulbProvider.cs` — implements `ISuggestedActionsSource`; exported as `[Export(typeof(ISuggestedActionsSourceProvider))]` MEF; `GetSuggestedActions()` finds `CodeIssueInfo` overlapping the requested span and returns one `SuggestedActionSet` per issue containing: one `FixAction` per `FixActionInfo`, one `SuppressLineFixAction`, one `DisableRuleGloballyFixAction`

### Fix Tests

- [X] T061 [P] [US2] Create `FixActionTests` in `tests/AkmlSql.Engine.Tests/Analysis/FixActionTests.cs` — verify that fix replacement text is correct for PE010 (`SELECT *` → `SELECT 1`), BP004 (`= NULL` → `IS NULL`), PE001 suppress produces `-- noqa: PE001`

**Checkpoint**: Lightbulb + fix menu fully functional. All fixable rules (PE001, PE002, PE009, PE010, BP001, BP004, DEP001) have working fix actions.

---

## Phase 5: User Story 3 — Rule Configuration (Priority: P3)

**Goal**: Users can enable/disable rules and change severity via Options; configuration exports to CAsettings JSON and a project-level file overrides global settings.

**Independent Test**: Open Options → Code Analysis tab. Disable rule PE009. Save. Open a stored procedure SQL file — no "Missing SET NOCOUNT ON" squiggle appears. Re-enable PE009. Squiggle reappears. Export settings to `team.casettings.json`. Create a new `.casettings` file in the SQL file's directory disabling PE003. The DELETE without WHERE error squiggle disappears for files in that directory only.

### Configuration Backend

- [X] T062 [US3] Extend `CaSettingsLoader` in `src/AkmlSql.Engine/Analysis/CaSettingsLoader.cs` — add `InvalidateCache()` method (called on `AnalysisSettingsChanged`); add file-system watcher per cached directory that auto-invalidates on `.casettings` change
- [X] T063 [P] [US3] Implement SQL Prompt CAsettings XML importer in `src/AkmlSql.Engine/Analysis/SqlPromptImporter.cs` — reads `<CASetting>` XML elements, maps known rule IDs (BP001–BP018, PE001–PE013, DEP001–DEP010, ST001–ST011, EX001–EX006) to AKML JSON format, writes output `.casettings` file; logs unmapped rules as skipped

### Options Dialog — Code Analysis Tab

- [X] T064 [US3] Add "Code Analysis" `TabPage` to `SettingsDialog` in `src/AkmlSql.Shell.Shared/Dialogs/SettingsDialog.cs` — contains: master enable/disable checkbox, "Run while typing" checkbox, "Run on save" checkbox, "Show in Error List" checkbox, a `DataGridView` listing all 200+ rules (columns: Enabled checkbox, Rule ID, Category, Description, Severity dropdown), and Import/Export CAsettings buttons
- [X] T065 [US3] Wire the Import CAsettings button to invoke file-open dialog → `CaSettingsLoader.LoadFromFile()` → populate grid; wire Export button to serialize current grid state → `CaSettings` JSON → file-save dialog
- [X] T066 [US3] On Settings dialog save, persist `CodeAnalysisSettings` changes via `ConfigManager.Save()`; fire `AnalysisSettingsChanged` RPC to Engine via `PipeClient`

### Configuration Tests

- [X] T067 [P] [US3] Create `CaSettingsLoaderTests` in `tests/AkmlSql.Engine.Tests/Analysis/CaSettingsLoaderTests.cs` — test: loads valid JSON; project-level file overrides global; missing file falls back to defaults; invalid JSON logs error and uses defaults; `InvalidateCache()` causes reload on next call

**Checkpoint**: Users can configure rules via Options dialog, export to file, and project-level `.casettings` takes precedence.

---

## Phase 6: User Story 4 — Bulk Analysis and Reporting (Priority: P4)

**Goal**: Users can analyze an entire folder of SQL files from the AKML menu and see a summary report. The CLI tool runs in CI/CD with correct exit codes.

**Independent Test (IDE)**: AKML SQL menu → Run Code Analysis → Analyze Directory → select a folder with 3 SQL files. Results dialog shows total issue count, grouped by category. Click an issue → correct SQL file opens at correct line number.

**Independent Test (CLI)**: `AkmlSql.Analyzer.exe --directory scripts/ --check --severity error` exits with code 1 when an error-severity issue exists; exits with code 0 when none exist.

### CLI Tool

- [X] T068 [US4] Create `Program.cs` in `src/AkmlSql.Analyzer/Program.cs` — parse CLI arguments (`--file`, `--directory`, `--recursive`, `--check`, `--severity`, `--settings`, `--report`, `--format`, `--rules`, `--exclude-rules`, `--version`, `--help`) per contracts/cli-interface.md; route to analysis and exit with correct code (0/1/2)
- [X] T069 [US4] Create `AnalyzerOptions` class in `src/AkmlSql.Analyzer/AnalyzerOptions.cs` — strongly-typed CLI options; `Parse(string[] args)` factory; validation (file-or-directory required, severity must be valid value)
- [X] T070 [US4] Create `ReportWriter` class in `src/AkmlSql.Analyzer/ReportWriter.cs` — serializes analysis results to JSON report format defined in contracts/cli-interface.md; handles `--format text` human-readable stdout and `--format json` / `--report file.json`
- [X] T071 [US4] Create `BatchFileAnalyzer` class in `src/AkmlSql.Analyzer/BatchFileAnalyzer.cs` — discovers `.sql` files in directory (recursive option); processes files sequentially (one at a time, streaming); returns aggregate `List<(file, IEnumerable<AnalysisDiagnostic>)>`; accepts `CancellationToken`

### Bulk Analysis IDE Command

- [X] T072 [US4] Create `BulkAnalysisCommand` in `src/AkmlSql.Shell.Shared/Commands/BulkAnalysisCommand.cs` — menu item under AKML SQL → Run Code Analysis; opens dialog to choose scope (current file / all open files / directory); calls Engine with `RequestAnalyze` for each target; aggregates results
- [X] T073 [US4] Create `BulkAnalysisResultDialog` in `src/AkmlSql.Shell.Shared/Dialogs/BulkAnalysisResultDialog.cs` — WinForms dialog showing: summary panel (total / by severity / by category), `DataGridView` with issue rows (file, line, rule, severity, message); double-click row navigates to `DTE.ItemOperations.OpenFile` + `TextSelection.MoveToLineAndOffset`

### CLI and Bulk Tests

- [X] T074 [P] [US4] Create `AnalyzerOptionsTests` in `tests/AkmlSql.Engine.Tests/Analysis/AnalyzerOptionsTests.cs` — test: valid args parsed correctly; missing required args throw; invalid severity value returns exit code 2
- [X] T075 [P] [US4] Create `ReportWriterTests` in `tests/AkmlSql.Engine.Tests/Analysis/ReportWriterTests.cs` — test: JSON report output matches schema in contracts/cli-interface.md; summary counts are correct; text format contains file/line/rule columns

**Checkpoint**: CLI tool usable in CI/CD. Bulk analysis command in SSMS navigates to issues.

---

## Phase 7: User Story 5 — Inline Suppression (Priority: P5)

**Goal**: `-- noqa: RULEID` comments suppress specific rules for a line. `-- noqa-begin` / `-- noqa-end` suppress all rules for a block.

**Independent Test**: Add `-- noqa: PE001` before a `SELECT *` line. Squiggle disappears for that line only. Other `SELECT *` lines in the same file still show squiggles. Remove the comment — squiggle returns on next analysis run.

### Suppression Engine

- [X] T076 [US5] Create `SuppressionParser` in `src/AkmlSql.Engine/Analysis/SuppressionParser.cs` — scans token stream for `-- noqa:` comments (case-insensitive); extracts rule IDs (comma-separated); builds `SuppressionMap` (SuppressedLines, SuppressedBlocks); fires Information diagnostic for unknown rule IDs in noqa comments; handles `-- noqa-begin` without matching `-- noqa-end` (suppress to EOF + Warning)
- [X] T077 [US5] Integrate `SuppressionParser` into `AnalysisEngine.AnalyzeAsync` in `src/AkmlSql.Engine/Analysis/AnalysisEngine.cs` — call `SuppressionParser.Parse(tokens)` before running rules; after rules complete, filter out any diagnostic whose `(Line, RuleId)` is covered by the `SuppressionMap`; also filter diagnostics covered by `CaSettings.GlobalSuppressions`

### Suppression Tests

- [X] T078 [P] [US5] Create `SuppressionParserTests` in `tests/AkmlSql.Engine.Tests/Analysis/SuppressionParserTests.cs` — test: `-- noqa: PE001` suppresses only PE001 on that line; `-- noqa: PE001, BP004` suppresses both; `-- noqa` (no rule IDs) suppresses all; `-- noqa-begin/end` block suppresses all in range; unknown rule ID in noqa generates Information diagnostic; `-- noqa-begin` without end suppresses to EOF

**Checkpoint**: Developers can suppress individual rules or blocks without disabling them globally.

---

## Phase 8: Remaining 190+ Rules

**Purpose**: Fill out the full rule library. Each task covers one rule category batch. Each rule follows the same `IAnalysisRule` + visitor pattern established in Phase 3 (US1 reference rules).

- [X] T079 [P] Implement PE011–PE020 (10 performance rules) in `src/AkmlSql.Engine/Analysis/Rules/Performance/` — PE011 ORDER_BY in INSERT, PE012 SET options recompilation, PE013 scalar function in WHERE, PE014 missing FK index, PE015 large IN list, PE016 correlated subquery, PE017 non-SARGable expression, PE018 table variable large dataset, PE019 missing clustered index, PE020 unused index
- [X] T080 [P] Implement PE021–PE035 (15 performance rules) in `src/AkmlSql.Engine/Analysis/Rules/Performance/` — covering DISTINCT misuse, UNION vs UNION ALL, excessive nesting, missing TOP, unnecessary GROUP BY, and 10 additional performance patterns per PRD section 4.1
- [X] T081 [P] Implement BP002–BP003, BP005–BP016 (14 best practice rules) in `src/AkmlSql.Engine/Analysis/Rules/BestPractices/` — TRY_CONVERT, missing error handling, EXEC(string), missing transaction, empty CATCH, missing RETURN, unused variable, unread variable, SET XACT_ABORT, hard-coded date, non-parameterized dynamic SQL, missing column list in INSERT, BEGIN/END with IF
- [X] T082 [P] Implement BP017–BP030 (14 best practice rules) in `src/AkmlSql.Engine/Analysis/Rules/BestPractices/` — GOTO usage, nested IF depth, magic numbers, output parameter not set, and 10 additional BP patterns per PRD section 4.2
- [X] T083 [P] Implement SE002–SE010 (9 security rules) in `src/AkmlSql.Engine/Analysis/Rules/Security/` — hardcoded password, GRANT to public, EXECUTE AS OWNER, TRUSTWORTHY, weak hash, cross-db chaining, xp_cmdshell, OPENROWSET, unencrypted connection string
- [X] T084 [P] Implement SE011–SE020 (10 security rules) in `src/AkmlSql.Engine/Analysis/Rules/Security/` — sa login usage, blank passwords, certificate expiry, and 7 additional security patterns per PRD section 4.3
- [X] T085 [P] Implement ST001–ST009 (9 style rules) in `src/AkmlSql.Engine/Analysis/Rules/Style/` — keyword casing, old-style alias `=`, old-style JOIN, missing semicolon, inconsistent alias, unnecessary brackets, missing schema prefix, inconsistent indentation
- [X] T086 [P] Implement ST010–ST025 (16 style rules) in `src/AkmlSql.Engine/Analysis/Rules/Style/` — naming conventions, comment style, whitespace consistency per PRD section 4.4
- [X] T087 [P] Implement DEP002–DEP020 (19 deprecated rules) in `src/AkmlSql.Engine/Analysis/Rules/Deprecated/` — deprecated system procedures, SET FMTONLY, old-style outer join, RAISERROR old syntax, numbered procedures, GROUP BY ALL, deprecated hint syntax, COMPUTE, WRITETEXT, legacy backup syntax per PRD section 4.5
- [X] T088 [P] Implement DE001–DE025 (25 design rules) in `src/AkmlSql.Engine/Analysis/Rules/Design/` — missing PK, missing clustered index, nullable PK column, VARCHAR(1/2), float for financial data, SQL_VARIANT, IDENTITY on non-integer, missing description, table naming, excessive columns, circular FKs, trigger complexity per PRD section 4.6
- [X] T089 [P] Implement EX001–EX020 (20 execution rules) in `src/AkmlSql.Engine/Analysis/Rules/Execution/` — division by zero, data truncation, ambiguous column, unreachable code, identical branch conditions, always-true/false, overflow risks, timezone issues, collation conflicts per PRD section 4.7
- [X] T090 [P] Implement NM001–NM025 (25 naming rules) in `src/AkmlSql.Engine/Analysis/Rules/Naming/` — reserved word as identifier, sp_ prefix, Hungarian notation, inconsistent naming convention, special characters, single-letter alias, length limits, prefix/suffix patterns, abbreviation consistency per PRD section 4.8

### Rule Tests (Bulk)

- [X] T091 [P] Create test classes for PE011–PE035 in `tests/AkmlSql.Engine.Tests/Analysis/Rules/Performance/` — at minimum: one trigger test + one false-positive test per rule
- [X] T092 [P] Create test classes for BP002–BP030 in `tests/AkmlSql.Engine.Tests/Analysis/Rules/BestPractices/`
- [X] T093 [P] Create test classes for SE002–SE020 in `tests/AkmlSql.Engine.Tests/Analysis/Rules/Security/`
- [X] T094 [P] Create test classes for ST001–ST025 in `tests/AkmlSql.Engine.Tests/Analysis/Rules/Style/`
- [X] T095 [P] Create test classes for DEP002–DEP020 in `tests/AkmlSql.Engine.Tests/Analysis/Rules/Deprecated/`
- [X] T096 [P] Create test classes for DE001–DE025 in `tests/AkmlSql.Engine.Tests/Analysis/Rules/Design/`
- [X] T097 [P] Create test classes for EX001–EX020 in `tests/AkmlSql.Engine.Tests/Analysis/Rules/Execution/`
- [X] T098 [P] Create test classes for NM001–NM025 in `tests/AkmlSql.Engine.Tests/Analysis/Rules/Naming/`

---

## Phase 9: Polish & Cross-Cutting Concerns

**Purpose**: QA sweep, performance validation, build integration, installer update.

- [X] T099 Add `AkmlSql.Analyzer` publish step to `build.ps1` — `dotnet publish src/AkmlSql.Analyzer/AkmlSql.Analyzer.csproj -c Release -r win-x64 -v quiet --nologo`
- [X] T100 [P] Add `AkmlSql.Analyzer` to the Inno Setup installer script `src/AkmlSql.Installer/AkmlSqlSetup.iss` — copy `AkmlSql.Analyzer.exe` to install directory; add to system PATH entry section
- [X] T101 [P] Run false-positive sweep: create a test SQL file containing 50+ valid, well-written stored procedures; verify no PE/BP/ST rules fire on correct code; document any false-positive discoveries
- [X] T102 [P] Run performance benchmarks: measure analysis time for a 1,000-line SQL file (target < 200ms) and a 10,000-line file (target < 1s); add benchmark results as comments in `AnalysisEngine.cs`
- [X] T103 Add `Tests: Analyzer` step to `build.ps1` — `dotnet test tests/AkmlSql.Engine.Tests/AkmlSql.Engine.Tests.csproj -c Release -v quiet --nologo` (engine tests already cover analysis rules)
- [X] T104 [P] Verify all 6 shell targets compile cleanly with the new `Analysis/` files in `AkmlSql.Shell.Shared` — rebuild each shell project individually with MSBuild; no new VSTHRD warnings should be introduced (or they should be justified suppressions)
- [X] T105 [P] Update `SettingsDialog` snapshot tests (if any exist) for the new Code Analysis tab — verify dialog renders without exception on SSMS 22
- [X] T106 [P] Add `Build: Analyzer CLI` section to `doc/progress.md` documenting: CLI tool usage examples, how to import SQL Prompt settings, how to configure CAsettings in a CI/CD pipeline

---

## Dependencies & Execution Order

### Phase Dependencies

- **Phase 1 (Setup)**: No dependencies — start immediately
- **Phase 2 (Foundational)**: Depends on Phase 1 — BLOCKS all user story phases
- **Phase 3 (US1 — Real-Time Detection)**: Depends on Phase 2
- **Phase 4 (US2 — Auto-Fix)**: Depends on Phase 3 (fix actions attach to squiggles that US1 creates)
- **Phase 5 (US3 — Configuration)**: Depends on Phase 2 only (CaSettings feeds into engine, independent of shell UI)
- **Phase 6 (US4 — Bulk + CLI)**: Depends on Phase 2 only (CLI uses engine directly, no shell dependency)
- **Phase 7 (US5 — Suppression)**: Depends on Phase 2 only (suppression is engine-side logic)
- **Phase 8 (Remaining Rules)**: Depends on Phase 2 (all rules implement `IAnalysisRule` established in Phase 2)
- **Phase 9 (Polish)**: Depends on all prior phases

### User Story Dependencies

- **US1 (P1)**: Requires Phase 2 complete
- **US2 (P2)**: Requires US1 complete (fix actions are surfaced by the same squiggles)
- **US3 (P3)**: Requires Phase 2 complete — independent of US1/US2
- **US4 (P4)**: Requires Phase 2 complete — independent of US1/US2/US3
- **US5 (P5)**: Requires Phase 2 complete — independent of US1/US2/US3/US4

### Within Each Phase

- IPC models (T016–T023) must be complete before Engine models (T028–T038)
- `AnalysisEngine` (T036) must be complete before PipeRpcServer wiring (T037)
- `RuleRegistry` (T034) must be complete before `AnalysisEngine` (T036)
- `AnalysisController` (T049) must be complete before `DiagnosticTagger` (T050) and `ErrorListReporter` (T051)

---

## Parallel Opportunities

### Phase 2 — Run Together

```
T016 DiagnosticSeverity enum          T017 FixType enum
T018 SuppressScope enum               T019 FixActionInfo model
T020 CodeIssueInfo model              T021 CodeAnalysisRequest model
T022 CodeAnalysisResponse model       T025 CaSettings model
T026 RuleConfig model                 T027 GlobalSuppression model
```

### Phase 3 — Reference Rules Run Together (after T038)

```
T039 PE001_AvoidSelectStar            T040 PE002_UnqualifiedObjectName
T041 PE003_MissingWhere               T042 PE004_LikeLeadingWildcard
T043 PE009_MissingSetNoCount          T044 PE010_SelectStarInExists
T045 BP004_NullComparison             T046 BP001_UseScopeIdentity
T047 SE001_SqlInjection               T048 DEP001_DeprecatedDataType
```

### Phase 8 — All Rule Batches Run Together (after Phase 2)

```
T079–T090 (all 12 rule batch tasks) can be implemented fully in parallel
T091–T098 (all 8 test batch tasks) can be implemented fully in parallel
```

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Phase 1: Setup (T001–T015)
2. Phase 2: Foundational (T016–T038) — CRITICAL gate
3. Phase 3: US1 (T039–T056) — 10 reference rules + shell squiggles
4. **STOP and VALIDATE**: Open SSMS 22, type `DELETE FROM dbo.Orders`, confirm red Error squiggle appears with message "DELETE without WHERE clause". Type `SELECT * FROM Orders` in a stored procedure — confirm warning squiggle. Check Error List panel shows the issues.
5. **MVP is shippable**: Real-time analysis with 10 rules is immediately useful.

### Incremental Delivery

1. Foundation (Phase 2) → MVP (US1) → Fix Menu (US2) → Configuration (US3) → CLI (US4) → Suppression (US5) → Full Rule Set (Phase 8)
2. Each phase adds value without breaking prior functionality
3. Phase 8 (remaining rules) can be developed in parallel with Phase 5–7 by different contributors

### Parallel Team Strategy

After Phase 2 (Foundational) is complete:
- **Developer A**: Phase 3 (US1 shell integration) → Phase 4 (US2 fix menu)
- **Developer B**: Phase 5 (US3 configuration) + Phase 7 (US5 suppression)
- **Developer C**: Phase 6 (US4 CLI tool) + Phase 8 rule batches (T079–T090)

---

## Notes

- `[P]` tasks operate on different files with no incomplete-task dependencies — safe to implement in parallel
- `[USn]` label maps each task to its user story for traceability
- Each user story phase ends with a named **Checkpoint** — validate independently before proceeding
- Rule tests follow the three-assertion pattern: trigger case, false-positive (non-trigger) case, suppression case
- The `AnalysisEngineTestHelper` (T038) is used by all rule tests — build it first within Phase 2
- Rules that `RequiresSchema = true` should skip gracefully when `ctx.SchemaCache == null` (return empty)
- All 200+ rules are auto-discovered by `RuleRegistry` — no manual registration needed after Phase 2 (T034)
