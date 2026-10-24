# Implementation Plan: Query tabs, one Connect to database dialog, and editor polish for the web edition

**Branch**: `043-web-query-tabs-polish` | **Date**: 2026-10-21 | **Spec**: [spec.md](./spec.md)
**Input**: Feature specification from `/specs/043-web-query-tabs-polish/spec.md`; plan-phase
codebase map in [codebase-map.md](./codebase-map.md)

## Summary

Give the web edition SSMS-style **query tabs**: each tab has its own text, name, connection,
engine session, results pane and running state; tabs are restored on reload with their
connection bindings and reconnect lazily; closing a tab releases its engine session. Around the
tabs: one **"Connect to database"** command and dialog for both kinds of server, regrouped so the
trust-certificate choice is visible and honoured on every reconnect; **editor typography** set
deliberately (explicit line height matched to SSMS, a short font list, syntax and chrome colours
that meet contrast in light and dark); a **dark-theme fit** pass (first confirmed defect: no
`color-scheme`, so native checkboxes and scrollbars render light); and **writing assistants**
kept out of the SQL editor. SSMS is untouched.

**Approach** ([research.md](./research.md) R1–R19): the page owns N `QueryTab` objects and one
`QueryTabSession` per tab (its own persisted engine session id); the existing
`ISqlConnectionService` becomes the *active tab's view* so its forty readers do not change, while
the few sites that can outlive a tab switch take the tab's session explicitly. One CodeMirror view
whose per-tab editor state is swapped in JavaScript; one results pane per tab mounted while
hidden. Tabs persist in a new IndexedDB store (DB version 5) with a one-time migration of the
single document. Engine: the WebSocket transport answers requests concurrently (notifications
inline, requests off the read loop, one send lock) and `SessionDisconnect` gains an append-only
`Release` flag. Tokens: a line-height entry and four editor colours in `theme-tokens.json`,
regenerated; the desktop's measured syntax fixes adopted.

**Spec amendments made at plan time** (2026-10-21): FR-014's Assumptions and Dependencies
bullets said no engine change was needed; the map found the engine's web transport answers one
request at a time, so overlapping runs need the transport change above (the requirement stands).
The "Today" baseline now records that the deployed web edition (1.26.1019.2313) already has the
trust checkbox, below the fold of a scrolling form. The "saved connection deleted" edge case now
keeps the tab's binding (the saved id is only a reference).

## Technical Context

**Language/Version**: C# 14 / .NET 10 (web, engine, tests); Blazor WebAssembly .NET 10;
JavaScript ES modules (`akml-editor.js`, `akml-indexeddb.js`, `akml-workspace.js`; CodeMirror 6
bundle `@codemirror/view` 6.43.5, committed, no rebuild needed); PowerShell 5.1 (theme generator)
**Primary Dependencies**: existing only — CodeMirror 6 (vendored), MessagePack 3.1.8, Playwright
1.*, xunit 2.x, bUnit; no package added
**Storage**: IndexedDB `AkmlSqlWeb` v4 → v5 (new store `editorTabs`; `analysisSettings/editorSettings`
gains `FontFamily`); engine session table and per-session schema cache (unchanged shape);
`docs/theme-tokens.json` (new entries)
**Testing**: xunit + bUnit (`tests/AkmlSql.Web.Tests`), node tests for the JS modules, engine
handler/transport tests (`tests/AkmlSql.Engine.Tests`), Playwright browser suites
(`tests/AkmlSql.Web.E2E.Tests`, serial, Debug builds, local SQL Server + PostgreSQL on 58432),
theme-token `-CheckOnly` gate, `WebCssTokenTests`, `ShortcutCollisionTests`
**Target Platform**: browser (Chromium, Edge, Firefox) against the Windows x64 engine; SSMS
extension unchanged
**Project Type**: web application (Blazor WASM) + out-of-process engine
**Performance Goals**: tab switch reflected in status bar, database selector and Schema panel
within 1 s (SC-003); restored tabs cost no connection at page load (FR-009); a run in one tab must
not delay another tab's completion request by more than the engine's own handling time (FR-014);
editor swap under 50 ms for a 10 MB document is not required, under 200 ms is the target
**Constraints**: IPC append-only, no new message type or capability; SSMS byte-for-byte unchanged
(named-pipe transport untouched); no password in browser storage; theme CSS generated, never
hand-edited; existing browser test ids keep their meaning for the active tab; 20 tabs, 10 MB per
tab; constitution V (smallest change: facade kept, grid state not lifted)
**Scale/Scope**: ~12 web services touched, `Editor.razor` restructured around a tab list, 1 new
IndexedDB store, 2 engine files (transport, disconnect handler) + cache/session removal, 1 JSON
token file + generator branch, ~13 strings, 5 user stories, ~8 new test classes

