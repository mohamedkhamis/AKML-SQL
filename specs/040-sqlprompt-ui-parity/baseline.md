# Baseline and verification record: 040-sqlprompt-ui-parity

## Setup baseline (T001–T003, 2026-09-28)

Branch `040-sqlprompt-ui-parity` at `9158eaa`, before any spec 040 code change.

**T001 — build.** `MSBuild AKML-SQL.slnx -t:Restore` then `-t:Build -p:Configuration=Release -m`:
green (0 errors).

**T002 — test suites** (Release, `dotnet test --no-build`; the shell suite through its built
net472 dll):

| Suite | Passed | Failed | Skipped | Notes |
|---|---|---|---|---|
| AkmlSql.Core.Tests | 899 | 1 | 3 | Pre-existing red: `Ipc.ProfileGetMessageTests.Response_key_layout_is_positional_and_append_only` (expects 7 keys, finds 9). Skipped: `VisualReferenceCoverageTests` and two others |
| AkmlSql.Engine.Tests | 1,882 | 0 | 0 | 19 min 24 s |
| AkmlSql.IntelliSense.Tests | 12 | 0 | 0 | |
| AkmlSql.Formatting.Tests | 1,498 | 0 | 0 | |
| AkmlSql.Site.Tests | 873 | 0 | 0 | |
| AkmlSql.Web.Tests | 503 | 42 | 0 | Pre-existing red: all 42 are `Services.FormatterServiceTests.Formatter_MatchesIdeBaseline_AcrossCorpusAndProfiles` (the `sp031-*` pending golden baselines named in CLAUDE.md) |
| AkmlSql.Shell.Shared.Tests | 426 | 0 | 0 | |

- **Completion corpus** (`CorpusGateTests`, OVERALL): **1,311 / 1,343 = 97.6 %**. It may only go up.
- **Format-parity corpus:** 363 corpus `.sql` inputs, 265 golden files under `tests/format-parity/golden`.
  The goldens must stay unchanged.
- `PerformanceBaselineTests` and the History 2 ms timing test passed in this run; they are
  known to drift with machine load.

