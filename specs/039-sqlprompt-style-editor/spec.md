# Feature Specification: SQL Prompt Style Editor (web + SSMS / Visual Studio)

**Feature Branch**: developed on `038-site-downloads-admin-portal` (not yet split out)
**Created**: 2026-09-23
**Status**: Implemented — calibration against SQL Prompt's built-in styles pending (see Open items)
**Input**: "please investigate into format style and compare with redgate sql prompt and copy as is (this feature is very important for me) and allow me to save it and check from web, and test it from web, and also when try it should verify that preview window is affected by changes"

## Overview

Before this feature, AKML formatted from its own 179-setting model. SQL Prompt styles could be
imported (spec 031), but the import mapped SQL Prompt's options into AKML's model and **42 of
SQL Prompt's 114 options changed nothing** in the output; SQL Prompt's own defaults produced
visibly broken layout (`nvarchar (100` + `);` on a new line, a dangling `)` after `TOP (10`,
`SUM (…` split mid-expression). The web edition had no style editor at all; the SSMS / Visual
Studio window edited AKML's model, not SQL Prompt's.

This feature makes SQL Prompt's model the style model:

- a style **is** a SQL Prompt style document (the same JSON SQL Prompt 10.5+ reads and writes);
- a new layout stage formats **directly from that document**, one decision per option;
- the web edition gets a Format styles page, and the SSMS / Visual Studio window switches to
  SQL Prompt's model — the same 4 categories, 14 pages, option names and values as SQL Prompt's
  "Edit formatting styles" window, with a live preview that changes when an option changes;
- styles are shared: the web edition saves through a paired engine into the styles folder
  SSMS and Visual Studio use, and keeps a browser copy when no engine is paired.

## Clarifications

### Session 2026-09-23

- **Q: What is the ground truth for SQL Prompt's layout?** → **A: Redgate's documentation**, not
  captured SQL Prompt output. Option semantics follow Redgate's schema descriptions, the option
  names, and Redgate's published articles; interpretations are recorded below so they can be
  checked against SQL Prompt and corrected in one place.
- **Q: SQL Prompt's built-in styles?** → **A: The user exports them** (Default, Collapsed, Commas
  before, Compact, Expanded, Indented, Right aligned) as `.json` and they ship unchanged. *Not yet
  received.*
- **Q: Where do web styles live?** → **A: Shared with SSMS / VS**: saved through the paired engine
  into the same styles folder; a browser copy when no engine is paired.
- **Q: Which editors?** → **A: Web and desktop**, one SQL Prompt-shaped model in both.

## User scenarios

1. **Edit a style and see it change.** On any page (Whitespace, Lists, … Operators) the user
   changes an option; the preview — that page's own sample, or their own SQL — reformats at once
   and marks the lines that moved. *Verified*: every option changes its page's preview or says in
   a note when it applies (`SqlPromptPreviewSampleTests`); a real-browser run changes the first
   option on all 14 pages and watches the preview change (`FormatStylesTests`).
2. **Save and use it.** Save keeps the style (engine or browser); a reload shows it unchanged; the
   editor's Format command uses it. *Verified* in a real browser.
3. **Bring a SQL Prompt style in, take it out.** Import a SQL Prompt `.json`; it formats from its
   own options; export writes a `.json` SQL Prompt imports as it is (same id, minimal form).
4. **Same style everywhere.** A style saved from the web on a paired engine is the style SSMS / VS
   format with, and the other way round.
5. **Classic styles.** A style written in AKML's model opens in SQL Prompt terms (its closest SQL
   Prompt reading, or the SQL Prompt file it was imported from); the editor says so; saving makes
   it a SQL Prompt style. Nothing changes for such styles until they are saved.

## Requirements

- **FR-001** The option catalog is SQL Prompt's: 114 schema options + the documented
  `whitespace.newLines.alignMultilineCommentsMatchingPatterns`; paths, types, allowed values and
  defaults equal Redgate's vendored schema (`SqlPromptOptionCatalogTests`).
- **FR-002** A style stores its SQL Prompt document in the `.akmlstyle` (`"sqlPrompt": {…}`),
  whole, including keys this build does not know. The AKML option groups beside it are a
  projection refreshed on every save, so older builds still format sensibly.
