# Research: QA Audit Remediation (spec 047)

Phase 0 of the plan. One decision per issue group. Each records what the code shows (**Cause**, confirmed or not), the
**Decision**, the **Rationale** and the **Alternatives considered**. Issue ids are the audit report's
(`reports/AKML-QA-UX-Documentation-Audit.md`). File references are to `master` at 0745268f.

Status of causes after this research: **27 confirmed in code or by a measurement**, 5 still hypotheses to settle
with a failing test first (FMT-003, FMT-004, FMT-005, UX-002 snippet half, WEB-008 token names). None is unresolved
in a way that blocks planning.

---

## R1 — CLI-001: the trimmed analyzer finds no rules

**Cause (confirmed by A/B build, code read).** `RuleRegistry` (src/AkmlSql.Analysis/RuleRegistry.cs:15-27) finds rules
with `Assembly.GetExecutingAssembly().GetTypes()` + `Activator.CreateInstance`. No rule class is referenced statically,
so `PublishTrimmed=true` in `src/AkmlSql.Analyzer/AkmlSql.Analyzer.csproj` removes them. The same commit built
untrimmed reports PE002/PE003/PE004 and ST004 (evidence `cli-analyzer-trimmed-vs-untrimmed.txt`). A second trim
hazard sits on the same path: `BatchFileAnalyzer.LoadFromExplicitPath` (line 118) and `CaSettingsLoader` deserialize
`CaSettings` with reflection-based System.Text.Json, which a trimmed app disables by default
(`JsonSerializerIsReflectionEnabledByDefault=false`). So `--settings` and the upward `.casettings` search would fail,
or silently use defaults, even after the rules came back. The engine is not trimmed (CLAUDE.md), so the editors are
unaffected. Only the analyzer CLI ships: `AkmlSqlSetup.iss:217`; the formatter CLI is not installed.

**Decision.** Keep trimming and root what reflection needs. In the analyzer project:
`<TrimmerRootAssembly Include="AkmlSql.Analysis" />` and `<TrimmerRootAssembly Include="AkmlSql.Core" />`, and
`<JsonSerializerIsReflectionEnabledByDefault>true</JsonSerializerIsReflectionEnabledByDefault>`. Add a **release
gate** to both `build.ps1` and `doc/Deploy-Build-Release.ps1`, right after "Analyzer CLI (publish)". It runs the
published exe on committed fixtures under `tests/fixtures/cli/`:

- `multi.sql` must report PE002, PE003 and PE004, and `--check` must exit 1.
- `sem1.sql --rules ST004` must report one information finding.
- `multi.sql` with a fixture `.casettings` that turns PE004 off must leave PE004 out.

Any other result fails the build. The same three checks also run as a permanent test,
`tests/AkmlSql.E2E.Tests/PublishedAnalyzerTests.cs` (`Category=PublishedCli`), which publishes Release to a temp
folder.

**Rationale.** It is the smallest change that keeps the exe at about 20 MB. Rooting two assemblies is explicit and
reviewable, and the smoke gate catches any future trim regression whatever its cause.

**Alternatives considered.**

- **Turn trimming off.** That makes a self-contained single-file exe carrying the engine's dependency graph
  (SqlClient, Npgsql, libpg_query), several times the size, for no user benefit.
- **A source-generated rule list.** It is correct, but it adds a generator and a new mechanism (Principle V) where
  one MSBuild item suffices.
- **`DynamicDependency` attributes on `RuleRegistry`.** These must list every rule type and go stale silently when a
  rule is added.
- **Source-generated STJ contexts for `CaSettings`.** This is cleaner for STJ, but it needs two contexts (CLI and
  Analysis library) and touches netstandard2.0 Core. The feature switch plus rooting is one line and the gate proves
  it.

## R2 — WEB-001: "Use this computer's engine" on an IIS install

**Cause (confirmed).** `PairLocalEngineAsync` (src/AkmlSql.Web/Services/IEngineAutoConnect.cs:329-381) tries
`EngineEndpoint.DefaultPort` (47291) unless `PageEngine` found the engine that served the page. `PageEngine` detects
only a page served by the engine itself, through `HEAD /` and `X-AKML-Host: engine` (standalone hosting, spec 045).
An IIS page is static, so nothing tells the browser that the installer chose bridge port 59417. The installer does
know the port (`WebBridgePort`, registry `BridgePort`).

**Decision.** The installer publishes the engine address next to the bundle. A new guarded support helper,
`src/AkmlSql.Installer/web-engine-manifest.ps1 -WebRoot <dir> -Port <n> -Scheme ws|wss`, writes
`{app}\Web\akml-engine.json` as `{"bridgePort":59417,"scheme":"ws"}` (contract in `contracts/engine-pairing.md`).

- **Who calls it:** `Web_PostInstall` calls it when the host is IIS or None (the engine service runs for every host
  choice), and so do `web-hosting-repair.ps1` and `web-iis-repair.ps1` whenever they change ports.
- **Service hosting:** the helper is not called there, because the engine serves the page and `HEAD /` already
  identifies it.
- **Page side:** `PageEngine.GetAsync` gains a second step. When `HEAD /` says static, it fetches `akml-engine.json`
  (same origin, no-cache) through a new `readManifest` in `js/akml-page-engine.js`. A valid file yields
  `PageEngineKind.Engine` with Host = the page's host, Port = `bridgePort`, Scheme = `scheme`.
