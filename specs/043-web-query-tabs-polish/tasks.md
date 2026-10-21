---
description: "Task list for 043-web-query-tabs-polish"
---

# Tasks: Query tabs, one Connect to database dialog, and editor polish for the web edition

**Input**: Design documents from `/specs/043-web-query-tabs-polish/`
**Prerequisites**: [plan.md](./plan.md), [spec.md](./spec.md), [research.md](./research.md), [data-model.md](./data-model.md), [contracts/ipc.md](./contracts/ipc.md), [contracts/web-ui.md](./contracts/web-ui.md), [contracts/theme-and-settings.md](./contracts/theme-and-settings.md), [quickstart.md](./quickstart.md), [codebase-map.md](./codebase-map.md)

**Tests are not optional here.** Constitution III requires every new behaviour to land with
automated checks and the existing gates to stay green. In every story, write the test tasks
first, see them fail, then implement.

**Spikes are measurement tasks, not TDD.** S1–S4 (research.md) sit before the design that depends
on them. A spike ends by recording its outcome and the chosen branch in research.md; it is done
when the record exists, not when something "passes".

**Organization**: one phase per user story, in spec priority order (US1 tabs P1, US2 typography
P2, US3 dialog P3, US4 dark theme P4, US5 assistants P5). US2–US5 do not depend on US1, so the
smaller stories can be delivered first (see Implementation Strategy). Phase 2 holds only what
more than one story needs.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: can run in parallel. The task touches different files and does not depend on an unfinished task.
- **[Story]**: `[US1]`…`[US5]`, mapping to the user stories in spec.md.
- `R12`, `S3` refer to the decisions and spikes in research.md. Read the referenced entry before
  starting a task; it names the alternatives rejected and the traps. Line numbers in
  codebase-map.md are from 2026-10-21 and will drift; search for the member, not the line.

## Rules for every task

- Repository root: `D:\Repo\01-Khamis-Projects\AKML-SQL`. Web sources under `src/AkmlSql.Web/`,
  engine under `src/AkmlSql.Engine/`, Core DTOs under `src/AkmlSql.Core/Ipc/Messages/`, tests
  under `tests/`. The shell projects are **not** touched by this feature.
- **The facade rule (R1)**: `ISqlConnectionService` reads the active tab. Any code path that
  `await`s and then needs the connection it started with takes the tab's `QueryTabSession`
  captured **before** the first await: execute, cancel, parse, apply, rollback, the history
  record, `KeepResult`, format and analysis requests, completion and hover requests,
  `SendDocumentAsync`. Never read the facade after an await in those paths.
- **Never route a tab swap through `SetTextAsync`** (it moves the baseline and un-modifies the
  tab); use the new `SwitchTabAsync` (R6). A swap must fire no `OnTextChangedFromJs`.
- **Reattach order (R2, ipc.md §2)**: `SessionDisconnect` → **await the reply** →
  `ConnectionChanged`. A tab's connection and database controls are disabled while that tab's
  run is in flight.
- **MessagePack DTOs**: append `[Key(n)]` with the next free index; never renumber. No new message
  type and no new capability string in this feature.
- **IndexedDB**: `DB_VERSION` 4 → 5 once (Phase 2); records are schemaless JSON and default on
  read; the store's tripwire test (no "password" in any persisted record) covers the tab record.
- **Theme CSS is generated**: edit `docs/theme-tokens.json` and `scripts/generate-theme-css.ps1`,
  regenerate **both** outputs (web and `src/AkmlSql.Site/wwwroot/css/themes`), keep
  `-CheckOnly`, `ThemeCssSyncTests` and `WebCssTokenTests` green. `color-scheme` is the one
  hand-written theme rule (R17) and goes in `app.css`.
- **Test ids** only from `contracts/web-ui.md`; `sql-editor`, `doc-name`, `doc-modified`,
  `results-pane`, `execute-complete` keep their meaning for the **active** tab.
- **Keys**: nothing is bound before `ShortcutCollisionTests`' reserved list has Ctrl+F4,
  Ctrl+PageDown, Ctrl+PageUp and Ctrl+Shift+W (T003).
- **Browser tests need Debug builds** of `src/AkmlSql.Web` and `src/AkmlSql.Engine`
  (`WebAppFixture` runs `dotnet run --no-build -c Debug`); run `tests/AkmlSql.Web.E2E.Tests`
  serially (`-- xUnit.ParallelizeTestCollections=false`); rebuilding the engine while a sandbox
  engine runs locks the DLLs. PostgreSQL for the live tests: `scripts/pg-test-server.ps1 -Port 58432`.
- **No browser pop-ups**: use `IDialogService` / `INotificationService`.
- **Engine redeploy is a full publish** (CLAUDE.md); the user approves any deploy to Program Files.
- **Git**: deliver changes uncommitted and ask "Ready to commit?" (constitution IV). Never
  commit, push or amend on your own.

---

## Phase 1: Setup (baseline and gates)

**Purpose**: a recorded "before" and the gates that must be in place before anything is bound or stored.

- [X] T001 Record the pre-feature baseline in `specs/043-web-query-tabs-polish/baseline.md`: run `dotnet test tests/AkmlSql.Web.Tests`, `dotnet test tests/AkmlSql.Engine.Tests`, `node --test "tests/AkmlSql.Web.Tests/js/*.test.mjs"`, `pwsh scripts/generate-theme-css.ps1 -CheckOnly`, and the serial browser classes `DocumentFlowTests`, `WorkspaceAccessibilityTests`, `WorkspaceLayoutTests` (Debug builds first); save the `.trx` files under `specs/043-web-query-tabs-polish/baseline/before/` for `scripts/compare-test-results.ps1`
- [X] T002 [P] Capture the dark-theme "before" of the deployed web edition (localhost:8083, theme set through Settings) for the editor, the connection dialog (both kinds), Settings › Editor and History at 1280x720 into `specs/043-web-query-tabs-polish/baseline/before/dark-*.png`, and note in `baseline.md` the two defects already confirmed (light native checkbox and scrollbar; Security group below the fold)
- [X] T003 [P] Add Ctrl+F4, Ctrl+PageDown, Ctrl+PageUp and Ctrl+Shift+W to the `BrowserReserved` list in `tests/AkmlSql.Web.Tests/Shell/ShortcutCollisionTests.cs` (R9) and run the class green

---

## Phase 2: Foundational (shared seams)

**Purpose**: the storage version and the IPC key that more than one story's tests build on.

**⚠️ CRITICAL**: US1 cannot start before this phase; US2–US5 only need T003 from Phase 1.

### Tests for Phase 2 (write first; they must fail)