- **FR-003** A style with a document formats with the SQL Prompt layout; every other style keeps
  the rule-based layout, byte for byte (977 goldens unchanged).
- **FR-004** Formatting never changes meaning and is idempotent for every style
  (`SqlPromptLayoutSafetyTests`: parity corpus × six contrasting styles).
- **FR-005** Every option's every value changes the output in a scenario where it applies
  (`SqlPromptOptionSensitivityTests`); the only exemption is `casing.useObjectDefinitionCase`,
  which needs a database connection.
- **FR-006** SQL Prompt 11's serializer quirk: a collapse threshold written without its switch
  means the collapse is on; turning it off writes the switch explicitly (spec 031 FR-003).
- **FR-007** The engine advertises `styles.sqlprompt.v1`; the web lists engine styles
  (`engine:<name>`), saves new styles there, and falls back to the browser without one.
- **FR-008** Import of a SQL Prompt `.json` keeps its document and id; export to `.json` writes
  SQL Prompt's minimal form without a BOM.
- **FR-009** Editors grey out an option while the option that turns it on is off, and show a note
  for options whose effect depends on other settings or code.

## Design

- `AkmlSql.Formatting/SqlPrompt/`
  - `SqlPromptOptionCatalog` — pages, options, labels, choices, gates, notes; editor schema JSON.
  - `SqlPromptStyleDocument` — the JSON document: get/set by path, defaults, minimal/explicit
    forms, case-insensitive reads, the collapse rule, Redgate's `intentedFromWhen` typo.
  - `SqlPromptStyle` — typed, resolved view for the layout engine.
  - `SqlPromptStyles` — document ⇄ `.akmlstyle`; `SqlPromptProjection` — AKML model → SQL Prompt.
  - `SqlPromptPreviewSamples` — one preview sample per page.
  - `Layout/SqlWriter` — emits every token once, in order, deciding only whitespace (so meaning
    cannot change); keeps comments on their lines; tabs / tabs-where-possible; trial layouts with
    rollback; flat measurement for collapse and "if longer than" decisions.
  - `Layout/SqlPromptPrinter.*` — one printer per construct: clauses, lists, joins, CASE,
    parentheses (9 styles), DDL, control flow, variables, CTEs, INSERT/UPDATE/DELETE/MERGE.
- `FormatterPipeline.Layout` branches on `profile.SqlPrompt`; parsing, casing, formatting-off
  regions, semantic validation and the idempotency check are shared.
- IPC (additive): `ProfileGetResponse.SqlPromptJson` (7) / `IsSqlPromptStyle` (8);
  `ProfileInfo.IsSqlPromptStyle` (8); `StyleEditorSchemaRequest.SqlPromptModel` (2);
  `ProfileExportSqlPrompt` writes `.json` when the destination ends in `.json`.
- Web: `IProfileStore` (engine-aware), `Pages/Styles.razor`, picker "Edit…" link.
- Desktop: the existing Format Styles window renders the SQL Prompt schema; setting ids are
  `sqlPrompt.<path>`, so preview and save carry the document; the merge base is the engine's
  explicit reading of the style.

## Option interpretations to confirm against SQL Prompt

Redgate documents what each option is *for*, not the exact geometry. Where the name leaves room,
AKML reads the option literally, as below. Each is one small, isolated decision in
`SqlPromptPrinter.*` — easy to correct once SQL Prompt's built-in style exports (or screenshots)
show otherwise.

