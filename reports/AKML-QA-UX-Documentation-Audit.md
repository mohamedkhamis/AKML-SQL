# AKML QA / UX / Documentation Audit — 2026-11-20

**Scope:** browser SQL editor (web edition), SSMS 22 extension, public site and documentation, the formatter (12 styles), AI assistance, every Settings page, Snippets, History, Explorer and the Format styles editor, plus the user-reported "saved credentials ask for password and display name again" problem.
**Builds tested:** web `1.26.1104.1510` at `http://localhost:59416/` (IIS; engine bridge on `ws://localhost:59417`), engine service `1.26.1104.1510`, SSMS 22 extension `1.26.1104.1510` (deployed 2026-11-04 15:10), public site `https://akml.khamis.work/` (crawled 2026-11-20 ~03:50 UTC+2). Repository HEAD `0745268f`; **no product code was changed.**
**Evidence:** [AKML-QA-UX-Documentation-Audit-evidence/](AKML-QA-UX-Documentation-Audit-evidence/) (screenshots, crawl JSON, axe JSON, CLI output). Every finding below is readable without them.

> **SSMS coverage note.** The first SSMS attempt (≈03:55) was Blocked: the RDP session reported *Active* but had no input desktop (`GetForegroundWindow() = 0`, screen capture threw "The handle is invalid"; see [ssms-probe-blocked.log](AKML-QA-UX-Documentation-Audit-evidence/ssms-probe-blocked.log)). After the desktop came back, SSMS 22.12.12405.243 was launched at ≈04:07 against `(local)` / `Northwind` with **Windows authentication** and driven with the UI-automation harness; the results are in UX-001, SSMS-002 and the coverage matrix, with screenshots and [ssms-probe-log.txt](AKML-QA-UX-Documentation-Audit-evidence/ssms-probe-log.txt). That run was **one pass**, single-line inputs only, Error List not open, and the SSMS Options/credential dialogs were not exercised.

---

## 1. Prioritized issue index

Severity: High = core task blocked or data/credential experience broken; Medium = wrong or confusing behaviour with a workaround; Low = polish. Fix priority: P1 = next slice, P2 = soon after, P3 = backlog.

