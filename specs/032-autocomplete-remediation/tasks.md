# Tasks: Autocomplete Campaign Remediation (Web + Engine)

**Input**: Design documents from `/specs/032-autocomplete-remediation/`
**Prerequisites**: [plan.md](./plan.md), [spec.md](./spec.md), [research.md](./research.md), [data-model.md](./data-model.md), [contracts/completion-and-editor.md](./contracts/completion-and-editor.md), [quickstart.md](./quickstart.md), campaign report [doc/web-autocomplete-campaign-2026-07-16.md](../../doc/web-autocomplete-campaign-2026-07-16.md)

**Tests**: INCLUDED — the project constitution gate mandates TDD for engine/library logic (plan.md Constitution Check). Every engine fix lands failing-test-first using the campaign repro SQL verbatim. Contract-matrix row references (P1–P24) point at [contracts/completion-and-editor.md §2](./contracts/completion-and-editor.md).

**Organization**: Tasks grouped by the spec's 8 user stories (US1/US2 = P1, US3/US4 = P2, US5–US8 = P3), preceded by corpus-gate infrastructure. Cluster letters (A1, B1, …) reference research.md / the campaign report.

> **GIT RULE**: no `git add/commit/push` without the user's explicit approval. Any commit checkpoint is summarize-and-ask.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (US1–US8)

## Phase 1: Setup (verification infrastructure)

**Purpose**: Bring the campaign corpus in-repo and stand up the measurement harness every story's acceptance uses.

