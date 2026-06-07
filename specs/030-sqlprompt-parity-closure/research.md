# Phase 0 Research — SQL Prompt Parity Gap Closure

All spec ambiguities were resolved in `/speckit.clarify` (single-feature phased delivery; database-wide Smart Rename; held latency budgets). This document records the **technical approach decisions** for each gap area, grounded in the per-row code evidence in `doc/_Prompt-Gap/` (each row carries a `file:line`/class note) and direct reads of the current code. Format: **Decision / Rationale / Alternatives considered**.

---

## R1 — Activate the formatter `Rules/*` passes (P1) — feasible but **risk-gated**, spike first

> **Correction (post-review):** this was initially framed as cheap wiring. The repo's own history disputes that on the hardest sub-part, so R1 is split into a de-risk spike + a graduated rollout, and is **not** a one-line insert. Read this before sequencing P1.

**What's confirmed true**: `LayoutEngine.BuildLayout(...)` returns `List<LayoutNode>`, and every rule set (`Rules/{Dml,Ddl,Join,List,Parenthesis,ControlFlow}Rules.Apply(List<LayoutNode>, FormattingProfile)`) plus `AlignmentCalculator`/`CollapseEvaluator` consume that exact IR. The pipeline today runs only `BuildLayout → ApplyCasing → Emit` and never calls the rule sets — verified by the audit (three ways) and a grep (the rule/action classes are never instantiated). `ControlFlowRules` even contains **real CASE + CTE token-stream pattern recognition** (`ApplyCaseRules` :343, `ApplyCteRules` :586, +CTE column-list/body/END-alignment helpers), built **after** the deferral below.

**What history disputes**: progress.md (2026-05-23, §"Phase B architectural finding") deferred the CTE/CASE/Operators/IN-list layout work **"as architectural, a separate spec — not a single PR,"** because `LayoutEngine` is **token-stream only** (walks `IList<TSqlParserToken>` with a `ClauseTracker`; no AST recognition for `CommonTableExpression`/`CaseExpression`/`InPredicate`/`BooleanComparisonExpression`) and there was no rule slot for those constructs. Since then CASE + CTE recognition got built into `ControlFlowRules`; **Operators (`BooleanComparison`) and IN-list (`InPredicate`) layout appear still unbuilt** — the genuine Phase-B residual, which is "build pattern recognition, then apply," not wiring.

**The real risk** the rules were never exercised **through the full pipeline** — their tests call `_rules.Apply(syntheticNodes, profile)` directly. Run post-`BuildLayout`/`LineBreakDecider`, they may (a) double-apply or fight line-break/indent decisions `LineBreakDecider` already made (ControlFlowRules.cs:1332 already carries a "pre-break the END line before ApplyCaseRules set its IndentLevel" ordering hack — evidence of delicate interaction), breaking **Stage 7 idempotency**, or (b) emit text that fails **Stage 6 semantic re-parse**. This is exactly the "wiring without layout integration" trap PR #239's review guarded against.

**Decision**:
1. **R1.0 — De-risk spike (first P1 task, time-boxed).** Behind an off-by-default flag, insert `rulesEngine.Apply(layoutNodes, profile)` after `BuildLayout` (in both `Format` and the `FormatInternal` idempotency helper). Run the existing format corpus through the **full** pipeline and record, per rule group, whether Stage 6 (validation) and Stage 7 (idempotency) hold and the latency delta. Output: a go/no-go per rule group.
2. **R1.1 — Graduated rollout.** Turn on only the rule groups the spike proves idempotent + validation-clean (likely Dml/Join/List/Parenthesis first; ControlFlow/CASE/CTE if the spike clears them). Each group ships with pipeline-level idempotency + semantic-equivalence tests, not just direct `_rules.Apply` tests.
3. **R1.2 — Operators/IN-list (residual Phase B).** Treat as build-pattern-recognition work (its own task/spike), **not** wiring; it may slip to a follow-up if it can't be made idempotent cheaply.

**Alternatives considered**: (a) Re-implement layout inside `LayoutEngine` — rejected: duplicates working, tested code. (b) Apply rules *after* `TextEmitter` as string transforms — rejected: loses structural context, worse for idempotency. (c) Ship all rule groups at once without the spike — rejected: this is precisely what was deferred as architectural; a group that breaks idempotency would corrupt formatting silently.

**Phasing implication**: R1 stays the P1 *value* target, but its first deliverable is the spike. If the spike finds deep idempotency conflicts across most groups, P1 should re-sequence to a wireable subset and the rest re-planned — re-evaluate after R1.0.

