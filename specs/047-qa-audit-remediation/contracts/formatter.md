# Contract: formatter fixes (FMT-001 … FMT-005, SSMS-004)

All changes are in `src/AkmlSql.Formatting` and reach SSMS, the web edition and the CLI alike.

## 1. Corpus cases (committed inputs, every built-in style)

| File (`tests/format-parity/corpus/`) | Covers | Issue |
| --- | --- | --- |
| `sp047-01-proc-params.sql` | `CREATE PROCEDURE` with unparenthesized parameters (`@b varchar(10) = 'x'`), parenthesized parameters, `nvarchar(max)`, `decimal(10, 2)` with a numeric default, a `NULL` default, `OUTPUT` (T-SQL parameter defaults are constants) | FMT-001 |
| `sp047-02-control-flow.sql` | `IF … BEGIN … END ELSE BEGIN … END`, `ELSE IF`, `WHILE … BEGIN … END`, nested blocks two deep, `BEGIN TRY … END TRY BEGIN CATCH … END CATCH` inside a block; added during implementation: a BEGIN on its own line under IF, a CASE expression inside a block, statements without semicolons after END, an `IF … ELSE IF … ELSE` chain, `BEGIN TRAN … COMMIT` | FMT-002 |
| `sp047-06-statements.sql` (added during implementation) | top-level statements without semicolons (CREATE TABLE, SELECT, DROP, PRINT, DECLARE, IF, EXEC, SET) and two statements on one line | FMT-002 root cause |
| `sp047-03-go-count.sql` | `GO`, `GO 3`, `GO 3 -- note`, a final `GO` | FMT-003 |
| `sp047-04-block-comment.sql` | a block comment whose second line is indented 3 spaces relative to the first, inside and outside a statement | FMT-004 |
| `sp047-05-spacing.sql` | `INNER JOIN`, `LEFT OUTER JOIN`, a lone `SET NOCOUNT ON;`, `WHERE x IN (10, 11)` | FMT-005 |

Goldens for these files are captured once from the **fixed** formatter (`AKML_UPDATE_PARITY_GOLDEN=1`). They are
reviewed by hand against the rules below before being committed. A golden is never captured from the broken
formatter. Existing goldens that change are reviewed one by one; only changes these rules require are accepted
(Principle III: the ratchet may not fall). The web parity baselines move with them (CLAUDE.md, "Parity goldens and
autocrlf").

## 2. Rules the fixed output must satisfy (asserted by `Sp047FormatterRulesTests`, independent of the goldens)

The tests run for each of the 8 `.akmlstyle` built-ins, the web's two POCO built-ins (`default`, `ansi`), and one SQL
Prompt document style (`SqlPromptStyles.ToProfile(SqlPromptStyleDocument.CreateDefault("sp047"))`), so both layouts
are covered.

1. **FMT-001.** No line of the output ends with `(` directly after a data-type name. The line holding `@b` contains
   the type and its size together, for example `varchar(10)` / `VARCHAR (10)`, in the style's casing and spacing.
2. **FMT-002.** For every `IF`, `ELSE` and `WHILE` whose body is a `BEGIN … END`:
   - when the style puts `BEGIN` on its own line, its indent equals the owner keyword's indent, or that plus one
     level when the style's documented control-flow option says so;
   - the matching `END` has the same indent as its `BEGIN`;
   - every body line is exactly one level deeper.

   No `BEGIN` or `END` inside an indented body is at column 0. A CASE expression's `END` is not a block's `END`, and
   `BEGIN TRAN` opens no block. Added during implementation (the same defect without semicolons): an `IF`/`WHILE`
   starts its line (`ELSE IF` excepted); a line that starts with a block's `END` holds nothing else (TRY/CATCH, `;`,
   `ELSE` or a comment aside); every statement, nested or top-level, starts its own line.
