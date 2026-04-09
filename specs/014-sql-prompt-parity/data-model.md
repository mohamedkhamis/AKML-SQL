# Phase 1 Data Model: SQL Prompt Parity

**Branch**: `014-sql-prompt-parity` | **Date**: 2026-04-09

This document captures every entity introduced by spec 014. Each entry has: **Name**, **Fields** (with types and notes), **Persistence** (where it lives), **Relationships**, **Validation**, and (for stateful entities) **State Transitions**.

Entities are grouped by user story.

---

## US1 — Pre-execution safety

### `ExecutionSafetyRule`

| Field | Type | Notes |
|---|---|---|
| `Id` | `string` | Stable identifier (e.g. `DELETE_NO_WHERE`, `UPDATE_NO_WHERE`, `MERGE_NO_FILTER`, `INSIDE_JOIN`, `INSIDE_PROC`) |
| `Severity` | `enum { Warning, Critical }` | All five default rules are `Critical` |
| `Enabled` | `bool` | Per-rule toggle |
| `MessageTemplate` | `string` | Composable template with `{statementType}`, `{server}`, `{database}`, `{environment}` placeholders |
| `EnvironmentOverride` | `Dictionary<string, bool>?` | Optional per-environment enable/disable (e.g. always-on for `Production`) |

**Persistence**: `AppSettings.ExecutionWarnings.Rules[]`.
**Validation**: `Id` must be unique; `MessageTemplate` must include at least `{statementType}`.
**Relationships**: Consumed by `SafetyCheckHandler` (engine) and `SafetyWarningDialog` (shell).

### `ExecutionWarningDialogState`

Transient (no persistence). Tracks per-session opt-outs:

| Field | Type | Notes |
|---|---|---|
| `SessionId` | `string` | The IPC session id |
| `SuppressedRuleIds` | `HashSet<string>` | Rules the user opted out of for this session (FR-006) |
| `LastShownAtUtc` | `DateTime` | For rate-limiting repeated warnings |

**Lifecycle**: Created on first warning; cleared when SSMS closes the document (text-view-closed event).

---

## US2 — Column Picker

### `ColumnPickerSelection`

Transient (no persistence). Lives only while the picker popup is open:

| Field | Type | Notes |
|---|---|---|
| `ParentTable` | `(string Schema, string Name)` | The table the picker is bound to |
| `ParentAlias` | `string?` | The alias if multiple tables are in scope |
| `Selected` | `List<ColumnEntry>` | In insertion order (FR-014) |
| `Filter` | `string` | Live filter input |
| `SortMode` | `enum { TableOrder, Alphabetical }` | Toggle (FR-011) |

### `ColumnEntry`

| Field | Type | Notes |
|---|---|---|
| `Name` | `string` | |
| `DataType` | `string` | |
| `IsPrimaryKey` | `bool` | Renders `⚷` badge |
| `IsForeignKey` | `bool` | Renders `🔗` badge |
| `IsNullable` | `bool` | |
| `Ordinal` | `int` | Drives `TableOrder` sort |

**Source**: Read from existing `DatabaseCache.Tables[].Columns` — no new schema query.

---

## US5 — Tab coloring

### `Environment`

| Field | Type | Notes |
|---|---|---|
| `Name` | `string` | E.g. `Production`, `Staging`, `Development` |
| `ColorHex` | `string` | `#RRGGBB` |
| `GradientEnabled` | `bool` | Per-environment override of the global gradient setting |
| `Label` | `string?` | Optional tooltip text |

**Persistence**: `AppSettings.TabColoring.Environments[]`.
**Validation**: `Name` unique; `ColorHex` matches `^#[0-9A-Fa-f]{6}$`.

### `TabColorAssignment`

| Field | Type | Notes |
|---|---|---|
| `Scope` | `enum { Server, Database, ServerGroup }` | |
| `ScopeValue` | `string` | E.g. server name, database name, registered-server-group id |
| `EnvironmentName` | `string` | FK to `Environment.Name` |
| `Priority` | `int` | Higher wins (server > database > group; FR-045) |

**Persistence**: `AppSettings.TabColoring.Assignments[]`.

---

## US6 — Code Analysis Issues window

### `AnalysisIssue`

| Field | Type | Notes |
|---|---|---|
| `RuleId` | `string` | E.g. `BP002`, `PE001` |
| `Severity` | `enum { Info, Warning, Error, Hint }` | |
| `Description` | `string` | Short rule description |
| `ProblemText` | `string` | Long-form text shown in Issue Details popup (FR-080) |
| `RemediationText` | `string` | Long-form remediation guidance |
| `StartLine` | `int` | 1-based |
| `StartColumn` | `int` | 1-based |
| `EndLine` | `int` | |
| `EndColumn` | `int` | |
| `IsAutoFixable` | `bool` | Drives the lightbulb color (orange vs blue, FR-079) |
| `Category` | `enum { BP, PE, ST, SE, DE, DEP, EX, NM }` | |

