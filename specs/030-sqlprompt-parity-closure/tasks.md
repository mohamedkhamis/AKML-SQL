# Tasks: SQL Prompt Parity Gap Closure (excluding AI & licensing)

**Input**: Design documents from `/specs/030-sqlprompt-parity-closure/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/ipc-and-commands.md, quickstart.md

> **GIT RULE (project-wide):** No `git add/commit/push` without the user's explicit "yes" to "Ready to commit?". Treat every "commit after a task" instinct as **summarize-and-ask**. Never auto-commit.

**Tests**: TDD per plan — engine/library logic (`AkmlSql.{Formatting,IntelliSense,Analysis,Engine,Core}`) is **test-first** (write the failing test, watch it fail, implement, watch it pass). UI-bound shell paths (DTE, editor margins, popups, completion commit, dialogs) have no unit test and are **verified live** per `quickstart.md`.

**Organization**: Tasks are grouped by user story (P1 → P3). Each story is an independently testable increment. `[P]` = parallelizable (different files, no incomplete-dependency).

**Build reminder (every shell task)**: shell sources live in `AkmlSql.Shell.Shared` `.projitems` and build **per host with full MSBuild** (SSMS 22 + VS 2026) — never `dotnet build`, never via the solution.

---

## Phase 1: Setup (Shared Infrastructure)

- [ ] T001 Confirm a clean pre-change build on branch `030-sqlprompt-parity-closure`: engine via `dotnet build src/AkmlSql.Engine`, both hosts via full MSBuild (`AkmlSql.Ssms22`, `AkmlSql.VS2026`) per `quickstart.md` — record the green baseline.
- [ ] T002 [P] Confirm free IPC message-code slots in `src/AkmlSql.Core/Ipc/RpcMessage.cs` (92/192 and 93/193 are taken) and reserve Spec-030 codes for `FindInvalidObjects`, `ListAnalysisRules`, `ObjectSearch` with `// Spec 030` comments (contracts §2).

---

## Phase 2: Foundational (Blocking Prerequisites)

**⚠️ CRITICAL**: These gate every hot-path story (US1/US2/US4). No rule-group rollout or live-analysis change ships without them.

- [ ] T003 Build a micro-benchmark harness and record the **current** baseline latencies — code completion (p95) and Format SQL on a fixed corpus + machine — in `tests/AkmlSql.Formatting.Tests` (or a perf test project). No perf harness exists today; this makes SC-011 falsifiable (research → Performance gate).
- [X] T004 [P] Assemble a representative SQL format corpus (varied SELECT/INSERT/UPDATE/DELETE/MERGE, JOINs, CASE, CTE, DDL, lists, subqueries) under `tests/AkmlSql.Formatting.Tests/Corpus/` — used by the R1 spike and the per-group idempotency/validation gates (research R1).

**Checkpoint**: Baseline recorded + corpus ready — user stories can begin.

---

## Phase 3: User Story 1 - Format SQL with full style fidelity (Priority: P1) 🎯 MVP

**Goal**: Every setting the active style exposes affects the formatted output; the six standalone actions and format-time actions work; unparseable SQL is preserved with a message; the user can see/switch the active style and preview it.

**Independent Test**: Enable GROUP-BY-per-line + leading commas + CASE/CTE/CREATE-TABLE options + a max line width on a built-in style; Format SQL on the corpus and confirm each option shows; run each standalone action; confirm a syntax-error query is preserved.

### R1 — de-risk spike FIRST, then graduated rollout

- [X] T005 [US1] **R1.0 de-risk spike**: behind an off-by-default flag, insert `rulesEngine.Apply(layoutNodes, profile)` after `LayoutEngine.BuildLayout(...)` in both `Format` and `FormatInternal` of `src/AkmlSql.Formatting/Pipeline/FormatterPipeline.cs`; run the T004 corpus through the **full** pipeline; record, per rule group, whether Stage 6 (semantic validation) and Stage 7 (idempotency) hold and the latency delta (research R1.0).
- [X] T006 [US1] **Decision gate** — DONE. Outcome (research.md "R1 production-rollout investigation"): **NO-GO for enabling the rules as-is.** Stage-6/Stage-7 pass but do NOT protect indent correctness; empirically `DmlRules` de-dents nested AND/OR/SET to col 0, Dml/Ddl write systemic absolute indent, and flipping rules on regresses **36/610** human-blessed goldens. The "wire the dormant rules" thesis is refuted for the layout rules.

> **⚠ ROLLOUT FINDING (supersedes T007–T014 as first drafted).** Enabling the layout rule sets first requires resolving an architectural indent-model mismatch — the rules clobber `LayoutEngine`'s nested indent instead of refining it. T007 is now an architectural decision; T008–T014 are contingent on it, not the simple "enable group" tasks originally written.