- **What follows:** auto-connect and the one-click button both use it unchanged. A loopback page pairs with no PIN;
  a LAN page gets the PIN prompt with the address pre-filled, as Service + LAN does today.
- **Failure:** `PairLocalEngineAsync` returns the address it tried. The status bar shows "No engine answered on
  {host}:{port}. This install may use another port." with **Add engine…**, which opens Settings › Connections with
  the Add form pre-filled. "Handshake failed" stays only for a real handshake rejection.

**Rationale.** The installer is the only party that knows the port. A same-origin static file needs no CSP change,
no IIS configuration, no proxy and no new engine endpoint. It also makes a first IIS run zero-click, as Service
hosting already is.

**Alternatives considered.**

- **Probing a list of ports from the page.** This is guesswork, slow (one WebSocket timeout per port), and noisy in
  the console.
- **An IIS response header set in web.config.** It needs a web.config edit per port change, and the static bundle's
  web.config is replaced on upgrade.
- **Reverse-proxying the bridge through IIS (ARR).** That is a new dependency, a new failure mode, and a WebSocket
  proxy to secure.
- **Blazor `appsettings.json`.** The installer would have to write a file the boot manifest does not list, which is
  fragile with fingerprinting.

## R3 — ENG-001: identifiable, revocable pairings

**Cause (confirmed).** The web sends the fixed `BrowserLabel = "Web edition"` (IEngineBridge.cs:292). The engine's
`BearerTokenStore` already has `List()` and `RevokeByHash()` (Pairing/BearerTokenStore.cs:93-135), but no IPC
exposes them, so 166 identical rows accumulate and cannot be told apart. Tokens are minted only for PIN pairing (LAN
mode, HandshakeHandler.cs:107-139); loopback connections hold none.

**Decision.** The owner's clarification applies: no automatic removal, only labels and per-pairing Revoke.

1. **Label.** The web sends `BrowserLabel = "{Browser} on {OS} · {origin}"`, for example
   "Edge on Windows · https://sql-host:443", truncated to 80 characters. The browser family and OS come from
   `navigator.userAgentData`, else the UA string, through a small pure helper in `js/akml-bridge.js`. Stored labels
   from before keep their text.
2. **List and revoke.** Two append-only IPC pairs: `PairingListRequest`/`Response` (227/228) and
   `PairingRevokeRequest`/`Response` (229/230), advertised as capability `pairing.manage.v1`.
   - **List** returns every entry: id = token hash, label, minted, last used, expires, and `IsCurrent` for the
     calling connection's own pairing (`BridgeConnection` records the validated hash).
   - **Revoke** removes one entry and closes the bridge sockets that authenticated with it.
   - **Who may call them:** any authenticated bridge connection. A paired browser already holds the full power of the
     engine account, so revoking another pairing adds no privilege.
   - **Where tokens don't exist:** an engine that does not require pairing (loopback-only) answers with an empty list
     and `PairingUsed = false`.
3. **Web UI.** Settings › Connections shows a **Paired browsers** list under the active engine row (this browser
   marked, created and last used, expired ones shown as such). Each row has a **Revoke…** with a confirmation;
   revoking this browser's own pairing warns that the PIN will be needed again.

**Rationale.** The store already has the operations, so this exposes them with labels that mean something. Closing
the revoked sockets makes Revoke take effect at once, which the security reading of "revoke" requires.

**Alternatives considered.**

- **Pruning never-used tokens.** The owner rejected it (spec Clarifications Q5).
- **Revoking only from the installer's repair helper.** That has no UI and needs an admin prompt.
- **Letting a browser revoke only its own pairing.** That does not solve 166 stale rows.

## R4 — WEB-002: Save with "Remember password" tests the login first

**Cause (confirmed).** `SaveAsync` (src/AkmlSql.Web/Shared/ConnectionManagerModal.razor:1441-1492) writes the row
with `RememberPassword` and never calls the engine. The engine keeps a SQL Server password only inside a successful
test or connect: `TestSqlConnectionHandler` line 86 calls
`SqlServerCredentialResolver.RememberIfRequested(... request.RememberPassword)`. The web passes `rememberPassword`
only from `ConnectSqlServerAsync` (ISqlConnectionService.cs:899-905); the public `TestAsync(ConnectionTarget)` never
sets it.

**Decision.** No IPC change (`TestSqlConnectionRequest.RememberPassword`, key 3, and `Response.Remembered`, key 4,
exist). The connection service gains `TestAndRememberAsync(ConnectionTarget)`, which calls the private
`TestSqlServerAsync(..., rememberPassword: true)` and returns `ConnectResult.Remembered`.

- **When it runs.** `SaveAsync` calls it when the form is a SQL login with Remember ticked, a password typed, and an
  engine that remembers SQL Server passwords (`EngineRemembersSqlServerPasswords`).
- **On success.** The row is saved with `RememberPassword = Remembered`, and the message says "Saved. The engine
  machine keeps the password for {login}."
- **On failure.** The row is still saved (name, server, login), but with `RememberPassword = false`. The message reads
  "Saved without the password: the login test failed — {server error}." or, with no engine,
  "…: no engine is connected."
- **Nothing typed.** With no password typed, nothing changes from today: a remembered password stays, and the row
  keeps its flag only if the engine already holds one (`RefreshRememberedAsync`).