**Source**: Existing `AnalysisEngine.RunAsync()` already produces this; spec 014 adds `ProblemText`, `RemediationText`, and `IsAutoFixable` to the existing record.

### `AnalysisIssuesPushed` (notification, not request)

| Field | Type | Notes |
|---|---|---|
| `SessionId` | `string` | |
| `DocumentPath` | `string` | |
| `Issues` | `AnalysisIssue[]` | Full set for the active document |
| `RunAtUtc` | `DateTime` | |

**Persistence**: None — notifications are ephemeral.

---

## US7 — Refactoring chord family

No new entities. Each chord dispatches to an existing `RefactoringEngine` operation. The chord-handler classes hold no state.

---

## US8 — Object Definition Box

### `ObjectDefinition`

| Field | Type | Notes |
|---|---|---|
| `ObjectType` | `enum { Table, View, Procedure, Function, Trigger, Synonym, Type }` | |
| `Schema` | `string` | |
| `Name` | `string` | |
| `Columns` | `ColumnEntry[]?` | Tables and views |
| `Parameters` | `ParameterEntry[]?` | Procedures and functions |
| `RowCountApprox` | `long?` | Tables only (from existing schema cache) |
| `CreateScript` | `string` | The CREATE statement |
| `WasDecrypted` | `bool` | True if the script was decrypted via DAC (FR-098) |
| `RetrievedAtUtc` | `DateTime` | For cache-busting |

### `ParameterEntry`

| Field | Type | Notes |
|---|---|---|
| `Name` | `string` | |
| `DataType` | `string` | |
| `Direction` | `enum { In, Out, InOut }` | |
| `DefaultValue` | `string?` | |
| `Ordinal` | `int` | |

---

## US9 — Formatting markers

### `FormattingDisableRegion`

Pure text artefact, not a runtime entity. Defined for documentation completeness:

| Field | Type | Notes |
|---|---|---|
| `StartOffset` | `int` | Position of `-- akml-format off` marker |
| `EndOffset` | `int` | Position of matching `-- akml-format on` marker, or end of document |

**Source**: Detected on the fly by `NoformatScanner` (existing).

---

## US13 — Script navigation

### `ScriptOutlineNode`

| Field | Type | Notes |
|---|---|---|
| `Id` | `Guid` | |
| `ParentId` | `Guid?` | For nested CTEs and `EXEC AS REVERT` pairs |
| `StatementType` | `enum { Use, Create, Alter, Select, Insert, Update, Delete, Exec, ExecAs, Revert, Drop, Truncate, Merge, Other }` | |
| `Label` | `string` | E.g. `CREATE PROCEDURE dbo.MyProc` |
| `StartLine` | `int` | |
| `StartColumn` | `int` | |
| `EndLine` | `int` | |
| `EndColumn` | `int` | |

**Persistence**: None — recomputed on every Summarize Script invocation.
**Source**: New `SummarizeScriptEngine` walks `ScriptDom` AST.

### `UnusedDeclaration`

| Field | Type | Notes |
|---|---|---|
| `Kind` | `enum { Variable, Parameter }` | |
| `Name` | `string` | E.g. `@unused` |
| `DeclaredLine` | `int` | |
| `DeclaredColumn` | `int` | |
| `EnclosingObject` | `string?` | Procedure / function name if applicable |

**Source**: New `FindUnusedEngine` does a single AST walk per script.

---

## US14 — Find Invalid Objects

### `InvalidObjectRecord`

| Field | Type | Notes |
|---|---|---|
| `Schema` | `string` | |
| `Name` | `string` | |
| `Type` | `enum { Table, View, Procedure, Function, Trigger, Synonym }` | |
| `ErrorMessage` | `string` | The SQL Server-emitted message |
| `SourceLine` | `int?` | Line in the object's definition where the bad reference occurs |
| `MissingDependency` | `string?` | The object the broken reference points at, if known |
| `ScannedAtUtc` | `DateTime` | |

**Persistence**: None — the tool window holds the live result set; the user clicks Refresh to re-scan.

---

## US15 — Smart Rename

### `SmartRenamePlan`

| Field | Type | Notes |
|---|---|---|
| `TargetIdentifier` | `(string Schema, string Name, string? ColumnOrParam)` | |
| `NewName` | `string` | |
| `Dependencies` | `RenameDependency[]` | |
| `Warnings` | `string[]` | |
| `PreservedPermissions` | `PermissionEntry[]` | |
| `PreservedExtendedProperties` | `ExtendedPropertyEntry[]` | |
| `GeneratedScript` | `string` | The full `BEGIN TRAN ... sp_rename + ALTER ... COMMIT` script |
| `HasUnresolvedCollision` | `bool` | Disables the Apply button (FR-073) |

### `RenameDependency`

| Field | Type | Notes |
|---|---|---|
| `Schema` | `string` | |
| `Name` | `string` | |
| `Type` | `enum { Table, View, Procedure, Function, Trigger }` | |
| `RewrittenDefinition` | `string` | The new ALTER body |

