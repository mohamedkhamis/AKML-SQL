# Phase 1 Data Model — Autocomplete Campaign Remediation

This feature repairs existing subsystems; the data model is the current one plus targeted state additions. No new persistent stores. For each entity: current code model, the **new fields / state** this feature adds, and the behavior rules from the spec.

---

## CursorContext (engine, per-request)

- **Maps to**: `CursorContext` produced by `CursorContextAnalyzer.Analyze` (`AkmlSql.IntelliSense/Parser`) — clause type, `PrecedingDot`/`DotPrefix`, `PartialText`, `AvailableAliases`, `AvailableCtes`, `AvailableCteSources`, `AvailableTempTables`, `AvailableVariables`, `IsInCteBody`, `CurrentJoinTargetAlias`.
- **New state**:
  - New `ClauseType` members (engine-internal enum, not wire): `InsertTarget`, `InsertColumnList` (split from `InsertColumns`), `OrderKeyword`, `GroupKeyword`, `JoinQualifier`, `SetOperator`, `CaseWhen`/`CaseThen`/`CaseElse` (B2–B6, C1/C2).
  - `PartialText` now includes `@`-tokens (variables/parameters) and is delimiter-trimmed (`[`, `"`) — C4/G2.
  - `DotPrefix` accepts multi-part chains and quoted (`AsciiStringOrQuotedIdentifier`) parts — A6/G3.
  - `AvailableAliases` gains INSERT-target injection in `InsertColumnList` contexts (mirrors the existing ALTER TABLE injection) — C1.
  - `AvailableVariables` becomes populated (from `VariableTracker`, batch-scoped DECLARE scan) — C4.
- **Rules**: scope containers reflect the **innermost paren scope containing the caret, merged with enclosing scopes (inner wins)** — A1/A4; FROM/JOIN mappings take precedence over DML-target tokens (A2); set-operator tokens bound the scope relative to the caret (A5); CTEs are statement-scoped (E3).

## Scope / alias map (engine, per-request)

- **Maps to**: `TokenBasedAliasExtractor.Extract` (token fallback) + `AliasResolver.ResolveAliasesInCursorScope` (AST path) feeding `CursorContext.AvailableAliases` (`alias → "schema.table"`).
- **New behavior**: two-pass extraction (FROM/JOIN first, DML targets second, no overwrite of pass-1 keys); cursor-scope-aware depth handling; multi-part name consumption; AST path covers `Update/Delete/MergeSpecification` and enumerates derived-table projections (replacing the `(derived:alias)` zero-column placeholder).
- **Rules**: FROM-less DML target injection (`UPDATE Orders SET |`) MUST keep working (existing deliberate behavior); sibling paren scopes never leak (existing invariant, kept under test).

## CTE / temp-table registries (engine, per-request)

- **Maps to**: `CteResolver` (AST) + `TokenBasedCteExtractor` (fallback) → `AvailableCtes` (`name → columns`) and `AvailableCteSources` (`name → source tables`); `TempTableTracker` → `AvailableTempTables` (`#name → columns`).
- **New state**: token-extracted CTEs keep explicit column lists (E6); `SELECT *` CTE bodies resolve columns via `AvailableCteSources` + schema cache at completion time (E4); recursive CTEs expose their own name inside the body (anchor-member projection) (E5); temp tracker records the `SELECT * INTO #t FROM src` source for later cache expansion (F3) and survives an unparsable trailing statement via the last-batch rule (F2).
- **Rules**: CTE lookup by alias resolves through `AvailableAliases` first (E1 — same pattern as the temp-table branch); temp-table **names** are offered in table positions (F1).

## CompletionItem (wire DTO — `AkmlSql.Core`)

- **Maps to**: `CompletionItem` in [CompletionResponse.cs](../../src/AkmlSql.Core/Ipc/Messages/CompletionResponse.cs) — keys 0–6 (`DisplayText`, `InsertText`, `ObjectType`, `SecondaryText`, `SourceObject`, `SortPriority`, `IsLinkedServer`).
- **New field**: `FilterText` at **`[Key(7)]`**, `string?` — the text fuzzy matching scores against (column name for `alias.column` display items). `CompletionEngine` scores `FilterText ?? DisplayText`. Additive/back-compatible: old peers deserialize without it; hosts may ignore it.
- **Rules**: never renumber existing keys; new items for parameters use `ObjectType = Parameter (11)` (exists), never `Snippet` (SSMS hides/expands Snippet-typed items); IDENTITY/computed columns are excluded only as UPDATE SET **targets** and INSERT column-list suggestions, not as readable columns elsewhere.

