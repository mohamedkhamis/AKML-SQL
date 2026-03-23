# Quickstart: Code Refactoring Toolkit

**Branch**: `006-code-refactoring` | **Date**: 2026-03-23

Developer integration guide for implementing and testing Phase 6 refactoring.

---

## Prerequisites

- Phase 5 (Static Code Analysis) merged — `ReplaceDeprecatedSyntax` reads Phase 5 diagnostics
- Engine running: `dotnet run --project src/AkmlSql.Engine`
- Tests: `dotnet test tests/AkmlSql.Engine.Tests`

---

## Scenario 1: Expand Wildcards (Lightweight — ActionType 3, existing)

**Already implemented.** Regression test:

```sql
-- Input (table Orders has columns: OrderId, CustomerId, OrderDate)
SELECT * FROM dbo.Orders WHERE OrderId = 1

-- Expected output after Expand Wildcards (ActionType 3)
SELECT OrderId, CustomerId, OrderDate FROM dbo.Orders WHERE OrderId = 1
```

**Test verification**: Send `FormatActionRequest { ActionType = 3, DocumentText = "SELECT * FROM dbo.Orders..." }` — response `FormattedText` must contain the explicit column list.

---

## Scenario 2: Expand INSERT Columns (Lightweight — ActionType 9, new)

```sql
-- Input (table Customers has columns: CustomerId, FirstName, LastName, Email)
INSERT INTO dbo.Customers VALUES (1, 'Alice', 'Smith', 'alice@example.com')

-- Expected output after Expand INSERT Columns (ActionType 9)
INSERT INTO dbo.Customers (CustomerId, FirstName, LastName, Email)
VALUES (1, 'Alice', 'Smith', 'alice@example.com')
```

**Test verification**: Send `FormatActionRequest { ActionType = 9 }` — `FormattedText` contains the column list. If schema cache has no entry for `dbo.Customers`, `Warnings` contains `"Could not resolve columns for: dbo.Customers"`.

---

## Scenario 3: Convert Old-Style JOINs (Lightweight — ActionType 12, new)

```sql
-- Input (old-style comma-separated FROM)
SELECT o.OrderId, c.FirstName
FROM dbo.Orders o, dbo.Customers c
WHERE o.CustomerId = c.CustomerId
  AND o.OrderDate > '2024-01-01'

-- Expected output
SELECT o.OrderId, c.FirstName
FROM dbo.Orders o
INNER JOIN dbo.Customers c ON o.CustomerId = c.CustomerId
WHERE o.OrderDate > '2024-01-01'
```

**Edge case**: Non-equi condition `o.OrderDate > '2024-01-01'` stays in WHERE. An equi-join condition `o.CustomerId = c.CustomerId` moves to ON.

---

## Scenario 4: Add GROUP BY Columns (Lightweight — ActionType 13, new)

```sql
-- Input (non-aggregated columns: CustomerId, FirstName)
SELECT c.CustomerId, c.FirstName, COUNT(*) AS OrderCount
FROM dbo.Customers c
JOIN dbo.Orders o ON c.CustomerId = o.CustomerId

-- Expected output
SELECT c.CustomerId, c.FirstName, COUNT(*) AS OrderCount
FROM dbo.Customers c
JOIN dbo.Orders o ON c.CustomerId = o.CustomerId
GROUP BY c.CustomerId, c.FirstName
```

---

## Scenario 5: Safe Rename — Current Script (Heavyweight)

```sql
-- Input: rename column alias "od" to "orderDate" in current script
SELECT o.OrderDate AS od, o.CustomerId
FROM dbo.Orders o
WHERE od > '2024-01-01'
ORDER BY od DESC
```

**Expected preview** (`RefactorPreviewResult`):
- `Changes[0]`: FilePath="", StartOffset=24, OldText="od", NewText="orderDate"  (alias definition)
- `Changes[1]`: FilePath="", OldText="od", NewText="orderDate"  (WHERE clause reference)
- `Changes[2]`: FilePath="", OldText="od", NewText="orderDate"  (ORDER BY reference)
- `CanApply = true`, `Errors = []`

**After apply** (`RefactorApplyResult`):
- `Success = true`, `AppliedCount = 3`
- `UpdatedDocumentText` contains "orderDate" in all 3 locations

---

## Scenario 6: Safe Rename — Name Collision

```sql
-- Rename column "CustomerId" to "OrderId" — but OrderId already exists in scope
SELECT OrderId, CustomerId FROM dbo.Orders
```

**Expected preview**:
- `CanApply = false`
- `Errors[0] = "Name collision: 'OrderId' already exists in this scope"`

Shell behaviour: Apply button is disabled in the preview dialog.

---

## Scenario 7: Extract to CTE

