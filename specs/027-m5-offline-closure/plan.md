# Implementation Plan: M5 — Offline Parity Closure (Snippets, Refactoring, Suppression Editing)

**Branch**: `027-m5-offline-closure` | **Date**: 2026-05-31 | **Spec**: [spec.md](./spec.md)
**Input**: Feature specification from `/specs/027-m5-offline-closure/spec.md`

## Summary

Close the genuinely-unmet M5 work. Most of M5's offline *substrate* already shipped under spec 021 Phase 6 (T100–T120) — `AkmlSql.IntelliSense` runs ScriptDom under WASM, the IndexedDB schema cache + LRU eviction + CHECKSUM_AGG drift sync + offline completion/quick-info/signature all work. What is missing is the **user-facing feature surfaces** Phase 6 stubbed at the service layer: snippet expansion/surround/management/import-export, the ten lightweight refactorings + menu/preview, the three heavyweight refactoring UIs, inline suppression editing, and a cache-aware status badge — plus the deferred offline E2E (T113) and a visual-parity audit. Six user stories, priority order.

Two scope reconciliations were made during planning (both user-confirmed), because the spec as first written collided with the actual engine:

- **Heavyweight refactoring stays bridge-only** (live engine + `refactoring.heavy`); the "run from cached schema" path (original FR-015) is **descoped** to a named follow-up. The cache stores flat `SchemaPhasePayload` bytes with no reverse-rehydrator to a `DatabaseCache`, and the online preview/apply path has zero test coverage today — verifying the online path is higher-value than building an offline path on an unverified one. (research.md Decision 3.)
- **Suppression delivers line (cross-surface `-- noqa: RULEID`) + global (browser-local override + a bugfix)**; file-scope-per-rule is **descoped** — no such directive exists in the shared format and adding one would touch the analyzer parser + engine tests + WPF. (research.md Decision 4.)

The one structural move is the **T101-pattern relocation** of the ten lightweight refactoring operations + `ILightweightOperation` + `RefactoringContext` from `AkmlSql.Engine` into `AkmlSql.IntelliSense` (namespaces unchanged, engine consumes transitively, engine tests are the regression gate). Everything else is new browser UI (`AkmlSql.Web`), one analyser bugfix, test code, and two docs.

## Technical Context

**Language/Version**: C# 12 / .NET 10 (`net10.0`) for `AkmlSql.IntelliSense`, `AkmlSql.Engine`, and all test projects; Blazor WebAssembly (`net10.0`) for `AkmlSql.Web`; JavaScript (ES modules) for the CodeMirror 6 editor layer (`wwwroot/js/akml-editor.js`).
**Primary Dependencies**: `Microsoft.SqlServer.TransactSql.ScriptDom` (already in `AkmlSql.IntelliSense`; drives the relocated lightweight ops in WASM); CodeMirror 6 `@codemirror/autocomplete` `snippet()` (already loaded by `akml-editor.js`) for tab-stop expansion; `MessagePack` (already integrated, for the heavyweight bridge path); xUnit + bUnit + Playwright .NET (already integrated). **No new package references; no new IPC message types** (lightweight ops run locally; heavyweight reuses `RequestRefactorPreview`(30)/`RequestRefactorApply`(31); suppression is text + IndexedDB).
**Storage**: No new IndexedDB store names. Snippets → existing `snippets` store; analyser overrides → existing `AnalysisSettings` store; schema → existing `schemaEntries`. Built-in snippets ship as an embedded resource in the WASM bundle. The parity audit is checked-in markdown.
**Testing**: `dotnet test` (xUnit + bUnit) for the snippet, lightweight-parity, suppression, status-indicator, and heavyweight-gating unit/component tests; `dotnet test --filter Category=BridgeE2E` for the offline-IntelliSense + heavyweight-online E2E (reuses the spec-025 `EngineLaunchFixture`). The **engine refactoring suite is the relocation regression gate** (FR-013 / SC-004).
**Target Platform**: Chromium (Playwright default) for the browser E2E; Windows 11 + .NET 10 SDK for engine + tests; the web bundle runs in any modern browser (WASM).
**Project Type**: Feature-build closure over an already-merged Blazor WASM + shared-library stack. One cross-project relocation (Engine → IntelliSense); the rest is additive in `AkmlSql.Web`.
**Performance Goals**: Offline cached completion responds in < 50 ms (PRD success metric, already structurally met by the shipped cache path; US5 makes it legible). Lightweight refactor apply is interactive (< 200 ms on a typical statement; the parser pass dominates). No new perf regression budget beyond the existing 10 MB document ceiling.
**Constraints**:

