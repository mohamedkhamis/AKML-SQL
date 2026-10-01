# AKML SQL vs SQL Prompt: UI/UX comparison and gap plan

> **Implementation status (spec 040, branch `040-sqlprompt-ui-parity`):** all 38 items are built.
> Tasks are in `specs/040-sqlprompt-ui-parity/tasks.md`; verification in `baseline.md` there.
>
> | Items | Status | Tasks |
> |---|---|---|
> | OPT-01, OPT-02, OPT-03 | Done | T007–T047 (US1) |
> | HIS-01 … HIS-06 | Done | T048–T074 (US2) |
> | STY-01, STY-02, STY-03 | Done | T075–T083 (US3) |
> | STY-04 … STY-09 | Done | T084–T111 (US4) |
> | HIS-07 … HIS-14 | Done | T112–T148 (US5) |
> | OPT-04 … OPT-09 | Done | T149–T170 (US6) |
> | X-01 … X-04, STY-10, STY-11 | Done | T171–T191 (US7) |
>
> Deferred (recorded in tasks.md › Deferred and `doc/progress.md`): commands with no handler stay
> off the AKML SQL menu (X-01); the VSCT menu remains invisible in SSMS 22, so the runtime menu is
> the one users see (X-01); Format SQL actions lack the AS-keyword and column-alias options (STY-11);
> "Record failed executions" and "Encrypt at rest" stay hidden until they work (HIS-06).

**Scope:** the SSMS 22 plugin only. The web edition is out of scope.

This plan covers three areas:
- the **Options** window
- the **SQL History** window
- **format style editing**, which includes the Format Styles window and switching the active style

It also covers the menu and theming issues those three areas share. It is about UI and UX. Where a logic bug makes the screen show something false, the fix is included: the screen can't be right until the data behind it is.

**Date:** 2026-09-27, against `master` at `e75771b`.

**Sources:**
- A code review of `src/AkmlSql.Shell.Shared`, `src/AkmlSql.Engine/History` and `src/AkmlSql.Ssms22`.
- Redgate's SQL Prompt 10/11 documentation, with links in the appendix.
- Redgate's own screenshots in this folder: `SQL History Redgate.png`, `SQL Prompt Options - Special characters Redgate.png` and `SQL Prompt Options - bottom bar Redgate.png`.
- Today's SSMS screenshots of the Format Styles window.

**Relation to earlier files:**
- This file replaces the recommendations in `10-UI-Fidelity-Options-and-History.md` for these three areas.
- File 10 rated SQL History "at parity". That rating judged the window's layout only. The review behind this file found data bugs underneath: the open/closed filter can never match anything, the preview is cut off, paging stops early, and row actions hit the wrong data. See section 1.

---

## 1. Summary

1. **The layout already matches SQL Prompt closely.**
   - The Options tree, the SQL History window and the style editor's pages (Global / Statements / Clauses / Expressions) are laid out as SQL Prompt does them.
   - What remains is **honesty, detail and polish**, not structure.
2. **Biggest problem: the screen promises things the product doesn't do.**
   - **Options:** about 40 controls change nothing (appendix A). For example, "Maximum suggestions", "Trigger delay", "Show nullability info", "Format on paste", "Freeze headers" and "Encrypt at rest".
   - **SQL History:**
     - The preview shows only the first 500 characters.
     - The open/closed filter and the green/red dot never change, because no code records that a query is open.
     - Scrolling stops loading before the end of the list.
     - Delete and Favorite on a grouped row act on the latest run only.
     - "Record failed executions" and "Encrypt at rest" do nothing.
   - **Styles:**
     - Labels are cut in the middle of a word, e.g. "Place subsequent items on r".
     - The live preview ignores the style's tab width, so tab-indented styles such as Khamis Style look broken.
     - Import doesn't refresh the ACTIVE marker.
3. **Main interaction gaps against SQL Prompt:**
   - There is no quick **Active Style ▸** menu.
   - The style editor has no **option search**.
   - SQL History has no **Advanced Search** panel (period, server, database, state).
   - Nested options are not **indented and greyed out** under their parent.
   - The Command Palette can't change **options**.
   - The **AKML SQL menu** is one flat list of about 50 commands, with no separators or submenus.
4. **AKML is already ahead in several places; keep these:**
   - a search box inside the Options window
   - a description under every option
   - History: compare, export, re-execute, search-hit highlighting and syntax colouring
   - a single style window with list, options and a live preview that has four sources
   - import of SQL Prompt `.json` / `.sqlpromptstylev2` styles
   - editable built-in styles with "Reset to built-in"
   - theme tokens throughout

---

## 2. Side-by-side comparison

Severity: **High** = users see wrong or broken behaviour · **Med** = clear UX gap against SQL Prompt · **Low** = polish.

### 2.1 Options window

