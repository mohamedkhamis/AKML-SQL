# Feature Specification: SSMS-grade workspace for the web edition

**Feature Branch**: `041-web-ssms-ux-parity`
**Created**: 2026-10-08
**Status**: Draft
**Input**: User description: "please at akml web i want to enhance UI and UX and functionallity like (modifiy colum width like ssms ) and also can hide some parts of screen like side menu , i want to be like ssms or more professional than it , also setting and tabs need review and collect it if needed , and so on , please if you have questions let me know"

## Overview

The web edition of AKML SQL already does the hard things: it pairs with the engine, connects
to a SQL Server, runs queries, shows results you can edit in place, formats and analyses SQL,
keeps history, and talks to AI providers. What it does not yet do is *feel* like a tool a SQL
developer lives in all day. SQL Server Management Studio (SSMS) is the yardstick this feature
uses, because it is the tool the web edition's users already know.

A walk through the live web edition on 2026-10-08 (version 1.26.1007.0613, a 1440 × 900
browser window, paired with the local engine and connected to a local SQL Server;
screenshots in `baseline/`) and a review of how each screen is built found these gaps:

- **Results grid.** Every column gets the same width whatever it holds, so a three-column
  result gives `database_id` a third of the screen while a long name column gets no more.
  Columns cannot be resized or fitted to content. There are no row numbers, no cell or row
  selection, no copy, no sort and no export. Clipped values have no tooltip. Date values
  show as raw text such as `2025-10-01T23:57:23.8800000`. (`baseline/editor-results.png`)
- **Messages.** A Messages tab appears only when a batch returns more than one result set,
  so for an ordinary single query the PRINT output and row counts cannot be seen at all.
  Errors arrive as a red banner with the message text only: no line number, nothing to
  click, and any result sets produced before the failing statement are thrown away. A
  script with `GO` separators is sent as one command and fails.
- **Fixed layout.** The Schema panel (240 px), the Problems panel (320 px) and the optional
  AI panel (380 px) sit to the right of the editor and cannot be hidden, moved or resized;
  the Problems panel is shown even when there are no problems. At 1100 px wide the editor
  is squeezed to less than half the window and grows a horizontal scrollbar while the panels
  keep their size. The results pane has a fixed height, no splitter, no hide and no
  maximise. Nothing about the layout is remembered. (`baseline/editor-narrow.png`)
- **Settings spread thin.** Settings, AI providers, Schema cache and Diagnostics are four of
  the eight top-level destinations, each a single column of a different width. Row limit
  and timeout live only in the editor toolbar. The web edition has no "restore defaults",
  no settings export or import, and no way to see or undo a rule that was suppressed from
  the Problems panel. Some settings are not true: "Enable analyser" is saved but changes
  nothing; the "Reset editor session" text promises more than it does. (`baseline/settings.png`)
- **Tabs, names and dialogs.** Three tab strips (results, AI panel, style preview) each look
  and behave differently. The toolbar calls styles "Profile" while the page calls them
  "Format styles"; "Collapsed" and "Khamis Style" appear twice in the picker — the open list
  groups them under "Shared with SSMS" and "Built-in", but the closed control shows only the
  name, and the "Shared with SSMS" label is not always true. Some confirmations use the browser's own pop-ups, others have
  no confirmation at all, including removing an AI provider or clearing every cached schema.
  Status text appears in five different places. The editor has one unnamed document, so
  every history entry is called "query-01"; there is no New query, Open file, or Save with a
  name. Analyser findings never appear in the editor itself, only in the panel. The status
  bar shows the engine's version with its 40-character commit hash appended; the current
  page is not highlighted in the navigation.

This feature closes those gaps so that someone who knows SSMS can sit down at the web
edition and work without looking for anything, and so that every control on screen does
what it says. Where it is cheap to be better than SSMS (an ellipsis with the full value on
hover, sort by clicking a header, a remembered layout), it is.

**Priority mapping:** **P1** = the results grid, the results pane with SSMS execution
behaviour (F5, `GO` batches, error lines) and the workspace layout — the things the user
named; **P2** = settings consolidation including the "tell the truth" fixes, and tab and
document handling; **P3** = the professional-polish pass.

## Clarifications

### Session 2026-10-08

- Q: Does "tabs" include multiple query document tabs as in SSMS? → A: **No, not in this
  feature.** Make every tab strip consistent and add New query, Open file and Save as for
  the single document. Multiple query tabs are deferred to a later feature (User Story 5,
  FR-072, Out of Scope).
- Q: Which workspace layout model? → A: **The SSMS arrangement**: Schema panel on the
  left, editor in the centre, one results pane at the bottom with Results, Messages and
  Problems tabs, AI
  panel on the right; every region hideable and resizable; no docking (FR-040, FR-041).
- Q: How should the consolidated Settings be arranged? → A: **One Settings page with a
  section list down the left**, with Settings, AI providers, Schema cache and Diagnostics
  folded in as sections, like the Format styles page and the SSMS edition's Options window
  (FR-050).

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Result columns that behave like SSMS (Priority: P1)

A developer runs a query and reads the result. Each column is as wide as its content needs,
up to a sensible maximum, so narrow columns stay narrow and long text does not push the rest
off-screen. They drag a column edge to make it wider, double-click an edge to fit it to its
content, and the widths stay put while they scroll or resize the window.
Row numbers run down the left. They click a row number to select the row, a column header to
select the column, the corner to select everything, and press Ctrl+C to copy, or
Ctrl+Shift+C to copy with headers, and paste straight into a spreadsheet.

**Why this priority**: Column width was the first thing the user named. Reading results is
what the editor is for, and today every result looks wrong at first glance and cannot be
copied out.

**Independent Test**: Run `SELECT name, database_id, create_date FROM sys.databases` and
confirm the three columns are sized to their content, resize and auto-fit them, select
rows/columns/all, copy with and without headers into a spreadsheet. Delivers value on its
own, with no change to layout or settings.

**Acceptance Scenarios**:

1. **Given** a result with a short integer column and a long text column, **When** it is
   first shown, **Then** the integer column is only as wide as its header or widest value,
   the text column is wider, and no value in a column below the maximum width is cut off.
2. **Given** a column whose widest value exceeds the maximum width, **When** it is shown,
   **Then** the column stops at the maximum, the value ends in an ellipsis, and hovering
   the cell shows the full value.
3. **Given** a result is shown, **When** the user drags the right edge of a column header,
   **Then** the column follows the pointer live, the other columns keep their widths, and
   the grid gains a horizontal scrollbar if the total now exceeds the pane.
4. **Given** a column was narrowed by hand, **When** the user double-clicks its right edge,
   **Then** it returns to its content-fitted width.
5. **Given** the user resized two columns, **When** they scroll to the bottom of a
   1,000-row result and back, resize the browser window, or hide the Schema panel,
   **Then** the two columns keep the widths they set.
6. **Given** a result is shown, **When** the user clicks row number 3, then Shift-clicks
   row number 6, **Then** rows 3–6 are selected; **When** they press Ctrl+C and paste into a
   spreadsheet, **Then** four rows land in separate cells with NULL shown as `NULL`.
7. **Given** a result is shown, **When** the user clicks the header of one column and
   presses Ctrl+Shift+C, **Then** the clipboard holds the column name on the first line and
   every value below it.
8. **Given** a result of 30 columns, **When** the user chooses "Auto-fit all columns" from
   the header menu, **Then** every column is content-fitted within the maximum width.
9. **Given** an editable result, **When** the user clicks a cell once, **Then** it is
   selected but not editing; **When** they press Enter, F2 or double-click, **Then** it
   edits; **When** they press Escape, **Then** the edit is cancelled and the fetched value
   is back.