- [X] T004 [P] Move the version pin to 5 and assert the `editorTabs` store exists after `onupgradeneeded` in `tests/AkmlSql.Web.Tests/js/akml-indexeddb.versionchange.test.mjs` (S4)
- [X] T005 [P] Add a MessagePack round-trip test for `SessionDisconnectRequest.Release` (default false; old payload without the key reads false) in `tests/AkmlSql.Core.Tests/Ipc/SessionDisconnectRequestTests.cs`

### Implementation for Phase 2

- [X] T006 Bump `DB_VERSION` to 5 and add `editorTabs` to `STORES` in `src/AkmlSql.Web/wwwroot/js/akml-indexeddb.js` (comment the version history as the file does); add `StoreNames.EditorTabs = "editorTabs"` in `src/AkmlSql.Web/Services/JsIndexedDbAdapter.cs`
- [ ] T007 Spike S4: open an existing browser profile (saved connections, history settings, a document) against the Debug web build, confirm the upgrade keeps every store's data, record the outcome in `specs/043-web-query-tabs-polish/research.md` under R8/S4
- [X] T008 [P] Append `Release` (bool, next free `[Key]`) to `SessionDisconnectRequest` in `src/AkmlSql.Core/Ipc/Messages/` (find the request class the `SessionDisconnect` handler reads) with the doc comment from `contracts/ipc.md` §1; make T005 pass

**Checkpoint**: storage version 5 and the IPC key exist; every story can start.

---

## Phase 3: User Story 1 — Several query tabs, each on its own connection (Priority: P1) 🎯 MVP

**Goal**: N query tabs with their own text, name, connection, engine session, results pane and
running state; restored on reload with lazy, clean reconnects; released on close; runs in
different tabs may overlap.

**Independent Test**: three tabs on Northwind, tempdb and PostgreSQL; a different query in each;
reload; every tab returns with its text, name, connection and (after a run) its own results
(quickstart §1 `QueryTabsTests`, §2 M1–M7, M15).

### Spikes for User Story 1 (record outcomes in research.md; they gate the design below)

- [X] T009 [US1] Spike S1: in `src/AkmlSql.Engine/Transports/WebSocketTransport.cs` prototype off-loop dispatch of requests (notifications inline, `Task.Run` per request, a `SemaphoreSlim(1,1)` around `socket.SendAsync`), then audit `src/AkmlSql.Engine/RpcContext.cs` (settings cache), the SQLite history store, `DatabaseProvider` / `TryClaimPopulation`, `SchemaCacheManager`, and the same-session race of `ConnectionChanged` / `SessionDisconnect` against an in-flight `ExecuteQuery`; record findings and the chosen mitigations under R3/S1 in `specs/043-web-query-tabs-polish/research.md`
- [X] T010 [P] [US1] Spike S2: in `src/AkmlSql.Web/wwwroot/js/akml-editor.js` prototype `switchTab` with `view.setState` and a stored-state map, confirm no update-listener callback fires during the swap, undo survives a round trip, and `state.update({changes}).state` applies a replace to a detached state; record under R6/S2 in `specs/043-web-query-tabs-polish/research.md`
- [X] T011 [P] [US1] Spike S3: confirm or design `SchemaCacheManager.RemoveSession(sessionId)` (memory entries for every database of the session and their disk files) and the `DatabaseProvider.InvalidateSession` call in `src/AkmlSql.Engine/Schema/SchemaCacheManager.cs`; record under R5/S3 in `specs/043-web-query-tabs-polish/research.md`

### Tests for User Story 1 (write first; they must fail)

