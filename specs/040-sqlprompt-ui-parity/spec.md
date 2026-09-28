# Feature Specification: SQL Prompt UI/UX parity for Options, SQL History and format styles

**Feature Branch**: `040-sqlprompt-ui-parity`
**Created**: 2026-09-27
**Status**: Draft
**Input**: User description: "SSMS 22 plugin UI/UX parity with SQL Prompt for three areas — the Options window, the SQL History window, and format style editing (Format Styles window + active-style switching) — plus the shared menu/theming/naming issues, exactly as analysed and planned in doc/_Prompt-Gap/11-UI-UX-Plan-Options-History-Styles.md (38 work items: Phase 0 "make the screen tell the truth" OPT-01..03, HIS-01..06, STY-01..03; Phase 1 SQL Prompt interaction patterns OPT-04..09, HIS-07..14, STY-04..09; Phase 2 polish and sharing X-01..04, STY-10, STY-11) with context from doc/_Prompt-Gap/00-INDEX-and-Questions.md. Web edition is out of scope. Include the logic fixes that make the screens show false data (dead settings, history preview truncation, open/closed status never recorded, paging stopping early, grouped-row actions, snapshot/search-index desync, restore-defaults wiping hidden data, theme dropdown saving behind Cancel, clipped style labels, preview tab width, import/export list state)."

## Overview

AKML SQL's SSMS 22 plugin already lays out its Options window, SQL History window and Format
Styles window much as Redgate SQL Prompt 11 does. A review on 2026-09-27
(`doc/_Prompt-Gap/11-UI-UX-Plan-Options-History-Styles.md`, "the gap plan") found that the
remaining gaps are mostly **trust** and **detail**, not structure:

- The three screens sometimes show things that aren't true:
  - About 40 Options settings change nothing.
  - SQL History cuts the preview at 500 characters, never marks a query as open, stops
    scrolling early, and applies Delete / Favorite to only one run of a grouped query.
  - Option labels in the style editor are cut off mid-word.
  - The live preview draws tabs at the wrong width.
- Several SQL Prompt interaction patterns are missing: an **Active Style** menu, option
  search in the style editor, History's **Advanced search**, indented child options, options
  in the Command Palette, and a grouped top menu.

This feature closes those gaps for the SSMS 22 plugin. It keeps the places where AKML is
already ahead of SQL Prompt. Each requirement below names the gap-plan item it comes from
(e.g. OPT-01), so the plan, this spec and later tasks stay traceable.

**Priority mapping:** spec priority **P1** = gap-plan Phase 0 ("make the screen tell the
truth"), **P2** = Phase 1 (SQL Prompt interaction patterns), **P3** = Phase 2 (polish and
sharing).

## Clarifications

### Session 2026-09-27

- Q: What to do with the ~40 Options settings that have no effect today? → A: **Wire the
  cheap ones and hide the rest.** Hidden settings keep their saved values (FR-001, FR-002).
- Q: How should the Options tree be arranged? → A: **Use SQL Prompt's group and page names
  for pages both products have, with AKML-only groups after them.** This is the gap plan's
  proposed tree (FR-050).
- Q: How much of the gap plan does this feature deliver? → A: **All three priorities: all 38
  items (≈41 working days) in this one feature** (FR-071).
- Planning research (2026-09-27) narrowed three requirements so they stay honest. See
  plan.md and research.md for details.
  - FR-031 compares options with SQL Prompt's default, as the web editor does.
  - FR-065 lists only Format SQL actions the formatter can perform.
  - FR-062 adds an Options help topic, because the docs site has none.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Options I can trust (Priority: P1)

A SQL developer opens **AKML SQL › Options** and changes a setting. Every setting they can see
does what its label says. Restoring defaults on a page only resets what that page shows.
Cancelling really discards their changes, including after a theme change, "Restore all
defaults" or Import.

**Why this priority**: A setting that silently does nothing, or a reset that wipes hidden work
(code-analysis rule choices, AI agents and keys, connection aliases), destroys trust in every
other screen. These are the most damaging problems and the cheapest to fix.

**Independent Test**: Change each setting listed in the gap plan's Appendix A and observe its
effect, or confirm it is no longer shown. Reset each page and confirm nothing outside it
changed. Cancel after each kind of change and confirm the saved settings are identical.

**Acceptance Scenarios**:

1. **Given** Options is open, **When** the user sets "Maximum suggestions" to 10 and presses
   OK, **Then** the completion list never shows more than 10 items. (OPT-01)
