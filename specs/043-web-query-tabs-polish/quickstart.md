# Quickstart: Query tabs, one Connect to database dialog, and editor polish for the web edition

Acceptance gate for "done" (constitution, Development Workflow). Each scenario names the success
criterion it proves. Automated gates first, then the manual walk-through.

## 0. Environment

1. **SQL Server** on `localhost:1433` with Northwind and `tempdb` (as for specs 041 and 042). Two
   databases on one server are enough for the automated three-tab gate; a second SQL Server
   instance is only needed for the manual three-server variant (SC-001).
2. **PostgreSQL** through `scripts/pg-test-server.ps1 -Port 58432` (the default 55432 is in a
   Windows-reserved range on this machine) and
   `AKML_PG_TEST_CONNECTION="Host=localhost;Port=58432;Database=northwind;Username=postgres;Password=akml"`.
3. Builds (Debug for the browser suites; a Release-only build silently tests stale code):
   ```bash
   "$MSBUILD" AKML-SQL.slnx -t:Restore -v:quiet
   "$MSBUILD" AKML-SQL.slnx -t:Build -p:Configuration=Release -m -v:minimal
   dotnet build src/AkmlSql.Engine/AkmlSql.Engine.csproj -c Debug
   dotnet build src/AkmlSql.Web/AkmlSql.Web.csproj -c Debug
   ```
4. Theme CSS regenerated after any token change:
   ```bash
   pwsh scripts/generate-theme-css.ps1
   pwsh scripts/generate-theme-css.ps1 -OutputFolder src/AkmlSql.Site/wwwroot/css/themes
   pwsh scripts/generate-theme-css.ps1 -CheckOnly
   ```
5. Browser suites run **serially** (`-- xUnit.ParallelizeTestCollections=false`) after every
   Debug build has finished; Playwright browsers installed (`playwright.ps1 install chromium`,
   plus `msedge` and `firefox` for the native-key checks).
6. For the manual assistant check: a browser profile with the Grammarly extension installed and
   enabled.

## 1. Automated gates

| Gate | Command | Proves |
| --- | --- | --- |
| Existing suites unchanged | `dotnet test tests/AkmlSql.Web.Tests`, `tests/AkmlSql.Engine.Tests`, `tests/AkmlSql.Core.Tests`, `tests/AkmlSql.Site.Tests`; `node --test "tests/AkmlSql.Web.Tests/js/*.test.mjs"` | no regression; SC-007's test updates |
| Theme token gate | `pwsh scripts/generate-theme-css.ps1 -CheckOnly` (web and site) | constitution II, FR-022 |
| CSS literal gate | `dotnet test tests/AkmlSql.Web.Tests --filter FullyQualifiedName~WebCssTokenTests` | no new literals |
| Shortcut table | `dotnet test tests/AkmlSql.Web.Tests --filter FullyQualifiedName~ShortcutCollisionTests` | the new keys are free of browser and CodeMirror keys |
| Tab model (unit) | `dotnet test tests/AkmlSql.Web.Tests --filter "FullyQualifiedName~QueryTab"` (tab set, naming, cap, persistence, migration, per-key debounce, binding snapshot, close confirmation flow, History open into a tab) | FR-001, FR-002, FR-007 to FR-011, FR-013, FR-015 |
| Editor state swap (node) | `node --test tests/AkmlSql.Web.Tests/js/akml-editor.tabs.test.mjs` (no text callback on swap; undo survives; a stored tab accepts replace-if-unchanged and diagnostics) | R6, FR-003 |
| IndexedDB upgrade (node) | `node --test tests/AkmlSql.Web.Tests/js/akml-indexeddb.versionchange.test.mjs` (pin moved to 5) | R8 |
| Engine transport (unit) | `dotnet test tests/AkmlSql.Engine.Tests --filter "FullyQualifiedName~WebSocketTransportConcurrency"` (two sessions overlap; notification-then-request ordering; send serialisation; `SessionDisconnect{Release}` removes the session and its caches) | FR-014, R3, R5 |
| Browser: tabs | `dotnet test tests/AkmlSql.Web.E2E.Tests --filter "FullyQualifiedName~QueryTabsTests"` (three tabs: Northwind, tempdb, PostgreSQL; each run lands in its own pane 30 times; switch updates status bar, database selector and schema panel within 1 s; reload restores five tabs; restored SQL-auth tab asks once; close with unsaved changes; temp table isolation; history naming) | SC-001, SC-002, SC-003, SC-004, US1 scenarios 1-11 |
| Browser: overlap | same class, `Overlapping_runs_show_their_own_state` (a `WAITFOR DELAY '00:00:05'` in tab 1 while tab 2 runs and completes) | FR-014 |
| Browser: connect dialog | `--filter "FullyQualifiedName~ConnectDialogTests"` (one palette command; constant title; groups; trust choice saved → reload → restored → connect; restored tab reattaches with the trust choice; loopback hint; fits 1280x720 without inner scroll) | SC-007, SC-008, US3 scenarios 1-8 |
| Browser: typography | `--filter "FullyQualifiedName~EditorTypographyTests"` (computed `line-height` = 1.25 × font size in both themes; font picker switches the editor, History preview, Styles output; completion popup follows the size setting; the six attributes present on `.cm-content` and both textareas) | FR-016, FR-017, FR-020, FR-021, FR-036, SC-006 (automated half) |
| Browser: contrast | `--filter "FullyQualifiedName~WorkspaceAccessibilityTests"` (18 + syntax + editor pairs, three themes) | SC-005, FR-018, FR-019, FR-032 |
| Browser: dark walk | `scripts/run-playwright.ps1 -IncludeScreenshots -Filter WorkspaceScreenshotTour` then review `specs/043-web-query-tabs-polish/baseline/dark/` | SC-009 (screens), FR-031, FR-033 |
| Browser: native keys | `--filter "FullyQualifiedName~NativeKeyAcceptanceTests"` with `-IncludeNative` (Ctrl+Alt+PageDown / PageUp / W in Chromium, Edge, Firefox) | FR-012, R9 |
| Corpus and golden ratchets | unchanged commands (`CorpusGateTests`, format goldens) | constitution III (no language change expected; proves it) |

