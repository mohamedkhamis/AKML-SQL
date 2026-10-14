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

Recorded in [research.md](research.md) under R14: the service runs as LocalSystem, the store is in that account's own
`%AppData%\AKML SQL` (the SYSTEM profile) and not beside the `--config` file, and the access list of that folder could not be read
without elevation. One elevated command is left to run; the consent text does not change either way.

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
- Manual SSMS check (task T129): **not done.** It needs a person to start SSMS 22 against the engine and look. The structural
  evidence above and the unchanged Shell.Shared result in `baseline/README.md` are all there is so far.

### Theme tokens (task T131)

`scripts/generate-theme-css.ps1 -CheckOnly` reports `OK` for light, dark and high contrast, and no hex or rgb colour was added to
any component or page in this story or the one before it.
