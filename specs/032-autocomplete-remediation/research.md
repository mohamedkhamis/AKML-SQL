# Phase 0 Research — Autocomplete Campaign Remediation

The evidence base is the 2026-07-16 campaign report ([doc/web-autocomplete-campaign-2026-07-16.md](../../doc/web-autocomplete-campaign-2026-07-16.md)), whose ~40 root causes were adversarially verified by a 28-agent workflow against the same source snapshot this branch sits on. For this plan, every load-bearing citation was **re-verified inline against the current working tree** (2026-07-17) — the multi-agent re-verification workflow was unavailable (session limit), and only one commit (4d15bf2, spec-031 formatter/profile remediation) landed since the campaign. Verification status: **all cited mechanisms confirmed present**, with the drift notes below. Format: **Decision / Rationale / Alternatives considered**.

## Source-drift notes (current tree vs. campaign report)

Differences found during re-verification that refine (not contradict) the report:

1. **`ClauseType.Delete` and a `TSqlTokenType.By` back-walk arm now exist** in `DetermineClauseType` ([CursorContextAnalyzer.cs:294](../../src/AkmlSql.IntelliSense/Parser/CursorContextAnalyzer.cs), :350-361). B5 is therefore *only* the missing `KeywordDictionary` mapping (the switch at [KeywordDictionary.cs:529-560](../../src/AkmlSql.IntelliSense/Completion/Dictionaries/KeywordDictionary.cs) has no `ClauseType.Delete` arm → falls to `GeneralKeywords`), and B2 is *only* the missing dedicated-token cases (`TSqlTokenType.Order/Group` with no `BY` yet typed).
2. **A `VariableProvider` exists and is registered** ([CompletionEngine.cs:125](../../src/AkmlSql.IntelliSense/Completion/CompletionEngine.cs), [VariableProvider.cs](../../src/AkmlSql.IntelliSense/Completion/Providers/VariableProvider.cs)) — but it is a dead shell: its `CanHandle` requires `PartialText.StartsWith("@")`, which never happens (`TSqlTokenType.Variable` is excluded from PartialText extraction, CursorContextAnalyzer.cs:206-211), and its data source `context.AvailableVariables` is never populated (`VariableTracker` has zero callers — grep-confirmed). C3/C4 are unchanged in substance; the fix is cheaper than "build a provider" — populate + trigger the one that exists, and add a proc-parameter provider.
3. **`CompletionObjectType.Parameter = 11` already exists** ([CompletionResponse.cs:60](../../src/AkmlSql.Core/Ipc/Messages/CompletionResponse.cs)) — the new parameter provider needs no enum/wire change.
4. **ColumnProvider's temp-table dot branch (spec 030) is the exact pattern the CTE-alias fix needs**: [ColumnProvider.cs:400-405](../../src/AkmlSql.IntelliSense/Completion/Providers/ColumnProvider.cs) already resolves `alias → AvailableAliases → BareTableName → AvailableTempTables`. E1 = replicate this for `AvailableCtes` (alias → `dbo.cte` → strip schema → CTE lookup) just above it (line 383).
5. **Spec 031 shipped the built-in styles as loadable `.akmlstyle` files** (`src/AkmlSql.Formatting/Profiles/BuiltIn/khamis-style.akmlstyle`, `collapsed.akmlstyle`, loaded by `ProfileManager.GetBuiltIn()`); since `AkmlSql.Formatting` runs in-browser (WASM) for web formatting, J3 is a reuse job, not a re-definition job.

---

## R-A — Scope resolution: rewrite `TokenBasedAliasExtractor` + extend `AliasResolver` (A1–A6, F4)

**Verified mechanisms**: depth>0 skip at [TokenBasedAliasExtractor.cs:66](../../src/AkmlSql.IntelliSense/Parser/TokenBasedAliasExtractor.cs); `IsFromOrJoinKeyword` includes `Update`/`Delete` (:147-157) with first-occurrence-wins (:140) → aliased-DML poisoning; statement bounds are semicolon-only (:28-45) → set-operator leakage; exactly-one-dot consumption (:83-99) → three-part-name corruption. `SuffixCompletionHelper.AppendDummyTokens` operates only on the document tail (whole-string `EndsWith`, :8-88). `AliasResolver.CursorScopeFinder` visits only `QuerySpecification` (:151-158, deepest-wins, no ancestor merge); derived tables become `(derived:alias)` placeholders with no columns (:118-125).