2. **Given** a setting has no effect yet, **When** the user opens its page, **Then** that
   setting is not shown. (OPT-01)
3. **Given** a code-analysis rule was switched off in Manage Code Analysis Rules, **When** the
   user restores defaults on the Code Analysis page, **Then** the rule stays off. (OPT-03)
4. **Given** the user presses Restore Defaults on any page, **When** the confirmation appears,
   **Then** it names the page as the tree shows it, and says what else will be removed if
   anything, e.g. "This also removes your 3 AI agents and their keys". (OPT-03)
5. **Given** the saved theme is Dark, **When** the user presses "Restore all defaults" and then
   Cancel, **Then** the settings file is unchanged and the window didn't close on its own.
   (OPT-02)
6. **Given** the theme preference is "System" and SSMS uses a dark theme, **When** Options
   opens, **Then** nothing is saved and the window stays open. (OPT-02)

---

### User Story 2 - SQL History I can trust (Priority: P1)

A developer looks up an earlier query in **SQL History**. The preview shows the whole query.
Open queries are marked as open. Scrolling reaches the oldest entry. Delete and Favorite act on
the whole query, not one run of it. Search finds what the query now contains. The History
settings only offer what really works.

**Why this priority**: History is a safety net. A net that silently cuts queries off, hides
entries or brings deleted entries back is worse than none.

**Independent Test**: Run the reproduction steps for HIS-01 … HIS-06 in the gap plan and check
the expected results below.

**Acceptance Scenarios**:

1. **Given** a query longer than 500 characters was run, **When** it is selected in SQL
   History, **Then** the preview shows the complete text. (HIS-01)
2. **Given** a query's tab is open, **When** SQL History is shown, **Then** that row is marked
   open, and "Show open queries only" includes it. **When** the tab is closed, **Then** the row
   shows as closed within 1 second. (HIS-02)
3. **Given** 250 entries match the current search, **When** the user scrolls to the end,
   **Then** all 250 are reachable. (HIS-03)
4. **Given** one tab ran three different queries (one grouped row, "×3"), **When** the user
   deletes the row and confirms, **Then** the row does not come back after refresh. (HIS-04)
5. **Given** a grouped row was starred and the tab was run again, **When** the user un-stars
   the row, **Then** the star turns off. (HIS-04)
6. **Given** a tab's text was changed and a version snapshot was taken, **When** the user
   searches for the new text, **Then** the entry is found. **When** they search for text that
   is no longer there, **Then** that entry is not returned. (HIS-05)
7. **Given** a History setting cannot work yet ("Encrypt at rest", "Record failed
   executions"), **When** the History settings page is opened, **Then** that setting is not
   shown. Settings that need a restart say so. (HIS-06)

---

### User Story 3 - A style editor I can read and trust (Priority: P1)

A developer edits a formatting style in the **Format Styles** window. Every option label can be
read in full, and clicking a label toggles its checkbox. The live preview lines up exactly as
the formatted SQL will in the editor, including styles that indent with tabs. After Import the
list shows the right active style. Export never silently drops unsaved changes.

**Why this priority**: The style editor is AKML's most visible feature (the one shown
publicly). Cut-off labels and a preview that looks broken for tab-based styles make it look
unfinished.

**Independent Test**: Open all 14 pages at the default window size, read every label, and
toggle options by clicking their labels. Select a tab-indented style and compare the preview
with the same SQL in the editor. Import a style and check the ACTIVE marker. Export with
unsaved changes.

**Acceptance Scenarios**:

1. **Given** the Format Styles window at its default size, **When** any of the 14 pages is
   shown, **Then** no option label is cut off. (STY-01)
2. **Given** an on/off option, **When** the user clicks its label, **Then** the option
   toggles. (STY-01)
3. **Given** a style that indents with tabs of width 2, **When** its live preview is shown,
   **Then** lines align exactly as the same SQL does in an editor set to tab width 2. (STY-02)
4. **Given** the user imports a style and it becomes active, **When** the import finishes,
   **Then** the ACTIVE marker, the "Active:" header chip and the "Set as active style" button
   all reflect the imported style at once. (STY-03)
5. **Given** the selected style has unsaved changes, **When** the user chooses Export,
   **Then** they are asked whether to save first (Yes / No / Cancel). (STY-03)

---

### User Story 4 - Find, compare and switch styles quickly (Priority: P2)

