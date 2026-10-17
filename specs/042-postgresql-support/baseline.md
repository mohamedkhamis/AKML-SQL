# Measured results and manual checks (spec 042)

This file records what was measured or checked by hand while the feature was built, one section per user story.
The automated comparison with the pre-feature test state is a separate record: [baseline/README.md](baseline/README.md)
holds the counts and the known reds, and `baseline/after/` holds the pass-lists of the latest comparison.

Environment for the browser runs: Windows 11, the repository's Debug builds of the web app and the engine, Chromium through
Playwright, a private PostgreSQL 18 cluster on port 55432 (`scripts/pg-test-server.ps1`), a local SQL Server on port 1433.

## User story 4 - switching, History, AI, remembered passwords (2026-10-14)

### SC-002 - twenty switches, each under three seconds

`PostgresWorkspaceTests.Twenty_switches_between_SQL_Server_and_PostgreSQL_each_follow_within_three_seconds_and_keep_the_text`
connects to SQL Server (Windows authentication, `tempdb`) and to the PostgreSQL cluster ten times each, alternating. The clock
runs from the click on Connect until the status bar names the new kind. The editor text is set before the first switch and checked
after the last.

| Switch | 1 | 2 | 3 | 4 | 5 | 6 | 7 | 8 | 9 | 10 |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| To SQL Server (ms) | 84 | 76 | 75 | 58 | 59 | 65 | 74 | 113 | 74 | 50 |
| To PostgreSQL (ms) | 83 | 150 | 136 | 127 | 159 | 170 | 371 | 280 | 147 | 122 |

Longest: 371 ms. Limit: 3,000 ms. The text was unchanged after twenty switches. The time to fill the form is not part of a switch.

### SC-018 - a remembered password connects without a prompt

`PostgresWorkspaceTests.A_remembered_password_connects_without_a_prompt_and_is_nowhere_it_should_not_be` creates a login role with
a password built from two halves on the server (so the script in the editor never holds it whole), saves a connection with
Remember password on, reloads the page, and connects from the saved row with an empty password field.

- Ten connects of ten asked for nothing.
- The password is not in any IndexedDB store, not in the engine's log files, and not in a settings export.
- Settings › Connections › Forget password removes the engine's copy; the next connect says the server asked for a password.
- The role is dropped at the end.

### SC-011 - AI prompts on a PostgreSQL connection

Automated, done: every feature's prompt names PostgreSQL and the server version, forbids T-SQL, and carries nothing of SQL Server
(`PromptDialectTests`, `AiPromptDialectTests`, `AiChatPanelDialectTests`, `GhostTextDialectTests`); the SQL Server prompts equal
files captured before the change (`tests/AkmlSql.AI.Tests/Fixtures`); a browser run checks the text the model actually receives
(`Text_to_SQL_on_PostgreSQL_asks_the_model_for_PostgreSQL`).

Manual, **not done**: the criterion asks for ten scripted prompts per feature answered by a real model and the answers read by a
person to confirm they are PostgreSQL. That needs a provider key and a reviewer; nobody has run it. What is known so far is only
what the model is told, not what it answers.

### Privacy of the script (FR-081)

`A_cloud_provider_gets_the_script_without_its_literals_and_the_answer_has_them_back` routes the provider's address in the browser:
the request holds `'__STR_0__'` and `__NUM_0__`, not `'UK'` and `30`, and the answer shown has the values back.
Chat is not redacted: it is free text, not a script. The server's DETAIL and HINT of an error are sent to the provider as the
requirement (FR-082) states; a DETAIL can quote a data value (a duplicate key shows the key), exactly as the SQL Server error text
the desktop edition already sends.

### Spike S7 - where the service stores a remembered password

Recorded in [research.md](research.md) under R14: the service runs as LocalSystem and the store is in that account's own
`%AppData%\AKML SQL` (the SYSTEM profile), not beside the `--config` file. Read from an elevated shell on 2026-10-15: the folder is open to
`SYSTEM` and `Administrators` only (full control, inherited), the same as the `config.json` beside it and the bridge file under `ProgramData`,
so the consent text ("any administrator of the engine machine") is accurate. The file itself did not exist yet; nothing was read from it.

### Not covered by a test

- The History restore advice is covered by component tests with a fake connection; the browser run covers the badge, the filter and
  the `kind:` term, not the notification.
- The "disabled in config" state of Diagnostics cannot be told from "older engine" by the browser: both are an engine that does not
  advertise the capability. The text names both causes.

## User story 5 - nothing leaks, nothing pretends (2026-10-14)

### SC-004 - the two dialects share nothing by accident

- **Dictionaries.** 223 names are in both the PostgreSQL reference and the T-SQL dictionaries. Each is in
  `tests/AkmlSql.PostgreSql.Tests/Isolation/shared-sql-names.json` with a reason: 195 are standard SQL with one meaning, 28 are the
  same word with a different meaning or form in each dialect (for example `ISNULL`, `CONVERT`, `FORMAT`, `TIMESTAMP`, `EXECUTE`),
  each described. A new shared name fails the test until a person writes it down; a stale entry fails it too.