## Providers (engine)

- **Maps to**: `ICompletionProvider` implementations registered in `CompletionEngine`.
- **New/changed**:
  - `ParameterProvider` (NEW, C3): emits `@param` items for the EXEC'd procedure from the schema cache's Phase-B parameter lists (same source `SignatureProvider` reads).
  - `VariableProvider` (existing): becomes reachable once `PartialText` includes `@` and `AvailableVariables` is populated — no structural change expected.
  - `ObjectProvider`: temp-table names branch (F1); `InsertTarget` filtering — tables/views only (C2); APPLY/TOP-paren suppression exemptions (B7/H3).
  - `ColumnProvider`: CTE-alias dot branch (E1); `InsertColumnList` single-table bare-column mode with IDENTITY/computed exclusion (C1/H2); sets `FilterText` on qualified items (H1).
  - Built-in function surfacing (D): `ScalarFunctions` emitted as `Function`-typed items in expression positions, SortPriority ≥ 200 (below columns).
- **Rules**: provider outputs stay deterministic per (context, cache); the 50-item cap and linked-server pinning behavior are unchanged.

## Formatter pipeline state (engine + WASM)

- **Maps to**: `FormatterPipeline` stages; `FormatDiagnostic` list; `ProfileMetadata.EnableIdempotencyCheck`.
- **New behavior**: Stage 7 (J2) — when the second pass differs, is non-empty, and passes Stage-6 re-validation, the pipeline **returns the second pass** and keeps the Warning diagnostic; the web front end surfaces format diagnostics instead of dropping them. `LineBreakDecider`/`ClauseTracker` become paren-aware for JOIN-modifier state (J1) so both passes agree inside CTE/derived-table bodies.
- **Rules**: Stage-6 semantic equivalence remains the hard gate (validation failure still returns the original SQL); `tests/format-parity` goldens are drift guards — a golden diff means review the change, never regenerate to pass.

## Web profile store (Blazor WASM, IndexedDB)

- **Maps to**: `ProfileStore` / `ProfileRecord` / `ProfileOrigin` in [IProfileStore.cs](../../src/AkmlSql.Web/Services/IProfileStore.cs); built-ins currently synthesized as `builtin.default` + `builtin.ansi`.
- **New state**: built-ins `builtin.khamis` (Khamis Style) and `builtin.collapsed` (Collapsed), loaded from the same `.akmlstyle` definitions the desktop `ProfileManager.GetBuiltIn()` uses (`AkmlSql.Formatting/Profiles/BuiltIn/`); `builtin.khamis` is the default active id for fresh installs. `builtin.default`/`builtin.ansi` remain for persisted references.
- **Rules**: built-ins are read-only (`ProfileOrigin.BuiltIn`); a persisted active-id that no longer resolves falls back to `builtin.khamis`.

## Web connection state (Blazor WASM)

- **Maps to**: `ISqlConnectionService` (+ impl), `ISavedSqlConnectionStore`, `ISchemaSync` (single owner), `StatusBar.razor` pill, `ConnectionManagerModal`/`ConnectionPickerComponent`.
- **New state**: an explicit three-valued connection status — `Offline` (no bridge) / `BridgeOnly` (bridge up, no SQL session) / `SqlConnected` — exposed as an observable state the pill renders; a `LastUsedConnectionId` marker in the saved-connection store driving boot-time auto-restore (Windows-auth only; re-runs the loopback guard; failure degrades to `BridgeOnly`).
- **Rules**: the pill never shows full-IntelliSense status without a SQL session (FR-032); selecting a saved connection seeds the DB dropdown's option list with the saved database (FR-033); the DB list carries a service-account-visibility hint; no SQL passwords at rest (spec 029 decision stands).

## Campaign corpus (test data)

- **Maps to**: NEW `tests/completion-corpus/` — the campaign's 22 JSON files (1,470 cases), each case: id, family, document text, caret, trigger kind, expected items / expected-absent, and an `excluded` marker (corpus-mistake or at-cap-ambiguous) with reason.
- **Rules**: the corpus gate (`CorpusGateTests`) runs `CompletionEngine.GetCompletions` against a fake `DatabaseCache` seeded to the `Northwind_AutoTest` shape (tables/views/procs/functions/params listed in the campaign report's environment table); excluded cases are reported but don't fail the gate; SC-001/SC-003 thresholds are asserted per family.