- **Unticking.** Unticking Remember on a row that had it calls the existing Forget path
  (`CredentialRequest` / `Forget`), so the engine drops it.
- **PostgreSQL.** Its Save is unchanged in this feature: its test does not remember, and that would need an engine
  change. It is recorded as a follow-up in `doc/progress.md`.

**Rationale.** It is what the owner chose (Clarifications Q1). The proof of a correct password is the same server
round trip Test already makes, and a wrong password is never kept.

**Alternatives considered.**

- **Store at Save without a test.** The owner rejected this, because it keeps wrong passwords.
- **Explain-only.** The owner rejected this too.
- **A new "save credential" IPC.** It would duplicate an existing path.

## R5 — WEB-004: blank Display name on reconnect

**Cause (confirmed).** `ApplyPrefill` (ConnectionManagerModal.razor:932-975) calls `NewConnection(b.Kind)`, which
clears `_name`, and never derives a name when no saved row matches.

**Decision.** After `NewConnection`, set `_name` to the same default `SaveAsync` uses: `"{server} / {database}"`
(PostgreSQL: `"{host} / {database}"`). A small note under the field reads "Not a saved connection yet".

**Rationale.** It uses one default and shows no blank field. Save then creates a row under exactly that name.

**Alternatives considered.** Placeholder text only. Rejected, because Save would still write the computed default
the user never saw.

## R6 — WEB-003: per-origin, best-effort browser storage

**Cause (confirmed for origin and for the missing persistence request; eviction itself not reproduced).** The only
`navigator.storage` use is `estimate()` (wwwroot/js/akml-indexeddb.js:172-178), and it has no C# caller. Nothing calls
`persist()`. IndexedDB is per origin, so `127.0.0.1` sees none of `localhost`'s data. The installer advertises
`http://localhost:{port}/` for Service + Localhost and for IIS (`HostingAddress`, web-hosting.iss:45-59), so
`localhost` is the canonical loopback address.

**Decision.** Following the owner's clarification (Q3, Option A):

1. **Persistence.** A new `IBrowserStorageState` service, backed by two functions in `akml-indexeddb.js`
   (`persist()`, `persisted()`), is called once from the saved-connection store's and the engine-connection store's
   Add paths. Settings › Diagnostics gains a row `diagnostics-storage`: "Browser storage: kept until you clear it
   (persistent)" or "…: the browser may clear it when space runs low (best effort)", with a **Ask again** button. A
   refusal is shown there, never as a repeated banner.
2. **Origin banner.** One dismissible banner on the editor page shows when all three hold:
   - this address has no saved SQL connection and no engine record;
   - the page host is a loopback alias other than `localhost` (`127.0.0.1`, `[::1]`);
   - `PageEngine` knows an engine for this page (R2).

   It reads: "Saved connections and pairings belong to the address you opened. If you used
   http://localhost:{port}/ before, open it there, or import your settings here." (scheme and port are the current
   page's). Dismissal is remembered per origin in `localStorage`, a per-viewer convenience. Any other origin gets no
   banner, because the right address cannot be known there.
3. **No engine-held list.** Saved connections stay in the browser (Clarifications Q3).

**Rationale.** It addresses both observed causes (eviction risk and the wrong address) without changing where data
lives.

**Alternatives considered.**

- **Redirect `127.0.0.1` to `localhost`.** It would break a user who works on `127.0.0.1` on purpose, and the
  owner did not choose it.
- **An engine-held list.** Deferred to a later spec, together with WEB-006.

## R7 — WEB-010: `EffectivePort` persisted on SQL Server rows

**Cause (confirmed).** `SavedSqlConnection.EffectivePort => Port ?? DefaultPostgresPort`
(ISavedSqlConnectionStore.cs:101) is a computed property that System.Text.Json serializes.

**Decision.** Mark it `[JsonIgnore]`. Old rows that carry the field load as before, because unknown members are
ignored on read.

**Alternatives considered.** Removing the property. Rejected, because the PostgreSQL form uses it.

## R8 — FMT-001: a sized parameter type split across two lines

**Cause (confirmed by code read).** `ParenthesisRules.ApplyProcedureParameters`
(src/AkmlSql.Formatting/Rules/ParenthesisRules.cs:426-480) takes **the first `(` after `PROCEDURE`** as the
parameter list's opening parenthesis. T-SQL parameter lists are usually written without parentheses
(`create procedure dbo.p @a int, @b varchar(10)='x' as`), so that `(` is `varchar`'s. The rule then puts "the first
parameter after open paren on a new line", which yields `varchar(` and then `10) = 'x'`. All 8 `.akmlstyle`
built-ins and the web's two POCO styles use this classic layout, which matches the 12-of-12 observation.

**Decision.** Treat the parenthesis as the parameter list only when it directly follows the procedure name: the token
after the one- to three-part name, skipping whitespace and comments. Otherwise the parameters are unparenthesized:

- the "each parameter on a new line" break applies to top-level commas between `PROCEDURE name` and `AS`;
- a parenthesis inside a parameter (a type size such as `(10)`, `(max)` or `(10, 2)`) is never a list.

New corpus cases `tests/format-parity/corpus/sp047-01-proc-params.sql` cover parameters without parentheses, with
parentheses, with `(max)`, with `decimal(10, 2)` and a numeric default, with a `NULL` default and with OUTPUT (T-SQL
parameter defaults are constants, so no function-call default).

**Rationale.** It is a one-function fix at the confirmed cause, and the corpus case runs in every style.

