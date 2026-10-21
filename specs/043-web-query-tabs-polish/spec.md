# Feature Specification: Query tabs, one Connect to database dialog, and editor polish for the web edition

**Feature Branch**: `043-web-query-tabs-polish`
**Created**: 2026-10-21
**Status**: Draft
**Input**: User description: "please i want editior to disable extention like grammely and any enhancement extention in text , also i want to use multi tab like ssms 22 but in web akml also can save sql query with diffrent connection string and can run it sperated , also i want to enhance fonts and style of editor and color to be fit also some screens not fit in dark mode , please if you have any questions let me know. also i want in web to save connection string with flag like Trust the server certificate without validating it. enhance window of Connect to SQL Server and rename verything to connecto to DB or fit name because it connect to posgress or sql server now. enhance fonts and color and style of query text ( now in web and if it done i iwll ask tou to use in ssms also ) because space after line need to review also focus to fit it in light and dark. finally if you have questions let me know"

## Overview

The web edition of AKML SQL works on one query at a time: one editor, one connection, one
results pane. SSMS users expect to keep several queries open at once, each against its own
server and database, and to run them without one disturbing another. This feature brings
that working model to the browser: **query tabs**, each with its own text, name, connection
and results, restored when the page is reopened.

Around the tabs, the feature finishes three things users notice every day:

- **One way to connect.** Since PostgreSQL support (spec 042) the product connects to two
  kinds of server, but the entry points are still named after SQL Server. There is one
  "Connect to database" action and dialog; the kind of server is a choice inside it. The
  dialog is tidied, and the choice to trust a server's certificate without validating it is
  kept with the saved connection and honoured on every reconnect.
- **Query text that reads well.** The editor's font, line spacing and colours are set
  deliberately for the light theme and the dark theme (and stay readable in high contrast),
  and every screen of the web edition fits the dark theme without light patches or unreadable
  text.
- **An editor that is left alone.** Browser writing assistants (Grammarly and the like), the
  browser's spell checker and auto-correction must not attach to the SQL editor.

SSMS is **not** changed by this feature. The user will ask for the editor typography to be
carried to SSMS once it is right in the web edition.

## Today

Facts about the current web edition that the stories below build on (verified on the
`042-postgresql-support` branch on 2026-10-21):

- The editor holds a single document. Spec 041 decided this on 2026-10-08 and deferred
  multiple query tabs to a later feature; this is that feature. New, Open, Save and Save as
  for that one document exist, as do `SQLQuery{n}` default names that are never reused.
- One browser session has one engine session and one live connection; the Schema panel,
  status bar, completion, formatting and analysis all follow that one connection.
- The connection dialog already offers both kinds of server. Its title switches between
  "Connect to SQL Server" and "Connect to PostgreSQL"; the command palette has two separate
  commands, one per kind; the connection picker's help text speaks of SQL Server only.
- "Trust the server certificate without validating it" already exists for SQL Server
  connections, is saved with the connection and restored when the saved connection is picked
  (added 2026-10-17). The web edition deployed on the user's machine (web and engine
  1.26.1019.2313, built 2026-10-19) already carries it, at the bottom of a connection form
  that scrolls inside the dialog; the likely reason it appears missing is that the Security
  group sits below the fold (plan-time finding, 2026-10-21).
- PostgreSQL connections save their TLS mode and CA certificate path the same way.
- The editor uses a monospace font at 13 px by default (the size is a setting). No line
  spacing is set anywhere, so the browser's default applies. Syntax colours come from the
  theme's shared token set, which the SSMS extension also reads.
- Nothing stops browser writing assistants or the browser's spell checker from attaching to
  the editor.
- Hard-coded colours are almost absent outside a developer-only spike page, so screens that
  do not fit the dark theme are not a matter of stray colour literals. The user will show
  the affected screens in screenshots (see Clarifications).

## Clarifications

### Session 2026-10-21

- Q: "Save SQL query with different connection string": is a tab that remembers its
  connection across reloads enough, or is a saved-query list wanted too? → A: **Remembered
  tabs are enough.** Tabs come back after a reload with their text, name and connection; Save
  and Save as keep writing a `.sql` file. No saved-query list (FR-015, Out of Scope).