10. **Given** a result is shown, **When** the user right-clicks a cell or presses Shift+F10
    on it, **Then** a menu offers Copy, Copy with headers, Copy as, Select all, View value,
    Save results as CSV… and Save results as tab-delimited…, and Set to NULL only when the
    result is editable; **When**
    they choose View value on a cell cut by the maximum width, **Then** a dialog shows the
    whole value with a Copy button.
11. **Given** a result is shown, **When** the user clicks a column's sort control once,
    twice and three times, **Then** the rows are shown ascending, then descending, then in
    the original order, and any pending edits stay on their rows; **When** they run the
    query again, **Then** no sort is applied.

---

### User Story 2 - Results and Messages like SSMS (Priority: P1)

A developer runs a batch that returns two result sets, prints a message and updates rows.
Below the editor they see **Results** and **Messages** tabs, whatever the number of result
sets. Results stacks the two grids with their own headers and widths. Messages lists
"(3 rows affected)", the PRINT text, the completion time and any error, and clicking an
error moves the caret to the offending line. When one statement fails, the results and
messages produced before it stay on screen. A `datetime` reads `2025-10-01 23:57:23.880`
and a `date` reads `2025-10-01`, as in SSMS. A running query shows that it is running and
for how long. They press F5 to run, as
they do in SSMS, and save a result set as a CSV file with headers.

**Why this priority**: SSMS users look for the Messages tab the moment something goes
wrong. Today the web edition hides messages for the most common case (one result set) and
tells the user nothing about where an error is.

**Independent Test**: Run a batch with two SELECTs, a PRINT and an UPDATE, then a query with
a deliberate error on line 4, and check both tabs, the click-to-line behaviour, the kept
earlier results, date formatting, F5, and the saved CSV. Independent of column resizing and
layout changes.

**Acceptance Scenarios**:

1. **Given** a single SELECT, **When** it finishes, **Then** the Results and Messages tabs
   are both present, Results is active, and Messages holds "(16 rows affected)" and a final
   `Completion time:` line.
2. **Given** a batch returns two result sets, **When** it finishes, **Then** the Results tab
   shows two grids one above the other, each with its own header row and column widths,
   separated by a visible divider, and the Messages tab shows one "rows affected" line per
   statement in the order they ran.
3. **Given** a batch prints "step 1 done", **When** it finishes, **Then** Messages shows
   "step 1 done" and a final `Completion time:` line.
4. **Given** a query fails on line 4, **When** it finishes, **Then** Messages opens by
   itself and shows the error with its line number, error number and severity; **When**
   the user clicks the error, **Then** the editor caret moves to line 4 and that line is
   highlighted. **When** the user had run a selection, **Then** the line is counted within
   the whole document, not the selection.
5. **Given** a batch whose second statement fails, **When** it finishes, **Then** the first
   statement's result set and messages are still shown, followed by the error.
6. **Given** columns of type datetime, date and datetime2(7), **When** a result is shown,
   **Then** they read `2025-10-01 23:57:23.880`, `2025-10-01` and
   `2025-10-01 23:57:23.8800000` respectively, and NULL reads `NULL` in a muted style;
   **When** the user edits a cell, **Then** the full-precision value is what they edit.
7. **Given** a query is running, **When** it starts, **Then** the results area and the status
   bar show a running indicator at once with the elapsed time counting up from zero (still
   counting when checked at two seconds), the previous results are no longer presented as
   current, and Execute is not offered again until the query finishes.
8. **Given** the editor or the results pane has focus, **When** the user presses F5,
   **Then** the query runs and the browser does not reload; Ctrl+Enter still works.
9. **Given** a result set is shown, **When** the user chooses "Save results as CSV",
   **Then** a file downloads with a header row and one line per returned row, and the user
   is warned first if the result was cut at the row limit.
10. **Given** a script of three `GO` batches whose second batch fails, **When** it finishes,
    **Then** Results holds the first and third batches' result sets, Messages shows the
    error between them with its line number counted in the whole document, and clicking it
    lands on that line.
11. **Given** a selection of cells, **When** the user chooses Copy as › JSON (and likewise
    CSV, Markdown table and INSERT statements), **Then** the clipboard holds the selection
    in that form, the same text the SSMS edition's Copy As produces for the same cells.

---

### User Story 3 - A workspace I can shape (Priority: P1)

A developer wants the editor to have the whole window. The Schema panel sits on the left as
in SSMS, Problems is a tab beside Results and Messages in the results pane, and the AI
panel is on the right. They hide the Schema panel with one click (or a key), close the AI
panel, and drag the results pane taller. When they need the schema again a thin edge strip brings
it back. Next morning the browser opens with the
same layout. On a small laptop the side panels fold away by themselves instead of crushing
the editor. "Reset layout" puts everything back.

**Why this priority**: Hiding parts of the screen was the user's second named ask. A fixed
940 px of side panels makes the editor unusable on smaller windows and is the main reason
the web edition feels less professional than SSMS.

**Independent Test**: Toggle each panel by click and by key, drag each splitter, reload the
page, narrow the window below the fold-away width, and reset the layout. Independent of
grid and settings work.

**Acceptance Scenarios**:

1. **Given** the editor page is open, **When** the user hides the Schema panel and the AI
   panel, **Then** the editor widens to fill the space and a thin strip at each edge shows
   the name of the hidden panel; **When** they click a name, **Then** that panel returns at
   its previous size.
2. **Given** the results pane is visible, **When** the user presses the results toggle key,
   **Then** the pane hides and the editor takes its height; **When** they execute a query,
   **Then** the results pane comes back on its own.
3. **Given** a results pane, **When** the user drags the splitter between editor and
   results, **Then** both areas resize live, neither can shrink below a minimum that keeps it
   usable, and the splitter can also be moved with the keyboard when focused.
4. **Given** the user chose "Maximise results", **When** the results pane fills the editor
   area, **Then** one click or key restores the previous split.
5. **Given** the user changed panel visibility and sizes, **When** they reload the page or
   open the editor the next day in the same browser, **Then** the layout is as they left
   it, fitted to the current window, including whether the AI panel was open.
6. **Given** the window is narrower than the fold-away width, **When** the editor page
   opens, **Then** the side panels are folded to the edge strip and the editor keeps at least
   its minimum width without a horizontal scrollbar for lines up to 120 characters.
7. **Given** any layout, **When** the user chooses "Reset layout", **Then** the default
   layout returns and the remembered layout is cleared.
8. **Given** the editor page, **When** the user opens the View menu, **Then** every panel
   toggle is listed with its key, and the same commands appear in the Command Palette.
9. **Given** no SQL connection, **When** the Schema panel would be empty, **Then** it shows
   a "Connect to SQL Server…" action instead of disappearing.

---

### User Story 4 - Settings in one place, and every setting true (Priority: P2)

A developer wants to change the theme, the default row limit, the AI privacy mode and clear a
cached schema. They open **Settings** once and find them all, grouped into sections named as
the SSMS edition names them, without visiting four different pages. Every setting shown
changes something. Each section can be reset on its own and says what the reset will remove;
all settings can be reset, exported and imported together, without ever exporting a key or
password. Query execution options still have a quick control near Execute, but the defaults
live in Settings. Rules suppressed from the Problems panel are listed and can be undone.

**Why this priority**: The user asked for settings to be reviewed and collected. Four
top-level destinations for settings is the most visible sign that the web edition grew page
by page, and a switch that does nothing destroys trust in every other switch. It is less
urgent than the grid and layout because everything is reachable today.

**Independent Test**: From a fresh browser, reach every existing setting from the single
Settings entry in at most two clicks; change one setting per section and observe its effect;
reset one section and confirm the others are untouched; export, change, import, confirm the
export held no secrets.

