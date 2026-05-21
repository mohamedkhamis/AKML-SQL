# AKML SQL — Phase 3: SQL Formatter & Code Beautifier

> **Version:** 1.0 | **Date:** March 2026 | **Author:** Mohamed Khamis
> **Status:** Ready for Implementation | **Classification:** Confidential
> **Depends on:** Phase 2 (Core IntelliSense Engine) — T-SQL parser & schema cache must be complete
> **Branch prefix:** `003-sql-formatter`

---

## 1. Executive Summary

Phase 3 delivers the SQL Formatter — the feature that transforms messy, inconsistent SQL into clean, readable, standardized code with a single keystroke. This is the second most-requested feature after IntelliSense and the primary driver of team-wide code consistency. Where Phase 2 helps you *write* SQL faster, Phase 3 ensures that *every* piece of SQL — whether freshly written or inherited from a decade-old stored procedure — looks like it was authored by the same meticulous developer.

The goal: a developer highlights 500 lines of legacy SQL spaghetti, presses **Ctrl+K, Y**, and in under 200ms the code is perfectly indented, consistently cased, logically aligned, and immediately reviewable — without altering a single byte of the query's semantics.

### Why Formatting Matters More Than It Seems

SQL formatting is not cosmetic. In a team of 10 developers, inconsistent formatting creates noise in every pull request, every code review, and every merge conflict. Studies show that developers spend 60–70% of their time reading code, not writing it. A codebase with consistent formatting reduces cognitive load, speeds up code reviews by 30–40%, and prevents entire categories of "diff noise" bugs where formatting changes obscure logic changes.

SQL Prompt's formatter is consistently cited by users as the single feature that makes the product indispensable — even more than IntelliSense. One user put it bluntly: *"I can tolerate slow IntelliSense, but I cannot work without one-click formatting."* AKML SQL must match this sentiment from day one.

### Core Philosophy

Formatting is **opinionated by default, infinitely configurable when needed**. Out of the box, AKML SQL ships with sensible formatting profiles that produce beautiful SQL without any configuration. For teams that need exact control over comma placement, keyword casing, or JOIN alignment — every single formatting decision is exposed as a configurable option. And for the 5% of code that should never be touched, `--noformat` / `--endnoformat` tags provide surgical exemptions.

---

## 2. Document Metadata

| Field | Value |
|---|---|
| **Phase** | Phase 3 — SQL Formatter & Code Beautifier |
| **Depends on** | Phase 2 (T-SQL parser, schema cache, out-of-process engine) |
| **Target SSMS** | SSMS 20 (x86), SSMS 21 (x64), SSMS 22 (x64) |
| **Target Visual Studio** | VS 2019, VS 2022, VS 2026 (with SSDT) |
| **Target SQL Server** | SQL Server 2016–2025, Azure SQL Database, Azure SQL MI, Microsoft Fabric |
| **.NET Version** | .NET Fx 4.7.2 (shell) + .NET 10/11 (formatter engine, out-of-proc) |
| **Performance Target** | Format 10,000-line script in < 500ms |
| **Benchmark** | Redgate SQL Prompt formatter + dbForge SQL Complete formatter (combined feature set) |

---

## 3. Goals & Non-Goals

### 3.1 Goals

- **One-click formatting:** Ctrl+K, Y formats the entire document or selected fragment instantly
- **250+ formatting options:** Covering every aspect of SQL layout — whitespace, casing, indentation, alignment, line breaks, parentheses, statements, clauses, expressions, JOINs, CTEs, DDL, DML, control flow, and more
- **Predefined profiles:** Ship with 5+ built-in profiles (Default, Compact, Expanded, MSDN, Minimalist) as starting points
- **Custom profiles:** Users can create unlimited personal formatting profiles with full option control
- **Team profile sharing:** Export/import profiles as JSON; cloud sync via AKML Platform (future)
- **Bulk formatting:** Format entire files, directories, and database scripts in a single operation
- **Command-line formatter:** CLI tool for CI/CD pipeline integration, pre-commit hooks, and automated formatting
- **Format-on-paste:** Automatically format SQL pasted from clipboard according to active profile
- **Format-on-save:** Optionally auto-format when saving .sql files
- **Format-on-delimiter:** Auto-format completed statement when semicolon or GO is typed
- **Noformat regions:** `--noformat` / `--endnoformat` comment tags to exclude specific code blocks
- **Live preview:** Real-time preview of formatting changes while editing profile options
- **Semantic preservation:** Formatting NEVER changes the semantic meaning of any SQL statement

### 3.2 Non-Goals (Deferred)

- No AI-powered formatting suggestions (Phase 9)
- No cross-database formatting (PostgreSQL, MySQL) — SQL Server T-SQL only in this phase
- No formatting of embedded SQL in C#/VB code (future consideration)
- No EditorConfig integration (Phase 8 or later)

---

## 4. Architecture Overview

### 4.1 Formatter Pipeline

The formatter operates as a deterministic pipeline inside the Phase 2 out-of-process engine. It reuses the T-SQL parser and AST from Phase 2, adding a layout engine that transforms the AST into formatted text.

```
┌───────────────────────────────────────────────────────────┐
│  SSMS / Visual Studio (UI Thread)                          │
│  ┌───────────────────────────────────────────────────┐     │
│  │  AkmlSql VSPackage (.NET Fx 4.7.2)                │     │
│  │  ┌──────────────────┐  ┌──────────────────────┐   │     │
│  │  │ Format Command   │  │ Profile Selector UI  │   │     │
│  │  │ (Ctrl+K, Y)      │  │ (dropdown + editor)  │   │     │
│  │  └──────┬───────────┘  └──────────▲───────────┘   │     │
│  │         │ FormatRequest           │ FormatResult   │     │
│  └─────────┼─────────────────────────┼───────────────┘     │
│            │ Named Pipe              │                      │
└────────────┼─────────────────────────┼──────────────────────┘
             │                         │
┌────────────▼─────────────────────────┴──────────────────────┐
│  AkmlSql.IntelliSense Engine (.NET 10/11, out-of-proc)      │
│                                                              │
│  ┌──────────────┐   ┌──────────────┐   ┌────────────────┐   │
│  │ T-SQL Parser │──►│ AST Builder  │──►│ Layout Engine  │   │
│  │ (ScriptDom)  │   │ (annotated)  │   │ (rule-based)   │   │
│  └──────────────┘   └──────────────┘   └───────┬────────┘   │
│                                                 │            │
│  ┌──────────────┐   ┌──────────────┐   ┌───────▼────────┐   │
│  │ Profile Mgr  │──►│ Rule Engine  │──►│ Text Emitter   │   │
│  │ (JSON files) │   │ (250+ rules) │   │ (indented text)│   │
│  └──────────────┘   └──────────────┘   └────────────────┘   │
│                                                              │
│  ┌────────────────────────────────────────────────────────┐  │
│  │              Noformat Region Handler                    │  │
│  │  (preserves original text inside noformat comment tags) │  │
│  └────────────────────────────────────────────────────────┘  │
└──────────────────────────────────────────────────────────────┘
```

### 4.2 Formatting Pipeline Stages