- Q: What does "space after line need to review" mean? → A: **Line spacing**, the vertical
  gap between lines of SQL. The editor sets an explicit line height matched to SSMS 22, with
  the gutter numbers on the same baseline (FR-016).
- Q: Which screens do not fit the dark theme? → A: **The user will send screenshots.** The
  screens they show are fixed first and confirmed by the user; the full dark-theme walk of
  every page and dialog still runs (FR-031 to FR-034). The screenshots are an input to
  planning and are recorded here when they arrive.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Several query tabs, each on its own connection (Priority: P1)

A user opens a query against the sales database on one SQL Server, a second query against a
PostgreSQL server, and a third against a different SQL Server. Each query lives in its own
tab with its own name, text, connection, results and modified state. The user runs each one
when they want to; the result of one never lands in another. Switching tabs switches
everything that depends on the connection: the status bar, the database selector, the Schema
panel, completion, formatting and analysis. Closing the browser and reopening the page brings
every tab back, with its text and its connection, ready to run.

**Why this priority**: It is the working model SSMS users expect and the reason the rest of
the feature exists. Without tabs, "save a query with its own connection and run it
separately" has nowhere to live.

**Independent Test**: Open three tabs on three different servers (at least one of them
PostgreSQL), run a different query in each, reload the page, and confirm that every tab still
shows its own text, name, connection and the results of its own run after the user runs it
again.

**Acceptance Scenarios**:

1. **Given** the editor page is open with one tab, **When** the user chooses New query,
   **Then** a second tab opens with the next unused default name, starts on the same
   connection as the tab that was active, and becomes the active tab.
2. **Given** two tabs on two different servers, **When** the user runs the query in the first
   tab, **Then** its results, messages and problems appear in that tab's results pane and the
   second tab's results pane is unchanged.
3. **Given** a tab connected to a PostgreSQL server and a tab connected to SQL Server,
   **When** the user switches between them, **Then** the status bar, the database selector,
   the Schema panel, completion, hover help, Format and Analyse all follow the active tab's
   server within one second, and the editor text of the other tab is untouched.
4. **Given** a tab with unsaved changes, **When** the user closes it, **Then** the app asks
   whether to save, discard or cancel, and the tab closes only on save or discard.
5. **Given** five open tabs, **When** the user reloads the page, **Then** all five tabs
   return in the same order, with the same names, text, modified marks and connection
   bindings, and the active tab is the one that was active before.
6. **Given** a restored tab whose connection needs a password that is not remembered,
   **When** the user runs its query, **Then** the app asks for the password once, connects,
   runs, and does not ask again for that tab while the page stays open.
7. **Given** a tab with no connection, **When** the user runs its query, **Then** the app
   opens the Connect to database dialog and, after a successful connect, runs the query.
8. **Given** two tabs that use the same saved connection, **When** the user creates a
   temporary table or opens a transaction in one of them, **Then** the other tab does not see
   it (each tab has its own live connection, as in SSMS).
9. **Given** a tab whose query is running, **When** the user switches to another tab and
   runs a query there, **Then** both runs proceed and each tab shows its own running state and
   its own outcome.
10. **Given** any tab, **When** the user runs a query, **Then** SQL History records it under
    that tab's name, and the re-run grouping of History keeps that tab's runs together.
11. **Given** a tab, **When** the user chooses Save or Save as, **Then** the file is saved as
    today and the tab takes the saved name; opening a `.sql` file opens it in a new tab unless
    the active tab is empty and unmodified, in which case it is opened there.

---

### User Story 2 - Query text that reads well in light and dark (Priority: P2)

A user writes SQL for hours in the web editor. The font is clear, lines are spaced so the
text is easy to scan without looking stretched, the caret and selection are easy to find, and
keywords, strings, numbers, comments and identifiers are told apart by colours that stay
readable in the light theme, the dark theme and high contrast. Every other place that shows
SQL text (SQL History preview, the Format styles preview, AI answers) uses the same
typography, so the product looks like one product.

**Why this priority**: The user's main daily surface. It is also the groundwork for carrying
the same choices to SSMS afterwards.

**Independent Test**: Open the same script in the light theme and the dark theme at the
default font size and compare the editor with SSMS 22 at the same size: line spacing is
within tolerance, every token class meets the contrast target against the editor surface, and
the caret, selection, active line and matching brackets are visible in a screenshot.

