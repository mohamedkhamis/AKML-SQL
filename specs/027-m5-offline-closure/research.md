# Research: M5 — Offline Parity Closure

**Branch**: `027-m5-offline-closure` | **Date**: 2026-05-31 | **Spec**: [spec.md](./spec.md)

Six technical decisions, one per user story, plus two **scope reconciliations** where the spec as first written collided with the actual engine and the user chose the narrower, lower-risk path. No open `NEEDS CLARIFICATION` items. Every decision below was checked against current source, not the M5 PRD's stale "current state" paragraph.

---

## Decision 1 — Snippet expansion + surround-with run in the browser (US1)

**Decision**: Snippet **expansion** and **surround-with** are pure browser-side text manipulation, implemented in the CodeMirror layer (`wwwroot/js/akml-editor.js`) driven by the existing `ISnippetStore`. No engine round-trip; no IPC. The placeholder grammar the browser interprets is the engine `Snippet` shape already mirrored by `WebSnippet`: `Body` is a `string[]` of lines; `Variables[]` carry `Name` / `Default` / `Tooltip`; the body embeds `${name:default}` / `${1:label}` placeholders (the form the two existing built-ins `ssf` / `cte` already use). Surround-with is gated on `WebSnippetMetadata.SurroundsWith` (the field already exists on the engine `SnippetMetadata`; it is added to `WebSnippetMetadata`) and substitutes the current selection for the snippet's `$selected$` token.

**Rationale**:

1. **Expansion is text, not schema.** The PRD §4.4 says "Snippet expansion runs entirely in the browser — pure text manipulation, no engine round-trip needed." The data path (`ISnippetStore` with built-in + user CRUD) already shipped (T114); only the editor-side expansion and the management UI are missing.
2. **CodeMirror already owns the editor surface.** `akml-editor.js` already exposes `insertAtCaret`, `getText`, `setSelection`, and a completion source bridged to `ICompletionService`. Snippet expansion is the same class of operation — a dispatched CM transaction with a tracked selection for the first tab-stop. CodeMirror 6's `@codemirror/autocomplete` ships `snippet()` / `snippetCompletion()` primitives with `${}` tab-stop semantics, so the placeholder walk is library-provided, not hand-rolled.
3. **The surround chord matches the WPF surface's `SurroundsWith` flag.** The engine `SnippetMetadata.SurroundsWith` already classifies which snippets wrap a selection. Mirroring that one bool into `WebSnippetMetadata` keeps import/export round-trips lossless and the two surfaces semantically aligned.
4. **Built-in set is defined fresh (see spec Assumptions).** The repo ships no canonical `.akmlsnippet` files (the engine loads them from an installer-placed dir at runtime via `SnippetLoader.LoadFromDirectory`). The browser's built-ins are therefore embedded resources authored in-repo; the existing `ssf` / `cte` are the floor.

**Alternatives considered**:

- **Expand via the bridge (engine `SnippetExpandHandler`)**: Rejected. Breaks the offline promise; adds a round-trip for a pure-text op; the engine's expander needs a live session. The bridge stays only for best-effort *save/delete* propagation (already shipped, capability-gated).
- **Hand-roll the tab-stop state machine**: Rejected. CM6's `snippet()` already implements placeholder navigation; reusing it is less code and fewer edge cases (nested/escaped `$`).

**Consumer**: US1 / FR-001 … FR-007.

---

## Decision 2 — Lightweight refactorings relocate into `AkmlSql.IntelliSense` and run in-browser (US2)

**Decision**: Relocate the ten lightweight operations, the `ILightweightOperation` interface, and `RefactoringContext` from `AkmlSql.Engine.Refactoring` into `AkmlSql.IntelliSense` (new `Refactoring/` folder), **keeping the namespaces stable** (`AkmlSql.Engine.Refactoring`, `AkmlSql.Engine.Refactoring.Operations.*`) exactly as T101 did for the completion/parser/schema move. The engine continues to consume them transitively (zero call-site edits). The browser's `RefactoringService` gains an `ApplyLightweightAsync(operationType, sql, selection)` path that parses with `TsqlParserService` (already in `AkmlSql.IntelliSense`), builds a `RefactoringContext`, and calls `op.Apply(ctx)` directly — no bridge, no IPC.

**Rationale**:

