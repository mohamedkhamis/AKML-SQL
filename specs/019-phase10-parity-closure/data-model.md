# Phase 1 Data Model: Phase 10 — SQL Prompt Parity Closure & Bug Fixes

This document captures every Key Entity surfaced by the spec, with field names, types, validation rules, and the layer at which it is persisted or held. Entities map to either:

- **Wire format** (MessagePack-serialised over named pipe) — already-shipped DTOs reused; called out for traceability.
- **Settings** (`config.json` — persisted across sessions) — extended by this spec.
- **In-memory** (transient, never persisted) — created per editor session or per dialog invocation.

The persistence layer is noted on each entity.

---

## 1. Column Picker Selection (US2, in-memory)

**Layer**: In-memory only; created when `Ctrl+Left Arrow` opens the picker and discarded when the picker closes (`Esc`, `Enter`, `Tab`, or focus loss).

**Fields**:

| Field | Type | Notes |
|---|---|---|
| `TableSchema` | `string` | Schema of the parent table, e.g. `"dbo"`. Required. |
| `TableName` | `string` | Name of the parent table, e.g. `"Customers"`. Required. |
| `TableAlias` | `string?` | Alias from the parent FROM/JOIN clause, e.g. `"c"`. Null when no alias is in scope. |
| `OtherTablesInScope` | `IReadOnlyList<string>` | Aliases of every other table currently in scope via FROM/JOIN. Used to decide if insertion needs alias qualification. |
| `AvailableColumns` | `IReadOnlyList<ColumnInfo>` | Snapshot from `DatabaseCache.GetTable(...).Columns` at the moment the picker opens. |
| `SelectedColumns` | `List<string>` | Column names selected by the user, in insertion order (Space-toggle order, not table order). |
| `SortMode` | `enum { TableOrder, Alphabetical }` | Default `TableOrder`. Toggled by a button at the top of the picker. |
| `Filter` | `string` | Live filter text (typed inside the picker after opening). Empty by default. |

**Validation rules**:
- `SelectedColumns` MUST be a subset of `AvailableColumns[*].Name`.
- Insertion-order is preserved: when `SelectedColumns` is committed to the editor, columns are emitted in the order the user added them.
- When `OtherTablesInScope.Count > 0`, the insertion qualifies each name with `TableAlias`. Otherwise the insertion uses bare column names.

**State transitions**:
- `Opened` → user typing filters via `Filter` → user `Space` adds to `SelectedColumns` / removes → user `Enter` or `Tab` → `Committed` (insertion runs, picker closes) → discarded.
- `Opened` → user `Esc` → `Closed` (no insertion) → discarded.
- `Opened` → user `Ctrl+Right Arrow` → picker hides, suggestion list regains focus → discarded.

---

## 2. Analysis Issue Display Row (US3, in-memory)

**Layer**: In-memory `ObservableCollection<AnalysisIssueDisplayRow>` inside `CodeAnalysisIssuesWindow`. Reconstructed on each `AnalysisCompleted` event.

**Fields**:

| Field | Type | Notes |
|---|---|---|
| `RuleId` | `string` | e.g. `"BP002"`. Required. Foreign-key reference to the rule catalog. |
| `Severity` | `enum { Ignore, Warning, Error }` | Mirrors the rule's per-document effective severity (rule default overridden by `.casettings` and inline `-- akml-disable`/`-- akml-enable` directives). |
| `Description` | `string` | Rule short description (≤ 80 chars). Required. |
| `Line` | `int` | 1-based line number. Required. |
| `Column` | `int` | 1-based column number. Required. |
| `EndLine` | `int` | 1-based line where the span ends. Required (equals `Line` for single-line spans). |
| `EndColumn` | `int` | 1-based column where the span ends. Required. |
| `IsAutoFixable` | `bool` | True when the rule has a registered fix routine in `RefactoringEngine`. |
| `SourceFindingRef` | `AnalysisFinding` | Underlying engine finding used for click-to-navigate to the editor span. |

**Validation rules**:
- `Line` ≤ `EndLine`. If equal, `Column` ≤ `EndColumn`.
- `RuleId` MUST match an entry in `AnalysisEngine.RuleRegistry`.