**Acceptance Scenarios**:

1. **Given** the navigation bar, **When** the user looks for settings, **Then** there is one
   Settings entry, and AI providers, Schema cache and Diagnostics are no longer separate
   top-level entries.
2. **Given** Settings is open, **When** the user reads the section list, **Then** every
   setting the web edition has today appears under one of: General, Editor, Format,
   Queries, Code analysis, AI assistance, Connections, Schema cache, Diagnostics.
3. **Given** the user sets the default row limit to 500 in Settings › Queries, **When** they
   return to the editor, **Then** the quick control near Execute shows 500 and the next
   execution uses it; **When** they change the quick control in the toolbar, **Then** the
   saved default is unchanged.
4. **Given** "Enable code analysis" is switched off, **When** the user clicks Analyse or
   formats with "re-analyse after format" on, **Then** no analysis runs and the Problems
   tab says analysis is off and offers to turn it on.
5. **Given** a rule was suppressed everywhere from the Problems panel, **When** the user
   opens Settings › Code analysis, **Then** the rule is listed as suppressed with an Undo
   that restores it.
6. **Given** three AI providers are configured, **When** the user resets the AI assistance
   section, **Then** the confirmation says "This removes your 3 providers and their keys",
   and after confirming the General and Connections sections are unchanged.
7. **Given** the user exports settings, **When** they open the file, **Then** it holds no
   API key, password or pairing token; **When** they import it in another browser,
   **Then** theme, editor, query, analysis and AI preferences and the provider list (without
   keys, each provider marked as needing one) are applied, and that browser's engine and
   SQL Server connections are untouched.
8. **Given** the Format styles tool, **When** the user is in Settings › Format, **Then** the
   active style can be chosen there and "Format styles…" opens the tool; the toolbar style
   picker shows the same active style and is labelled "Style", not "Profile".
9. **Given** two choices of the same kind (on/off) in different sections, **When** the user
   compares them, **Then** they use the same control, the same label position and the same
   help-text style.
10. **Given** Settings is open on General, **When** the user types "timeout" in the filter
    box, **Then** only the settings whose label or help text contains "timeout" are shown,
    each under its section name, and the sections with no match are greyed in the list;
    **When** they clear the box, **Then** General is shown again in full.

---

### User Story 5 - Tabs and documents that behave like SSMS (Priority: P2)

A developer moves between Results and Messages, between the style editor's "Whitespace
sample" and "My SQL", and between the AI panel's Actions and Chat. Every tab strip looks the same, works
the same with the keyboard, and shows the same active marker. They start a new query, open a
`.sql` file from disk, and save the current query under a name of their choosing. History
records the query under that name. The editor keeps a single document in this feature;
multiple query tabs as in SSMS are deferred to a later feature (see Clarifications).

**Why this priority**: The user asked for tabs to be reviewed. Inconsistent tab strips are a
visible quality problem, and the lack of New / Open / Save as is a daily friction for anyone
coming from SSMS, but neither blocks work.

**Independent Test**: Visit every tab strip in the app and operate each with mouse and
keyboard; confirm identical look and behaviour. Start a new query, open a file, save under a
name, and find it under that name in History.

**Acceptance Scenarios**:

1. **Given** any tab strip in the app, **When** the user focuses it and presses Left/Right,
   **Then** the previous/next tab becomes the active tab and its content is shown at once,
   with no further key press; Home/End do the same for the first/last tab; the active tab
   is marked the same way in every strip and announced to assistive technology.
2. **Given** the Results/Messages strip and the style-preview strip, **When** shown side by
   side in a screenshot, **Then** tab height, spacing, typography and active marker match.
3. **Given** a tab has a badge (for example an error count on Messages), **When** the count
   changes, **Then** the badge updates in place and reads the same as badges elsewhere.
4. **Given** a modified document (its text differs from the last Save, Open or New query),
   **When** the user chooses New query, **Then** they are asked whether to discard it, and
   after confirming the editor is empty with the next numbered default name and no modified
   marker.
5. **Given** a `.sql` file on disk, **When** the user chooses Open and picks it, **Then** its
   text replaces the editor content and its file name becomes the document name; **When**
   the current document was modified, **Then** the same confirmation as for New query is
   asked first.
6. **Given** a document named "orders-audit", **When** the user saves and then runs it,
   **Then** the downloaded file is `orders-audit.sql` and the History entry is named
   "orders-audit", its earlier runs included; **When** they then choose New query and run,
   **Then** a new History entry appears and "orders-audit" keeps its name.

---

### User Story 6 - A shell that reads as one professional tool (Priority: P3)

A developer opens the web edition and sees a compact application bar that highlights where
they are, a toolbar with grouped commands, icons and tooltips that show keys, a database
selector like SSMS's, and a status bar with the segments SSMS shows (connection, server and
database, execution result, caret position) and a short version. Lists on every page share
one table style; buttons, dialogs and notifications share one look; nothing uses the
browser's own pop-ups. Empty panes say what to do next. Analyser findings are underlined in
the editor as well as listed in the panel. The style picker tells "Collapsed (built-in)" from
"Collapsed (on this engine)". The browser tab is titled after the query and connection.

**Why this priority**: These are the finishing touches that make the first impression
professional. They matter, but each is small and none blocks a workflow.

**Independent Test**: Compare the editor page, History, Snippets, Schema cache and
Diagnostics pages at the same window size and confirm shared header, toolbar, table,
dialog, notification and empty-state styles; hover every toolbar button; switch database
from the toolbar; read the status bar; trigger an analyser finding and see it in the editor.

**Acceptance Scenarios**:

1. **Given** the editor toolbar, **When** the user hovers Execute, **Then** a tooltip shows
   the command name and its keys; commands are visibly grouped (database selector; execute
   and parse; format/analyse; refactor/AI; file) with separators.
2. **Given** a SQL connection, **When** the user opens the toolbar database selector and
   picks another database, **Then** the connection switches to it, the Schema panel and
   IntelliSense follow, and the status bar shows the new database.
3. **Given** the status bar, **When** a query has run, **Then** it shows "Query executed
   successfully.", the connection state, server/database, "16 rows · 87 ms" and the caret
   position "Ln 1, Col 12"; the web and engine versions show as `1.26.1007.0613` and the
   commit hash appears only on hover or in Diagnostics.
4. **Given** the style picker, **When** two styles share a name, **Then** each entry shows
   its group so the two can be told apart, and the group is called "On this engine" unless
   the engine is known to be the user's own SSMS engine.
5. **Given** the Schema cache, Diagnostics, Snippets and History pages, **When** compared,
   **Then** their page headers, action buttons and tables use the same sizes, spacing and
   colours, and the current page is highlighted in the navigation.
6. **Given** any destructive action (remove a provider, remove an engine, delete a saved
   connection, clear all cached schemas, delete a style, clear history), **When** the user
   triggers it, **Then** an in-app confirmation names what will be removed; no browser
   pop-up is used anywhere.
7. **Given** the analyser reports a warning on line 7, **When** the Problems panel lists it,
   **Then** line 7 is also underlined in the editor and hovering shows the message.
8. **Given** the results pane before any execution, **When** shown, **Then** it reads
   "Run a query to see results here (F5)" rather than an empty box.
9. **Given** any new surface, **When** viewed in Light, Dark and High contrast,
   **Then** text meets the contrast target, focus is visible, edited and deleted grid rows
   carry a glyph as well as a colour, and the saved theme is applied before the first
   paint (no flash of another theme).
10. **Given** the Schema panel for a database with 300 tables, **When** the user types
    "ord" in its filter box, **Then** only objects whose name contains "ord" remain, under
    their schema and kind nodes, each with its kind's icon; **When** they clear the box and
    click Refresh, **Then** the whole tree returns and reloads from the server, with a
    loading state meanwhile.