**Alternatives considered.** Annotating data-type parentheses in `AstAnnotator`. That is broader than needed for the
confirmed cause, and is kept as a fallback if the SQL Prompt layout shows the same symptom (R10).

## R9 — FMT-002: `BEGIN`/`END` of IF/ELSE/WHILE at column 0

**Cause (confirmed by code read).** `ControlFlowRules.ApplyBeginEndRules` (Rules/ControlFlowRules.cs:234-284) puts
`BEGIN` on a new line (`BeginOnNewLine`) but leaves its `IndentLevel` untouched. `LayoutEngine` gave that token
`indentLevel = 0` (Pipeline/LayoutEngine.cs:184-187: "No break = no indentation needed"). `ApplyIndentBetweenBeginEnd`
(line 286-337) then pushes that 0 as the block's level and aligns the matching `END` to it. The body gets
`max(level, 0 + 1)`, which is why it is indented while `BEGIN`/`END` sit at column 0.

**Decision.** When a rule moves `BEGIN` onto its own line, give it the indent level of the statement that owns it:
the `IF`/`ELSE`/`WHILE` keyword's level, or one deeper than the enclosing `BEGIN` when it is a bare block.
`ApplyIndentBetweenBeginEnd` must raise a nested `BEGIN` to at least the enclosing block's level + 1, as it already
does for other content. Styles whose documented control-flow option indents `BEGIN` relative to `IF` keep that option
(`ControlFlowOptions`). Corpus case `sp047-02-control-flow.sql` covers IF, ELSE, ELSE IF, WHILE and nested
`TRY…CATCH`.

**Rationale.** It fixes the confirmed cause and keeps each style's option meaning (spec 039).

## R10 — FMT-003: the statement after `GO n` is indented

**Cause (hypothesis, strong).** ScriptDom does not parse `GO 3` (LayoutEngine.cs:45-47 says so), so the batch after
it is missing from `BuildBatchStartSet` and `BuildStatementStartSet`. The token after the count gets a line break
(`afterGo && !goCount`, line 170-175), but `tracker.Reset()` / `baseIndent = 0` (line 133-140) never run for it. The
previous statement's clause context (the select list of `SELECT 2`) carries over, and `decision.IndentDelta` indents
`PRINT`. Casing still applies, because casing is token-based, which matches the observed `    PRINT 'x'`.

**Decision.** Treat the first semantic token after `GO` or `GO n` as a batch start (reset the clause tracker,
`baseIndent` and the indent tracker), whether or not the tree lists that batch. Check `SqlPromptPrinter.cs:86-102`
(the same "GO 5" path in the SQL Prompt layout) and fix it the same way if the corpus case fails there. Corpus case
`sp047-03-go-count.sql` covers `GO`, `GO 3`, `GO 3 -- comment`, and `GO` at the end of the script.

**Rationale.** It fixes the separator path for both layouts.

## R11 — FMT-004 and FMT-005: comment text and spacing

**Cause (hypotheses).**

- **FMT-004.** "Align multi-line comments" (Khamis Style) trims each inner line instead of shifting the block,
  which changes the comment's relative indentation.
- **FMT-005.** The keyword-column alignment pads for a keyword that is not on the line:
  - `INNER JOIN` gets three spaces;
  - `SET    nocount ON` gets `SET` padded to `SELECT`'s width (the same cause, seen in the audit's procedure output);
  - the two-item `IN (10, 11)` list exceeds the list-collapse threshold.

  The seams are `Layout/RightAligner.cs`, `Layout/AlignmentCalculator.cs`, `Layout/CollapseEvaluator.cs` and
  `Rules/ListRules.cs`.

**Decision.** Start each with a failing corpus case (`sp047-04-block-comment.sql`, `sp047-05-spacing.sql`), then fix
at the seam the failing case points to:

- **Comments:** shift every line of an aligned block by the same offset.
- **Alignment padding:** pad only to keywords present in the same aligned group.
- **Short lists:** a list whose collapsed form fits the line stays collapsed unless the style forces one item per
  line.
- **By-design differences:** if the investigation shows one is by design, for example ANSI-compact keyword columns,
  record it in that style's description instead of changing it.

**Rationale.** These are cosmetic and low priority; a failing case first keeps the fix honest.

## R12 — SSMS-004: Format Document turns CRLF into LF

**Cause (confirmed mechanism).** `FormatActionHelper.ApplyFormattedText`
(src/AkmlSql.Shell.Shared/Formatting/FormatActionHelper.cs) replaces the whole buffer with the engine's text verbatim
(18 call sites). The formatter emits `\n`.

**Decision.** Fix it once in the shared pipeline. `FormatterPipeline` ends with a "keep the input's line endings"
step: when the input's dominant line ending is CRLF, every `\n` in the output becomes `\r\n`. LF and mixed-LF inputs
are unchanged. It runs after validation and the idempotency check, so goldens (which normalize to LF) are
unaffected. This also fixes Format Selection, Unformat, format on paste or delimiter, Bulk Format and the formatter
CLI. The web (CodeMirror, LF) is unaffected.

**Rationale.** There is one place and every host benefits. It is still formatting work, so the shell stays thin
(Principle I).

**Alternatives considered.** Converting in `ApplyFormattedText` (shell only). Rejected, because it leaves Bulk Format
(it writes files) and the CLI rewriting endings.