A developer finds any style option by typing part of its name. They see which options differ
from the base style and can reset one option at a time. They read a syntax-coloured preview.
They switch the active style from a menu in two clicks, and are told which style formatted
their code.

**Why this priority**: These are the SQL Prompt patterns people use every day with styles
(Active Style menu, option search). The desktop editor also gains the change markers the web
editor already has.

**Independent Test**: Search "comma" in the style editor. Change an option and reset it with
its reset control. Switch the active style from the Active Style menu, then run Format
Document and read the status message.

**Acceptance Scenarios**:

1. **Given** the Format Styles window, **When** the user types "comma" in the option search,
   **Then** only pages with matching options remain in the page tree, each with a match
   count, and the matching options are highlighted. (STY-04)
2. **Given** an option differs from SQL Prompt's default, **When** its page is shown, **Then**
   the option is marked as changed, has a one-click reset to that default, and its page shows
   a changed-options count in the tree. (STY-05)
3. **Given** the user changes an option, **When** the preview updates, **Then** the lines that
   moved are highlighted briefly. (STY-05)
4. **Given** the light theme, **When** the preview is shown, **Then** it uses the theme's
   colours, with keywords, strings and comments coloured. (STY-06)
5. **Given** the AKML SQL menu or the editor's right-click menu, **When** the user opens
   "Active Style", **Then** every style is listed, the active one is ticked, and "Edit Styles…"
   is at the end. Picking a style makes it active immediately. (STY-07)
6. **Given** the active style changed anywhere, **When** the status bar is visible, **Then**
   it shows the new style name (if the user chose to show it). (STY-07)
7. **Given** a style list, **When** the user copies a style, **Then** they are asked for the
   new name. Name conflicts are reported while typing. F2 renames, Delete deletes (never a
   built-in or the active style), Ctrl+S saves and Ctrl+F focuses search. (STY-08)
8. **Given** Format Document or Format Selection succeeds, **When** it finishes, **Then** the
   status bar briefly says which style was used. If the style could not be loaded, both
   commands say so. (STY-09)

---

### User Story 5 - SQL History that works like SQL Prompt's (Priority: P2)

A developer searches history as they type and narrows it by period, server, database and
state. Each row shows where the query ran (server, database and environment colour). They can
open or compare any saved version, use the whole window from the keyboard, and get lost tabs
back after a restart.

**Why this priority**: These are the interactions SQL Prompt users rely on in SQL History. AKML
already has the layout; the behaviour behind it is incomplete.

**Independent Test**: Type into the search box and watch the list filter. Use Advanced search
for last week on one database. Open version 2 of 4 of a query. Operate the window without a
mouse. Close an unsaved tab, restart SSMS and find it in SQL History.

**Acceptance Scenarios**:

1. **Given** SQL History is open, **When** the user types in the search box, **Then** the list
   filters shortly after typing stops and the first result is selected and previewed. The
   search covers name, file path, SQL, server and database. A help popup lists the search
   syntax. (HIS-07)
2. **Given** Advanced search, **When** the user picks "Last week", a server, a database and
   "Starred", **Then** only matching entries remain, the active filters are visible, and Reset
   clears them all. (HIS-08)
3. **Given** a row whose server matches a tab-colour environment, **When** it is shown,
   **Then** it shows server, database and the environment name in the environment's colour.
   Open rows carry an "open" marker. Date groups are Today, Yesterday, This week, Last week,
   This month and Older, each with a count. (HIS-09)
4. **Given** a query with 4 versions, **When** the user selects version 2 and chooses Open,
   **Then** the new tab contains version 2's text. "Compare with current" shows the changed
   lines highlighted. (HIS-10)
5. **Given** the history list has focus:
   - **When** the user presses Enter / Delete / F2 / Ctrl+C / Space / Tab, **Then** it opens /
     removes (after asking) / renames / copies / stars / moves focus to the preview.
   - **When** in the preview, **Then** text can be selected and copied. (HIS-11)
6. **Given** a row's action menu, **When** it opens, **Then** every action affects only that
   row. Export and "Clear history…" are available from the window's own menu, not the row's.
   Dates shown in confirmations are readable. (HIS-12)
7. **Given** SQL History is open, **When** the user runs a query, **Then** it appears within
   1 second without losing the current selection or scroll position. If the engine is
   unavailable, a Retry action is offered. (HIS-13)
8. **Given** an unsaved query tab was closed and SSMS restarted, **When** the user looks in
   SQL History under closed queries, **Then** the tab can be reopened with its text, and
   reconnected if the user chose that. (HIS-14)