**T003 — settings backup.** `%AppData%\AKML SQL\config.json` and `history\` copied to
`C:\Users\Administrator\Documents\AKML SQL backups\spec-040-20260928\`. There is no
`profiles\` folder on this machine (no user styles), so there was nothing else to copy.

From here on, any new red in the list above is a regression this feature caused.

## US1–US3 verification (T047, T074, T083, 2026-09-28)

One Release pass covers all three P1 stories: they were built in order on the same working
tree, so each story's gate is this run.

**Build.** `MSBuild AKML-SQL.slnx -t:Restore`, then `-t:Build -p:Configuration=Release -m`:
green (0 errors). The first attempt failed in `AkmlSql.Ssms22` (CS1929: `await
TaskScheduler.Default` in the US2 history reconcile needs `Microsoft.VisualStudio.Threading`).
It was fixed with that `using` in `AkmlSqlPackage.cs`. The shell test project does not compile
the package, which is why the Debug shell runs had not caught it.

| Suite | Passed | Failed | Skipped | Against the baseline |
|---|---|---|---|---|
| AkmlSql.Core.Tests | 908 | 1 | 3 | +9 new tests. The same pre-existing red (`ProfileGetMessageTests.Response_key_layout_is_positional_and_append_only`) |
| AkmlSql.Engine.Tests | 1,916 | 0 | 0 | +34 new tests; 17 min 22 s. The History retry-timing test that ran 5.86 s against its 5.9 s floor in Debug passed here |
| AkmlSql.IntelliSense.Tests | 25 | 0 | 0 | +13 new tests |
| AkmlSql.Formatting.Tests | 1,499 | 0 | 0 | +1 (`EditorSchemaFixtureTests`) |
| AkmlSql.Site.Tests | 873 | 0 | 0 | Unchanged |
| AkmlSql.Web.Tests | 503 | 42 | 0 | Unchanged: the same 42 `FormatterServiceTests` sp031 goldens. `WebHistoryLogicTests` 14/14 |
| AkmlSql.Shell.Shared.Tests | 538 | 0 | 0 | +112 new tests (US1 75, US2 20, US3 17) |

- **Completion corpus** (`CorpusGateTests`, OVERALL): **1,311 / 1,343 = 97.6 %**, unchanged.
- **Format-parity goldens:** `git status tests/format-parity` is clean, so no golden changed.

**Quickstart scenarios 1–21: pending.** They need this build deployed into SSMS 22 on this
server, and nothing is installed there without the user's approval. Until then the stories
are verified by their automated tests only. The T083 screenshots (the style editor at its
default size) are taken with Northwind data when the scenarios are run.

**Deviations from tasks.md, recorded for review:**

- T073 (History settings page) was done in US1 with the other Options pages, so the US1
  allow-list test stayed green.
- T080 moved the preview card to theme colours (T100's colour part). `SqlPreviewView` colours
  SQL from theme tokens, which were unreadable on the fixed dark card in the light theme.
- T081: a clashing name on Import now always asks for a new name. The old "Style 'X' already
  exists. Overwrite?" confirm for your own styles is gone, so an import never overwrites a style.
- US2 added a `content_hash` column to `history_versions` (schema v3), so version counts and
  dedup include snapshots. It also hooks `DTEEvents.OnBeginShutdown` as well as the package's
  `QueryClose`, so tabs closed by shutdown stay open for restore.
- Tests call `OptionsCommand.RunOptionsLoop` rather than `ShowOptions`, which needs the VS main
  thread (`ThreadHelper`).
- `FormatStylesRowLayoutTests` lays out the window's content in a themed host, because a window
  that is never shown stays Collapsed and skips layout.

## Engine test isolation fix (2026-09-28, after the P1 commit)

The P1 Release run migrated this machine's real `%AppData%\AKML SQL\history\sqlhistory.db` to
schema v3 (no rows lost: 1,525 before and after; the morning backup is intact). The cause was
already there before spec 040: `HistoryDatabase()` and five other engine paths read
`Environment.SpecialFolder.ApplicationData` directly and ignored `AKML_APP_DATA_ROOT`. The engine
tests that build the whole engine (`EngineComposition.Build`, `EngineHost`) therefore opened the
user's real config and history database, including retention trimming.

- The six paths now use `AkmlSql.Core.Constants.AppDataPath`, which gives the same path in
  production.
- `tests/AkmlSql.Engine.Tests/TestAppDataRoot.cs` redirects `AKML_APP_DATA_ROOT` to a temp folder
  for the whole engine test run (module initializer).
- Two full Engine runs since the fix: the real history database and `config.json` keep their
  timestamps.

## US4 verification (T111, 2026-09-28)

**Build.** Release solution build: green (0 errors), including the VSCT (31 new buttons).

| Suite | Passed | Failed | Skipped | Against the P1 run |
|---|---|---|---|---|
| AkmlSql.Core.Tests | 910 | 1 | 3 | +2 (`FormatSelectionResponseTests`). Same pre-existing red |
| AkmlSql.Engine.Tests | 1,918 | 0 | 0 | +2 (`ProfileFallbackWarningTests` for Format Selection) |
| AkmlSql.IntelliSense.Tests | 25 | 0 | 0 | Unchanged |
| AkmlSql.Formatting.Tests | 1,499 | 0 | 0 | Unchanged |
| AkmlSql.Web.Tests | 503 | 42 | 0 | Unchanged (the same 42 sp031 goldens) |
| AkmlSql.Shell.Shared.Tests | 590 | 0 | 0 | +52 US4 tests |

- **Completion corpus:** 1,311 / 1,343 = 97.6 %, unchanged. **Format-parity goldens:** unchanged.
- One Debug shell run (of eight) failed `ActiveStyleMenuTests.Choosing_an_empty_slot_changes_nothing`
  once and passed in the other seven; it reads `config.json` through the process-wide
  `AKML_APP_DATA_ROOT`, so a test in another xunit collection touching config at the same moment
  is the likely cause. Watch for a recurrence.
- **T110 context bar:** not known until SSMS runs this build; the package logs every candidate
  name at Debug and the one it picked at Information ("Active Style: added to the editor context
  menu '…'").

**Quickstart scenarios 22–28: pending**, together with 1–21: deploying this build into SSMS was
requested but blocked by the session's permission rules, so nothing has been installed.

**Deviations from tasks.md:**

- T097: the page-tree badges inherit the leaf's colour rather than `AccentPrimary`, so they stay
  readable on the selected leaf.
- T102: no "team" styles exist yet, so Delete refuses built-in and active styles. The ⋮ glyph's
  accessible name (part of T185) was added here because T091 tests it.
- T098: `IsDirty` is now "differs from the saved values", so setting an option back clears it.

## US5 verification (T148, 2026-09-29)

**Build.** Release solution build (`-t:Restore`, then `-t:Build -p:Configuration=Release -m`):
green, 0 errors. The warnings are the existing VS threading-analyzer ones.

| Suite | Passed | Failed | Skipped | Against the US4 run |
|---|---|---|---|---|
| AkmlSql.Core.Tests | 952 | 1 | 3 | +42 (History date groups, line diff, US5 IPC contracts). Same pre-existing red (`ProfileGetMessageTests…append_only`: the response has 9 keys since spec 039, the test expects 7) |
| AkmlSql.Engine.Tests | 1,944 | 0 | 0 | +26 US5 tests; 17 min 44 s. The first full run failed 3 timing tests under the parallel load (the two History scale tests at 506/561 ms, and the existing `RefactoringPerformanceTests.SC001`); all 8 passed alone. The scale tests now run in a non-parallel collection after the rest |
| AkmlSql.Web.Tests | 503 | 42 | 0 | Unchanged: the same 42 sp031 goldens. History 37/37 |
| AkmlSql.Shell.Shared.Tests | 673 | 0 | 0 | +83 US5 tests (Release; Debug 673/673 too) |

- **Format-parity goldens:** unchanged (`git status tests/format-parity` clean).
- **Scale (T124):** 100,000 runs in 20,000 sessions: a grouped page and its count in about
  200 ms, a free-text search in about 250 ms (budget 250 ms each), after the grouped query was
  rewritten (aggregate first, then page, one pass for the total) and indexed on the group key.
- **Flaky shell tests:** tests that read `config.json` can fail once in several runs when a test
  in another xunit collection loads config at the same moment (`ConfigManager.Load` writes the
  defaults when the file is missing). The new History control, keyboard and restore tests run in
  the AppData isolation collection, so they don't add to it. Seen this session:
  `DisableRuleFixActionTests`, `ActiveProfileResolutionTests`, `FormatStylesLifecycleTests`, each
  once, each green on rerun.

**Found and fixed while testing (outside the US5 tasks):**

- *Show in Error List* never reached the Error List: `ErrorListReporter` fed a `TaskProvider`
  (the Task List). It is an `ErrorListProvider` now. (Scenario 6.)
- Every command in the SQL editor's command group was recorded in History as a run:
  `ExecutionCapture` matched the Query.Execute command by GUID only. It matches GUID and ID now
  (`ExecutionCommandMatchTests`).
- A never-run tab could not be offered for restore: the autosave and shutdown drafts did not mark
  the query open for this SSMS. They do now.
- Closing a query tab did not refresh an open History window, so the open bar stayed until the
  next refresh. The close now refreshes it.
- With rows loaded, a lost engine showed nothing: the centred message only covers an empty list.
  A banner with Retry now shows above the rows.
- History rows, versions, search box and preview had no accessible names (a screen reader read
  `AkmlSql.Core.Ipc.Messages.HistoryEntryDto`). They are named now.

**Quickstart scenarios 29–37 (and 1–28): queued.** The runner drives SSMS only while the desktop
is visible and idle; the Remote Desktop window has been minimised since 22:45 on 2026-09-28. A
chain is waiting: it closes the runner's own SSMS, deploys this build (SSMS only, the originals
kept in `Documents\AKML SQL backups\spec-040-deploy-20260928`), runs scenarios 1, 6, 7, 9–17 and
29–37 with a Format Styles probe, switches SSMS IntelliSense back on, closes SSMS and restores the
original `config.json` and History database. Results land in the scratchpad's
`verify\results.txt`; they will be recorded here.

**Deviations from tasks.md:** see the notes under T147 (the `GetEntries` action, version
server/database, `HistoryQueryOpener`, the remove-older date format, the rename refusal text).

## Final verification (T170, T191, T198, 2026-10-04)

**Build.** `040-sqlprompt-ui-parity` at `45a8c63` plus the working tree (the fixes below), deployed
into SSMS 22 only with the private engine (`deploy-ssms.ps1`; the originals stay in
`Documents\AKML SQL backups\spec-040-deploy-20260928`, and each run restores `config.json` and the
History database afterwards).

| Suite | Passed | Failed | Skipped | Notes |
|---|---|---|---|---|
| AkmlSql.Shell.Shared.Tests | 819 | 0 | 0 | Release |
| AkmlSql.Engine.Tests | 1,990 | 0 | 0 | |
| AkmlSql.Core.Tests | 1,092 | 1 | 3 | the same pre-existing red (`ProfileGetMessageTests…append_only`) |
| AkmlSql.Formatting.Tests | 1,515 | 0 | 0 | format-parity goldens unchanged |
| AkmlSql.IntelliSense.Tests | 25 | 0 | 0 | |
| AkmlSql.Site.Tests | 877 | 0 | 0 | |

**Quickstart scenarios 1–49** were driven in SSMS 22 by a UI Automation runner (Northwind only).
Each result is the last run on this build or, for scenarios the later fixes cannot reach, the
morning's full run. A regression pass on the final build (2, 3, 4, 5, 6, 12, 14, 16, 29, 35, 38,
39, 44, 45) was all green.

| # | Scenario | Result | Notes |
|---|---|---|---|
| 1 | Settings that do nothing are gone | Pass | 29 pages walked, no dead rows |
| 2 | Maximum suggestions | Pass | 10 → 10 objects, 50 → 30 (all there are) |
| 3 | Trigger delay | Pass | list after 1.37 s with a 1 s delay; Ctrl+Space at once |
| 4 | Fuzzy matching | Pass | |
| 5 | Detail text | Pass | |
| 6 | Error List | Pass | |
| 7 | Restore defaults keeps hidden data | Pass | rule override kept |
| 8 | AI reset warns | Pass | |
| 9 | Cancel means Cancel | Pass | |
| 10 | System theme | Pass | SSMS runs its light theme here; the dark case was not seen |
| 11 | Full preview | Pass | |
| 12 | Open state | Pass | |
| 13 | Scroll to the end | Pass | |
| 14 | Grouped delete | Pass | after the repeated-close fix |
| 15 | Grouped star | Pass | |
| 16 | Search after snapshot | Pass | after the tab-switch snapshot fix |
| 17 | History settings | Pass | |
| 18 | Labels | Pass | |
| 19 | Tab-true preview | Check | screenshots `s19-*`: lines line up |
| 20 | Import | Pass | |
| 21 | Export with unsaved edits | Pass | |
| 22 | Option search | Pass | |
| 23 | Change markers | Pass | |
| 24 | Coloured preview | Check | screenshot `s24-light-preview` |
| 25 | Active Style menu | Pass | |
| 26 | Editor context menu | Pass | Format Document and Active Style ▸ in "SQL Files Editor Context" (an earlier run read the menu before SSMS had added them) |
| 27 | List actions | Pass | |
| 28 | Format feedback | Pass | after the notice fix |
| 29 | Search as you type | Pass | |
| 30 | Advanced search | Pass | |
| 31 | Rows | Pass | |
| 32 | Versions | Pass | |
| 33 | Keyboard only | Pass | after the F2 and focus fixes |
| 34 | Row menu | Pass | |
| 35 | Live refresh | Pass | |
| 36 | Disconnected | Pass | |
| 37 | Restore | Pass | |
| 38 | Tree | Pass | |
| 39 | Child options | Pass | |
| 40 | Numbers | Pass | |
| 41 | Palette options | Pass | after the Enter and ranking fixes |
| 42 | Tab colours | Check | the rule is added and selected; choosing Production, reordering, the red tab and the Safety prompt are a manual check (screenshots `s42-*`) |
| 43 | Dark theme | Check | screenshots `s43-dark-*` |
| 44 | Menu | Pass | |
| 45 | Titles | Pass | |
| 46 | F1 | Waived | F1 opens the default browser; routing is covered by `HelpRoutingTests` and `F1SlugTests` |
| 47 | Screen reader | Pass | every History button named; listening with Narrator is manual |
| 48 | Team styles | Pass | |
| 49 | Format SQL actions | Pass | after the startup-session and cache fixes; also right after 36 kills the engine ("Engine restarted: sent 1 open editor(s) again") |

**Found and fixed while verifying** (details in `doc/progress.md` › Spec 040 › Issues hit):

- SSMS raises `DocumentClosing` up to three times per tab close; the repeats recorded an executed
  query again as "Not executed" drafts and doubled Reopen Closed Tab entries
  (`RepeatedCloseFilter`).
- The tab-switch snapshot never ran on run → edit → switch; an unchanged text is no longer a new
  version.
- A tab open at SSMS start had no schema session (its connection was dropped while the engine
  started); evicted schema caches were never reloaded, and eviction ignored use.
- Enter in the Command Palette was swallowed by the editor's command handler; options now rank
  before letter-by-letter command matches.
- No Format notice ever showed (`FormatFailureNotifier` cast `SVsShell` to `IServiceProvider`).
- History keyboard: focus returns to the selected row after Space; F2 reaches the list through the
  pane's `PreProcessMessage`; a key pressed while the list reloads runs afterwards.
- An engine restart (crash, or scenario 36 killing it) left every open tab without a session in
  the new engine; the shell now sends each open editor's text and connection again.
- Format styles message boxes are titled `AKML SQL – Format styles`.

**Screenshot tour (T193):** `SsmsScreenshotTour.Capture_spec_040_windows` passed on the
deployed build (2026-10-04 15:53): the AKML SQL menu with Active Style ▸, Options › Behavior,
History and Color in light and dark, SQL History with Advanced search open, and Format Styles on
Lists — nine images in `%TEMP%\akml-ssms-tour\spec040-*.png`, Northwind only, none blank. The
theme it changes is put back; `config.json`, History and the styles folder were restored after the
runs (the imported test styles and their `.source.json` files removed).