- **PostgreSQL probe.** All 379 cases of the PostgreSQL completion corpus return no `GO`, `@`, `#`, `[`, `sp_`, `xp_` or `NOCOUNT`
  item and nothing only T-SQL has. One word needed a decision: `hash`, an index access method after `USING`, is PostgreSQL's by
  right and is listed as such.
- **SQL Server probe.** All 1,376 cases of the SQL Server corpus return no key word or function only PostgreSQL has. Four words
  the T-SQL providers offer from their own lists (`ASC`, `DESC`, `BY`, `OFFSET`) are real T-SQL and are listed with their reasons.
- **T-SQL dictionaries.** Unchanged from the snapshot taken before the feature (`DictionarySnapshotTests`).

### SC-010 - the T-SQL implementation never sees PostgreSQL text

`NoTsqlOnPostgresSessionTests` counts every call into the T-SQL parser (a process-wide counter in `TsqlParserService`) while a
PostgreSQL session asks for completion, hover, signature help, wildcard expansion, statement boundaries, formatting, analysis,
the safety check and execution. The same requests on a SQL Server session move the count, so it is a working instrument.

It found two real leaks, both fixed in this story:
- code analysis of a PostgreSQL session ran the T-SQL parser and the T-SQL rules on PostgreSQL text (it now answers "no findings"
  until the PostgreSQL rules are routed in);
- formatting a PostgreSQL session ran the T-SQL formatter (it now refuses with "Not available for PostgreSQL" and returns the text
  untouched until PostgreSQL formatting is routed in). The formatter parses with ScriptDom directly, which the counter cannot see, so
  the refusal itself is asserted.

In the browser, the Refactor button is disabled with the tooltip "Not available for PostgreSQL" and `data-unavailable="postgresql"`,
and the palette row is disabled with the same tooltip and never runs (`PostgresWorkspaceTests`).

### FR-091 - SQL Server behaviour and SSMS

- The commits of this spec change no file in `src/AkmlSql.Ssms22`, `src/AkmlSql.Shell.Shared` or `tests/AkmlSql.Shell.Shared.Tests`
  (`git diff --stat 92388c7..HEAD` on those paths is empty). The only changes in Core are appended message keys and the PostgreSQL
  helpers.
- SSMS check (task T129): **half done, the "after" half is waiting for one approval.** The check is an observation, not a judgement:
  `tests/AkmlSql.UiTests/Check042.cs` starts the installed SSMS 22 on a Northwind script (own app-data folder, so the owner's history and
  settings are untouched) and writes down the menus, the Error List, what completion commits, what Format Document makes of the script,
  what SQL History lists after an execution, every window and prompt, and the warnings and errors in the logs; `logsummary.sh` adds the stable
  log facts (analysis finding counts, history records sent). It runs only when `AKML_CHECK042_LABEL` is set, so routine runs never touch the desktop.
  **Before** (2026-10-15, the installed engine 1.26.1013.1822, which has no PostgreSQL library) is recorded in
  `baseline/acceptance/t129/`: the AKML SQL menu has its 12 entries and the submenus of `AkmlMenuTable`, no dialog or prompt appeared,
  completion committed `dbo.Orders`, Format Document produced the layout in the file, the analyser reported 11, 11 and 3 findings, one history record was sent,
  no warning or error except the normal "Parent process exited" line at shutdown.
  **After** needs the engine built from this branch (1.26.1015.0825, with `AkmlSql.PostgreSql.dll`) in `C:\Program Files (x86)\AKML SQL\Engine`,
  the folder the unchanged shell starts it from. Putting it there is a write into the installed product that also stops the web engine service for a moment, so it
  was not done without the owner's approval (the automated attempt was declined). With it in place: run
  `AKML_CHECK042_LABEL=after AKML_CHECK042_OUT=<folder> dotnet test tests/AkmlSql.UiTests --filter FullyQualifiedName~Check042`, run
  `logsummary.sh` on the sandbox, and compare with `observations-before.txt` and `logsummary-before.txt`: a line that differs is the finding
  (the engine version line differs by design).

### Theme tokens (task T131)

`scripts/generate-theme-css.ps1 -CheckOnly` reports `OK` for light, dark and high contrast, and no hex or rgb colour was added to
any component or page in this story or the one before it.

## User story 6 - edit PostgreSQL results in place (2026-10-14)

### SC-014 - thirty edits across ten column types land exactly

`PgApplyRunnerTests.Thirty_edits_inserts_and_deletes_across_ten_column_types_land_exactly` applies 15 updates, 10 inserts and 5
deletes in one Apply to a table of ten column types (serial, text, numeric(10,2), date, text[], jsonb, boolean, integer, uuid,
timestamptz). The same changes are written as plain statements to a twin table, and the two tables must have the same checksum of
all their rows; they do. Every value was typed the way PostgreSQL prints it and converted by the server.