---

## 3. Lightbulb Fix Descriptor (US3, in-memory)

**Layer**: In-memory; held by `LightbulbDetailsPopup` for the duration of the popup. Queued via `Dictionary<DiagnosticSpan, FixDescriptor>` when the fix needs schema metadata not yet loaded.

**Fields**:

| Field | Type | Notes |
|---|---|---|
| `RuleId` | `string` | Required. |
| `IsAutoFixable` | `bool` | If false, the popup omits the **Apply Fix** button. |
| `ProblemText` | `string` | Rule-specific problem statement (≤ 200 chars). |
| `RemediationText` | `string` | Rule-specific remediation paragraph (≤ 500 chars). |
| `FixRoutineRef` | `Func<ITextEdit, ITextEdit>?` | Reference to the `RefactoringEngine` fix routine. Null when `IsAutoFixable == false`. |
| `RequiresSchemaPhaseB` | `bool` | True when the fix depends on column metadata; the fix is queued if Phase B is not yet loaded. |

**State transitions**:
- `Created` → user clicks **Apply Fix** → `Applied` (text replaced, popup closes) → discarded.
- `Created` → `RequiresSchemaPhaseB == true` and Phase B not loaded → `Queued` → `SchemaCacheManager.PhaseBLoaded` event fires → `Applied`.
- `Created` → user clicks **Dismiss** or popup loses focus → `Discarded`.

---

## 4. Tab Color Assignment (US4, settings)

**Layer**: Settings (`AppSettings.Tabs.Assignments` in `config.json`).

**Fields**:

| Field | Type | Notes |
|---|---|---|
| `Scope` | `enum { Server, Database, ServerGroup }` | Determines how the assignment matches. |
| `Pattern` | `string` | Server name / `server.database` / Registered Server Group id. Globs supported (e.g., `*PROD*`). |
| `EnvironmentName` | `string` | Foreign key into `AppSettings.Tabs.Environments[].Name`. |

**Priority resolution** (per FR-045): `Server` > `Database` > `ServerGroup`. A specific `Server` assignment overrides a `ServerGroup` assignment of which the server is a member. A `Database`-scope assignment is most-specific and wins over `Server`.

**Validation rules**:
- `EnvironmentName` MUST match an entry in `AppSettings.Tabs.Environments`.
- `Pattern` MUST be non-empty.
- Glob characters `*` and `?` are honoured by `EnvironmentMatcher` (already implemented).

---

## 5. Environment (US4, settings)

**Layer**: Settings (`AppSettings.Tabs.Environments` in `config.json`).

**Fields**:

| Field | Type | Notes |
|---|---|---|
| `Name` | `string` | Display name, e.g. `"Production"`, `"Custom-UAT"`. Required, unique. |
| `Color` | `string` (hex `#RRGGBB`) | Base color, e.g. `"#E74C3C"`. Required. |
| `GradientEnabled` | `bool` | True → lighter-at-top gradient. Default `true`. |
| `HighContrastClampEnabled` | `bool` | True → clamp under Windows High Contrast. Default `true`. |

**Validation rules**:
- `Color` MUST match `^#[0-9A-Fa-f]{6}$`.
- `Name` MUST be ≤ 50 chars and unique within `AppSettings.Tabs.Environments`.

---

## 6. Command Palette Entry (US6, in-memory)

**Layer**: In-memory; aggregated per keystroke by `CommandPaletteWindow`.

**Fields**:

| Field | Type | Notes |
|---|---|---|
| `Label` | `string` | Display text, e.g. `"Format Document"`. Required. |
| `Category` | `enum { AkmlCommand, AkmlOption, HostCommand, DatabaseObject }` | Determines the badge. Required. |
| `MatchScore` | `int` | Fuzzy-match score from `FuzzyMatcher.Match(query, label)`. 0-1000 range. |
| `IconResourceKey` | `string?` | Optional `ThemeTokens` key for an icon resource. |
| `Invoke` | `Func<Task>` | Action that runs when the user picks this entry. |
| `Tooltip` | `string?` | Optional secondary text (e.g., Options page path). |

