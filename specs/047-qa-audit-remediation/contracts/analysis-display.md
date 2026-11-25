# Contract: analysis display, syntax feedback and open-file analysis (UX-001, SSMS-001, SSMS-002, WEB-005)

## 1. Presentation by severity

| Severity | `marker` (default) | `underline` |
| --- | --- | --- |
| Error 3 (rules and `SYNTAX`) | underline + margin dot | underline + margin dot |
| Warning 2 | underline + margin dot | underline + margin dot |
| Information 1 | **margin dot only** | underline + margin dot |
| Hint 0 | **margin dot only** | underline + margin dot |

"Margin dot only" means that no character of the SQL text changes colour, background, underline or decoration. The
dot's tooltip lists every finding on the line, with its message and rule id. Problems / the Error List list every
finding in both modes.

## 2. SSMS

- **`DiagnosticTagger.GetTags`.** When `ConfigManager`'s `codeAnalysis.squiggleStyle` equals `"marker"`, it yields no
  `IErrorTag` for issues with `Severity <= 1`. Otherwise it yields as today. It reads the setting through the
  controller's 2 s settings cache, not per tag.
- **`WarningGlyph`:** unchanged. It already draws one severity-coloured dot per line.
- **`ErrorListReporter`:** unchanged.
- **`LightbulbProvider`.** When `showAdvisoryHints` is false, it returns no suggested-action set for an issue that has
  no automatic fix. Suppress and disable stay available from the glyph menu.
- **Options › Code analysis** gains a row in the "Analysis engine" group:
  - label "Advisory findings", a combo with "Margin marker only" (`marker`) and "Underline in the text"
    (`underline`), registered for search;
  - help text: "Information and hint findings, such as a missing semicolon. Warnings and errors are always
    underlined.";
  - `CodeAnalysisControls.Load` and `Save` read and write `SquiggleStyle`.
- **Migration** `ConfigManager.MigrateSquiggleStyle(AppSettings)`, called next to `MigrateTriggerDelay` on both load
  paths: `if (ca.SquiggleStyleVersion != null) return; if (ca.SquiggleStyle == "underline") ca.SquiggleStyle = "marker"; ca.SquiggleStyleVersion = 1;`.
  It runs in memory and is persisted by the next save. A failure is logged and leaves the settings as read.
- **Docs.** `doc/configuration.md` and `doc/analysis-rules.md` list `squiggleStyle` as `marker` (default) |
  `underline`, and document `showAdvisoryHints`.

## 3. Web

- **Setting.** `AnalysisSettings.AdvisoryDisplay` (`"marker"` default | `"underline"`) gets a Settings › Code analysis
  row `analysis-advisory-display`: label "Advisory findings", options "Margin marker only" / "Underline in the text",
  and the same help text as SSMS. It is included in the section's export, import and Restore defaults.
- **`toLintDiagnostics(doc, diags, options)`.** A new third argument `{ advisoryMarkersOnly: boolean }`. When true,
  diagnostics with severity `hint` or `info` get `markClass: 'akml-lint-marker-only'`. The editor's theme extension
  adds `.cm-lintRange.akml-lint-marker-only { background: none; text-decoration: none; }`, overriding CodeMirror's
  wavy background image, so the gutter marker (`lintGutter`) and its tooltip remain. `EditorComponent` passes the
  option from the setting and re-applies diagnostics when it changes.
- **Node tests** extend `akml-editor.diagnostics.test.mjs`: marker-only mapping per severity, with the class present
  only for hint and info.

## 4. Syntax errors (web)

- **Shared library.** `AnalysisEngine.AnalyzeAsync(request, serverVersion, schemaCache, settings, ct, reportParseErrors = false)`.
  When true, every `ParseError` becomes an issue: RuleId `SYNTAX`, Severity 3, Message = the parse error's message,
  and Line, Column = the error's, with EndColumn the end of the offending token when it is known. This happens whether
  or not a partial tree exists. The issues are added before rule findings and are not cached per batch.
- **`AnalyserService`.** The web passes `reportParseErrors: true`. The SSMS engine handler and the CLI keep `false`.
- **Problems:**
  - an Error row for each `SYNTAX` issue, listed first;
  - the empty state "No problems found." is never shown while a `SYNTAX` issue exists;
  - with code analysis disabled, nothing is analysed (spec 041 R50), so no `SYNTAX` issues appear either.
- **Parse:**
  - **Enabled** whenever the tab is T-SQL and not running; a connection is no longer required.
  - **Connected, engine has `execute.v2`:** the server checks it, as today.
  - **Otherwise:** the browser parses it with `TsqlParserService.Parse`. The Messages pane gets one info line, "Checked
    in the browser; the server was not asked.", followed by one error line per parse error, in the server's
    format: `Msg 102, Level 15, Line n: Incorrect syntax near '*'.`.
  - **Either way:** the first error with a line becomes `tab.ErrorMark`, a red mark on that line or column (the
    existing click-to-jump mark), set without a click.
  - **Status segment:** "Parse failed: 1 syntax error." / "Parse failed: {n} syntax errors." / "Parse succeeded.".
    The Run status keeps "Query completed with errors.".
  - **Tooltip:** "Parse (Ctrl+F5) — checks the syntax without running it; on the server when connected, else in the
    browser".

## 5. Open-file analysis (SSMS-002)

- **`AnalysisController.RunAnalysisAsync`.** When `client == null || !client.IsConnected`, it logs Debug
  "Analysis deferred for session {Session}: engine not connected". If no deferral is pending, it registers one
  `ConnectionWiringHelper.WhenEngineReadyAsync(EngineReady, () => { ScheduleAnalysis(); return Task.CompletedTask; })`
  and returns. A 10 s timeout logs a Warning, as `SendFullDocumentWhenReady` does.
- **`ConnectionWiringHelper.SendConnectionChangedAsync`.** After a successful send, it calls `TriggerReanalysis()` on
  the `AnalysisController` stored in the tracked view's buffer properties, if one exists.
- **Acceptance (shell test).** A controller created while a fake client is disconnected raises `DiagnosticsUpdated`
  once the client connects, without any buffer edit.
