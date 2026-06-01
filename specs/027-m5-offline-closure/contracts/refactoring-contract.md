# Contract: Refactoring — lightweight (offline) + heavyweight (bridge-only) (US2, US3)

**Spec**: [spec.md](../spec.md) · **Research**: [research.md](../research.md) Decisions 2 & 3 · **FRs**: FR-008 … FR-017

## Part A — Lightweight (US2): offline, in-browser, parser-only

### Relocation (FR-013)

Move from `src/AkmlSql.Engine/Refactoring/` into `src/AkmlSql.IntelliSense/Refactoring/`, **namespaces unchanged** (`AkmlSql.Engine.Refactoring`, `AkmlSql.Engine.Refactoring.Operations`, `AkmlSql.Engine.Refactoring.Operations.Lightweight`):

- `ILightweightOperation.cs`
- `RefactoringContext.cs`
- `Operations/Lightweight/*.cs` (all ten)

The engine references `AkmlSql.IntelliSense` already and keeps calling these types transitively — **zero engine call-site edits** (the T101 pattern). `RefactoringSettings` (used by `RefactoringContext.Settings`) lives in `AkmlSql.Core.Config` and is already shared. **Heavyweight ops, `HeavyweightOperationBase`, and `ReferenceCollector` do NOT move** — they stay engine-side (Part B).

**WASM-safety invariant**: the relocated files import only `Microsoft.SqlServer.TransactSql.ScriptDom`, `AkmlSql.Engine.Schema.Models` (already in the shared lib), and `AkmlSql.Core.Config`. The two ops that call `ConfigManager.Load()` (`ExpandInsertColumns`, `ExpandExecParameters`) only do so when `RefactoringContext.IntelliSense == null`; the browser always sets it, so no disk read under WASM. No `System.IO`, no SqlClient, no Serilog-to-file.

### The ten operations

`ExpandInsertColumns`, `ExpandUpdateColumns`, `ConvertOldStyleJoins`, `EncapsulateBeginEnd`, `RemoveSemicolons`, `ReplaceDeprecatedSyntax`, `ExpandExecParameters`, `ConvertSpExecutesql`, `AddGroupByColumns`, `Unformat`.

> The PRD's illustrative list is wrong: `ConvertTempTable` is **heavyweight**, and "Add/Remove Square Brackets" is a formatter/casing action (`FormatActionType.AddSquareBrackets`/`RemoveSquareBrackets`), not a refactoring op. This list is the engine's real `ILightweightOperation` registry.

### Browser execution path

`RefactoringService` gains (offline, no bridge):

```
Task<LightweightPreview> PreviewLightweightAsync(LightweightKind kind, string sql, int selStart, int selLen)
Task<string> ApplyLightweightAsync(LightweightKind kind, string sql, int selStart, int selLen)
```

Both: parse via `TsqlParserService` → build `RefactoringContext` with `IntelliSense` supplied → `op.Apply(ctx)`. `LightweightKind` maps 1:1 to the existing `FormatActionType` enum values 9–17 (no new enum).

### Menu + preview (FR-010, FR-011, FR-012)

- A refactoring menu (editor context / toolbar) lists all ten; inapplicable-to-selection ops MAY render disabled with a reason, but the menu is **never empty offline** (FR-010).
- Selecting an op shows a before/after preview (`LightweightPreview { before, after, warnings[], changed }`); `changed == false` ⇒ explicit "no change / not applicable" (FR-011).
- Apply replaces editor content as a **single undoable edit** (one CodeMirror transaction) and respects the 10 MB `DocumentSizeLimit` ceiling (FR-012).

### Parity (FR-009, SC-003)

Output equals the engine's for the same input — structural, because both run the same `Apply`. Test: `tests/AkmlSql.Web.Tests/Refactoring/LightweightParityTests.cs` runs each op on a representative snippet and asserts the browser path == a direct engine-side `op.Apply` (or a recorded golden). The existing engine refactoring suite MUST stay green post-relocation (FR-013 / SC-004).

## Part B — Heavyweight (US3): bridge-only, gated when offline

### Scope (Decision 3 — cached path descoped)

The three PRD-named ops only:

| UI label | Engine `RefactorOperationType` |
|---|---|
| Smart Rename | `SafeRename` (0) |
| Parameterize Values | `ParameterizeValues` (7) |
| Extract Procedure | `ExtractToProc` (2) |

Run **only** via the existing bridge path (`IRefactoringService.PreviewAsync`/`ApplyAsync` → `RequestRefactorPreview`(30)/`RequestRefactorApply`(31)), gated on `HeavyAvailable` (bridge `Open` + `refactoring.heavy` capability). **No relocation, no cache rehydrator.**

### Availability + gating (FR-015 revised, FR-017)

- Available ⇔ `HeavyAvailable == true` (live engine + capability).
- Otherwise (bridge not open, or capability absent — **including engine-down-but-cache-present**): the three ops render the existing `CapabilityNotice` (`RequiredCapability="refactoring.heavy"`), never silently absent (FR-017).

### Preview + conflict (FR-014, FR-016)

- Preview renders `RefactorPreviewResponse.Changes[]` (affected sites) + `GeneratedObjectTexts[]` (e.g. proc body).
- `CanApply == false` with `Errors` (e.g. `SafeRename`'s "Name collision: '<x>' already exists") ⇒ conflict state; user resolves (e.g. new name) or cancels before apply (FR-016).
- Apply sends `RefactorApplyRequest { OperationType, ApprovedChanges }`.

### Inputs per op

- Smart Rename: `OriginalIdentifier` + `NewName` (rename dialog).
- Extract Procedure: requires a selection + `ExtractedUnitName` (proc name).
- Parameterize Values: operates on the document/selection; no extra name.

### Test contract (FR-014; closes the zero-coverage gap)

`tests/AkmlSql.Web.Tests/Refactoring/HeavyweightServiceTests.cs` (existing 4 gating tests retained) **plus** an online preview/apply path exercised in the US6 E2E suite (`[Trait("Category","BridgeE2E")]`) against a real engine: rename preview lists sites → apply renames all; parameterize/extract preview → apply.

## Out of scope (named follow-ups)

- Heavyweight execution against a **cached** schema (needs a `SchemaPhasePayload → DatabaseCache` rehydrator) — Decision 3.
- The five non-PRD heavyweight ops (`ExtractToCte`, `ExtractToDerivedTable`, `EncapsulateAsView`, `ConvertTempTable`, `SplitTable`) — the bridge path is generic, so adding them later is a UI-only follow-on.