| ID | Surface | Type | Severity | Fix priority | Title | Status |
| --- | --- | --- | --- | --- | --- | --- |
| [CLI-001](#cli-001-the-installed-akmlsqlanalyzerexe-reports-no-issues-for-any-sql) | CLI / installer | Defect | High | P1 | The installed `AkmlSql.Analyzer.exe` reports "No issues found" for any SQL | Confirmed |
| [WEB-001](#web-001-use-this-computers-engine-fails-on-an-iis-install-whose-bridge-port-is-not-47291) | Web | Defect | High | P1 | "Use this computer's engine" fails on an IIS install whose bridge port is not 47291 | Confirmed |
| [WEB-002](#web-002-saving-a-sql-auth-connection-with-remember-ticked-stores-only-the-flag-not-the-password) | Web | Defect / UX | High | P1 | Saving a SQL-auth connection with "Remember" ticked stores only the flag, not the password | Confirmed (user-reported) |
| [WEB-003](#web-003-saved-connections-and-engine-pairing-live-in-per-origin-non-persistent-browser-storage) | Web | Defect | High | P1 | Saved connections and engine pairing live in per-origin, non-persistent browser storage | Origin part confirmed; eviction not reproduced (user-reported) |
| [UX-001](#ux-001-an-optional-missing-semicolon-st004-underlines-the-whole-statement-instead-of-one-small-blue-marker) | Web + SSMS | UX | Medium | P1 | An optional missing semicolon (ST004) underlines the whole statement instead of one small blue marker | Confirmed in web and in SSMS 22 (observed) |
| [WEB-005](#web-005-syntax-errors-get-no-feedback-from-analyse-and-no-in-editor-marker-from-parse) | Web | Defect | Medium | P1 | Syntax errors get no feedback from Analyse ("No problems found.") and no in-editor marker from Parse | Confirmed |
| [DOC-001](#doc-001-connecting-to-sql-server-covers-ssms-only-and-never-explains-web-connections-or-credentials) | Docs | Gap | Medium | P1 | "Connecting to SQL Server" covers SSMS only and never explains web connections or credentials | Confirmed |
| [WEB-004](#web-004-the-reconnect-dialog-opens-with-a-blank-display-name-when-the-tab-has-no-saved-row) | Web | UX | Medium | P2 | The reconnect dialog opens with a blank Display name when the tab has no saved row | Confirmed (synthetic tab record) |
| [WEB-006](#web-006-the-web-edition-cannot-reuse-credentials-saved-by-ssms-by-design--answer-to-the-users-question) | Web + SSMS | Enhancement | Medium | P2 | The web edition cannot reuse credentials saved by SSMS (by design) | Confirmed by code and files; needs a spec |
| [WEB-007](#web-007-the-sql-editor-has-no-accessible-name) | Web | Accessibility | Medium | P2 | The SQL editor has no accessible name | Confirmed (axe) |
| [SSMS-001](#ssms-001-codeanalysissquigglestyle-and-showadvisoryhints-are-documented-but-never-read) | SSMS + Docs | Defect | Medium | P2 | `codeAnalysis.squiggleStyle` and `showAdvisoryHints` are documented but never read | Confirmed by code search |
| [SSMS-002](#ssms-002-a-statement-loaded-from-a-file-is-not-flagged-by-analysis-until-it-is-edited) | SSMS | Defect | Medium | P2 | A statement loaded from a file is not flagged by analysis until it is edited | Observed once; cause is a hypothesis |
| [DOC-002](#doc-002-troubleshooting-has-no-entry-for-the-pairing-and-asks-for-the-password-again-symptoms) | Docs | Gap | Medium | P2 | Troubleshooting has no entry for the pairing and "asks for the password again" symptoms | Confirmed |
| [WEB-008](#web-008-two-badges-fail-colour-contrast) | Web | Accessibility | Low | P3 | Two badges fail colour contrast (3.29:1 and 4.26:1) | Confirmed (axe) |
| [WEB-009](#web-009-minor-accessibility-batch) | Web | Accessibility | Low | P3 | Minor accessibility batch (nested interactive, title-only labels, landmarks, headings) | Confirmed (axe) |
| [WEB-010](#web-010-saved-sql-server-records-persist-a-postgresql-port-effectiveport-5432) | Web | Data hygiene | Low | P3 | Saved SQL Server records persist a PostgreSQL port (`EffectivePort: 5432`) | Confirmed |
| [ENG-001](#eng-001-the-pairing-token-store-holds-166-indistinguishable-web-edition-entries) | Engine | Observation | Low | P3 | The pairing token store holds 166 indistinguishable "Web edition" entries | Confirmed |
| [DOC-003](#doc-003-docsconfiguration-has-a-useless-meta-description-and-a-dead-link) | Docs | Defect | Low | P3 | `/docs/configuration` has a useless meta description and a dead link | Confirmed |
| [FMT-001](#fmt-001-a-parameter-type-with-a-size-is-split-across-two-lines-in-the-formatted-procedure-header) | Web + SSMS | Defect | Medium | P1 | A parameter type with a size is split across two lines in the formatted procedure header | Confirmed (12 of 12 styles) |
| [SN-001](#sn-001-a-custom-snippet-saved-on-the-snippets-page-never-expands-in-the-editor) | Web | Defect | Medium | P1 | A custom snippet saved on the Snippets page never expands in the editor | Confirmed (2 runs) |
| [WEB-011](#web-011-three-refactor-items-can-never-work-in-the-web-edition-and-the-three-engine-only-items-are-disabled-although-the-engine-is-live) | Web | Defect / UX | Medium | P2 | Three Refactor items can never work in the web edition; the engine-only items are disabled although the engine is live | Confirmed |
| [WEB-013](#web-013-the-explorer-shows-stored-procedures-0-for-northwind-which-has-7) | Web | Defect | Medium | P2 | The explorer shows "Stored Procedures (0)" for Northwind, which has 7 | Confirmed; cause unknown |
| [FMT-002](#fmt-002-begin--end-of-an-if-else-or-while-block-are-written-at-column-0) | Web + SSMS | Defect | Medium | P2 | BEGIN / END of an IF, ELSE or WHILE block are written at column 0 | Confirmed (12 of 12 styles) |
| [FMT-003](#fmt-003-the-statement-after-go-n-is-indented) | Web + SSMS | Defect | Medium | P2 | The statement after GO n is indented | Confirmed (12 of 12 styles) |
| [AI-001](#ai-001-the-no-schema-privacy-mode-builds-a-prompt-that-contradicts-itself) | Web | Defect | Medium | P2 | The "No schema" privacy mode builds a prompt that contradicts itself | Confirmed (captured prompt) |
| [WEB-012](#web-012-escape-does-not-close-the-refactor-list-dialog) | Web | Defect | Low | P3 | Escape does not close the Refactor list dialog | Confirmed |
| [WEB-014](#web-014-the-explorer-filter-has-no-empty-state) | Web | UX | Low | P3 | The explorer filter has no empty state | Confirmed |
| [FMT-004](#fmt-004-format-changes-the-inside-of-a-block-comment-khamis-style) | Web + SSMS | Defect | Low | P3 | Format changes the inside of a block comment (Khamis Style) | Confirmed |
| [FMT-005](#fmt-005-odd-spacing-and-line-breaks-in-some-styles) | Web + SSMS | UX | Low | P3 | Odd spacing and line breaks in some styles | Confirmed (cosmetic) |
| [AI-002](#ai-002-fix-without-a-preceding-error-sends-error-number-0--no-error) | Web | UX | Low | P3 | Fix without a preceding error sends "Error Number: 0 / (no error)" | Confirmed |
| [UX-002](#ux-002-import-errors-the-style-import-prints-parser-internals-and-the-snippet-import-says-nothing) | Web | UX | Low | P3 | Import errors: the style import prints parser internals and the snippet import says nothing | Confirmed |
| [SSMS-003](#ssms-003-find-invalid-objects-reports-0-dependency-rows-scanned-on-a-database-that-has-63) | SSMS | Defect | Medium | P2 | Find Invalid Objects reports "0 dependency rows scanned" on a database that has 63 | Observed once; cause unknown |
| [SSMS-004](#ssms-004-format-document-turns-crlf-line-endings-into-lf) | SSMS | Defect | Low | P3 | Format Document turns CRLF line endings into LF | Observed once |

**Counts by priority:** P1 = 9 · P2 = 12 · P3 = 12 · total 33. **By severity:** High 4 · Medium 17 · Low 12.

---

## 2. Answers to the two questions in the request

### 2.1 "Save credentials in the web, open it after some time, it asks for password and display name again"

I tested this against the real engine. The web edition **never stores a password in the browser** (by design, [ISavedSqlConnectionStore.cs:14-19](../src/AkmlSql.Web/Services/ISavedSqlConnectionStore.cs)); a password survives only if the engine remembers it after a **successful Connect** with "Remember the password on the engine machine" ticked. I found three concrete ways the user ends up retyping everything, in likely order:

1. **Save without Connect (WEB-002, reproduced 2/2).** Save writes `RememberOnEngine=true` into the browser record but sends nothing to the engine. The reopened row shows "Remember" ticked, an empty password box and the only message "Enter the SQL password to connect (it is not stored in the browser)." — it looks saved but is not.
2. **A different browser address (WEB-003, reproduced).** `http://127.0.0.1:59416/` and `http://localhost:59416/` are different origins: the second one showed **no saved connections, no paired engine and an empty Display name**. Browser storage is also never marked persistent (`navigator.storage.persisted() === false`), so the browser may evict it; I could not reproduce eviction.
3. **A tab with no saved row (WEB-004, reproduced with a synthetic tab record).** The reconnect dialog opens with a **blank Display name** and an empty password.

What **works**: Windows-authentication connections and their saved rows survived a full browser restart and reconnected by themselves (W05, W06). **Not verified:** a SQL login whose password the engine really remembers — see §6 (Blocked: no valid SQL login was available and creating one was denied).

### 2.2 "Can the web catch the SSMS credentials automatically?"

**Not today, and not automatically by design (WEB-006).** SSMS saves SQL-auth passwords in `%AppData%\AKML SQL\sql-credentials.json` (DPAPI, current user; the file on this machine holds one entry). The web engine runs as `LocalSystem`, so it has its own empty store under `C:\Windows\System32\config\systemprofile\AppData\Roaming\AKML SQL\` (no `sql-credentials.json` exists there), and its entries use different keys (`mssql-web://…`) on purpose so that "a paired browser never signs in with a password it was not given" ([SqlServerCredentialResolver.cs:11-13](../src/AkmlSql.Engine/Pairing/SqlServerCredentialResolver.cs)). A safe opt-in design is in WEB-006.

---

## 3. Findings

### CLI-001 The installed `AkmlSql.Analyzer.exe` reports no issues for any SQL

- **Surface and location** — Command-line analyzer shipped by the installer: `C:\Program Files (x86)\AKML SQL\AkmlSql.Analyzer.exe` (20,336,631 bytes; source `src/AkmlSql.Analyzer`, `PublishTrimmed=true`, `PublishSingleFile=true`).
- **Environment and prerequisites** — Version 1.26.1104.1510. Fixture (`multi.sql`, three statements):
  ```sql
  SELECT * FROM dbo.Customers WHERE CompanyName LIKE '%a';
  UPDATE dbo.Foo SET a=1;
  SELECT NOLOCK FROM x;
  ```
  and the one-line `SELECT CustomerID, CompanyName FROM dbo.Customers` (no semicolon).
- **Steps to reproduce** — 1) `"C:\Program Files (x86)\AKML SQL\AkmlSql.Analyzer.exe" --file multi.sql`. 2) Run `--file sem1.sql --rules ST004`. 3) Run the same two commands with the un-trimmed build `src\AkmlSql.Analyzer\bin\Debug\net10.0\win-x64\AkmlSql.Analyzer.exe`. Reproduced on every run (6/6 across both fixtures and `--severity hint`); the local `bin\Release\...\publish\` copy (same size and date as the installed one) behaves identically.
- **Expected behavior** — Three findings for `multi.sql` (PE002, PE003, PE004) and ST004 for the one-liner, exactly as the un-trimmed build prints them.
- **Actual behavior** — Installed/published exe: `No issues found. Summary: 0 issues…` for both fixtures, and also for `DELETE FROM dbo.Foo`. Exit code is success, so a CI `--check` gate passes on any SQL. The JSON report's `"settings"` field contains the SQL file name (`"settings": "sem1.sql"`) instead of the settings path.
- **User impact and priority** — Anyone using the CLI in CI gets a silent false "clean". P1 because it is a shipped feature that cannot do its one job and fails silently; the fix is small.
- **Evidence** — [cli-analyzer-trimmed-vs-untrimmed.txt](AKML-QA-UX-Documentation-Audit-evidence/cli-analyzer-trimmed-vs-untrimmed.txt): side-by-side output of both builds on the same files.
- **Technical investigation** — The trimmed single-file exe finds nothing while the same commit un-trimmed finds everything. `RuleRegistry` discovers rules with `Assembly.GetExecutingAssembly().GetTypes()` + `Activator.CreateInstance` ([RuleRegistry.cs:15-21](../src/AkmlSql.Analysis/RuleRegistry.cs)); nothing references the rule classes statically, so the IL trimmer very likely removes them. **Cause: strongly indicated by the A/B result, not yet confirmed by inspecting the trimmed assembly.** The engine is not trimmed (CLAUDE.md), so IDE/web analysis is unaffected.
- **Proposed fix** — Either stop trimming this CLI, or give the trimmer what it needs (root the `AkmlSql.Analysis` assembly / annotate `RuleRegistry` with `DynamicallyAccessedMembers`, or register rules from a source-generated list). Add a post-publish smoke step to `build.ps1` / `Deploy-Build-Release.ps1`: run the published exe on a fixture and fail the build unless PE003 is reported. Fix the `settings` field of the JSON report to hold the real settings path (or null).
- **Acceptance criteria** — *Given* the published exe, *when* `--file multi.sql` runs, *then* it prints PE002, PE003 and PE004 and `--check` exits 1. *Given* `--file sem1.sql --rules ST004`, *then* one information finding is printed. *Given* the build pipeline, *when* the published CLI reports zero issues on the fixture, *then* the build fails.
- **Regression checks and dependencies** — Re-run after any change to trimming settings; check the engine and web still discover all 130+ rules (rule count test); installer must ship the rebuilt exe. Independent of every other item — do first.

### WEB-001 "Use this computer's engine" fails on an IIS install whose bridge port is not 47291

- **Surface and location** — Web editor status bar, button **Use this computer's engine** (`data-testid=engine-use-local`), also shown in the Settings › Connections page footer.
- **Environment and prerequisites** — Fresh browser profile (no saved engine). Web served by IIS at `http://localhost:59416/`; the installer put the engine bridge on **59417** (`C:\ProgramData\AKML SQL Web\config.json`, `INSTALL-SUMMARY.txt`: "Bridge port: 59417"). Nothing listens on 47291.
- **Steps to reproduce** — 1) Open `http://localhost:59416/` in a profile with no data. 2) Click **Use this computer's engine**. 3) Read the status bar and the browser console. Reproduced 2/2 clicks.
- **Expected behavior** — The one-click path pairs with the engine that belongs to the page the user is on, or says precisely why it cannot.
- **Actual behavior** — Status pill **Failed**, text "Handshake failed. Check engine pairing." The console shows `WebSocket connection to 'ws://localhost:47291/akmlsql' failed: net::ERR_CONNECTION_REFUSED` and the same for `wss://`. The wording blames pairing although nothing answered at all, and it does not mention the port. The user must discover Settings › Connections › Add and look up the bridge port in a file; manual pairing on port 59417 then works with no PIN (loopback).
- **User impact and priority** — Every first-run user of an IIS install with a non-default port (this one) hits a dead first step; the saved-connection experience in WEB-002/WEB-003 starts after it. P1.
- **Evidence** — [web-006-use-local-engine-failed.png](AKML-QA-UX-Documentation-Audit-evidence/web-006-use-local-engine-failed.png): Failed pill and the generic message. [web-007-add-engine-form.png](AKML-QA-UX-Documentation-Audit-evidence/web-007-add-engine-form.png): the manual form that does work. [web-009-after-pair-click.png](AKML-QA-UX-Documentation-Audit-evidence/web-009-after-pair-click.png): manual pairing on 59417 connected.
- **Technical investigation** — `UseLocalEngineAsync` ([StatusBar.razor:354](../src/AkmlSql.Web/Shared/StatusBar.razor)) and the auto-connect path ([IEngineAutoConnect.cs:336](../src/AkmlSql.Web/Services/IEngineAutoConnect.cs)) use `EngineEndpoint.DefaultPort = 47291` ([EngineEndpoint.cs:29](../src/AkmlSql.Web/Services/EngineEndpoint.cs)); the button's own tooltip says "localhost:47291" ([StatusBar.razor:349](../src/AkmlSql.Web/Shared/StatusBar.razor)). `PageEngine` only detects an engine that serves the page itself (`HEAD /`), which IIS hosting does not. **Confirmed.**
- **Proposed fix** — Let the installer write the chosen bridge port next to the bundle (for example `engine.json` with `{"scheme":"ws","port":59417}` in the IIS site root; the standalone host already serves its own address) and have `PageEngine`/`UseLocalEngineAsync` read it before falling back to 47291. If the connection is refused, replace the message with: **"No engine answered on localhost:47291. This install may use another port. Add engine… (Settings › Connections)"** with a button that opens the Add form pre-filled with host `localhost`. Reserve "Handshake failed. Check engine pairing." for a real handshake rejection.
- **Acceptance criteria** — *Given* an IIS install with bridge port 59417 and an empty profile, *when* the user clicks **Use this computer's engine**, *then* the pill becomes Live without opening Settings. *Given* no engine anywhere, *when* clicked, *then* the message names the port tried and offers **Add engine…**. *Given* an engine that rejects the handshake, *then* the old wording appears.
- **Regression checks and dependencies** — Standalone service host (engine serves the page), LAN/TLS mode (`wss`, certificate), repair/upgrade flows, `browser-setup` doc. Pairs with DOC-002.

### WEB-002 Saving a SQL-auth connection with "Remember" ticked stores only the flag, not the password

- **Surface and location** — Web › status bar **Connect** › *Connect to database* dialog › SQL Server authentication › **Save** button (`conn-save-btn`) and the saved-row view.
- **Environment and prerequisites** — Engine paired (see WEB-001), engine ≥ `credentials.sqlserver.v2` (the Remember checkbox is shown). Test input: Display name `QA SQL auth remember`, server `localhost`, SQL authentication, login `qa_nologin_user` (a login that does not exist — a valid SQL login was not available; the behaviour under test happens before any server call), password typed, **Remember the password on the engine machine** ticked.
- **Steps to reproduce** — 1) Open the dialog, **＋ New**, fill the form above, tick Remember. 2) Click **Save** (not Connect): "Saved." appears. 3) Close the dialog, reopen it, click the saved row `QA SQL auth remember`. 4) Observe the form. 5) Click **Connect** with the password empty. Reproduced 2/2 (two runs).
- **Expected behavior** — Either the password is kept on the engine when the user saves with Remember on, or the dialog says clearly that it is not and when it will be.
- **Actual behavior** — Step 3 shows Remember **ticked**, Password **empty** with no placeholder, no "Forget it" button (the engine holds nothing) and no hint. Step 5 answers in red: "Enter the SQL password to connect (it is not stored in the browser)." — which reads as if Remember were broken. A later restored tab shows "The engine has no password it can use for this login. Type it; with Remember on, the engine keeps it." only on the reconnect path, not on the plain saved-row view.
- **User impact and priority** — This is the most likely cause of the user's complaint: they save credentials once, the UI says "Saved.", and next time they must type the password again. P1 (High): it silently contradicts the user's intent.
- **Evidence** — [web-016-sql-auth-form-remember-ticked.png](AKML-QA-UX-Documentation-Audit-evidence/web-016-sql-auth-form-remember-ticked.png): the form before Save. [web-019-saved-sql-row-selected.png](AKML-QA-UX-Documentation-Audit-evidence/web-019-saved-sql-row-selected.png): reopened row, Remember ticked, empty password. [web-020-connect-empty-password.png](AKML-QA-UX-Documentation-Audit-evidence/web-020-connect-empty-password.png): the contradictory red message. [web-021-connect-nonexistent-login.png](AKML-QA-UX-Documentation-Audit-evidence/web-021-connect-nonexistent-login.png): server answer for the bad login ("Login failed for user 'qa_nologin_user'.").
- **Technical investigation** — `SaveAsync` ([ConnectionManagerModal.razor:1441-1492](../src/AkmlSql.Web/Shared/ConnectionManagerModal.razor)) writes `RememberPassword = !_windowsAuth && _sqlRemember` to IndexedDB and never calls the engine. The engine stores a password only in `SqlServerCredentialResolver.RememberIfRequested`, which runs after the server **accepted** a Connect ([SqlServerCredentialResolver.cs:127-162](../src/AkmlSql.Engine/Pairing/SqlServerCredentialResolver.cs)). The missing-password hint (`conn-sql-missing-hint`) is gated on `PasswordMissingForForm`, which is false for a plain saved row. **Confirmed.**
- **Proposed fix** — (a) In the dialog, when Remember is ticked and the engine has no password for this login, show under the checkbox: **"The engine will keep this password after your next successful Connect. Save alone does not store it."** (b) After Save with a typed password and Remember on, offer **Save and connect** (one click) — or have Save run the existing test, then the remember call. (c) On selecting a row whose flag is on but whose engine answer is "none", show the same hint the reconnect path already uses and replace the red message with the specific cause. (d) Consider labelling the row badge `SQL · password not stored yet`.
- **Acceptance criteria** — *Given* a new SQL-auth form with a valid login, password and Remember on, *when* the user clicks **Save and connect**, *then* the connection succeeds, the row shows Remember on, and after a browser restart opening that row shows the placeholder "Remembered on the engine machine" and Connect works with the password box empty. *Given* Save only, *then* the dialog states the password is not stored yet. *Given* a row with the flag on and no engine password, *then* the hint appears on selection (not only after Connect fails).
- **Regression checks and dependencies** — Forget it flow, PostgreSQL remember (`SavePostgresAsync`), copy-password-from-tab (`session.copy.v2`), restored-tab reconnect hint, tripwire test that forbids the word "password" in stored records. Needs a valid SQL login to verify end to end (see §6). Pairs with WEB-003, WEB-004, DOC-001.

### WEB-003 Saved connections and engine pairing live in per-origin, non-persistent browser storage

- **Surface and location** — Web edition, all of IndexedDB (`AkmlSqlWeb` v5: `savedSqlConnections`, `editorTabs`, `connections`/`pairingTokens`).
- **Environment and prerequisites** — Profile that already holds the QA engine pairing and the saved row `QA Local Northwind`. Same server, two addresses: `http://localhost:59416/` and `http://127.0.0.1:59416/`.
- **Steps to reproduce** — 1) In the paired profile open `http://localhost:59416/` — status Live, saved row present. 2) Open `http://127.0.0.1:59416/`. 3) Click **Connect**. 4) In the first origin run `navigator.storage.persisted()`. Reproduced 1/1 (deterministic: browser storage is origin-scoped).
- **Expected behavior** — The user is told that this address has no saved data, or the data follows them; the browser is asked to keep the data.
- **Actual behavior** — `127.0.0.1`: pill **Offline**, "No engine paired yet", dialog with **"No saved connections — fill the form and Save."** and empty Display name/password — exactly the experience the user describes. `navigator.storage.persisted()` returns **false** for the working origin: the app never requests persistent storage, so Chrome/Edge may evict it under storage pressure or when the user clears site data on exit (**not reproduced** — see §6).
- **User impact and priority** — Users who open the page by another name (IP, machine name, `https`, a different port after a host change) or whose browser clears data lose all saved connections and the pairing without any explanation. P1 (High) because it directly produces the reported symptom and has no recovery hint.
- **Evidence** — [web-022-other-origin-127.0.0.1.png](AKML-QA-UX-Documentation-Audit-evidence/web-022-other-origin-127.0.0.1.png): the empty state on the other origin. [web-saved-connection-record.json](AKML-QA-UX-Documentation-Audit-evidence/web-saved-connection-record.json): stored record and `storage.persisted:false`. [web-014-after-browser-restart.png](AKML-QA-UX-Documentation-Audit-evidence/web-014-after-browser-restart.png): the same origin keeps everything across a restart.
- **Technical investigation** — The only `navigator.storage` use is `estimate()` in `akml-indexeddb.js:173-176`; there is no `persist()` call anywhere in `src/AkmlSql.Web`. `browser-setup` already warns that connections "at the old address do not appear at the new one", but the app itself says nothing. **Confirmed (origin and no-persist); eviction is a hypothesis.**
- **Proposed fix** — (1) Call `navigator.storage.persist()` the first time a connection or engine is saved, and show the result in Settings › Diagnostics ("Browser storage: persistent / best-effort"). (2) When the app starts with no engine and no data on a non-canonical host, show a one-line banner: **"Saved connections are stored per browser address. If you used another address (for example localhost vs 127.0.0.1), open that one, or Import settings here."** (3) Evaluate keeping non-secret connection metadata on the engine (it is already shared per machine) so any address sees the list.
- **Acceptance criteria** — *Given* a first connection save, *then* `navigator.storage.persisted()` is true when the browser grants it and Diagnostics shows the state. *Given* an empty profile on `127.0.0.1` whose engine is reachable, *when* the page loads, *then* the banner explains the address difference. *Given* a connection saved on one origin and the engine-held list enabled, *then* the other origin lists it.
- **Regression checks and dependencies** — Export/Import settings round trip, IndexedDB version migrations (v5), standalone host (single origin), LAN `wss` origin. Pairs with WEB-001, WEB-002, DOC-001.

### UX-001 An optional missing semicolon (ST004) underlines the whole statement instead of one small blue marker

- **Surface and location** — Web SQL editor (after **Analyse**) and SSMS 22 editor (live analysis). Rule `ST004` (Style, default severity Information, "Statement is not terminated with a semicolon — add ';' for clarity and future compatibility").
- **Environment and prerequisites** — Web `1.26.1104.1510`, light theme, Chromium. SSMS 22.12.12405.243 with extension `1.26.1104.1510`, `(local)` / `Northwind`, Windows authentication, light theme, `codeAnalysis.enabled/runOnType = true`, `squiggleStyle = "underline"`, `disabledNativeIntelliSense = false`. **Smallest snippet:**
  ```sql
  SELECT * FROM dbo.Customers
  ```
  (one statement, no `;`; T-SQL does not require it). Also tested: the same with `;`, and two statements without `;` (`SELECT 1` newline `SELECT 2`).
- **Steps to reproduce** — *Web:* 1) Open the editor. 2) Replace the text with the snippet. 3) Click **Analyse** (diagnostics are a snapshot of Analyse, they do not appear while typing). 4) Look at the line, hover the underline and the gutter dot. 5) Append `;` and click **Analyse** again. Reproduced 3/3 cases. *SSMS:* 1) Open a query window on `(local)` / Northwind (Windows authentication). 2) Click into the editor, type `select * from dbo` (then, in a fresh buffer, `SELECT 1 AS a`, Enter, `SELECT 2 AS b`). 3) Wait about 3–10 s for live analysis. 4) Look at the text; then type `;` at the end of a single statement and look again. Reproduced 2/2 typed cases; the file-loaded case is SSMS-002.
- **Expected behavior** — A missing optional terminator is advice, not an error: **one small blue marker on the relevant line (gutter), with no recolouring, background highlight or underline of the SQL text.** Adding the `;` removes the marker. Genuine syntax errors keep strong feedback (red).
- **Actual behavior** — *Web:* a blue **wavy underline over the entire statement text** (`cm-lintRange cm-lintRange-info`, `text-decoration: underline wavy rgb(37,99,235)`) plus a blue dot in the gutter and a tooltip "ST004: Statement is not terminated with a semicolon — add ';' for clarity and future compatibility". With two statements both whole statements are underlined. With `;` appended the underline and dot disappear (clears correctly). *SSMS (observed):* after typing, **a magenta wavy squiggle runs under the whole statement** — `select * from dbo` is underlined end to end; with two statements `SELECT 1 AS a` and `SELECT 2 AS b` each is underlined in full. The AKML log shows the matching analysis passes (`Analysis complete: 4 findings`). This is exactly what `DiagnosticTagger` is coded to do: an `IErrorTag` over the rule's span (the whole statement) with Information (1) mapped to `PredefinedErrorTypeNames.OtherError`. After the statement is complete and `;` is typed the squiggle is gone and the VS health indicator reads "No issues found". **Source of the highlight:** the web underline is AKML-only (CodeMirror, no host parser). In SSMS the missing-semicolon squiggle is **AKML's**: native SSMS draws nothing for an unterminated but valid statement (the opened-file case in SSMS-002 shows native "No issues found" with no marks), and the magenta colour is `OtherError`, not the red native syntax squiggle. Native SSMS does add its own red squiggles and error counts for *real* syntax problems (see below), independently of AKML. A yellow warning glyph appeared in the glyph margin for `select * from dbo` (a Warning-level finding). In a later run, after Format Document on a four-statement script, the glyph margin showed **blue dots for Information findings** (lines 2 and 3) next to magenta squiggles on `dbo.Orders` and green squiggles on the `INSERT`, and an orange dot for a warning ([ssms-menu-M01.png](AKML-QA-UX-Documentation-Audit-evidence/ssms-menu-M01.png)), so the blue marker the owner asked for already exists in SSMS: the fix there is to stop drawing the text squiggle.
- **Genuine syntax errors in SSMS (observed):** `SELEC CustomerID FROM dbo.Customers;` shows red wavy squiggles under `SELEC` and `FROM` and the status area changes to "2 errors, 0 warnings"; `... WHERE` (incomplete) shows only a tiny red mark at the end of the line. Which of AKML or native SSMS draws these red marks could not be separated in this run (both can produce red); the point for the fix is that real errors are already red and visibly different from the magenta ST004 squiggle.
- **User impact and priority** — Every T-SQL author who omits optional semicolons gets whole-statement underlines on all their code, which the user wants removed; it makes real errors harder to see. Medium / P1 because the user asked for it explicitly and the web part is small.
- **Evidence** — [web-sem-1-missing.png](AKML-QA-UX-Documentation-Audit-evidence/web-sem-1-missing.png): wavy underline under the whole statement. [web-sem-7-range-tooltip.png](AKML-QA-UX-Documentation-Audit-evidence/web-sem-7-range-tooltip.png): underline, gutter dot and ST004 tooltip. [web-sem-2-with-semicolon.png](AKML-QA-UX-Documentation-Audit-evidence/web-sem-2-with-semicolon.png): marker gone after adding `;`. [web-sem-3-two-stmts.png](AKML-QA-UX-Documentation-Audit-evidence/web-sem-3-two-stmts.png): two statements, two whole-statement underlines. [cli-analyzer-trimmed-vs-untrimmed.txt](AKML-QA-UX-Documentation-Audit-evidence/cli-analyzer-trimmed-vs-untrimmed.txt): un-trimmed analyzer reports `sem1.sql(1,1): information ST004` — the rule fires from column 1, i.e. the whole-statement span. *SSMS:* [ssms-sem-F-two-statements-editor.png](AKML-QA-UX-Documentation-Audit-evidence/ssms-sem-F-two-statements-editor.png) (both statements underlined in full), [ssms-dbo-E1-list-after-dbo.png](AKML-QA-UX-Documentation-Audit-evidence/ssms-dbo-E1-list-after-dbo.png) (`select * from dbo` squiggled, yellow glyph, error/warning counters), [ssms-sem-B-with-semicolon-full.png](AKML-QA-UX-Documentation-Audit-evidence/ssms-sem-B-with-semicolon-full.png) (with `;`: clean, "No issues found"), [ssms-sem-C-syntax-error-full.png](AKML-QA-UX-Documentation-Audit-evidence/ssms-sem-C-syntax-error-full.png) and [ssms-sem-C-syntax-error-editor.png](AKML-QA-UX-Documentation-Audit-evidence/ssms-sem-C-syntax-error-editor.png) (real syntax error, red), [ssms-sem-D-incomplete-editor.png](AKML-QA-UX-Documentation-Audit-evidence/ssms-sem-D-incomplete-editor.png) (incomplete `WHERE`).
- **Technical investigation** — Rule: [ST004_MissingSemicolon.cs:54-77](../src/AkmlSql.Analysis/Rules/Style/ST004_MissingSemicolon.cs) (range = whole statement; a `Add semicolon` insert fix at the end). Web rendering: `toLintDiagnostics` ([akml-editor.js:1777](../src/AkmlSql.Web/wwwroot/js/akml-editor.js)) turns every finding into a CodeMirror lint range. SSMS: [DiagnosticTagger.cs:89-103](../src/AkmlSql.Shell.Shared/Analysis/DiagnosticTagger.cs). Settings that look like they should control this (`squiggleStyle`, `showAdvisoryHints`) are never read — see SSMS-001. **Cause confirmed for web; confirmed from source for SSMS.**
- **Proposed fix** — Give ST004 (and the other advisory Information/Hint style rules) a **marker-only presentation**. *Web:* keep the finding in Problems and the gutter dot; add `markClass: 'akml-lint-marker-only'` for ST004 and CSS `.akml-lint-marker-only { text-decoration: none; background: none; }`, or emit the diagnostic with a zero-width range at the end of the statement so no text is decorated; keep the tooltip on the dot with the suggested copy **"Optional: end this statement with ';'. Add semicolon (Alt+Enter)"** and the existing quick fix. *SSMS:* do **not** emit an `IErrorTag` for marker-only rules; draw the existing warning-glyph infrastructure (`WarningGlyph`) as a small blue information glyph in the glyph margin and keep the Error List entry (Information). *Host limitation / alternative:* the SSMS/VS editor has no text-neutral per-line tag other than the glyph margin (and overview-margin annotations); `PredefinedErrorTypeNames.Suggestion` still draws three dots under text, so if the glyph proves too subtle, use that as the fallback and say so in the docs. Real syntax errors stay red (`SyntaxError`/web error severity).
- **Acceptance criteria** — *Given* `SELECT * FROM dbo.Customers` in the web editor, *when* Analyse runs, *then* the line has one small blue gutter marker and `getComputedStyle` of the statement text shows no underline, background or colour change. *Given* the marker, *when* `;` is appended and Analyse reruns, *then* the marker is gone. *Given* `SELEC * FROM dbo.Customers;`, *then* a red error indication still appears (see WEB-005). *Given* SSMS 22 with the same snippet, *then* the statement text has no squiggle, the glyph margin shows a blue info glyph, the Error List lists ST004 as Information, and after typing `;` both disappear.
- **Regression checks and dependencies** — Other Information/Hint rules (decide which are marker-only), Problems tab counts, lightbulb/Add-semicolon fix, disable-rule scopes, high-contrast and dark themes, PostgreSQL `PGST002` (Hint — "Missing statement terminator"), format-on-save with `semicolons: insert`. Sequence after SSMS-001 (settings plumbing). The SSMS glyph margin already shows a blue dot for Information findings (verified), so no new glyph is needed.

### WEB-005 Syntax errors get no feedback from Analyse and no in-editor marker from Parse

- **Surface and location** — Web editor toolbar **Analyse** and **Parse**, Problems and Messages tabs, editor text.
- **Environment and prerequisites** — Web `1.26.1104.1510`; tested without a connection and with Northwind connected. Inputs: `SELEC * FROM dbo.Customers;`, `SELECT * FROM WHERE;`, `SELECT * FROM dbo.Customers WHERE`.
- **Steps to reproduce** — 1) Put an input in the editor. 2) Click **Analyse** and read the Problems tab. 3) Click **Parse** (needs a connection) and read Messages. 4) Look for any mark in the editor. Reproduced 3/3 inputs.
- **Expected behavior** — A script that does not parse is reported as such wherever the user looks: Problems lists an error with line and column, the editor marks the spot in red, and "No problems found." is never shown for unparseable SQL. Parse works offline because the browser already formats and analyses offline.
- **Actual behavior** — Analyse: **"No problems found. Run Analyse (Ctrl+K, Ctrl+L) again after a change."** for all three inputs, and no lint range. Parse button is **disabled without a connection** (tooltip "Parse needs a database connection"); with a connection it posts `Msg 102, Level 15, State 1, Line 1 / Incorrect syntax near '*'.` (or `Msg 156 … near the keyword 'WHERE'`) to Messages, status text "Query completed with errors.", and still **no marker in the editor** (the editor and gutter stay clean).
- **User impact and priority** — A user relying on Analyse is told their broken query is fine; the first syntax feedback comes only from the server. It also undermines the "marker for optional advice vs strong feedback for real errors" model of UX-001. Medium / P1.
- **Evidence** — [web-025-problems-tab-no-connection.png](AKML-QA-UX-Documentation-Audit-evidence/web-025-problems-tab-no-connection.png): "No problems found." for `SELEC …`. [web-syn-1-after-parse.png](AKML-QA-UX-Documentation-Audit-evidence/web-syn-1-after-parse.png), [web-syn-2-after-parse.png](AKML-QA-UX-Documentation-Audit-evidence/web-syn-2-after-parse.png), [web-syn-3-after-parse.png](AKML-QA-UX-Documentation-Audit-evidence/web-syn-3-after-parse.png): Messages tab error, clean editor. [web-sem-4-syntax-error.png](AKML-QA-UX-Documentation-Audit-evidence/web-sem-4-syntax-error.png), [web-sem-5-syntax-error2.png](AKML-QA-UX-Documentation-Audit-evidence/web-sem-5-syntax-error2.png): clean editor after Analyse.
- **Technical investigation** — `AnalyseTabAsync` (`Editor.razor:2354`) shows whatever `IAnalyserService.AnalyseAsync` returns; rules run on a parsed batch, so an unparseable batch yields no findings. **Hypothesis:** the analyser skips batches that fail to parse and nothing converts the parse error into a finding. Not verified which analyser (in-browser or engine) answered with the engine paired.
- **Proposed fix** — When the analyser's parse step fails, return an **Error** finding (`SYNTAX`, message from ScriptDom: "Incorrect syntax near '*'.", line/column of the offending token) so Problems, the red marker and the status line all show it. Enable **Parse** offline using the in-browser ScriptDom parser (label the result "Checked in the browser; the server was not asked"). Map Parse errors from the engine to editor ranges too (red wavy under the token). Replace the empty-state text when the script has a syntax error. Status copy: **"Parse failed: 1 syntax error."** instead of "Query completed with errors."
- **Acceptance criteria** — *Given* `SELEC * FROM dbo.Customers;` and no connection, *when* Analyse runs, *then* Problems lists one Error with line 1 and the token is marked red. *Given* the same text, *when* Parse runs (connected or not), *then* the same marker appears and the status says "Parse failed". *Given* valid SQL, *then* Problems still says "No problems found."
- **Regression checks and dependencies** — Format on a script that does not parse (the formatter returns the text unchanged), PostgreSQL parse errors (`PgErrorHints`), ST004 marker-only behaviour (UX-001), large scripts (parse cost).

### DOC-001 "Connecting to SQL Server" covers SSMS only and never explains web connections or credentials

- **Surface and location** — `https://akml.khamis.work/docs/topics/connecting` (last updated 7 October 2026) and `https://akml.khamis.work/docs/topics/browser-setup`.
- **Environment and prerequisites** — Live site crawled 2026-11-20; page text saved during the crawl.
- **Steps to reproduce** — 1) Open `/docs/topics/connecting`. 2) Search for "browser", "web", "Remember", "engine", "display name". 3) Open `/docs/topics/browser-setup` › SQL Server and look for password behaviour. Reproduced on both pages.
- **Expected behavior** — A user of the web edition can learn how to connect, what Save and Remember do, where passwords live, and why a password may be requested again.
- **Actual behavior** — `connecting` describes only SSMS ("AKML SQL works with the connection your query window already uses…", DPAPI file `%AppData%\AKML SQL\sql-credentials.json`, "Once a credential is saved, other query windows… pick it up"). It says nothing about the web dialog, "Remember the password on the engine machine", that the browser stores no password, that SSMS and web passwords are separate stores, or that saved connections are per browser address. `browser-setup` › SQL Server says only "supply the server and appropriate authentication… Use Test… then Connect".
- **User impact and priority** — The reported "asks for the password again" cannot be understood or self-solved from the docs. Medium / P1 because it ships with the WEB-002 fix.
- **Evidence** — [site-11_docs_topics_connecting.png](AKML-QA-UX-Documentation-Audit-evidence/site-11_docs_topics_connecting.png) and [site-10_docs_topics_browser-setup.png](AKML-QA-UX-Documentation-Audit-evidence/site-10_docs_topics_browser-setup.png) (top of each page); [site-crawl-results.json](AKML-QA-UX-Documentation-Audit-evidence/site-crawl-results.json) lists all 20 pages checked.
- **Technical investigation** — Pages are auto-ingested from `doc/topics/*` (spec 034); source files `doc/topics/connecting.md` and `doc/topics/browser-setup.md`. **Confirmed gap; no code cause.**
- **Proposed fix** — Add a section **"Connecting in the browser"** to `connecting` (and link it from `browser-setup`): (1) the Connect to database dialog fields; (2) Windows authentication uses the **engine's** account; (3) **Save** stores name/server/login only; **Remember the password on the engine machine** stores the password (encrypted, in the engine's account) after the next successful Connect; (4) the browser never stores passwords; (5) web and SSMS passwords are separate (WEB-006); (6) saved connections and pairing belong to the browser address (`localhost` ≠ `127.0.0.1`), with Export/Import settings. Screenshots to capture: the dialog with SQL authentication and Remember ticked; the "engine will keep this password after your next successful Connect" hint (after WEB-002); Settings › Connections.
- **Acceptance criteria** — *Given* the published docs, *when* a reader searches the site for "remember password", *then* the section is found and answers where the password lives and when it is asked again. *Given* the fixed UI, *then* the screenshots match it.
- **Regression checks and dependencies** — Docs metadata regeneration (`docs-metadata.json`), site search, links from the dialog help. Write after WEB-002/WEB-003 behaviour is settled.

### WEB-004 The reconnect dialog opens with a blank Display name when the tab has no saved row

- **Surface and location** — Web › *Connect to database* dialog opened from a restored tab (F5 on a tab that needs a password).
- **Environment and prerequisites** — A tab record whose connection binding is SQL authentication (`Login = qa_nologin_user`, Remember on) but whose `SavedConnectionId` is null or no longer exists (a deleted row, cleared store or a connection typed without saving). **The tab record was written by hand into IndexedDB `editorTabs`** to reproduce this; a real user reaches it by connecting without Save, or after deleting the row.
- **Steps to reproduce** — 1) Edit the tab record's `Binding.SavedConnectionId` to `null`. 2) Reload the page. 3) Press F5. 4) Read the dialog fields. Reproduced 1/1 (synthetic record). With `SavedConnectionId` set to the real row the name `QA SQL auth remember` was filled (control case).
- **Expected behavior** — The dialog reconnects the tab to where it was and shows what it can recall; if no name is known it proposes one (`localhost / master`, the default Save would use) or says "Not saved".
- **Actual behavior** — Display name is empty, the login is filled, the password is empty and a hint says the engine has no password. The empty name field looks like the app forgot the connection.
- **User impact and priority** — Adds to the "it forgot my display name and password" experience; harmless but confusing. Medium / P2.
- **Evidence** — [web-027-unsaved-binding-after-F5.png](AKML-QA-UX-Documentation-Audit-evidence/web-027-unsaved-binding-after-F5.png): blank name, filled login. [web-024-restored-sqlauth-tab-after-F5.png](AKML-QA-UX-Documentation-Audit-evidence/web-024-restored-sqlauth-tab-after-F5.png): control case with the name filled and the clear hint.
- **Technical investigation** — `ApplyPrefill` ([ConnectionManagerModal.razor:932-975](../src/AkmlSql.Web/Shared/ConnectionManagerModal.razor)): when no saved row matches, it calls `NewConnection(b.Kind)`, which clears `_name`, and never derives a name from the binding. **Confirmed.**
- **Proposed fix** — Prefill `_name` with the binding's `server / database` default (the same default `SaveAsync` uses), grey placeholder text "Not saved yet — Save to keep this connection", and show a small "Not a saved connection" note.
- **Acceptance criteria** — *Given* a restored tab with no saved row, *when* the dialog opens, *then* Display name shows `localhost / master` (or a placeholder) and Save creates a row with that name.
- **Regression checks and dependencies** — Saved-row prefill path, PostgreSQL prefill, New query tab inheriting a binding.

### WEB-006 The web edition cannot reuse credentials saved by SSMS (by design) — answer to the user's question

- **Surface and location** — Web Connect to database dialog vs SSMS credential dialog; engine `SqlCredentialStore`.
- **Environment and prerequisites** — This machine: SSMS store `C:\Users\Administrator\AppData\Roaming\AKML SQL\sql-credentials.json` (one entry: server `(local)`, login `sa`; the blob was not decrypted or copied); web engine service `AkmlSqlWebEngine` as `LocalSystem` with `C:\Windows\System32\config\systemprofile\AppData\Roaming\AKML SQL\` (contains `config.json`, `history`, `logs`; **no** `sql-credentials.json`).
- **Steps to reproduce** — 1) Confirm an SSMS credential exists (file above). 2) In the web dialog choose SQL authentication for `localhost` with login `sa` and leave the password empty. 3) Connect. (Not executed against `sa`; the outcome follows from the code below — the web dialog requires a typed password, "Enter the SQL password to connect".)
- **Expected behavior** — A user who already saved a login in SSMS can choose to reuse it in the browser without retyping, with their explicit consent.
- **Actual behavior** — Never reused: (1) web entries are keyed `mssql-web://` / `mssql-web-trust://` and the resolver serves only bridge requests ([SqlServerCredentialResolver.cs:11-13, 32-36](../src/AkmlSql.Engine/Pairing/SqlServerCredentialResolver.cs)); (2) DPAPI `CurrentUser` + entropy ([SqlCredentialStore.cs:270-284](../src/AkmlSql.Core/Config/SqlCredentialStore.cs)) means the `LocalSystem` engine cannot decrypt the interactive user's blobs; (3) the SSMS engine speaks only a per-process named pipe.
- **User impact and priority** — The user asked for it; a cross-product convenience. Medium / P2 because it needs a security design, not a quick patch.
- **Evidence** — [web-020-connect-empty-password.png](AKML-QA-UX-Documentation-Audit-evidence/web-020-connect-empty-password.png): the web form demands a typed password. File and path facts above were read from disk; no screenshot of SSMS (Blocked).
- **Technical investigation** — Class remarks of `SqlServerCredentialResolver` record the intent: "a paired browser never signs in with a password it was not given". **Confirmed by code and files.**
- **Proposed fix** — Do **not** make it automatic. Offer an explicit, per-login opt-in **from SSMS**: in the SSMS credential dialog add a checkbox **"Also use this login in the browser workspace on this computer"**; on consent the SSMS side hands the password over a loopback-only, token-protected channel to the web engine, which stores it under its own key for that one server and login (visible in Settings › Connections › Forget password). The browser dialog then shows "Use the password saved from SSMS (sa on (local))" as an option, never as the silent default. Alternatives considered: running the web engine as the signed-in user (widens the engine's privileges), or a browser "import from SSMS" (the browser cannot read the file). Needs a spec and a security review (consent, audit log line, revoke).
- **Acceptance criteria** — *Given* a login saved in SSMS without the opt-in, *then* the browser still asks for the password. *Given* the opt-in ticked for one login, *then* the browser offers it, connects without a typed password, and Forget in the browser removes only the web copy. *Given* a different login on the same server, *then* nothing is shared.
- **Regression checks and dependencies** — Existing web remember/forget (WEB-002), SSMS credential dialog, DPAPI scope, bridge guards (`BridgeSqlTargetGuard`, `BridgeSqlSecurity`), docs (DOC-001).

### WEB-007 The SQL editor has no accessible name

- **Surface and location** — Web editor, CodeMirror `.cm-content` (the contenteditable that holds the SQL).
- **Environment and prerequisites** — axe-core 4.10.2 (wcag2a, wcag2aa, best-practice), route `/`, light theme.
- **Steps to reproduce** — 1) Open `/`. 2) Run axe. 3) Read `aria-input-field-name`. Reproduced 1/1 (same on every load).
- **Expected behavior** — The editor announces itself, for example "SQL query editor, multiline text".
- **Actual behavior** — axe (serious): ".cm-content — aria-label attribute does not exist or is empty; aria-labelledby does not exist".
- **User impact and priority** — A screen-reader user lands in an unnamed edit field in the main task of the product. Medium / P2.
- **Evidence** — [web-axe-results.json](AKML-QA-UX-Documentation-Audit-evidence/web-axe-results.json) (entry `light /`, `aria-input-field-name`).
- **Technical investigation** — The editor already sets content attributes (writing-assistant opt-outs, `akml-editor.attributes.test.mjs`); a name is missing. **Confirmed.**
- **Proposed fix** — Add `aria-label="SQL query editor"` (per tab: `SQL query editor — SQLQuery1`), `role="textbox"`, `aria-multiline="true"` through `EditorView.contentAttributes`, and extend the attributes unit test.
- **Acceptance criteria** — *Given* the editor page, *when* axe runs, *then* `aria-input-field-name` is absent and a screen reader announces the name when focus enters the editor.
- **Regression checks and dependencies** — Tab switching (attributes follow the active tab), the writing-assistant opt-out attributes test.

