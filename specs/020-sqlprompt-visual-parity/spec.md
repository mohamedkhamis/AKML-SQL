# Feature Specification: SQL Prompt Visual Parity Across All AKML-SQL Surfaces (with Format & Upload Formatter Gap Closure)

**Feature Branch**: `020-sqlprompt-visual-parity`
**Created**: 2026-05-13
**Status**: Draft
**Input**: User description: "please scan all styles and screens size and color based on sql prompt documents, and write for all features" — interpreted with user confirmation as: (a) drive visual parity (colors, screen sizes, styles) with Redgate SQL Prompt across every AKML-SQL screen, and (b) close functional gaps in SQL Format and "Upload Formatter" (import of SQL Prompt format style files).

**Reference material**: `doc/SQL-PROMPT/SQL-Prompt-Features/` (Core + AI), `doc/SQL-PROMPT/SQL-Prompt-Option/`, `doc/SQL-PROMPT/SQL-Prompt-History/` — each containing full design tables (window sizes, color hex values per element, fonts, layouts) plus 14 SVG mockups.

---

## Clarifications

### Session 2026-05-13

- Q: What counts as a passing match for SC-007 (formatter parity vs SQL Prompt)? → A: Normalise trailing whitespace per line, normalise line endings to LF, strip UTF-8 BOM, then require byte-exact equality. Anything still different is a mismatch.
- Q: Does AKML-SQL ship Redgate's built-in styles, or only support user import? → A: Ship 3–5 read-only Native styles transcribed from SQL Prompt's documented defaults (Compact, Indented, AlignedLeftBracket, …). User must fork-to-Native copy to edit. Do not redistribute Redgate-authored `.sqlpromptstyle` binaries.
- Q: Tab Coloring (FR-011) — visual only, visual + audit, or visual + full functional parity? → A: Visual parity is in scope (swatch palette + chrome). An audit of Phase 5's existing assignment rules against SQL Prompt's documented rules is in scope and produces a written gap report. Closing any functional gaps is OUT of scope for this spec.
- Q: How should the editor present settings from an imported `.sqlpromptstyle` that AKML doesn't yet support? → A: Each unsupported setting appears in the tree under its real group with the control disabled, the imported value visible, and a "not yet supported" badge. Value remains in `PassthroughUnknownKeys` for round-trip. No separate bottom panel.
- Q: Active-style scope — global, per-host, per-host-family, or per-server-connection? → A: Global. One `ActiveProfile` per user, shared across SSMS 20/21/22 and VS 2019/22/26, matching SQL Prompt's own behaviour. Lives in `AppSettings.FormatterSettings.ActiveProfile`.

---

## User Scenarios & Testing *(mandatory)*

### User Story 1 — Unified visual theme across every AKML-SQL surface (Priority: P1)

A SQL developer who is already a daily SQL Prompt user installs AKML-SQL in SSMS or Visual Studio. Every visible AKML-SQL surface — the suggestion popup, the Options dialog, the Format Styles editor, the SQL History window, the Code Analysis output, AI window, tab colours, editor margins, tooltips, dialogs — looks like the same product family as SQL Prompt: same dark/light palette, same accent blues, same icon colours, same window proportions, same fonts, same spacing.

**Why this priority**: This is the foundation. If AKML-SQL looks unfamiliar to a SQL Prompt user, every other parity claim suffers credibility loss. A unified colour/sizing/style baseline is also what makes every individual surface "look right" without per-feature tuning.

**Independent Test**: Take a screenshot of any AKML-SQL surface in dark theme; place beside the corresponding SQL Prompt screen from `doc/SQL-PROMPT/` SVG; verify background, border, accent, text, and icon colours all match the documented hex values for that theme (Light or Dark), and that dimensions fall inside the documented min/preferred sizes. A reviewer can sample 5 surfaces and validate without seeing any other story shipped.

**Acceptance Scenarios**:

1. **Given** AKML-SQL is installed and the host's theme is Dark, **When** the user opens the suggestion popup, **Then** the popup background, border, item-row hover, selected-row highlight, icon-badge palette, and font match the values defined in `doc/SQL-PROMPT/SQL-Prompt-Features/SQL_Prompt_Features_Core.md §1.1` for Dark theme.
2. **Given** AKML-SQL is installed and the host's theme is Light, **When** the user opens the same suggestion popup, **Then** every chrome colour switches to the documented Light-theme values without any element retaining a hardcoded dark colour.
3. **Given** any AKML-SQL chrome surface (popup, dialog, tool window, margin, adornment), **When** the host theme changes at runtime, **Then** the surface re-themes itself without requiring a restart.
4. **Given** a SQL Prompt user runs both products side-by-side, **When** they compare the same conceptual screen, **Then** the family resemblance is clear at a glance — proportions, accent colour, typography, and iconography all align.

---

### User Story 2 — Import an existing SQL Prompt format style ("Upload Formatter") (Priority: P1)

A team standardised on SQL Prompt has a shared `.sqlpromptstyle` file in their repo. A team member who has switched to AKML-SQL opens AKML-SQL's Format Styles editor, clicks an Import / Upload button, picks the team's `.sqlpromptstyle` file, and gets a working style inside AKML-SQL with the same name, identical settings, and live preview confirming the formatting output matches what SQL Prompt produces.

**Why this priority**: This is the primary "switch from SQL Prompt to AKML-SQL" pathway the user explicitly called out. Without it, teams who depend on a shared house style cannot move. It is also independently shippable — the import path lives entirely inside the Format Styles editor and does not require visual parity to land first.

**Independent Test**: Take any real-world `.sqlpromptstyle` file, import it into AKML-SQL, format a representative SQL corpus, diff the output against what SQL Prompt v11 produces for the same input. Pass = ≥ 95 % of files match per the SC-007 normalisation rule (trailing whitespace stripped, line endings normalised to `\n`, BOM removed, then byte-exact).

**Acceptance Scenarios**:

1. **Given** the user has a `.sqlpromptstyle` JSON file on disk, **When** they click "Import…" in the Format Styles editor and select the file, **Then** a new AKML-SQL style appears in the style list with the name from the file's `metadata.name`, every settable option populated from the JSON, and an entry in the recent-imports history.
2. **Given** an imported style is selected, **When** the user formats a sample SQL document, **Then** the output matches what SQL Prompt would produce for the same input under the same style for the documented setting matrix (whitespace, lists, parentheses, casing, DML, DDL, JOINs, CASE, operators, IN statements, CTE), per the SC-007 match definition (trailing whitespace, line endings, and BOM normalised before byte-exact comparison).
3. **Given** a `.sqlpromptstyle` file references a setting AKML-SQL does not yet support, **When** the user imports it, **Then** the import succeeds and each unsupported setting appears in the settings tree at its natural group location with the control disabled, the imported value visible, and a "not yet supported" badge — no setting is silently dropped, and the value is preserved for round-trip on export.
4. **Given** a malformed `.sqlpromptstyle` file, **When** the user attempts to import it, **Then** the user sees a clear error naming the JSON section that failed validation, and no half-imported style is created.
5. **Given** AKML-SQL's own native format profile and the shipped read-only Native built-ins (Compact, Indented, AlignedLeftBracket transcribed from SQL Prompt defaults) exist, **When** the user imports a SQL Prompt style, **Then** the native profile and shipped built-ins are untouched and all coexist in the style list — the user is never forced to migrate, and the read-only built-ins remain visible as a no-import-needed starting point.

---

### User Story 3 — Options dialog and Format Styles editor look and feel (Priority: P2)

A user opens AKML-SQL's Options dialog. They see the same three-pane layout SQL Prompt uses (tree on the left, content panel on the right, button bar at the bottom), the same page hierarchy (Suggestions → Behavior / Types / Database; Inserted Code → Qualification / INSERT / JOIN; Format → Styles; Queries → History / Execution Warnings / Query Results; Tabs → Color; Code Analysis; Snippets; Prompt AI; Miscellaneous → Labs), the same widths and colours, and the same bottom-bar buttons (Restore All Defaults, Import…, Export… on the left; OK, Cancel on the right).