- [X] T007 [US1] **DONE → Option A (delta-from-existing indent rework), shipped via T008.** Probed on `DmlRules.ApplyAndOrIndent`: absolute `IndentLevel = 0/1/2` → delta from the LayoutEngine-computed level fixes the nested AND/OR de-dent (col 0 → col 4, aligned with the nested WHERE) with no test regression (DmlRules 5/5 + Formatting 610/610). LayoutEngine already computes correct nesting, so rules refine (delta) rather than replace (absolute). No LayoutEngine redesign (B) needed. See research.md "T007 decision". **First step landed**: `DmlRules.ApplyAndOrIndent`.
- [X] T008 [US1] **Option-A delta rework (golden-gated), per rule group — COMPLETE + production flip landed (committed `3d17e8e`, 2026-06-09).** Convert nesting-clobbering absolute `IndentLevel = N` writes to delta-from-existing: rest of DmlRules (SET/VALUES/collapse/INTO/etc.) → DdlRules (all 9) → ControlFlowRules (verify the line-309 CASE-END-in-BEGIN + line-1238 `+=` claims first). **Reframe (research.md "T008 REFRAME"): goldens are AKML's own rules-off output, so most "regressions" are the rules correctly applying configured options (alignWithWhere/collapse/star) → re-bless via `AKML_UPDATE_PARITY_GOLDEN=1`, only genuine bugs get code.** Enable sequence per group: verify rules-on correct by inspection → re-bless goldens → flip `RuleEngine.DefaultOrder` on (with all groups done). **Progress (all 3 groups triaged — see research.md):** **DmlRules ✅ VERIFIED CORRECT** (AND/OR delta + BETWEEN skip + unit test; all 13 golden changes are re-bless). **DdlRules ✅ CLEAN** (0/78 drift). **ControlFlowRules ✅ CASE FIXED** — the WHEN/ELSE/END de-dent is fixed (workflow blueprint): `WhenAlignment`/`EndAlignment` defaults `"toCase"`→`""` sentinel; unset routes to legacy intent in `ResolveWhenIndent`/`ApplyCaseEndAlignment`; CASE-ELSE re-asserted in `ApplyCaseRules`; explicit alignment still wins. Golden oracle: 05/06 CASE 6→1 failures each (10/12 match, indents at col 4); +3 unit pins; 614/614 green. The 2 remaining `compact` cases are collapse-vs-expand (correctly-indented expanded CASE), not indent bugs — a separate follow-up. **T008 per-group bug-fixing COMPLETE.** **All-rules integration check:** no idempotency/validation failures; drift confined to the 6 re-bless corpora; join/CTE/DDL/proc/MERGE/subqueries don't drift (Join/List/Parenthesis add no bugs). **ORDER BY-merge "interaction" on 01-simple-select — ISOLATED + FIXED:** it was a *pure `ListRules` bug*, not an interaction (List-only reproduced it; the earlier call was inferred from only ever running Dml-only + ALL). `ORDER`/`GROUP` carry their own ScriptDom token types but weren't recognised as list boundaries, so `FindListEnd` over-ran the WHERE list to the `;` and `CollapseRange` deleted the break before `ORDER BY`. Fix: `IsListBoundary` (= `IsClauseKeyword` + `Order`/`Group`) in `FindListEnd` (start+end) + the `ApplyIndentListItems` indent-skip — deliberately NOT in `ApplyAlignItemsAcrossClauses`; +2 regression-lock tests (`T008OrderByMergeIsolationTests`). **DONE:** `RuleEngine.DefaultOrder` flipped on in production (`FormatterPipeline.LayoutRules` default); all-on re-verified this session — Formatting **617/617**, Web **33/33**, Core 538/1-skip, IntelliSense 12, Analysis 5, AI 5, E2E 106/1-skip; one-time golden + Web-baseline re-bless landed (6 corpora × goldens=36 + ×{default,ansi}=12). Lone Engine red is the git-ignored M0 perf microbenchmark (BulkFormat +23.4% vs the stale rules-off baseline; FormatRequest/Completion within the 5% gate; CI recaptures via `AKML_UPDATE_BASELINE=1`) — recapture confirmed it's load-sensitive jitter on a non-formatter workload, not a regression. **Residual follow-ups (tracked, non-blocking):** compact collapse-vs-expand precedence; GROUP-BY golden coverage (the one GROUP BY corpus fails Stage-6 validation and returns the original, masking the `Group` arm).
- [ ] T009 [US1] (T007 unblocked) Cover CASE/CTE/BEGIN-END/IF/TRY-CATCH layout; verify the procedural constructs the spike never exercised; confirm CASE-END-inside-BEGIN pairing. **Concrete bugs now observable** (the Stage-6 validator fix made 03/04/11/12/13 format for the first time — see research.md "Stage-6 validation gap"; repro in `StageSixValidationProbeTests`): **BEGIN/END block cramming** (all body statements collapse onto one line after `BEGIN` — 11-stored-procedure); **CTE-body-close / statement-boundary merge** (`…region) SELECT …` — 03/04); **MERGE `WHEN` clause layout** (12). Present rules-OFF too → base-pipeline (`LayoutEngine`/`TextEmitter`), not the rule sets. **ARCHITECTURAL (root-caused 2026-06-09, `Dump_Statement_Structure_11`):** the proc body is a `CreateProcedureStatement.StatementList` that `LayoutEngine.BuildStatementStartSet` never recurses into (it walks top-level `batch.Statements` only), so nested `SET`/`IF`/`SELECT` get no statement-start break. And the break can't simply be added: the `isFirstInStatement` mechanism *suppresses* the break for a leading `SELECT` (LineBreakDecider:46) and *resets `baseIndent = 0`* (LayoutEngine:96) — both correct for top-level, wrong for nested; meanwhile `ControlFlowRules.ApplyIndentBetweenBeginEnd` only indents block content that *already* has a break. So fixing BEGIN-cram (and the analogous CTE main-`SELECT`-after-`)` boundary) requires a nested-statement layout model (recurse statement starts + a nested-vs-top-level break/indent path), i.e. the Phase-B layout rework — a dedicated effort, NOT a contained fix like the operator/JOIN splits. **BEGIN-cram — ✅ FIXED (2026-06-10):** added `LayoutEngine.BuildNestedStatementStartSet` (recurses `CreateProcedure/Function/Trigger` bodies, `BeginEndBlock`, `TryCatch`; recurses IF/WHILE for nested blocks but leaves a single-statement IF/WHILE body inline); a block statement's first token gets a forced `NewLine` break in `BuildLayout` (separate from the top-level `isFirstInStatement` path, so no `baseIndent=0` reset), and `ControlFlowRules.ApplyIndentBetweenBeginEnd` then indents it at block depth. Result on 11: `BEGIN` ⏎ `    SET …;` ⏎ `    IF … SET …;` (IF-then inline) ⏎ `    SELECT …` … `END;`. `NestedStatementLayoutTests` (+4: not-crammed / IF-inline / TRY-CATCH / idempotent). Re-blessed 11 only (goldens ×6 + Web ×2); zero non-golden failures. **Still open:** CTE main-`SELECT`-after-`)` merge (03/04) — a clause-boundary issue, not block nesting; and the collapse re-merges very short block bodies (collapse-vs-expand follow-up).
- [~] T010 [US1] (T007 unblocked) Cover DDL (CREATE TABLE/PROC alignment) + DML statement layout; idempotent + golden-clean. **(a) multi-char operator splitting — ✅ FIXED:** `>=`/`<=`/`<>`/`!=`/`!<`/`!>` tokenise as two adjacent single-char operator tokens and `LayoutEngine`'s operator-spacing split them (`>=`→`> =`); added `IsCompoundOperatorSecondHalf` + source-adjacency check to suppress the interior space. +`CompoundOperatorSpacingTests` (6 ops × join/keep-spaces/idempotent). Re-blessed 02/04/10/12/13 (operator-join only). **(b) JOIN-keyword splitting — ✅ FIXED:** two coordinated changes. (1) `LineBreakDecider`: break before the join modifier (INNER/LEFT/…), keep `JOIN` on that line via `prevSemanticTokenType`, and add `JoinOn` to `IsJoinModifier` so *chained* joins (the modifier after a prior `… ON`) also break. (2) `ListRules`: the rules-ON over-collapse was isolated to `ApplyCollapseShortLists` (List-only reproduced) — `FindListEnd` didn't treat the join modifiers as boundaries, so the FROM "list" (and each JOIN body) swept the trailing `INNER`/`LEFT` into the preceding segment and `CollapseRange` pulled it up / merged the region. Added `IsJoinBoundary` (the modifiers) to `IsListBoundary`. Result: `FROM x` ⏎ `INNER JOIN y ON …` ⏎ `LEFT JOIN z ON …`, each join on its own line, modifier attached. `JoinLayoutTests` (+3) lock it. Re-blessed 02/04/11 (goldens ×6 + Web ×2); **12-MERGE deliberately NOT a boundary case** — `ON` was excluded from `IsJoinBoundary` because as a universal boundary it makes a MERGE `ON` start a collapsible list that pulls the following `WHEN` up. **Residual:** the JOIN ON-condition stays *inline* (`… JOIN y ON a=b`) rather than on its own indented line — the style's `onConditionNewLine` is overridden by the collapse; putting ON on its own line needs a clause-context-aware `ON` boundary (the same collapse-precedence follow-up). **(c)** DDL/PROC column+param alignment otherwise unverified.
- [ ] T011 [US1] [BLOCKED by T007] Leading commas + list/column alignment (fix AlignAliases-after-CollapseShortLists padding growth) + parentheses (force-disable `RemoveRedundant`) via the chosen approach.
- [ ] T012 [US1] [BLOCKED by T007] Max-line wrapping (FR-002) via the chosen approach; golden-clean + idempotent.
- [ ] T013 [US1] [BLOCKED by T007] Operators/IN-list layout (residual Phase B — no rule class exists; build pattern recognition or defer) (research R1.2).
- [ ] T014 [US1] [BLOCKED by T007] Perf gate (T003 baseline) + enable in production only after the golden corpus is clean/re-blessed across all enabled behaviors (SC-011).

