# AKML SQL — Formatter Reference

## Overview

The SQL formatter rewrites T-SQL text according to a formatting profile (`.akmlstyle`), preserving semantic equivalence. It runs as part of the out-of-process Engine and is invoked via the IPC API.

---

## Formatting Pipeline

The pipeline processes SQL in sequential stages. Failures in the semantic validation stage cause the original SQL to be returned unchanged.

```
Input SQL
  Stage 0a: NoformatScanner      → identifies -- noformat / -- endnoformat regions
  Stage 0b: SqlcmdPreprocessor   → replaces :r / :setvar directives with placeholders
  Stage 1:  TSql170Parser        → parses into TSqlScript AST
  Stage 2:  AstAnnotator         → attaches comments to AST nodes
  Stage 3:  LayoutEngine         → produces LayoutNode list (tokens + whitespace rules)
  Stage 4:  CasingEngine         → applies keyword/identifier casing from profile
  Stage 5:  TextEmitter          → emits formatted string
  Stage 5b: SqlcmdPreprocessor   → restores SQLCMD placeholders
  Stage 6:  SemanticValidator    → re-parses both versions; normalizes and compares
  Stage 7:  IdempotencyCheck     → formats again; verifies output is identical
Output FormattedSQL
```

### Stage details

| Stage | Class | Notes |
|-------|-------|-------|
| 0a | `NoformatScanner` | Scans for `-- noformat` / `-- endnoformat` markers; protected regions pass through unchanged |
| 0b | `SqlcmdPreprocessor` | Replaces `:r file` and `:setvar` with unique placeholders to prevent parse errors |
| 1 | `TSql170Parser` | Microsoft TSqlParser with `initialQuotedIdentifiers: true` |
| 2 | `AstAnnotator` | Walks the AST and attaches preceding/trailing comment tokens to nodes |
| 3 | `LayoutEngine` | Converts AST + profile options into an IR list of `LayoutNode` items |
| 4 | `CasingEngine` | Applies `CasingOptions` to keyword and identifier tokens |
| 5 | `TextEmitter` | Serializes the IR into a string |
| 5b | `SqlcmdPreprocessor` | Restores original SQLCMD text |
| 6 | `SemanticValidator` | Parses the formatted output and the original AST, normalizes both, and compares token streams |
| 7 | `IdempotencyCheck` | Formats the output a second time; if the result differs, emits a diagnostic warning |

**Stage 6 failure**: returns original SQL unchanged.
**Stage 7 failure**: returns the (possibly non-idempotent) formatted SQL with a diagnostic warning appended.

---

## noformat Regions

Wrap any block you want the formatter to skip:

```sql
-- noformat
SELECT   weird   spacing   FROM   dbo.LegacyTable
-- endnoformat
```

The `NoformatScanner` identifies these regions before parsing, so even syntactically unusual SQL is safe.

---

## Profile Files (`.akmlstyle`)

Profiles are stored as JSON files:

```
%AppData%\AKML SQL\profiles\{name}.akmlstyle
```

Built-in profiles (read-only) are embedded in the extension. User profiles can be created, edited, imported, and exported via the Options UI.

### Profile file structure

```jsonc
{
  "metadata": {
    "id": "d3b07384-d9a4-4b8b-9a1d-3b4f5c6d7e8f",
    "schemaVersion": 1,
    "name": "My Profile",
    "description": "Adapted from Default",
    "author": "Jane Dev",
    "version": "1.0.0",
    "created": "2024-06-01T12:00:00Z",
    "modified": "2024-07-15T09:30:00Z",
    "basedOn": null,
    "isBuiltIn": false,
    "skipValidation": false,
    "enableIdempotencyCheck": true
  },
  "whitespace": { ... },
  "casing": { ... },
  "list": { ... },
  "parenthesis": { ... },
  "dml": { ... },
  "join": { ... },
  "ddl": { ... },
  "controlFlow": { ... },
  "case": { ... },
  "cte": { ... },
  "expression": { ... },
  "actions": { ... }
}
```

---

## Profile Options Reference

### `metadata`

