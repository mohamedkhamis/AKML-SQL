# Feature Specification: QA Audit Remediation

**Feature Branch**: `047-qa-audit-remediation`
**Created**: 2026-11-21
**Status**: Draft
**Input**: User description: "@reports/AKML-QA-UX-Documentation-Audit.md" — fix the defects, UX problems and documentation gaps recorded in the 2026-11-20 audit of the web editor, the SSMS 22 extension, the command-line analyzer and the public documentation.

## Overview

The audit exercised every part of the web editor, a large part of the SSMS 22 extension, the command-line analyzer and the public site, and recorded 33 issues with reproduction steps and acceptance criteria (full entries: `reports/AKML-QA-UX-Documentation-Audit.md`; issue IDs below refer to it). This feature fixes 32 of them. The 33rd, **WEB-006** (reuse of SSMS-saved credentials in the browser), is a new capability that needs its own security review, so it is out of scope here and must get its own spec; its question is answered in the report (§2.2): today the browser cannot, by design.

The stories below follow the report's recommended fix order. Each is independent: shipping one still delivers value.

## Clarifications

### Session 2026-11-21

- Q: What does Save do when "Remember password" is ticked on a SQL-login connection? → A: Save tests the login first; on success the engine machine keeps the password immediately; on failure the dialog says so and nothing is kept.
- Q: Does the quiet marker apply only to the missing semicolon or to all advisory findings? → A: Every Information-level and Hint-level finding gets a margin marker only, by default; warnings and errors keep underlines; the squiggle-style setting can restore underlines.
- Q: How far does the fix for saved data on another address or browser go? → A: Request permanent browser storage and show an explanatory banner; the saved-connection list stays in the browser; an engine-held list is deferred to a later spec.
- Q: What proves a fix stays fixed? → A: Every fixed issue gets at least one permanent automated test in the repository's existing suites, with its samples committed as test inputs; the throwaway audit sweep is optional extra evidence.
- Q: When are unused engine pairings removed automatically? → A: Never; the engines list only gains distinct labels and per-pairing Revoke.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - The command-line analyzer reports what it finds (Priority: P1)

A developer or CI pipeline runs the installed command-line analyzer on SQL files. Today it prints "No issues found" for any input, so a build gate built on it never fails.

**Why this priority**: A silent false "clean" result defeats the tool's whole purpose and misleads CI. It is small and independent of everything else.

**Independent Test**: Run the installed analyzer on the audit's sample files and compare with the findings the same rules produce elsewhere.

**Acceptance Scenarios**:

1. **Given** the installed analyzer and the audit's `multi.sql`, **When** it runs on the file, **Then** it prints the three expected findings (PE002, PE003, PE004) and its check mode exits with failure.
2. **Given** a one-line file missing its semicolon, **When** it runs with only the missing-semicolon rule selected, **Then** exactly one informational finding is printed.
3. **Given** a release build, **When** the published analyzer reports zero findings on the known-bad fixture, **Then** the release build fails before it can ship.

---

### User Story 2 - First-run engine pairing works on a standard install (Priority: P1)

A user opens the web editor for the first time after an installer-based setup and clicks **Use this computer's engine**. Today it fails with a handshake error because it tries a fixed port while the installed engine listens elsewhere. Paired browsers also pile up as indistinguishable "Web edition" entries, so the user cannot tell which to revoke.

**Why this priority**: It is the first thing every new web user does; failure here blocks all later use.

**Independent Test**: On a machine installed with a non-default engine port and an empty browser profile, click the button once.

**Acceptance Scenarios**:

1. **Given** an install whose engine port is not the old default and an empty browser profile, **When** the user clicks **Use this computer's engine**, **Then** the connection indicator becomes Live without the user opening Settings.
2. **Given** no engine is running, **When** the user clicks it, **Then** the message names the address that was tried and offers **Add engine…**.
3. **Given** an engine that rejects the handshake, **Then** the existing rejection wording still appears.
4. **Given** two browsers paired with the same engine, **When** the user opens the engines list, **Then** each browser has a distinct, recognisable label and revoking one leaves the other working.
5. **Given** many existing pairings, including ones made before this change, **When** the user opens the engines list, **Then** each shows when it was created and last used, so the user can tell which to revoke; none is removed without the user's action.