| Stage | Input | Output | Description |
|---|---|---|---|
| **1. Parse** | Raw SQL text | ScriptDom AST | Full T-SQL parse via ScriptDom. Tolerates syntax errors (formats what it can). |
| **2. Annotate** | AST | Annotated AST | Attach noformat regions, comment positions, and semantic context to each AST node |
| **3. Layout** | Annotated AST + Profile | Layout tree | Apply 250+ formatting rules from the active profile to determine line breaks, indentation, spacing, and alignment for every token |
| **4. Casing** | Layout tree + Profile + Schema cache | Cased layout tree | Apply keyword/function/datatype/identifier casing rules. Optionally synchronize identifier case with database catalog. |
| **5. Emit** | Cased layout tree | Formatted SQL string | Serialize the layout tree to a flat string with correct whitespace, newlines, and indentation |
| **6. Validate** | Original SQL + Formatted SQL | Boolean (pass/fail) | Verify tokenization equivalence — the formatted output must parse to a semantically identical AST. If not, return the original SQL unchanged with a warning. |

### 4.3 Why This Architecture?

- **AST-based formatting guarantees semantic preservation.** Unlike regex-based formatters that can break SQL, the AST-based approach understands the language structure and can never produce invalid transformations.
- **Profile-driven rules decouple formatting logic from code.** Adding a new formatting option means adding a JSON key and a rule handler — no core pipeline changes.
- **Out-of-process execution keeps SSMS responsive.** Even formatting a 50,000-line script won't freeze the IDE.
- **ScriptDom reuse from Phase 2** means zero additional parsing overhead — the formatter can operate on an already-cached AST.

---

## 5. Formatting Options — Complete Taxonomy

AKML SQL ships with **250+ formatting options** organized into 8 categories. Each option has a sensible default, a set of allowed values, and a live preview showing its effect. All options are stored as a JSON profile.

### 5.1 Category 1: Whitespace & Indentation

Controls how horizontal and vertical whitespace is managed across the entire script.

| Option | Default | Allowed Values | Description |
|---|---|---|---|
| `whitespace.tabStyle` | `spaces` | `spaces`, `tabs` | Use spaces or tab characters for indentation |
| `whitespace.tabSize` | `4` | `1`–`8` | Number of spaces per indentation level (when tabStyle = spaces) |
| `whitespace.indentStyle` | `block` | `block`, `hanging`, `alignedBlock` | Block: standard indent. Hanging: continuation lines indented further. AlignedBlock: align to opening keyword. |
| `whitespace.maxLineWidth` | `120` | `80`–`200`, `0` (unlimited) | Wrap lines exceeding this width. 0 = no wrapping. |
| `whitespace.lineBreakBeforeClause` | `true` | `true`, `false` | Insert line break before each major clause (SELECT, FROM, WHERE, etc.) |
| `whitespace.lineBreakAfterClause` | `false` | `true`, `false` | Insert line break after each clause keyword |
| `whitespace.lineBreakBeforeComma` | `false` | `true`, `false` | Place commas at the beginning of the next line (leading commas style) |
| `whitespace.lineBreakAfterComma` | `true` | `true`, `false` | Place items after comma on new line |
| `whitespace.emptyLineBetweenStatements` | `1` | `0`–`3` | Number of empty lines between SQL statements |
| `whitespace.emptyLineBeforeGO` | `true` | `true`, `false` | Insert empty line before GO batch separator |
| `whitespace.emptyLineAfterGO` | `true` | `true`, `false` | Insert empty line after GO batch separator |
| `whitespace.preserveEmptyLines` | `true` | `true`, `false` | Preserve user-inserted empty lines within statements |
| `whitespace.maxConsecutiveEmptyLines` | `2` | `1`–`5` | Collapse multiple empty lines to this maximum |
| `whitespace.trailingWhitespace` | `remove` | `remove`, `preserve` | Remove trailing spaces on each line |
| `whitespace.finalNewline` | `ensure` | `ensure`, `remove`, `preserve` | Ensure file ends with exactly one newline |
| `whitespace.spaceAfterComma` | `true` | `true`, `false` | Add space after comma in lists |
| `whitespace.spaceAroundOperators` | `true` | `true`, `false` | Add spaces around =, <>, <, >, +, -, *, /, etc. |
| `whitespace.spaceAroundBooleanOperators` | `true` | `true`, `false` | Add spaces around AND, OR, NOT |
| `whitespace.spaceInsideParentheses` | `false` | `true`, `false` | Add space after ( and before ) |
| `whitespace.spaceBeforeParentheses` | `false` | `true`, `false` | Add space before opening parenthesis in function calls |
| `whitespace.lineBreakAfterSemicolon` | `true` | `true`, `false` | Insert line break after statement terminator semicolon |

### 5.2 Category 2: Casing

Controls the capitalization of keywords, functions, data types, identifiers, and variables.

| Option | Default | Allowed Values | Description |
|---|---|---|---|
| `casing.reservedKeywords` | `UPPERCASE` | `UPPERCASE`, `lowercase`, `PascalCase`, `camelCase`, `AsIs` | Casing for T-SQL reserved keywords (SELECT, FROM, WHERE, etc.) |
| `casing.builtInFunctions` | `UPPERCASE` | `UPPERCASE`, `lowercase`, `PascalCase`, `camelCase`, `AsIs` | Casing for built-in functions (GETDATE, ISNULL, CONVERT, etc.) |
| `casing.builtInDataTypes` | `lowercase` | `UPPERCASE`, `lowercase`, `PascalCase`, `camelCase`, `AsIs` | Casing for data types (int, varchar, datetime, etc.) |
| `casing.systemObjects` | `lowercase` | `UPPERCASE`, `lowercase`, `PascalCase`, `camelCase`, `AsIs` | Casing for sys.*, sp_*, xp_*, fn_* objects |
| `casing.globalVariables` | `lowercase` | `UPPERCASE`, `lowercase`, `PascalCase`, `camelCase`, `AsIs` | Casing for @@ROWCOUNT, @@ERROR, @@IDENTITY, etc. |
| `casing.localVariables` | `AsIs` | `UPPERCASE`, `lowercase`, `PascalCase`, `camelCase`, `AsIs` | Casing for @variables |
| `casing.identifiers` | `AsIs` | `UPPERCASE`, `lowercase`, `PascalCase`, `camelCase`, `AsIs` | Casing for table/column/schema names |
| `casing.syncWithDatabase` | `false` | `true`, `false` | Synchronize identifier casing with the database catalog (requires active connection). Overrides `casing.identifiers`. |
| `casing.camelCaseDictionary` | `true` | `true`, `false` | Use CamelCase word dictionary for identifier formatting (e.g., `customerid` → `CustomerId`) |
| `casing.applyOnTyping` | `true` | `true`, `false` | Apply casing rules in real-time as you type (not just on format) |

### 5.3 Category 3: Lists & Alignment

Controls how comma-separated lists (column lists, parameter lists, value lists) are laid out and aligned.

