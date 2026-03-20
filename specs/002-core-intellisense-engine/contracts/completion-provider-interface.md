# Completion Provider Interface Contract

**Version**: 1.0 | **Branch**: `002-core-intellisense-engine`

## Overview

The engine routes completion requests to the appropriate provider(s) based on the parser's cursor context analysis. Each provider is responsible for one type of completion.

## Provider Chain

When a completion request arrives, the engine:

1. Computes `CursorContext` from document state
2. If `InComment` or `InString` → return empty (FR-021)
3. Routes to one or more providers based on `ClauseType` and `PrecedingDot`
4. Merges results, deduplicates, applies fuzzy filter, sorts by `SortPriority`
5. Truncates to `MaxSuggestions` (default: 50)

## Provider Routing

| Context | Primary Provider | Secondary Provider(s) |
|---|---|---|
| After `alias.` or `table.` | ColumnProvider | — |
| After `schema.` | ObjectProvider | — |
| After `database.` | SchemaProvider | — |
| After `database.schema.` | ObjectProvider | — |
| After `FROM` / `JOIN` (no dot) | ObjectProvider | KeywordProvider (JOIN types) |
| After `JOIN` (position for table) | JoinProvider | ObjectProvider |
| After `EXEC` | ObjectProvider (procedures only) | — |
| After `(` on function/proc name | SignatureProvider | — |
| SELECT / WHERE / GROUP BY / HAVING / ORDER BY (no dot) | ColumnProvider (all tables), KeywordProvider | SnippetProvider |
| After FROM table ref (space) | AliasProvider | KeywordProvider |
| General keyword position | KeywordProvider | SnippetProvider |
| Variable position (`@`) | VariableProvider | — |

## Provider Interfaces

### ICompletionProvider

```
Name: string                    // Provider identifier for logging
CanHandle(context: CursorContext, cache: DatabaseCache): bool
GetCompletions(context: CursorContext, cache: DatabaseCache): CompletionItem[]
```

### Provider Implementations

**KeywordProvider**: Returns T-SQL keywords valid at the current clause position. Keywords are version-aware (selected by ServerVersion). Casing follows `KeywordCase` setting.

**ObjectProvider**: Returns database objects (tables, views, procedures, functions, synonyms). Filters by schema if dot-preceded. Filters by object type based on clause (e.g., only procedures after EXEC).

**ColumnProvider**: Returns columns for resolved table/alias references. Ranks by static heuristics (PK first, FK second, ordinal position). Includes data type, nullability, PK/FK badges as secondary text.

**JoinProvider**: Returns tables with FK relationships to already-referenced tables. Generates preview ON clause text. Ranks direct FK relationships first.

**AliasProvider**: Returns suggested alias based on table name abbreviation (first letters of PascalCase parts). Checks for conflicts with existing aliases in scope.

**SignatureProvider**: Returns function/procedure parameter signatures. For built-in functions: uses static dictionary. For user-defined: uses schema cache parameters.

**SnippetProvider**: Returns basic built-in snippet triggers (`ssf`, `sel`, `ins`, `upd`, `del`, `cte`). Each snippet has a template with tab-stop placeholders.

**VariableProvider**: Returns `@variables` declared in the current batch scope with their types.

## Fuzzy Matching Algorithm

Applied to filter text against `DisplayText` of each item:

1. **Exact prefix match**: Highest score (text starts with filter)
2. **Case-insensitive prefix match**: High score
3. **CamelCase match**: Filter characters match uppercase letters (e.g., "OD" → "OrderDate")
4. **Substring match**: Filter appears anywhere in text
5. **Non-contiguous character match**: All filter characters appear in order (e.g., "custid" → "CustomerID")
6. **No match**: Item excluded

Score combines match type + match position (earlier = better) + sort priority (PK/FK/ordinal).

## Built-in Snippet Set (Phase 2 only)

| Shortcode | Expansion |
|---|---|
| `ssf` | `SELECT * FROM $1` |
| `sel` | `SELECT $1 FROM $2` |
| `ins` | `INSERT INTO $1 ($2) VALUES ($3)` |
| `upd` | `UPDATE $1 SET $2 WHERE $3` |
| `del` | `DELETE FROM $1 WHERE $2` |
| `cte` | `WITH $1 AS ($2) SELECT $3 FROM $1` |

`$N` = tab-stop positions. `$1` = first stop after expansion.