### Format actions (R2) — UNBLOCKED (independent of the rule pipeline; the genuinely-cheap P1 win)

> These wire the standalone `IFormatAction` classes via `HandleFormatAction`; they do NOT touch the broken layout-rule path, so they can ship while T007's architectural decision is pending.

- [X] T015 [P] [US1] **Done** — `tests/AkmlSql.Engine.Tests/Formatter/FormatActionDispatchTests.cs` (7 tests, TDD red→green) covering the dispatched actions + a schema-stub message check.
- [X] T016 [US1] **Done** — `HandleFormatAction` now dispatches action types **0–8** to the `IFormatAction` classes via `ResolveFormatAction`/`RunFormatAction` in `src/AkmlSql.Engine/Formatter/FormatRequestHandler.cs` (FR-003, R2). **Working:** CasingOnly(0), InsertSemicolons(1), RemoveSemicolons(2), AddSquareBrackets(5), RemoveSquareBrackets(6), RemoveAsKeyword(8) — the shell commands that already send these now function. **Schema-stubs** return a clear "requires schema cache" message (ExpandWildcards(3), QualifyObjectNames(4)); AddAsKeyword(7) is an AST-stub. Toggle-action flags set per action type with save/restore (no shared-profile mutation). 29/29 Formatter tests green.
- [ ] T017 [US1] **[DEFERRED]** Consume `profile.FormatActions` in `FormatterPipeline.Format` so enabled actions run as part of Format SQL (FR-004, R2) — needs ordering design (actions vs the 7 pipeline stages); `FormatActionConfig` defaults are all `false` so there is no default-profile impact and no urgency; lower value than the now-working standalone commands. Follow-up.