---

### Edge Cases

- A result with 200 columns: the grid must stay responsive while auto-fitting, and
  auto-fit may sample rather than measure every row, as long as visible values are not cut.
- A column whose header is longer than every value: the header sets the width, never
  truncated below readability.
- A column with no name: the header reads "(No column name)" as in SSMS.
- A result with zero rows: headers still show, sized to the header text; Messages says
  "(0 rows affected)".
- A statement returns no result set (DDL only): Messages shows "Commands completed
  successfully." and the Results tab says there is nothing to show.
- Two columns with the same name: both render; selection and copy use position, not name.
- A cell holding 50,000 characters, binary data or XML: the cell shows a bounded preview,
  hover shows the first part, and a "view value" action shows the whole thing. A value the
  engine itself cut (very large text or binary) says so and gives its size; it cannot be
  viewed in full and the interface does not pretend otherwise.
- A column is resized while a new result is arriving: the width applies only to the result
  it was set on; the new result is fitted afresh.
- The user hides every panel including results, then executes a query: results return
  automatically; the edge strip is always reachable.
- The window becomes narrower than the editor's minimum width: the page scrolls
  horizontally as a whole rather than hiding the toolbar's Execute control.
- A remembered layout from an older version has values the new layout does not recognise:
  the default layout is used and the stale values are discarded without an error.
- Browser zoom at 150 %: splitters, edge strips and column edges remain hit-able.
- Keyboard only: every panel toggle, splitter, tab strip, column auto-fit and column resize
  is reachable without a pointer (a focused column header resizes with Shift+Alt+Left/Right).
- Touch pointer: edge drag targets are wide enough to grab; nothing depends on hover alone
  (the full cell value is also available through the "view value" action).
- The grid is in edit mode (an editable result): a single click selects a cell, a second
  click, Enter or F2 edits it; Escape cancels; Tab moves to the next editable cell; a cell
  can be set to NULL explicitly; pending edits are never discarded without a warning when
  the user runs again or moves to another page of the app.
- A result arrives while the user is sorting or has a selection: the selection is cleared,
  widths are recomputed for the new result, and user-set widths apply only to the result
  they were set on.
- A script contains `GO` in the middle of a string or comment: it is not treated as a
  batch separator, as in SSMS.
- F5 is pressed while focus is in the browser's address bar or in Settings: the browser
  reloads as usual; the key is claimed only on the editor page.
- Settings reset while another browser tab of the app is open: the other tab picks up the
  new values on its next load and never writes stale values back.
- The app is open in two browser tabs at once: both show and change the one document, and
  what is remembered (text and name) is the most recent change made in either tab. A tab
  writes the document only when the user changes it in that tab; loading, reloading or
  restoring never writes it back, so an untouched tab cannot replace a newer change from
  another tab.
- An import file was produced by a newer version: unknown settings are ignored, known ones
  are applied, and the user is told how many were skipped.

## Requirements *(mandatory)*

### Functional Requirements

#### Results grid — columns, selection, copy, editing (User Story 1)

- **FR-001**: On first display, each column MUST be as wide as the wider of its header
  and its widest returned value, up to a maximum width, and never narrower than a minimum
  that keeps the header readable.
- **FR-002**: Values wider than the maximum column width MUST be cut with an ellipsis, and
  the full value MUST be available on hover and through a "view value" action.
- **FR-003**: Users MUST be able to resize a column by dragging the right edge of its
  header; the pointer MUST change to a resize cursor over the edge, and the width MUST
  follow the pointer live. A focused column header MUST also resize in steps with
  Shift+Alt+Left/Right.
- **FR-004**: Double-clicking a column's right edge MUST fit that column to its content.
- **FR-005**: A header menu MUST offer "Auto-fit this column", "Auto-fit all columns",
  "Reset column widths", "Copy column name" and the sort commands.
- **FR-006**: Column widths MUST be kept while the user scrolls (including through a long
  result whose rows are drawn as they come into view), resizes the results pane or the
  window, or shows and hides panels; they MUST NOT reflow to equal widths.
- **FR-007**: When the total column width exceeds the pane, the grid MUST scroll
  horizontally with the header row staying visible and aligned, and row highlights MUST
  span the full row width.
- **FR-008**: The grid MUST show a row-number column at the left. Clicking a cell MUST
  select that cell, in read-only and editable results alike; clicking a row number MUST
  select the row; clicking a column header MUST select the column; clicking the top-left
  corner MUST select all. Shift-click MUST extend and Ctrl-click MUST add or remove
  a row, column or cell.
- **FR-009**: Ctrl+C MUST copy the selection as tab-separated text with one line per row;
  Ctrl+Shift+C MUST copy the same with a header line holding only the copied columns'
  headers in grid order; NULL MUST copy as `NULL`. Copied text MUST be the text the grid
  shows, except that a value cut by the maximum column width is copied in full and a value
  the engine itself cut is copied as its indicator with its size, and the user MUST be told
  how many such values the copy holds. A line break inside a value MUST be replaced by a
  space so the row stays on one line, as SSMS does by default, unless the Settings › Queries
  option to retain line breaks is on.
- **FR-010**: Arrow keys, Home/End, Page Up/Down, Ctrl+Home/End and Ctrl+A MUST move the
  cell focus and selection as in a spreadsheet; Shift+Arrow MUST extend the selection from
  the focused cell, and dragging the pointer across cells MUST select the rectangle between
  the first and the current cell.
- **FR-011**: Column data types MUST be available on header hover. Showing the type as a
  permanent second header line MUST be a setting, off by default.
- **FR-012**: Clicking a column header's sort control MUST sort the returned rows by that
  column ascending, again for descending, and a third time to restore the original order;
  the sort applies to the returned rows only and is cleared on the next execution. Rows
  MUST be ordered by the column's value as SQL Server would order it (numbers as numbers,
  dates as dates, NULL first), not by the text shown in the cell.
- **FR-013**: In an editable result, a single click MUST select a cell; a second click (a
  double-click counts), Enter or F2 MUST start editing; Escape MUST cancel the edit; Tab and Shift+Tab MUST move
  to the next and previous editable cell; typing a printable character on a selected
  editable cell MUST start editing with that character replacing the value, as SSMS's Edit
  Rows grid does.
- **FR-014**: Users MUST be able to set an editable cell to NULL with a command and a key
  (Ctrl+0, as in SSMS); the command MUST be disabled, with the reason shown, on columns that
  do not allow NULL. An emptied cell MUST be saved as an empty string for character types;
  for every other type an emptied cell is invalid and MUST be marked so until a value is
  entered or NULL is set, and Apply MUST be blocked while any cell is invalid. NULL and an
  empty string MUST be shown differently.
- **FR-015**: Pending edits MUST NOT be discarded without a confirmation when the user runs
  a query, opens a file, starts a new query or moves to another page of the app; applying or
  discarding the edits of one result set MUST NOT touch the pending edits of another.
- **FR-016**: Existing editing, apply-changes and row-marker behaviour MUST continue to work
  with selection, copy, sorting and resizing in place; edited, new and deleted rows MUST
  carry a glyph as well as a colour. A sort changes only the order in which rows are shown:
  every pending edit, deletion and new row MUST stay on the row it was made on, and Apply
  MUST write to that row.
- **FR-017**: Right-clicking a cell, a row number or the selection (and Shift+F10 or the
  Menu key from the keyboard) MUST open a grid context menu with, in this order: Copy, Copy
  with headers, Copy as ▸ (FR-031), Select all, View value (FR-002), Set to NULL (FR-014;
  editable results only), Save results as CSV… and Save results as tab-delimited…
  (FR-030), each showing its key. "View
  value" MUST open an in-app dialog (FR-086) holding the whole value as text with its size
  and a Copy button; for a value the engine itself cut it MUST say so (see Edge Cases).

