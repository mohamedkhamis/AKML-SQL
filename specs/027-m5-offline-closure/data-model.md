# Data Model: M5 — Offline Parity Closure

**Branch**: `027-m5-offline-closure` | **Date**: 2026-05-31 | **Spec**: [spec.md](./spec.md)

This closure introduces **no new IndexedDB store names** beyond what spec 021 shipped — snippets persist in the existing `snippets` store (`ISnippetStore`), analyser overrides in the existing `AnalysisSettings` store (`IAnalysisSettingsStore`), schema in `schemaEntries` (`ISchemaCacheStore`). The entities below are mostly *conceptual*: in-memory editor state, a relocated runtime context, and one extended POCO field. They exist so `tasks.md` can name them without ambiguity.

---

## E1 — WebSnippet (extended)

**Owner**: `Services/ISnippetStore.cs` (already exists; this spec extends it).

**Purpose**: The browser's snippet shape; JSON-compatible with the engine `Snippet` so `.akmlsnippet` import/export round-trips.

**Change in this spec**: add `bool SurroundsWith` to `WebSnippetMetadata`, mirroring the engine `SnippetMetadata.SurroundsWith`. Required so the surround-with chord (FR-003) can filter to surround-capable snippets and so import/export stays lossless.

**Fields** (post-change):

| Field | Type | Meaning |
|---|---|---|
| `Metadata.Id` | `string` | `builtin.*` prefix ⇒ immutable. |
| `Metadata.Shortcode` | `string` | The trigger the user types to expand. |
| `Metadata.Title` / `Description` / `Author` / `Tags[]` | — | Display + management metadata. |
| `Metadata.SurroundsWith` | `bool` **(new)** | True ⇒ snippet wraps a selection (surround-with eligible). |
| `Variables[]` | `WebSnippetVariable[]` | `Name` / `Default` / (new optional `Tooltip`) per placeholder. |
| `Body` | `string[]` | Lines; embed `${name:default}` / `${1:label}` tab-stops; surround snippets embed a `$selected$` token. |

**Validation rules**:

- `IsBuiltIn` (id starts `builtin.`) ⇒ save/delete throw `InvalidOperationException` (already enforced).
- A malformed body (unbalanced `${}`) ⇒ expansion falls back to literal insertion (edge case), never throws.
- Import: a `.akmlsnippet` whose shortcode collides with a built-in is rejected or renamed (FR-005); it never overwrites a `builtin.*`.

---

## E2 — Built-in snippet set

**Owner**: embedded resource set in `AkmlSql.Web` (authored in-repo by this spec).

**Purpose**: The curated, immutable snippet library present with no engine and no network (FR-001). The repo has no canonical engine `.akmlsnippet` files to mirror, so this set is **defined fresh** (spec Assumptions); `ssf` + `cte` (already synthesised in `SnippetStore.BuildBuiltIns`) are the floor.

**Construction rule**: embedded as a JSON resource (or a small set of `.akmlsnippet` resources) compiled into the WASM bundle; loaded into the `builtin.*` namespace at `SnippetStore` construction. Each entry MUST be valid against E1 and MUST set `SurroundsWith` correctly (e.g. a `BEGIN…END` wrapper sets it true).

---

## E3 — LightweightRefactorOperation (relocated runtime entity)

**Owner**: relocated from `AkmlSql.Engine.Refactoring.Operations.Lightweight` into `AkmlSql.IntelliSense` (namespace unchanged: `AkmlSql.Engine.Refactoring.Operations.Lightweight`).

**Purpose**: One parser-only text transformation; the unit US2 runs in-browser. Ten members:

`ExpandInsertColumns`, `ExpandUpdateColumns`, `ConvertOldStyleJoins`, `EncapsulateBeginEnd`, `RemoveSemicolons`, `ReplaceDeprecatedSyntax`, `ExpandExecParameters`, `ConvertSpExecutesql`, `AddGroupByColumns`, `Unformat`.

**Contract** (unchanged from today): `(string modifiedText, string[] warnings) Apply(RefactoringContext context)`.

**Construction rule (browser path)**: `RefactoringService.ApplyLightweightAsync` builds a `RefactoringContext` (E4) via `TsqlParserService` (already in `AkmlSql.IntelliSense`), supplies `context.IntelliSense` from the browser's settings (so `ConfigManager.Load()` is never reached under WASM), and calls `op.Apply(ctx)`.

**Validation rules**:

- Unparseable SQL ⇒ op returns `(originalText, [])` (already the behaviour; mirrors the engine).
- Output MUST equal the engine's output for the same input (FR-009) — structural because it is the same code.
- The relocated files MUST NOT introduce any `System.IO` / SqlClient / native dependency (FR-013).

**Note**: heavyweight operations are **not** relocated (Decision 3) — they stay in `AkmlSql.Engine` and run via the bridge.

---

## E4 — RefactoringContext (relocated)

**Owner**: relocated alongside E3 into `AkmlSql.IntelliSense` (namespace `AkmlSql.Engine.Refactoring`).

**Purpose**: Per-request inputs a lightweight op needs.

**Fields used by the browser path**:

