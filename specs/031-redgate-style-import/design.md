# Design: Redgate SQL Prompt JSON Style Import & Full-Fidelity Formatting

**Approved**: 2026-07-15 (conversation review; Approach A of three presented)
**Companion to**: [spec.md](spec.md) — this file records the approved *how*; the spec records the *what*. The plan phase (`/speckit.plan` or writing-plans) should consume both.

## Approach decision

Three approaches were considered:

- **A. Full fidelity, phased** *(chosen)* — importer first, ground-truth goldens second, layout gap closure third, each gated by the previous.
- **B. Importer only** — rejected: leaves the style's defining visuals (tab-stop alignment, comma gutter, paren expansion) unrendered; fails "as is".
- **C. Hardcoded style preset** — rejected: not a general upload feature; brittle against style-file edits.

## 1. Importer

New `RedgateJsonStyleImporter` in `src/AkmlSql.Formatting/Profiles/`:

- **Declarative mapping table** keyed by JSON path (e.g. `lists.placeCommasBeforeItems` → `profile.List.CommaPosition = "leading"`), one entry per key of the vendored `formattingstyle-schema.json` **plus documented post-schema additions** (currently `whitespace.newLines.alignMultilineCommentsMatchingPatterns`, SP 10.14 — verified absent from the vendored schema but present in the target style). Each entry declares: target field + value transform, or `Unsupported(reason)`.
- **Schema defaults for omitted keys** (Redgate's, not AKML's): materialize the full option set from the schema defaults first, then overlay the file. Notable defaults that matter for the target style: `wrapLongLines=true`, `emptyLinesAfterBatchSeparator=1`, `addSpaceAfterComma=true`, `join.placeOnNewLine=true`, `controlFlow.placeBeginAndEndOnNewLine=true`, `controlFlow.indentContentsOfStatements=true`, `caseExpressions.placeElseOnNewLine=true`, `alignElseToWhen=true`, `ddl.alignDataTypesAndConstraints=true`, `insertStatements.columns.placeSubsequentColumnsOnNewLines=always`.
- **Threshold-implies-enabled**: `dml.collapseStatementsShorterThan` / `dml.collapseSubqueriesShorterThan` / `controlFlow.collapseStatementsShorterThan` present without their gating booleans (SP11 serializer drops them) ⇒ enable the collapse. Verify against goldens in Phase 3.
- **Case-insensitive enum matching** (`tabsIfPossible` == `TabsIfPossible`).
- **Classification report**: every key → `mapped` | `unsupported(reason)` | `unknown` (not in vendored schema). Returned to the caller; nothing silent.
- Existing `SqlPromptImporter` (XML — AKML's own spec-020 export shape; real Redgate v2 files are JSON) untouched. `FormatRequestHandler.HandleProfileImport` gains content sniffing **scoped to the `sqlprompt` SourceFormat branch** (first non-whitespace char `{` → Redgate JSON importer, `<` → XML importer) so both flow through IPC `ProfileImport (17)`; the `akmlstyle` branch is unaffected (it also receives JSON, so sniffing must not be global). A Redgate JSON mis-sent as `SourceFormat=akmlstyle` still deserializes to a near-default profile (System.Text.Json ignores unknown members) — accepted, since the new UI always sends `sqlprompt` for both filter extensions; noted for a possible future strictness pass. **Bug fix**: parse failure → `Success=false`, no `ProfileManager.Save`, diagnostics in response (today: `XmlException` swallowed at `SqlPromptImporter.cs:340-343`, saved as all-defaults with `Success=true`).
- Source preservation: original JSON stored verbatim as `<name>.source.json` beside the `.akmlstyle` in the profiles folder.

## 2. Profile schema additions

New/changed `FormattingProfile` fields (all round-tripped through `FormatSettingSchema` so the Format Styles editor shows them):

| Section | Field | Type / values | Notes |
|---|---|---|---|
| Whitespace | `SemicolonPlacement` | `none` \| `spaceBefore` \| `newLineBefore` | Gates the currently-unconditional `NormalizeSemicolonSpacing` (`FormatterPipeline.cs:67-75`) |
| Whitespace | `EmptyLinesAfterBatchSeparator` | int (default 1) | After GO |
| Whitespace | `EmptyLineBetweenStatements` | int — **honored as a count** | Break model change, see §3.1 |
| List | `SpaceBeforeComma` | bool | Inline `a , b` |
| List | `CommaAlignment` | `beforeItem` \| `toList` \| `toStatement` | Leading-comma gutter |
| List | `AlignItemsToTabStops` | bool | Alignment columns round up to tab stops |
| Parenthesis | `Style` | 9-value Redgate enum | Legacy `OpenOnSameLine`/`CloseOnNewLine` remain for old profiles; enum wins when set |
| Ddl / Cte | `ParenthesisStyle` | same enum | Per-construct overrides |
| Dml | `NewLineAfterDistinctTop` | bool | Distinct from existing `TopOnSameLine`/`DistinctOnSameLine` (those break *before*) |
| InsertStatements *(new section)* | `Columns.ParenthesisStyle`, `Columns.IndentContents`, `Columns.PlaceSubsequentColumnsOnNewLines`, `Values.ParenthesisStyle`, `Values.IndentContents`, `Values.PlaceSubsequentValuesOnNewLines` | mirrors Redgate | Deprecates dead `Dml.InsertColumnListFormat` / `Dml.ValuesFormat` |
| ControlFlow | `IndentBeginEndKeywords` | bool | Keywords indent from IF; contents one further level |
| Cte | `PlaceNameOnNewLine`, `IndentName` | bool | |
| Cte | `ColumnAlignment` | `indented` \| `leftAligned` \| `rightAligned` | |
| Declare | `EqualsOnNewLine` | bool | DECLARE/SET continuation lines |
| Join | `AlignJoinKeyword` | + `toTable` (full enum `toFrom`/`rightAlignedToFrom`/`toTable`/`indented`) | `JoinRules.cs:116-160` currently knows only none/right |
| FunctionCalls | `SpaceAroundParentheses`, `SpaceAroundArgumentList`, `SpaceBetweenEmptyParentheses` | bool ×3 | Call detection at `ControlFlowRules.cs:1412` requires zero spaces — must be relaxed with the feature |
| Case | `ThenAlignment` | `indentedFromWhen` \| `toWhen` \| `toWhenExpression` | For line-start THENs (existing `AlignThen` pads inline THENs only) |
| Case | `EndOnNewLine=false` semantics | — | Becomes an active "keep END inline" instruction |
| Operators | `Alignment` | + `toFirstListItem`, `beforeFirstListItem` | Unrecognized values currently silently no-op |
| Operators | `BetweenAndAlignment` | `toBetween` \| `rightAlignedToBetween` \| `toBeginningOfExpression` | Applies on wrapped BETWEEN |
| InStatements | `SpaceAroundContents` | bool | |

Dead fields to **wire, not duplicate**: `Ddl.ConstraintColumnsOnNewLine` (enum exists since spec 020), `Whitespace.TabStyle="tabsWhenPossible"` (imported but rendered as spaces).

**Phase assignment**: all §2 profile fields (storage + `FormatSettingSchema` exposure + importer mapping) land in **Phase 1**, so import is lossless from day one and the report is stable; layout *honoring* lands per-feature in **Phase 3**. The importer classifies each option against the engine's honoring table: a stored-but-not-yet-rendered option reports **mapped-pending-render** (badged in the editor) and flips to **mapped** as its Phase-3 feature ships. For the target style this means Phase 1 reports zero unsupported / zero unknown — ~30 mapped, the rest pending.

## 3. Layout engine changes (dependency order)

### 3.1 Tab & alignment infrastructure (first — everything else builds on it)

- Layout continues to compute in **character columns** (spaces). `TextEmitter` converts leading whitespace at emission: `tabs` → indent levels as tabs; `tabsWhenPossible` → tabs up to the last whole tab stop, spaces beyond. (`TextEmitter.cs:19` currently string-compares `== "tabs"` only.)
- Remove `RightAligner`'s tabs-mode no-op guard (`RightAligner.cs:26`) — it works in columns; the emitter renders.
- `AlignItemsToTabStops`: alignment targets round **up** to the next multiple of `TabSize`.
- Blank-line counts: `BreakType.EmptyLine` gains a count (or `LayoutNode` carries `BlankLineCount`), sourced from `EmptyLineBetweenStatements` / `EmptyLinesAfterBatchSeparator`; `TextEmitter.cs:39-42` emits N+1 newlines. `LineBreakDecider.cs:34,41` sets counts per context (statement boundary vs GO).
- Invariant property test: emitted text re-measured (tabs expanded at `TabSize`) equals the intended column for every line — guards the math without circular goldens.

### 3.2 Comma gutter

`ListRules` + post-collapse pass: leading-comma placement per `CommaAlignment` (`toList`: comma+space gutter immediately left of the list column, item text flush with first item), `SpaceBeforeComma` for inline lists and gutter width.

### 3.3 Parenthesis style enum

`ParenthesisRules` interprets the 9 values with per-context resolution (global → DDL/CTE/INSERT override). For this style: `expandedToStatement` = both parens on own lines at the statement's first column; `expandedSimple` (INSERT) = own lines at natural indent, no re-alignment. `compact*` variants map onto today's boolean behaviors for back-compat.

### 3.4 Per-construct rules (parallelizable after 3.1–3.3)

CTE name/indent/column alignment (`ControlFlowRules.ApplyCteRules`); INSERT values one-per-line + indent asymmetry (`DmlRules`); DISTINCT/TOP break-after (`LineBreakDecider`/`DmlRules`); BEGIN/END keyword indent (`ControlFlowRules`); DECLARE/SET `=`-on-new-line (`DeclareRules`); JOIN `toTable` (`JoinRules`, measured against FROM's table column); CASE `toFirstItem`/`toWhen` via the RightAligner-style measured pass; BETWEEN AND right-alignment; IN/function-call/paren inner spacing; semicolon placement (gate `NormalizeSemicolonSpacing`); wire `Ddl.ConstraintColumnsOnNewLine` (`ifLongerOrMultipleColumns`: expand when composite OR too long).

### 3.5 Casing sync

Assign `CasingEngine.IdentifierLookup` (declared `CasingEngine.cs:17`, read `:95-100`, never assigned) from the engine schema cache via the same bridge `FormatRequestHandler` uses for schema-aware format actions. No connection / cache miss → identifier left as typed.

### Deliberately not building

`alignMultilineCommentsMatchingPatterns` beyond mapping to existing `Comments.MultilineFormatting="normaliseIndent"` + `RecognizeCommonPatterns=true` — Redgate's pattern set is undocumented; iterate only if goldens diverge.

## 4. Import UX & data flow

Format Styles editor toolbar: **Import…** next to Export → `OpenFileDialog` (`SQL Prompt style (*.json;*.sqlpromptstylev2)|*.json;*.sqlpromptstylev2|All files (*.*)|*.*`), 1 MB cap, shell-side UTF-8 read (snippet-import pattern, `SnippetManagerDialog.cs:723-736`) → `ProfileImport (17)` IPC → engine sniffs format → imports → saves `<metadata.name>.akmlstyle` + `<name>.source.json` → response with per-option classifications → summary dialog (counts + expandable list) → list refresh, select, **set active** (same path as Set Active: `AppSettings.Formatter.ActiveProfile` via `ConfigManager`) → unsupported badges visible on the style (spec-020 FR-023 surface). Name collision → overwrite/rename prompt **before** IPC send (list is client-side known) or on engine "exists" response.

`ProfileImportResponse` extension: add `Options[] {Path, Value, Status, Reason}` alongside existing counts (MessagePack — engine and shells rebuilt/deployed together per the full-publish rule).

## 5. Phasing

1. **Phase 1 — Import pipeline**: importer + defaults + sniffing + silent-failure fix + IPC extension + **all §2 profile fields (storage + editor schema, per the §2 phase-assignment note)** + editor button + report dialog + set-active + unit tests (user file fixture, `full-style.json.example` round-trip, malformed-input regression). User's style usable immediately at current fidelity, imported losslessly.
2. **Phase 2 — Ground truth**: ~20 corpus files authored (each isolating an option family; MERGE included for the spec-030 residual interplay); runbook for the user (open in SSMS → activate style → Format SQL → save); goldens committed as `golden/<stem>__mohamedkhamis.sql`; driver wired. Deliverable: measured starting fidelity %.
3. **Phase 3 — Gap closure** in §3 order, each feature gated by its corpus files going green; existing self-goldens re-blessed per feature with reviewed diffs; perf harness after the emitter/alignment work.

## 6. Risks

- **CASE `toFirstItem` / right-alignments** need post-emission measurement — the RightAligner mechanism exists but extending it is the most intricate piece; sequenced immediately after 3.1 so surprises surface early.
- **Function-call spacing** conflicts with call-detection's zero-space assumption — detection fix is part of the same work item, with regression tests for wildcard-expansion and other call-recognition consumers.
- **Blank-line/break model** touches `LayoutNode`/`TextEmitter` near MERGE/GROUP BY residual quirks (spec 030 T009) — corpus coverage first, then change.
- **SQL Prompt 10 vs 11**: goldens are SP11 (user's install) — authoritative for every documented ambiguity (collapse-bool quirk, gutter rendering, `expandedSimple` nesting).
- **Idempotency (stage 7)** with tab emission: format∘format must be a fixed point; covered by SC-006 on the full corpus.

## Reference material (vendored under `reference/`)

- `formattingstyle-schema.json` — Redgate's official option schema (from ADS extension `redgate.sql-prompt-0.2.11`).
- `full-style.json.example` — every option with every value (same source).
- `MohamedKhamis-2cd71422-30f2-4360-800f-240f2897fd3e.json` — the target style (test fixture).