| Option | AKML reading |
|---|---|
| `lists.alignItemsAcrossClauses` | The first items of SELECT / FROM / WHERE / GROUP BY … start in one column (`SELECT   a` / `FROM     t` / `GROUP BY a`). |
| `dml.clauses.clauseAlignment` | left: keywords at the statement column; right: keywords right-aligned (river); to first list item: keywords under SELECT's first item. |
| `operators.andOr.alignment` | left-aligned: AND/OR at the owning keyword's column (WHERE, ON…); right-aligned: its right edge on the keyword's; before first list item: ends just before the first condition; to first list item: at the first condition; indented: keyword + one tab. |
| `joinStatements.join.keywordAlignment` | to FROM: JOIN at FROM's column; right-aligned to FROM: the JOIN phrase ends where FROM ends (needs room left of FROM); to table: at the first table; indented: FROM + one tab. |
| `joinStatements.join.indentJoinTable` | Applies when the joined table is on its own line: indented one tab from JOIN. |
| `joinStatements.on.*` | ON aligned to JOIN / right-aligned to JOIN or to INNER / to the table / indented; its condition to ON / INNER / table / indented. |
| `parentheses.parenthesisStyle` (and DDL / CTE / INSERT) | compact: content right after "("; expanded: "(" ends the line; split: "(" on its own line. ")" hugs the content (simple), goes to the statement column (to statement / split), one tab in (indented), or under "(" (right aligned). Short content that fits stays inline. |
| `…indentParenthesesContents` | expanded: content one tab in; compact: continuation lines one tab from "(". |
| `caseExpressions.placeExpressionOnNewLine` | The THEN / ELSE result expression on its own line (confirmed by Redgate's article on custom styles). |
| `caseExpressions.whenAlignment` = to first item | WHENs line up after `CASE ` (or with an inline first WHEN). |
| `caseExpressions.alignElseToWhen` = off | ELSE under THEN when THEN is on its own line, otherwise one tab past WHEN. |
| `caseExpressions.endAlignment` | to CASE / to WHEN / right-aligned to WHEN (END ends where WHEN ends). |
| `operators.in.alignment` | Wrapped IN values: left-aligned with the first value / right-aligned on their last character / indented from the expression. |
| `operators.between.*` | BETWEEN on its own line one tab in; its AND to BETWEEN / right-aligned to BETWEEN / to the start of the expression. |
| `variables.placeEqualsSignOnNewLine` | When an assigned value is too long for its line and moves down, "=" moves with it. |
| `ddl.placeConstraintsOnNewLines` | Column constraints other than NULL / NOT NULL and IDENTITY start their own line under the column. |
| `whitespace.newLines.preserveExisting…` | Keep more empty lines than configured when the author left them; never fewer. |
| `whitespace.spacesOrTabs` = tabs | Leading whitespace in tabs only (an alignment column between tab stops moves to the next one). |

## Verification

| Suite | What it proves |
|---|---|
| `SqlPromptOptionCatalogTests` | The catalog is Redgate's schema, page by page. |
| `SqlPromptStyleDocumentTests` | Document semantics: defaults, minimal form, unknown keys, case, collapse rule. |
| `SqlPromptOptionSensitivityTests` | Every option value changes the output. |
| `SqlPromptPreviewSampleTests` | Every option changes its own page's preview, or carries a note. |
| `SqlPromptLayoutSafetyTests` | Meaning kept, idempotent, comments kept, formatting-off kept — corpus × 6 styles. |
| `SqlPromptStylesTests` | Stored styles, projection of built-ins, Khamis Style ⇄ MohamedKhamis. |
| Engine `SqlPromptStyleHandlerTests` | Import, ProfileGet document, editor schema, preview, `.json` export, capability. |
| Web `SqlPromptStyleStoreTests` / `StylesPageTests` | Browser + engine storage; the page as a user drives it. |
| Web E2E `FormatStylesTests` (Chromium) | Preview reacts on all 14 pages; save survives reload; editor formats with it; import/export round trip. |
| Web E2E `FormatStylesSharedEngineTests` (Chromium + this tree's engine) | Paired with a real engine (web mode, loopback, `AKML_APP_DATA_ROOT` sandbox), a style saved on the web is written to the engine's styles folder as a SQL Prompt style, and a web edit updates that file and its AKML projection. |
| Shell `FormatStylesSqlPromptModelTests` | The SSMS / VS view model on SQL Prompt's model. |

## Open items

- **Calibrate against SQL Prompt's built-in styles** once the exports arrive: ship them as
  built-ins, format the parity corpus with each, and compare with SQL Prompt's own output of the
  same files (the interpretation table above lists every place a correction would land).
- The built-in Khamis Style is still an AKML-model style (its projection matches the user's
  MohamedKhamis SQL Prompt style); switching it to the SQL Prompt document is a one-line change
  once the SQL Prompt layout of that style is confirmed.
- Manual check of the SSMS / VS window (WPF): open Format Styles, change an option on each page,
  watch the preview, save, and format a query with the style.