Also against the live server: the written row comes back as stored (`5` typed, `5.00` returned; a trigger's upper-casing; the
sequence's next value and the column default for an insert), composite and quoted keys, a key that changed or vanished (the
failing edit and the rest named, nothing kept), NOT NULL, bad text, unique and check violations with the server's own words, a
failure in the middle of 3 edits, a key-less update refused, a type name that tries to end the cast refused, the user's open
transaction joined and left open (and a failure undoing only the apply), an aborted transaction refused with the advice to roll back.

### FR-130 - which results can be edited

`PgResultSetProvenanceTests` (22 cases): editable for a single-table SELECT with its whole primary key, also with `ORDER BY` and
`LIMIT`, with an alias, beside an expression, for a composite key, quoted mixed-case names in another schema, a partitioned table
and a table with generated and identity columns. Read-only, each with its reason: a join, an aggregate, a constant, a view, a
materialized view, a table with no key, part of a composite key (the missing column is named), a column twice. Nothing is collected
when the client did not ask, or for a statement that is not a SELECT, WITH, TABLE or VALUES.

One finding changed the design: the driver's own schema query does not describe partitioned or foreign tables, so the engine reads
the names, key and defaults from the catalog by table OID and attribute number instead (research R11, spike S6).

### Browser

`PostgresWorkspaceTests`, three scenarios against a live server: edit, Set to NULL (Ctrl+0), insert and delete in one Apply, with
the grid showing `5.00` and the generated key and the table read back by a new query; a join, a view, an aggregate and key-less and
partial-key tables each read-only with the reason beside the grid; bad text and a row deleted after the grid read it, each reported in
the server's words with nothing else written. The pending-edit confirmation still stops a run until the edits are discarded.

### What the grid does not do on PostgreSQL

It does not run the script again after an Apply (FR-133): the write returns the stored rows. The `QueryRefreshSafety` rule is
unchanged for SQL Server.

## User story 7 - format PostgreSQL scripts, Parse without the server (2026-10-14)

### SC-015 - the format corpus

`tests/pg-format-corpus`: 151 scripts (SELECT forms, joins, subqueries and CTEs, data-changing statements, definitions, functions with dollar
bodies, utility statements, comments, regression cases) formatted with the 8 built-in styles, 1,208 pairs. Each pair is accepted (the script is valid,
so the formatter must format it), parses to the same statements as the original (the tree compared with every location ignored), formats to
itself, keeps every quoted name, string and dollar-quoted body as written, and equals its golden. All 1,208 hold.

Two kinds of defect were found by the corpus and the style probe and fixed before the goldens were kept:
- layout: a parenthesis that never broke because a forced-break flag sat outside its group; a space lost after `SELECT *`; `PRIMARY KEY`,
  `OVER w`, `IS UNKNOWN`, the words of a `CAST(x AS type)` and of `timestamp with time zone` not cased; the head and constraint words of
  `CREATE` / `ALTER` / `GRANT` / `MERGE` / function options left lower case;
- stability: a comment placed after code on one line that the layout moved to a line start, and one that the layout moved past a comma,
  both changed on the second pass. Comments now remember whether anything follows them on their line, and a comment written after an item
  stays with the item in comma-first styles.

### FR-145 - what a style option does for PostgreSQL

`PgStyleApplicabilityTests` runs the corpus under every option of the SQL Prompt catalog (115 options), with each other value of it, on three
page widths. An option listed in `SqlPromptApplicability` (27 of them: batch separators, global variables, control flow, variables, comparison
alignment, comment alignment, the collapse thresholds the layout does not read ...) changes nothing in any script. Every other option changes the
output of at least one script. The first run of the probe listed 10 options that did nothing; after a fix (contents of a parenthesis ignored their indentation option) and three
scripts with long `VALUES` rows and column lists, none is left.

### Other checks

- Unit: `PgFormatterSafetyTests` (syntax errors with line and offset, unterminated quotes, psql commands, comments in order, Windows line ends and the
  final newline, dollar bodies with their own line ends, selection by whole statements, terminators), `PgCasingTests` (keywords, functions and types
  by their options, names never recased, words that are keywords only in their place), `PgFormatRoutingTests` (the engine: style as JSON or by name,
  the Format SQL actions, expand wildcards and qualify names against a cache, preview in PostgreSQL), web: `FormatterServicePgRoutingTests`,
  `StylesPagePgTests`.
- SC-010: formatting a PostgreSQL session leaves the T-SQL parser's invocation count where it was (`NoTsqlOnPostgresSessionTests`,
  `Formatting_a_PostgreSQL_session_never_reaches_the_T_SQL_parser`). The US5 refusal test became the formatting test.