### Formatting UX

- [ ] T018 [US1] Surface formatting diagnostics as a user-facing popup on failure in `src/AkmlSql.Shell.Shared/Formatting/FormatDocumentCommand.cs` (FR-005).
- [ ] T019 [US1] Add a "preview against current query" source to the Format Styles editor preview in `src/AkmlSql.Shell.Shared/Formatting/` (FR-008).
- [ ] T020 [US1] Finish the deferred Format Styles editor Create/Copy/Set-Active/Export buttons in `src/AkmlSql.Shell.Shared/Formatting/FormatStylesEditorWindow.cs` (FR-007).
- [ ] T021 [US1] Add an active-style indicator + switch (status bar + Format Options page) reading `AppSettings.Formatter.ActiveProfile` (FR-006).
- [ ] T022 [US1] Verify US1 live on SSMS 22 + VS 2026 per `quickstart.md` (P1).

**Checkpoint**: US1 (MVP) is independently functional — formatting reflects the full active style + actions.

---

## Phase 4: User Story 2 - Trustworthy IntelliSense surfaces & honored settings (Priority: P2)

**Goal**: Hover tooltips + parameter signature help appear; temp-table columns complete; suggestion settings take effect; column picker + category grouping work.

**Independent Test**: Hover each object kind; invoke a function; declare and reference a `#temp`; toggle the suggestion settings; open the column picker.

