# Baseline and verification record: 041-web-ssms-ux-parity

## Setup baseline (T001–T003, 2026-10-08)

Branch `041-web-ssms-ux-parity` at `a41826f`, before any spec 041 code change.

**T001 — build.**
- `MSBuild AKML-SQL.slnx -t:Restore`, then `-t:Build -p:Configuration=Release -m`: green, 0 errors.
  The warnings are the existing CA1416 platform warnings. No `ctoFiles.json` / `resources.json` /
  `mergeCto.cache` appeared at a drive root.
- MSBuild on this machine is Visual Studio 18 **Insiders**
  (`/c/Program Files/Microsoft Visual Studio/18/Insiders/MSBuild/Current/Bin/MSBuild.exe`); tasks.md and
  quickstart.md were corrected from "Enterprise".
- Debug builds of `AkmlSql.Web`, `AkmlSql.Engine`, `AkmlSql.Web.E2E.Tests`, `AkmlSql.Web.Tests`,
  `AkmlSql.Engine.Tests`, `AkmlSql.Core.Tests`, `AkmlSql.Site.Tests`, `AkmlSql.Formatting.Tests` and
  `AkmlSql.IntelliSense.Tests`: green.
- `playwright.ps1 install chromium`: done.
- `generate-theme-css.ps1 -CheckOnly`: green for the web edition and for the product site.