| Option | Default | Allowed Values | Description |
|---|---|---|---|
| `lists.commaPosition` | `trailing` | `trailing`, `leading` | `trailing`: comma at end of line. `leading`: comma at start of next line. |
| `lists.alignItemsAcrossClauses` | `true` | `true`, `false` | Align first column of SELECT, FROM, WHERE lists to the same indent level |
| `lists.alignAliases` | `true` | `true`, `false` | Right-align AS aliases in column lists for visual consistency |
| `lists.oneItemPerLine` | `true` | `true`, `false` | Place each list item on its own line |
| `lists.collapseShortLists` | `true` | `true`, `false` | Keep short lists (< threshold) on a single line |
| `lists.collapseThreshold` | `60` | `30`–`200` (characters) | Lists shorter than this width remain on one line |
| `lists.indentListItems` | `true` | `true`, `false` | Indent list items relative to their clause keyword |
| `lists.alignDataTypesInDDL` | `true` | `true`, `false` | Align data types and constraints in CREATE TABLE column lists |
| `lists.alignValuesInInsert` | `true` | `true`, `false` | Align VALUES list items with their corresponding column names |
| `lists.spaceAfterListComma` | `true` | `true`, `false` | Add space after comma in lists (overrides global setting for lists) |

### 5.4 Category 4: Parentheses

Controls how parentheses (round brackets) are positioned and formatted.

| Option | Default | Allowed Values | Description |
|---|---|---|---|
| `parentheses.openOnSameLine` | `true` | `true`, `false` | Opening parenthesis on same line as keyword/function, or new line |
| `parentheses.closeOnNewLine` | `false` | `true`, `false`, `auto` | Closing parenthesis on its own line. `auto`: new line only if content is multi-line. |
| `parentheses.collapseShort` | `true` | `true`, `false` | Keep short parenthesized expressions on one line |
| `parentheses.collapseThreshold` | `40` | `20`–`120` (characters) | Parenthesized content shorter than this stays on one line |
| `parentheses.indentContents` | `true` | `true`, `false` | Indent content inside parentheses |
| `parentheses.spaceInside` | `false` | `true`, `false` | Add space after ( and before ) |
| `parentheses.removeRedundant` | `false` | `true`, `false` | Remove parentheses that don't change operator precedence |
| `parentheses.createTableColumns` | `newLine` | `sameLine`, `newLine` | Opening parenthesis placement for CREATE TABLE column list |
| `parentheses.procedureParameters` | `newLine` | `sameLine`, `newLine` | Opening parenthesis placement for CREATE PROCEDURE parameter list |
| `parentheses.subqueryStyle` | `indent` | `indent`, `alignWithClause` | How to indent subquery content within parentheses |

### 5.5 Category 5: DML Statements

Controls the formatting of SELECT, INSERT, UPDATE, DELETE, and MERGE statements.

| Option | Default | Allowed Values | Description |
|---|---|---|---|
| `dml.selectItemsOnNewLine` | `true` | `true`, `false` | Each column in SELECT on its own line |
| `dml.selectStarOnSameLine` | `true` | `true`, `false` | Keep `SELECT *` on one line (even when selectItemsOnNewLine is true) |
| `dml.fromOnNewLine` | `true` | `true`, `false` | FROM clause starts on a new line |
| `dml.whereOnNewLine` | `true` | `true`, `false` | WHERE clause starts on a new line |
| `dml.andOrNewLine` | `before` | `before`, `after`, `sameLine` | AND/OR placement: before the condition (new line), after the condition, or same line |
| `dml.andOrIndent` | `alignWithWhere` | `alignWithWhere`, `indent`, `noIndent` | AND/OR alignment relative to WHERE keyword |
| `dml.groupByOnNewLine` | `true` | `true`, `false` | GROUP BY on new line |
| `dml.havingOnNewLine` | `true` | `true`, `false` | HAVING on new line |
| `dml.orderByOnNewLine` | `true` | `true`, `false` | ORDER BY on new line |
| `dml.topOnSameLine` | `true` | `true`, `false` | Keep TOP(n) on same line as SELECT |
| `dml.distinctOnSameLine` | `true` | `true`, `false` | Keep DISTINCT on same line as SELECT |
| `dml.intoOnNewLine` | `true` | `true`, `false` | INSERT INTO table on new line from column list |
| `dml.valuesOnNewLine` | `true` | `true`, `false` | VALUES keyword on new line in INSERT |
| `dml.setOnNewLine` | `true` | `true`, `false` | SET clause items each on new line in UPDATE |
| `dml.deleteFromOnSameLine` | `true` | `true`, `false` | DELETE FROM on one line |
| `dml.mergeWhenOnNewLine` | `true` | `true`, `false` | WHEN MATCHED / NOT MATCHED on new lines in MERGE |
| `dml.collapseShortStatements` | `true` | `true`, `false` | Keep short DML statements on one/few lines |
| `dml.collapseThreshold` | `80` | `40`–`200` (characters) | DML statements shorter than this stay collapsed |
| `dml.collapseShortSubqueries` | `true` | `true`, `false` | Keep short subqueries on one line |
| `dml.subqueryCollapseThreshold` | `60` | `30`–`150` (characters) | Subqueries shorter than this stay on one line |

### 5.6 Category 6: JOIN Clauses

Controls the formatting of JOIN … ON clauses and related constructs.

| Option | Default | Allowed Values | Description |
|---|---|---|---|
| `join.onNewLine` | `true` | `true`, `false` | Each JOIN starts on a new line |
| `join.indentJoin` | `false` | `true`, `false` | Indent JOIN keyword relative to FROM |
| `join.onConditionNewLine` | `true` | `true`, `false` | ON condition on new line after JOIN table |
| `join.onConditionIndent` | `indent` | `indent`, `alignWithJoin`, `alignWithTable` | ON condition indent relative to JOIN keyword |
| `join.multipleOnConditions` | `newLine` | `newLine`, `sameLine` | Multiple ON conditions (AND) placement |
| `join.emptyLineBeforeJoin` | `false` | `true`, `false` | Insert empty line before each JOIN |
| `join.alignJoinKeyword` | `right` | `right`, `left`, `indent` | Right-align JOIN types (LEFT, INNER, etc.) for visual consistency |
| `join.joinTypeStyle` | `explicit` | `explicit`, `asIs` | Normalize: `JOIN` → `INNER JOIN`, `LEFT JOIN` → `LEFT OUTER JOIN` (or leave as-is) |
| `join.crossApplyNewLine` | `true` | `true`, `false` | CROSS APPLY / OUTER APPLY on new line |

### 5.7 Category 7: DDL Statements

Controls the formatting of CREATE, ALTER, DROP, and related schema modification statements.

| Option | Default | Allowed Values | Description |
|---|---|---|---|
| `ddl.createTableColumnsOnNewLine` | `true` | `true`, `false` | Each column definition in CREATE TABLE on its own line |
| `ddl.alignDataTypes` | `true` | `true`, `false` | Vertically align data types in column definitions |
| `ddl.alignConstraints` | `true` | `true`, `false` | Vertically align NULL/NOT NULL, DEFAULT, CHECK constraints |
| `ddl.constraintsOnNewLine` | `false` | `true`, `false` | Place column constraints on a separate line below the data type |
| `ddl.inlineConstraintStyle` | `sameLine` | `sameLine`, `newLine` | Inline CHECK, DEFAULT constraints placement |
| `ddl.tableConstraintsSeparate` | `true` | `true`, `false` | Separate table-level constraints (PK, FK, UNIQUE) with empty line |
| `ddl.firstParameterOnNewLine` | `auto` | `always`, `never`, `auto` | First procedure/function parameter on same line or next line. `auto`: new line if > 2 params. |
| `ddl.parameterAlignment` | `aligned` | `aligned`, `indented`, `hanging` | Parameter list alignment in stored procedures and functions |
| `ddl.alignParameterDataTypes` | `true` | `true`, `false` | Vertically align parameter data types |
| `ddl.alignParameterDefaults` | `true` | `true`, `false` | Vertically align parameter default values |
| `ddl.asOnNewLine` | `true` | `true`, `false` | AS keyword on new line in CREATE PROCEDURE/FUNCTION/VIEW |
| `ddl.beginOnNewLine` | `true` | `true`, `false` | BEGIN on new line after AS |
| `ddl.collapseShortDDL` | `true` | `true`, `false` | Keep short DDL (e.g., `CREATE INDEX`) on fewer lines |
| `ddl.collapseThreshold` | `60` | `30`–`150` (characters) | DDL statements shorter than this stay collapsed |

