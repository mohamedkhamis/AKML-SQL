# Baseline and verification log: spec 043

**Branch**: `043-web-query-tabs-polish` (on top of `042-postgresql-support`) | **Started**: 2026-10-21

## Before (T001, recorded 2026-10-21 at commit 3270667)

Debug builds, run on the development machine (SQL Server on localhost:1433, no PostgreSQL test server).
Raw `.trx` files: `%LOCALAPPDATA%\AKML SQL\spec043-tests\before\`. Pass-lists: [baseline/tests/before/](./baseline/tests/before/)
(written by `scripts/compare-test-results.ps1`).

| Suite | Passed | Failed | Skipped | Note |
| --- | --- | --- | --- | --- |
| Web (`tests/AkmlSql.Web.Tests`) | 1,681 | 0 | 0 | includes T003's reserved-key change, which passes |
| Engine (`tests/AkmlSql.Engine.Tests`) | 2,487 | 1 | 132 | 19 min 39 s; skips are the live PostgreSQL tests (no server) |
| Core (`tests/AkmlSql.Core.Tests`) | 1,458 | 1 | 1 | |
| Site (`tests/AkmlSql.Site.Tests`) | 910 | 1 | 0 | |
| JavaScript (`node --test`) | 137 | 0 | 0 | |
| Theme drift gate (web and site, `-CheckOnly -RepoRoot .`) | OK | | | under PowerShell 5.1 the script needs `-RepoRoot .` |

The three failures are pre-existing on the 042 branch and unrelated to this feature:

| Test | Message | Cause |
| --- | --- | --- |
| `Engine.PerformanceBaselineTests.Capture_or_compare_M0_baseline` | FormatRequest p50 399 ms against a 150 ms baseline | the known environmental and toolchain drift of the M0 gate; re-baseline only with the user's approval |
| `Core.Ipc.ProfileGetMessageTests.Response_key_layout_is_positional_and_append_only` | expected 7 keys, found 9 | the ProfileGet response gained two append-only keys earlier; the test's count was not moved |
| `Site.Docs.F1SlugTests.EveryOptionsPageHasItsOwnAnchor` | expected 30 anchors, found 27 | the SSMS Options redesign changed the page list; the docs' anchors lag |

The serial browser classes listed in T001 are run against the feature's own changes in each story instead (their
before-state is the deployed build captured below).

## Dark theme before (T002, deployed web and engine 1.26.1019.2313 on localhost:8083, 1280x720)

Captures: [baseline/before/](./baseline/before/) (`dark-editor.png`, `dark-history.png`, `dark-settings-general.png`,
`dark-settings-editor.png`, `dark-connect-sqlserver.png`, `dark-connect-postgresql.png`).

Defects confirmed on these captures:

1. **Native controls stay light.** The connection dialog's checkboxes and its inner scrollbar draw light on the dark
   panel; the page declares no `color-scheme` (computed `normal`), so the browser draws its own controls in light.
2. **The trust choice is below the fold.** The SQL Server form scrolls inside the 500 px dialog (473 px of content in a
   415 px pane); with Windows authentication the "Trust the server certificate" box is the last visible row and its
   help is cut off; with SQL Server authentication it is out of view.
3. **Wording.** The dialog title is "Connect to SQL Server" or "Connect to PostgreSQL"; the status bar reads "Engine
   connected — connect to SQL Server for schema-aware IntelliSense."

## Story log

Entries are added as each story reaches its checkpoint. Browser runs: Debug web and engine builds, serial, Chromium,
SQL Server on localhost:1433; raw logs under `%LOCALAPPDATA%\AKML SQL\spec043-tests\`.

### US5 — writing assistants (2026-10-21)

- `node --test` `akml-editor.attributes.test.mjs` 3/3 (failed first: the export did not exist); bUnit
  `WritingAssistantOptOutTests` 3/3; browser `EditorTypographyTests` (three assistant tests) 3/3.
- `.cm-content` carries `data-gramm`, `data-gramm_editor`, `data-enable-grammarly`, `data-lt-active` = false beside
  CodeMirror's own `spellcheck`/`autocorrect`/`autocapitalize`; the snippet body and the Format styles input carry the
  same four; the AI chat box carries none.
- The FR-038 note lives in Settings › Editor › Writing assistants (the keyboard help is a per-control tooltip).
- **Pending (manual, quickstart M13)**: typing with Grammarly installed and active, in Chromium and Edge. Needs a browser
  profile with the extension; not available to the automated run.

### US3 — one Connect to database dialog (2026-10-21)

- bUnit: `ConnectionManagerModalKindTests` (constant title; four fieldsets in order; the kind first; no positive
  tabindex; the trust choice in Security, disabled with the reason for a server on the engine's own computer) and
  `ConnectCommandTests` (one connect command, `sql:connect`, "Connect to database…").
- Browser `ConnectDialogTests` 6/6: one palette match; one title for both kinds; the SQL Server form fits 1280x720
  without an inner scroll with Windows and with SQL Server authentication (every group fully in view); the trust choice
  saved, restored after a reload, and refused for localhost; no visible "SQL Server" where it means the server; a
  PostgreSQL connection keeps `verify-ca` and its CA path. First run: two test-side fixes (close the dialog with its
  button; wait for "Saved" before reloading, the reload had raced the IndexedDB write).
- **Pending**: the restored-tab reattach with the trust choice (after US1); connecting to a remote self-signed server
  (manual, M8).

### US2 — typography and colours (2026-10-21)

Measured against `Surface.Editor` (WCAG 2.x), before → after:

| Pair | Light | Dark |
| --- | --- | --- |
| String | 3.30 → 5.02 (`#15803D`) | 8.40 |
| Number | 3.56 → 5.18 (`#C2410C`) | 6.46 |
| Table/column | 3.60 → 5.36 (`#0E7490`) | 8.09 |
| Comment | 4.76 | 3.07 → 5.05 (`#8A99AF`) |
| Keyword, function | 5.17, 5.38 | 5.75, 5.54 |
| Every class on the active line (`#F8FAFC` / `#21304A`) | ≥ 4.55 | ≥ 4.57 |
| Body text on selection (`#BFDBFE` / `#264F78`), on a find match (`#FDE68A` / `#5C4813`) | 12.6, 14.3 | 6.9, 7.1 |
| Bracket outline (`#2563EB` / `#60A5FA`) on the editor | 5.17 | 5.75 |

