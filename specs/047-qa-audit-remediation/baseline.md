# Baseline and acceptance record: spec 047

The acceptance gate for "done" (quickstart.md). Filled in during implementation. **Before** is the branch as it was
at the start (master 0745268f + the plan documents). **After** is the state at the end of the feature.

## Ratchets (Principle III: may only go up)

| Gate | Before | After |
| --- | --- | --- |
| `FormatParityTests` (golden pairs passing / total) | 264 / 264 | 312 / 312 (the 48 new sp047 goldens; the 102 changed goldens are listed below) |
| `CorpusGateTests` completion corpus score | 1311 / 1343 (97.6 %), all family thresholds met | 1311 / 1343 (97.6 %), all family thresholds met (unchanged) |
| PostgreSQL completion corpus gate | green (inside AkmlSql.PostgreSql.Tests, 2264 pass) | green (2264 pass, unchanged) |
| PostgreSQL format corpus gate | green (inside AkmlSql.PostgreSql.Tests) | green (unchanged) |

## Suite results (pass / fail / skip)

| Suite | Before | After | Notes |
| --- | --- | --- | --- |
| AkmlSql.Core.Tests | 1635 / 0 / 3 | 1641 / 0 / 3 | |
| AkmlSql.Formatting.Tests | 1676 / 0 / 0 | 1930 / 0 / 0 | incl. `FormatParityTests` 312 / 312 |
| AkmlSql.IntelliSense.Tests | 25 / 0 / 0 | 25 / 0 / 0 | |
| AkmlSql.Analysis.Tests | 5 / 0 / 0 | 10 / 0 / 0 | |
| AkmlSql.AI.Tests | 92 / 0 / 0 | 122 / 0 / 0 | |
| AkmlSql.Engine.Tests | 2717 / 0 / 132 (21 min) | 2731 / 0 / 132 (22 min) | the live Northwind facts ran (SQL Server on (local)); PostgreSQL live facts skip (no server); `PerformanceBaselineTests` passed this time |
| AkmlSql.PostgreSql.Tests | 2264 / 0 / 0 | 2264 / 0 / 0 | |
| AkmlSql.Shell.Shared.Tests | 1082 / 1 / 0 | 1111 / 0 / 0 | the 1 failure before is the known parallel-run flake below |
| AkmlSql.Web.Tests | 2146 / 0 / 0 | 2305 / 0 / 0 | |
| Web JS (node) | 309 / 0 / 0 (the baseline run reported 316: node ran last, after the 7 new spec 047 tests and their code existed) | 321 / 0 / 0 | |
| AkmlSql.Site.Tests | 917 / 0 / 0 | 925 / 0 / 0 | incl. the 8 `DocsQualityTests` |
| AkmlSql.Installer.Tests (`Category!=InstallerSmoke`) | 129 / 0 / 0 | 139 / 0 / 0 | |
| AkmlSql.E2E.Tests | 111 / 0 / 1 | 115 / 0 / 1 (16 min) | incl. the 4 `PublishedCli` facts on a fresh trimmed publish. The same skip as before (`RevokedBearer_OnReconnect_ReturnsPinRequired`). The first full run had 5 `BridgeHandshakeTests` fixture failures from the publish/build race described below; fixed. |
| AkmlSql.Web.E2E.Tests (per-story classes) | taken per story (tasks.md execution note 3) | full serial run 2026-11-22: 156 / 7 / 65 (47 min) | 6 are the environmental failures known before (5 `EngineAutoConnectTests`: this install is localhost-only; `EngineServedWebTests`: the Debug bundle's hot-reload 404). The 7th, `RefactorMenuKeyboardTests`, was a race in the new test: it pressed Tab before the menu had taken focus. It now waits for that, and passed 5 of 5 runs. The 65 skips need a PostgreSQL server, a SQL login or an opt-in capture. |

A race this feature introduced, and fixed: in the full T138 run of `AkmlSql.E2E.Tests`, the 5 `BridgeHandshakeTests`
failed in their fixture (`CS2012: Cannot open ...\AkmlSql.Analysis\obj\Release\net10.0\AkmlSql.Analysis.dll for writing`).
US1's `PublishedAnalyzerFixture` publishes the analyzer in Release, and the engine-launch fixture builds the engine in
Release. Both compile `AkmlSql.Analysis` into the same `obj/Release`, and xUnit ran the two collections side by side.
US1 had run `Category=PublishedCli` alone, so it did not show then. `PublishedAnalyzerTests` now has its own collection
with parallelization off (`PublishedCliCollection`), so xUnit runs it alone after the parallel ones.

Known failures before this feature, which are not regressions:

- `FormatStylesLifecycleTests.Deleting_the_active_style_is_refused_before_any_ipc` (Shell.Shared) failed in the full parallel
  run ("FakeRpcClientAccessor: no canned response") and passes alone (7/7). It writes the active style into the shared
  `config.json`, so a parallel test can race it, as CLAUDE.md describes for `ConfigManager`-based tests.
- Build: the T005 Release build was green (0 errors; only the existing MSB3270 architecture warnings). The theme gate was
  green for the web and site themes (`generate-theme-css.ps1 -CheckOnly -RepoRoot .`; under Windows PowerShell 5.1 the
  script needs `-RepoRoot .`). MSBuild here is `C:\Program Files\Microsoft Visual Studio\18\Insiders\...`, not `18\Enterprise`.

## Browser regression runs (per story, tasks.md execution note 3)

| Run | Classes | Result | Analysis |
| --- | --- | --- | --- |
| US2/US3, 2026-11-21 (Debug web + engine builds of the working tree) | EngineAutoConnectTests, EngineServedWebTests, ConnectDialogTests, SecretStorageTests, SettingsPageTests | 20 / 6 / 0 | All 6 failures are environmental, see below. ConnectDialogTests, SecretStorageTests and SettingsPageTests are 100 % green. |
| US6, 2026-11-21 (Debug web + engine builds of the working tree) | ParseMarkerBrowserTests, ResultsMessagesTests | 12 / 0 / 0 | Green. ParseMarkerBrowserTests fails on HEAD (fails-before log). |
| US7 before, 2026-11-21 (the US6 Debug builds, before the first US7 change; `--no-build`) | QuickstartAcceptanceTests, ShellPolishTests | 19 / 0 / 0 | The baseline for T111. |
| US8 after (T116), 2026-11-21 (Debug web build with the AI panel change) | UserStory5AiTests | 3 / 0 / 0 | Green. |
| US10 after (T133), 2026-11-22 (Debug web build with every US10 change) | AxeAccessibilityTests, TabStripKeyboardTests, WorkspaceAccessibilityTests; WorkspaceLayoutTests, ShellPolishTests, QueryTabsTests | 7 / 0 / 0; 43 / 0 / 6 | Axe: 0 violations on all 18 route × theme pairs. The first after-run left 6, all fixed: `aria-required-children` (a close button inside the tablist, now owned out through `aria-owns`), the muted badge on a selected row in dark and high contrast, and the hovered Settings nav link in dark. The 6 skips are QueryTabsTests that need a PostgreSQL server or a SQL login. |
| US10 before, 2026-11-21 (before the first US10 change) | WorkspaceAccessibilityTests, WorkspaceLayoutTests, TabStripKeyboardTests, AxeAccessibilityTests | 17 / 2 / 0 | The 2 failures are the new tests' fails-before (T126 and the T127 fact); the existing classes are green. Axe reported 41 violations over the 18 route × theme pairs, listed under the fails-before log. |
| US7 after (T111), 2026-11-21 (Debug web + engine builds of the working tree) | SnippetsInEditorTests, RefactorMenuKeyboardTests, QuickstartAcceptanceTests, ShellPolishTests | 20 / 1 / 0, then 21 / 0 / 0 | The one failure was the new test, not the product. It pressed Tab within CodeMirror's `interactionDelay` after the list refreshed, so the line got indented (four spaces before `qaz`). The test now waits for `qaz` to be the selected option of a settled list, and passes. No regression against the "before" row. |

- **EngineAutoConnectTests (5 failures).** These tests run the working-tree web app against the *installed*
  `AkmlSqlWebEngine` service: the 11/20 install in Program Files, so none of this feature's engine changes are in it. They pair
  by `Environment.MachineName` over `wss`, which needs a network-exposed install. This machine's install is
  `Exposure = Localhost` (`HKLM\Software\AKML SQL\Web`), and HTTP.sys listens on `127.0.0.1:59417` only. Every pairing
  failed with `net::ERR_CONNECTION_REFUSED` on `wss://1442-mohamedkha:59417` before any handshake. The class has no
  `InstalledEngine.IsNetworkMode` skip; that is a harness follow-up, not a product defect.
- **EngineServedWebTests.Engine_serves_the_real_app_… (1 failure).** Every functional step passed: 10 routes at 200 with the
  contract headers and Live, SQL formatted, 7 of 7 corpus rows. The final zero-HTTP-error assertion failed on 404s for
  `_framework/dotnet-browser-tools/hot-reload-settings.json`. The test serves the Debug bundle, and the SDK's
  hot-reload agent (`Microsoft.DotNet.HotReload.WebAssembly.Browser`, in `bin/Debug/.../_framework`) requests that file.
  CLAUDE.md notes this 404 as dev tooling. The request comes from no code this feature changed.
- T026: no `web.config` change is needed for `akml-engine.json`. IIS already maps `.json` to `application/json`
  (`appcmd`), and the CSP's `connect-src 'self'` allows the same-origin GET. An unknown file falls back to `index.html`
  with 200, and `readManifest` returns null for it (JSON parse fails).

## Fails-before log (FR-038)

| Test | Issue | Failure seen before the fix |
| --- | --- | --- |
| `scripts/test-analyzer-cli.ps1` against the installed 1.26.1104.1510 exe | CLI-001 | all 4 checks FAIL: "No issues found" for multi.sql and sem1.sql; `--check` exits 0 |
| `PublishedAnalyzerTests` (4 facts) | CLI-001 | the same four checks failed on the pre-fix published exe (the installed 1.26.1104.1510 build): "No issues found"; with the fix 4/4 pass on a fresh trimmed publish |
| `Sp047FormatterRulesTests` (93 style/rule cases + 66 stability cases over the six `sp047-*` inputs) | FMT-001…005 | First run, five inputs: 41 rule cases failed. FMT-001, FMT-002, FMT-003 and FMT-005 failed in all 10 classic-layout styles (8 built-ins, web default, web ANSI), and FMT-004 in khamis-style. **Final suite against HEAD's code (2026-11-21, a HEAD worktree with the final test file and inputs): 72 of 159 fail.** Seven rule facts fail in all 10 classic styles: FMT-001; FMT-002 alignment; FMT-002 statements after a block or CASE end; FMT-002 every statement on its own line; FMT-003; FMT-004 statement after a leading comment; FMT-005. FMT-004 relative indentation fails in khamis-style, plus the CRLF case. The SQL Prompt-layout style passes every rule on HEAD, and every stability/token case passes. Working tree: 159 / 159. |
| `LineEndingTests.A_CRLF_script_comes_back_CRLF_with_no_lone_LF` | SSMS-004 | CRLF in, LF out (also failing in the HEAD worktree run above) |
| `IndentationTrackerTests` (CASE END, BEGIN TRAN family ×5, END CONVERSATION) | FMT-002 | 7 failures against the old tracker, with the look-ahead parameter added first and no behaviour change; 7 / 7 after |
| `Parity2_CommentFormattingTests` (rewritten normaliseIndent cases) | FMT-004 | the spec 030 cases asserted the flattening FR-014 forbids, so they were rewritten to the uniform-shift contract together with the fix (21 / 21) |
| `InListPlacementTests`, `FormattingProfileTests.ExpressionOptions_Defaults` | FMT-005 / FR-015 | asserted the old `multiLine` default that exploded a 3-item list; updated with the default change, plus a long-list and an explicit-`multiLine` case |
| `ParseErrorStatementTests.A_broken_statements_own_line_breaks_are_written_like_the_rest` | SSMS-004 | asserted "no CR at all" (LF-only output); restated as "no mixed endings": CRLF in gives CRLF throughout, no lone LF, no CR CR |
| `SqlPromptPreviewSampleTests` (align multi-line comments) | FMT-004 | the preview showed a difference only through a CRLF/LF artifact. Stage 9 writes one convention, which exposed it. The Whitespace sample's comment now stands deeper than its code, so the option visibly applies. `sqlprompt-editor-schema.json` regenerated. |
| `FormatRequestHandlerTests.HandleFormat_CrLfDocument_ComesBackCrLf` (engine) | SSMS-004 | written with the fix; the same input fails before it at the pipeline level (row above) |
| US2/US3 web and engine tests (`PageEngineManifestTests`, `LocalPairResultTests`, `LocalPairFailureTests`, `PairingManageHandlerTests`, `PairedBrowsersSectionTests`, `ConnectionSaveRememberTests`, `ConnectionPrefillNameTests`, `TestAndRememberTests`, `SavedConnectionJsonTests`, `BrowserStorageStateTests`, `DiagnosticsStorageRowTests`, `OriginBannerTests`, handshake token-hash cases) | WEB-001…004, WEB-010, ENG-001 | written before the code: they did not compile (the types, members and components they use did not exist), then passed against the implementation |
| `akml-page-engine.test.mjs` (readManifest) | WEB-001 | SyntaxError: the module does not provide an export named 'readManifest' |
| `SquiggleStyleMigrationTests`, `DiagnosticTaggerPresentationTests`, `LightbulbAdvisoryHintsTests`, `CodeAnalysisPageAdvisoryRowTests`, `AnalysisDeferredUntilEngineTests`, `AdvisoryDisplaySettingTests` | UX-001, SSMS-001, SSMS-002 | written before the code: they did not compile (`MigrateSquiggleStyle`, `ShouldTag`, `OffersFixActions`, the controller seams, the Advisory findings combo and `AdvisoryDisplay` did not exist). HEAD's `SquiggleStyle` default is `"underline"`, which the default fact rejects |
| `akml-editor.diagnostics.test.mjs` (marker-only mode) | UX-001 | HEAD worktree, HEAD's `akml-editor.js`: 13 pass, 1 fail. "marker-only mode marks hint and info diagnostics for no underline" expected `akml-lint-marker-only` on the hint and info diagnostics and got `undefined` |
| `ParseErrorFindingTests`, `SyntaxCheckTests` | WEB-005 | written before the code: they did not compile (`reportParseErrors` and `SyntaxCheck` did not exist) |
| `AnalyserServiceTests.AnalyseAsync_reports_sql_that_does_not_parse_as_a_syntax_error` | WEB-005 | HEAD worktree: `Assert.Single()` failure, no `SYNTAX` issue for `SELEC * FROM dbo.Customers;` |
| `ProblemsSyntaxErrorTests.Syntax_errors_are_listed_first_and_the_empty_state_is_absent` | WEB-005 | HEAD worktree: the list read `["ST004", "SYNTAX"]` |
| `ParseMarkerBrowserTests` (Playwright, HEAD's Debug web build) | WEB-005 | HEAD worktree: no Error row in Problems after Analyse (`ToHaveCountAsync(1)` timed out after 30 s). Working tree: 1 / 1 |

| US7 web tests against the T093 stubs (`CompletionUserSnippetTests`, `SchemaTreeMetadataLimitedTests`, `SchemaTreeFilterEmptyTests`, `RefactorSchemaTests`, `RefactorExpandWithSnapshotTests`, `StyleImportMessageTests`, `SnippetImportMessageTests`) | SN-001, WEB-011, WEB-013, WEB-014, UX-002 | 33 of 41 failed. The 8 that passed are the guards: mid-clause gives no snippet, complete metadata keeps its counts, a matching filter shows no empty row, and valid imports still import. |
| `PhaseAMetadataLimitedTests` against the stub probe | WEB-013 | 2 of 3 failed: the payload flag, and the live probe (the stub said "not limited" after `EXECUTE AS LOGIN = N'NT AUTHORITY\SYSTEM'`). The older-payload guard passed. |
| `FindInvalidObjectsCountTests.On_Northwind_the_count_matches_the_dependency_catalog` (live) | SSMS-003 | HEAD worktree: Expected 63, Actual 0, the audit's "0 dependency rows scanned". |
| `RefactorPreviewKeyboardTests` (written with T109) | WEB-012 | HEAD worktree: 4 / 4 failed (no `stashFocus`/`focusFirst`, Escape does nothing, a no-change preview hides its warnings) |
| `RefactorMenuTests`, `FindInvalidObjectsDialogTextTests`, `FindInvalidObjectsCountTests` pure fact | WEB-012, SSMS-003 | written before the code: they did not compile (`RefactorMenu`, `SummaryText`, `ShowsGrid` and `BuildResponse` did not exist) |
| `SnippetsInEditorTests` (Playwright) | SN-001 | HEAD's Debug web build: no `qaz` completion option, 15 s timeout |
| `RefactorMenuKeyboardTests` (Playwright) | WEB-012 | HEAD's Debug web build: focus never moved into the menu (the wait for a focused menu item timed out after 30 s) |
| `PromptPrivacyModeTests` | AI-001 | against the unchanged prompts: 16 of 30 failed (every no-schema case, 7 builders × 2 dialects, plus the index-advice case per dialect); the 14 with-schema guards passed |
| `AiPanelFixWithoutErrorTests` | AI-002 | against the unchanged panel: 3 / 3 failed (no `ai-fix` control; Fix enabled without an error) |
| `DocsQualityTests` (Site) | DOC-001…003 | HEAD worktree (HEAD's `doc/`): 8 / 8 failed. configuration's summary was "The configuration file is stored at:" (36 characters); `configuration.md -> deployment.md#web-edition` is a link the site cannot serve; the five web symptom headings were missing; connecting had no "Remember" / "engine machine" text. No other published page failed. |
| `akml-editor.attributes.test.mjs` (editor name) | WEB-007 | SyntaxError: the module does not provide an export named 'editorNameAttributes' |
| `TabStripKeyboardTests.A_query_tabs_close_button_is_beside_it_and_every_way_of_closing_works` | WEB-009 | failed before US10's first change: the close button is a descendant of its tab |
| `AxeAccessibilityTests` (18 route × theme pairs) | WEB-007…009 | 41 violations before US10's first change. The same in all three themes: `aria-input-field-name` (`.cm-content`), `nested-interactive` (`#doc-tab-…`) and `page-has-heading-one` on `/editor`; `region` on all six routes (the status bar's items); `heading-order` (`h3`) and `scrollable-region-focusable` (`pre`) on `/styles`; `label-title-only` (the Server and Database selects) on `/history`. `color-contrast` on `/styles` (`.akml-styles-badge.muted`: the selected row in light, every built-in row in dark and high contrast) and on `/settings/connections` (`.akml-connections-badge--active` in light and dark, plus the active Settings nav link in dark, which the audit's light-only run could not see). `empty-table-header` did not show (no saved connection was listed). |

HEAD worktree runs carry the new `sp047-*` corpus inputs without their web baselines, so
`AnalyserServiceTests.Analyser_MatchesIdeBaseline_AcrossCorpus(sp047-*)` fails there. That says nothing either way.

## Validation scenarios (quickstart.md §3)

| # | Scenario | Before | After | Result | Evidence |
| --- | --- | --- | --- | --- | --- |
| V1 | Installed analyzer on the CLI fixtures | 2026-11-21: installed 1.26.1104.1510 reports "No issues found" for all four checks | 2026-11-21: `build.ps1 -SkipTests -SkipShell` step [11] "Analyzer CLI (smoke)" ok x4 on the new publish | Pass (build); installed-exe check after the next install (T140) | `scripts/test-analyzer-cli.ps1` output |
| V2 | New profile on the IIS page pairs in one click or none, < 30 s, 10 of 10 | audit: "Use this computer's engine" failed on an IIS install whose bridge port is not 47291 | `PageEngineManifestTests`, `akml-page-engine.test.mjs` (readManifest), `EngineManifestScriptTests` | Automated: Pass. installed-product run: Not Run (needs the T140 install, owner go-ahead) | baseline.md fails-before rows (WEB-001) |
| V3 | Engine stopped: failure notice + Add engine… | audit: no notice | `LocalPairFailureTests` (notice text, Add engine… prefill) | Automated: Pass. installed-product run: Not Run (needs the T140 install, owner go-ahead) | fails-before rows (WEB-001) |
| V4 | Two paired browsers listed; revoke one | audit: 166 indistinguishable "Web edition" entries | `PairingManageHandlerTests` (list, IsCurrent, revoke closes sockets), `PairedBrowsersSectionTests` | Automated: Pass. A LAN-mode run with two real browsers: Not Run (T037; needs a LAN-exposed install) | fails-before rows (ENG-001) |
| V5 | Save with Remember (correct password) survives a browser and engine restart | audit: blocked (no SQL login) | `ConnectionSaveRememberTests`, `TestAndRememberTests` (fake service) | Automated: Pass. Live: Not Run (needs a SQL login on (local), owner go-ahead, and the T140 install) | fails-before rows (WEB-002) |
| V6 | Save with Remember (wrong password) keeps nothing | audit: the flag was saved without the password | `ConnectionSaveRememberTests` (the "login test failed" row) | Automated: Pass. installed-product run: Not Run (needs the T140 install, owner go-ahead) | fails-before rows (WEB-002) |
| V7 | `127.0.0.1` banner; Diagnostics storage row | audit: another address showed nothing saved, with no explanation | `OriginBannerTests`, `DiagnosticsStorageRowTests`, `BrowserStorageStateTests` | Automated: Pass. installed-product run: Not Run (needs the T140 install, owner go-ahead) | fails-before rows (WEB-003) |
| V8 | Formatter rules on sp047 + the audit's samples, all styles | 72 of 159 rule and stability cases failed on HEAD | `Sp047FormatterRulesTests` 159 / 159 (11 styles), `FormatParityTests` with the sp047 goldens; web parity baselines | Pass (automated, every style, second format unchanged) | fails-before row (FMT-001…005); golden table below |
| V9 | SSMS Format Document keeps CRLF | CRLF in, LF out (HEAD) | `LineEndingTests`, `FormatRequestHandlerTests.HandleFormat_CrLfDocument_ComesBackCrLf` | Automated: Pass. SSMS desktop run: Not Run (needs the T140 install and a live SSMS session) | fails-before rows (SSMS-004) |
| V10 | Missing semicolon: margin dot only (web + SSMS) | audit: the whole statement underlined | `DiagnosticTaggerPresentationTests`, `akml-editor.diagnostics.test.mjs` (marker-only), `AdvisoryDisplaySettingTests` | Automated: Pass. Visual check in the web editor and SSMS: Not Run (T140) | fails-before rows (UX-001) |
| V11 | SSMS Advisory findings = Underline brings underlines back | audit: `squiggleStyle` never read | `CodeAnalysisPageAdvisoryRowTests`, `SquiggleStyleMigrationTests`, `DiagnosticTaggerPresentationTests` | Automated: Pass. SSMS desktop run: Not Run (needs the T140 install and a live SSMS session) | fails-before rows (SSMS-001) |
| V12 | SSMS opened file analysed without typing | audit: no finding until an edit | `AnalysisDeferredUntilEngineTests` (a late engine gets one deferred analysis) | Automated: Pass. SSMS desktop run: Not Run (needs the T140 install and a live SSMS session) | fails-before rows (SSMS-002) |
| V13 | Web Analyse/Parse on invalid SQL, offline | audit: "No problems found."; Parse disabled | `ParseMarkerBrowserTests` (Playwright, no engine: Error row, red mark, "Parse failed: 1 syntax error.", browser-check line), `ParseErrorFindingTests`, `SyntaxCheckTests` | Pass (Debug web build). installed-product run: Not Run (needs the T140 install, owner go-ahead) | fails-before rows (WEB-005) |
| V14 | Custom snippet completes and expands | HEAD: no `qaz` option | `SnippetsInEditorTests` (Playwright: create, Tab expands with the caret at `$CURSOR$`, delete stops it), `CompletionUserSnippetTests` (online, offline, kind, cache) | Pass (Debug web build). installed-product run: Not Run (needs the T140 install, owner go-ahead) | fails-before rows (SN-001) |
| V15 | Explorer metadata note; 7 procedures after the grant | audit: "Stored Procedures (0)" for a database with 7 | `PhaseAMetadataLimitedTests` (live: as `NT AUTHORITY\SYSTEM` on Northwind the probe says limited), `SchemaTreeMetadataLimitedTests` | Automated: Pass. The GRANT and the count of 7: Not Run (`GRANT VIEW DEFINITION` needs the owner's go-ahead, and the T140 install) | fails-before rows (WEB-013) |
| V16 | Expand INSERT columns; Escape and focus | audit: Expand items never worked; Escape did nothing | `RefactorExpandWithSnapshotTests` (dbo.Region columns, `@CustomerID = 'ALFKI'`), `RefactorMenuTests`, `RefactorPreviewKeyboardTests`, `RefactorMenuKeyboardTests` (Playwright) | Pass (automated; menu and preview by keyboard in the browser). Apply on a connected installed page: Not Run (T140) | fails-before rows (WEB-011, WEB-012) |
| V17 | Explorer filter empty state | audit: the tree went blank | `SchemaTreeFilterEmptyTests` | Pass (automated) | fails-before rows (WEB-014) |
| V18 | Plain import messages | audit: parser internals; snippets said nothing | `StyleImportMessageTests` (.txt, a snippet JSON, an array), `SnippetImportMessageTests` (.txt, array, style JSON; `aria-live`) | Pass (automated) | fails-before rows (UX-002) |
| V19 | SSMS Find Invalid Objects summary | HEAD: Northwind reported 0 (expected 63) | `FindInvalidObjectsCountTests` (live: 63 = the independent count, no records), `FindInvalidObjectsDialogTextTests` ("No invalid objects found. All 63 references resolve.", no grid) | Automated: Pass. SSMS desktop run: Not Run (needs the T140 install and a live SSMS session) | fails-before rows (SSMS-003) |
| V20 | AI No-schema prompt; Fix disabled without an error | audit: "following schema:" with no schema; Fix sent "(no error)" | `PromptPrivacyModeTests` (7 builders × 2 dialects), `AiPanelFixWithoutErrorTests`, `UserStory5AiTests` (mock provider) | Pass (automated) | fails-before rows (AI-001, AI-002) |
| V21 | Site search finds the symptoms | audit: no entries | `DocsQualityTests` (summaries, links, the five symptom headings, the connecting text) | Automated: Pass. The search on a local site preview: Not Run (needs an isolated local preview with its own analytics database, CLAUDE.md) | fails-before row (DOC-001…003) |
| V22 | axe 0 violations; editor name announced | 41 violations before US10 | `AxeAccessibilityTests` (6 routes × 3 themes), `akml-editor.attributes.test.mjs` (the name follows the tab) | see the US10 after row (browser runs). A screen reader on the editor: Not Run (no screen reader on this machine; the name is checked through axe `aria-input-field-name` and the node test) | fails-before rows (WEB-007…009) |

## Golden changes accepted (T064, T065)

Reviewed 2026-11-21. Each changed golden was compared with HEAD after `git diff --ignore-space-at-eol`. A script undid the
known rule-required changes in the old text one at a time (join padding, statement-level SET padding, collapsed short IN
list, statement break after a semicolon) and compared the result with the new text; 85 of 102 matched exactly. The other
17 were read by hand. No change is left unexplained.

**Totals:** 102 existing goldens changed (of 264), plus 48 new `sp047-*` goldens (6 inputs × 8 styles, all hand-read
against contracts/formatter.md §2 and passing `Sp047FormatterRulesTests`). `FormatParityTests`: 312 / 312.

| Goldens | Rule that requires the change |
| --- | --- |
| 61 across `02-multi-join`, `04-multiple-ctes`, `11-stored-procedure`, `sp031-05-joins`, `sp031-06-subqueries`, `sp031-08-case-long`, `sp031-10-cte-columns`, `sp031-13-update-delete`, `sp031-17-function-calls` and others | FMT-005 (FR-015): one space after a join keyword. `INNER JOIN   dbo.Orders o` was JOIN padded from its own 4 letters to SELECT's 6 although the line starts with INNER; join keywords are no longer in the keyword column. |
| 37, mainly `07-in-list-short`, `sp031-04-between-in`, plus short lists in `sp031-06`, `sp031-08`, `sp031-13` | FR-015: a short IN list stays on one line. `inListStyle` was `multiLine` only as the class default in the six classic built-ins and the web's two profiles, so even `('Open', 'Pending', 'Shipped')` was exploded. It is now `auto` (60-character threshold); long lists still get one item per line. In AlignedLeftBracket a collapsed list also takes its bracket back onto the IN line (`IN(10, 11)`, not `IN` / `(10, 11)`). |
| 16, e.g. `11-stored-procedure` ×8 (`SET nocount ON`), `sp031-14-declare-set` (`SET @totalfreight`) | FMT-005: a SET statement is not padded to another keyword's width. The keyword column is now per statement and per parenthesis level: collected over the whole script, it padded a lone SET to an earlier statement's SELECT, and a `SET @x = (` to its own subquery's SELECT. |
| `sp031-12-insert-select` ×7 | FMT-005, same cause: the NOT EXISTS subquery's FROM/WHERE were padded to the outer `INSERT INTO` width (`FROM        dbo.orders o`); they now line up with the subquery's own `SELECT 1`. The outer statement keeps its INSERT INTO column. |
| Compact: `sp031-13-update-delete`, `sp031-14-declare-set`, `sp031-16-control-flow`, `sp031-18-semicolons-go`, `sp031-19-comments`, `sp031-04-between-in`, `sp031-15-create-table` | FMT-002 root cause: every statement starts its own line. With no empty line between statements, Compact glued statements together (`SELECT 1 AS a;SELECT 2 AS b;`, `…0100'); INSERT INTO …`, `END WHILE (`, `note */SELECT 1;`) and indented a DECLARE as a continuation. |
| Compact: `sp031-02-distinct-top`, `sp031-11-insert-values` | Consequence of the row above. The old indented second line (`ORDER BY c.country;SELECT …`, `VALUES (…); INSERT …`) was a line wrap caused by the next statement being glued onto the line. Each statement now fits on its own line, as Compact's "collapses short statements" says. |
| `sp031-08-case-long__khamis-style`, `05-case-searched__khamis-style` | FMT-002 (FR-012). A CASE expression's END is not a block's END. The old BEGIN/END pass moved it onto a line of its own at column 0 in the middle of the select list (`END AS freightband`), against the style's `case.endOnNewLine: false`. It now stays on its last branch line. |
| `sp031-16-control-flow__khamis-style` | FMT-002 (FR-012, "or the style's documented option"). Khamis Style sets `controlFlow.indentBeginEndKeywords: true`, which the classic layout never applied. The BEGIN/END of an IF/ELSE/WHILE body is now one level under its owner and the body one level under the BEGIN; continuation lines move with their statement. |
| `sp031-19-comments__khamis-style` | FMT-004 (FR-014): the multi-line comment keeps its inner indentation (`line` and `note */` stay three columns in) instead of being flattened. |

Web parity baselines (T065): 26 changed (13 inputs × default/ANSI) plus 18 new (6 inputs × default formatter, ANSI
formatter, default analyser). The same script explains 24; the other 2 are the `sp031-12-insert-select` subquery column
above. No analyser baseline changed.