#### Results pane — tabs, messages, execution (User Story 2)

- **FR-020**: The results pane MUST always have **Results**, **Messages** and **Problems**
  tabs, whatever the number of result sets. After each execution the active tab MUST follow the
  outcome, as in SSMS: Results when at least one result set came back and nothing failed,
  otherwise Messages (no result set, or an error — FR-023); the tab restored by FR-045 holds
  only until the next execution.
- **FR-021**: Results MUST stack every result set of a batch in order, each with its own
  header row and column widths, separated by a visible divider, with a way to jump to the
  n-th set when there are many. An editable result set in the stack keeps its own pending
  edits and its own Apply and Discard controls; Apply commits one result set at a time.
- **FR-022**: Messages MUST list, in execution order: one "(N rows affected)" line per
  statement that reports one (for a SELECT cut at the row limit the line reads "(N rows
  returned; more exist)" and never claims an exact total); every PRINT or informational
  message; each error in SSMS's form — `Msg <number>, Level <severity>, State <state>,
  Line <n>` followed by the message text, with `Procedure <name>,` before `Line` when the
  error was raised inside a stored procedure, trigger or function (its line is counted
  inside that object), and otherwise with <n> the line the user can find in the document
  even in a script of several `GO` batches; and, as the last line, `Completion time:` with
  the local date and time, as SSMS prints it. The elapsed time is shown in the status bar
  (FR-083).
- **FR-023**: Clicking an error in Messages MUST move the editor caret to the line of the
  document the error refers to and highlight it; the server counts lines from the start of
  the batch it ran, so the line MUST be translated into a document line whether the error
  came from a selection or from a batch after a `GO`, as SSMS does. An error raised inside a
  stored procedure, trigger or function, or one the server reports without a line, has no
  document line, and clicking it MUST NOT move the caret. When an execution fails the
  Messages tab MUST become active and the same underline and gutter marker the analyser
  uses (FR-087) MUST appear on the error's document line in the editor.
- **FR-024**: When a statement fails, the result sets and messages produced before it MUST
  be kept and shown, followed by the error; the elapsed time MUST be reported.
- **FR-025**: Scripts containing `GO` batch separators MUST run batch by batch as SSMS does.
  `GO` is a separator only on a line of its own (any letter case, optionally followed by a
  repeat count and a comment), and `GO 3` runs its batch three times. A failing batch MUST
  NOT stop the batches after it: they still run, and their results and messages follow the
  error in order, as in SSMS; only an error that closes the connection, a batch that times
  out, or a cancel stops the script, and Messages MUST then say that the remaining batches
  did not run. A `GO` line holding anything other than a repeat count and a comment
  (`GO SELECT 2`, `GO;`, `GO 0`) refuses the whole script before any batch runs, with SSMS's
  message "Incorrect syntax was encountered while parsing GO." Every row count, message and
  error MUST be attributed to the batch that produced it.
- **FR-026**: Values MUST display as SSMS does, type by type: `date` as `yyyy-MM-dd`;
  `smalldatetime` as `yyyy-MM-dd HH:mm:ss`; `datetime` as `yyyy-MM-dd HH:mm:ss.fff`; `time`,
  `datetime2` and `datetimeoffset` with the number of fractional-second digits the column
  declares (so `datetime2(7)` shows seven and `time(0)` none), `datetimeoffset` followed by
  a space and its offset (`2025-10-01 23:57:23.8800000 +02:00`), as SSMS shows it; binary
  types as `0x` and upper-case hexadecimal; uniqueidentifier
  in upper case; decimal and numeric with the column's declared scale (`12.50`), money with
  four decimal places (`12.5000`); bit as 1/0; NULL as `NULL` in a muted style. Editing a
  cell MUST show and preserve the full-precision value.
- **FR-027**: While a query runs, the results pane MUST show a running indicator with
  elapsed time, the previous results MUST no longer be presented as current, the status bar
  MUST show the running state, and Execute MUST NOT be offered again — by button, key or
  Command Palette — until the query finishes, so a second trigger cannot start or queue
  another run. If an execution cannot be interrupted, the interface MUST say so rather than
  offer a Cancel that does nothing. The running state MUST end when the engine reports a
  result or an error; when the engine connection is lost (Messages MUST then say the
  connection dropped and that the statement may still have run); or when no reply arrives
  within the command timeout plus a short grace period (Messages MUST then say the result
  was not received). In every case Execute MUST be offered again.
- **FR-028**: F5 MUST execute the query whenever focus is anywhere on the editor page
  (editor, toolbar, Schema panel, results pane, AI panel, status bar) without reloading the
  browser — not on other pages, and never when focus is in the browser's own controls,
  which the page cannot see; Ctrl+Enter MUST keep working; Execute MUST be offered in the
  Command Palette.
- **FR-029**: When a result is cut at the row limit, the grid MUST say how many rows were
  returned and that more exist; the status bar shows the returned row count and time as
  FR-083 requires.
- **FR-030**: Users MUST be able to save a result set as a file through one menu item per
  format, comma-delimited (CSV) or tab-delimited, the two formats SSMS's "Save Results As"
  offers; the file MUST have a header row; a value containing the delimiter, a double quote
  or a line break MUST be enclosed in
  double quotes with inner quotes doubled; NULL MUST be written as `NULL`; the file MUST be
  named after the document; and the user MUST be warned before saving a result that was cut
  at the row limit or that holds values the engine cut.
- **FR-031**: Users MUST be able to copy a selection or a result set "as" CSV, JSON,
  Markdown table and INSERT statements, formatted as the SSMS edition's results-grid
  "Copy As" menu formats them. That menu's other formats (TSV, XML, HTML table and IN
  clause) are not required here; tab-separated copy is FR-009.
- **FR-032**: The results pane MUST have a splitter against the editor, a hide/show toggle
  with a key, and a maximise/restore command; a hidden results pane MUST reappear on the
  next execution (FR-034 says when an analysis brings it back).
- **FR-033**: Before any execution the results pane MUST show a short instruction rather
  than an empty area.
- **FR-034**: When the user runs Analyse, or a format re-analyses, the results pane MUST be
  shown if it was hidden and the Problems tab MUST become active when there is at least one
  finding; with no finding the pane and active tab stay as they were, the Problems badge
  clears, and the notification area (FR-086) says "No problems found". The badge MUST count
  the findings that pass the current severity filters (FR-055). While code analysis is off,
  Analyse and the Problems tab MUST say that analysis is off and offer to turn it on, and
  the badge MUST be hidden.
- **FR-035**: The toolbar MUST offer Parse (Ctrl+F5, claimed on the editor page like F5),
  which checks the document, or the selection, for syntax errors on the server without
  executing anything and writes to Messages either `Commands completed successfully.` or
  each error in the FR-022 form, with FR-023's click-to-line.

#### Workspace layout (User Story 3)

- **FR-040**: The editor page MUST arrange its regions as SSMS does: the Schema panel on
  the left, the editor in the centre, one results pane at the bottom holding the Results,
  Messages and Problems tabs, and the AI panel on the right. The Problems tab MUST show its count as a
  badge so it is useful without taking a column of its own. Panels have fixed homes; they
  are not dockable or floating.
- **FR-041**: Each of these regions MUST be individually hideable and showable: the
  navigation bar, the Schema panel, the AI panel and the results pane. The toolbar and
  status bar MUST always stay visible.
- **FR-042**: Every hide/show toggle MUST be available from a View menu on the editor page,
  from a keyboard shortcut shown next to it, and from the Command Palette.
- **FR-043**: Hidden side panels MUST leave a thin edge strip naming them, and clicking a
  name MUST restore that panel at its previous size, pinned back in place (SSMS's hover
  fly-out is not offered); the strip's tooltip MUST read "Show <panel> (<key>)".
- **FR-044**: Side panels and the results pane MUST be resizable by dragging a splitter and
  by keyboard when the splitter has focus; each region MUST have a minimum size that keeps
  it usable.
- **FR-045**: Panel visibility and sizes, the AI panel's open state and selected tab, and
  the selected results tab MUST be remembered in the browser and restored on the next
  visit, with sizes fitted to each region's minimum and to the current window; a remembered
  layout that would leave the editor or the results pane below its minimum, or every region
  hidden, MUST be corrected on load. A browser tab MUST write only the layout fields the
  user changed in it, never the whole layout on load. "Reset layout" MUST restore the
  defaults and clear the remembered layout.
- **FR-046**: Below a fold-away window width, side panels MUST fold to the edge strip on
  their own so the editor keeps its minimum width; widening the window MUST bring them back
  if the user had not hidden them.
- **FR-047**: A remembered layout the current version does not recognise MUST be discarded
  silently in favour of the default.
- **FR-048**: The Schema panel MUST NOT disappear when there is no SQL connection; it MUST
  show a "Connect to SQL Server…" action instead.
- **FR-049**: Keyboard shortcuts MUST meet four rules: (a) where the browser lets the page
  claim the key, the SSMS key is used — F5 execute, Ctrl+F5 parse, Ctrl+R toggle results,
  F8 Schema panel, F6 cycle panes; (b) for SSMS keys the browser reserves (new tab, close
  tab, switch tab) the command gets an alternative that is shown in the menus; (c) every key
  the page claims stops the browser's own action for it while the editor page has focus and
  is verified in the supported browsers before release — a key that cannot be claimed gets
  an alternative under (b); (d) no shortcut collides with the editor's own keys (today the
  Format chord also opens Find).

#### Settings (User Story 4)

- **FR-050**: The navigation MUST have exactly one Settings entry, which opens one Settings
  page with a section list down the left; the current Settings, AI providers, Schema cache
  and Diagnostics pages MUST become sections of it, laid out like the Format styles page
  and the SSMS edition's Options window. Each section MUST be reachable by its own address
  so links and the Command Palette can open it directly; the existing addresses for AI
  providers, Schema cache and Diagnostics MUST keep working by opening the matching section,
  and the Command Palette's navigation entries MUST point at the sections.
- **FR-051**: Every setting the web edition offers today MUST appear under exactly one of
  these sections, named as the SSMS edition's Options window names them where both editions
  have the setting: General, Editor, Format, Queries, Code analysis, AI assistance,
  Connections, Schema cache, Diagnostics.
- **FR-052**: Settings › Queries MUST hold the default row limit, command timeout, the
  "show column types" option and the "retain line breaks on copy or save" option (off by
  default, as in SSMS); the editor toolbar MUST keep a compact quick control that
  shows the current row limit and timeout. A change made there applies to every run for the
  rest of the visit, is not remembered for the next visit, and never changes the saved
  defaults.
- **FR-053**: Settings › General MUST hold the theme choice; Settings › Editor MUST hold at
  least font size, word wrap, tab size and the "reset editor session" action, whose text
  MUST describe exactly what it clears and which MUST ask for confirmation naming the
  document it discards.
- **FR-054**: Settings › Format MUST let the user choose the active style and open the
  Format styles tool; the toolbar style picker and this setting MUST always agree, both
  MUST be labelled "Style", and deleting a style MUST fall back to the same default style
  wherever it is deleted from.
- **FR-055**: Settings › Code analysis MUST hold the enable switch (which MUST stop
  analysis when off), the re-analyse-after-format switch, the default Problems filters, and
  a list of rules suppressed everywhere with an Undo for each.
- **FR-056**: Settings › AI assistance MUST hold providers, the active provider, privacy
  modes and ghost-text options, with provider hints that match what the form allows.
- **FR-057**: Settings › Connections MUST hold engine connections (add, connect, remove,
  and re-pair with a new PIN) and SQL Server connections (saved connections, connect,
  disconnect, manage); the vocabulary MUST be one set of words used everywhere. A saved SQL
  Server connection MUST never store a password: SQL authentication asks for the password
  at connect and test, and no "Remember password" option is offered. Saved connections and
  the toolbar database selector MUST pass the same server check as a new connection, and a
  saved connection that fails it MUST say why rather than being removed silently.
- **FR-058**: Each section MUST have "Restore defaults" with a confirmation that names the
  section and states anything else that will be removed (for example providers and their
  keys); resetting one section MUST NOT change another.
- **FR-059**: Settings MUST offer "Restore all defaults" (every section's Restore defaults
  at once, behind one confirmation that lists everything it will remove), "Export…" and
  "Import…". The export MUST contain exactly: the General, Editor, Format (active style
  only), Queries and Code analysis settings, including the list of rules suppressed
  everywhere; the AI assistance settings with each provider's name, kind, model and
  endpoint, the active provider, the privacy modes and the ghost-text options; the
  workspace layout (FR-045); the grid options are part of Queries. It MUST NOT contain API keys, passwords,
  pairing tokens, engine connections, SQL Server connections, the schema cache, snippets,
  history or the document. An import MUST apply the known settings the file holds, leave
  settings the file does not mention unchanged, ignore unknown settings and report how many
  it skipped; an imported provider has no key until the user enters one and MUST be marked
  so.
- **FR-060**: The same kind of choice MUST use the same control everywhere (one control for
  on/off, one for a pick from a list, one for a number, one way to mark "the active one"),
  with the same label placement and help-text style; simple preferences MUST apply at once
  and say so, while multi-field forms (provider, connection) MUST have Save and Cancel.
- **FR-061**: Settings MUST have a filter box above the section list that searches every
  section at once: while text is typed, the page shows only the settings whose label or
  help text contains it, each under its section name, and greys out the sections with no
  match in the list; clearing the box returns to the section that was open, shown in full.
- **FR-062**: The Diagnostics section MUST keep the log viewer's export, clear and level
  filters, and MUST also open from the status bar's engine indicator.
- **FR-063**: Labels MUST tell the truth: a group of styles MUST be called "Shared with SSMS"
  only when the engine is the user's own SSMS engine, otherwise "On this engine" (the paired
  engine says whether it is the signed-in user's engine, the one SSMS starts, or the
  installed service; when it does not say, the group is "On this engine"); a picker group
  that can never have entries MUST NOT be shown; a choice that is not available yet
  MUST NOT be shown as a disabled "future" option.

- **FR-064**: When settings are changed, reset or imported in one browser tab of the app,
  every other open tab of the app MUST use the new values on its next load and MUST NOT
  write its stale values back over them.

#### Tabs and documents (User Story 5)

- **FR-070**: All tab strips in the app MUST share one look (height, spacing, typography,
  active marker, badge style) and one keyboard behaviour (Left/Right and Home/End move the
  focus and make the newly focused tab active at once, so its content switches without a
  further key press; Enter or Space also activates the focused tab), and MUST announce the
  active tab to assistive technology.
- **FR-071**: A tab MAY carry a badge (count or state); badges MUST update in place and look
  the same in every strip.
- **FR-072**: Users MUST be able to start a New query, Open a `.sql` file from disk, Save
  the document (which downloads it under its current name, asking for a name first only
  while the name is still the default) and Save as (which always asks for a name). A
  document is *modified* when its text differs from the text last saved, opened or created
  by New query; New query and Open MUST ask for confirmation when a modified document would
  be lost. A new document's default name MUST be numbered as SSMS numbers them (SQLQuery1,
  SQLQuery2, … counting up within the visit, never reusing the name of the document it
  replaces). The document name MUST be used for the saved file, the browser title and the
  History entry, and the name, text and modified state MUST be restored on the next visit.
  A New query and an opened file each begin a new History entry; the entries of earlier
  documents keep their names. Save as names the current document's entry, including its
  runs before the save; a name the user gave the entry in History is kept. Open MUST read
  files SSMS saved (UTF-16 with a byte-order mark) as well as UTF-8 files, and Save MUST
  write a file SSMS opens correctly; a file larger than the editor's document limit MUST be
  refused with a message naming the limit; the document name is the file name without its
  extension, with characters not allowed in file names removed.
- **FR-073**: The document name and its modified state MUST be visible in the editor
  workspace at all times, as the title of the editor area with `*` after the name while the
  document is modified (as SSMS shows it on the document tab), as well as in the browser
  title (FR-089).

#### Shell and polish (User Story 6)

- **FR-080**: The navigation bar MUST have at most five top-level entries and MUST
  highlight the current page.
- **FR-081**: Toolbar commands MUST be grouped with separators (database selector; Execute
  and Parse; Format and Analyse; Refactor and AI; New, Open and Save), show an icon and
  label, and show a tooltip with the command name and keys on hover; icons MUST come from
  one icon set, not emoji or text symbols.
- **FR-082**: The toolbar MUST show the current server and database and let the user switch
  database from a list, as SSMS's database selector does; the Schema panel, IntelliSense and
  status bar MUST follow the switch. The switch MUST NOT ask for credentials again and MUST
  keep the session's temporary tables and SET options. When a script changes database with
  `USE`, the selector MUST show the new database and the Schema panel, IntelliSense and
  status bar MUST follow it as for a switch from the list. The list MUST hold the databases
  the login can access on the connected server.
- **FR-083**: The status bar MUST show, as separate segments: the execution outcome
  (`Query executed successfully.`, `Query completed with errors.` or `Executing query…`,
  with an icon that is not the only cue), connection state, server and database (click
  opens the connection manager), the login when known, the last execution's rows and time
  (or the elapsed time counting while running), caret position, and the version; the
  build's commit hash MUST appear only on hover or in Diagnostics, and a web/engine version
  mismatch MUST be shown.
- **FR-084**: Where two styles share a display name, the style picker MUST show the group in
  the closed control as well as in the open list (for example "Collapsed (built-in)"), so
  they can be told apart.
- **FR-085**: Pages that list things (History, Snippets, Schema cache, Diagnostics) MUST use
  one page-header pattern, one action-button style, one table style and one selected-row
  style, at one content width rule.
- **FR-086**: The app MUST use one in-app dialog style for confirmations and prompts and one
  notification area for status messages; browser pop-ups MUST NOT be used; every destructive
  action MUST ask for confirmation naming what will be removed.
- **FR-087**: Analyser findings MUST appear in the editor (underline and gutter marker with
  the message on hover) as well as in the Problems panel, and the two MUST stay in step.
- **FR-088**: Every pane MUST have an empty state that says what to do next, and a loading
  state while content arrives; the application's own start-up and failure screens MUST be
  styled like the rest of the app.
- **FR-089**: The browser tab title MUST show the document name, SSMS's `*` while the
  document is modified, and the connected server/database — document name first, then
  server and database, as SSMS titles its window.
- **FR-090**: Button labels MUST describe what the button does (for example a button that
  inserts text MUST NOT say "copy"); unused or placeholder interface pieces MUST be removed.
- **FR-091**: All new and changed surfaces MUST take every colour from the current theme in
  Light, Dark and High contrast, so that no element keeps a fixed colour when the theme is
  switched; MUST meet the WCAG AA contrast target, show a visible focus ring, and carry no
  meaning by colour alone; in High contrast, text on a selected or hovered row MUST use the
  matching high-contrast text colour.
- **FR-092**: The saved theme MUST be applied before the first paint so the page never
  flashes another theme; monospace text MUST have a fallback font when the preferred one is
  missing.
- **FR-093**: The Schema panel MUST show the server and database it lists in its header; an
  icon per object kind (table, view, procedure, function, column, key) from the same icon
  set as the toolbar (FR-081); a filter box that narrows the tree to objects whose name
  contains the typed text, keeping their schema and kind nodes so a match stays in place; a
  Refresh action that reloads the current database's schema from the server; and a loading
  state while the schema loads (FR-088).

#### Compatibility and quality

- **FR-100**: Existing capabilities MUST keep working unchanged: editable results and
  apply-changes, IntelliSense, format, analyse, refactor, AI panel and chat, history
  capture, snippets, style editing, connection manager, Command Palette, engine pairing.
- **FR-101**: Everything this feature remembers MUST stay in the user's browser and be
  clearable from Settings; the only new data that leaves the browser goes to the paired
  engine to do the work — the document name attached to history entries (FR-072), the
  database-switch request (FR-082) and the Parse request (FR-035). Nothing new is sent
  anywhere else.
- **FR-102**: Grid interactions (resize, select, copy, sort, scroll) MUST stay smooth at the
  default row limit and with at least 50 columns.
- **FR-103**: Every new control MUST be reachable and operable by keyboard and MUST have an
  accessible name. Information shown on hover (column type, full cell value, analyser
  message, toolbar tooltip) MUST also appear when the element has keyboard focus and MUST
  be dismissible with Escape; the start, completion and failure of an execution MUST be
  announced to assistive technology; animations MUST respect the user's reduced-motion
  preference.
- **FR-104**: Behaviour this feature changes MUST be covered by automated checks the
  existing suites can run, including the editable-results flow and the settings pages that
  have none today.

### Key Entities

- **Workspace layout**: which regions are visible, their sizes, whether results are
  maximised, the AI panel's state; one per browser; has a default; can be reset.
- **Column layout**: the widths the user set for the columns of a shown result set, plus
  any sort applied; belongs to that result set and is discarded when a new result replaces
  it.
- **Result set**: one grid in a batch's Results tab — its columns (name, type), returned
  rows, whether it was cut at the row limit, whether it is editable, and the batch it came
  from.
- **Message**: one line in the Messages tab — kind (rows affected, print, error,
  completion), text, optional line number, error number and severity, order within the
  batch.
- **Document**: the query being edited — name, modified state, text and caret; exactly one
  in this feature. Its results and messages are shown while the page is open but are not
  restored on the next visit.
- **Settings section**: a named group of settings with its own restore-defaults; the nine
  sections in FR-051.
- **Settings export**: the portable set of browser-side preferences, with secrets excluded.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: On first display, 100 % of result columns are sized to content within the
  maximum width; in a 1,000-row, 30-column result, zero visible values are cut in columns
  below the maximum.
- **SC-002**: A user can resize a column, auto-fit it and copy a selection with headers in
  under 10 seconds total, and during the drag the column edge follows the pointer with no
  perceptible delay (under 100 ms).
- **SC-003**: Hiding the Schema and AI panels gives the editor at least 95 % of the
  window width; at 1100 px wide the editor keeps its minimum width without a horizontal
  scrollbar for lines up to 120 characters.
- **SC-004**: The workspace layout survives a page reload in the same browser in 100 % of
  attempts; column widths the user set survive scrolling, a window resize and panel toggles
  for the result they were set on; and "Reset layout" returns the default in one
  action.
- **SC-005**: Every setting is reachable from the single Settings entry in at most two
  clicks, the navigation has at most five top-level entries, and 100 % of visible settings
  change observable behaviour.
- **SC-006**: In a hallway test, at least 9 of 10 developers who know SSMS complete these
  six tasks unaided within 4 minutes total: resize a column, copy a result with headers,
  find the error line from Messages, hide the Schema panel, switch database, change the
  theme.
- **SC-007**: Every panel toggle, splitter, tab strip and the column auto-fit are
  operable with the keyboard alone.
- **SC-008**: Every automated check for the web edition that passes before this feature's
  first change still passes after it, compared check by check; and the editable-results flow
  passes the automated checks FR-104 requires for it.
- **SC-009**: A 1,000-row, 30-column result shows with sized columns within 1 second of
  the results arriving.
- **SC-010**: Every new surface passes the WCAG AA contrast target in all three themes, and
  no element keeps a fixed colour when the theme is switched.
- **SC-011**: The Messages tab shows an error's line number for 100 % of errors the server
  reports with a line in the document (not one inside a stored procedure, trigger or
  function), and clicking it lands on that document line, including when a selection was
  run and when the error is in a batch after a `GO`.
- **SC-012**: Zero browser pop-up dialogs remain in the app, and 100 % of destructive
  actions ask for confirmation.
- **SC-013**: A settings export contains zero secrets (keys, passwords, tokens) in 100 % of
  exports, verified by inspection of the file.

## Assumptions

- **Yardstick**: "like SSMS" means the SSMS 20/22 query editor: Object Explorer at the left,
  results and messages below the query, a thin status bar, a database selector in the
  toolbar, and a grid whose columns fit their content and can be dragged. Where SSMS is weak
  (no ellipsis, no sort, no remembered layout) this feature does better rather than copying
  it.
- **Maximum column width**: a sensible default (so one long text column cannot push the
  others off-screen) that the user can override by dragging; the exact value is chosen in
  planning and may become a setting later.
- **Fold-away width and minimum sizes**: the window width below which the side panels fold
  (FR-046), the editor's minimum width, the minimum size of each panel and pane (FR-044) and
  the width of the edge strip (FR-043) are chosen in planning, within SC-003: at 1100 px
  wide, a 120-character line at the default editor font must fit without a horizontal
  scrollbar.
- **Auto-fit source**: widths are fitted to the header and the returned rows that are
  available in the browser; for very large results a representative sample is acceptable
  if no visible value is cut.
- **Keys**: F5, Ctrl+F5 and Ctrl+R are claimed only while focus is on the editor page, so
  the browser's reload stays reachable from the address bar and from any other page. Keys
  the browser reserves outright (Ctrl+N, Ctrl+T, Ctrl+W, Ctrl+Tab) are not attempted.
- **Where things are remembered**: layout, grid options, settings and the like are kept
  per browser profile. They are not synchronised with the SSMS edition or across machines.
  The theme choice is also kept where the page can read it before the application starts,
  so FR-092 can be met; it stays per browser and is cleared with the other settings.
- **Settings vocabulary**: section names follow the SSMS edition's Options window
  (General, Editor, Format, Queries, Code analysis, AI assistance) so that documentation and
  support can speak of one product; web-only sections (Connections, Schema cache,
  Diagnostics) come after them.
- **Save model**: simple preferences apply immediately with visible confirmation (as today
  for theme and analysis); multi-field forms keep an explicit Save. SSMS's OK/Cancel model is
  not adopted for the web.
- **Audience and devices**: desktop and laptop browsers at 1100 px wide and above are the
  target; narrower windows must stay usable but are not designed for; phones are out of
  scope.
- **Engine work is in scope where SSMS behaviour needs it**: error line numbers and
  severity, per-statement row counts, keeping results produced before a failure, and `GO`
  batch splitting are engine additions this feature requires (see Dependencies). The
  engine's row cap, byte cap, timeout ceilings and the fact that a running statement cannot
  be interrupted are unchanged; the interface tells the truth about them.
- **Developer-only pages**: the `/spike` page is a development aid outside navigation and
  stays as it is.
- **Format styles tool**: stays its own full-page tool (built in spec 039); this feature adds
  the way in from Settings, the "Style" label, the truthful group names and the picker
  disambiguation, nothing more.
- **Schema panel scope**: stays a single-database tree; the toolbar database selector is how
  the user moves between databases. Object-type icons, a filter box and refresh are required
  by FR-093; a server-wide tree is not.

## Out of Scope

- Dockable or floating panels (decided 2026-10-08: panels have fixed homes).
- Multiple query tabs (decided 2026-10-08: deferred to a later feature; New query, Open
  file and Save as for the single document are in scope).
- Results to text, results to file as a mode, execution plans, client statistics, and
  other SSMS results-pane modes beyond grid and messages.
- Column reordering by drag, column hiding, freezing columns, cell-level filtering.
- Interrupting a running statement (a true Cancel), which needs engine transport work.
- Editing the engine's IntelliSense, formatter or analysis rule settings from the web
  edition (they belong to the shared engine configuration), and making the web edition's
  own analysis rule overrides apply in SSMS or vice versa.
- Synchronising web preferences with the SSMS edition or across browsers.
- Two-way snippet synchronisation with the engine.
- Changes to the public product site or the SSMS edition.

## Dependencies and Risks

- **Engine additions this feature needs**: error line number, error number, severity and
  state with each error, the name of the procedure, trigger or function it was raised in,
  and the document line each `GO` batch starts at (the server counts error lines from the
  start of its batch); one row count per statement, saying whether it is exact or the
  result was cut at the row limit; results and messages kept when a later statement fails,
  with elapsed time; a server-side syntax check that executes nothing (Parse, FR-035); `GO` batch splitting that respects strings and comments, with
  the batches after a failed one still run; each result column's declared precision and
  scale, so that `datetime2(3)` and `decimal(10,2)` show the digits SSMS shows; listing the
  databases and switching the database of a session's existing connection, so the browser
  never re-sends credentials (it does not keep SQL-authentication passwords) and temporary
  tables and SET options survive the switch as they do in SSMS, plus the session's current
  database reported after each execution so a `USE` in a script moves the selector
  (re-connecting with a new database is not a substitute: it would ask SQL-authentication
  users for their password again and silently drop temporary tables and SET options); and
  telling a paired browser whether the engine is the signed-in user's engine (the one SSMS
  starts) or the installed service, so the style group can be named truthfully (FR-063).
  PRINT and informational messages are already returned. Planning sizes these; they are
  the only engine work in scope.
- **Editable-results interaction**: moving from double-click-to-edit to click-to-select then
  edit (FR-013) touches the most security-sensitive flow in the web edition; it has one
  automated check today (the apply-result message), so tests for click-to-select,
  double-click/Enter/F2, Escape, Set to NULL, Tab, Apply and Discard must land with the
  change (FR-104), and sorting must map displayed rows back to fetched rows so edits land on
  the right row.
- **Layout change (FR-040)**: moving Problems into the results pane and Schema to the left
  changes where users look; the edge strip and View menu must make the new places
  obvious, and the first visit may show a one-time hint.
- **"Shared with SSMS" may not be true today**: when the web engine is installed as a
  service it runs under a machine account whose style folder and history are not the
  signed-in user's. FR-063 requires the label to say what is true; whether the installed
  engine should share the user's files is a separate question for the installer, not this
  feature.
- **Existing shared components**: the editor, status bar, Command Palette and style picker
  are shared by every page; changes must be verified on all pages, not only the editor.
- **Spec 040 lessons**: the SSMS edition's Options work found that resets which wipe hidden
  data and settings that do nothing destroy trust; FR-055, FR-058 and FR-063 carry those
  lessons over.
- **Visual tests**: the product-site screenshot tour captures the results grid; its images
  will change and must be refreshed deliberately, not treated as failures.