### R1 spike results — T006 gate (2026-06-07): **GO**

Ran the off-by-default `FormatterPipeline.LayoutRules` hook over a 12-statement corpus through the **full** pipeline (default profile), per rule group and all-together (`tests/AkmlSql.Formatting.Tests/Pipeline/R1RuleWiringSpikeTests.cs`):

| group | new valFail | idemFail | exc | changed vs baseline | clean |
|---|--:|--:|--:|--:|--:|
| Dml | 0 | 0 | 0 | 6/12 | 10/12 |
| Ddl | 0 | 0 | 0 | 1/12 | 10/12 |
| Join | 0 | 0 | 0 | 0/12 | 10/12 |
| List | 0 | 0 | 0 | 9/12 | 10/12 |
| Parenthesis | 0 | 0 | 0 | 5/12 | 10/12 |
| ControlFlow | 0 | 0 | 0 | 3/12 | 10/12 |
| **ALL** | **0** | **0** | **0** | **10/12** | 10/12 |

The rules actively transform output (List 9/12, Dml 6/12, ALL 10/12 changed) yet introduce **zero new Stage-6 validation failures, zero idempotency failures, zero exceptions** — even all groups together. The feared LineBreakDecider conflict / double-apply / idempotency break did **not** occur on this corpus. **Decision: GO** — proceed to the graduated rollout (T007+).

Caveats carried into the rollout:
- **Corpus is 12 statements + default profile only.** Expand it (nested CTEs, window functions, complex CASE, MERGE variants, deeply nested parens) and parametrize key profile variants (leading commas, CASE/CTE placement) in the per-group gates before flipping rules on in production.
- **JoinRules changed 0/12** — inactive at default options here; verify it transforms + stays clean under a profile enabling its options (e.g. `AlignJoinKeyword`) before marking Join done.
- **Operators/IN-list confirmed unbuilt** — only six rule classes exist (Dml/Ddl/Join/List/Parenthesis/ControlFlow); no `OperatorsRules`/`InStatementsRules`. R1.2 stays "build pattern recognition," likely a follow-up.
- **Pre-existing base-pipeline bug (separate follow-up):** the GROUP-BY/HAVING-aggregate and CTE statements fail Stage 6 validation **with no rules** — the current production formatter returns the original for those. Out of R1's scope but worth a tracked fix.
- **Latency delta not yet measured** — needs the T003 perf-baseline harness; gate at T014 before production enable.

### R1 production-rollout investigation — REVISED: **NO-GO for enabling the rules as-is**

The spike's GO was correct *only on the two axes it measured* (Stage-6 validation + Stage-7 idempotency). A deeper rollout-readiness workflow (8 agents) + three empirical checks proved those two gates are **insufficient** — they do not protect **visual indent correctness**, which the as-is rules regress.

Evidence chain (all reproducible in `tests/AkmlSql.Formatting.Tests`):

1. **Nested-indent de-dent — confirmed** (`R1IndentInspectionTests`): with `DmlRules` on, nested `AND`/`OR` inside a subquery move from column 8 to **column 0**. Output stays `ValidationPassed=true` and is idempotent, so it passes both gates yet is a visible default-profile regression.
2. **Root cause is systemic, not a localized bug** (grep of `Rules/*` for `IndentLevel =` writes): **DmlRules (~13) and DdlRules (9) write exclusively hardcoded absolute indent** (`= 0/1/2`); ControlFlow/Join are mixed (some `Math.Max`/computed-relative). The rule sets were authored to **own** indentation (their unit tests feed flat synthetic node lists), so when run after `LayoutEngine` they **clobber** the nested indent LayoutEngine already computed instead of refining it. This is exactly the "architectural" mismatch the spec-020 Phase-B note flagged.
3. **Golden oracle — 36 of 610 tests regress** (temporarily flipped `ApplyLayoutRules` default to `RuleEngine.DefaultOrder`, ran the full suite, then reverted): all 36 are `FormatParityTests.Corpus_Matches_Golden` — human-blessed expected outputs that **move**. Sampled diffs are regressions, not improvements: `01-simple-select` collapses `SELECT`+columns onto one line the golden breaks; `07-in-list-short` collapses `FROM`/`WHERE` and injects double-spaces (`FROM   orders`); `05-case-searched` diverges on list layout.

