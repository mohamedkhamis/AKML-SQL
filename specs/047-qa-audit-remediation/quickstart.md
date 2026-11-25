# Quickstart: QA Audit Remediation (spec 047)

The acceptance gate for "done" (constitution, Development Workflow): §1 builds green, §2 every listed test passes,
and §3 every validation scenario is observed. Record the results in `specs/047-qa-audit-remediation/baseline.md`
(created during implementation), one row per scenario, with evidence paths.

## 1. Build

```bash
MSBUILD="/c/Program Files/Microsoft Visual Studio/18/Enterprise/MSBuild/Current/Bin/MSBuild.exe"
"$MSBUILD" AKML-SQL.slnx -t:Restore -v:quiet
"$MSBUILD" AKML-SQL.slnx -t:Build -p:Configuration=Release -m -v:minimal      # one pass, green (Principle II)
powershell -ExecutionPolicy Bypass -File scripts/generate-theme-css.ps1 -CheckOnly   # token drift gate (WEB-008)
# Web E2E prerequisites (CLAUDE.md): Debug builds of the web app and the engine
dotnet build src/AkmlSql.Web/AkmlSql.Web.csproj -c Debug
dotnet build src/AkmlSql.Engine/AkmlSql.Engine.csproj -c Debug
# Release gate including the new "Analyzer CLI (smoke)" step (CLI-001)
powershell -ExecutionPolicy Bypass -File build.ps1 -SkipTests
```

## 2. Tests (FR-038: one or more permanent tests per issue, each failing before its fix)

| Issue | Test (new unless marked *ext*) | Suite / command |
| --- | --- | --- |
| CLI-001 | `PublishedAnalyzerTests` (4 runs on the published exe) | `dotnet test tests/AkmlSql.E2E.Tests --filter "Category=PublishedCli"` |
| WEB-001 | `PageEngineManifestTests` (fake JS module: served page / manifest / none / invalid); `akml-page-engine.test.mjs` *ext* (`readManifest`); `EngineManifestScriptTests` (writes, refuses, removes) | Web.Tests, node, Installer.Tests |
| WEB-001 | `LocalPairFailureTests` (bUnit: failure notice text and Add engine… prefill) | Web.Tests |
| ENG-001 | `PairingManageHandlerTests` (list, IsCurrent, revoke closes sockets, unknown id, pairing not used); `akml-bridge.label.test.mjs` | Engine.Tests, node |
| ENG-001 | `PairedBrowsersSectionTests` (bUnit: rows, this browser, revoke confirm) | Web.Tests |
| WEB-002 | `ConnectionSaveRememberTests` (bUnit, fake service: every row of the Save table) | Web.Tests |
| WEB-004 | `ConnectionPrefillNameTests` (bUnit) | Web.Tests |
| WEB-003 | `BrowserStorageStateTests` (once per run, Diagnostics text); `OriginBannerTests` (conditions, dismiss) | Web.Tests |
| WEB-010 | `SavedConnectionJsonTests` (no `effectivePort`; old JSON loads) | Web.Tests |
| FMT-001…005 | `Sp047FormatterRulesTests` (rules §2 × 11 styles) + `FormatParityTests` *ext* (sp047 corpus goldens) | `dotnet test tests/AkmlSql.Formatting.Tests` |
| SSMS-004 | `LineEndingTests`; `FormatRequestHandlerLineEndingTests` | Formatting.Tests, Engine.Tests |
| UX-001 / SSMS-001 | `DiagnosticTaggerPresentationTests` (marker / underline per severity); `SquiggleStyleMigrationTests`; `CodeAnalysisPageAdvisoryRowTests`; `LightbulbAdvisoryHintsTests` | Shell.Shared.Tests, Core.Tests |
| UX-001 (web) | `akml-editor.diagnostics.test.mjs` *ext*; `AdvisoryDisplaySettingTests` (bUnit, export/import/reset) | node, Web.Tests |
| SSMS-002 | `AnalysisDeferredUntilEngineTests` (fake client connects late) | Shell.Shared.Tests |
| WEB-005 | `ParseErrorFindingTests` (opt-in on, off; partial tree); `ProblemsSyntaxEmptyStateTests`; `OfflineParseTests` (bUnit); `ParseMarkerBrowserTests` (Playwright) | Analysis.Tests, Web.Tests, Web.E2E.Tests |
| SN-001 | `CompletionUserSnippetTests` (online merge, offline statement start, kind filter, cache drop); `SnippetsInEditorTests` (Playwright) | Web.Tests, Web.E2E.Tests |
| WEB-013 | `PhaseAMetadataLimitedTests` (payload key 8 round trip, old payload); `SchemaTreeMetadataLimitedTests` (bUnit); `PhaseAViewDefinitionLiveTests` (opt-in, live SQL: as a login without VIEW DEFINITION the flag is true) | Engine.Tests, Web.Tests |
| WEB-011 | `SchemaSnapshotCacheTests` (converter); `RefactorExpandWithSnapshotTests` (Expand INSERT on dbo.Region from a Phase B fixture; disabled reasons) | Web.Tests |
| WEB-012 | `RefactorMenuKeyboardTests` (Playwright: Escape closes, focus returns, Enter runs an item) | Web.E2E.Tests |
| WEB-014 | `SchemaTreeFilterEmptyTests` (bUnit) | Web.Tests |
| UX-002 | `StyleImportMessageTests`, `SnippetImportMessageTests` (bUnit) | Web.Tests |
| SSMS-003 | `FindInvalidObjectsCountTests` (engine); `FindInvalidObjectsDialogTextTests` (shell) | Engine.Tests, Shell.Shared.Tests |
| AI-001 | `PromptPrivacyModeTests` (7 features × modes) | AI.Tests |
| AI-002 | `AiPanelFixWithoutErrorTests` (bUnit) | Web.Tests |
| DOC-001…003 | `DocsQualityTests` (summary ≥ 50, no unresolved relative link, symptom headings, connecting text) | Site.Tests |
| WEB-007 | `akml-editor.attributes.test.mjs` *ext* | node |
| WEB-008 / WEB-009 | `AxeAccessibilityTests` (6 routes × 3 themes, zero violations); `TabStripKeyboardTests` *ext* (close button sibling, keys kept) | Web.E2E.Tests |