- SC-017: `Twenty_seeded_syntax_errors_are_twenty_messages_each_at_its_line_and_column` (user story 1) and a browser scenario: three statements with
  errors give three Messages entries with their lines and nothing runs.
- Browser (`PostgresWorkspaceTests`): format lays out a multi-statement script and a second format changes nothing; a function body and quoted names are
  verbatim; a syntax error leaves the text and names the problem; the Format Styles page marks the options and previews a PostgreSQL sample.

### Not covered

- The comparison with a person's idea of a good layout: the goldens are drift guards read against the options, not an authority.
- Formatting inside PL/pgSQL bodies (out of scope: they are kept as written).
- `tasks.md` T128 (the final dialect gate) waits for story 8.

## User story 8 - code analysis for PostgreSQL (2026-10-14)

### SC-016 - the rule set

52 rules in the eight categories (floor: 40), every one with a PostgreSQL 18 manual link and a description of the behaviour it rests on
(`PgRuleRegistryTests`). Each has a positive script that names, with `[[ ]]`, the exact text the finding must underline, and one to three
negative scripts (`PgRuleCases`, run once per rule id by `PgRuleCaseTests`). The four schema-aware rules (PGPE004, PGPE005, PGBP005, PGDE006) also
report nothing without a cache. `PgRulesCorpusSweepTests` runs every rule over the 151 scripts of the format corpus: 0 crashes, 0 impossible positions,
0 SQL Server rule ids, and the whole-corpus engine run reports no syntax finding. `PgRulesDocTests` keeps `doc/analysis-rules.md` equal to the registry.

The sweep was also a noise check: a corpus of valid scripts should trip few rules. Three rules fired where they should not and were fixed before
this was recorded - PGNM002 on quoted names (a quoted name is exactly what it spells), PGPE010 on `CROSS JOIN LATERAL`, PGDE005 on temporary tables.

### FR-151 / FR-154 / FR-155 - behaviour around the rules

`PgAnalysisEngineTests`: a script that does not parse is one `PGSYN001` finding at the first error and no rule finding (also for T-SQL text and for a
script with several errors); a psql command is `PGEX004`, not a syntax error; code analysis off returns nothing without parsing; `-- akml-disable-line`,
`akml-disable` / `akml-enable`, `akml-disable` to the end of the script, the legacy `-- noqa` forms, "this session", "everywhere", severity overrides and the
nearest `.casettings` all work for PostgreSQL rule ids. An unclosed `-- noqa-begin` is reported (`NOQA001`); the PostgreSQL engine adds that report after the suppression filter, so the block
it opens cannot hide it.

### FR-152 / SC-010 - the two sets never meet

- `PgRuleRegistryTests`: no id in common, no T-SQL rule is a `PGxx` id, no PostgreSQL rule implements `IAnalysisRule` and the other way round.
- Engine `AnalysisHandlersPgTests`: the same text gives `PGBP002` on a PostgreSQL session and `PE003` on a SQL Server one; the T-SQL parser's invocation
  count does not move for a PostgreSQL session (and does for a SQL Server one, so the count is a working instrument).
- Rules window: `ListAnalysisRules` returns the PostgreSQL set only for a request with `IncludePostgreSql`, so the SSMS Manage Rules window still lists the SQL Server
  rules exactly as before; the web Settings page asks and groups by database, then category.

### Browser

`PostgresWorkspaceTests`: the story's script gives PGNM001, PGBP002, PGEX001 and PGDE001 with manual links in the Problems tab and nothing from the T-SQL rules, and a
`-- akml-disable-line PGBP002` removes that one only; a script with a syntax error gives `PGSYN001` alone; Settings › Code analysis lists 52 rules in 8 categories under PostgreSQL
beside the SQL Server rules.

### Speed

Measured in a Debug build (a Release build is faster): a script of 2,688 statements and 235,000 characters is split and parsed in 0.8 s and all 52 rules run
over it in 1.0 s; ten typical corpus scripts take 0.7 ms each for parse and all rules. The first measurement was 2.6 s for the split alone. The statement splitter
found each statement's line and column by counting from the start of the script (quadratic); it now continues from the previous statement (`PgPositions.Cursor`), and the
tree of a statement is walked once for all rules (`PgScriptStatement.All`). `A_script_of_two_thousand_statements_is_analysed_in_seconds_not_minutes` guards it.

### Not covered

- Whether each message reads well to a person who is not a PostgreSQL expert: reviewed by the author only.
- The SSMS Manage Rules window does not show the PostgreSQL rules (the shell is not part of this spec; the engine keeps its answer unchanged for it).
- PGSE001 reads `EXECUTE` in `plpgsql` bodies from their tokens; a body in another language, or a command built in a variable first, is not examined.
- PGBP005 and the other rules that resolve names need the loaded schema and say nothing about a table it has not loaded.

## FR-101 - TLS modes (2026-10-15)