## Constitution Check

*GATE: passed before Phase 0; re-checked after Phase 1 (see end of file).*

| Principle | Status | Evidence |
| --- | --- | --- |
| I. Process isolation & host safety | PASS | Shell projects untouched; engine changes are transport dispatch and session release in `AkmlSql.Engine`; browser code stays UI and orchestration (R1, R3, R5). |
| II. Build integrity | PASS | Token changes go through `theme-tokens.json` + `generate-theme-css.ps1` (new line-height branch), both outputs regenerated, `-CheckOnly` and `ThemeCssSyncTests` stay green; `color-scheme` is not a colour literal (`WebCssTokenTests` allowlist only shrinks); solution builds in one pass; no SDK change. |
| III. Tests & corpora non-regressible | PASS | New behaviour lands with unit, node, engine and browser tests (quickstart §1); corpora and goldens untouched and re-run as proof; the 18-pair contrast probe only grows. |
| IV. Git consent | PASS | No git mutation in this plan; implementation delivers uncommitted changes and asks. |
| V. Simplicity & convention fidelity | PASS with justification | Reuse first: `ISqlConnectionService` kept as the active-tab view; `TabStrip` extended, not duplicated; `EditorSessionStore` generalised per key; `SessionDisconnect` extended, no new message; CodeMirror's own multi-document pattern. Two items justified in Complexity Tracking: the engine transport change and per-tab mounted results panes. |

**Additional constraints**: no secret in the tab record (tripwire test extended); the loopback /
SSRF guard unchanged and run per tab; 10 MB document and 1 MB snippet limits unchanged; IPC
local-only unchanged; technology pins unchanged; `CLAUDE.md`, `doc/ipc-api.md`,
`doc/configuration.md`, `doc/progress.md` and the served docs updated in the same change.

## Project Structure

### Documentation (this feature)

```text
specs/043-web-query-tabs-polish/
├── plan.md              # this file
├── spec.md              # feature spec (amended at plan time, see Summary)
├── codebase-map.md      # where the web assumes one document / one connection (plan input)
├── research.md          # Phase 0: decisions R1–R19, spikes S1–S4
├── data-model.md        # Phase 1: tabs, sessions, bindings, records, settings, tokens
├── quickstart.md        # Phase 1: acceptance gate
├── contracts/
│   ├── ipc.md                 # SessionDisconnect.Release, transport concurrency contract
│   ├── web-ui.md              # tab strip, keys, panes, dialog, wording, History, attributes, dark walk
│   └── theme-and-settings.md  # token additions, generator branch, CSS variables, editor settings
├── checklists/requirements.md
├── baseline/dark/       # dark-walk captures (created by the tour during implementation)
└── tasks.md             # Phase 2 (/speckit.tasks), not created here
```

### Source Code (repository root)