---

### User Story 6 - Options arranged the way SQL Prompt users expect (Priority: P3)

A developer moving from SQL Prompt finds each setting where they expect it:
- the pages both products have use SQL Prompt's group and page names;
- dependent options are indented and greyed out while their master switch is off;
- large numbers are typed exactly;
- a single option can be switched from the Command Palette;
- tab colours are set in a grid with a colour picker;
- every Options window follows the Dark theme.

**Why this priority**: Improves findability and polish. It depends on User Story 1, because
moving settings that do nothing would only move the problem.

**Independent Test**:
- Compare the Options tree with the agreed target tree.
- Switch master options off and watch their children.
- Type 250,000 into a large-number field.
- Toggle "nullability" from the Command Palette.
- Add a Production tab-colour rule without typing a colour code.
- Take a dark-theme tour of all Options sub-windows.

**Acceptance Scenarios**:

1. **Given** Options, **When** the tree is shown, **Then** it matches the agreed target
   arrangement. Each page's tree label, header and title are the same text. No setting
   appears on two pages. (OPT-04)
2. **Given** a master option (e.g. "Enable IntelliSense") is off, **When** its page is shown,
   **Then** its dependent options are indented and disabled. (OPT-05)
3. **Given** a numeric setting with a range wider than 100 values, **When** the user edits it,
   **Then** they can type an exact value and see its unit. (OPT-06)
4. **Given** the Command Palette, **When** the user types part of an option's name, **Then**
   the option is listed with its page path. An on/off option can be toggled right there;
   other options open Options on that setting. (OPT-07)
5. **Given** the tab-colour settings, **When** the user adds a rule, **Then** they choose
   server/group, database and environment in a grid, pick colours from a colour picker, and
   can reorder rules. (OPT-08)
6. **Given** the Dark theme, **When** any window reached from Options (including Manage Code
   Analysis Rules and the tab-rule editor) is opened, **Then** it uses dark-theme colours
   throughout. (OPT-09)

---

### User Story 7 - Polish and team sharing (Priority: P3)

- The AKML SQL menu is organised into groups and submenus, with About and Check for Updates
  last.
- Every AKML window names itself the same way.
- F1 opens help for the screen in front of the user.
- Icon-only buttons are announced by screen readers.
- A team shares one set of formatting styles through a shared folder.
- Options › Format › Styles offers SQL Prompt's "When you run Format SQL, AKML SQL will:"
  list of actions.

**Why this priority**: Valuable polish and team features, with no impact on correctness.

**Independent Test**:
- Open the AKML SQL menu and count its top-level entries.
- Compare window titles.
- Press F1 on an Options page.
- Use Narrator on the SQL History toolbar.
- Point two machines at the same style folder.
- Tick one Format SQL action and run Format Document.

**Acceptance Scenarios**:

1. **Given** a query window is active, **When** the user opens the AKML SQL menu, **Then**
   commands are grouped with separators and submenus (Format, Refactor, Navigate, Tabs &
   history, AI, tools, Options, Help), and About / Check for Updates are last. (X-01)
2. **Given** any AKML window, **When** it opens, **Then** its title follows one pattern:
   "AKML SQL – ‹Window›". (X-02)
3. **Given** an Options page, the Format Styles window or SQL History, **When** the user
   presses F1, **Then** the matching help topic opens. (X-03)
4. **Given** a screen reader, **When** focus reaches an icon-only button in the three areas,
   **Then** the button's name is announced. (X-04)
5. **Given** two users set the same shared style folder, **When** either opens Format Styles,
   **Then** both see the same team styles. Team styles are read-only unless the folder can be
   written to. (STY-10)
6. **Given** Options › Format › Styles, **When** the user ticks "Insert semicolons" in the
   Format SQL actions, **Then** Format Document inserts semicolons. Unticked actions don't run.
   (STY-11)

---

### Edge Cases

**SQL History data**

- **Very long queries (up to 1 MB, the history capture limit):** the preview must show the
  full text without freezing SSMS. If a query is too large to display comfortably, the preview
  says so and offers Open instead.
- **Very large history (100,000 entries):** scrolling to the end, search as you type and
  Advanced search stay responsive.
- **Engine not connected:** History shows "unavailable" with Retry, not an empty list. It
  refreshes by itself when the engine returns. No action pretends to succeed.