**Acceptance Scenarios**:

1. **Given** the default settings, **When** the user looks at a 40-line script, **Then** the
   line spacing is an explicit, consistent value (not the browser's default) and the line
   numbers in the gutter sit on the same baseline as the text.
2. **Given** the light theme and then the dark theme, **When** the user views a script that
   contains keywords, strings, numbers, comments, functions, operators and
   table/column names, **Then** each class is visibly distinct and each meets a 4.5:1
   contrast ratio against the editor background.
3. **Given** any theme, **When** the user places the caret, selects text, and puts the caret
   next to a bracket, **Then** the caret, the selection, the active line and the matching
   bracket are each visible against the background and against each other.
4. **Given** the user changes the editor font size in Settings, **When** the editor repaints,
   **Then** line spacing, gutter and the completion list scale with it and nothing overlaps.
5. **Given** SQL History, the Format styles preview and an AI answer that contains SQL,
   **When** the user views them, **Then** they use the same font family, size scale and syntax
   colours as the editor.
6. **Given** a user who prefers a different editor font, **When** they choose one from the
   offered list in Settings, **Then** the editor and the other SQL surfaces switch to it.

---

### User Story 3 - One Connect to database dialog (Priority: P3)

A user wants to connect to a server. Wherever they start (the toolbar, the command palette,
an empty-state message, the status bar), the action is called "Connect to database" and opens
one dialog. The kind of server is a choice at the top; the fields below follow the kind. The
dialog reads as one tidy form in every theme. For a SQL Server with a self-signed certificate
the user ticks "Trust the server certificate without validating it", saves the connection,
and never has to tick it again: it is restored when the saved connection is chosen and
honoured on every reconnect, including the silent reconnect of a restored tab.

**Why this priority**: The product connects to two kinds of server now; the naming is
misleading and the dialog is the first thing a new user meets. The trust flag is the user's
specific request; it is implemented on this branch but not yet in the build they have.

**Independent Test**: From a fresh browser profile, connect to a remote SQL Server with a
self-signed certificate using the single entry point, save the connection with the trust
choice ticked, reload the page, pick the saved connection and connect again without touching
the checkbox. Then repeat with a PostgreSQL server from the same dialog.

**Acceptance Scenarios**:

1. **Given** any place in the web edition that offers to connect, **When** the user reads
   it, **Then** it says "Connect to database" (or "Connect to database..." where it opens the
   dialog) and no entry point is named after one kind of server.
2. **Given** the command palette, **When** the user types "connect", **Then** exactly one
   connect command is listed and it opens the dialog on the kind the active tab uses (or SQL
   Server when there is none).
3. **Given** the dialog, **When** it opens, **Then** its title is "Connect to database", the
   kind choice is the first control, and the fields are grouped as Server, Sign-in, Database
   and Security with a short hint under each group that needs one.
4. **Given** a SQL Server connection with the trust choice ticked, **When** the user saves,
   reloads the page, and picks the saved connection, **Then** the checkbox is ticked and
   Connect succeeds without a certificate error.
5. **Given** a restored tab bound to such a saved connection, **When** the tab reconnects
   silently on its first run, **Then** the trust choice is applied and no certificate error is
   shown.
6. **Given** the dialog in the dark theme and in high contrast, **When** the user views it,
   **Then** every field, hint, badge and button is readable and the layout is the same as in
   the light theme.
7. **Given** a server address that points at the engine's own machine, **When** the user
   looks at the trust choice, **Then** the dialog says the certificate is never validated for
   such a server and the choice makes no difference.
8. **Given** a saved connection of either kind in the dialog's list, **When** the user reads
   the list, **Then** each row shows its kind and its sign-in method at a glance.

---

### User Story 4 - Every screen fits the dark theme (Priority: P4)

A user who works in the dark theme opens every page and dialog of the web edition and finds
none that looks wrong: no light surfaces, no text that disappears into its background, no
control that cannot be found, nothing clipped or overflowing that fits in the light theme.

**Why this priority**: The user reports screens that do not fit; it is a quality gate on the
rest of the feature's new surfaces (tabs, the dialog, the editor) as much as a fix of old ones.

**Independent Test**: Walk every page, dialog, menu, popup, tooltip and notification in the
dark theme at 1280x720 and 1920x1080, capture each, and check that no screen fails the
checks in FR-031 to FR-034. The screens the user names are checked first.

**Acceptance Scenarios**:

1. **Given** the dark theme, **When** the user opens each page (Editor, SQL History,
   Snippets, Format styles, Settings and its sections) and each dialog (Connect to database,
   Save, Open, Cell value, confirmations, the command palette, keyboard help), **Then** every
   surface uses the dark palette and no light block remains.
2. **Given** the dark theme, **When** text is measured against its background on every
   screen, **Then** body text meets 4.5:1 and large or secondary text meets 3:1.
3. **Given** the dark theme at 1280x720, **When** the user opens each screen, **Then** no
   content is clipped or hidden behind another element that is visible in the light theme at
   the same size.
4. **Given** the screens in the user's dark-theme screenshots,
   **When** they are reopened after the fix, **Then** each passes scenarios 1 to 3 and the
   user confirms it.

---

### User Story 5 - Browser writing assistants stay out of the editor (Priority: P5)

A user who has Grammarly or a similar assistant installed types SQL in the editor. No
assistant overlay, underline or suggestion appears over the SQL, the browser's own spell
checker does not underline identifiers, and nothing auto-corrects or auto-capitalises what
the user typed.

**Why this priority**: Small and self-contained, but the user listed it first: assistants
mark SQL as misspelt English and can silently change text.

**Independent Test**: With Grammarly installed and enabled in the browser, type a 20-line
script with identifiers and keywords and confirm no overlay, underline or suggestion is shown
in the editor, while the AI chat composer (natural language) still receives them.

**Acceptance Scenarios**:

1. **Given** Grammarly (or another assistant) is installed and active, **When** the user
   types in the SQL editor, **Then** no assistant badge, underline or card appears inside or
   over the editor.
2. **Given** the browser's spell checker is on, **When** the user types identifiers such as
   `dbo.CustOrdrHist`, **Then** the editor shows no spelling underline.
3. **Given** a browser or device that auto-corrects or auto-capitalises, **When** the user
   types `select`, **Then** the text stays exactly as typed.
4. **Given** the AI chat composer, **When** the user types a question, **Then** assistants
   and the spell checker work there as before (only SQL-text surfaces are protected).

---

### Edge Cases

- **Closing a tab while its query runs**: the app asks; on confirm the tab disappears from
  the strip, a cancel is sent, and the tab's connection is released only after the run has
  ended (reply or deadline). Until then the tab still counts toward the limit of 20.
- **Changing or disconnecting a tab's connection while its query runs**: not allowed; the
  connection and database controls of that tab are disabled until the run ends (as SSMS greys
  Change Connection during execution); other tabs are unaffected.
- **Closing a tab with an open transaction**: the same warning the existing Disconnect shows
  (open or aborted transaction) is shown before the tab closes; the transaction is rolled back
  on close.
- **The engine is lost while tabs are connected**: every tab shows disconnected; each tab
  reconnects on its next run (asking for a password where one is not remembered); nothing is
  lost from any tab's text.
- **Reloading with many tabs**: text and bindings are restored at once; live connections are
  opened lazily, when a tab is activated or run, not all at page load.
- **A saved connection that a tab uses is deleted**: the tab keeps its live connection and
  its binding shows as "not saved"; the binding itself (server, database, sign-in method,
  security choices) is kept with the tab, so after a reload the tab reconnects as before and
  only the link to the saved entry is gone.
- **The open-tab limit**: the app allows up to 20 open tabs; a 21st New query is refused
  with a message that names the limit (assumption, see Assumptions).
- **Two browser windows on the same app**: both show the same tab set; the last change to a
  tab wins, as the single document behaved in spec 041. No cross-window conflict resolution.
- **Opening a file already open in another tab**: a second tab opens with the same name; the
  modified mark and Save act per tab.
- **A trust choice ticked for a server on the engine's machine**: ignored, as today; the
  dialog says so.
- **Assistant that injects into every editable area**: the editor is protected by the
  recognised opt-out signals; an assistant that ignores them is outside the product's
  control and is noted as such in the help text.
- **Very long lines and word wrap on**: line spacing stays consistent across wrapped lines.
- **Font not installed on the user's machine**: the next font in the offered stack is used
  and the layout does not shift.

## Requirements *(mandatory)*

### Functional Requirements

#### Query tabs

- **FR-001**: The editor page MUST show a tab strip of open query tabs; each tab has its own
  name, text, modified mark, connection binding, results pane content and running state.
- **FR-002**: New query MUST open a new tab with the next unused default name
  (`SQLQuery{n}`), make it active, and bind it to the active tab's connection (or no
  connection when none). The new tab opens its own live connection at once when the binding
  needs no password (Windows authentication or a remembered password), otherwise on its first
  run or an explicit Connect.