| Key | Type | Default | Description |
|-----|------|---------|-------------|
| `id` | string (UUID) | auto | Unique profile identifier |
| `schemaVersion` | int | 1 | Profile schema version (for future migrations) |
| `name` | string | — | Display name |
| `description` | string | — | Optional description |
| `author` | string | — | Creator name |
| `version` | string | `"1.0.0"` | SemVer string |
| `created` | string | ISO 8601 | Creation timestamp |
| `modified` | string | ISO 8601 | Last modified timestamp |
| `basedOn` | string? | `null` | UUID of parent profile |
| `isBuiltIn` | bool | `false` | Read-only built-in profile flag |
| `skipValidation` | bool | `false` | Skip semantic round-trip (for test pipelines) |
| `enableIdempotencyCheck` | bool | `true` | Enable second-pass idempotency verification |

---

### `whitespace`

Controls line breaks, indentation, and spacing.

| Key | Type | Default | Description |
|-----|------|---------|-------------|
| `tabStyle` | `"spaces"` \| `"tabs"` | `"spaces"` | Indentation character |
| `tabSize` | int | 4 | Spaces per indent level |
| `indentStyle` | `"block"` \| `"hanging"` | `"block"` | Block vs hanging indent for continuations |
| `maxLineWidth` | int | 120 | Soft line-length limit |
| `lineBreakBeforeClause` | bool | true | New line before `SELECT`, `FROM`, `WHERE`, etc. |
| `lineBreakAfterClause` | bool | false | New line after clause keyword |
| `lineBreakBeforeComma` | bool | false | Leading comma style (`,col` vs `col,`) |
| `lineBreakAfterComma` | bool | true | Trailing comma — break after each comma |
| `emptyLineBetweenStatements` | int | 1 | Blank lines inserted between top-level statements |
| `emptyLineBeforeGO` | bool | true | Blank line before `GO` batch separator |
| `emptyLineAfterGO` | bool | true | Blank line after `GO` |
| `preserveEmptyLines` | bool | true | Keep existing blank lines (up to `maxConsecutiveEmptyLines`) |
| `maxConsecutiveEmptyLines` | int | 2 | Cap on consecutive blank lines |
| `trailingWhitespace` | `"remove"` \| `"preserve"` | `"remove"` | Trailing spaces on each line |
| `finalNewline` | `"ensure"` \| `"remove"` | `"ensure"` | Ensure file ends with a newline |
| `spaceAfterComma` | bool | true | Space after `,` in lists |
| `spaceAroundOperators` | bool | true | Spaces around `=`, `+`, `>`, etc. |
| `spaceAroundBooleanOperators` | bool | true | Spaces around `AND`, `OR`, `NOT` |
| `spaceInsideParentheses` | bool | false | Spaces inside `( expr )` |
| `spaceBeforeParentheses` | bool | false | Space before `(` in function calls |
| `lineBreakAfterSemicolon` | bool | true | New line after `;` statement terminator |

---

### `casing`

Controls keyword and identifier casing.

| Key | Type | Default | Description |
|-----|------|---------|-------------|
| `reservedKeywords` | `"UPPERCASE"` \| `"lowercase"` \| `"AsIs"` | `"UPPERCASE"` | T-SQL reserved words (`SELECT`, `FROM`, …) |
| `builtInFunctions` | `"UPPERCASE"` \| `"lowercase"` \| `"AsIs"` | `"UPPERCASE"` | Built-in functions (`ISNULL`, `GETDATE`, …) |
| `builtInDataTypes` | `"UPPERCASE"` \| `"lowercase"` \| `"AsIs"` | `"lowercase"` | Data type keywords (`int`, `varchar`, …) |
| `systemObjects` | `"UPPERCASE"` \| `"lowercase"` \| `"AsIs"` | `"lowercase"` | System catalog objects (`sys.objects`, …) |
| `globalVariables` | `"UPPERCASE"` \| `"lowercase"` \| `"AsIs"` | `"lowercase"` | `@@ROWCOUNT`, `@@ERROR`, etc. |
| `localVariables` | `"UPPERCASE"` \| `"lowercase"` \| `"AsIs"` | `"AsIs"` | `@param`, `@localVar` — preserve original |
| `identifiers` | `"UPPERCASE"` \| `"lowercase"` \| `"AsIs"` | `"AsIs"` | Table/column/procedure names |
| `syncWithDatabase` | bool | false | Match identifier casing from live schema cache |
| `camelCaseDictionary` | bool | true | Preserve known camelCase identifiers |
| `applyOnTyping` | bool | true | Apply casing corrections as you type |