**Conclusion**: the "built but not wired = cheap" thesis is **false for the formatter layout rules**. Enabling them as-is would regress ~6 % of the golden corpus plus nested-indent everywhere. **Do not enable.** The hook stays off-by-default; `RuleEngine.DefaultOrder` is documented as a not-yet-wired target.

**Strategic fork (needs a deliberate decision before P1 layout-fidelity proceeds):**
- **(A) Rework the rules' indent model** — change absolute `IndentLevel = n` writes to refine LayoutEngine's existing nested indent (read-existing + delta), systemically across Dml/Ddl and partially ControlFlow/Join. Delicate; requires the rules to know nesting depth they currently discard.
- **(B) Move layout into `LayoutEngine`/`LineBreakDecider`** (the Phase-B architectural position) — implement the rules' *intent* (AND/OR placement, CASE/CTE/DDL layout) where nested-indent context already lives. Bigger redesign, architecturally sound.
- **(C) Narrow scope** — enable only the non-indent-affecting behaviors (comma placement, spacing) and defer all indent-affecting layout. Salvages some parity cheaply; defers the hard part.

**Still genuinely cheap + unblocked (independent of the rule pipeline):** the format-action dispatch (FR-003/004 — wire action types 0–5 in `HandleFormatAction`; the `IFormatAction` classes are standalone) and the formatting UX items (error popup, current-query preview, style-editor buttons, active-style selector). These do **not** touch the broken rule-pipeline path and remain low-cost.

**Workflow-claimed but UNVERIFIED** (do not encode as fact until confirmed the way the Dml de-dent was): ControlFlowRules line-309 CASE-END-inside-BEGIN mis-pairing; line-1238 non-idempotent `IndentLevel += 1`; `Parenthesis.RemoveRedundant` peel-one-layer-per-pass. Plausible (code-grounded) but not empirically reproduced here.

---

## R2 — Dispatch the standalone format actions + format-time actions (P1)

**Decision**: Extend `FormatRequestHandler.HandleFormatAction`'s switch to map action types **0–5** (Apply Casing, Insert Semicolons, Remove Semicolons, Expand Wildcards, Qualify Object Names, Add/Remove Square Brackets) to the existing `IFormatAction` classes in `AkmlSql.Formatting/Actions/`. Separately, have `FormatterPipeline.Format` consult `profile.FormatActions` (the format-time action config) and run the enabled actions as part of Format SQL.

**Rationale**: The audit + a direct read of `HandleFormatAction` confirmed it dispatches only heavyweight ops (types 9–17) + Unformat and returns "not supported here" for 0–5; a grep proved the `IFormatAction` classes are **never instantiated**. The shell commands + VSCT already send these actions; only the engine dispatch and the pipeline's format-time-action consumption are missing.

**Alternatives considered**: (a) New action classes — rejected: the classes exist and are tested. (b) Run actions only standalone (not at format time) — rejected: SQL Prompt parity (and file 02 §4.14) requires both.

---

## R3 — Make `.casettings` + suppressions apply in the live editor (P3)

**Decision**: Add a document file path to the analysis request (`CodeAnalysisRequest.FilePath`), thread it from the shell's `AnalysisController` (it has the active document path), and in the engine `AnalysisHandler` resolve `Path.GetDirectoryName(filePath)` into `CaSettingsLoader.Load(dir, ...)` instead of passing `null`. Inline suppressions already work; the gap is the per-project settings directory.

**Rationale**: The audit found the live `AnalysisEngine.AnalyzeAsync` is handed a `null` directory and `CodeAnalysisRequest` carries no path, so per-rule enable/severity + global suppressions only take effect in the CLI (`BatchFileAnalyzer` passes the real dir). This is a thread-through, not new logic; the loader + upward-search already exist.

**Alternatives considered**: (a) Ship `.casettings` resolution in the shell — rejected: violates the engine-owns-analysis boundary. (b) Watch the filesystem from the engine without a path — rejected: the engine doesn't know the document's location without the request carrying it.

---

## R4 — Connect the QuickInfo / SignatureHelp shell stubs (P2)

**Decision**: Implement `QuickInfoSource` and `SignatureHelpSource` to send `RequestQuickInfo` (5) and `RequestSignatureHelp` (4) to the engine and render the responses, replacing the log-only stubs. The engine handlers already exist and return real data.

**Rationale**: The audit found both shell classes are skeletons that only log; the engine `QuickInfoHandler`/`SignatureHelpHandler` are implemented. Wiring restores an entire SQL-Prompt surface (hover tooltips, parameter help) with no engine work.