**What was checked, live, on the private PostgreSQL 18 with a self-signed certificate** (`PgTlsLiveTests`, 10 tests, through `PgConnectionString.Build` and `TestSqlConnectionHandler`):
`disable` never uses TLS; `prefer` and an empty mode use it when offered and do not check the certificate; `require` insists on TLS and does not check the certificate either, as libpq
does (a self-signed server is accepted; `pg_stat_ssl` confirms the session is encrypted); `verify-ca` with the server's own certificate as root connects, without a root or with another
authority's it refuses; `verify-full` connects for `localhost` and `127.0.0.1`, which the certificate names, and refuses `::1`, which it does not, while `verify-ca` accepts `::1`.

**What the driver says, and the fix.** For every certificate refusal the driver gives one sentence. `PgTlsDiagnosis` repeats the start of the connection once, only after such a refusal (the eight-byte
SSLRequest, the handshake, nothing after it), and `PgConnectionFailure.DescribeAsync` puts the specific reason after where it happened. The refusal says which thing was wrong: an authority this machine
does not know ("... is not signed by a root this machine trusts ... or use TLS mode require"), a certificate that does not chain to the root given, a certificate outside its dates (the certificate that is outside them: the
server's, an intermediate's or the root given, each with its own date), a root given that does not hold the issuer (the file is wrong, or the issuer is an intermediate the server should send), or a host name the
certificate does not carry (with the names it does). A root certificate file the driver cannot read is worded by `DescribeAsync` as that file and why (it never reaches the diagnosis: the driver fails inside its handshake).
The root in force is the driver's: the connection string, else `PGSSLROOTCERT`, else `%APPDATA%\postgresql\root.crt`, in PEM or `.pfx`. What a server puts in its certificate is shown cleaned (no line breaks, bidirectional
or formatting characters, no double quote) and cut to 64 characters.

**Tests:** `PgTlsDiagnosisTests` 47 (a fake PostgreSQL TLS endpoint with certificates made in the test: authorities, intermediates, expired, not yet valid, wrong names, no names; it records what it received, so the tests
assert that the client sent the SSLRequest and nothing after the handshake and that the modes which do not verify never connect), `PgTlsHandlerTests` 4 (test connection, list databases by string and by session, switch database), `PgTlsLiveTests` 10,
and the browser test `The_verifying_TLS_modes_name_the_certificate_problem_and_the_root_certificate_fixes_it` (skips, rather than fails, without TLS or an IPv6 loopback on the server).
The PostgreSQL and handler namespaces of `AkmlSql.Engine.Tests` ran 528 passed, 0 failed; the whole project ran 2,520 passed and 2 failed (the known `PerformanceBaselineTests`, and a history search that took 311 ms
against a budget of 250 ms and passes alone) before the fixes below.

**Adversarial review of this change** (4 reviewers, each finding tried by 2 skeptics; 20 confirmed, 6 refuted): all 20 were acted on. Production: a certificate outside its dates was always blamed on the server's own
certificate with the server's dates; a `.pfx` root was reported as unreadable PEM; the diagnosis ignored `PGSSLROOTCERT` and the profile root file and so named the wrong cause; a root that lacks the issuer was worded as
"give the root" although it was given; server-controlled certificate text went into the sentence unsanitised; and an unexpected exception inside the diagnosis would have escaped the handler instead of leaving the
driver's sentence. Tests: no case used an expired or future authority or an intermediate chain; the unreadable-root case pinned a branch real connections never reach; the "not contacted" and "nothing sent" tests could not fail; the
real driver exception was never used offline; two of three call sites were untested; the suite took 40 s because `localhost` resolved to `::1` first (now 127.0.0.1 with an address in the certificate, 14 s). One trap found on the way:
Windows remembers certificates by subject name, so two test authorities with one name and different keys make a chain fail its signature check instead of reporting a missing issuer; each test authority now has a name of its own.

## Code review of PR #256 - the fourteen findings and their fixes (2026-10-15)

The review (`/code-review` at xhigh effort over every source change of the PR) reported 14 findings; all 14 were acted on in one pass.