---

### `list`

Controls how lists of items (SELECT columns, parameters, etc.) are laid out.

| Key | Type | Default | Description |
|-----|------|---------|-------------|
| `commaPosition` | `"trailing"` \| `"leading"` | `"trailing"` | Comma after (`col,`) or before (`,col`) each item |
| `alignItemsAcrossClauses` | bool | true | Align SELECT/INSERT columns vertically |
| `alignAliases` | bool | true | Align `AS alias` expressions |
| `oneItemPerLine` | bool | true | Each list item on its own line |
| `collapseShortLists` | bool | true | Keep short lists on one line |
| `collapseThreshold` | int | 60 | Max characters for a list to be kept on one line |
| `indentListItems` | bool | true | Indent list items relative to clause keyword |
| `alignDataTypesInDDL` | bool | true | Align data-type columns in `CREATE TABLE` |
| `alignValuesInInsert` | bool | true | Align value columns in `INSERT … VALUES` |
| `spaceAfterListComma` | bool | true | Space after `,` in lists |

---

### `parenthesis`

Controls formatting of parenthesized groups (subqueries, expressions, parameter lists).

| Key | Type | Default | Description |
|-----|------|---------|-------------|
| `openOnSameLine` | bool | true | `(` on the same line as the preceding keyword |
| `closeOnNewLine` | `"false"` \| `"true"` \| `"when_multiline"` | `"false"` | When to place `)` on its own line |
| `collapseShort` | bool | true | Keep short expressions on one line |
| `collapseThreshold` | int | 40 | Max characters to collapse onto one line |
| `indentContents` | bool | true | Indent contents inside parentheses |
| `spaceInside` | bool | false | Spaces inside `( expr )` |
| `removeRedundant` | bool | false | Remove unnecessary parentheses |
| `createTableColumns` | `"newLine"` \| `"sameLine"` | `"newLine"` | Column list style in `CREATE TABLE` |
| `procedureParameters` | `"newLine"` \| `"sameLine"` | `"newLine"` | Parameter list in `CREATE PROCEDURE` |
| `subqueryStyle` | `"indent"` \| `"aligned"` | `"indent"` | Subquery indentation style |

---

### `dml`

Controls DML (`SELECT`, `INSERT`, `UPDATE`, `DELETE`, `MERGE`) statement layout.

| Key | Type | Default | Description |
|-----|------|---------|-------------|
| `selectItemsOnNewLine` | bool | true | Each `SELECT` column on its own line |
| `selectStarOnSameLine` | bool | true | `SELECT *` stays on one line |
| `fromOnNewLine` | bool | true | `FROM` clause starts on a new line |
| `whereOnNewLine` | bool | true | `WHERE` clause starts on a new line |
| `andOrNewLine` | `"before"` \| `"after"` \| `"sameLine"` | `"before"` | Where to break before/after `AND`/`OR` |
| `andOrIndent` | `"alignWithWhere"` \| `"indent"` | `"alignWithWhere"` | Indent of `AND`/`OR` relative to `WHERE` |
| `groupByOnNewLine` | bool | true | `GROUP BY` on a new line |
| `havingOnNewLine` | bool | true | `HAVING` on a new line |
| `orderByOnNewLine` | bool | true | `ORDER BY` on a new line |
| `topOnSameLine` | bool | true | `TOP (n)` stays on the `SELECT` line |
| `distinctOnSameLine` | bool | true | `DISTINCT` stays on the `SELECT` line |
| `intoOnNewLine` | bool | true | `INTO` in `SELECT INTO` on a new line |
| `valuesOnNewLine` | bool | true | `VALUES` on a new line in `INSERT` |
| `setOnNewLine` | bool | true | `SET` on a new line in `UPDATE` |
| `deleteFromOnSameLine` | bool | true | `FROM` on same line as `DELETE` |
| `mergeWhenOnNewLine` | bool | true | `WHEN MATCHED` / `WHEN NOT MATCHED` on new lines |
| `collapseShortStatements` | bool | true | Collapse simple statements to one line |
| `collapseThreshold` | int | 80 | Max characters for a statement to collapse |
| `collapseShortSubqueries` | bool | true | Collapse short subqueries to one line |
| `subqueryCollapseThreshold` | int | 60 | Max characters for subquery collapse |