**Alternatives considered**: (a) Build new hover/sig providers — rejected: engine handlers + IPC already present. Note: two of these files are already locally modified on the working branch — reconcile with that in-progress work first.

---

## R5 — Temp-table (`#temp`) completion (P2)

**Decision**: Call the existing `TempTableTracker` from `CompletionEngine`'s pipeline — track `CREATE TABLE #t`/`SELECT … INTO #t` structures from the live document and emit their columns when a `#temp` alias is dereferenced. Document the known limit (later `ALTER TABLE` columns may not re-register).

**Rationale**: The audit found `TempTableTracker` + `AvailableTempTables` exist with unit tests but are never called from `CompletionEngine`. Wiring is the work.

**Alternatives considered**: Re-parse on every keystroke for temp structures — rejected: the tracker already captures structure at first parse; reuse it.

---

## R6 — Honor the suggestion settings (P2)

**Decision**: Read `IntelliSense.Enabled`, `AutoTrigger`, and `ColumnScope` in the completion trigger path (`CompletionController` / completion source) so toggling them changes behavior: `Enabled=false` suppresses the box, `AutoTrigger=false` makes suggestions on-demand only (`Ctrl+Space`), `ColumnScope=All` lists all columns after `SELECT`.

**Rationale**: The audit + a grep proved these flags are written/read only by the Options pages and never consulted by `CompletionController` — the toggles are currently cosmetic. Gating restores user trust (US2) and resolves the file 08 over-claims downgraded during the audit.

**Alternatives considered**: Remove the dead toggles instead of wiring them — rejected: SQL Prompt parity expects the controls to work.

---

## R7 — Fix desktop snippet expansion + built-in pack + SQL Prompt import (P2)

**Decision**: (a) Fix `CompletionController` snippet commit (case 4) so it passes the **shortcode** (not the full body) to `TryExpandSnippetAtPosition`, so the engine `SnippetExpand` lookup succeeds and the body inserts; honor `$CURSOR$`/selection on desktop. (b) Ship a real **built-in `.akmlsnippet` pack** (installer + engine BuiltIn folder). (c) Implement `.sqlpromptsnippet` XML → `.akmlsnippet` import in the snippet import handler, mapping tokens (`$DBNAME$`→`$DATABASE$`, `$PASTE$`→`$CLIPBOARD$`, etc.). (d) Add create-from-selection and surround-with commands that pass the selection as `$SELECTEDTEXT$`; preserve custom variables on Snippet-Manager save.

**Rationale**: The audit found desktop snippet insertion is broken (body passed as shortcode → lookup fails → nothing inserts), works only in the Web edition; import formats 1/2/3 are stubbed; the Snippet Manager wipes variables on save; the BuiltIn folder ships empty. These are the file 05 ❌/🟡 rows (including the 4 downgraded to ❌ on the desktop bar).

**Alternatives considered**: Change `SnippetProvider` to put the shortcode in `InsertText` — viable alternative integration point; tasks.md picks whichever keeps the popup display text correct while passing the shortcode to expansion. Reuse the formatting-style `SqlPromptImporter` for snippets — rejected: that importer is for styles, not snippets; a dedicated snippet converter is needed.

---

## R8 — Database-wide Smart Rename + Find Invalid Objects + inline refactors (P3)

**Decision**: Implement **database-wide Smart Rename** (per clarification) as a heavyweight refactor: resolve dependents via `sys.sql_expression_dependencies`, generate `sp_rename`/drop-recreate + per-dependent `ALTER` statements as a **reviewable script** the user approves before apply, delivered through the existing `RefactorPreview`/`RefactorApply` IPC with a new rename kind. Implement **Find Invalid Objects** (replace the stub) via a dependency/compile check, surfaced in a results list. Implement **Inline stored procedure**, **Inline EXEC**, **INSERT→UPDATE**, and **Script-as-ALTER** as refactor operations.

**Rationale**: The audit found the current `SafeRenameOperation` does cross-file text replace with no dependency safety; `FindInvalidObjectsHandlerStub` is a stub; inline-proc/EXEC and INSERT→UPDATE statement-level refactors don't exist. The clarification chose true (DB-wide) parity over script-local. This is the largest greenfield slice; isolate it behind preview/apply so it's always reviewable.

**Alternatives considered**: Script-local rename only (clarification rejected). A new IPC channel for rename — rejected: `RefactorPreview/Apply` already model preview-then-apply; extend with a kind. Find-invalid via parsing only — rejected: needs server-side dependency metadata for accuracy; run it engine-side with a SQL query.