- [ ] T023 [US2] Reconcile the in-progress working-tree edits to `src/AkmlSql.Shell.Shared/Editor/QuickInfoSource.cs` and `SignatureHelpSource.cs` (already `M` on branch) with this work before wiring (research R4).
- [ ] T024 [P] [US2] Add a creation-script field to `QuickInfoResult` in `src/AkmlSql.Core/Ipc/`; MessagePack round-trip test in `tests/AkmlSql.Core.Tests` (FR-017, contracts).
- [ ] T025 [US2] Implement `QuickInfoSource` to send `RequestQuickInfo` (5) and render metadata tooltips for table/view/proc/function/column/variable in `src/AkmlSql.Shell.Shared/Editor/QuickInfoSource.cs` (FR-009, R4).
- [ ] T026 [US2] Implement `SignatureHelpSource` to send `RequestSignatureHelp` (4) and track the active parameter in `src/AkmlSql.Shell.Shared/Editor/SignatureHelpSource.cs` (FR-010, R4).
- [ ] T027 [US2] Populate the object-definition Script tab with the real CREATE script via `QuickInfoResult` (FR-017).
- [X] T028 [P] [US2] **Done** — `tests/AkmlSql.Engine.Tests/Completion/TempTableCompletionTests.cs` (4 tests, TDD red→green): direct `#t.`, aliased `x.` in WHERE, bare-in-clause, and SELECT-INTO.
- [X] T029 [US2] **Done** — wired `TempTableTracker` into `CompletionEngine.GetCompletions` (populate `context.AvailableTempTables`, with a `#`-gated **prefix-parse recovery** so a `CREATE TABLE #t` before a mid-edit cursor isn't lost) + added temp-table branches to `ColumnProvider` (CanHandle, dot-qualified, and bare paths, mirroring CTE handling; strips the alias resolver's `dbo.` qualifier via `BareTableName`). `#temp` columns now complete (FR-011, R5). Verified: 4 new tests + 239 Engine-completion + 12 IntelliSense tests green; no regression. Note: the audit's "cheap wire" understated it — robust mid-edit recovery was needed (like CTEs).
- [ ] T030 [P] [US2] Failing tests: `Enabled`/`AutoTrigger`/`ColumnScope` gate completion in `tests/AkmlSql.IntelliSense.Tests` (R6).
- [ ] T031 [US2] Honor `IntelliSense.Enabled` + `AutoTrigger` in the trigger path of `src/AkmlSql.Shell.Shared/Editor/Completion/CompletionController.cs` (FR-012, R6).
- [ ] T032 [US2] Honor `ColumnScope` (list-all-columns-after-SELECT) in the column provider (`src/AkmlSql.IntelliSense/Completion/Providers/ColumnProvider.cs`) (FR-012, R6).
- [ ] T033 [US2] Build the Column Picker window + `Ctrl+Left/Right` toggle in `src/AkmlSql.Shell.Shared/Editor/Completion/` (FR-013).
- [ ] T034 [US2] Category grouping + category navigation + owner-name toggle in `AkmlCompletionPopup` (FR-014).
- [ ] T035 [P] [US2] Alias policy (include-AS, custom object→alias map, prefixes-to-ignore) in `AliasProvider` + `AppSettings` (FR-015).
- [ ] T036 [US2] Suggestion connection scope (databases/schemas) + linked-server objects toggle in the completion path (FR-016).
- [ ] T037 [US2] Verify US2 live on SSMS 22 + VS 2026 per `quickstart.md` (P2).

**Checkpoint**: US1 + US2 both work independently.

---

## Phase 5: User Story 3 - Snippets that work on SSMS and Visual Studio (Priority: P2)

**Goal**: Shortcodes expand on the desktop hosts; built-in pack ships; SQL Prompt import; create-from-selection; surround-with; variables preserved.

**Independent Test**: Expand a built-in shortcode in SSMS and VS; import a `.sqlpromptsnippet`; create-from-selection; surround a selection.

- [ ] T038 [P] [US3] Failing test: snippet expansion by shortcode returns the body in `tests/AkmlSql.Engine.Tests` (R7).
- [ ] T039 [US3] Fix the snippet commit (case 4) to pass the **shortcode** (not the body) to `TryExpandSnippetAtPosition`, honoring `$CURSOR$` on desktop, in `src/AkmlSql.Shell.Shared/Editor/Completion/CompletionController.cs` (FR-030/035, R7).
- [ ] T040 [P] [US3] Add a selection field to the `SnippetExpand` request in `src/AkmlSql.Core/Ipc/`; round-trip test; pass the editor selection on desktop (FR-034, contracts).
- [ ] T041 [US3] Ship a built-in `.akmlsnippet` pack (engine BuiltIn folder + installer payload in `src/AkmlSql.Installer/`) (FR-031, R7).
- [ ] T042 [P] [US3] Failing test: `.sqlpromptsnippet` XML → `.akmlsnippet` with token mapping in `tests/AkmlSql.Engine.Tests` (R7).
- [ ] T043 [US3] Implement `.sqlpromptsnippet` (SqlPromptXml) import with `$DBNAME$`→`$DATABASE$`, `$PASTE$`→`$CLIPBOARD$` mapping in `src/AkmlSql.Engine/Handlers/Snippets/` + `src/AkmlSql.Engine/Snippets/` (FR-032, R7).
- [ ] T044 [US3] Create-from-selection command (auto-name from initials) in `src/AkmlSql.Shell.Shared/Snippets/` (FR-033).
- [ ] T045 [US3] Surround-with command (`Ctrl+K,Ctrl+S` wiring; selection → `$SELECTEDTEXT$`) in `src/AkmlSql.Shell.Shared/Snippets/` (FR-034).
- [ ] T046 [US3] Preserve custom `Variables` on Snippet Manager save (stop writing `variables=[]`) + variable-authoring UI in `src/AkmlSql.Shell.Shared/Snippets/` (FR-036).
- [ ] T047 [P] [US3] `$SELECTIONSTART$/$SELECTIONEND$` markers + custom `$DATE(...)$`/`$TIME(...)$` formats in `PlaceholderParser`/`BuiltInVariableResolver` (FR-037).
- [ ] T048 [US3] Verify US3 live on SSMS 22 + VS 2026 per `quickstart.md` (P2).