### SSMS-001 `codeAnalysis.squiggleStyle` and `showAdvisoryHints` are documented but never read

- **Surface and location** — SSMS Options/`config.json` › `codeAnalysis`; docs `/docs/configuration`; code `AppSettings.cs:757-774`.
- **Environment and prerequisites** — Installed config on this machine: `"squiggleStyle": "underline"`, `"showAdvisoryHints": true`.
- **Steps to reproduce** — 1) Search `src` for `SquiggleStyle` / `ShowAdvisoryHints`. 2) Read the docs table row for `squiggleStyle`. Reproduced (code search, one run).
- **Expected behavior** — A documented setting changes behaviour: `squiggleStyle` (`underline`, `dotted`, `solid` per the docs) alters the drawn squiggle; `showAdvisoryHints = false` hides hint-level markers.
- **Actual behavior** — The only references are the two property definitions ([AppSettings.cs:757-758, 773-774](../src/AkmlSql.Core/Config/AppSettings.cs)); `DiagnosticTagger` chooses its error type from severity alone. The docs ([doc/configuration.md:281](../doc/configuration.md) and the site page, row `squiggleStyle … (underline, dotted, solid)`) promise an effect that does not exist. **Not verified live in SSMS** (Blocked) — this is a code finding.
- **User impact and priority** — Users cannot reduce the noise of advisory squiggles even though the docs say they can; it is also the natural switch for UX-001. Medium / P2.
- **Evidence** — Code search results above; the site's configuration page row is in `site-text` captured by the crawl ([site-06_docs_configuration.png](AKML-QA-UX-Documentation-Audit-evidence/site-06_docs_configuration.png) shows the page top).
- **Technical investigation** — Search for `squiggleStyle|SquiggleStyle|showAdvisoryHints|ShowAdvisoryHints` in `src` returns the definitions only. **Confirmed.**
- **Proposed fix** — Wire both settings into `DiagnosticTagger` (and the Options page): `showAdvisoryHints=false` suppresses Hint tags; add a `marker` value to `squiggleStyle` (glyph only, see UX-001) and make Information-level advisory rules default to it. Until implemented, remove the rows from the docs.
- **Acceptance criteria** — *Given* `showAdvisoryHints=false`, *then* Hint-level findings draw nothing in the editor but remain in the Error List. *Given* `squiggleStyle="marker"`, *then* Information findings show only the glyph. *Given* the docs, *then* each documented value works.
- **Regression checks and dependencies** — Options page controls, `.casettings` overrides, Error List reporter. Prerequisite of UX-001's SSMS half.