| # | Finding | Fix |
|---|---|---|
| 1 | AI Fix sent the server's DETAIL/HINT to a cloud provider unredacted | `AiOutbound.Protect`: on a redacted call the message and hint get the script's literals replaced by their placeholders; the detail, which can hold row data, is not sent. Local providers and SQL Server unchanged |
| 2 | A dollar-quoted body after AS/DO was redacted "as code" whatever its LANGUAGE | `PgLiteralRedactor` reads each statement's LANGUAGE ahead (before or after the body); only SQL, PL/pgSQL or none is read as code, anything else is one literal |
| 3 | Statement boundaries were quadratic in script length | `PgPositions.Cursor`; 20,000 statements in well under 5 s |
| 4 | A CREATE/ALTER/DROP inside an open transaction reloaded the catalog (invisibly) and COMMIT never did | The reload waits for the run that ends the transaction; `SessionConnection.SchemaChangePending`; `ExecuteQueryResult.SchemaChanged` (key 14) tells the browser |
| 5 | A refresh during Phase B only marked the cache stale, which nothing read | `SchemaRefreshService` carries it out once the load has settled, once per load |
| 6 | Edits on columns of mixed-case user types failed: `$1::public.Mood` was folded | `PgCrudWriteGenerator.CastType` quotes the parts that need it, outside the modifier and array brackets |
| 7 | `postgreSql.enabled = false` still let a PostgreSQL session exist and fall into the T-SQL engines | ConnectionChanged ignores the request; TestSqlConnection and ListDatabases answer `DialectErrors.FeatureOffMessage` |
| 8 | `PgFormatter.Format` blocked a thread on its parse tasks inside the handler path | `FormatAsync`/`FormatSelectionAsync` are the cores; the format service, the request handler and the four handlers await them; the blocking forms remain for tools and tests |
| 9 | Triggers and GRANT/REVOKE with the word UPDATE raised "UPDATE without WHERE" | An UPDATE or DELETE is a statement only when it starts the text (past EXPLAIN) or follows `(` or `)` |
| 10 | Each statement was re-lexed three or four times for its first words | `PgLeadingWords`; the splitter records them on `PgStatement.LeadingWords` during its one walk; the tag, the tracker and the inspectors read them |
| 11 | `$SERVER$`/`$USER$` expanded empty on PostgreSQL (the SQL Server builder threw) | The PostgreSQL builder: `host:port` and the user name |
| 12 | `SessionState.TransactionState` was written and never read | Removed; the state lives on the persistent connection |
| 13 | `PostgresFormatRefusal` described the pre-US7 behaviour | `PostgresFormatDispatch.IsPostgres` |
| 14 | `EngineComposition` read `config.json` twice | One read through `ctx.EnsureSettings()` |

**Tests added or changed**: `SchemaRefreshServiceTests` (new, 4), `PostgresFlagRegistrationTests` (+3), `PgCrudWriteGeneratorTests` (+11), `PgApplyRunnerTests` (+1 live, with `CREATE TYPE "Mood"`), `PgExecuteRunnerTests`
(+2 live, +1), `PgStatementBoundariesTests` (+2), `SnippetHandlersTests` (+1), `PgLiteralRedactorTests` (+3), `PgDestructiveStatementCheckTests` (+13 cases), `PgStatementSplitterTests` (+2),
`AiPromptDialectTests` (+2), `PgFormatterSafetyTests` (+1); `SessionManagerKindTests` and `SessionDatabaseHandlersPgTests` moved to the connection's state.

**The suites after the fixes** (Debug, the private PostgreSQL 18 cluster): `AkmlSql.PostgreSql.Tests` 2,226 passed; `AkmlSql.AI.Tests` 92 passed; `AkmlSql.Core.Tests` 1,457 passed, 1 failed (the known
`ProfileGet` key-layout test, red before the feature); `AkmlSql.Web.Tests` 1,476 passed, 66 failed (the known sp031 formatter goldens, red before the feature); `AkmlSql.Engine.Tests` without the 20-minute
performance baseline 2,570 passed, 3 failed on time budgets while the review agents loaded the machine (`RefactoringPerformanceTests.SC001` 100 ms against < 100, two `HistoryScaleTests` at 256 and 279 ms
against 250) - all three pass alone (4 of 4).

**Adversarial verification of the fixes** (14 judges, one per finding, each claimed problem tried by 2 skeptics, plus a completeness critic over the whole diff; 65 agents): 20 problems
confirmed, 5 refuted, 11 sweep items. All were acted on in a second round:

- Fix prompt (1): protection is now a property of the call, not of the placeholder map, so a script with no literal and the "Send anyway" consent path also withhold the detail; the script's
  literals are masked in one pass, as written and as a value, only as whole words (a literal `'in'` is not found in "invalid"; a lone space names nothing); in a data error (SQLSTATE 22) every
  other quoted value of the message, which may come from a row, becomes `(value not sent)`.
- Redactor (2): a non-SQL language sticks for the rest of its statement (two functions sharing a statement after a missing semicolon), a psql meta-command ends a statement, and a table named
  `language` no longer hides the `LANGUAGE` word after it.
- Reload after DDL (4): the owed reload is carried in `PgSchemaChangeState` on the session's connection, never re-read from the capped message list; visibility is judged per statement, so a
  change made before a `BEGIN` in the same run, or committed before another `BEGIN`, is reported at once; the web's rule is `SchemaChangeSignal`, with its own tests.
- Refresh during a load (5): "a load is running" is the population claim, not the phase (a failed Phase B leaves PhaseA with nothing running, and is refreshable at once); the refresh takes
  the claim like every other load; a deferred refresh is dropped when the session moved to another database meanwhile.