---

## R9 — Tab coloring by database (P3)

**Decision**: Extend `EnvironmentMatcher` with a `database` (and `database-on-any-server`) match target alongside the existing `serverName`, and evaluate it in `TabColoringManager` using the database already available from `SsmsConnectionContextResolver`.

**Rationale**: The audit found the matcher only supports `serverName` (DB matching is marked "future"); the connection resolver already yields the database. Additive matcher change + rule shape.

**Alternatives considered**: Per-tab manual color menu — out of SQL Prompt's auto model and broader than the gap; defer.

---

## R10 — Version-preserving history retention + remove-older-than (P3)

**Decision**: Change `HistoryRetentionService` to trim **old versions** while keeping each query's latest version and all execution records (today it purges whole entries/cascades versions). Add a "remove older than selected" bulk action and an Options **disable-auto-trim** toggle. Align the default retention with the spec's intent.

**Rationale**: The audit found retention purges whole entries (losing executions) and there's no remove-older-than and no disable toggle in the UI (config-only). The history store already keeps a `history_versions` table, so version-level trim is a query change.

**Alternatives considered**: Keep entry-level purge — rejected: diverges from SQL Prompt's "keep latest, don't remove executions."

---

## R11 — Options coverage for config-only settings (P3)

**Decision**: Add controls to the existing Options pages for the settings that exist in `AppSettings` but have no UI (object/parameter tooltips, insertion keys, decrypt-encrypted, suggestion auto-trigger/scope once R6 makes them real), and add the missing alias-policy (include-AS, custom map, prefixes), special-characters (auto-close, add-parentheses), active-style selector, and suggestion connection-scope settings + per-page help. Where a backing `AppSettings` field is missing, add it (atomic writes).

**Rationale**: The audit (file 08) found multiple supported settings are config-file-only; this is the cross-cutting "every in-scope setting is adjustable from Options" requirement (FR-042, SC-007). Several of these only become meaningful after their feature is wired (R6, R7, R1), so sequence Options work after the feature it exposes.

**Alternatives considered**: A single new "advanced settings" page — rejected: parity expects settings on their semantic pages.

---

## R12 — Command-Palette object search + Bulk-Format launcher (P3)

**Decision**: Add a database-object provider to the Command Palette (it already fuzzy-matches commands; the engine has object-search capability) so typing an object name surfaces objects and selecting one navigates/inserts. Wire a `CmdBulkFormat` menu/palette command that opens the already-built `BulkFormatWizard`.

**Rationale**: The audit found the palette searches commands only (its row requires DB-object search) and the fully-built `BulkFormatWizard` has no caller. Both are connect-existing-capability.

**Alternatives considered**: Build a separate object-search window only — already exists (`ObjectSearchWindow`); the gap is surfacing it *in the palette*.

---

## Cross-cutting decisions

- **New IPC message types**: allocate from the reserved free ranges, mirroring spec 029's pattern (it took 93/193). Candidates: Find-Invalid-Objects request/result, list-analysis-rules request/result (for Manage-Rules), command-palette object-search request/result. Smart Rename + the inline refactors reuse `RefactorPreview/Apply` (30/31 → 130/131) with new kinds. See `contracts/ipc-and-commands.md`.
- **Performance gate (SC-011) — establish the baseline FIRST**: the 100 ms / 200 ms figures are PRD targets (`doc/use-cases.md`), **not measured current behavior**, so "no regression" is unfalsifiable until a measured baseline exists, and **no perf harness is known to exist**. The first task of any hot-path change (R1, R3, R6) is to build/standardize a micro-benchmark and record the *current* completion-latency and Format-SQL latency on a fixed corpus + machine as the baseline. Then gate the change on **both** no-regression-vs-baseline and the PRD targets. A regression blocks the corresponding phase.
- **Build & host parity (FR-047)**: every shell change lands in `AkmlSql.Shell.Shared` `.projitems` and is built per host with full MSBuild (SSMS 22 + VS 2026); no `dotnet build` for shell, no solution-level build.
- **Idempotency/validation gate (R1)**: the formatter rule pass is guarded by pipeline-level idempotency + semantic-equivalence tests; a rule that breaks either is disabled until fixed rather than shipped.
- **Deferred to /speckit.tasks**: SQL Prompt snippet/style **import version coverage** (v11 vs older) and accessibility/DPI audits — low-impact, sized at task time.