### SSMS-002 A statement loaded from a file is not flagged by analysis until it is edited

- **Surface and location** — SSMS 22 query window opened from a `.sql` file; AKML live analysis (ST004).
- **Environment and prerequisites** — SSMS 22.12.12405.243, extension `1.26.1104.1510`, `(local)` / Northwind, Windows authentication, `codeAnalysis.runOnType = true`. File `qa-semicolon.sql` (in `%TEMP%`) containing exactly: `SELECT CustomerID, CompanyName FROM dbo.Customers` (no semicolon, no trailing newline).
- **Steps to reproduce** — 1) Launch `Ssms.exe qa-semicolon.sql -S (local) -d Northwind -C -nosplash` (Windows authentication is the default). 2) Wait until the status bar shows Connected. 3) Wait 15 s without touching the editor. 4) Look for an ST004 squiggle and read the AKML log. Reproduced 1/1 (one launch; not repeated).
- **Expected behavior** — The opened document is analysed once after connecting, so the same text gives the same findings as after typing (and as the web editor and the CLI give: ST004 on this text).
- **Actual behavior** — No squiggle and the VS indicator reads "No issues found". The AKML log shows an analysis pass for the document (`Analysis triggered` 04:07:05, then at 04:07:35 `Analysis complete: 0 findings in 173ms`), i.e. **ST004 was not reported for this text**, whereas after typing similar single-statement text (`select * from dbo`, two `SELECT … AS` lines) the engine returned findings (`4 findings`) and the squiggles appeared. The first pass at open (04:07:05) has no matching "complete" line.
- **User impact and priority** — A user who opens an existing script sees it as clean until they touch it, so advisory findings (and possibly warnings) are missing on first view; the web editor and CLI disagree with SSMS on the same text. Medium / P2 because it makes analysis results look random.
- **Evidence** — [ssms-sem-A-missing-full.png](AKML-QA-UX-Documentation-Audit-evidence/ssms-sem-A-missing-full.png): opened file, no marks, "No issues found". [ssms-sem-A-missing-editor.png](AKML-QA-UX-Documentation-Audit-evidence/ssms-sem-A-missing-editor.png): editor close-up. [ssms-probe-log.txt](AKML-QA-UX-Documentation-Audit-evidence/ssms-probe-log.txt): editor text at each step. The log lines above are from `%AppData%\AKML SQL\logs\akmlsql-20261120.log`.
- **Technical investigation** — **Hypothesis, not confirmed:** the first analysis ran before the connection/schema or the engine was ready (note the 173 ms first pass and the missing completion for the 04:07:05 trigger) and is not repeated until an edit; or a rule result is cached per session. Look at `AnalysisController` (the trigger and debounce paths) and what the engine does when a pass is requested before `ConnectionChanged` (sent at 04:07:11, after the first trigger).
- **Proposed fix** — Re-run analysis when the connection is established (after `ConnectionChanged` / schema Phase A) and when a document finishes loading, so an opened file is analysed once everything is ready; log a line when a pass is skipped or fails.
- **Acceptance criteria** — *Given* `qa-semicolon.sql` opened from disk, *when* the connection is established and 10 s pass without typing, *then* the engine logs a completed pass with the same findings the CLI reports and the squiggle (or marker, after UX-001) appears. *Given* the same document edited, *then* the result is identical.
- **Regression checks and dependencies** — Large files (analysis cost on open), disconnected windows, multiple windows, the Error List reporter. Repeat the observation first (it was one run); do it together with UX-001 so the marker change is verified on both paths.

### DOC-002 Troubleshooting has no entry for the pairing and "asks for the password again" symptoms

- **Surface and location** — `https://akml.khamis.work/docs/topics/troubleshooting` and `/docs/topics/browser-setup`.
- **Environment and prerequisites** — Live site crawl (page text of both).
- **Steps to reproduce** — Search both pages for "Handshake", "Failed", "pair", "password again", "47291", "bridge port". The only password mention in troubleshooting is "Logs never contain passwords or API keys". `browser-setup` says the localhost address is "normally http://localhost:47291/" and "use the actual URL provided by setup".
- **Expected behavior** — Troubleshooting lists the symptoms users actually hit: "Failed — Handshake failed", "Offline / No engine paired yet", "my saved connections are gone", "it asks for my password again".
- **Actual behavior** — None of those are listed; the IIS two-port arrangement (page 59416, engine 59417) and where to find the bridge port are not explained (the app's own Add form mentions `INSTALL-SUMMARY.txt`).
- **User impact and priority** — Users hitting WEB-001/002/003 find no help. Medium / P2.
- **Evidence** — [site-20_docs_topics_troubleshooting.png](AKML-QA-UX-Documentation-Audit-evidence/site-20_docs_topics_troubleshooting.png), [site-10_docs_topics_browser-setup.png](AKML-QA-UX-Documentation-Audit-evidence/site-10_docs_topics_browser-setup.png).
- **Technical investigation** — Content gap; sources `doc/topics/troubleshooting.md`, `doc/topics/browser-setup.md`. **Confirmed.**
- **Proposed fix** — Add a "Web edition" section to troubleshooting with four short entries (symptom → cause → fix): *Failed / Handshake failed* (wrong port or engine stopped → read Bridge port in `%ProgramData%\AKML SQL Web\INSTALL-SUMMARY.txt`, Settings › Connections › Add); *No engine paired yet*; *Saved connections missing* (different browser address, cleared site data, Export/Import); *Asks for the password again* (Save vs Connect, Remember on the engine machine, Forget). Add a short IIS note to `browser-setup`: page and engine use two ports. Screenshots: the Failed pill with the new message, Settings › Connections › Add.
- **Acceptance criteria** — *Given* the docs, *when* a user pastes "Handshake failed" or "asks for password again" into site search, *then* the matching entry is the top result and its steps resolve the symptom on this machine's setup.
- **Regression checks and dependencies** — Update after WEB-001 (message wording) and WEB-002 (hint copy) are final.

### WEB-008 Two badges fail colour contrast

- **Surface and location** — Settings › Connections: the **Active** badge (`.akml-connections-badge--active`); Format styles: the muted badge in the selected row (`.selected.akml-styles-row … .akml-styles-badge.muted`).
- **Environment and prerequisites** — axe-core, light theme; the Active badge needs a paired engine (QA engine was paired).
- **Steps to reproduce** — Open `/settings/connections` and `/styles`, run axe `color-contrast`. Reproduced 2/2 routes.
- **Expected behavior** — Text contrast of at least 4.5:1.
- **Actual behavior** — White on `#16a34a` at 11 px = **3.29:1**; `#697586` on `#f1f5f9` at 10 px = **4.26:1**.
- **User impact and priority** — Small status badges are hard to read for low-vision users. Low / P3.
- **Evidence** — [web-axe-results.json](AKML-QA-UX-Documentation-Audit-evidence/web-axe-results.json) entries `light /settings/connections` and `light /styles`.
- **Technical investigation** — Theme tokens for badges (the web semantic `Badge` tokens); **confirmed by axe values, token names not traced.**
- **Proposed fix** — Darken the active-badge fill (a green that reaches 4.5:1 with white) and the muted badge text token in selected rows; verify with axe in all three themes.
- **Acceptance criteria** — *Given* either route, *when* axe runs in light, dark and high-contrast themes, *then* no `color-contrast` violation remains.
- **Regression checks and dependencies** — Other `Badge` uses; the theme-token sync gate (`docs/theme-tokens.json`).

### WEB-009 Minor accessibility batch

- **Surface and location** — Web edition pages as listed.
- **Environment and prerequisites** — axe-core 4.10.2, light theme (dark theme was **not** verified: the forced-theme run returned identical results, so it is treated as Not Run).
- **Steps to reproduce** — Run axe on `/`, `/snippets`, `/styles`, `/history`, `/settings`, `/settings/connections`. Reproduced on every load.
- **Expected behavior** — No axe violations at wcag2a/aa; landmarks and headings in order.
- **Actual behavior** — (a) `/`: `nested-interactive` (serious) — the tab `#doc-tab-…` contains a focusable close control inside an interactive tab; `page-has-heading-one` (moderate). (b) `/history`: `label-title-only` (serious) — `select[title="Server"]`, `select[title="Database"]` have only a `title`. (c) `/styles`: `scrollable-region-focusable` (serious) — a scrollable `<pre>` is not keyboard-focusable; `heading-order` (moderate). (d) `/settings/connections`: `empty-table-header` (minor) — 5th `th` has no text. (e) All routes: `region` (moderate) — 6 nodes outside landmarks.
- **User impact and priority** — Keyboard and screen-reader friction in secondary screens. Low / P3.
- **Evidence** — [web-axe-results.json](AKML-QA-UX-Documentation-Audit-evidence/web-axe-results.json) (all routes, with selectors).
- **Technical investigation** — Tab strip is the shared `TabStrip` with `Closable`; History filters are plain selects with `title`. **Confirmed by axe; component fixes not traced.**
- **Proposed fix** — Give the History selects real labels (visually hidden `<label>`); make the tab close button a sibling with `aria-label="Close SQLQuery1"` instead of nested; add `tabindex="0"` and a name to the scrollable `<pre>`; give the empty `<th>` visually hidden text ("Actions"); add a visually hidden `<h1>` per route and fix the heading order on `/styles`; wrap the app chrome in `header/nav/main/footer` landmarks.
- **Acceptance criteria** — *Given* each route, *when* axe runs (wcag2a, wcag2aa, best-practice), *then* zero violations; keyboard Tab reaches the close buttons and the scrollable preview.
- **Regression checks and dependencies** — Tab strip keyboard model (Delete, middle click, Ctrl+Alt+W), Playwright selectors (`doc-tab`, `doc-tab-close`) that must keep working.

### WEB-010 Saved SQL Server records persist a PostgreSQL port (`EffectivePort: 5432`)

- **Surface and location** — IndexedDB `savedSqlConnections`.
- **Environment and prerequisites** — Any saved SQL Server row.
- **Steps to reproduce** — Save a Windows-auth connection, read the record from IndexedDB (DevTools › Application, or script). Reproduced 1/1.
- **Expected behavior** — Only stored fields are persisted; a SQL Server record carries no PostgreSQL port.
- **Actual behavior** — The record contains `"EffectivePort":5432` next to `"Kind":0`.
- **User impact and priority** — Harmless today but pollutes stored data and could be mistaken for the connection's port. Low / P3.
- **Evidence** — [web-saved-connection-record.json](AKML-QA-UX-Documentation-Audit-evidence/web-saved-connection-record.json).
- **Technical investigation** — `EffectivePort => Port ?? DefaultPostgresPort` ([ISavedSqlConnectionStore.cs:101](../src/AkmlSql.Web/Services/ISavedSqlConnectionStore.cs)) is a computed read-only property that `System.Text.Json` serializes. **Confirmed.**
- **Proposed fix** — Mark it `[JsonIgnore]`; existing records keep the extra field harmlessly (it is ignored on read).
- **Acceptance criteria** — *Given* a newly saved row, *then* the stored JSON has no `EffectivePort`; *given* an old row, *then* it still loads.
- **Regression checks and dependencies** — The store's tripwire test (forbids the word "password"), settings export/import.

### ENG-001 The pairing token store holds 166 indistinguishable "Web edition" entries

- **Surface and location** — Engine `C:\ProgramData\AKML SQL Web\tokens.json` (`tokenTtlDays: 90`).
- **Environment and prerequisites** — This machine, read without printing token values.
- **Steps to reproduce** — Count entries and labels in `tokens.json`. Reproduced 1/1.
- **Expected behavior** — Each paired browser is identifiable and revocable; unused entries are pruned.
- **Actual behavior** — 166 entries, **all labelled "Web edition"**, minted 2026-08-31 → 2026-10-26, 0 expired, 28 never used since minting; the file is ~49 KB. Loopback pairing in this audit minted no token (no new entry), so loopback needs no PIN as designed. The oldest entries reach their 90-day expiry around 2026-11-29, after which affected browsers must re-pair.
- **User impact and priority** — The user cannot tell which browser to revoke; the list grows with every test run. Low / P3.
- **Evidence** — Counts taken from the file (no screenshot; values are secrets).
- **Technical investigation** — Fields are `browserLabel, mintedAt, lastUsedAt, ttlExpiresAt` only. **Confirmed.**
- **Proposed fix** — Label tokens with browser family and the page origin, show "Paired browsers" with last used and **Revoke** in the web Settings and installer repair helper, and prune never-used tokens older than a set number of days.
- **Acceptance criteria** — *Given* two browsers paired, *then* Settings lists each with a distinct label and Revoke removes only that one; *given* a never-used token older than the limit, *then* it is pruned.
- **Regression checks and dependencies** — LAN PIN pairing, token expiry handling in the browser (re-pair message), `tokens.json` migration.

### DOC-003 `/docs/configuration` has a useless meta description and a dead link

- **Surface and location** — `https://akml.khamis.work/docs/configuration`.
- **Environment and prerequisites** — Live site.
- **Steps to reproduce** — 1) View source / read `meta[name=description]`. 2) Click the "web edition" link that points to `deployment.md#web-edition`. Reproduced 2/2.
- **Expected behavior** — A sentence that describes the page; every link resolves.
- **Actual behavior** — Description is **"The configuration file is stored at:"** (36 characters; the other 19 pages have 102–200). One internal link `deployment.md#web-edition` returns **404** (the crawl found 1 broken link in 20 pages).
- **User impact and priority** — Poor search snippet and a dead link. Low / P3.
- **Evidence** — [site-06_docs_configuration.png](AKML-QA-UX-Documentation-Audit-evidence/site-06_docs_configuration.png), [site-crawl-results.json](AKML-QA-UX-Documentation-Audit-evidence/site-crawl-results.json) (`broken` and `desc` for the page).
- **Technical investigation** — The docs ingest takes the first paragraph as the description; the link is a repository-relative `deployment.md` path that the ingest does not rewrite (it exists as `doc/deployment.md` in the repo). **Hypothesis for the link rewrite; the meta cause is confirmed.**
- **Proposed fix** — Add an explicit front-matter `description:` to `doc/configuration.md` (for example "Every config.json and .casettings setting, with defaults and examples.") and point the link at a published page or the GitHub blob URL. Add a docs-pipeline check that fails on descriptions shorter than 50 characters and on unresolved relative links.
- **Acceptance criteria** — *Given* the page, *then* the meta description is a full sentence and the crawl reports 0 broken links across all sitemap pages.
- **Regression checks and dependencies** — `docs-metadata.json` regeneration, other pages that link to `deployment.md`.

