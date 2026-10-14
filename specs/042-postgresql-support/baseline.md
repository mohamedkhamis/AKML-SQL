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