| Aspect | SQL Prompt 11 | AKML today | Gap |
|---|---|---|---|
| Opening it | SQL Prompt › Options…; F1 in any pane opens that pane's docs | AKML SQL › Options, editor-toolbar gear, Command Palette "AKML SQL Options" | Low: no F1 help |
| Window | "SQL Prompt – Options", resizable, one intro line | "AKML SQL Options", 880×620, resizable, custom WPF window (nothing under Tools › Options) | — |
| Page tree | 16 pages. **Suggestions:** Behavior, Connections, Join conditions, Snippets, Warnings & highlighting. **Inserted code:** Objects & statements, Qualification, Aliases, Special characters. **Format:** Styles. **Navigation.** **Queries:** Query Results, History, Color. **AI.** **Labs.** | 25 pages. **General.** **Suggestions:** Behavior, Types of suggestion, Tooltips, Connections, Join conditions, Database. **Inserted Code:** Qualification, Aliases, Special characters, INSERT statements. **Format:** Styles. **Editor:** Productivity, Navigation, Refactoring. **Queries:** History, Execution Warnings, Query Results, Execution. **Tabs:** Color. **Code Analysis.** **Snippets.** **Connections & Memory.** **AI Assistance.** **Miscellaneous:** Labs. | Med: shared pages sit in different places (Snippets, Warnings, Color, Navigation); some settings are split across pages (schema cache on 2 pages, JOIN on 3) |
| Page header | Grey band with breadcrumb and **Restore defaults** on the right | Same, plus a "?" page-help button | Match |
| Groups | Text heading and a rule line. **Child options are indented and greyed out while the parent is off.** | Heading and a rule line; rows indented 20 px; no parent/child greying | Med |
| Controls | Checkbox with its label on the right, dropdowns, **numeric spinners** (e.g. "Maximum query size: [1 MB ▾]", "remove queries older than: [7] days"), grids | Toggle rows, dropdowns, **sliders**, even for 1,000–1,000,000 entries or 128,000 tokens | Med: sliders are imprecise for big ranges |
| Help | Grey text under many options; blue "?" call-outs on some | Grey text under every option; "?" per page | AKML ahead |
| Search | **None in the dialog.** The Command Palette (Alt+S) has an **Options** tab: each option has an On/Off toggle, or opens its page with the option highlighted | Search box in the dialog (Ctrl+E) that jumps to and flashes the row; the palette has only one "Options" entry | Med: the palette can't toggle options |
| Restore defaults | Per page, and "Restore all defaults" | Same, but: per page also **wipes hidden data** (rule overrides, all AI agents and keys, connection aliases, environment severities, the active style); the confirmation shows an internal key ("'CompletionPolish' page"); "Restore all" makes a new install ID | **High** |
| Import / Export | Bottom left | Bottom left; the Import message says "Click OK or **Apply**", but there is no Apply button | Low |
| Honesty | Every visible option works | **About 40 options do nothing** (appendix A); duplicated settings (snippets in completion list, ghost text, backups) | **High** |
| Theme | Follows the SSMS theme (11.3.11+) | Own Dark / Light / System; the window closes and reopens on change. The tab-rule editor and `ManageRulesDialog` (WinForms, hard-coded light colours) are not themed, and some buttons are unstyled in dark | Med |
| Theme dropdown | — | Likely bug, confirm with a test first: `OnThemeSelectionChanged` saves **all** controls to disk and closes the window. Any code that sets the dropdown after the window opens triggers it, e.g. Restore all / Import changing the theme, or "System" on a dark host. That bypasses Cancel and can undo "Restore all defaults" | **High** |
| Tab colour | Grid: SERVER/GROUP · DATABASE · ENVIRONMENT (colour swatch); "Edit environments" dialog with colours; right-click **Tab Color (Server / Database / Group)** on tabs and in Object Explorer | A list of rules (Label, Pattern, hex colour) in an unthemed plain window; no colour picker, no reorder (although "first match wins"); `MatchTarget` / `DatabaseName` have no UI | Med |
| Naming | One name per page | Tree label, breadcrumb and title differ ("Color" / "Tabs & UI", "Execution Warnings" / "Execution Safety"); Title Case and sentence case mixed; odd labels ("Tables Alias", "Temperature (x10)"); jargon ("Phase A/B", "Future-pending") | Low |

### 2.2 SQL History

| Aspect | SQL Prompt 11 | AKML today | Gap |
|---|---|---|---|
| Opening it | "SQL History" toolbar button; dockable tab | Toolbar, AKML SQL menu, Ctrl+Alt+H, editor toolbar "⏱ History", Command Palette | AKML ahead |
| Layout | Search · "Recent queries" with refresh / star / open / closed icons · list grouped by date · "History for ‹file›" versions · preview with a dark header · **Open** | Same layout (42/58 split) | Match |
| Row | Name, time, **server + database + environment name** (tab-colour environment, e.g. green "Development"); a **vertical blue bar marks open queries**; star | Name, relative time, "×N · M versions", "● server" (green/red dot), ⋮ | Med: no database or environment. **High:** the dot is always red, because nothing records "open" |
| Date groups | Today / Yesterday / Last week… | Today / This Week / "Two Months Ago" (which actually covers 7–59 days) / Older; no counts | Low |
| Search scope | Name, path, SQL, server, database; all words must match; `*` `?` wildcards; prefixes `name: path: sql: type: date:[…] server: database: starred: open:`; CamelCase | SQL text only (name needs `name:`); runs **only on Enter**; prefixes without `path:`, `type:`, `date:`; no help in the UI | Med |
| Advanced Search | Expander: object type, period (week / month / 3 months / custom with date pickers), Server ▾, Database ▾, State (Starred / Open), Reset, "?" | Only a Source ▾ menu, filled from rows already loaded; view-model has DateFrom / DateTo / Status with no UI | Med |
| Search highlight | Search term highlighted in the preview | Same, and syntax-coloured | Match |
| Preview | Full SQL; keyboard: Tab into the preview, Ctrl+A, Ctrl+C | **Cut at 500 characters**; a TextBlock, so no selection; no line numbers or context menu | **High** |
| Row actions | Star click; hover "⋯": Open query, Rename query, Remove query and its history, Remove queries older than this; double-click opens | Richer (Copy, Open in new tab, Re-execute, Rename, Favorite, Compare, Export, Delete, Remove older), but: **Delete has no confirmation**; Delete / Favorite / versions act on the **latest run only** of a grouped row; Export sits in the row menu but exports everything; Re-execute sets no connection | **High** |
| Versions | Each version with time, server and environment; open an older version | Versions of one run only; **Open and Copy ignore the selected version**; Compare works on two rows, not two versions, with no diff highlighting | Med |
| Keyboard | ↑/↓, Enter opens, Tab to the preview | Only Enter in the search box; no Enter / Delete / F2 / Ctrl+C in the list; star and ⋮ are mouse-only | Med |
| Restore | "Restore open queries when SSMS starts", max N, "Automatically reconnect restored queries"; crash restore | Separate WinForms "Session Recovery" dialog; Ctrl+Shift+T is an in-memory list of 20 with no UI, lost on restart, and doesn't reconnect | Med |
| Settings page | Enable · Maximum query size · restore settings above · Remember advanced search · "Automatically remove queries older than [7] days" with a "?" | Enable · Record failed executions (**dead**: status is always Success) · Deduplication (described wrongly) · Retention days · Max entries (slider) · Encrypt at rest (**dead**) · Disable trim; most need a restart and don't say so | **High** / Med |
| Live update | — | No refresh after F5 while the window is open; star or delete resets scroll and selection; engine disconnected shows no Retry | Med |