```sql
-- Input: user selects the subquery (SELECT CustomerId, COUNT(*) AS cnt FROM dbo.Orders GROUP BY CustomerId)
SELECT c.FirstName, sub.cnt
FROM dbo.Customers c
JOIN (SELECT CustomerId, COUNT(*) AS cnt FROM dbo.Orders GROUP BY CustomerId) sub
  ON c.CustomerId = sub.CustomerId

-- Expected after Extract to CTE (CTE name provided as "OrderCounts")
WITH OrderCounts AS (
    SELECT CustomerId, COUNT(*) AS cnt FROM dbo.Orders GROUP BY CustomerId
)
SELECT c.FirstName, oc.cnt
FROM dbo.Customers c
JOIN OrderCounts oc ON c.CustomerId = oc.CustomerId
```

**Preview changes**:
- `Changes[0]`: Remove `(SELECT … GROUP BY CustomerId) sub` from FROM, replace with `OrderCounts oc`
- `GeneratedObjectTexts[0]`: The full CTE block prepended to the statement

---

## Scenario 8: Extract to Stored Procedure

```sql
-- Input: user selects the three-statement block
DECLARE @StartDate date = '2024-01-01'
DECLARE @EndDate   date = '2024-12-31'
SELECT OrderId, OrderDate FROM dbo.Orders
WHERE OrderDate BETWEEN @StartDate AND @EndDate
```

**Expected wizard output** (proc name: "usp_GetOrdersByDateRange"):
- Detected parameters: `@StartDate date`, `@EndDate date` (declared in outer scope — they exist here as the entire script, so both are parameters)
- Generated procedure:

```sql
CREATE PROCEDURE dbo.usp_GetOrdersByDateRange
    @StartDate date,
    @EndDate   date
AS
BEGIN
    SET NOCOUNT ON
    SELECT OrderId, OrderDate FROM dbo.Orders
    WHERE OrderDate BETWEEN @StartDate AND @EndDate
END
```

- Call site replacement: `EXEC dbo.usp_GetOrdersByDateRange @StartDate = '2024-01-01', @EndDate = '2024-12-31'`

---

## Scenario 9: Convert Temp Table to Table Variable

```sql
-- Input
CREATE TABLE #TempOrders (OrderId int, CustomerId int, OrderDate date)
INSERT INTO #TempOrders SELECT OrderId, CustomerId, OrderDate FROM dbo.Orders

-- Expected output (with warning about statistics)
DECLARE @TempOrders TABLE (OrderId int, CustomerId int, OrderDate date)
INSERT INTO @TempOrders SELECT OrderId, CustomerId, OrderDate FROM dbo.Orders
```

**Warning**: `"Table variables do not support statistics. Queries using @TempOrders may perform differently from #TempOrders."`

---

## Scenario 10: Parameterize Literal Values

```sql
-- Input
SELECT * FROM dbo.Orders WHERE CustomerId = 42 AND OrderDate > '2024-01-01'

-- Expected output
DECLARE @CustomerId int = 42
DECLARE @OrderDate  date = '2024-01-01'
SELECT * FROM dbo.Orders WHERE CustomerId = @CustomerId AND OrderDate > @OrderDate
```

Variable names are inferred from column context; data types are inferred from the literal (integer → `int`, date string → `date`).

---

## Running Tests

```bash
# All refactoring tests
dotnet test tests/AkmlSql.Engine.Tests --filter "Category=Refactoring"

# Specific operation
dotnet test tests/AkmlSql.Engine.Tests --filter "FullyQualifiedName~SafeRename"
dotnet test tests/AkmlSql.Engine.Tests --filter "FullyQualifiedName~ExtractToCte"
dotnet test tests/AkmlSql.Engine.Tests --filter "FullyQualifiedName~ConvertOldStyleJoins"
```

## Key Files

| File | Purpose |
|------|---------|
| `src/AkmlSql.Engine/Refactoring/RefactoringEngine.cs` | Engine entry point — dispatches to operation handlers |
| `src/AkmlSql.Engine/Refactoring/ReferenceCollector.cs` | TSqlFragmentVisitor collecting all identifier references |
| `src/AkmlSql.Engine/Refactoring/Operations/Heavyweight/SafeRenameOperation.cs` | Safe Rename implementation |
| `src/AkmlSql.Engine/Refactoring/Operations/Heavyweight/ExtractToCteOperation.cs` | Extract to CTE |
| `src/AkmlSql.Engine/Formatter/FormatRequestHandler.cs` | Extend `HandleFormatAction()` for types 8–15 |
| `src/AkmlSql.Shell.Shared/Refactoring/RefactoringPreviewDialog.cs` | WinForms preview dialog with diff view |
| `src/AkmlSql.Shell.Shared/Refactoring/SafeRenameCommand.cs` | Shell command handler for Safe Rename |
