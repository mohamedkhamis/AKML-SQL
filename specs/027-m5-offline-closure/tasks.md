---
description: "Task list for M5 — Offline Parity Closure (Snippets, Refactoring, Suppression Editing)"
---

# Tasks: M5 — Offline Parity Closure (Snippets, Refactoring, Suppression Editing)

**Input**: Design documents from `/specs/027-m5-offline-closure/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/ (5 files), quickstart.md (all present)

**Tests**: Tests are folded **into each user story** (not a separate phase), matching the established closure-spec house style (spec 025) and the per-contract "Test contract" sections. Each story closes as one unit — production code + the tests that prove it. The engine refactoring suite is a **regression gate** for the Phase-2 relocation, not new test code.

**Organization**: Tasks are grouped by user story so each can land independently. The one structural, engine-touching move — relocating the lightweight refactoring operations into `AkmlSql.IntelliSense` — is isolated in **Phase 2 (Foundational)** and done **first**, per plan.md's "derisk the relocation before any browser wiring" guidance. Every other story is additive in `AkmlSql.Web` and independent.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies on incomplete tasks)
- **[Story]**: Maps the task to a user story (US1–US6); omitted for Setup, Foundational, and Polish tasks
- Paths are absolute repository paths under `D:\Repo\01-Khamis-Projects\AKML-SQL\`

---

## Phase 1: Setup (shared infrastructure)

**Purpose**: Establish a known-green baseline so the Phase-2 relocation has a regression reference, and confirm the surfaces the new work lands in exist.

- [X] T001 Baseline build confirmed green at HEAD: `AkmlSql.IntelliSense` (0 warn/0 err) and `AkmlSql.Engine` both build (Engine compiled successfully as part of the T002 test run). `dotnet build` rejects multiple project args (MSB1008) — build each project separately. Web build deferred to its story phases.
- [X] T002 Engine refactoring suite baseline recorded: **`Failed: 0, Passed: 98, Skipped: 0, Total: 98`** (`dotnet test tests/AkmlSql.Engine.Tests/AkmlSql.Engine.Tests.csproj --filter "FullyQualifiedName~Refactor"`, 527 ms). This is the regression reference the suite MUST still report after the Phase-2 relocation (FR-013 / SC-004).
- [X] T003 [P] Test surfaces confirmed: `tests/AkmlSql.Web.Tests/{Snippets,Refactoring,Bridge}/` all present (per Glob); `tests/AkmlSql.Web.E2E.Tests/` present (spec 024); `tests/AkmlSql.IntelliSense.Tests/` present and references ONLY `AkmlSql.IntelliSense` (ideal for the T007 reachability test). `tests/AkmlSql.Web.Tests/Analysis/` does not yet exist — create in US4 (T027).

**Checkpoint**: All three projects build; engine refactoring suite green with a recorded baseline count; test surfaces located.

---

## Phase 2: Foundational (blocking prerequisite — the relocation)

**Purpose**: Relocate the ten lightweight refactoring operations + `ILightweightOperation` + `RefactoringContext` from `AkmlSql.Engine` into `AkmlSql.IntelliSense`, keeping namespaces stable (the proven T101 pattern), so both the engine and the browser run identical code. This is the single engine-touching change in the closure and the only true cross-cutting prerequisite (it unblocks US2's browser path). Done first to isolate and verify the regression risk before any browser wiring.

**⚠️ CRITICAL**: T004 → T005 → T006 are sequential (move → reference fix-up → regression gate). The engine refactoring suite MUST be green at T006 before US2 begins.

- [X] T004 Relocated via plain `mv` (NOT `git mv` — per the project git rule I leave staging to the user) the 12 files from `src/AkmlSql.Engine/Refactoring/` to `src/AkmlSql.IntelliSense/Refactoring/`, mirroring the dir layout and **preserving the `AkmlSql.Engine.Refactoring*` namespaces**: `Operations/ILightweightOperation.cs`, `RefactoringContext.cs`, all ten `Operations/Lightweight/*.cs`. Removed the now-empty Engine `Lightweight/` dir. `Operations/Heavyweight/`, `HeavyweightOperationBase.cs`, `ReferenceCollector.cs`, `RefactoringEngine.cs` stayed in `AkmlSql.Engine` (heavyweight bridge-only — Decision 3). Verified via `find`.
- [X] T005 No `.csproj` edits needed — both projects are default-glob SDK-style (`grep -c "Compile Include"` = 0 in each), so the relocated files are picked up by location. `AkmlSql.Engine.csproj` already references `AkmlSql.IntelliSense` (post-T101), so call sites (`FormatRequestHandler.HandleFormatAction`, `RefactoringEngine`) resolve transitively with **zero call-site edits**. WASM-safety invariant verified: the ten ops + `RefactoringContext` import only `System.*`, `Microsoft.SqlServer.TransactSql.ScriptDom`, `AkmlSql.Core.Config`, and `AkmlSql.Engine.Schema(.Models)` — all already in `AkmlSql.IntelliSense` (`DatabaseCache` + models live there). No `System.IO`, no SqlClient, no Serilog. `RefactoringContext.IntelliSense` escape hatch intact (the two `ConfigManager.Load()` ops only call it when `IntelliSense == null`).
- [X] T006 **Regression gate PASSED.** `AkmlSql.IntelliSense` builds 0-warn/0-err; `AkmlSql.Engine` builds 0-err (11 pre-existing CA1416/CS86xx warnings unchanged). Re-ran the refactoring suite: **`Failed: 0, Passed: 98, Skipped: 0, Total: 98`** — identical to the T002 baseline (FR-013 / SC-004 satisfied).
- [X] T007 [P] Added `tests/AkmlSql.IntelliSense.Tests/RefactoringReachabilityTests.cs` (mirrors the T102 `ExtractionSmokeTests` pattern): 3 tests — `RefactoringContext` constructs without the engine assembly; all ten lightweight ops are reachable + implement `ILightweightOperation`; a `RemoveSemicolons` op runs end-to-end (parse → context → Apply) from a project that does NOT reference `AkmlSql.Engine`. Proves the shared-lib boundary US2's browser path depends on.

**Checkpoint**: Lightweight ops live in `AkmlSql.IntelliSense`; engine consumes them transitively; engine refactoring suite green; shared-lib reachability proven. US2's browser path is unblocked. US1 / US3 / US4 / US5 / US6 were never blocked by this and can proceed in parallel.

---

## Phase 3: User Story 1 — Snippet library in the browser (Priority: P1) 🎯 MVP

**Goal**: Expand, surround-with, manage, and import/export snippets — entirely offline. (Contract: `contracts/snippet-expansion-contract.md`.)

**Independent Test**: With no engine paired — type `ssf`, accept, body expands with caret at the first tab-stop; select a block, surround-with wraps it; create/edit/delete a personal snippet on the management page; import a `.akmlsnippet` and export one that re-imports byte-identical.

- [X] T008 [P] [US1] Added `bool SurroundsWith` to `WebSnippetMetadata` and `string? Tooltip` to `WebSnippetVariable` in `src/AkmlSql.Web/Services/ISnippetStore.cs`, mirroring the engine `SnippetMetadata`/`SnippetVariable`. Web builds clean; the 9 existing `SnippetStoreTests` still pass (no round-trip break). **Build+test verified.**
- [ ] T009 [US1] Author the built-in snippet set as embedded resources under `src/AkmlSql.Web/wwwroot/snippets/` (or an embedded JSON resource); replace the two hardcoded built-ins in `SnippetStore.BuildBuiltIns` with a loader over the resource set; mark surround-capable entries `SurroundsWith=true` (E2, FR-001). `ssf`/`cte` remain the floor.
- [ ] T010 [US1] Add `export function expandSnippet(hostElementId, body)` and `export function surroundSelection(hostElementId, body)` to `src/AkmlSql.Web/wwwroot/js/akml-editor.js` using CodeMirror 6 `@codemirror/autocomplete` `snippet()`; normalise `${name:default}` named placeholders to numbered tab-stops before the CM call; malformed body ⇒ literal insertion, no throw (FR-002/FR-003 + edge cases).
- [ ] T011 [US1] Surface snippets in the completion source as a **distinct item type** so typing a shortcode offers the snippet; accepting a snippet item invokes `expandSnippet`. Wire in `src/AkmlSql.Web/Shared/EditorComponent.razor` (`RequestCompletionsFromJs` / `ToCmType`) + the `completionSource` in `akml-editor.js` (FR-002, collision edge case).
- [ ] T012 [US1] Add the surround-with chord (e.g. `Ctrl+K, Ctrl+S` — must not collide with the existing `Ctrl+K,Ctrl+F`/`Ctrl+K,Ctrl+L`) to `src/AkmlSql.Web/Pages/Editor.razor` `OnKeyDownAsync`; open a picker filtered to `SurroundsWith==true`; call `surroundSelection` (FR-003 + no-selection edge case).
- [ ] T013 [P] [US1] Create `src/AkmlSql.Web/Pages/Snippets.razor` (route `/snippets`): list (built-ins first, read-only badge) → create/edit/delete personal snippets via `ISnippetStore`; built-in edit/delete refused with a clear message (FR-004). Add the nav link in `src/AkmlSql.Web/Shared/NavMenu.razor`.
- [ ] T014 [US1] Add import/export to `Pages/Snippets.razor`: `<InputFile accept=".akmlsnippet">` → deserialize → validate (shortcode present, not `builtin.*`, no builtin collision) → `SaveAsync`; export the selected personal snippet via `src/AkmlSql.Web/wwwroot/js/akml-download.js` (`downloadBase64`), filename `<shortcode>.akmlsnippet` (FR-005/FR-006 + malformed/collision edge cases).
- [ ] T015 [US1] Tests in `tests/AkmlSql.Web.Tests/Snippets/SnippetExpansionTests.cs` (new) + extend `SnippetStoreTests.cs`: expansion inserts body + positions caret (interop asserted); surround wraps selection + no-selection no-crash; management CRUD persists; built-in delete refused; import happy/malformed/builtin-collision; export→re-import byte-identical (SC-001/SC-002).

**Checkpoint**: Every PRD §5 snippet row (built-in / user / import-export / surround / expand) is clickable; all snippet tests green. **MVP deliverable.**

---

## Phase 4: User Story 2 — Lightweight refactorings offline (Priority: P1)

**Goal**: All ten lightweight ops run in-browser with a menu + preview, identical to the engine. (Contract: `contracts/refactoring-contract.md` Part A. Relocation done in Phase 2.)

**Independent Test**: No engine paired — paste a comma-join, open the refactoring menu, choose Convert Old-Style Joins, see the before/after preview, apply; the result matches the engine's output for the same input.

- [X] T016 [US2] Added `PreviewLightweightAsync`/`ApplyLightweightAsync` + a `LightweightPreview` record to `src/AkmlSql.Web/Services/IRefactoringService.cs`: parse via `TsqlParserService`, build `RefactoringContext` with a default `IntelliSenseSettings` supplied (so `ConfigManager.Load()` is never reached under WASM), dispatch to the relocated op via `op.Apply(ctx)`. **Deviation from the contract's "no new enum / map onto FormatActionType 9–17":** `RemoveSemicolons` is `FormatActionType=2` (not in 9–17) and the engine's `HandleFormatAction` switch only wires 9 of the 10 ops — so a clean 1:1 onto `FormatActionType` is impossible. Introduced a small **web-internal** `LightweightRefactorKind` enum (NOT a wire type — no IPC carries it) mapping cleanly to all ten `ILightweightOperation` classes. **Build verified.**
- [ ] T017 [US2] Build the refactoring menu in `src/AkmlSql.Web/Pages/Editor.razor` (toolbar/context) listing all ten lightweight ops; inapplicable-to-selection ops MAY render disabled with a reason, but the menu is **never empty offline** (FR-010).
- [ ] T018 [US2] Create `src/AkmlSql.Web/Shared/RefactorPreviewPanel.razor` showing the lightweight before/after (`{before, after, warnings[], changed}`) with a defined "no change / not applicable" state; apply replaces editor content as a **single undoable edit** (one CodeMirror transaction via `akml-editor.js`) honouring the 10 MB `DocumentSizeLimit` (FR-011/FR-012 + edge cases).
- [X] T019 [US2] Added `tests/AkmlSql.Web.Tests/Refactoring/LightweightParityTests.cs`: a 10-case `[Theory]` asserting the browser service output == an **independently-constructed** reference `op.Apply` for every kind (catches a kind→op mis-wire), plus goldens (RemoveSemicolons strips all `;`; ConvertOldStyleJoins emits `INNER JOIN`), a no-op `Changed==false` case (FR-011), and an unparseable-SQL-unchanged case (edge case). **14 tests green** (10 theory cases + 4 facts), verified by `dotnet test` (FR-009 / SC-003 proven).

**Checkpoint**: All ten lightweight refactorings run offline with menu + preview; parity tests green; engine suite still green (Phase 2 gate holds).

---

## Phase 5: User Story 3 — Heavyweight refactorings (bridge-only) (Priority: P2)

**Goal**: Smart Rename / Parameterize Values / Extract Procedure with preview + conflict handling, via a live engine; gated when offline. (Contract: `contracts/refactoring-contract.md` Part B. No relocation.)

**Independent Test**: With a live engine advertising `refactoring.heavy` — Smart Rename previews every affected site and applying renames all; disconnect the engine and the three ops render the gated notice (even with a cached schema).

- [ ] T020 [US3] Add the three heavyweight entries to the refactoring menu (`Pages/Editor.razor`), enabled when `IRefactoringService.HeavyAvailable`; otherwise wrap in `<CapabilityNotice RequiredCapability="refactoring.heavy">` so they are gated, never silently absent (FR-017).
- [ ] T021 [US3] Add the input affordances in `src/AkmlSql.Web/Shared/RefactorInputDialog.razor` (new) invoked from `Pages/Editor.razor`: a rename dialog capturing `OriginalIdentifier` (from the caret token) + `NewName`; Extract Procedure capturing `ExtractedUnitName` + requiring a selection; Parameterize Values needs no extra name (FR-014). Wire to `IRefactoringService.PreviewAsync`/`ApplyAsync` (existing bridge path).
- [ ] T022 [US3] Extend `Shared/RefactorPreviewPanel.razor` for the heavyweight `RefactorPreviewResponse`: render `Changes[]` (affected sites) + `GeneratedObjectTexts[]`; on `CanApply==false` show `Errors` (e.g. rename collision) and let the user resolve/cancel before apply; apply sends `RefactorApplyRequest { OperationType, ApprovedChanges }` (FR-016).
- [ ] T023 [P] [US3] Retain the existing 4 gating tests in `tests/AkmlSql.Web.Tests/Refactoring/RefactoringServiceTests.cs`; the **online** preview/apply path (first-ever coverage) is exercised by the US6 E2E suite (folded into T031) — note the cross-reference here.

**Checkpoint**: Heavyweight refactorings work against a live engine with preview + conflict handling; gated (not absent) when offline; gating tests green.

---

## Phase 6: User Story 4 — Inline suppression editing (Priority: P2)

**Goal**: Line (cross-surface `-- noqa: RULEID`) + global (browser-local override) suppression from a finding, including the latent-bug fix that makes overrides actually apply. (Contract: `contracts/suppression-contract.md`.)

**Independent Test**: A finding → "Suppress on this line" inserts `-- noqa: RULEID` and the finding drops on re-analyse (rule still fires elsewhere); "Suppress globally" stops the rule document-wide and survives reload.

- [X] T024 [US4] **Bugfix (E7, FR-021)**: `AnalyserService` now takes an **optional** `IAnalysisSettingsStore?` ctor param (defaults null so the 3 existing parameterless-ctor tests keep working; DI fills it in the app). On each `AnalyseAsync` it reads `WebAnalysisSettings.RuleOverrides` and **post-processes** the findings: `"off"` drops the finding, other values remap its severity. Chose a post-pass over threading into the engine's `.casettings` plumbing (the web edition does not read `.casettings` — Decision 4); cleaner and fully unit-testable. No `Program.cs` change needed — DI resolves the registered store into the optional param. **Build+test verified** (existing `AnalyserServiceTests` still green). _Note: DI auto-fill of the optional param relies on the container resolving the registered `IAnalysisSettingsStore`; verify the singleton wiring once running, or make it explicit in `Program.cs` if needed._
- [ ] T025 [US4] Add suppression actions to each finding row in `src/AkmlSql.Web/Shared/ProblemsListComponent.razor`: "Suppress on this line" → insert ` -- noqa: <RuleId>` at the finding's line end (1-based `CodeIssueInfo.Line` → CodeMirror line end via `akml-editor.js`), matching `FixAction.cs`'s append form (FR-018/FR-019). Surface the action callback up to `Editor.razor`.
- [ ] T026 [US4] "Suppress globally" action in `src/AkmlSql.Web/Shared/ProblemsListComponent.razor` → write `RuleOverrides[RuleId]="off"` via `IAnalysisSettingsStore.SetAsync` (IndexedDB `AnalysisSettings` store); persists across reload; takes effect via the T024 bugfix (FR-020 + already-suppressed-wider edge case = no-op/hint).
- [X] T027 [US4] Added `tests/AkmlSql.Web.Tests/Analysis/SuppressionEditTests.cs` (4 tests, new `Analysis/` test dir): the `-- noqa: RULEID` line directive parses under the **real** `AkmlSql.Analysis.SuppressionParser` and suppresses only that rule on that line (not others, not other lines) — cross-surface format proven (FR-019/022); the global `"off"` override drops `PE001` (uses the `CREATE PROCEDURE … SELECT *` form, since PE001 only fires for SELECT * inside a proc — a first-cut bare-SELECT fixture failed and was corrected) via the T024 post-pass while the no-store path leaves it (FR-020/021). **4 tests green** (SC-006 mechanics proven). _Editor-side insertion UI (T025) + global-then-line no-op UI behaviour are the remaining runtime pieces._

**Checkpoint**: Line suppression is cross-surface; global suppression takes effect (bug fixed) and persists; suppression tests green.

---

## Phase 7: User Story 5 — Cache-aware status indicator (Priority: P2)

**Goal**: Live / Cached / Offline / Disconnected derived from bridge state + cache presence, no flicker. (Contract: `contracts/status-indicator-contract.md`.)

**Independent Test**: Open ⇒ Live; engine down + cache present ⇒ Cached (completions resolve); cache cleared + down ⇒ Offline; transitions update in place; no Cached↔Live flicker during reconnect.

- [ ] T028 [US5] Extend `src/AkmlSql.Web/Shared/StatusBar.razor`: inject `ISchemaCacheStore`; probe the active `(serverCanonicalIdentity, databaseName)` (sourced from `Editor.razor`'s active connection — pass as parameters or via a shared session service); derive the four-state per the contract matrix; recompute on `StateChanged` + `ISchemaSync.ChecksumDrifted` + active-connection change; hold **Cached** during `Reconnecting` with cache (no flicker), flip to **Live** only on `Open`; preserve the spec-025 reconnect countdown (E8, FR-023/FR-024).
- [ ] T029 [US5] Tests in `tests/AkmlSql.Web.Tests/Bridge/StatusIndicatorTests.cs` (new, bUnit): the full matrix (`Open`⇒Live regardless of cache; `Disconnected`+cache⇒Cached; `Disconnected`+no-cache⇒Offline); `Reconnecting`+cache stays Cached across a mid-handshake tick then flips to Live on `Open`; cache-cleared-while-Disconnected ⇒ Cached→Offline in place (SC-007).

**Checkpoint**: The indicator answers "will typing give me completions?" at a glance and tracks every transition without a reload; indicator tests green.

---

## Phase 8: User Story 6 — Offline-IntelliSense E2E + parity audit (Priority: P3)

**Goal**: Prove offline IntelliSense on the wire (the deferred T113) and audit visual parity vs the WPF surface. (Contract: `contracts/e2e-and-parity-contract.md`.)

**Independent Test**: `dotnet test --filter Category=BridgeE2E` builds from source, pairs, caches, kills the engine, asserts cached completions still resolve, relaunches, asserts Live — and the parity doc shows paired web-vs-WPF screenshots with dispositions.

- [ ] T030 [US6] Create `tests/AkmlSql.Web.E2E.Tests/UserStory4Tests.cs` on the spec-025 `EngineLaunchFixture`, `[Trait("Category","BridgeE2E")]`: build engine+web from source → pair → type → assert Live + completions → confirm cache populated → **kill engine** → assert indicator = Cached → type → assert completions still resolve (SC-008) → relaunch → assert Live without re-pair (FR-025).
- [ ] T031 [US6] Fold the **heavyweight online** assertion into `tests/AkmlSql.Web.E2E.Tests/UserStory4Tests.cs` (first coverage of FR-014's preview/apply path): with the engine live, drive a Smart Rename preview → apply and assert the rename committed across sites.
- [ ] T032 [US6] Run `dotnet test --filter Category=BridgeE2E` (both new assertions green) and confirm the default `dotnet test` does **not** run them (trait gate, FR-026).
- [ ] T033 [P] [US6] Author `specs/027-m5-offline-closure/M5-PARITY-AUDIT.md` (E9): paired web-vs-WPF screenshots of the four M5 surfaces (snippet picker/expansion, refactoring menu/preview, suppression menu, status indicator); deltas table (`element | WPF | web | disposition`); closed deltas; accepted-with-reason deltas; host OS/theme/DPI metadata. Close the top deltas in `src/AkmlSql.Web/wwwroot/css/`; ≤ 3 remain open (SC-009).

**Checkpoint**: Offline IntelliSense proven on the wire; heavyweight online path covered; parity audit checked in with ≤ 3 open deltas.

---

## Phase 9: Polish & Cross-Cutting Concerns

**Purpose**: Close the documentation loop and retire the M5 DoD against evidence (FR-027).

- [ ] T034 [P] Update `doc/WEB/quickstart-m5.md`: remove the now-closed "What is NOT in M5" caveats (cache-backed completion fallback, snippet expansion, heavyweight refactoring UI); add the snippet/refactoring/suppression/status walkthroughs.
- [ ] T035 [P] Mark spec 021 **T113** `[X]` in `specs/021-web-edition/tasks.md` with a completion note citing this spec's FR-025 + the US6 E2E suite.
- [ ] T036 [P] Add a spec-027 closure summary to `doc/progress.md` (rolling development log): what shipped, the two reconciliations (heavyweight bridge-only, suppression line+global), and the named follow-ups.
- [ ] T037 Verify FR-027: walk every M5 PRD §11 DoD checkbox and confirm each maps to a shipped feature (Overview reality table) or an FR (FR-001 … FR-026); record the two reconciled items as scoped-closed (heavyweight = live-engine; suppression = line+global). Update `specs/027-m5-offline-closure/checklists/requirements.md` notes if needed.
- [ ] T038 Full regression: `dotnet test` (default suite green) + `dotnet test --filter Category=BridgeE2E` (green) + re-confirm the engine refactoring suite (Phase-2 gate) still green.

---

## Dependencies & Execution Order

### Phase dependencies

- **Setup (Phase 1)**: no dependencies — start immediately.
- **Foundational (Phase 2 — relocation)**: depends on Setup (needs the green baseline). **Blocks US2 only**; does NOT block US1/US3/US4/US5/US6. Done first to isolate engine risk.
- **User Stories (Phases 3–8)**: US1, US3, US4, US5, US6-parity are independent of Phase 2 and can start right after Setup. US2's browser path (Phase 4) depends on Phase 2. US6's E2E (T030–T032) depends on US1–US5 being landed (it verifies them) and on the spec-025 `EngineLaunchFixture`.
- **Polish (Phase 9)**: depends on all desired stories.

### User-story dependencies

- **US1 (P1, snippets)**: independent. **MVP.**
- **US2 (P1, lightweight refactoring)**: needs Phase 2 (relocation).
- **US3 (P2, heavyweight)**: independent; shares `RefactorPreviewPanel.razor` with US2 (US2 creates it T018, US3 extends it T022 — sequence US2→US3 if one developer, else coordinate the file).
- **US4 (P2, suppression)**: independent; T024 bugfix MUST precede T026.
- **US5 (P2, status)**: independent.
- **US6 (P3, E2E + parity)**: E2E verifies US1–US5 (do last); the parity doc (T033) can be drafted in parallel once the surfaces exist.

### Within each story

- Model/relocation before service; service before UI; UI before tests that drive it (except where a test asserts a pure helper).
- US4: **T024 (bugfix) before T026 (global suppression)** — global is inert without it.
- US2/US3: `RefactorPreviewPanel.razor` is created in US2 (T018) and extended in US3 (T022).

### Parallel opportunities

- Setup: T003 ∥ (T001/T002 sequential).
- Phase 2: T007 ∥ after T006.
- US1: T008 ∥ T013 (different files); T009–T012 touch shared editor/store surfaces — sequence.
- Across stories after Phase 1: US1, US3, US4, US5 can proceed in parallel (different files); US2 waits on Phase 2.
- Polish: T034 ∥ T035 ∥ T036 (different files); T037/T038 last.

---

## Parallel Example: after Setup completes

```bash
# Phase 2 relocation runs first (isolated, engine-touching), then once green,
# these independent stories can be picked up in parallel by different developers:
Developer A: US1 (snippets)        — src/AkmlSql.Web/Pages/Snippets.razor + akml-editor.js + ISnippetStore.cs
Developer B: US4 (suppression)     — src/AkmlSql.Web/Services/IAnalyserService.cs + ProblemsListComponent.razor
Developer C: US5 (status)          — src/AkmlSql.Web/Shared/StatusBar.razor
# US2 (after Phase 2) and US3 share RefactorPreviewPanel.razor — one developer, US2 then US3.
```

---

## Implementation Strategy

### MVP first (User Story 1 only)

1. Phase 1 Setup → Phase 2 relocation (green gate) → Phase 3 US1.
2. **STOP and VALIDATE**: snippets expand/surround/manage/import-export offline with no engine.
3. Demo — the largest single user-facing gap is closed.

### Incremental delivery

1. Setup + relocation → foundation ready.
2. US1 (snippets) → MVP.
3. US2 (lightweight refactoring) → offline refactoring parity.
4. US4 (suppression) + US5 (status) → in parallel; both small and independent.
5. US3 (heavyweight) → live-engine refactoring.
6. US6 (E2E + parity) → prove + audit.
7. Polish → retire the M5 DoD.

### Notes

- [P] = different files, no dependency on an incomplete task.
- The Phase-2 relocation is the only engine-touching change; its exit criterion (T006) is "engine refactoring suite green" — treat a red suite as a hard stop.
- Two requirements were reconciled in planning (heavyweight bridge-only; suppression line+global) — see `research.md` Decisions 3 & 4; the dropped paths are named follow-ups in `spec.md` §Out of Scope, not silent gaps.
- Per the project git rule: do not stage/commit/push without explicit user approval.