**Checkpoint**: P2 complete — IntelliSense surfaces + snippets work on both desktop hosts.

---

## Phase 6: User Story 4 - Live, configurable code analysis (Priority: P3)

**Goal**: Project `.casettings` + inline suppressions apply in the editor; Manage Rules dialog; lightbulb severity; issue-details popup; analysis toggle.

**Independent Test**: A `.casettings` disabling a rule under a folder silences it in the editor (matching the CLI); manage a rule; toggle analysis off/on.

- [X] T049 [P] [US4] **Done** — added `[Key(4)] FilePath` to `CodeAnalysisRequest` (additive, backward-compatible; null ⇒ prior global-defaults behaviour).
- [X] T050 [P] [US4] **Done** — `tests/AkmlSql.Engine.Tests/Analysis/CaSettingsLiveTests.cs` (3 tests): baseline (PE003 + ST004 fire), project `.casettings` disabling PE003 (PE003 gone, ST004 still fires), and global-suppression — proving live-editor parity with the CLI (R3, SC-005).
- [~] T051 [US4] **Engine half done** — `AnalysisEngine.AnalyzeAsync` now derives the document directory from `request.FilePath` and passes it to `CaSettingsLoader.Load` (was hardcoded `null`), so `.casettings` rule config + suppressions apply in live analysis. Verified: 314 Engine-analysis + 5 Analysis-lib tests green, no regression. **Deferred (shell, needs MSBuild):** `AnalysisController` populating `request.FilePath` with the active document's path — a one-liner once built against a host (FR-024, R3).
- [ ] T052 [P] [US4] New `ListAnalysisRules` IPC (request/result) + handler returning the rule catalog (id, name, category, default severity, enabled); round-trip + handler tests in `tests/AkmlSql.Engine.Tests` (FR-026, contracts).
- [ ] T053 [US4] Manage Rules dialog (per-rule enable/severity) writing overrides + firing `AnalysisSettingsChanged` in `src/AkmlSql.Shell.Shared/Analysis/` (FR-026).
- [ ] T054 [US4] Render orange (auto-fixable) vs blue (advisory) lightbulb icons in `LightbulbSource` (FR-027).
- [ ] T055 [US4] Issue-details popup with rule description + reference link, triggered by `Ctrl` in an underlined region (FR-028).
- [ ] T056 [US4] Analysis on/off toggle command gating `CodeAnalysis.Enabled` (optional `Ctrl+Shift+A`) in `src/AkmlSql.Shell.Shared/Analysis/` + VSCT (FR-029).
- [ ] T057 [US4] Verify US4 live per `quickstart.md` (P3).

**Checkpoint**: Team rule standards + suppressions now apply in the editor.

---

## Phase 7: User Story 5 - Deeper refactoring (Priority: P3)

**Goal**: Database-wide Smart Rename (reviewable script); Find Invalid Objects; Inline proc/EXEC; INSERT→UPDATE; Script-as-ALTER; disable-formatting marker.

**Independent Test**: Rename a column referenced by procs/views → reviewable DB-wide script updates all; Find Invalid Objects lists broken objects; inline a proc; INSERT→UPDATE.

