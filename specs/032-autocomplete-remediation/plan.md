# Implementation Plan: Autocomplete Campaign Remediation (Web + Engine)

**Branch**: `030-closure-followups` *(spec ID `032-autocomplete-remediation` — kept on the current branch by user request)* | **Date**: 2026-07-17 | **Spec**: [spec.md](./spec.md)
**Input**: Feature specification from `/specs/032-autocomplete-remediation/spec.md` + campaign report [doc/web-autocomplete-campaign-2026-07-16.md](../../doc/web-autocomplete-campaign-2026-07-16.md)

> **GIT RULE (project-wide, overrides any sub-skill's "commit often"):** This repo forbids `git add/commit/push` without the user's explicit "yes" to "Ready to commit?". Any `Commit` checkpoint in downstream tasks is **summarize-and-ask**, not an automatic git action.

## Summary

Close the ~40 verified root causes behind the campaign's 334 autocomplete failures (75.6% → ≥95% target), the 4 web-editor trigger/keyboard breaks, the formatter idempotency oscillation, and the web connection-status dishonesty. The work is **repair, not greenfield**: every defect has a confirmed `file:line` mechanism (re-verified inline against the current tree — see [research.md](./research.md), including drift notes where the code moved since the report). Because the completion engine is shared, the engine-side fixes (clusters A–H) land for the desktop SSMS/VS edition automatically.

Approach by leverage (mirrors the report's fix-priority ladder):

1. **Two one-liners first** — `case TSqlTokenType.Exec:` (B1) and PartialText bracket-trim (G2) — the largest pass-rate wins per line of code.
2. **Web editor triggers/keys** (I1–I4 + C5) — one JS file; restores dot-trigger, DML-space trigger, Tab-accept, Ctrl+Enter. Immediate SSMS-parity feel.
3. **Scope-resolution rework** (A1–A6, F4) — `TokenBasedAliasExtractor` cursor-scope rewrite + `AliasResolver` UPDATE/DELETE/MERGE + ancestor-merge support + caret-position parse repair. Unlocks subqueries, aliased DML, and CTE bodies at once — the three worst families.
4. **INSERT target injection + parameter/variable completion** (C1–C4) — clause split with target injection (pattern proven by the ALTER TABLE path), new `ParameterProvider`, wire up the existing-but-dead `VariableTracker`/`VariableProvider`.
5. **Breadth fixes** — built-in functions per clause (D), keyword context sets (B2–B7), CTE fixes (E), temp-table names/tracking (F1–F3), bracket/quote handling (G1/G3/G4), ranking fidelity via additive `FilterText` (H).
6. **Formatter + trust** — JOIN-in-parens idempotency root fix + converge-and-validate Stage 7 (J1/J2), web built-in Khamis/Collapsed styles reusing the spec-031 `.akmlstyle` files (J3), three-valued connection pill + auto-restore + DB-dropdown fix (W).

Verification is corpus-driven: the campaign's 1,470-case corpus moves in-repo as an engine-level xunit gate; the browser keystroke pass and a full campaign re-run remain the acceptance gates (SC-001…SC-009).

## Technical Context

**Language/Version**: C# (LangVersion latest) — `AkmlSql.IntelliSense`, `AkmlSql.Formatting`, `AkmlSql.Engine` target **net10.0** (engine ships self-contained, single-file, win-x64, trimmed); `AkmlSql.Core` dual-targets **netstandard2.0 + net10.0** (wire DTOs — no records/init-only in message types, MessagePack `[Key(n)]` append-only); `AkmlSql.Web` is Blazor **WASM** (runs `AkmlSql.Formatting` + offline completion in-browser); editor glue is vanilla JS over a **vendored CodeMirror 6** bundle (`tools/codemirror` esbuild → `wwwroot/lib/codemirror`; no CDN).
**Primary Dependencies**: `Microsoft.SqlServer.TransactSql.ScriptDom` (`TSql170Parser` — dedicated token types are the clause-detection signal), MessagePack (IPC), CodeMirror 6 (`@codemirror/autocomplete`: `startCompletion`, `acceptCompletion`, `completionStatus`), Serilog.
**Storage**: no new stores. Web profiles in IndexedDB (`ProfileStore`); saved SQL connections in the existing web store (no SQL passwords at rest); engine schema cache in-memory per session.
**Testing**: xunit — `tests/AkmlSql.Engine.Tests` (`Completion/*`, `Parser/*` — per-cluster test classes already exist), `tests/AkmlSql.Formatting.Tests` (+ `tests/format-parity` goldens, 610+), `tests/AkmlSql.Web.Tests` / `AkmlSql.Web.E2E.Tests`; new in-repo completion corpus gate (campaign's 22 JSON files); `PerformanceBaselineTests` (~13 min, re-baseline via `AKML_UPDATE_BASELINE=1` on environmental drift only).
**Target Platform**: Web edition (Blazor WASM + IIS, WebSocket bridge to the engine service) **and** desktop SSMS 22 / VS 2026 (same engine over named pipe). Engine-side fixes must not assume either host.
**Project Type**: out-of-process .NET 10 engine + two front ends (web WASM, desktop shell). This feature touches **no desktop shell code** — desktop benefits ride entirely on the shared engine.
**Performance Goals**: completion keystroke path p95 < 100 ms (scope-resolution rework adds two-pass extraction + ancestor merges — gate with `PerformanceBaselineTests`); format < 200 ms typical; dot-trigger popup latency in web ≤ explicit-invoke latency today.
**Constraints**: IPC wire format preserved (only additive `CompletionItem.FilterText [Key(7)]`; current keys end at 6); 50-item suggestion cap unchanged (at-cap failures addressed via scoping/ranking only); 5-level fuzzy matcher semantics unchanged (fuzzy-by-design corpus cases stay excluded); engine stays trimmable; engine redeploy = full publish copy (never partial DLL swap); web deploy must include `dotnet publish` of AkmlSql.Web (build-script gap fixed previously — verify it publishes); completion item kinds must avoid `Snippet` ObjectType for non-snippet items (SSMS hides/expands them).
**Scale/Scope**: 33 FRs across 10 fix clusters; 16 engine/web source files carry the confirmed mechanisms; acceptance = 1,370-case battery ≥ 95%, keystroke pass 100%, formatting 100/100 idempotent, desktop suites green.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

No `.specify/memory/constitution.md` exists. The de-facto project principles in `CLAUDE.md` are used as gates (same convention as spec 030):

| Gate (from CLAUDE.md) | Assessment |
|---|---|
| **Out-of-process boundary** — completion/format logic engine-side; front ends orchestrate | ✅ All clause/scope/provider/formatter fixes are engine-library code; web changes are editor glue (JS triggers/keys), profile-store wiring, and status UI. No logic duplicated into a front end. |
| **IPC wire compatibility** — codes unchanged, additive fields only | ✅ Zero new message types. One additive field: `CompletionItem.FilterText` at `[Key(7)]` (verified free). Round-trip + old-peer tests required. |
| **Shared `.projitems` / per-host MSBuild** | ✅ N/A — no shell-shared sources touched. Desktop impact arrives via the engine publish only. |
| **TDD for engine/Core logic** | ✅ Every cluster has an existing xunit test class to extend; plan mandates failing-test-first with the campaign repro SQL. |
| **Async/IPC conventions** (`async Task<RpcMessage?>`, no `.GetAwaiter().GetResult()`) | ✅ No handler signatures change; providers stay synchronous enumerators as today. |
| **Formatting Stage-6/Stage-7 invariants** | ✅ J1 fixes an idempotency violation; J2 strengthens Stage 7 (converged output must still pass Stage-6 re-validation). Full golden suite is the regression oracle — goldens are drift-guards, never regenerated to make a fix pass. |
| **Performance non-regression** | ✅ `PerformanceBaselineTests` gates the scope-resolution rework (the only hot-path structural change). |
| **WPF theme tokens / atomic config writes** | ✅ N/A — no WPF, no config-schema changes (web status pill uses existing web token CSS). |

**Result: PASS** — no violations; Complexity Tracking left empty.

*Post-Phase-1 re-check (2026-07-17)*: design artifacts introduce no new projects, no new IPC codes, no shell edits — gates unchanged, still PASS.

## Project Structure

### Documentation (this feature)

```text
specs/032-autocomplete-remediation/
├── plan.md              # This file
├── spec.md              # Feature spec
├── research.md          # Phase 0 — per-cluster decisions + inline re-verification + drift notes
├── data-model.md        # Phase 1 — context/scope/item model deltas
├── quickstart.md        # Phase 1 — build/test/deploy/verify workflow
├── contracts/
│   └── completion-and-editor.md   # Phase 1 — wire delta, behavior matrix, editor key/trigger contract
├── checklists/
│   └── requirements.md  # Spec quality checklist (from /speckit.specify)
└── tasks.md             # Phase 2 — created by /speckit.tasks (NOT here)
```

### Source Code (repository root — files this feature touches)

```text
src/
├── AkmlSql.IntelliSense/
│   ├── Parser/
│   │   ├── TokenBasedAliasExtractor.cs   # A1/A2/A5/A6/F4 — cursor-scope rewrite, two-pass, set-op bounds, multi-part names
│   │   ├── SuffixCompletionHelper.cs     # A1 — RepairAtCursor; H4 — " OR" boundary fix
│   │   ├── AliasResolver.cs              # A3/A4 — Update/Delete/Merge scopes, ancestor merge, derived-table projections
│   │   ├── CursorContextAnalyzer.cs      # B1–B7 dedicated-token cases; C1/C2 INSERT split+injection; C4 @PartialText; G2/G3 delimiters; A6 DotPrefix chain
│   │   ├── CteResolver.cs                # E3 statement scoping; E4 sources fallback; E5 recursive self-ref
│   │   ├── TokenBasedCteExtractor.cs     # E6 — capture explicit column lists
│   │   ├── TempTableTracker.cs           # F2 last-batch gate; F3 star-into source capture
│   │   └── VariableTracker.cs            # C4 — gains its first caller (Analyze)
│   └── Completion/
│       ├── CompletionEngine.cs           # G1 caret-local neutralization; H1 FilterText scoring; ParameterProvider registration
│       ├── Providers/
│       │   ├── ObjectProvider.cs         # F1 temp names; C2 InsertTarget filtering; B7/H3 TOP-paren + APPLY suppression fixes
│       │   ├── ColumnProvider.cs         # E1 CTE-alias branch; C1 InsertColumnList; H1 FilterText; H2 IDENTITY/computed SET filter
│       │   ├── JoinProvider.cs           # G4 — respect typed schema qualifier
│       │   ├── VariableProvider.cs       # C4 — now reachable (no code change expected)
│       │   └── ParameterProvider.cs      # C3 — NEW: @param items from cached proc parameters
│       └── Dictionaries/KeywordDictionary.cs  # B2–B6 new sets (AfterDelete, JoinQualifier, SetOperator, Case*, OrderKeyword/GroupKeyword); C2 INTO; D AfterInsertValues + function surfacing
├── AkmlSql.Core/Ipc/Messages/CompletionResponse.cs  # H1 — CompletionItem.FilterText [Key(7)] (additive)
├── AkmlSql.Formatting/
│   ├── Layout/LineBreakDecider.cs        # J1 — paren-aware ClauseTracker/JOIN-modifier state
│   └── Pipeline/FormatterPipeline.cs     # J2 — Stage 7 returns converged+revalidated second pass
└── AkmlSql.Web/
    ├── wwwroot/js/akml-editor.js         # I1 dot-trigger; I2 DML keywords; I3 Tab-accept; I4 Mod-Enter order; C5 span regex
    ├── Services/IProfileStore.cs         # J3 — built-in Khamis/Collapsed from Formatting/Profiles/BuiltIn/*.akmlstyle
    ├── Services/ISqlConnectionService.cs (+ impl) # W — auto-restore on boot; three-valued state exposure
    └── Shared/{StatusBar,ConnectionManagerModal,ConnectionPickerComponent}.razor  # W — pill truth, DB dropdown seed, filtered-list hint

tests/
├── AkmlSql.Engine.Tests/
│   ├── Parser/{TokenBasedAliasExtractor,CursorContextAnalyzer,CteResolver,TempTableTracker,SuffixCompletionHelper,VariableTracker,AliasResolver}Tests.cs
│   ├── Completion/{CompletionEngine,ColumnProvider,TempTableCompletion,VariableProvider,KeywordDictionary,…}Tests.cs (+ ParameterProviderTests — NEW)
│   ├── Completion/CorpusGateTests.cs     # NEW — corpus-driven battery gate (fake cache, 22 corpus files)
│   └── PerformanceBaselineTests.cs       # perf gate for the scope rework
├── AkmlSql.Formatting.Tests/             # J1 property test (double-format byte-equal) + full goldens
├── AkmlSql.Web.Tests/                    # J3 ProfileStore built-ins; W state logic (bUnit where present)
├── AkmlSql.Web.E2E.Tests/                # I1–I4 keystroke checks where feasible
└── completion-corpus/                    # NEW — campaign corpus (22 JSON files) checked in with exclusion markers
```

**Structure Decision**: No new projects; one new provider class and one new corpus directory. All completion logic changes live in `AkmlSql.IntelliSense` (engine-side, shared by both editions); the only wire change is one additive DTO field in `AkmlSql.Core`; web changes are confined to the editor JS, the profile store, and the connection-status components. Delivery order follows the leverage ladder in the Summary (one-liners → web keys → scope rework → INSERT/params → breadth → formatter/trust), which also front-loads the biggest SC-001 movers.

## Complexity Tracking

> No Constitution Check violations — section intentionally empty.