- **FR-003**: Running, parsing, formatting, analysing, completion and hover help MUST act on
  the active tab and use that tab's connection and dialect.
- **FR-004**: Each tab MUST hold its own live connection; two tabs on the same server MUST
  NOT share temporary objects, transactions or session settings.
- **FR-005**: The status bar, the database selector and the Schema panel MUST reflect the
  active tab's connection, and MUST update within one second of a tab switch.
- **FR-006**: Changing a tab's database or connection MUST affect only that tab.
- **FR-007**: Closing a tab with unsaved changes MUST ask to save, discard or cancel; closing
  a tab with an open or aborted transaction MUST warn as Disconnect warns today.
- **FR-008**: The set of open tabs MUST survive a page reload: order, names, text, modified
  marks, active tab and connection bindings are restored. Passwords are never stored in the
  browser; a tab whose connection needs one asks on its first run after a reload.
- **FR-009**: Restored tabs MUST open their live connections lazily (on activation when no
  password is needed; otherwise on the first run or an explicit Connect, so a tab switch never
  asks for a password), not all at page load.
- **FR-010**: SQL History MUST record a run under its tab's name and keep each tab's runs in
  their own session grouping.
- **FR-011**: Open file MUST open the file in a new tab unless the active tab is empty and
  unmodified; Save and Save as MUST act on the active tab.