| Field | Type | Browser value |
|---|---|---|
| `Script` | `TSqlScript` | from `TsqlParserService.Parse(text)` |
| `Tokens` | `IList<TSqlParserToken>` | from `TsqlParserService.GetTokenStream(text)` |
| `DocumentText` | `string` | editor content |
| `SelectionStart` / `SelectionLength` | `int` | current selection (0/0 ⇒ whole doc) |
| `IntelliSense` | `IntelliSenseSettings?` | **always supplied by the browser** ⇒ no disk read |
| `SchemaCache` | `DatabaseCache?` | null for lightweight (parser-only) |

**Validation rules**: `SelectionLength > 0` ⇒ op acts on the selection; else whole document. The browser must pass a non-null `IntelliSense` for the two ops that would otherwise call `ConfigManager.Load()` (`ExpandInsertColumns`, `ExpandExecParameters`).

---

## E5 — RefactorPreview (in-memory; lightweight + heavyweight)

**Owner**: the browser refactoring UI (in-memory only).

**Purpose**: The before/after representation shown before commit (FR-011, FR-016).

**Shape**:

- **Lightweight**: `{ before: string, after: string, warnings: string[], changed: bool }` — derived locally by running `Apply` and diffing against the input. `changed == false` ⇒ "no change / not applicable" state.
- **Heavyweight**: the existing `RefactorPreviewResponse` from the bridge (`Changes[]`, `Warnings[]`, `Errors[]`, `CanApply`, `GeneratedObjectTexts[]`). `CanApply == false` with `Errors` (e.g. "Name collision") ⇒ the conflict state the user resolves or cancels.

**State transitions**: preview shown → apply (commit as single undoable edit) | cancel (discard). Heavyweight apply sends `RefactorApplyRequest` with `ApprovedChanges`.

---

## E6 — SuppressionEdit (in-memory action)

**Owner**: the browser problems-list / editor finding UI.

**Purpose**: One user action turning a finding into a suppression (FR-018 … FR-022).

**Two scopes** (file-scope dropped — Decision 4):

| Scope | Effect | Cross-surface? |
|---|---|---|
| **Line** | Insert `-- noqa: RULEID` at the finding's line (append to line end, matching `FixAction.cs`). | **Yes** — engine + WPF + web read it identically. |
| **Global** | Add/replace `RuleId → "off"` in `WebAnalysisSettings.RuleOverrides` (IndexedDB); persists across reload. | **No** — browser-local; web does not read `.casettings`. |

**Validation rules**:

- Line suppression for a rule already globally off ⇒ no-op / hint, not a duplicate directive (edge case).
- The inserted directive MUST be the exact `-- noqa: RULEID` form `SuppressionParser` matches (`--\s*noqa\s*:\s*RULEID`), so the next analysis pass drops the finding.
- Global override MUST actually take effect — requires the E7 bugfix.

---

## E7 — Analyser settings wiring (bugfix)

**Owner**: `Services/IAnalyserService.cs` (`AnalyserService`).

**Purpose**: Make per-rule overrides real. Today `AnalyserService` constructs `new CodeAnalysisSettings { Enabled = true }` and **never reads** `IAnalysisSettingsStore.RuleOverrides`, so every override the Settings UI writes (and every "Suppress globally" this spec adds) is inert.

**Change**: inject `IAnalysisSettingsStore`; on each `AnalyseAsync`, read `WebAnalysisSettings.RuleOverrides` and project them onto the `CodeAnalysisSettings` the engine consumes — a rule mapped to `"off"` goes into `GloballySuppressedRules`; other values map to per-rule severity. `Enabled` / `AutoAnalyseOnFormat` come from the same record.

**Validation rules**: an override of `"off"` ⇒ the rule's findings are filtered (the engine already filters `GloballySuppressedRules` in `AnalysisEngine.AnalyzeAsync`). Changing an override invalidates the cached settings so the next pass reflects it.

---

## E8 — IntelliSenseAvailabilityState (in-memory; derived)

**Owner**: `Shared/StatusBar.razor`.

**Purpose**: The single user-facing state answering "will typing give me completions?" (FR-023, FR-024). Derived from `IEngineBridge.State` + cache presence for the active `(server, db)` per the Decision 5 matrix.

**States**: `Live` | `Cached` | `Offline` | `Disconnected` (+ the reconnect countdown text from spec 025 is preserved as a sub-label of `Reconnecting`).

**State transitions**: recomputed on `IEngineBridge.StateChanged`, `ISchemaSync.ChecksumDrifted`, and active-connection change; updates in place (no reload, FR-024). During `Reconnecting` with cache present, holds `Cached` (no flicker) until `Open`.

---

## E9 — M5 parity audit document

**Owner**: `specs/027-m5-offline-closure/M5-PARITY-AUDIT.md` (checked-in markdown).

**Purpose**: The web-vs-WPF comparison record (FR-026), following the spec 024 `M2-THEME-PARITY-AUDIT.md` shape.

**Required content**: paired screenshots (web + WPF) for each M5 surface — snippet picker/expansion, refactoring menu/preview, suppression menu, status indicator; a deltas table (surface element / WPF rendering / web rendering / disposition); the list of closed deltas; the list of accepted-with-reason deltas; host OS/theme/DPI metadata. ≤ 3 deltas may remain open (SC-009).