- The lightweight relocation MUST NOT regress the engine — engine refactoring tests stay green with zero call-site edits (FR-013 / SC-004); the T101 stable-namespace pattern guarantees this.
- The relocated code MUST stay free of `System.IO` / SqlClient / native deps so it loads under WASM (FR-013); the browser always supplies `RefactoringContext.IntelliSense` so `ConfigManager.Load()` is never reached.
- Snippet expansion + lightweight refactoring MUST work with no engine paired (offline, FR-002/FR-008).
- Suppressions the browser writes at line scope MUST be the exact `-- noqa: RULEID` form the shared `SuppressionParser` honours (FR-019/FR-022).
- The status indicator MUST NOT flicker Cached↔Live during reconnect (FR-024).
- E2E suites MUST build from current source and be `[Trait("Category","BridgeE2E")]`-gated out of the default run (FR-025/FR-026).

**Scale/Scope**: Six user stories; 27 functional requirements (two reconciled). Deltas: relocate ~12 files (no logic change); ~1 model field (`SurroundsWith`); ~2 JS functions; ~3 new Razor surfaces (snippet management page, refactoring menu/preview, suppression actions on the problems list) + `StatusBar` extension; 1 analyser bugfix; built-in snippet resource set; ~6 new test classes + 1 E2E test class on the existing fixture; 1 parity-audit doc; quickstart/progress doc updates.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

No `.specify/memory/constitution.md` exists in this repository, so no formal constitution gates apply. The closure adopts the same three self-imposed gates the spec-025 closure used, which hold here:

- **No new IPC message types.** Lightweight refactorings run locally (no wire call); heavyweight reuses the already-shipped `RequestRefactorPreview`/`RequestRefactorApply`; suppression is inline text + IndexedDB; snippets reuse the already-shipped snippet messages for best-effort save/delete. No envelope is added.
- **No new test framework.** Existing xUnit + bUnit + Playwright .NET stacks are extended.
- **Shared logic stays in the shared library; the engine keeps consuming it.** The relocation follows T101 exactly — engine call sites are unchanged because namespaces are preserved; the engine references `AkmlSql.IntelliSense` and runs the identical code, so engine behaviour cannot diverge from the browser's.

A fourth, scope-specific gate: **the two reconciliations only ever narrow scope, never silently** — both are recorded in research.md with rationale, reflected in the revised FRs below, and listed as named follow-ups in the spec's Out of Scope.

These are re-checked in the Post-Design re-evaluation.

## Project Structure

### Documentation (this feature)

```text
specs/027-m5-offline-closure/
├── plan.md                              # This file
├── spec.md                              # /speckit.specify output (FRs reconciled per Decisions 3 & 4)
├── research.md                          # 6 decisions + 2 reconciliations
├── data-model.md                        # 9 conceptual/extended entities
├── quickstart.md                        # per-user-story build walkthrough
├── contracts/
│   ├── snippet-expansion-contract.md    # US1
│   ├── refactoring-contract.md          # US2 (lightweight relocation) + US3 (heavyweight bridge-only)
│   ├── suppression-contract.md          # US4 (line cross-surface + global browser-local + bugfix)
│   ├── status-indicator-contract.md     # US5
│   └── e2e-and-parity-contract.md       # US6
├── checklists/
│   └── requirements.md                  # /speckit.specify output; all green
├── M5-PARITY-AUDIT.md                   # US6 output (created during implementation)
└── tasks.md                             # /speckit.tasks output (NOT created here)
```

### Source Code (repository root)

```text
src/
├── AkmlSql.Engine/
│   └── Refactoring/                      # ← lightweight ops + ILightweightOperation + RefactoringContext RELOCATED OUT (US2/FR-013)
│       └── Operations/Heavyweight/       #    heavyweight ops + ReferenceCollector STAY (US3 bridge-only)
│
├── AkmlSql.IntelliSense/
│   └── Refactoring/                      # ← NEW folder; relocated lightweight ops (namespaces unchanged) (US2)
│       ├── ILightweightOperation.cs
│       ├── RefactoringContext.cs
│       └── Operations/Lightweight/*.cs   #    the ten ops
│
└── AkmlSql.Web/
    ├── Services/
    │   ├── ISnippetStore.cs              # ← +SurroundsWith on WebSnippetMetadata (US1)
    │   ├── IRefactoringService.cs        # ← +PreviewLightweightAsync/ApplyLightweightAsync (US2); heavyweight UI uses existing path (US3)
    │   └── IAnalyserService.cs           # ← inject IAnalysisSettingsStore; honour RuleOverrides (US4 bugfix / FR-021)
    ├── Pages/
    │   ├── Snippets.razor                # ← NEW; snippet management + import/export (US1)
    │   └── Editor.razor                  # ← refactoring menu + surround chord + suppression actions wired in
    ├── Shared/
    │   ├── ProblemsListComponent.razor   # ← suppression actions on each finding (US4)
    │   ├── StatusBar.razor               # ← cache-aware 4-state indicator (US5)
    │   └── RefactorPreview*.razor        # ← NEW; lightweight + heavyweight preview surface (US2/US3)
    └── wwwroot/
        ├── js/akml-editor.js             # ← +expandSnippet/+surroundSelection (US1)
        └── snippets/                     # ← NEW; embedded built-in snippet set (US1)

tests/
├── AkmlSql.Engine.Tests/                 # refactoring suite = relocation regression gate (US2/SC-004) — run, not rewritten
├── AkmlSql.IntelliSense.Tests/           # ← optional: assert lightweight ops reachable from the shared lib
├── AkmlSql.Web.Tests/
│   ├── Snippets/                         # ← expansion, surround, CRUD, import/export round-trip (US1)
│   ├── Refactoring/
│   │   ├── LightweightParityTests.cs     # ← NEW; browser==engine output (US2/FR-009)
│   │   └── RefactoringServiceTests.cs    # existing gating tests retained (US3)
│   ├── Analysis/SuppressionEditTests.cs  # ← NEW; line directive + global override+bugfix (US4)
│   └── Bridge/StatusIndicatorTests.cs    # ← NEW; 4-state matrix + no-flicker (US5)
└── AkmlSql.Web.E2E.Tests/
    └── UserStory4Tests.cs                # ← NEW; offline IntelliSense + heavyweight online, on spec-025 EngineLaunchFixture (US6)

doc/WEB/quickstart-m5.md                  # ← update: remove the now-closed "What is NOT in M5" caveats
doc/progress.md                           # ← spec-027 closure summary
```

