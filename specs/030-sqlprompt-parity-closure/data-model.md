# Phase 1 Data Model — SQL Prompt Parity Gap Closure

This feature is parity-closure over **existing** subsystems, so the data model is mostly the current one. For each entity: its current code model, the **new fields / state** this feature adds, and the validation/behavior rules from the spec. No new persistent stores are introduced (existing `config.json`, `.akmlstyle`, `.akmlsnippet`, `.casettings`, and the SQLite history DB are reused).

---

## Formatting Style

- **Maps to**: `FormattingProfile` (`AkmlSql.Formatting/Profiles`), persisted as `.akmlstyle`; `ProfileMetadata` (SkipValidation, EnableIdempotencyCheck); `FormatActionConfig` (the format-time action toggles).
- **New behavior/state**: `FormatActionConfig` becomes **consumed** by `FormatterPipeline.Format` (R2); the full layout settings become **honored** by the new rules pass (R1). Active-style is `AppSettings.Formatter.ActiveProfile` — now exposed in Options with a selector + an in-editor "active style" indicator (US1/FR-006).
- **Relationships**: one active style at a time; built-in (read-only) vs user styles; import/export to `.akmlstyle` and `.sqlpromptstylev2` (round-trip already exists).
- **Rules**: a style setting exposed in the editor MUST produce a visible effect (FR-001/SC-001); formatting MUST stay semantically equivalent (Stage 6) and idempotent (Stage 7) after the rules pass.

## Format Action

- **Maps to**: `IFormatAction` implementations (`AkmlSql.Formatting/Actions`) + `FormatActionType` enum; routed via `FormatAction` IPC (13 → 113) and `FormatRequestHandler.HandleFormatAction`.
- **New state**: action types **0–5** (casing, insert/remove semicolons, expand wildcards, qualify, add/remove brackets) become **dispatched** (R2). No new fields.
- **Rules**: each action applies only its transformation (FR-003/SC-002); reversible as one undo (FR-049).

## Snippet

- **Maps to**: `.akmlsnippet` JSON read by `SnippetLoader`; `SnippetIndex` (shortcode/category/search); `SnippetExpand` IPC (20 → 120); Snippet Manager (`AkmlSql.Shell.Shared/Snippets`).
- **New state / data**: a shipped **built-in snippet pack** (new `.akmlsnippet` files + installer payload); imported snippets from `.sqlpromptsnippet` (R7); the Snippet Manager **preserves `Variables`** on save (today it writes `variables=[]`).
- **Relationships**: built-in (read-only) vs personal; shortcode → snippet (case-insensitive); a snippet has 0..N placeholders/variables.
- **Rules**: shortcode commit MUST expand on SSMS + VS (FR-030/SC-003); shortcode SHOULD be unique within a source (personal overrides built-in); import maps SQL Prompt tokens to AKML tokens (FR-032).

## Placeholder / Variable

- **Maps to**: `PlaceholderParser` + `BuiltInVariableResolver` (engine); the snippet `Variables` list.
- **New state**: `$SELECTEDTEXT$` receives the editor selection on **desktop** (today web-only); `$CURSOR$` caret honored on desktop; new `$SELECTIONSTART$/$SELECTIONEND$` markers; custom `$DATE(...)$`/`$TIME(...)$` formats; custom variables with default + order persisted.
- **Rules**: a placeholder needing input (selection/clipboard) with none available resolves to empty without error (Edge Cases).

## Analysis Rule & Rule Settings

- **Maps to**: `IAnalysisRule` impls + `RuleRegistry` (reflection discovery); `.casettings` JSON via `CaSettingsLoader` (upward search); `SuppressionParser` (inline `-- noqa`/akml-disable); `RequestAnalyze` IPC (25 → 125); `AnalysisSettingsChanged` (26).
- **New state / data**: `CodeAnalysisRequest` gains a **document FilePath** (R3) so the live engine resolves the project `.casettings` directory; a **rule-list** surface (id, name, category, default severity, enabled) for the Manage-Rules dialog; per-rule enable/severity overrides written to the global/project settings.
- **Relationships**: rule → category (PE/BP/SE/ST/DE/DEP/EX/NM); settings cascade — nearest `.casettings` up the tree wins (Edge Cases).
- **Rules**: editor findings MUST match the CLI on the same file + settings (FR-024/SC-005); auto-fixable issues are visually distinct from advisory (FR-027).

## Tab-Color Environment & Rule

- **Maps to**: `AppSettings` coloring rules (Label + Color + match); `EnvironmentMatcher`; `TabColoringManager`; `SsmsConnectionContextResolver`.
- **New state**: a `MatchTarget` of **database** and **database-on-any-server** (today only `serverName`); rule shape gains an optional database field.
- **Rules**: a tab connected to a matching database takes the environment color on any server (FR-038).

## History Entry, Version & Execution

- **Maps to**: `HistoryDatabase` (SQLite + FTS5); `history` + `history_versions` tables; executions; `HistoryRetentionService`.
- **New behavior**: retention trims **old versions** while keeping each query's **latest version + all executions** (today purges whole entries); new **remove-older-than** action; an Options **disable-auto-trim** toggle.
- **Rules**: trimming never deletes the latest version or execution records (FR-039); disabling auto-trim halts purging (FR-040).

## Completion Item

- **Maps to**: `CompletionItemModel` (type, badges, secondary text); `CompletionEngine` providers; `AkmlCompletionPopup`; `CompletionController`.
- **New state**: temp-table column items (R5); category grouping + owner-name display (FR-014); column-picker multi-select (FR-013); items gated by honored `Enabled`/`AutoTrigger`/`ColumnScope` (R6); alias items honoring include-AS/custom-map/prefixes (FR-015); object-definition Script tab content = real CREATE script (FR-017).
- **Rules**: suggestions appear within 100 ms p95 (SC-011); settings take effect (FR-012).

## Options Setting

- **Maps to**: `AppSettings` (POCO; `ConfigManager` atomic writes) ↔ Options dialog pages (`AkmlSql.Shell.Shared/Dialogs/Pages`).
- **New state**: new `AppSettings` fields where a setting is missing (alias policy, special-characters, etc.); new dialog controls so **no in-scope setting is config-only** (FR-042/SC-007); per-page help (FR-044).
- **Rules**: every in-scope supported setting is viewable + changeable in Options; writes are atomic.