---

---

## 3b. Findings from the full-surface sweep (second pass)

### FMT-001 A parameter type with a size is split across two lines in the formatted procedure header

- **Surface and location** — Web editor › **Format** with any of the engine and built-in styles; T-SQL `CREATE PROCEDURE` with a parameter that has a sized type and a default.
- **Environment and prerequisites** — Web/engine `1.26.1104.1510`, Northwind connection (not needed for formatting). Input (single line): `create procedure dbo.p @a int, @b varchar(10)='x' as begin set nocount on; if @a>1 begin select 1 end else begin select 2 end; while @a>0 begin set @a=@a-1 end end`.
- **Steps to reproduce** — 1) Paste the input. 2) Pick a style in the Style list. 3) Click **Format**. 4) Read the `@b` line. Repeated for all 12 styles (8 on the engine, 4 built-in): **12 of 12** split the type (the built-in `Khamis Style` writes `VARCHAR (` / `10 ) = 'x'`, `ANSI-compact` writes lower-case `varchar(` / `10)`).
- **Expected behavior** — `@b varchar(10) = 'x'` stays on one line, in every style.
- **Actual behavior** — Default style prints `@b varchar(` and, on the next line, indented, `10) = 'x'`. The text is still valid T-SQL (a second Format gives the same text), but the type is torn apart. Whether the split also happens without a default value (`@b varchar(10)`) was not tested.
- **User impact and priority** — Every stored procedure with a sized, defaulted parameter is reformatted into something that looks broken and that a reviewer will distrust. Medium / P1: wide effect, visible, easy to reproduce.
- **Evidence** — [web-format-outputs.jsonl](AKML-QA-UX-Documentation-Audit-evidence/web-format-outputs.jsonl): the `…::proc` rows hold the full output of every style. [web-format-results.json](AKML-QA-UX-Documentation-Audit-evidence/web-format-results.json): pass/fail per style and sample.
- **Technical investigation** — Hypothesis: the formatter treats the parenthesis after a data type in a parameter list that has a default as an argument list and applies the list-breaking rule to it. Every style is affected, including the SQL Prompt-layout built-in, so the cause is probably shared (parser annotation or a common rule). **Not confirmed in code.**
- **Proposed fix** — Treat `(n)` / `(n,m)` / `(max)` after a data-type name in a parameter or column definition as part of the type token, never as a breakable list. Add the sample as a corpus case in `tests/format-parity` for all styles.
- **Acceptance criteria** — *Given* the input above, *when* Format runs with each of the 12 styles, *then* the `@b` line contains `varchar(10)` (with the style's own casing and spacing) on one line and a second Format changes nothing.
- **Regression checks and dependencies** — `DECLARE @x varchar(10)`, `CREATE TABLE` column types, `CAST(x AS varchar(10))`, `decimal(18,2)`, `nvarchar(max)`; the parity goldens (`AKML_UPDATE_PARITY_GOLDEN`) and web baselines must move with the fix.

### FMT-002 `BEGIN` / `END` of an `IF`, `ELSE` or `WHILE` block are written at column 0

- **Surface and location** — Web editor › **Format**, same procedure sample as FMT-001.
- **Environment and prerequisites** — As FMT-001.
- **Steps to reproduce** — Format the procedure sample with each style and look at the `IF`/`ELSE`/`WHILE` blocks. Reproduced for **12 of 12** styles (`ANSI-compact` writes lower-case `begin`/`end` at column 0).
- **Expected behavior** — A block's `BEGIN` and `END` align with the statement that owns them (or follow the style's documented control-flow option), and the body is indented one level deeper.
- **Actual behavior** — Default style: `IF @a > 1` is indented, but the next line `BEGIN` is at column 0, the body is indented 8 spaces, and `END` is at column 0 again; `ELSE`, `WHILE` behave the same. The outer procedure `BEGIN`/`END` are correct.
- **User impact and priority** — Nested control flow becomes hard to read; reformatting a file makes code review noise. Medium / P2.
- **Evidence** — [web-format-outputs.jsonl](AKML-QA-UX-Documentation-Audit-evidence/web-format-outputs.jsonl) (`Default::proc`, `Khamis Style (built-in)::proc` and the other rows).
- **Technical investigation** — Hypothesis: the nested `BEGIN…END` inside an `IF` has its indent computed from the statement start instead of the enclosing block. Control-flow options exist (Format styles › Control flow, 2 options) but the sample uses their defaults. **Not confirmed in code.**
- **Proposed fix** — Align a block's `BEGIN`/`END` with its owner and indent the body relative to it; cover `IF`, `ELSE`, `ELSE IF`, `WHILE`, `TRY…CATCH` with nested blocks in the parity corpus.
- **Acceptance criteria** — *Given* the sample, *when* Format runs, *then* every `BEGIN`/`END` of an `IF`/`ELSE`/`WHILE` block is indented to the owner's level and the body one level deeper, in all styles that use block layout.
- **Regression checks and dependencies** — Idempotency (a second Format must not move anything), `CREATE PROCEDURE` outer block, SSMS Format Document (same engine), style editor preview sample.

### FMT-003 The statement after `GO n` is indented

- **Surface and location** — Web editor › **Format**.
- **Environment and prerequisites** — Input: `select 1` ⏎ `GO` ⏎ `select 2` ⏎ `GO 3` ⏎ `print 'x'`.
- **Steps to reproduce** — Format with each style. Reproduced 12/12.
- **Expected behavior** — `PRINT 'x'` starts at column 0 after `GO 3`, as `SELECT 2` does after `GO`.
- **Actual behavior** — All 12 styles indent `PRINT 'x'` (4 spaces, a tab in `Khamis Style`); in `Compact` the `GO 3` line itself is indented too.
- **User impact and priority** — Scripts that use `GO n` get a stray indent that changes how the next batch reads. Medium / P2.
- **Evidence** — [web-format-outputs.jsonl](AKML-QA-UX-Documentation-Audit-evidence/web-format-outputs.jsonl) (`…::go` rows).
- **Technical investigation** — Hypothesis: the batch separator with a count is parsed as part of the following statement. The 2026-11-03 report noted a related `GO` joining problem in SSMS. **Not confirmed.**
- **Proposed fix** — Treat `GO <n>` exactly like `GO` in the layout stage (SQLCMD preprocessor) and add a corpus case.
- **Acceptance criteria** — *Given* the input, *when* Format runs in each style, *then* every line after a `GO`/`GO n` starts at column 0 and the output is idempotent.
- **Regression checks and dependencies** — `GO` followed by a comment, `GO` with trailing spaces, `GO` inside a string (must be left alone).

### FMT-004 Format changes the inside of a block comment (Khamis Style)

- **Surface and location** — Web editor › **Format** with `Khamis Style` (built-in and "on this engine").
- **Environment and prerequisites** — Input: `-- header comment` ⏎ `select a, /* inline */ b from t -- trailing` ⏎ `where x=1;` ⏎ `/* block` ⏎ `   comment */` ⏎ `select 2`.
- **Steps to reproduce** — Format with Khamis Style. Reproduced 2/2; the other 10 styles keep the comment text byte for byte.
- **Expected behavior** — Comment text is never altered (re-indenting the whole block is acceptable).
- **Actual behavior** — The second comment line `   comment */` becomes `comment */` (its leading spaces are removed); two blank lines are put before the comment.
- **User impact and priority** — Comments with aligned or indented text (ASCII tables, license headers) lose their layout. Low / P3.
- **Evidence** — [web-format-outputs.jsonl](AKML-QA-UX-Documentation-Audit-evidence/web-format-outputs.jsonl) (`Khamis Style (…)::comments`).
- **Technical investigation** — The style enables "Align multi-line comments" (Format styles › Whitespace › Align multi-line comments); the alignment trims the inner lines. **Hypothesis from the option name.**
- **Proposed fix** — When aligning, shift every line by the same offset instead of trimming each line; keep relative indentation.
- **Acceptance criteria** — *Given* the input, *when* Format runs with Khamis Style, *then* the comment's second line keeps its three leading spaces relative to the first.
- **Regression checks and dependencies** — Comments with tabs, CRLF input, `--` comments (untouched).

### FMT-005 Odd spacing and line breaks in some styles

- **Surface and location** — Web editor › **Format**, several styles.
- **Environment and prerequisites** — Samples `basic`, `dml`, `merge` (see the evidence file).
- **Steps to reproduce** — Format and read: `Default`/`Leading Commas`: `INNER JOIN   dbo.Orders o` (three spaces after the join keyword). `ANSI-compact`: `where RegionID in(` / `10,` / `11` / `)` — a two-item IN list is exploded over four lines and has no space before the parenthesis; `update      dbo.Region` is padded to a keyword column. `Compact`: `MERGE dbo.Region AS t USING(` then `SELECT 1 …` inside.
- **Expected behavior** — Single spaces between keyword and object name; short lists stay on one line unless the style says otherwise.
- **Actual behavior** — As above. Output is still correct and idempotent.
- **User impact and priority** — Cosmetic; users may take it for a bug in their style. Low / P3.
- **Evidence** — [web-format-outputs.jsonl](AKML-QA-UX-Documentation-Audit-evidence/web-format-outputs.jsonl).
- **Technical investigation** — Some of it is by design (keyword-column alignment in `ANSI-compact`); the triple space after `INNER JOIN` and the exploded `in(10, 11)` look unintended. **Hypothesis: the alignment pass pads for a longer keyword that is not on the line.**
- **Proposed fix** — Review the alignment padding for `JOIN` forms and the `IN (` list threshold; if intended, document it in the style's description.
- **Acceptance criteria** — *Given* the samples, *then* no style prints more than one space between `INNER JOIN` and the table name and a two-element IN list stays on one line.
- **Regression checks and dependencies** — Parity goldens for every style; style editor previews.

### WEB-011 Three Refactor items can never work in the web edition, and the three engine-only items are disabled although the engine is live

- **Surface and location** — Web editor › **Refactor** › *Expand INSERT columns*, *Expand UPDATE columns*, *Expand EXEC parameters*; and the group *Schema-aware (requires engine)* › *Smart Rename*, *Parameterize Values*, *Extract Procedure*.
- **Environment and prerequisites** — Northwind connected through the paired engine (pill Live). Inputs: `INSERT INTO dbo.Region VALUES (9, 'Test')`, `UPDATE dbo.Region SET RegionDescription = 'x' WHERE RegionID = 1`, `EXEC dbo.CustOrderHist 'ALFKI'`.
- **Steps to reproduce** — 1) Enter an input. 2) **Refactor** and pick the matching item. 3) Read the preview dialog. Reproduced 3/3.
- **Expected behavior** — The refactoring runs using Northwind's columns and parameter names, or the item is clearly marked unavailable before it is clicked.
- **Actual behavior** — The preview says "**no change** — This refactoring does not apply to the current document — no change would be made", with **Apply** disabled, although the document is exactly what the item is for. The three engine-only items are greyed with an "engine" tag and the tooltip "Requires a paired engine advertising 'refactoring.heavy'" even with a live paired engine.
- **User impact and priority** — Three of the ten lightweight items look broken; the heavy ones look broken too. Medium / P2.
- **Evidence** — [rf-noapply-Expand-INSERT-columns.png](AKML-QA-UX-Documentation-Audit-evidence/rf-noapply-Expand-INSERT-columns.png), [rf-noapply-Expand-UPDATE-columns.png](AKML-QA-UX-Documentation-Audit-evidence/rf-noapply-Expand-UPDATE-columns.png), [rf-noapply-Expand-EXEC-parameters.png](AKML-QA-UX-Documentation-Audit-evidence/rf-noapply-Expand-EXEC-parameters.png), [rf-schema-aware-items.png](AKML-QA-UX-Documentation-Audit-evidence/rf-schema-aware-items.png), [web-refactor-results-2.json](AKML-QA-UX-Documentation-Audit-evidence/web-refactor-results-2.json). The other seven items (old-style joins, BEGIN/END, remove semicolons, GROUP BY columns, unformat, deprecated syntax, sp_executesql) previewed, applied and were undone with one Ctrl+Z.
- **Technical investigation** — `IRefactoringService` runs "the ten lightweight (parser-only, no-schema) refactoring operations" in the browser, but `ExpandInsertColumnsOperation`, `ExpandUpdateColumnsOperation` and `ExpandExecParametersOperation` read `context.SchemaCache` (e.g. `ExpandInsertColumnsOperation.cs:97`) which the web call does not provide, so they answer "no change". The engine does not advertise `refactoring.heavy` (`Capabilities.cs:131`: "when T117 wires heavy refactorings"). **Confirmed.**
- **Proposed fix** — Either pass the cached schema of the active connection to these three operations, or run them through the engine; until then, disable them with a precise reason ("Needs the database schema — connect first" / "Not available in the browser yet"). Change the heavy items' tooltip to "Not available yet" when the engine is connected but lacks the capability, and show the operation's own warnings ("Could not resolve columns for dbo.Region") in the preview instead of the generic sentence.
- **Acceptance criteria** — *Given* Northwind connected, *when* *Expand INSERT columns* runs on the input, *then* the preview shows the column list of `dbo.Region` and Apply works. *Given* no schema, *then* the item is disabled with its reason. *Given* an engine without `refactoring.heavy`, *then* the tooltip says so plainly.
- **Regression checks and dependencies** — SSMS Refactor menu (same operations, schema available), undo, preview/Apply flow.

