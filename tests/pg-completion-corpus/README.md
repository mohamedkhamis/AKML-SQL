# PostgreSQL completion corpus (spec 042)

The PostgreSQL counterpart of [`tests/completion-corpus`](../completion-corpus/README.md): a ratcheted
set of completion cases run against a fake schema cache, so the gate needs no server.

- **Runner**: `tests/AkmlSql.PostgreSql.Tests/Completion/PgCompletionCorpusGateTests.cs`
  (`[Trait("Category", "CompletionCorpus")]`).
- **Fake schema**: `NorthwindPgTestCacheFactory` - the lower-case Northwind tables in `public`
  (`customers`, `orders`, `order_details`, `products`, `categories`, `employees`, `suppliers`,
  `shippers`, ...) plus a `sales` schema holding `sales.orders`. Names are the PostgreSQL port's
  (`customer_id`, `company_name`, ...), not the SQL Server spellings.
- **Gate**: `gate.json` holds `minPassRate` (0.95) and `minCases` (300). The pass rate may only go
  up; a change that lowers it is a regression to fix, not a baseline to bless.
- **Exclusions**: `exclusions.json` lists case ids that are deliberately not counted, each with a
  reason. Keep it short.

## Case format

Identical to the T-SQL corpus:

```json
{
  "id": "PGSEL-001",
  "family": "select-columns",
  "doc": "SELECT | FROM customers",
  "expect": {
    "mustContain": ["customer_id", "company_name"],
    "mustNotContain": ["product_name"],
    "minCount": 2
  },
  "note": "Bare select list scoped to customers"
}
```

`doc` is the document text with `|` marking the caret. Optional fields: `serverMajor` (default 18) to
check version gating, `searchPath` (default `["public"]`).

## Families

| File | Family |
|------|--------|
| `p01-select-columns.json` | select list columns, aliases, `DISTINCT ON`, `FILTER (WHERE ...)` |
| `p02-from-tables.json` | tables, views, materialized views and sequences; search path ordering; quoting |
| `p03-join-on.json` | JOIN ... ON from foreign keys, `USING`, `LATERAL`, `NATURAL` |
| `p04-where-group-order.json` | predicates, GROUP BY, HAVING, ORDER BY, `LIMIT`/`OFFSET`/`FETCH` |
| `p05-insert-conflict.json` | INSERT columns, `ON CONFLICT` targets and actions, `RETURNING` |
| `p06-update-delete.json` | UPDATE SET, DELETE USING, `RETURNING` |
| `p07-cte-window.json` | `WITH ... AS [NOT] MATERIALIZED`, recursive CTEs, window functions |
| `p08-functions.json` | built-in and user-defined functions, signature positions |
| `p09-types-casts.json` | `::` casts, `CAST(... AS type)`, DDL column types |
| `p10-dollar-quoted.json` | bodies of `$$ ... $$` functions, `DO` blocks |
| `p11-json-array-ops.json` | `->`, `->>`, `@>`, array subscripts, `ANY(array)` |
| `p12-ddl-and-types.json` | `CREATE TABLE/INDEX/VIEW`, constraints, identity columns |
| `p13-isolation.json` | nothing T-SQL appears: no `@`, `#`, `[`, `GO`, `sp_` suggestions |