### 2.3 Format style editing

| Aspect | SQL Prompt 11 | AKML today | Gap |
|---|---|---|---|
| Opening it | SQL Prompt › Edit Formatting Styles…; **Active Style ▸ › Edit Styles…**; Options › Format › Styles | AKML SQL › Format Styles…; Options › Format › Styles › "Edit formatting styles…"; Command Palette | Med: no Active Style menu |
| Structure | Two steps: a styles window (list, sample preview, Set as active / **Edit style** / Close), then a separate style editor (Save / Cancel) | One window: list │ pages │ options + live preview | AKML's is quicker; keep it |
| Style list | YOUR STYLES / REDGATE STYLES; ✔ on active; blue left bar on selected; hover ⋮ (Set as active, Edit…, Copy…); "+ Create a style…" (Name + Copy from) | YOUR STYLES / BUILT-IN STYLES; ACTIVE pill; accent border; ⋮ (Set Active, Copy, Rename…, Delete, Reset to built-in, Export…); "+ New Style" (Name + Based on); "Set as active style" | Match. Low: **Copy doesn't ask for a name**; a duplicate name is reported only after OK |
| Team sharing | "Style folder:" path + "…" (network or Dropbox folder) | None | Med (feature, P2) |
| Option search | **"Search for options…"** box | None | **Med** |
| Option rows | Label and control on one line; checkbox label beside the box; bold sub-sections | Fixed **170 px label column**; labels **clipped mid-word**; checkbox has no text, so clicking the label doesn't toggle it; notes only in tooltips | **High** (visible in screenshots) |
| Nested options | Indented and greyed out under the parent | Disabled, and a tooltip names the parent; not indented | Low |
| Integers | Spinner | 72 px text box with a range hint, no ▲▼ | Low |
| Changed from default | — | None on desktop. The web editor has a ↺ per-option reset, bold changed labels and changed counts per page | Med (AKML can beat SQL Prompt cheaply) |
| Preview | Sample for the current page; "Preview current query" checkbox | Page sample / My sample / Current query / Edit sample (richer), but **tabs drawn at the WPF default width, not the style's**; no syntax colouring; changed lines not highlighted | **High** (tabs) / Med |
| Import / Export | No import (folder based) | Import… / Export… (.json, .sqlpromptstylev2), but after Import the **ACTIVE pill and "Active:" chip stay on the old style**, and **Export ignores unsaved edits** without warning | **High** |
| Keyboard | — | No Ctrl+S, F2, Delete, Ctrl+F or access keys | Low |
| Switching active style | Menu **Active Style ▸** (styles with ✔ + Edit Styles…); editor right-click › Active Style | Only the Options dropdown or the editor window; status-bar text isn't clickable and goes stale when the style changes in Options | **Med** |
| Format SQL feedback | Popup listing reasons on failure | Failure message for Format Document only; success doesn't say which style was used | Low |
| Options › Format › Styles | "Actions: when you run Format SQL, SQL Prompt will:" apply casing, insert semicolons, expand wildcards, qualify object names, brackets, AS keyword, alias style | Behaviour / safety toggles, of which "Format on paste / save / delimiter", "Confirm before bulk format" and "Validate formatting" **do nothing** | High (dead) / Med (actions) |

### 2.4 Shared across all three

| Aspect | SQL Prompt 11 | AKML today | Gap |
|---|---|---|---|
| Top menu | Grouped with separators; Active Style ▸ submenu; Options and Help near the end | **About 50 commands in one group**: About and Check for Updates first, then Options, formatting, refactors, tabs, navigation, snippets, bookmarks and AI, with no separators or submenus | **Med** |
| Window titles | "SQL Prompt – X" | "AKML SQL Options", "AKML SQL — Format Styles Editor", "AKML SQL - Snippet Manager" (three separators) | Low |
| Accessibility | — | Icon-only buttons without automation names; mouse-only glyph buttons (★ ⋮) | Low |
| Help | F1 in any pane opens docs | No F1 | Low |

---

## 3. Plan

Each item has an **ID**, a **priority** and a **size** (S ≤ ½ day, M ≤ 2 days, L > 2 days), then *Problem → Change → Files → Done when*. Paths are under `src/`. Line numbers are from 2026-09-27 and will drift.

The phases:
- **Phase 0 (P0), make the screen tell the truth.** Do this first: every later item builds on screens whose data is correct.
- **Phase 1 (P1), match SQL Prompt's interaction patterns.**
- **Phase 2 (P2), polish and sharing.**

### Phase 0: make the screen tell the truth

**OPT-01 · Remove, wire or label every setting that does nothing** · P0 · M
- *Problem:* about 40 Options controls have no effect (appendix A). Users change them, see nothing happen, and stop trusting the product.
- *Change:* go through appendix A one row at a time:
  - **Wire** the cheap ones: `CompletionEngine.SetMaxSuggestions` (it has no caller), trigger delay, fuzzy matching, the data-type / nullability / PK-FK display switches, "Show in Error List".
  - **Hide** the rest. Drop their rows from the pages, but keep the `AppSettings` properties so config files still load.
  - Remove duplicates, keeping the working one:
    - snippets in the completion list: keep Suggestions › Behavior
    - ghost text: keep AI Assistance
    - backups: one setting
  - Remove "Keyword casing" (casing belongs to the style).
