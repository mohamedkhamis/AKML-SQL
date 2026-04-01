# SQL Prompt — Complete Feature Reference (Core Features)

> **Purpose:** Design reference for AKML SQL — every feature, UI element, color, setting, and formatting option from Redgate SQL Prompt, described in full detail.
>
> **Scope:** All features EXCEPT AI (see separate AI document)

---

## Table of Contents

1. [Code Completion & IntelliSense](#1-code-completion--intellisense)
2. [Code Formatting & Styles](#2-code-formatting--styles)
3. [Code Snippets](#3-code-snippets)
4. [Code Analysis (Static Analysis)](#4-code-analysis-static-analysis)
5. [Tab Management — Coloring & History](#5-tab-management--coloring--history)
6. [Code Refactoring](#6-code-refactoring)
7. [Navigation & Productivity](#7-navigation--productivity)
8. [Results Grid Enhancements](#8-results-grid-enhancements)
9. [Complete Settings Reference](#9-complete-settings-reference)
10. [Complete Color & Theme System](#10-complete-color--theme-system)
11. [Keyboard Shortcuts Reference](#11-keyboard-shortcuts-reference)

---

## 1. Code Completion & IntelliSense

The core engine of SQL Prompt. Provides context-aware, ranked autocomplete suggestions as the user types SQL inside SSMS or Visual Studio query editors.

![SQL Prompt Code Completion Popup](./images/01_suggestion_popup.svg)

### 1.1 Suggestion Popup (Candidate List)

The suggestion popup appears automatically as you type (or manually via `Ctrl+Space`). It shows a scrollable list of contextually relevant items.

**UI Design Spec:**

| Element | Design Detail |
|---------|---------------|
| **Container** | Floating popup, dark background (`#252836`), 1px solid border (`#3A3F4E`), 8px border-radius, soft drop-shadow `0 8px 24px rgba(0,0,0,0.4)` |
| **Width** | Auto-sized to content, min ~220px, max ~400px |
| **Item height** | ~28px per row |
| **Selected item** | Blue highlight background `rgba(79,140,255,0.15)`, white text |
| **Unselected item** | Gray text `#8892A8` on transparent background |
| **Font** | Monospaced (matches editor font), ~12px size |
| **Scrollbar** | Thin, dark themed, appears when list exceeds visible area |
| **Transparency** | Hold `Ctrl` key to make the popup semi-transparent so code behind is readable |
| **Dismiss** | Press `Esc` or `Ctrl` to dismiss |

**Ranking logic:** Items are ranked by contextual relevance:
- After `FROM` → Tables and Views appear first
- After `SELECT` → Columns from already-referenced tables rank highest
- After `JOIN` → Tables with FK relationships to existing tables rank highest
- After `EXEC` → Stored procedures appear first
- After a dot (`.`) → Members of the preceding schema/table/alias

### 1.2 Suggestion Icon Types & Colors

Each suggestion item has a small colored icon badge (18×18px, 4px border-radius) indicating the object type.

| Icon Letter | Object Type | Background Color | Text Color | Hex |
|:-----------:|-------------|-----------------|------------|-----|
| **T** | Table | `rgba(229,192,75,0.20)` | `#E5C04B` | Yellow |
| **V** | View | `rgba(86,182,194,0.20)` | `#56B6C2` | Teal |
| **C** | Column | `rgba(97,175,239,0.20)` | `#61AFEF` | Blue |
| **P** | Stored Procedure | `rgba(198,120,221,0.20)` | `#C678DD` | Purple |
| **F** | Function (Scalar/Table) | `rgba(209,154,102,0.20)` | `#D19A66` | Orange |
| **S** | Snippet | `rgba(61,214,140,0.20)` | `#3DD68C` | Green |
| **K** | Keyword | `rgba(171,178,191,0.15)` | `#ABB2BF` | Gray |
| **D** | Database | `rgba(224,108,117,0.20)` | `#E06C75` | Red |
| **Sc** | Schema | `rgba(152,195,121,0.20)` | `#98C379` | Green |
| **Tr** | Trigger | `rgba(190,80,70,0.20)` | `#BE5046` | Dark Red |
| **Ix** | Index | `rgba(171,178,191,0.10)` | `#7F848E` | Dim Gray |
| **Sy** | Synonym | `rgba(86,182,194,0.15)` | `#56B6C2` | Teal |

### 1.3 Object Definition Box

A secondary popup that appears to the right of the suggestion popup when an item is highlighted.

**UI Design Spec:**

| Element | Detail |
|---------|--------|
| **Container** | Separate floating panel, same dark theme, ~300px wide |
| **Tabs** | Two tabs: **Summary** (default) and **Script** |
| **Summary tab** | Shows: Column names, Data types, Nullable (✓/✗), Key icons (🔑 PK, 🔗 FK, ◆ UQ), Estimated row count |
| **Script tab** | Full CREATE statement for the object, syntax highlighted |
| **Toggle** | Click the Script tab once → it becomes default view going forward |
| **Font** | Monospaced, same as editor |

### 1.4 Column Picker

When the cursor is on `*` (asterisk) in a `SELECT` statement, pressing `Tab` or `Ctrl+Left Arrow` opens the Column Picker.

![Column Picker and Snippet Manager](./images/08_column_picker_snippets.svg)

**UI Design Spec:**

| Element | Detail |
|---------|--------|
| **Container** | Modal popup, checklist of all columns in referenced table(s) |
| **Each row** | Checkbox + Column Name + Data Type + Key icon |
| **Select All** | Toggle button at top — selects all or clears all |
| **Sort order** | Configurable: Alphabetical (default) or Table-defined order |
| **Multi-table** | When multiple tables referenced, columns grouped by table (in table order mode) |
| **Output** | Selected columns inserted at cursor, formatted per active style settings |
| **Keyboard** | Space to toggle checkbox, Enter to confirm, Esc to cancel |

### 1.5 JOIN Condition Completion

When you type `JOIN`, SQL Prompt:
1. Suggests matching tables (ranked by FK relationships to existing tables)
2. After selecting a table, auto-generates the `ON` condition based on:
   - Foreign key relationships (primary method)
   - Matching column names (fallback when no FK exists)
3. The entire `JOIN dbo.Table t ON t.ID = o.TableID` is offered as a single suggestion

### 1.6 Keyword Auto-Casing

As you type, SQL keywords are automatically re-cased to match your formatting style settings.

**Options (set in Format → Casing):**

| Setting | Values | Default |
|---------|--------|---------|
| Reserved Keywords | `UPPER CASE`, `lower case`, `Title Case`, `Leave as is` | `UPPER CASE` |
| Built-in Functions | `UPPER CASE`, `lower case`, `Title Case`, `Leave as is` | `UPPER CASE` |
| Built-in Data Types | `UPPER CASE`, `lower case`, `Title Case`, `Leave as is` | `lower case` |
| Global Variables | `UPPER CASE`, `lower case`, `Title Case`, `Leave as is` | `UPPER CASE` |
| System Stored Procs | `UPPER CASE`, `lower case`, `Title Case`, `Leave as is` | `Leave as is` |

**Behavior:** If you type `select`, it is replaced with `SELECT` immediately. If you type `sel` and accept from the suggestion list, `SELECT` is inserted using the configured case.

### 1.7 Schema Qualification on Insert

When a table/view/proc is inserted from the suggestion list, SQL Prompt can auto-qualify with the schema name.

**Options:**

| Setting | Behavior |
|---------|----------|
| Always qualify | Every object gets `dbo.TableName` |
| Only non-default schemas | Only qualifies if schema ≠ `dbo` |
| Never qualify | Inserts bare `TableName` |

### 1.8 INSERT Statement Completion

After typing `INSERT INTO TableName`, SQL Prompt auto-generates the full column list with metadata comments.

**Generated output example:**
```sql
INSERT INTO dbo.Products
(
    ProductName,    -- nvarchar(40), not null
    SupplierID,     -- int, null
    CategoryID,     -- int, null
    UnitPrice,      -- money, null, default ((0))
    UnitsInStock    -- smallint, null, default ((0))
)
VALUES
(
    $CURSOR$
)
```

**Configurable elements:**

| Setting | Type | Default |
|---------|------|---------|
| Include column names | Bool | ✅ On |
| Include data types as comments | Bool | ✅ On |
| Include default values | Bool | ✅ On |

### 1.9 CamelCase / Substring Filtering

As you type in the suggestion popup, the list is filtered:
- **Prefix match:** `Pro` matches `Products`, `Procedures`
- **CamelCase match:** `PC` matches `ProductCategory`, `PriceCalc`
- **Substring match:** `Cat` matches `ProductCategory`
- **Sensitive to:** typed characters, symbols, whitespaces, CamelCase patterns

### 1.10 Suggestion Refresh / Cache

SQL Prompt caches database schema metadata for performance.

| Setting | Type | Description |
|---------|------|-------------|
| Auto-refresh on connection | Bool | Refresh metadata when connecting to a new database |
| Auto-refresh on schema change | Bool | Detect DDL changes and refresh automatically |
| Refresh MS IntelliSense cache | Bool | Also refresh SSMS native IntelliSense when refreshing SQL Prompt |
| Manual refresh shortcut | — | `Ctrl+Shift+D` |

---

## 2. Code Formatting & Styles

One-shortcut code formatting (`Ctrl+K, Y`) that applies a comprehensive, deeply customizable style to SQL code. Styles are stored as `.sqlpromptstyle` files (JSON format) that can be shared across teams.

![Formatting Before/After](./images/04_formatting_before_after.svg)

### 2.1 Style Management

| Action | How |
|--------|-----|
| **Format SQL** | `Ctrl+K, Y` — formats entire file or selection |
| **Edit styles** | SQL Prompt menu → Edit formatting styles |
| **Create new style** | Click `+ Create a Style`, name it, base it on an existing style |
| **Copy style** | Vertical ellipsis `⋮` next to a style → Copy |
| **Export** | Save as `.sqlpromptstyle` file (JSON format) |
| **Import** | Load `.sqlpromptstyle` file from disk |
| **Share via Redgate Platform** | SQL Toolbelt Essentials only — sync styles to team spaces |
| **Built-in Redgate styles** | Read-only defaults provided by Redgate |
| **Preview pane** | Real-time preview of formatting on sample SQL code |

### 2.2 Style File Format (`.sqlpromptstyle`)

The style file is JSON. Here is the complete structure with every section and key:

```json
{
  "metadata": {
    "id": "GUID",
    "name": "My Custom Style"
  },
  "whitespace": {
    "newLines": {
      "preserveExistingEmptyLinesAfterBatchSeparator": false
    }
  },
  "lists": {
    "alignItemsAcrossClauses": false,
    "alignAliases": true,
    "placeCommasBeforeItems": false,
    "addSpaceAfterComma": true
  },
  "parentheses": {
    "collapseShortParenthesisContents": true,
    "collapseParenthesesShorterThan": 35
  },
  "casing": {
    "reservedKeywords": "uppercase",
    "builtInFunctions": "uppercase",
    "builtInDataTypes": "lowercase",
    "globalVariables": "uppercase",
    "useObjectDefinitionCase": true
  },
  "dml": {
    "collapseShortStatements": true,
    "collapseStatementsShorterThan": 35,
    "collapseShortSubqueries": true,
    "collapseSubqueriesShorterThan": 78
  },
  "ddl": {
    "alignDataTypesAndConstraints": true,
    "placeFirstProcedureParameterOnNewLine": "never",
    "collapseShortStatements": true,
    "collapseStatementsShorterThan": 55
  },
  "controlFlow": {
    "collapseStatementsShorterThan": 78
  },
  "cte": {
    "placeColumnsOnNewLine": "always"
  },
  "joins": {
    "joinKeywordAlignment": "toTable",
    "placeOnConditionOnNewLine": true
  },
  "caseExpressions": {
    "placeFirstWhenOnNewLine": "ifInputExpression",
    "whenAlignment": "toFirstItem",
    "placeThenOnNewLine": true,
    "placeExpressionOnNewLine": false
  },
  "operators": {
    "alignment": "indentedFromStatement",
    "placeBetweenKeywordOnNewLine": false
  },
  "inStatements": {
    "alignment": "indentedFromStatement"
  }
}
```

### 2.3 Formatting Settings — Complete Detailed Reference

#### 2.3.1 GLOBAL OPTIONS

These affect the entire SQL document.

##### Whitespace

| Setting | Type | Values / Range | Default | Description |
|---------|------|---------------|---------|-------------|
| Number of spaces in tabs | `Number` | 1–8 | **4** | How many space characters per indentation level |
| Tab behavior | `Enum` | `Spaces only`, `Tabs only`, `Tabs where possible` | **Spaces only** | Tabs where possible = tabs for indentation, spaces for alignment |
| Wrap column | `Number` | 40–200 | **80** | Line width at which wrapping occurs |
| Blank lines between statements | `Number` | 0–5 | **1** | Empty lines inserted between SQL statements |
| Blank lines before GO | `Number` | 0–5 | **1** | Empty lines before batch separator |
| Preserve existing empty lines after batch separator | `Bool` | — | **Off** | Keep user's own blank lines intact |

##### Lists

| Setting | Type | Values | Default | Description |
|---------|------|--------|---------|-------------|
| Comma placement | `Enum` | `Trailing` (end of line), `Leading` (start of new line) | **Trailing** | Where commas go in column lists |
| Add space after comma | `Bool` | — | **On** | Insert a space character after each comma |
| Align items across clauses | `Bool` | — | **Off** | Vertically align SELECT, FROM, WHERE contents |
| Align aliases | `Bool` | — | **Off** | Vertically align AS aliases |
| Place subsequent items on new lines | `Enum` | `Always`, `Never`, `If longer than wrap column` | **Always** | When to break column lists onto new lines |

##### Parentheses

| Setting | Type | Values | Default | Description |
|---------|------|--------|---------|-------------|
| Place opening parenthesis on new line | `Bool` | — | **Off** | Opening `(` on same line or new line |
| Place closing parenthesis on new line | `Bool` | — | **Off** | Closing `)` on same line or new line |
| Indent parenthesis contents | `Bool` | — | **On** | Indent content between `()` |
| Collapse short parenthesis contents | `Bool` | — | **On** | Keep short expressions on one line |
| Collapse shorter than | `Number` | 20–120 chars | **35** | Threshold for collapsing |
| Add spaces around parentheses | `Bool` | — | **Off** | Space before `(` and after `)` |

##### Casing

| Setting | Type | Values | Default |
|---------|------|--------|---------|
| Reserved keywords | `Enum` | `UPPER CASE`, `lower case`, `Title Case`, `Leave as is` | **UPPER CASE** |
| Built-in functions | `Enum` | same as above | **UPPER CASE** |
| Built-in data types | `Enum` | same as above | **lower case** |
| Global variables | `Enum` | same as above | **UPPER CASE** |
| Use object definition case | `Bool` | — | **On** |

#### 2.3.2 STATEMENTS (DML)

Controls the layout of SELECT, INSERT, UPDATE, DELETE, MERGE.

| Setting | Type | Values | Default | Description |
|---------|------|--------|---------|-------------|
| Collapse short statements | `Bool` | — | **On** | Keep short queries on one line |
| Collapse statements shorter than | `Number` | 20–120 | **35** | Threshold |
| Collapse short subqueries | `Bool` | — | **On** | Keep short subqueries inline |
| Collapse subqueries shorter than | `Number` | 20–200 | **78** | Threshold |
| Place clauses on new line | `Bool` | — | **On** | Each clause (SELECT, FROM, WHERE) starts a new line |
| Right-align clauses | `Bool` | — | **Off** | Right-justify clause keywords |
| Clause indentation | `Enum` | `None`, `Indented`, `Right-aligned to statement` | **None** | How sub-clauses are indented |
| Place INTO on new line (SELECT INTO) | `Bool` | — | **On** | Break after SELECT for INTO |
| Place SET on new line (UPDATE) | `Bool` | — | **On** | Each SET assignment on new line |
| INSERT column list format | `Enum` | `One per line`, `Compact`, `If longer than wrap` | **One per line** | How INSERT column lists are laid out |
| VALUES format | `Enum` | same | **One per line** | How VALUES lists are laid out |

#### 2.3.3 STATEMENTS (DDL)

Controls CREATE/ALTER TABLE, PROCEDURE, FUNCTION, VIEW, TRIGGER, INDEX.

| Setting | Type | Values | Default | Description |
|---------|------|--------|---------|-------------|
| Align data types and constraints | `Bool` | — | **Off** | Vertically align data types in CREATE TABLE |
| Place first procedure parameter on new line | `Enum` | `Always`, `Never`, `If longer than wrap` | **Never** | First parameter placement |
| Place constraints on new lines | `Bool` | — | **Off** | PRIMARY KEY, NOT NULL etc. on own line |
| Place constraint columns on new lines | `Enum` | `Always`, `Never`, `If longer or multiple columns` | **If longer or multiple** | Column lists inside constraints |
| Collapse short DDL statements | `Bool` | — | **On** | Keep short DDL on one line |
| Collapse DDL shorter than | `Number` | 20–120 | **55** | Threshold |

#### 2.3.4 JOINs

| Setting | Type | Values | Default | Description |
|---------|------|--------|---------|-------------|
| Join keyword alignment | `Enum` | `To table`, `To FROM`, `Indented from FROM`, `Right-aligned` | **To table** | How JOIN keyword aligns relative to the FROM clause |
| Place ON condition on new line | `Bool` | — | **On** | ON clause on separate line |
| ON condition indentation | `Enum` | `Indented from JOIN`, `To table`, `Indented from table` | **Indented from JOIN** | How the ON clause is indented |
| Insert empty line before JOIN | `Bool` | — | **Off** | Blank line between each JOIN |
| Place AND/OR in ON on new line | `Bool` | — | **On** | Multiple ON conditions broken |

#### 2.3.5 CASE Expressions

| Setting | Type | Values | Default | Description |
|---------|------|--------|---------|-------------|
| Place first WHEN on new line | `Enum` | `Always`, `Never`, `If there is an input expression` | **If input expression** | When to break WHEN |
| WHEN alignment | `Enum` | `To CASE`, `To first item`, `Indented from CASE` | **To first item** | Alignment of WHEN clauses |
| Place THEN on new line | `Bool` | — | **On** | THEN on its own line |
| Place expression on new line | `Bool` | — | **Off** | Result expression on new line after THEN |
| ELSE on new line | `Bool` | — | **On** | ELSE clause placement |
| END alignment | `Enum` | `To CASE`, `Indented` | **To CASE** | Where END aligns |

#### 2.3.6 CTEs (Common Table Expressions)

| Setting | Type | Values | Default | Description |
|---------|------|--------|---------|-------------|
| Place columns on new line | `Enum` | `Always`, `Never`, `If longer` | **Always** | CTE column list line breaks |
| Place AS on new line | `Bool` | — | **Off** | AS keyword placement |
| Indent CTE body | `Bool` | — | **On** | Indent the SELECT inside CTE |

#### 2.3.7 Operators (AND/OR/+/-/etc.)

| Setting | Type | Values | Default | Description |
|---------|------|--------|---------|-------------|
| Alignment | `Enum` | `Indented from statement`, `To clause keyword`, `To first item` | **Indented from statement** | How AND/OR align in WHERE |
| Place BETWEEN on new line | `Bool` | — | **Off** | Break on BETWEEN |
| Place AND between BETWEEN on new line | `Bool` | — | **Off** | The AND in `BETWEEN x AND y` |

#### 2.3.8 Function Calls

| Setting | Type | Values | Default | Description |
|---------|------|--------|---------|-------------|
| Place parameters on new line | `Enum` | `Always`, `Never`, `If longer than wrap` | **If longer** | Parameter list line breaks |
| Indent function parameters | `Bool` | — | **On** | Indent params inside function call |

#### 2.3.9 IN Statements

| Setting | Type | Values | Default |
|---------|------|--------|---------|
| Alignment | `Enum` | `Indented from statement`, `To IN keyword` | **Indented from statement** |
| Place items on new line | `Enum` | `Always`, `Never`, `If longer` | **If longer** |

#### 2.3.10 Semicolons & Comments

| Setting | Type | Default | Description |
|---------|------|---------|-------------|
| Auto-insert semicolons | `Bool` | **Off** | Add missing semicolons to all statements |
| Multiline comment formatting | `Enum` | **Preserve** | How `/* */` comments are reformatted |
| Recognize common comment patterns | `Bool` | **On** | Detect header/block comments |

### 2.4 Formatting Actions (Menu Commands)

| Command | Shortcut | Description |
|---------|----------|-------------|
| Format SQL | `Ctrl+K, Y` | Apply active formatting style to selection or entire file |
| Apply Casing Options | `Ctrl+B, Ctrl+U` | Apply only casing (no layout changes) |
| Insert Semicolons | `Ctrl+B, Ctrl+C` | Add missing semicolons everywhere |
| Expand Wildcards | `Ctrl+B, Ctrl+W` | Replace `SELECT *` with column list |
| Qualify Object Names | `Ctrl+B, Ctrl+Q` | Add schema prefix to all objects |
| Unformat | Actions List | Remove all formatting whitespace |
| Disable formatting for selection | Actions List | Wrap in `-- SQL Prompt formatting off/on` comments |

### 2.5 Before / After Formatting Example

**Before (unformatted):**
```sql
select p.name,p.price,c.categoryname from products p inner join categories c on p.categoryid=c.id where p.price>10 and p.discontinued=0 order by p.name
```

**After (`Ctrl+K, Y` with default style):**
```sql
SELECT p.Name,
       p.Price,
       c.CategoryName
FROM dbo.Products p
INNER JOIN dbo.Categories c
    ON p.CategoryID = c.ID
WHERE p.Price > 10
    AND p.Discontinued = 0
ORDER BY p.Name;
```

---

## 3. Code Snippets

Pre-defined code templates inserted by typing a short abbreviation and pressing `Tab`. More powerful than SSMS templates — supports placeholder macros and selected text wrapping.

![Column Picker and Snippet Manager](./images/08_column_picker_snippets.svg)

### 3.1 Snippet Manager UI

| Element | Detail |
|---------|--------|
| **Access** | SQL Prompt menu → Snippet Manager |
| **Layout** | Left panel: snippet list with search. Right panel: snippet body editor |
| **Snippet fields** | Name, Abbreviation, Description, Body (with syntax highlighting) |
| **Actions** | New, Edit, Delete, Duplicate, Import, Export |
| **Storage** | Individual files in a configurable folder |

### 3.2 Placeholder Macros

| Macro | Description |
|-------|-------------|
| `$CURSOR$` | Final cursor position after snippet is inserted |
| `$SELECTEDTEXT$` | Currently selected text in the editor (for "surround with" snippets) |
| `$PASTE$` | Content of the system clipboard |
| `$DATE$` | Current date in server locale format |
| `$TIME$` | Current time |
| `$DBNAME$` | Name of the currently connected database |
| `<ParamName, type, default>` | SSMS template-style replacement parameter |

### 3.3 Built-in Snippets

| Abbreviation | Name | Expansion |
|:------------:|------|-----------|
| `ssf` | Select Star From | `SELECT * FROM $CURSOR$` |
| `ii` | Insert Into | Full INSERT INTO with column list, types, defaults |
| `w2` | Who2 | `sp_who2` (auto-executes) |
| `chk` | Check Stats | Execution plan + statistics harness |
| `eata` | Alter Table Add | ALTER TABLE with NULL/documentation standards |
| `timings` | Timing Harness | SET STATISTICS TIME/IO wrapper around `$SELECTEDTEXT$` |
| `tvc` | Table Variable Capture | Creates temp table from procedure results |
| `citf` | Create Inline TVF | Full inline table-valued function template |
| `csp` | Create Stored Procedure | Full procedure template with header |
| `b` | BEGIN/END | `BEGIN $SELECTEDTEXT$ END` |
| `tc` | TRY/CATCH | Full TRY/CATCH wrapper around `$SELECTEDTEXT$` |

### 3.4 Shared Snippets Settings

| Setting | Description |
|---------|-------------|
| Snippet folder path | Local folder where personal snippets are stored |
| Shared folder path | Network/Dropbox folder for team-shared snippets |
| Redgate Platform sync | (SQL Toolbelt Essentials only) Sync snippets to team spaces |
| Merge behavior | Shared snippets appear alongside personal snippets in the suggestion list |

---

## 4. Code Analysis (Static Analysis)

Real-time as-you-type scanning that flags code quality issues. Issues appear as colored wavy underlines with tooltip explanations. Over 94 rules across 7 categories.

![Code Analysis Underlines](./images/03_code_analysis.svg)

### 4.1 UI Indicators

| Indicator | Visual | Description |
|-----------|--------|-------------|
| **Warning underline** | Green wavy underline under the offending code | Standard issue indicator |
| **Error underline** | Red wavy underline | Severe issue or actual syntax error |
| **Tooltip** | Hover over underline → popup with rule code, description, and "Learn more" link | Quick explanation |
| **Lightbulb icon** | Yellow 💡 in the left margin gutter | Click for auto-fix options |
| **Issues List panel** | Dockable panel at bottom of SSMS | Full list of all issues, groupable by Rule or Location |
| **Issue count** | Status bar shows `3 warnings | 0 errors` | Running count |

### 4.2 Rule Categories with Colors

| Prefix | Category | Color | Hex | Purpose |
|:------:|----------|-------|-----|---------|
| **BP** | Best Practices | 🔵 Blue | `#4F8CFF` | General coding standards |
| **PE** | Performance | 🟠 Orange | `#FF9F43` | Code that may hurt performance |
| **DEP** | Deprecated | 🔴 Red | `#FF5C5C` | Syntax deprecated by Microsoft |
| **ST** | Style | 🟣 Purple | `#A78BFA` | Code style inconsistencies |
| **MI** | Miscellaneous | 🟡 Yellow | `#FBBF24` | Various potential issues |
| **EI** | Execution Issue | 🔵 Cyan | `#22D3EE` | Runtime behavior concerns |
| **SC** | Source Control | 🟢 Green | `#3DD68C` | File-level issues (EOL markers) |

### 4.3 Complete Rules Reference

#### Best Practices (BP)

| Rule | Description | Auto-fixable |
|------|-------------|:------------:|
| BP005 | Use of `SELECT *` in production code | ✅ |
| BP006 | `TOP` without `ORDER BY` | ✅ |
| BP007 | Table without clustered index | ✗ |
| BP008 | Use of `MONEY`/`SMALLMONEY` data type | ✗ |
| BP011 | `SET NOCOUNT` missing in stored procedure | ✗ |
| BP013 | `EXECUTE(string)` — SQL injection risk | ✗ |
| BP014 | Column without explicit `NULL`/`NOT NULL` | ✗ |
| BP015 | `ORDER BY` using ordinal position | ✗ |
| BP016 | Deterministic function used non-deterministically | ✗ |
| BP018 | `DELETE`/`UPDATE` without `WHERE` clause | ✗ |
| BP022 | Use of `INSERT INTO` without column list | ✗ |
| BP023 | `FLOAT`/`REAL` data type is used | ✅ |
| BP024 | Variable-length string without length | ✗ |

#### Performance (PE)

| Rule | Description | Auto-fixable |
|------|-------------|:------------:|
| PE001 | Schema name not specified for stored procedure | ✅ |
| PE002 | Schema name not specified for table/view | ✅ |
| PE003 | Table created by `SELECT INTO` | ✅ |
| PE006 | Table hint is used | ✅ |
| PE010 | Implicit column list in `INSERT` | ✗ |
| PE011 | `NOLOCK` used outside of read-only query | ✗ |
| PE012 | `SET` inside procedure may cause recompile | ✗ |
| PE016 | `ISNULL` used instead of `COALESCE` | ✗ |
| PE019 | Consider `EXISTS` instead of `IN` | ✗ |
| PE021 | Scalar UDF used as global constant | ✗ |
| PE023 | DDL without specifying schema name | ✅ |

#### Deprecated (DEP)

| Rule | Description | Auto-fixable |
|------|-------------|:------------:|
| DEP002 | Old-style JOIN syntax (`=`, `*=`, `=*`) | ✗ |
| DEP007 | Old-style `RAISERROR` format | ✗ |
| DEP009 | `SET ROWCOUNT` to limit results | ✗ |
| DEP012 | Old outer join syntax (`*=` or `=*`) | ✗ |
| DEP014 | `sp_addtype` / `sp_droptype` | ✗ |
| DEP019 | Deprecated system table used | ✗ |
| DEP021 | String literal as column alias | ✅ |
| DEP022 | Deprecated hint syntax | ✗ |
| DEP026 | `SET ANSI_PADDING OFF` | ✗ |

#### Style (ST)

| Rule | Description | Auto-fixable |
|------|-------------|:------------:|
| ST001 | Inconsistent use of alias definition | ✗ |
| ST002 | Old-style column alias via `=` sign | ✅ |
| ST003 | Procedure body not enclosed in `BEGIN`/`END` | ✅ |
| ST006 | Old-style `TOP` clause (no parentheses) | ✅ |
| ST007 | Cursor not explicitly deallocated | ✗ |
| ST008 | Non-named parameter style used | ✅ |
| ST010 | Missing alias on table source | ✅ |
| ST013 | Non-ANSI `!=` operator used (instead of `<>`) | ✅ |

#### Miscellaneous (MI)

| Rule | Description | Auto-fixable |
|------|-------------|:------------:|
| MI001 | `@@IDENTITY` used instead of `SCOPE_IDENTITY()` | ✗ |
| MI003 | Unqualified column name | ✅ |
| MI005 | Variable declared but never used | ✅ |
| MI006 | Unused parameter in procedure | ✗ |

#### Execution Issues (EI)

| Rule | Description | Auto-fixable |
|------|-------------|:------------:|
| EI003 | Non-scalar subquery used as scalar value | ✅ |
| EI024 | Possible null concatenation | ✗ |
| EI029 | Implicit data type conversion | ✗ |

#### Source Control (SC)

| Rule | Description | Auto-fixable |
|------|-------------|:------------:|
| SC005 | BOM (Byte Order Mark) detected | ✗ |
| SC006 | EOL marker is not expected CR/LF | ✅ |

### 4.4 Code Analysis Settings

| Setting | Type | Description |
|---------|------|-------------|
| Enable Code Analysis | `Bool` | Master toggle. Can be set separately for SSMS and Visual Studio |
| Per-rule severity | `Enum` | Each rule: `Ignore` / `Warning` / `Error` |
| Settings file (.casettings) | `XML File` | Stores all rule states. Shareable via file share or Redgate Platform |
| Save As | Button | Create multiple settings files (strict, lax, per-team) |
| Auto-fix popup | `Bool` | Show lightbulb for fixable issues |

**`.casettings` file format (XML):**
```xml
<?xml version="1.0" encoding="utf-8"?>
<CodeAnalysisSettings>
  <Rule id="BP005" level="Warning" />
  <Rule id="BP006" level="Error" />
  <Rule id="ST002" level="Ignore" />
  <!-- ... -->
</CodeAnalysisSettings>
```

---

## 5. Tab Management — Coloring & History

Safety-net features that prevent wrong-server mistakes and recover lost work.

![Tab Coloring](./images/02_tab_coloring.svg)

### 5.1 Tab Coloring

Color-codes each SSMS query tab based on the environment (Production, Development, etc.) of the connected server/database.

**Where color is applied:**
1. **Tab header bar** — 3px colored strip at the top of the tab
2. **Status bar** — Full-width colored bar at the bottom of the query pane
3. **Floating window border** — Colored outline on undocked/floating query windows
4. **Active vs inactive** — Active tab uses bright color; inactive tabs use darker shade

#### Default Environment Colors

| Environment | Color | Hex Code | RGB | Usage |
|-------------|-------|----------|-----|-------|
| **Production** | 🔴 Red | `#E74C3C` | `rgb(231,76,60)` | Live servers — maximum caution |
| **Staging** | 🟠 Orange | `#F39C12` | `rgb(243,156,18)` | Pre-production environments |
| **Testing** | 🔵 Blue | `#3498DB` | `rgb(52,152,219)` | QA / test servers |
| **Development** | 🟢 Green | `#2ECC71` | `rgb(46,204,113)` | Dev / sandbox — safe to experiment |
| **Local** | ⚪ Gray | `#95A5A6` | `rgb(149,165,166)` | localhost / local instances |
| **Custom (e.g. UAT)** | 🟣 Purple | `#9B59B6` | `rgb(155,89,182)` | User-defined environments |

#### Tab Color Settings

| Setting | Type | Description |
|---------|------|-------------|
| Use gradient colors | `Bool` | Apply a gradient (lighter at top, darker at bottom). Default: **On** |
| Edit environments | Color Picker | Add/remove/rename environments. Click swatch → OS color picker |
| Assignment hierarchy | Hierarchy | **Group → Servers in Group → Server → Database**. Lower overrides higher |
| Right-click assignment methods | Context Menu | `Tab Color (Server)`, `Tab Color (Database)`, `Tab Color (Group)`, `Tab Color (Servers in Group)` |
| Status bar matches tab | `Bool` | Status bar at bottom auto-matches tab color. Default: **On** |
| Default color | Enum | Color for tabs with no assignment. Usually: no color (transparent) |

#### Assignment Hierarchy Detail

```
Level 1: Registered Server Group      (e.g., "All Production Servers" → Red)
  └─ Level 2: Servers in Group         (all servers inherit group color)
       └─ Level 3: Individual Server    (e.g., "SQLPROD01" → override to Orange)
            └─ Level 4: Database         (e.g., "AdventureWorks" → override to Blue)
```

**Rule:** Lower levels override higher. Set `Default` at lower levels to inherit from parent.

### 5.2 SQL History (formerly Tab History)

Comprehensive searchable history of every query tab and its content. Survives SSMS crashes.

![Suggestion Icon Types](./images/05_icon_types.svg)

| Feature | Description |
|---------|-------------|
| **Access** | Toolbar icon in SSMS toolbar (clock icon) |
| **Search** | Full-text search across all history items |
| **Filters** | All tabs / Open tabs only / Closed tabs only. Toggle with `Ctrl+Right/Left` |
| **Each entry shows** | File name (if saved), server name, database name, environment color, timestamp, SQL content preview |
| **Crash recovery** | If SSMS crashes, SQL History auto-restores all open tabs on next launch |
| **Reconnect** | Optionally reconnects restored tabs to their previous database |
| **Storage** | Local database on disk — persists across sessions |

### 5.3 Execution Guard

| Feature | Description |
|---------|-------------|
| **DELETE without WHERE** | Confirmation dialog appears before executing. Shows server name + environment prominently |
| **DROP statements** | Warning before executing DROP TABLE/DATABASE/INDEX on Production-colored tabs |
| **Prominent display** | Dialog background color matches the environment color (bright red for Production) |

---

## 6. Code Refactoring

Actions to restructure and improve SQL code, from renaming across entire databases to encapsulating queries.

<!-- Smart Rename: shows dependency tree preview before applying changes across database -->

### 6.1 Smart Rename

| Feature | Detail |
|---------|--------|
| **Trigger** | Right-click object in Object Explorer → Smart Rename, or select identifier in query → F2 |
| **Scope** | Entire database — updates all references in procedures, views, functions, triggers, constraints |
| **Engine** | Built on Redgate SQL Compare's dependency analysis |
| **Preview** | Shows a dependency tree + list of all scripts that will be modified |
| **Apply** | Generates and executes ALTER scripts |
| **Supports** | Tables, columns, stored procedures, functions, views |

### 6.2 Encapsulate as Stored Procedure

| Feature | Detail |
|---------|--------|
| **Trigger** | Select SQL code block → Actions List → Encapsulate as New Stored Procedure |
| **Auto-detection** | Detects variables used in the code and offers them as parameters |
| **Dialog** | Choose procedure name, parameter names, data types, schema |
| **Output** | Generates `CREATE PROCEDURE` skeleton + replaces original code with `EXEC` call |

### 6.3 Split Table

| Feature | Detail |
|---------|--------|
| **Trigger** | Right-click table in Object Explorer → Split Table |
| **Dialog** | Choose which columns go to the new table |
| **Auto-generates** | New table DDL, foreign key, data migration script, updates to dependent objects |
| **Use case** | Normalization refactoring |

### 6.4 Actions List

Appears when you select any code block (or click the lightbulb margin icon).

| Action | Description |
|--------|-------------|
| Qualify object names | Add schema prefix to all objects in selection |
| Expand wildcards | Replace `*` with explicit column list |
| Surround with BEGIN/END | Wrap in `BEGIN`...`END` block |
| Surround with TRY/CATCH | Wrap in `BEGIN TRY`...`BEGIN CATCH` |
| Comment / Uncomment | Toggle line comments |
| Create snippet from selection | Save selected text as a new snippet |
| Unformat | Remove all formatting whitespace |
| Disable formatting | Insert `-- SQL Prompt formatting off/on` markers |
| Insert semicolons | Add missing semicolons |
| Convert sp_executesql to SQL | Convert dynamic SQL to static SQL |
| Rename alias | Rename all occurrences of an alias in the script |
| Move to new line | Break expression onto new line |

---

## 7. Navigation & Productivity

### 7.1 Feature List

| Feature | Shortcut | Description |
|---------|----------|-------------|
| **Go to Definition** | `F12` | Jump to object in Object Explorer or script CREATE statement |
| **Highlight Occurrences** | Auto (click identifier) | All occurrences of the identifier highlighted in the editor |
| **Syntax Pair Matching** | Auto | Highlights matching `BEGIN`/`END`, `(`/`)` pairs |
| **Execute Current Statement** | `Shift+F5` | Execute only the statement under the cursor |
| **Command Palette** | `Ctrl+Shift+P` | Quick-search all SQL Prompt commands |
| **Parameter Info** | Auto (typing function call) | Tooltip showing parameter names, types, defaults |
| **Quick Info** | Hover over identifier | For tables: columns + types + row count. For functions: signature |
| **Find Object** | `Ctrl+Shift+F12` | Search for any database object by name |

---

## 8. Results Grid Enhancements

| Feature | Trigger | Description |
|---------|---------|-------------|
| **Export to Excel** | Right-click results → Open in Excel | Opens data directly in Excel |
| **Excel precision option** | Settings | Save 15+ digit numbers as text to prevent Excel rounding |
| **Copy as IN clause** | Right-click selected values | Generates `WHERE col IN ('val1', 'val2', ...)` |
| **Script as INSERT** | Right-click selected rows | Generates `INSERT INTO ... VALUES (...)` for each row |
| **Copy as CSV** | Right-click | Tab-delimited or comma-delimited copy |
| **Copy with headers** | Right-click | Include column headers in copy |
| **Aggregate totals** | Select numeric cells | Shows Sum, Average, Count, Min, Max in status area |

---

## 9. Complete Settings Reference

All settings accessed via **SQL Prompt menu → Options**.

| Options Page | Settings Summary |
|-------------|-----------------|
| **Main → Behavior** | Enable suggestions (Bool), Insertion keys (Tab/Enter/Space/Dot), Auto-show (Bool), Object qualification (Always/Non-default/Never), Auto-uppercase (Bool) |
| **Main → Database** | Databases to ignore (list), Auto-refresh on change (Bool), Refresh MS IntelliSense cache (Bool) |
| **Main → Editors** | Enable in SSMS (Bool), Enable in VS (Bool), File extensions (.sql, .prc) |
| **Format → Style** | Active style (dropdown), Create/Edit/Import/Export, Preview pane |
| **Format → Casing** | Keywords/Types/Functions/System procs casing (each: UPPER/lower/Title/As-is) |
| **Tabs → Color** | Environment editor (name + color), Server/DB/Group assignments, Gradient toggle |
| **Tabs → History** | Enable (Bool), Auto-restore on crash (Bool), Max history items |
| **Code Analysis** | Enable (Bool per IDE), Rule manager, Settings file path, Auto-fix popup (Bool) |
| **Snippets** | Personal folder path, Shared folder path, Snippet Manager link |
| **Query Results** | Excel 15+ digit precision as text (Bool) |
| **INSERT columns** | Include names (Bool), Include types (Bool), Include defaults (Bool) |
| **Column Picker** | Sort: Alphabetical / Table order |
| **Object Definition** | Show automatically (Bool), Default view: Summary / Script |
| **Import / Export** | Export All Settings (XML), Import, Reset This Page, Reset All |

---

## 10. Complete Color & Theme System

### 10.1 Syntax Highlighting — Light Theme (SSMS Default)

| Element | Color | Hex |
|---------|-------|-----|
| Keywords (SELECT, FROM) | Blue | `#0000FF` |
| Built-in Functions (GETDATE) | Magenta | `#FF00FF` |
| Comments (-- or /* */) | Green | `#008000` |
| String Literals ('text') | Red | `#FF0000` |
| Identifiers (column names) | Black | `#000000` |
| Operators (+, =, <>) | Gray | `#808080` |
| Numbers | Black | `#000000` |

### 10.2 Syntax Highlighting — Dark Theme (SSMS 21+ / VS Dark)

| Element | Color | Hex |
|---------|-------|-----|
| Keywords | Cornflower Blue | `#569CD6` |
| Functions | Light Yellow | `#DCDCAA` |
| Comments | Olive Green | `#6A9955` |
| Strings | Salmon | `#CE9178` |
| Identifiers | Light Gray | `#D4D4D4` |
| Operators | Light Gray | `#D4D4D4` |
| Numbers | Light Green | `#B5CEA8` |

### 10.3 Code Analysis Indicator Colors

| Indicator | Color | Hex | Appearance |
|-----------|-------|-----|------------|
| Warning | Green | `#3DD68C` | Wavy underline |
| Error | Red | `#FF5C5C` | Wavy underline |
| Auto-fix lightbulb | Yellow | `#FFC107` | Margin icon |
| Occurrence highlight | Blue | `rgba(79,140,255,0.2)` | Background highlight |
| Pair matching | Light Blue | `rgba(79,140,255,0.3)` | Background highlight |

### 10.4 Tab Environment Colors

| Environment | Hex | Active Tab | Inactive Tab | Status Bar |
|-------------|-----|------------|--------------|------------|
| Production | `#E74C3C` | Bright red bg | Dark red bg | Full red bar |
| Staging | `#F39C12` | Bright orange bg | Dark orange bg | Full orange bar |
| Testing | `#3498DB` | Bright blue bg | Dark blue bg | Full blue bar |
| Development | `#2ECC71` | Bright green bg | Dark green bg | Full green bar |
| Local | `#95A5A6` | Bright gray bg | Dark gray bg | Full gray bar |

### 10.5 Suggestion Popup Icon Colors

| Object | Hex | Background |
|--------|-----|------------|
| Table | `#E5C04B` | `rgba(229,192,75,0.20)` |
| Column | `#61AFEF` | `rgba(97,175,239,0.20)` |
| Procedure | `#C678DD` | `rgba(198,120,221,0.20)` |
| Function | `#D19A66` | `rgba(209,154,102,0.20)` |
| Snippet | `#3DD68C` | `rgba(61,214,140,0.20)` |
| View | `#56B6C2` | `rgba(86,182,194,0.20)` |
| Keyword | `#ABB2BF` | `rgba(171,178,191,0.15)` |
| Database | `#E06C75` | `rgba(224,108,117,0.20)` |
| Schema | `#98C379` | `rgba(152,195,121,0.20)` |
| Trigger | `#BE5046` | `rgba(190,80,70,0.20)` |

---

## 11. Keyboard Shortcuts Reference

| Shortcut | Action |
|----------|--------|
| `Ctrl+Space` | Open/invoke suggestion list |
| `Tab` | Accept selected suggestion |
| `Enter` | Accept selected suggestion (configurable) |
| `Esc` | Dismiss suggestion popup |
| `Ctrl+K, Y` | Format SQL |
| `Ctrl+B, Ctrl+U` | Apply casing only |
| `Ctrl+B, Ctrl+C` | Insert semicolons |
| `Ctrl+B, Ctrl+W` | Expand wildcards |
| `Ctrl+B, Ctrl+Q` | Qualify object names |
| `Ctrl+Shift+D` | Refresh suggestion cache |
| `F2` | Rename alias/variable (local) |
| `F12` | Go to definition |
| `Shift+F5` | Execute current statement only |
| `Ctrl+Shift+P` | Command Palette |
| `Ctrl+Left Arrow` | Column Picker (in suggestion) |
| `Ctrl+Right/Left` | Navigate SQL History filters |
| `Ctrl (hold)` | Make suggestion popup semi-transparent |

---

*Document compiled for AKML SQL gap analysis. Source: Redgate SQL Prompt documentation, University courses, product pages, and release notes.*
