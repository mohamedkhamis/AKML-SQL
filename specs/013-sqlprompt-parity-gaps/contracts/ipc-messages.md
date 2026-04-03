# IPC Message Contracts: SQL Prompt Parity Gaps

## Existing Messages (No Changes)

### FormatActionRequest (MessageType 13)
Already supports `ActionType = 17 (Unformat)`. No new IPC messages needed for US3.

### HistorySearchRequest (MessageType 20)
`SearchText` field will carry the FTS5 query string directly (currently wrapped in quotes). No schema change — the field semantics change from "literal phrase" to "FTS5 query syntax".

### HistoryActionRequest (MessageType 22)
`Action = 6 (Rename)` with `NewName` field already exists. No changes for US7.

## New Shell Commands (VSCT)

### Unformat Command
- **Command ID**: `0x0220` (CmdUnformat)
- **Command Set**: `{A1B2C3D4-1111-2222-3333-444455557777}`
- **Keyboard Shortcut**: Ctrl+B, Ctrl+U
- **Menu Placement**: AkmlSqlFormatGroup (alongside Format Document, Format Selection)
- **Behavior**: Sends `FormatActionRequest` with `ActionType = 17` via existing IPC channel

## Search Query Protocol

### Input (User → HistorySearchParser)
```
Raw text: "Product* OR NOT DROP "create view" server:PROD PC"
```

### Parsed Output (HistorySearchParser → HistorySearchRequest)
```
SearchText: "Product* OR NOT DROP \"create view\""  (FTS5 query)
Server: "PROD"                                      (prefix filter)
CamelCaseTokens: ["PC"]                             (post-filter)
```

### FTS5 Query (HistoryDatabase → SQLite)
```sql
SELECT ... FROM history h
INNER JOIN history_fts fts ON h.id = fts.rowid
WHERE history_fts MATCH 'Product* OR NOT DROP "create view"'
  AND h.server_name LIKE '%PROD%'
```

### Post-Filter (HistoryDatabase → Results)
CamelCase tokens applied as in-memory filter on returned `sql_text` values.