- **FR-012**: Tabs MUST be reachable with the keyboard: the tab strip is navigable with the
  arrow keys as the product's other tab strips are, the command palette offers Next tab,
  Previous tab and Close tab, and the keyboard help lists them.
- **FR-013**: The app MUST allow up to 20 open tabs and refuse a further New query with a
  message that names the limit.
- **FR-014**: Runs in different tabs MAY overlap: starting a run in one tab while another
  tab's run is in flight MUST be allowed, and each tab MUST show its own running state and
  its own outcome (as SSMS behaves).
- **FR-015**: Remembered tabs (FR-008) are the only place a query's connection is kept; the
  product MUST NOT add a separate saved-query list, and a saved `.sql` file MUST stay plain
  SQL with no connection information in it.

#### Query text typography and colours

- **FR-016**: The editor MUST set an explicit line spacing; the gutter's line numbers MUST
  share each line's baseline; the value MUST be within 10 percent of SSMS 22's line spacing
  at the same font size (the user confirmed "space after line" means this vertical gap
  between lines).
- **FR-017**: The editor MUST use a monospace font stack chosen for clarity, and Settings
  MUST offer a short list of fonts to choose from (at least the default, Cascadia Mono /
  Cascadia Code, Consolas and the system monospace font); the existing font-size setting is
  kept.
- **FR-018**: In each theme (light, dark, high contrast) every syntax token class (keyword,
  string, number, comment, function, operator, table/column name, variable) MUST meet a 4.5:1
  contrast ratio against the editor surface and MUST be visibly distinct from the others.
- **FR-019**: In each theme the caret, the selection, the active line, matching brackets,
  find matches and analysis underlines MUST each be visible against the surface and against
  one another.
- **FR-020**: Every surface that shows SQL text (SQL History's preview and compare view, the
  Format styles preview, the snippet editor, SQL in AI answers) MUST use the same font family
  and size scale as the editor, and the same syntax colours wherever SQL is already
  highlighted. Adding highlighting to surfaces that show plain text today (History, AI
  answers) is not required.
- **FR-021**: The completion list, hover tooltips and signature help MUST use the same font
  and MUST remain readable in all three themes.
- **FR-022**: The typography and colour choices MUST be expressed in the shared theme token
  set so that the SSMS extension can adopt the same values later without a redesign.

#### One Connect to database dialog