```text
src/
├── AkmlSql.Core/
│   └── Ipc/Messages/SessionDisconnectRequest.cs      # + Release (append-only key)
├── AkmlSql.Engine/
│   ├── Transports/WebSocketTransport.cs              # notifications inline, requests off-loop, send lock (R3)
│   ├── Handlers/Control/SessionDisconnectHandler.cs  # Release → RemoveSession + cache/provider removal (R5)
│   ├── Server/SessionManager.cs                      # RemoveSession gains a caller
│   └── Schema/SchemaCacheManager.cs                  # RemoveSession(sessionId) (spike S3)
├── AkmlSql.Web/
│   ├── Program.cs                                    # IQueryTabs registration; facade wiring
│   ├── Pages/Editor.razor                            # tab strip in the centre cell; per-tab state moved to QueryTab; N results panes; tab commands
│   ├── Pages/History.razor                           # Open / Re-execute into a new tab (R11)
│   ├── Pages/Snippets.razor, Pages/Styles.razor      # assistant opt-out attributes; runtime font variables
│   ├── Services/
│   │   ├── QueryTabs.cs                              # NEW: IQueryTabs, QueryTab, tab set rules (cap, names, order, active)
│   │   ├── QueryTabSession.cs                        # NEW: per-tab session (engine id, binding, state machine R2)
│   │   ├── ConnectionBinding.cs                      # NEW: binding snapshot (data-model §4)
│   │   ├── ISqlConnectionService.cs                  # facade over the active tab's session; session-taking overloads
│   │   ├── IQueryExecutionService.cs                 # takes the tab session (execute, cancel, apply, parse)
│   │   ├── PostgresFormat.cs, PostgresAnalysis.cs    # session id from the tab
│   │   ├── IQuickInfoService.cs, ISignatureHelpService.cs  # offline paths read the ACTIVE tab's record (the legacy "current" record is migrated away)
│   │   ├── IEditorSessionStore.cs                    # per-key debounce; editorTabs records; migration
│   │   ├── EditorSessionKeys.cs, EditorDocumentFlow.cs  # per-tab document flow (one flow instance per tab)
│   │   ├── IWorkspaceStatus.cs                       # active tab's view; per-tab phase/outcome
│   │   ├── WorkspaceKeyMap.cs                        # editor:next-tab / previous-tab / close-tab
│   │   ├── JsIndexedDbAdapter.cs                     # StoreNames.EditorTabs
│   │   ├── IEditorSettingsStore.cs                   # FontFamily
│   │   └── Settings/SettingsCatalog.cs, SettingsSections.cs  # editor-font-family row; EditorDto.FontFamily
│   ├── Shared/
│   │   ├── TabStrip.razor, TabItem.cs                # Closable, IsModified, OnClose (R10)
│   │   ├── EditorComponent.razor                     # SwitchTabAsync, CloseTabAsync, tab-aware SetText/Replace/Diagnostics; font-family variable; new cm-* CSS
│   │   ├── ResultsPaneComponent.razor, ResultSetGrid.razor  # IdPrefix parameter; RunStartedAt; active-only test ids; session on Apply
│   │   ├── ConnectionManagerModal.razor              # constant title; four groups; fit; connect binds a tab
│   │   ├── CommandPalette.razor                      # one sql:connect command
│   │   ├── StatusBar.razor, DatabaseSelectorComponent.razor, SchemaTreeComponent.razor, MainLayout.razor  # active-tab view; SchemaSync follows the active tab; wording
│   │   ├── Settings/EditorSection.razor, Settings/ConnectionsSection.razor  # font row; wording
│   │   └── AiChatPanel.razor, RefactorPreviewPanel.razor  # runtime font variables
│   └── wwwroot/
│       ├── js/akml-editor.js                         # switchTab / closeTab, tab-aware ops, contentAttributes (R6, R14)
│       ├── js/akml-indexeddb.js                      # DB_VERSION 5, editorTabs store
│       ├── js/akml-workspace.js                      # (keys come from the map; no change expected)
│       └── css/app.css, css/themes/*.css             # color-scheme per theme (hand CSS); regenerated themes
├── AkmlSql.Site/wwwroot/css/themes/*.css             # regenerated copies
docs/theme-tokens.json                                # lineHeight, Editor.* colours, Syntax fixes
scripts/generate-theme-css.ps1                        # lineHeight branch
doc/ipc-api.md, doc/configuration.md, doc/deployment.md, doc/topics/getting-started.md, doc/progress.md, doc/WEB/

tests/
├── AkmlSql.Core.Tests/                               # SessionDisconnectRequest key round-trip
├── AkmlSql.Engine.Tests/Transports/WebSocketTransportConcurrencyTests.cs   # NEW (R3, S1)
├── AkmlSql.Engine.Tests/Handlers/SessionDisconnectReleaseTests.cs          # NEW (R5)
├── AkmlSql.Web.Tests/
│   ├── Editor/QueryTabsTests.cs, QueryTabSessionTests.cs, EditorTabStoreTests.cs, TabMigrationTests.cs  # NEW
│   ├── Editor/EditorDocumentFlowTests.cs, EditorSessionKeyTests.cs, SessionRestoreTests.cs  # per-tab semantics
│   ├── Shell/TabStripTests.cs, ShortcutCollisionTests.cs, ConnectionManagerModalKindTests.cs  # closable strip; reserved keys; title
│   ├── Results/ResultsPaneComponentTests.cs          # IdPrefix, RunStartedAt
│   ├── Settings/*                                    # FontFamily store/export/reset
│   ├── Theme/WebCssTokenTests.cs                     # unchanged rules
│   └── js/akml-editor.tabs.test.mjs (NEW), akml-indexeddb.versionchange.test.mjs (pin 5), akml-editor.options.test.mjs
├── AkmlSql.Web.E2E.Tests/
│   ├── QueryTabsTests.cs, ConnectDialogTests.cs, EditorTypographyTests.cs  # NEW
│   ├── WorkspaceAccessibilityTests.cs                # + syntax and editor pairs
│   ├── WorkspaceScreenshotTour.cs                    # + dialogs/popups, dark output folder
│   ├── NativeKeyAcceptanceTests.cs                   # + three tab keys
│   ├── DocumentFlowTests.cs                          # New opens a tab (was: replaces)
│   └── Harness/SqlSession.cs                         # unchanged selectors (active tab)
└── AkmlSql.Site.Tests/ThemeCssSyncTests.cs           # regenerated copies
```