### WEB-012 Escape does not close the Refactor list dialog

- **Surface and location** — Web editor › **Refactor** button › "Refactor" list dialog.
- **Environment and prerequisites** — Any connected or unconnected tab.
- **Steps to reproduce** — 1) Click **Refactor**. 2) Press Escape (with focus anywhere). 3) Observe. Reproduced 3/3. The same test on the View menu, the Connect to database dialog, a schema context menu and the Settings "Restore defaults" confirmation closed on Escape.
- **Expected behavior** — Escape dismisses the dialog, as it does for the others.
- **Actual behavior** — The dialog stays open until **Cancel** is clicked.
- **User impact and priority** — Keyboard users cannot dismiss it quickly. Low / P3.
- **Evidence** — [web-escape-results.json](AKML-QA-UX-Documentation-Audit-evidence/web-escape-results.json) (`ESC-REFACTOR` fail, other rows pass). [inv-menu-ai-button.png](AKML-QA-UX-Documentation-Audit-evidence/inv-menu-ai-button.png): the Refactor dialog still open after Escape.
- **Technical investigation** — The list dialog is a custom dialog, not the shared dialog service the others use. **Hypothesis.**
- **Proposed fix** — Use the shared dialog (Escape, focus trap, return focus) for the Refactor list and its preview.
- **Acceptance criteria** — *Given* the Refactor list or its preview is open, *when* Escape is pressed, *then* it closes and focus returns to the Refactor button.
- **Regression checks and dependencies** — Focus handling for the preview dialog; WEB-009 accessibility batch.

### WEB-013 The explorer shows "Stored Procedures (0)" for Northwind, which has 7

- **Surface and location** — Web editor › schema panel › Database "Northwind" › Schema "dbo".
- **Environment and prerequisites** — Northwind connected (Windows authentication through the engine service). `sys.procedures` (user procedures) returns 7 (e.g. `CustOrderHist`); functions 0, views 17, tables 13.
- **Steps to reproduce** — 1) Connect to Northwind. 2) Expand Schema "dbo". 3) Read the group counts; click the schema refresh button and read them again. Reproduced 2/2.
- **Expected behavior** — `Stored Procedures (7)`, listing the procedures with the same context menu actions as tables where meaningful.
- **Actual behavior** — `Tables (13)`, `Views (17)` are right; `Stored Procedures (0)`; `Functions (0)` (correct for this database). The 2026-11-03 report saw procedure counts on its test database, so this may depend on the database or on a recent change.
- **User impact and priority** — Procedures cannot be browsed or scripted from the web explorer, and procedure-based completion and Expand EXEC parameters have nothing to work with. Medium / P2.
- **Evidence** — [ex-procs-count.png](AKML-QA-UX-Documentation-Audit-evidence/ex-procs-count.png), [ex-tree.png](AKML-QA-UX-Documentation-Audit-evidence/ex-tree.png), [web-escape-results.json](AKML-QA-UX-Documentation-Audit-evidence/web-escape-results.json) (`EX-PROCS`).
- **Technical investigation** — The engine's catalog query asks for `type IN ('U','V','P','FN','IF','TF','SN','SO')` (`SchemaMetadataService.cs:210`) and the tree maps `DbObjectType.Procedure` to the procedures group (`SchemaTreeComponent.razor:668-690`), so the loss is between the two (type mapping of `SQL_STORED_PROCEDURE`, the web payload, or the engine account's visibility of procedures under Windows authentication as `NT AUTHORITY\SYSTEM`). **Cause not determined.**
- **Proposed fix** — Compare the engine's Phase A payload with `sys.procedures` for this database (log the object-type histogram), then fix the mapping or the visibility check. Add a Northwind-based test that expects 7 procedures.
- **Acceptance criteria** — *Given* Northwind, *when* the explorer loads, *then* Stored Procedures shows 7 and `dbo.CustOrderHist` appears; *given* a database with no procedures, *then* the group shows 0.
- **Regression checks and dependencies** — PostgreSQL "Procedures", Functions group, schema cache counts in Settings › Schema cache, SSMS schema loading.

### WEB-014 The explorer filter has no empty state

- **Surface and location** — Web editor › schema panel › **Filter objects**.
- **Environment and prerequisites** — Northwind connected.
- **Steps to reproduce** — Type `zzzz` in the filter. Reproduced 1/1.
- **Expected behavior** — A message such as "No objects match 'zzzz'" with a clear-filter control.
- **Actual behavior** — The tree collapses to the database node only; nothing says that the filter hid everything.
- **User impact and priority** — Looks like the schema vanished. Low / P3.
- **Evidence** — [ex-filter-nomatch.png](AKML-QA-UX-Documentation-Audit-evidence/ex-filter-nomatch.png).
- **Technical investigation** — `EnumerateKinds` hides empty groups while filtering ("the caller hides them while filtering"). **Confirmed in code comment.**
- **Proposed fix** — Show the empty-state row with **Clear filter**.
- **Acceptance criteria** — *Given* a filter with no match, *then* a "No objects match" row is shown; clearing restores the tree.
- **Regression checks and dependencies** — Filter with a match, keyboard navigation of the tree.

### AI-001 The "No schema" privacy mode builds a prompt that contradicts itself

- **Surface and location** — Web › Settings › AI assistance › "Text-to-SQL privacy mode" = *No schema* (the same builder is used by the other features); AI dock › Text to SQL.
- **Environment and prerequisites** — A local mock provider on `http://127.0.0.1:11434` (an OpenAI-compatible endpoint); the agent was added through Settings; the request bodies were captured by the mock.
- **Steps to reproduce** — 1) Set the privacy mode of Text-to-SQL to *No schema*. 2) Type a request in the editor, open AI › Actions › **Text to SQL**. 3) Read the captured system prompt. Reproduced 1/1 for this feature.
- **Expected behavior** — With no schema shared, the prompt does not mention a schema, or says "No schema is available; use placeholders".
- **Actual behavior** — The system prompt says "The user's database has the following schema:" followed by an empty block, and then "Important rules: Use only tables and columns from the provided schema … Use schema-qualified names (e.g., dbo.TableName)". The model is told to use only a schema that was not provided.
- **User impact and priority** — The privacy choice is honoured (nothing leaks) but the answers will be poor or invented. Medium / P2.
- **Evidence** — [ai-privacy-NoSchema-systemprompt.txt](AKML-QA-UX-Documentation-Audit-evidence/ai-privacy-NoSchema-systemprompt.txt) (646 characters, empty schema block) against [ai-privacy-FullSchema-systemprompt.txt](AKML-QA-UX-Documentation-Audit-evidence/ai-privacy-FullSchema-systemprompt.txt) and [ai-privacy-SchemaNamesOnly-systemprompt.txt](AKML-QA-UX-Documentation-Audit-evidence/ai-privacy-SchemaNamesOnly-systemprompt.txt); [ai-privacy-summary.json](AKML-QA-UX-Documentation-Audit-evidence/ai-privacy-summary.json) compares the four modes (Full schema 3,142 characters with tables and columns; Names only: table and column names without types; No schema: 646; Fully local: same as Full schema but only for a local endpoint).
- **Technical investigation** — The prompt template always contains the schema section and the rules; the schema text is blank for *No schema*. **Confirmed from the captured prompt.**
- **Proposed fix** — Build the schema section conditionally and swap the rule for "No schema is provided; use generic table and column names and say so". Test every feature × mode combination against the captured wire bodies.
- **Acceptance criteria** — *Given* *No schema*, *when* any AI action runs, *then* the captured system prompt has no empty schema section and no "provided schema" rule.
- **Regression checks and dependencies** — Privacy badges on the action buttons, per-feature overrides, the privacy-consent gate.

### AI-002 Fix without a preceding error sends "Error Number: 0 / (no error)"

- **Surface and location** — Web AI dock › Actions › **Fix**.
- **Environment and prerequisites** — Mock provider; text `SELECT * FROM dbo.Customers WHERE Country = 'UK'` (it ran fine, or was never run).
- **Steps to reproduce** — Click **Fix**; read the captured user message. Reproduced 1/1.
- **Expected behavior** — With no error at hand the button is disabled or explains "Run the query first, or paste the error message".
- **Actual behavior** — The request tells the model "The following SQL query failed with an error. Error Number: 0, Error Message: (no error)", so the answer is invented.
- **User impact and priority** — A Fix on a healthy query yields a misleading answer. Low / P3.
- **Evidence** — [ai-mock-captures-run1.json](AKML-QA-UX-Documentation-Audit-evidence/ai-mock-captures-run1.json) (request 3).
- **Technical investigation** — `AiPanel` takes `LastErrorProvider`; null sends the placeholder (`AiPanel.razor`, "sends what Fix always sent when no error was at hand"). **Confirmed.**
- **Proposed fix** — SSMS already handles this well: AKML SQL › AI › Fix SQL with no error shows "AI Fix: No error captured. Run the query first for better results." in the status bar (SSMS sweep M28). Do the same in the browser, or rename the action to "Review" with a prompt that asks for problems, or ask for the error text in a small input.
- **Acceptance criteria** — *Given* no error, *then* the request body never contains "(no error)".
- **Regression checks and dependencies** — PostgreSQL errors with SQLSTATE/hint, retry button.

### SN-001 A custom snippet saved on the Snippets page never expands in the editor

- **Surface and location** — Web › Snippets › **New**, then Editor.
- **Environment and prerequisites** — Fresh test snippet: shortcode `qaz`, title `QA snippet`, body `SELECT TOP 5 * FROM dbo.Customers -- QA`; saved ("Saved 'qaz'."). The form has no database-kind control.
- **Steps to reproduce** — 1) Save the snippet. 2) Open the Editor (reload the page). 3) Type `qaz`, wait 2 s, press Tab (then Enter). Reproduced 2/2 runs. A built-in (`ssf`) expands correctly with the same steps (`SELECT *` ⏎ `FROM `).
- **Expected behavior** — `qaz` shows in the completion list; Tab expands the body.
- **Actual behavior** — No completion item; Tab inserts indentation (`    qaz`), Enter inserts a new line. The snippet is still listed on the Snippets page.
- **User impact and priority** — The page offers to create snippets that cannot be used. Medium / P1.
- **Evidence** — [sn-saved-custom.png](AKML-QA-UX-Documentation-Audit-evidence/sn-saved-custom.png), [sn-qaz-completion.png](AKML-QA-UX-Documentation-Audit-evidence/sn-qaz-completion.png), [web-snippet-expand.json](AKML-QA-UX-Documentation-Audit-evidence/web-snippet-expand.json).
- **Technical investigation** — **Hypothesis:** the editor's snippet source holds the built-in list only (or filters by kind and a custom snippet is saved without the SQL Server kind), or the editor loads snippets once at start. Not traced.
- **Proposed fix** — Feed saved snippets to the editor's snippet completion (and refresh it on save), give the form a database-kind selector, and cover it with a browser test.
- **Acceptance criteria** — *Given* a saved snippet `qaz`, *when* `qaz` + Tab is typed in a SQL Server tab, *then* the body is inserted with `$CURSOR$` honoured; deleting it removes the completion.
- **Regression checks and dependencies** — Built-in snippets, PostgreSQL kind filtering, Import/Export, `$SELECTEDTEXT$` surround snippets.

### UX-002 Import errors: the style import prints parser internals and the snippet import says nothing

- **Surface and location** — Web › Format styles › **Import SQL Prompt style**; Web › Snippets › **Import**.
- **Environment and prerequisites** — A text file containing `nope`.
- **Steps to reproduce** — Import it on each page. Reproduced 1/1 each.
- **Expected behavior** — A plain message: "That file is not a SQL Prompt style (.json)" / "No snippets were found in that file".
- **Actual behavior** — Styles: “_tmp-notastyle.txt” is not a SQL Prompt style (.json): ExpectedNull, nope LineNumber: 0 … (a raw JSON parser message). Snippets: no visible message at all.
- **User impact and priority** — Confusing feedback. Low / P3. (The Settings import, by contrast, answers clearly: "That file is not an AKML SQL web settings export".)
- **Evidence** — [web-styles-results.json](AKML-QA-UX-Documentation-Audit-evidence/web-styles-results.json) (`STY-IMPORT-BAD`), [web-snhist-results.json](AKML-QA-UX-Documentation-Audit-evidence/web-snhist-results.json).
- **Technical investigation** — The style importer surfaces the exception text; the snippet importer swallows the failure. **Confirmed from outputs.**
- **Proposed fix** — Catch the parser exception and show the one-line message; show a count ("Imported 3 snippets") or a "nothing found" notice for snippets.
- **Acceptance criteria** — *Given* a non-style file, *then* no exception text appears; *given* a non-snippet file, *then* a message appears.
- **Regression checks and dependencies** — Valid imports, the shared notification service.

### SSMS-003 Find Invalid Objects reports "0 dependency rows scanned" on a database that has 63

- **Surface and location** — SSMS 22 › AKML SQL › Refactor › **Find Invalid Objects**.
- **Environment and prerequisites** — SSMS 22.12.12405.243, extension `1.26.1104.1510`, `(local)` / Northwind, Windows authentication, a query window connected to Northwind. `sys.sql_expression_dependencies` holds 63 rows in this database (38 from views, 17 from procedures).
- **Steps to reproduce** — 1) Open a query window on Northwind. 2) Menu AKML SQL › Refactor › Find Invalid Objects. 3) Read the message. Reproduced 1/1.
- **Expected behavior** — The scan reads the database's dependencies (63 rows) and reports none invalid, or lists the broken ones.
- **Actual behavior** — The window says "No invalid objects found (0 dependency rows scanned)." above an empty grey grid. A scan that read nothing reports success.
- **User impact and priority** — A user cannot tell a clean database from a scan that did not run. Medium / P2.
- **Evidence** — [ssms-menu-M13.png](AKML-QA-UX-Documentation-Audit-evidence/ssms-menu-M13.png): the result window with "0 dependency rows scanned".
- **Technical investigation** — **Hypothesis:** the dependency query returns no rows for this connection (it may filter by the active schema cache, which also shows no procedures in the web edition, WEB-013), or the engine call fails and is swallowed. Not traced.
- **Proposed fix** — Log the query and its row count; when zero rows come back from a database that has dependencies, say "Could not read dependencies" instead of success; fill or hide the empty grid.
- **Acceptance criteria** — *Given* Northwind, *when* Find Invalid Objects runs, *then* the window reports 63 dependency rows scanned. *Given* a view over a dropped table, *then* it is listed with the missing dependency.
- **Regression checks and dependencies** — Databases without dependencies (message "nothing to scan"), cross-database references, WEB-013 (same schema loader).