- *Files:* `Dialogs/Pages/*.cs`, `Dialogs/SettingsWindow.cs` (reset lists at around lines 1840–1900), plus the feature code for the settings you wire.
- *Done when:*
  - every visible control changes behaviour;
  - a unit test holds an allow-list of "known unwired" settings. The list must be empty, or only contain rows shown with a "Coming soon" note.

**OPT-02 · Fix the theme dropdown so it can't save behind the user's back** · P0 · S
- *Problem:* `SettingsWindow.OnThemeSelectionChanged` (around line 1660) saves every control to disk and closes the window. The dropdown can change without the user picking a theme: when values are loaded or reset, or on the System theme with a dark SSMS host. The result bypasses Cancel and can undo Restore all defaults.
- *Change:*
  - Attach the handler only after the controls are loaded.
  - Ignore changes that come from loading or resetting (a `_loading` flag).
  - When the user does change the theme, keep the **unsaved working copy** in memory and hand it to the reopened window instead of writing to disk.
  - Compare against the value the user actually picked, not `_theme`: the constructor maps "system" to Light.
- *Done when:* tests show that (a) opening with `theme: "system"` on a dark host doesn't save or close, and (b) Restore all → Cancel leaves `config.json` unchanged.

**OPT-03 · Make Restore Defaults safe and readable** · P0 · S
- *Problem:*
  - Per-page reset replaces whole sub-objects, which wipes data the page doesn't show: `CodeAnalysis.RuleOverrides`, all AI agents and keys, `Navigation.ConnectionAliases`, `Safety.EnvironmentSeverity`, and the active style.
  - The confirmation text shows internal keys.
  - "Restore all" creates a new `InstallId`.
- *Change:*
  - Reset only the fields shown on that page. Use a per-page field list that is tested against the page's rows.
  - Show the page's display name in the confirmation ("Reset the settings on Suggestions › Tooltips?").
  - Keep install and state fields on "Restore all", as Import already does (around lines 1780–1782).
- *Done when:* resetting the Code Analysis page keeps rule overrides, resetting AI Assistance says what it will remove and asks first, and a round-trip test proves hidden fields survive.

**HIS-01 · Show the full query in the preview** · P0 · S
- *Problem:* search returns `substr(sql_text, 1, 500)` (`Engine/History/HistoryDatabase.cs` around lines 999 and 1052), and the preview renders that. Long queries are silently cut off.
- *Change:* when a row or version is selected, fetch the full text with the existing `GetFullSql` action, cached per entry, and render that. Keep the 500-character snippet for the list only.
- *Done when:* a 5,000-character query shows completely, and Copy / Open use the full text.

**HIS-02 · Make "open" real, or remove it** · P0 · M
- *Problem:* rows are inserted with `is_open = 0`, and the shell never sends `SetOpenStatus`. So every dot is red, and "Show open queries only" (and `open:true`) always returns nothing.
- *Change:*
  - In `History/ExecutionCapture.cs`, send `SetOpenStatus(true)` for a document's history entries when it is executed or activated, and `false` on close (`OnDocumentClosing`).
  - When SSMS starts, mark all entries closed; the engine has `CloseByTabTitleAsync`, or add "close all".
  - Replace the red/green dot with SQL Prompt's **vertical accent bar** on open rows (section 2.2).
- *Done when:* opening a query from history turns its row "open" without a refresh, the open and closed filters return the right rows, and a test covers the transitions.

**HIS-03 · Scroll to the end of the list** · P0 · S
- *Problem:* `HasMoreEntries => _currentOffset + Entries.Count < TotalCount` (`History/HistoryViewModel.cs` line 182) counts earlier pages twice, so loading stops at about half the results.
- *Change:* use `Entries.Count < TotalCount`, and make `TotalCount` the grouped count when grouping is on.
- *Done when:* a 250-row test loads all 250.

**HIS-04 · Make row actions act on the whole grouped query** · P0 · M
- *Problem:* with grouping on, a row is a query *session*, but:
  - Delete removes only its latest run, so the row comes back showing the previous run;
  - Favorite flips one run while the star shows the maximum, so a re-run favourite can't be un-starred;
  - the version panel lists one run's snapshots.
- *Change:* add engine actions keyed by session (`COALESCE(session_id, 'hash:'||content_hash)`) for delete, favourite and get-versions. The UI calls them for grouped rows. Add a confirmation to Delete ("Remove 'query-03' and its history?"), matching SQL Prompt's "Remove query and its history".
- *Files:* `Engine/History/HistoryDatabase.cs`, `HistoryRequestHandler.cs`, `Core/Ipc/Messages/History*.cs`, `Shell.Shared/History/HistoryViewModel.cs`.
- *Done when:* after deleting a query run 3 times it doesn't reappear, starring and un-starring works after re-runs, and the versions shown match "M versions" on the row.

**HIS-05 · Keep search results correct after snapshots** · P0 · M
- *Problem:* a tab-switch or close snapshot overwrites `sql_text` and `executed_at`, but not the FTS index or `content_hash`. It also writes a second timestamp format. The result is that search misses new text, sort order breaks, and the external-content FTS index can be corrupted.
- *Change:* store snapshots only in `history_versions`, and leave the `history` row untouched. If the row must change, update FTS inside the same transaction (delete the old text, insert the new) and write one timestamp format.
- *Done when:* after a snapshot, a search finds the new text, the order is by time, and an FTS integrity check passes.

**HIS-06 · Hide options History can't honour yet** · P0 · S
- *Problem:*
  - "Record failed executions" has no effect: capture always records Success with 0 rows.
  - "Encrypt at rest" has no effect: `HistoryEncryption` is never called.
  - "Enable deduplication" is described wrongly: it only groups the list.