- **A grouped query's runs have mixed star states** (an older run starred, the latest not):
  the row shows starred, and un-starring clears every run.
- **SSMS crashes with queries open:** on the next start, no query is left marked "open" that
  isn't really open.
- **A version snapshot is taken while a search is shown:** the search results stay correct
  (no stale or missing matches).

**Options window**

- **Settings that are hidden but still hold a value:** existing settings files load
  unchanged. Hidden values are kept, and are neither wiped nor applied.
- **Theme preference "System" when the host theme can't be detected:** falls back to Light
  without saving anything.
- **Resetting a page whose settings are also used elsewhere:** only the fields shown on that
  page are reset.
- **Settings read only when SSMS starts:** their rows say "Takes effect after SSMS restarts".

**Format styles**

- **Import of a style whose name matches a built-in style:** the user is told, and can choose
  a new name. The built-in is never overwritten silently.
- **Active style deleted or renamed elsewhere** (e.g. in the shared folder): Format commands
  fall back to the default style and say so. The Active Style menu reflects reality the next
  time it opens.
- **Style names with characters not allowed in file names, or very long names:** refused
  while typing, with a reason.
- **Keyboard Delete on a built-in or the active style:** nothing is deleted, and the user is
  told why.
- **Shared style folder unreachable** (network down): team styles are shown as unavailable.
  The user's own styles keep working.

**Everywhere**

- **High-contrast theme:** everything in the three areas stays readable.

## Requirements *(mandatory)*

### Functional Requirements

**Options: trust (P1)**

- **FR-001** (OPT-01): Every setting shown in the Options window MUST change the product's
  behaviour as its label and description say.
  - These settings, which have no effect today, MUST be made to work:
    - Maximum suggestions
    - Trigger delay
    - Enable fuzzy matching
    - Show column data types
    - Show nullability info
    - Show PK/FK indicators
    - Show in Error List
  - Every other setting in the gap plan's Appendix A MUST be hidden until it works.
  - Any other setting found to have no effect while this feature is built MUST be either
    wired, if that takes no more than about half a day, or hidden.
- **FR-002** (OPT-01): Settings that are hidden MUST keep any value already saved, and older
  settings files MUST still load without error.
- **FR-003** (OPT-01): Duplicate settings (snippets in the completion list, ghost text,
  backups before formatting / refactoring) MUST be reduced to one working setting each.
  Keyword casing MUST be governed only by the formatting style.
- **FR-004** (OPT-02): Changing the theme MUST NOT save any other setting and MUST NOT bypass
  Cancel. Loading, resetting or importing settings MUST NOT trigger a theme-change save.
- **FR-005** (OPT-03): Restore Defaults on a page MUST reset only the settings that page
  shows. Rule overrides, AI agents and keys, connection aliases, environment severities and
  the active style MUST survive a reset of a page that doesn't show them.
- **FR-006** (OPT-03): Every reset confirmation MUST name the page as the tree shows it, and
  MUST list anything the reset will remove beyond that page's visible fields.
- **FR-007** (OPT-03): "Restore all defaults" MUST keep installation identity and first-run
  state, as Import already does.

**SQL History: trust (P1)**

- **FR-010** (HIS-01): The History preview MUST show the complete text of the selected entry
  or version, up to the capture limit.
- **FR-011** (HIS-02): Each history entry MUST show whether its query is open in a tab. The
  state MUST update within 1 second of a tab being opened, activated or closed, and MUST be
  reset correctly when SSMS starts.
- **FR-012** (HIS-02): The "open only" / "closed only" filters and the `open:` search prefix
  MUST return entries according to that state.
- **FR-013** (HIS-03): Scrolling MUST make every entry matching the current search and
  filters reachable.
- **FR-014** (HIS-04): When runs are grouped into one row, Delete, Favorite / un-favorite and
  the versions list MUST apply to the whole group. Delete MUST ask for confirmation, using
  SQL Prompt's wording ("Remove query and its history").
- **FR-015** (HIS-05): After any version snapshot, search MUST find the entry by its current
  text and MUST NOT find it by text it no longer contains. Entries MUST stay in
  chronological order.
- **FR-016** (HIS-06): History settings MUST only be shown when they work. The grouping
  setting MUST be described as grouping repeated runs, not as avoiding storage. Settings that
  take effect only after a restart MUST say so.

**Format styles: trust (P1)**

- **FR-020** (STY-01): At the Format Styles window's default size, no option label may be
  truncated on any page. Clicking an on/off option's label MUST toggle it.