### SSMS-004 Format Document turns CRLF line endings into LF

- **Surface and location** — SSMS 22 › AKML SQL › **Format Document** (also Ctrl+K, Y).
- **Environment and prerequisites** — A query window opened from a file saved with CRLF line endings (status bar shows CRLF), active style Khamis Style.
- **Steps to reproduce** — 1) Open the CRLF file. 2) Run Format Document. 3) Read the status bar. Reproduced 1/1; the line-ending field showed CRLF again after Undo.
- **Expected behavior** — The document keeps its line endings.
- **Actual behavior** — The status bar changes from `CRLF` to `LF` after the format, so the file would be saved with different endings (and a document edited by hand afterwards can end up mixed).
- **User impact and priority** — Source-control diffs show every line changed; Windows tools that expect CRLF complain. Low / P3.
- **Evidence** — [ssms-menu-M01.png](AKML-QA-UX-Documentation-Audit-evidence/ssms-menu-M01.png): formatted text, status `LF`, "Formatted with 'Khamis Style'". [ssms-menu-M07.png](AKML-QA-UX-Documentation-Audit-evidence/ssms-menu-M07.png): the same document before, status `CRLF`.
- **Technical investigation** — The formatter returns text with `\n` (the formatter writes LF; see the parity-golden note in CLAUDE.md) and the replacement is applied as is. **Hypothesis, not confirmed in the shell code.**
- **Proposed fix** — Convert the formatted text to the document's dominant line ending before replacing it (or use the editor's newline setting).
- **Acceptance criteria** — *Given* a CRLF document, *when* Format Document runs, *then* the status bar still reads CRLF and no LF remains; the same for an LF document.
- **Regression checks and dependencies** — Format Selection, Unformat, format on paste/delimiter, mixed-ending documents, web Format (browser text uses `\n`).

---

## 4. Tested environments