The Format Styles editor (opened from Format → Styles) likewise mirrors SQL Prompt's three-vertical-panel layout: style list (left), settings tree (middle), settings controls + live preview (right).

**Why this priority**: These are the deepest, most-used setting surfaces in the product, and where visual deviation is most jarring because users return to them repeatedly. Independent of P1 because each setting page can be re-skinned individually once the token system from P1 exists.

**Independent Test**: Open Options to each top-level page in the documented order; verify dialog size, tree-nav width, page title styling, row zebra striping, and button bar layout match the values in `doc/SQL-PROMPT/SQL-Prompt-Option/SQL_Prompt_Options_Dialog.md §18`. Open Format Styles editor; verify the three-panel split and the settings tree categories match `§8` and `SQL_Prompt_Features_Core.md §2`.

**Acceptance Scenarios**:

1. **Given** AKML-SQL is installed, **When** the user opens the Options dialog, **Then** the dialog size is approximately 880 × 600 with a documented min of 700 × 500, resizable, modal, with a left-side tree at ≈ 220 px and the documented page hierarchy.
2. **Given** the Options dialog is open, **When** the user navigates between pages, **Then** the page title is rendered in the documented accent colour, sections have zebra-striped setting rows, and the "Restore Defaults" link appears in the top-right corner of every page.
3. **Given** the user opens the Format Styles editor, **When** they look at the layout, **Then** they see three vertical panels (style list, settings tree, settings + live preview), with the same category groupings as SQL Prompt's tree (Global → Whitespace / Lists / Parentheses / Casing; Statements → DML / DDL / CTE / Control Flow / Variables; Clauses → Joins / INSERT columns; Expressions → CASE / Operators / Function calls / IN / VALUES; Other → Semicolons / Comments).
4. **Given** a setting page is open, **When** the user clicks "Restore Defaults", **Then** only that page's settings reset; clicking "Restore All Defaults" in the bottom bar resets every page after a confirmation.

---

### User Story 4 — IntelliSense surfaces (suggestion popup, object definition box, column picker, snippet manager) (Priority: P2)

A developer types SQL in the editor. The suggestion popup, object-definition side panel, column picker, and snippet manager all match the SQL Prompt visual specification: floating popup with dark background and accent border, 28 px item rows, 18 × 18 colour-coded icon badges per object type (Table = yellow, View = teal, Column = blue, Stored Proc = purple, Function = orange, Snippet = green, Keyword = gray, Database = red, Schema = green, Trigger = dark red, Index = dim gray, Synonym = teal), Ctrl-held semi-transparency, monospaced font.

**Why this priority**: This is the most frequently seen AKML-SQL surface and the one most directly compared to SQL Prompt by every user. Independently shippable — re-skinning the popup does not require any other surface to land.

**Independent Test**: Place AKML-SQL popup screenshot next to `doc/SQL-PROMPT/SQL-Prompt-Features/images/01_suggestion_popup.svg`; verify container colour, border, drop shadow, item row height, icon palette, and font all match.

**Acceptance Scenarios**:

1. **Given** the user types in the editor, **When** the suggestion popup appears, **Then** the popup chrome (background, border, shadow, item height, font) matches the documented dark-theme values, and switches to documented light-theme values when the host theme is Light.
2. **Given** the popup is visible, **When** the user holds `Ctrl`, **Then** the popup becomes semi-transparent so editor text behind it remains readable, and reverts to opaque when `Ctrl` is released.
3. **Given** a suggestion item is highlighted, **When** the object-definition box appears to the right, **Then** it uses the same chrome palette and shows a Summary tab by default with Script tab available, with click-once-to-prefer-Script behaviour.
4. **Given** the cursor is on `*` inside a SELECT, **When** the user presses `Tab`, **Then** the column picker opens with the documented modal chrome, checkbox-per-column rows, key-icon badges, sort order toggle, Select-All toggle, and the documented keyboard behaviour (Space toggle / Enter accept / Esc cancel).
5. **Given** the user opens Snippet Manager, **When** they look at the layout, **Then** the chrome matches the SQL Prompt mockup (`08_column_picker_snippets.svg`).

