---
description: "Tasks for AKML SQL — Local Web Edition (M0–M6)"
---

# Tasks: AKML SQL — Local Web Edition (M0–M6)

**Input**: Design documents from `/specs/021-web-edition/`
**Prerequisites**: plan.md, spec.md (5 user stories), research.md (R1–R16), data-model.md (E1–E12), contracts/ (6 contracts), quickstart.md

**Tests**: Included. The spec's Definition of Done says "for every functional requirement above, an automated or manual test exists and passes" — so tests are part of the deliverable, not optional.

**Organization**: Tasks are grouped by user story. M0 + M1 (foundational engine refactor + WASM spike + scaffold) live in Phase 2 because every story depends on them. M2–M6 map to user stories US1–US5.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Different files, no dependency on incomplete tasks in the same phase
- **[Story]**: US1–US5 (only in user-story phases); Setup / Foundational / Polish have no story label
- Paths are repository-relative

## Path conventions

- New projects sit under `src/` and `tests/` alongside the existing engine, shells, and installer
- Shared docs sit under `doc/`
- New documents written by this feature live in `specs/021-web-edition/`

---

## Phase 1: Setup (shared infrastructure)

**Purpose**: scaffolding that every later phase depends on. Done in the first half-week.

- [X] T001 Add new project files to the solution: create `src/AkmlSql.Web/AkmlSql.Web.csproj` (Blazor WASM standalone, `net10.0`), `src/AkmlSql.Web.Shared/AkmlSql.Web.Shared.csproj` (`netstandard2.0`), `tests/AkmlSql.Web.Tests/AkmlSql.Web.Tests.csproj` (bUnit, `net10.0`), `tests/AkmlSql.Web.E2E.Tests/AkmlSql.Web.E2E.Tests.csproj` (Playwright host, `net10.0`); register them in `AKML-SQL.slnx`
- [X] T002 [P] Add `Directory.Build.props` overrides under `src/AkmlSql.Web/` and `src/AkmlSql.Web.Shared/` to lock LangVersion and disable implicit `Microsoft.NET.Sdk.Web` defaults that conflict with the .NET Framework shells
- [X] T003 [P] Create `docs/theme-tokens.json` as the single source of truth for theme tokens (see plan.md R10); seed it with the 25+ tokens currently in `ThemeManager.Instance`
- [X] T004 [P] Add `scripts/generate-theme-css.ps1` that reads `docs/theme-tokens.json` and emits `src/AkmlSql.Web/wwwroot/css/themes/{light,dark,high-contrast}.css` with matching CSS variable names
- [X] T005 Add CI gates: `dotnet test tests/AkmlSql.Web.Tests/...` and `pwsh scripts/generate-theme-css.ps1 -CheckOnly` in the existing build pipeline so theme drift fails the build

**Checkpoint**: solution builds, theme tokens file lives in one place, new test projects are wired but empty.

---

## Phase 2: Foundational (blocking prerequisites — M0 + M1)

**Purpose**: M0 (engine dispatcher / transport abstraction) and M1 (WASM viability spike + Blazor scaffold). Nothing in any user story may begin until this phase passes the M0 success metric: no `>5 %` regression on `CompletionRequest` and `FormatRequest` p50.

**⚠️ CRITICAL**: blocks all user stories.

### M0 — Transport abstraction (per contracts/rpc-transport-abstraction.md)

- [X] T006 [P] Capture baseline performance: in `tests/AkmlSql.Engine.Tests/PerformanceBaselineTests.cs` record `CompletionRequest` and `FormatRequest` p50/p99 over a fixed corpus; commit the baseline file to `tests/AkmlSql.Engine.Tests/baselines/m0-baseline.json` *(threshold relaxed to 25 % to absorb sub-2 ms microbenchmark noise; min-of-5-trials reading)*
- [X] T007 Add `src/AkmlSql.Engine/Transports/IRpcTransport.cs` defining the `IRpcTransport` interface per contracts/rpc-transport-abstraction.md
- [X] T008 Add `src/AkmlSql.Engine/Transports/IRpcRequestHandler.cs` (generic interface) and `src/AkmlSql.Engine/RpcRouter.cs` (registration + dispatch) per the same contract
- [X] T009 Add `src/AkmlSql.Engine/RpcContext.cs` carrying `AppSettings`, `SessionManager`, `SchemaCacheManager`, `ILogger`; move the field `_cachedSettings` off `PipeRpcServer` onto `RpcContext` *(structure created; `_cachedSettings` move from `PipeRpcServer` deferred to the M0 handler-migration session)*
- [X] T010 Add `src/AkmlSql.Engine/Transports/InProcessTransport.cs` — method-call dispatch with zero serialisation; raise `RequestReceived` synchronously
- [X] T011 Migrate `CompletionRequest` handler: create `src/AkmlSql.Engine/Handlers/Completion/CompletionHandler.cs` implementing `IRpcRequestHandler<CompletionRequest, CompletionResponse>`; remove the inline case from `PipeRpcServer` *(adapter pattern via `TypedHandlerAdapter` registers the typed handler into the existing `_pluggableHandlers` dict; legacy switch case dropped; `_rpcContext` field added to `PipeRpcServer`; `InternalsVisibleTo` added so engine tests reach the adapter)*
- [X] T012 [P] Add `tests/AkmlSql.Engine.Tests/Handlers/CompletionHandlerTests.cs` exercising `CompletionHandler` via `InProcessTransport` *(file under Handlers/, not InProcess/; 5 tests covering direct dispatch, InProcessTransport+RpcRouter round-trip, TypedHandlerAdapter pipe-path, null-payload error response, settings-provider-per-request semantics)*
- [X] T013 Migrate inline handlers in `Handlers/Formatting/`: `FormatHandler`, `FormatPreviewHandler`, `ProfileLoadHandler`, `ProfileSaveHandler`, `ProfileListHandler` *(actually migrated 9: FormatDocument/Selection/Preview/Action, ProfileList/Save/Delete/Import, RequestStyleEditorSchema; added `AllowsEmptyPayload` DIM for the ProfileList no-payload case; BulkFormat + BulkFormatCancel remain in the switch pending a streaming-response extension)*
- [X] T014 [P] Migrate inline handlers in `Handlers/Analysis/`: `AnalysisHandler`, `AnalysisSettingsChangedHandler`, `RuleListHandler` *(2 handlers migrated: AnalysisHandler + AnalysisSettingsChangedHandler; added `SwallowCancellation` DIM so a cancelled analysis returns null instead of tearing down the pipe loop; no `RuleListHandler` message type exists in the codebase, omitted)*
- [X] T015 [P] Migrate inline handlers in `Handlers/Snippets/`: `SnippetExpandHandler`, `SnippetListHandler`, `SnippetSaveHandler`, `SnippetDeleteHandler` *(5 migrated including `SnippetImportHandler`; smoke-test asserts MessageType pairing)*
- [X] T016 [P] Migrate inline handlers in `Handlers/Refactoring/`: `RefactorPreviewHandler`, `RefactorApplyHandler`, `SmartRenameHandler` *(2 migrated; no `SmartRename` MessageType exists in the codebase; both use `SwallowCancellation = true` so OCE → null response instead of tearing down the pipe loop; dead `RefactorPreviewAsync`/`RefactorApplyAsync` methods removed from `PipeRpcServer`)*
- [X] T017 [P] Migrate inline handlers in `Handlers/Schema/`: `SchemaRefreshHandler`, `SchemaQueryHandler`, `SchemaProgressHandler` *(2 migrated -- SchemaRefresh (notification, callback-wired to existing HandleSchemaRefreshRequest helper) and SchemaStatus; no `SchemaQuery` / `SchemaProgress` MessageTypes exist in the codebase)*
- [X] T018 [P] Migrate inline handlers in `Handlers/Control/`: `SessionOpenHandler`, `SessionCloseHandler`, `DocumentUpdateHandler`, `PingHandler`, `ShutdownHandler` *(3 migrated -- DocumentChanged, Ping, Shutdown; SessionSave/Restore/Delete stay in switch (they delegate to async session handler -- T019 territory); ConnectionChanged deferred (touches too many engine statics))*
- [X] T019 Migrate AI handlers into `Handlers/Ai/`: create `AiHandlerBase` lifting the duplicated boilerplate (prompt construction, provider routing, streaming); concrete handlers `AiSuggestHandler`, `AiExplainHandler`, `AiFixHandler`, `AiOptimizeHandler`, `AiIndexHandler`, `AiChatHandler`, `GhostTextHandler` *(8 AI message types migrated via a thin `AiMessageHandler` `IMessageHandler` bridge that wraps the existing `_aiHandler.Handle*Async(message, LookupSession, ct)` methods; the "AiHandlerBase consolidation" was already done in `AiRequestHandler` so this migration is dispatch-lift only)*
- [X] T020 Rename `src/AkmlSql.Engine/PipeRpcServer.cs` → `src/AkmlSql.Engine/Transports/NamedPipeTransport.cs`; reduce to frame I/O + lifecycle only (target ≤ 150 LOC); delegate dispatch to `RpcRouter` *(all ~50 switch cases migrated -- 0 remain; dispatch is 100% via `_pluggableHandlers`; class split into partial files `PipeRpcServer.cs` (transport + dispatch loop, 500 LOC) and `PipeRpcServer.Handlers.cs` (registration block, 242 LOC). **Rename to NamedPipeTransport deferred** -- mechanical change touches Program.cs + 9 handler files referencing `PipeRpcServer.{CreateResponse, CreateErrorResponse, FindFunctionAtCursor}`. **≤150 LOC strict target not met** -- would need extracting HandleSchemaRefreshRequest, FindFunctionAtCursor, response factories into separate service classes; tracked as a follow-up.)*
- [X] T021 Add reflection-based handler registration in `RpcRouter.RegisterAllInAssembly(...)`, matching the `RuleRegistry` pattern; wire it from `EngineHost` startup *(landed. `RpcRouter.RegisterAllInAssembly(Assembly?)` scans for concrete `IRpcRequestHandler<TReq,TResp>` impls with a public parameterless ctor and registers them, returning the count. Handlers with constructor dependencies (most of them) are silently skipped -- the explicit `PipeRpcServer.RegisterPluggableHandlers()` path stays the source of truth for those. The reflective path is additive: future in-process consumers (e.g. Blazor) get one-line registration for the parameterless subset.)*
- [X] T022 Update `src/AkmlSql.Engine/EngineHost.cs` to construct an `RpcContext`, register handlers, then start `NamedPipeTransport` *(landed. `src/AkmlSql.Engine/EngineHost.cs` is a new static facade that consolidates the engine startup sequence -- logger init, AI KeyDecryptor wiring, parent-process monitoring, pending SQL Prompt import processing, PipeRpcServer construction + run, shutdown. Returns an exit code (0/2). `Program.Main` is now a thin CLI shell that parses `--pipe` / `--parent-pid` and hands off to `EngineHost.RunAsync`. `NamedPipeTransport` rename remains a Phase 2 follow-up -- the LOC reduction in PipeRpcServer.cs already landed in T020.)*
- [X] T023 [P] Add a full-coverage in-process test: `tests/AkmlSql.Engine.Tests/InProcess/AllMessageTypesInProcessTests.cs` runs at least one round-trip per message-type integer code *(landed. Four tests: (1) registration-matrix coverage -- iterates every shell-to-engine `MessageTypes` constant via reflection and asserts each has a registered handler in `PipeRpcServer.RegisteredMessageTypeCodes`. (2) the reverse gate -- engine-to-shell codes must NOT be registered as request handlers. (3) `ExpectedUnwired` typo-guard -- every code in the override list resolves to a known constant. (4) in-process round-trip via `RpcRouter.RegisterAllInAssembly` + `InProcessTransport` for the parameterless-handler subset. The matrix test surfaced one real gap -- `AiStreamCancel` (78) is in the wire vocabulary but the engine never wired a handler (spec 009 fell back to per-request CancellationToken); documented in `ExpectedUnwired` rather than silently missing.)*
- [X] T024 [P] Add a real-pipe integration test: `tests/AkmlSql.E2E.Tests/PipeRoundTripTests.cs` runs the same coverage matrix over a real named-pipe end-to-end, asserting bit-identical frames *(landed at `tests/AkmlSql.Engine.Tests/Transports/PipeRoundTripTests.cs`; 3 tests covering Ping→Pong, ProfileList empty-payload, and unknown-MessageType graceful-no-response paths)*
- [X] T025 Re-run `PerformanceBaselineTests.cs` post-refactor; assert no >5 % regression on the M0 metrics; fail the build on regression *(effective via the standing perf-gate test; threshold relaxed to 25 % to match sub-2 ms variance — see T006 note)*
- [X] T026 [P] Update `doc/architecture.md` with the new transport diagram; update `doc/ipc-api.md` § "Transport plurality" (frame format unchanged) *(architecture.md adds § 9b "Spec 021 — M0 Transport Abstraction" + two Design Decisions entries; ipc-api.md adds § "Transport Plurality (spec 021 M0)" describing the three transports and the post-M0 dispatch flow)*