## 2. Manual walk-through

| # | Steps | Expect | Proves |
| --- | --- | --- | --- |
| M1 | Open the editor; New query twice; connect tab 1 to Northwind, tab 2 to tempdb, tab 3 to PostgreSQL (Connect to database… from the palette each time) | three tabs, each with its own connection in the status bar; switching tabs switches the Schema panel, database selector, kind badge and completion dialect | US1 1-3 |
| M2 | In tab 1 run `CREATE TABLE #t (i int); SELECT 1`; in tab 2 run `SELECT * FROM #t` | tab 2 fails with "Invalid object name '#t'" (its own connection) | US1 8, FR-004 |
| M3 | Tab 1: `WAITFOR DELAY '00:00:10'; SELECT 1`, run; switch to tab 2 and run `SELECT 2` | tab 2 finishes while tab 1 is still running; both show their own state and outcome | FR-014 |
| M4 | Type in two tabs, reload the page | both come back with text, names, `*` marks and bindings; the first run in a SQL-auth tab asks for the password once | US1 5-6, SC-002 |
| M5 | Close a modified tab | "Save changes to {name}?"; Cancel keeps it; Don't save closes it; Save downloads the file then closes | US1 4, FR-007 |
| M6 | Start a transaction in a tab (`BEGIN TRAN`), close the tab | the open-transaction warning; on confirm the transaction is rolled back (check with `DBCC OPENTRAN` in another tab) | edge case |
| M7 | Open 20 tabs, press New | refused with the limit message | FR-013 |
| M8 | Connect to a remote SQL Server with a self-signed certificate from a fresh profile: tick Trust, Save, reload, pick the saved row, Connect; then reload again and run in that tab | no certificate error on either path; the checkbox is ticked after reload | SC-008, US3 4-5 |
| M9 | Three-server variant of M1 (second SQL Server instance) | as M1 | SC-001 manual |
| M10 | Light theme and dark theme: open a 40-line script at the default size; compare with SSMS 22 at Consolas 10 pt | line spacing within 10 percent; keywords, strings, numbers, comments, functions, names distinct; caret, selection, active line, matching bracket visible; the user confirms it "reads right" | SC-006, US2 1-3 |
| M11 | Settings › Editor: change the font and the size | editor, History preview, Format styles preview and completion popup follow | US2 4-6 |
| M12 | Dark theme: walk every page, dialog, menu and popup from the list in `codebase-map.md` §6 at 1280x720; compare with the user's screenshots | no light surface, no unreadable text, nothing clipped; the user confirms their screens | SC-009, FR-031 to FR-034 |
| M13 | With Grammarly active: type a 20-line script in the editor, then a sentence in the chat composer | no badge, underline or card in the editor; the composer still gets them | SC-010, US5 |
| M14 | Command palette: type "connect" | exactly one command, "Connect to database…"; search the UI for "SQL Server" where it means "the server" | SC-007 |
| M15 | History: Open in editor and Re-execute an entry while a modified tab is active | a new tab opens (no discard prompt); Re-execute runs once the tab is connected | R11, US1 10 |

## 3. Documentation and release

- `doc/ipc-api.md` (SessionDisconnect `Release`, transport behaviour), `doc/configuration.md`
  (editor font setting), `doc/WEB/` release notes for 043, `doc/deployment.md` and
  `doc/topics/getting-started.md` wording, `CLAUDE.md` (tabs, keys, DB version 5, line-height
  token), `doc/progress.md` (phase tables, deferred items: idle-session sweep in the engine,
  mid-run cancel) are updated in the same change.
- Deploy the web edition and the engine from this branch to the user's machine for M8, M10,
  M12 and M13 (a private engine update needs the user's approval, as for spec 042).