---

### User Story 5 — Format settings & live preview parity (functional gap closure) (Priority: P2)

Beyond visual parity, the user wants every SQL Prompt format setting to be exposed in AKML-SQL's Format Styles editor with equivalent semantics and a live preview pane that updates as settings change. This closes the functional gaps the user called out for "sql format" — not just looks but behaviour.

**Why this priority**: Pairs with US2 (import). Importing a `.sqlpromptstyle` is only useful if AKML-SQL actually honours every setting in it. Independently testable because each setting has a documented input → output mapping.

**Independent Test**: For each section in `SQL_Prompt_Features_Core.md §2.3`, set the AKML-SQL setting to a non-default value, format a target SQL snippet, verify output matches SQL Prompt's output for the same input + setting. Maintain a matrix table in `tests/format-parity/` and pass the story when every row is green.

**Acceptance Scenarios**:

1. **Given** the Format Styles editor is open, **When** the user expands each tree node, **Then** every setting documented in `SQL_Prompt_Features_Core.md §2.3` is present with the same name, type (bool / enum / number / range), and default value.
2. **Given** the user changes any setting, **When** they look at the preview pane, **Then** the preview re-formats the displayed SQL sample within 250 ms using the new setting.
3. **Given** the user clicks "Save as…" / "Export", **When** they choose a destination, **Then** AKML-SQL writes a JSON file in the `.sqlpromptstyle` schema (so the same file can be re-imported into SQL Prompt by another team member, round-trip preserving every setting AKML-SQL knows about).
4. **Given** a user formats SQL via `Ctrl+K, Y` (the SQL Prompt shortcut) with the active style applied, **When** they compare output to SQL Prompt's output with the same style on the same input, **Then** the outputs are byte-identical after the SC-007 normalisation (trailing whitespace stripped, line endings = `\n`, BOM removed) for ≥ 95 % of a representative corpus (covering whitespace, lists, parentheses, casing, DML, DDL, JOINs, CASE, operators, IN, CTE).

---

### User Story 6 — SQL History window, Tab Coloring, Code Analysis surfaces (Priority: P3)

A user opens AKML-SQL's SQL History window (in SSMS). The three-panel layout (query list left, version history middle, code preview right), the search bar with "Advanced search" link, the All / Starred / Open / Closed filter button group, the per-query status icons (green open / red closed / gold star), version-list metadata, the syntax-highlighted preview, and the colour palette all match SQL Prompt's design. Tab coloring uses the same colour swatches and assignment rules. Code Analysis squiggles and the message list use the same severity palette.

**Why this priority**: Important parity surfaces but lower-frequency than IntelliSense / Options / Format. Independent of P1 once the token system exists.

**Independent Test**: Open SQL History; compare with `doc/SQL-PROMPT/SQL-Prompt-History/` SVGs and the colour table in `§16.2`. Apply tab coloring to a connection; verify colour swatches and rules match. Trigger a Code Analysis warning; verify severity colouring.

**Acceptance Scenarios**:

1. **Given** the SQL History window is open in dark theme, **When** the user inspects each region, **Then** background, search bar, filter buttons, selected-query highlight, open / closed icons, star colour, query-name text, metadata text, search-match highlight, code-preview background, and version-current label all match the values in `doc/SQL-PROMPT/SQL-Prompt-History/SQL_Prompt_SQL_History.md §16.2`.
2. **Given** the user assigns a colour to a server / database **using the existing Phase 5 assignment behaviour**, **When** they look at the tab title bar, **Then** the tab adopts the documented SQL Prompt colour swatch (visual parity) — assignment-rule semantics are inherited from Phase 5 and are audited but not modified by this feature.
3. **Given** Code Analysis flags an issue, **When** the user sees the squiggle and the message-list entry, **Then** the severity colour matches the documented palette (Error / Warning / Suggestion).

