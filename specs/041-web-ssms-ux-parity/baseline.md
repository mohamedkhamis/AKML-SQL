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