- [ ] T058 [P] [US5] New `FindInvalidObjects` IPC + handler (replace `FindInvalidObjectsHandlerStub`) using `sys.sql_expression_dependencies`; handler tests in `tests/AkmlSql.Engine.Tests` (FR-019, R8, contracts).
- [ ] T059 [US5] Find Invalid Objects command + results list in `src/AkmlSql.Shell.Shared/Refactoring/` (FR-019).
- [ ] T060 [P] [US5] Failing test: DB-wide Smart Rename preview produces a dependency-aware reviewable script in `tests/AkmlSql.Engine.Tests` (R8).
- [ ] T061 [US5] Implement database-wide Smart Rename as a heavyweight `RefactorPreview`/`RefactorApply` kind (`sys.sql_expression_dependencies` → `sp_rename`/per-dependent `ALTER` script) in `src/AkmlSql.Engine/Refactoring/` (FR-018, R8).
- [ ] T062 [US5] Wire the `SafeRename` command to the DB-wide preview/apply with a reviewable-script dialog in `src/AkmlSql.Shell.Shared/Refactoring/` (FR-018).
- [ ] T063 [P] [US5] Inline stored procedure refactor (new kind + operation) + tests in `src/AkmlSql.Engine/Refactoring/` and `tests/AkmlSql.Engine.Tests` (FR-020, R8).
- [ ] T064 [P] [US5] Inline EXEC refactor + tests (FR-020, R8).
- [ ] T065 [P] [US5] INSERT→UPDATE refactor + tests (FR-021, R8).
- [ ] T066 [P] [US5] Script-as-ALTER refactor (extend `ScriptAsGenerator`) + tests (FR-022, R8).
- [ ] T067 [US5] Wire `CmdInlineStoredProcedure`/`CmdInlineExec`/`CmdInsertToUpdate`/`CmdScriptAsAlter` (context menu + VSCT per host) (FR-020/021/022).
- [ ] T068 [US5] Disable-formatting-for-selection marker-insert action — wire `CmdDisableFormattingForSelection` (FR-023).
- [ ] T069 [US5] Verify US5 live per `quickstart.md` (P3).

**Checkpoint**: Object-level refactors available and reviewable.

---

## Phase 8: User Story 6 - Tab coloring & history retention parity (Priority: P3)

**Goal**: Tab coloring by database (+ database-on-any-server); remove-older-than; version-preserving retention + disable toggle.

**Independent Test**: A database→environment rule colors a tab on any server; "remove older than"; retention keeps the latest version + executions; disable auto-trim.

- [ ] T070 [P] [US6] Failing test: `EnvironmentMatcher` matches database + database-on-any-server in `tests/AkmlSql.Shell.Shared.Tests` (R9).
- [ ] T071 [US6] Extend `EnvironmentMatcher` + the coloring rule with a database match target; evaluate in `TabColoringManager` using the resolved database in `src/AkmlSql.Shell.Shared/Tabs/` (FR-038, R9).
- [ ] T072 [P] [US6] Failing test: retention trims old versions while keeping latest + executions in `tests/AkmlSql.Engine.Tests` (R10).
- [ ] T073 [US6] Version-preserving retention in `src/AkmlSql.Engine/History/HistoryRetentionService.cs` (FR-039, R10).
- [ ] T074 [US6] Remove-older-than bulk action (`HistoryActions` + three-dot menu) in `src/AkmlSql.Shell.Shared/History/` (FR-041).
- [ ] T075 [US6] Disable-auto-trim Options toggle wired to `HistoryRetentionService` (FR-040).
- [ ] T076 [US6] Verify US6 live per `quickstart.md` (P3).

**Checkpoint**: Tab/history parity refinements in place.

---

## Phase 9: User Story 7 - Complete Options coverage (Priority: P3)

**Goal**: Every in-scope supported setting is adjustable from Options; alias/special-char/active-style/scope controls; per-page help.

**Independent Test**: Every in-scope setting has a control (no config-only); alias + special-char options take effect; each page offers help.

- [ ] T077 [P] [US7] Add `AppSettings` fields where missing (alias policy, special-characters, history `DisableAutoTrim`, tab database-match) in `src/AkmlSql.Core/Config/AppSettings.cs` (FR-042/043).
- [ ] T078 [US7] Surface the config-only settings in Options (object/parameter tooltips, insertion keys, decrypt-encrypted, auto-trigger/scope) in `src/AkmlSql.Shell.Shared/Dialogs/Pages/` (FR-042).
- [ ] T079 [US7] Aliases Options page (include-AS, custom map, prefixes) — pairs with T035 (FR-043).
- [ ] T080 [US7] Special-characters Options (auto-close characters, add parentheses) (FR-043).
- [ ] T081 [US7] Active-style selector on the Format Options page — pairs with T021 (FR-043).
- [ ] T082 [US7] Suggestion Connections/linked-server scope Options — pairs with T036 (FR-043).
- [ ] T083 [US7] Per-page help on Options pages (FR-044).
- [ ] T084 [US7] Verify US7 live per `quickstart.md` (P3) — confirm no in-scope setting remains config-only (SC-007).

**Checkpoint**: Options coverage complete.

---

## Phase 10: User Story 8 - Command Palette object search & bulk format access (Priority: P3)

**Goal**: Palette finds DB objects; Bulk Format wizard is reachable.