Regression suites that must stay at their current levels (SC-010; Principle III ratchets):

```bash
dotnet test tests/AkmlSql.Core.Tests/AkmlSql.Core.Tests.csproj
dotnet test tests/AkmlSql.Formatting.Tests            # parity goldens, incl. FormatParityTests
dotnet test tests/AkmlSql.IntelliSense.Tests          # CorpusGateTests ratchet (~97.5 %)
dotnet test tests/AkmlSql.Analysis.Tests
dotnet test tests/AkmlSql.AI.Tests
dotnet test tests/AkmlSql.Engine.Tests
dotnet test tests/AkmlSql.PostgreSql.Tests            # PostgreSQL corpora untouched
dotnet test tests/AkmlSql.Shell.Shared.Tests
dotnet test tests/AkmlSql.Web.Tests
node --test "tests/AkmlSql.Web.Tests/js/*.test.mjs"
dotnet test tests/AkmlSql.Site.Tests
dotnet test tests/AkmlSql.Installer.Tests --filter "Category!=InstallerSmoke"
dotnet test tests/AkmlSql.Web.E2E.Tests -- xUnit.ParallelizeTestCollections=false   # serial, after the Debug builds
```

Known load-sensitive suites (`AiChatPanelCopyButtonTests`, `NotificationHostTests`) and the environmental
`PerformanceBaselineTests` drift are re-run alone before being called regressions (CLAUDE.md).

## 3. Validation scenarios (acceptance gate)

Machine: this owner machine. IIS-hosted web edition at `http://localhost:59416/` with bridge port 59417; SSMS 22;
`(local)` / Northwind. Install the build from §1 first.