- **FR-021** (STY-02): The live preview MUST render tabs at the selected style's tab width,
  so its alignment matches the formatted SQL in an editor with that tab width. The formatted
  text itself MUST NOT change.
- **FR-022** (STY-03): After Import, the style list, the ACTIVE marker, the header's active
  chip and the "Set as active style" button MUST reflect the new state without reopening the
  window.
- **FR-023** (STY-03): Export of a style with unsaved changes MUST first ask whether to save
  them (Yes / No / Cancel).

**Format styles: SQL Prompt patterns (P2)**

- **FR-030** (STY-04): The style editor MUST offer an option search across labels,
  descriptions and option names. It MUST filter the page tree to pages with matches (with
  counts), highlight matching options, jump to the first match on Enter, and clear on Esc.
- **FR-031** (STY-05): Options whose value differs from SQL Prompt's default for that option
  MUST be visibly marked and individually resettable to that default. This is the same rule
  the web editor uses. Each page MUST show its count of changed options in the tree.
- **FR-032** (STY-05): After an option changes, the preview MUST briefly highlight the lines
  whose layout changed.
- **FR-033** (STY-06): The preview MUST syntax-colour SQL, show line numbers, and follow the
  current theme's colours.
- **FR-034** (STY-07): An "Active Style" submenu MUST be available from the AKML SQL menu and
  the SQL editor's context menu. It lists all styles, ticks the active one and ends with
  "Edit Styles…". Choosing a style makes it active at once.
- **FR-035** (STY-07): The status-bar style indicator MUST update whenever the active style
  changes anywhere, and MUST respect the "Show active style in status bar" setting.
- **FR-036** (STY-08):
  - Copying a style MUST ask for the new name.
  - Name dialogs MUST validate as the user types: duplicates, illegal characters and empty
    names.
  - The style list MUST support F2 (rename), Delete (delete; never built-ins or the active
    style), Ctrl+S (save), Ctrl+F (search) and Enter (set active).
  - Integer options MUST offer step controls.
  - Option notes ("Takes effect when…") MUST be visible without hovering.
  - Dependent options MUST be indented under their parent.
- **FR-037** (STY-09): After Format Document or Format Selection succeeds, the product MUST
  briefly state which style was used. Both commands MUST warn when the active style could not
  be loaded.

**SQL History: SQL Prompt patterns (P2)**

- **FR-040** (HIS-07): History search MUST run shortly after typing stops, as well as on
  Enter.
  - It MUST search name, file path, SQL text, server and database.
  - It MUST select the first result.
  - It MUST offer an in-window help listing the supported search syntax: the prefixes
    `name: path: sql: server: database: starred: open: date:`, quoted phrases, OR / NOT, and
    wildcards.
- **FR-041** (HIS-08): History MUST offer Advanced search:
  - period: Everything / Last week / Last month / Last 3 months / Custom range;
  - server and database pickers covering all history, not only loaded rows;
  - state: Starred, Open;
  - Reset.
  Active filters MUST be visible and removable. Remembering advanced search settings MUST be
  an option.
- **FR-042** (HIS-09):
  - Each row MUST show server and database, and the tab-colour environment name in its
    colour when a rule matches.
  - Open entries MUST carry a visible "open" marker.
  - Date groups MUST be Today, Yesterday, This week, Last week, This month and Older, with
    counts.
- **FR-043** (HIS-10): Opening, copying or re-executing MUST use the selected version. A
  version MUST be comparable with the current text, with added, removed and changed lines
  highlighted. The sides MUST be labelled with name and time.
- **FR-044** (HIS-11): The preview text MUST be selectable and copyable. The list MUST
  support Enter, Delete (with confirmation), F2, Ctrl+C, Space (star) and Tab / Shift+Tab. The
  star and the row menu MUST be reachable by keyboard.
- **FR-045** (HIS-12):
  - Row menu actions MUST affect only the selected entry.
  - Export and "Clear history…" MUST be offered from the window's own menu.
  - Confirmations MUST show readable dates.
  - Re-execute MUST use the entry's connection or ask for one.
- **FR-046** (HIS-13):
  - A newly executed query MUST appear in an open History window within 1 second.
  - Selection and scroll position MUST be kept after refreshes, starring and deleting.
  - When the engine is unavailable, the window MUST show that state with Retry, and MUST
    refresh when the engine returns.