### 5.8 Category 8: Control Flow, CASE, CTEs & Expressions

Controls formatting of IF/ELSE, WHILE, TRY/CATCH, BEGIN/END, CASE expressions, CTEs, and operators.

| Option | Default | Allowed Values | Description |
|---|---|---|---|
| `controlFlow.beginOnNewLine` | `true` | `true`, `false` | BEGIN on new line after IF/ELSE/WHILE |
| `controlFlow.endOnNewLine` | `true` | `true`, `false` | END on its own line |
| `controlFlow.indentBetweenBeginEnd` | `true` | `true`, `false` | Indent code between BEGIN and END |
| `controlFlow.collapseShortIfElse` | `true` | `true`, `false` | Keep short IF statements on one line |
| `controlFlow.collapseThreshold` | `60` | `30`–`150` (characters) | Control flow statements shorter than this stay collapsed |
| `controlFlow.elseOnNewLine` | `true` | `true`, `false` | ELSE on its own line |
| `controlFlow.elseAlignWithIf` | `true` | `true`, `false` | Align ELSE with matching IF |
| `controlFlow.tryCatchOnNewLine` | `true` | `true`, `false` | BEGIN TRY / BEGIN CATCH on new lines |
| `case.whenOnNewLine` | `true` | `true`, `false` | Each WHEN on its own line |
| `case.thenOnNewLine` | `false` | `true`, `false` | THEN on same line as WHEN, or new line |
| `case.elseOnNewLine` | `true` | `true`, `false` | ELSE on its own line in CASE |
| `case.endOnNewLine` | `true` | `true`, `false` | END on its own line |
| `case.indentWhen` | `true` | `true`, `false` | Indent WHEN clauses relative to CASE |
| `case.alignThen` | `true` | `true`, `false` | Vertically align all THEN keywords in a CASE block |
| `case.collapseShortCase` | `true` | `true`, `false` | Keep short CASE expressions on one line |
| `case.collapseThreshold` | `60` | `30`–`120` (characters) | CASE expressions shorter than this stay collapsed |
| `cte.withOnNewLine` | `true` | `true`, `false` | WITH keyword on new line (if not first statement) |
| `cte.cteBodyIndent` | `true` | `true`, `false` | Indent the CTE body relative to the CTE name |
| `cte.commaBeforeCte` | `false` | `true`, `false` | Leading comma before subsequent CTEs |
| `cte.emptyLineBetweenCtes` | `true` | `true`, `false` | Empty line between multiple CTE definitions |
| `expressions.booleanOperatorNewLine` | `before` | `before`, `after`, `sameLine` | AND/OR in WHERE/HAVING: place on new line before or after condition |
| `expressions.betweenOnOneLine` | `true` | `true`, `false` | Keep BETWEEN … AND … on one line when short |
| `expressions.inListStyle` | `multiLine` | `multiLine`, `singleLine`, `auto` | IN (...) list layout. `auto`: single line if < threshold. |
| `expressions.inListThreshold` | `60` | `30`–`200` (characters) | IN list shorter than this stays on one line |
| `expressions.existsSubqueryIndent` | `indent` | `indent`, `alignWithExists` | Indentation of EXISTS subquery |

---

## 6. Predefined Formatting Profiles

AKML SQL ships with 5 built-in profiles. Users cannot modify built-in profiles but can copy them as a base for custom profiles.

### 6.1 Profile: Default

The recommended starting point. Balanced between readability and space efficiency.

```sql
SELECT
    o.OrderID,
    o.OrderDate,
    c.CompanyName,
    SUM(od.Quantity * od.UnitPrice) AS TotalAmount
FROM dbo.Orders AS o
INNER JOIN dbo.Customers AS c
    ON c.CustomerID = o.CustomerID
INNER JOIN dbo.OrderDetails AS od
    ON od.OrderID = o.OrderID
WHERE o.OrderDate >= '2025-01-01'
    AND o.Status = 'Active'
GROUP BY
    o.OrderID,
    o.OrderDate,
    c.CompanyName
HAVING SUM(od.Quantity * od.UnitPrice) > 1000
ORDER BY TotalAmount DESC;
```

### 6.2 Profile: Compact

Minimizes vertical space. Ideal for quick scripts and ad-hoc queries.

```sql
SELECT o.OrderID, o.OrderDate, c.CompanyName,
       SUM(od.Quantity * od.UnitPrice) AS TotalAmount
FROM dbo.Orders AS o
INNER JOIN dbo.Customers AS c ON c.CustomerID = o.CustomerID
INNER JOIN dbo.OrderDetails AS od ON od.OrderID = o.OrderID
WHERE o.OrderDate >= '2025-01-01' AND o.Status = 'Active'
GROUP BY o.OrderID, o.OrderDate, c.CompanyName
HAVING SUM(od.Quantity * od.UnitPrice) > 1000
ORDER BY TotalAmount DESC;
```

### 6.3 Profile: Expanded

Maximum readability. Each element on its own line with generous spacing.

```sql
SELECT
    o.OrderID
    ,o.OrderDate
    ,c.CompanyName
    ,SUM(od.Quantity * od.UnitPrice)    AS TotalAmount
FROM
    dbo.Orders                          AS o
INNER JOIN
    dbo.Customers                       AS c
        ON c.CustomerID = o.CustomerID
INNER JOIN
    dbo.OrderDetails                    AS od
        ON od.OrderID = o.OrderID
WHERE
    o.OrderDate >= '2025-01-01'
    AND o.Status = 'Active'
GROUP BY
    o.OrderID
    ,o.OrderDate
    ,c.CompanyName
HAVING
    SUM(od.Quantity * od.UnitPrice) > 1000
ORDER BY
    TotalAmount DESC
;
```

### 6.4 Profile: Leading Commas

Popular in data engineering and analytics teams. Commas at the start of each line.

```sql
SELECT
      o.OrderID
    , o.OrderDate
    , c.CompanyName
    , SUM(od.Quantity * od.UnitPrice) AS TotalAmount
FROM dbo.Orders AS o
INNER JOIN dbo.Customers AS c
    ON c.CustomerID = o.CustomerID
INNER JOIN dbo.OrderDetails AS od
    ON od.OrderID = o.OrderID
WHERE o.OrderDate >= '2025-01-01'
  AND o.Status = 'Active'
GROUP BY
      o.OrderID
    , o.OrderDate
    , c.CompanyName
HAVING SUM(od.Quantity * od.UnitPrice) > 1000
ORDER BY TotalAmount DESC;
```

### 6.5 Profile: Minimalist

Least amount of reformatting. Only applies casing, trailing whitespace removal, and statement separation. Preserves original line breaks and indentation as much as possible.

---

## 7. Noformat Regions

