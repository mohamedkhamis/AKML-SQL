# Implementation Plan: SSMS-grade workspace for the web edition

**Branch**: `041-web-ssms-ux-parity` | **Date**: 2026-10-08 | **Spec**: [spec.md](./spec.md)
**Input**: Feature specification from `/specs/041-web-ssms-ux-parity/spec.md`; baseline screenshots in `baseline/`

## Summary

This feature makes the web edition's workspace behave like SQL Server Management Studio: a
results grid whose columns fit their content and can be dragged, Results / Messages /
Problems tabs with SSMS's messages and click-to-line errors, `GO` batches, F5, a database
selector, panels the user can hide and resize (Schema left, results below, AI right), one
Settings page with sections named like the SSMS edition's Options window, one tab-strip,
dialog and notification idiom, a named document with New / Open / Save / Save as, and a status
bar with SSMS's segments. It also makes every switch on screen true.

**Approach:** repair the data first, then reshape the screens, reusing seams the codebase
already has (research.md R1–R74; findings N1–N26; cross-area arbitrations X1–X10).

**Results grid (US1)**
- One CSS variable of column tracks on the grid root; C#-selected candidates measured by a
  canvas; drag and keyboard resize through a small pointer-capture module (R1–R3).
- Spreadsheet selection with `aria-activedescendant`, Escape-cancel editing, Set to NULL,
  invalid-cell gating, sort as a permutation of the row index the CRUD code already keys on
  (R4, R6, R9, R14).
- Copy and Copy-as through the SSMS edition's own formatters, moved to Core behind goldens;
  a clipboard write that works on the http LAN deployment (R5, R12).
- A context-menu component and a view-value dialog that is honest about engine-cut values
  (R7, R8).

**Results pane and engine (US2)**
- The engine runs scripts batch by batch over the token stream (`GO n`, strings, comments),
  continues after a failed batch as SSMS does, chooses the command behaviour per batch (which
  also fixes `CREATE PROCEDURE` under KeyInfo), records every error with number, severity,
  state, line and procedure, counts rows per statement, keeps partial results, stamps the
  completion time, budgets the whole reply, and adds Parse (R16–R24).
- The declared SQL type travels in the existing `ColumnSqlTypes` string; display formatting is
  a pure Core function; the 255 precision sentinel that breaks Apply on date/money columns is
  fixed (R10, R11).
- Permanent Results / Messages / Problems tabs, SSMS-formatted messages, a document-line
  mapping contract, and a reply deadline so the running state always ends (R20, R22, R34).

**Workspace (US3)**
- One CSS grid with named areas and CSS-variable sizes; pointer splitters in JS, keyboard
  splitters in Blazor; side panels fold away when the editor would drop below its minimum
  width (measured, R35); a dedicated layout store with per-field writes; page-scoped key
  capture (F5, Ctrl+F5, Ctrl+R, F8, F6, the Ctrl+K chords); one command table feeding the
  View menu, the palette and the keys; a Schema panel that stays present without a
  connection (R33–R42). The analyser findings in the editor (FR-087) and the Schema panel's
  filter, refresh and icons (FR-093) belong to US6, as the spec assigns them; tasks.md
  builds them there (T197, T200).

**Settings (US4)**
- One Settings component with per-section routes and the old addresses as aliases; nine
  section components; a shared stylesheet and `SettingRow`; a section contract for Restore
  defaults / Restore all / Export / Import with secrets excluded by construction; stores gain
  reset and reload; "Enable code analysis" works; Problems default filters and a suppressed-rules
  list; query defaults in Settings with a session-only toolbar override; editor font size, word
  wrap and tab size; the saved theme applied before first paint (R43–R57).

**Tabs, documents and polish (US5, US6)**
- `TabStrip`, `IDialogService` + `DialogHost`, `INotificationService` + `NotificationHost`,
  `shell.css` with one button / dialog / field / header / table style, a vendored Lucide icon
  sprite, `NavLink` highlighting with five entries, a named document model wired to History
  naming, status-bar segments with a short version, styled start-up screens, and font fallbacks
  from the token generator (R58–R67).