**Ranking**:
- Sort by `MatchScore` descending. Tiebreak by `Category` priority: `AkmlCommand > AkmlOption > HostCommand > DatabaseObject`.

---

## 7. Command Palette Recent Items (US6, settings)

**Layer**: Settings (`AppSettings.CommandPalette.RecentItems` in `config.json`). Stored per-host.

**Fields**:

| Field | Type | Notes |
|---|---|---|
| `RecentItems` | `Dictionary<string, List<string>>` | Key: host id (`"SSMS"`, `"VS"`). Value: list of recent entry labels (most-recent first, max 10). |

**Validation rules**:
- Max 10 entries per host (older entries are evicted on each new pick).
- Labels are case-sensitive and matched exactly against the next palette session's available entries; entries that no longer exist (e.g., command removed) are silently skipped.

---

## 8. Script Outline Node (US7, in-memory)

**Layer**: In-memory tree rendered in the Summarize Script dialog.

**Fields**:

| Field | Type | Notes |
|---|---|---|
| `StatementType` | `enum { Create, Alter, Select, Insert, Update, Delete, Exec, Use, Cte, Other }` | Required. |
| `Label` | `string` | Display label, e.g. `"CREATE PROCEDURE dbo.MyProc (line 42)"`. Required. |
| `ParentId` | `int?` | Parent node id for nesting (CTEs inside a SELECT, statements inside a procedure body). Null for root. |
| `LineStart` | `int` | 1-based line number where the statement starts. Required. |
| `LineEnd` | `int` | 1-based line number where the statement ends. |
| `Offset` | `int` | Editor offset for click-to-navigate. Required. |

---

## 9. Invalid Object Record (US8, wire format — already shipped)

**Layer**: Wire format. DTO already defined in `src/AkmlSql.Core/Ipc/Messages/InvalidObjectRecord.cs` (shipped by spec 014 Phase 2).

**Fields** (per existing DTO):

| Field | Type | Notes |
|---|---|---|
| `ObjectName` | `string` | Bare object name. |
| `SchemaName` | `string` | Schema name. |
| `ObjectType` | `string` | `"TABLE"`, `"VIEW"`, `"PROCEDURE"`, `"FUNCTION"`, `"TRIGGER"`, `"SYNONYM"`. |
| `ErrorMessage` | `string` | Human-readable error from the catalog query (e.g., `"Invalid column 'OldCol' in view dbo.MyView"`). |
| `LineNumber` | `int` | 1-based line number in the object's definition. 0 if unknown. |
| `BrokenDependencyName` | `string?` | Name of the missing referent (e.g., `"dbo.DroppedTable"`). Null if not extractable. |

**Validation rules**:
- `ObjectName`, `SchemaName`, `ObjectType`, `ErrorMessage` MUST be non-empty.
- `LineNumber` ≥ 0.

---

## 10. Smart Rename Plan (US10, in-memory)

**Layer**: In-memory; created when the user invokes Smart Rename and discarded after Apply (or Cancel). The preview is not persisted.

**Fields**:

| Field | Type | Notes |
|---|---|---|
| `TargetIdentifier` | `IdentifierRef` | Schema + Name + Kind (Table / Column / Procedure / Function / Parameter). Required. |
| `NewIdentifier` | `string` | The new name. Required. |
| `DependentObjects` | `IReadOnlyList<DependentObjectInfo>` | Every object that references the target, from `sys.sql_expression_dependencies`. |
| `Script` | `string` | The full generated T-SQL: validation block + `sp_rename` (or drop+recreate) + per-dependent `ALTER`s. Required. |
| `Warnings` | `IReadOnlyList<RenameWarning>` | Name collisions, extended-property breakage, permission preservation notes. May be empty. |
| `PreservedPermissions` | `IReadOnlyList<PermissionGrant>` | Permissions captured before rename, replayed after. |
| `PreservedExtendedProperties` | `IReadOnlyList<ExtendedProperty>` | Extended properties captured before rename, replayed after. |

**State transitions**:
- `Preview` (no script run yet) → user clicks **Apply** → engine runs the script transactionally → `Applied` or `RolledBack`.
- `Preview` → user changes `NewIdentifier` and clicks Preview again → recomputed `Preview`.
- `Preview` with `Warnings.Any(w => w.Kind == NameCollision)` → **Apply** button disabled until the collision is resolved (per FR-043).