- **FR-047** (HIS-14):
  - Closed query tabs, including never-saved ones, MUST be findable and reopenable from SQL
    History after an SSMS restart.
  - Restore-on-start settings MUST live on the History settings page, using SQL Prompt's
    wording: restore open queries, maximum to restore, reconnect restored queries.
  - The crash-recovery prompt MUST follow the current theme.

**Options: SQL Prompt patterns (P3)**

- **FR-050** (OPT-04): The Options tree MUST use SQL Prompt's group and page names for pages
  both products have, with AKML-only groups after them, in this order:
  - **General**
  - **Suggestions:** Behavior · Types of suggestion · Tooltips · Connections · Join conditions
    · Snippets · Warnings & highlighting (today's Execution Warnings)
  - **Inserted code:** Objects & statements (today's INSERT statements) · Qualification ·
    Aliases · Special characters
  - **Format:** Styles
  - **Navigation**
  - **Queries:** Query Results · History (also holds the restore-on-start settings, see
    FR-047) · Color (today's Tabs › Color) · Execution
  - **Editor:** Productivity · Refactoring
  - **Code Analysis**
  - **Connections & Memory** (also holds all schema-cache settings, including today's
    Suggestions › Database)
  - **AI Assistance**
  - **Labs**

  In addition:
  - Each page MUST use one name for its tree label, header and title, in sentence case.
  - No setting may appear on more than one page.
  - Existing deep links to pages MUST keep working.
- **FR-051** (OPT-05): Dependent options MUST be indented under their parent option and
  disabled while it is off, updating immediately.
- **FR-052** (OPT-06): Numeric settings with more than 100 possible values MUST accept typed
  exact values with step controls and a unit label.
- **FR-053** (OPT-07): The Command Palette MUST list individual options with their page path.
  On/off options MUST be switchable there, taking effect at once; other options open Options
  at that setting, highlighted.
- **FR-054** (OPT-08): Tab-colour rules MUST be edited in a grid (server or group pattern,
  database, environment) with reordering and a wildcard hint. Environments MUST be edited with
  a colour picker, the gradient option and restore-default-environments. The rule's match
  target and database MUST be editable.
- **FR-055** (OPT-09): Every window reachable from Options MUST follow the current theme,
  including Manage Code Analysis Rules (with a checklist by category, a description pane and
  the settings-file location) and the tab-rule editor.

**Shared polish and sharing (P3)**

- **FR-060** (X-01): The AKML SQL menu MUST be organised into separated groups and submenus
  (Format with Active Style; Refactor; Navigate; Tabs & history; AI; tools; Options; Help),
  with About and Check for Updates last.
- **FR-061** (X-02): Every AKML window title MUST follow "AKML SQL – ‹Window›".
- **FR-062** (X-03): F1 on any Options page, the Format Styles window or SQL History MUST open
  the matching help topic on the product's documentation site. Every topic F1 points at MUST
  exist. That includes a new Options topic with a section per page, because the site has none
  today.
- **FR-063** (X-04): Every icon-only control in the three areas MUST have an accessible name.
  Text sizes MUST follow the product's type scale. All colours MUST update when the theme
  changes.
- **FR-064** (STY-10): Users MUST be able to set a shared style folder.
  - Its styles appear as a separate "Team styles" group.
  - They are read-only unless the folder can be written to.
  - When the folder can't be reached, this is shown and the user's own styles keep working.
- **FR-065** (STY-11): Options › Format › Styles MUST offer a "When you run Format SQL, AKML
  SQL will:" list.
  - It lists only actions AKML can actually perform: apply layout, apply casing,
    insert / remove semicolons, add / remove square brackets, expand wildcards, qualify
    object names.
  - Each ticked action runs on Format Document and Format Selection; unticked ones don't.
  - SQL Prompt's "add / remove AS keyword" and "apply column alias style" are left out until
    the formatter supports them (FR-001: never show what doesn't work).

**Scope**

- **FR-070**: This feature covers the SSMS 22 plugin only. The web edition MUST NOT change.
- **FR-071**: This feature delivers all three priorities: all 38 gap-plan items (P1, P2 and
  P3). Work proceeds in priority order: every P1 requirement is complete and verified before
  P2 work is merged, and P2 before P3. Each user story stays independently testable.

### Key Entities

- **Visible setting:** an option shown on an Options page. It has a page, a label, an optional
  description, an optional parent (master) setting, and an observable effect. It may be hidden
  while keeping its saved value.
- **Page reset scope:** the exact set of settings a page shows. It is the only thing Restore
  Defaults on that page may change.
- **History entry (query session):** one row in SQL History. It has:
  - a name and file path;
  - server, database and, where a rule matches, a tab-colour environment;
  - open / closed state and a starred flag;
  - one or more runs and one or more versions.
- **Version snapshot:** saved text of a query at a point in time, openable and comparable.
- **Format style:** a named style. It has:
  - a kind (built-in, own, or team);
  - an active flag;
  - its options, each compared with SQL Prompt's default to track changes;
  - a tab width, used by the preview.
- **Shared style folder:** a location whose styles appear as team styles for everyone who
  points at it.
- **Tab-colour rule and environment:** a pattern (server / group, database) mapped to a named
  environment with a colour. It is used by query tabs and by History rows.

## Success Criteria *(mandatory)*

### Measurable Outcomes

**Trust (P1)**

- **SC-001:** 0 settings in the Options window that have no observable effect. Each shown
  setting passes its test in the gap plan's Appendix A, or is no longer shown.
- **SC-002:** Restoring defaults on any single page changes 0 settings outside that page.
- **SC-003:** After any sequence of changes followed by Cancel (including theme change,
  "Restore all defaults" and Import), the saved settings are unchanged in 100% of attempts.
- **SC-004:** The History preview shows 100% of the text for queries of any length up to the
  capture limit.
- **SC-005:** The History "open" state matches the real tab state for 100% of entries, within
  1 second of a tab opening or closing.
- **SC-006:** All entries matching a search are reachable by scrolling, for histories of up to
  100,000 entries.
- **SC-007:** A deleted grouped query never reappears. Starring and un-starring a grouped
  query works on the first try, 100% of the time.
- **SC-008:** Search finds an entry by its current text, and never by text removed from it, in
  100% of cases after version snapshots.
- **SC-009:** 0 truncated option labels across all 14 style pages at the default window size.
- **SC-010:** For tab-indented styles, preview line alignment matches the editor output for
  100% of sample lines.

**SQL Prompt patterns (P2 and P3)**

- **SC-011:** A user can change the active style in 2 clicks from the menu, with no dialog
  opening.
- **SC-012:** In testing, users find any named style option within 10 seconds using the
  option search.
- **SC-013:** Every SQL History action can be done with the keyboard alone.
- **SC-014:** A newly executed query appears in an open SQL History window within 1 second.
- **SC-015:** The AKML SQL menu has at most 12 top-level entries, with About and Check for
  Updates last.
- **SC-016:** In the Dark theme, 0 windows or controls in the three areas show light-theme
  colours.
- **SC-017:** Every flaw listed in the gap plan (Appendix A and its P0 items) passes its
  verification step ("Verify after the fix").

## Assumptions

- **Reference product:** Redgate SQL Prompt 11 as documented up to September 2026 (links in
  the gap plan's Appendix B). Where Redgate's documentation is silent, AKML's own conventions
  apply.
- **Style window:** it stays a single window (style list, pages, options and preview
  together). SQL Prompt's two-step design is not copied (gap-plan decision 3).
- **Preview colours:** the live preview follows the current theme instead of a fixed dark
  panel (gap-plan decision 4).
- **AKML's advantages are kept:** search inside Options, a description under every option,
  History compare / export / re-execute / highlighting, the four-source live preview, SQL
  Prompt style import, and editable built-ins with reset.
- **Existing user data is preserved and migrated where needed:** settings files, history
  database, styles and snippets. No user loses history, styles or settings by upgrading.
- **Text:** all new text is English, in sentence case, and uses SQL Prompt's wording where an
  equivalent exists.
- **Performance:** interactions in the three areas respond within normal desktop expectations
  (under 1 second for filtering, refreshing and switching).
- **Out of scope:** the web edition; Visual Studio (support removed); AI feature UX beyond
  moving the AI page in the Options tree; new formatting options; Redgate-cloud features;
  pixel-exact copies of SQL Prompt's screens.

## Dependencies

- **SQL History (HIS-02, HIS-04, HIS-05, HIS-14):** needs changes in the engine's history
  store as well as the SSMS window.
- **Format SQL actions (STY-11):** relies on the format-time actions the formatter already
  has.
- **F1 help (X-03):** needs matching topics on the product documentation site.
- **Source material:** the gap plan and its companion page, `AKML-UI-Gap-Plan.html`, which has
  reproduction steps and code evidence for every item.