- **FR-023**: Every entry point that opens the connection dialog MUST be named "Connect to
  database" (with a trailing ellipsis where it opens a dialog); the command palette MUST offer
  exactly one connect command, which replaces the two per-kind commands added in spec 042.
- **FR-024**: The dialog's title MUST be "Connect to database"; the kind of server MUST be
  the first control; the fields MUST be grouped as Server, Sign-in, Database and Security,
  each group with a short hint where one is needed.
- **FR-025**: Wording inside the product that means "the server you connect to" MUST be
  neutral ("server", "database"); the product names "SQL Server" and "PostgreSQL" remain only
  where a specific kind is meant (kind choice, badges, kind-specific hints and errors).
- **FR-026**: The "Trust the server certificate without validating it" choice MUST be saved
  with a SQL Server connection, restored when the saved connection is picked, and applied on
  every connect and silent reconnect that uses that saved connection.
- **FR-027**: The PostgreSQL TLS mode and CA certificate path MUST be saved, restored and
  applied the same way.
- **FR-028**: The dialog MUST explain, next to the trust choice, what the choice risks and
  that a server on the engine's own machine is never validated.
- **FR-029**: The saved-connection list MUST show each row's kind and sign-in method.
- **FR-030**: The dialog MUST read the same in the light theme, the dark theme and high
  contrast: the same layout, every control and hint readable, and the keyboard order top to
  bottom.

#### Dark theme fit

- **FR-031**: Every page, dialog, menu, popup, tooltip, notification and empty state of the
  web edition MUST use the dark palette in the dark theme; no surface may remain light.
- **FR-032**: In the dark theme body text MUST meet 4.5:1 and large or secondary text 3:1
  against its background on every screen.
- **FR-033**: No content that fits at 1280x720 in the light theme may be clipped, hidden or
  overflowing in the dark theme at the same size.
- **FR-034**: The screens shown in the user's dark-theme screenshots MUST be fixed first and
  each confirmed by the user against the same screenshot; the list of those screens is
  recorded under Clarifications when the screenshots arrive.
- **FR-035**: The high-contrast theme MUST keep passing the same checks after the changes.

#### Writing assistants

- **FR-036**: The SQL editor and every other input that takes SQL text (the snippet body
  and the Format styles preview input, which already turn the spell checker off) MUST carry
  the recognised opt-out signals so that browser writing assistants and the browser's spell
  checker, auto-correction and auto-capitalisation do not attach to them.
- **FR-037**: Natural-language inputs (the AI chat composer, search boxes, names) MUST keep
  the browser's assistance as it is today.
- **FR-038**: The help text MUST note that an assistant which ignores the standard opt-out
  signals is outside the product's control.

### Key Entities

- **Query tab**: one open query in the editor page - name, text, modified state, connection
  binding, results, running state, position in the strip. Owns one live connection while
  bound.
- **Tab set**: the ordered list of query tabs and which one is active; restored on reload.
- **Connection binding**: what a tab remembers about its server - which saved connection (if
  any), kind, server, database, sign-in method, and the security choices (trust the
  certificate; TLS mode and CA path). Never a password.
- **Saved connection**: a named connection the user keeps in the browser; already exists and
  already carries the security choices. Shown with its kind and sign-in method.
- **Editor appearance**: the user's font family and font size, and the product's line
  spacing, caret, selection and syntax colours per theme, expressed in the shared theme
  tokens.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: With three tabs on three different servers (one PostgreSQL), 100% of runs land
  in the tab that started them across 30 runs, and no other tab's results change.
- **SC-002**: After a page reload with five open tabs, all five return with their names,
  text, order, modified marks and connection bindings; the first run in each tab needs at
  most one prompt (a password), never a re-entry of the server or database.
- **SC-003**: A tab switch updates the status bar, database selector and Schema panel within
  one second in 95% of switches.
- **SC-004**: A user can open a second query against a different server, run it, and return
  to the first in under 30 seconds without touching the connection dialog for the first tab.
- **SC-005**: In both the light and the dark theme, every syntax token class and every UI
  text style in the editor meets its contrast target (4.5:1 body, 3:1 large or secondary),
  measured on the default palette.
- **SC-006**: Line spacing at the default font size is within 10 percent of SSMS 22's, and
  the user confirms the editor "reads right" in both themes.