---

## 11. Theme Token (US12, wire format — already shipped)

**Layer**: In-memory constants in `src/AkmlSql.Shell.Shared/Ui/Theme/ThemeTokens.cs` (shipped by spec 016 Phase 2 T005).

Phase 10 does not add new tokens; it only consumes the existing 35 tokens during the remaining ~15 WPF surface migrations.

---

## 12. AI Conversation Turn (US13, settings — per session)

**Layer**: Persisted to `%AppData%\AKML SQL\cache\ai-history-<sessionId>.json` (new file per AI panel session). Cleared on session end (SSMS close). Older sessions retained for ≤ 7 days.

**Fields**:

| Field | Type | Notes |
|---|---|---|
| `Timestamp` | `DateTime` (UTC) | When the prompt was issued. Required. |
| `SourceAction` | `enum { Explain, Fix, Optimize, CommentToSql, Manual, IndexAnalysis }` | Determines the prompt template used. Required. |
| `Prompt` | `string` | The user's prompt text or the selected SQL. |
| `Answer` | `string` | The AI's response. |
| `TokenCount` | `int` | Estimated token count (from `TokenEstimator`). |
| `FollowUpSuggestions` | `IReadOnlyList<string>` | 0–3 follow-up prompts suggested by the AI. |
| `RevertToStateOffset` | `int?` | Offset into the editor where the prompt was issued (so "revert to this state" can restore caret position). |

**Validation rules**:
- `Prompt` and `Answer` are stored after privacy transformation (literal redaction per `AiPrivacyValidator`).
- Files older than 7 days are removed by the existing `HistoryRetentionService` extension.

---

## 13. Suggestion Toggle State (US11, in-memory)

**Layer**: In-memory only. Per-session boolean held by `CompletionToggleListener`.

**Fields**:

| Field | Type | Notes |
|---|---|---|
| `Suppressed` | `bool` | Default `false`. Toggled by `Ctrl+Shift+P`. Reset to `false` when SSMS is restarted. |

---

## 14. Custom Commit Key Set (US11, settings)

**Layer**: Settings (`AppSettings.CompletionPolish.CommitKeys` in `config.json`).

**Fields**:

| Field | Type | Notes |
|---|---|---|
| `CommitKeys` | `HashSet<string>` | Subset of `{ "Tab", "Enter", "Space", "Dot", "Comma", "OpenParen" }`. Default `{ "Tab", "Enter" }`. |

**Validation rules**:
- `CommitKeys.Count` ≥ 1 (at least Tab must always be present in the default; the UI prevents removing the last key).
- Unknown key names are ignored on load.

---

## 15. Temp Table Schema (US11, in-memory)

**Layer**: In-memory; held by `TempTableSchemaCollector` per text view. Rebuilt on every analysis pass.

**Fields**:

| Field | Type | Notes |
|---|---|---|
| `Name` | `string` | `"#temp"` or `"##temp"`. Required. |
| `Columns` | `IReadOnlyList<ColumnInfo>` | Parsed from CREATE / SELECT INTO. |
| `Scope` | `enum { Statement, Batch, File }` | Statement / batch / file scope. |
| `ScopeStart` | `int` | Editor offset where the scope begins. |
| `ScopeEnd` | `int` | Editor offset where the scope ends (or `int.MaxValue` if the scope is open until end-of-file). |

---

## 16. Browse Open Tabs Entry (US7, in-memory)

**Layer**: In-memory; populated on `Ctrl+Q` press by enumerating `DTE.Documents`.

**Fields**:

| Field | Type | Notes |
|---|---|---|
| `DisplayLabel` | `string` | `"<filename> – <server> – <database>"`. Required. |
| `Host` | `enum { Ssms, Vs }` | Which IDE owns the tab. Required. |
| `TabIndex` | `int` | Position in the host's document list. |
| `Activate` | `Action` | Brings the tab to focus when invoked. |

---

## 17. Formatting Disable Region (US11, in editor text)