---

### User Story 7 — Prompt AI window, ghost text, tooltips, editor margins (Priority: P3)

The Prompt AI window (when AI features are enabled), inline ghost-text suggestions, hover tooltips, and editor-margin indicators (schema-loading spinner, etc.) all conform to the SQL Prompt visual spec.

**Why this priority**: Lower frequency surfaces, but still in scope for "all features" coverage. Independently shippable.

**Independent Test**: Compare each surface against `06_ai_window.svg`, `07_ai_ghost_text.svg`, and the established `SchemaProgressMargin` pattern. Verify colours, sizes, fonts, animation.

**Acceptance Scenarios**:

1. **Given** AI is enabled and the user opens the AI window, **When** they look at the chrome, **Then** it matches the documented dark / light palette and layout.
2. **Given** ghost text is offered inline, **When** the suggestion renders, **Then** it uses the documented dimmed foreground and accept-on-Tab behaviour.
3. **Given** the user hovers an object, **When** the tooltip appears, **Then** the tooltip chrome matches the object-definition box style.
4. **Given** schema is loading, **When** the user looks at the editor margin, **Then** the spinner matches the documented arc-spinner pattern (12 × 12 ellipse, accent stroke, 1100 ms rotation).

---

### Edge Cases

- **Host theme switch at runtime.** Every chrome surface must re-theme without requiring the host to be restarted. Surfaces that hold cached brushes must invalidate on theme-change events.
- **High-DPI / per-monitor DPI.** All documented pixel sizes are reference values at 100 %; they must scale correctly at 125 %, 150 %, 200 % without truncation, clipping, or layout breakage.
- **Very narrow / very wide displays.** Modal dialogs respect their documented min sizes and remain usable at the min; tool windows handle narrow side-dock widths gracefully.
- **`.sqlpromptstyle` files from different SQL Prompt versions.** Import must succeed for v10.x and v11.x style files; unknown future keys are preserved on re-export, not dropped.
- **Style file with embedded paths or non-setting content.** Import must never execute or read filesystem paths from the JSON — settings only.
- **Visual surfaces shared by multiple shells (SSMS 20 / 21 / 22 + VS 2019 / 22 / 26).** Each host has its own theme service — surfaces derive from the same token bank but adapt to each host's actual theme colours where the host provides them.
- **Existing AKML-SQL users with custom theme overrides.** Existing user customisations must not be silently overwritten; if a value collides with the new token system, the user setting wins and a one-time migration notice is shown.
- **Colour-blind / accessibility users.** Severity, icon-type, and status colours (red / green / amber) must remain distinguishable without relying solely on hue — pair with shape or letter badges as the SQL Prompt design already does.

---

## Requirements *(mandatory)*

### Functional Requirements

#### Theme & token system

- **FR-001**: System MUST expose a single centralised theme / token bank from which every chrome surface (popup, dialog, tool window, margin, adornment, button, input, text style) reads its colours, brushes, and fonts.
- **FR-002**: The token bank MUST provide both Light-theme and Dark-theme variants for every token, with values mapped to the hex codes documented in `doc/SQL-PROMPT/SQL-Prompt-Option/SQL_Prompt_Options_Dialog.md §18.5`, `doc/SQL-PROMPT/SQL-Prompt-History/SQL_Prompt_SQL_History.md §16.2`, and `doc/SQL-PROMPT/SQL-Prompt-Features/SQL_Prompt_Features_Core.md §10`.
- **FR-003**: System MUST switch every visible chrome surface to the new theme within 1 second of a host theme-change event, without restart.
- **FR-004**: No chrome surface MAY contain a hardcoded colour value. Semantic colours (error red, warning amber, success green, info blue) are the only allowed hardcoded values, and they MUST remain identical across themes.

#### Surface coverage (visual parity)