**Database and identity**
- `ListDatabases` by session, `ChangeDatabase` on the persistent connection (no credential
  re-send, #temp/SET kept), the current database reported after each execute so `USE` moves the
  selector, and the engine's host kind on the handshake so "Shared with SSMS" is only said when
  true (R25–R32).

**Tests and gates**
- Loose-mode bUnit with module setups, by-design test updates listed up front, Playwright
  techniques that can actually prove key capture, an engine unit/integration split, a
  check-by-check pass-list comparison for SC-008, a reserved test-id vocabulary, and a CSS
  token gate that ratchets from the real inventory (R68–R74).

## Technical Context

**Language/Version**: C# on .NET 10 (`AkmlSql.Web` Blazor WebAssembly, `AkmlSql.Engine`,
`AkmlSql.IntelliSense`), `netstandard2.0` + `net10.0` (`AkmlSql.Core`), LangVersion latest,
nullable enabled; JavaScript ES modules under `src/AkmlSql.Web/wwwroot/js` (vendored CodeMirror 6
bundle built by esbuild under `tools/codemirror`).
**Primary Dependencies**: existing only — Microsoft.AspNetCore.Components.WebAssembly 10.x,
Microsoft.Data.SqlClient 7.1.1, Microsoft.SqlServer.TransactSql.ScriptDom 180.102.0 (engine
**and** browser, through `AkmlSql.IntelliSense`), MessagePack 3.1.8, System.Text.Json,
Serilog; CodeMirror 6 with `@codemirror/lint` already in the vendored bundle. One new
**dev-time** vendoring, `lucide-static` (ISC), builds a committed SVG sprite exactly as
CodeMirror is built; nothing is loaded from a CDN.
**Storage**: browser IndexedDB `AkmlSqlWeb` version 3 → 4 (new `workspaceLayout` store with
per-field records; `analysisSettings` store gains the `editorSettings` key; `executionSettings`
and `editorSession` records gain fields additively); `localStorage['akml.theme']` as a
first-paint mirror of the theme; engine session state in memory (`SessionState.DatabaseName`
updated by the switch); the shared history SQLite file is unchanged (naming reuses
`TabTitle`/`SessionKey`).
**Testing**: xunit 2.x + bUnit 2.9.0 (`tests/AkmlSql.Web.Tests`, Loose JS interop, `node --test`
for JS modules), Microsoft.Playwright 1.63 (`tests/AkmlSql.Web.E2E.Tests`, `BridgeE2E` +
`SkippableFact`, needs a **Debug** build of `AkmlSql.Web` and `AkmlSql.Engine`, Chromium, and
a local SQL Server for grid/messages suites), `tests/AkmlSql.Engine.Tests` (pure + `(local)`
tempdb integration), `tests/AkmlSql.Core.Tests` (DTO compatibility, text helpers, formatter
goldens), `tests/AkmlSql.Shell.Shared.Tests` (one throwaway run to capture the Copy-As goldens),
`tests/AkmlSql.Site.Tests` (`ThemeCssSyncTests`). Ratchets: completion corpus and format-parity
goldens untouched (the formatter pipeline is not changed).
**Target Platform**: evergreen desktop browsers (Chromium, Edge, Firefox) at 1100 px and wider,
served by IIS over http on a LAN or by the dev server; the engine is .NET 10 win-x64 either
started by SSMS (`--pipe`) or installed as the LocalSystem service (`--web`).
**Project Type**: web application (Blazor WASM front end) over an out-of-process engine
reached through the WebSocket bridge with MessagePack frames (16 MB cap).
**Performance Goals**:
- A 1,000-row × 30-column result shows with sized columns within 1 s of arrival (SC-009);
  200 columns stay responsive (edge case).
- Column drag follows the pointer with no perceptible delay (SC-002, < 100 ms): live updates
  in JS, one interop call on release.
- Hiding both side panels gives the editor ≥ 95 % of the width; at 1100 px a 120-character line
  fits without a horizontal scrollbar (SC-003).
- Engine: lexing a script for `GO` costs ~50–70 ms per 10K lines and is skipped when no line
  starts with `GO`; the whole reply stays under the 15 MB budget.
- No new per-keystroke disk I/O; the caret segment is throttled in JS.