- *Change:* hide the first two until they are real. Rename the third "Group repeated runs of the same query". Add "Takes effect after SSMS restarts" to settings read only at start-up (Enable, Retention, Max entries, Disable trim).
- *Done when:* every History setting does what its text says.

**STY-01 · Stop clipping option labels** · P0 · S
- *Problem:* in `Formatting/FormatStylesEditorWindow.cs` (around lines 2011–2043) each label sits in a horizontal StackPanel inside a fixed 170 px column, so it never wraps. Examples: "Place subsequent items on r", "Empty lines between statem", "Insert empty line between JC".
- *Change:* follow SQL Prompt:
  - **Booleans:** a `CheckBox` whose content is the label, across the full width, wrapping.
  - **Choices and integers:** a label column that grows (`Auto`, minimum 200, maximum 45%), with wrapping, and the control beside it.
  - Put the full label in the tooltip as well.
- *Done when:* at the default window size no label is clipped on any of the 14 pages (checked with the UiTests screenshot tour), and clicking a label toggles its checkbox.

**STY-02 · Draw the preview with the style's tab width** · P0 · S
- *Problem:* the preview TextBox draws tabs at WPF's default width, which is unrelated to `whitespace.numberOfSpacesInTabs`, so tab-indented styles look misaligned (Khamis Style's preview on the Lists page).
- *Change:* before display, expand tabs to spaces **for the preview only**, using the style's tab size and column-aware tab stops. The SQL the formatter returns stays as it is.
- *Done when:* Khamis Style's preview lines up with SSMS set to the same tab size.

**STY-03 · Keep the style list true after Import, and warn on Export** · P0 · S
- *Problem:*
  - Import makes the new style active but doesn't refresh the list: the ACTIVE pill, the "Active:" chip and "Set as active style" all stay on the old style.
  - Export writes the saved file and silently drops unsaved edits.
- *Change:*
  - After import, run the same refresh as set-active (around line 1128).
  - Before Export with unsaved edits, ask "Save changes to 'X' before exporting?" (Yes / No / Cancel).
- *Done when:* both flows are covered by view-model tests.

### Phase 1: match SQL Prompt's interaction patterns