- **FR-005**: System MUST apply the SQL Prompt visual specification to the IntelliSense suggestion popup (container, item rows, icon badges, scrollbar, transparency on Ctrl, dismiss keys).
- **FR-006**: System MUST apply the SQL Prompt visual specification to the object-definition side panel (Summary and Script tabs, font, layout).
- **FR-007**: System MUST apply the SQL Prompt visual specification to the column picker (modal chrome, checkbox rows, sort toggle, Select All, keyboard behaviour).
- **FR-008**: System MUST apply the SQL Prompt visual specification to the Options dialog (size, tree nav, content panel, button bar, page hierarchy, Restore Defaults link).
- **FR-009**: System MUST apply the SQL Prompt visual specification to the Format Styles editor (three vertical panels, settings tree categories, live preview).
- **FR-010**: System MUST apply the SQL Prompt visual specification to the SQL History window (three panels, search bar, filter button group, query / version / preview panes, status icons, star icon).
- **FR-011**: System MUST apply the SQL Prompt visual specification to Tab Coloring — swatch palette and tab chrome only. The existing Phase 5 implementation provides the per-connection / per-server / per-database assignment behaviour and persistence; that behaviour is not changed by this feature.
- **FR-011a**: An audit MUST be produced comparing Phase 5's Tab Coloring assignment rules to SQL Prompt's documented rules. The audit MUST live at `specs/020-sqlprompt-visual-parity/tab-coloring-audit.md` and enumerate each documented SQL Prompt rule with a status of `Matches` / `Differs` / `Missing`. Closing any `Differs` or `Missing` items is explicitly OUT OF SCOPE for this spec and becomes a follow-up.
- **FR-012**: System MUST apply the SQL Prompt visual specification to Code Analysis output (severity palette, squiggle colours, message list).
- **FR-013**: System MUST apply the SQL Prompt visual specification to the Prompt AI window, inline ghost text, hover tooltips, and editor-margin indicators (spinner, badges).
- **FR-014**: System MUST apply the SQL Prompt visual specification to the Snippet Manager and snippet-editing surfaces.
- **FR-015**: Every modal dialog MUST honour its documented preferred size and minimum size, and remain resizable where SQL Prompt is resizable.

#### Sizing & layout

- **FR-016**: All documented pixel sizes (item heights, icon sizes, panel widths, padding) MUST scale correctly across 100 %, 125 %, 150 %, and 200 % DPI without clipping or layout breakage.
- **FR-017**: Spacing, padding, and margins MUST be defined as named tokens (e.g., `Spacing.S`, `Spacing.M`) referenced by every surface, rather than ad-hoc literal values.
- **FR-018**: Typography (font family, sizes, weights) MUST be defined as named tokens and applied consistently — Segoe UI for chrome at the documented sizes; the host's editor font for code surfaces.

#### Format & Upload Formatter (functional gap closure)

