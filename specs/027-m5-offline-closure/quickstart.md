# Quickstart: M5 — Offline Parity Closure

**Spec**: [spec.md](./spec.md) · **Plan**: [plan.md](./plan.md) · **Research**: [research.md](./research.md)
**Branch**: `027-m5-offline-closure` · **Date**: 2026-05-31

Implementation walkthrough, one section per user story, priority order. Unlike the M2/M3 closures this is mostly **new feature build** on top of the already-shipped Phase-6 substrate (`AkmlSql.IntelliSense`, schema cache, offline completion). Estimated 6–9 focused days.

---

## US1 — Snippet library in the browser (P1, ~2 days)

**Goal**: expand, surround-with, manage, import/export snippets — offline.

**Steps**:

1. **Extend the model**: add `bool SurroundsWith` to `WebSnippetMetadata` (`Services/ISnippetStore.cs`); add optional `Tooltip` to `WebSnippetVariable` if not present. Keep JSON names camelCase to match the engine `Snippet`.
2. **Author the built-in set**: add embedded `.akmlsnippet` (or one JSON resource) under `wwwroot/snippets/` or as an `EmbeddedResource`; load into `builtin.*` in `SnippetStore.BuildBuiltIns` (replace the 2 hardcoded ones with the loaded set). Mark surround-capable entries `SurroundsWith=true`.
3. **Editor expansion** (`wwwroot/js/akml-editor.js`): add `expandSnippet(hostId, body)` + `surroundSelection(hostId, body)` using CM6 `@codemirror/autocomplete` `snippet()`. Normalise `${name:default}` → numbered tab-stops before the CM call.
4. **Completion wiring**: surface snippets in the completion source as a distinct item type so typing a shortcode offers the snippet; accepting calls `expandSnippet`.
5. **Surround chord**: add a chord (e.g. `Ctrl+K, Ctrl+S`) in `Editor.razor`'s `OnKeyDownAsync`; open a picker of `SurroundsWith` snippets; call `surroundSelection`.
6. **Management page**: new `Pages/Snippets.razor` (route `/snippets`), linked from `NavMenu.razor`. List (built-ins first) / create / edit / delete via `ISnippetStore`.
7. **Import/export**: `<InputFile accept=".akmlsnippet">` import (validate → `SaveAsync`); export via `akml-download.js`. See `contracts/snippet-expansion-contract.md`.
8. **Tests**: `tests/AkmlSql.Web.Tests/Snippets/` — expansion, surround, CRUD, import/export round-trip (extends the existing `SnippetStoreTests`).

**DoD**: PRD §5 snippet rows (built-in / user / import-export / surround / expand) all clickable.

---

## US2 — Lightweight refactorings offline (P1, ~2 days)

**Goal**: all ten lightweight ops run in-browser, identical to the engine.

**Steps**:

1. **Relocate** (the T101 pattern): `git mv` `ILightweightOperation.cs`, `RefactoringContext.cs`, and `Operations/Lightweight/*.cs` from `AkmlSql.Engine/Refactoring/` to `AkmlSql.IntelliSense/Refactoring/`. **Keep namespaces** (`AkmlSql.Engine.Refactoring.*`). Heavyweight, `HeavyweightOperationBase`, `ReferenceCollector` stay put.
2. **Build engine + run its refactoring tests** — must stay green with zero call-site edits (FR-013 / SC-004). This is the regression gate; do it immediately after the move.
3. **Browser path**: add `PreviewLightweightAsync` / `ApplyLightweightAsync` to `RefactoringService` — parse with `TsqlParserService`, build `RefactoringContext` with `IntelliSense` supplied (so no `ConfigManager.Load()`), call `op.Apply`. Map `LightweightKind` to `FormatActionType` 9–17.
4. **Menu + preview**: refactoring menu in the editor (toolbar/context) listing the ten; before/after preview pane; apply as one undoable CM edit.
5. **Parity test**: `tests/AkmlSql.Web.Tests/Refactoring/LightweightParityTests.cs` — browser output == engine output per op (FR-009).
6. **Manual smoke**: no engine paired; paste a comma-join → Convert Old-Style Joins → preview → apply.

**DoD**: PRD "All 9 lightweight refactorings work offline" (the real count is 10).

---

## US3 — Heavyweight refactorings (bridge-only) (P2, ~1.5 days)