**State Transitions**: `Drafted → Previewed → (Applied | RolledBack | Cancelled)`. The plan is destroyed when the dialog closes.

---

## US16 — Result-grid productivity

### `ResultGridContext`

| Field | Type | Notes |
|---|---|---|
| `Schema` | `string?` | Best-effort guess from the originating query |
| `Table` | `string?` | Best-effort guess |
| `Columns` | `ResultGridColumn[]` | Always populated |
| `Rows` | `ResultGridRow[]` | Selected rows, or all visible rows if none selected |

### `ResultGridColumn`

| Field | Type | Notes |
|---|---|---|
| `Name` | `string` | |
| `SqlType` | `string` | E.g. `INT`, `NVARCHAR(50)`, `DECIMAL(38,4)` |
| `IsIdentity` | `bool` | Drives `SET IDENTITY_INSERT` opt-in |
| `IsNullable` | `bool` | |

### `ResultGridScript`

| Field | Type | Notes |
|---|---|---|
| `Mode` | `enum { CopyAsInClause, ScriptAsInsert, OpenInExcel }` | |
| `Payload` | `string` | The clipboard content for the first two modes; an Excel file path for the third |
| `Warnings` | `string[]` | E.g. "10 NULL values omitted from IN clause" |

---

## US17 — Lightbulb fixes

### `LightbulbFix`

| Field | Type | Notes |
|---|---|---|
| `RuleId` | `string` | FK to `AnalysisIssue.RuleId` |
| `IsAutoFixable` | `bool` | |
| `FixRoutineId` | `string` | E.g. `RewriteNotEqualsOperator`, `RemoveOuterParens` |
| `RequiresSchemaPhaseB` | `bool` | If true, the fix is queued until Phase B completes (FR-083) |

**Source**: New `AnalysisFixDispatcher` registers one entry per known auto-fix (~27 rules per A17).

---

## US18 — AI feature reach

### `AiConversationTurn`

| Field | Type | Notes |
|---|---|---|
| `Id` | `Guid` | |
| `Timestamp` | `DateTime` | |
| `SourceAction` | `enum { Explain, Fix, Optimize, CommentToSql, Manual, IndexAnalysis, FixOnError }` | |
| `Prompt` | `string` | The user input or selected SQL |
| `Answer` | `string` | The AI response |
| `TokenCount` | `int` | |
| `FollowupSuggestions` | `string[]` | Up to 3 (FR-090) |

**Persistence**: In-memory per AI panel session. Cleared on AKML SQL extension unload.

### `IndexAnalysisRecommendation`

| Field | Type | Notes |
|---|---|---|
| `ExistingPlanSummary` | `string` | One-line summary of the current execution plan |
| `HintedPlanSummary` | `string` | One-line summary of the hinted plan |
| `EstimatedImpactPercent` | `double` | E.g. `42.0` for 42% improvement |
| `CreateIndexScript` | `string` | Ready-to-paste `CREATE NONCLUSTERED INDEX` |
| `Confidence` | `enum { High, Medium, Low }` | `Low` when statistics are missing (edge case) |

---

## US19 — Completion polish

### `CompletionToggleState`

| Field | Type | Notes |
|---|---|---|
| `Suppressed` | `bool` | Per-session, runtime-only |

**Persistence**: None — resets to `false` on SSMS restart (edge case in spec).

### `CommitKeySet`

| Field | Type | Notes |
|---|---|---|
| `Keys` | `HashSet<CommitKey>` | Default `{ Tab, Enter }` |

### `CommitKey` (enum)

`Tab`, `Enter`, `Space`, `Dot`, `Comma`, `OpenParen`

**Persistence**: `AppSettings.CompletionPolish.CommitKeys[]`.

### `TempTableSchema`

| Field | Type | Notes |
|---|---|---|
| `TableName` | `string` | E.g. `#temp` or `##temp` |
| `Columns` | `ColumnEntry[]` | Parsed from CREATE / SELECT INTO |
| `DefinedAtOffset` | `int` | First definition offset in the script |
| `DroppedAtOffset` | `int?` | If `DROP TABLE #temp` is encountered later |

**Persistence**: None — recomputed per keystroke by `TempTableProvider`.

---

## US20 — Execution shortcuts and Browse Open Tabs

No new persisted entities. The two new execute commands operate on the existing text view; Browse Open Tabs reads `EnvDTE.DTE.Documents` live.

---

## Cross-cutting: AppSettings additions

The complete list of new `AppSettings` sections (mirrors R-012):

```text
AppSettings.ExecutionWarnings   // US1
AppSettings.TabColoring          // US5
AppSettings.CommandPalette       // US4
AppSettings.Ai                   // US10, US18
AppSettings.CompletionPolish     // US2, US8, US19
AppSettings.ResultGrid           // US16
AppSettings.Lightbulbs           // US17
AppSettings.Navigation           // US13, US20
```

**Validation**: every section round-trips through the existing `ConfigManager.Save / Load` test (extend `tests/AkmlSql.Core.Tests/Config/AppSettingsTests.cs` to cover the new properties).