## R13 — UX-001 and SSMS-001: advisory findings as margin markers

**Cause (confirmed).**

- **SSMS.** `DiagnosticTagger.SeverityToErrorType` (src/AkmlSql.Shell.Shared/Analysis/DiagnosticTagger.cs:94-103)
  emits an `IErrorTag` for every severity: Information becomes `OtherError` (magenta squiggle) and Hint becomes
  `HintedSuggestion`. `WarningGlyph` already draws a per-line dot coloured by severity, blue for Information
  (observed in the SSMS sweep).
- **Web.** `toLintDiagnostics` (wwwroot/js/akml-editor.js:1777-1805) turns every finding into a CodeMirror lint range;
  `lintGutter()` adds the dot.
- **Settings.** `codeAnalysis.squiggleStyle` (documented values `underline`, `dotted`, `solid`) and
  `showAdvisoryHints` are never read; the only references are their definitions (AppSettings.cs:757-774). The code's
  own documentation of `showAdvisoryHints` (spec 014 US17 / FR-079) is: "show blue lightbulbs for advisory-only rules
  (no auto-fix). When false, only auto-fixable rules (orange) get a lightbulb."
- **Existing configs.** Every config carries `"squiggleStyle": "underline"`: the POCO default is serialized on every
  save.

**Decision.** Following the owner's clarification (Q2, Option B), contract in `contracts/analysis-display.md`:

1. **`squiggleStyle`.** It takes `"marker"` (new default) and `"underline"`.
   - **`marker`:** Information and Hint findings get no text decoration, only the margin marker. Warning and Error
     findings keep their underlines.
   - **`underline`:** every finding is underlined, as before.
   - **Old values:** `dotted` and `solid` never worked and are read as `underline`.
   - **Migration:** `ConfigManager` gains a one-time `MigrateSquiggleStyle`, with the marker
     `codeAnalysis.squiggleStyleVersion`, following `MigrateTriggerDelay`. A config without the marker whose value is
     `"underline"` (the old, never-read default) becomes `"marker"`. Any other value is kept as the user's choice.
2. **SSMS rendering.** With `marker`, `DiagnosticTagger` yields no tag for severity ≤ 1. `WarningGlyph` keeps its dot
   and the Error List keeps its entries. The Code analysis Options page gains one row, **Advisory findings: Margin
   marker only / Underline**.
3. **Web rendering.** A new web preference `AdvisoryDisplay` (`marker` default, `underline`) in the analysis settings
   record, plus a row `analysis-advisory-display` in Settings › Code analysis that is included in export, import and
   reset. With `marker`, `toLintDiagnostics` sets `markClass: 'akml-lint-marker-only'` on Hint and Info diagnostics.
   An editor theme rule removes the underline and background for that class, so the gutter dot and its tooltip stay.
4. **`showAdvisoryHints`.** It is wired to its documented meaning (plan-time amendment A1 to the spec): when false,
   the SSMS lightbulb offers no fix actions for findings that have no automatic fix. Their marker, Error List entry
   and glyph-menu suppress actions stay.
5. **Docs.** `doc/configuration.md` and `doc/analysis-rules.md` list the real values, and `showAdvisoryHints` is
   documented.

**Rationale.** It is the owner's choice, it reuses the existing glyph and gutter, and one setting with a migration
gives the new default without overriding anyone's explicit choice.

**Alternatives considered.**

- **A new setting name.** It would leave `squiggleStyle` documented but dead, which is the very SSMS-001 defect.
- **`PredefinedErrorTypeNames.Suggestion` dots under the text.** That still decorates the text, so it fails FR-018.
- **Giving `showAdvisoryHints` the audit's "hide hint markers" meaning.** It contradicts its documented contract and
  duplicates what the marker default now does.

## R14 — SSMS-002: an opened file is not analysed until edited

**Cause (confirmed by code read).** `AnalysisController`'s constructor schedules an initial pass
(Analysis/AnalysisController.cs:44-46), and `RunAnalysisAsync` returns silently when the engine is not connected yet
(line 117-118: "No-ops if the engine isn't connected yet; the first edit re-triggers"). A file opened when SSMS
starts arrives before the engine connects, so nothing analyses it until an edit.