**OPT-04 · Re-arrange the Options tree around SQL Prompt's** · P1 · M
- *Change:* use SQL Prompt's group and page names where a page exists in both products. Group AKML-only pages after them. Proposed tree:
  - **General**
  - **Suggestions:** Behavior · Types of suggestion · Tooltips · Connections · Join conditions · **Snippets** (moved from top level) · **Warnings & highlighting** (Execution Warnings moved here, as in SQL Prompt)
  - **Inserted code:** Objects & statements (today's "INSERT statements", renamed) · Qualification · Aliases · Special characters
  - **Format:** Styles
  - **Navigation** (moved out of Editor, as in SQL Prompt)
  - **Queries:** Query Results · History (also takes Restore on startup, max tabs and reconnect from Tabs › Color, as in SQL Prompt) · **Color** (moved from Tabs) · Execution
  - **Editor:** Productivity · Refactoring
  - **Code Analysis**
  - **Connections & Memory**, which also takes "Suggestions › Database" so all schema-cache settings are in one place
  - **AI Assistance**
  - **Labs**
- Make the tree label, breadcrumb and `IPageBuilder.Title` one string, and use sentence case everywhere.
- Rewrite odd labels and jargon, e.g. "Tables Alias" → "Suggest table aliases"; "Temperature (x10)" → "Creativity (temperature)"; remove "Phase A/B" and "Future-pending".
- *Files:* `Dialogs/SettingsWindow.cs` (tree at around lines 527–570, page list, reset switch), `Dialogs/Pages/*.cs`, `OptionsNavStructureTests`.
- *Done when:* the nav-structure test pins the new tree, every existing deep link still works (`ShowOptions("AI Assistance")` etc.), and there is no duplicate or split setting.

**OPT-05 · Indent and grey out child options** · P1 · M
- *Change:* add a `parent` to `RowFactory` toggles, dropdowns and so on. A child row is indented one step more and disabled, with its label greyed, while the parent is off. It updates live. Apply to: Enable IntelliSense → all Behavior rows; Enable SQL history recording → History rows; Show execution warnings → each warning; Enable tab coloring → the rules grid; Enable code analysis → its rows; Enable snippets → snippet rows.
- *Files:* `Dialogs/Pages/RowFactory.cs`, the pages.
- *Done when:* turning a master switch off greys all its children on that page.

**OPT-06 · Numbers: spinners, not sliders, for wide ranges** · P1 · S
- *Change:* add a `RowFactory.AddNumber(label, min, max, step, unit)` with a text box, ▲▼ and a unit label ("days", "MB", "entries", "tokens"). Use it where the range is more than 100 values: History max entries, retention, AI max tokens, refresh intervals. Keep sliders for small ranges (e.g. max suggestions 5–200).
- *Done when:* the value 250,000 can be typed exactly.

**OPT-07 · Change options from the Command Palette** · P1 · M
- *Change:*
  - Index every registered search row (the Options search index already exists) as palette items under a new **Options** category, shown as "Suggestions › Behavior › Show column data types".
  - Booleans show an On/Off toggle that saves at once, through `OptionsCommand.SaveAndNotify`.
  - Other types open Options on that row with the existing flash.
  - This is what SQL Prompt's palette does.
- *Files:* `Productivity/CommandPalette/CommandRegistry.cs`, `CommandPaletteViewModel.cs`, `Dialogs/SettingsWindow.cs` (expose the search index).
- *Done when:* typing "nullab" in the palette shows the option and toggles it.

**OPT-08 · Tab colour like SQL Prompt** · P1 · M
- *Change:* replace the rule list and plain editor with:
  - a themed grid: **Server / group pattern · Database · Environment** (swatch + dropdown), with "+ Add server/database", ↑↓ order ("first match wins") and the hint "You can use wildcards (*)";
  - an **Edit environments** dialog: Name · Colour (colour picker, not hex text) · "Use gradient colours" · "Restore default environments".
  - Expose `MatchTarget` / `DatabaseName`.
- *Files:* `Dialogs/Pages/TabsPage.cs`, `Dialogs/SettingsWindow.cs` (around lines 2001–2151), tab-colouring config.
- *Done when:* a Production rule can be added without typing hex, and the grid is readable in dark theme.

**OPT-09 · Theme every Options sub-dialog** · P1 · M
- *Change:*
  - Port `Analysis/ManageRulesDialog.cs` from WinForms with hard-coded light colours to WPF on `ThemeTokens`, with SQL Prompt's layout: a checklist grouped by category, a description pane, "Settings file:" path.
  - Theme `RowFactory.AddButton`, `AiAgentListView.MakeButton`, the tab-rule editor and the search badges.
- *Done when:* the dark-theme screenshot tour shows no light-grey buttons or white panels.

**HIS-07 · Search as you type, with help** · P1 · S
- *Change:*
  - Search 250 ms after typing stops, and still on Enter.
  - Select the first result so the preview isn't empty.
  - Search name, path, server and database as well as SQL, as SQL Prompt does.
  - Add a "?" next to the box with a popup listing the prefixes (`name: sql: server: db: starred: open: date:`), quotes, `OR` / `NOT` and `*`.
  - Show the placeholder "Search".
- *Done when:* typing filters the list, and the "?" popup matches `HistorySearchParser`.

**HIS-08 · Advanced search panel** · P1 · M
- *Change:* add a collapsible **Advanced search** under the box, as in SQL Prompt:
  - Period: Everything / Last week / Last month / Last 3 months / Custom (two date pickers), wired to the existing `DateFrom` / `DateTo`;
  - Server ▾ and Database ▾, filled from the engine's distinct lists (`GetDistinctServersAsync`), not from loaded rows;
  - State: Starred, Open;
  - Reset.
- Show active filters as small removable chips under the search box. Add "Remember advanced search settings" to Options › History.
- *Done when:* each filter narrows the list and Reset clears them.

**HIS-09 · Richer, SQL Prompt-like rows** · P1 · S
- *Change:*
  - Line 2 right shows "server · database". Add the **tab-colour environment name in its colour** when a rule matches (e.g. green "Development").
  - Open rows get a 3 px accent bar on the left (after HIS-02), replacing the dot.
  - Date groups become Today / Yesterday / This week / Last week / This month / Older, each with a count.
  - Keep "×N · M versions", but in the muted colour.
- *Done when:* the list reads like Redgate's screenshot, plus the counts.

**HIS-10 · Versions you can open and compare** · P1 · M
- *Change:*
  - Selecting a version drives Open, Copy and Re-execute: they use that version.
  - Add a page glyph and "server · environment" to each version row.
  - Add **Compare with current** on a version.
  - Upgrade `HistoryDiffWindow` to show line-level diff highlighting (added, removed, changed) with headers naming each side and its time.
- *Done when:* opening v2 of 4 opens v2's text, and the compare window marks changed lines.

**HIS-11 · Selectable preview and keyboard use** · P1 · M
- *Change:*
  - Preview: a read-only, selectable, syntax-coloured view with line numbers and a context menu (Copy, Copy all, Open), e.g. a RichTextBox fed by `SqlPreviewTokenizer`.
  - List keys: ↑/↓, Enter = open, Delete = remove (with confirmation), F2 = rename, Ctrl+C = copy SQL, Space = star, Tab / Shift+Tab between list and preview.
  - Turn star and ⋯ into real focusable buttons with `AutomationProperties.Name`.
- *Done when:* the whole window can be used without a mouse.

**HIS-12 · Tidy the row menu** · P1 · S
- *Change:*
  - Show ⋯ on hover only, as SQL Prompt does.
  - Use its wording: Open query · Rename query · Remove query and its history · Remove queries older than this.
  - Keep AKML's extras: Copy SQL · Open in new tab · Re-execute · Compare.
  - Move **Export…** and a new **Clear history…** to a "⋯" overflow in the window's toolbar; the engine already supports delete-all.
  - Show a readable date in "Remove older" (not raw ISO).
  - Re-execute uses the entry's connection or asks for one.
- *Done when:* no action in the row menu affects rows other than the selected one.

**HIS-13 · Live and resilient** · P1 · S
- *Change:*
  - Refresh the list after each F5 while the window is open, keeping selection and scroll position; also keep them after star and delete.
  - Show a spinner instead of "Loading…", using the SchemaProgressMargin ellipse pattern.
  - When the engine is disconnected, show a **Retry** button and auto-refresh on reconnect.
  - Add empty states for the preview and versions panes.
- *Done when:* running a query adds it to the open window within 1 s.

**HIS-14 · One restore story** · P1 · M
- *Change:*
  - Move restore settings to Options › Queries › History, in SQL Prompt's wording: "Restore open queries when SSMS starts", "Maximum number of queries to restore", "Automatically reconnect restored queries".
  - Show recently closed tabs in SQL History (Closed filter, after HIS-02) instead of the invisible 20-item Ctrl+Shift+T stack.
  - Persist that stack and reconnect on restore.
  - Restyle the WinForms "Session Recovery" dialog in WPF with theme tokens.
- *Done when:* a closed, never-saved tab can be found and reopened from SQL History after an SSMS restart.

**STY-04 · "Search for options…" in the style editor** · P1 · M
- *Change:*
  - Add a search box above the page tree. Typing filters the tree to pages with matches, shows a match count per page, and highlights matching rows on the right.
  - Enter jumps to the first match. Esc clears the search.
  - Search labels, descriptions and option paths (`lists.placeCommasBeforeItems`).
- *Files:* `Formatting/FormatStylesEditorWindow.cs`, `FormatStylesSchemaModel.cs`.
- *Done when:* typing "comma" leaves Lists (and any other page with comma options) with its rows highlighted.

**STY-05 · Show what changed** · P1 · M
- *Change:* port the web editor's logic (`src/AkmlSql.Web/Pages/Styles.razor`, around lines 91–97, 157–197, 216–217 and 269–270) to WPF. The web editor itself stays as it is. Desktop gets:
  - changed labels in bold;
  - a small ↺ per changed option to reset it to the base style;
  - a changed count per page in the tree ("Lists  3");
  - after an option change, the preview lines that moved, highlighted for about 2 s.
- *Done when:* changing one option shows the bold label, the page count and the highlighted preview lines, and ↺ restores it.

**STY-06 · A preview that reads like code** · P1 · S
- *Change:*
  - Syntax-colour the preview with `Core/Text/SqlPreviewTokenizer.cs`, already used by History.
  - Add line numbers.
  - Keep the dark panel, but take its colours from theme tokens, not the hard-coded `#1E2230`.
- *Done when:* keywords, strings and comments are coloured in both themes.

**STY-07 · Active Style menu, as in SQL Prompt** · P1 · M
- *Change:*
  - Add an **Active Style ▸** submenu to the AKML SQL menu and to the SQL editor's right-click menu. It is a dynamic list of styles with ✔ on the active one, then a separator and **Edit Styles…**. Use a VSCT `DynamicItemStart` command.
  - Choosing a style saves `Formatter.ActiveProfile` and updates the status bar at once.
  - Also refresh the status-bar text when the active style changes in Options. Honour the "Show active style in status bar" toggle everywhere; today the editor updates it even when the toggle is off.
- *Files:* `Ssms22/AkmlSqlSsms22.vsct`, a new `Commands/ActiveStyleMenuCommand.cs`, `StatusBar/StatusBarManager.cs`, `Dialogs/Pages/FormattingPage.cs`.
- *Done when:* switching style from the menu changes the next Format Document with no dialog.

**STY-08 · Friendlier style-list actions** · P1 · S
- *Change:*
  - **Copy…** asks for a name, pre-filled "X copy".
  - The New / Rename dialogs show "A style named 'X' already exists" as you type, and OK is disabled until the name is valid.
  - Keys: Ctrl+S save, F2 rename, Delete delete (not built-ins or the active style), Ctrl+F focus search, Enter on a row = set active.
  - Integer options get ▲▼ spinners.
  - "Takes effect when…" notes show as grey text under the option, not only in tooltips.
  - Nested options are indented under their parent.
- *Done when:* each key works and there are no hidden-only notes.

**STY-09 · Say which style formatted the code** · P1 · S
- *Change:* after Format Document or Format Selection succeeds, show "Formatted with 'Khamis Style'" in the status bar for a few seconds. Give Format Selection the same "style could not be loaded" warning as Format Document.
- *Done when:* both commands report the style.

### Phase 2: polish and sharing

**X-01 · Organise the AKML SQL menu** · P2 · M
- *Change:* split the single VSCT group into groups with separators and submenus, following SQL Prompt:
  1. Format Document · Format Selection · Unformat Document · **Active Style ▸** · Edit Formatting Styles… · Bulk Format…
  2. **Refactor ▸:** Smart Rename · Script as ALTER · Inline EXEC · Inline Stored Procedure · Convert INSERT to UPDATE · Split Table · Generate CRUD Procedures · Find Invalid Objects
  3. **Navigate ▸:** Go To Definition · Peek Definition · Find All References · Object Search · Next / Previous Statement · Matching Pair · Document Outline · Bookmarks
  4. **Tabs & history:** SQL History · Restore Closed Tab · Close All Unmodified · Duplicate Tab · Pin Tab
  5. **AI ▸:** Chat Panel · Text to SQL · Explain · Fix · Optimize · Index Analysis
  6. Snippet Manager · Manage Code Analysis Rules… · Toggle Code Analysis · Command Palette
  7. Options…
  8. **Help ▸:** Send Feedback · View Logs · Refresh Schema Cache · Check for Updates · About AKML SQL
- *Files:* `Ssms22/AkmlSqlSsms22.vsct`, plus menu items added in code (`AkmlSqlPackage.cs` around line 445).
- *Done when:* the menu fits on one screen, and About / Check for Updates are last.

**X-02 · One naming style for windows** · P2 · S
- Use "AKML SQL – ‹Window›" everywhere (Options, Format Styles, Snippet Manager, Code Analysis Rules, SQL History Comparison…), with the AKML icon.

**X-03 · F1 help** · P2 · S
- F1 in any Options page, the Format Styles window or SQL History opens that topic on the product site's docs (`https://akml.khamis.work/docs/...`, spec 034).

**X-04 · Accessibility and type tokens** · P2 · S
- Give every icon-only button an `AutomationProperties.Name`.
- Replace hard-coded font sizes in `HistoryToolWindowControl.cs` (9.5–14) with `Typography` tokens.
- Make the History converters (`OpenClosedColorConverter`, `FavoriteColorConverter`) update on theme change.

**STY-10 · Shared style folder for teams** · P2 · L
- Add "Style folder:" with a path and "…" at the top of the style list, as in SQL Prompt: a network or cloud folder whose styles show as "TEAM STYLES" (read-only unless the folder is writable). The engine's profile loader reads the extra folder.

**STY-11 · Format SQL "Actions"** · P2 · L (needs engine work)
- Replace Format › Styles' dead toggles with SQL Prompt's **"When you run Format SQL, AKML SQL will:"** list: Apply layout · Apply casing · Insert semicolons · Expand wildcards · Qualify object names · Add/remove square brackets · Add/remove AS · Apply column alias style. Each maps to an existing format-time action in the pipeline.

---

## 4. Order and effort

| Step | Items | Size | Why this order |
|---|---|---|---|
| 1 | OPT-02, OPT-03, HIS-01, HIS-03, STY-01, STY-02, STY-03 | ~4 days | Small fixes for things users notice at once (lost settings, cut-off preview, clipped labels, broken preview) |
| 2 | OPT-01, HIS-02, HIS-04, HIS-05, HIS-06 | ~6 days | The honesty work; needs engine changes for History |
| 3 | STY-04, STY-05, STY-06, STY-07, STY-08, STY-09 | ~7 days | Style editing is the most visible feature (and the one on LinkedIn) |
| 4 | HIS-07 … HIS-14 | ~8 days | SQL History interactions |
| 5 | OPT-04 … OPT-09 | ~8 days | Options structure and theming |
| 6 | X-01 … X-04, STY-10, STY-11 | ~8 days | Polish and sharing |

Tests for every step:
- **View-model tests:** in `tests/AkmlSql.Shell.Shared.Tests` (or the matching test project) for each view-model change.
- **Engine tests:** for the History engine changes.
- **Screenshot tour:** a run of `tests/AkmlSql.UiTests` (FlaUI) in light and dark theme at the end of each step. Keep the Northwind-only rule for any screenshot that may be published.

---

## 5. Decisions for you

1. **Settings that do nothing (OPT-01): wire or hide?**
   - *Recommendation:* wire the ~8 cheap ones and hide the rest now. A "Coming soon" row still looks broken.
2. **Options tree (OPT-04): follow SQL Prompt exactly, or keep AKML's grouping?**
   - *Recommendation:* follow SQL Prompt for pages both products have (users switching from SQL Prompt find things where they expect), and keep AKML-only groups after them.
3. **Style window: keep one window, or split into two steps as SQL Prompt does?**
   - *Recommendation:* keep one window. It is faster, and the gaps are in the details (STY-01 … STY-09), not the structure.
4. **Live preview colours: keep the fixed dark panel, or follow the theme?**
   - *Recommendation:* follow the theme through tokens (STY-06), so the light theme doesn't show a dark block.

---

## Appendix A: Options that change nothing (static search, 2026-09-27)

Method: for each setting on an Options page, search `src/` for readers outside the Options pages, `AppSettings` and tests. Treat this as strong evidence, not proof: confirm each row when you work on it.

| Page | Setting(s) with no reader |
|---|---|
| Suggestions › Behavior | Enable fuzzy matching · Keyword casing · Show column data types · Show nullability info · Show PK/FK indicators · Trigger delay (ms) · Maximum suggestions (`SetMaxSuggestions` has no caller, so the limit stays at 50) |
| Suggestions › Types of suggestion | List all database columns after SELECT |
| Suggestions › Tooltips | Everything except "Show the object definition box" |
| Suggestions › Database | All three (auto-refresh, interval, detect DDL) |
| Connections & Memory | All three cache settings |
| Inserted Code › Qualification | Qualify columns with table name or alias |
| Format › Styles | Format on paste · Format on save · Format on delimiter (stub handlers, never created) · Confirm before bulk format · Validate formatting preserves semantics |
| Editor › Productivity | Named regions |
| Editor › Navigation | Go to Definition · Peek Definition (toggles) |
| Editor › Refactoring | Show preview before applying · Include string literals in rename scope · Rename scope |
| Queries › History | Encrypt at rest · Record failed executions (always Success) |
| Queries › Query Results | Freeze headers |
| Code Analysis | Show in Error List |
| Snippets | Show in IntelliSense completions (the working one is on Behavior) · Filter by SQL context · Track usage for ranking · Personal folder (the engine hard-codes the path) |
| AI Assistance | Chat panel toggle |
| Labs | All three toggles |

Also stale text: "Include linked-server objects" says "currently has no effect", but it now works.

## Appendix B: SQL Prompt references

- Options and restore defaults: https://documentation.red-gate.com/sp/managing-sql-prompt-behavior
- Current Options tree (Aug 2026 screenshot): https://documentation.red-gate.com/sp/ai-in-sql-prompt/enabling-disabling-ai-features-in-sql-prompt
- Sharing settings: https://documentation.red-gate.com/sp/managing-sql-prompt-behavior/sharing-your-settings
- Command Palette (Options tab): https://documentation.red-gate.com/sp/command-palette
- SQL History: https://documentation.red-gate.com/sp/ssms-tab-management/sql-history
- Searching SQL History (prefixes, Advanced Search): https://documentation.red-gate.com/sp/ssms-tab-management/sql-history/searching-sql-history
- SQL History as a safety net (rows, environment, open bar): https://www.red-gate.com/hub/product-learning/sql-prompt/sql-prompt-safety-net-features-ssms-sql-history/
- Tab colouring: https://documentation.red-gate.com/sp/ssms-tab-management/coloring-query-tabs
- Formatting styles: https://documentation.red-gate.com/sp/sql-code-formatting-and-styles/customizing-your-formatting-style
- Shared style folder: https://documentation.red-gate.com/sp/sql-code-formatting-and-styles/using-a-shared-folder-for-formatting-styles
- Code analysis rules window: https://documentation.red-gate.com/sp/sql-code-analysis/enabling-disabling-code-analysis-rules
- Theme support (11.3.11+): https://documentation.red-gate.com/sp/theme-switching-support-aligned-with-ssms-21-ssms-22

Not verified in Redgate's docs, so treat as open:
- the current built-in style names;
- whether the ⋮ menu has Rename / Delete / Export;
- how the style editor's option search behaves (filter or highlight);
- whether Ctrl+Q opens SQL History;
- the Options dialog's default size.
