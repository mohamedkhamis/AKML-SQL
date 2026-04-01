# Wildcard Expansion (SELECT * → Column List)

## Overview

When the cursor is on `*` (bare or qualified like `o.*`) in a SELECT statement and the user presses Tab, show a SQL Prompt-style checkbox popup listing all columns from the FROM-clause tables. The user toggles columns, then Tab/Enter commits the expansion.

## Scope

- Bare wildcard: `SELECT *` → expand all columns from all FROM-clause tables
- Qualified wildcard: `SELECT o.*` → expand only columns from the table aliased as `o`
- Column prefixing: use alias if defined, table name if not
- Multi-line formatting aligned after SELECT
- Dark-themed checkbox popup matching existing completion popup

---

## Section 1: Tab Detection in CompletionController

In `CompletionController.Exec()`, when Tab is pressed and the completion popup is **not** open:

1. Get cursor position in the text buffer.
2. Check if character at/adjacent to cursor is `*`:
   - Cursor right after `*` (e.g., `SELECT *|`) -- most common.
   - Cursor right before `*` (e.g., `SELECT |*`).
3. Walk backwards from `*` to detect qualified form: if preceded by `identifier.`, extract the qualifier (e.g., `o` from `o.*`).
4. Verify SELECT context by scanning backwards for `SELECT` keyword, skipping `DISTINCT`/`TOP N` if present. This prevents triggering on arithmetic like `2 * 3`.
5. If valid wildcard detected, send `WildcardExpansionRequest` to engine.

**Edge cases:**
- `SELECT DISTINCT *` -- triggers
- `SELECT TOP 10 *` -- triggers
- `SELECT 2 * 3` -- does NOT trigger (arithmetic)
- `SELECT *, Name` -- triggers when cursor is on the `*`
- `SELECT o.*, p.*` -- each `*` expands only its qualifier's columns

---

## Section 2: IPC Message Types

New message type constants:

```
WildcardExpansionRequest  = 18  (Shell -> Engine)
WildcardExpansionResponse = 118 (Engine -> Shell)
```

### WildcardExpansionRequest

```csharp
public class WildcardExpansionRequest
{
    public string SessionId;
    public int CursorOffset;
    public string DocumentText;  // Full text, not relying on session sync
    public string Qualifier;     // null for bare *, "o" for o.*
}
```

`DocumentText` is sent directly because Tab bypasses the normal typed-character flow, so the engine session document may not be up-to-date.

### WildcardExpansionResponse

```csharp
public class WildcardExpansionResponse
{
    public bool Success;
    public WildcardTableGroup[] Tables;
    public string ErrorMessage;
}

public class WildcardTableGroup
{
    public string TableName;    // Display name: "Orders"
    public string Qualifier;    // Prefix for columns: alias if defined, table name if not
    public WildcardColumn[] Columns;
}

public class WildcardColumn
{
    public string ColumnName;
    public string TypeDisplay;  // e.g., "int, NOT NULL, PK"
}
```

---

## Section 3: Engine Wildcard Expansion Handler

New class `WildcardExpansionHandler` in the Engine.

**Flow:**

1. **Tokenize** document text via `TsqlParserService.GetTokenStream()`.
2. **Resolve aliases** using existing infrastructure:
   - Try `ParseWithSuffix()` then `AliasResolver.ResolveAliases()` (AST-based).
   - Fallback to `TokenBasedAliasExtractor.Extract()` for incomplete SQL.
3. **Filter tables** based on qualifier:
   - Bare `*`: return columns for ALL tables in the alias dictionary.
   - `o.*`: find the single table matching qualifier `o`.
4. **Fetch columns** from `DatabaseCache`:
   - `cache.FindObject(schema, table)` then read `dbObject.Columns`.
   - If `!dbObject.ColumnsLoaded` return `Success = false` (shell does nothing, Tab acts as normal).
5. **Build response** with `WildcardTableGroup[]`, one per table.
6. **Column ordering**: PK columns first, then by original ordinal position.

Reuses: `AliasResolver`, `TokenBasedAliasExtractor`, `DatabaseCache.FindObject()`, same schema/table parsing as `ColumnProvider`.

The engine trusts the shell's SELECT-context validation. It only resolves tables and fetches columns.

---

## Section 4: WPF Checkbox Popup