**Decision.** When a pass finds the engine not connected, `RunAnalysisAsync` logs one Debug line ("analysis deferred:
engine not connected") and waits once through `ConnectionWiringHelper.WhenEngineReadyAsync`, the existing 10 s
poller, then calls `ScheduleAnalysis()`. A pass is also re-run after `SendConnectionChangedAsync` succeeds for a
tracked view, so schema-aware rules see the connection; this goes through `TriggerReanalysis` on the buffer's
controller. It is covered by a shell test with a fake client that connects late.

**Rationale.** It uses existing primitives (`WhenEngineReadyAsync`, `TriggerReanalysis`). The fix happens where the
silent skip happens.

## R15 — WEB-005: syntax errors in Analyse and Parse

**Cause (confirmed by code read).**

- **Analyse.** `AnalysisEngine.AnalyzeAsync` (src/AkmlSql.Analysis/AnalysisEngine.cs:101-111) returns no issues when
  the parse fails, and drops `errors` when a partial tree exists. The web always analyses T-SQL in the browser
  (`AnalyserService` wraps the same engine).
- **Parse.** The web's Parse (`Editor.razor:1297-1321`) needs a connection (`CanParse`). It shows errors in Messages
  only; an editor mark appears only when the user clicks a message (`OnNavigateToLineAsync`).

**Decision.**

1. `AnalysisEngine.AnalyzeAsync` gains an optional `reportParseErrors` (default false). When true, each ScriptDom
   parse error becomes an Error finding with RuleId `SYNTAX`, the parser's message, and its line and column. The web's
   `AnalyserService` passes true. The SSMS engine path keeps false, because SSMS draws its own syntax squiggles and
   duplicates would follow. The CLI keeps false (unchanged behaviour, out of scope).
2. **Parse with no connection** runs in the browser (`TsqlParserService`, already in the WASM bundle). The Messages
   pane shows "Checked in the browser; the server was not asked." plus the errors, and `CanParse` no longer requires
   a connection.
3. **Parse with a connection** runs on the server, as today. Either way the first error with a line becomes the tab's
   `ErrorMark` (red, as today's click-to-jump mark), and the status says "Parse failed: {n} syntax error(s)." instead
   of "Query completed with errors.".
4. With a syntax error present, Problems never says "No problems found.".

**Rationale.** One shared change (opt-in) plus web wiring. SSMS is unaffected by design.

## R16 — SN-001: custom snippets never expand

**Cause (confirmed by code read).** Completion snippet items come only from the shared
`SnippetProvider.BuiltInSnippets` (six fixed entries, src/AkmlSql.IntelliSense/Completion/Providers/SnippetProvider.cs).
Online, the web asks the engine; offline (`BuildFromCacheAsync`), there are no snippets at all. In the editor, a
snippet expands only by accepting its completion item (akml-editor.js:744-754). A snippet saved on the Snippets page
lives in IndexedDB (`ISnippetStore`) and never reaches completion.

**Decision.** `CompletionService` takes an optional `ISnippetStore` and appends the user's snippets of the active
dialect (`ListAsync(kind)`, non-built-in) as snippet items. Each item carries DisplayText = shortcode, InsertText =
body lines joined with `\n`, ObjectType = Snippet, SecondaryText = title and SortPriority = 500.

- **Online:** they are appended wherever the engine's response already contains snippet items, that is, where the
  engine judged a snippet allowed.
- **Offline:** they are appended at a statement start, with the same text check the offline path uses.

The store's list is cached, and the cache is invalidated by Save, Delete and Import. A browser test covers save,
expand and delete.

**Rationale.** It touches the web only, the engine IPC is untouched, and the existing expansion path (`$CURSOR$` and
`$Name$` stops) is reused.

**Alternatives considered.** Pushing web snippets to the engine (`SnippetsWrite`). That works only with that
capability and online, while the browser store is the source of truth (spec 027).

## R17 — WEB-013: the explorer shows "Stored Procedures (0)" for Northwind

**Cause (confirmed by measurement, 2026-11-21).** The web engine service signs in to SQL Server as
`NT AUTHORITY\SYSTEM`, which is not sysadmin here. `EXECUTE AS LOGIN = 'NT AUTHORITY\SYSTEM'` against Northwind
returns U 13, V 17, P 0, against U 13, V 17, P 7 for the signed-in user. Tables and views are visible through public
grants, while procedures need EXECUTE or VIEW DEFINITION. The engine query, the payload and the tree mapping are all
correct.

**Decision.** This is SQL Server's metadata visibility, so the product cannot list objects the account may not see.
It must stop presenting "(0)" as fact (plan-time amendment A2 to the spec):

- **Engine.** Phase A also reads `HAS_PERMS_BY_NAME(DB_NAME(), 'DATABASE', 'VIEW DEFINITION')` on the same
  connection, in one scalar query (CLAUDE.md schema conventions). The result is stored on `DatabaseCache`.
- **Payload.** It travels as `SchemaPhasePayload.MetadataLimited` (key 8, append-only; absent from older engines,
  which reads as false).
- **Explorer.** When the flag is true, Stored Procedures and Functions show "May be incomplete" instead of a
  confident count, with a note: "The engine signs in as {RunsAs}, which cannot see every object in {database}. Grant
  it VIEW DEFINITION on the database, or connect with a login that can." `RunsAs` comes from the handshake.
- **Docs.** The troubleshooting page gets the same entry.

**Rationale.** The measurement settles the cause. A notice is the honest fix, and the 7 appear as soon as the
account can see them.

**Alternatives considered.**

- **Run the engine service as the user.** That widens privileges; it is the WEB-006 territory and out of scope.
- **Hide empty groups.** That hides the problem instead of explaining it.

## R18 — WEB-011: the three Expand refactorings in the browser

**Cause (confirmed).** `RefactoringService.RunLightweight` (src/AkmlSql.Web/Services/IRefactoringService.cs:161-175)
builds a `RefactoringContext` without `SchemaCache`. `ExpandInsertColumnsOperation` (and the UPDATE and EXEC variants)
answer "Could not resolve columns for …" when it is null (line 91-100). The heavy items' gate text is
"Requires a paired engine advertising 'refactoring.heavy'", and no engine advertises that capability.

**Decision.** Build a `DatabaseCache` in the browser from the active tab's cached schema snapshot (Phase B:
columns and parameters; Phase A gives objects only). This is a new pure converter, `SchemaSnapshotCache.ToDatabaseCache(SchemaPhasePayload)`
in `src/AkmlSql.Web/Services/`, and it is passed as `SchemaCache` for these three kinds.

- **No snapshot (never connected) or Phase A only:** the three items show disabled, with "Needs the database
  schema — connect to a database first" or "Still loading the column list", visible before the click.
- **Operation warnings:** they are shown in the preview, instead of a generic "no change".
- **Heavy items:** the tooltip becomes "Not available in the browser yet." when the engine is connected but lacks
  the capability.

**Rationale.** It runs in the browser and offline from the last snapshot, as completion's offline path already does.
The IPC is unchanged.

**Alternatives considered.** Routing the three operations through the engine. That needs heavy-refactoring IPC for
web sessions (not wired; Capabilities.cs:131), which is much larger.

## R19 — WEB-014, WEB-012, UX-002: explorer empty state, Escape, import messages

**Cause.**

- **WEB-014 (confirmed).** `EnumerateKinds` returns empty groups and "the caller hides them while filtering"
  (SchemaTreeComponent.razor:660-705), so a filter with no match leaves nothing.
- **WEB-012 (confirmed).** The Refactor list (Editor.razor:368-407) and its preview are custom scrim dialogs of
  `<li @onclick>` items, with no key handling and no focus management. `DialogHost` and `CellValueDialog` use
  `akml-ui.js` `stashFocus` / `trapTab` / `restoreFocus`.
- **UX-002 style half (confirmed).** The style import surfaces `ex.Message` (Styles.razor:726-790).
- **UX-002 snippet half (unsettled).** The snippet import (Snippets.razor:443-479) does set `_status` messages, but
  the audit saw nothing for a non-snippet file. Either the message is not rendered after the async file read (no
  `StateHasChanged`) or the status line is outside the visible area. A bUnit test settles which.

**Decision.**

- **WEB-014:** while a filter is active and no group has a match, show one row, "No objects match '{filter}'", with
  **Clear filter**.
- **WEB-012:** the Refactor list becomes buttons in a list, so each item is keyboard-operable. The list and its
  preview adopt the shared pattern: Escape closes, focus is trapped, and focus returns to the Refactor button.
- **UX-002:**
  - The style import maps parser and JSON exceptions to "That file is not a SQL Prompt style (.json)." and logs the
    detail to diagnostics.
  - The snippet import always shows a result line ("Imported 'qaz'." / "No snippet was found in that file.") and
    calls `StateHasChanged`.

## R20 — SSMS-003: "0 dependency rows scanned"

**Cause (confirmed by code read).** `FindInvalidObjectsHandler.ReadDependencyRowsAsync`
(src/AkmlSql.Engine/Handlers/Refactoring/FindInvalidObjectsHandler.cs:135-150) selects only rows with
`d.referenced_id IS NULL`, the unresolved candidates, and `TotalScanned = rows.Count`. On Northwind every reference
resolves, so the scan works but reports "0 dependency rows scanned"
(src/AkmlSql.Shell.Shared/Refactoring/FindInvalidObjectsDialog.cs:55).

**Decision.**

- **Engine.** The same command also returns the total checked, `SELECT COUNT(*)` over the same join without the
  `referenced_id IS NULL` filter, in one batch, and `TotalScanned` carries that number. Its meaning is documented as
  "references checked"; the shell and engine ship together, so there is no version skew.
- **Dialog.** It says "No invalid objects found. All {n} references resolve." and hides the empty grid. With
  `n = 0` it says "This database has no object references to check.".
- **Permission errors** stay as today (Status 1).

**Rationale.** The data is correct and only the count was wrong. The fix is one query and one sentence.

## R21 — AI-001 and AI-002: prompt coherence

**Cause (confirmed).**

- **AI-001.** Every builder goes through `PromptTemplates.BuildSystemPrompt` (src/AkmlSql.AI/Prompts/PromptTemplates.cs:23-70),
  which always emits "The user's database has the following schema:" and "Use only tables and columns from the
  provided schema". With privacy mode *No schema* the schema text is empty. `IndexAnalysisPrompt` and
  `OptimizePrompt` instructions also say "provided in the schema context".
- **AI-002.** The web `AiPanel` sends Fix with "Error Number: 0 / (no error)" when `LastErrorProvider` returns null
  (AiPanel.razor:128; FixPrompt.cs:68). SSMS already refuses with "AI Fix: No error captured. Run the query first for
  better results."

**Decision.**

- **AI-001.** `BuildSystemPrompt` (both dialect variants) emits the schema section and the provided-schema rule only
  when the schema text is not blank. Otherwise it emits "No database schema is shared (privacy mode). Use
  placeholder table and column names, and say that you did." The index and optimize instructions get a matching
  no-schema line. Tests cover every feature × privacy mode in `tests/AkmlSql.AI.Tests`.
- **AI-002.** In the web, Fix is disabled while no error is known for the tab, with the tooltip "Run the query
  first: Fix needs the error the server returned." This mirrors SSMS, and no request carries a no-error placeholder.

**Rationale.** There is one shared place for AI-001, so it fixes SSMS too. AI-002 matches the host that already
behaves well.

## R22 — DOC-001, DOC-002, DOC-003: documentation

**Cause (confirmed).**

- **DOC-001, DOC-002.** These are content gaps in `doc/topics/connecting.md`, `troubleshooting.md` and
  `browser-setup.md`.
- **DOC-003, summary.** `MarkdownRenderer.ExtractSummary` takes the first paragraph, which in `doc/configuration.md`
  is "The configuration file is stored at:".
- **DOC-003, dead link.** `deployment.md#web-edition` (configuration.md:62) points at a file the site does not
  publish, and links to unpublished files "stay untouched".
- **Search.** It indexes title, all headings, and only the first 4,000 characters of the body
  (DocsContentService.cs:26, 219-232). `browser-setup.md` is already 4,965 bytes.

**Decision.**

1. **`connecting.md`** gets a section "Connecting in the browser" covering the dialog, Windows authentication as the
   engine's account, what Save and Remember do (after R4), where passwords live, that web and SSMS passwords are
   separate (WEB-006 answer), and that saved connections belong to the address you opened.
2. **`troubleshooting.md`** gets a "Web edition" section whose four entries are **headings** named after the
   symptoms ("Handshake failed", "No engine paired yet", "My saved connections are gone", "It asks for my password
   again"), plus the WEB-013 visibility entry. Headings are indexed in full, so search finds them whatever the body
   length.
3. **`configuration.md`** opens with a descriptive sentence, and its deployment link points at a published page.
4. **Pipeline checks.** `tests/AkmlSql.Site.Tests/Docs` gains two tests over every published document: the summary
   is at least 50 characters, and no relative link points at an unpublished file. A third test checks that each
   symptom phrase occurs in the troubleshooting page's headings.
5. **Screenshots** are retaken after Stories 2 and 3 through the existing `DocsScreenshotsTests` flow.

**Rationale.** It is content-first, adds no renderer change, and the new tests keep it from regressing.

## R23 — WEB-007, WEB-008, WEB-009: accessibility

**Cause (confirmed by axe; component seams located).**

- **WEB-007.** The editor content has no accessible name.
- **WEB-008, Active badge.** `.akml-connections-badge--active` is `--akml-status-success` with white text, 3.29:1
  (ConnectionPickerComponent.razor:190).
- **WEB-008, muted badge.** `.akml-styles-badge.muted` uses `opacity: 0.8` on secondary text, 4.26:1 on a selected
  row (Styles.razor:265-267).
- **WEB-009.** A close control nested inside the interactive tab (`TabStrip` with `Closable`), title-only selects on
  History, an unfocusable scrollable `<pre>` on /styles, an empty `<th>` on connections, no `<h1>` per route, the
  heading order on /styles, and chrome outside landmarks.

**Decision.**

- **WEB-007.** `EditorView.contentAttributes` adds `aria-label="SQL query editor — {tab name}"`,
  `aria-multiline="true"` and `role="textbox"`. The existing attributes unit test is extended.
- **WEB-008.**
  - The Active badge uses the semantic success badge pair (web `Badge` / `StatusText` tokens, spec 041) that
    reaches 4.5:1, from `docs/theme-tokens.json`. If no such pair exists, a token pair is added there and generated
    (never hand-edited CSS; Principle II).
  - The muted badge drops `opacity` and uses the secondary-text token.
- **WEB-009.**
  - The tab close button becomes a sibling of the tab element, labelled "Close {name}". The `doc-tab` and
    `doc-tab-close` test ids and the keys (Delete, middle click, Ctrl+Alt+W) stay.
  - The History selects get visually hidden `<label>`s.
  - The preview `<pre>` gets `tabindex="0"` and a label.
  - The empty `<th>` gets visually hidden "Actions".
  - Each route gets a visually hidden `<h1>`, and the /styles heading order is fixed.
  - `MainLayout` wraps chrome in `header`/`nav`/`main`.
- **Gate.** A new `tests/AkmlSql.Web.E2E.Tests/AxeAccessibilityTests.cs` runs axe (wcag2a, wcag2aa, best-practice)
  on `/`, `/snippets`, `/styles`, `/history`, `/settings` and `/settings/connections` in light, dark and high
  contrast, and asserts zero violations. It uses the test-only package `Deque.AxeCore.Playwright` (see plan
  Complexity Tracking).

**Rationale.** These are component-local fixes, with an automated gate so they stay fixed.

**Alternatives considered.** Vendoring `axe.min.js` into the test project. Rejected: a pinned NuGet gives typed
results and one version source, and both are test-only (MPL-2.0, not shipped).

## R24 — FR-038: where each permanent test lives

**Decision.** Every issue has at least one permanent test that fails before its fix and passes after it. The mapping
is in `quickstart.md` § Tests. They use the suites the repository already has:

- Core.Tests: config migration, line endings.
- Formatting.Tests with `tests/format-parity` corpus cases `sp047-*`, run for every built-in style.
- Analysis.Tests: the parse-error finding.
- AI.Tests: prompts.
- Engine.Tests: pairing IPC, Phase A flag, invalid-objects count, formatter line endings through the handler.
- Shell.Shared.Tests: tagger, migration, deferred analysis, dialog text.
- Web.Tests (bUnit and node): connections, page engine, snippets, refactor, explorer, settings, editor attributes.
- Web.E2E.Tests (Playwright, serial, Debug build): snippets in a real browser, Parse markers, axe.
- Site.Tests: docs checks.
- Installer.Tests: the manifest helper.
- E2E.Tests: the published CLI.

Tests that need a live SQL Server, a desktop session or SSMS use the suites' existing opt-in categories
(`WithSqlOrSkipAsync`, `InstallerSmoke`, the FlaUI UiTests). The audit's evidence and script folders are git-ignored,
so every sample input a test needs is committed under the test project, never read from `reports/`.