- [ ] T001 Import the campaign corpus (22 JSON files, 1,470 cases) into `tests/completion-corpus/`, adding `excluded` markers (+reason) for the 24 corpus-mistake cases and `atCap` annotations, per the report's "Corpus corrections" section (source: static copy at `C:\Program Files (x86)\AKML SQL\Web\test-corpus\`; fallback: session scratchpad `corpus/`)
- [ ] T002 [P] Create the fake `Northwind_AutoTest`-shaped schema-cache fixture builder in `tests/AkmlSql.Engine.Tests/Completion/NorthwindAutoTestCacheFactory.cs` (tables/views/procs+params/functions per the report's Environment table, incl. `Sales` schema and IsIdentity/IsComputed flags)
- [ ] T003 Create the corpus-driven gate runner `tests/AkmlSql.Engine.Tests/Completion/CorpusGateTests.cs` — feeds each corpus case to `CompletionEngine.GetCompletions` with the T002 cache, asserts expected/absent items, reports per-family pass rates; excluded cases reported-not-failed; run it once and record the engine-level baseline in the test output (thresholds asserted later, per story)
- [ ] T004 [P] Capture the pre-change perf baseline: run `tests/AkmlSql.Engine.Tests/PerformanceBaselineTests.cs` and record results (reference for T021/T060; re-baseline only per quickstart.md rules)

---

## Phase 2: Foundational (blocking prerequisites)

**Purpose**: The one wire/scoring change several stories build on (H1 core, FR-026).

**⚠️ CRITICAL**: Complete before any user story starts — US2/US4/US7 provider tasks set `FilterText`.

- [ ] T005 Add `[Key(7)] string? FilterText` to `CompletionItem` in `src/AkmlSql.Core/Ipc/Messages/CompletionResponse.cs` with MessagePack round-trip + old-payload (no Key 7 → null) tests in `tests/AkmlSql.Core.Tests/` (contract §1; failing tests first)
- [ ] T006 Score `FilterText ?? DisplayText` in the fuzzy filter at `src/AkmlSql.IntelliSense/Completion/CompletionEngine.cs:381` with a unit test in `tests/AkmlSql.Engine.Tests/Completion/CompletionEngineTests.cs` proving DisplayText fallback unchanged when FilterText is null

**Checkpoint**: Wire + scoring infrastructure ready — user stories can begin.

---

## Phase 3: User Story 1 — Completion appears when and how I type; keyboard works (Priority: P1) 🎯 MVP

**Goal**: Dot-trigger, DML-keyword-space trigger, Tab-accept, Ctrl+Enter execute, `@`/`#` span replace — SSMS-parity feel in the web editor (FR-001…FR-005; clusters I1–I4, C5).

**Independent Test**: Contract §3 gesture table, keyboard-only, in the deployed web editor against `Northwind_AutoTest`.

### Implementation for User Story 1

> All edits below touch `src/AkmlSql.Web/wwwroot/js/akml-editor.js` — sequential by design (no [P]).

- [ ] T007 [US1] Verify `acceptCompletion` is exported by the vendored CM6 bundle in `src/AkmlSql.Web/wwwroot/lib/codemirror/`; if missing, add the export in `tools/codemirror` and rebuild the bundle (esbuild; no CDN)
- [ ] T008 [US1] I1/FR-001 — add `DOT_MEMBER_TRIGGER` (identifier/`]`/`"` + trailing `.`) in `src/AkmlSql.Web/wwwroot/js/akml-editor.js`: (a) `startCompletion` arm in the updateListener `typedNonWord` branch (~line 126-133), (b) accept-arm in the `completionSource` gate (~line 152, `from: context.pos`); no popup inside comments/strings/numeric literals
- [ ] T009 [US1] I2/FR-002 — extend `POST_KEYWORD_TRIGGER` (~line 92) with `update|insert(\s+into)?|into|delete(\s+from)?|exec(ute)?` in `src/AkmlSql.Web/wwwroot/js/akml-editor.js`
- [ ] T010 [US1] I3/FR-003 — register `{ key: 'Tab', run: cm.autocomplete.acceptCompletion }` after `ghostKeymap`, before `wildcardKeymap` (~line 578) in `src/AkmlSql.Web/wwwroot/js/akml-editor.js`; precedence: ghost-accept → completion-accept → wildcard-expand → indent
- [ ] T011 [US1] I4/FR-004 — bind `Mod-Enter → runExecute` ahead of the `defaultKeymap` spread (~line 580; navKeymap currently at line 603) in `src/AkmlSql.Web/wwwroot/js/akml-editor.js`; F5 stays unbound
- [ ] T012 [US1] C5/FR-005 — widen the replace-span regex `/[\w]+/` → `/[@#\w]+/` (~line 140) in `src/AkmlSql.Web/wwwroot/js/akml-editor.js`
- [ ] T013 [US1] Deploy web to dev IIS (quickstart.md) and add/execute keystroke checks for all §3 gesture rows (incl. offline no-empty-popup, popup-contents-equal-explicit) in `tests/AkmlSql.Web.E2E.Tests/` or the campaign Playwright harness; record results

**Checkpoint**: Web editor feels SSMS-native (48 dot-trigger scenarios + Tab/Ctrl+Enter pass) — independent of engine fixes.

---

## Phase 4: User Story 2 — Suggestions respect statement scope (Priority: P1)

**Goal**: Subqueries, CTE bodies, aliased UPDATE/DELETE, set-operator branches, three-part names resolve to the correct scope (FR-006…FR-011; clusters A1–A6, F4).

**Independent Test**: Contract §2 rows P1–P4; corpus families subqueries / update / delete / multi-statement ≥ 90%.

### Tests for User Story 2 (write first, ensure FAIL)

- [ ] T014 [P] [US2] Failing tests for A1 (caret-in-parens keeps own+outer scope), A2/F4 (aliased-DML poisoning incl. temp tables), A5 (UNION leak), A6 (three-part names) in `tests/AkmlSql.Engine.Tests/Parser/TokenBasedAliasExtractorTests.cs` using report repro SQL (UPD-045…58 / DEL-031…44 / MULTI-045 shapes); assert FROM-less `UPDATE Orders SET |` injection and sibling-paren exclusion still hold
- [ ] T015 [P] [US2] Failing tests for A3 (Update/Delete/MergeSpecification scopes) and A4 (correlated outer aliases merge inner-wins; derived-table projections replace `(derived:alias)`) in `tests/AkmlSql.Engine.Tests/Parser/AliasResolverTests.cs`
- [ ] T016 [P] [US2] Failing tests for caret-position repair (broken-at-caret subquery parses; `dbo.Or` tail not treated as OR — shared with H4) in `tests/AkmlSql.Engine.Tests/Parser/SuffixCompletionHelperTests.cs`

### Implementation for User Story 2

- [ ] T017 [US2] Rework `src/AkmlSql.IntelliSense/Parser/TokenBasedAliasExtractor.cs` per research R-A: innermost-paren-span-of-caret extraction + enclosing-scope merge (inner wins), two-pass FROM/JOIN-wins-over-DML-targets, depth-0 set-operator scope bounds, multi-part identifier chain consumption
- [ ] T018 [US2] Add `RepairAtCursor(sql, cursorOffset)` to `src/AkmlSql.IntelliSense/Parser/SuffixCompletionHelper.cs` (existing tail patterns applied at the caret + close parens unbalanced after it) and wire it in `src/AkmlSql.IntelliSense/Completion/CompletionEngine.cs` (call before `ParseWithSuffix` when the plain parse fails and the caret is inside parens)
- [ ] T019 [US2] Extend `src/AkmlSql.IntelliSense/Parser/AliasResolver.cs`: `CursorScopeFinder` visits `Update/Delete/MergeSpecification`; merge ancestor `QuerySpecification` scopes (inner wins); enumerate derived-table projections (reuse `CteResolver.InferColumnsFromQuery`)
- [ ] T020 [US2] A6 second half — multi-part `DotPrefix` chain consumption in `src/AkmlSql.IntelliSense/Parser/CursorContextAnalyzer.cs:176-201`
- [ ] T021 [US2] Gate: corpus families subqueries/update/delete/multi-statement ≥ 90% and zero-item cases = 0 in those families (`tests/AkmlSql.Engine.Tests/Completion/CorpusGateTests.cs`); re-run `PerformanceBaselineTests` vs T004 baseline (completion p95 < 100 ms held)

**Checkpoint**: The three worst families fixed engine-side; desktop benefits automatically.

---

## Phase 5: User Story 3 — Stored procedure execution assistance (Priority: P2)

**Goal**: `EXEC ` offers procs; parameters complete; declared `@vars` complete (FR-012, FR-016, FR-017; clusters B1, C3, C4).

**Independent Test**: Contract §2 rows P5–P7; exec-procs family ≥ 90%.

### Tests for User Story 3 (write first, ensure FAIL)

- [ ] T022 [P] [US3] Failing tests: `EXEC |` → `ClauseType.Exec` (dedicated `TSqlTokenType.Exec`) in `tests/AkmlSql.Engine.Tests/Parser/CursorContextAnalyzerTests.cs`; `@`-PartialText + `AvailableVariables` population in `tests/AkmlSql.Engine.Tests/Parser/VariableTrackerTests.cs`; new `tests/AkmlSql.Engine.Tests/Completion/ParameterProviderTests.cs` (params of `usp_GetCustomerOrders` from the T002 cache)

### Implementation for User Story 3

- [ ] T023 [US3] B1 one-liner — add `case TSqlTokenType.Exec:` beside `Execute` at `src/AkmlSql.IntelliSense/Parser/CursorContextAnalyzer.cs:336` (first engine fix to land; biggest win per line)
- [ ] T024 [US3] C4 — include `TSqlTokenType.Variable` in PartialText extraction (`src/AkmlSql.IntelliSense/Parser/CursorContextAnalyzer.cs:206-211`) and populate `context.AvailableVariables` from `VariableTracker` in `Analyze` (first caller for `src/AkmlSql.IntelliSense/Parser/VariableTracker.cs`); existing `VariableProvider` becomes reachable unchanged
- [ ] T025 [US3] C3 — create `src/AkmlSql.IntelliSense/Completion/Providers/ParameterProvider.cs` (EXEC-context `@param` items from the cache's Phase-B parameter lists, the same source `SignatureProvider` reads; `ObjectType = Parameter (11)`, never `Snippet`) and register it in the `CompletionEngine` constructor (`src/AkmlSql.IntelliSense/Completion/CompletionEngine.cs:119-127`)
- [ ] T026 [US3] Gate: exec-procs family ≥ 90% + matrix rows P5–P7 in `tests/AkmlSql.Engine.Tests/Completion/CorpusGateTests.cs`

**Checkpoint**: EXEC assistance works end-to-end (with US1's `@` span fix, accepting `@CustomerID` over `@C` is clean in the web editor).

---

## Phase 6: User Story 4 — INSERT statements guide to the right columns (Priority: P2)

**Goal**: `INSERT INTO t (|` → t's columns; `INSERT INTO |` → tables/views only; `INSERT |` → INTO (FR-015; clusters C1, C2).

**Independent Test**: Contract §2 rows P8–P9; insert family ≥ 90%.

### Tests for User Story 4 (write first, ensure FAIL)

- [ ] T027 [P] [US4] Failing tests: `InsertTarget`/`InsertColumnList` split + target-table injection in `tests/AkmlSql.Engine.Tests/Parser/CursorContextAnalyzerTests.cs`; column-list = target's columns minus IDENTITY/computed in `tests/AkmlSql.Engine.Tests/Completion/ColumnProviderTests.cs`; `INTO` in AfterInsert in `tests/AkmlSql.Engine.Tests/Completion/Dictionaries/KeywordDictionaryTests.cs`

### Implementation for User Story 4

- [ ] T028 [US4] C1 — split the INSERT clause in `src/AkmlSql.IntelliSense/Parser/CursorContextAnalyzer.cs` (~line 424): forward-scan for `INTO <multi-part name> (`; caret inside paren → new `ClauseType.InsertColumnList` + inject target into `AvailableAliases` (mirror the ALTER TABLE pattern at ~:498-501); caret at table position → new `ClauseType.InsertTarget`
- [ ] T029 [US4] C2 — `ObjectProvider` serves `InsertTarget` with tables/views only (`src/AkmlSql.IntelliSense/Completion/Providers/ObjectProvider.cs`); `ColumnProvider` serves `InsertColumnList` in single-table bare-column mode excluding `IsIdentity`/`IsComputed` (`src/AkmlSql.IntelliSense/Completion/Providers/ColumnProvider.cs`); add `"INTO"` to `AfterInsert` + map the new clause types in `src/AkmlSql.IntelliSense/Completion/Dictionaries/KeywordDictionary.cs:707-711,529-560`
- [ ] T030 [US4] Gate: insert family ≥ 90% + matrix rows P8/P9 in `tests/AkmlSql.Engine.Tests/Completion/CorpusGateTests.cs`

**Checkpoint**: INSERT scoping fixed (38/80 family failures addressed).

---

## Phase 7: User Story 5 — Context-correct keywords and built-in functions (Priority: P3)

**Goal**: Position-correct keyword sets and built-in functions in expression positions (FR-013, FR-014, FR-018; clusters B2–B7, D).

**Independent Test**: Contract §2 rows P10–P15; keywords/functions/where-having families ≥ 90%.

### Tests for User Story 5 (write first, ensure FAIL)

- [ ] T031 [P] [US5] Failing tests for KW-023 (`ORDER |`→BY), KW-026…030 (`LEFT |`→JOIN/OUTER), `UNION |`→SELECT/ALL, `DELETE |`→FROM, CASE THEN/ELSE, `UPDATE TOP (5) t SET |` in `tests/AkmlSql.Engine.Tests/Parser/CursorContextAnalyzerTests.cs` + `tests/AkmlSql.Engine.Tests/Completion/Dictionaries/KeywordDictionaryTests.cs`; built-ins in `WHERE >= | / SET = | / VALUES (|` + scalar UDF in JOIN ON in `tests/AkmlSql.Engine.Tests/Completion/KeywordProviderTests.cs` (or new BuiltInFunctionProvider tests)

### Implementation for User Story 5

- [ ] T032 [US5] B2–B6 — add dedicated-token cases (`Order`/`Group` → new `OrderKeyword`/`GroupKeyword`; `Left/Right/Inner/Cross/Full/Outer` → `JoinQualifier`; `Union/Intersect/Except` → `SetOperator`; `Case/When/Then/Else` state arms) to `DetermineClauseType` in `src/AkmlSql.IntelliSense/Parser/CursorContextAnalyzer.cs:279-431` and the matching keyword sets (`["BY"]`, join-qualifier variants, `SELECT/ALL`, `AfterDelete = FROM/TOP/OUTPUT`, CASE sets) in `src/AkmlSql.IntelliSense/Completion/Dictionaries/KeywordDictionary.cs:529-560`
- [ ] T033 [US5] B7 — TOP-balanced-paren skip in the SET↔UPDATE back-scan (`src/AkmlSql.IntelliSense/Parser/CursorContextAnalyzer.cs:324-334`) and in `IsAfterTableTargetIdentifier` (`src/AkmlSql.IntelliSense/Completion/Providers/ObjectProvider.cs:156-165`)
- [ ] T034 [US5] D — surface `KeywordDictionary.ScalarFunctions` as `Function`-typed items (SortPriority ≥ 200) in expression positions (Where/Having/UpdateSet-value/InsertValues/Select/OrderBy/GroupBy/JoinOn) via `src/AkmlSql.IntelliSense/Completion/Providers/KeywordProvider.cs` (or a new `BuiltInFunctionProvider.cs` + registration); add `AfterInsertValues` mapping; include scalar UDFs in JOIN ON schema-qualified completion (`src/AkmlSql.IntelliSense/Completion/Providers/ObjectProvider.cs:491-499`)
- [ ] T035 [US5] Gate: keywords/functions/where-having families ≥ 90% + matrix rows P10–P15 in `tests/AkmlSql.Engine.Tests/Completion/CorpusGateTests.cs`

**Checkpoint**: Keyword/function polish complete.

---

## Phase 8: User Story 6 — CTEs, temp tables, bracketed/quoted names (Priority: P3)

**Goal**: CTE aliasing/scoping/recursion, temp-table names+tracking, bracket/quote resilience (FR-019…FR-025; clusters E, F1–F3, G).

**Independent Test**: Contract §2 rows P16–P20; cte/temp-tables/brackets-quoted families ≥ 90%.

### Tests for User Story 6 (write first, ensure FAIL)

- [ ] T036 [P] [US6] Failing tests E1 (alias over CTE), E3 (statement-scoped), E4 (`SELECT *` body via sources), E5 (recursive self-ref), E6 (explicit column lists kept) in `tests/AkmlSql.Engine.Tests/Parser/CteResolverTests.cs` + `tests/AkmlSql.Engine.Tests/Parser/TokenBasedCteExtractorTests.cs` + `tests/AkmlSql.Engine.Tests/Completion/ColumnProviderTests.cs`
- [ ] T037 [P] [US6] Failing tests F1 (names offered), F2 (survives unparsable trailing statement), F3 (`SELECT * INTO #t` columns) in `tests/AkmlSql.Engine.Tests/Parser/TempTableTrackerTests.cs` + `tests/AkmlSql.Engine.Tests/Completion/TempTableCompletionTests.cs`
- [ ] T038 [P] [US6] Failing tests G1 (unterminated `[`/`"` neutralized), G2 (PartialText delimiter trim), G3 (`"dbo"."|` dot-scoping), G4 (JOIN schema qualifier respected) in `tests/AkmlSql.Engine.Tests/Parser/CursorContextAnalyzerTests.cs` + `tests/AkmlSql.Engine.Tests/Completion/CompletionEngineTests.cs` + `tests/AkmlSql.Engine.Tests/Completion/JoinOnFkProviderTests.cs` (JoinProvider coverage)

### Implementation for User Story 6

- [ ] T039 [US6] G2 one-liner — `TrimStart('[', '"')` on PartialText + G3 accept `AsciiStringOrQuotedIdentifier` in DotPrefix extraction, in `src/AkmlSql.IntelliSense/Parser/CursorContextAnalyzer.cs:183-211`
- [ ] T040 [US6] G1 — caret-local neutralization of an unterminated `[`/`"` before context tokenization in `src/AkmlSql.IntelliSense/Completion/CompletionEngine.cs` (~line 155; session document untouched)
- [ ] T041 [US6] G4 — respect a typed schema qualifier in FK-join suggestions in `src/AkmlSql.IntelliSense/Completion/Providers/JoinProvider.cs:40-57` (filter to the schema, emit insert text minus the already-typed part)
- [ ] T042 [US6] E1/E3/E4/E5 — CTE-alias dot branch (resolve `DotPrefix` via `AvailableAliases`, copy the temp-table pattern at `src/AkmlSql.IntelliSense/Completion/Providers/ColumnProvider.cs:400-405` onto the CTE branch at :383); statement-scope the CTE walk + recursion-aware self-reference in `src/AkmlSql.IntelliSense/Parser/CteResolver.cs:113-139,128-136`; `SELECT *` bodies fall back to `AvailableCteSources` + schema cache at completion time
- [ ] T043 [US6] E6 — capture explicit column lists in `src/AkmlSql.IntelliSense/Parser/TokenBasedCteExtractor.cs:59-70` (collect depth-1 identifiers instead of discarding)
- [ ] T044 [US6] F1/F2/F3 — temp-table-names branch beside the CTE branch in `src/AkmlSql.IntelliSense/Completion/Providers/ObjectProvider.cs:169-187`; last-batch rule for the containment gate in `src/AkmlSql.IntelliSense/Parser/TempTableTracker.cs:26-32`; record `SELECT * INTO #t FROM src` source (:135-137) and expand from the schema cache at completion time
- [ ] T045 [US6] Gate: cte/temp-tables/brackets-quoted families ≥ 90% + matrix rows P16–P20 in `tests/AkmlSql.Engine.Tests/Completion/CorpusGateTests.cs`

**Checkpoint**: All engine completion families at target.

---

## Phase 9: User Story 7 — Trustworthy suggestions, ranking, connection status (Priority: P3)

**Goal**: FilterText adoption + ranking guards (H2–H4) and honest web connection status with auto-restore (W) (FR-026…FR-028, FR-032, FR-033).

**Independent Test**: Contract §2 rows P21–P23 + §5 status/connection contracts; SC-009 reload check.

### Tests for User Story 7 (write first, ensure FAIL)

- [ ] T046 [P] [US7] Failing tests: qualified items carry FilterText = column name (no table-name flooding in ORDER BY) in `tests/AkmlSql.Engine.Tests/Completion/ColumnProviderTests.cs`; IDENTITY/computed excluded as SET targets (same file); `CROSS APPLY fn_|` offers TVFs in `tests/AkmlSql.Engine.Tests/Completion/CompletionEngineTests.cs`; `…dbo.Or` repair boundary in `tests/AkmlSql.Engine.Tests/Parser/SuffixCompletionHelperTests.cs`

### Implementation for User Story 7

- [ ] T047 [US7] H1-adoption + H2 — set `FilterText` on qualified/decorated items and exclude `IsIdentity || IsComputed` columns as `UpdateSet`-target suggestions in `src/AkmlSql.IntelliSense/Completion/Providers/ColumnProvider.cs:243-304`
- [ ] T048 [US7] H3 + H4 — exempt `APPLY` from the after-table-target suppression in `src/AkmlSql.IntelliSense/Completion/Providers/ObjectProvider.cs:156-165`; word-boundary guard on the `" OR"` repair pattern in `src/AkmlSql.IntelliSense/Parser/SuffixCompletionHelper.cs:48-51` (mirror the `" ON"` fix below it)
- [ ] T049 [US7] W — three-valued connection state (`Offline`/`BridgeOnly`/`SqlConnected`) exposed by the `ISqlConnectionService` implementation + boot-time auto-restore of the last-used Windows-auth saved connection (re-run loopback guard, canonical single SessionId, non-blocking, failure → `BridgeOnly`) in `src/AkmlSql.Web/Services/` ; pill renders the three states distinctly in `src/AkmlSql.Web/Shared/StatusBar.razor` (never "Live" without a SQL session)
- [ ] T050 [US7] W — seed the Database dropdown option list with the saved database on saved-connection selection + service-account-visibility hint in `src/AkmlSql.Web/Shared/ConnectionManagerModal.razor` / `src/AkmlSql.Web/Shared/ConnectionPickerComponent.razor`
- [ ] T051 [US7] Gate: matrix rows P21–P23 in `tests/AkmlSql.Engine.Tests/Completion/CorpusGateTests.cs`; connection state-transition tests in `tests/AkmlSql.Web.Tests/`; live SC-009 check per quickstart.md (F5 reload with saved connection → restored or honest not-connected)

**Checkpoint**: Ranking honest, status honest.

---

## Phase 10: User Story 8 — Formatter idempotency + web built-in styles (Priority: P3)

**Goal**: FMTA-006 oscillation fixed at the root, Stage 7 converges-and-revalidates, web ships Khamis Style/Collapsed built-ins (FR-029…FR-031; cluster J).

**Independent Test**: Double-format byte-equality on FMTA-006; formatting battery 100/100; web style list shows built-ins with Khamis Style active.

### Tests for User Story 8 (write first, ensure FAIL)

- [ ] T052 [P] [US8] Failing property test — format-twice byte-equality over the FMTA-006 chained-CTE input (`tests/completion-corpus/` formatting file or inline) + a parenthesized-JOIN corpus slice, in `tests/AkmlSql.Formatting.Tests/` (per the T009 lesson: property test, never golden regeneration)

### Implementation for User Story 8

- [ ] T053 [US8] J1 — make the JOIN-modifier/ClauseTracker state paren-aware so both passes agree inside CTE/derived-table bodies (kills the oscillation and the stray `INNER JOIN   ` multi-space) in `src/AkmlSql.Formatting/Layout/LineBreakDecider.cs:84-103,195-210` (+ `src/AkmlSql.Formatting/Pipeline/LayoutEngine.cs:384-385` if the explicit-join rewrite needs the same state); full `tests/format-parity` goldens green with zero regenerations
- [ ] T054 [US8] J2 — Stage 7 returns the second pass when it differs, is non-empty, and passes Stage-6 re-validation (Warning kept) in `src/AkmlSql.Formatting/Pipeline/FormatterPipeline.cs:251-271`; surface format diagnostics in the web UI instead of dropping them (web format call site in `src/AkmlSql.Web/`)
- [ ] T055 [US8] J3 — web `ProfileStore` loads `builtin.khamis` + `builtin.collapsed` from `src/AkmlSql.Formatting/Profiles/BuiltIn/{khamis-style,collapsed}.akmlstyle` (same definitions as desktop `ProfileManager.GetBuiltIn()`), default active `builtin.khamis`, dangling-active-id fallback, `builtin.default`/`builtin.ansi` retained — in `src/AkmlSql.Web/Services/IProfileStore.cs` (+ impl) with unit tests in `tests/AkmlSql.Web.Tests/`
- [ ] T056 [US8] Gate: re-run the 100-case formatting battery → 100% idempotent (SC-005) and verify the web style list + default live per quickstart.md

**Checkpoint**: All 8 stories functionally complete.

---

## Phase 11: Polish & Cross-Cutting (acceptance + closure)

- [ ] T057 Full campaign battery re-run via the Playwright harness against the deployed build (corpus from `tests/completion-corpus/`): overall ≥ 95%, zero-item = 0, every previously failing family ≥ 90%, passing families not regressed, engine log zero ERR/WRN (SC-001/002/003/007) — record results in `specs/032-autocomplete-remediation/`
- [ ] T058 Full keystroke-scenario re-run: 100% on dot-trigger, DML-space, Tab-accept, Ctrl+Enter (SC-004) + keyboard-only authoring walkthrough (SC-006)
- [ ] T059 Desktop smoke (SC-008): full engine publish copied to the desktop install (never partial DLL swap), SSMS 22 spot-check of contract rows P2/P5/P8/P16, full desktop test suites green
- [ ] T060 Final perf gate: `tests/AkmlSql.Engine.Tests/PerformanceBaselineTests.cs` vs the T004 baseline (completion p95 < 100 ms, format < 200 ms)
- [ ] T061 [P] Documentation: record the FilterText wire field in `doc/ipc-api.md`, spec-032 outcomes + per-family table in `doc/progress.md`
- [ ] T062 Post-acceptance cleanup (ask the user first): `DROP DATABASE Northwind_AutoTest` + remove the SYSTEM grant note, delete `C:\Program Files (x86)\AKML SQL\Web\test-corpus\` and `.playwright-mcp/results-*.json` + screenshots (campaign report "Cleanup performed / owed")

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: none — start immediately. T002/T004 parallel with T001; T003 needs T001+T002.
- **Foundational (Phase 2)**: needs Setup (T003 harness proves T005/T006 didn't regress scoring). **Blocks all stories** (US2/US4/US7 set FilterText).
- **US1 (Phase 3)**: independent of all engine work — can run in parallel with US2+ (different codebase area: web JS only).
- **US2–US6 (Phases 4–8)**: sequential in priority order **by design** — they all edit `src/AkmlSql.IntelliSense/Parser/CursorContextAnalyzer.cs` (T020/T023/T024/T028/T032/T033/T039) and share `ObjectProvider`/`ColumnProvider`/`KeywordDictionary`; do not run these stories concurrently.
- **US7 (Phase 9)**: engine part (T046–T048) after US6 (same files); web part (T049–T051) independent — may run alongside any engine phase.
- **US8 (Phase 10)**: fully independent of completion work (`AkmlSql.Formatting` + web profile store) — may run in parallel with US2–US7.
- **Polish (Phase 11)**: after all desired stories; T057/T058 need a fresh engine+web deploy.

### Within Each User Story

- Test tasks (marked "write first") MUST fail before their implementation tasks.
- Corpus-gate tasks (T021/T026/T030/T035/T045/T051) close each story — thresholds become asserted from then on (ratchet, no regressions).

### Parallel Opportunities

- **Three independent tracks after Phase 2**: Track A = US1 (web JS, T007–T013); Track B = US2→US3→US4→US5→US6→US7-engine (shared engine files, sequential); Track C = US8 (formatter/profiles, T052–T056) and US7-web (T049–T050).
- Within stories: all "failing tests" tasks marked [P] (different test files) can be written concurrently.
- T002 ∥ T001; T004 ∥ T001–T003; T061 ∥ T057–T060.

---

## Implementation Strategy

**Quick wins first**: after Phase 2, land T023 (B1 `Exec` one-liner) and T039 (G2 bracket-trim) early — they are self-contained and move exec-procs/brackets-quoted immediately; the story checkpoints still gate their families formally.

**MVP = US1 + US2** (both P1): US1 alone restores the SSMS *feel* (dot-trigger, Tab, Ctrl+Enter) and is independently shippable; US2 is the largest suggestion-quality payload (three worst families). Validate each at its checkpoint, deploy/demo after either.

**Incremental delivery**: each story ends with a corpus-gate/E2E checkpoint that ratchets thresholds — later work cannot silently regress earlier families. Engine stories ship to desktop for free via the next engine publish (verify at T059, not before).

---

## Notes

- 62 tasks: Setup 4, Foundational 2, US1 7, US2 8, US3 5, US4 4, US5 5, US6 10, US7 6, US8 5, Polish 6.
- [P] = different files + no pending dependency; engine stories intentionally NOT parallel with each other (shared files).
- Line numbers cite the 2026-07-17 tree (re-verified in research.md); re-check before editing.
- Commit checkpoints are summarize-and-ask (GIT RULE).
