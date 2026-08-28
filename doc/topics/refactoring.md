# Refactoring

Refactoring rewrites your SQL into a cleaner or safer shape without changing what it does. AKML SQL offers three heavyweight, schema-aware operations plus a set of quick text-level rewrites. Find them on the AKML SQL menu and the editor right-click menu.

## Preview before anything changes

By default every refactoring shows a preview first: a diff of what will change, with checkboxes for individual edits. Nothing is applied until you confirm, and applied changes are undoable with Ctrl+Z. You can turn the preview off in Options, but keeping it on is recommended.

## Smart Rename

Renames a table, column, or alias everywhere it appears.

1. Right-click the identifier and choose **Smart Rename**.
2. Type the new name.
3. Review the list of every occurrence, uncheck any you want to leave alone, and apply.

The rename can cover just the current script or every `.sql` file in the project directory (set the scope in Options). All approved edits apply as one undo step, and name collisions are blocked before anything changes.

## Parameterize Values

Replaces hard-coded literals with declared variables.

```sql
-- before
SELECT * FROM dbo.Orders WHERE CustomerID = 42 AND OrderDate >= '2026-01-01'

-- after
DECLARE @CustomerID int = 42;
DECLARE @OrderDate date = '2026-01-01';
SELECT * FROM dbo.Orders WHERE CustomerID = @CustomerID AND OrderDate >= @OrderDate;
```

Variable types are inferred from the literals, and names are derived from the columns they compare against. Uncheck any literal you want to keep hard-coded.

## Convert Temp Table

Converts a `#temp` table to an `@table` variable, or the reverse. Useful when tuning: table variables suit small row counts, temp tables suit larger ones. The preview warns you about the statistics impact of the direction you picked.

## Lightweight refactorings

These run quickly on the current script, still with preview:

- Expand `SELECT *` into the explicit column list (uses the live schema)
- Expand `INSERT` to include an explicit column list
- Convert old-style comma joins (`FROM a, b WHERE a.id = b.id`) to ANSI `JOIN ... ON` syntax
- Encapsulate a statement in `BEGIN`/`END`
- Add the `AS` keyword to implicit aliases
- Qualify object names with their schema prefix
- Add square brackets around identifiers
- Insert missing `;` statement terminators
- Add `SET NOCOUNT ON` to a stored procedure

## Settings

Open **Tools** -> **Options** -> **AKML SQL** -> **Refactoring** to control preview, backups, and rename scope. See the [Configuration reference](../configuration.md) for all keys.

Related: [Static Code Analysis](static-analysis.md), [Formatting](formatting.md).