- Line spacing 1.25 (CodeMirror used 1.4; SSMS 22's Consolas renders 1.17 of the font size): browser test measures
  `line-height` = 1.25 × font size in light and dark, the line box equal to it, the gutter number on the same box.
- Bracket matching was not enabled at all; it is now (outlined).
- Settings › Editor › Font (Default, Cascadia Mono, Consolas, System monospace) reaches the editor, the completion list,
  the Format styles preview and, through the document root, every other page (browser test with Consolas at 16 px).
- Tests: node `akml-editor.options.test.mjs` 8/8 (failed first); bUnit settings store, portability and reset tests;
  `EditorThemeTokenTests` (generated variables and the measured pairs); browser `EditorTypographyTests` 3 typography
  tests and `WorkspaceAccessibilityTests.Editor_colours_meet_their_contrast_targets_in_light_and_dark`.
- **Pending (manual, M10)**: the user's side-by-side comparison with SSMS 22.

### US4 — dark theme (2026-10-21)

- `color-scheme: dark` / `light` on the theme attribute: browser test `Native_controls_follow_the_theme` (the page and
  the connection dialog's checkbox report the theme's scheme). Before: `normal`, a white checkbox and a light scrollbar.
- Dark walk (`WorkspaceScreenshotTour.Capture_the_dark_walk_of_pages_dialogs_menus_and_popups`): 42 captures in
  [baseline/dark/](./baseline/dark/) at 1280x720 and 1920x1080 — editor and results, grid context menu, both connection
  forms, command palette, completion, Find panel, a notification, every Settings section, History, Snippets (list and
  editor), Format styles. Not reachable in this environment and logged by the tour: the schema context menu (tempdb shows
  no object row in the sandbox).
- Defects found and fixed (contrast scan of every token text-on-background rule, then a unit test,
  `ThemeContrastScanTests`, that holds them at 4.5:1 in light and dark):

| Surface | Before (dark) | Fix |
| --- | --- | --- |
| Schema panel "stale" banner, TLS mismatch banner | white on the warning colour, 2.26:1 (light 3.56:1) | `--akml-badge-warning` |
| Capability notice icon, engine-connections badge, Problems count | white on the info colour, 2.54:1 | `--akml-accent-primary` |
| Startup error bar (`#blazor-error-ui`) | white on the danger colour, 2.77:1 | `--akml-badge-error` |
| Saved-connection kind badge | 4.04:1 | neutral badge tokens |
| Snippets "built-in" badge, profile picker gate badge, refactor preview badge | white on the secondary text colour, 2.6:1; the snippet badge wrapped to "built-/in" | neutral badge tokens, one line |

  Held at 3:1 by design (secondary or hover text, FR-032): the result-set header (4.04:1) and a navigation link on hover
  (4.07:1).
- High contrast: `WorkspaceAccessibilityTests.Themes_have_readable_token_pairs_focus_help_and_reduced_motion` passes in
  all three themes; it is run again in the final gate after these fixes (T091, T101).
- **Pending**: the user's screenshots (clarification Q3, T088).

### US1 — query tabs (2026-10-21)

- Unit: web 1,762 passed (tab strip closable mode, results pane per tab, editor component tab API including the creation
  race and the kept-state swap, query tabs and tab sessions, tab store and migration, History into tabs, status bar
  restore with tabs); JS 152 passed; engine concurrency and SessionDisconnect release tests passed.
- Browser: `QueryTabsTests` 7/7 (own connection, text and results per tab with the switch; temporary table isolation;
  overlapping runs; reload of three tabs; close with Save / Don't save / Cancel; the tab keys; a tab with no connection
  asks for one and then runs). Updated for spec 043 behaviour: `DocumentFlowTests` (New and Open no longer replace),
  `QuickstartAcceptanceTests` (pending grid edits stay in their tab; a too-large file opens nothing),
  `ReviewRegressionTests` (History opens a new tab, nothing asked), `ResultsGridTests` (tab-scoped grid ids),
  `ReviewFixBrowserTests` (snippet rows no longer wrap, so the list overflows only in a shorter window).
- PostgreSQL browser classes against the throwaway cluster (`pg-test-server.ps1 -Port 58432`): 35 passed, 1 skipped
  (needs a PostgreSQL 14 server).
- Full serial run before the fixes below: 121 passed, 10 failed, 54 skipped. Every failure was diagnosed by a workflow
  with an independent skeptic per verdict:

| Failure | Verdict | Action |
| --- | --- | --- |
| `EngineAutoConnectTests` ×5 | environment: the installed `AkmlSqlWebEngine` is a localhost-only install (bridge on 127.0.0.1:47291, no TLS, no current PIN); these tests pair at the computer name with a PIN | none in this change; needs a LAN-mode install (user's approval) or the reference machine |
| `ResultsGridTests` keyboard | spec 043 changed the grid ids to `akml-grid-{tab}-{set}` (contract §3) | test updated |
| `ResultsGridTests` pointer long task 113 ms | pre-existing since 31dde2d: the Schema-peek handler on the results re-rendered the page, and so every visible grid row, on each press | fixed (a non-rendering handler); the drag now records no long task |
| `ReviewRegressionTests` ×2 | spec 043 R11 removed the "Discard the changes" question; the skeptic also found History naming the new tab after the engine's automatic `query-NN` session | test updated; `TabNameOf` drops `query-NN` (new unit test) |
| `ReviewFixBrowserTests` snippet list | the US4 badge fix made every row one line, so 22 rows no longer overflow at 900 px | test uses a 600 px window, as REV-08's acceptance asks |

- Also fixed while measuring: a switch to a tab holding a 9 MB document took 4.6 s because the page sent the tab's whole
  text to the editor on every switch; the editor now keeps track of the tabs whose state it holds and sends text only the
  first time. A tab-scoped call for a tab never shown no longer creates an empty editor state (that state would have been
  shown instead of the tab's text). The legacy `?autorun=1&kind=` link no longer runs anything (contract §6).
- After a reload on any page, the status bar's one-time restore reconnects the active tab to its own connection when no
  password is needed; two reconnects of one tab at once share one SessionDisconnect and one ConnectionChanged.

Measured (Debug build, Chromium, this machine; T102):

| Measure | Result |
| --- | --- |
| SC-003: click a tab until the status bar and database selector show its connection, 20 switches | median 87 ms, p90 118 ms, max 126 ms |
| Switch between a 9 MB tab and an empty one, 6 switches (target under 200 ms) | before the fix median 4,603 ms; after median 106 ms, max 221 ms |
| Reload with 20 tabs until the tabs and the active editor show | 2,611 ms; the active tab connected again at 2,616 ms |

- **Pending**: the native keyboard run with the three tab keys (opt-in, interactive desktop, T025 added); the SQL-auth
  password after a reload (M4, needs a SQL login). The engine restart with two tabs connected is automated since the
  review below.

### Review of US1 (2026-10-21)

A multi-agent review of the US1 code, each finding checked by an independent skeptic, confirmed 37 defects and 9 coverage
gaps (G1–G9). All 37 and the behaviour gaps G1–G3 are fixed; the missing tests for G4–G9 are written. By area:

| Area | Findings | Fix |
| --- | --- | --- |
| Two app windows | 1, 18, 19, 20, 22 | a Web Lock per engine session id (`ISessionLeases`, `akml-session-leases.js`): a window that finds a tab's session held by another runs that tab on a session of its own and keeps the record's id for its owner; the tab set is written read-modify-write under a lock and merged with what other windows added or closed; the old single document migrates tab record first, and an interrupted migration is finished on the next load; a tab write still in its debounce never brings back a closed tab |
| Engine | 21, 23 | a run that ends after its session was connected again cannot move the new connection's database; completions on the shared completion engine take a lock around their per-request settings |
| Connection lifecycle | 2, 4, 6, 7, 16 | a run tried while the engine link is down says the engine is away instead of marking the session lost; Disconnect forgets the tab's binding; a connect that a close or a newer connect superseded never sends `ConnectionChanged`; a release the engine did not confirm is sent again when the link is back, and the user is told |
| Runs and closing | 3, 8, 9, 12, 13, 31 | Cancel and closing stop a run that is still connecting; closing a running tab asks first and the tab counts toward the limit until it is released; a closing tab is never shown again; a run that ends after the page was left shows on the page that is there; the transaction warning comes before the Save question |
| Editor state | 5, 10, 11, 14, 15, 32 | a tab not shown is analysed when it is shown, with its own dialect and session; History reuses only an empty, unedited tab with no pending grid edits; leaving the page drops the pending-edit flags; a page coming back draws the active tab's findings; text read or written while CodeMirror is created is kept; a Re-execute runs only the query History sent |
| Keyboard and accessibility | 17, 24–27 | completion, ghost text, wildcard expansion, quick info and signature help drop a reply for a tab no longer shown; F6 focuses the editor first, and only what is on screen and in the Tab order; closable tabs are keyed, Delete / Enter / Space act on the focused tab, a held Delete closes one tab, and focus moves to the tab now active; New query at the limit says why |
| Connect to database | 28–30, 33, 34 | the dialog opens on the tab it connects (that tab's connection, else SQL Server); the tab's own connection wins over the saved row it came from; the dialog is named by its visible title; saved rows are buttons in the Tab order |
| Neighbours | 35–37, G1–G3 | the database menu closes when another tab is shown and refuses a pick under a run; Settings › Connections Connect and Disconnect wait for the run; the snippet body and the Format styles input also turn off auto-correction and auto-capitalisation; closing a SQL Server tab says when its open transaction was rolled back (the second round below makes it ask first); the status bar keeps a background tab's outcome; a tab's tooltip says "not saved" when its connection is not one of the saved ones |

Tests after the fixes:

- Web unit (Release) 1,793 passed before the last two dialog cases were added; JS 162 passed; engine 2,497 passed and
  132 skipped, with one failure: `PerformanceBaselineTests` (FormatRequest p50 274 ms against a 150 ms baseline, measured
  while browser tests ran on the same machine; the known environmental drift, the formatter is untouched). Engine
  PostgreSQL and session classes against the test cluster: 429 passed; one fixture connection timeout, and that class
  passes 32/32 when run again alone.
- Browser `QueryTabsTests` 16/16 (Chromium, Debug builds, the PostgreSQL cluster on port 58432). New: Delete on the focused
  tab; Save from the close question (a cancelled name prompt keeps the tab); the tab commands in the palette and their
  keys in the tab help; History per tab; closing a running tab; 30 runs across three tabs; a reload of five tabs; a
  PostgreSQL tab beside a SQL Server tab with the Schema panel following; a restored tab whose password is not remembered
  asking once; an engine restart with two tabs connected (`SandboxEngine.RestartAsync`).
- Engine restart: in each tab the first run found that the engine no longer had the session and the second run connected
  again with nothing typed, as data-model §3 describes.

Measured in that run:

| Measure | Result |
| --- | --- |
| Click the PostgreSQL tab until its kind and Schema panel show (FR-005) | 102 ms |
| Click the SQL Server tab until its kind and Schema panel show (FR-005) | 379 ms |
| SC-003 tab switch with three checks | 119 ms |
| FR-014 run in tab 2 while tab 1 waits five seconds | 339 ms |

#### Second and third verification rounds (2026-10-21/22)

Each fix round was checked again by read-only reviewers, one lane per area, with an independent skeptic per claim; what survived
was fixed. Second round: 23 confirmed (3 medium); third round: 23 confirmed (1 medium). The behaviour that changed:

| Area | Change |
| --- | --- |
| Cancel | Since the engine answers a page's requests side by side (R3), `ExecuteCancel` stops a statement that is running; the engine now also registers the cancel as soon as an execute is read, so it stops one still queued on the session or still opening its connection (`RunningStatementCancelIntegrationTests`, 2 cases). The toolbar button is "Cancel", the running note "Cancel stops the running statement.", and closing a running tab says it cancels the query. |
| Closing a tab | A SQL Server session is asked about an open transaction when its tab closes (`SELECT XACT_STATE()`, at most 3 s; the tab's tooltip says it is checking) — never after each run, which would reset `@@ROWCOUNT` and `@@ERROR`; the question is asked once; a run started while a question is open is asked about; a second close of the same tab while one asks does nothing; a tab that never reached the engine is closed without a warning. |
| Two app windows | Where a page has no Web Locks (`http://<computer name>/`) the windows ask each other over a BroadcastChannel, and two asking at once settle it the same way (the lower window token wins); a window keeps the sessions it uses instead of the stored ones in sessionStorage, so its reload takes them back (and gives up its own when the stored one is free again); the move to tabs writes the set with IndexedDB `add`, so two windows moving at once leave one set and no stray tab. Residual: a window that holds a session but is busy for longer than the 150 ms wait is not heard. |
| Connect to database | Opens on the tab it connects; a tab still on its saved row shows that row as saved; a tab connected differently, or (for a run that reconnects it) in another database, opens as a new connection with its own values, so Save never writes the tab's state into a row. PostgreSQL hosts typed with brackets or a port match their saved row. A remembered-password answer for an earlier form is dropped. |
| Settings › Connections | Disconnect acts on the tab it was clicked for (captured before the check), waits at most 3 s for the transaction check, and is disabled meanwhile. |
| Tabs and keys | The editor page claims its keys on its first render (F5 during the load no longer reloads the page; only view commands run until the page is ready). Closing the only tab hands focus to its replacement. A tab the user disconnected stays disconnected after a reload. "Not saved" follows the saved rows (a row saved after the connect counts; a deleted row counts only through another row for the same database). A copy of an older build's document edited here is never overwritten. |

Tests after the third round:

- Web unit (Release) 1,828 passed; JS 167 passed; engine execute, session, transport and PostgreSQL classes against the
  test cluster (port 58432) 863 passed; the full engine suite before the cancel change: 2,497 passed, `PerformanceBaselineTests`
  the only failure (known environmental drift, the formatter is untouched).
- Full browser gate (every class but the screenshot tours and `EngineAutoConnectTests`, Debug builds, serial, Chromium, SQL Server
  and the PostgreSQL cluster): **172 passed, 0 failed, 22 skipped** — the skips are the opt-in native-key and measurement runs, the
  spec 025/027 placeholders, a missing SQL login for the alias test, the localhost-mode installed engine (TLS checks), the spike URL
  and PostgreSQL 14. `EngineAutoConnectTests` (5) need a LAN-mode installed engine and are left out until the user approves the
  reinstall.
- Second-round gate: 171 passed, 1 failed — `WorkspaceLayoutTests.The_layout_survives_closing_and_reopening_the_browser`, F8 pressed
  right after the editor appeared reached the browser because the page claimed its keys only after loading its stores (a pause of
  3 s made it pass); fixed by claiming them on the first render.

Measured in the third-round gate:

| Measure | Result |
| --- | --- |
| Cancel a running `WAITFOR` until the run ends (status Cancelled, the next batch not run) | 217 ms after the click |
| Click the PostgreSQL tab until its kind and Schema panel show (FR-005) | 95 ms |
| Click the SQL Server tab until its kind and Schema panel show (FR-005) | 164 ms |
| SC-003 tab switch with three checks | 100 ms |
| FR-014 run in tab 2 while tab 1 waits five seconds | 353 ms |

## Final gates (T103, T091, T101, T007 — 2026-10-22, commit df40eed + task notes)

Debug builds of `AkmlSql.Web` and `AkmlSql.Engine`; SQL Server on localhost:1433; the PostgreSQL cluster on port 58432
(binaries `C:\Tools\pgsql\bin`, `PG_BIN`). Raw `.trx`: `%LOCALAPPDATA%\AKML SQL\spec043-tests\after\`. Pass-lists:
[baseline/tests/after/](./baseline/tests/after/). The before `.trx` files were no longer on the machine, so the pass-lists were
compared directly with [baseline/tests/before/](./baseline/tests/before/).

**T103 — no-regression gates**

| Gate | Result |
| --- | --- |
| Theme drift, web and site (`generate-theme-css.ps1 -CheckOnly -RepoRoot .`) | OK (dark.css, high-contrast.css both folders) |
| `WebCssTokenTests` + `ShortcutCollisionTests` | 8 passed |
| `CorpusGateTests` (engine) | passed |
| Format goldens (`FormatParityTests` 264; the whole `AkmlSql.Formatting.Tests` 1,642) | all passed |

**T101 — quickstart §1 automated rows**

| Suite | Passed | Failed | Skipped | Before |
| --- | --- | --- | --- | --- |
| Web unit | 1,828 | 0 | 0 | 1,681 / 0 |
| Engine | 2,621 | 2 | 10 | 2,487 / 1 / 132 |
| Core | 1,458 | 1 | 3 | 1,458 / 1 / 1 |
| Site | 910 | 1 | 0 | 910 / 1 |
| Formatting | 1,642 | 0 | 0 | — |
| JavaScript (`node --test`) | 167 | 0 | 0 | 137 |
| Browser rows: `QueryTabsTests`, `ConnectDialogTests`, `EditorTypographyTests`, `WorkspaceAccessibilityTests` | 35 | 0 | 0 | — |

- Failures: the three pre-existing ones from the before table (`PerformanceBaselineTests`, `ProfileGetMessageTests` key
  count, `F1SlugTests` anchors) and `RefactoringPerformanceTests.SC001_RemoveSemicolons_Under100ms_On2000LineDocument`, which
  timed out only while the browser classes ran on the same machine; run alone it passes (7/7 with `CorpusGateTests`).
- Pass-list comparison: no test that passed before has stopped passing for a product reason. The names that drop out are
  (a) theory cases differing only in case, where the ` [#2]` suffix moved to the other case (same count); (b) tests replaced by
  this feature: `HistoryDialogTests` "Opening ... starts a new default named document" / "asks first and Cancel keeps it"
  (History now opens into a new tab, FR-013) and `ResultsPaneComponentTests` "...says it cannot be interrupted..." (Cancel now
  stops a running statement); (c) `Core.VisualReferenceCoverageTests` ×2, skipped because the gitignored `doc/SQL-PROMPT/`
  reference pack is not on this machine.
- The two PostgreSQL `QueryTabsTests` cases skipped in the first browser pass (the cluster had stopped when the call that started
  it ended) and passed 2/2 on a rerun with it up; the 35 above counts them.
- Not run here (need the user): the native-key row (T060) and the dark walk review (T088).

**T091 — light and high contrast after the dark fixes (FR-035)**: `WorkspaceAccessibilityTests` 4/4 passed (token pairs, focus,
help, reduced motion in all three themes; editor contrast; native controls; touch). `WorkspaceScreenshotTour`
`Capture_workspace_in_three_themes_at_both_supported_sizes` passed; its light and high-contrast captures (60) are in
[baseline/light-hc/](./baseline/light-hc/) (the tour writes into spec 041's folder; those files were restored). Reviewed: no dark
colour leaks into either theme, the tab strip, grid, status bar and Schema panel read as before.

**T007 — spike S4**: passed; see research.md R8 (a version-4 profile from the pre-043 build upgraded to version 5 with all 15
records unchanged and the document moved into tab 1).

## Follow-ups reported by the user and deployment (2026-10-22)

| Report | Cause | Change |
| --- | --- | --- |
| High contrast: the current Settings section (`akml-settings-nav-link`) is a black bar; the item font differs | HC fills hover and selection with `CanvasText`, the same colour as the text; the link had no font of its own | `settings.css`: inverted text on hover/current in HC, chrome font family and size, no hover underline |
| A new query tab has no connection and the Schema panel says "Not connected to a database" | A new tab copies only the connection's description; with SQL authentication (or a PostgreSQL password not remembered) the browser has no password, so the tab waited for its first run | Engine `session.copy.v1`: the new tab names the tab it was opened from (`ConnectionInfo` key 7, `TestSqlConnectionRequest` key 2) and the engine copies that session's password for the same server and login only (`SessionPasswordCopy`); the tab connects on its own as SSMS's New Query does |
| SQL Server should use saved passwords and settings like PostgreSQL | Only PostgreSQL could remember a password | Engine `credentials.sqlserver.v1` (`SqlServerCredentialResolver`, store key `mssql-web://`, bridge requests only, never the SSMS extension's logins); dialog "Remember the password on the engine machine" + Forget for SQL authentication; a remembered login reconnects after a reload; Trust the server certificate travels with the saved row and the tab |
| Tab 1 ran on tab 2's server | Not reproduced against this branch's engine (new test `A_new_tab_moved_to_another_server_leaves_the_first_tab_on_its_own`, and the installed walk below) | — |

Tests: engine credential, copy, capability and connection classes 66 passed (11 live-PostgreSQL skips); web unit 1,831 passed; Core
`ProfileGetMessageTests` key count is the known pre-existing failure. Browser (Debug, serial, SQL Server + PostgreSQL cluster, a temporary
SQL login created for the run and dropped after): the four new cases passed; QueryTabs, ConnectDialog, SqlServerAlias, Quickstart,
PostgresWorkspace, SecretStorage and WorkspaceAccessibility 70 passed, 2 skipped (no client alias on this machine; no PostgreSQL root
certificate). `EngineAutoConnectTests` 5/5 against the installed LAN engine. `InstalledEngineCertificateTests`: the deep-link test failed
because the harness turned the summary's HTTPS URL into plain HTTP on the HTTPS port; `InstalledEngine.SiteUrl` now uses the summary's
local `http://localhost:{port}/` line, and it passes; the certificate cases skip until `bridge.cer` is trusted on this machine.

**T104 deployment** (user-approved): installer 1.26.1022.0157 from this branch (`Deploy-Build-Release.ps1 -SkipTests`), silent upgrade
`/WEB_HOST=IIS /WEB_EXPOSURE=LAN /WEB_PORT=59416 /BRIDGE_PORT=59417 /WEB_HTTPS_PORT=59418` (443 belongs to other sites on this server).
IIS `AkmlSqlWeb` http :59416 + https :59418, service `AkmlSqlWebEngine` running, bridge wss :59417, engine and web both 1.26.1022.0157.
The upgrade issued a new bridge certificate (0F1C5805…): browsers paired before must pair again. Walk against the installed site
(Playwright, pairing with the PIN): 16/16 — pair Live; HC Settings readable; SQL login on Northwind; New query connected with the
Schema panel on Northwind and running as the login without a prompt; tab 2 `USE master` while tab 1 stays on Northwind; a PostgreSQL
tab and a New query from it connected without a prompt; tab 1 still on SQL Server Northwind afterwards. The manual M8/M10/M12/M13 walk
with the user is still open.

### Code review of the follow-up (2026-10-22) and redeploy

A high-effort review of `df40eed..38140436` found 10 issues; all were fixed:
- Password copy: only from a session the same bridge WebSocket connection connected (`BridgeConnection`, `SessionPasswordCopy.MayCopyFrom`
  — never an SSMS session or another browser's), and only when encryption and certificate checking match (PostgreSQL: SSL mode and root
  certificate).
- No empty-password logins: the engine answers a web SQL login with no password itself (`dialect.password-required`) in test, connect
  and database list; the web marks the tab `PasswordMissing` (no quiet retries; the next run asks); a server that is down is reported
  as it is, not as a password prompt; an engine that can supply no password is never sent one.
- Remember: sent only to an engine advertising `credentials.sqlserver.v1`; saved by the connect's own test (`TestSqlConnectionRequest`
  key 3) instead of a second login; Forget also clears the saved row's flag; the remembered state counts only for the server and login it
  was asked about; a refused remembered password is recognised by `sqlserver.login-failed`, not by English text.
- Markup: the Forget button and the note are outside the checkbox's label; the misplaced doc comment in `ConnectionChangedHandler` moved.

Tests after the fixes: engine 96 passed (11 live-PostgreSQL skips) in the credential, copy, capability, connection, disconnect and
transport classes; web unit 1,834 passed; browser QueryTabs, ConnectDialog, SqlServerAlias, PostgresWorkspace 54 passed, 2 skipped,
1 failed — `PostgresWorkspaceTests.The_explorer_shows_the_kinds_columns_and_comments_follows_a_script_and_a_database_switch`, which was
bisected in a worktree: it fails and passes alternately on the unchanged previous commit too (explorer refresh after a seed script in a new
database, a pre-existing timing race), so it is not caused by these changes and is left as a known intermittent.

Redeployed 1.26.1022.0701 (same silent LAN upgrade): installed walk 16/16; `EngineAutoConnectTests` 5/5 and the deep-link test 1/1
against the installed engine. T060 native keys: Edge passed once; Chromium and Firefox skipped because Windows refused the browser
foreground focus in this RDP session — still for the user at the desktop.

### Tab on SELECT * and the completion list (reported 2026-10-22)

| Report | Cause | Change |
| --- | --- | --- |
| With the caret before `*`, Tab showed a list of columns instead of expanding | Tab expanded only with the caret just after `*`; before it, Tab indented the line and the edit opened completion | `akml-editor.js` `detectWildcardAtCaret`: the caret just before `*` counts too (the engine is asked with the caret after it), as SQL Prompt in SSMS |
| In the open list, Tab put in a space instead of the column | A ghost suggestion behind the list took Tab first | `acceptGhost` yields while a completion list is open: Tab commits the highlighted item, as in SSMS |
| The column list looked poor | CodeMirror's default rendering: a generic square icon, an italic monospace detail packed against the name | `Editor.razor`: a coloured kind badge per kind (C column, T table, K keyword, ƒ function, S schema, @ variable, » snippet) from the syntax tokens, the type/table right-aligned upright in the chrome font, flex rows, 320 px minimum width; `:root` outranks CodeMirror's scoped theme rules |

New browser class `WildcardExpansionTests` (3): Tab just after and just before `*` on the reported script expands to the eleven
Customers columns without indenting the line or opening a list; the list shows the "C" badge, an upright right-aligned detail and flex
rows, and Tab inserts the highlighted column. Related classes (EditorTypography, PostKeywordTrigger, PostgresEditing, UserStory2/4/5)
15 passed, 9 known skips; JS 167 passed; `WebCssTokenTests` passed (no colour literals added).