- **SC-007**: The command palette lists exactly one connect command; no visible string in the
  web edition names SQL Server where it means "the server".
- **SC-008**: A saved SQL Server connection with the trust choice ticked reconnects without
  a certificate error after a page reload in 100% of 10 attempts, including the silent
  reconnect of a restored tab.
- **SC-009**: Every page and dialog of the web edition passes the dark-theme checks (no
  light surface, contrast targets, no clipping at 1280x720); the screens the user named are
  confirmed fixed by the user.
- **SC-010**: With Grammarly installed and active, zero assistant overlays, underlines or
  suggestions appear in the SQL editor during a 20-line typing session, while the chat
  composer still shows them.

## Assumptions

- "Multi tab like SSMS 22" means several query tabs in the one editor page, each with its
  own connection and results pane, with SSMS's New query behaviour (a new tab inherits the
  active tab's connection). The SSMS arrangement of the workspace (spec 041) is unchanged.
- "Save SQL query with different connection string" means: a tab remembers its own
  connection, and that memory survives a reload (FR-008). The user confirmed that no
  separate saved-query list is wanted (FR-015).
- "Run it separated" is read as: each tab runs on its own connection, shows its own
  results, and may run while another tab's query is still in flight (FR-014), as in SSMS.
  Plan-time amendment (2026-10-21): the browser's link to the engine already carries several
  requests at once, but the engine answers a browser's requests one at a time, so FR-014
  needs an engine change (see plan.md); the requirement stands because without it a long
  query in one tab would also stall completion and tab switching in every other tab.
- "Space after line need to review" means line spacing (the vertical distance between lines
  of SQL), which today is the browser's default; the user confirmed this reading (FR-016).
- "Connect to DB or fit name" is implemented as "Connect to database"; the dialog's kind
  choice keeps the product names SQL Server and PostgreSQL.
- The trust choice is treated as a verification item (it exists on the branch) plus the new
  requirement that silent reconnects of restored tabs apply it.
- The tab limit of 20 bounds the number of live connections one browser holds; SSMS has no
  such limit, but each web tab costs the engine a connection.
- The offered editor fonts are a short fixed list, not a free-text font name, so the choice
  cannot produce an unreadable editor.
- Passwords stay out of browser storage, as in every earlier feature; a restored tab asks
  for one when it is needed.
- Two browser windows on the same app share the tab set with last-writer-wins, as the single
  document did; no multi-window conflict handling is added.

## Out of Scope

- Any change to the SSMS extension's editor, colours or fonts ("if it done I will ask you to
  use in SSMS also"). FR-022 only prepares for it.
- A saved-query list or library kept outside the tabs (the user chose remembered tabs
  only).
- Docking, floating or side-by-side tab layouts; tab groups; dragging tabs between windows.
- Cancelling a query mid-run (the existing timeout and caps remain the bound).
- A connection-string text box; connections are still entered field by field.
- Storing passwords in the browser.
- New PostgreSQL or SQL Server capabilities beyond what tabs and the dialog need.
- Changes to the public website or the installer other than shipping the web files.

## Dependencies

- Builds on the `042-postgresql-support` branch (PR #256), which holds the two-kind
  connection dialog and the saved trust choice; this branch stacks on it. Merge order: PR #256
  first; then rebase this branch onto `master` before opening its PR. If 042 is squash-merged,
  the rebase drops 042's commits from this branch's history.
- Closes the "multiple query tabs" deferral recorded in spec 041's clarifications.
- The engine already keeps one live connection per session and runs each session's queries
  behind its own gate; the web edition's own link to the engine already carries several
  requests at once. Plan-time amendment (2026-10-21): the engine's web transport handles a
  browser's requests one after another, so overlapping runs (FR-014) need the engine to
  answer requests concurrently; closing a tab also needs the engine to release that tab's
  session. Both are additive engine changes with no new message types; an older engine
  degrades to today's one-at-a-time behaviour.
- Verification of the trust choice and the dark-theme walk need the web edition redeployed
  from this branch to the user's machine; the installed build predates both.
- The user's dark-theme screenshots (Clarifications, Q3) name the screens FR-034 fixes
  first; planning can start before they arrive, since FR-031 to FR-033 cover every screen.