3. **FMT-003.** The first line after every `GO` / `GO n` line starts at column 0.
4. **FMT-004.** The relative indentation between the lines of a block comment is unchanged. The whole block may move
   by one offset. Added during implementation: the script's first statement after its leading comments keeps the
   line break written after them (it was glued to the comment's `*/`).
5. **FMT-005.**
   - Exactly one space separates a join keyword (`JOIN`, `INNER JOIN`, `LEFT OUTER JOIN`, …) from the table name,
     unless the style aligns join keywords in a column, which is then documented in its description. No built-in does.
   - A lone `SET` statement is not padded to another keyword's width. The keyword column is per statement and per
     parenthesis level.
   - `IN (10, 11)` stays on one line, with its `IN`, when it fits the style's line width. `inListStyle` defaults to
     `auto` (60 characters); an explicit `multiLine` still expands every list.
6. **All cases.** A second format of the output changes nothing (idempotent), and the token stream is preserved
   (case, brackets and semicolons ignored), as the audit's sweep checked.

## 3. Seams (from research R8–R11)

| Issue | Where | Change |
| --- | --- | --- |
| FMT-001 | `Rules/ParenthesisRules.ApplyProcedureParameters` | The parameter-list paren is the `(` immediately after the procedure name only. Unparenthesized lists break at top-level commas between the name and `AS`. |
| FMT-002 | `Rules/ControlFlowRules.ApplyBeginEndRules` / `ApplyIndentBetweenBeginEnd` | A moved `BEGIN` takes its owner's indent. A nested `BEGIN` is at least the enclosing level + 1. |
| FMT-003 | `Pipeline/LayoutEngine` (batch start after `GO`/`GO n`); `SqlPrompt/Layout/SqlPromptPrinter.cs` if the SQL Prompt case fails | Reset the clause tracker and indent at the first token after a separator. |
| FMT-004 | comment alignment (WhitespaceRules / CommentAttachment, located by the failing case) | Shift lines uniformly; never trim inner indentation. |
| FMT-005 | `Layout/RightAligner`, `Layout/AlignmentCalculator`, `Layout/CollapseEvaluator`, `Rules/ListRules` (located by the failing case) | Pad only within an aligned group. Apply the short-list rule. |

**As built (2026-11-21).** The failing cases pointed at these seams:

- **FMT-002.**
  - `LayoutEngine` records parse-tree facts on each node: `StartsStatement`, `StatementLastTokenIndex`, and
    `OwnerTokenIndex` for the BEGIN of an IF/ELSE/WHILE body and for an ELSE.
  - `ControlFlowRules.PlaceBlocks` puts each BEGIN at its owner's line, plus one level with `indentBeginEndKeywords`,
    shifting the body with it. It also aligns END with BEGIN and BEGIN CATCH with END TRY, and aligns ELSE with its own
    IF.
  - `IndentationTracker` and `ClassifyBlockKeywords` know CASE … END, BEGIN TRAN/DISTRIBUTED/DIALOG/CONVERSATION and
    END CONVERSATION.
  - Range finders stop at a statement boundary through the shared `Layout/StatementBounds`: the DML collapse, the list
    collapse, and the DDL collapse, which also stops at the statement's own extent. The DDL "AS on a new line" check
    stops at another statement too.
  - The LayoutEngine gives every top-level statement a line break and resets the clause context of block statements.
- **FMT-003.** `LayoutEngine` (the token after GO / GO n starts a batch) and `ListRules.UpdateClauseContext` (GO and
  statement starts end a list).
- **FMT-004.** `TextEmitter.FormatBlockComment` shifts the block by the distance its `/*` moved (`LayoutNode.OriginalColumn`).
  `LayoutEngine` keeps the break after leading comments.
- **FMT-005.** `ListRules.ApplyAlignItemsAcrossClauses` (per statement and parenthesis level, no join keywords);
  `ExpressionOptions.InListStyle` default `auto` and the six classic built-ins; `ControlFlowRules.CollapseInList`.
  RightAligner, AlignmentCalculator and CollapseEvaluator needed no change.
- **SSMS-004.** `FormatterPipeline` stage 9 (`UsesCrLf`, `WithLineEndings`).

## 4. Line endings (SSMS-004)

`FormatterPipeline` adds a final step after the semantic validation and idempotency checks:

- the output takes the input's dominant line ending (data-model §12);
- `FormatResult.FormattedText` is in that convention;
- a failed or skipped format returns the original text, so it is untouched by definition.

Tests in `tests/AkmlSql.Formatting.Tests/Pipeline/LineEndingTests.cs`:

- a CRLF input gives a CRLF output with no lone `\n`;
- an LF input gives an LF output;
- a mixed input that is mostly LF gives LF;
- an empty or one-line input is unchanged.

An engine test checks the same through `FormatRequestHandler`.