**Goal**: Smart Rename / Parameterize / Extract Proc with preview, via a live engine; gated when offline.

**Steps** (no relocation — Decision 3):

1. **Refactoring UI for heavyweight**: in the same menu, three heavy entries enabled when `IRefactoringService.HeavyAvailable`; otherwise wrap in `<CapabilityNotice RequiredCapability="refactoring.heavy">` (FR-017).
2. **Rename dialog**: capture `OriginalIdentifier` (from caret token) + `NewName`; Extract Proc captures `ExtractedUnitName` + requires a selection.
3. **Preview**: render `RefactorPreviewResponse.Changes[]` + `GeneratedObjectTexts[]`; on `CanApply==false` show `Errors` (e.g. name collision) and let the user fix/cancel (FR-016).
4. **Apply**: send `RefactorApplyRequest { OperationType, ApprovedChanges }`; replace editor content.
5. **Online E2E** (folds into US6): first real preview/apply coverage against a live engine.

**DoD**: PRD heavyweight row closed as **live-engine**; cached-schema path is a named follow-up.

---

## US4 — Inline suppression editing (P2, ~1 day)

**Goal**: line (cross-surface) + global (browser-local) suppression from a finding.

**Steps**:

1. **Bugfix first** (`Services/IAnalyserService.cs`): inject `IAnalysisSettingsStore`; read `RuleOverrides` per analyse; project onto `CodeAnalysisSettings` (`"off"` → `GloballySuppressedRules`, else per-rule severity). Without this, global suppression is inert.
2. **Line suppression**: from a `ProblemsListComponent` row action, insert ` -- noqa: <RuleId>` at the finding's line end (matches `FixAction.cs`); re-analyse drops it. Use the real `SuppressionParser` form.
3. **Global suppression**: write `RuleOverrides[RuleId]="off"` via `IAnalysisSettingsStore.SetAsync`; persists; takes effect via step 1.
4. **Tests**: `tests/AkmlSql.Web.Tests/Analysis/SuppressionEditTests.cs` — line directive parses + suppresses; global persists + suppresses post-bugfix.

**DoD**: PRD "Inline suppression editing" closed for line + global; file-scope is a named follow-up.

---

## US5 — Cache-aware status indicator (P2, ~0.5 day)

**Goal**: Live / Cached / Offline / Disconnected from bridge + cache.

**Steps**:

1. **Wire cache presence into `StatusBar.razor`**: inject `ISchemaCacheStore`; probe the active `(server, db)`; derive the four-state per `contracts/status-indicator-contract.md`.
2. **No flicker**: during `Reconnecting` with cache, hold **Cached** until `Open`.
3. **Tests**: `tests/AkmlSql.Web.Tests/Bridge/StatusIndicatorTests.cs` — the matrix + the no-flicker case.

**DoD**: PRD four-state badge.

---

## US6 — E2E + parity audit (P3, ~1.5 days)

**Goal**: prove offline IntelliSense on the wire; audit visual parity.

**Steps**:

1. **`tests/AkmlSql.Web.E2E.Tests/UserStory4Tests.cs`** on the spec-025 `EngineLaunchFixture` + `[Trait("Category","BridgeE2E")]`: pair → cache → kill engine → assert Cached + completions resolve → relaunch → assert Live. Fold in the heavyweight online preview/apply assertion.
2. **Run**: `dotnet test --filter Category=BridgeE2E` (green); confirm default `dotnet test` skips it.
3. **`specs/027-m5-offline-closure/M5-PARITY-AUDIT.md`**: paired web-vs-WPF screenshots of the four M5 surfaces; deltas table; close top deltas; ≤ 3 open.

**DoD**: PRD "Offline IntelliSense works with cable yanked" + "Visual parity audit screenshots".

---

## Wrap-up

1. `dotnet test` (default) green; `dotnet test --filter Category=BridgeE2E` green.
2. Mark spec 021 **T113** `[X]` (offline E2E) and add completion notes citing this spec's FRs.
3. Update `doc/WEB/quickstart-m5.md` — remove the "What is NOT in M5" caveats now closed (cache-backed completion, snippet expansion, heavyweight UI).
4. Update `doc/progress.md` with the spec-027 closure summary.
5. Verify every M5 PRD §11 DoD checkbox maps to a shipped feature or an FR (FR-027), with the two reconciled items recorded as scoped.