New class `WildcardExpansionPopup` -- dark-themed WPF control matching the existing `AkmlCompletionPopup`.

### Layout

```
+---------------------------------------------+
|  Orders                          [header]    |
|  [x] OrderId        int, NOT NULL, PK       |
|  [x] CustomerName   nvarchar(100), NULL      |
|  [x] OrderDate      datetime, NOT NULL       |
|---------------------------------------------|
|  OrderDetails                    [header]    |
|  [x] DetailId       int, NOT NULL, PK       |
|  [x] OrderId        int, NOT NULL, FK       |
|  [x] ProductId      int, NOT NULL           |
|  [x] Quantity       int, NOT NULL           |
+---------------------------------------------+
```

- Single-table case: skip the header, show columns directly.
- All columns checked by default.

### Keyboard

| Key | Action |
|-----|--------|
| Up/Down | Move selection highlight (skip headers) |
| Space | Toggle checkbox on highlighted row |
| Tab / Enter | Commit -- expand with checked columns |
| Escape | Dismiss without expanding |
| Ctrl+A | Check all |
| Ctrl+D | Uncheck all |

### Styling

- Same dark background, border, font as existing completion popup.
- Table group headers: bold, subtle background separator.
- Checked columns: normal text. Unchecked: dimmed/grayed.
- Type info right-aligned in secondary color.
- Column icon matching `CompletionObjectType.Column` style.
- Positioned below cursor, flipped above if near editor bottom (same logic as `CompletionPopupAdornment`).

---

## Section 5: Text Replacement

When the user commits (Tab/Enter), build expansion text from checked columns.

### Formatting

- First column replaces `*` (or `alias.*`) on the same line as SELECT.
- Each subsequent column on a new line, indented to align with the first column. Indentation = number of characters from line start to the `*` position (preserves any leading whitespace before SELECT).
- Comma at end of each line except last.

### Examples

**Single table, no alias:**
```sql
-- Before:
SELECT * FROM Orders

-- After:
SELECT OrderId,
       CustomerName,
       OrderDate
FROM Orders
```

**Multiple tables with aliases:**
```sql
-- Before:
SELECT * FROM Orders o JOIN OrderDetails od ON o.Id = od.OrderId

-- After:
SELECT o.OrderId,
       o.CustomerName,
       o.OrderDate,
       od.DetailId,
       od.ProductId,
       od.Quantity
FROM Orders o JOIN OrderDetails od ON o.Id = od.OrderId
```

**Qualified wildcard:**
```sql
-- Before:
SELECT o.* FROM Orders o JOIN OrderDetails od ON o.Id = od.OrderId

-- After:
SELECT o.OrderId,
       o.CustomerName,
       o.OrderDate
FROM Orders o JOIN OrderDetails od ON o.Id = od.OrderId
```

### Replacement Span

- Bare `*`: replace just the `*` character (1 char).
- `o.*`: replace from qualifier start to `*` inclusive (e.g., 3 chars for `o.*`).

### Edge Cases

- All columns unchecked: do nothing, dismiss popup.
- Single column checked: no trailing comma, single line.
- `SELECT DISTINCT *`: replace only the `*`, keep `DISTINCT`.
- `SELECT TOP 10 *`: replace only the `*`, keep `TOP 10`.

---

## Files to Create/Modify

### New Files (Engine)
- `src/AkmlSql.Engine/Completion/WildcardExpansionHandler.cs` -- main handler

### New Files (Core - IPC messages)
- `src/AkmlSql.Core/Ipc/Messages/WildcardExpansionRequest.cs`
- `src/AkmlSql.Core/Ipc/Messages/WildcardExpansionResponse.cs`

### New Files (Shell)
- `src/AkmlSql.Shell.Shared/Editor/Completion/WildcardExpansionPopup.cs` -- checkbox popup control

### Modified Files (Core)
- `src/AkmlSql.Core/Ipc/MessageTypes.cs` -- add constants 18/118

### Modified Files (Engine)
- `src/AkmlSql.Engine/Ipc/PipeRpcServer.cs` -- register handler for message type 18

### Modified Files (Shell)
- `src/AkmlSql.Shell.Shared/Editor/Completion/CompletionController.cs` -- Tab detection logic
- `src/AkmlSql.Shell.Shared/Editor/Completion/CompletionPopupAdornment.cs` -- host the new popup