**Structure Decision**: existing web + engine layout; no new project. New web files are three
small services (`QueryTabs`, `QueryTabSession`, `ConnectionBinding`) next to the services they
replace the single-document behaviour of; everything else is an edit in place.

## Design notes (how the pieces fit)

**Tab switch** (`Editor.razor`): set `IQueryTabs.ActiveId` → facade raises `StateChanged` →
`MainLayout` restarts `SchemaSync` for the new session (stop / start, as it does on connect) →
`EditorComponent.SwitchTabAsync(tabId, seedText, kind)` swaps the CodeMirror state without a text
callback → the page republishes the active tab's phase / outcome / result generation into
`IWorkspaceStatus` → the results host shows that tab's pane → page title and `doc-name` update →
set record written with the new active id. Target: under 1 s end to end (SC-003), the schema
panel reading the browser cache for an already-seen server immediately.

**Execute** (`Editor.razor` → `IQueryExecutionService`): capture `tab` and `tab.Session` before
the first await; `tab.IsExecuting = true`, `tab.RunStartedAt = now`; request carries
`tab.Session.EngineSessionId`; on reply, write `tab.Result`, `tab.ResultGeneration`,
`tab.Session.ReportCurrentDatabase/TransactionState`, the history record with `tab.SessionKey`
and `tab.Name`; if `tab` is still active, republish to the status bar. Never read the facade
after an await.

**Reload** (`Editor.razor` `OnInitializedAsync`): read `editorTabs/set` (or migrate the legacy
record); build tabs with `NeedsReattach = binding != null`; mount; activate the saved active tab;
no engine traffic until the user activates (already active tab: on first user action that needs
the server, or immediately if a run is requested) or runs (R2 sequence: `SessionDisconnect`,
password prompt if needed, `ConnectionChanged`).

