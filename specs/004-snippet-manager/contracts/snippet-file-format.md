# Snippet File Format Contract

**Version**: 1.0 | **Branch**: `004-snippet-manager`

## Overview

Snippets are stored as individual `.akmlsnippet` JSON files. Each file contains one snippet with metadata, variables, and body.

## File Format

- **Extension**: `.akmlsnippet`
- **Encoding**: UTF-8 (no BOM)
- **One file per snippet**
- **Filename**: `{shortcode}.akmlsnippet` (e.g., `ct.akmlsnippet`)

## Schema

```json
{
  "metadata": {
    "id": "7f3a1b2c-4d5e-6789-abcd-ef0123456789",
    "shortcode": "ct",
    "name": "Create Table",
    "description": "Creates a new table with primary key and common columns",
    "author": "AKML SQL",
    "version": "1.0",
    "created": "2026-06-01T00:00:00Z",
    "modified": "2026-06-01T00:00:00Z",
    "category": "DDL",
    "tags": ["create", "table", "ddl", "schema"],
    "context": ["global", "batch_start"],
    "surroundsWith": false
  },
  "variables": [
    {
      "name": "SchemaName",
      "default": "dbo",
      "tooltip": "Schema name",
      "schemaAware": "schemas"
    },
    {
      "name": "TableName",
      "default": "NewTable",
      "tooltip": "Table name"
    }
  ],
  "body": [
    "CREATE TABLE [$SchemaName$].[$TableName$]",
    "(",
    "    [$PKColumn$] $PKType$ IDENTITY(1, 1) NOT NULL,",
    "    $CURSOR$",
    "    CONSTRAINT [PK_$TableName$] PRIMARY KEY CLUSTERED ([$PKColumn$])",
    ");",
    "GO"
  ]
}
```

## Built-in Variables (auto-resolved, NOT listed in `variables` array)

| Variable | Description | Resolution |
|---|---|---|
| `$CURSOR$` | Final cursor position | Removed from text; position tracked |
| `$SELECTEDTEXT$` | Selected text (surround-with) | Replaced with current selection |
| `$CLIPBOARD$` | Clipboard content | Replaced with clipboard text |
| `$DATE$` | Current date | ISO format: `2026-07-15` |
| `$DATETIME$` | Current date and time | `2026-07-15 14:30:00` |
| `$TIME$` | Current time | `14:30:00` |
| `$USER$` | Windows username | `akhamis` |
| `$MACHINE$` | Machine name | `DEV-PC-01` |
| `$DATABASE$` | Current database name | From active connection, or empty |
| `$SERVER$` | Current server name | From active connection, or empty |
| `$SCHEMA$` | Current default schema | From active connection, or `dbo` |
| `$GUID$` | New random GUID | `a1b2c3d4-...` |
| `$YEAR$` | Current year | `2026` |
| `$FILENAME$` | Current file name | `GetOrders.sql` |

## Schema-Aware Types

| Type | IntelliSense Suggestions |
|---|---|
| `schemas` | Schema names from schema cache |
| `tables` | Table names |
| `views` | View names |
| `columns` | Column names (table context from preceding placeholder) |
| `procedures` | Stored procedure names |
| `functions` | Function names |
| `datatypes` | SQL Server data types (built-in + UDTs) |
| `databases` | Database names |
| `indexes` | Index names |

## Context Values

| Value | When Snippet is Shown |
|---|---|
| `global` | Batch start, after GO, empty line |
| `after_select` | Inside SELECT clause |
| `after_from` | After FROM or JOIN |
| `after_where` | Inside WHERE clause |
| `after_join_on` | Inside ON condition |
| `after_group_by` | Inside GROUP BY |
| `after_order_by` | Inside ORDER BY |
| `after_insert` | After INSERT INTO |
| `after_update` | Inside UPDATE SET |
| `after_exec` | After EXEC/EXECUTE |
| `after_create` | After CREATE |
| `after_with` | Inside WITH (CTE) |

## Category Values

`DML`, `DDL`, `DBA`, `ControlFlow`, `SurroundWith`, `Custom`

## Storage Locations

| Source | Path | Priority |
|---|---|---|
| Personal | `%AppData%/AKML SQL/snippets/` | 1 (highest) |
| Team | User-configured path | 2 |
| Built-in | `<install>/snippets/` | 3 (lowest) |