**T003 — test suites** (Debug, `dotnet test --no-build`; the shell suite through its Release net472 dll;
results in `%LOCALAPPDATA%\AKML SQL\spec041-tests\before\`, per-test pass-lists in
[`baseline/tests/before/`](./baseline/tests/before/)):

| Suite | Passed | Failed | Skipped | Notes |
|---|---|---|---|---|
| AkmlSql.Core.Tests | 1,120 | 1 | 3 | Pre-existing red: `Ipc.ProfileGetMessageTests.Response_key_layout_is_positional_and_append_only` (also red in spec 040's baseline) |
| AkmlSql.Engine.Tests | 2,005 | 0 | 0 | 22 min 15 s (the whole suite, not only Execution/InProcess/Handlers/Parser: `Pr247_ExecuteQueryHandlerFix` lives outside those folders) |
| AkmlSql.IntelliSense.Tests | 25 | 0 | 0 | |
| AkmlSql.Formatting.Tests | 1,635 | 0 | 0 | |
| AkmlSql.Site.Tests | 904 | 0 | 0 | |
| AkmlSql.Web.Tests | 503 | 42 | 0 | Pre-existing red: all 42 are `Services.FormatterServiceTests.Formatter_MatchesIdeBaseline_AcrossCorpusAndProfiles` (the `sp031-*` pending golden baselines named in CLAUDE.md) |
| AkmlSql.Shell.Shared.Tests | 882 | 0 | 0 | |
| AkmlSql.Web.E2E.Tests | 17 | 0 | 10 | **Run serially** (see below). The 10 skips are the long-standing `Skip=` scaffolds (UserStory2/4, PostKeywordTrigger, Spike) and the alias test without its login |
| node (`tests/AkmlSql.Web.Tests/js/*.test.mjs`) | 17 | 0 | 0 | |

- **Browser suite must run serially.** Every E2E test class starts its own web app on port 5000, and
  two of them start the mock AI provider on port 11434. Under xUnit's default class parallelism they
  collide: the first (parallel) run had 6 failures (`ERR_CONNECTION_REFUSED`, "conflicts with an
  existing registration"). Run with `-- xUnit.ParallelizeTestCollections=false`; serially all 17 pass.
  The parallel run is kept for reference in `spec041-tests\before-parallel-e2e\`.
- **node --test**: Node 24 does not accept a folder; use the glob
  `node --test "tests/AkmlSql.Web.Tests/js/*.test.mjs"` (tasks.md corrected).
- **Completion corpus** (`CorpusGateTests`, OVERALL): **1,311 / 1,343 = 97.6 %**. It may only go up.
- **Format-parity corpus:** 363 corpus `.sql` inputs, 265 golden files under `tests/format-parity/golden`.
  The goldens must stay unchanged.

From here on, any new red against these lists is a regression this feature caused. Compare with
`scripts/compare-test-results.ps1 -Before …\before -After …\after -OutDir specs/041-web-ssms-ux-parity/baseline/tests/after`.

## Resumed implementation — 2026-10-10

Resumed from the existing staged work with exactly T186 and T206–T216 unchecked. Completed
task markers and the existing index were preserved. No deployment or Git mutation is part
of this continuation. The requirements checklist remains read-only, 16/16 checked.

### T206 — control and label audit

Audited the routed Razor pages and shared components, including multiline disabled-control
tags, handler bodies, menu actions, component references and remaining status fields.

| Surface | Finding / action |
|---|---|
| Connection manager | Removed the unsupported Entra/Future radio and its unused styles. Loading, busy and no-selection states now explain disabled controls. Refresh uses the existing icon sprite. |
| Editor toolbar | Parse explains running, disconnected and unsupported-engine states separately. New/Open/Save/Save as, Format, Analyse and View handlers are present. |
| History | Compare failure, prune and export now use notifications; removed the old `_statusMsg` field and footer fragment. Copy and Open in editor labels match their handlers. |
| Snippets | Built-in fields explain that they are read-only; personal Save/Delete and import/export have handlers. |
| Settings | Dependent analysis setting, disconnected saved-connection Connect and provider-specific disabled endpoint explain their state. Other sections already have working controls and resets. |
| Dialogs / refactoring | Invalid prompt confirmation explains validation; disabled Apply explains the unavailable preview. No empty action handlers found. |
| Result grid | Applying disables Discard/Apply with a reason; invalid cells keep their existing explanatory text. Copy, Copy as, View value, Save, sort and resize actions are wired. |
| AI actions / chat | Insert at caret, Copy and Export match their handlers. Busy/empty-input controls already explain their disabled state. |
| Spike development route | Running and unloaded-corpus controls now explain their disabled state. |
| Navigation / menus / status | Working routes and commands; no unsupported future option remains. Every shared Razor component has a production reference (MainLayout is the router's default layout). |

No rendered “coming soon”/“not available yet” placeholders, TODO handlers or commented-out
markup remain. Architectural Razor comments are retained. Live status fields in Editor,
Snippets and Styles are retained where current handlers and tests still consume them.

### Pending external acceptance

- **T209 complete (2026-10-11):** after the user made the interactive desktop available,
  the native Windows SendInput replay passed **3/3**, with no failures or skips.
  Earlier foreground-focus skips/setup failures are superseded by this successful run.
- **SC-006:** the ten-SSMS-user hallway study remains pending participant availability.
- **T215 complete (2026-10-12):** browser replay, actual SSMS engine verification and
  actual Calc clipboard paste are recorded below. Desktop automation performed the
  spreadsheet acceptance; this is not a claim of human usability-study participation.
- **T216 complete (2026-10-11):** approved Web/Engine deployment and the two refreshed
  product-site screenshots are recorded in the follow-up below.

### Browser key capture (T209)

`BrowserKeyCaptureTests` runs a real browser against the sandbox engine and local SQL.
The table records cancellation of the DOM default action; it does **not** certify native
Windows/browser accelerator handling. All three engines passed without skips in the
targeted run; the final suite repeats these checks.

| Keys / context | Chromium | Edge | Firefox |
|---|---|---|---|
| F5, Ctrl+F5, Ctrl+R, F8 | Prevented | Prevented | Prevented |
| F6, Shift+F6 | Prevented | Prevented | Prevented |
| Ctrl+S, Ctrl+Shift+S | Prevented | Prevented | Prevented |
| Ctrl+Alt+N, Ctrl+Alt+O | Prevented | Prevented | Prevented |
| Ctrl+K, Ctrl+F / Ctrl+L / Ctrl+S | Prevented | Prevented | Prevented |
| Grid: Ctrl+Shift+C, Ctrl+0, Shift+Alt+Left/Right | Prevented | Prevented | Prevented |
| Synthetic Polish ó / ń with Ctrl+Alt | Not intercepted | Not intercepted | Not intercepted |
| F5 on Settings | Not intercepted | Not intercepted | Not intercepted |

Native input established that Edge keeps Ctrl+Shift+S for Screenshot. Save as now also
accepts **Ctrl+K, Ctrl+A**, displayed in its toolbar tooltip, View menu and palette.
Shortcut collision tests (6/6), all three DOM browser cases, and Node tests (98/98) pass.
The native harness checks scan-code input, unchanged layout on Shift+Alt+arrows with
English/Arabic installed, and Polish Programmers AltGr inserting `óń`. It refuses to send
keys when another application owns focus and restores the original test-window layout.
The final interactive replay passed **3/3** in 66 seconds on 2026-10-11. Native input
results below are separate from the DOM table above; “Claimed” means the page cancelled
the trusted input and retained the document. Both menu and palette opened Save as with
the alternative displayed. Original browser input layouts were restored after testing.

| Native Windows input / context | Chromium | Edge | Firefox |
|---|---|---|---|
| F5, Ctrl+F5, Ctrl+R, F8 | Claimed | Claimed | Claimed |
| F6, Shift+F6 | Claimed | Claimed | Claimed |
| Ctrl+S | Claimed | Claimed | Claimed |
| Ctrl+Shift+S | Claimed | Browser Screenshot | Claimed |
| Ctrl+K, Ctrl+A (Save as alternative) | Opens Save as | Opens Save as | Opens Save as |
| Ctrl+Alt+N, Ctrl+Alt+O | Claimed | Claimed | Claimed |
| Ctrl+K then Ctrl+F / Ctrl+L / Ctrl+S | Claimed | Claimed | Claimed |
| Grid: Ctrl+Shift+C, Ctrl+0 | Claimed | Claimed | Claimed |
| Grid: Shift+Alt+Left/Right, English/Arabic installed | Claimed; layout unchanged | Claimed; layout unchanged | Claimed; layout unchanged |
| Polish Programmers AltGr+O / AltGr+N | Inserts óń | Inserts óń | Inserts óń |

Local evidence: `%TEMP%/spec041-native-unlocked/Native.trx` and
`%TEMP%/spec041-native-unlocked.log`. T209 is complete.

### Accessibility implementation (T207)

- Toolbar help and lint-gutter diagnostics open on keyboard focus and dismiss with Escape.
  Grid type/value help, menus and focus rings retain their existing keyboard coverage.
- Reduced motion now disables animations and transitions. Assigning a tiny nonzero global
  transition duration had inadvertently animated controls that originally had no transition.
- High contrast uses the inverse `CanvasText`/`Canvas` pair for selected/hovered surfaces and
  accent controls. Chromium's default `Highlight`/`HighlightText` measured **4.4989:1**,
  below 4.5; the inverse pair measures **21:1** on this VM. These remain system colors.
- Success, error and warning badge fills are web-only tokens; destructive confirmation buttons share
  the error fill. Primary hover uses the existing pressed accent. Desktop hex tokens are unchanged.
- Web-only status text tokens provide readable success/warning/error text on both panel and
  canvas backgrounds. Status dots and border indicators retain the original status colors.
- Connection-status pills use contrast-tested fills; Offline/Disconnected use the neutral
  badge pair. The accessibility test also measures the rendered Live pill, not just token pairs.
- Generated theme files were regenerated for both Web and Site from `docs/theme-tokens.json`.

### Grid performance implementation (T210)

The original 50-column drag repeatedly produced 101–121 ms main-thread tasks. A row render
boundary now skips rows whose pointer-visible state is unchanged. Selection, focus and
width-dependent hover titles participate in the state key; normal renders invalidate all
rows so edits, sort and new results remain current. The existing grid suites cover those
paths, with an added regression check for clipped titles after narrowing/widening a column.
Final browser measurements are recorded below after the complete suite.

Screenshot inspection also exposed a one-pixel sizing error that the earlier `+1` test
tolerance allowed: a datetime cell had a 168 px track, 167 px client width and 168 px scroll
width. Auto-fit now includes its 1 px right border. The large-result query includes
`datetime2(7)`, and both sizing checks reject **any** `scrollWidth > clientWidth`.

The status-bar review found a stale "No engine is paired" startup failure next to a Live
connection after manual pairing. The failure strip now hides whenever the bridge is open;
a component regression test and the three-theme shell browser test check this state.

### Measured accessibility results

`WorkspaceAccessibilityTests`, Chromium on this VM, explicit Light/Dark/High contrast
themes with reduced motion enabled. Ratios use the WCAG relative-luminance calculation
on resolved browser colors; assertions compare unrounded values against 4.5.

| Foreground / background | Light | Dark | High contrast |
|---|---:|---:|---:|
| Primary / editor | 17.85 | 11.87 | 21.00 |
| Neutral badge text / neutral badge | 14.48 | 8.40 | 21.00 |
| Primary / dialog or tooltip panel | 17.85 | 11.87 | 21.00 |
| On-accent / strong selection | 5.17 | 5.17 | 21.00 |
| Secondary / panel | 7.58 | 5.71 | 5.17 |
| On-danger / error badge or confirmation | 6.47 | 6.47 | 21.00 |
| On-accent / warning badge | 7.09 | 7.09 | 21.00 |
| On-accent / success badge | 7.13 | 7.13 | 21.00 |
| Rendered Live status pill | 7.13 | 7.13 | 21.00 |
| On-accent / primary button | 5.17 | 5.17 | 21.00 |
| On-accent / primary button hover | 8.72 | 8.72 | 21.00 |
| Success text / panel | 7.13 | 8.40 | 21.00 |
| Success text / canvas | 6.51 | 10.25 | 21.00 |
| Warning text / panel | 7.09 | 6.46 | 21.00 |
| Warning text / canvas | 6.47 | 7.89 | 21.00 |
| Danger text / panel | 6.47 | 5.29 | 21.00 |
| Danger text / canvas | 5.91 | 6.45 | 21.00 |

Status-text pairs on panel/canvas are checked in the same test. Keyboard help, lint help,
high-contrast row and button hover, visible focus and disabled motion are also asserted.

| Touch target | 100% hit area / drag change | 150% hit area / drag change |
|---|---|---|
| Results splitter | 12 px / 64 px | 18 px / 96 px |
| Column edge | 8 px / 64 px | 12 px / 96 px |
| Collapsed Schema strip | At least 24 px; tap restores | At least 36 px; tap restores |

These use touch events with `HasTouch`, not a mouse-drag substitute. Zoom is CSS zoom
inside a Chromium viewport. Native Windows display scaling remains a separate manual check.

Final targeted sizing run: **14.7 ms** for 1,000 × 30 tracks; **zero clipped cells** below
the 480 px cap at top, middle and bottom, including `datetime2(7)`; **no reported long
tasks** during the 50-column resize/select (the Long Tasks API reports tasks of at least
50 ms); 200-column auto-fit completed. The assertion still rejects any task over 100 ms.

### Quickstart replay and remaining manual variants (T215)

The checks below distinguish real-browser replay against Debug web/engine and local SQL
from variants covered only by unit/integration tests. A checked item means its listed browser
scenario ran; an unchecked item identifies a remaining manual variant, not an omitted feature.
T215 is complete after the actual Calc paste follow-up below. Desktop automation pasted
the Windows clipboard into a real spreadsheet and verified its cells and saved workbook;
no claim is made that a human participant performed the walkthrough.

**§0:** Debug builds, published sandbox engine, local SQL, full Release solution build,
theme drift gates and full test suites. Final SC-008 results are recorded separately below.

**§1 — Result columns** (`ResultsGridTests`, `QuickstartAcceptanceTests`):

- [x] 1.1 Database results fit without clipped values, including dates.
- [x] 1.2 The capped wide value opens in View value.
- [x] 1.3 Drag width and header/body alignment.
- [x] 1.4 Edge double-click and auto-fit.
- [x] 1.5 User widths survive virtualization, resize and panel hiding; a new result resets them.
- [x] 1.6 Row/column selection, tab-separated clipboard content and actual Calc paste
  into separate cells, including NULLs and a copied column header (2026-10-12).
- [x] 1.7 Pointer/keyboard context menu and Copy as commands; output formats also have goldens.
- [x] 1.8 Sort cycles and a new query clears sorting.
- [x] 1.9 Selection/editing, invalid input, NULL, persisted date/money changes.
- [x] 1.10 F5, Open, New and History confirm pending edits; cancelling preserves the edits and document.
- [x] 1.11 Keyboard grid navigation, resize, tooltip and menu dismissal.

**§2 — Results and Messages** (`ResultsMessagesTests`, `QuickstartAcceptanceTests`):

- [x] 2.1 A SELECT shows results and completion messages.
- [x] 2.2 Multiple SELECT/PRINT/UPDATE messages retain order.
- [x] 2.3 Document and selected-text errors in later GO batches navigate to the correct document line.
- [x] 2.4 Divide-by-zero retains surrounding result sets.
- [x] 2.5 GO loops, invalid GO, and three selected batches with the second failing and third returning results.
- [x] 2.6 CREATE PROCEDURE / GO / EXEC / GO / DROP.
- [x] 2.7 Timeout skips later work.
- [x] 2.8 F5 runs from each editor region; Settings leaves its DOM default unclaimed.
- [x] 2.9 Running state ends on sandbox-engine loss and says the statement may still have run.
- [x] 2.10 Parse reports syntax without executing the script.
- [x] 2.11 CSV/TSV downloads retain the full 2,000-character value, date and NULL; truncated results
  require confirmation and export only fetched rows. Actual Calc paste also preserved
  the full value, date text and NULL in the 2026-10-12 follow-up.

**§3 — Workspace** (`WorkspaceLayoutTests`, `ShellPolishTests`, `QuickstartAcceptanceTests`):

- [x] 3.1 Hide/restore side panels.
- [x] 3.2 Hide results; execution brings them back.
- [x] 3.3 Pointer and keyboard splitters respect bounds.
- [x] 3.4 Maximise and restore.
- [x] 3.5 Layout survives reload and browser restart.
- [x] 3.6 Responsive folding and a 120-character line at 1100 px.
- [x] 3.7 Reset layout survives reload.
- [x] 3.8 View/palette commands and F6 focus order.
- [x] 3.9 Disconnected prompt, schema target, filtering and refresh.
- [x] 3.10 Line-7 analysis marker and analysis-off/Turn on path.
- [x] 3.11 Hidden navigation restores and other pages retain navigation.

**§4 — Settings** (`SettingsPageTests`, `FormatStylesTests`, `FormatStylesSharedEngineTests`):

- [x] 4.1 Five navigation entries and legacy routes.
- [x] 4.2 All nine settings sections.
- [x] 4.3 Saved query defaults versus toolbar session overrides.
- [x] 4.4 Analysis off and Turn on.
- [x] 4.5 Suppress everywhere and Undo.
- [x] 4.6 Restore three providers without resetting other sections.
- [x] 4.7 Secret-free export, import into another browser context, unknown-setting warning.
- [x] 4.8 Active style, standalone-engine sharing and actual SSMS-started-engine sharing
  replayed. Duplicate-name selection passes for the engine and built-in copies. After
  the approved SSMS engine update, the picker and Format styles page both show
  `Shared with SSMS`; closed selections show `Khamis Style (shared with SSMS)` and
  `Khamis Style (built-in)` correctly. See the installed acceptance record below.
- [x] 4.9 Live editor preferences survive reload; editor reset confirms the document.
- [x] 4.10 Settings search retains section context.
- [x] 4.11 Dark theme loads before first paint.
- [x] 4.12 A second tab does not restore stale preferences.

**§5 — Documents** (`DocumentFlowTests`, shared `TabStrip` component tests):

- [x] 5.1 Arrow/Home/End navigation replayed across Results, AI and style-preview tab strips.
- [x] 5.2 Modified-document New confirmation and numbering.
- [x] 5.3 UTF-16/UTF-8 and modified-document Open replayed; a browser file above 10 MB is refused
  with its limit explained and the existing document preserved.
- [x] 5.4 Named Save/Save as, UTF-8 BOM and title/modified marker.
- [x] 5.5 History keeps named sessions separate from a new query.

**§6 — Shell polish** (`ShellPolishTests`, three themes with reduced motion): toolbar/icons,
temporary-table-preserving database switches and USE, status segments, in-app destructive
confirmations, consistent headers/startup, keyboard diagnostics and theme behavior are
replayed. A SQL-authenticated connection also retained its temporary table across the database
switch without a new password prompt. The randomly named test login and its pooled sessions
were removed after the check. The actual SSMS-started-engine variant passed in the
installed acceptance follow-up below.

**§7:** final comparison and local captures are recorded here. The human hallway study and
post-deployment product-site captures remain pending; no deployment was performed.

### Final SC-008 comparison — 2026-10-10

| Suite | Passed | Failed | Skipped |
|---|---:|---:|---:|
| Core | 1,285 | 1 | 3 |
| Engine | 2,091 | 0 | 0 |
| Formatting | 1,635 | 0 | 0 |
| IntelliSense | 25 | 0 | 0 |
| Site | 904 | 0 | 0 |
| Web | 1,131 | 42 | 0 |
| Shell shared | 882 | 0 | 0 |
| Web E2E | 78 | 0 | 10 |
| Node | 97 | 0 | 0 |

The Core positional-key failure and 42 Web formatter-corpus failures are the same failures
recorded in T003. The ten browser skips are unchanged. The comparison script reports
**zero regressed, missing, newly skipped or unexpected tests** across all eight TRX suites;
no allow-list was needed. Final pass lists are in [`baseline/tests/after/`](baseline/tests/after/).
Node is recorded separately because it does not produce TRX. Formatting goldens and the
completion corpus were not modified; completion remains **1,311 / 1,343 (97.6%)**.

The clean full browser run completed in 15m19s. An earlier run failed during the service
restart because a concurrent Debug rebuild changed browser asset hashes; the clean serial
rerun, with builds finished first, passed all 78 executable cases. Both screenshot tours are
excluded from that suite. The final status-pill contrast adjustment is verified separately
with the focused shell/accessibility run and refreshed local screenshot tour.

The final source passed a full MSBuild solution restore and Release build. Both Web and
Site theme folders passed `generate-theme-css.ps1 -CheckOnly`. Debug Web (including its
Blazor output) and E2E assemblies were built before the focused browser run. The final
Web unit rerun retained 1,131 passes and the same 42 formatter failures; Site passed all
904 tests and Node passed all 97 tests. No extension hooks are registered
(`.specify/extensions.yml` is absent).

**Final focused run: 10/10 passed** (seven shell cases, two accessibility cases and the
workspace screenshot tour; 2m25s). All 18 contrast measurements per theme passed, including
the rendered Live pill. These supplemental results are outside the full-suite TRX folder
to avoid double-counting. All **90** PNGs in [`baseline/after/`](baseline/after/) were refreshed
at 23:11–23:12 local time: 45 at 1440×900 and 45 at 1100×700, 30 per theme. Representative
Light and High contrast editor captures at 1100×700 and Dark Settings at 1440×900 were
visually inspected: date values fit, the Live pill is readable, and the stale pairing-failure
strip is absent. Theme-change notifications visible in captures are expected transient UI.
The installed bridge service is Running after verification; no test host remains.

**Continuation outcome (updated 2026-10-12):** all twelve originally open tasks are
complete. SC-006's ten-user hallway study remains explicitly pending participant
availability, as T215 permits. Previously completed task markers were preserved.

### Approved deployment and acceptance follow-up — 2026-10-11

- Earlier work was committed separately as `01150c0`, `debbbbc`, `7f21566`, `f5a7def`
  and `15c3612`. The subsequent acceptance and Save as changes remain uncommitted.
- The expanded quickstart/tab browser checks passed **10/10**, covering pending-edit
  New/Open/History cancellation, selected multi-batch errors, full CSV/TSV values and
  truncation confirmation, oversized Open, SQL authentication/session preservation,
  and tab keyboard navigation. These are supplemental results, not a rerun of the full
  78-pass browser suite. An earlier native-input attempt had one setup timeout and two
  foreground-focus skips; the final unlocked-desktop replay passed 3/3 as recorded above.
- Web unit tests: **1,131 passed / 42 unchanged formatter failures**. Node: **98/98**.
  SC-008 comparison rerun: **zero regressions, missing tests or newly skipped tests**
  against T003, with no allow-list. Release Web publish succeeded.
- User explicitly approved deploying the staged Web and Engine. Version **1.26.1010.2224**
  now runs in IIS `AkmlSqlWeb` at **http://localhost:59416/** and service `AkmlSqlWebEngine`.
  The initial port-80 check returned 404 and automatically restored the old payload;
  deployment succeeded after using the actual existing binding. No IIS bindings changed.
- Both old payload folders and root ACLs are backed up at
  `%LOCALAPPDATA%/AKML SQL/spec041-deployment/backup-20261011-102256`.
  All deployed files match the candidate SHA-256 manifest. Existing `web.config` was
  preserved (URL Rewrite is not installed); ProgramData configuration, certificates
  and user data were preserved. HTTP returned 200 and the bridge service is Running.
- `SiteScreenshotTour` passed **2/2** against the installed release. The Northwind query
  returned **50 rows**, and the screenshot shows matching Web/Engine versions. Visually
  inspected `query-results.png` (1920×889) and `connect-dialog.png` (720×501), then copied
  both into `src/AkmlSql.Site/wwwroot/img/screenshots/`. The captures contain sample data;
  SSMS screenshots were preserved. The separate marketing site was not deployed.
- Final focused follow-up passed **4/4**: Chromium, Firefox and Edge each opened Save as
  using the displayed View/palette alternative, and the real-engine style test verified
  duplicate-name source labels in the closed picker while switching both copies.
  A native-input retry initially skipped **3/3** because foreground focus was unavailable;
  after the user unlocked the desktop, the final native replay passed **3/3**.
- T215 still needs human spreadsheet paste; the actual SSMS-started-engine style grouping
  variant passed in the follow-up below. SC-006 awaits ten SSMS users. No participant results
  or manual sign-off are inferred from automated checks. Extension hooks are absent.

### Actual SSMS/Northwind acceptance attempt — 2026-10-11

At the user's request, opened a dedicated SSMS 22 instance with a saved read-only
Northwind script and an isolated AKML app-data profile. SSMS connected with Windows
authentication and executed `SELECT TOP (6) CustomerID, CompanyName, City FROM
dbo.Customers ORDER BY CustomerID;`, returning six sample rows. The screenshot was
visually inspected; the AKML SQL menu is present.

Playwright paired with the actual child engine launched by that SSMS process, queried
Northwind successfully, selected rows 3–6, and copied four tab-separated rows with
three columns (ANTON through BLAUS, including accented customer/city names). There were
no JavaScript page errors. Local evidence is under `%TEMP%/spec041-ssms-acceptance/`:
`ssms-northwind-executed.png`, `web-northwind-results.png`, and `browser-result.json`.

The expected **Shared with SSMS** group did not appear: SSMS uses a private engine
under its extension's `Engine` folder, version **1.26.1006.2219**, rather than the
**1.26.1010.2224** engine deployed for the web service. The browser showed **On this
engine**, so this check is recorded as failing on the old installed engine, not passing
acceptance of the new implementation. A backup/update/retest plan for that separate
folder is staged in `SSMS-DEPLOYMENT.md` beside the verified deployment candidate;
additional deployment approval was requested and subsequently granted.

With explicit user approval, backed up the SSMS-private Engine folder and its root ACL
to `%LOCALAPPDATA%/AKML SQL/spec041-deployment/ssms-engine-backup-20261011-203436`,
closed only our dedicated saved Northwind window, and deployed the verified
**1.26.1010.2224** engine. All **319** candidate file hashes match. Extension DLLs,
the web service, user configuration and databases were not changed by this deployment.
Reopened SSMS on Northwind and verified that the new engine is its direct child process.

The repeated Playwright check **passed**: both the editor picker and Format styles
page show **Shared with SSMS**; selecting either duplicate Khamis Style retains the
correct `(shared with SSMS)` / `(built-in)` suffix. A fresh Northwind query and four-row,
three-column copy passed with **zero JavaScript errors**. Inspected the styles screenshot.
The dedicated SSMS Northwind window remains open as requested. Evidence also includes
`web-ssms-styles.png`, `browser-updated.log` and `ssms-deployment.json` in the local folder.

No Excel or LibreOffice installation was found. Clipboard content is verified, but an
actual spreadsheet paste and the ten-person hallway study remain unverified.

### Actual spreadsheet acceptance completed — 2026-10-12

Used LibreOffice Calc Portable **26.2.4**, obtained from the Document Foundation's
[official portable download directory](https://download.documentfoundation.org/libreoffice/portable/26.2.4/).
The package's SHA-256 matched its published checksum and its Authenticode signature was
valid. It was extracted under `%LOCALAPPDATA%/AKML-Acceptance-Tools/LibreOfficePortable`,
with a separate acceptance profile; no system-wide Office installation was required.

Playwright executed a read-only Northwind Customers query, selected rows 3–6 and copied
them with Ctrl+C. Calc received that actual Windows clipboard using its paste operation
and Text Import dialog. The saved spreadsheet contains **four rows × six columns**:
customer ID, company, city, literal `NULL`, ISO date text, and a **2,000-character** value.
Read all 24 cells back through UNO and compared them exactly with the copied values;
all passed, including accents and full long-value lengths. Adjacent cells remained empty.

Copied the CompanyName column with Ctrl+Shift+C and pasted it into H1:H7. Verified the
header plus all six complete names. Calc's automatic single-column delimiter detection
initially split names at spaces; choosing **Separated by → Tab only** preserved each
name in one cell. Disabled Detect special numbers to preserve ISO dates as text.
This is an import-setting requirement in Calc, not truncation in the web clipboard.

Saved and visually inspected the workbook. Evidence:

- [Workbook](baseline/acceptance/Northwind-spreadsheet-paste.ods)
- [Calc screenshot](baseline/acceptance/calc-verified.png)
- [Cell verification results](baseline/acceptance/spreadsheet-result.json)

T215 is complete. SC-006 remains pending ten real SSMS users; no study results were
invented or inferred from automated interaction. No product source changed for this check.

### PR #255 review verification — 2026-10-12

Verified Claude's `759c6e5` review corrections against fresh Debug Web/Engine builds and
the local SQL Server. Added ten Playwright cases for CREATE OR ALTER, History replacement
and cancellation, Windows-1252 file loading and dotted names, error underline clearing,
typed JSON/INSERT copy, and date/time/binary edits applied to their source database after
a later USE. The existing date-edit browser test now enters the displayed date and still
verifies the persisted result.

Browser verification exposed two product problems, fixed in this follow-up:

- JSON strings did not escape every U+0000–U+001F control character. The shared formatter
  now escapes all of them, including in headers. Its unknown-type fallback also keeps
  NaN, Infinity and overflowing numeric-looking text as strings instead of invalid JSON
  numbers. Six added Core cases and the browser clipboard check cover these paths.
- A second click could open an editor before Blazor dispatched dblclick, allowing the
  late event to move editing to the adjacent cell. The double-click handler now preserves
  an already-open editor. Both keyboard and double-click Apply/re-query cases pass.

The touch acceptance test now checks whether Schema is already folded at the current
zoom before toggling it, and verifies that tapping its edge strip restores the panel.
The stronger assertion initially failed: an unpaced burst of CDP drag moves suppressed
the next tap, also reproduced on a plain HTML page without application handlers.
Pacing the eight drag steps at 100ms resolves that test-input problem. Both splitter
and column resizing remain covered at 100% and 150%; temporary tracing was removed.

| Suite | Passed | Failed | Skipped |
|---|---:|---:|---:|
| Core | 1,345 | 1 | 3 |
| Engine, full suite | 2,118 | 0 | 0 |
| Web | 1,152 | 42 | 0 |
| Shell, serial | 882 | 0 | 0 |
| Formatting | 1,635 | 0 | 0 |
| IntelliSense | 25 | 0 | 0 |
| Site | 904 | 0 | 0 |
| Analysis | 5 | 0 | 0 |
| AI | 55 | 0 | 0 |
| Web JavaScript | 98 | 0 | 0 |
| Playwright, unique outcomes including follow-ups through 2026-10-13 | 98 | 0 | 10 |

The 23m46s full Playwright run initially returned **92 passed / 3 failed / 10 skipped**.
Two installed-service reconnect cases encountered a pairing PIN older than its 24-hour
lifetime. After refreshing the service, all five reconnect cases passed. The third
failure was the touch interaction described above. The seven-case follow-up
(five reconnect cases, touch, and the extended JSON copy case) passed **7/7**. The updated
touch assertion failed in `NativeAndTouch.Final.trx`; after correcting gesture timing,
both accessibility tests passed in `Accessibility.Final.trx` on 2026-10-13.
The three native keyboard cases in `NativeAndTouch.Final.trx` skipped because Windows
refused browser foreground focus; no native input was sent. The prior 2026-10-11 native
pass remains historical evidence, not a claimed pass for this review run. Screenshot-generation
tours were excluded; the behavioral suite covers themes and supported layouts.

The Core positional-profile-contract failure and all 42 Web formatter-corpus failures
match the previous baseline by test name. A shell active-style failure under parallel
execution and a 40ms toast assertion race under load passed in serial reruns; the logs
retain those exploratory failures. No previously passing browser test regressed. Eight
database-name refusal cases and one wire-value editor case were intentionally replaced
in Claude's commit by legal-name/quoting and lossless displayed-text coverage. Other
previously passing unit/integration cases remain passing. Completion parity remains
**1,311/1,343 (97.6%)**; no golden corpus was changed.

The full solution passes VS 18 Insiders MSBuild in Release after the corrections.
Both generated-theme checks pass. Raw logs and TRX files are under
`%LOCALAPPDATA%/AKML SQL/spec041-review-759c6e5/`; `browser-comparison.json` records the
deduplicated browser outcomes and zero regressions. This verifies the working-tree
builds; installed payloads were not updated. The installed bridge service is restored
to Running after its reconnect tests.

### Verification of the updated branch — 2026-10-13

The checkout advanced to `d360022` during the review, incorporating the earlier grid
corrections plus separate SSMS shortcut, updater and web-theme commits. Rebuilt the
Debug Web bundle before browser verification and rebuilt the complete solution in
Release with VS 18 Insiders MSBuild. Both generated-theme checks pass.

`Review.Current.trx` passes **14/14**: all ten review regression cases, the three new
Light/Dark/HighContrast popup/filter cases, and a second run of the corrected touch
case. `Accessibility.Final.trx` also passes **2/2**. Combining these with the full cycle
and reconnect follow-up gives **98 passed / 0 failed / 10 existing skips**, with no lost
previously passing browser cases. These are deduplicated results across the recorded
runs, not a claim that the complete suite was rerun after every later commit.

The updated schema component checks pass **16/16** and updater download/scheduling
unit checks pass **43/43**. The built updater PE subsystem is Windows GUI (2), verifying
the console-free executable setting without running an installation. No installer
smoke tests or deployment were performed. The bridge service is Running, and zero
`akml_review_*` tables/views remain in tempdb after the final browser run.

The full updated shell suite passes **927/927** (Shell.Current.trx), including the new shortcut isolation cases.

### Playwright report failure corrected — 2026-10-13

Inspected playwright-results/20261013-105553-385/playwright.html and its TRX:
97 passed, 1 failed, 13 skipped. Firefox saw two Save as palette rows. The editor's
asynchronous initialization could resume after navigation disposed the page, then
register commands whose disposal token was never released. A deterministic lifetime
regression reproduced the leaked commands before the fix. Initialization now stops
after awaited loads when the editor is disposed, before attaching more handlers or
registering commands. The browser's unique-command assertion remains unchanged.

The 13 focused lifetime/command-registry unit tests pass. The headed Playwright rerun
passes all three browser keyboard cases (Chromium, Edge, Firefox) plus persistent
browser reopening: 4 passed, 0 failed, 1 expected spike-page skip. The runner now counts
NotExecuted result entries, since this adapter reported Counters.notExecuted=0 despite
13 skips in the original report. Its console correctly reports the single follow-up skip.
The original report is preserved; new HTML/TRX evidence is under
%LOCALAPPDATA%/AKML SQL/report-fix-20261013/. Original skips are opt-in native checks,
missing alias/spike configuration, and pre-existing scenario placeholders. This was
a focused fix verification, not another full-suite run.