**Constraints**:
- IPC changes are additive keys or new adjacent types in the 200+ band, gated by capabilities
  (`session.database.v1`, `execute.v2`); an older bundle keeps banner + grids + text messages.
- No change to the SSMS edition's behaviour: the Copy-As formatter move is proven
  byte-identical by goldens captured before the move.
- The engine's row cap, byte cap, timeout ceilings and queued-only Cancel are unchanged; the
  interface tells the truth about them.
- Theme CSS is generated from `docs/theme-tokens.json` only; new tokens are regenerated into
  both theme folders; the `-CheckOnly` gate stays green.
- Secrets (API keys, passwords, pairing tokens) never leave their vaults; the settings export
  reads only preference stores.
- Browser-reserved keys (Ctrl+N/T/W/Tab) are not attempted; claimed keys are verified per
  browser before release (FR-049(c)).
- Run-immediately editable results stay (no confirmation on Apply); confirmation is only for
  *discarding* edits.
- Core stays `netstandard2.0`-safe; shell projects build with full MSBuild only.

**Scale/Scope**: 6 user stories, 81 functional requirements, 13 success criteria; about 54 new
source files (18 components, 20 services/helpers/models, 3 JS modules, 2 stylesheets, 1 sprite
toolchain), about 55 modified files, about 50 new test files; 2 new IPC types, 5 additive key
sets, 2 capabilities; about 50 working days in seven slices.

## Constitution Check

*GATE: evaluated before Phase 0, re-evaluated after Phase 1 design. Constitution v1.0.0.*

| Principle | Pre-design | Post-design | Notes |
|---|---|---|---|
| **I. Process Isolation & Host Safety** | PASS | PASS | All new capability is engine-side (batch loop, structured errors, row counts, Parse, ChangeDatabase, host kind) or in shared libraries (`AkmlSql.Core`: `SqlTypeName`, `SqlValueDisplay`, `GridTextFormats`, `AppVersion.StripBuildMetadata`; `AkmlSql.IntelliSense`: `SplitBatches`). The only shell change delegates `GridCopyAsMenu`'s formatters to Core with no behaviour change. Nothing in-process in SSMS gains logic. |
| **II. Build Integrity** | PASS | PASS | No SDK, package or toolchain version change. New tokens (`surface-editor`, dialog scrim, splitter hover, badge) and font fallbacks go into `docs/theme-tokens.json` and are regenerated into both theme folders by `scripts/generate-theme-css.ps1`; `-CheckOnly` stays green; `ThemeCssSyncTests` keeps the site copies equal. Shell projects untouched by the build path except the delegation. Web E2E runs against a Debug build. |
| **III. Tests & Corpora Non-Regressible** | PASS | PASS | Every touched `src/` project gains tests in its matching `tests/` project (listed per slice below). The formatter pipeline, completion engine and corpora are untouched. The Copy-As goldens are captured before the move. SC-008 is enforced check by check (R72). Tests that change by design are enumerated (R69). |
| **IV. Git Consent** | PASS | PASS | No git mutation is part of this plan. The branch was created by `/speckit.specify` at the user's request. Work is delivered uncommitted. |
| **V. Simplicity & Convention Fidelity** | PASS | PASS with four declared deviations | Reuse list and deviations below. |

**Principle I, in detail**
- The engine owns execution semantics: `ExecuteQueryHandler` loop, `FireInfoMessageEventOnUserErrors`
  scoped to the gate, `StatementCompleted`, PARSEONLY, `ChangeDatabase` on `SessionConnection`,
  `SessionManager.SetDatabase`, `EngineIdentity`.
- The browser does display, selection, layout and settings; it reuses `TsqlParserService` (already
  in the WASM bundle) only to estimate a reply deadline.
- Core gains pure text helpers with tests; no shell project gains logic.

**Principle III, in detail**
- New engine tests: `TsqlParserServiceTests` (splitter), `ExecuteOutcomeTests`,
  `ExecuteQueryIntegrationTests` (batches, errors, counts, Parse, ChangeDatabase, declared types,
  Apply on date/money), `ChangeDatabaseHandlerTests`, `ListDatabasesHandlerTests`,
  `HandshakeHandlerTests`.