**Layer**: In the SQL text itself, as comment markers `-- akml-format off` / `-- akml-format on`. Already parsed by `NoformatScanner` (shipped earlier). The new editor action just inserts the markers around the selection.

No fields — this is text-level state in the source document, not a data-model entity.

---

## 18. AppSettings extensions (cross-cutting, settings)

The following property additions to existing nested `AppSettings` classes (or their post-split per-domain files) are introduced by this spec. Settings are persisted to `%AppData%\AKML SQL\config.json` by `ConfigManager`. Defaults are applied by `EnsureDefaults()` so a fresh install gets sane behaviour without writing every key.

### `IntelliSenseSettings` (Phase 10 additions)

| Property | Type | Default | Spec ref |
|---|---|---|---|
| `ColumnPickerEnabled` | `bool` | `true` | US2 FR-007 |
| `ColumnPickerSortMode` | `"TableOrder" \| "Alphabetical"` | `"TableOrder"` | US2 FR-008 |
| `WildcardTabExpansionEnabled` | `bool` | `true` | US2 FR-011 |

### `CompletionPolishSettings` (Phase 10 additions)

| Property | Type | Default | Spec ref |
|---|---|---|---|
| `ToggleSuggestionsShortcut` | `string` | `"Ctrl+Shift+P"` | US11 FR-047 |
| `CommitKeys` | `string[]` | `["Tab", "Enter"]` | US11 FR-048 |
| `CategoryCycleEnabled` | `bool` | `true` | US11 FR-049 |
| `ShowMsDescriptionInTooltip` | `bool` | `true` | US11 FR-050 |
| `HighlightNextParameterInSignature` | `bool` | `true` | US11 FR-051 |
| `DecryptEncryptedObjectsWithDac` | `bool` | `true` | US11 FR-052 |
| `TempTableIntelliSenseEnabled` | `bool` | `true` | US11 FR-053 |
| `ObjectDefinitionBoxSize` | `{Width:double, Height:double}` | `{ 360, 220 }` | US11 FR-054 |

### `CodeAnalysisSettings` (Phase 10 additions)

| Property | Type | Default | Spec ref |
|---|---|---|---|
| `IssuesWindowEnabled` | `bool` | `true` | US3 FR-012 |
| `LightbulbDetailsPopupEnabled` | `bool` | `true` | US3 FR-014 |
| `LightbulbApplyFixOnAllOccurrencesShortcut` | `string` | `"Shift+Enter"` | US3 (edge case: Shift on Apply Fix) |

### `TabSettings` (Phase 10 additions)

| Property | Type | Default | Spec ref |
|---|---|---|---|
| `RightClickAssignEnabled` | `bool` | `true` | US4 FR-017 |
| `HighContrastWcagClampEnabled` | `bool` | `true` | US4 FR-019 |

### `NavigationSettings` (Phase 10 additions)

| Property | Type | Default | Spec ref |
|---|---|---|---|
| `SummarizeScriptEnabled` | `bool` | `true` | US7 FR-027 |
| `ScriptAsAlterOnF12Enabled` | `bool` | `true` | US7 FR-028 |
| `SelectInObjectExplorerEnabled` | `bool` | `true` | US7 FR-029 |
| `FindUnusedVariablesEnabled` | `bool` | `true` | US7 FR-030 |
| `BrowseOpenTabsEnabled` | `bool` | `true` | US7 FR-031 |
| `BrowseOpenTabsShortcut` | `string` | `"Ctrl+Q"` | US7 FR-031 |

### `CommandPaletteSettings` (Phase 10 additions)

| Property | Type | Default | Spec ref |
|---|---|---|---|
| `IncludeAkmlCommands` | `bool` | `true` | US6 FR-023 |
| `IncludeAkmlOptions` | `bool` | `true` | US6 FR-023 |
| `IncludeHostCommands` | `bool` | `true` | US6 FR-023 |
| `IncludeDatabaseObjects` | `bool` | `true` | US6 FR-023 |
| `MaxRecentItemsPerHost` | `int` | `10` | US6 FR-024 |
| `RecentItems` | `Dictionary<string, List<string>>` | `{}` | US6 FR-024 |