**Decision**: One coordinated rework of the token fallback plus targeted AST-resolver extensions:

1. **Cursor-scope-aware token extraction (A1)**: compute the innermost parenthesis span containing the caret; run the existing FROM/JOIN pattern scan **within that span** (treating its bounds as the statement bounds), and additionally collect outer-scope aliases from depth levels *enclosing* the caret (outer entries yield to inner on key conflict). Depth-sibling groups stay excluded (the current depth>0 skip is correct for *siblings* — the bug is that it also excludes the caret's *own* scope).
2. **Two-pass extraction, FROM/JOIN wins (A2/F4)**: pass 1 registers FROM/JOIN-introduced tables (with aliases); pass 2 registers UPDATE/DELETE *target* tokens **only if** the token is not already registered as an alias by pass 1 and the statement has no FROM clause mapping for it. This preserves the deliberate FROM-less DML injection (`UPDATE Orders SET |` — see the code comment at :148-152 and memory `dml-target-alias-resolution`) while unpoisoning `UPDATE o SET … FROM Orders o`.
3. **Set-operator boundaries (A5)**: during the statement-bounds computation, depth-0 `Union/Intersect/Except` dedicated tokens bound the scan relative to the caret (same shape as the semicolon logic).
4. **Multi-part chain consumption (A6)**: replace the single `schema.table` two-identifier parse with a loop consuming `id(.id)*` (up to 4 parts), keeping the last part as the table, second-to-last as schema; same loop feeds `DotPrefix` extraction in `CursorContextAnalyzer` (:176-201) so `db.dbo.|` and `"dbo"."|` scope correctly (with R-G3).
5. **Cursor-position dummy insertion (A1, second half)**: add `SuffixCompletionHelper.RepairAtCursor(sql, cursorOffset)` that applies the existing tail-repair patterns **at the caret** (insert dummy tokens into the string at cursorOffset, then close any parens left unbalanced *after* the caret) so a broken-at-caret subquery parses and the AST path takes over. `CompletionEngine.GetCompletions` calls it before `ParseWithSuffix` when the plain parse fails and the caret is inside parens.
6. **AST-resolver extensions (A3/A4)**: `CursorScopeFinder` also visits `UpdateSpecification`/`DeleteSpecification`/`MergeSpecification` (their `FromClause` + target table); scope resolution merges **ancestor** QuerySpecification scopes into the innermost result (inner wins on conflict) for correlated subqueries; `QueryDerivedTable` gets its projection enumerated with the same select-element walker `CteResolver.InferColumnsFromQuery` already implements (expose/reuse it) instead of the zero-column placeholder.

**Rationale**: A1/A2 alone explain the three worst families (subqueries, update, delete). The token fallback is the layer every broken-at-caret document lands on, so fixing scope there lifts all families at once; the AST extensions make cleanly-parsing DML/correlated cases *not need* the fallback. Keeping the two-pass rule inside the one file also fixes F4 for free.

**Alternatives considered**: (a) AST-only fix (drop the token fallback) — rejected: mid-edit documents frequently don't parse even with tail repair; the fallback is load-bearing. (b) Caret-position repair only (no token-extractor rework) — rejected: repair can't fix alias-map poisoning (A2) or set-operator leaks in still-unparsable docs. (c) Full incremental-parser adoption — out of scope, disproportionate.

**Blast radius / regression guards**: `TokenBasedAliasExtractor.Extract` feeds `AvailableAliases` for *every* completion on unparsable docs, and wildcard expansion uses it too (comment at :47-51). Guards: keep the existing depth-sibling exclusion tests green (`TokenBasedAliasExtractorTests`), memory traps `dml-target-alias-resolution` (FROM-less DML must keep working) and `joinon-current-target-scoping` (ON-clause empty-target fallback is load-bearing).

## R-B — Clause detection: add dedicated-token cases + keyword sets (B1–B7)

**Verified mechanisms**: the `DetermineClauseType` switch ([CursorContextAnalyzer.cs:279-431](../../src/AkmlSql.IntelliSense/Parser/CursorContextAnalyzer.cs)) has `case TSqlTokenType.Execute:` (:336) but no `Exec`; no `Order`/`Group` dedicated-token cases (the Identifier-text arms :363-373 are dead when ScriptDom emits dedicated tokens); Identifier-text arms for UNION/INTERSECT/EXCEPT and join qualifiers (:383-398) are equally dead for dedicated tokens; the SET↔UPDATE back-scan (:324-334) breaks on `)` (B7); `case TSqlTokenType.Insert: return ClauseType.InsertColumns;` (:424) conflates target and column positions (see R-C).

**Decision**:
- **B1**: add `case TSqlTokenType.Exec:` beside `Execute` — the one-liner, first fix to land.
- **B2**: add `case TSqlTokenType.Order:` / `case TSqlTokenType.Group:` returning new `ClauseType.OrderKeyword` / `GroupKeyword` (keyword set = `["BY"]`); keep existing `OrderBy/GroupBy` for the after-BY position.
- **B3**: add dedicated-token cases for `Left/Right/Inner/Cross/Full/Outer` returning new `ClauseType.JoinQualifier` (keyword set = `JOIN`, `OUTER JOIN`, `APPLY` variants as appropriate per qualifier); the existing Identifier-arm "continue" behavior remains for genuinely identifier-tokenized text.
- **B4**: add dedicated-token cases for `Union/Intersect/Except` returning new `ClauseType.SetOperator` (keyword set = `SELECT`, `ALL` for UNION) — also the statement-boundary behavior the Identifier arm already intends.
- **B5**: add `ClauseType.Delete => AfterDelete` to `GetKeywordsForClause` with `AfterDelete = ["FROM", "TOP", "OUTPUT"]`.
- **B6**: add CASE tracking: a backward-walk arm for `TSqlTokenType.Case/When/Then/Else` that classifies `CaseWhen` (offer `WHEN`), `CaseThen` (offer `THEN`), `CaseElse` positions with keyword sets; expression providers still contribute (columns/functions valid inside CASE arms).
- **B7**: make the SET back-scan and `IsAfterTableTargetIdentifier` ([ObjectProvider.cs:156-165](../../src/AkmlSql.IntelliSense/Completion/Providers/ObjectProvider.cs)) skip a **balanced** paren group when the preceding keyword is `TOP` (scan back over `( … )` where the group is preceded by `TOP`).

**Rationale**: each is a narrow, additive switch case or array; the backward walk stays O(distance-to-clause-keyword). New ClauseType values are engine-internal (not on the wire).

**Alternatives**: text-matching every token (`t.Text.ToUpper()`) instead of dedicated-token cases — rejected: that's the existing dead-code trap; dedicated token types are the reliable signal. A full grammar-state machine — rejected as disproportionate.

## R-C — INSERT target injection, proc parameters, variables (C1–C5)

**Verified mechanisms**: INSERT collapses to one `ClauseType.InsertColumns` (:424) while `DetectAlterClauseType` (:443+) shows the working target-injection pattern (populate `context.AvailableAliases` during clause detection); `AfterInsert` lacks `INTO` ([KeywordDictionary.cs:707-711](../../src/AkmlSql.IntelliSense/Completion/Dictionaries/KeywordDictionary.cs)); `ClauseType.InsertValues` falls to `GeneralKeywords` (:559); PartialText excludes `Variable` tokens (:206-211); `VariableTracker` uncalled; `VariableProvider` dead (drift note 2).

**Decision**:
1. **C1/C2 — split the INSERT context and inject the target**: in the backward walk, when hitting `Insert`, scan **forward** for `INTO` + multi-part table name + `(`: if the caret is inside that paren → `ClauseType.InsertColumnList` with the target table injected into `AvailableAliases` (mirroring the ALTER TABLE pattern); if the caret is before/at the table position → `ClauseType.InsertTarget` (ObjectProvider offers tables/views only — no procs/functions); otherwise (right after `INSERT`) stay `InsertColumns` with `AfterInsert` gaining `"INTO"`. `ColumnProvider` treats `InsertColumnList` as a single-table bare-column context (no alias qualification), excluding IDENTITY/computed columns (shares R-H2's filter; matches `ExpandInsertColumnsOperation`'s existing rule at [ExpandInsertColumnsOperation.cs:134](../../src/AkmlSql.IntelliSense/Refactoring/Operations/Lightweight/ExpandInsertColumnsOperation.cs)).
2. **C3 — new `ParameterProvider`**: `CanHandle` = clause `Exec` (after B1 it actually fires) with a resolved proc name in the statement, or PartialText starting `@` in an EXEC argument position. Data source: the schema cache's per-procedure parameter list (already loaded by Phase B; `SignatureProvider` reads it — reuse its lookup). Items: `ObjectType = Parameter (11)`, InsertText `@Name`, SecondaryText = type. Registered in the `CompletionEngine` constructor.
3. **C4 — variables**: include `TSqlTokenType.Variable` in PartialText extraction (so `@C|` yields PartialText `@C`), and populate `context.AvailableVariables` from `VariableTracker` in `CursorContextAnalyzer.Analyze` (batch-scoped DECLARE scan; token-based so it works mid-edit). The existing `VariableProvider` then works as written.
4. **C5 — web replace-span**: widen the CM6 `matchBefore` regex ([akml-editor.js:140](../../src/AkmlSql.Web/wwwroot/js/akml-editor.js)) from `/[\w]+/` to `/[@#\w]+/` so accepting `@CustomerID` over `@C` (and `#t` over `#`) replaces the full token — no `@@`/`##` doubling.

**Rationale**: C1 is the highest-value single fix after B1 (38/80 failures); the injection pattern is proven in-file. C3/C4 make both EXEC assistance and plain variable completion work with one small provider plus two wiring changes. Desktop caveat honored: parameter items use `Parameter`, never `Snippet` (memory `ssms-completion-snippet-objecttype`).

**Alternatives**: resolving INSERT columns via the AST insert-statement node — rejected: mid-typing `INSERT INTO t (` rarely parses; the token walk is already positioned to do this. A DECLARE-parsing AST visitor for variables — rejected: `VariableTracker` exists and is token-based (mid-edit-safe); it just needs a caller.

## R-D — Built-in function surfacing (D)

**Verified mechanisms**: `ScalarFunctions` (~130 entries, :156-223) referenced only by `GetAllKeywords` (:575); no `InsertValues` mapping (:559); JOIN ON schema-qualified completion excludes scalar UDFs ([ObjectProvider.cs:491-499](../../src/AkmlSql.IntelliSense/Completion/Providers/ObjectProvider.cs)).

**Decision**: add a `BuiltInFunctionProvider` (or extend `KeywordProvider`) that emits `ScalarFunctions` as `ObjectType = Function` items with `InsertText = "NAME("`-style insertion **in expression positions**: `Where`, `Having`, `UpdateSet` (value side), `InsertValues`, `Select`, `OrderBy`/`GroupBy`, JOIN ON. Add an `AfterInsertValues` keyword mapping (`DEFAULT`, `NULL`, `SELECT`). Ranked below columns (SortPriority ≥ 200) so schema data stays on top. Include scalar UDFs in JOIN ON schema-qualified completion (drop the exclusion at ObjectProvider :491-499).

**Rationale**: the catalog exists and is versioned; per-clause emission is a routing change, not new data. Ranking below columns prevents flooding the popup in column-first positions.

**Alternatives**: emitting functions from `GetAllKeywords` into every clause — rejected: floods contexts where functions are invalid (FROM table positions). Schema-cache-driven built-ins — rejected: built-ins aren't catalogued in `sys.objects`.

## R-E — CTE resolution (E1–E6)

**Verified mechanisms**: raw-name-only CTE branch at [ColumnProvider.cs:383](../../src/AkmlSql.IntelliSense/Completion/Providers/ColumnProvider.cs); batch-scoped (not statement-scoped) resolution at [CteResolver.cs:113-139](../../src/AkmlSql.IntelliSense/Parser/CteResolver.cs); blanket self-reference exclusion (:128-136); `SELECT *` bodies unresolved (:207-219, "can't fully resolve without schema info"); explicit column lists discarded by the token fallback ([TokenBasedCteExtractor.cs:59-70](../../src/AkmlSql.IntelliSense/Parser/TokenBasedCteExtractor.cs) — the list is depth-skipped, contents dropped).

**Decision** (six fixes, one subsystem):
1. **E1**: in `GetDotQualifiedColumns`, before the schema-cache fallthrough, resolve `DotPrefix` through `AvailableAliases` and check the *resolved bare name* against `AvailableCtes` — copy of the adjacent temp-table branch (drift note 4).
2. **E2**: covered by R-A1 (cursor-scope extraction) — no separate CTE work.
3. **E3**: statement-scope the resolver: bound the CTE walk to the statement containing the cursor (locate the `WITH` statement's extent; CTEs from other statements in the batch are skipped) — the same per-statement scoping `AliasResolver` already received.
4. **E4**: when a CTE body is an unqualified `SELECT *`, fall back to the body's source tables: `CteVisitor` records the body's FROM table names (`AvailableCteSources` exists for this); `ColumnProvider`/`CteResolver` expand them against the schema cache at completion time (cache is available there).
5. **E5**: replace the blanket exclusion with a recursion-aware rule: if the CTE's body contains a set operator (the recursive shape) or the reference is anywhere after the anchor member, offer the CTE name (columns = anchor-member projection); simple non-recursive self-reference stays suppressed.
6. **E6**: make `TokenBasedCteExtractor` capture the explicit column list while skipping it (collect identifiers at depth 1 between the parens instead of discarding), and route later-CTE-body parse failures through R-A5's caret repair.

**Rationale**: each fix is at the layer that owns the data; E1 and E4 together cover the most-failed CTE shapes. E4's schema-cache expansion mirrors what wildcard expansion already does for real tables.

**Alternatives**: full CTE symbol table with lazy projection resolution — rejected: the current dictionary model (`name → columns`) plus a sources-fallback covers the corpus; a symbol table is a rewrite with no additional corpus coverage.

## R-F — Temp tables (F1–F3)

**Verified mechanisms**: `ObjectProvider` has a CTE-names branch but no temp branch (:169-187); batch-containment gate drops all definitions when the parsed extent shrinks ([TempTableTracker.cs:26-32](../../src/AkmlSql.IntelliSense/Parser/TempTableTracker.cs)); `SELECT * INTO #t` records empty columns (:135-137).

**Decision**: (F1) add a temp-table-names branch beside the CTE branch in `ObjectProvider` (SortPriority 50, SecondaryText "temp table") for From/JoinTable/JoinOn/InsertTarget/UpdateTable clauses. (F2) relax the gate: when the cursor is past the last parsed batch's extent (the shrunken-parse case), still visit the **last** batch (definitions before the caret are what matter). (F3) at completion time, when a temp table's recorded column list is empty and its source table is known, expand from the schema cache (record the source table name in the tracker for `SELECT * INTO #t FROM src` shapes; the visitor already walks the query — capture its FROM). F4 rides on R-A2.

**Rationale**: names-first (F1) is the visible win; F2's "last batch" heuristic matches how `TokenBasedAliasExtractor` already treats the trailing unparsable statement.

**Alternatives**: tracking temp tables in the schema cache/session — rejected: they're per-document artifacts; context-level tracking (existing model) is correct.

## R-G — Bracketed/quoted identifiers (G1–G4)

**Verified mechanisms**: PartialText keeps `[`/`"` (CursorContextAnalyzer.cs:206-211 — `Substring` from token start); DotPrefix accepts only `Identifier/QuotedIdentifier` (:183, :196), so `AsciiStringOrQuotedIdentifier` (`"dbo"."`) never scopes; unterminated `[`/`"` at the caret fuses the remainder into one token before `_contextAnalyzer.Analyze` sees it ([CompletionEngine.cs:155](../../src/AkmlSql.IntelliSense/Completion/CompletionEngine.cs)); `JoinProvider` ignores typed schema qualifiers ([JoinProvider.cs:40-57](../../src/AkmlSql.IntelliSense/Completion/Providers/JoinProvider.cs) — no DotPrefix awareness).

**Decision**: (G2) `TrimStart('[', '"')` on PartialText — the second one-liner, lands with B1. (G3) accept `AsciiStringOrQuotedIdentifier` in DotPrefix extraction (trim the quotes). (G1) cursor-local neutralization in `CompletionEngine` before tokenization: if the token containing the caret is an unterminated quoted identifier/string (detectable: last token spans caret to EOF and opens with `[`/`"`), virtually close it at the caret for the *context* tokenization pass (the session document is untouched). (G4) in `JoinProvider.GetCompletions`, when `context.PrecedingDot`/`DotPrefix` names a schema, filter FK-join suggestions to that schema and emit unqualified insert text for the already-typed part.

**Rationale**: G2 alone un-blanks every `[partial` filter; G1 turns "stream destroyed" into "normal partial-identifier completion". Insert-side bracket wrapping already exists (bracket-mode parity tests) and composes.

**Alternatives**: lexer-level tolerant tokenizer — rejected: ScriptDom's lexer isn't pluggable; caret-local string surgery is contained and testable.

## R-H — Ranking & filter fidelity (H1–H4)

**Verified mechanisms**: fuzzy scores `DisplayText` (CompletionEngine.cs:381) while ambiguity-prone clauses emit `alias.column` display labels (ColumnProvider.cs:287-303); no IDENTITY/computed filter in the SET-target path (ColumnProvider.cs:243-304; model has `IsIdentity/IsComputed` — [DatabaseObject.cs:41-42](../../src/AkmlSql.IntelliSense/Schema/Models/DatabaseObject.cs)); `IsAfterTableTargetIdentifier` suppresses after `)`/identifier incl. `APPLY` (ObjectProvider.cs:156-165); `EndsWith("OR")` tail-match ([SuffixCompletionHelper.cs:48](../../src/AkmlSql.IntelliSense/Parser/SuffixCompletionHelper.cs)).

**Decision**: (H1) add `FilterText` to `CompletionItem` (`[Key(7)]`, additive — current keys end at 6, [CompletionResponse.cs:43](../../src/AkmlSql.Core/Ipc/Messages/CompletionResponse.cs)); providers set it to the *matchable* text (column name for qualified items); `CompletionEngine` scores `FilterText ?? DisplayText`. Wire-compatible; hosts ignore it. (H2) exclude `IsIdentity || IsComputed` columns when the clause is `UpdateSet` target position (value side unaffected). (H3) exempt `APPLY` (and `TABLESAMPLE`-style contexts) from the after-table-target suppression via a text check on the preceding identifier; `CROSS/OUTER APPLY` positions route to function-including object completion. (H4) tighten the repair patterns to require a word boundary before the keyword (` OR`/line-start, matching the existing ` ON` fix one block below it).

**Rationale**: H1 is the systemic fix (every family's ranking improves); the rest are one-guard changes. Fuzzy *matching semantics* stay untouched (fuzzy-by-design cases remain excluded per spec Assumptions).

**Alternatives**: scoring against InsertText — rejected: qualified items insert `alias.column`, same flooding. Splitting display into columns-only labels — rejected: the qualified display is a deliberate disambiguation UX.

## R-I — Web editor triggers & keys (I1–I4, C5)

**Verified mechanisms**: `POST_KEYWORD_TRIGGER` lacks DML verbs ([akml-editor.js:92](../../src/AkmlSql.Web/wwwroot/js/akml-editor.js)); the manual-open path exists and is where non-word-char triggers must hook (:113-134 — `typedNonWord` regex already includes `.` but the line is then gated on the keyword regex only); the `completionSource` gate (:140-152) accepts word-prefix/post-keyword/explicit only; keymap precedence: ghost → wildcard → `defaultKeymap`+`indentWithTab` (:578-585) with **no** accept-completion Tab arm, and `navKeymap` (Mod-Enter → runExecute, :567-571) registered **after** the defaultKeymap spread (:603) so `defaultKeymap`'s own Mod-Enter (insertBlankLine) shadows it.

**Decision**:
1. **I1 dot-trigger**: add `DOT_MEMBER_TRIGGER = /[\w\]"]\.$/` (identifier, `]`, or `"` followed by dot at caret): (a) in the updateListener's `typedNonWord` branch, `startCompletion` when it matches the line-up-to-caret; (b) in `completionSource`, accept the context when it matches (return `from: context.pos`). Both places are required — CM6's `activateOnTyping` never fires on non-word chars, so the manual open is the only path in.
2. **I2**: extend `POST_KEYWORD_TRIGGER` with `update|insert(\s+into)?|into|delete(\s+from)?|exec(ute)?`.
3. **I3 Tab-accept**: register `{ key: 'Tab', run: cm.autocomplete.acceptCompletion }` in a keymap placed **after ghostKeymap, before wildcardKeymap** — ghost-text accept keeps priority; `acceptCompletion` returns false when no popup is open, falling through to wildcard-expand then indent. Verify `acceptCompletion` is exported by the vendored bundle (`tools/codemirror` esbuild — rebuild if the export is missing; the bundle is vendored, no CDN — memory `web-codemirror-vendor-and-iis`).
4. **I4 Ctrl+Enter**: move the `Mod-Enter → runExecute` binding into a keymap registered **before** the `defaultKeymap` spread (or hoist `navKeymap` above it in the extensions array). Keep F5 unbound (browser refresh).
5. **C5**: replace-span regex → `/[@#\w]+/` (R-C4).
6. **Offline guard**: the new triggers call the same `completionSource`; when offline it returns keyword/snippet items or null — verify no empty-popup flicker by returning null for zero items (already the case, :160).

**Rationale**: all four are localized to one file plus a possible vendored-bundle rebuild; they restore the SSMS muscle-memory gestures the campaign measured as the single worst UX failure (48/101 keystroke scenarios).

**Alternatives**: CM6 `autocompletion({ activateOnCompletion })`/custom `closeBrackets`-style trigger extension — rejected: the manual `startCompletion` path already exists and is the project's established pattern (T109).

## R-J — Formatter idempotency + web built-in styles (J1–J3, finding 7)

**Verified mechanisms**: Stage 7 is detect-only — computes the second pass, appends a Warning, returns the **first** pass ([FormatterPipeline.cs:251-271](../../src/AkmlSql.Formatting/Pipeline/FormatterPipeline.cs)); the JOIN-modifier collapse guard depends on `prevSemanticTokenType`/`ClauseTracker` state ([LineBreakDecider.cs:84-103](../../src/AkmlSql.Formatting/Layout/LineBreakDecider.cs)) which is paren-blind per the report (ClauseTracker early-returns inside parens → first pass breaks bare `JOIN`, second pass sees `INNER JOIN` and collapses); web `ProfileStore.BuiltInIds = {"builtin.default","builtin.ansi"}` ([IProfileStore.cs:48](../../src/AkmlSql.Web/Services/IProfileStore.cs)).

**Decision**: (J1) fix the root oscillation: make `ClauseTracker`/the JOIN-modifier detection paren-aware so pass 1 and pass 2 make the same decision inside CTE/derived-table bodies (also kills the stray multi-space run). Prove by **property test** (format twice, assert byte-equal) over the FMTA-006 shape + a parenthesized-JOIN corpus slice — *not* by regenerating goldens (T009 lesson: goldens are AKML-own drift guards; memory `t009-formatter-paren-hug`). (J2) Stage 7 returns the **second pass** when it differs, is non-empty, and passes a Stage-6 re-validation; the Warning diagnostic stays. Web side: surface format diagnostics (toast/status line) instead of dropping them. (J3) web `ProfileStore` loads the same `khamis-style.akmlstyle` + `collapsed.akmlstyle` built-ins `ProfileManager.GetBuiltIn()` uses (embed as WASM resources), IDs `builtin.khamis` (default active for fresh installs) + `builtin.collapsed`, keeping `builtin.default`/`builtin.ansi` for backward compatibility of persisted references.

**Rationale**: J1 removes the cause; J2 makes the safety net honest for any residual non-idempotent rule; J3 closes the spec-031 desktop/web inconsistency with zero duplicated style definitions.

**Alternatives**: (J2-alt) always return second pass — rejected: on a genuinely oscillating rule the second pass isn't guaranteed stable either; converge-and-validate is the honest contract. (J3-alt) fetch built-ins from the engine over the bridge — rejected: web formatting must work offline (WASM pipeline).

**Blast radius**: `tests/format-parity` goldens (610+) must stay green — J1 runs the full suite; existing golden diffs indicate a real behavior change to review, not to regenerate. Perf note: Stage 7 already runs the second pass; J2 adds only a Stage-6 validation of it (bounded by `EnableIdempotencyCheck`).

## R-W — Web connection status honesty (findings 5–6, FR-032/033)

**Mechanisms located** (behavior verified end-to-end by the campaign; component map): status pill = [StatusBar.razor](../../src/AkmlSql.Web/Shared/StatusBar.razor) ("Live — Live IntelliSense available." reflects **bridge** connectivity only); SQL session state = `ISqlConnectionService` + `ISavedSqlConnectionStore`; DB dropdown + saved-connection selection = `ConnectionManagerModal.razor`/`ConnectionPickerComponent.razor`; schema ownership = `ISchemaSync` (single-owner; memory `web-sql-connection-architecture`).

**Decision**: (1) **Status truth**: pill state becomes three-valued — Offline / Bridge-only ("Live · not connected to SQL") / SQL-connected — driven by `ISqlConnectionService` state, not just bridge state. (2) **Auto-restore**: on boot, when a last-used saved connection exists, attempt a non-blocking reconnect (Windows auth only — no SQL password is at rest by design; SQL-auth saved connections surface a "reconnect" prompt instead). The restore re-runs the loopback guard and routes through the canonical one-SessionId wiring; failure degrades to the honest Bridge-only pill. (3) **DB dropdown**: when a saved connection is selected, seed the dropdown's option list with the saved database (the bound value is already correct — the option list just isn't repopulated). (4) **Filtered-list hint**: static hint under the dropdown ("Databases the engine service account can access") — no permission changes (spec Out of Scope).

**Rationale**: matches the spec's floor (honest status) plus the preferred default (auto-restore) with the security constraints already established in spec 029/030 designs.

**Alternatives**: blocking connect-on-boot — rejected (first-paint latency, failure UX). Persisting SQL-auth secrets to enable full auto-restore — rejected (spec 029 design decision stands).

## R-T — Test & verification strategy

**Verified infrastructure**: completion/parser unit tests live in `tests/AkmlSql.Engine.Tests/Completion/*` + `Parser/*` (direct harness: `new CompletionEngine(new TsqlParserService()).GetCompletions(sql, offset, cache)`; per-cluster classes already exist: `TokenBasedAliasExtractorTests`, `CursorContextAnalyzerTests`, `CteResolverTests`, `TempTableTrackerTests`, `SuffixCompletionHelperTests`, `VariableTrackerTests`, `ColumnProviderTests`, `VariableProviderTests`, `TempTableCompletionTests`, `KeywordDictionaryTests`…). Formatter: `tests/AkmlSql.Formatting.Tests` + `tests/format-parity` goldens + `FormatParityTests`. Perf: `tests/AkmlSql.Engine.Tests/PerformanceBaselineTests.cs` (~13 min; baseline drift is environmental — re-baseline with `AKML_UPDATE_BASELINE=1`, don't hunt phantom regressions; memory `perf-gate-baseline-drift`). E2E: the campaign harness (Playwright + in-page CM driver) with its 22-file/1,470-case corpus — corpus lives in the session scratchpad + a static copy at `C:\Program Files (x86)\AKML SQL\Web\test-corpus\`; **not yet in-repo**.

**Decision**:
1. **Unit-first (TDD)** per cluster in the existing test classes — every fix lands with a failing test reproducing the campaign case (use the report's repro SQL verbatim).
2. **Corpus-in-repo**: import the campaign corpus (22 JSON files) to `tests/completion-corpus/` and add a **corpus-driven engine-level test runner** (xunit theory feeding `CompletionEngine.GetCompletions` with a fake `DatabaseCache` seeded to the Northwind_AutoTest shape). This converts SC-001/SC-002/SC-003 into a locally runnable gate — no browser needed for the engine-side 90% of fixes. At-cap (`atCap`) and corpus-mistake cases carry an `excluded` marker.
3. **Browser keystroke pass** (SC-004): re-run the campaign's Playwright keystroke scenarios against the deployed web build for I1–I4 (they can't be unit-tested); wire the check into `AkmlSql.Web.E2E.Tests` where feasible.
4. **Formatter**: property-based idempotency test (format twice → byte-equal) over the formatting corpus + FMTA-006; full `Formatting.Tests` + goldens stay green.
5. **Perf gate**: run `PerformanceBaselineTests` before/after the scope-resolution rework (the completion hot path gains work: two-pass extraction, ancestor merge). Budget: completion p95 < 100 ms unchanged (SC/plan constraint).
6. **Desktop smoke** (SC-008): existing desktop suites + one manual SSMS pass over the fixed families (engine is shared; wait-2s rule for web session-doc sync applies to web tests only — memory `web-completion-path`).

**Alternatives**: browser-only verification (re-run the whole campaign per change) — rejected as the inner loop (minutes vs. seconds); it remains the **acceptance** gate. Golden-file completion snapshots — rejected: item sets are cache-dependent; assertion-per-case is stabler.