**Close** (`Editor.razor`): pending grid edits and modified text → confirmations (FR-007); open
transaction → the Disconnect warning; then `SessionDisconnect{Release=true}` (fire and forget
with a log on failure), `EditorComponent.CloseTabAsync`, pane unmount, record delete, set record
write, activate the neighbour (the previous tab, as SSMS does).

**Connect** (`ConnectionManagerModal`): `Connect` binds the tab that opened the dialog (the
active one, or the tab whose run asked for a connection), building `ConnectionBinding` from the
target plus the saved id; the status bar's one-time auto-restore only binds an unbound active tab
(R19).

**History** (`History.razor`): `Open in editor` / `Re-execute` → `IQueryTabs.OpenInNewTabAsync(text,
name, kind, autorun)` writes the record and the set, then navigates to `/`.

**Typography and theme**: token file → generator → CSS; the editor host sets the two runtime
variables; the new `.cm-*` rules live in `EditorComponent.razor`'s style block; `color-scheme`
lines in `app.css` under the theme attribute selectors.

## Complexity Tracking

| Violation | Why Needed | Simpler Alternative Rejected Because |
| --- | --- | --- |
| Engine transport dispatch change for a web feature (touches a shared engine component) | FR-014 and FR-003/FR-005: with the serial loop, one long query stalls every other tab's completion, execution, cancel and schema polling | Queuing runs fails FR-014; per-session lanes need the session id at dispatch time, which the envelope does not carry; the named-pipe transport already shows the off-loop pattern, so the change is small and contained |
| One results pane per tab mounted while hidden (N component trees) | pending grid edits, sort and selection live inside `ResultSetGrid` and are lost on unmount; SSMS keeps them per tab | lifting the grid's edit state into a page-level model is the larger refactor; a single swapped pane loses edits |

## Phase 0: Research (complete)

[research.md](./research.md): R1 tab model and facade; R2 persisted engine session ids and clean
reattach; R3 transport concurrency; R4 per-session schema cache kept; R5 session release; R6 one
editor view with swapped state; R7 per-tab panes; R8 persistence and migration; R9 keys; R10 tab
strip; R11 History; R12 typography; R13 syntax contrast; R14 writing assistants; R15 rename; R16
dialog regroup; R17 dark theme; R18 tab limit; R19 auto-restore. Spikes S1 (transport shared
state), S2 (state swap), S3 (cache removal API), S4 (DB upgrade) are the first tasks of their
stories.

## Phase 1: Design (complete)

[data-model.md](./data-model.md), [contracts/ipc.md](./contracts/ipc.md),
[contracts/web-ui.md](./contracts/web-ui.md),
[contracts/theme-and-settings.md](./contracts/theme-and-settings.md),
[quickstart.md](./quickstart.md). Agent context: `CLAUDE.md` has no "Active Technologies"
section for the Spec Kit script to fill and this feature adds no technology, so the script makes
no content change; the feature's conventions (tabs, keys, DB version 5, line-height token) are
added to `CLAUDE.md` by hand during implementation (documentation currency).

### Constitution re-check after design

| Principle | Status | Note |
| --- | --- | --- |
| I | PASS | engine work stays in `AkmlSql.Engine`; browser holds no secret, no parsing |
| II | PASS | generator branch + regenerated outputs; `color-scheme` outside the token gate but inside the literal gate's rules |
| III | PASS | every new behaviour has a named test in quickstart §1; existing suites and corpora are re-run as the regression proof |
| IV | PASS | nothing committed by the plan |
| V | PASS with the two justified items | no new project; three small new service files; existing components extended |

## Phase 2: Tasks (not created here)

`/speckit.tasks` generates `tasks.md`. Suggested story order for independent delivery: US5
(assistants; smallest), US3 (dialog and wording), US2 (typography and colours), US4 (dark walk,
after US2 and US3 so their surfaces are included), US1 (tabs; largest, with S1–S4 first).
