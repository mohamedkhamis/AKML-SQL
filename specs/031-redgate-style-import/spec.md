# Feature Specification: Redgate SQL Prompt JSON Style Import & Full-Fidelity Formatting

**Feature Branch**: `031-redgate-style-import`
**Created**: 2026-07-15
**Status**: Draft
**Input**: User description: "Read the Redgate SQL Prompt documentation on SQL code formatting and styles, make AKML format exactly like my custom SQL Prompt style (JSON provided — `MohamedKhamis-2cd71422-30f2-4360-800f-240f2897fd3e.json`), and allow me to upload it as a custom style format."

## Context

Modern SQL Prompt (10.5+) persists each formatting style as a single **JSON** file (`<name>-<guid>.json`, sections `whitespace` / `lists` / `parentheses` / `casing` / `dml` / `ddl` / `controlFlow` / `cte` / `variables` / `joinStatements` / `insertStatements` / `functionCalls` / `caseExpressions` / `operators`). AKML's spec-020 SQL Prompt round-trip targets an **XML** `.sqlpromptstylev2` shape of AKML's own devising — real Redgate `.sqlpromptstylev2` files (pre-10.5) were also JSON; only pre-v8 `.sqlpromptstyle` was XML — so a real Redgate style file cannot be imported today. Worse, feeding JSON through the existing `ProfileImport` IPC does not fail: the XML parse exception is swallowed and an **all-defaults profile is saved with `Success=true`** (`SqlPromptImporter.cs:340-343`, `FormatRequestHandler.cs:509-520`). There is also **no style-import button** anywhere in the desktop UI (the Options dialog's Import… handles whole-AppSettings JSON, not formatting styles) — only style Export was wired (the spec-003 T125 claim of an import button is stale).

A field-by-field audit of the target style (60+ options) against the formatting engine found three tiers:

- **29 of 65 already honored** — casing, all collapse thresholds, leading commas, ON-clause placement, CASE first-WHEN/THEN breaks, function-args/IN/BETWEEN inline modes, wrap width, tab size, DDL constraint breaks.
- **15 of 65 partially honored or modeled-but-dead** — a field exists but its semantics are incomplete for this style's value (e.g. `tabsIfPossible` imports but renders as spaces; CASE `toFirstItem` degrades to `toCase`; AND/OR `toFirstListItem` silently no-ops), or the field is never read by layout code at all (`Ddl.ConstraintColumnsOnNewLine`, INSERT values format).
- **19 of 65 unrepresentable** — and these define the style's visual identity: tab-stop alignment (`alignItemsToTabStops`), the leading-comma gutter with space-before-comma, Redgate's 9-value parenthesis styles (`expandedToStatement` / `expandedSimple`) with per-construct overrides, CTE name/column layout, space-before-semicolon (actively stripped today), newline after DISTINCT/TOP, function-call spacing, BETWEEN's AND right-alignment, and IN-list inner spacing. (The remaining 2 of 65 hold by construction; `useObjectDefinitionCase` — the schema-cache → CasingEngine bridge was never connected — counts among the partials.)

The canonical option reference is Redgate's own `formattingstyle-schema.json` + `full-style.json.example` (shipped with the SQL Prompt ADS extension; copies vendored into this spec's `reference/` folder). Verification ground truth is a golden corpus formatted by the user's live SQL Prompt 11 install (the ADS CLI formatter on this machine is expired and is not used). The existing parity harness (`tests/format-parity/`, byte-exact after trailing-whitespace/LF/BOM normalization, capture/compare modes) is reused unchanged.

## Clarifications

### Session 2026-07-15

