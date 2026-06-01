# Feature Specification: M5 — Offline Parity Closure (Snippets, Refactoring, Suppression Editing)

**Feature Branch**: `027-m5-offline-closure`
**Created**: 2026-05-31
**Status**: Draft
**Input**: User description: PRD `doc/WEB/M5-indexeddb-schema.md` ("M5 — IndexedDB Schema Cache, Offline IntelliSense, Snippets & Refactoring"; Status: Draft; Estimated effort 2–3 weeks)

## Overview

The M5 PRD reads as greenfield ("Snippets: not supported at all in the browser", "Refactoring: not supported at all", "IndexedDB used only for theme preference"). That "current state" section is **stale**: the offline-IntelliSense substrate the PRD asks for already shipped under spec 021 Phase 6 (tasks T100–T120) and spec 025. This is a closure spec — but unlike the M2/M3 closures (which were verification + small plumbing against substantially-finished features), **the genuine M5 gap is mostly new feature build**: the user-facing snippet, refactoring, and suppression-editing surfaces that Phase 6 stubbed at the service layer but never wired into the editor. The spec is honest about that: it is a feature-build closure, not a "prove it's done" closure.

### Reality table — what already exists vs what this spec builds

| M5 PRD area | Status today | Evidence |
|---|---|---|
| `AkmlSql.IntelliSense` shared library (completion / quick-info / signature / parser / `DatabaseCache`) | **Shipped** (T100–T103). Engine + web both reference it; ScriptDom `TSql170Parser` runs under WASM | `src/AkmlSql.IntelliSense/` (32 files), `src/AkmlSql.Web.csproj` reference |
| IndexedDB schema cache, composite `(server, db)` key | **Shipped** (T107) | `Services/ISchemaCacheStore.cs` (`SchemaSnapshot`) |
| LRU eviction on `QuotaExceededError` | **Shipped** (T110) | `Services/ISchemaCacheEvictor.cs` |
| `CHECKSUM_AGG` drift detection (30 s poll, idle suspend, Phase A/B fetch) | **Shipped** (T108) | `Services/ISchemaSync.cs` |
| Offline completion / quick-info / signature from cache | **Shipped** (T109) | `Services/CompletionService.cs`, `QuickInfoService.cs`, `SignatureHelpService.cs`, `OfflineSqlScanner.cs` |
| Schema-cache settings page (list / size / clear-one / clear-all) | **Shipped** (T111) | `Pages/SchemaCacheSettings.razor` |
| Snippet **store** (built-in + user CRUD, capability-gated bridge writes) | **Partial** — data path only; **2** built-ins synthesised in code; **no editor expansion, no surround-with, no import/export, no management page** | `Services/ISnippetStore.cs` |
| Refactoring **service shell** (format-selection local; heavy preview/apply via bridge) | **Partial** — service only; **no lightweight ops in the browser, no refactoring menu, no preview pane, no heavyweight UI** | `Services/IRefactoringService.cs` |
| Connection status badge | **Partial** — surfaces *bridge* state (Disconnected / Connecting / Open / Reconnecting / Failed); **does not surface cache-awareness** (Live vs Cached vs Offline) | `Shared/StatusBar.razor` |
| Inline suppression editing | **Not built** — `ProblemsListComponent` is display-only | `Shared/ProblemsListComponent.razor` |
| Offline-IntelliSense E2E (US4 acceptance scenarios) | **Deferred** (T113) — needs Playwright + running engine | `specs/021-web-edition/tasks.md` T113 |
| Visual parity audit vs WPF surface | **Not done** | — |

This spec covers the bottom six rows, framed as six prioritised user stories. Everything in the top six rows is already shipped and is explicitly **not** rewritten here.

> **Planning reconciliations (2026-05-31).** Two requirements were narrowed during `/speckit.plan` after the design audit hit hard engine constraints; both are user-confirmed and recorded in [research.md](./research.md) Decisions 3 & 4:
> 1. **Heavyweight refactoring is bridge-only** (live engine + `refactoring.heavy`). The original "run from a cached schema when the engine is offline" path is descoped — the cache holds flat `SchemaPhasePayload` bytes with no reverse-rehydrator to a `DatabaseCache`, and the online preview/apply path has no test coverage yet. FR-015/FR-017, US3 scenario 3, and SC-005 are revised accordingly; the cached path is a named follow-up.
> 2. **Suppression delivers line + global, not file.** Line scope (`-- noqa: RULEID`) is cross-surface; global scope is browser-local (plus a bugfix making per-rule overrides actually apply). File-scope-per-rule has no directive in the shared format and inventing one would touch the analyzer parser + engine tests + WPF. FR-018/FR-020/FR-021/FR-022, US4 scenario 2, and SC-006 are revised accordingly; file-scope is a named follow-up.