Users can exclude specific code blocks from formatting using comment-based tags.

### 7.1 Syntax

```sql
-- Formatted normally
SELECT *
FROM dbo.Orders;

--noformat
-- Everything between these tags is preserved exactly as-is
SELECT   o.OrderID,   o.OrderDate
  FROM     dbo.Orders   o
  WHERE    o.Status='Active'
--endnoformat

-- Formatting resumes here
SELECT *
FROM dbo.Customers;
```

### 7.2 Rules

- Tags are case-insensitive: `--NOFORMAT`, `--noformat`, `/* noformat */` all work
- Block comment syntax also supported: `/* noformat */` … `/* endnoformat */`
- Noformat regions can span multiple statements and GO batches
- Nested noformat tags are treated as a single region (first open to last close)
- If `--noformat` has no matching `--endnoformat`, the rest of the file is preserved as-is
- Noformat regions are highlighted in the editor with a subtle background tint (configurable)

---

## 8. Format Actions

Beyond the main "Format SQL" command, AKML SQL provides standalone formatting actions that can run independently or as part of the Format SQL command.

### 8.1 Action List

| Action | Shortcut | Standalone | Part of Format SQL | Description |
|---|---|---|---|---|
| **Format SQL** | `Ctrl+K, Y` | — | — | Apply full formatting with active profile |
| **Format Selection** | `Ctrl+K, F` | Yes | — | Format only the selected text |
| **Apply Casing Only** | `Ctrl+B, Ctrl+U` | Yes | Optional | Apply casing rules without changing layout |
| **Insert Semicolons** | `Ctrl+B, Ctrl+S` | Yes | Optional | Add missing statement terminators |
| **Remove Semicolons** | — | Yes | Optional | Remove statement terminators |
| **Expand Wildcards** | `Ctrl+B, Ctrl+W` | Yes | Optional | Replace `SELECT *` with explicit column list |
| **Qualify Object Names** | `Ctrl+B, Ctrl+Q` | Yes | Optional | Add schema prefix (e.g., `Orders` → `dbo.Orders`) |
| **Add/Remove AS Keyword** | `Ctrl+B, Ctrl+A` | Yes | Optional | Add or remove AS keyword on alias definitions |
| **Add/Remove Square Brackets** | `Ctrl+B, Ctrl+B` | Yes | Optional | Add or remove `[square brackets]` on identifiers |
| **Format on Paste** | — | Auto | — | Auto-format pasted SQL content |
| **Format on Save** | — | Auto | — | Auto-format when .sql file is saved |
| **Format on Delimiter** | — | Auto | — | Auto-format statement when `;` or `GO` is typed |

### 8.2 Configurable Action Inclusion

Users configure which actions are included when running Format SQL:

```json
{
  "formatActions": {
    "applyLayout": true,
    "applyCasing": true,
    "insertSemicolons": false,
    "expandWildcards": false,
    "qualifyObjectNames": false,
    "addAsKeyword": true,
    "addSquareBrackets": false
  }
}
```

---

## 9. Profile Management

### 9.1 Profile Storage

| Location | Type | Description |
|---|---|---|
| `%AppData%\AKML SQL\profiles\` | Personal | User's custom profiles (JSON files) |
| `<install>\profiles\` | Built-in | Read-only built-in profiles shipped with AKML SQL |
| AKML Platform (future) | Team | Cloud-synced team profiles |

### 9.2 Profile File Format

Profiles are stored as human-readable, commented JSON files with a `.akmlstyle` extension:

```json
{
  "metadata": {
    "id": "a3b7c9d1-e2f4-5678-9012-abcdef123456",
    "name": "Our Team Standard",
    "description": "Team-wide formatting standard for the data engineering team",
    "author": "Mohamed Khamis",
    "version": "1.2",
    "created": "2026-04-15T10:30:00Z",
    "modified": "2026-06-20T14:15:00Z",
    "basedOn": "Default"
  },
  "whitespace": { /* ... */ },
  "casing": { /* ... */ },
  "lists": { /* ... */ },
  "parentheses": { /* ... */ },
  "dml": { /* ... */ },
  "join": { /* ... */ },
  "ddl": { /* ... */ },
  "controlFlow": { /* ... */ },
  "case": { /* ... */ },
  "cte": { /* ... */ },
  "expressions": { /* ... */ },
  "formatActions": { /* ... */ }
}
```

### 9.3 Profile Operations

| Operation | UI | CLI | Description |
|---|---|---|---|
| Create | ✔ | ✔ | Create new profile from scratch or copy existing |
| Edit | ✔ | — | Visual editor with live preview pane |
| Delete | ✔ | ✔ | Delete custom profile (built-in profiles cannot be deleted) |
| Duplicate | ✔ | ✔ | Copy a profile as starting point for a new one |
| Export | ✔ | ✔ | Export profile as `.akmlstyle` JSON file |
| Import | ✔ | ✔ | Import profile from `.akmlstyle` file |
| Compare | ✔ | ✔ | Side-by-side diff of two profiles showing all option differences |
| Set Active | ✔ | ✔ | Set the active profile used for one-click formatting |
| Quick Switch | ✔ | — | Dropdown in toolbar for instant profile switching |
| Share (Team) | ✔ | — | Upload to AKML Platform for team access (future) |

### 9.4 Redgate SQL Prompt Profile Import

To ease migration, AKML SQL can import SQL Prompt `.sqlpromptstyle` JSON files and convert them to `.akmlstyle` format with best-effort mapping of equivalent options.

---

## 10. Command-Line Formatter (CLI)

### 10.1 Overview

The CLI formatter is a standalone executable (`akmlsql-format.exe`) that formats SQL files from the command line. It enables integration with CI/CD pipelines, pre-commit Git hooks, and automated bulk formatting.

### 10.2 Usage

```bash
# Format a single file in-place
akmlsql-format.exe --file "path/to/query.sql"

# Format a single file with a specific profile
akmlsql-format.exe --file "path/to/query.sql" --profile "OurTeamStandard"

# Format all .sql files in a directory (recursive)
akmlsql-format.exe --directory "path/to/scripts/" --recursive

# Check formatting without modifying files (CI validation mode)
akmlsql-format.exe --directory "path/to/scripts/" --check
# Exit code: 0 = all files formatted correctly, 1 = formatting violations found

# Output to stdout instead of modifying file
akmlsql-format.exe --file "query.sql" --stdout

# Format from stdin (pipe mode)
cat query.sql | akmlsql-format.exe --stdin --profile "Compact"

# Diff mode — show what would change
akmlsql-format.exe --file "query.sql" --diff

# Use a specific profile file (not installed)
akmlsql-format.exe --file "query.sql" --profile-file "path/to/custom.akmlstyle"

# List available profiles
akmlsql-format.exe --list-profiles

# Bulk format with report
akmlsql-format.exe --directory "path/to/scripts/" --recursive --report "report.json"
```

### 10.3 Git Pre-Commit Hook Integration

```bash
#!/bin/sh
# .git/hooks/pre-commit
# Validate SQL formatting before commit

SQL_FILES=$(git diff --cached --name-only --diff-filter=d | grep '\.sql$')
if [ -n "$SQL_FILES" ]; then
    akmlsql-format.exe --check $SQL_FILES
    if [ $? -ne 0 ]; then
        echo "ERROR: SQL files are not formatted. Run 'akmlsql-format.exe' to fix."
        exit 1
    fi