---

### User Story 3 - Saved connections are remembered, and the dialog tells the truth (Priority: P1)

A user saves a SQL Server connection with "Remember password", closes the browser, and returns later. Today they are asked for the password and the display name again, with no explanation. Causes found: saving with Remember ticked does not actually store the password until a later successful connect; the browser's saved data is tied to one address and can be evicted; a restored tab can open the dialog with a blank name.

**Why this priority**: This is the user's reported problem and a daily annoyance for anyone using SQL logins.

**Independent Test**: Save a SQL-login connection with Remember ticked, restart the browser and the engine, reopen the tab, and observe what is asked.

**Acceptance Scenarios**:

1. **Given** the user saves a SQL-login connection with Remember on and a correct password, **When** they restart the browser and the engine and reopen it, **Then** it connects without asking for the password.
2. **Given** the user saves with Remember on and a wrong password (or an unreachable server), **When** Save runs, **Then** the dialog says the login test failed and why, the connection details may still be saved, and no password is kept.
3. **Given** a restored tab with no saved row, **When** the reconnect dialog opens, **Then** Display name is pre-filled with a sensible default (server / database) and Save creates a row under that name.
4. **Given** the page is opened on a different address of the same machine (for example the numeric loopback address) with no saved data, **When** the page loads, **Then** a banner explains that saved data belongs to the other address.
5. **Given** the user saves a first connection, **Then** the browser is asked to keep the data permanently and Diagnostics shows whether it agreed.
6. **Given** a newly saved SQL Server connection, **Then** it carries no PostgreSQL-specific data, and older saved connections still load.

---

### User Story 4 - The formatter produces correct, stable layout in every style (Priority: P1)

A developer formats T-SQL in the web editor or in SSMS. Today, across all 12 built-in styles: a parameter type with a size (`varchar(10)`) is split over two lines; `BEGIN`/`END` of `IF`/`ELSE`/`WHILE` blocks sit at column 0; the statement after `GO 3` is indented. Smaller issues: comment text is altered in one style, some styles print double spaces or break short lists, and in SSMS formatting changes a CRLF document to LF.

**Why this priority**: Formatting is the product's headline feature, and the web editor and SSMS share one formatting engine, so one fix reaches both.

**Independent Test**: Format the audit's sample scripts with each of the 12 styles and compare with the stated expectations; format a second time and confirm nothing changes.

**Acceptance Scenarios**:

1. **Given** a procedure header with `@b varchar(10) = 'x'`, **When** formatted with any of the 12 styles, **Then** the size stays on the same line as its type, in the style's own casing and spacing.
2. **Given** `IF`, `ELSE` and `WHILE` blocks using `BEGIN`/`END`, **When** formatted, **Then** `BEGIN`/`END` align with the owning statement (or follow the style's documented option) and the body is one level deeper.
3. **Given** `GO` and `GO 3` separators, **When** formatted, **Then** every statement after a separator starts at column 0.
4. **Given** a block comment with an indented second line, **When** formatted with the style that altered it, **Then** the comment's internal relative indentation is preserved.
5. **Given** the audit's spacing samples, **Then** no style prints more than one space between a join keyword and the table name, and a two-item `IN` list stays on one line unless the style says otherwise.
6. **Given** a document with CRLF line endings, **When** Format Document runs in SSMS, **Then** the document still uses CRLF throughout; an LF document stays LF.
7. **Given** any fixed output, **When** formatted again, **Then** nothing changes (stable), and every token of the original is still present.

---

### User Story 5 - Advisory findings, such as a missing semicolon, show one small marker (Priority: P1)

A developer writes T-SQL without a terminating semicolon. This is advice, not an error. Today the web editor draws a blue wavy underline over the whole statement and SSMS draws a magenta squiggle over it, which is noisy and looks like an error. The same applies to every advisory (Information or Hint) finding: it should be drawn as a marker in the margin, never over the text, while warnings and errors keep their underlines. Two documented SSMS settings (`squiggleStyle`, `showAdvisoryHints`) are never read, and a script opened from disk is not analysed until it is edited.

**Why this priority**: The owner stated the exact desired behaviour; it affects every statement without a semicolon, in both hosts.

**Independent Test**: Open a script whose statements lack semicolons in both hosts and look at the editor.

**Acceptance Scenarios**:

1. **Given** a statement without a terminating semicolon, **When** analysis completes in the web editor or SSMS, **Then** one small blue marker appears on the relevant line, and the SQL text itself gets no recolouring, background or underline.
2. **Given** the marker is showing, **When** the user adds the `;`, **Then** the marker disappears.
3. **Given** a genuine syntax error or a Warning/Error-level finding, **Then** it keeps its underline and colour (not softened by this change).
4. **Given** any other Information-level or Hint-level finding, **When** analysis completes, **Then** it too shows only a margin marker and the text is drawn normally; the finding still appears in the problems list with its message.
5. **Given** the SSMS advisory-hints setting is off, **When** a finding has no automatic fix, **Then** its lightbulb offers no fix actions, while its margin marker, its problems-list entry and its suppress actions stay (the setting's documented meaning; plan-time amendment A1).
6. **Given** the squiggle-style setting is left at its default, **Then** advisory findings show only the margin marker; **given** the user picks an underline value, **Then** advisory findings are underlined in that style again; and every value the documentation lists works as described.
7. **Given** a script opened from a file in SSMS and connected, **When** about ten seconds pass with no typing, **Then** the same findings appear as after editing the text, and as the command-line analyzer reports.

---

### User Story 6 - Syntax errors are reported clearly in the web editor (Priority: P1)

A user types `SELEC * FROM dbo.Customers;` in the web editor and clicks Analyse or Parse. Today Analyse says "No problems found." and Parse shows no marker in the text.

**Why this priority**: Telling a user their broken SQL is fine is a correctness failure on the product's core path.

**Independent Test**: Enter invalid SQL, run Analyse and Parse with and without a connection.

**Acceptance Scenarios**:

1. **Given** unparseable SQL and no connection, **When** Analyse runs, **Then** the problems list shows one error with line and column and the offending spot is marked in red.
2. **Given** the same text, **When** Parse runs (connected or not), **Then** the same marker appears and the status says the parse failed.
3. **Given** valid SQL, **Then** the problems list still says "No problems found."

---

### User Story 7 - Workspace features do what they promise (Priority: P2)

Several web and SSMS features visibly fail or mislead: custom snippets never expand in the editor; the schema explorer shows "Stored Procedures (0)" for a database with seven; three Refactor items cannot work in the browser and engine-only ones are disabled although the engine is live; the explorer filter shows a blank area when nothing matches; Escape does not close the Refactor dialog; bad import files give parser internals or no message; and SSMS **Find Invalid Objects** reports "0 dependency rows scanned" for a database that has 63.

**Why this priority**: Each is a clear defect with a small, local fix, but none blocks the core editing path.

**Independent Test**: Run each item against the Northwind sample database as described in its acceptance scenario.

**Acceptance Scenarios**:

1. **Given** a saved custom snippet, **When** its prefix and Tab are typed in a SQL Server tab, **Then** it appears in completion and Tab inserts its body with the cursor placeholder honoured; deleting it removes it from completion.
2. **Given** Northwind and an engine whose sign-in can see its procedures, **When** the explorer loads, **Then** Stored Procedures shows 7 and lists them; a database with none shows 0. **Given** an engine whose sign-in cannot see every object (for example the web engine service as `NT AUTHORITY\SYSTEM` without VIEW DEFINITION, the cause measured on the audit machine), **Then** the procedures and functions groups say they may be incomplete, name the account, and say how to fix it, instead of showing a count (plan-time amendment A2).
3. **Given** a connected database, **When** the user runs Expand INSERT columns, Expand UPDATE columns or Expand EXEC parameters, **Then** the preview uses the real column or parameter names and Apply works; with no schema available the item is disabled with its reason shown before the click.
4. **Given** an engine without heavy refactoring support, **Then** the tooltip says so plainly; given an engine that has it, the engine-only items are enabled.
5. **Given** an explorer filter with no match, **Then** a "No objects match" message and a clear-filter control appear; clearing restores the tree.
6. **Given** the Refactor list or its preview is open, **When** Escape is pressed, **Then** it closes and focus returns to the Refactor button.
7. **Given** a file that is not a style (or not a snippet file) is imported, **Then** a plain-language message appears and no parser or exception text is shown.
8. **Given** Northwind, **When** SSMS Find Invalid Objects runs, **Then** it reports the true number of dependency rows read (63); given a view over a dropped table, that view is listed with the missing dependency; if the dependencies cannot be read, the window says so instead of reporting success.

---

### User Story 8 - AI prompts are coherent in every privacy mode (Priority: P2)

With the privacy mode "No schema", the prompt the assistant receives contains an empty schema section and a rule referring to "the provided schema". With **Fix** and no preceding error, the request says "Error Number: 0 / (no error)".

**Why this priority**: These degrade answer quality and are quick to correct, but AI is optional.

**Independent Test**: Capture the request each AI action sends under each privacy mode and when Fix is used without an error.

**Acceptance Scenarios**:

1. **Given** the privacy mode "No schema", **When** any AI action runs, **Then** the prompt has no empty schema section and no rule that refers to a provided schema (it may state that no schema is available).
2. **Given** no error has occurred, **When** the user chooses Fix, **Then** the action is unavailable or asks the user to run the query or paste the error; no request ever contains "(no error)".

---

### User Story 9 - The documentation explains connections, credentials and common symptoms (Priority: P2)

The "Connecting to SQL Server" page covers SSMS only. Troubleshooting has no entry for "Failed — Handshake failed", "No engine paired yet", "my saved connections are gone" or "it asks for my password again". The configuration page has an unhelpful search description and a dead link.

**Why this priority**: The credential complaint that started the audit is partly a documentation failure. It depends on Stories 2 and 3 settling the final wording.

**Independent Test**: Search the site for the symptom phrases and follow the steps on a fresh machine.

**Acceptance Scenarios**:

1. **Given** the published docs, **When** a reader searches for "remember password", **Then** a section explains how to connect from the web edition, what Save and Remember do, where the password lives, and why it may be asked for again.
2. **Given** a reader searches for "Handshake failed" or "asks for password again", **Then** the matching troubleshooting entry is the top result and its steps resolve the symptom.
3. **Given** the configuration page, **Then** its description is a full sentence and a link check over all published pages finds zero broken links.
4. **Given** the corrected interface, **Then** screenshots in the affected pages match it.

---

### User Story 10 - The web editor is accessible (Priority: P3)

A screen-reader or keyboard user finds the SQL editor unnamed, two badges below the minimum contrast, nested interactive controls, title-only labels, and landmark/heading gaps.

**Why this priority**: Important for inclusion and compliance, but the issues are low in severity and affect fewer users.

**Independent Test**: Run an automated accessibility scan on every route in the light, dark and high-contrast themes, and tab through the affected controls.

**Acceptance Scenarios**:

1. **Given** the editor page, **When** focus enters the editor, **Then** a screen reader announces a name such as "SQL query editor".
2. **Given** each route in all three themes, **Then** no colour-contrast violation remains (at least 4.5:1 for text).
3. **Given** each route, **Then** the scan reports zero violations at the standard AA level plus best-practice rules, and Tab reaches the close buttons and the scrollable preview.

---

### Edge Cases

- A connection saved before this change (older stored shape, extra field, no display name) must still open without data loss or an error.
- Save with Remember while the engine is offline or the server is unreachable: the login test cannot run, so no password is kept and the dialog says why; a later successful Connect with Remember still keeps it, as today.
- Pairings made before this change have no label: the list still shows them, with a generic label, their creation time and last use, and Revoke works on them.
- The user unticks Remember on an existing connection: the kept password is removed from the engine machine.
- Pairing when two engines are reachable (service and desktop-started) — the one belonging to the current page wins; the user is not paired to the wrong one silently.
- Formatting: styles that deliberately put `BEGIN` on its own line, a procedure with no parameters, `GO` with a count and trailing comment, input that does not parse (left unchanged), mixed line endings in one document, a block comment containing tabs.
- The browser denies the request to keep data permanently — the user sees the state in Diagnostics and a banner is not repeated on every load.
- Missing-semicolon marker when two statements are on one line, when the last statement has a trailing comment, and in a script with a real syntax error elsewhere.
- A database with no dependencies or no procedures: counts show 0 truthfully and "nothing to scan" is distinguishable from "could not read".
- A CI machine without a console: the command-line analyzer's exit code is the contract.
- Documentation screenshots taken before the UI changes must be refreshed, not left stale.

## Requirements *(mandatory)*

### Functional Requirements

#### Command-line analyzer

- **FR-001**: The installed command-line analyzer MUST report every finding that the same rules report in the editors, for the audit's sample inputs (CLI-001).
- **FR-002**: The release process MUST fail when the published analyzer reports zero findings on a known-bad fixture (CLI-001).

#### Engine pairing

- **FR-003**: The one-click "Use this computer's engine" action MUST pair with the engine that serves the current page, whatever port the installer chose, or report precisely why it cannot, naming the address tried and offering **Add engine…** (WEB-001).
- **FR-004**: Each paired browser MUST have a distinct, recognisable label and show when it was created and last used, and MUST be revocable individually. Pairings MUST NOT be removed automatically; only the user's Revoke removes one (ENG-001).

#### Saved connections and credentials

- **FR-005**: Saving a SQL-login connection with "Remember password" ticked MUST test the login first. On success the engine machine MUST keep the password at once, so later opens need no password. On failure the dialog MUST say the test failed and why, and MUST NOT keep the password (WEB-002).
- **FR-006**: The reconnect dialog MUST pre-fill the display name with a sensible default when no saved row exists (WEB-004).
- **FR-007**: The product MUST ask the browser to keep its saved data permanently when the first connection is saved, and MUST show the outcome in Diagnostics (WEB-003).
- **FR-008**: When the page is loaded from an address that has no saved data while a reachable engine exists, the product MUST explain that saved data is tied to the address used, and name the address to use instead when it knows one (WEB-003). Saved connections and pairings stay in the browser; this feature does not copy them to the engine or between addresses.
- **FR-009**: Saved SQL Server connections MUST NOT store PostgreSQL-specific fields, and previously saved connections MUST still load (WEB-010).
- **FR-010**: Passwords MUST NOT be stored in the browser, and nothing in this feature may widen where credentials are stored or who can read them.

#### Formatter

- **FR-011**: In all 12 built-in styles a parameter type with a size MUST stay on one line (FMT-001).
- **FR-012**: `BEGIN`/`END` of `IF`, `ELSE` and `WHILE` blocks MUST align with the owning statement (or the style's documented option) with the body one level deeper, in every style that uses block layout (FMT-002).
- **FR-013**: Every statement following `GO` or `GO n` MUST start at column 0 (FMT-003).
- **FR-014**: Formatting MUST NOT alter the text inside a comment other than uniform re-indentation of the whole block (FMT-004).
- **FR-015**: No style MAY print more than one space between a join keyword and its table, and short value lists MUST stay on one line unless the style says otherwise (FMT-005).
- **FR-016**: Formatting a document in SSMS MUST preserve the document's line-ending convention (SSMS-004).
- **FR-017**: Formatting MUST remain stable (a second pass changes nothing) and token-preserving in all 12 styles, verified over the audit's sample set.

#### Advisory markers

- **FR-018**: By default, every Information-level and Hint-level finding, including a missing optional statement terminator, MUST appear as one small marker on the relevant line, with no recolouring, background highlight or underline of the SQL text, in the web editor and in SSMS. Information markers are blue. Fixing the cause (for example adding the terminator) removes the marker (UX-001).
- **FR-019**: Syntax errors and Warning- and Error-level findings MUST keep their underlines and colours (UX-001).
- **FR-020**: The documented analysis display settings (squiggle style and advisory hints) MUST change behaviour as documented, for every documented value. The squiggle-style setting MUST let the user bring back underlines for advisory findings (SSMS-001; the web editor gets the same choice in its Code analysis settings). The advisory-hints setting keeps its documented meaning: when off, findings without an automatic fix offer no lightbulb fix actions (A1).
- **FR-021**: A script opened from a file in SSMS MUST be analysed once the connection is ready, with the same result as after an edit (SSMS-002).

#### Syntax feedback

- **FR-022**: Unparseable SQL MUST appear as an error with line and column in the problems list after Analyse, and as a red in-editor marker after Analyse or Parse, with or without a connection; "No problems found." MUST NOT be shown for unparseable SQL (WEB-005).

#### Workspace features

- **FR-023**: Custom snippets saved by the user MUST appear in completion and expand with Tab in the editor, and stop doing so when deleted (SN-001).
- **FR-024**: The explorer MUST list stored procedures with their true count when the engine's sign-in can see them, and MUST say that the list may be incomplete, naming the account and the fix, when it cannot; it MUST NOT present a count it cannot vouch for (WEB-013, A2).
- **FR-025**: The "expand columns / parameters" refactorings MUST use the connected database's real names; when no schema is available the items MUST be disabled with the reason visible before clicking, and engine-capability limits MUST be explained plainly (WEB-011).
- **FR-026**: An explorer filter with no match MUST show an empty-state message with a clear control (WEB-014).
- **FR-027**: Escape MUST close the Refactor list and preview dialogs and return focus to the Refactor button (WEB-012).
- **FR-028**: Importing a file that is not a style or not a snippet file MUST show a plain-language message with no parser internals (UX-002).
- **FR-029**: SSMS Find Invalid Objects MUST report the true number of dependency rows read, list broken dependencies, and say so when dependencies cannot be read rather than reporting success (SSMS-003).

#### AI

- **FR-030**: AI prompts MUST be internally consistent in every privacy mode; with no schema shared they MUST NOT contain an empty schema section or refer to a provided schema (AI-001).
- **FR-031**: With no preceding error, the Fix action MUST be unavailable or ask for the error; requests MUST NOT contain a placeholder meaning "no error" (AI-002).

#### Documentation

- **FR-032**: The documentation MUST explain web connections, what Save and Remember do, where passwords live, and why a password may be requested again (DOC-001).
- **FR-033**: Troubleshooting MUST contain entries for "Handshake failed", "No engine paired yet", "my saved connections are gone" and "it asks for my password again" (DOC-002).
- **FR-034**: Every published page MUST have a descriptive search description and no broken links; affected screenshots MUST match the corrected UI (DOC-003).

#### Accessibility

- **FR-035**: The SQL editor MUST have an accessible name (WEB-007).
- **FR-036**: All text MUST meet 4.5:1 contrast in the light, dark and high-contrast themes (WEB-008).
- **FR-037**: The web editor's routes MUST show no automated accessibility violations at the standard AA level plus best-practice rules, and the close buttons and scrollable preview MUST be keyboard-reachable (WEB-009).

#### Verification

- **FR-038**: Every one of the 32 in-scope issues MUST be covered by at least one permanent automated test in the repository's existing test suites that fails before the fix and passes after it. The sample inputs those tests need MUST be committed as test inputs (the audit's evidence and script folders are not tracked). A test that needs a live SQL Server, a desktop session or a running SSMS MAY sit in the suites' existing opt-in categories, but it MUST exist.

### Key Entities

- **Audit issue**: one recorded finding (ID, surface, severity, priority, reproduction, expected behaviour, acceptance criteria). The report is the source of truth for each.
- **Saved connection**: a named server/database/login choice kept by the browser, with a flag for whether the password may be kept on the engine machine. Never contains a password.
- **Engine pairing**: the trust record between one browser and one engine, with a label, creation time and last-used time.
- **Formatting style**: one of 12 built-in (or user) style definitions that determine layout.
- **Analysis finding**: a rule result with severity (hint, information, warning, error), location and a presentation (marker or underline) chosen by severity and user settings.
- **Custom snippet**: a user-defined prefix and body that expands in the editor.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: All 32 in-scope audit issues pass their acceptance criteria as stated in this spec (where this spec narrows a report criterion, this spec wins), each proven by a permanent automated test (32 of 32 issues covered); as extra evidence, a re-run of the audit's sweep shows 0 failures among the 39 checks that failed, with no new failures among the 85 that passed.
- **SC-002**: On a freshly installed machine with an empty browser profile, a new user reaches a live engine connection with one click in under 30 seconds, 10 out of 10 attempts.
- **SC-003**: After saving a SQL-login connection with Remember and a correct password, then restarting the browser and the engine, 100% of reopen attempts connect with no password prompt and no blank display name; 0 wrong passwords are ever kept.
- **SC-004**: Across the 12 styles × 10 audit samples (120 formatter runs), 0 outputs split a sized type, mis-indent `BEGIN`/`END`, or indent a statement after `GO`; 120 of 120 remain stable on a second pass and token-preserving.
- **SC-005**: With default settings, Information- and Hint-level findings (the missing semicolon among them) change nothing about how the SQL text is drawn in either editor — 0 recoloured, highlighted or underlined characters — while a marker is visible on 100% of the affected lines; Warning- and Error-level findings stay underlined in 100% of cases.
- **SC-006**: 100% of unparseable inputs in the audit set produce a visible error after Analyse, with or without a connection.
- **SC-007**: The installed command-line analyzer matches the editors' findings on 100% of the audit's sample files, and a release with a silent analyzer cannot be published.
- **SC-008**: Automated accessibility scans over every web route in the three themes report 0 violations.
- **SC-009**: A link check over every published documentation page finds 0 broken links, and a search for each of the four troubleshooting symptoms returns the matching entry first.
- **SC-010**: Fixes introduce no regression in the existing formatting, completion and analysis regression suites (all stay at their current pass levels).

## Assumptions

- WEB-006 (SSMS-to-browser credential reuse) is excluded: it is a new capability that needs its own spec and security review. The report's answer — not possible automatically today, by design — stands, and the documentation (FR-032) says so.
- A saved-connection list held on the engine (shared across addresses and browsers), suggested in the report's WEB-003 acceptance criteria, is excluded and belongs with WEB-006 in a later spec. The "other origin lists it" criterion from the report is therefore not an acceptance test here.
- For FR-005 the password is kept where the engine already keeps remembered passwords (the engine account's protected store); this feature changes when it is kept, not where.
- The marker for FR-018 reuses the margin marker SSMS already draws for informational findings; the web editor's gutter already supports one. Marker colours follow the product's existing severity colours (Information blue; Hint the existing hint colour).
- Several advisory findings on one line show one marker; its tooltip lists them all.
- Findings flagged "observed once" or "cause unknown" (SSMS-002, SSMS-003, WEB-013) must be reproduced and their cause established first; the acceptance criteria stay as written. Planning established all three (plan research R14, R17, R20).
- Plan-time amendments (2026-11-21, see plan.md Summary): **A1**: the SSMS advisory-hints setting keeps its documented lightbulb meaning (Story 5 scenario 5, FR-020). **A2**: WEB-013's measured cause is SQL Server metadata visibility for the engine's sign-in, so the explorer explains the gap instead of always showing 7 (Story 7 scenario 2, FR-024).
- Implementation-time amendments (2026-11-21, contracts/formatter.md §2, baseline.md):
  - **A3**: FMT-002's cause also joined statements written without semicolons ("END WHILE …", "SELECT 1 DROP TABLE …"). FR-012 therefore includes every statement starting its own line. Fixing the block layout cannot leave that out, because the same range finders produce both.
  - **A4**: FR-014's sample showed a statement glued to a leading block comment's `*/`. The first statement after leading comments keeps its line break.
  - **A5**: FR-015's short-list rule is met by making `auto` the IN-list default. The built-ins had `multiLine` only as an unchosen default, and an explicit `multiLine` still expands.
  - These change existing goldens only as baseline.md lists.
  - **A6** (contracts/workspace-ai.md §1): the engine filters its completion list by the word typed, so for a shortcode
    no built-in matches it returns no snippet at all. User snippets are therefore also offered online at a statement
    start, by the same rule as offline. Without that, SN-001's "type `qaz`, press Tab" could never work with an engine.
  - **A7** (contracts/workspace-ai.md §3, data-model §11): the in-browser schema for refactorings is the existing
    `SchemaPhaseRehydrator`, which the AI schema context already uses. It maps everything data-model §11 lists. No
    second converter (`SchemaSnapshotCache`) was written.
- "Standard accessibility level" means WCAG 2.1 AA plus the scanner's best-practice rules, as used in the audit.
- The 12 built-in styles and the audit's 10 formatter samples are the reference set for formatter checks; the samples are copied from the audit's (untracked) evidence folder into the repository's formatter corpus as committed test inputs (FR-038).
- Product behaviour outside the 32 listed issues is unchanged; unrelated audit observations (unverified items such as real cloud AI providers or PostgreSQL) are not in scope.
- Documentation and screenshot updates ship together with the behaviour they describe (Story 9 lands after Stories 2 and 3).