- New Core tests: `SqlTypeNameTests`, `SqlValueDisplayTests`, `GridTextFormatsTests`,
  `ExecuteQueryMessagesCompatTests`, `AppVersion` strip cases.
- New Web tests: grid (layout, selection, editing, sort, copy, context menu, cell text, display,
  header, null/invalid), results pane, messages view, workspace (layout store, splitter, shell,
  keys), settings (routing, sections, reset, portability, filter, catalog consistency), shell
  (tab strip, dialog host, notification host, nav, status segments, document), services
  (query execution deadline, execution settings, editor settings, analyser gate), JS
  (`akml-workspace`, `akml-editor` diagnostics, `akml-download`, theme boot parity), gates
  (`WebCssTokenTests`, `ShortcutCollisionTests`).
- New E2E suites per user story plus a `WithSqlOrSkipAsync` harness helper.

**Simplicity evidence** (Principle V)

1. Column widths are one CSS variable on one element, not per-cell styles (R1).
2. Sorting permutes the existing `_rowIndexes` seam; the CRUD code is untouched (R6).
3. The Copy-As formatters move rather than being rewritten; SSMS keeps one-line delegations (R5).
4. `GO` splitting extends `TsqlParserService.SplitBatches`, which already exists (R16).
5. The declared type rides the existing `ColumnSqlTypes` string — no new keys (R10).
6. `ListDatabases` (94/194) gets one key instead of a new message (R25);
   `SchemaRefreshService.Refresh` populates the cache after a switch (R26).
7. Database changes reuse `SqlConnectionService.StateChanged`, which every consumer already
   subscribes to (R28).
8. The Problems list, schema tree, command registry, modal idiom, `akml-download.js`,
   `InputFile`, `NavLink` and `PageTitle` are extended, not replaced.
9. Settings sections are the existing pages' `@code` blocks re-homed; the routes keep working.

**Deviations declared** (see Complexity Tracking): a new IndexedDB object store; three new JS
modules; a dev-time icon vendoring; the `ISettingsSection` abstraction.

**Post-design re-check note**
- Phase 1 added: 2 IPC message types (217/218, 219/220), additive keys on `ExecuteQueryResult`,
  `ExecuteResultSet`, `HandshakeResponse`, `ListDatabasesRequest`, two new DTOs
  (`ExecuteMessageDto`, `ExecuteBatchDto`), two `ChangeDatabase` DTOs, three `ExecuteStatus`
  values, two capabilities; one IndexedDB store and one version bump; additive JSON fields on
  three browser records; four Core helpers; three JS modules; two stylesheets; a sprite.
- It removes: the four separate settings pages, the Blazor page-level key handler, the toolbar
  status text, the thirteen `.akml-tool-button` copies, the three overlay copies, the browser
  pop-ups, the `[binary N bytes]` local condensation, and `PersistCapsAsync`.