- **FR-019**: System MUST provide an "Import…" action in the Format Styles editor that accepts `.sqlpromptstyle` JSON files.
- **FR-020**: System MUST parse every setting documented in `SQL_Prompt_Features_Core.md §2.3` (Whitespace, Lists, Parentheses, Casing, DML, DDL, JOINs, CASE, Operators, IN, CTE, Control Flow, Variables, Function calls, VALUES, Semicolons, Comments) and apply it to formatting output.
- **FR-021**: On successful import, the imported style MUST appear in the style list with the source file's `metadata.name`, and the user MUST be able to set it as the active style.
- **FR-022**: On import failure (malformed JSON or schema violation), the user MUST see a clear error message naming the failed section, and no partial style MUST be created.
- **FR-023**: When the imported file contains settings AKML-SQL does not yet support, each unsupported setting MUST appear in the editor's settings tree at its natural group location, with its control disabled, the imported value displayed, and a "not yet supported" badge adjacent to the control. The setting's value MUST be preserved in `PassthroughUnknownKeys` so it round-trips on export. The rest of the style MUST still apply normally.
- **FR-024**: System MUST provide an "Export…" action that writes the active style as a `.sqlpromptstyle`-schema JSON file, preserving every setting (including any pass-through "unknown" keys captured during a previous import) so the file can round-trip between AKML-SQL and SQL Prompt.
- **FR-025**: The Format Styles editor MUST include a live preview pane that re-formats the sample SQL within 250 ms whenever any setting changes.
- **FR-026**: `Ctrl+K, Y` MUST be bound to "Format SQL with active style" in every supported host (SSMS 20 / 21 / 22, VS 2019 / 22 / 26) to match the SQL Prompt shortcut.
- **FR-027**: AKML-SQL native format profiles and imported SQL Prompt styles MUST coexist — no automatic migration; user picks which is active.
- **FR-027a**: AKML-SQL MUST ship at least three read-only Native styles transcribed from SQL Prompt's documented defaults (Compact, Indented, AlignedLeftBracket). These styles MUST be flagged read-only; editing one MUST fork it to a writable Native copy. AKML-SQL MUST NOT redistribute Redgate-authored `.sqlpromptstyle` binary files inside the installer or update payloads.
- **FR-027b**: Exactly one style MUST be active at a time, **globally per user** (shared across SSMS 20 / 21 / 22 and VS 2019 / 22 / 26). The active selection MUST persist in `AppSettings.FormatterSettings.ActiveProfile` and MUST NOT be split by host or by server connection. Selecting a new active style in any host MUST take effect in every other host on next document open.
- **FR-028**: Formatting output for any imported style MUST match SQL Prompt's output for the same input for ≥ 95 % of a representative SQL corpus (whitespace-equivalent matches count).

#### Accessibility & state

- **FR-029**: Severity, object-type, and status colours MUST also be carried by a letter or icon glyph so the surface remains intelligible without colour.
- **FR-030**: A user's existing theme / colour customisations (if any) MUST NOT be silently overwritten; on first launch with the new token system, conflicting customisations MUST take precedence and a one-time notice MUST inform the user.

### Key Entities

- **Theme Token** — a named design value (colour hex, font family + size + weight, spacing scalar, brush, drop shadow) with one variant per supported theme (Light / Dark). All chrome surfaces resolve their visual properties by token name.
- **Surface** — any visible AKML-SQL UI element with its own bounds: a popup, dialog, tool window, margin, adornment, tooltip, message list, etc. Every surface declares which tokens it consumes.
- **Format Style** — a named bundle of formatting settings. Two flavours: a native AKML-SQL profile (existing) and a SQL Prompt-imported `.sqlpromptstyle`. Both expose the same setting matrix to the editor UI.
- **Format Setting** — a single configurable option (e.g., "Reserved keyword casing" enum, "Wrap column" number, "Place ON condition on new line" bool) with a defined type, value range, default, and mapping to both AKML-SQL and SQL Prompt schemas.
- **Style File** — a JSON document conforming to the `.sqlpromptstyle` schema documented in `SQL_Prompt_Features_Core.md §2.2`. Round-trips between AKML-SQL and SQL Prompt without setting loss.
- **Visual Reference** — the per-surface design contract drawn from `doc/SQL-PROMPT/` — colour table, dimensions, fonts, behaviour notes — that the surface MUST satisfy to be considered at parity.