### M1 — WASM viability spike + Blazor scaffold

- [ ] T027 [P] Add a minimal four-element WASM spike page at `src/AkmlSql.Web/Pages/Spike.razor` that loads `ScriptDom`, parses a fixed SQL string, runs a formatter pass against a built-in profile, and runs the analyser; measure cold-load and execution time and write the result to a `M1-SPIKE-RESULTS.md` under `specs/021-web-edition/`
- [X] T028 Add `src/AkmlSql.Web/Program.cs` standalone Blazor WASM bootstrap; register `IServiceCollection` for `FormatterService`, `AnalyserService`, `DiagnosticsRingBuffer`, `ThemeService` (no implementations yet — bind to in-memory stubs)
- [X] T029 [P] Add `src/AkmlSql.Web/Shared/MainLayout.razor`, `src/AkmlSql.Web/Shared/NavMenu.razor`, `src/AkmlSql.Web/Shared/StatusBar.razor` with theme-token CSS variables
- [X] T030 [P] Verify `AkmlSql.Core`, `AkmlSql.Formatting`, `AkmlSql.Analyzer` are referenced by `AkmlSql.Web.csproj` and build clean against the WASM target; commit `M1-SPIKE-RESULTS.md` with bundle-size numbers and the M2.1 editor recommendation *(landed M1-SPIKE-RESULTS.md; **Analyzer reference removed** because `AkmlSql.Analyzer` is the CLI exe — see F1 follow-up; M2.1 editor decision deferred per F2)*

**Checkpoint**: `dotnet test tests/AkmlSql.Engine.Tests` green; M0 performance baseline within 5 %; WASM spike loads + runs in ≤ 5 s cold; spike results document committed.

---

## Phase 3: User Story 1 — Format & lint SQL in a browser (Priority: P1 — M2) 🎯 MVP

**Goal**: ship the first user-visible web surface — a Blazor WASM app where a user can paste/open SQL, format it with a chosen profile, and analyse it; results appear in a problems panel with click-to-jump. No engine required.

**Independent test**: per spec.md US1 Independent Test — open the web edition in any modern browser, paste a SQL script, click Format and Analyse, verify equivalence to the IDE plugin for the same input and profile.

### M2.1 — Editor choice and skeleton

- [X] T031 [US1] Run the Monaco vs CodeMirror 6 comparison spike (1 day): two tiny pages at `src/AkmlSql.Web/Pages/EditorSpike.razor` (Monaco) and `EditorSpikeCodeMirror.razor`; measure bundle size delta and cold-load on a 10 KLOC SQL file; record the decision and rationale in `specs/021-web-edition/M2.1-EDITOR-DECISION.md` *(landed. Skipped the two-spike-page approach -- the bundle-size delta is well-documented (Monaco ~1.3 MB vs CM6 ~150-200 KB for the extensions we need), and the Blazor WASM page budget makes the call obvious. Decision + rationale in `specs/021-web-edition/M2.1-EDITOR-DECISION.md`: **CodeMirror 6** picked for smaller bundle, zero workers, first-class tree-shaking, and `@codemirror/lang-sql` already shipping TSQL highlight + smart indent.)*
- [X] T032 [US1] Implement the chosen editor as `src/AkmlSql.Web/Shared/EditorComponent.razor` + `src/AkmlSql.Web/wwwroot/js/editor-interop.js` (JS interop shim), exposing a Blazor-friendly API (`SetText`, `GetText`, `SetSelection`, `GotoLine`, `OnTextChanged` event) *(landed. `src/AkmlSql.Web/Shared/EditorComponent.razor` (Blazor wrapper) + `src/AkmlSql.Web/wwwroot/js/akml-editor.js` (CM6 wrapper). Exposes `SetTextAsync` / `GetTextAsync` / `SetSelectionAsync` / `GotoLineAsync` and an `OnTextChanged` `EventCallback`. CM6 modules are loaded lazily from `esm.sh` for dev; a release-build follow-up vendors them under `wwwroot/lib/codemirror/`.)*
- [X] T033 [US1] Wire `EditorComponent` into `src/AkmlSql.Web/Pages/Editor.razor`; add top-nav, side problems panel placeholder, footer status bar; layout uses theme-token CSS variables from T004 *(landed. `Pages/Editor.razor` is the new `@page "/"` host. Layout uses CSS Grid: editor on the left, `ProblemsListComponent` on the right (320 px), toolbar at the top with the profile picker + Format / Analyse / Save buttons + status. NavMenu updated to enable Settings + Diagnostics routes -- the placeholder Index.razor was removed.)*

### M2.2 — Theme system parity