| # | Story | Scenario | Pass when |
| --- | --- | --- | --- |
| V1 | 1 | Run the installed `AkmlSql.Analyzer.exe --file multi.sql`, then with `--check`, `--rules ST004` on sem1.sql, and `--settings pe004-off.casettings` | PE002, PE003, PE004; exit 1; one ST004; PE004 absent |
| V2 | 2 | New browser profile → `http://localhost:59416/` | the engine pill is Live with no click, or after one click on **Use this computer's engine**, < 30 s, 10 of 10 runs (SC-002) |
| V3 | 2 | Stop `AkmlSqlWebEngine`, open the page | the status bar names the address it tried ("The engine is not responding at localhost:59417 — it may be starting up or stopped.") with Retry, and the status text says "Engine not reachable." (not "Handshake failed"); starting the service reconnects with no reload. A page with nothing paired, where **Use this computer's engine** shows, says "No engine answered on localhost:59417…" and **Add engine…** opens the pre-filled form (bUnit). *Amended 2026-11-22 after the installed run: an install with `akml-engine.json` pairs by itself and never shows that button.* |
| V4 | 2 | LAN-mode engine (or a test engine with a PIN), two browsers paired | Settings lists both with distinct labels; Revoke one → that browser is disconnected and needs the PIN; the other still works |
| V5 | 3 | Save a SQL-login connection with Remember and the correct password; restart the browser and `AkmlSqlWebEngine`; reopen | connects with no password prompt and the right display name (SC-003). Needs a SQL login on `(local)`: create one only with the owner's go-ahead (the audit's attempt was blocked) |
| V6 | 3 | Same with a wrong password | "Saved without the password: the login test failed — Login failed…"; reopening asks for the password; nothing kept |
| V7 | 3 | Open `http://127.0.0.1:59416/` in the profile used in V2 | origin banner naming `http://localhost:59416/`; dismiss → not shown again on reload; Diagnostics shows Browser storage state |
| V8 | 4 | Format the 5 `sp047-*` cases + the audit's 10 samples with every style (web and SSMS) | the rules in contracts/formatter.md §2 hold; second Format changes nothing (SC-004) |
| V9 | 4 | SSMS: open a CRLF file, Format Document | status bar still CRLF |
| V10 | 5 | Web and SSMS: a script of statements without `;` | blue margin dot per line, no underline or colour change on the text; adding `;` removes the dot; a syntax error is still red (SC-005) |
| V11 | 5 | SSMS Options › Code analysis › Advisory findings = Underline | Information findings underlined again; back to Margin marker → gone |
| V12 | 5 | SSMS: start SSMS with a file of statements without `;` on Northwind; wait 10 s, no typing | the dots appear (SSMS-002) |
| V13 | 6 | Web, no connection: `SELEC * FROM dbo.Customers;` → Analyse, then Parse | Problems lists one Error with line and column; red mark; Parse says "Checked in the browser…", status "Parse failed: 1 syntax error." (SC-006) |
| V14 | 7 | Save snippet `qaz`; type `qaz` + Tab in a SQL Server tab | body inserted, caret at `$CURSOR$`; deleted snippet no longer completes |
| V15 | 7 | Explorer on Northwind through `AkmlSqlWebEngine` (runs as SYSTEM) | Stored Procedures "(may be incomplete)" with the VIEW DEFINITION note; after `GRANT VIEW DEFINITION ON DATABASE::Northwind TO [NT AUTHORITY\SYSTEM]` (owner's go-ahead) and Refresh: 7, `dbo.CustOrderHist` listed |
| V16 | 7 | Refactor › Expand INSERT columns on `insert into dbo.Region values (9,'x')` (connected) | preview shows `(RegionID, RegionDescription)`; Apply works; Escape closes the menu, focus on Refactor |
| V17 | 7 | Explorer filter `zzzz` | "No objects match 'zzzz'." + Clear filter |
| V18 | 7 | Import a .txt as a style and as a snippet | plain messages, no exception text |
| V19 | 7 | SSMS: Find Invalid Objects on Northwind | "No invalid objects found. All 63 references resolve." no empty grid |
| V20 | 8 | Web AI with privacy mode No schema (mock provider as in the audit) | captured system prompt has no "following schema" / "provided schema"; Fix disabled with its tooltip until a run errors |
| V21 | 9 | Site search for "remember password", "Handshake failed", "asks for password again" (local preview of the site) | the right page is the first result; the steps resolve the symptom on this machine (SC-009) |
| V22 | 10 | axe on the 6 routes × 3 themes; screen reader on the editor | 0 violations; "SQL query editor — SQLQuery1" announced (SC-008) |

SSMS scenarios (V9–V12, V19) need a live desktop session (FlaUI or by hand). If the RDP session is disconnected, record
them as Not Run with the reason, never as Pass.

## 4. Deferred and out of scope (also recorded in `doc/progress.md`)

- WEB-006 (SSMS → browser credential reuse) and an engine-held saved-connection list: a later spec with a security
  review.
- PostgreSQL "Save with Remember tests the login": its engine test path does not remember; a follow-up.
- The CLI JSON report's `settings` field (audit CLI-001, minor): a follow-up.
- The 24 SSMS menu commands, toolbar, Options pages and SQL History window the audit could not run: not part of these
  fixes; the next QA pass.
