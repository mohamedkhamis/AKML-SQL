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