### Two PRD-vs-reality discrepancies that shape the spec

1. **The PRD's lightweight-refactoring list is partly inaccurate.** It lists "Convert Temp Table" as lightweight — but in the engine `ConvertTempTable` is a **heavyweight** (schema-aware) operation. It lists "Add/Remove Square Brackets" — but **no such engine operation exists** (bracket normalisation is a formatter/casing concern, not a refactoring op). The engine's *actual* lightweight registry is **ten** operations with different names (`ExpandInsertColumns`, `ExpandUpdateColumns`, `ConvertOldStyleJoins`, `EncapsulateBeginEnd`, `RemoveSemicolons`, `ReplaceDeprecatedSyntax`, `ExpandExecParameters`, `ConvertSpExecutesql`, `AddGroupByColumns`, `Unformat`). This spec targets the real ten, not the PRD's illustrative list.
2. **The PRD says built-in snippets are an "embedded JSON resource — same files as the engine ships."** The engine ships **no** `.akmlsnippet` files in the repo; it loads built-ins from an installer-placed directory at runtime (`SnippetLoader.LoadFromDirectory`). There is therefore **no canonical in-repo built-in set** to embed. This spec defines the built-in set fresh as embedded resources in the web bundle (see Assumptions).

## User Scenarios & Testing *(mandatory)*

### User Story 1 — Use the full snippet library in the browser (Priority: P1)

A user typing in the web editor can expand a built-in or personal snippet by typing its shortcode and accepting it (tab-trigger), can wrap a selection in a snippet via a surround-with shortcut, can browse / create / edit / delete personal snippets in a dedicated management surface, and can import a `.akmlsnippet` file from disk and export a personal snippet back to disk. Built-in snippets are always present (shipped in the bundle) and cannot be modified or deleted.

**Why this priority**: Snippets are a marquee productivity feature on the WPF surface and a PRD §5 row marked **Yes** across five sub-features (built-in, user, import/export, surround-with, expand). Today only the *data path* exists (`ISnippetStore` with two synthesised built-ins) — the user cannot expand a snippet in the editor, cannot manage snippets, and cannot import/export. This is the single largest unmet user-facing gap and it has no engine dependency for the offline path (expansion is pure text manipulation), so it delivers value standalone.

**Independent Test**: Open the web editor with no engine paired. Type a built-in shortcode (e.g. `ssf`), accept the suggestion, and confirm the snippet body expands with the caret landing on the first tab-stop. Select a block, invoke the surround-with shortcut, pick a surrounding snippet, and confirm the selection is wrapped. Open the snippet management surface, create a personal snippet, expand it in the editor, then delete it. Import a `.akmlsnippet` file and confirm the imported snippet appears and expands; export a personal snippet and confirm a download is offered whose contents round-trip back through import unchanged.

**Acceptance Scenarios**:

1. **Given** a built-in snippet with a shortcode, **When** the user types the shortcode in the editor and accepts the completion, **Then** the snippet body is inserted at the caret with placeholder variables rendered as editable tab-stops and the caret positioned at the first tab-stop.
2. **Given** a multi-line selection, **When** the user invokes the surround-with shortcut and chooses a surround-capable snippet, **Then** the selection is wrapped by the snippet's leading and trailing segments and the original selected text is preserved inside.
3. **Given** the snippet management surface, **When** the user creates a personal snippet with a shortcode, title, body, and variables, **Then** it is persisted locally, appears in the list below the built-ins, and is immediately expandable in the editor.
4. **Given** a personal snippet exists, **When** the user edits its body or deletes it, **Then** the change persists across a browser reload; **and** attempting to edit or delete a built-in snippet is refused with a clear message.
5. **Given** a `.akmlsnippet` file on disk, **When** the user imports it, **Then** it is validated, persisted as a personal snippet, and made expandable; a malformed file is rejected without corrupting the existing library.
6. **Given** a personal snippet is selected, **When** the user exports it, **Then** a `.akmlsnippet` download is offered whose JSON re-imports to a byte-identical snippet (round-trip stable) and is loadable by the WPF/engine surface.

---

### User Story 2 — Run lightweight refactorings offline in the browser (Priority: P1)