---

### `join`

Controls JOIN clause formatting.

| Key | Type | Default | Description |
|-----|------|---------|-------------|
| `onNewLine` | bool | true | `JOIN` on a new line |
| `indentJoin` | bool | false | Indent `JOIN` relative to `FROM` |
| `onConditionNewLine` | bool | true | `ON` clause on a new line |
| `onConditionIndent` | `"indent"` \| `"alignWithJoin"` | `"indent"` | Indentation of `ON` condition |
| `multipleOnConditions` | `"newLine"` \| `"sameLine"` | `"newLine"` | Multiple `AND` conditions in `ON` |
| `emptyLineBeforeJoin` | bool | false | Blank line before each `JOIN` |
| `alignJoinKeyword` | `"left"` \| `"right"` \| `"center"` | `"right"` | Align join type keywords (`LEFT`, `INNER`, etc.) |
| `joinTypeStyle` | `"explicit"` \| `"implicit"` | `"explicit"` | Always write explicit join type |
| `crossApplyNewLine` | bool | true | `CROSS APPLY` / `OUTER APPLY` on new line |

---

### `ddl`

Controls DDL (`CREATE TABLE`, `CREATE PROCEDURE`, etc.) statement layout.

| Key | Type | Default | Description |
|-----|------|---------|-------------|
| `createTableColumnsOnNewLine` | bool | true | Each column definition on its own line |
| `alignDataTypes` | bool | true | Align data type in column definitions |
| `alignConstraints` | bool | true | Align inline constraints |
| `constraintsOnNewLine` | bool | false | Table constraints on separate lines |
| `inlineConstraintStyle` | `"sameLine"` \| `"newLine"` | `"sameLine"` | Inline column constraints placement |
| `tableConstraintsSeparate` | bool | true | Empty line before table-level constraints |
| `firstParameterOnNewLine` | `"auto"` \| `"always"` \| `"never"` | `"auto"` | First procedure parameter placement |
| `parameterAlignment` | `"aligned"` \| `"hanging"` | `"aligned"` | Parameter list alignment |
| `alignParameterDataTypes` | bool | true | Align data types in parameter lists |
| `alignParameterDefaults` | bool | true | Align default values in parameter lists |
| `asOnNewLine` | bool | true | `AS` keyword on a new line in CREATE |
| `beginOnNewLine` | bool | true | `BEGIN` on a new line |
| `collapseShortDdl` | bool | true | Collapse simple DDL to one line |
| `collapseThreshold` | int | 60 | Max characters for DDL collapse |

---

### `controlFlow`

Controls `BEGIN/END`, `IF/ELSE`, `TRY/CATCH`, and `WHILE` formatting.

| Key | Type | Default | Description |
|-----|------|---------|-------------|
| `beginOnNewLine` | bool | true | `BEGIN` on its own line |
| `endOnNewLine` | bool | true | `END` on its own line |
| `indentBetweenBeginEnd` | bool | true | Indent statements between `BEGIN` and `END` |
| `collapseShortIfElse` | bool | true | Collapse simple `IF` to one line |
| `collapseThreshold` | int | 60 | Max characters for `IF` collapse |
| `elseOnNewLine` | bool | true | `ELSE` on a new line |
| `elseAlignWithIf` | bool | true | Align `ELSE` with its `IF` |
| `tryCatchOnNewLine` | bool | true | `TRY`/`CATCH` blocks on new lines |

---

### `case`

Controls `CASE … WHEN … THEN … ELSE … END` expression layout.

| Key | Type | Default | Description |
|-----|------|---------|-------------|
| `whenOnNewLine` | bool | true | Each `WHEN` on a new line |
| `thenOnNewLine` | bool | false | `THEN` on the same line as `WHEN` |
| `elseOnNewLine` | bool | true | `ELSE` on a new line |
| `endOnNewLine` | bool | true | `END` on a new line |
| `indentWhen` | bool | true | Indent `WHEN` branches |
| `alignThen` | bool | true | Align `THEN` values vertically |
| `collapseShortCase` | bool | true | Collapse simple `CASE` to one line |
| `collapseThreshold` | int | 60 | Max characters for `CASE` collapse |