**Structure Decision**: Feature build over the merged Phase-6 stack. Exactly one cross-project relocation (lightweight refactoring ops Engine→IntelliSense, T101 pattern, engine tests gate it). All other new code is additive in `AkmlSql.Web` (3 new Razor surfaces + StatusBar extension + 2 JS functions + 1 model field + 1 analyser bugfix + built-in snippet resources), plus test classes and two docs. No new csproj, no new IPC message type, no new IndexedDB store, no new persistence layer.

## Phase 0: Research

Six decisions (one per user story) + two reconciliations, in `research.md`:

1. Snippet expansion + surround-with run in the browser via CodeMirror `snippet()`; built-in set defined fresh (no canonical engine files exist).
2. Lightweight refactorings relocate into `AkmlSql.IntelliSense` (T101 pattern) and run in-browser → structural parity, no engine regression.
3. **Reconciliation**: heavyweight stays bridge-only; cached-schema execution descoped (no rehydrator, untested online path).
4. **Reconciliation**: suppression = line (cross-surface `-- noqa: RULEID`) + global (browser-local + `AnalyserService` bugfix); file-scope dropped.
5. Cache-aware status indicator derives a four-state from bridge state + cache presence.
6. E2E + parity reuse the spec-024/025 harnesses (`EngineLaunchFixture`, `BridgeE2E` trait, `M2-THEME-PARITY-AUDIT.md` shape).

Each verified against current source (see research.md "Verified against current source").

## Phase 1: Design & Contracts

- **data-model.md**: 9 entities — `WebSnippet` (extended +`SurroundsWith`), built-in snippet set, relocated `LightweightRefactorOperation` + `RefactoringContext`, `RefactorPreview`, `SuppressionEdit` (2 scopes), the `AnalyserService` settings-wiring bugfix, the derived `IntelliSenseAvailabilityState`, and the parity-audit doc.
- **contracts/**: five contracts (snippet, refactoring, suppression, status-indicator, e2e+parity), each binding its FRs to verified current-source facts and a test contract.
- **Agent context**: run `.specify/scripts/powershell/update-agent-context.ps1 -AgentType claude` to record the new surfaces (browser snippet expansion, relocated lightweight refactoring in `AkmlSql.IntelliSense`, cache-aware status indicator, suppression editing).

## Phase 2 planning note

`/speckit.tasks` generates `tasks.md`. Expected shape, priority order: **US2 relocation first task = move the ten ops + run the engine refactoring suite green (the regression gate) before any browser wiring**; then US1 (model field → built-ins → JS expansion → completion wiring → surround chord → management page → import/export → tests); US2 browser path (service methods → menu → preview → parity test); US3 (heavy menu entries gated by `CapabilityNotice` → rename/extract dialogs → preview → apply → fold online E2E into US6); US4 (the `AnalyserService` bugfix FIRST, then line suppression, then global); US5 (StatusBar cache probe + 4-state + no-flicker test); US6 (offline E2E on the existing fixture + parity audit doc). Each story is independently demoable.

## Complexity Tracking

No constitution gate violations (no constitution). The self-imposed gates hold post-design:

- **No new IPC message types** — lightweight runs locally; heavyweight reuses messages 30/31; suppression + snippets reuse existing paths.
- **No new test framework** — xUnit/bUnit/Playwright extended.
- **Relocation can't diverge the engine** — T101 stable-namespace move; engine refactoring suite is the gate.
- **Reconciliations narrow scope explicitly** — Decisions 3 & 4 are recorded, reflected in the revised FRs, and listed as named follow-ups; nothing is silently dropped.

The single non-trivial risk is the relocation (FR-013). It is mitigated by precedent (T101 moved 32 files the same way), by the WASM-safety invariant (verified: ScriptDom + already-relocated schema models + the `IntelliSense` escape hatch), and by making "engine refactoring tests green" the first task's exit criterion.
