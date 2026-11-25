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

The installed-product results are in the next section; it supersedes the "installed-product run: Not Run" notes above.

## Installed-product run, 1.26.1122.1400 (T140, T141), 2026-11-22

The owner built and installed 1.26.1122.1400. Setup took the existing install over in place:

- install folder: `C:\Program Files (x86)\AKML SQL\`;
- hosting: IIS, site on 59416, `AkmlSqlWebEngine` as LocalSystem on 59417;
- `{app}\Web\akml-engine.json`: `{"manifestVersion":1,"bridgePort":59417,"scheme":"ws"}`.

The engine and SSMS assemblies carry SHA `0745268f`, because the build started about two minutes before commit `26b467e1`. The spec 047 code is nonetheless in them; strings from it were found in the installed engine, Core, SSMS extension and web bundle. The installed analyzer carries `26b467e1`.

The checks ran from two places:

- `tests/AkmlSql.Web.E2E.Tests/InstalledWebCycleTests.cs`, with `AKML_INSTALLED_WEB=1`. It drives headless Chromium on a new profile against the IIS site and the installed service.
- `tests/AkmlSql.UiTests/Check047.cs`, with `AKML_CHECK047=1`. It drives the installed SSMS 22 extension through FlaUI, with a sandboxed AKML profile.

Text output is in `baseline/installed-2026-11-22/`. The first web run had five failures; three were mistakes in the new test, fixed and re-run (the log keeps every run).

| # | Installed result | Observed |
| --- | --- | --- |
| V1 | Pass | The installed `AkmlSql.Analyzer.exe`: reports PE002, PE003 and PE004 on multi.sql; `--check` exits 1; reports one information ST004 on sem1.sql; `--settings pe004-off.casettings` drops PE004. `scripts/test-analyzer-cli.ps1 -Exe <installed>`: 4/4. |
| V2 | Pass | 10 of 10 new profiles went Live with no click, in 2.1–2.6 s ("Live · no SQL"). |
| V3 | Differs from the scenario (F4) | With the service stopped, a new profile shows: pill "Failed" with the text "Handshake failed. Check engine pairing."; "The engine is not responding — it may be starting up or stopped."; Retry. "Use this computer's engine", and with it the "No engine answered on localhost:59417…" notice and Add engine…, never appear: the page pairs from `akml-engine.json` itself. After `sc start`, the page went Live again with no reload. |
| V4 | Not Run | Needs a LAN-mode engine. The paired-browser list is shown only when the engine keeps PIN pairings (T037). |
| V5 | Not Run | Needs a SQL login on `(local)`, which needs the owner's go-ahead. |
| V6 | Pass | "Saved without the password: the login test failed — Login failed for user 'akml_qa_no_such_login'." Reopened, the password box is empty and Remember is off. Delete removes the row. |
| V7 | Pass | `http://127.0.0.1:59416/` shows the banner naming `http://localhost:59416/`, with Open, Import settings… and Dismiss. After Dismiss and a reload it is gone. Diagnostics › Browser storage reads "Best effort: the browser may clear it when the disk is low. Saved connections and pairings are kept per address." |
| V8 (web) | Pass | 12 styles in the editor's Style box × 6 `sp047-*` scripts. Every second Format left the text unchanged, and 72 of 72 outputs equal the committed goldens and web baselines. The styles: the engine's 8 built-ins; the web's AKML Default and ANSI-compact; Collapsed and Khamis Style, built-in. |
| V10 (web) | **Fail** (F1) | The margin markers work: three of them; one ';' removes one; the syntax error is a red wavy line. But every advisory range is still underlined: computed `text-decoration-line: underline wavy`, although it carries `akml-lint-marker-only`. |
| V13 | Pass | No SQL connection, page settled. Problems: one Error, "SYNTAX Incorrect syntax near 'SELEC'. L1". One red range in the editor. Ctrl+F5: "Parse failed: 1 syntax error.", "Checked in the browser; the server was not asked.", "Msg 102, Level 15". Text typed the moment the editor appears survives start-up, and Analyse finds its error. |
| V14 | Pass | `qaz` is offered and Tab expands it to `SELECT * FROM dbo.Region;`, with the caret at `$CURSOR$`. After Delete, `qaz` is not offered and Tab leaves `qaz`. |
| V15 | Partial (F3) | Through the service (NT AUTHORITY\SYSTEM) on Northwind, the note reads: "The engine signs in as NT AUTHORITY\SYSTEM, which cannot see every object in Northwind. Grant it VIEW DEFINITION on the database, or connect with a login that can." The groups read "Stored Procedures (may be incomplete)" and "Tables (13)". The note is drawn over the next tree rows. The GRANT and the count of 7: Not Run (needs the owner's go-ahead). |
| V16 | Pass (F6) | Expand INSERT columns is enabled from the cached schema. The preview and Apply give `insert into dbo.Region (RegionID, RegionDescription) values (9,'x')` as a vertical list with type comments. Escape closes the menu with focus on Refactor. |
| V17 | Pass | "No objects match 'zzzz'." with Clear filter, which empties the filter. |
| V18 | Pass | Style: "That file is not a SQL Prompt style (.json)." Snippet: "No snippet was found in that file. Existing snippets are unchanged." |
| V20 | **Half Fail** (F2) | No schema: Optimize's system prompt has no "following schema" or "provided schema" and no column names. It says "No database schema is shared (privacy setting)…" and "Existing indexes are unknown…". Fix is disabled with "Run the query first: Fix needs the error the server returned." **After a failed run (Msg 208, "Query completed with errors."), Fix stays disabled.** |
| V21 | Partial | Local preview of the published site, with its own analytics database and downloads folder: "remember password": Connecting to SQL Server first; "Handshake failed": Troubleshooting first; "asks for password again": Connecting first, with Troubleshooting ("It asks for my password again") second. The steps were not followed end to end, because that needs V5. |
| V22 | Pass | axe on the IIS site: 0 violations on 6 routes × 3 themes (18 runs). Screen reader: Not Run. |
| Cycle (web) | Pass | Connected through the service to `localhost/Northwind`; web and engine both 1.26.1122.1400. `SELECT TOP (5)` returned 5 rows, ALFKI first. Format, then Format again: unchanged. `dbo.Cust` offers Customers. History lists the run. |
| SSMS, first run (16:11) | Invalid (F8) | V9 (0 CRLF, 11 LF), V19 ("This database has no object references to check.") and V8 failed because SSMS ran a different engine. The extension's own `Engine` folder held a 2026-10-10 engine (1.26.1010.2224, a hand-placed private update), which the shell starts before `{app}\Engine`. The engine was moved to `%LocalAppData%\AKML SQL\stale-ssms-engine-1.26.1010.2224\` and the run repeated. |
| V8 (SSMS) | Pass | 8 styles from Active Style × 6 `sp047-*` scripts, pasted, Format Document twice, saved. Every second Format left the text unchanged, and 48 of 48 outputs equal the goldens. |
| V9 | Pass | The CRLF script after Format Document: the saved file has 13 CRLF and no bare LF, and the status bar stays CRLF. |
| V10 (SSMS) | Pass | Without typing, blue dots on the three lines and no underline; adding ';' takes 3 findings to 2 and removes that line's dot. A pasted `SELEC * FROM dbo.Customers` is underlined red by SSMS's own IntelliSense a few seconds later; the 3 s capture was too early. |
| V11 | **Half Fail** (F10) | The option saves (`squiggleStyle`: underline, then marker). The open window did not change: no underline appeared until the next edit. |
| V12 | Pass | SSMS was started on the file and nothing was typed. The log shows "Analysis complete: 3 findings" 7 s after the connection, and the dots are shown. |
| V19 | Pass | "No invalid objects found. All 63 references resolve.", with no grid. |
| Cycle (SSMS) | Pass | `SELECT TOP (5)` → "Query executed successfully", 5 rows. SQL History lists the run (query-01, 2 versions, Northwind). Ctrl+Space then Tab after `dbo.Ord` commits `dbo.[Order Details]`. |

Findings from this run (none of them is caught by the automated suites, which is why each names the gap):

- **F1 (UX-001, web): advisory findings are still underlined in the web editor.**
  - Cause: `Shared/EditorComponent.razor:79` (spec 041) draws every `.cm-lintRange` as `text-decoration: underline wavy` under `.akml-editor .cm-editor .cm-lintRange`, specificity 0,3,0. The marker-only override in `wwwroot/js/akml-editor.js:1264` is a CodeMirror theme rule, also 0,3,0 with its scope class. It ties and loses on document order: CodeMirror's style is in `<head>`, the component's `<style>` comes later.
  - Why no test caught it: `akml-editor.diagnostics.test.mjs` checks only the class.
  - Fix: a rule that outranks it, for example `.akml-editor .cm-editor .cm-lintRange.akml-lint-marker-only { text-decoration-line: none; }`, and a browser test on the computed style.
- **F2 (AI-002, web): Fix can never be enabled on a SQL Server tab.**
  - Cause: `Pages/Editor.razor:2652` `GetLastExecuteError()` returns null unless the connection is PostgreSQL. That has been unchanged since spec 042. Spec 047 made Fix require that error (`Shared/AiPanel.razor:49`), so after "Query completed with errors." Fix stays disabled. Before 047 it was enabled and sent "(no error)".
  - Why no test caught it: `AiPanelFixWithoutErrorTests` feeds the panel a fake error provider.
  - Fix: take the first `ExecuteMessageKind.Error` message, with its `Number`, for SQL Server as well, and add a browser test with a failing query.
- **F3 (WEB-013, web): the metadata note overlaps the explorer.**
  - Cause: `.akml-schema-tree-node { height: 24px }` also applies to the note row. The note sets `white-space: normal` but keeps the fixed height, so at the default panel width its wrapped text (114 px) is drawn over `Schema "dbo"` and the rows below.
  - Fix: `.akml-schema-tree-note { height: auto; min-height: 24px; }`.
- **F4 (web, status bar with the engine stopped): the pill says "Handshake failed. Check engine pairing."**
  - Cause: `Shared/StatusBar.razor:684` uses that fixed text for any `Failed` state, although the failure is `Unreachable` and the next line says so correctly. Nothing was handshaken and the pairing is fine.
  - Effect on V3: its expected notice, and Add engine…, belong to the `NotPaired` path, which an install with `akml-engine.json` never shows. The scenario should describe what this install shows, or the pill should be worded by failure kind.
- **F5 (installer, low): setup leaves stale compressed files.** `{app}\Web` holds 68 `.br`/`.gz` files (0.4 MB, dated 2026-10-10) from an older install. IIS never serves them: `js/akml-editor.js` came from the current file with IIS's own gzip. Setup could delete them.
- **F6 (cosmetic, existing behaviour): the Expand INSERT layout is untidy.** Its output has a trailing space after `dbo.Region` and type comments that are not aligned (`RegionID,    --` / `RegionDescription     --`). A CRLF script also got LF lines.
- **F7 (found while fixing F6, existing behaviour): Expand INSERT writes an IDENTITY column into the list.** It appears as a bare name with no comma (`CategoryID    -- IDENTITY…` before `CategoryName,`). The list is then invalid, and one column longer than the VALUES of an INSERT without a column list. Fix: name it on a comment line only.
- **F8 (installation): SSMS ran an old engine.** `EngineProcessManager.FindEnginePath` tries `{ext}\Engine` before `{app}\Engine`, and setup never ships or removes that folder. Fix: setup deletes it (`[InstallDelete]`), and the shell prefers the engine of its own FileVersion and logs which it chose.
- **F10 (SSMS-001): Advisory findings applies only after the next edit.** The tagger re-read the style only when a new analysis arrived. Fix: Options OK re-applies every open document's tags, after dropping the 2 s settings cache.

### Fixes made on 2026-11-22 (owner: "do it, all please"); not committed

| Finding | Change | Test (new unless marked) |
| --- | --- | --- |
| F1 | `Shared/EditorComponent.razor`: `.akml-editor .cm-editor .cm-lintRange.akml-lint-marker-only { text-decoration-line: none; background-image: none; }` (0,4,0) | `InstalledRunFixesTests.Advisory_findings_are_margin_markers_only_and_a_syntax_error_stays_underlined` (computed style, Debug bundle) |
| F2 | `AiErrorInfo.FromMessages(messages, kind)` (Services/AiDialect.cs), used by `Pages/Editor.razor` for both dialects | `AiErrorInfoFromMessagesTests`; `InstalledRunFixesTests.On_SQL_Server_Fix_is_enabled_after_a_failed_run_and_sends_the_servers_error` (mock provider, live SQL) |
| F3 | `Shared/SchemaTreeComponent.razor`: `.akml-schema-tree-note { height: auto; min-height: 24px; }` | `InstalledRunFixesTests.The_explorer_note_grows_with_its_text_instead_of_covering_the_next_row` (layout in the browser) |
| F4 | `IEngineAutoConnect.TryOnceAsync` names the address tried; `StatusBar.FailedText()` follows the failure kind ("Engine not reachable.") | `EngineStoppedWordingTests` (3) |
| F5 | `web-installer.iss` `[InstallDelete]`: `*.br`/`*.gz` in the 8 folders that held them | `InstalledRunCleanupSourceTests` |
| F6, F7 | `ExpandInsertColumnsOperation`: the list takes the place of the blank after the table name; comments in one column; the script's line ending; identity named on a comment line | `ExpandInsertLayoutTests` (5); `InsertMetadataTests.BuildColumnListWithTypes_IdentityColumn_AnnotatedAsIdentity` *changed* (it pinned the invalid list) |
| F8 | `AkmlSqlSetup.iss` `[InstallDelete]` `{code:GetSSMS22ExtDir}\Engine`; `EngineProcessManager.ChooseEngine` (the engine of the extension's version wins) | `InstalledRunCleanupSourceTests`; `EngineChoiceTests` (4) |
| F10 | `DiagnosticTagger` re-applies through `ErrorListReporter.ReapplyAll` (Options OK); `AnalysisController.InvalidateSettings` | `AdvisoryOptionReappliesTests` (2) |
| V3 | `quickstart.md` V3 restated for an install that pairs by itself (amended 2026-11-22) | — |

### Re-run on 1.26.1122.1658 (the fixes installed), 2026-11-22 17:24–17:38

The owner built and installed 1.26.1122.1658 from the fixed tree. Before testing, the installed binaries were checked: the F1, F3, F4, F7 and F8 strings are in the web bundle, engine and SSMS extension. `{ext}\Engine` is gone, and setup deleted every stale `.br`/`.gz` (F5: 0 left). Notes: `baseline/installed-2026-11-22/web-observations-1658.txt` and `ssms-observations-1658.txt`.

| # | Result | Observed on 1.26.1122.1658 |
| --- | --- | --- |
| V1 | Pass | Unchanged from the run above (the analyzer was not touched). |
| V2 | Pass | 10 of 10 new profiles Live with no click. |
| V3 | Pass | Engine stopped: "The engine is not responding at localhost:59417 — it may be starting up or stopped." with Retry. Live again with no reload after `sc start` (F4). |
| V5 | **Pass** | Temporary login `akml_qa_v5`, dropped after. Save with Remember said "Saved. The engine machine keeps the password for akml_qa_v5." After the browser (same profile) and AkmlSqlWebEngine restarted, it connected as akml_qa_v5 with an empty password box. **Forget it** said "The remembered password was forgotten." and the engine's store is `[]`. |
| V6, V7 | Pass | As above. |
| V8 | Pass | Web: 12 styles × 6 scripts, 72 of 72 match, all stable. SSMS: 8 styles × 6, 48 of 48 match, all stable. |
| V9 | Pass | The saved file has 13 CRLF and no bare LF; the status bar stays CRLF. |
| V10 | **Pass** | Web (F1): the advisory ranges compute to `text-decoration-line: none` and the syntax error to `underline wavy rgb(220, 38, 38)`. SSMS: dots, no underline, and ';' removes its dot. The red mark on `SELEC` is drawn by SSMS's IntelliSense a few seconds after the paste; it shows in the V11 screenshots, and Check047 now waits 10 s for it. |
| V11 | **Pass** | Underline in the text: lines 3–4 are underlined at once. Margin marker only: the underlines are gone at once (F10; screenshots v11-1, v11-2). |
| V12, V13, V14 | Pass | As above. |
| V15 | **Pass** | The note's row grows to 118 px, with no overlap (F3). With the owner-approved `GRANT VIEW DEFINITION ON DATABASE::Northwind TO [NT AUTHORITY\SYSTEM]` and Refresh: the note is gone, "Stored Procedures (7)", and CustOrderHist is listed. Revoked after. |
| V16 | Pass | The list is aligned with no trailing blank (F6): `insert into dbo.Region ⏎( ⏎ RegionID, -- int … ⏎ RegionDescription -- nchar(100) …⏎) values (9,'x')`. |
| V17, V18 | Pass | As above. |
| V19 | Pass | "No invalid objects found. All 63 references resolve." |
| V20 | **Pass** | No schema is sent, and Fix is disabled with its tooltip before a run. **After a failed run (Msg 208), Fix is enabled** (F2). |
| V21 | Partial | Unchanged: "asks for password again" puts Connecting to SQL Server before Troubleshooting. |
| V22 | Pass | 0 axe violations, 18 runs. |
| Cycles | Pass | Web: run, grid, Format, completion, History. SSMS: execute, SQL History, completion. The shell logs `Engine: C:\Program Files (x86)\AKML SQL\Engine\AkmlSql.Engine.exe (1.26.1122.1658)` (F8). |
| V4 | Not Run | Needs a LAN-mode engine. |
| Screen reader (V22) | Not Run | No screen reader on this machine. |

The server is back to its state before the run:

- the GRANT is revoked (0 VIEW DEFINITION grants to SYSTEM);
- the V5 login is dropped;
- the engine's credential store is empty.

The October engine that SSMS had been running is kept in `%LocalAppData%\AKML SQL\stale-ssms-engine-1.26.1010.2224\`.

Suites after the fixes (Debug, 2026-11-22):

| Suite | Passed / failed / skipped |
| --- | --- |
| Web | 2313 / 0 / 0 (8 new) |
| Engine | 2735 / 1 / 132 (5 new) |
| Core | 1641 / 0 / 3 |
| IntelliSense | 25 / 0 / 0 |
| Shell.Shared | 1117 / 0 / 0 (6 new) |
| Installer | 149 / 0 / 0 (10 new) |
| node | 321 / 0 / 0 |
| `InstalledRunFixesTests` (browser, Debug bundle) | 3 / 0 / 0 |

The Engine failure is `PerformanceBaselineTests.Capture_or_compare_M0_baseline`: "FormatRequest.p50 regressed by 6.4% (baseline 436.502 ms, current 464.248 ms). Allowed: 5%." That is the machine-dependent timing test CLAUDE.md lists as failing here on an untouched HEAD. The run was on a busy machine, and no change touches the formatter it times. A recapture is a separate decision. Every new test failed on the pre-fix code first (see the log of the swap run in the session; counts: web 4, Expand INSERT 4, Core 1, Shell 1, installer 9).

### Re-run on 1.26.1122.1936, 2026-11-22 20:32–20:59

The owner deployed 1.26.1122.1936, built from 6b0d7f32. There is no product change since 1658, only the site release files. Checked before testing:

- every DLL is stamped 6b0d7f32;
- the main web assembly (`AkmlSql.Web.vulvqg4wlv.wasm`) carries the F1, F3 and F4 strings, and the engine and the SSMS extension carry F7 and F8;
- `{ext}\Engine` is absent;
- 0 stale `.br`/`.gz` files are left.

Notes: `baseline/installed-2026-11-22/web-observations-1936.txt`, `ssms-observations-1936.txt` and `ssms-observations-1936-v8.txt`.

| Area | Result |
| --- | --- |
| V1 | Pass: `test-analyzer-cli.ps1` on the installed analyzer, 4/4. |
| Web | Pass: V2, V3, V5 (Forget it empties the engine's store), V6–V8 (72/72), V10, V13–V18, V15 both halves (GRANT revoked after), V20, V22 (0 violations), the start-up check and the cycle. **V21 partial**, as before. |
| SSMS | Pass: V9, V10, V11 (applies at once), V12, V19 and the cycle. **V10-syntax pass**: with the 10 s wait the Error List shows "Incorrect syntax near 'SELEC'." on line 5. V8 (48/48) passed on its own run (`AKML_CHECK047_ONLY=V8`) after the full run lost it: focus left SSMS for the VS Code window, and an SSMS menu does not open behind another window. The shell logged `Engine: C:\Program Files (x86)\AKML SQL\Engine\AkmlSql.Engine.exe (1.26.1122.1936)`. |
| Clean-up | Done: the V5 login is dropped, SYSTEM has 0 VIEW DEFINITION grants, and the engine's credential store is `[]`. |

### Owner request 2026-11-23: Windows authentication in the browser signs in as you, like SSMS (1.26.1123.0500)

The owner connected the web edition to `localhost/aqmar` with Windows authentication. The explorer showed only `Database "aqmar"` and the note "The engine signs in as NT AUTHORITY\SYSTEM, which cannot see every object in aqmar…". As admin, aqmar has 4 tables. `NT AUTHORITY\SYSTEM` there is only `db_backupoperator` with CONNECT: it could see 0 of the 4 tables and SELECT from 0 of them. The owner asked for it to be "automatic like ssms".

The change (CLAUDE.md › Browser Windows sign-in):

- The engine service finds the Windows user of a browser on its computer, signs in to SQL Server as that user, and names them in the handshake.
- A browser on another computer, a caller it cannot place, and `bridge.windowsSignInAsBrowserUser: false` sign in as the engine's account.

Tests:

- `BrowserUserSignInTests` (Engine, 15): the TCP-table match, a live trace from a loopback connection to its process, a live impersonated sign-in to SQL Server, the refresh registration and the handshake.
- `BridgeWindowsSignInTests` (Core, 2): the config switch.
- `WindowsAuthHintTests` (Web, 4): the Connect dialog names the user.

A first version of `WindowsAuthHintTests` rendered the dialog with a bridge registered. That slowed the timing-based `ReconnectLoopTests.RetrySucceedsRestoresOpen` running beside it, which then failed in every full run and passed alone. The wording is now tested as a pure function.

Suites: Engine 2748/0/132 (without the machine-dependent PerformanceBaselineTests), Web 2317, Core 1643/0/3, Site 925, Shell 1117, node 321.

Installed 1.26.1123.0500 (built with `build.ps1 -SkipTests`; analyzer smoke 4/4; installed silently, keeping the IIS hosting):

| Check | Result |
| --- | --- |
| aqmar in the web explorer, Windows authentication | **Pass**: `Database "aqmar"` › `Schema "dbo"` › Tables (4), Views (0), Stored Procedures (0), Functions (0); no note. |
| The engine log | `browser process 26196 signs in to SQL Server as 1442-MOHAMEDKHA\Administrator` |
| SQL Server's view of the engine service's sessions | All logged in as `1442-MOHAMEDKHA\Administrator` (aqmar ×2, master ×1), none as SYSTEM. |
| Northwind, signed in as the user | No note; "Stored Procedures (7)" with no GRANT. |
| The installed web cycle | All pass: V2, V3, V5, V6–V8 (72/72), V10, V13–V18, V15 after a GRANT (revoked), V20, **V21 (3/3: the updated troubleshooting entry now comes first for "asks for password again")**, V22, start-up and the cycle. |
| Clean-up | The temporary login is dropped, 0 grants, and the engine's credential store is `[]`. |
| SSMS (Check047, one full run) | All pass: V8 (48/48), V9, V10 with V10-syntax, V11, V12, V19 and the cycle. The shell ran `C:\Program Files (x86)\AKML SQL\Engine\AkmlSql.Engine.exe (1.26.1123.0500)`, and its named-pipe path is unchanged. |

Notes: `baseline/installed-2026-11-22/web-observations-0500.txt` and `ssms-observations-0500.txt`.

Test harness changes from this run:

- `InstalledWebCycleTests` gained V5, V15 (after the GRANT), V21 and a start-up check, and reads computed `text-decoration-line`.
- `Check047` brings SSMS to the front before it opens an AKML menu, and `AKML_CHECK047_ONLY` (for example `V8`) runs single scenarios.
- `Check047` closes the floating SQL History window with Shift+Esc. The "Close button inside the pane" rule had closed the query window. It also reads completion by committing the first suggestion, since the list has no UIA items.

### Re-run on 1.26.1123.0537 and a UX pass (owner: "test full cycle … and enhance UX"), 2026-11-23

The owner deployed 1.26.1123.0537 (the browser-user sign-in, 72d51be7, and the site release 3f676d87). Engine, SSMS extension and installer are all 1.26.1123.0537; hosting is IIS.

| Area | Result |
| --- | --- |
| Web (`InstalledWebCycleTests`, 15) | **14 pass, 1 skip**. The skip is V15 after the GRANT, which was not run: the web signs in as the browser's user and needs no permission change on Northwind. Pass: V2 (10/10), V3, V5, V6–V8 (72/72), V10, V13, V14, V16–V18, V20, V21, V22 (0 violations, 18 pages), the start-up check, the cycle, and aqmar's explorer (Tables (4), no note). |
| SSMS (`Check047`, one full run) | All pass: V8 (48/48), V9, V10 with V10-syntax, V11, V12, V19 and the cycle. The shell log has 0 warnings or errors. |
| Clean-up | The temporary login is dropped, SYSTEM holds 0 VIEW DEFINITION grants on Northwind, and the engine's credential store is `[]`. |

Notes: `baseline/installed-2026-11-22/web-observations-0537.txt` and `ssms-observations-0537.txt`.

UX findings from the screenshots of these runs, and what changed (not deployed; verified on Debug builds with a sandbox engine and Northwind):

| Finding | Change |
| --- | --- |
| The web toolbar took two rows at 1440 px; the second held three controls. | One row from 1280 px. Button words drop in steps (1780 / 1580 / 1405 px, and narrower style and database boxes at 1335 px). Execute keeps its word; every button keeps its name and tooltip. Measured connected, with engine styles listed, idle and while a query runs, at 1920 / 1536 / 1440 / 1366 / 1280 px: 40 px tall each time, with 55–150 px to spare. It wraps at 1024 px, as before. |
| Format's message ("Already canonical.") sat at the end of the toolbar, and a long one wrapped it. | Editor action messages go to the status bar (`status-notice`, 12 s, cleared by a run). The wording is plainer: "Already formatted: nothing to change.", "Style changed to …", "Format failed" (not "crashed"), "This refactoring needs the engine, which is not reachable." (not "Heavyweight refactoring is unavailable"), "Applied 1 change." (not "change(s)"). |
| The status bar said "Live" and "Live IntelliSense available."; a stopped engine read "Failed  Engine not reachable.  The engine is not responding at …". | No words beside Live. A failure the issue strip explains is not repeated, and the pill names it: **Unreachable**, **Needs pairing** or **Failed**. Offline says what still works ("Keywords only; Format and Analyse still work."). The answer to "Use this computer's engine" stands alone until it is dismissed (the two strips had cut each other to "No engine paired y…" and "This inst…"). An issue's text is whole when there is room; the state words and versions give way first. |
| Nothing on the web said who a Windows-authentication connection signed in as; SSMS's status bar does. | The status bar names the Windows user (`1442-MOHAMEDKHA\Administrator`). |
| With the login added, every status-bar segment shrank alike at 1440 px ("SQL Servei", "Ln 1, Co…"). | Priorities: the message gives way first, then the versions, then the login. The outcome, rows and time, caret, server kind and connection keep their words. |
| An engine that stopped with the page open showed "No engine paired yet." It was a failure left from before a pairing made in Settings, and it hid "Reconnecting · next try in 3s". | `EngineAutoConnect` clears its failure whenever the bridge opens. Verified: no engine → Offline; engine stopped → Reconnecting with its countdown; reload with the engine down → Unreachable with the address and Retry. |
| A red struck-through "deleted" headed every editable result. | Until something changes, the bar says "Double-click a cell to edit. Nothing changes on the server until you click Apply."; the legend appears with the first change. |
| The History preview pane stood empty, and the Connect dialog's password hint was cut off ("Remembered on the engine mac"). | "Select a query to see its full text here."; "Remembered on the engine" and "Same as the tab it came from". |
| SSMS: SQL History said "1 entries found"; Find Invalid Objects said "1 invalid object(s)" in a resizable window with Minimise and Maximise that only Enter closed; the completion footer said "owner: on". | "1 entry found"; "1 invalid object — a reference that can no longer be resolved." in a fixed dialog that Escape closes; "schema names: on". |

Not changed in this pass: NM005 flags every bracketed name with a space, including references to tables the user did not name. One example is Northwind's `[Order Details]`, which raised a warning in the SSMS run. Narrowing it to definitions would change the rule's tests and the parity baselines, so it was left for its own change. It was narrowed after the 1.26.1123.1213 run (below).

Suites:

- Unit: Web 2327 (+10), Shell 1125 (+8).
- Browser, 18 classes, serial, Debug builds:
  - ShellPolish, WorkspaceAccessibility, Axe, EngineAutoConnect, ResultsGrid, ResultsMessages, FormatStyles, ConnectDialog, WorkspaceLayout, QuickstartAcceptance, StandaloneQueryCycle, SettingsPage, DocumentFlow, QueryTabs, ReviewRegression, ReviewFixBrowser, EditorTypography, TabStripKeyboard.
  - Result: 138 tests, 125 pass, 7 skip (opt-in), 6 fail.
  - The 6 failures: 5 `EngineAutoConnectTests`, environmental and recorded since spec 043 (they pair at the computer name with a PIN and need a LAN-mode install; this one is localhost-only). 1 `StandaloneQueryCycleTests`, whose bundle-freshness guard tripped on a source edit made during the run; it passes on the rebuilt bundle.
  - After the last status-bar changes: ShellPolish, Axe, WorkspaceAccessibility and ConnectDialog 21/21.
- `DocsWebScreenshotTour` retook `doc/images/web/status-no-engine.png`, `status-engine-not-answering.png` and `connect-remember.png`; the troubleshooting text and alt text follow.

### Re-run on 1.26.1123.1213 (the UX pass installed), 2026-11-23 12:42–13:07

The owner deployed 1.26.1123.1213, built from e17325f5 (the UX pass) and the site release 70ab018f. The engine, the SSMS extension, the analyzer and the web bundle are all 1.26.1123.1213; hosting is IIS. Both harnesses gained checks for the UX pass before the run (below).

| Area | Result |
| --- | --- |
| V1 (`scripts/test-analyzer-cli.ps1` on the installed analyzer) | 4/4. |
| Web (`InstalledWebCycleTests`, 16) | **14 pass, 1 skip, 1 fail.** The skip is V15 after the GRANT, which was not run. The failure is the new UX check, on one point (below). Pass: V2 (10/10, Live in 2.1–3.0 s with no click), V3, V5, V6, V7, V8 (72/72), V10, V13, V14, V16–V18, V20, V21 (3/3, on the local site publish from 1.26.1123.0500), V22 (0 violations), the start-up check, the cycle, and aqmar's explorer (no note). |
| SSMS (`Check047`, one full run, 15 min 53 s) | **All pass:** V8 (48/48), V9, V10, V10-syntax, V11, V12, V19, the cycle, and the three new checks: V19-dialog, history-count ("1 entry found") and completion-footer ("schema names: on"). The shell ran the installed engine (`Engine: C:\Program Files (x86)\AKML SQL\Engine\AkmlSql.Engine.exe (1.26.1123.1213)`). |
| Clean-up | The temporary login is dropped, SYSTEM holds 0 VIEW DEFINITION grants on Northwind, and the engine's credential store is `[]`. |

The UX pass on the installed product (the new `UX_…` check, V3 and V5):

- **Toolbar:** 40 px tall at 1920 / 1536 / 1440 / 1366 / 1280 px, idle and while a query runs, with 63–173 px to spare. All button words show at 1920 px; Format, Refactor and AI at 1536 and 1440 px; none at 1366 and 1280 px. Execute says "Execute" ("Running…" while it runs), and no control is without a name.
- **Status bar:** "Live" alone, then `1442-MOHAMEDKHA\Administrator` with the title "Windows authentication: SQL Server sees this Windows user". Format says "Formatted.", then "Already formatted: nothing to change.", in the bar and not in the toolbar; 13 s later the message is gone. At 1280 px with a message showing, the message, login and versions give way; the outcome, rows, caret, kind and connection stay whole.
- **Engine stopped (V3):** the pill says **Unreachable**, with no state words beside it, and the issue says "The engine is not responding at localhost:59417 — it may be starting up or stopped." with Retry. Stopped while the page is open, the pill says **Reconnecting** with "Reconnecting · next try in 2s", there is no issue strip, and the page reconnects by itself.
- **V5:** the password box hint reads "Remembered on the engine" and fits (172 of 185 px).
- **Results and History:** an editable result says "Double-click a cell to edit. Nothing changes on the server until you click Apply.", with no legend chips. The History preview says "Select a query to see its full text here."

The failure: a run did not replace the message, so "Already formatted: nothing to change." stayed beside "Query executed successfully." Every query tab's run starts through `IWorkspaceStatus.Show(RunSnapshot.Running(…))` (`Editor.BeginRun`). Only `Begin()` cleared the message, the app never calls it, and the unit test called `Begin()`.

Fixed (not deployed):

- `WorkspaceStatus.Show` clears a message that is older than the run it shows. Activating a tab whose run began before the message keeps the message, and so does showing a finished run.
- Tests: `WorkspaceStatusTests` +4. `StatusBarSegmentsTests.A_run_replaces_the_last_action_message` now starts the run the way the editor does.
- Verified on the Debug build with a sandbox engine: Format, then run, twice; the bar read "Formatted.", then nothing.

The shell log has 2 warnings, `[PipeRpc] WriteAsync req#0 _writeLock acquired in 300.9569ms (CONTENDED)` and one of 132.8 ms, both for document-change notifications during V8. Nothing failed, and the 0537 run had none. The log shows no other write in flight either time: the previous write had released the lock about 100 ms before. The time was measured from before a log line, so a stall of the SSMS process (a garbage collection, a slow log write) read as contention.

Fixed after the run (owner: "fix issues if found"; not deployed):

- **The pipe warning tells the truth.** `PipeRpcClient.EnterAsync` takes the write lock without waiting when it is free, and times only a wait for another writer. "CONTENDED" now means another write held the lock. Tests: `PipeRpcWriteLockTests` (4).
- **NM005 reports the names a script gives, once.** It reported every bracketed name with a space or special character: every reference to Northwind's `[Order Details]`, principals such as `[NT AUTHORITY\SYSTEM]` in a GRANT, and, reading the whole document's tokens for every batch, each name once per batch. It now reads the batch's syntax tree and reports a name where the script gives it:
  - a created object;
  - a column, constraint or index;
  - a table made by SELECT … INTO;
  - a CTE and its columns;
  - a column or table alias.

  A reference and a principal are never reported, so the rule now reports a subset of what it did before.
  - Tests: `Nm005Tests` (17 reported, 10 not reported, one for several batches).
  - The four web parity baselines that held a finding for a reference to `[order details]` lose those 5 findings and keep the rest unchanged: `sp031-04-between-in`, `sp031-10-cte-columns`, `sp031-13-update-delete` and `sp031-20-merge`, all `.expected.json`.
  - `doc/analysis-rules.md` and `RuleMetadataCatalog` describe the rule as it is. Both said "unquoted identifier", which it never checked.
  - PGNM005, the PostgreSQL rule, is unchanged. It is Information, a margin marker by default.

Harness changes:

- `InstalledWebCycleTests` gains `UX_the_toolbar_keeps_one_row_and_the_status_bar_says_each_thing_once`. V3 also checks the pill's word and stops the engine with the page open, and V5 checks the password hint.
- `Check047` gains V19-dialog, history-count and completion-footer.

Suites after the fixes:

- Web 2331 (+4), Shell 1129 (+4).
- Engine analysis tests 433, with `Nm005Tests` at 29 (was 3), and Core analysis tests 127.

Notes: `baseline/installed-2026-11-22/web-observations-1213.txt` and `ssms-observations-1213.txt`.

### Code review of PR #261 (owner: `/code-review #261 --fix`), 2026-11-25

The review read the PR's source diff against master (about 4,500 inserted lines). It covered the engine (loopback caller, `SqlOpen`, pairing list and revoke, schema), Core, the web edition, the formatter, the SSMS shell, the installer scripts and the analyzer gate. Fixed in the working tree, not committed and not deployed:

| Finding | Fix | Tests (each fails before the fix) |
| --- | --- | --- |
| Stage 9 rewrote every line break after validation, those inside string literals and quoted identifiers included. In a CRLF script, `N'line one<LF>line two'` came back with a CR, so the INSERT stored other text. | `FormatterPipeline.WithLineEndings` converts outside the literals and quoted identifiers that hold a line break, using the lexer's tokens. When the tokens do not cover the text end to end, it converts nothing. | `LineEndingTests`: a literal and a quoted identifier keep their own line breaks in a CRLF script; the step narrows and widens around a literal; a comment's line breaks follow the script. |
| The browser's Expand refactorings (WEB-011) ran on a rehydrated schema with no identity, computed or default facts. Expand INSERT columns listed an IDENTITY and a computed column, so the INSERT was one column longer than its VALUES (the F7 bug, in the browser), and Expand UPDATE columns set a computed column. | `SchemaPhaseColumn` keys 8–10 (`IsIdentity`, `IsComputed`, `DefaultValue`, additive). `SchemaPhaseSerializer` sends them and `SchemaPhaseRehydrator` reads them. | `RefactorExpandWithSnapshotTests` +2, `SchemaPhaseBHandlerTests` +1, `SchemaPhaseRehydratorTests` +1. |
| Expand UPDATE columns added an IDENTITY column as `Id = NULL`, which the server refuses. Its comment said identity was skipped. This predates the PR, but WEB-011 made it reachable from the browser. | `ExpandUpdateColumnsOperation` skips identity columns. | `Expand_UPDATE_columns_never_sets_an_identity_or_computed_column`. |
| Completion tokenized the whole text before the caret (`AtStatementStart`) on every request the engine answered without a snippet, also for someone with no snippet of their own. | `CompletionService` reads its kept list of user snippets first and returns when it is empty. | Behaviour unchanged: `CompletionUserSnippetTests` pass. |

Found, not changed (to decide separately):

- **Pre-existing, outside the PR's diff.** `TextEmitter.RemoveTrailingWhitespace` drops the CR, and trailing spaces, inside a multi-line string literal. Validation then refuses the whole format. A CRLF script, which is what SSMS buffers are, is returned unformatted when it has a string literal over several lines.
- **Pre-existing.** Expand INSERT / UPDATE columns write column names unquoted, so a column named with a space or a reserved word gives invalid SQL.
- **As designed.** A caller the engine can place, but that runs as another user than its session's (or in session 0), still uses the engine's account (LocalSystem) for Windows authentication. Loopback pairing needs no PIN.

Suites after the fixes:

- Formatting 1934, IntelliSense 26, Core 1643 (3 skipped), Web 2333.
- Engine 2775 with 132 skipped, run without `PerformanceBaselineTests`. That class passes alone (`Capture_or_compare_M0_baseline` in 19 min 35 s). Its baseline was the one failure in the full parallel run, the known timing-sensitive test.

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