- Feature off (7): the gate is the start-up decision (the catalog reader exists only when the feature was on), not settings re-read later, so a switch flipped at runtime waits for a restart like the registrations.
- Safety check (9): `PREPARE ... AS`, a rule's `DO INSTEAD` / `DO ALSO` action, the statements of a `BEGIN ATOMIC` body and the statement after a `WITH` clause ending in `SEARCH` / `CYCLE` are checked again.
- Tests that could not fail were made to (the never-settles refresh, the recorded leading words); the live Apply test puts the type in the seed and pins the driver's `public.Mood`; a Build-level
  test pins the quoted cast; the contracts (`contracts/ipc.md`, `data-model.md`, `plan.md`), `doc/ipc-api.md` and `doc/configuration.md` describe key 14, the moved transaction state and the
  feature-off refusal; the BOMs a scripted edit had added to three files were removed.
- Left as known limits: a lower-case user type whose name has a space is shown unquoted by the driver and cannot be told from a built-in; the faulted-parse half of finding 8 is verified by reading
  (`PgParser` is static, so a throwing parse cannot be injected without a test-only seam).

**After the second round** (the private cluster): the PostgreSQL library tests of the changed areas 212 passed; the web AI-prompt and schema-signal tests 37 passed; the engine's changed handlers, runner,
Apply, refresh and TLS tests 237 passed, live. The full suites again: `AkmlSql.PostgreSql.Tests` 2,237 passed; `AkmlSql.Web.Tests` without the known sp031 goldens 1,476 passed; `AkmlSql.Engine.Tests`
without the performance baseline 2,576 passed, with the same three time-budget tests red under the load of the verification agents and green alone.

**Seen on the way, not fixed here**: the driver reports a column's type as `-.-` when the type was created on the same connection after it opened (its type cache dates from the open), so a grid edit on such a column is
refused until the connection is reopened; a reload of the driver's types after a `CREATE TYPE` would remove it.

## Quickstart section 2 - the walk-through (task T179)

Nobody walked these scenarios by hand. Each step is mapped to the automated test that performs the same steps against the private PostgreSQL 18 cluster and the Debug
engine and web builds; the steps no test performs are listed after the table. The tests are in `tests/AkmlSql.Web.E2E.Tests` (`PostgresWorkspaceTests`, `PostgresEditingTests`)
unless a project is named.