---

### `cte`

Controls Common Table Expression (CTE) formatting.

| Key | Type | Default | Description |
|-----|------|---------|-------------|
| `withOnNewLine` | bool | true | `WITH` on a new line before the first CTE |
| `cteBodyIndent` | bool | true | Indent the body of each CTE |
| `commaBeforeCte` | bool | false | Leading comma before each CTE name |
| `emptyLineBetweenCtes` | bool | true | Blank line between consecutive CTEs |

---

### `expression`

Controls expression and predicate formatting.

| Key | Type | Default | Description |
|-----|------|---------|-------------|
| `booleanOperatorNewLine` | `"before"` \| `"after"` \| `"sameLine"` | `"before"` | Break before or after `AND`/`OR` |
| `betweenOnOneLine` | bool | true | `BETWEEN x AND y` on one line |
| `inListStyle` | `"multiLine"` \| `"sameLine"` | `"multiLine"` | `IN (a, b, c)` — one line or multiple |
| `inListThreshold` | int | 60 | Max characters before `IN` list breaks |
| `existsSubqueryIndent` | `"indent"` \| `"hanging"` | `"indent"` | Indentation of subquery in `EXISTS(...)` |

---

### `actions` (Format Action Config)

Controls which transformations are applied during Format Document / Format Action operations.

| Key | Type | Default | Description |
|-----|------|---------|-------------|
| `applyLayout` | bool | true | Apply all whitespace and line-break rules |
| `applyCasing` | bool | true | Apply casing rules |
| `insertSemicolons` | bool | false | Add missing `;` statement terminators |
| `removeSemicolons` | bool | false | Remove all `;` terminators |
| `expandWildcards` | bool | false | Replace `SELECT *` with explicit column list (requires schema) |
| `qualifyObjectNames` | bool | false | Add schema prefix to unqualified names |
| `addAsKeyword` | bool | true | Add `AS` to implicit aliases |
| `addSquareBrackets` | bool | false | Wrap all identifiers in square brackets |

---

## Format Actions (IPC)

Format operations can target specific transformations using the `FormatActionType` enum:

| Value | Name | Description |
|-------|------|-------------|
| 0 | `CasingOnly` | Apply keyword casing, no layout changes |
| 1 | `ExpandWildcards` | Replace `SELECT *` with column list |
| 2 | `InsertSemicolons` | Add missing statement terminators |
| 3 | `RemoveSemicolons` | Remove all `;` |
| 4 | `QualifyObjectNames` | Add `dbo.` schema prefix |
| 5 | `AddAsKeyword` | Add `AS` to implicit aliases |
| 6 | `AddSquareBrackets` | Bracket all identifiers |
| 7 | `NormalizeWhitespace` | Collapse extra spaces (no indent changes) |
| 8 | `AlignColumns` | Align columns and aliases |
| 9 | `ReorderJoins` | Sort joins by type |
| 10 | `ExtractCte` | Extract subquery to CTE |
| 11 | `InlineCte` | Inline a CTE back as a subquery |
| 12 | `AddNocount` | Insert `SET NOCOUNT ON` |
| 13 | `AddSchemaPrefix` | Add schema prefix to all object references |
| 14 | `FormatComments` | Normalize comment style |
| 15 | `FullFormat` | Apply all layout + casing rules (equivalent to Format Document) |

---

## Bulk Formatting

The `BulkFormatter` applies formatting to multiple files in parallel (up to `Environment.ProcessorCount` threads). Configuration:

- `confirmBulkFormat`: show confirmation dialog before starting
- `createBackups`: write `.bak` files alongside originals
- `handleParseErrors`: skip files with parse errors instead of aborting
- `respectNoformat`: honour `-- noformat` regions in all files

Bulk format can be cancelled mid-operation via the `BulkFormatCancel` IPC message.

---

## Diagnostic Codes

The formatter may emit diagnostics alongside the formatted output:

| Code | Level | Description |
|------|-------|-------------|
| `FMT001` | Warning | Semantic validation failed — original SQL returned unchanged |
| `FMT002` | Warning | Idempotency check failed — re-formatting would change the output |
| `FMT003` | Information | Parse error in file — skipped during bulk format |
| `FMT004` | Information | File skipped — entirely inside a `-- noformat` region |