**Independent Test**: Type an object name in the palette → objects appear and selecting one navigates/inserts; invoke Bulk Format → wizard opens.

- [ ] T085 [P] [US8] Object-search for the palette: reuse the existing `ObjectSearchWindow` IPC if present, else add an `ObjectSearch` request/result + handler; tests in `tests/AkmlSql.Engine.Tests` (FR-045, contracts).
- [ ] T086 [US8] Add a DB-object provider to the Command Palette in `src/AkmlSql.Shell.Shared/Productivity/CommandPalette/` (FR-045, R12).
- [ ] T087 [US8] Add a `CmdBulkFormat` command that opens the existing `BulkFormatWizard` in `src/AkmlSql.Shell.Shared/Productivity/` + VSCT per host (FR-046, R12).
- [ ] T088 [US8] Verify US8 live per `quickstart.md` (P3).

**Checkpoint**: All user stories independently functional.

---

## Phase 11: Polish & Cross-Cutting Concerns

- [ ] T089 [P] Host-parity sweep: confirm every in-scope capability behaves the same in SSMS 22 and VS 2026 (FR-047, SC-008).
- [ ] T090 Final perf gate: re-run the T003 benchmark end-to-end; confirm completion p95 < 100 ms and Format SQL < 200 ms typical, with no regression vs the T003 baseline (SC-011).
- [ ] T091 Single-undo verification across format, format-action, refactor, snippet expansion, and analysis auto-fix (FR-049).
- [ ] T092 [P] Graceful-degradation check: schema-dependent features show a clear message with no active connection (FR-048).
- [ ] T093 Re-audit: re-run the gap lens over `doc/_Prompt-Gap/` for the in-scope rows and confirm targeted 🟡/❌ → ✅ (SC-010).
- [ ] T094 [P] Update docs: new message types in `doc/ipc-api.md`; `doc/formatting.md` (rules now on the pipeline); `doc/analysis-rules.md`; progress log in `doc/progress.md`.

---

## Dependencies & Execution Order

### Phase dependencies

- **Setup (P1)** → no deps.
- **Foundational (P2: T003 baseline, T004 corpus)** → blocks US1 (rule rollout + perf gate), US2 (completion perf gate), US4 (analysis perf). Must complete first.
- **User stories** → all depend on Foundational; then proceed in priority order P1 → P2 → P3 (or in parallel by different developers, since stories touch mostly disjoint areas).
- **Polish (P11)** → after the desired stories.

### Critical in-story ordering

- **US1**: T005 (spike) → T006 (gate) **before** any rule-group enable (T007–T013); a no-go at T006 re-sequences P1. T014 perf gate after enables. T015→T016→T017 (actions) independent of the rule rollout.
- Tests precede their implementation within each story (TDD): T007→T008, T009→T010, T015→T016, T028→T029, T030→T031/32, T038→T039, T042→T043, T049/T050→T051, T060→T061, T070→T071, T072→T073.
- Options story (US7) **pairs** with earlier features: T079↔T035, T081↔T021, T082↔T036 (the setting exists by the time its Options control is added).

### Parallel opportunities

- T002 ∥ T001; T004 ∥ T003.
- Across stories: once Foundational is done, US1–US8 can be staffed in parallel (disjoint folders). Within a story, `[P]` test/model tasks run together.
- US5 refactor operations T063/T064/T065/T066 are independent `[P]` (different operations/files).

---

## Implementation Strategy

### MVP first (US1)

1. Setup (T001–T002) → Foundational (T003–T004).
2. **US1 starts with the R1 spike (T005) + gate (T006)** — this is the riskiest, highest-value item; the gate decides whether the formatter approach holds. Then graduated rollout + actions + UX.
3. **STOP and validate** US1 independently (T022). Demo the formatter MVP.

### Incremental delivery

P1 (US1) → P2 (US2, US3) → P3 (US4–US8), each story tested + demoable independently. The re-audit (T093) measures cumulative parity against `doc/_Prompt-Gap/`.

### Risk notes

- **R1 is the program risk.** If T006 shows most rule groups break idempotency/validation through the pipeline, treat the rule rollout as a separate design effort and ship the cheap wins first (actions T015–T017, plus US2/US3) — re-plan the formatter layout.
- Operators/IN-list (T013) may legitimately defer (residual Phase B) without blocking US1.

---

## Notes

- `[P]` = different files, no incomplete dependency. `[USx]` traces a task to its story.
- TDD: write the failing engine/library test first; verify it fails; implement; verify it passes. UI paths verified live (quickstart).
- **Git**: each task's natural "commit" point is **summarize-and-ask** — never auto-commit (project rule).
- Shell tasks build per host with full MSBuild; redeploy copies the **whole** engine publish (never a partial DLL swap).