- [X] T012 [P] [US1] `tests/AkmlSql.Web.Tests/Editor/QueryTabsTests.cs`: new tab gets the next unused default name and inherits the active tab's binding; names never reused after close; 20-tab cap refuses the 21st with the message; close activates the previous tab; order and active id round-trip through the set record; `OpenInNewTabAsync` reuses an empty unmodified active tab (data-model §1–§2)
- [X] T013 [P] [US1] `tests/AkmlSql.Web.Tests/Editor/QueryTabSessionTests.cs`: state machine of data-model §3 with a fake bridge — restored tab sends nothing at load; first activation sends `SessionDisconnect`, awaits the reply, then `ConnectionChanged`; password prompt path; engine refusal keeps the binding; bridge URL change bumps every tab's generation; tab close sends `Release=true`; close during a run → `Closing` → `Release` only after the reply; a run that finds no connection → `Disconnected` keeps the binding → the next run reattaches (a link drop alone changes nothing); a restored PostgreSQL binding reattaches with its `SslMode` and `RootCertificatePath` — done 2026-10-21 in `QueryTabSessionTests` except "close during a run → `Release` only after the reply", which lives in the editor page (`EndRunAsync`): the browser test `QueryTabsTests.Closing_a_tab_while_its_query_runs_asks_first_and_leaves_the_other_tab_alone` covers what the user sees, not the release order
- [X] T014 [P] [US1] `tests/AkmlSql.Web.Tests/Editor/EditorTabStoreTests.cs`: per-key debounce (two tabs saved within 500 ms both persist), `RestoreAsync(key)` returns the pending record for that key only, `FlushAsync` flushes every key, set record written at once, tripwire (no "password" in any serialised tab record incl. the binding); a debounced save for a deleted tab id writes nothing
- [X] T015 [P] [US1] `tests/AkmlSql.Web.Tests/Editor/TabMigrationTests.cs`: a legacy `editorSession/current` record becomes tab 1 (text, cursor, profile, session key, name, modified, `NextDefaultNumber` → set record) and is cleared; a profile with no legacy record starts with one empty `SQLQuery1`; migration runs once
- [X] T016 [P] [US1] Extend `tests/AkmlSql.Web.Tests/Shell/TabStripTests.cs`: `Closable` strip renders `div role="tab"` with a `doc-tab-close` button (`tabindex=-1`, `aria-label="Close {name}"`), `IsModified` renders the mark, Delete on a focused tab and middle click raise `OnClose`, non-closable strips keep today's `button role="tab"` markup (R10)
- [X] T017 [P] [US1] Extend `tests/AkmlSql.Web.Tests/Results/ResultsPaneComponentTests.cs`: `IdPrefix` parameter drives pane and grid element ids; `results-pane` / `execute-complete` test ids rendered only when `IsActive`; the running clock reads `RunStartedAt`
- [X] T018 [P] [US1] Update `tests/AkmlSql.Web.Tests/Editor/EditorDocumentFlowTests.cs` and `tests/AkmlSql.Web.Tests/EditorSessionKeyTests.cs` for one flow instance per tab: New creates a tab instead of replacing; Open into an empty unmodified tab mints a new session key; `StartDocumentAsync` writes a tab record, not `current`
- [X] T019 [P] [US1] Add contract rows for `editor:next-tab` (Ctrl+Alt+PageDown), `editor:previous-tab` (Ctrl+Alt+PageUp), `editor:close-tab` (Ctrl+Alt+W) in `tests/AkmlSql.Web.Tests/Shell/ShortcutCollisionTests.cs` and the display text in `tests/AkmlSql.Web.Tests/Shell/ToolbarTitleTests.cs`
- [X] T020 [P] [US1] `tests/AkmlSql.Web.Tests/js/akml-editor.tabs.test.mjs`: `switchTab` fires no text callback; undo survives switch and return; `replaceTextIfUnchanged` and `setDiagnostics` on a stored tab update the stored state and are visible after switching back; `closeTab` drops the state; scroll position restored
- [X] T021 [P] [US1] `tests/AkmlSql.Engine.Tests/Transports/WebSocketTransportConcurrencyTests.cs`: two requests on two sessions overlap (a slow handler on session A does not delay session B's reply); `DocumentChanged` then `Completion` on one socket sees the new text; replies are serialised on the socket; a handler exception still yields the error reply for its request id
- [X] T022 [P] [US1] `tests/AkmlSql.Engine.Tests/Handlers/SessionDisconnectReleaseTests.cs` (read `tests/AkmlSql.Engine.Tests/Execution/SessionTargetIntegrationTests.cs` first; it already drives `SessionDisconnect` and may be the right home for the `Release=false` case): `Release=true` removes the session from `SessionManager`, drops its schema caches (memory and disk) and provider state after the connection close; `Release=false` behaves as today; an unknown session id with `Release=true` is a no-op success
- [X] T023 [P] [US1] `tests/AkmlSql.Web.E2E.Tests/QueryTabsTests.cs` (`[Trait("Category","BridgeE2E")]`, `WithSqlOrSkipAsync`, PostgreSQL via `AKML_PG_TEST_CONNECTION`): the scenarios of quickstart §1 "Browser: tabs" and "Browser: overlap" — 16 tests after the review of US1 (2026-10-21): each tab keeps its own connection, text and results, and a switch updates the status bar, database selector and Schema panel within 1 s (also a PostgreSQL tab beside a SQL Server tab); a database change in one tab leaves the other's alone; 30 runs across three tabs land in their own panes; a reload restores five tabs with order, names, text, marks and bindings; a restored tab whose password is not remembered asks once (PostgreSQL; the SQL-auth variant stays manual, quickstart M4); close with Save / Don't save / Cancel, Delete on the focused tab, closing a running tab; temp-table isolation; overlapping runs; History keeps each tab's runs under its own session; an engine restart with two tabs connected — each tab's next run reconnects with nothing typed (its first run finds the session gone, data-model §3)
- [X] T024 [P] [US1] Update `tests/AkmlSql.Web.E2E.Tests/DocumentFlowTests.cs` (`New_query_…` expects a second tab `doc-tab[data-active=true]` named SQLQuery2 and the first tab still modified) and `tests/AkmlSql.Web.Tests/History/HistoryPageKindTests.cs:313-314` (Re-execute writes a tab record with `AutorunPending`; `Editor.AutorunKind` is gone)
- [X] T025 [P] [US1] Add Ctrl+Alt+PageDown, Ctrl+Alt+PageUp and Ctrl+Alt+W to `tests/AkmlSql.Web.E2E.Tests/NativeKeyAcceptanceTests.cs` for Chromium, Edge and Firefox (`-IncludeNative`)

### Implementation for User Story 1 — models and services

- [X] T026 [P] [US1] Create `src/AkmlSql.Web/Services/ConnectionBinding.cs` (data-model §4: fields, `FromTarget(ConnectionTarget, savedId)`, `ToTarget(password)`, `RememberOnEngine` JSON name, no password member)
- [X] T027 [P] [US1] Create `src/AkmlSql.Web/Services/QueryTabSession.cs` (data-model §3 and R2): engine session id, binding, state, `ConnectAsync(target, bridge)`, `ReattachAsync` (disconnect → await reply → connect; password prompt callback), `DisconnectAsync(release)`, `ChangeDatabaseAsync`, `ReportCurrentDatabase`, `ReportTransactionState`, `ConnectionGeneration`, `StateChanged`, `IsBusy` (run in flight blocks connection changes)
- [X] T028 [US1] Create `src/AkmlSql.Web/Services/QueryTabs.cs` (`IQueryTabs` + `QueryTab`, data-model §1–§2): set, active id, cap 20, naming through `EditorDocument`, `NewAsync(inheritFrom)` (connects at once when no password is needed, else leaves `NeedsReattach`), `OpenInNewTabAsync(text, name, kind, autorun)` with the reuse rule, `CloseAsync`, `ActivateAsync`, `Changed` event; register as a singleton in `src/AkmlSql.Web/Program.cs` (depends on T026, T027)
- [X] T029 [US1] Generalise `src/AkmlSql.Web/Services/IEditorSessionStore.cs`: per-key pending map and debounce, `EditorTabRecord` / `EditorTabSetRecord` (data-model §5), `SaveTabAsync` / `RestoreTabAsync` / `DeleteTabAsync` / `SaveSetAsync` / `RestoreSetAsync` / `FlushAsync` over `StoreNames.EditorTabs`; keep the legacy `editorSession/current` read for the migration; `SaveTabAsync` is a no-op for an id missing from the set record; make T014 pass
- [X] T030 [US1] Add the one-time migration (data-model §5) to `src/AkmlSql.Web/Services/EditorSessionKeys.cs` (`MigrateLegacyAsync` → tab 1 + set, clears `current`) and move `NextDefaultNumber` to the set record; make T015 pass
- [X] T031 [US1] Make `src/AkmlSql.Web/Services/EditorDocumentFlow.cs` per tab: one instance per `QueryTab` (constructed by `IQueryTabs`, not a DI singleton — remove the singleton registration in `src/AkmlSql.Web/Program.cs`), `New` creates a tab through `IQueryTabs`, `Open` follows FR-011, `OpenQueryAsync` writes a tab record, `PersistAsync` writes the tab's record; make T012 and T018 pass
- [X] T032 [US1] Turn `src/AkmlSql.Web/Services/ISqlConnectionService.cs` into the active-tab facade (R1): properties and `StateChanged` delegate to `IQueryTabs.Active.Session`; `StateChanged` also raised on activation; `ConnectAsync` / `DisconnectAsync` / `ChangeDatabaseAsync` / `SendDocumentAsync` gain overloads taking a `QueryTabSession`; `TestAsync`, `ListDatabasesAsync`, credential calls and the guard unchanged; make T013 pass
- [X] T033 [US1] Make `src/AkmlSql.Web/Services/IQueryExecutionService.cs` take the `QueryTabSession` on `ExecuteAsync`, `ParseAsync`, `CancelAsync`, `ApplyAsync` (session id, kind, `ReportCurrentDatabase`, `ReportTransactionState` all from the passed session; no facade read after an await)
- [X] T034 [P] [US1] Pass the tab session into `src/AkmlSql.Web/Services/PostgresFormat.cs` and `src/AkmlSql.Web/Services/PostgresAnalysis.cs` (session id on `FormatRequest`, `FormatPreviewRequest`, `CodeAnalysisRequest`) through the formatter / analyser service signatures the editor page calls — as built (review of US1): a tab is analysed only while it is the active one, checked again right after its text is read; a tab formatted or refreshed while another is shown is analysed when it is shown again, whichever way it comes on screen. The services still read the active session, so no signature takes the tab's session.
- [X] T035 [P] [US1] Make the offline paths of `src/AkmlSql.Web/Services/IQuickInfoService.cs` and `src/AkmlSql.Web/Services/ISignatureHelpService.cs` read the active tab's text from `IQueryTabs` instead of the legacy `current` record
- [X] T036 [P] [US1] Make `src/AkmlSql.Web/Services/IWorkspaceStatus.cs` the active tab's view: each `QueryTab` holds phase, outcome, elapsed start and kept result; `IQueryTabs` republishes the active tab's values on activation and on that tab's changes; `SetCaret` unchanged
- [X] T037 [P] [US1] Add `editor:next-tab`, `editor:previous-tab`, `editor:close-tab` to `src/AkmlSql.Web/Services/WorkspaceKeyMap.cs` with the keys of `contracts/web-ui.md` §2; make T019 pass

### Implementation for User Story 1 — engine

- [X] T038 [US1] Implement R3 in `src/AkmlSql.Engine/Transports/WebSocketTransport.cs` per the S1 record: notifications inline, requests dispatched off the read loop, replies under the send lock, cancellation of in-flight handlers on socket close; make T021 pass
- [X] T039 [US1] Implement `Release` in `src/AkmlSql.Engine/Handlers/Control/SessionDisconnectHandler.cs`: after today's close, `SessionManager.RemoveSession`, `SchemaCacheManager.RemoveSession` (from S3) and provider invalidation; add `RemoveSession` to `src/AkmlSql.Engine/Schema/SchemaCacheManager.cs`; make T022 pass
- [X] T040 [P] [US1] Document `SessionDisconnect.Release` and the transport guarantees of `contracts/ipc.md` §2 in `doc/ipc-api.md`

### Implementation for User Story 1 — editor, panes, strip

- [X] T041 [US1] Implement `switchTab`, `closeTab`, and tab-aware `setText` / `replaceTextIfUnchanged` / `setDiagnostics` / `clearDiagnostics` in `src/AkmlSql.Web/wwwroot/js/akml-editor.js` per the S2 record (stored-state map per instance, callback suppression flag, compartment re-apply for dialect and options, scroll save/restore); make T020 pass
- [X] T042 [US1] Add `SwitchTabAsync(tabId, seedText, kind)`, `CloseTabAsync(tabId)` and the `tabId` overloads of `SetTextAsync`, `ReplaceTextIfUnchangedAsync`, `SetDiagnosticsAsync`, `ClearDiagnosticsAsync` to `src/AkmlSql.Web/Shared/EditorComponent.razor`; stamp completion, quick info, signature, go-to-definition, wildcard and `SendDocumentAsync` with the owning tab's session (T032) captured when the request starts
- [X] T043 [US1] Extend `src/AkmlSql.Web/Shared/TabItem.cs` (`IsModified`, `Closable`) and `src/AkmlSql.Web/Shared/TabStrip.razor` (`OnClose`, closable markup, Delete and middle click, scroll-active-into-view) with the strip CSS in `src/AkmlSql.Web/wwwroot/css/components/shell.css` (180 px truncation, modified mark, close button, hover) using tokens only; make T016 pass
- [X] T044 [US1] Parameterise `src/AkmlSql.Web/Shared/ResultsPaneComponent.razor` (`IdPrefix`, `IsActive`, `RunStartedAt`, active-only test ids) and `src/AkmlSql.Web/Shared/ResultSetGrid.razor` (`IdPrefix` from the pane; Apply uses the `QueryTabSession` passed with the result's `ConnectionStamp`); make T017 pass

### Implementation for User Story 1 — the editor page and its neighbours

- [X] T045 [US1] Restructure `src/AkmlSql.Web/Pages/Editor.razor` state: move the per-document fields (text, findings, error mark, result, generation, executing, active query id, last executed SQL, received at, autorun) into `QueryTab`; the page keeps `Tabs`, `Active` and the layout; `OnInitializedAsync` restores the set (migration first), builds tabs with `NeedsReattach`, mounts, activates the saved tab without engine traffic
- [X] T046 [US1] Replace `.akml-docbar` in `src/AkmlSql.Web/Pages/Editor.razor` with the closable `TabStrip` (`IdPrefix="doc"`, `Name="doc"`, test ids of `contracts/web-ui.md` §1, `doc-name` / `doc-modified` on the active tab), `doc-new` disabled at the cap with the limit tooltip, page title from the active tab
- [X] T047 [US1] Render one `ResultsPaneComponent` per tab in `src/AkmlSql.Web/Pages/Editor.razor` inside a keyed loop (`@key=tab.Id`, `hidden` when inactive, `IdPrefix="akml-results-{id}"`, `IsActive`, `RunStartedAt`); `NavigationLock` and `ConfirmDiscardPendingEditsAsync` count pending edits across every pane
- [X] T048 [US1] Rewire execute, parse, cancel, re-run-after-Apply, format, analyse and the history record in `src/AkmlSql.Web/Pages/Editor.razor` to capture `tab` and `tab.Session` before the first await (plan › Design notes › Execute); `FormatRequestGate` tickets keyed by the tab's session key; replies for a non-active tab go to that tab's stored editor state and pane
- [X] T049 [US1] Implement tab switch in `src/AkmlSql.Web/Pages/Editor.razor` (plan › Design notes › Tab switch): `IQueryTabs.ActivateAsync` → `SwitchTabAsync` → republish status → set record; disable the connection and database controls of a tab whose run is in flight
- [X] T050 [US1] Implement tab close in `src/AkmlSql.Web/Pages/Editor.razor`: pending-edit and modified confirmations (`doc-close-dialog` with Save / Don't save / Cancel through `IDialogService`), the open-transaction warning, `Session.DisconnectAsync(release: true)`, `CloseTabAsync`, record delete, set write, activate the previous tab; wire `editor:close-tab` (title "Close query tab", group File), Delete and middle click; if the tab's run is in flight, send cancel (best effort), hide the tab, keep its session until the run's reply or deadline, then `DisconnectAsync(release: true)`
- [X] T051 [US1] Implement New / Open / Save / Save as per tab in `src/AkmlSql.Web/Pages/Editor.razor` (`editor:new` inherits the active binding; Open into a new tab unless the active tab is empty and unmodified; Save / Save as act on the active tab) and register `editor:next-tab` ("Next query tab") / `editor:previous-tab` ("Previous query tab") in group View in `BuildContextActions`, titles exactly as `contracts/web-ui.md` §2
- [X] T052 [US1] Implement reattach and autorun per tab in `src/AkmlSql.Web/Pages/Editor.razor`: a restored tab reattaches through `Session.ReattachAsync` on activation when no password is needed, otherwise on its first run or Connect (password prompt through the existing credential dialog; a tab switch never prompts); a run on an unbound tab opens the connection dialog and continues after a successful connect (US1 scenario 7); `AutorunPending` consumed once the tab is connected
- [X] T053 [P] [US1] Make `src/AkmlSql.Web/Shared/MainLayout.razor` restart `SchemaSync` on the facade's `StateChanged` for the active tab's session (stop / start as today) and ignore changes of inactive tabs
- [X] T054 [P] [US1] In `src/AkmlSql.Web/Shared/StatusBar.razor`: `RollbackAsync` captures the active `QueryTabSession` before the await; the one-time auto-restore binds the active tab only when it has no binding (R19); outcome and rows segments read the active tab's status
- [X] T055 [P] [US1] In `src/AkmlSql.Web/Shared/DatabaseSelectorComponent.razor`: disabled while the active tab's run is in flight (`Session.IsBusy`) with the tooltip "Wait for the current run to finish"
- [X] T056 [P] [US1] In `src/AkmlSql.Web/Shared/ConnectionManagerModal.razor`: `Connect` binds the tab that opened the dialog (`IConnectionManagerController` carries the requesting tab; default the active tab) and builds the `ConnectionBinding` with the saved id; the transaction confirmation reads that tab's session
- [X] T057 [P] [US1] In `src/AkmlSql.Web/Shared/Settings/ConnectionsSection.razor`: Connect binds the active tab; Disconnect sends `Release=false` for the active tab; the connected summary reads the active tab
- [X] T058 [P] [US1] In `src/AkmlSql.Web/Pages/History.razor`: `OpenInEditorAsync` and `ReExecuteAsync` call `IQueryTabs.OpenInNewTabAsync` (R11) and navigate to `/` without the `?autorun` query string; the "ran elsewhere" suggestion reads the active tab
- [X] T059 [P] [US1] In `src/AkmlSql.Web/Shared/Settings/EditorSection.razor`: "Reset session" clears every `editorTabs` record and the legacy record through `EditorSessionKeys.ResetAsync`
- [ ] T060 [US1] Add the three tab keys to the keyboard help (`src/AkmlSql.Web/wwwroot/js/akml-ui.js` help text source or the component that feeds it) and make T023, T024 and T025 pass (serial browser run, Debug builds)

**Checkpoint**: US1 complete — quickstart §1 "Browser: tabs" and "Browser: overlap" green; M1–M7 and M15 pass by hand.

---

## Phase 4: User Story 2 — Query text that reads well in light and dark (Priority: P2)

**Goal**: explicit line height matched to SSMS, a short font list, syntax and chrome colours that
meet contrast in every theme, the same typography on every SQL surface.

**Independent Test**: the same script in light and dark at the default size compared with SSMS 22
(quickstart §1 "Browser: typography" and "Browser: contrast", §2 M10–M11).

### Tests for User Story 2 (write first; they must fail)

- [X] T061 [P] [US2] Extend `tests/AkmlSql.Web.Tests/js/akml-editor.options.test.mjs`: `fontFamily` option normalises to the four enum stacks and `applyEditorFont` sets / clears `--akml-editor-font-family`
- [X] T062 [P] [US2] Extend `tests/AkmlSql.Web.Tests/Services/EditorSettingsStoreTests.cs`, `tests/AkmlSql.Web.Tests/Settings/SettingsPortabilityTests.cs` (export carries `fontFamily`; an old export without it keeps the current value) and `tests/AkmlSql.Web.Tests/Settings/SettingsResetTests.cs` (reset text "Default font, 13 px, off and 4")
- [X] T063 [P] [US2] Add a generator test (`tests/AkmlSql.Site.Tests/ThemeCssSyncTests.cs` or a new `ThemeGeneratorLineHeightTests.cs` next to it) asserting the generated themes declare `--akml-type-editor-line-height: 1.25` and the four new `--akml-editor-*` colours
- [X] T064 [P] [US2] Extend `tests/AkmlSql.Web.E2E.Tests/WorkspaceAccessibilityTests.cs` with the pairs of `contracts/theme-and-settings.md` §5 (six syntax pairs, active line, selection, matching bracket, search match) per theme
- [X] T065 [P] [US2] `tests/AkmlSql.Web.E2E.Tests/EditorTypographyTests.cs`: computed `line-height` of `.cm-content` = 1.25 × font size in light and dark; changing the font and the size in Settings updates the editor, the History preview, the Styles output and the completion popup; the gutter number baseline matches the text baseline

### Implementation for User Story 2

- [X] T066 [US2] Edit `docs/theme-tokens.json`: `typography.editor.lineHeight = 1.25`; `Editor.Selection`, `Editor.ActiveLine`, `Editor.MatchingBracket`, `Editor.SearchMatch` with light / dark / high-contrast values measured with the accessibility probe (record the measured ratios in the token `description`); `Syntax.String.light = #15803D`, `Syntax.Comment.dark = #8A99AF`; re-measure `Syntax.Number.light` and `Syntax.TableColumn.light` against `#FFFFFF` and darken below 4.5:1 (R13); write the final hex values and measured ratios back into `contracts/theme-and-settings.md` §1 and `data-model.md` §7 so no "measured" placeholder remains
- [X] T067 [US2] Add the `lineHeight` branch to `scripts/generate-theme-css.ps1` typography emission; regenerate `src/AkmlSql.Web/wwwroot/css/themes/*.css` and `src/AkmlSql.Site/wwwroot/css/themes/*.css`; run `-CheckOnly` for both; make T063 pass
- [X] T068 [P] [US2] Update the drift comments in `src/AkmlSql.Shell.Shared/Ui/Theme/ThemePalette.cs` (light String, dark Comment) to say the JSON now matches; no colour change on the desktop (FR-022; the shell project text only)
- [X] T069 [US2] Add `FontFamily` to `src/AkmlSql.Web/Services/IEditorSettingsStore.cs` (enum `EditorFontFamily`, default Default, unknown → Default), the `editor-font-family` row in `src/AkmlSql.Web/Services/Settings/SettingsCatalog.cs` and `src/AkmlSql.Web/Shared/Settings/EditorSection.razor`, `EditorDto.FontFamily` and the reset text in `src/AkmlSql.Web/Services/Settings/SettingsSections.cs`; make T062 pass
- [X] T070 [US2] In `src/AkmlSql.Web/wwwroot/js/akml-editor.js`: `fontFamily` in `normaliseEditorOptions`, `applyEditorFont` setting `--akml-editor-font-family` next to `applyFontSize`, called from `create` and `setEditorOptions`; make T061 pass
- [X] T071 [US2] In `src/AkmlSql.Web/Shared/EditorComponent.razor`: `ToJs` passes `fontFamily`; style block sets `.akml-editor .cm-scroller { line-height: var(--akml-type-editor-line-height) }`, the font family from `var(--akml-editor-font-family, var(--akml-type-editor-family))`, and token-driven `.cm-cursor`, `.cm-selectionBackground`, `.cm-activeLine`, `.cm-matchingBracket`, `.cm-searchMatch`; quick-info and peek bodies read the runtime size variable
- [X] T072 [P] [US2] In `src/AkmlSql.Web/Pages/Editor.razor` completion popup CSS (`.cm-tooltip-autocomplete`): runtime font family and size variables instead of the fixed `--akml-type-editor-size`
- [X] T073 [P] [US2] In `src/AkmlSql.Web/Pages/History.razor`: `.akml-hist-preview` and the compare `<pre>`s read the runtime font variables and the line-height token; the token classes of the preview spans switch to `--akml-syntax-*` (keyword, string, comment, number as applicable)
- [X] T074 [P] [US2] In `src/AkmlSql.Web/Pages/Styles.razor` (`.akml-preview-input`, `.akml-preview-output`: drop the fixed 13 px / 1.45), `src/AkmlSql.Web/Pages/Snippets.razor`, `src/AkmlSql.Web/Shared/AiChatPanel.razor` (`.akml-chat-turn-text`), `src/AkmlSql.Web/Shared/AiPanel.razor`, `src/AkmlSql.Web/Shared/RefactorPreviewPanel.razor`: runtime font family / size variables and the line-height token
- [X] T075 [US2] Run `WebCssTokenTests` and shrink the per-file literal allowlists where literals were removed; make T064 and T065 pass; document the font setting in `doc/configuration.md`

**Checkpoint**: US2 complete — "Browser: typography" and "Browser: contrast" green; M10–M11 confirmed by the user.

---

## Phase 5: User Story 3 — One Connect to database dialog (Priority: P3)

**Goal**: one "Connect to database…" entry point and title, a regrouped form that fits 1280x720,
neutral wording, the trust choice saved, restored and honoured.

**Independent Test**: from a fresh profile, connect to a self-signed SQL Server through the single
entry point, save with Trust ticked, reload, pick the row, connect; then PostgreSQL from the same
dialog (quickstart §1 "Browser: connect dialog", §2 M8, M14).

### Tests for User Story 3 (write first; they must fail)

- [X] T076 [P] [US3] Update `tests/AkmlSql.Web.Tests/Shell/ConnectionManagerModalKindTests.cs:110,146` to the constant title "Connect to database"; add assertions for the four group test ids and that the Security group renders for both kinds; keep `:484` (no "SQL Server" in PostgreSQL hints) green
- [X] T077 [P] [US3] Add a palette test in `tests/AkmlSql.Web.Tests/Shell/` (next to `ToolbarTitleTests.cs`) asserting exactly one command whose title starts with "Connect to" and that its id is `sql:connect`
- [X] T078 [P] [US3] `tests/AkmlSql.Web.E2E.Tests/ConnectDialogTests.cs`: one palette match for "connect"; title constant across kinds; trust ticked → Save → reload → row restored with the box ticked → Connect without a certificate error (local server: assert the flag reaches the connection string through the engine's test endpoint or a status probe); loopback hint disables the box; no inner scroll of the SQL Server form at 1280x720 (`scrollHeight == clientHeight`); wording search finds no "SQL Server" in the strings of `contracts/web-ui.md` §5; the restored-tab reattach case is tagged to run after US1; PostgreSQL: TLS mode `verify-ca` and a CA path saved → reload → row restored with both values → the connect request carries them
- [X] T079 [P] [US3] Update the pinned strings in `tests/AkmlSql.Web.Tests/Bridge/SchemaTreeComponentTests.cs:380` and `tests/AkmlSql.Web.E2E.Tests/WorkspaceLayoutTests.cs:443` if their assertions include the renamed text

### Implementation for User Story 3

- [X] T080 [US3] In `src/AkmlSql.Web/Shared/CommandPalette.razor`: keep `sql:connect` titled "Connect to database…" opening the dialog on the active tab's kind (SQL Server when none); remove `sql:connect-postgresql`; make T077 pass
- [X] T081 [US3] In `src/AkmlSql.Web/Shared/ConnectionManagerModal.razor`: title constant; regroup into Server / Sign-in / Database / Security with `conn-group-*` ids and one-line hints (R16); PostgreSQL port beside the host; Security visible without inner scroll at 1280x720 for SQL Server; trust checkbox disabled with the loopback hint; make T076 pass
- [X] T082 [P] [US3] Rename the "means the server" strings of `contracts/web-ui.md` §5 in `src/AkmlSql.Web/Shared/ConnectionPickerComponent.razor`, `src/AkmlSql.Web/Services/EngineConnectDiagnosis.cs`, `src/AkmlSql.Web/Shared/StatusBar.razor` (two), `src/AkmlSql.Web/Pages/Editor.razor` (Execute tooltip), `src/AkmlSql.Web/Shared/SchemaTreeComponent.razor`, `src/AkmlSql.Web/Shared/EditorComponent.razor`, `src/AkmlSql.Web/Shared/Settings/ConnectionsSection.razor` (three), `src/AkmlSql.Web/Services/ISqlConnectionService.cs`, and the same-family lines in `DiagnosticsSection.razor`, `StatusBar.razor` aria-label, `ISqlConnectionService.cs:624`
- [X] T083 [P] [US3] Update the served docs `doc/deployment.md` ("Connect to database") and `doc/topics/getting-started.md`; add a note to `specs/042-postgresql-support/contracts/web-ui.md` that the `sql:connect-postgresql` row is superseded by 043
- [ ] T084 [US3] Make `tests/AkmlSql.Web.E2E.Tests/ConnectDialogTests.cs` (T078) and the T079 updates pass in a serial browser run; confirm the restored-tab reattach case in the same class once US1 is in

**Checkpoint**: US3 complete — "Browser: connect dialog" green; M8 and M14 pass by hand.

---

## Phase 6: User Story 4 — Every screen fits the dark theme (Priority: P4)

**Goal**: no light surface, unreadable text or clipped content in the dark theme on any page,
dialog, menu or popup; the user's screenshots fixed first; high contrast still passing.

**Independent Test**: the extended screenshot tour in dark at two sizes reviewed against FR-031 to
FR-034 (quickstart §1 "Browser: dark walk", §2 M12). Best run after US2 and US3 so their surfaces
are covered.

### Tests for User Story 4 (write first; they must fail)

- [X] T085 [P] [US4] Extend `tests/AkmlSql.Web.E2E.Tests/WorkspaceScreenshotTour.cs` with the surfaces of `contracts/web-ui.md` §8 (connection dialog both kinds, command palette, cell value dialog, schema context menu, grid tooltip, completion, quick info, Find panel, History compare, Snippets listbox, a notification) in the dark theme at 1280x720 and 1920x1080, writing to `specs/043-web-query-tabs-polish/baseline/dark/`
- [X] T086 [P] [US4] Add a dark-theme assertion to `tests/AkmlSql.Web.E2E.Tests/WorkspaceAccessibilityTests.cs`: `getComputedStyle(document.documentElement).colorScheme` is `dark` in the dark theme and `light` in the light theme; a checkbox's computed `color-scheme` follows it

### Implementation for User Story 4

- [X] T087 [US4] Add `color-scheme: dark` under `:root[data-akml-theme="dark"]` and `color-scheme: light` under `:root[data-akml-theme="light"]` in `src/AkmlSql.Web/wwwroot/css/app.css` (R17; high contrast untouched); make T086 pass
- [ ] T088 [US4] Record the user's screenshots (clarification Q3) under `specs/043-web-query-tabs-polish/baseline/user/` and list each screen with its symptom in `spec.md` › Clarifications and in `baseline.md`; fix each one first, re-capture, and mark it for the user's confirmation
- [X] T089 [US4] Run the extended tour (`scripts/run-playwright.ps1 -IncludeScreenshots -Filter WorkspaceScreenshotTour`), review every capture in `specs/043-web-query-tabs-polish/baseline/dark/` against FR-031 to FR-033, and log each defect (surface, symptom, token or layout cause) in `baseline.md`
- [X] T090 [US4] Fix the logged defects in their components (tokens through `docs/theme-tokens.json` + regeneration where a colour is wrong; layout in the component's style block where content clips), re-run the tour, and update `baseline.md` with the after captures
- [ ] T091 [US4] Re-run `tests/AkmlSql.Web.E2E.Tests/WorkspaceAccessibilityTests.cs` for high contrast and the light / high-contrast variants of `tests/AkmlSql.Web.E2E.Tests/WorkspaceScreenshotTour.cs` to prove FR-035 (no regression from the dark fixes); record in `specs/043-web-query-tabs-polish/baseline.md`

**Checkpoint**: US4 complete — SC-009's automated part green; the user confirms their screens.

---

## Phase 7: User Story 5 — Browser writing assistants stay out of the editor (Priority: P5)

**Goal**: Grammarly-family and LanguageTool opt-outs on the SQL editor and the two SQL text
boxes; the chat composer untouched; one help line.

**Independent Test**: with Grammarly active, a 20-line script in the editor shows no overlay,
underline or suggestion while the chat composer still does (quickstart §2 M13; attributes checked
by `EditorTypographyTests`).

### Tests for User Story 5 (write first; they must fail)

- [X] T092 [P] [US5] `tests/AkmlSql.Web.Tests/js/akml-editor.attributes.test.mjs`: after `create`, `.cm-content` carries `data-gramm="false"`, `data-gramm_editor="false"`, `data-enable-grammarly="false"`, `data-lt-active="false"` and CodeMirror's `spellcheck="false"`
- [X] T093 [P] [US5] Add bUnit assertions in `tests/AkmlSql.Web.Tests/Shell/WritingAssistantOptOutTests.cs` (the Shell folder holds cross-component checks; there is no Pages folder) for `Pages/Snippets.razor` (`#akml-snippet-body`) and `Pages/Styles.razor` (`.akml-preview-input`) carrying the four `data-*` attributes, and for `Shared/AiChatPanel.razor`'s composer **not** carrying them
- [X] T094 [P] [US5] Add the attribute checks for `.cm-content` and the two textareas to `tests/AkmlSql.Web.E2E.Tests/EditorTypographyTests.cs` (one test per surface)

### Implementation for User Story 5

- [X] T095 [US5] Add `cm.view.EditorView.contentAttributes.of({...})` with the four attributes to the extensions list in `src/AkmlSql.Web/wwwroot/js/akml-editor.js` (R14; no bundle rebuild); make T092 pass
- [X] T096 [P] [US5] Add the four attributes to the textareas in `src/AkmlSql.Web/Pages/Snippets.razor` and `src/AkmlSql.Web/Pages/Styles.razor`; make T093 pass
- [X] T097 [P] [US5] Add the help line of `contracts/web-ui.md` §7 to the keyboard help source (`src/AkmlSql.Web/wwwroot/js/akml-ui.js` or the component that supplies its text)
- [ ] T098 [US5] Make T094 pass; perform M13 with Grammarly installed in Chromium and Edge and record the result in `specs/043-web-query-tabs-polish/baseline.md`

**Checkpoint**: US5 complete — SC-010 recorded.

---

## Phase 8: Polish & cross-cutting concerns

- [ ] T099 [P] Update `CLAUDE.md` (query tabs, the three tab keys, IndexedDB version 5 and the `editorTabs` store, the line-height token, the facade rule, the reattach order) and `doc/progress.md` (phase tables for 043; deferred items: idle-session sweep in the engine, saved-query list declined by the user; mid-run cancel turned out to work, see Deferred)
- [X] T100 [P] Write `doc/WEB/workspace-043-release-notes.md` (tabs, dialog, typography, dark theme, assistants; keys; upgrade note for the storage version)
- [ ] T101 Run the full quickstart §1 gate (every row) after Debug and Release builds; compare with `specs/043-web-query-tabs-polish/baseline/before/` through `scripts/compare-test-results.ps1`; record in `baseline.md`
- [ ] T102 Measure and record in `baseline.md`: tab switch to status-bar / schema-panel update (SC-003, 20 switches), editor state swap time for a 10 MB document, page load with 20 restored tabs (no engine traffic until activation)
- [ ] T103 Run `pwsh scripts/generate-theme-css.ps1 -CheckOnly` for web and site, `WebCssTokenTests`, `ShortcutCollisionTests`, `CorpusGateTests` and the format goldens as the final no-regression proof; record in `baseline.md`
- [ ] T104 Deploy the web edition and the engine from this branch to the user's machine (user approval first; full engine publish) and walk quickstart §2 M8, M10, M12, M13 with the user; record outcomes in `baseline.md`

---

## Deferred (recorded with reasons; constitution Development Workflow)

- **Idle session sweep in the engine**: sessions abandoned by a closed browser window still
  accumulate (only `Release` on tab close removes them); a timer-driven sweep is engine work
  outside this feature's scope.
- **Mid-run cancel**: works as a side effect of R3 (review of US1: `RunningStatementCancelIntegrationTests`, `ResultsMessagesTests.Cancel_stops_a_running_statement_and_the_batches_after_it`); not a requirement of this feature
  (spec Out of Scope).
- **Shared engine schema cache across same-server tabs**: rejected in R4 (SSMS precedent, per-session design); revisit only if Phase A loads per tab prove slow in practice.
- **Carrying the typography to SSMS**: the user will ask separately (spec Out of Scope); FR-022
  keeps the tokens shared.

---

## Dependencies & Execution Order

### Phase dependencies

- **Phase 1 (Setup)**: no dependencies. T001–T003 can run together.
- **Phase 2 (Foundational)**: T004–T005 first (tests), then T006 → T007; T008 in parallel with T006. Blocks US1 only.
- **US1 (Phase 3)**: T009–T011 (spikes) before T038, T039, T041; tests T012–T025 before their implementation tasks; services T026 → T027 → T028 → (T029, T030) → T031 → T032 → T033 → (T034–T037 in parallel); engine T038, T039 after S1/S3; editor T041 → T042; strip T043; panes T044; page T045 → T046 → T047 → T048 → T049 → T050 → T051 → T052; neighbours T053–T059 in parallel after T032; T060 last.
- **US2 (Phase 4)**: independent of US1. T066 → T067 → (T068–T074) → T075.
- **US3 (Phase 5)**: independent of US1 except the reattach case of T078/T084. T080–T083 in parallel after the tests; T084 last.
- **US4 (Phase 6)**: best after US2 and US3 (their surfaces are part of the walk); T087 first (it fixes the confirmed defect), T088 as soon as the user's screenshots arrive, T089 → T090 → T091.
- **US5 (Phase 7)**: independent; T095–T097 in parallel after the tests; T098 last.
- **Polish (Phase 8)**: after every story the release includes; T104 needs the user's approval.

### Cross-story touch points (soft dependencies)

- `EditorComponent.razor` is edited by US1 (T042), US2 (T071) and US5 (none; JS only). Land US2's
  style-block change and US1's API change as separate edits of the same file.
- `Editor.razor` is edited by US1 (T045–T052), US2 (T072) and US3 (T082, one tooltip string).
- `ConnectionManagerModal.razor` is edited by US1 (T056) and US3 (T081): do T081's regroup first if
  both are in flight, then T056's binding change.
- `WorkspaceAccessibilityTests.cs` grows in US2 (T064) and US4 (T086).
- `akml-editor.js` is edited by US1 (T041), US2 (T070) and US5 (T095): all additive; keep the
  extensions list order (contentAttributes before the language compartment is fine).

### Within each story

- Tests first; they must fail; spikes record before the design they gate is built.
- Models and services before components; components before the page; the page before the
  browser tests.

### Parallel opportunities

- Phase 1: T001, T002, T003.
- Phase 2: T004 ∥ T005; T006 ∥ T008.
- US1 tests: T012–T025 all parallel. US1 services: T034, T035, T036, T037 after T033. US1
  neighbours: T053–T059 after T032/T044.
- US2: T061–T065 parallel; T068, T072, T073, T074 parallel after T067.
- US3: T076–T079 parallel; T082, T083 parallel with T080/T081.
- US5: T092–T094 parallel; T096, T097 parallel with T095.
- Stories: US2, US3 and US5 can run in parallel with US1 by different people; US4 waits for US2 and US3.

---

## Parallel Example: User Story 1 (tests)

```bash
# After Phase 2, launch the US1 test files together (different files, no shared state):
Task: "QueryTabsTests.cs"                 # tests/AkmlSql.Web.Tests/Editor/
Task: "QueryTabSessionTests.cs"           # tests/AkmlSql.Web.Tests/Editor/
Task: "EditorTabStoreTests.cs"            # tests/AkmlSql.Web.Tests/Editor/
Task: "TabMigrationTests.cs"              # tests/AkmlSql.Web.Tests/Editor/
Task: "akml-editor.tabs.test.mjs"         # tests/AkmlSql.Web.Tests/js/
Task: "WebSocketTransportConcurrencyTests.cs"  # tests/AkmlSql.Engine.Tests/Transports/
Task: "SessionDisconnectReleaseTests.cs"  # tests/AkmlSql.Engine.Tests/Handlers/
Task: "QueryTabsTests.cs (E2E)"           # tests/AkmlSql.Web.E2E.Tests/
```

## Parallel Example: User Story 2 (surfaces after the tokens exist)

```bash
# After T067 (regenerated themes):
Task: "ThemePalette.cs drift comments"    # src/AkmlSql.Shell.Shared/Ui/Theme/
Task: "Editor.razor completion popup CSS" # src/AkmlSql.Web/Pages/
Task: "History.razor preview fonts + syntax tokens"
Task: "Styles / Snippets / AiChatPanel / AiPanel / RefactorPreviewPanel font variables"
```

---

## Implementation Strategy

### MVP first (User Story 1)

1. Phase 1 and Phase 2 (one short session).
2. US1 spikes S1–S3 (half a day; they decide the transport and the editor swap).
3. US1 tests, then services, engine, editor, page, neighbours; stop at the checkpoint and run
   `QueryTabsTests` plus M1–M7.
4. Demo: three tabs on three connections, reload, overlap.

### Incremental delivery (what ships value soonest)

US2–US5 do not need US1. If the user wants visible progress before the tabs land, deliver in the
order the plan suggested: **US5** (one afternoon), **US3** (dialog and wording), **US2**
(typography), **US4** (dark walk, once US2 and US3 surfaces exist), then **US1**. Each story stops
at its checkpoint with its quickstart rows green.

### Parallel team strategy

One person on US1 end to end (it is one continuous refactor of `Editor.razor` and the services);
a second person on US5 → US3 → US2 → US4; merge US2's `EditorComponent.razor` style change before
US1's API change lands in the same file.

---

## Notes

- [P] tasks = different files, no dependencies.
- Keep every existing SQL Server behaviour and test id intact for the active tab.
- Verify tests fail before implementing; record spikes in research.md before building on them.
- Deliver uncommitted; ask "Ready to commit?" at each checkpoint.