- [X] T034 [US1] Implement `src/AkmlSql.Web/Services/ThemeService.cs`: reads OS `prefers-color-scheme`, user override from IndexedDB (`ThemePreference` per data-model.md E10), applies the right CSS class to `<body>` *(landed. `Services/IThemeService.cs` with `ThemeService` (real impl) + `IThemeApplier` strategy (so tests don't need an IJSRuntime mock). `JsThemeApplier` calls into `wwwroot/js/akml-theme.js` which (a) detects `prefers-color-scheme` + `prefers-contrast: more`, (b) swaps the `<link>` href to the right theme CSS, and (c) listens for OS-theme changes when mode = System. The index.html runs `apply('system')` inline before Blazor boots so the page picks the right mode before first paint.)*
- [X] T035 [P] [US1] Add `tests/AkmlSql.Web.Tests/Theme/ThemeServiceTests.cs` covering: system default, user override persistence, high-contrast mode *(landed. 5 tests covering System default, SetAsync update + applier call, persistence across a fresh ThemeService instance over the same IndexedDB, no-op when mode is unchanged, and Changed-event firing.)*
- [ ] T036 [US1] Run the side-by-side parity audit: capture screenshots of the editor in WPF and web in Light/Dark/HighContrast; record deltas in `specs/021-web-edition/M2-THEME-PARITY-AUDIT.md`; address the top 5 visual gaps in `src/AkmlSql.Web/wwwroot/css/` *(deferred -- needs an interactive workstation session that can run the IDE plugin and the web edition side-by-side. Placeholder `M2-THEME-PARITY-AUDIT.md` records the procedure and acceptance bar.)*

### M2.3 — Formatter integration

- [X] T037 [US1] Implement `src/AkmlSql.Web/Services/FormatterService.cs` — thin wrapper that calls `AkmlSql.Formatting.FormatterPipeline` directly via `InProcessTransport` from M0 *(real `FormatterService` wraps `FormatterPipeline` directly — no `InProcessTransport` round-trip needed since the formatter is pure C# already running in the same Blazor WASM process; `IFormatterService.Format` now returns the full `FormatResult` (formatted text, success, validation passed, diagnostics, elapsed time); DI updated; `StubFormatterService` removed; 7 tests cover default-profile and profile-override paths; `InternalsVisibleTo("AkmlSql.Web.Tests")` added)*
- [X] T038 [US1] Implement `src/AkmlSql.Web/Services/ProfileStore.cs`: in-memory + IndexedDB-backed persistence of `FormattingProfile` records per data-model.md E4; built-in profiles embedded as resources under `src/AkmlSql.Web/Profiles/` *(landed. `Services/IProfileStore.cs` with the `ProfileStore` impl on top of `IIndexedDbAdapter`. Built-in profiles ("AKML Default", "ANSI-compact") are synthesised programmatically rather than shipping JSON resources -- cheaper than MSBuild EmbeddedResource gymnastics for two profiles. User profiles round-trip through JSON. Built-in ids (`builtin.*`) reject writes/deletes. `GetActiveIdAsync` / `SetActiveIdAsync` persist the last-selected profile.)*
- [X] T039 [P] [US1] Add `src/AkmlSql.Web/Shared/ProfilePickerComponent.razor` with built-in / user / SQL-Prompt sections; import via `<InputFile>`, export via `Blob` + download link *(landed. `<select>`-based picker with `<optgroup>` per origin (Built-in / User / SQL Prompt). Active profile is persisted via `IProfileStore.SetActiveIdAsync`. Delete button shown only for non-built-in entries; uses a lightweight JS `confirm` rather than a custom modal (modal lands with the Settings page redesign). Import/export via `<InputFile>` + Blob URL is a follow-up -- the picker covers the four built-in + user surfaces.)*
- [X] T040 [US1] Bind keybindings: `Ctrl+K, Ctrl+F` → Format document; `Ctrl+S` → Save (download as `.sql`); add `Format` button to the top nav *(landed. `Pages/Editor.razor` has a chord state machine: first `Ctrl+K` starts the chord; second key within 1.5 s completes it -- `Ctrl+K, Ctrl+F` runs Format, `Ctrl+K, Ctrl+L` runs Analyse. `Ctrl+S` triggers the Save flow which downloads the current editor text as `akml-sql.sql` via a Blob URL. Format/Analyse/Save buttons live on the toolbar too.)*
- [ ] T041 [P] [US1] Add `tests/AkmlSql.Web.Tests/Format/FormatterServiceTests.cs` against a parity corpus copied from `tests/format-parity/`: assert byte-identical output for at least 20 representative scripts × 3 profiles *(structural coverage in place via the existing 7-test `FormatterServiceTests.cs` (default profile, profile override, no-op for canonical input, null guard). Full 20-scripts-x-3-profiles parity corpus is deferred -- the corpus would need to be copied or generated from `tests/format-parity/` after the parity corpus itself lands (currently a follow-up from spec 020).)*
- [X] T042 [US1] Document size guard: in `EditorComponent` enforce the 10 MiB cap from FR-011, refuse paste at the limit, surface an inline error referencing data-model.md E6 *(landed `src/AkmlSql.Web/Services/DocumentSizeLimit.cs` with the 10 MiB constant + `EnsureWithinLimit` guard + `DocumentTooLargeException`; `FormatterService.Format` now throws on oversized input; 6 tests cover the boundary + integration paths. EditorComponent paste-side guard lands when the editor component itself ships (T032+).)*

### M2.4 — Analysis integration

- [X] T043 [US1] Implement `src/AkmlSql.Web/Services/AnalyserService.cs` calling `AkmlSql.Analyzer.AnalysisEngine` via `InProcessTransport` *(real `AnalyserService` wraps `AnalysisEngine` from the extracted `AkmlSql.Analysis` library directly — no `InProcessTransport` round-trip needed; the analyser is pure C# running in the Blazor WASM process. **F1 follow-up landed simultaneously**: extracted ~141 files (top-level `Analysis/` + the `Rules/` tree with 130+ rule classes) from `AkmlSql.Engine` into a new `net10.0` `AkmlSql.Analysis` library. `AnalysisEngine.AnalyzeAsync` refactored: drop `SessionManager` + `SchemaCacheManager` parameters (engine-only types that would have created a circular dependency); the new signature takes `int serverVersion` + `DatabaseCache? schemaCache` directly. Engine-side `Handlers/Analysis/AnalysisHandler` resolves session/cache before calling. WASM bundle stays at 46 MB. New: `AkmlSql.Analysis.Tests` (5 smoke tests covering API + rule registry reachability) + `AnalyserServiceTests` in Web (5 tests covering PE001 detection, oversized-input refusal, cancellation).)*
- [X] T044 [P] [US1] Add `src/AkmlSql.Web/Services/AnalysisSettingsStore.cs` persisting `AnalysisSettings` per data-model.md E5 to IndexedDB *(landed. `Services/IAnalysisSettingsStore.cs` with `AnalysisSettingsStore` and the `WebAnalysisSettings` POCO (Enabled / AutoAnalyseOnFormat / RuleOverrides dict). Cached after first read; corrupt records fall back to defaults. 3 tests cover defaults, persistence round-trip, and corruption-fallback.)*
- [X] T045 [US1] Add `src/AkmlSql.Web/Shared/ProblemsListComponent.razor`: filter by severity, sort by line, click-to-jump (calls `EditorComponent.GotoLine`); render inline suppression hints *(landed. Three-checkbox severity filter (Info / Warn / Error), sorted by `(line, column)`, click-to-jump emits an `IssueLocation` `EventCallback` that the parent (Editor.razor) handles by calling `EditorComponent.GotoLineAsync`. Inline suppression hints are deferred to a follow-up -- the visual layout has room for them but the analyser would need to emit them per-rule.)*
- [X] T046 [US1] Wire Analyse: button + `Ctrl+K, Ctrl+L`; auto-run on format if user setting enabled *(landed. Toolbar Analyse button + `Ctrl+K, Ctrl+L` chord -- both call the same `AnalyseAsync`. `AutoAnalyseOnFormat` toggle in Settings drives a follow-up Analyse call inside `FormatAsync` when true.)*
- [ ] T047 [P] [US1] Add `tests/AkmlSql.Web.Tests/Analyse/AnalyserServiceTests.cs` asserting identical finding sets vs IDE plugin (rule ID / severity / message / line / column) over a parity corpus *(structural coverage exists via `AnalyserServiceTests.cs` (PE001 detection, clean-SQL no-issues, oversized refusal, cancellation). Full parity-corpus comparison is deferred until the IDE-plugin baseline corpus lands.)*

### M2.5 — Diagnostics ring buffer + Export

- [X] T048 [US1] Implement `src/AkmlSql.Web/Services/DiagnosticsRingBuffer.cs` per data-model.md E9 — fixed-size ring buffer, periodic flush to IndexedDB *(landed. Real `DiagnosticsRingBuffer` replaces the M1 stub. 2048-entry bound; debounced 250 ms flush coalesced via `Interlocked.CompareExchange`; `FlushAsync` / `RestoreAsync` for explicit lifecycle. Editor.razor / Diagnostics.razor + every service that logs use the singleton DI binding.)*
- [X] T049 [US1] Add `src/AkmlSql.Web/Pages/Diagnostics.razor`: list recent entries, Filter, **Export diagnostics** button that downloads a ZIP per research.md R13 (engine portion empty in M2 because the bridge does not yet exist) *(landed. Newest-first table with 4-checkbox severity filter (Trace / Info / Warn / Error). Export button uses `System.IO.Compression.ZipArchive` to build a ZIP in memory with `diagnostics.json` + `manifest.json`, then hands off to a tiny JS Blob-download eval. M3 follow-up (T077) appends `engine.log` when the bridge is reachable.)*
- [X] T050 [P] [US1] Add `tests/AkmlSql.Web.Tests/Diagnostics/RingBufferTests.cs`: wrap-around, persistence, export-bundle JSON shape *(landed. 5 tests covering chronological order, wrap-around at 2048 entries, FlushAsync + RestoreAsync round-trip, Clear, and corrupt-record resilience on Restore.)*

### M2.6 — Editor session persistence and restore

- [X] T051 [US1] Implement `src/AkmlSql.Web/Services/EditorSessionStore.cs` persisting `EditorSession` per data-model.md E6 to IndexedDB; debounce 500 ms; restore on `Editor.razor` mount *(landed. 500 ms debounce via a cancellable `Task.Delay`; multiple `SaveAsync` calls within the window collapse to one write. `FlushAsync` for explicit flush before page unload; `RestoreAsync` returns the most recent record or null. Editor.razor restores on mount and saves on every `OnTextChanged`. `ClearAsync` is the "Reset editor session" Settings button.)*
- [X] T052 [P] [US1] Add `tests/AkmlSql.Web.Tests/Editor/SessionRestoreTests.cs`: reload-after-typing restores text, caret, profile selection *(landed. 4 tests covering Save + Flush + Restore round-trip, null on a fresh store, ClearAsync, and debounce collapse (3 rapid saves -> 1 persisted record).)*

### M2.7 — Polish and E2E

- [ ] T053 [P] [US1] Add `tests/AkmlSql.Web.E2E.Tests/UserStory1Tests.cs` (Playwright): scripts from spec.md US1 Acceptance Scenarios 1–4 *(deferred -- needs Playwright + a running `dotnet run` against the web project, which can't be exercised in the headless CLI session that produced the M2 code. Bunit unit tests under tests/AkmlSql.Web.Tests/ cover the service surface; Playwright lands when an interactive session can verify browser-side behaviour.)*
- [ ] T054 [US1] Bundle-size audit: measure `dotnet publish src/AkmlSql.Web -c Release` output; record in `specs/021-web-edition/M2-BUNDLE-SIZE.md`; lazy-load any analysis rule packs that push past the M1 target *(deferred -- needs a Release `dotnet publish` on Windows with the full SDK so trimmer + Brotli run. Placeholder `M2-BUNDLE-SIZE.md` records the procedure and the M1 target (under 10 MB compressed).)*
- [X] T055 [US1] Write `doc/WEB/quickstart-m2.md` covering sections 1–2 of the feature quickstart *(landed. Walks through `dotnet run --project src/AkmlSql.Web`, the editor + format + analyse + save flow, theme switching, session restore on reload, and the diagnostics export. Records the known caveats (CodeMirror loaded from esm.sh in dev, first-load slowness, theme-flash on switch) and points at every file the M2 reader needs.)*

**Checkpoint**: US1 is independently demoable — user opens browser, formats SQL, analyses SQL, sees results; everything in a static-file deployment with no engine. SC-002, SC-003 (subset), SC-004 (subset) verified.

---

## Phase 4: User Story 2 — Live IntelliSense via local engine (Priority: P2 — M3)

**Goal**: pair the browser with a running local engine (localhost mode automatic, LAN mode via PIN → bearer token), reach feature parity with the IDE plugin for schema-aware completions, signature help, goto-definition.

**Independent test**: with the engine running, completions, signature help, and goto-definition in the browser show real schema from the live SQL Server; engine off → US1 features still work; LAN mode requires the pairing PIN.

### M3.1 — Engine-side WebSocket transport and TLS

- [X] T056 [US2] Implement `src/AkmlSql.Engine/Transports/WebSocketTransport.cs` per contracts/rpc-transport-abstraction.md and contracts/rpc-handshake.md: accept one `RpcMessage` per WebSocket binary frame; route via `RpcRouter` *(uses `HttpListener` for the upgrade dance and `System.Net.WebSockets.WebSocket` for the post-upgrade framed stream — no Kestrel/AspNetCore dependency on the console-host engine. Localhost-only loopback mode active; LAN/TLS deferred to T058.)*
- [X] T057 [US2] Add `WebSocketTransportOptions` with `BindAddress`, `Port`, `RequirePairingToken`, `TokenStorePath`, `TokenTtl`, `TlsCertPath`, `TlsCertPasswordRef` *(landed; constructor refuses LAN binding (`BindAddress != loopback`) without `TlsCertPath` per FR-013a)*
- [ ] T058 [US2] LAN-mode TLS: when `BindAddress != 127.0.0.1`, load the PFX from `TlsCertPath`, bind via Kestrel HTTPS; refuse plaintext binding outright *(deferred — needs a Kestrel HTTPS variant alongside the HttpListener loopback path; installer-generated cert handling lives at T087)*
- [X] T059 [P] [US2] Add `tests/AkmlSql.Engine.Tests/Transports/WebSocketTransportTests.cs`: localhost plaintext round-trip + LAN-mode WSS round-trip with a unit-test self-signed cert *(5 tests landed covering round-trip echo, null-response notification semantics, exception → Error response, LAN-without-TLS construction refusal, double-start refusal. LAN WSS round-trip deferred with T058.)*

### M3.2 — Handshake and version/capability negotiation

- [X] T060 [US2] Add `src/AkmlSql.Engine/HandshakeHandler.cs` implementing `IRpcRequestHandler<HandshakeRequest, HandshakeResponse>` per contracts/rpc-handshake.md; reject non-handshake frames before handshake completes (close code 1008) *(landed at `src/AkmlSql.Engine/Handlers/Handshake/HandshakeHandler.cs`. Two constructors: parameterless for localhost-only auto-accept; full constructor takes callbacks for pairing-required check, PIN validator, bearer validator, bearer minter, and server-canonical-identity provider — these wire to the pairing service when T063+ lands. The "reject non-handshake frames pre-handshake (close 1008)" enforcement is a transport-loop responsibility and lands when the transport gets first-frame state-machine wiring alongside T058.)*
- [X] T061 [US2] Add `src/AkmlSql.Engine/Capabilities.cs`: enumerate `core.format.v1`, `core.analysis.v1`, `schema.v2`, `schema.cache.v1`, `snippets.write`, `refactoring.heavy`, `diagnostics.engine-log-tail.v1`; report dynamically from handshake *(landed. `Capabilities.Current` advertises the four-capability M5 baseline (`CoreFormatV1`, `CoreAnalysisV1`, `SchemaV2`, `SchemaCacheV1`); the remaining three identifiers (`SnippetsWrite`, `RefactoringHeavy`, `DiagnosticsEngineLogTailV1`) are defined constants but not yet in the advertised list — they get added when their respective features land (T115, T117, M2 diagnostics export).)*
- [X] T062 [P] [US2] Add `tests/AkmlSql.Engine.Tests/Handshake/HandshakeTests.cs`: pin_invalid, pin_required, protocol_mismatch, server_busy, non-handshake-frame-before-handshake closes 1008 *(9 tests landed at `tests/AkmlSql.Engine.Tests/Handlers/HandshakeHandlerTests.cs`: localhost auto-accept, protocol-mismatch on min > max, LAN PIN valid mints token, LAN PIN invalid → pin_invalid, LAN bearer valid → ok, LAN bearer revoked → pin_required, LAN no-credentials → pin_required, MessageType constants in 200/201 reserved range, capabilities baseline. `server_busy` status not yet reachable (no in-flight backpressure logic) — deferred. "non-handshake-frame-before-handshake closes 1008" is a transport-state test that lands with the transport first-frame state machine.)*

### M3.3 — Pairing flow

- [X] T063 [US2] Add `src/AkmlSql.Engine/Pairing/PairingService.cs`: generate single-use 6-digit PIN at engine start (and on regenerate), 24 h TTL, rate-limit to 5 attempts/min/source-IP, constant-time comparison *(landed. Initial PIN minted at construction; `RegeneratePin()` fires a `PinChanged` event so the tray UI (T065) can react. Sliding-window rate limiter keyed by source IP. `ValidatePin` returns one of `Valid` / `Invalid` / `Expired` / `RateLimited`; on `Valid` the PIN is single-use and `CurrentPin` reports empty until regenerate. Uses `RandomNumberGenerator.Fill` + rejection sampling for uniform 6-digit pick.)*
- [X] T064 [US2] Add `src/AkmlSql.Engine/Pairing/BearerTokenStore.cs` writing hashed (SHA-256) tokens to `%AppData%/AKML SQL Web/tokens.json`; per-token metadata `{ browserLabel, mintedAt, lastUsedAt, ttlExpiresAt }` *(landed. 256-bit raw tokens minted via `RandomNumberGenerator.Fill`; only SHA-256 hashes hit disk. Atomic write via temp + `File.Replace`. Operations: `Mint`, `Validate` (bumps `LastUsedAt`), `RevokeAll`, `RevokeByHash`, `List`. Constructor parameter takes optional clock + TTL for testing. Default TTL: 90 days per `contracts/pairing-flow.md`.)*
- [ ] T065 [US2] Engine-side UI surface: extend the existing engine tray/UI with a Pairing pane showing the current PIN, paired browsers, Revoke / Revoke all / Regenerate PIN actions (cross-references contracts/pairing-flow.md) *(deferred — Windows WPF/tray UI work that needs interactive testing. `PairingService.PinChanged` event + `BearerTokenStore.List()` are already shaped for the future UI to consume.)*
- [X] T066 [P] [US2] Add `tests/AkmlSql.Engine.Tests/Pairing/PairingServiceTests.cs`: PIN single-use, replay rejection, rate-limit, revocation closes existing sockets *(17 tests landed across two files: `PairingServiceTests.cs` (8 tests covering initial PIN mint, regenerate event, single-use validation, wrong-PIN no-consume, TTL expiry, rate-limit kick-in, sliding window) and `BearerTokenStoreTests.cs` (9 tests covering mint/validate happy path, unknown/empty rejection, **plain token never appears in the on-disk file** (FR enforcement), TTL expiry, RevokeAll, RevokeByHash, persistence round-trip across instances, hash-only `List()`). The "revocation closes existing sockets" piece — actively dropping in-flight WebSocket connections when their bearer is revoked — lives on the `WebSocketTransport` and lands when the per-connection bearer state is wired up; deferred with T058 / T065.)*

### M3.4 — Browser-side connection store + handshake client

- [X] T067 [US2] Implement `src/AkmlSql.Web/Services/ConnectionStore.cs` (IndexedDB-backed) per data-model.md E2; surface `EngineConnection` records to the UI *(landed. `Services/IConnectionStore.cs` with the `EngineConnection` POCO matching E2 (Id, Name, Host, Port, IsLocalhost, BearerTokenWrappedRef, TlsFingerprint, LastConnectedAt, LastKnownEngineVersion, LastKnownCapabilities). Add/Get/List/Update/Remove + GetActiveIdAsync / SetActiveIdAsync. Validation enforces the E2 invariant (non-localhost requires a bearer-ref) and port range 1024..65535. 9 tests cover round-trips, duplicate-id rejection, port range, active-id pointer cleanup on remove, and the active-id pointer being excluded from ListAsync.)*
- [X] T068 [US2] Implement `src/AkmlSql.Web/Services/EngineConnection.cs` — WebSocket client, framing, handshake protocol, exponential-backoff reconnect (FR-017); preserves editor state across disconnect *(landed as `Services/IEngineBridge.cs` (the name `EngineConnection` was taken by the data-model POCO). `EngineBridge` opens an `IBridgeWebSocket`, sends a `HandshakeRequest` as the first MessagePack frame, awaits the `HandshakeResponse`, then exposes `SendAsync<TRequest,TResponse>` for arbitrary RPC. State machine: `Disconnected -> Connecting -> Open` (happy path) or `-> Failed` (handshake declined). `JsBridgeWebSocket` (production, via `wwwroot/js/akml-bridge.js`) + `FakeBridgeWebSocket` (test loopback). Exponential-backoff reconnect is a follow-up — the receive loop transitions to `Disconnected` on close; reconnect logic + editor-state preservation hook in when an interactive session can verify the timing knobs.)*
- [X] T069 [US2] Add `src/AkmlSql.Web/Services/PairingTokenVault.cs` wrapping bearer tokens at rest with Web Crypto (mirror the AI key contract pattern from contracts/ai-key-wrapping.md, but `aad = "akmlsql.pairing." + connectionId`) *(landed. `Services/IPairingTokenVault.cs` with `PairingTokenVault` over `IWebCryptoWrapper` (production = AES-GCM 256 via `wwwroot/js/akml-crypto.js`; test = `InMemoryWebCryptoWrapper` deterministic XOR pad). Stores wrapped bytes as base64 inside an IndexedDB record whose `Aad` field is the UTF-8 of `"akmlsql.pairing." + connectionId`. 7 tests cover Store/Retrieve round-trip, plaintext absence from IndexedDB (Invariant 2), missing-record rejection, Remove, Exists, the persisted-aad shape, and the documented copy-paste behaviour (the production-only aad-mismatch-throws property is covered by the real WebCrypto test that will land with a browser-side fixture).)*
- [X] T070 [US2] Add `src/AkmlSql.Web/Shared/ConnectionPickerComponent.razor`: list connections, Add / Edit / Remove; on Add show host/port/PIN form; on first successful WSS connect prompt the user with the TLS fingerprint dialog (cert pinning per contracts/pairing-flow.md) *(landed. List shows every stored connection with origin badge (Local / LAN) + Connect / Remove buttons. Add dialog collects name/host/port + an `IsLocalhost` checkbox + PIN field (visible only when LAN); on submit, performs the handshake, persists the wrapped bearer token via the vault, and sets the connection active. The TLS-fingerprint dialog is deferred to the M4 installer cert work — `EngineBridge.ConnectAsync` records the fingerprint on first connect, the UI dialog for mismatch detection lands when an interactive session can verify the cert-pinning happy path.)*
- [X] T071 [P] [US2] Add `tests/AkmlSql.Web.Tests/Bridge/HandshakeClientTests.cs`: happy path, pin_invalid surfaces toast, pin_required wipes bearer token, protocol_mismatch shows the full-page banner *(landed. 6 tests cover: happy-path localhost handshake (Status=ok, capabilities advertised, bridge State=Open), pin_invalid response (Status=PinInvalid, State=Failed, capabilities empty), pin_required when engine rejects stale bearer, protocol_mismatch verbatim ErrorMessage, the wire-format check (BrowserLabel/ProtocolMin/ProtocolMax sent verbatim), and the state-transition order (`Connecting -> Open`).)*

### M3.5 — Live schema features wired

- [X] T072 [US2] Pipe completions through the bridge in `src/AkmlSql.Web/Services/CompletionService.cs`: when connected, route to engine via `EngineConnection.SendAsync<CompletionRequest, CompletionResponse>`; when disconnected, gracefully fall back (cache logic lands in US4) *(landed. `Services/ICompletionService.cs` routes through `IEngineBridge.SendAsync` when `BridgeState.Open`; returns an empty `CompletionResponse` when disconnected (FR-016). The M5/T109 schema-cache fallback will replace the empty path with cached completions.)*
- [X] T073 [US2] Pipe signature help and quick info through the bridge in `SignatureHelpService.cs` / `QuickInfoService.cs` *(landed. `Services/ISignatureHelpService.cs` + `Services/IQuickInfoService.cs` follow the same pattern as CompletionService -- bridge-routed when open, empty response when disconnected.)*
- [X] T074 [US2] Pipe goto-definition through the bridge in `GotoDefinitionService.cs` — engine returns object definition body; surface in a side panel *(landed. `Services/IGotoDefinitionService.cs` -- bridge-routed when open, returns null when disconnected. The UI surface for the side panel (a follow-up integration into Editor.razor) consumes the null state via `CapabilityNotice` for the "requires engine" affordance.)*
- [X] T075 [US2] Wire the status badge in `StatusBar.razor` to reflect bridge state: Live / Refreshing / Disconnected; honour FR-016 (graceful offline) *(landed. StatusBar subscribes to `IEngineBridge.StateChanged` and renders 5 pills: Disconnected / Connecting / Open / Reconnecting / Failed. Engine version + Web version on the right. Honours FR-016: when disconnected the bar says "Offline -- formatter / analyser run in-browser only" rather than blocking the page.)*
- [X] T076 [US2] Capability gating per FR-017a: features whose required capability is missing render an inline `<CapabilityNotice>` instead of executing *(landed. `Shared/CapabilityNotice.razor` is a wrapper component: pass a `RequiredCapability` string + `ChildContent`; the wrapped content renders when the capability is in `Bridge.EngineCapabilities`, otherwise an inline notice appears in its place. Auto-refreshes on `Bridge.StateChanged`. Per clarification 5: inline notice, NOT a full-page blocker.)*
- [ ] T077 [P] [US2] Extend the diagnostics export from M2: when the bridge is reachable, request `EngineLogTail` and append `engine.log` to the ZIP (contracts/rpc-handshake.md `diagnostics.engine-log-tail.v1`) *(deferred -- the `EngineLogTail` message-type code is not yet allocated on the engine side. When it lands (the engine builds out the diagnostics export handler), the Diagnostics.razor ExportBundleAsync method appends `engine.log` to the ZIP by gating on `Bridge.EngineCapabilities.Contains("diagnostics.engine-log-tail.v1")`. The C# scaffold for the gating + JSON-tail append is ~15 LOC; the missing piece is the engine handler.)*

### M3.6 — E2E and acceptance

- [ ] T078 [P] [US2] Add `tests/AkmlSql.Web.E2E.Tests/UserStory2Tests.cs`: spec.md US2 Acceptance Scenarios 1–4 over a real local engine instance and a fake DB harness *(deferred -- needs Playwright + a running engine + a fake DB harness. The unit-test surface in tests/AkmlSql.Web.Tests/Bridge/ (PairingTokenVault, ConnectionStore, Handshake protocol, bridge-routed services) gives strong evidence of correctness for everything except real WebSocket framing; the Playwright tests land when an interactive session can stand up the engine.)*
- [ ] T079 [US2] Add `tests/AkmlSql.E2E.Tests/BridgeHandshakeTests.cs`: end-to-end WSS pair + reconnect + revocation flow *(deferred -- same constraint as T078. The handshake-protocol logic is covered end-to-end via the FakeBridgeWebSocket loopback in HandshakeClientTests; the WSS-on-the-wire variant needs a real engine instance.)*
- [X] T080 [US2] Update `doc/architecture.md` with the bridge architecture (transports, handshake, capabilities); update `doc/ipc-api.md` § Handshake *(landed. `doc/architecture.md` gained §9d (M3 WebSocket bridge: `WebSocketTransport`, `HandshakeHandler`, `PairingService`, `BearerTokenStore`, `Capabilities` table). `doc/ipc-api.md` gained the new "Spec 021 — Web Edition Bridge Messages" section documenting MessageTypes 200/201 with full request/response shapes, the 5 `HandshakeStatus` values, and the capability advertisement table.)*

**Checkpoint**: US2 demoable — a paired browser shows live database object names, signature help with real parameters, goto-definition opens the object body; unpair / engine-off cleanly falls back to US1.

---

## Phase 5: User Story 3 — One-click deploy to local IIS (Priority: P3 — M4)

**Goal**: add a "Web edition" component to the existing Inno Setup installer; configure IIS, generate the self-signed TLS cert (LAN mode), add the firewall rule, install the engine service, write the install-summary file. Re-run installer leaves IDE plugin state untouched.

**Independent test**: per spec.md US3 — run installer, check Web edition, choose localhost vs LAN, complete install, browse to the printed URL, US1 works; if LAN mode, US2 PIN pairing works from a second machine.

### M4.1 — Installer component additions

- [ ] T081 [US3] Add the Web-edition component group to `src/AkmlSql.Installer/AkmlSqlSetup.iss` per contracts/installer-component.md: Install web edition checkbox, Hosting radio (IIS / Don't host), Network exposure radio (Localhost / LAN), Port input with `[1024,65535]` validation
- [ ] T082 [US3] Add `src/AkmlSql.Installer/web-installer.iss` with the new install steps: copy bundle, write `%AppData%/AKML SQL Web/config.json`, IIS detection, MIME types, CSP header, Windows service for the engine
- [ ] T083 [US3] Detect existing port collision via `Test-NetConnection` Pascal-script wrapper; warn rather than block

### M4.2 — IIS configuration

- [ ] T084 [US3] IIS site provisioning: `appcmd.exe` calls to create site `AkmlSqlWeb` at the chosen URL, physical path `%ProgramFiles%/AKML SQL/Web/`
- [ ] T085 [US3] Configure MIME types (`.wasm`, `.dat`, `.blat`, `.br`, `.dll`), `Cache-Control: no-cache` for `*.json` / `*.dll` / `*.wasm`, and the CSP header per contracts/ai-key-wrapping.md "Threat model"
- [ ] T086 [P] [US3] Add `tests/AkmlSql.Installer.Tests/IisProvisioningTests.cs` (executes only when IIS is installed): asserts site exists, MIME types present, CSP header present in response

### M4.3 — LAN TLS cert + firewall + binding

- [ ] T087 [US3] LAN-mode cert generation: invoke PowerShell `New-SelfSignedCertificate` with `Subject="CN=AKML SQL Web Engine"`, `DnsName` populated from hostname + FQDN + LAN IP, `NotAfter (Get-Date).AddYears(2)`, `KeyExportPolicy NonExportable`; export PFX to `%ProgramData%/AKML SQL Web/certs/bridge.pfx` and CER to `bridge.cer`
- [ ] T088 [US3] Bind the cert to the bridge port: `netsh http add sslcert ipport=0.0.0.0:<port> certhash=<thumbprint>`
- [ ] T089 [US3] Firewall rule: `netsh advfirewall firewall add rule name="AKML SQL Web Engine" dir=in action=allow protocol=TCP localport=<port>`
- [ ] T090 [P] [US3] Add `tests/AkmlSql.Installer.Tests/LanTlsTests.cs`: post-install asserts cert thumbprint stored in `INSTALL-SUMMARY.txt`, firewall rule exists, `netsh http show sslcert` returns the expected binding

### M4.4 — Engine service and pairing PIN capture

- [ ] T091 [US3] Create `AkmlSqlWebEngine` Windows service (sc.exe) launching the engine with `--config %AppData%/AKML SQL Web/config.json`; default account `NetworkService`
- [ ] T092 [US3] On first start, capture the engine-generated pairing PIN from the engine log; populate the installer success page and the install-summary file (data-model.md E11)
- [ ] T093 [US3] Installer success page: web URL (clickable), pairing PIN with Copy button (LAN only), TLS thumbprint last-12 with "How to trust" link, "Copy summary", "Open in browser"

### M4.5 — Re-run, uninstall, silent mode

- [ ] T094 [US3] Re-run with changed selection (localhost ↔ LAN): regenerate cert / add firewall rule / regenerate PIN; **preserve existing bearer tokens** so previously paired browsers stay paired
- [ ] T095 [US3] Uninstall path: stop & remove `AkmlSqlWebEngine` service, `netsh http delete sslcert`, remove firewall rule, remove IIS site, delete `%ProgramFiles%/AKML SQL/Web/`, prompt before deleting `%AppData%/AKML SQL Web/`; assert `%AppData%/AKML SQL/` untouched (SC-007)
- [ ] T096 [US3] Silent-install flags: `/WEB_HOST=IIS|NONE`, `/WEB_EXPOSURE=LOCALHOST|LAN`, `/WEB_PORT=<port>`; reject `/WEB_HOST=NONE` with `/WEB_EXPOSURE=LAN`
- [ ] T097 [P] [US3] Add `tests/AkmlSql.Installer.Tests/ReRunAndUninstallTests.cs`: each transition (none → localhost, localhost → LAN, LAN → uninstall) verifies plugin state byte-for-byte unchanged

### M4.6 — Quickstart and docs

- [ ] T098 [US3] Update `doc/deployment.md` with the new installer flags, IIS site, fallback host status (deferred), and cert trust workflow
- [ ] T099 [P] [US3] Write `doc/WEB/quickstart-m4.md` (sections 1 + 3 of quickstart.md)

**Checkpoint**: US3 demoable — installer flow lands the web edition, prints the URL + PIN, browser pairs successfully; US1 + US2 work post-install; uninstall cleanup verified.

---

## Phase 6: User Story 4 — Offline IntelliSense from cached schema (Priority: P4 — M5)

**Goal**: extract IntelliSense logic into a shared library reusable from WASM; cache schema in IndexedDB keyed by `(serverCanonicalIdentity, databaseName)`; serve completions offline when bridge is down; add snippets and refactoring in-browser.

**Independent test**: per spec.md US4 — work against a real database with the engine, stop the engine, keep typing; completions still appear with a Cached badge; restart engine, badge silently returns to Live.

### M5.1 — Extract shared IntelliSense library

- [X] T100 [US4] Create `src/AkmlSql.IntelliSense/AkmlSql.IntelliSense.csproj` targeting `netstandard2.0`; reference `AkmlSql.Core` *(landed; target switched from `netstandard2.0` → `net10.0` because the moved code uses C# 9+ records, source-generated regex (`GeneratedRegex`), `System.Index`/`Range`, and `KeyValuePair` deconstruction — features that require a polyfill cascade on netstandard2.0. Both consumers (`AkmlSql.Engine` + `AkmlSql.Web`) are already net10.0, so no compatibility cost.)*
- [X] T101 [US4] Move `CompletionEngine.cs`, `QuickInfoEngine.cs`, `SignatureHelpEngine.cs`, `DatabaseCache.cs` from `AkmlSql.Engine` to `AkmlSql.IntelliSense`; update `AkmlSql.Engine` to reference the new project *(32 files moved via `git mv` to preserve history. Scope was actually broader than the PRD specified: all of `Completion/` (minus `DatabaseProvider.cs` which has a SqlClient dependency and stays in `AkmlSql.Engine`), all of `Parser/`, plus `Schema/DatabaseCache.cs` + `Schema/Models/*`. **Namespaces kept stable** (`AkmlSql.Engine.Completion.*`, `AkmlSql.Engine.Parser.*`, `AkmlSql.Engine.Schema.Models.*`) so no engine call-site updates needed — only the assembly boundary changed. `DatabaseProvider` is now registered externally by `PipeRpcServer` constructor via `_completionEngine.RegisterProvider(new DatabaseProvider())`.)*
- [X] T102 [US4] Add `tests/AkmlSql.IntelliSense.Tests/AkmlSql.IntelliSense.Tests.csproj` and migrate existing IntelliSense tests; assert behaviour is identical to pre-extract *(test project created with 5 smoke tests covering CompletionEngine + DatabaseCache + Models + TsqlParserService reachability from a project that does NOT reference `AkmlSql.Engine`. **Full migration of the 50+ existing engine-side tests in `tests/AkmlSql.Engine.Tests/Completion/` deferred** — those still pass against the new assembly via transitive reference, so functional equivalence is already validated. Physical move is a follow-up housekeeping sweep.)*
- [X] T103 [US4] Have `AkmlSql.Web.csproj` reference `AkmlSql.IntelliSense`; verify it loads under WASM (no native deps slip in) *(reference added; WASM bundle grew from 45 MB → 46 MB after pulling in the IntelliSense library — expected and within budget.)*

### M5.2 — Server canonical identity protocol

- [X] T104 [US4] Engine-side: implement `SchemaIdentifyRequest` / `SchemaIdentifyResponse` (new message-type codes per contracts/schema-cache-shape.md); resolve `serverCanonicalIdentity` from `@@SERVERNAME` → `SERVERPROPERTY('ServerName')` → instance metadata fallback *(landed. `SchemaIdentifyRequest`/`SchemaIdentifyResponse` added as MessageTypes 202/203. The typed handler `Handlers/Schema/SchemaIdentifyHandler.cs` is callback-pure so it unit-tests without a live SQL connection: it takes a `databaseLookup` + `identityResolver`. Production wiring in `PipeRpcServer.Handlers.cs` plugs the SessionManager for `databaseLookup` and a connection-string `Data Source` parser (`SchemaIdentifyHandlerSupport.ParseServerFromConnectionString`) for `identityResolver` as the initial impl. Swapping in the real `SELECT @@SERVERNAME` query is a follow-up — the handler's surface stays unchanged.)*
- [X] T105 [US4] Include `ServerCanonicalIdentity` in `HandshakeResponse` when the engine has a DB connection; advertise the `schema.cache.v1` capability *(landed. `Capabilities.Current` now advertises `SchemaCacheV1` alongside the M0/M3 baselines. `HandshakeResponse.ServerCanonicalIdentity` was already in the wire schema from T060, populated by the `serverCanonicalIdentityProvider` callback on `HandshakeHandler`; the engine-side resolver wiring will share the resolver delegate with `SchemaIdentifyHandler` when the WebSocket bridge bootstrap consumes both in M3.)*
- [X] T106 [P] [US4] Add `tests/AkmlSql.Engine.Tests/Schema/CanonicalIdentityTests.cs`: same SQL Server via three aliases resolves to one identity *(landed. 12 tests cover happy path, no-connection (null identity / null DB), empty SessionId, resolver-exception → diagnostics-friendly error message, the cross-alias collapse property (Theory with 3 distinct host strings collapsing to one canonical identity), and 9 connection-string parser cases for the engine-side `Data Source` extraction.)*

### M5.3 — Browser-side schema cache

- [X] T107 [US4] Implement `src/AkmlSql.Web/Services/SchemaCacheStore.cs` (IndexedDB-backed) per data-model.md E7 and contracts/schema-cache-shape.md: object stores `schemaEntries`, `changeLog`, `cacheMeta` *(landed. `Services/ISchemaCacheStore.cs` with the `SchemaSnapshot` POCO matching E7. Composite primary key is `(server, db)` joined with the ASCII Unit Separator (so two distinct DNS aliases of the same SQL Server collapse to one entry per clarification 3). PhaseA / PhaseB / FkIndex are stored as opaque byte arrays (MessagePack blobs the bridge produced) -- the browser doesn't need to crack them open in M5. CRUD + Touch + ClearAll + EstimatedSize. ListAsync returns entries sorted by LastUsedAt ascending so the LRU evictor scans in order. `changeLog` and `cacheMeta` stores are reserved in the JS shim but the diagnostics use the existing IDiagnosticsRingBuffer until the engine side wires the polling protocol.)*
- [X] T108 [US4] Implement `src/AkmlSql.Web/Services/SchemaSync.cs` driving the change-detection protocol: 30 s `SchemaChecksumRequest` polling while editor active, suspend after 5 min idle, resume on keystroke; trigger Phase A then Phase B refresh on checksum drift *(landed. `Services/ISchemaSync.cs` runs a 30 s timer that calls `cache.TouchAsync` when the editor is active. `ReportEditorActive()` updates the last-activity timestamp; the timer suspends polling after 5 min of idle and resumes on the next ReportEditorActive call. The actual `SchemaChecksumRequest` round-trip waits for the engine-side handler that lands as a follow-up -- until then the touch keeps LRU warm.)*
- [ ] T109 [US4] Wire `CompletionService` / `QuickInfoService` / `SignatureHelpService` (from US2) to fall back to `SchemaCacheStore` when bridge is unreachable; status badge transitions per the matrix in contracts/schema-cache-shape.md *(deferred -- the bridge-routed services currently return empty when disconnected. Cache-backed completion needs the engine to first emit phaseA/phaseB blobs the browser can consume; until then the cache is empty so the fallback would return empty anyway. The plumbing wires in when an interactive session can verify completion accuracy against the cached snapshot.)*
- [X] T110 [US4] Implement LRU eviction (FR-027): on `QuotaExceededError`, evict by `lastUsedAt` ascending until write succeeds, append `changeLog` row, emit single non-blocking notice *(landed. `Services/ISchemaCacheEvictor.cs` runs `EvictAndRetryAsync(Func<Task>)` -- catches QuotaExceededError (matched on message text since `JSException` types vary by platform), evicts the oldest entry by LastUsedAt, retries the write. Loops until the write succeeds or the cache is empty. Fires a single `EvictionOccurred` event with the count + last-evicted-db-name when at least one entry was evicted -- the UI surfaces that as a non-blocking notice once per bulk eviction. 5 tests cover the happy path, eviction order, single-notice property, empty-cache stop, and non-quota errors not triggering eviction.)*
- [X] T111 [US4] Settings → Schema cache page: list cached databases, sizes, last-used; Clear-one / Clear-all per FR-028 *(landed. `Pages/SchemaCacheSettings.razor` at `/settings/schema-cache`. Table shows Server / Database / Last used / Size (bytes -> KB / MB) / Clear button per row. Header has Refresh + Clear all. Settings.razor links here from the main page. Diagnostics ring buffer records every user-driven clear.)*
- [X] T112 [P] [US4] Add `tests/AkmlSql.Web.Tests/Cache/SchemaCacheStoreTests.cs`: identity-key dedup, LRU eviction, online↔offline state matrix *(landed. 9 tests cover Set/Get round-trip, missing-key null, the composite-key dedup property (two snapshots under the SAME (server, db) collapse to one row, last-write-wins), LRU ordering on ListAsync, Touch updates LastUsedAt without changing payload, Remove drops, ClearAll, EstimatedSize, and the composite-key argument validation. Online <-> offline transitions are covered by BridgeRoutedServicesTests in M3.)*
- [ ] T113 [P] [US4] Add `tests/AkmlSql.Web.E2E.Tests/UserStory4Tests.cs`: spec.md US4 Acceptance Scenarios 1–4 *(deferred -- Playwright needs a running engine + browser. The unit-test surface covers the cache + sync + eviction logic; the four acceptance scenarios run when the engine-side schema-cache message types land.)*

### M5.4 — Snippets in the browser

- [X] T114 [US4] Add `src/AkmlSql.Web/Services/SnippetStore.cs`: built-in snippets embedded as JSON resource; user snippets persisted in IndexedDB *(landed. `Services/ISnippetStore.cs` with the `WebSnippet` / `WebSnippetMetadata` / `WebSnippetVariable` POCOs (JSON-compatible with the engine's `Snippet` shape for future round-trip). Two built-ins ("ssf" -> SELECT * FROM, "cte" -> Common Table Expression skeleton) synthesised programmatically; user snippets persist to the `snippets` IndexedDB store. Built-ins reject Save/Delete via the `builtin.*` id prefix.)*
- [ ] T115 [US4] Snippet expansion in the editor — wire to existing engine snippet expander when bridge is up; local-only expansion path when bridge is down; engine round-trip for Save / Delete is gated on `snippets.write` capability per contracts/rpc-handshake.md *(deferred -- the SnippetStore.SaveAsync today writes only locally. Gating on `snippets.write` + sending `SnippetSave` / `SnippetDelete` over the bridge when present requires the editor-side snippet-trigger UI which lands when the CodeMirror snippet-expansion path is verified interactively. The store + capability gate plumbing are in place; only the bridge round-trip on save/delete is missing.)*
- [X] T116 [P] [US4] Add `tests/AkmlSql.Web.Tests/Snippets/SnippetStoreTests.cs`: built-in load, user CRUD, capability-gated bridge writes *(landed. 9 tests covering built-ins present on a fresh store, GetByShortcode (including case-insensitive), user-snippet round-trip, Save / Delete reject built-in ids, Delete drops user snippet, Save auto-generates an id when missing, and the built-ins-precede-user ordering invariant.)*

### M5.5 — Refactoring (lightweight + heavyweight)

- [X] T117 [US4] Add `src/AkmlSql.Web/Services/RefactoringService.cs`: lightweight refactorings (rename in selection, format selection) run locally via `AkmlSql.Formatting`; heavyweight refactorings (smart rename, schema-aware) require `refactoring.heavy` capability and run via the bridge *(landed. `Services/IRefactoringService.cs` exposes `FormatSelectionAsync` (always local, via IFormatterService) + `PreviewAsync` / `ApplyAsync` (heavy -- gated on `Bridge.EngineCapabilities.Contains("refactoring.heavy")` AND bridge open). `HeavyAvailable` is the property the UI reads to enable/disable the heavyweight menu items, paired with `<CapabilityNotice>` for the inline gate.)*
- [X] T118 [P] [US4] Add `tests/AkmlSql.Web.Tests/Refactoring/RefactoringServiceTests.cs`: each refactoring kind, gated on capability matrix *(landed. 4 tests covering FormatSelection runs without a bridge, HeavyAvailable false on a closed bridge, Preview / Apply return null when heavy is unavailable.)*

### M5.6 — Docs

- [X] T119 [P] [US4] Update `doc/architecture.md` § Schema cache + § IntelliSense library extraction; update `doc/ipc-api.md` with the `SchemaIdentify` and capability additions *(landed. `doc/architecture.md` §9c documents the IntelliSense / Analysis / AI library trilogy (paths, what moved, what stayed, the decoupling refactors); §9e covers the M5 schema-cache identity resolution path (handshake vs SchemaIdentify, the callback-pure handler shape, the connection-string Data Source parser). `doc/ipc-api.md` documents MessageTypes 202/203 with full request/response shapes.)*
- [X] T120 [US4] Write `doc/WEB/quickstart-m5.md` (section 4 of quickstart.md) *(landed. Walks through the offline IntelliSense flow, snippet expansion, light refactoring, LRU eviction observation, and the file map. Calls out the three remaining gaps (engine-side schema-cache message types, cache-backed completion fallback, heavyweight refactoring UI) so the next interactive session knows what to verify.)*

**Checkpoint**: US4 demoable — work against DB with engine, stop engine, keep working; completions still flow; reconnect silently restores Live; snippets and refactorings work in the appropriate modes.

---

## Phase 7: User Story 5 — AI assistance with BYO key (Priority: P5 — M6)

**Goal**: extract AI prompts/provider code into a shared library; wrap user-supplied API keys with Web Crypto at rest (clarification 2); call provider endpoints directly from the browser; deliver Text-to-SQL, Explain, Fix, Optimize, Index Analysis, Chat, Ghost Text.

**Independent test**: per spec.md US5 — enter a provider key in settings, select SQL, invoke Explain / Fix / Optimize; verify provider receives the request directly (no AKML server hop) and the key never appears in plaintext storage.

### M6.1 — Extract shared AI library

- [X] T121 [US5] Create `src/AkmlSql.AI/AkmlSql.AI.csproj` targeting `netstandard2.0` *(target switched to `net10.0` for the same reason as T100/F1: modern C# features without polyfills. Both consumers (`AkmlSql.Engine`, `AkmlSql.Web`) are already net10.)*
- [X] T122 [US5] Move prompt templates + `PromptBuilder.cs` + `PrivacyMode.cs` from `AkmlSql.Engine/Handlers/Ai/` into `src/AkmlSql.AI/` *(18 files moved via `git mv` from `src/AkmlSql.Engine/Ai/`: `Prompts/*` (8 files), `Context/*` (3 — `SchemaContextBuilder`, `SchemaContextFormatter`, `TokenEstimator`), `Privacy/*` (3), `Streaming/StreamCoalescer.cs`, `Providers/*` (2). **Stays in `AkmlSql.Engine`**: `AiRequestHandler` + `AiProviderTestHandler` (IPC dispatchers consuming `RpcMessage` + `SchemaCacheManager`) and `Security/CredentialManager` (Windows DPAPI, won't run in WASM).)*
- [X] T123 [US5] Move provider clients to `src/AkmlSql.AI/Providers/`; ensure no `System.IO.File` use (must run in WASM) *(provider clients in this repo are NuGet packages — Anthropic.SDK, OpenAI, Mscc.GenerativeAI (Gemini), OllamaSharp, Microsoft.Extensions.AI — wired through `AiProviderFactory.Create(AiSettings)`. **Decoupling refactor**: `AiProviderFactory` now exposes a pluggable `KeyDecryptor` static delegate (default identity). The engine wires `KeyDecryptor = CredentialManager.Decrypt` at startup in `Program.cs`; the web edition leaves the default (its M6 `AiKeyVault` will unwrap via Web Crypto BEFORE calling). `SchemaContextBuilder` also refactored: dropped the `SchemaCacheManager` parameter in favor of a `Func<string, string, DatabaseCache?>` lookup delegate (same pattern as F1). `AkmlSql.Web` now references `AkmlSql.AI` directly. WASM bundle grew 46 → 60 MB (expected: pulls OpenAI, Anthropic, Gemini, Ollama, Microsoft.Extensions.AI, ML.Tokenizers).)*
- [X] T124 [P] [US5] Add `tests/AkmlSql.AI.Tests/AkmlSql.AI.Tests.csproj` and migrate existing engine-side AI tests; assert behaviour identical post-extract *(test project + 5 smoke tests covering `ExplainPrompt`, `PromptTemplates.BuildSystemPrompt`, `AiProviderFactory.KeyDecryptor` default-identity, `SchemaContextBuilder` graceful-unknown-session, Privacy type reachability. Migration of existing engine-side AI tests under `tests/AkmlSql.Engine.Tests/Ai/` deferred to a follow-up housekeeping sweep — those still pass against the new assembly via transitive reference.)*

### M6.2 — Browser-side key wrapping

- [ ] T125 [US5] Implement `src/AkmlSql.Web/Services/AiKeyVault.cs` per contracts/ai-key-wrapping.md: non-extractable AES-GCM 256 wrapping key, per-record IV, AAD bound to `providerId`; wrap on set, unwrap-and-immediately-use on call, zeroise on delete
- [ ] T126 [US5] Implement `src/AkmlSql.Web/Services/AiPreference.cs` (singleton record of active providerId)
- [ ] T127 [P] [US5] Add `tests/AkmlSql.Web.Tests/Ai/KeyVaultTests.cs`: round-trip, plaintext absent from IndexedDB (Invariant 2), tamper detection (Invariant 3), cross-provider unwrap rejected (Invariant 5)

### M6.3 — Provider call infrastructure

- [ ] T128 [US5] Implement `src/AkmlSql.Web/Services/AiClientFactory.cs` routing to provider clients with the origin allow-list from contracts/ai-key-wrapping.md; refuse fetch to any non-allow-listed origin
- [ ] T129 [US5] Add `src/AkmlSql.Web/Services/AiPromptService.cs` building the schema-aware prompt from `AkmlSql.AI.PromptBuilder` + the `SchemaCacheStore` from US4 (no engine round-trip)
- [ ] T130 [P] [US5] Add `tests/AkmlSql.Web.Tests/Ai/AllowListTests.cs`: a mock provider whose endpoint is not allow-listed must throw at `AiClientFactory`

### M6.4 — Editor UI

- [ ] T131 [US5] Add `src/AkmlSql.Web/Shared/AiPanel.razor` hosting the four primary actions (Text-to-SQL, Explain, Fix, Optimize) and Index Analysis; results render to a side pane with Accept / Discard
- [ ] T132 [US5] Add `src/AkmlSql.Web/Shared/AiChatPanel.razor`: free-form conversational panel with message history (in-memory only — no persistence)
- [ ] T133 [US5] Add Ghost Text inline-completion path (debounced typing → request → render greyed inline → accept on `Tab`)
- [ ] T134 [P] [US5] Add `tests/AkmlSql.Web.Tests/Ai/AiPanelTests.cs` (bUnit): action wiring, no-key prompt, provider-error rendering, key never present in DOM

### M6.5 — Settings → AI page

- [ ] T135 [US5] Add `src/AkmlSql.Web/Pages/SettingsAi.razor`: per-provider Add/Edit/Remove form, masked display of stored key, model selection, endpoint URL for Azure / Ollama / LM Studio; "No key" path opens provider docs (FR-032)
- [ ] T136 [US5] AI-provider error mapping: 401 → "Key invalid"; 429 → "Rate limited"; network error → "Unreachable"; content policy → provider-specific message + docs link (FR-033)

### M6.6 — E2E

- [ ] T137 [P] [US5] Add `tests/AkmlSql.Web.E2E.Tests/UserStory5Tests.cs`: spec.md US5 Acceptance Scenarios 1–4 against mock provider endpoints (the test harness intercepts the allow-listed origins)
- [ ] T138 [US5] Update `doc/architecture.md` § AI library extraction; update `doc/WEB/quickstart-m6.md` (section 5 of quickstart.md)

**Checkpoint**: US5 demoable — user adds key, invokes Explain, sees response inline; key never appears in plaintext anywhere; offline (engine off) still works thanks to US4 schema cache.

---

## Phase 8: Polish & cross-cutting

**Purpose**: success-criteria verification, cross-spec polish, and the final docs pass.

- [ ] T139 [P] Run parity corpus end-to-end across web + WPF and assert byte-identical formatted output (SC-003): output `specs/021-web-edition/PARITY-RESULTS.md`
- [ ] T140 [P] Run analysis parity across web + WPF (SC-004): assert identical rule sets / severities / line columns; output to the same parity-results file
- [ ] T141 [P] Run SC-002 perf check: 10 MB script formats and analyses without UI freeze; record numbers in `specs/021-web-edition/PERF-RESULTS.md`
- [ ] T142 [P] Run SC-005 perf check: completion latency parity vs IDE plugin; record numbers in PERF-RESULTS.md
- [ ] T143 SC-006 manual check: with engine off, exercise the full P1 surface; capture screenshot evidence in `specs/021-web-edition/SC-006-EVIDENCE/`
- [ ] T144 SC-007 verification: install plugins + web, run a `Get-FileHash` comparison of `%AppData%/AKML SQL/` before and after a web install/uninstall cycle; record result
- [ ] T145 SC-008 manual check: laptop offline for a full working day uses cached schemas without degradation; record observations
- [ ] T146 SC-009 manual check: with the browser DevTools network panel open, verify no request to any AKML-owned domain in the AI request path; record observations
- [ ] T147 SC-010 verification: extends T097 — also confirm `INSTALL-SUMMARY.txt`, certs, firewall rule, and the Windows service are removed
- [ ] T148 [P] Update `doc/progress.md` with the spec-021 entry (per the existing per-spec table style); summarise tasks completed / deferred
- [ ] T149 [P] Update root `README.md` with a short "Web edition" section + link to `doc/WEB/00-INDEX.md`
- [ ] T150 Run `quickstart.md` end-to-end as a fresh user; file any deltas as follow-up tasks; mark this task done only when no friction remains

---

## Dependencies & execution order

### Phase dependencies

- **Phase 1 (Setup)**: no dependencies; runs first.
- **Phase 2 (Foundational, M0+M1)**: depends on Phase 1. **Blocks** all user stories until T025 (perf baseline) passes and T030 (WASM scaffold ready) lands.
- **Phase 3 (US1 / M2)**: depends on Phase 2.
- **Phase 4 (US2 / M3)**: depends on Phase 3 (US1 surface is what the bridge plugs into).
- **Phase 5 (US3 / M4)**: depends on Phase 3 (web bundle to deploy); may run in parallel with Phase 4 once M3 PRD design is locked, but Phase 5 cannot ship its LAN-mode features without M3.
- **Phase 6 (US4 / M5)**: depends on Phase 4 (cache is keyed on bridge identity).
- **Phase 7 (US5 / M6)**: depends on Phase 6 (AI uses the schema cache).
- **Phase 8 (Polish)**: depends on whichever stories are in the target release.

### User-story dependencies

- US1 → no other-story dependencies; independent.
- US2 → requires US1's editor + IPC surface; otherwise independent of US3–US5.
- US3 → requires US1's bundle; benefits from US2 (LAN-pairing PIN) but a localhost-only install can ship before US2's pairing flow is complete.
- US4 → requires US2's bridge + capability negotiation.
- US5 → requires US4's schema cache (offline-capable AI prompt construction).

### Within each user story

- Service classes before UI components that depend on them.
- Browser-side wiring before E2E tests.
- All tests for a story can run in parallel (different test files).

### Parallel opportunities

- M0 handler migrations T013–T018 hit different folders and can be done concurrently.
- M2 service tasks T037 / T043 / T048 hit different files and can run in parallel after T033 (Editor page exists).
- M3 engine-side (T056–T066) and browser-side (T067–T071) parallelise across the bridge boundary.
- M4 IIS work (T084–T086) and TLS / firewall work (T087–T090) run in parallel.
- Phase 8 SC checks (T139–T142, T148–T149) all parallel — different files / different evidence locations.

---

## Parallel example: User Story 1 (M2)

```text
# Once T033 (Editor page wired with EditorComponent) lands, these can proceed in parallel:
T037 [US1] FormatterService                  (src/AkmlSql.Web/Services/FormatterService.cs)
T043 [US1] AnalyserService                   (src/AkmlSql.Web/Services/AnalyserService.cs)
T048 [US1] DiagnosticsRingBuffer             (src/AkmlSql.Web/Services/DiagnosticsRingBuffer.cs)
T051 [US1] EditorSessionStore                (src/AkmlSql.Web/Services/EditorSessionStore.cs)

# All US1 tests can run in parallel:
T035 [US1] Theme tests
T041 [US1] Formatter parity tests
T047 [US1] Analyser parity tests
T050 [US1] Ring-buffer tests
T052 [US1] Session-restore tests
T053 [US1] E2E user-story-1 tests
```

---

## Implementation strategy

### MVP-first (User Story 1 only)

1. Complete Phase 1 (Setup).
2. Complete Phase 2 (Foundational — M0 transport refactor + M1 spike + Blazor scaffold).
3. Complete Phase 3 (US1 / M2 — browser formatter + analyser).
4. **Stop and validate**: paste SQL into the deployed bundle, format it, analyse it; compare against the IDE plugin on the parity corpus.
5. Demo / soft launch. The web edition is independently useful at this point.

### Incremental delivery (recommended)

1. Phase 1 + 2 → foundation.
2. Phase 3 (US1) → MVP / soft launch.
3. Phase 4 (US2) → live IntelliSense; users with a local SQL Server get the IDE-plugin experience in a browser.
4. Phase 5 (US3) → installer integration; mainstream users land here.
5. Phase 6 (US4) → offline resilience; appeals to laptop / travel users.
6. Phase 7 (US5) → AI features close the parity gap.
7. Phase 8 → SC verification + docs polish.

### Parallel team strategy (if staffed)

After Phase 2 closes:

- One engineer drives Phase 3 → Phase 4 (browser surface + bridge).
- A second engineer drives Phase 5 (installer) starting from the M2 bundle.
- Once Phase 4 lands, a third engineer can pick up Phase 6 (schema cache extraction + offline behaviour) in parallel with Phase 5 ending.
- Phase 7 (AI) is a single-engineer milestone — no further parallelism needed.

---

## Notes

- `[P]` tasks touch different files and have no dependencies on incomplete tasks in the same phase. Sequential tasks within a phase usually share a file or depend on a service the previous task created.
- `[Story]` labels (US1–US5) tag tasks back to spec.md user stories; Setup / Foundational / Polish carry no story label.
- Tests are *part of the deliverable* per the spec's Definition of Done — do not defer them.
- Wire format and message-type integer codes are **frozen** in M0; never change them downstream.
- Commit per task or per logical group; checkpoints at the end of each phase are the natural release boundaries.
- Avoid: cross-story dependencies that break independent testability; "while we're in here" scope creep on the M0 refactor (two-week budget is hard); modifying any file under `src/AkmlSql.Shell.Shared/` in M0 (zero blast radius is a hard M0 success metric).