- The gates stand. Watch items for implementation: KeyInfo per batch (N1), the 255 sentinel
  (N2), PARSEONLY reset (N6), the focus-stash stack shared by menu → dialog chains (N21), the
  root `contextmenu` listener (N22), bake-after-apply resetting the sort (N23), the IndexedDB
  reopen without a version argument (N26), schema refresh as a notification and the claimed
  cache populate on a database switch (N27), the splitter's targeted key listener (no Tab
  trap), `CurrentDatabase` on error replies, and the existing checks that pin surface text
  (`status-connection` `Server/Database`, the Styles page's `styles-status` line, tab ids
  `ai-tab-chat` / `preview-mysql`, `RefactorPreviewPanel`'s `akml-tool-button` class).

## Project Structure

### Documentation (this feature)

```text
specs/041-web-ssms-ux-parity/
├── plan.md              # This file
├── spec.md              # Feature specification (6 user stories, 81 FRs, 13 SCs, clarifications)
├── research.md          # Phase 0 — findings N1..N26, arbitrations X1..X10, decisions R1..R74
├── data-model.md        # Phase 1 — browser records, grid state, IPC DTOs, engine session state, state machines
├── quickstart.md        # Phase 1 — build/test commands and the manual acceptance scenarios
├── contracts/
│   ├── ipc.md           # Additive keys, new types 217–220, capabilities, compatibility rules
│   ├── ui.md            # Layout constants, keyboard map, command ids, settings tree, status bar, dialogs, documents
│   └── testids.md       # Reserved data-testid vocabulary (bUnit + Playwright)
├── baseline/            # 2026-10-08 screenshots + README; tests/ pass-lists land here at implementation time
├── checklists/
│   └── requirements.md  # Spec quality checklist (all pass)
└── tasks.md             # Phase 2 output (/speckit.tasks — NOT created here)
```

### Source Code (repository root)

Slices: **[A]** US1 results grid · **[B]** US2 results pane + engine · **[C]** US3 workspace ·
**[D]** US4 settings · **[E]** US5 tabs + documents · **[F]** US6 shell polish ·
**[G]** tests and gates (cross-cutting). Database selector and identity are **[B]**/**[F]**.
tasks.md builds the shared pieces in its Foundational phase, ahead of the slices that first
use them: TabStrip, ContextMenu, the dialog and notification services, page key capture, the
workspace status service, editor diagnostics, icons, `shell.css`, the Core type helpers and
the `ResultSetGrid` split.

```text
src/AkmlSql.Core/
├── Text/SqlTypeName.cs                        [B] NEW — Format/Parse of declared SQL types, IsCharacterType
├── Text/SqlValueDisplay.cs                    [B] NEW — FR-026 display formatting (pure)
├── Text/EngineCutIndicator.cs                 [A] NEW — [text N chars] / [binary N bytes] detection
├── Text/GridTextFormats.cs                    [A] NEW — Copy-As formatters moved from the shell (goldens-first)
├── Text/SqlMessageText.cs                     [B] NEW — SSMS message lines (Msg/Level/State/Line, row counts, completion time)
├── AppVersion.cs                              [F] StripBuildMetadata helper
├── Ipc/RpcMessage.cs                          [B] ChangeDatabase 217/218, ExecuteParse 219/220
├── Ipc/Messages/ExecuteQueryMessages.cs       [B] keys 8–12 on ExecuteQueryResult, key 11 on ExecuteResultSet,
│                                                  ExecuteMessageDto, ExecuteBatchDto, ExecuteStatus 5–7, doc comments
├── Ipc/Messages/ChangeDatabaseMessages.cs     [B] NEW — request/response DTOs
├── Ipc/Messages/ListDatabasesMessages.cs      [B] key 1 SessionId
└── Ipc/Messages/HandshakeResponse.cs          [F] keys 8 HostKind, 9 RunsAs

src/AkmlSql.IntelliSense/
└── Parser/TsqlParserService.cs                [B] SplitBatches rewritten over the token stream (SqlBatchSpan, repeat count, separator error)

src/AkmlSql.Engine/
├── Execution/ExecuteQueryHandler.cs           [B] per-batch loop, behaviour per batch, info-message errors, StatementCompleted,
│                                                  partial results, completion stamp, message/repeat caps, parse mode, current database
├── Execution/ResultSetReader.cs               [B] declared types via SqlTypeName, 255 → null, shared budget, BatchIndex, set callbacks
├── Execution/ExecuteOutcome.cs                [B] NEW — pure aggregation of messages, batches, caps and status
├── Execution/SessionConnection.cs             [B] ChangeDatabase under the gate; note on the info-message flag
├── Server/SessionManager.cs                   [B] SetDatabase(sessionId, db, connStr)
├── Handlers/Control/ChangeDatabaseHandler.cs  [B] NEW
├── Handlers/Control/ListDatabasesHandler.cs   [B] session branch
├── Handlers/Handshake/HandshakeHandler.cs     [F] HostKind / RunsAs
├── EngineIdentity.cs                          [F] NEW — static host kind set by EngineHost
├── EngineHost.cs                              [F] set identity in RunAsync / RunWebAsync
├── Capabilities.cs                            [B] session.database.v1, execute.v2
└── EngineHandlerRegistry.cs                   [B] register 217 and 219; pass parser + refresh delegate

src/AkmlSql.Shell.Shared/
└── Productivity/Grid/GridCopyAsMenu.cs        [A] formatter bodies delegate to Core (no behaviour change)

src/AkmlSql.Web/
├── Pages/Editor.razor                         [A–F] workspace grid, toolbar groups (db selector, Execute/Parse, New/Open/Save),
│                                                  View menu, key dispatch, pane slots, gates on pending edits, document flows
├── Pages/Settings.razor                       [D] rewritten: routes, section list, filter, header actions
├── Pages/SettingsAi.razor                     [D] DELETED → Shared/Settings/AiAssistanceSection.razor
├── Pages/SchemaCacheSettings.razor            [D] DELETED → Shared/Settings/SchemaCacheSection.razor
├── Pages/Diagnostics.razor                    [D] DELETED → Shared/Settings/DiagnosticsSection.razor
├── Pages/History.razor, Snippets.razor, Styles.razor   [E][F] TabStrip, dialogs, notifications, page header, shell.css
├── Shared/ResultsPaneComponent.razor          [B] NEW — tabs, stacked sets, messages slot, Problems slot, empty/running states
├── Shared/ResultSetGrid.razor                 [A] NEW — one result set: widths, selection, editing, sort, copy, menus
├── Shared/ResultsGridComponent.razor          [A] split in T037; DELETED after US2 once the Pr247 test renders ResultsPaneComponent (FR-090)
├── Shared/MessagesView.razor                  [B] NEW — SSMS-formatted lines, click → document line
├── Shared/ContextMenu.razor                   [A] NEW
├── Shared/MenuItem.cs                         [A] NEW — menu item model
├── Shared/CellValueDialog.razor               [A] NEW
├── Shared/SplitterComponent.razor             [C] NEW
├── Shared/DatabaseSelectorComponent.razor     [B] NEW
├── Shared/TabStrip.razor                      [E] NEW
├── Shared/TabItem.cs                          [E] NEW — tab model
├── Shared/DialogHost.razor                    [F] NEW
├── Shared/NotificationHost.razor              [F] NEW
├── Shared/Icon.razor                          [F] NEW
├── Shared/Settings/*Section.razor (9)         [D] NEW — one per section
├── Shared/Settings/SettingRow.razor           [D] NEW
├── Shared/MainLayout.razor                    [C][F] DialogHost, NotificationHost, route-scoped padding and nav hide
├── Shared/NavMenu.razor                       [F] NavLink, five entries
├── Shared/StatusBar.razor                     [F] segments, short version, cache-probe key fix, Diagnostics link
├── Shared/SchemaTreeComponent.razor           [C] always rendered, connect action; [F] header, filter, refresh, icons
├── Shared/ProblemsListComponent.razor         [C][D] ShowHeader, OnVisibleCountChanged, DefaultFilter, analysis-off state
├── Shared/EditorComponent.razor               [B][C][D] selection-start-line, SetDiagnosticsAsync, caret events, editor options
├── Shared/CommandPalette.razor                [C][D] IsEnabled/IsChecked rendering, section navigation entries, icons
├── Shared/ProfilePickerComponent.razor        [D][F] "Style", group name from host kind, store-default fallback
├── Shared/ConnectionManagerModal.razor, ConnectionPickerComponent.razor  [F] dialogs, shell.css
├── Shared/Grid/GridColumnLayout.cs            [A] NEW
├── Shared/Grid/GridSelection.cs               [A] NEW
├── Shared/Grid/GridSortComparer.cs            [A] NEW
├── Shared/Grid/GridClipboard.cs               [A] NEW
├── Shared/Grid/GridCellText.cs                [A] NEW
├── Services/IQueryExecutionService.cs         [B] deadline + NoReply/Disconnected, ParseAsync, CurrentDatabase forwarding
├── Services/ExecuteLineMapper.cs              [B] NEW
├── Services/ISqlConnectionService.cs          [B] WindowsAuth, ListDatabasesForSessionAsync, ChangeDatabaseAsync, ReportCurrentDatabase
├── Services/ISchemaSync.cs                    [F] RefreshAsync
├── Services/IWorkspaceLayoutStore.cs          [C] NEW
├── Services/IWorkspaceStatus.cs               [C] NEW
├── Services/WorkspaceKeyMap.cs                [C] NEW — claimed-key table for akml-workspace.js and ShortcutCollisionTests
├── Services/ICommandRegistry.cs               [C] IsChecked / IsEnabled
├── Services/IExecutionSettingsStore.cs        [D] ShowColumnTypes, RetainLineBreaksOnCopy, SessionOverride, GetEffectiveAsync, ResetAsync, ReloadAsync
├── Services/IEditorSettingsStore.cs           [D] NEW
├── Services/IAnalysisSettingsStore.cs         [D] ProblemsFilter, ResetAsync, ReloadAsync
├── Services/IAnalyserService.cs               [D] honour Enabled
├── Services/IAiFeatureSettings.cs, IAiKeyVault.cs, IAiPreference.cs, IThemeService.cs, IProfileStore.cs  [D] reset/reload/upsert/group name
├── Services/IDiagnosticsRingBuffer.cs         [D] idempotent restore
├── Services/Settings/ISettingsSection.cs      [D] NEW (+ 6 section implementations, SettingsPortabilityService, SettingsCatalog, SettingsFilter, export DTOs)
├── Services/IDialogService.cs                 [F] NEW
├── Services/INotificationService.cs           [F] NEW
├── Services/EditorDocument.cs                 [E] NEW
├── Services/EditorDocumentFlow.cs             [E] NEW — New/Open/Save/Save as, testable without the Editor page
├── Services/EditorSessionKeys.cs, IEditorSessionStore.cs  [E] name/modified/counter, StartDocumentAsync
├── Services/WebHistoryLogic.cs                [E] tabTitle
├── Services/IEngineBridge.cs                  [F] EngineHostKind / EngineRunsAs
├── Services/JsIndexedDbAdapter.cs             [C] StoreNames.WorkspaceLayout
├── Program.cs                                 [all] registrations
├── wwwroot/index.html                         [D][F] classic theme boot script, styled boot/error screens
├── wwwroot/js/akml-workspace.js               [C] NEW — splitters, fold-away observer, page keys
├── wwwroot/js/akml-results-grid.js            [A] NEW — measure, resize, drag select, key trap, contextmenu
├── wwwroot/js/akml-ui.js                      [F] NEW — focus stack, trapTabStripKeys, copyText
├── wwwroot/js/akml-connection-manager.js      [F] re-exports from akml-ui.js
├── wwwroot/js/akml-editor.js                  [B][C][D][F] getSelectionInfo, setDiagnostics, caret listener, compartments, F5 comment
├── wwwroot/js/akml-download.js                [A] downloadText
├── wwwroot/js/akml-theme.js, akml-theme-boot.js  [D] localStorage mirror; classic synchronous boot
├── wwwroot/js/akml-indexeddb.js               [C] DB_VERSION 4, workspaceLayout store, onversionchange fix
├── wwwroot/css/app.css                        [F] imports, boot/error rules
├── wwwroot/css/components/results-grid.css    [A][B] tracks, resizer, selection, indicators, pane
├── wwwroot/css/components/shell.css           [F] NEW
├── wwwroot/css/components/settings.css        [D] NEW
├── wwwroot/lib/icons/akml-icons.svg + THIRD-PARTY-NOTICES.txt   [F] NEW (generated, committed)
└── tools/icons/{package.json,build-sprite.mjs,icons.json,.gitignore}  [F] NEW

docs/theme-tokens.json + scripts/generate-theme-css.ps1   [F] surface-editor token, dialog/splitter/badge tokens, font fallbacks
src/AkmlSql.Web/wwwroot/css/themes/*.css, src/AkmlSql.Site/wwwroot/css/themes/*.css   [F] regenerated
doc/ipc-api.md                                 [B][F] 94/194, 212–220, capabilities, handshake keys
doc/progress.md, CLAUDE.md                     [all] spec 041 entry; web structure notes
scripts/compare-test-results.ps1               [G] NEW

tests/AkmlSql.Core.Tests/
├── Text/SqlTypeNameTests.cs, SqlValueDisplayTests.cs, GridTextFormatsTests.cs   NEW
├── Ipc/ExecuteQueryMessagesCompatTests.cs                                      NEW
└── AppVersionTests.cs (strip cases)                                            NEW/extended
tests/AkmlSql.Engine.Tests/
├── Parser/TsqlParserServiceTests.cs            extended (splitter cases)
├── Execution/ExecuteOutcomeTests.cs            NEW
├── Execution/ExecuteQueryIntegrationTests.cs   extended (batches, errors, counts, Parse, ChangeDatabase, types, Apply)
├── Execution/CrudWriteGeneratorTests.cs        extended (null scale)
├── Handlers/ChangeDatabaseHandlerTests.cs, ListDatabasesHandlerTests.cs, HandshakeHandlerTests.cs   NEW/extended
└── InProcess/AllMessageTypesInProcessTests.cs  factory entries 217/219
tests/AkmlSql.Shell.Shared.Tests/              one throwaway golden-capture test for GridCopyAsMenu (removed after capture)
tests/AkmlSql.Web.Tests/
├── Fakes/WebTestFakes.cs                       NEW (shared fakes)
├── Grid/*  Results/*  Workspace/*  Settings/*  Shell/*  Services/*  Editor/*   NEW suites (see research R68)
├── js/akml-workspace.*.test.mjs, akml-editor.diagnostics.test.mjs, akml-download.test.mjs, theme-boot parity   NEW
├── Theme/WebCssTokenTests.cs + css-literal-allowlist.txt, Shell/ShortcutCollisionTests.cs   NEW gates
└── Styles/StylesReviewFixTests.cs, Pr247_ResultsGridApplyMessageTests.cs, Bridge/*Tests.cs   updated by design
tests/AkmlSql.Web.E2E.Tests/
├── Harness/WebAppFixture.cs (WithSqlOrSkipAsync), Harness/Keys.cs   NEW/extended
├── ResultsGridTests.cs, ResultsMessagesTests.cs, WorkspaceLayoutTests.cs, SettingsPageTests.cs,
│   TabStripKeyboardTests.cs, ShellPolishTests.cs, WorkspaceScreenshotTour.cs   NEW
└── FormatStylesTests.cs, FormatStylesSharedEngineTests.cs, EngineAutoConnectTests.cs, SqlServerAliasWebTests.cs, SiteScreenshotTour.cs   updated by design
```

**Structure Decision**: the existing web application layout is kept (pages, shared
components, services, `wwwroot/js` modules, `wwwroot/css/components` stylesheets); new grid
helpers live under `Shared/Grid/`, settings sections under `Shared/Settings/`, and settings
services under `Services/Settings/` so the feature's files are findable without a new project.
Engine and Core changes stay in their existing folders; no new project is created.

## Complexity Tracking

| Violation | Why Needed | Simpler Alternative Rejected Because |
|-----------|------------|-------------------------------------|
| New IndexedDB object store `workspaceLayout` (version bump 3 → 4) | FR-045 "write only the fields the user changed" and FR-101 "clearable from Settings" need per-field records that can be cleared as a unit | A JSON blob under the shared `analysisSettings` store forces whole-record writes (races between tabs) and cannot be cleared without touching other keys; the bump's known risk is removed by fixing `onversionchange` (N26) |
| Three new JS modules (`akml-workspace.js`, `akml-results-grid.js`, `akml-ui.js`) | Pointer capture, synchronous `preventDefault`, canvas measurement and a focus stack are only possible in JS; the repo's rule is one module per concern | Piggybacking on `akml-command-palette.js` (documented single-purpose, lives on every route) or Blazor event handlers (cannot `preventDefault` conditionally, one interop hop per pointer move) |
| Dev-time vendoring of `lucide-static` to build a committed SVG sprite | FR-081/FR-093 require one icon set instead of emoji; no icon set exists; CDN assets are forbidden | Inline SVG dictionaries in C# bloat every render; an icon font needs `@font-face` and fails in forced-colours mode; Codicons' artwork licence (CC BY 4.0) differs from the repo's MIT |
| `ISettingsSection` abstraction (six ~60-line classes + an aggregator) | FR-058/FR-059 need reset text, reset, export and import to agree per section, and the layout and grid areas must plug in without editing an exporter | A single service with a switch over section ids forces every area to edit it and lets the reset text drift from the export |