| Item | Detail |
| --- | --- |
| Machine | Windows Server 2022 Standard (10.0.20348), user `Administrator`, RDP session 2 (no input desktop during the first SSMS attempt; available for the second) |
| SQL Server | Microsoft SQL Server 2022 Enterprise 16.0.1000.6, mixed mode, Windows auth used by the engine (`LocalSystem`) |
| Web | `http://localhost:59416/` (IIS), build `1.26.1104.1510`; engine bridge `127.0.0.1:59417`, service `AkmlSqlWebEngine` `1.26.1104.1510` |
| Browser | Chromium 1255 (Playwright core 1.56) headless, 1600×950, fresh persistent profile in the scratchpad; axe-core 4.10.2 |
| SSMS | SSMS 22.12.12405.243, extension `1.26.1104.1510` under `Release\Common7\IDE\Extensions\AkmlSql` (deployed 2026-11-04 15:10); driven with the UI-automation harness on `(local)` / Northwind, Windows authentication, light theme, 1920×1080 |
| CLI | Installed `AkmlSql.Analyzer.exe` 1.26.1104.1510 vs un-trimmed build of the same source |
| Public site | `https://akml.khamis.work/` — 20 sitemap URLs, Chromium 1440×900 |
| AI test provider | A throwaway Node server on `127.0.0.1:11434` emulating an OpenAI-compatible endpoint (buffered and streamed), logging each request; no real provider or key was used |
| Not available | PostgreSQL server (no PostgreSQL binaries under `C:\Program Files\PostgreSQL`; `scripts/pg-test-server.ps1` needs them); a valid SQL login (creating a scratch login was denied by the environment's safety classifier, so it was not attempted again); Edge/Firefox; dark-theme run |
| Footprint of the audit | No product code changed. No SQL objects created. About 60 queries from the sweeps now sit in the engine's SQL History (SELECT 1, WAITFOR, divide-by-zero and similar, on Northwind). One failed login for the non-existent `qa_nologin_user` (visible in the SQL Server and engine logs). SSMS was launched once on a temp file `%TEMP%\qa-semicolon.sql` (unchanged on disk; SSMS was closed by the harness, edits unsaved). No pairing token minted. Evidence and this report are the only new repository files (untracked). |

## 5. Coverage matrix

| # | Area | Check | Result | Ref |
| --- | --- | --- | --- | --- |
| W01 | Web | App loads, console clean | Pass | |
| W02 | Web | One-click "Use this computer's engine" | Fail | WEB-001 |
| W03 | Web | Manual pairing (Settings › Connections › Add, port 59417) | Pass | |
| W04 | Web | Windows-auth connect and save named connection | Pass | |
| W05 | Web | Windows-auth tab reconnects after browser restart | Pass | |
| W06 | Web | Saved row persists across restart | Pass | |
| W07 | Web | SQL-auth Save with Remember keeps the password | Fail | WEB-002 |
| W08 | Web | Failed SQL login shows the server message | Pass | |
| W09 | Web | Restored SQL-auth tab prompts clearly (saved row linked) | Pass | |
| W10 | Web | Restored tab with no saved row keeps its display name | Fail | WEB-004 |
| W11 | Web | Another address (127.0.0.1) shows saved connections and engine | Fail | WEB-003 |
| W12 | Web | Persistent storage requested | Fail | WEB-003 |
| W13 | Web | Browser eviction of IndexedDB after time | Not Run | WEB-003 |
| W14 | Web | Valid SQL login with Remember survives restart / engine restart | Blocked | no SQL login |
| W15 | Web | PostgreSQL connect / save / remember | Blocked | no server |
| W16 | Web | Execute query, result grid | Pass | |
| W17 | Web | `dbo.` completion and dot-commit | Pass | |
| W18 | Web | Missing semicolon marker presentation | Fail | UX-001 |
| W19 | Web | Adding `;` clears the marker | Pass | |
| W20 | Web | Syntax error feedback from Analyse | Fail | WEB-005 |
| W21 | Web | Parse with a connection reports the server error | Pass | |
| W22 | Web | Parse without a connection | Fail | WEB-005 |
| W23 | Web | axe `/` | Fail | WEB-007, WEB-009 |
| W24 | Web | axe `/snippets` | Pass | `region` only, in WEB-009 |
| W25 | Web | axe `/styles` | Fail | WEB-008, WEB-009 |
| W26 | Web | axe `/history` | Fail | WEB-009 |
| W27 | Web | axe `/settings` | Pass | `region` only, in WEB-009 |
| W28 | Web | axe `/settings/connections` | Fail | WEB-008, WEB-009 |
| W29 | Web | axe dark theme | Not Run | theme did not apply |
| W30 | Web | Keyboard-only and screen-reader walkthrough | Not Run | |
| W31 | Web | Narrow / mobile width | Not Run | |
| S01 | SSMS | Extension deployed, version equals engine and web | Pass | |
| S02 | SSMS | Launch SSMS 22 on Northwind (Windows auth) and drive the UI | Pass | second attempt; first was Blocked (no input desktop) |
| S03 | SSMS | Missing-semicolon rendering (typed text) | Fail | UX-001; opened-file case SSMS-002 |
| S04 | SSMS | Adding `;` clears the squiggle | Pass | |
| S05 | SSMS | Syntax-error feedback | Pass | red squiggles and error count; AKML vs native not separable |
| S06 | SSMS | `dbo.` dot-commit (open item S1 of 2026-11-03) | Pass | `dbo` + `.` kept `dbo.` and listed tables |
| S07 | SSMS | Native vs AKML attribution of the semicolon highlight | Pass | AKML (magenta `OtherError`); native draws nothing |
| S08 | SSMS | `squiggleStyle` / `showAdvisoryHints` honoured (code audit) | Fail | SSMS-001 |
| S09 | SSMS | Saved-credential reuse across query windows | Not Run | |
| S10 | SSMS | Opened file is analysed without an edit | Fail | SSMS-002 |
| C01 | CLI | Installed analyzer detects violations | Fail | CLI-001 |
| C02 | CLI | Un-trimmed analyzer detects violations | Pass | |
| E01 | Engine | Pairing-token store hygiene | Fail | ENG-001 |
| P01 | Site | All 20 sitemap URLs return 200 | Pass | |
| P02 | Site | Title and exactly one `h1` on every page | Pass | |
| P03 | Site | Meaningful meta descriptions | Fail | DOC-003 |
| P04 | Site | Internal links resolve | Fail | DOC-003 |
| P05 | Site | Images load and have `alt` | Pass | |
| P06 | Site | No console errors or failed requests | Pass | |
| P07 | Site | No horizontal scroll at 1440 px | Pass | |
| P08 | Site | No stale product claims (Visual Studio 2026) | Pass | |
| P09 | Site | Connecting doc covers web credentials | Fail | DOC-001 |
| P10 | Site | Troubleshooting covers the reported symptoms | Fail | DOC-002 |
| P11 | Site | Configuration doc matches behaviour | Fail | SSMS-001 |
| P12 | Site | Mobile width layout | Not Run | |
| P13 | Site | Download page installer works | Not Run | |

**Totals (57 checks): Pass 26 · Fail 22 · Blocked 2 · Not Run 7.**


### 5b. Full-surface sweep (second pass, 2026-11-20, about 04:20 to 05:20)

The follow-up request was to test every part of the product. This pass drove the web edition systematically through Chromium (Playwright): every Refactor item, the formatter with all 12 styles on 10 samples (120 runs), every AI action against a local OpenAI-compatible mock provider, every Settings control, 271 Format-styles options, Snippets, History, Explorer, results grid, tabs, editor keys and the connection dialogs. Per-check results are in the JSON files named in the evidence appendix; the sweep scripts were throwaway and are not committed. **SSMS was covered only in part**: the first two attempts failed because the RDP session had no input desktop; the third ran 20 of 46 menu commands (M01 to M20, M28, M29) before a leftover dialog from the sweep itself blocked the menu. The harness is fixed; the rest needs a live desktop.

| # | Area | Check | Result | Ref / note |
| --- | --- | --- | --- | --- |
| R01 | Refactor | Convert old-style joins: preview, Apply, one Ctrl+Z undoes | Pass |  |
| R02 | Refactor | Encapsulate in BEGIN/END | Pass |  |
| R03 | Refactor | Remove semicolons | Pass |  |
| R04 | Refactor | Add GROUP BY columns | Pass |  |
| R05 | Refactor | Unformat | Pass |  |
| R06 | Refactor | Replace deprecated syntax (`!=` to `<>`) | Pass |  |
| R07 | Refactor | Convert sp_executesql | Pass |  |
| R08 | Refactor | Expand INSERT columns | Fail | WEB-011 |
| R09 | Refactor | Expand UPDATE columns | Fail | WEB-011 |
| R10 | Refactor | Expand EXEC parameters | Fail | WEB-011 |
| R11 | Refactor | Smart Rename / Parameterize Values / Extract Procedure (engine) | Fail | WEB-011 |
| R12 | Refactor | Escape closes the Refactor list dialog | Fail | WEB-012 |
| F01 | Format | `basic` select with join, 12 styles | Pass | idempotent, tokens kept |
| F02 | Format | `cte`, 12 styles | Pass |  |
| F03 | Format | `casewhen` / IIF, 12 styles | Pass |  |
| F04 | Format | `dml` insert/update/delete, 12 styles | Pass |  |
| F05 | Format | `merge`, 12 styles | Pass | cosmetic only (FMT-005) |
| F06 | Format | `unicode` text and bracketed names, 12 styles | Pass |  |
| F07 | Format | Comments preserved byte for byte, 12 styles | Fail | FMT-004 (Khamis, 2 of 12) |
| F08 | Format | `GO` / `GO n`, 12 styles | Fail | FMT-003 (12 of 12) |
| F09 | Format | `CREATE PROCEDURE` with control flow, 12 styles | Fail | FMT-001, FMT-002 (12 of 12) |
| F10 | Format | Second Format changes nothing (idempotency), 120 runs | Pass | 120 of 120 |
| F11 | Format | Formatted text keeps every token (case, brackets, semicolons ignored), 108 runs | Pass | 108 of 108 |
| F12 | Format | Unparseable input is left unchanged and the user is told why | Not Run | text unchanged in 12 of 12; message not checked |
| A01 | AI | Dock without an agent shows the empty state with a Settings link | Pass |  |
| A02 | AI | Add agent (Ollama-shaped local mock), Test connection | Pass |  |
| A03 | AI | Saved agent becomes the active agent; row Test works | Pass |  |
| A04 | AI | Explain streams an answer; request carries the SQL; Insert and Discard work | Pass |  |
| A05 | AI | Fix streams an answer | Pass |  |
| A06 | AI | Fix prompt when there was no error | Fail | AI-002 |
| A07 | AI | Optimize, Text to SQL, Index Analysis stream answers | Pass |  |
| A08 | AI | Privacy badge per action; modes Full schema, Names only, Fully local behave as labelled | Pass |  |
| A09 | AI | Privacy mode No schema builds a coherent prompt | Fail | AI-001 |
| A10 | AI | Ghost text appears and Tab accepts it | Pass |  |
| A11 | AI | Chat: send, streamed answer, Shift+Enter newline, Export and Clear buttons present | Pass |  |
| A12 | AI | Ghost text dismissed by Escape; chat starters; New chat / Clear | Not Run | selectors; not verified |
| A13 | AI | Real cloud providers (OpenAI, Anthropic, Gemini, Azure) and API-key storage (encrypted, never plain) | Blocked | no keys; mock only |
| A14 | AI | Failing provider shows an error and Retry; fallback behaviour | Not Run |  |
| T01 | Settings | General: Theme System, Light, Dark, High contrast apply and survive reload | Pass |  |
| T02 | Settings | Editor: font, font size, word wrap persist and take effect; tab size persists | Pass | tab-size effect not checked |
| T03 | Settings | Queries: row limit and timeout persist and reach the toolbar; column types shows in grid headers; retain line breaks persists | Pass | line-break effect not checked |
| T04 | Settings | Code analysis: enable, re-analyse after format, Info / Warning / Error persist | Pass |  |
| T05 | Settings | AI assistance: ghost text, delay, rate limit and 7 per-feature privacy modes persist | Pass |  |
| T06 | Settings | Search settings filters rows | Pass |  |
| T07 | Settings | Export contains no secrets; valid import applies; malformed and foreign files are refused clearly | Pass |  |
| T08 | Settings | Restore defaults (section and all) ask, cancel keeps, confirm resets | Pass |  |
| T09 | Settings | Schema cache: lists Northwind with size, Refresh, Clear all asks first | Pass |  |
| T10 | Settings | Diagnostics: four levels toggle, export downloads (.zip), Clear asks first | Pass |  |
| T11 | Settings | Diagnostics export holds no secrets | Not Run | the file is a .zip; contents not unpacked |
| T12 | Settings | Connections: engine row Remove asks first; Disconnect / Connect; saved rows listed | Pass |  |
| Y01 | Format styles | Khamis Style: 134 option controls change with no preview error | Pass | 11 disabled |
| Y02 | Format styles | Default: 137 option controls change with no preview error | Pass | 8 disabled |
| Y03 | Format styles | Preview tab Whitespace sample renders | Pass | the My SQL tab shows no sample until SQL is pasted |
| Y04 | Format styles | Import of a non-style file gives a plain message | Fail | UX-002 |
| Y05 | Format styles | New / Save / Save as / Export (write engine state) | Not Run | not exercised, to keep the engine's styles unchanged |
| N01 | Snippets | Kind filter, selection, built-ins read-only, refresh | Pass |  |
| N02 | Snippets | Create custom snippet, delete it | Pass |  |
| N03 | Snippets | Built-in `ssf` + Tab expands in the editor | Pass |  |
| N04 | Snippets | Custom snippet expands in the editor | Fail | SN-001 |
| N05 | Snippets | Import of a non-snippet file gives a message | Fail | UX-002 |
| H01 | History | List grouped by date, search (Enter), no-match state, favorite and Favorites only, filters list real values | Pass |  |
| H02 | History | Export CSV, JSON and SQL download | Pass |  |
| H03 | History | Open puts the query in a new tab | Not Run | not verified |
| X01 | Explorer | Schema expands with Tables and Views counts; filter narrows; context menu lists 8 actions | Pass |  |
| X02 | Explorer | Select Top 1000 Rows and Script as SELECT / INSERT / UPDATE / DELETE open a new tab with the script | Pass | 5 of 5 |
| X03 | Explorer | Stored Procedures group lists the procedures | Fail | WEB-013 |
| X04 | Explorer | Filter with no match shows an empty state | Fail | WEB-014 |
| X05 | Explorer | Insert name, Copy name, Pin, Refresh, Close | Not Run | script stopped early |
| E01 | Execute | Two result sets; GO batches with an error keep the others; syntax error with line; selection only; PRINT/RAISERROR; row cap | Pass |  |
| E02 | Execute | Timeout message; Cancel (Query was cancelled by user.); Parse; F5 and Ctrl+Enter | Pass |  |
| E03 | Grid | Copy TSV, Copy with headers, Copy as submenu (CSV, JSON, Markdown, INSERT), Save CSV, View value | Pass |  |
| E04 | Grid | Column sort ascending / descending | Not Run | test selector failed |
| E05 | Tabs | New tab, close prompt for a modified tab, Save as .sql, Open file | Pass |  |
| E06 | Editor | Auto-close, Ctrl+/, Ctrl+F, undo, Tab / Shift+Tab, Ctrl+K Ctrl+L | Pass |  |
| E07 | Analyse | Problems list, Info / Warn / Error filters, click jumps to line | Pass |  |
| E08 | Connect | Test, database refresh, PostgreSQL form, unreachable server message | Pass |  |
| E09 | Keyboard | Escape closes View menu, Connect dialog, schema menu, confirmation dialogs | Pass |  |
| Z01 | SSMS | AKML SQL > Format Document formats the script and says "Formatted with 'Khamis Style'" | Pass |  |
| Z02 | SSMS | Format Document keeps the document's line endings | Fail | SSMS-004 |
| Z03 | SSMS | Unformat Document; Active Style > Edit Styles and Edit Formatting Styles open the Format styles window (built-in styles, options, live preview); Bulk Format opens its dialog | Pass |  |
| Z04 | SSMS | Refactor commands answer with guidance when the cursor is not on an object (Smart Rename, Script as ALTER, Inline EXEC, Convert INSERT to UPDATE); Inline Stored Procedure shows a preview | Pass | a selection was lost for Format Selection and Disable Formatting, so those two were not judged |
| Z05 | SSMS | Find Invalid Objects scans the database's dependencies | Fail | SSMS-003 |
| Z06 | SSMS | Navigate: Go To Definition, Peek, Find All References, Next / Previous Statement, Matching Pair invoked without error | Not Run | outcome not judged: the caret was on a keyword |
| Z07 | SSMS | Object Search and Command Palette open (palette lists commands with shortcuts) | Pass |  |
| Z08 | SSMS | AI > Fix SQL with no captured error says so in the status bar | Pass |  |
| Z09 | SSMS | Remaining menu commands: Document Outline, bookmarks, SQL History, AI Chat, Explain SQL, Execute Current Statement / to Cursor, Snippet Manager, Surround, Manage Code Analysis Rules, Toggle Code Analysis, Export Results Grid, Options, Refresh Schema Cache, Check for Updates, About, Tabs (4), View Logs | Not Run | a stray dialog from the sweep stayed open and blocked the menu; harness fixed, rerun needs a live desktop |
| Z10 | SSMS | Editor toolbar buttons; Options pages; SQL History window; Code analysis rules window; Active Style slots | Not Run |  |
| Z11 | SSMS | Information findings get a blue glyph in the margin | Pass | UX-001 |

**Second-pass totals (88 checks; the 120 formatter runs are grouped into 12 checks): Pass 59 · Fail 17 · Blocked 1 · Not Run 11.**

**Combined with the first matrix (145 checks): Pass 85 · Fail 39 · Blocked 3 · Not Run 18.**

### Suspected issues I could not reproduce — what remains unverified

- **Browser storage eviction (W13).** The browser never requested persistent storage, so eviction is possible, but I did not observe it. To verify: in the user's real Chrome/Edge profile, check `navigator.storage.persisted()` on `http://localhost:59416/`, whether site data is cleared on exit (Settings › Privacy › "Clear on exit"), and whether saved connections are present after a day.
- **Remembered SQL password across time (W14).** No valid SQL login was available, so I could not verify that a password the engine really remembered survives a browser restart or an engine-service restart. WEB-002's reproduction stops at "the engine has no password because Save did not store it". To verify: with a throwaway login (`CREATE LOGIN … ; db_datareader`) run Save and connect with Remember, restart the browser, then restart `AkmlSqlWebEngine`, and reopen the row — expect the placeholder "Remembered on the engine machine".
- **SSMS 22 — what the single run did not cover (S09 and more).** Credential reuse between query windows, the Options and credential dialogs, the disable-rule scopes and lightbulb, whether the glyph margin shows a glyph for an *Information* finding (a yellow glyph appeared for a Warning), which of AKML or native SSMS draws the red squiggles for real syntax errors, multi-line scripts typed with Enter, and format-on-paste. The opened-file result (SSMS-002) is a single observation; repeat it before fixing. `squiggleStyle` / `showAdvisoryHints` (SSMS-001) are confirmed in code only.
- **PostgreSQL (W15)** — connect/save/remember not exercised.
- **Dark theme, mobile width, Edge/Firefox, keyboard-only and screen-reader runs** were not done.
- **SSMS 22, rest of the sweep (Z06, Z09, Z10).** 22 of 46 menu commands ran; the others, the editor toolbar, Options pages, SQL History window and Code analysis rules window were not exercised (a leftover dialog blocked the menu; the RDP session then dropped). It is a ready test at `scratchpad\ssmsprobe\MenuSweep.cs` (throwaway; copy the harness from `tests/AkmlSql.UiTests`); run it with the desktop connected: `dotnet test --filter FullyQualifiedName~MenuSweepTests`. `Help > Send Feedback` is deliberately skipped.
- **AI with real providers.** Only a local OpenAI-compatible mock was used, so provider-specific behaviour (OpenAI and Azure CORS notice, Anthropic and Gemini wire formats, key storage in IndexedDB, fallback order) is unverified. The mock answered Fix with a fixed text, so answer quality is not assessed.
- **Format on unparseable input.** The text was left unchanged in all 12 styles, but whether the user is told why was not checked. The format-on-paste and format-on-delimiter paths are SSMS-only and were not exercised.
- **Controls whose effect was not checked.** Settings: editor Tab size, Retain line breaks on copy or save. Grid: column sort. Explorer: Insert name, Copy name, Pin. History: Open. Chat: starters, New chat, Clear. Diagnostics export contents (a zip).
- **WEB-013 cause.** Only the symptom (0 procedures against 7 in `sys.procedures`) is confirmed.

## 6. Recommended fix order (Spec Kit slices)

1. **Slice 1: ship the CLI analyzer correctly (CLI-001).** Small, independent, silent false-clean in CI. Includes the post-publish smoke test.
2. **Slice 2: first-run engine pairing (WEB-001, ENG-001).** Installer writes the bridge port next to the bundle; clearer failure copy and an Add-engine shortcut; label and revoke paired browsers.
3. **Slice 3: credential persistence and honesty (WEB-002, WEB-004, WEB-003, WEB-010).** Save-and-connect, truthful Remember hint, name prefill, persistent-storage request, origin banner, `EffectivePort` cleanup. Verify with a real SQL login.
4. **Slice 4: formatter correctness (FMT-001, FMT-002, FMT-003, FMT-004, FMT-005).** One corpus-driven slice in `AkmlSql.Formatting` / `tests/format-parity`: sized types in parameter lists, nested `BEGIN`/`END`, `GO n`, block-comment alignment, spacing. Every style and both layouts; goldens and web baselines move together. High reach: SSMS and web share the engine.
5. **Slice 5: quiet advisory markers (SSMS-001, SSMS-002, UX-001).** Plumb `squiggleStyle` / `showAdvisoryHints`, analyse opened files once the connection is ready (repeat the observation first), then marker-only presentation for ST004 in web and SSMS.
6. **Slice 6: syntax feedback (WEB-005).** Parse error becomes an Analyse finding, offline Parse, in-editor red marker, clearer status copy.
7. **Slice 7: web workspace gaps (SN-001, WEB-013, WEB-011, WEB-014, WEB-012, UX-002).** Custom snippets into the editor; procedures in the explorer (find the cause first); schema for the three Refactor items or honest disabling; explorer empty state; Escape on the Refactor dialog; import messages.
8. **Slice 8: AI prompt quality (AI-001, AI-002).** Conditional schema section for *No schema*; Fix without an error. Add a wire-body test per feature and privacy mode.
9. **Slice 9: documentation (DOC-001, DOC-002, DOC-003).** After slices 2 and 3 settle the wording; add docs-pipeline checks for descriptions and links.
10. **Slice 10: accessibility pass (WEB-007, WEB-008, WEB-009).** One slice, verified with axe in all three themes.
11. **Slice 11: SSMS-to-browser credential sharing (WEB-006).** Own spec and security review; last.

---

## 7. Other evidence files (supporting, not tied to one finding)

All files are in [AKML-QA-UX-Documentation-Audit-evidence/](AKML-QA-UX-Documentation-Audit-evidence/).

| File | What it shows |
| --- | --- |
| [web-001-first-load.png](AKML-QA-UX-Documentation-Audit-evidence/web-001-first-load.png) | Empty profile, editor offline, "No engine paired yet" |
| [web-002-paired.png](AKML-QA-UX-Documentation-Audit-evidence/web-002-paired.png), [web-004-engine-failed-state.png](AKML-QA-UX-Documentation-Audit-evidence/web-004-engine-failed-state.png), [web-005-settings-connections.png](AKML-QA-UX-Documentation-Audit-evidence/web-005-settings-connections.png) | WEB-001: the failed one-click pairing and Settings › Connections with no engine |
| [web-003-connect-dialog-initial.png](AKML-QA-UX-Documentation-Audit-evidence/web-003-connect-dialog-initial.png) | Connect dialog with no engine (database list and PostgreSQL disabled with an explanation) |
| [web-008-add-engine-filled.png](AKML-QA-UX-Documentation-Audit-evidence/web-008-add-engine-filled.png) | Add-engine form filled with `localhost` / `59417` |
| [web-011-connect-dialog-win-auth.png](AKML-QA-UX-Documentation-Audit-evidence/web-011-connect-dialog-win-auth.png), [web-012-after-save.png](AKML-QA-UX-Documentation-Audit-evidence/web-012-after-save.png), [web-013-connected.png](AKML-QA-UX-Documentation-Audit-evidence/web-013-connected.png) | Windows-auth connect, "Saved." and the connected state (W04) |
| [web-015-dialog-after-restart.png](AKML-QA-UX-Documentation-Audit-evidence/web-015-dialog-after-restart.png) | After a browser restart the saved Windows row is still listed (W06) |
| [web-017-sql-auth-after-save-only.png](AKML-QA-UX-Documentation-Audit-evidence/web-017-sql-auth-after-save-only.png), [web-018-sql-auth-reopened.png](AKML-QA-UX-Documentation-Audit-evidence/web-018-sql-auth-reopened.png) | WEB-002: the form right after Save-only and after reopening the dialog |
| [web-023-restored-sqlauth-tab.png](AKML-QA-UX-Documentation-Audit-evidence/web-023-restored-sqlauth-tab.png), [web-026-restored-unsaved-binding.png](AKML-QA-UX-Documentation-Audit-evidence/web-026-restored-unsaved-binding.png) | Restored tabs (linked and unlinked binding) before F5 |
| [web-028-execute-ok.png](AKML-QA-UX-Documentation-Audit-evidence/web-028-execute-ok.png), [web-029-dbo-dot-completion.png](AKML-QA-UX-Documentation-Audit-evidence/web-029-dbo-dot-completion.png) | W16 query result and W17 `dbo.` completion (typing `dbo` lists "dbo Schema" first; `.` commits `dbo.` and lists tables) |
| [web-sem-6-gutter-tooltip.png](AKML-QA-UX-Documentation-Audit-evidence/web-sem-6-gutter-tooltip.png) | UX-001: hover on the blue gutter dot |
| [ssms-sem-B-with-semicolon-editor.png](AKML-QA-UX-Documentation-Audit-evidence/ssms-sem-B-with-semicolon-editor.png), [ssms-probe-log.txt](AKML-QA-UX-Documentation-Audit-evidence/ssms-probe-log.txt), [ssms-dbo-E2-after-dot.png](AKML-QA-UX-Documentation-Audit-evidence/ssms-dbo-E2-after-dot.png) | Editor close-up with `;` added (clean), the SSMS probe log (editor text per step) and S06: after `dbo` + `.` the text is `select * from dbo.` and the list shows the schema's tables (30 of 30 objects) |
| [web-control-inventory.json](AKML-QA-UX-Documentation-Audit-evidence/web-control-inventory.json), `inv-settings-*.png`, `inv-menu-*.png` | Inventory of every control on each Settings section and the toolbar menus |
| [web-refactor-results.json](AKML-QA-UX-Documentation-Audit-evidence/web-refactor-results.json), [web-refactor-results-2.json](AKML-QA-UX-Documentation-Audit-evidence/web-refactor-results-2.json), `rf-*.png` | Refactor sweep (first run used inputs that do not apply; the second run uses the right ones) |
| [web-format-outputs.jsonl](AKML-QA-UX-Documentation-Audit-evidence/web-format-outputs.jsonl), [web-format-results.json](AKML-QA-UX-Documentation-Audit-evidence/web-format-results.json) | All 120 formatter outputs (12 styles × 10 samples) with idempotency and token checks |
| [web-ai-results-2.json](AKML-QA-UX-Documentation-Audit-evidence/web-ai-results-2.json), [web-ai-results-3.json](AKML-QA-UX-Documentation-Audit-evidence/web-ai-results-3.json), [web-ghost-results.json](AKML-QA-UX-Documentation-Audit-evidence/web-ghost-results.json), `ai-*.png`, `ai-privacy-*` | AI sweep: actions, wire bodies captured by the mock, privacy-mode system prompts, ghost text, chat |
| [web-settings-results.json](AKML-QA-UX-Documentation-Audit-evidence/web-settings-results.json), [web-settings-results-2.json](AKML-QA-UX-Documentation-Audit-evidence/web-settings-results-2.json), [web-settings-export.json](AKML-QA-UX-Documentation-Audit-evidence/web-settings-export.json), `st-*.png`, `sx-*.png` | Settings persistence, export (no secrets), import, restore, schema cache, diagnostics |
| [web-styles-results.json](AKML-QA-UX-Documentation-Audit-evidence/web-styles-results.json), `sty-preview.png`, [web-snhist-results.json](AKML-QA-UX-Documentation-Audit-evidence/web-snhist-results.json), [web-snippet-expand.json](AKML-QA-UX-Documentation-Audit-evidence/web-snippet-expand.json), `sn-*.png`, `hi-row-selected.png`, `pg-*.png` | Format styles options, Snippets and History pages |
| [web-editor3-results.json](AKML-QA-UX-Documentation-Audit-evidence/web-editor3-results.json), [web-editor3b-results.json](AKML-QA-UX-Documentation-Audit-evidence/web-editor3b-results.json), [web-final1-results.json](AKML-QA-UX-Documentation-Audit-evidence/web-final1-results.json), [web-escape-results.json](AKML-QA-UX-Documentation-Audit-evidence/web-escape-results.json), `ex-*.png`, `gr-*.png`, `tb-close-prompt.png`, `an-problems.png`, `cd-*.png`, `cn-saved.png` | Execution, grid, tabs, editor keys, Analyse filters, connection dialogs, Escape handling, Explorer. The first editor3 run holds selector errors that editor3b and final1 repeat. |
| `site-01_.png` … `site-20_*.png` (e.g. [site-02_features.png](AKML-QA-UX-Documentation-Audit-evidence/site-02_features.png), [site-03_download.png](AKML-QA-UX-Documentation-Audit-evidence/site-03_download.png)) | Top-of-page capture of each of the 20 sitemap URLs at 1440×900 |