### `RefactoringSettings` (Phase 10 additions)

| Property | Type | Default | Spec ref |
|---|---|---|---|
| `BracketsToggleShortcut` | `string` | `"Ctrl+B,Ctrl+B"` | US10 FR-041 |
| `InlineStoredProcedureShortcut` | `string` | `"Ctrl+B,Ctrl+I"` | US10 FR-041 |
| `EncapsulateAsStoredProcedureShortcut` | `string` | `"Ctrl+B,Ctrl+E"` | US10 FR-041 |
| `SmartRenameEnabled` | `bool` | `true` | US10 FR-042 |
| `SmartRenamePreserveExtendedProperties` | `bool` | `true` | US10 FR-072 |

### `ExecutionProductivitySettings` (Phase 10 additions)

| Property | Type | Default | Spec ref |
|---|---|---|---|
| `ExecuteCurrentBatchEnabled` | `bool` | `true` | US10 FR-044 |
| `ExecuteCurrentBatchShortcut` | `string` | `"Alt+Shift+F5"` | US10 FR-044 |
| `ExecuteToCursorEnabled` | `bool` | `true` | US10 FR-045 |
| `ExecuteToCursorShortcut` | `string` | `"Ctrl+Shift+F5"` | US10 FR-045 |

### `FormatterSettings` (Phase 10 additions)

| Property | Type | Default | Spec ref |
|---|---|---|---|
| `DisableFormattingForSelectionEnabled` | `bool` | `true` | US11 FR-056 |

### `AiSettings` (Phase 10 additions)

| Property | Type | Default | Spec ref |
|---|---|---|---|
| `OpenPanelShortcut` | `string` | `"Alt+Z"` | US13 FR-063 |
| `FixSelectionShortcut` | `string` | `"Shift+Alt+R"` | US13 FR-063 |
| `OptimizeSelectionShortcut` | `string` | `"Ctrl+Alt+Z"` | US13 FR-063 |
| `ManualGhostTextShortcut` | `string` | `"Ctrl+Alt+Up"` | US13 FR-063 |
| `ExplainSqlEnabled` | `bool` | `true` | US13 FR-065 |
| `QueryIndexAnalysisEnabled` | `bool` | `true` | US13 FR-066 |
| `AutoFixOnErrorEnabled` | `bool` | `true` | US13 FR-067 |
| `CommentToSqlEnabled` | `bool` | `true` | US13 FR-068 |
| `PanelHistoryEnabled` | `bool` | `true` | US13 FR-069 |
| `SelectionIconEnabled` | `bool` | `true` | US13 FR-070 |
| `FollowUpSuggestionsEnabled` | `bool` | `true` | US13 FR-071 |
| `PanelHistoryRetentionDays` | `int` | `7` | (derived from spec 015 history retention) |

### `GridSettings` (Phase 10 additions / audits)

| Property | Type | Default | Spec ref |
|---|---|---|---|
| `CopyAsInClauseReportNullCount` | `bool` | `true` | US9 FR-038 |
| `ScriptAsInsertPromptIdentityToggle` | `bool` | `true` | US9 FR-039 |
| `OpenInExcelWidePrecisionAsText` | `bool` | `true` | US9 FR-040 |
| `OpenInExcelWidePrecisionThreshold` | `int` | `15` | US9 FR-040 (digits-of-precision threshold) |

---

## Validation summary

Every persisted entity round-trips through `System.Text.Json` deserialisation in `ConfigManager.Load()`. `EnsureDefaults()` applies the defaults above for any missing keys, so a fresh install or an upgrade from a pre-Phase-10 `config.json` does not require user action. No migration step is required.

Every in-memory entity is constructed by the corresponding shell control on demand and discarded when no longer needed; no in-memory entity persists across SSMS restarts except the AppSettings sections above.

Wire-format DTOs (`InvalidObjectRecord`, `FindInvalidObjectsRequest/Response`, `FindUnusedVariablesRequest/Response`, `UnusedDeclarationDto`, `EncryptedObjectDecryptionRequest/Response`) ship as-is from spec 014 Phase 2 — Phase 10 only adds the engine handlers that produce/consume them.