---

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: 100 % of in-scope chrome surfaces (enumerated in FR-005 .. FR-014) read every colour and font from the central token bank — verified by an automated scan that flags any hardcoded hex outside the documented semantic palette.
- **SC-002**: Both Light and Dark themes are available, and switching the host theme re-themes every visible surface in under 1 second with no surface left mid-theme.
- **SC-003**: Side-by-side screenshot comparison of every documented surface (one per SVG in `doc/SQL-PROMPT/`) against AKML-SQL produces no chrome-colour, dimension, or layout deviation greater than 8 px or one tonal step from the documented value.
- **SC-004**: 5 evaluators familiar with SQL Prompt cannot reliably distinguish AKML-SQL screenshots from SQL Prompt screenshots better than 60 % of the time on the in-scope surfaces.
- **SC-005**: Every modal dialog opens at its documented preferred size, respects its documented minimum size, and remains usable at 100 %, 125 %, 150 %, and 200 % DPI.
- **SC-006**: A user can import any real-world `.sqlpromptstyle` file (v10.x or v11.x) and the import succeeds — either fully (all settings applied) or partially (unsupported settings clearly listed) — with zero hard failures across a test set of 20 representative files.
- **SC-007**: After importing a SQL Prompt style, AKML-SQL's formatter produces output that matches SQL Prompt's output for the same input for ≥ 95 % of a 200-file representative corpus. **Match definition**: strip trailing whitespace per line; normalise line endings to `\n`; drop UTF-8 BOM if present; then require byte-exact equality. Anything still different counts as a mismatch.
- **SC-008**: Round-trip test — export an AKML-SQL style as `.sqlpromptstyle`, import the result back into a fresh AKML-SQL profile, the resulting profile is setting-identical to the source 100 % of the time.
- **SC-009**: The Format Styles editor's live preview re-renders within 250 ms of any setting change for a 200-line SQL sample.
- **SC-010**: `Ctrl+K, Y` invokes "Format SQL with active style" in every supported host with no conflict with the host's native bindings — verified manually in SSMS 20 / 21 / 22 and VS 2019 / 22 / 26.
- **SC-011**: 0 user-reported regressions in existing theme customisations after the migration — verified by a one-time-notice flow and a beta cohort of at least 10 existing users.
- **SC-012**: Colour-blind simulation (deuteranopia, protanopia, tritanopia) of every status / severity / icon surface shows the user can still distinguish each category by shape or letter alone.

---

## Assumptions

- Both Light and Dark theme parity are in scope, since SQL Prompt's reference docs document both and AKML-SQL targets the same hosts.
- "Parity" means visually equivalent within the tolerances in SC-003, not pixel-perfect — exact pixel matching is infeasible under OS DPI scaling and varying host theme services.
- `.sqlpromptstyle` import targets the v10.x / v11.x JSON schema currently documented in the reference material; newer schema versions are pass-through preserved on re-export but may surface as "not yet supported".
- AKML-SQL keeps its own native format profile alongside imported SQL Prompt styles; users are never forced to migrate.
- Semantic colours (error red, warning amber, success green, info blue) are allowed to remain hardcoded so they are consistent across themes.
- Severity, status, and type colours are always paired with a letter or shape glyph so the surface is accessible without colour.
- Existing user customisations take precedence over new defaults on first launch.

---

## Out of Scope

- Functional behaviour of features not on the parity list above (e.g., the safety / execution-warning dialog logic itself is already implemented in earlier phases — only its chrome is in scope here).
- Net-new features SQL Prompt does not have.
- **Tab Coloring functional gap closure.** Phase 5 already provides the assignment-rule engine; this spec audits it against SQL Prompt (FR-011a) but does not change behaviour. Any `Differs` / `Missing` items from that audit become a follow-up spec, not this one.
- Migration of SQL Prompt **licensing**, **schema cache files**, or **AI conversation history** — only `.sqlpromptstyle` is in scope.
- Visual parity for the installer or updater UIs (install-time / one-shot surfaces with no SQL Prompt equivalent).
- Locale-specific typography or right-to-left support beyond what each host already provides.

---

## Dependencies

- `doc/SQL-PROMPT/` reference material remains the canonical visual contract; any deviation between reference and implementation is reconciled by updating the doc, not by silently drifting the implementation.
- Existing AKML-SQL theme infrastructure (the WPF surfaces in `CLAUDE.md` — `ThemeManager.Instance`, frozen brushes, hoisted font families) is the platform the token bank extends; this spec does not replace it.
- Existing Format profile schema (`AppSettings` formatting section) must accept a mapping from the SQL Prompt setting matrix so imported settings flow into the existing pipeline (`NoformatScanner` → … → `IdempotencyCheck`).
- Existing analysis rule severity palette (`doc/analysis-rules.md`) defines the semantic-colour anchor points.