fi
```

### 10.4 CI/CD Pipeline Integration

```yaml
# Azure DevOps pipeline step
- task: CmdLine@2
  displayName: 'Validate SQL formatting'
  inputs:
    script: |
      akmlsql-format.exe --directory "$(Build.SourcesDirectory)/sql" --recursive --check --report "$(Build.ArtifactStagingDirectory)/format-report.json"
```

### 10.5 CLI Exit Codes

| Code | Meaning |
|---|---|
| `0` | Success — all files formatted (or already formatted in check mode) |
| `1` | Formatting violations found (check mode) |
| `2` | Parse error — one or more files could not be parsed |
| `3` | File not found or permission denied |
| `4` | Invalid profile or profile not found |
| `5` | Internal error |

---

## 11. Format Preview UI

### 11.1 Profile Editor with Live Preview

The profile editor is a split-pane dialog:

```
┌────────────────────────────────────────────────────────────────────┐
│  Edit Formatting Profile: "Our Team Standard"                [X]  │
├────────────────────┬───────────────────────────────────────────────┤
│  ▼ Whitespace      │  BEFORE (original)          AFTER (preview)  │
│    Tab style       │  ┌─────────────────┐  ┌─────────────────────┐│
│    Tab size        │  │ SELECT          │  │ SELECT              ││
│    Max line width  │  │  o.OrderID,     │  │     o.OrderID,      ││
│  ▼ Casing          │  │  o.OrderDate    │  │     o.OrderDate     ││
│    Keywords        │  │ FROM Orders o   │  │ FROM dbo.Orders o   ││
│    Functions       │  │ where Status=1  │  │ WHERE Status = 1    ││
│  ▼ Lists           │  └─────────────────┘  └─────────────────────┘│
│    Comma position  │                                               │
│  ▼ Parentheses     │  ─── Your Code Preview ───                   │
│  ▼ DML             │  ┌───────────────────────────────────────────┐│
│  ▼ JOINs           │  │ (live preview of your last 50 lines      ││
│  ▼ DDL             │  │  of active editor formatted with          ││
│  ▼ Control Flow    │  │  current profile settings)                ││
│  ▼ CASE & CTEs     │  └───────────────────────────────────────────┘│
│  ▼ Expressions     │                                               │
│  ▼ Format Actions  │  [Reset Category]  [Reset All]  [Compare...] │
├────────────────────┴───────────────────────────────────────────────┤
│                   [Cancel]    [Save]    [Save & Apply]             │
└────────────────────────────────────────────────────────────────────┘
```

### 11.2 Key UI Features

| Feature | Description |
|---|---|
| **Live dual preview** | Left pane shows unformatted SQL, right pane shows result with current settings. Updates in real-time as options change. |
| **Your Code preview** | Bottom pane shows the last 50 lines of your active editor document formatted with current settings — so you see the impact on your actual code, not just sample SQL. |
| **Category collapse** | Left sidebar collapses/expands option categories. Modified options highlighted with a dot indicator. |
| **Reset per category** | Reset a single category to the base profile's defaults without affecting other categories. |
| **Compare button** | Opens side-by-side diff of the current profile against any other profile, showing all differing options. |
| **Search options** | Search bar at the top of the left sidebar to find options by name (e.g., typing "comma" highlights `lists.commaPosition`). |
| **Undo/Redo** | Full undo/redo history within the profile editor session. |

---

## 12. Bulk Formatting

### 12.1 Bulk Format Wizard (IDE)

Accessible via AKML SQL menu → Bulk Format Files:

```
┌────────────────────────────────────────────────────────────┐
│  Bulk Format SQL Files                                [X]  │
├────────────────────────────────────────────────────────────┤
│                                                            │
│  Source:                                                   │
│  ○ Current Solution / Database Project                     │
│  ● Directory: [C:\Projects\Database\Scripts] [Browse...]   │
│  ○ File list: [path/to/filelist.txt]       [Browse...]     │
│                                                            │
│  ☑ Include subdirectories                                  │
│  File pattern: [*.sql]                                     │
│                                                            │
│  Profile: [Our Team Standard ▼]                            │
│                                                            │
│  Actions:                                                  │
│  ○ Format in-place (modify files)                          │
│  ● Preview changes (generate report only)                  │
│  ○ Format to output directory: [...] [Browse...]           │
│                                                            │
│  ☑ Create backup of original files (.bak)                  │
│  ☑ Skip files with parse errors                            │
│  ☑ Respect noformat regions                                │
│                                                            │
│                         [Cancel]  [Start]                  │
└────────────────────────────────────────────────────────────┘
```

### 12.2 Bulk Format Report

After bulk formatting, a JSON report is generated:

```json
{
  "timestamp": "2026-07-15T14:30:00Z",
  "profile": "Our Team Standard",
  "totalFiles": 347,
  "formatted": 312,
  "alreadyFormatted": 28,
  "parseErrors": 5,
  "skippedNoformat": 2,
  "totalLinesChanged": 8421,
  "elapsedMs": 4230,
  "details": [
    { "file": "dbo.GetOrders.sql", "status": "formatted", "linesChanged": 42 },
    { "file": "dbo.Legacy_Proc.sql", "status": "parseError", "error": "Unexpected token at line 87" }
  ]
}
```

---

## 13. Communication Protocol Extensions

The following messages extend the Phase 2 named pipe protocol for formatter operations:

| Message | Direction | Purpose |
|---|---|---|
| `FormatRequest` | Shell → Engine | Format SQL text with specified profile and actions |
| `FormatResult` | Engine → Shell | Formatted text, or original text with error if formatting failed |
| `FormatPreviewRequest` | Shell → Engine | Request live preview during profile editing |
| `FormatPreviewResult` | Engine → Shell | Preview of formatted text |
| `ProfileListRequest` | Shell → Engine | List all available profiles |
| `ProfileListResult` | Engine → Shell | Array of profile metadata |
| `BulkFormatRequest` | Shell → Engine | Bulk format with file list and profile |
| `BulkFormatProgress` | Engine → Shell | Progress updates during bulk format |
| `BulkFormatResult` | Engine → Shell | Final report |

---

## 14. Configuration & Options

All formatter settings accessible via AKML SQL → Options → Formatting.

### 14.1 General Formatter Settings

| Setting | Default | Description |
|---|---|---|
| `formatter.enabled` | `true` | Master switch for the formatter |
| `formatter.activeProfile` | `"Default"` | Active formatting profile name |
| `formatter.formatOnPaste` | `false` | Auto-format SQL pasted from clipboard |
| `formatter.formatOnSave` | `false` | Auto-format when saving .sql files |
| `formatter.formatOnDelimiter` | `false` | Auto-format statement when `;` or `GO` is typed |
| `formatter.shortcutKey` | `Ctrl+K, Y` | Primary keyboard shortcut for Format SQL |
| `formatter.showProfileInStatusBar` | `true` | Show active profile name in status bar |
| `formatter.confirmBulkFormat` | `true` | Prompt for confirmation before bulk formatting |
| `formatter.createBackups` | `true` | Create .bak backups before bulk format modifies files |
| `formatter.respectNoformat` | `true` | Honor noformat comment tags |
| `formatter.handleParseErrors` | `bestEffort` | `bestEffort`: format what can be parsed. `skip`: return original if any error. |
| `formatter.semanticValidation` | `true` | Verify AST equivalence after formatting (safety net) |

---

## 15. Performance Requirements

| Metric | Target | Measurement |
|---|---|---|
| **Format 100-line script** | < 50ms | Time from Ctrl+K, Y to formatted text displayed |
| **Format 1,000-line script** | < 100ms | Same |
| **Format 10,000-line script** | < 500ms | Same |
| **Format 50,000-line script** | < 2 seconds | Same |
| **Bulk format 100 files** | < 10 seconds | Total time for 100 × 500-line files |
| **Profile switch** | < 50ms | Time to load and apply a new profile |
| **Live preview update** | < 100ms | Time from option change to preview refresh |
| **CLI single file** | < 200ms | Time from invocation to formatted file written (including process startup) |
| **Memory overhead** | < 20MB | Additional memory for formatter beyond Phase 2 engine baseline |

---

## 16. Testing Requirements

### 16.1 Unit Tests

| Area | Test Count Target | Description |
|---|---|---|
| Whitespace rules | 60+ | Tab/space, indentation, line breaks, trailing whitespace, empty lines |
| Casing rules | 40+ | All casing modes for keywords, functions, types, identifiers, variables |
| List formatting | 50+ | Trailing/leading commas, alignment, collapse thresholds, one-per-line |
| Parentheses | 30+ | Open/close placement, collapse, subquery indentation |
| DML formatting | 80+ | SELECT, INSERT, UPDATE, DELETE, MERGE with all clause variations |
| JOIN formatting | 40+ | All JOIN types, ON conditions, multi-table, self-joins |
| DDL formatting | 60+ | CREATE TABLE/PROC/FUNC/VIEW/INDEX, ALTER, DROP |
| Control flow | 30+ | IF/ELSE, WHILE, TRY/CATCH, BEGIN/END nesting |
| CASE expressions | 25+ | Simple CASE, searched CASE, nested CASE, short collapse |
| CTE formatting | 20+ | Single CTE, multiple CTEs, recursive CTEs |
| Noformat regions | 15+ | Comment styles, nesting, unclosed regions, mixed with formatted code |
| Semantic preservation | 100+ | Verify AST equivalence for all formatted outputs |
| Profile management | 20+ | Create, edit, delete, import, export, compare, SQL Prompt import |
| CLI | 25+ | All CLI flags, exit codes, pipe mode, diff mode, check mode |
| Edge cases | 40+ | Empty files, comments-only, GO-only, deeply nested subqueries, 50K+ lines |

### 16.2 Integration Tests

| Test | Description |
|---|---|
| **End-to-end SSMS** | Format SQL in SSMS query window, verify output matches expected profile |
| **End-to-end VS** | Same in Visual Studio with SSDT project |
| **Format Selection** | Format partial selection within a larger script |
| **Format on Paste** | Paste unformatted SQL, verify auto-format applies |
| **Bulk format** | Format a directory of 100+ SQL files, verify all outputs |
| **CLI pipeline** | Run CLI in check mode in simulated CI pipeline |
| **Git pre-commit hook** | Simulate commit with unformatted SQL, verify hook rejects |
| **Profile import** | Import SQL Prompt .sqlpromptstyle file, verify conversion accuracy |
| **Profile quick switch** | Switch between 3 profiles rapidly, verify each applies correctly |
| **Large scripts** | Format AdventureWorks full DDL (30,000+ lines) |

### 16.3 Performance Benchmarks

| Test | Target | Method |
|---|---|---|
| Format latency (100 lines) | < 50ms | Automated, 1000 iterations, measure p95 |
| Format latency (10K lines) | < 500ms | Same |
| Bulk format throughput | > 50 files/sec | 500-line files, measure total time |
| CLI startup overhead | < 150ms | Cold start, measure to first output byte |
| Profile switch latency | < 50ms | Measure UI responsiveness on profile change |

---

## 17. SQL Server Version & Syntax Coverage

| Syntax Category | Examples | Coverage |
|---|---|---|
| **DML core** | SELECT, INSERT, UPDATE, DELETE, MERGE, TRUNCATE | Full |
| **DDL core** | CREATE/ALTER/DROP TABLE, VIEW, PROC, FUNCTION, INDEX, TRIGGER, SCHEMA | Full |
| **Joins** | INNER, LEFT/RIGHT/FULL OUTER, CROSS, CROSS APPLY, OUTER APPLY | Full |
| **CTEs** | WITH … AS, recursive CTEs | Full |
| **Window functions** | OVER (PARTITION BY … ORDER BY … ROWS/RANGE) | Full |
| **JSON (2016+)** | JSON_VALUE, JSON_QUERY, JSON_MODIFY, OPENJSON, FOR JSON | Full |
| **JSON (2022+)** | JSON_OBJECT, JSON_ARRAY, JSON_PATH_EXISTS | Full |
| **Temporal tables** | FOR SYSTEM_TIME, SYSTEM_VERSIONING | Full |
| **Graph tables (2017+)** | MATCH, SHORTEST_PATH, LAST_NODE | Full |
| **STRING_AGG (2017+)** | STRING_AGG with WITHIN GROUP | Full |
| **GENERATE_SERIES (2022+)** | GENERATE_SERIES function | Full |
| **GREATEST/LEAST (2022+)** | GREATEST, LEAST functions | Full |
| **Ledger tables (2022+)** | LEDGER = ON, ENABLE_LEDGER_VIEW | Full |
| **SQL Server 2025** | Vector types, DiskANN index, APPROX functions | Full |
| **Azure SQL** | CREATE EXTERNAL TABLE, elastic queries | Full |
| **Microsoft Fabric** | SQL endpoint syntax, Warehouse queries | Full |
| **SQLCMD mode** | `:setvar`, `:connect`, `$(variable)` — preserved as-is | Passthrough |
| **Comments** | `--`, `/* */`, nested comments — position preserved | Full |

---

## 18. Acceptance Criteria

1. **One-click format:** Ctrl+K, Y formats the entire active document in < 200ms for typical scripts
2. **Selection format:** Highlighting a fragment and formatting only reformats that fragment
3. **All 5 built-in profiles** produce visually distinct, correct output for all test queries
4. **Custom profiles** can be created, edited, duplicated, exported, imported, and deleted
5. **Profile quick-switch** via toolbar dropdown changes active profile and status bar indicator
6. **250+ options** are configurable in the profile editor with live preview
7. **Casing rules** correctly transform keyword/function/datatype case across all SQL constructs
8. **Database identifier sync** matches identifier casing to the database catalog when connected
9. **CamelCase dictionary** correctly splits compound identifiers (e.g., `customerorderid` → `CustomerOrderId`)
10. **Noformat regions** preserve original text exactly, including whitespace and casing
11. **Semantic preservation:** Formatted output parses to an identical AST as the input (100% of test cases)
12. **Bulk format** processes 100+ files without errors or data loss
13. **CLI formatter** passes all exit code tests and integrates with Git pre-commit hooks
14. **SQL Prompt .sqlpromptstyle import** maps 90%+ of options correctly
15. **Format on paste** correctly detects and formats pasted SQL content
16. **Performance:** All benchmarks met (50ms for 100 lines, 500ms for 10K lines)
17. **No crashes:** Formatter failure returns original text unchanged; never crashes the IDE
18. **Theming:** Profile editor and preview pane follow IDE theme (Light/Dark)

---

## 19. Timeline & Milestones

| Week | Milestone | Deliverable |
|---|---|---|
| 1–2 | Formatting Engine Core | Layout engine, text emitter, whitespace rules, indentation, line breaks. Basic end-to-end: input SQL → formatted SQL. |
| 3–4 | Casing & Lists | All casing rules (keywords, functions, datatypes, identifiers, DB sync). List alignment, comma position, collapse thresholds. |
| 5–6 | DML & JOIN Formatting | SELECT/INSERT/UPDATE/DELETE/MERGE formatting. JOIN/ON clause layout. All DML options. |
| 7–8 | DDL, Control Flow, CASE, CTE | CREATE/ALTER/DROP formatting. IF/ELSE/WHILE/TRY-CATCH. CASE expression layout. CTE formatting. |
| 9 | Noformat Regions & Actions | Noformat tag parser. Standalone actions (expand wildcards, qualify names, insert semicolons, etc.). |
| 10 | Profile Management & Import | Profile CRUD, export/import, compare. SQL Prompt .sqlpromptstyle import. Predefined profiles finalization. |
| 11 | Profile Editor UI & Live Preview | Split-pane editor dialog, live dual preview, option search, undo/redo. |
| 12 | CLI Formatter | Standalone executable, all CLI modes (file, directory, check, diff, pipe, report). Git hook template. |
| 13 | Bulk Format & Format-on-Paste/Save | Bulk format wizard UI, backup creation, reporting. Auto-format triggers (paste, save, delimiter). |
| 14 | QA & Performance | Full test matrix, performance benchmarks, edge cases, bug fixes, v3.0.0 release. |

**Total estimated duration: 14 weeks** (3.5 months). This phase benefits significantly from Phase 2's T-SQL parser and named pipe infrastructure.

---

## 20. Risks & Mitigations

| Risk | Impact | Mitigation |
|---|---|---|
| ScriptDom cannot round-trip all T-SQL constructs | Formatted output drops comments or changes semantics | Build custom AST annotator that preserves comments, whitespace, and noformat regions independent of ScriptDom's AST |
| 250+ options create combinatorial explosion for testing | Untested option combinations produce broken output | Use property-based testing (FsCheck/QuickCheck) with randomized option combinations + AST equivalence assertion on every test |
| SQL Prompt profile import inaccuracy | Users migrating from SQL Prompt disappointed | Build a detailed option mapping table; document known unmappable options; allow manual override during import |
| Format-on-paste interferes with non-SQL content | User pastes C# code and it gets mangled | Detect SQL content using a lightweight pre-check (look for SQL keywords in first 200 chars); offer manual trigger instead of auto |
| CLI formatter too slow for large projects | CI/CD pipeline timeouts | Parallelize bulk formatting (one file per CPU core); implement incremental check (skip unchanged files via hash) |
| Database identifier sync slow on large databases | Casing sync adds latency to formatting | Use Phase 2's cached schema metadata (already loaded); never query the database during formatting |
| Conflicting keyboard shortcuts with other extensions | Ctrl+K, Y collides with another extension's shortcut | Make all shortcuts configurable; detect conflicts on first load and suggest alternatives |

---

## 21. Dependencies

| Dependency | Version | Purpose |
|---|---|---|
| Microsoft.SqlServer.TransactSql.ScriptDom | Latest NuGet | T-SQL parsing and AST (shared with Phase 2) |
| MessagePack-CSharp | 2.x | Named pipe message serialization (shared with Phase 2) |
| System.Text.Json | 8.x | Profile JSON serialization/deserialization |
| DiffPlex | Latest | Text diff for profile comparison and CLI diff mode |
| Microsoft.VisualStudio.SDK | Per-SSMS-version | Editor text buffer manipulation |
| Serilog | 4.x | Structured logging (shared with Phase 1/2) |

---

## 22. Competitive Comparison

| Feature | SSMS Native | SQL Prompt | dbForge Complete | AKML SQL Phase 3 |
|---|---|---|---|---|
| One-click format | No | ✔ Ctrl+K, Y | ✔ Ctrl+K, D | ✔ Ctrl+K, Y |
| Formatting options count | ~10 | ~200 | ~150 | **250+** |
| Predefined profiles | 0 | 3 (Redgate styles) | 3 (Default, Profile 1, Profile 2) | **5** (Default, Compact, Expanded, Leading Commas, Minimalist) |
| Custom profiles | No | Unlimited | Unlimited | Unlimited |
| Team profile sharing | No | ✔ (Redgate Platform, TBE only) | ✔ (file export) | ✔ (export/import + AKML Platform future) |
| Profile comparison | No | ✔ (via PowerShell script) | ✔ | **✔ (built-in visual diff)** |
| Format on paste | No | No | ✔ | **✔** |
| Format on save | No | No | No | **✔** |
| Format on delimiter | No | ✔ | ✔ | **✔** |
| Noformat regions | No | ✔ | ✔ | **✔** (block + line comment syntax) |
| Bulk file formatting | No | ✔ (TBE only) | ✔ (wizard + CLI) | **✔** (wizard + CLI + report) |
| Command-line formatter | No | ✔ | ✔ | **✔** (with check mode, diff mode, pipe mode) |
| CI/CD integration | No | ✔ (basic) | ✔ (basic) | **✔** (check mode + exit codes + Git hook template + report) |
| Live preview in editor | No | ✔ | Limited | **✔** (dual preview + your-code preview) |
| Database identifier case sync | No | ✔ | ✔ | **✔** |
| CamelCase dictionary | No | No | ✔ | **✔** |
| SQL Prompt profile import | N/A | N/A | No | **✔** |
| SQL Server 2025 syntax | N/A | ✔ | ✔ | **✔** |
| Microsoft Fabric syntax | N/A | ✔ | No | **✔** |
| Semantic validation | No | No | No | **✔** (AST equivalence check — unique differentiator) |
| Format selection only | No | ✔ | ✔ | **✔** |
| Leading commas profile | No | ✔ (configurable) | ✔ (configurable) | **✔** (dedicated built-in profile) |
| Standalone casing action | No | ✔ | No | **✔** |
| Expand wildcards action | No | ✔ (via IntelliSense) | ✔ | **✔** (standalone + in Format SQL) |
| Qualify object names action | No | ✔ | ✔ | **✔** (standalone + in Format SQL) |
| Insert semicolons action | No | ✔ | ✔ | **✔** (standalone + in Format SQL) |

---

## 23. Success Metrics

- **Formatting accuracy:** 100% of formatted outputs parse to semantically identical ASTs
- **Option coverage:** 250+ individually configurable formatting options across all 8 categories
- **Performance:** < 100ms for 1,000-line scripts, < 500ms for 10,000-line scripts
- **Profile migration:** > 90% of SQL Prompt formatting options successfully imported
- **User satisfaction:** > 90% of beta testers rate formatting output as "equal or better than SQL Prompt"
- **Adoption:** > 95% of AKML SQL users use the formatter at least once per session
- **CLI adoption:** > 30% of teams integrate CLI formatter into their CI/CD pipeline within 3 months
- **Zero semantic regressions:** Zero reported cases where formatting changed the meaning of SQL code
- **Phase 4 readiness:** Snippet Manager (Phase 4) can embed formatting-aware snippet expansion without modifications

---

*End of Phase 3 PRD — AKML SQL v1.0*