A user with no engine paired can select SQL (or place the caret in a statement), open a refactoring menu, choose any of the engine's lightweight (text/parser-only) refactorings, see a before/after preview, and apply it — entirely in the browser with no engine round-trip. The same refactoring produces the same result the WPF/engine surface produces for the same input.

**Why this priority**: This is the PRD's headline offline promise ("lightweight refactorings need no engine round-trip at all"). The lightweight operations are parser-only (`TSql170Parser` is already WASM-safe and already runs in `AkmlSql.IntelliSense`), so they can run client-side. Today the browser has only a `FormatSelectionAsync` shim — none of the ten real operations are reachable. Shipping them offline gives the web surface genuine refactoring parity for the no-engine case and is independently demonstrable.

**Independent Test**: Open the web editor with no engine paired. Paste SQL exercising each lightweight operation (e.g. an `INSERT … VALUES` without a column list for Expand INSERT Columns; a comma-join for Convert Old-Style Joins). For each operation, open the refactoring menu, select it, confirm the preview shows the transformed text, apply it, and confirm the editor content matches the engine's output for the same input.

**Acceptance Scenarios**:

1. **Given** SQL eligible for a lightweight refactoring and no engine paired, **When** the user opens the refactoring menu, **Then** all ten lightweight operations are listed (operations not applicable to the current selection may be shown disabled with a reason, but the menu is never empty offline).
2. **Given** a lightweight operation is chosen, **When** the preview renders, **Then** it shows the operation's resulting text (or a clear "no change / not applicable" state) before the user commits.
3. **Given** the preview is shown, **When** the user applies the operation, **Then** the editor content is replaced with the transformed text and a single undo restores the prior content.
4. **Given** the same input SQL and operation, **When** the operation runs in the browser versus the engine, **Then** the output is identical (offline lightweight refactoring is at parity with the engine).
5. **Given** the lightweight operations were relocated into the shared library so both surfaces run identical code, **When** the existing engine refactoring test suite runs, **Then** it stays green (no regression in the engine's refactoring behaviour).

---

### User Story 3 — Run heavyweight (schema-aware) refactorings when schema is available (Priority: P2)

A user with a live engine paired (advertising the `refactoring.heavy` capability) can invoke the three PRD-named heavyweight refactorings — Smart Rename, Parameterize Values, Extract Procedure — see a preview of the multi-site change, resolve any conflicts the operation surfaces, and apply it. When the engine is unreachable (regardless of whether a schema is cached), these operations are visibly gated (not silently missing) with an explanation of what unlocks them.

**Why this priority**: Heavyweight refactorings are a PRD §5 row marked **Yes** and complete the refactoring story, but they are P2 (below the offline-lightweight story) because they require a live engine and carry more UI (rename target entry, conflict resolution). The service path already exists (`IRefactoringService.PreviewAsync` / `ApplyAsync`, gated on the `refactoring.heavy` capability); the gap is the user-facing UI plus the absence of any end-to-end coverage of the online preview/apply path. (Running heavyweight ops against a cached schema while the engine is offline is descoped to a named follow-up — see the planning reconciliation note in the Overview.)

**Independent Test**: With a live engine advertising `refactoring.heavy`, select an identifier and invoke Smart Rename; confirm the preview lists every affected site and applying it renames all of them. Repeat for Parameterize Values and Extract Procedure. Then disconnect the engine and confirm the operations render a gated notice rather than disappearing — even when a schema is cached for the active database.

**Acceptance Scenarios**:

1. **Given** a live engine advertising the heavyweight capability, **When** the user invokes Smart Rename / Parameterize Values / Extract Procedure, **Then** a preview of the affected sites renders and applying it commits the multi-site change.
2. **Given** a heavyweight operation surfaces a conflict (e.g. a rename collision), **When** the preview renders, **Then** the conflict is shown and the user can resolve or cancel before applying.
3. **Given** the engine is unreachable (whether or not a schema is cached), **When** the user opens the refactoring menu, **Then** the three heavyweight operations are shown gated with a notice explaining that a paired engine advertising the capability is required — they are never silently absent.

---

### User Story 4 — Edit analysis suppressions inline (Priority: P2)

A user looking at an analysis finding in the browser can act on it directly: from the finding (in the problems list or at the editor location) they can choose to suppress that rule on this line or globally, and the suppression takes effect without leaving the editor. A line suppression written by the browser uses the exact inline-directive form the engine and WPF surface already understand, so a line suppressed in the browser reads as suppressed everywhere. (File-scope-per-rule is descoped — see the Overview reconciliation note.)

**Why this priority**: PRD §5 marks "Inline suppression editing" **Yes** and explicitly notes it "was display-only in M2." The current `ProblemsListComponent` renders findings but offers no action on them. This is a self-contained feature (no engine dependency — line suppressions are inline comments; global is a browser-local override) that closes a distinct DoD checkbox. It also fixes a latent bug: the analyser currently ignores the per-rule overrides the Settings UI already writes, so "Suppress globally" depends on wiring those overrides into the analysis pass.

**Independent Test**: Open the web editor with SQL that produces at least one analysis finding. From the finding, choose "Suppress on this line" and confirm an inline `-- noqa: RULEID` directive is inserted at that line and the finding disappears on the next analysis pass. Then choose "Suppress globally" on another finding and confirm the rule stops firing across the whole document and the suppression survives a reload. Confirm the line directive is the same form the engine recognises (re-parse it with the shared suppression parser).

**Acceptance Scenarios**:

1. **Given** an analysis finding for a rule on a specific line, **When** the user chooses "Suppress on this line", **Then** a line-scoped `-- noqa: RULEID` directive is inserted such that the next analysis pass no longer reports that finding on that line, while the rule still fires elsewhere.
2. **Given** an analysis finding, **When** the user chooses "Suppress globally", **Then** the rule is disabled at the browser-local override level and the change persists across reloads — and the analyser actually honours the override (the latent no-op bug is fixed).
3. **Given** a **line** suppression authored in the browser, **When** the same SQL is analysed by the engine or WPF surface, **Then** the suppression is honoured identically (the inline `-- noqa: RULEID` form is the shared format, not a browser-specific one). Global suppression is browser-local and is not expected to cross surfaces.

---

### User Story 5 — See at a glance whether offline IntelliSense will work (Priority: P2)

A user can tell from the editor's status indicator whether IntelliSense is currently served live from the engine, served from a local cache (engine unreachable but schema cached), running keyword-only (engine unreachable and no cache), or fully disconnected. The indicator distinguishes *cache availability* from *bridge connectivity* so the user knows what to expect before they start typing.

**Why this priority**: PRD §4.3 specifies a four-state badge — "Live", "Cached", "Offline", "Disconnected" — driven by the cache-availability matrix, not just bridge state. Today `StatusBar.razor` surfaces only bridge state (five pills) and cannot say "you are disconnected but cached completions still work." This is a small but high-visibility signal that makes the already-shipped offline IntelliSense legible; it is P2 because the underlying offline behaviour already functions — only the user-facing signal is missing.

**Independent Test**: With a live engine, confirm the indicator reads "Live". Disconnect the engine while a schema is cached for the active database and confirm it reads "Cached" (and completions still resolve). Clear the cache and stay disconnected and confirm it reads "Offline" (keyword-only). Confirm the indicator reflects each transition without a reload.

**Acceptance Scenarios**:

1. **Given** an open bridge, **When** the user views the status indicator, **Then** it shows the live state and indicates engine-backed IntelliSense.
2. **Given** the bridge is down but a cached schema exists for the active database, **When** the user views the indicator, **Then** it shows a cache-backed state distinct from both "live" and "no IntelliSense", and completions resolve from the cache.
3. **Given** the bridge is down and no cached schema exists, **When** the user views the indicator, **Then** it shows an offline/keyword-only state.
4. **Given** the engine restarts or the cache is cleared mid-session, **When** the relevant state changes, **Then** the indicator updates in place without a page reload.

---

### User Story 6 — Prove and audit M5 offline parity against the WPF surface (Priority: P3)

A maintainer can run an end-to-end browser test that drives the offline-IntelliSense acceptance scenarios (the deferred T113) against a real engine — pair, cache, yank the connection, keep getting completions, restart, return to live — and can open a checked-in visual-parity audit comparing the web surface's M5 features (snippet picker, refactoring menu/preview, suppression menu, status indicator) against the WPF surface, with the deltas recorded and the top ones closed.

**Why this priority**: DoD items "Offline IntelliSense works with cable yanked" and "Visual parity audit screenshots" cannot be retired against evidence today. T113 is explicitly deferred. This is P3 because it is verification of features the other stories build — it cannot meaningfully run until US1–US5 land — but it is what converts "we built it" into "we proved it."

**Independent Test**: Run the offline-IntelliSense E2E suite; it builds engine + web from source, pairs, populates the cache, kills the connection, asserts completions still resolve, restarts, and asserts the indicator returns to live. Open the parity audit document and confirm it contains paired screenshots of each M5 feature surface (web vs WPF), a deltas table, and a record of which deltas were closed versus accepted-with-reason.

**Acceptance Scenarios**:

1. **Given** a fresh checkout, **When** the maintainer runs the offline-IntelliSense E2E suite, **Then** it builds both surfaces from current source, drives pair → cache → disconnect → cached-completion → reconnect, and reports pass.
2. **Given** the engine is killed mid-session in the E2E run, **When** the test types after the kill, **Then** completions still resolve from the cache and the status indicator reflects the cached state.
3. **Given** the parity audit document, **When** a reviewer opens it without running the build, **Then** they can see paired web-vs-WPF screenshots for each M5 feature, every recorded delta, and each delta's disposition (closed / accepted-with-reason).
4. **Given** the audit identifies more than the agreed number of closeable deltas, **When** the maintainer ranks them by user impact, **Then** the top ones are closed and the remainder filed as named follow-ups.

---

### Edge Cases

- **Snippet shortcode collides with a schema identifier or keyword** — the completion list must still let the user reach both; snippet items are visually distinguishable from schema/keyword items so an accidental expansion is unlikely.
- **Snippet body with unbalanced or nested placeholders / `$`-escapes** — expansion must not corrupt the document; a malformed body falls back to literal insertion of the body text rather than throwing.
- **Surround-with invoked with no selection** — the operation either no-ops with a hint or inserts the snippet at the caret (defined, not a crash).
- **Imported `.akmlsnippet` whose shortcode already exists** — the import either renames or prompts; it never silently overwrites a built-in, and never produces two snippets that both claim the same shortcode without a defined precedence.
- **Lightweight refactoring on unparseable SQL** — the operation reports "could not parse / not applicable" and leaves the document unchanged (mirrors the engine's behaviour where the parser fails).
- **Lightweight refactoring on a 10 MB document** — honours the same per-document size ceiling the editor already enforces; warns near the limit rather than freezing the tab.
- **Heavyweight invoked while offline** — the three heavyweight operations are shown gated (with the capability notice) whenever the engine is unreachable, even if a schema is cached; they never silently disappear and never attempt a stale-cache execution (cached-schema heavyweight is descoped).
- **Suppressing a rule that is already suppressed globally** — adding a line suppression for a rule already disabled by a global override is a no-op (or a hint), not a duplicate directive that confuses later reads.
- **Suppress-globally when no override record exists yet** — the browser-local override record is created on first global suppression rather than failing.
- **Status indicator during the reconnect window** — while the bridge is mid-reconnect with a cache present, the indicator must not flicker between "cached" and "live"; it shows a stable cached/reconnecting state until the handshake completes.
- **Built-in snippets after a "Clear all" of the schema cache** — clearing the schema cache must not remove built-in snippets (they are bundled, not cached); personal snippets live in their own store and are only removed by an explicit snippet delete.

## Requirements *(mandatory)*

### Functional Requirements

#### Snippet library (US1)

- **FR-001**: The web edition MUST ship a curated built-in snippet set embedded in the bundle (present with no engine and no network), and these built-ins MUST be immutable (save/delete refused) — extending the existing `builtin.*`-prefixed model in `ISnippetStore`.
- **FR-002**: The editor MUST expand a snippet when the user types its shortcode and accepts the corresponding completion, inserting the snippet body with placeholder variables rendered as editable tab-stops and the caret at the first tab-stop; tabbing advances through stops.
- **FR-003**: The editor MUST provide a surround-with action (keyboard chord) that wraps the current selection using a surround-capable snippet chosen by the user, preserving the selected text between the snippet's leading and trailing segments.
- **FR-004**: The web edition MUST provide a snippet management surface where the user can list (built-ins then personal), create, edit, and delete **personal** snippets, with changes persisted locally and surviving a browser reload.
- **FR-005**: The web edition MUST let the user import a `.akmlsnippet` file from disk (validated, persisted as a personal snippet, made expandable) and export a personal snippet to a `.akmlsnippet` download; a malformed import MUST be rejected without corrupting the existing library.
- **FR-006**: Snippet JSON the browser writes/exports MUST use the shared snippet shape so a snippet exported from the browser loads in the engine/WPF surface and vice-versa (round-trip stable for the fields both surfaces support).
- **FR-007**: When the bridge is open and the engine advertises the snippet-write capability, personal snippet save/delete MAY propagate to the engine (best-effort, as the existing data path already does); the local store remains the source of truth and bridge failure MUST NOT lose the local change.

#### Offline lightweight refactoring (US2)

- **FR-008**: The web edition MUST expose all ten of the engine's lightweight refactoring operations (`ExpandInsertColumns`, `ExpandUpdateColumns`, `ConvertOldStyleJoins`, `EncapsulateBeginEnd`, `RemoveSemicolons`, `ReplaceDeprecatedSyntax`, `ExpandExecParameters`, `ConvertSpExecutesql`, `AddGroupByColumns`, `Unformat`) and MUST run them entirely in the browser with no engine round-trip.
- **FR-009**: Each lightweight operation invoked in the browser MUST produce output identical to the engine's output for the same input (behavioural parity), achieved by both surfaces executing the same operation code rather than a re-implementation.
- **FR-010**: The web edition MUST present a refactoring menu (from the editor / selection context) listing the available operations; operations inapplicable to the current selection MAY be shown disabled with a reason, but the menu MUST NOT be empty when offline.
- **FR-011**: The web edition MUST show a before/after preview for a chosen operation prior to applying it, including a defined "no change / not applicable" state.
- **FR-012**: Applying a lightweight operation MUST replace the editor content with the transformed text as a single undoable edit, and MUST respect the existing per-document size ceiling.
- **FR-013**: Relocating the lightweight operations so both surfaces share one implementation MUST NOT regress the engine: the existing engine refactoring test suite MUST remain green, and the relocated code MUST stay free of native / SqlClient / file-IO dependencies so it loads under WASM.

#### Heavyweight refactoring (US3)

- **FR-014**: The web edition MUST surface the three PRD-named heavyweight refactorings — Smart Rename (engine `SafeRename`), Parameterize Values (`ParameterizeValues`), Extract Procedure (`ExtractToProc`) — with a preview of the change and an apply action, using the existing bridge preview/apply path.
- **FR-015**: A heavyweight operation MUST be available when a live engine advertises the `refactoring.heavy` capability (bridge `Open`). Running heavyweight operations against a cached schema while the engine is offline is **out of scope** for this round (see the Overview reconciliation note and Out of Scope); it requires a `SchemaPhasePayload`→`DatabaseCache` rehydrator that does not exist.
- **FR-016**: When a heavyweight operation surfaces a conflict (e.g. a rename collision or an ambiguous target), the preview MUST present the conflict and let the user resolve or cancel before applying.
- **FR-017**: When the engine is not reachable or does not advertise the capability (**including when a schema is cached**), the three heavyweight operations MUST be shown gated with an inline notice explaining that a paired engine advertising `refactoring.heavy` is required — they MUST NOT be silently absent (reuse the existing capability-notice pattern).

#### Inline suppression editing (US4)

- **FR-018**: From an analysis finding (in the problems list and/or at the editor location), the user MUST be able to choose to suppress the rule on this line or globally. (File-scope-per-rule is out of scope — no such directive exists in the shared format.)
- **FR-019**: "Suppress on this line" MUST insert a line-scoped `-- noqa: RULEID` directive (the exact form `SuppressionParser` honours and `FixAction` emits) such that the next analysis pass no longer reports that finding on that line while the rule still fires elsewhere.
- **FR-020**: "Suppress globally" MUST disable the rule via a browser-local per-rule override (`WebAnalysisSettings.RuleOverrides` set to "off"), persisted to IndexedDB and surviving a reload; the override record MUST be created on first use if absent.
- **FR-021**: The analyser MUST honour the browser-local per-rule overrides — `AnalyserService` MUST read `IAnalysisSettingsStore` and project `RuleOverrides` onto the analysis pass (a rule set to "off" is suppressed; others map to their severity). This fixes the current latent no-op where overrides are written but ignored; "Suppress globally" depends on it.
- **FR-022**: A **line** suppression the browser authors MUST use the shared `-- noqa: RULEID` inline form so it is recognised identically by the engine and WPF surface. Global suppression is explicitly browser-local (the web edition does not read project `.casettings`) and is not required to cross surfaces.

#### Cache-aware status indicator (US5)

- **FR-023**: The editor status indicator MUST distinguish at least four user-facing states: engine-backed (live), cache-backed (engine unreachable, schema cached for the active database), offline/keyword-only (engine unreachable, no cache), and disconnected — derived from both bridge state and cache availability, not bridge state alone.
- **FR-024**: The indicator MUST update in place (no reload) as bridge state and cache availability change, and MUST present a stable state during the reconnect window rather than flickering between cached and live.

#### Verification & audit (US6)

- **FR-025**: An offline-IntelliSense end-to-end test (the deferred T113 scenarios) MUST exist that builds engine + web from current source, pairs, populates the cache, disconnects, asserts cached completions still resolve, reconnects, and asserts the indicator returns to live; it MUST be opt-in / excluded from the default test run (matching the established `BridgeE2E` / `SpikeGenerator` trait pattern).
- **FR-026**: A visual-parity audit document MUST exist comparing each M5 feature surface (snippet picker/expansion, refactoring menu/preview, suppression menu, status indicator) on the web edition against the WPF surface, with paired screenshots, a deltas table, and per-delta dispositions (closed / accepted-with-reason); the highest-impact deltas MUST be closed and the remainder filed as named follow-ups.
- **FR-027**: After this spec lands, every M5 PRD §11 Definition-of-Done checkbox MUST be closeable against either an already-shipped feature (per the reality table) or one of FR-001 … FR-026.

### Key Entities

- **Built-in snippet set**: the curated, immutable snippets embedded in the web bundle; identified by the `builtin.*` id prefix; the in-repo source of record defined by this spec (no pre-existing engine-shipped file set).
- **Personal snippet**: a user-authored snippet persisted in browser-local storage; CRUD-able; round-trips to `.akmlsnippet` via import/export.
- **Lightweight refactoring operation**: a parser-only (no schema) text transformation; one of the engine's ten; shared between engine and browser so the result is identical.
- **Heavyweight refactoring operation**: a schema-aware transformation (Smart Rename, Parameterize Values, Extract Procedure) that runs on the engine and is invoked from the browser **via the bridge** (live engine + `refactoring.heavy`).
- **Refactoring preview**: the before/after (and conflict, for heavyweight) representation shown before the user commits a refactoring.
- **Suppression edit**: a line-scoped inline `-- noqa: RULEID` directive (cross-surface), or a browser-local per-rule override (global, browser-only).
- **IntelliSense availability state**: the user-facing status (live / cached / offline / disconnected) derived from bridge state + cache availability.
- **M5 parity audit document**: the checked-in record of web-vs-WPF screenshots, deltas, and dispositions for the M5 feature surfaces.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A user with no engine paired can expand a built-in snippet, expand a personal snippet they created, surround a selection, and import + export a snippet — all without DevTools, the address bar, or a diagnostic page.
- **SC-002**: A snippet exported from the web edition re-imports to a byte-identical snippet and loads on the engine/WPF surface without warnings (round-trip stable).
- **SC-003**: All ten lightweight refactorings run offline in the browser and produce output identical to the engine's output for the same input across a representative test set.
- **SC-004**: The existing engine refactoring test suite remains green after the lightweight operations are relocated to the shared library (zero regression).
- **SC-005**: The three named heavyweight refactorings work when a live engine advertising `refactoring.heavy` is paired, and are visibly gated (not absent) when the engine is unreachable — including when a schema is cached. The online preview/apply path has end-to-end coverage (it had none before).
- **SC-006**: A **line** suppression made in the browser (`-- noqa: RULEID`) is honoured identically by the engine and WPF surface for the same SQL. A **global** suppression takes effect in the browser (the previously-inert override now applies) and persists across reloads.
- **SC-007**: A user can determine from the status indicator alone whether typing will yield live, cached, or keyword-only IntelliSense, and the indicator tracks engine restart and cache-clear transitions without a reload.
- **SC-008**: With a cached schema and the engine killed mid-session, IntelliSense continues to resolve completions (offline parity holds), demonstrated by the opt-in E2E suite.
- **SC-009**: The visual-parity audit records the web-vs-WPF deltas for every M5 feature surface with a disposition for each, and ≤ 3 deltas remain open (excluding the deferred multi-tab gap).
- **SC-010**: Every M5 PRD Definition-of-Done checkbox is closed against either a shipped feature or a requirement in this spec.

## Assumptions

- **Built-in snippet source**: Because the repo ships no canonical `.akmlsnippet` set (the engine loads built-ins from an installer-placed directory at runtime), this spec **defines** the browser's built-in set fresh as embedded resources, seeded from / mirroring the WPF surface's commonly-shipped snippets where they exist; the exact membership is settled during planning. The existing two synthesised built-ins (`ssf`, `cte`) are the floor, not the ceiling.
- **Heavyweight subset**: M5 surfaces the **three** heavyweight operations the PRD names. The engine has five further heavyweight operations (`ExtractToCte`, `ExtractToDerivedTable`, `EncapsulateAsView`, `ConvertTempTable`, `SplitTable`); because the bridge preview/apply path is operation-key-generic, surfacing them later is a small follow-on, but they are **out of scope** for this round.
- **Lightweight op list**: The authoritative list is the engine's actual ten-operation registry, not the PRD's illustrative list (which mis-classifies `ConvertTempTable` as lightweight and names a non-existent "Add/Remove Square Brackets" op).
- **Offline lightweight refactoring is parser-only**: the lightweight operations depend solely on ScriptDom (`TSql170Parser`) + `RefactoringContext` + `DatabaseCache`, all already WASM-safe and present in `AkmlSql.IntelliSense`, so relocating them is mechanically clean and does not pull native or SqlClient dependencies into the bundle.
- **Single active connection**: per PRD scope, the browser holds one engine connection at a time; the "active database" for cache-gating and the status indicator is the single active session (the existing LRU-last heuristic for the offline path is retained).
- **Suppression format**: for **line** scope the browser writes the same inline `-- noqa: RULEID` directive the engine's `SuppressionParser` already parses and the WPF `FixAction` already emits — no browser-specific dialect. **Global** scope is a browser-local per-rule override (IndexedDB), deliberately not the engine's project `.casettings` form (the web edition does not read `.casettings`); it is therefore per-surface by design, not cross-surface.
- **E2E suite runs developer-side** (not CI), matching the established constraint for the bridge/parity suites in specs 024/025.
- **Parity audit needs an interactive workstation** running both the web edition and the WPF surface at the same OS theme — same constraint as the M2 theme-parity audit.

## Dependencies

- **Spec 021 Phase 6 (T100–T120, merged)** — `AkmlSql.IntelliSense` extraction, schema cache store, sync, LRU evictor, offline completion/quick-info/signature, schema-cache settings page, snippet store, refactoring service shell. This spec builds the UI/feature surfaces on top of that substrate and does not re-touch the shipped service code except to extend it (snippet built-ins, refactoring operations).
- **Spec 025 (M3 bridge closure, merged)** — reconnect, status-bar countdown, capability-notice pattern, schema tree. US5's indicator extends `StatusBar.razor`; US3 reuses `CapabilityNotice.razor`.
- **`AkmlSql.Engine.Refactoring`** — source of the ten lightweight operations relocated for US2 and the three heavyweight operations US3 invokes over the bridge; the engine continues to consume them after relocation.
- **`AkmlSql.Analysis` suppression model** (`SuppressionParser`, `SuppressionMap`, `CaSettingsLoader`) — the shared suppression format US4 writes against.
- **`AkmlSql.IntelliSense` `SnippetProvider`** — the completion-side snippet surfacing US1's expansion builds on.
- **Playwright .NET stack** — already wired into `tests/AkmlSql.Web.E2E.Tests/` per spec 024; reused for US6.

## Out of Scope (deferred follow-ups)

- **Heavyweight refactoring against a cached schema while the engine is offline** (original FR-015) — requires relocating the heavyweight ops + `ReferenceCollector` into the shared library **and** building a `SchemaPhasePayload`→`DatabaseCache` rehydrator (a permanent second deserialization path). Descoped per research.md Decision 3; becomes cheap if a rehydrator is ever needed for another reason.
- **File-scope-per-rule suppression** (a new shared `-- noqa-file: RULEID` directive) — would touch the `AkmlSql.Analysis` parser, its engine tests, and the WPF emit/read paths. Descoped per research.md Decision 4.
- **The five non-PRD heavyweight refactorings** (`ExtractToCte`, `ExtractToDerivedTable`, `EncapsulateAsView`, `ConvertTempTable`, `SplitTable`) in the browser UI — cheap follow-on given the generic bridge path, but not this round.
- **Multi-tab editor, multi-connection, tab colouring** — PRD §9 explicitly defers these to separate specs.
- **AI features** — M6 (spec 021 Phase 7 / the M6 closure).
- **Engine-resident snippet sync** — snippets stay independent per surface; only manual import/export bridges them (PRD §10 open question 3).
- **Schema diff / compare** — separate roadmap phase.
- **Git integration in the browser** — out of scope for the web track entirely.
- **Full security-gate variant of any capability notice** — the inline non-blocking notice is the chosen pattern (consistent with spec 025).