1. **This is the second instance of the M0/T101 pattern.** T101 already moved 32 files (completion, parser, `DatabaseCache`, schema models) into `AkmlSql.IntelliSense` while preserving the `AkmlSql.Engine.*` namespaces, so no engine call site changed — only the assembly boundary. The lightweight ops are the same shape of move. The proven pattern de-risks FR-013's "no engine regression" requirement: the engine keeps calling the identical code.
2. **The ops are already WASM-safe.** Verified: every Lightweight/*.cs depends only on `Microsoft.SqlServer.TransactSql.ScriptDom` (already in `AkmlSql.IntelliSense`) plus `AkmlSql.Engine.Schema.Models` (already relocated by T101). The only host-coupling is two ops calling `ConfigManager.Load()` — and `RefactoringContext.IntelliSense` is a deliberate escape hatch (the op uses it when non-null and never touches disk). The browser always supplies `context.IntelliSense`, so `ConfigManager.Load()` is never reached under WASM.
3. **In-browser = identical output = parity for free (FR-009).** Because both surfaces execute the *same* `op.Apply`, output parity is structural, not a re-implementation to keep in sync. The parity test (FR-009 / SC-003) becomes a regression guard, not a reconciliation chore.
4. **No new IPC message type.** The engine already exposes lightweight ops over `FormatAction` (MessageType 13) for the IDE plugin; the browser bypasses that entirely by running the op locally. The gate "no new IPC message types" holds.

**Alternatives considered**:

- **Browser calls the engine's `FormatAction` over the bridge**: Rejected. Defeats the PRD's headline offline promise ("lightweight refactorings need no engine round-trip at all"); the user chose **extract-to-shared-lib** explicitly.
- **Re-implement the ten ops in TypeScript/JS**: Rejected. Two divergent implementations to keep byte-identical forever; the parser isn't even available in JS. The C# ops already run in WASM.
- **Move into a third new library**: Rejected. `AkmlSql.IntelliSense` is exactly "the shared logic both surfaces run"; refactoring ops belong with the parser they depend on.

**Consumer**: US2 / FR-008 … FR-013.

---

## Decision 3 — Heavyweight refactorings stay bridge-only; cached-schema execution is descoped (US3) — RECONCILIATION

**Decision**: The three heavyweight operations (Smart Rename → engine `SafeRename`, Parameterize Values → `ParameterizeValues`, Extract Procedure → `ExtractToProc`) run **only via the engine bridge** using the already-shipped `IRefactoringService.PreviewAsync` / `ApplyAsync` path, gated on bridge-open + the `refactoring.heavy` capability. When the engine is unreachable — **even if a schema is cached** — the operations render the inline `CapabilityNotice` (gated, never silently absent). The spec's original FR-015 "or when a cached schema is available" execution path is **dropped**. This spec instead adds the first end-to-end test coverage of the online preview/apply path (which has none today).

**Rationale** (user-confirmed via the planning question):

1. **The cached path is the largest, riskiest piece of M5 for the least certain payoff.** Satisfying "run heavyweight offline from cache" requires (a) relocating `SafeRenameOperation`, `ParameterizeValuesOperation`, `ExtractToProcOperation`, `HeavyweightOperationBase`, and `ReferenceCollector` into the shared lib, **and** (b) building a brand-new `SchemaPhasePayload → DatabaseCache` rehydrator. The cache stores flat `SchemaPhasePayload` MessagePack bytes (the `DatabaseCache → payload` serializer exists; **no reverse exists**). That rehydrator becomes a permanent second deserialization path that must stay byte-compatible with the engine's `DatabaseCache` forever.
2. **The online path itself is untested.** `IRefactoringService.PreviewAsync` / `ApplyAsync` have only 4 unit tests asserting they return null when heavy is unavailable — there is **zero** coverage of an actual preview/apply against a live engine. Closing that gap is higher-value than building an offline path on top of an unverified online one.
3. **Honest gating beats a silent half-feature.** The `CapabilityNotice` pattern (spec 025 / T076) already exists for exactly this — "feature requires engine X." Surfacing heavyweight as gated-when-offline is truthful and consistent; pretending it works offline against a possibly-stale rehydrated cache would mislead.
4. **The relocation stays cheap.** With heavyweight bridge-only, `ReferenceCollector` and the heavyweight ops **stay in `AkmlSql.Engine`** — only the *lightweight* ops move (Decision 2). The shared-lib surface grows by exactly what US2 needs and no more.

**What this changes in the spec**: FR-015 narrows to "live engine + capability"; FR-017's "neither live engine nor cached schema" gating trigger becomes "bridge not open or capability absent"; US3 acceptance scenario 3 (cached execution) becomes a gated-notice scenario; SC-005 drops "when only a cached schema is present." The cached-schema heavyweight path moves to **Out of Scope** as a named follow-up (it becomes cheap once a rehydrator is ever needed for another reason).

**Alternatives considered**:

- **Full FR-015 (relocate + rehydrator)**: Rejected by the user — largest/riskiest, permanent second deserialization path, builds on an unverified online path.
- **Drop heavyweight from M5 entirely**: Rejected. The bridge path is already 90% wired; surfacing the UI + adding online E2E is a genuine, demonstrable feature with no new infrastructure.

**Consumer**: US3 / FR-014, FR-016, FR-017 (revised); FR-015 (narrowed).

---

## Decision 4 — Suppression: line-scope is cross-surface, global is browser-local; file-scope dropped (US4) — RECONCILIATION

**Decision**: The browser offers **two** suppression scopes, not three:

- **"Suppress on this line"** → inserts `-- noqa: RULEID` at the finding's line. This is the format `AkmlSql.Analysis.SuppressionParser` already honors and that `FixAction.cs` already emits on the WPF side, so it is **genuinely cross-surface** (engine + WPF + web read it identically).
- **"Suppress globally"** → writes a browser-local per-rule override into `WebAnalysisSettings.RuleOverrides` (IndexedDB), and **fixes the latent bug** that `AnalyserService` today ignores `RuleOverrides` entirely (it hardcodes `new CodeAnalysisSettings { Enabled = true }`). Global suppression is explicitly **per-surface** (browser-local), because the web edition deliberately does not read the IDE's `.casettings` files (documented in `IAnalysisSettingsStore`).

**"Suppress in this file" is dropped.** There is no per-rule file-scope directive in the shared format — `-- noqa-begin/-end` suppresses **all** rules for a block, not one rule for a file. Adding a new `-- noqa-file: RULEID` directive would mean changing the `AkmlSql.Analysis` parser, its engine tests, *and* the WPF emit/read paths — engine- and WPF-touching work inside a web-track closure. Out of scope.

**Rationale** (user-confirmed):

1. **Line-scope already round-trips; ship the thing that works.** `SuppressionParser` parses `-- noqa: RULEID` (per-rule, per-line) and `FixAction.cs` emits exactly that string. The browser emitting the same string is true cross-surface parity with zero shared-format change.
2. **Global needs a bugfix anyway.** `AnalyserService` constructs hardcoded settings and never reads the IndexedDB `RuleOverrides` — so the per-rule overrides the Settings UI already writes are inert. Wiring `IAnalysisSettingsStore.RuleOverrides` into the analyser's `CodeAnalysisSettings.GloballySuppressedRules` / per-rule severity is a real fix that "Suppress globally" depends on, and it makes the existing Settings surface actually work.
3. **File-scope-per-rule doesn't exist and isn't worth inventing here.** A new shared directive is a cross-cutting format change touching three surfaces; the closure's job is web parity, not extending the suppression grammar. Two scopes (the one that's cross-surface + the one that's browser-local) cover the user-visible need.

**What this changes in the spec**: FR-018 entry points drop "in this file"; FR-020 becomes "Suppress globally (browser-local override + bugfix)"; FR-021 becomes the explicit `AnalyserService` override-honoring requirement; FR-022 narrows its cross-surface parity claim to line-scope directives and states global is per-surface; SC-006 narrows accordingly; US4 scenario 2 (file) is removed.

**Alternatives considered**:

- **Line + File + Global (full FR-020)**: Rejected by the user — adds a shared `-- noqa-file:` directive touching the analyzer parser + engine tests + WPF.
- **Line only**: Rejected by the user — leaves the inert-`RuleOverrides` bug unfixed and drops a useful browser-local capability that is nearly free once the bugfix lands.

**Consumer**: US4 / FR-018 … FR-022 (revised).

---

## Decision 5 — Cache-aware status indicator derives a four-state from bridge + cache (US5)

**Decision**: Replace `StatusBar.razor`'s bridge-state-only pill with a derived **IntelliSense availability** state computed from two inputs: `IEngineBridge.State` and "is a `SchemaSnapshot` present for the active `(server, db)` in `ISchemaCacheStore`." The four user-facing states map:

| Bridge state | Cache present? | Indicator |
|---|---|---|
| `Open` | any | **Live** |
| `Disconnected` / `Failed` / `Connecting` | yes | **Cached** |
| `Disconnected` / `Failed` | no | **Offline** (keyword-only) |
| `Reconnecting` | yes | **Cached** (stable; no flicker — see edge case) |
| `Reconnecting` | no | **Reconnecting** |

The existing five bridge-state pills are retained internally (and the reconnect countdown from spec 025 is preserved); the indicator adds the cache dimension on top.

**Rationale**:

1. **PRD §4.3 specifies exactly this four-state badge** ("Live / Cached / Offline / Disconnected") driven by the cache-availability matrix, not bridge state alone. Today the bar cannot say "disconnected but cached completions still work" — which is the whole point of the shipped offline path being legible.
2. **Both inputs already exist.** `StatusBar` already subscribes to `IEngineBridge.StateChanged` + `RetryScheduled`; adding an `ISchemaCacheStore` probe (keyed by the active session's `(server, db)` from `Editor.razor`) is the only new wiring. The probe is cheap (one IndexedDB `GetAsync`) and re-runs on state change.
3. **Stability during reconnect (edge case + FR-024).** During `Reconnecting` with a cache present, the indicator holds **Cached** rather than oscillating to Live the instant a handshake starts; it flips to Live only on `Open`. This avoids the flicker the spec edge-case calls out.

**Alternatives considered**:

- **Keep bridge-state pills, add a separate cache badge**: Rejected. Two badges for one question ("will typing give me completions?") is worse UX than one derived state.
- **Poll cache on a timer**: Rejected. Event-driven (on bridge `StateChanged` + `ISchemaSync.ChecksumDrifted` + active-connection change) is sufficient and avoids a polling loop.

**Consumer**: US5 / FR-023, FR-024.

---

## Decision 6 — E2E + parity reuse the spec 024/025 harnesses (US6)

**Decision**: The offline-IntelliSense E2E (the deferred T113) uses the `EngineLaunchFixture` pattern spec 025 established (`IAsyncLifetime`: build engine from source → free port → launch → readiness probe → teardown) under `tests/AkmlSql.Web.E2E.Tests/`, gated by `[Trait("Category","BridgeE2E")]` so the default `dotnet test` skips it. The visual-parity audit is a checked-in markdown doc following the spec 024 `M2-THEME-PARITY-AUDIT.md` shape (paired web-vs-WPF screenshots, deltas table, per-delta disposition).

**Rationale**:

1. **The harness already exists.** Spec 025 built `EngineLaunchFixture` + the `BridgeE2E` trait + the Playwright wiring (spec 024). T113 was deferred precisely because that harness didn't exist yet — now it does. The offline scenario is one new test class on the existing fixture: pair → populate cache → kill engine → assert cached completion → relaunch → assert Live.
2. **The parity audit format is established.** Spec 024 set the paired-screenshot + deltas-table + disposition pattern and the "close top-N, file the rest" rule. M5's audit covers the new surfaces (snippet picker/expansion, refactoring menu/preview, suppression menu, status indicator).
3. **Developer-side, not CI.** Matches the standing constraint for bridge/parity suites (specs 024/025) — these need a real engine and an interactive workstation.

**Alternatives considered**:

- **New harness**: Rejected. The `BridgeE2E` fixture is exactly what T113 needs; reuse keeps one test convention.
- **Automated pixel-diff parity**: Rejected. The project's parity audits are human-reviewed screenshot comparisons (DPI/font variance is accepted-with-reason); pixel-diff would be noise.

**Consumer**: US6 / FR-025, FR-026, FR-027.

---

## Verified against current source

| Decision | Checked file / fact | Result |
|---|---|---|
| 1 — Snippet expansion in browser | `Services/ISnippetStore.cs` (`WebSnippet`/`WebSnippetMetadata`, 2 built-ins, bridge save/delete); `wwwroot/js/akml-editor.js` (`insertAtCaret`, completion source); engine `SnippetMetadata.SurroundsWith` exists | ✓ data path shipped; expansion + mgmt UI missing |
| 2 — Lightweight relocation | 10 ops in `Refactoring/Operations/Lightweight/` depend only on ScriptDom + `Engine.Schema.Models` (T101-relocated) + the `RefactoringContext.IntelliSense` escape hatch; `FormatRequestHandler:128-164` dispatches them; T101 proved the stable-namespace move | ✓ clean relocation |
| 3 — Heavyweight bridge-only | heavy ops in `Engine/Refactoring/Operations/Heavyweight/` use `HeavyweightOperationBase` (preview/apply); `IRefactoringService` routes via `RequestRefactorPreview`(30)/`RequestRefactorApply`(31); cache stores flat `SchemaPhasePayload`, `SchemaPhaseSerializer` is one-way (no rehydrator) | ✓ cached path genuinely absent → descoped |
| 4 — Suppression format | `AkmlSql.Analysis/SuppressionParser.cs` honors `-- noqa: RULEID` (line) + `-- noqa-begin/-end` (block, all-rules); `FixAction.cs:99` emits `-- noqa: {ruleId}`; `AnalyserService` hardcodes `Enabled=true`, ignores `RuleOverrides`; web does not read `.casettings` | ✓ line cross-surface; file-per-rule absent; global wiring is a bugfix |
| 5 — Status indicator | `StatusBar.razor` subscribes to `StateChanged`+`RetryScheduled`, renders 5 bridge pills, no cache dimension; `ISchemaCacheStore.GetAsync(server, db)` available | ✓ derive 4-state from bridge + cache |
| 6 — E2E + parity harness | `tests/AkmlSql.Web.E2E.Tests/` exists (spec 024); spec 025 `EngineLaunchFixture` + `[Trait("Category","BridgeE2E")]`; T113 deferred awaiting exactly this | ✓ reuse |