- Q: How is "format exactly like SQL Prompt" verified? → A: **User-generated goldens** — the user formats a provided corpus in SSMS with SQL Prompt 11 using the target style; those outputs become golden tests (byte-exact after the harness's existing normalization).
- Q: Which surfaces get the upload/import UI? → A: **Desktop only** (SSMS 22 + VS 2026 Format Styles editor). Web edition is out of scope.
- Q: What happens after import? → A: The imported style appears in the styles list **and becomes the active style immediately**.
- Q: Delivery shape? → A: **Approach A — full fidelity, phased**: (1) import pipeline, (2) ground-truth corpus, (3) layout gap closure gated by the goldens. One spec, phased like spec 030.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Import a Redgate JSON style as a custom AKML style (Priority: P1)

A developer clicks **Import…** in the Format Styles editor, picks their SQL Prompt style file (modern JSON or legacy XML), and gets a named AKML style plus an honest per-option report: how many options mapped, which are recognized-but-unsupported (with reasons), and which are unknown. The imported style is selected and becomes the active formatting style immediately.

**Why this priority**: Without the importer nothing else is usable; it also fixes a live silent-corruption bug (JSON "imports" as an all-defaults profile claiming success). Standalone value even before fidelity work: the style imports at current engine fidelity.

**Independent Test**: Import `MohamedKhamis-….json`; confirm a "MohamedKhamis" style appears, is active, formats immediately, and the report lists every option in the file as mapped/unsupported/unknown with zero silent drops.

**Acceptance Scenarios**:

1. **Given** a modern SQL Prompt JSON style file, **When** the developer imports it via the Format Styles editor, **Then** a style named from `metadata.name` is created, selected in the list, and set active, and Format SQL immediately uses it.
2. **Given** the import completes, **When** the summary is shown, **Then** it reports counts and a per-option breakdown (mapped / mapped-pending-render / unsupported-with-reason / unknown), and the same statuses are visible on the style's options in the editor.
3. **Given** an XML `.sqlpromptstylev2` file previously exported by AKML's own Export, **When** it is imported, **Then** the existing XML mapping path runs unchanged (both formats auto-detected from content, not extension). A genuine Redgate pre-10.5 `.sqlpromptstylev2` (which is JSON) flows through the JSON importer, with keys outside the vendored schema reported as unknown.
4. **Given** a file that is neither valid JSON nor valid XML, **When** import is attempted, **Then** the operation fails with a clear parse error, **no profile is saved**, and the response reports failure (regression test for the current swallow-and-succeed bug).
5. **Given** a style whose name collides with an existing custom style, **When** the developer imports it, **Then** they are asked to overwrite or rename before anything is written.
6. **Given** options omitted from the file, **When** the style is imported, **Then** Redgate's documented schema defaults are applied (e.g. `wrapLongLines=true`, `emptyLinesAfterBatchSeparator=1`, `addSpaceAfterComma=true`), not AKML defaults.
7. **Given** a collapse threshold present without its gating boolean (SQL Prompt 11 serializer quirk), **When** imported, **Then** the collapse behavior is treated as **enabled** with that threshold.
8. **Given** the original JSON file, **When** import succeeds, **Then** the verbatim source is preserved alongside the profile for lossless future re-import.

---

### User Story 2 - Format SQL renders the imported style as-is (Priority: P1)

With the imported style active, the developer formats any script and the output matches what SQL Prompt 11 produces with the same style — same tabs/alignment, comma gutter, parenthesis expansion, CTE/CASE/INSERT/JOIN layout, spacing, and blank-line policy.

**Why this priority**: This is the user's core request ("do like it as is"). The import alone (US1) yields ~55% visual fidelity; this story closes the rest.

**Independent Test**: Format the parity corpus with the imported style; compare byte-exact (post-normalization) against the SQL Prompt 11 goldens; ≥95% of files match, with a written deviation note for any miss.

**Acceptance Scenarios**:

1. **Given** the target style is active, **When** the corpus is formatted, **Then** ≥95% of corpus files are byte-identical to the SQL Prompt 11 goldens after the harness's normalization, and every option row in the *Option Fidelity Contract* below renders per its Required Outcome.
2. **Given** `spacesOrTabs=tabsIfPossible` + `alignItemsToTabStops=true` + `numberOfSpacesInTabs=2`, **When** any aligned construct is emitted, **Then** leading whitespace uses tab characters up to the last whole tab stop with space padding beyond, and alignment columns round up to tab stops.
3. **Given** leading commas with `addSpaceBeforeComma=true` and `commaAlignment=toList`, **When** a multi-line list is formatted, **Then** commas sit in a gutter left of the list column with item text flush to the first item.
4. **Given** `whiteSpaceBeforeSemiColon=spaceBefore`, **When** any statement is terminated, **Then** exactly one space precedes the semicolon (the current unconditional space-stripping normalizer is gated off).
5. **Given** `emptyLinesBetweenStatements=2`, **When** consecutive statements are emitted, **Then** exactly two blank lines separate them (and exactly one follows GO, per the omitted-key default).
6. **Given** a construct whose option is documented as a known deviation, **When** the corpus runs, **Then** the deviation is listed in the spec's deviations table with a reason — no undocumented mismatches.

---

### User Story 3 - Trustworthy coverage reporting for any style file (Priority: P2)

A developer importing **any** SQL Prompt style (not just this one) can see exactly which of its options AKML honors. Unsupported options are visible in the Format Styles editor as recognized-but-unsupported rather than silently missing, and unknown keys (from newer SQL Prompt versions) are reported.

**Why this priority**: Honesty is what makes the feature safe to ship broadly; it converts every future "why doesn't X apply?" support question into a visible report line. Builds on machinery from US1.

**Independent Test**: Import Redgate's `full-style.json.example` (every option, every value); confirm 100% of keys resolve to mapped or explicitly-unsupported — zero unknown for the vendored schema version — and statuses surface in the editor.

**Acceptance Scenarios**:

1. **Given** `full-style.json.example`, **When** it is imported, **Then** every key in the file is classified (mapped / mapped-pending-render / unsupported / unknown) and the counts reconcile with the file's key count.
2. **Given** an option value outside the vendored schema's enum (newer SQL Prompt), **When** imported, **Then** the option is reported unknown, the schema default is applied, and import still succeeds.
3. **Given** an imported style open in the Format Styles editor, **When** the developer inspects an unsupported option, **Then** it is shown with its imported value and an unsupported badge (spec-020 FR-023 surface reused).

---

### Edge Cases

- File > 1 MB, non-UTF-8, or with a UTF-8 BOM → rejected (size) / decoded correctly (BOM) shell-side before IPC.
- Empty JSON object `{}` → imports as an all-Redgate-defaults style; report shows all keys defaulted.
- `metadata.name` empty, missing (including the `{}` case above), or containing filesystem-hostile characters → falls back to the source file stem; sanitized by the existing `ProfileManager` filename rules.
- Import while the engine is disconnected → existing "Engine not connected" status path; nothing written.
- Repeated import of the same file → overwrite prompt; on confirm, same profile file is replaced (no duplicate list entries).
- `--` / `/* */` comments inside formatted constructs, `noformat` regions, and SQLCMD blocks must survive the new layout passes unchanged (existing pipeline invariants).
- Tabs rendering vs. the idempotency check (stage 7) — formatting twice with `tabsIfPossible` must be a fixed point.
- MERGE statements (spec-030 residual area) formatted under the new style must not regress the ed007f9 MERGE WHEN layout fix — corpus includes MERGE.
- Semicolon spacing must compose with the Insert Semicolons format action (inserted semicolons also get the space).
- Stage-6 semantic validation failure on any corpus file still returns original SQL unchanged (never a half-formatted result).

## Requirements *(mandatory)*

### Functional Requirements

**Import pipeline**

- **FR-001**: The system MUST import modern SQL Prompt JSON style files, mapping every key in Redgate's `formattingstyle-schema.json` (vendored at `specs/031-redgate-style-import/reference/`) **plus documented post-schema additions** (currently one: `whitespace.newLines.alignMultilineCommentsMatchingPatterns`, added in SQL Prompt 10.14 and absent from the vendored schema) to an AKML profile setting or an explicit unsupported/unknown classification. Enum values MUST be matched case-insensitively (files serialize lowerCamelCase; the example doc uses UpperCamelCase).
- **FR-002**: Omitted keys MUST take Redgate's schema defaults, not AKML defaults.
- **FR-003**: A collapse threshold present without its gating boolean MUST enable that collapse (SP11 serializer quirk), subject to golden verification.
- **FR-004**: The import entry point MUST auto-detect JSON vs XML from content (not extension); the XML path (which serves AKML's own spec-020 exports — real Redgate v2 files are JSON) MUST keep working unchanged.
- **FR-005**: A file that parses as neither MUST fail the import: `Success=false`, parse diagnostics returned, no profile saved. (Fixes the swallow-and-succeed bug; regression test required.)
- **FR-006**: `metadata.name`/`metadata.id` MUST populate the AKML profile metadata; the verbatim source JSON MUST be preserved alongside the saved profile for lossless re-import.
- **FR-007**: The import response MUST carry per-option classifications (name, imported value, status, reason) — not just counts. Statuses: **mapped** (stored in a profile field the formatter honors end-to-end), **mapped-pending-render** (recognized and stored; rendering ships in a later phase of this spec — the classification is computed from the engine's honoring table, so the same file re-imported after Phase 3 reports it as mapped), **unsupported** (recognized, deliberately not modeled, with reason), **unknown** (key absent from the vendored schema + documented additions).
- **FR-008**: Name collisions with existing custom styles MUST prompt overwrite-or-rename before writing; collision with a **built-in** style name (e.g. "Default") MUST force a rename (built-ins cannot be shadowed by import).

**Desktop UI**

- **FR-010**: The Format Styles editor MUST gain an **Import…** toolbar action (next to Export) with file filter `SQL Prompt style (*.json;*.sqlpromptstylev2)`, a 1 MB size cap, and shell-side UTF-8 read (mirroring the snippet-import pattern).
- **FR-011**: After a successful import the new style MUST be selected in the editor list **and set as the active formatting style** (same code path as Set Active / `AppSettings.Formatter.ActiveProfile`).
- **FR-012**: An import summary MUST be shown (mapped / mapped-pending-render / unsupported / unknown counts with an expandable per-option list) and unsupported and pending-render options MUST appear with badges on the style's settings (reusing the spec-020 FR-023 surface).

**Formatting fidelity** — the engine MUST honor every option per the *Option Fidelity Contract* table below; grouped requirements:

- **FR-020 (Tabs & alignment)**: `tabsIfPossible` emission (tabs to the last whole tab stop, space padding beyond), plain `tabs`, and `spaces`; alignment features MUST work in all three modes; `alignItemsToTabStops` rounds alignment columns up to the next tab stop.
- **FR-021 (Lists & commas)**: space-before-comma; leading-comma gutter alignment (`beforeItem` / `toList` / `toStatement`).
- **FR-022 (Parentheses)**: Redgate's 9-value parenthesis style enum interpreted globally and per construct (DDL, CTE, INSERT columns, INSERT values), with `indentParenthesesContents`, collapse thresholds, and inner-space options composing with it.
- **FR-023 (DML)**: newline after DISTINCT/TOP (select list starts on the next line; DISTINCT/TOP stays on the SELECT line).
- **FR-024 (DDL)**: constraint-columns placement honoring `ifLongerOrMultipleColumns` (wire the existing dead field).
- **FR-025 (Control flow)**: `indentBeginAndEndKeywords` — indent the BEGIN/END keywords themselves one level from the controlling statement, with block contents one further level (per the omitted-key default `indentContentsOfStatements=true`).
- **FR-026 (CTE)**: name on new line, indented name, column-list alignment (`indented` / `leftAligned` / `rightAligned`), AS-inline, body indent, CTE-scoped parenthesis style.
- **FR-027 (Variables)**: `placeEqualsSignOnNewLine` for DECLARE/SET continuation lines; no data-type/value column alignment when `alignDataTypesAndValues=false`.
- **FR-028 (JOIN)**: `keywordAlignment` full enum (`toFrom` / `rightAlignedToFrom` / `toTable` / `indented`) — the target style uses `toTable` (JOIN starts at the FROM table-name column); ON inline with `indented` fallback alignment on wrap.
- **FR-029 (INSERT)**: per-construct column/values parenthesis styles (`expandedSimple` here), asymmetric `indentContents` (columns no / values yes), `placeSubsequentValuesOnNewLines=always` (one value per line per tuple).
- **FR-030 (Function calls)**: spacing trio — space between name and `(`, spaces inside the argument list, space between empty parens (`GETDATE ( )`); call-site detection MUST be fixed to recognize calls with these spaces present; `placeArgumentsOnNewLines=never` keeps arg lists inline regardless of wrap.
- **FR-031 (CASE)**: `whenAlignment=toFirstItem` as true column alignment (measured, not indent-approximated); `thenAlignment=toWhen` for line-start THENs; `placeEndOnNewLine=false` actively keeps END inline after the last clause; ELSE on its own line aligned to WHEN (omitted-key defaults).
- **FR-032 (Operators)**: AND/OR `alignment=toFirstListItem` (operator's left edge at the first condition's column; `beforeFirstListItem` also implemented for enum completeness); BETWEEN's `andAlignment=rightAlignedToBetween` on wrapped BETWEEN; IN-list inner spacing (`addSpaceAroundInContents`).
- **FR-033 (Semicolons)**: `whiteSpaceBeforeSemiColon` (`none` / `spaceBefore` / `newLineBefore`); the unconditional `NormalizeSemicolonSpacing` pass becomes option-gated.
- **FR-034 (Blank lines)**: `emptyLinesBetweenStatements` honored as a count (N blank lines), `emptyLinesAfterBatchSeparator` honored after GO.
- **FR-035 (Casing sync)**: `useObjectDefinitionCase` — identifier casing rewritten to catalog definition case via the schema cache when a live connection exists; graceful as-typed no-op otherwise.
- **FR-036 (Comments)**: `alignMultilineCommentsMatchingPatterns` maps to the existing block-comment re-indent + pattern recognition; fidelity verified (and if needed tuned) against goldens — Redgate's exact pattern set is undocumented.

**Verification infrastructure**

- **FR-040**: The parity corpus MUST be extended with ~20 files, each targeting an option family of the target style (tabs/commas, parens, CTE, INSERT, CASE, operators, DDL, control flow, semicolons/blank lines, casing, MERGE interplay); goldens generated by the user's SQL Prompt 11 via a provided runbook and stored as `tests/format-parity/golden/<stem>__mohamedkhamis.sql`.
- **FR-041**: The existing `FormatParityTests` driver MUST run the imported style against those goldens using the existing normalization (strip trailing whitespace, LF, drop BOM, then byte-exact); each Phase-3 layout feature merges only when its corpus files pass.
- **FR-042**: Existing AKML self-goldens (78 pairs) MUST be re-blessed per feature with reviewed diffs — never wholesale; tab-emission math MUST additionally be covered by an invariant property test (emitted text re-measures to the intended column), not goldens alone.

### Option Fidelity Contract *(the target style, option by option)*

Status legend — **wired**: honored today; **partial**: field exists, semantics incomplete for this value; **dead**: field exists, layout never reads it; **missing**: no representation; **n/a**: the user's value coincides with the layout engine's unconditional behavior, so no wiring is needed (the underlying preserve-fields are dead code, which is irrelevant at these values). "Required outcome" is the testable behavior with this style active.

| # | Option (JSON path) | Value | Today | Required outcome |
|---|---|---|---|---|
| 1 | whitespace.spacesOrTabs | tabsIfPossible | partial | Tabs to last whole tab stop, space padding beyond (FR-020) |
| 2 | whitespace.numberOfSpacesInTabs | 2 | wired | Indent unit / tab stop = 2 |
| 3 | whitespace.wrapLinesLongerThan | 200 | wired | Hard wrap >200 cols (wrapLongLines default true) |
| 4 | whitespace.whiteSpaceBeforeSemiColon | spaceBefore | missing* | One space before `;` (*actively stripped today; FR-033) |
| 5 | whitespace.newLines.preserveExistingEmptyLinesBetweenStatements | false | n/a | Holds by construction (layout rebuilds blank lines) |
| 6 | whitespace.newLines.preserveExistingEmptyLinesAfterBatchSeparator | false | n/a | Holds by construction; normalized to count (FR-034) |
| 7 | whitespace.newLines.alignMultilineCommentsMatchingPatterns | true | partial | Block comments re-indented as a unit (FR-036) |
| 8 | whitespace.newLines.emptyLinesBetweenStatements | 2 | partial | Exactly 2 blank lines between statements (FR-034) |
| 9 | lists.alignItemsToTabStops | true | missing | Alignment columns round up to tab stops (FR-020) |
| 10 | lists.placeCommasBeforeItems | true | wired | Leading commas |
| 11 | lists.addSpaceBeforeComma | true | missing | `a , b` inline; feeds gutter width (FR-021) |
| 12 | lists.commaAlignment | toList | missing | Comma gutter left of list column, items flush (FR-021) |
| 13 | parentheses.parenthesisStyle | expandedToStatement | partial | Parens on own lines at statement column (FR-022) |
| 14 | parentheses.indentParenthesesContents | true | wired | +1 indent inside expanded parens |
| 15 | parentheses.collapseShortParenthesisContents | true | wired | Collapse short paren contents |
| 16 | parentheses.collapseParenthesesShorterThan | 100 | wired | Threshold 100 |
| 17 | parentheses.addSpacesInsideParentheses | true | wired | `( x )` on inline parens |
| 18 | casing.reservedKeywords | uppercase | wired | SELECT, FROM, … |
| 19 | casing.builtInFunctions | uppercase | wired | GETDATE, SUM, … |
| 20 | casing.builtInDataTypes | uppercase | wired | INT, NVARCHAR, … |
| 21 | casing.useObjectDefinitionCase | true | partial | Catalog-case identifiers when connected (FR-035) |
| 22 | dml.addNewLineAfterDistinctAndTopClauses | true | missing | Select list on new line after DISTINCT/TOP (FR-023) |
| 23 | dml.collapseStatementsShorterThan | 160 | wired | Collapse short DML (threshold-present ⇒ enabled) |
| 24 | dml.collapseSubqueriesShorterThan | 78 | wired | Collapse short subqueries (⇒ enabled) |
| 25 | ddl.parenthesisStyle | expandedToStatement | partial | DDL-scoped paren style (FR-022) |
| 26 | ddl.indentParenthesesContents | true | dead | Column defs indented (+ aligned types, omitted-key default) |
| 27 | ddl.placeConstraintsOnNewLines | true | wired | Constraints on own lines |
| 28 | ddl.placeConstraintColumnsOnNewLines | ifLongerOrMultipleColumns | dead | Composite/long keys expand; single-column stay inline (FR-024) |
| 29 | ddl.collapseShortStatements | true | wired | Collapse short DDL |
| 30 | ddl.collapseStatementsShorterThan | 75 | wired | Threshold 75 |
| 31 | controlFlow.indentBeginAndEndKeywords | true | missing** | BEGIN/END keywords indented from IF; contents further (**today only contents indent; FR-025) |
| 32 | controlFlow.collapseStatementsShorterThan | 35 | wired | Collapse short control flow (⇒ enabled) |
| 33 | cte.parenthesisStyle | expandedToStatement | partial | CTE-scoped paren style (FR-022) |
| 34 | cte.indentContents | true | wired | CTE body +1 indent |
| 35 | cte.placeNameOnNewLine | true | missing | CTE name on line after WITH (FR-026) |
| 36 | cte.indentName | true | missing | Name indented from WITH (FR-026) |
| 37 | cte.columnAlignment | rightAligned | missing | Column list right-aligned to name gutter (FR-026) |
| 38 | cte.placeAsOnNewLine | false | wired | AS stays on name line |
| 39 | variables.alignDataTypesAndValues | false | wired | No column alignment in DECLARE |
| 40 | variables.placeEqualsSignOnNewLine | true | missing | `=` leads continuation line (FR-027) |
| 41 | joinStatements.join.keywordAlignment | toTable | partial | JOIN starts at FROM-table column (FR-028) |
| 42 | joinStatements.join.indentJoinTable | false | wired | No extra table indent |
| 43 | joinStatements.on.placeOnNewLine | false | wired | ON inline with table |
| 44 | joinStatements.on.keywordAlignment | indented | wired | Applies when wrap forces ON to own line |
| 45 | insertStatements.columns.parenthesisStyle | expandedSimple | missing | Parens on own lines at natural indent (FR-029) |
| 46 | insertStatements.columns.indentContents | false | missing | Columns flush with parens (FR-029) |
| 47 | insertStatements.values.parenthesisStyle | expandedSimple | missing | Per-tuple parens on own lines (FR-029) |
| 48 | insertStatements.values.indentContents | true | missing | Values +1 indent inside tuple (FR-029) |
| 49 | insertStatements.values.placeSubsequentValuesOnNewLines | always | dead | One value per line per tuple (FR-029) |
| 50 | functionCalls.placeArgumentsOnNewLines | never | wired | Arg lists always inline |
| 51 | functionCalls.addSpacesAroundParentheses | true | missing | `SUM (…)` (FR-030) |
| 52 | functionCalls.addSpacesAroundArgumentList | true | missing | `SUM ( Amount )` (FR-030) |
| 53 | functionCalls.addSpaceBetweenEmptyParentheses | true | missing | `GETDATE ( )` (FR-030) |
| 54 | caseExpressions.placeFirstWhenOnNewLine | never | wired | `CASE WHEN …` first WHEN inline |
| 55 | caseExpressions.whenAlignment | toFirstItem | partial | Subsequent WHENs column-aligned under first WHEN (FR-031) |
| 56 | caseExpressions.placeThenOnNewLine | true | wired | THEN on own line |
| 57 | caseExpressions.thenAlignment | toWhen | partial | Line-start THEN at WHEN's column (FR-031) |
| 58 | caseExpressions.placeEndOnNewLine | false | partial | END kept inline after last clause (FR-031) |
| 59 | caseExpressions.collapseShortCaseExpressions | true | wired | Collapse short CASE |
| 60 | caseExpressions.collapseCaseExpressionsShorterThan | 110 | wired | Threshold 110 |
| 61 | operators.andOr.alignment | toFirstListItem | partial | AND/OR left edge at first condition's column (FR-032) |
| 62 | operators.between.placeOnNewLine | false | wired | BETWEEN inline |
| 63 | operators.between.andAlignment | rightAlignedToBetween | missing | Wrapped AND right-aligned to BETWEEN (FR-032) |
| 64 | operators.in.placeFirstValueOnNewLine | never | wired | IN lists inline |
| 65 | operators.in.addSpaceAroundInContents | true | missing | `IN ( 1, 2 )` (FR-032) |

### Key Entities

- **Redgate JSON style importer** — declarative JSON-path → profile-setting mapping table built from the vendored Redgate schema; produces a profile + per-option classification report.
- **Import report** — per-option `{path, value, status: mapped|unsupported|unknown, reason}`; carried in the existing `ProfileImportResponse` (extended), rendered in the summary dialog and as editor badges.
- **FormattingProfile additions** — new/extended fields per FR-020…FR-036 (semicolon placement, comma gutter, tab-stop alignment, 9-value paren styles with per-construct overrides, INSERT section, CTE name/column layout, control-flow keyword indent, `=`-on-new-line, JOIN/CASE/operator alignment enums, spacing options, blank-line counts); all surfaced through the Format Styles editor schema.
- **Parity corpus & goldens** — ~20 new corpus files + SQL Prompt 11 outputs; the fidelity contract's executable form.
- **Vendored Redgate references** — `formattingstyle-schema.json`, `full-style.json.example`, and the user's style file as test fixtures.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Importing the target style yields zero silently-dropped options: all 65 rows above are classified, with **zero unsupported and zero unknown** for the target file at every phase (rows awaiting Phase-3 rendering report mapped-pending-render), and importing malformed content fails visibly (no saved profile). Verified by unit tests including the swallow-and-succeed regression.
- **SC-002**: After import, the style is active without further clicks: the next Format SQL request carries its name (observable in the request) and output changes accordingly.
- **SC-003**: ≥95% of corpus files byte-match SQL Prompt 11 goldens after normalization (spec-020's SC-007 parity bar), with **100% of the 65 contract rows** rendering per Required Outcome or listed in a documented-deviations table with reasons. Zero undocumented mismatches.
- **SC-004**: Importing `full-style.json.example` classifies 100% of its keys as mapped, mapped-pending-render, or unsupported (zero unknown at the vendored schema version).
- **SC-005**: No perf regression: Format SQL < 200 ms on the perf corpus (SC-011 absolute targets); completion latency budgets untouched.
- **SC-006**: Formatting is idempotent under the new style (format∘format = format on the full corpus, stage-7 check green).
- **SC-007**: Existing 78 self-golden pairs pass after each feature lands (with per-feature reviewed re-blesses only).

### Documented Deviations

Populated during Phase 3 as corpus results come in; the target end-state is an **empty table**. Any corpus mismatch not resolved by implementation MUST be recorded here (option, corpus file, SQL Prompt 11 rendering, AKML rendering, reason) — SC-003 forbids undocumented mismatches.

| Option | Corpus file | SQL Prompt 11 | AKML | Reason |
|---|---|---|---|---|
| *(none yet)* | | | | |

## Assumptions

- The user's SQL Prompt 11 output is the authority wherever Redgate documentation is ambiguous (collapse-bool omission quirk, comma-gutter rendering, `expandedSimple` vs `expandedToStatement` nesting behavior, comment pattern set).
- The vendored `formattingstyle-schema.json` (ADS extension 0.2.11) is the canonical option list; keys newer than it are handled via the `unknown` classification, not guessed.
- The expired ADS CLI formatter is not used for golden generation (license expiry is respected); goldens come from the user's licensed SSMS SQL Prompt 11.
- Byte-exactness is measured after the harness's existing normalization (trailing whitespace, LF, BOM) — consistent with spec-020 SC-007.

## Dependencies

- **Phase 2 gate**: user-generated goldens (SSMS + SQL Prompt 11 + the target style, runbook provided). Phase 3 features merge only against their goldens.
- `useObjectDefinitionCase` depends on the existing engine schema cache and the schema-bridge overload in `FormatRequestHandler` (as used by schema-aware format actions); degrades gracefully with no connection.
- Editor badge surface from spec-020 FR-023; `ProfileImportResponse` IPC message (extended, engine + shell rebuilt together per the full-publish deployment rule).

## Out of Scope

- Web-edition style upload/import UI (desktop-only per clarification; the importer lives in `AkmlSql.Formatting`, so web reuse is possible later without engine work).
- Exporting AKML profiles **to** Redgate JSON (existing XML export unchanged; candidate follow-up).
- Pre-v8 `.sqlpromptstyle` XML migration; shared network style folders; Redgate Platform cloud style sharing.
- Any Redgate option absent from the vendored schema (beyond `unknown` reporting).
- Right-aligned "river" layout options **not** used by the target style beyond what FR-020…FR-032 already require for enum completeness.