| Scenario and step | Test that performs it |
|---|---|
| US1.1 connect, status bar shows the kind, version, server | `The_status_bar_says_PostgreSQL_with_the_version_and_the_toolbar_runs_a_script` |
| US1.2 stacked result sets, UPDATE tag, notice, error with SQLSTATE, caret on the failing line, statements stop | `Two_SELECTs_and_an_UPDATE_stack_two_grids_and_list_the_servers_tags`, `A_notice_is_listed_as_information_and_the_run_still_succeeds`, `An_error_opens_Messages_and_clicking_it_puts_the_caret_on_the_failing_statement`, `Twenty_different_errors_each_jump_to_their_statements_line` |
| US1.3 aborted transaction, one-click rollback | `A_failure_in_a_transaction_shows_it_aborted_and_one_click_rolls_it_back` |
| US1.4 `GO` is sent as written, the error explains it | `A_GO_line_is_sent_as_it_is_and_the_error_says_it_belongs_to_SQL_Server` |
| US1.5 a PostgreSQL form aimed at a SQL Server port | `Testing_a_PostgreSQL_form_aimed_at_a_SQL_Server_port_asks_to_check_the_kind` |
| US2.1 to 2.4 explorer kinds, columns, comments, follows a script and a database switch | `The_explorer_shows_the_kinds_columns_and_comments_follows_a_script_and_a_database_switch` |
| US2.5 a role without USAGE on a schema | `A_role_without_usage_on_a_schema_sees_the_rest_and_the_schema_marked_not_accessible` |
| US3.1 to 3.4 completion after FROM, alias dot, ON CONFLICT; hover; signature help; no T-SQL sigils | `PostgresEditingTests`: `After_FROM_the_search_path_is_unqualified_other_schemas_are_qualified_and_odd_names_are_quoted`, `After_an_alias_dot_the_columns_come_with_their_types`, `After_ON_CONFLICT_the_actions_are_PostgreSQLs`, `Hovering_string_agg_shows_its_signatures_description_and_the_manual_link`, `Inside_date_trunc_the_parameters_are_shown_and_the_active_one_follows_the_commas`, `Typing_at_hash_or_bracket_offers_no_t_sql` |
| US3.5 an older server is not offered later commands | `An_older_server_is_not_offered_commands_that_arrived_later` needs a PostgreSQL 14 server (`AKML_PG14_TEST_CONNECTION`) and is skipped without one; the version gating itself is `PgVersionGatingTests` (PostgreSql.Tests) |
| US4.1 twenty switches, text intact | `Twenty_switches_between_SQL_Server_and_PostgreSQL_each_follow_within_three_seconds_and_keep_the_text` |
| US4.2 History kind badge, filter, `kind:` term | `History_shows_the_kind_filters_by_it_and_takes_a_kind_term` |
| US4.3 the AI prompt names PostgreSQL (what the model is told; its answer is not read, see SC-011) | `Text_to_SQL_on_PostgreSQL_asks_the_model_for_PostgreSQL`, `A_cloud_provider_gets_the_script_without_its_literals_and_the_answer_has_them_back` |
| US4.4 remembered password, no prompt, nowhere it should not be | `A_remembered_password_connects_without_a_prompt_and_is_nowhere_it_should_not_be` |
| US4.5 an engine without PostgreSQL disables the choice | `An_engine_with_PostgreSQL_switched_off_disables_the_choice_and_says_why` |
| US5.1 the existing suites unchanged | `baseline/after-us*/compare.txt` |
| US5.2 refactorings and Find invalid objects off with the reason; the engine never reaches the T-SQL code | `On_PostgreSQL_the_refactorings_are_off_and_say_why_in_the_toolbar_and_the_palette`; `NoTsqlOnPostgresSessionTests` (Engine.Tests) |
| US6.1 edit, NULL, add, delete, Apply | `Edits_inserts_and_deletes_land_on_their_rows_and_the_grid_shows_what_the_server_stored` |
| US6.2 join, view, aggregate, keyless table are read-only with the reason | `A_join_a_view_an_aggregate_and_a_table_without_a_key_are_read_only_and_say_why` |
| US6.3 invalid cell, vanished row | `Text_the_column_cannot_read_and_a_row_that_vanished_are_reported_and_nothing_else_is_written` |
| US7.1 to 7.3 format, body and quoted names verbatim, syntax error left alone, Format Styles marks | `Format_lays_out_a_PostgreSQL_script_with_the_active_style_and_a_second_Format_changes_nothing`, `Format_keeps_a_function_body_and_quoted_names_exactly_as_written`, `A_script_with_a_syntax_error_is_left_alone_and_the_message_names_the_problem`, `The_Format_Styles_page_marks_the_options_with_no_PostgreSQL_meaning_and_previews_a_PostgreSQL_sample` |
| US7.4 Parse lists every error without contacting the server | `Parse_lists_every_syntax_error_with_its_line_and_column_without_running_anything` |
| US8.1 the four findings with links; the line directive | `Analyse_reports_the_PostgreSQL_rules_with_links_and_a_line_directive_removes_only_its_finding` |
| US8.2 rules by category under their own database; a SQL Server session shows no PostgreSQL finding | `Settings_lists_the_PostgreSQL_rules_by_category_under_their_own_database_beside_the_SQL_Server_rules`; `AnalysisHandlersPgTests` (Engine.Tests) |

**The run behind the table** (2026-10-14, Debug builds, serial, the private PostgreSQL 18 cluster and the local SQL Server): `PostgresWorkspaceTests` and `PostgresEditingTests`, 34 passed,
1 skipped (the PostgreSQL 14 test), 8 min 51 s.

**Not run by anyone**: the hallway tests for SC-001 and SC-012 (five people); US5.3 (SSMS 22 started against the same engine, task T129); the browser check against a real PostgreSQL 14 server of US3.5
(skipped without `AKML_PG14_TEST_CONNECTION`); the SC-011 reading of real model answers.

## Performance (task T180)

Local PostgreSQL 18, one machine, nothing else running.

| What | Result | Budget |
|---|---|---|
| SC-008: names of 1,000 objects (Phase A), cold connection | 1,055 ms in Debug, 954 ms in Release | 2,000 ms |
| SC-008: columns, keys, foreign keys, indexes, parameters of the same 1,000 objects (Phase B) | 53 ms in Debug, 63 ms in Release | 10,000 ms |
| Format a 2,010-line script (96,632 characters) with every safety check, style `default`, Release, running engine | median 262 to 305 ms over three runs (single runs 200 to 372 ms) | 500 ms |
| The same in a Debug build | about 700 ms | (guard of 4 s) |
| SC-002: a switch between SQL Server and PostgreSQL (user story 4) | longest 371 ms of 20 | 3,000 ms |

What the formatting measurement is: `PgFormatter.Format` end to end (parse the original, lay it out, parse the result, compare the trees, compare the tokens and comments, lay the result out again), with
eight unmeasured runs first because the runtime optimises a method only after it has run many times; the product runs for hours. The first version of the pipeline took 426 ms for 2,099 lines. Three changes
brought it down: each text is read into tokens once, not four times; the two native parses (about two thirds of the time) overlap the layout work, as the PostgreSQL parser can run on two threads at once
(measured: two parses at once 176 ms, one after the other 291 ms); and a script that is already laid out as the style wants it skips every check. Script analysis and the statement splitter are in
the user story 8 section. `PerformanceBaselineTests` (the T-SQL formatter) is a known failure before and after; no T-SQL code changed.

