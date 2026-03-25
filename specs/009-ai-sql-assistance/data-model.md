# Data Model: AI-Powered SQL Assistance

**Date**: 2026-03-25
**Branch**: `009-ai-sql-assistance`

## Entities

### AiSettings (Configuration POCO — persisted in config.json)

| Field | Type | Default | Description |
|-------|------|---------|-------------|
| Enabled | bool | false | Master switch for all AI features |
| Provider | string | "" | Active provider: "anthropic", "openai", "azure", "gemini", "ollama", "lmstudio", "custom" |
| Model | string | "" | Model identifier (e.g., "claude-sonnet-4-20250514", "gpt-4o") |
| ApiKey | string | "" | DPAPI-encrypted API key (stored as "dpapi:base64blob") |
| Endpoint | string | "" | Custom endpoint URL (for Azure, Ollama, LM Studio, custom) |
| MaxTokens | int | 4096 | Maximum tokens per AI response |
| Temperature | double | 0.2 | AI temperature (0.0 – 1.0) |
| Timeout | int | 30 | Request timeout in seconds |
| Retries | int | 2 | Number of retry attempts on failure |
| PrivacyMode | string | "schemaOnly" | Privacy level: "full", "schemaOnly", "anonymous", "offline", "disabled" |
| OfflineProvider | string | "" | Fallback provider for offline/failure (e.g., "ollama") |
| OfflineModel | string | "" | Fallback model name |
| OfflineEndpoint | string | "" | Fallback endpoint URL |
| TextToSql | bool | true | Enable text-to-SQL feature |
| Explain | bool | true | Enable AI Explain feature |
| Fix | bool | true | Enable AI Fix feature |
| AutoFixOnError | bool | false | Auto-offer fix on query failure |
| Optimize | bool | true | Enable AI Optimize feature |
| IndexSuggestions | bool | true | Enable AI index analysis |
| InlineCompletion | bool | false | Enable ghost text predictions (opt-in) |
| ChatPanel | bool | true | Enable AI chat panel |

**Validation Rules**:
- `Provider` must be one of the known values or empty (disabled)
- `MaxTokens` must be 1–100,000
- `Temperature` must be 0.0–2.0
- `Timeout` must be 5–120 seconds
- `Retries` must be 0–5
- `PrivacyMode` must be one of the known values
- `ApiKey` when non-empty must start with "dpapi:" prefix (encrypted)

---

### AiRequest (IPC Message — Shell → Engine)

Base fields shared by all AI request types:

| Field | Type | Key | Description |
|-------|------|-----|-------------|
| SessionId | string | 0 | Active editor session ID (for schema context lookup) |
| RequestType | string | 1 | "textToSql", "explain", "fix", "optimize", "indexAnalysis", "chat", "ghostText" |
| Prompt | string | 2 | User's natural language prompt or selected SQL |
| ErrorMessage | string? | 3 | SQL error message (for "fix" requests only) |
| ExecutionPlanXml | string? | 4 | Execution plan XML (for "indexAnalysis" requests only) |
| ConversationHistory | List<ChatTurn>? | 5 | Previous turns (for "chat" requests only) |
| CursorOffset | int | 6 | Cursor position in document (for "ghostText" requests) |

**State Transitions**: Pending → Processing → Completed / Failed / TimedOut / Cancelled

---

### ChatTurn (Nested in AiRequest/AiResponse)

| Field | Type | Key | Description |
|-------|------|-----|-------------|
| Role | string | 0 | "user" or "assistant" |
| Content | string | 1 | Message text |

---

### AiResponse (IPC Message — Engine → Shell)

Base fields shared by all AI response types:

| Field | Type | Key | Description |
|-------|------|-----|-------------|
| Success | bool | 0 | Whether the request succeeded |
| RequestType | string | 1 | Echo of the request type |
| GeneratedSql | string? | 2 | Generated/fixed/optimized SQL (for textToSql, fix, optimize) |
| Explanation | string? | 3 | Structured explanation (for explain) |
| Annotations | List<AiAnnotation>? | 4 | Inline annotations for changes (for fix, optimize) |
| IndexScripts | List<IndexSuggestion>? | 5 | Index creation scripts (for optimize, indexAnalysis) |
| ChatResponse | string? | 6 | AI response text (for chat) |
| GhostText | string? | 7 | Predicted completion text (for ghostText) |
| ErrorMessage | string? | 8 | Error description on failure |
| TokensUsed | int | 9 | Total tokens consumed by this request |
| LatencyMs | int | 10 | Response time in milliseconds |

---

### AiStreamChunk (IPC Message — Engine → Shell, streaming)

| Field | Type | Key | Description |
|-------|------|-----|-------------|
| RequestId | int | 0 | Correlates chunks to the original request |
| Text | string | 1 | Delta text for this chunk |
| IsComplete | bool | 2 | True when this is the final chunk |
| FinishReason | string? | 3 | "stop", "length", "error", null |
| TokensUsed | int | 4 | Running total of tokens consumed |

---

### AiAnnotation (Nested in AiResponse)

| Field | Type | Key | Description |
|-------|------|-----|-------------|
| StartLine | int | 0 | 0-based start line in the generated SQL |
| EndLine | int | 1 | 0-based end line |
| Category | string | 2 | "safe" or "review" |
| Description | string | 3 | What was changed and why |

---

### IndexSuggestion (Nested in AiResponse)

| Field | Type | Key | Description |
|-------|------|-----|-------------|
| CreateScript | string | 0 | Full CREATE INDEX statement |
| TableName | string | 1 | Target table |
| Columns | List<string> | 2 | Indexed columns |
| IncludeColumns | List<string>? | 3 | Covering index INCLUDE columns |
| EstimatedImprovement | string | 4 | Human-readable improvement estimate |
| EstimatedSizeKb | int | 5 | Estimated index size in KB |
| WriteImpact | string | 6 | Impact on write performance (Low/Medium/High) |

---

### SchemaContext (Internal — not persisted, built per request)

| Field | Type | Description |
|-------|------|-------------|
| DatabaseName | string | Current database name |
| CompressionLevel | int | 1-4 (names, columns, keys, descriptions) |
| Objects | List<SchemaObjectSummary> | Filtered objects (max 500) |
| ForeignKeys | List<FkSummary> | Relevant FK relationships |
| EstimatedTokens | int | Token count estimate for this context |

---

### SchemaObjectSummary (Nested in SchemaContext)

| Field | Type | Description |
|-------|------|-------------|
| Schema | string | Schema name (e.g., "dbo") |
| Name | string | Object name |
| Type | string | "Table", "View", "Procedure", "Function" |
| ApproxRowCount | long | Approximate row count |
| Columns | List<ColumnSummary>? | Column details (Level 2+) |
| PrimaryKey | List<string>? | PK column names (Level 3+) |
| Indexes | List<string>? | Index descriptions (Level 3+) |
| Description | string? | Extended property description (Level 4) |

---

### ColumnSummary (Nested in SchemaObjectSummary)

| Field | Type | Description |
|-------|------|-------------|
| Name | string | Column name |
| Type | string | Compact type (e.g., "INT", "NVARCHAR(50)") |
| IsNullable | bool | Nullable flag |
| IsPrimaryKey | bool | Part of primary key |
| ForeignKeyTarget | string? | FK target as "schema.table.column" |
| Description | string? | Extended property description |

---

### PrivacyTransformation (Internal — applied per request)

| Field | Type | Description |
|-------|------|-------------|
| Mode | string | "full", "schemaOnly", "anonymous" |
| SessionKey | byte[] | 32-byte random HMAC key (for anonymous mode) |
| LiteralMap | Dictionary<string, string> | Placeholder → original value mapping |
| IdentifierMap | Dictionary<string, string> | Hashed → original identifier mapping |

**State Transitions** (applied to outgoing requests):
1. `full` → no transformation
2. `schemaOnly` → AST-walk to replace literals with `__STR_N__`, `__INT_N__`, etc.
3. `anonymous` → literal redaction + HMAC-based identifier hashing with category prefixes

**Reverse Transformation** (applied to incoming responses):
1. De-hash identifiers using `IdentifierMap`
2. Optionally substitute literal placeholders

## Relationships

```
AppSettings 1──1 AiSettings (configuration)
AiSettings  1──* AiRequest (generates requests based on config)
AiRequest   1──1 SchemaContext (built per request from DatabaseCache)
AiRequest   1──1 PrivacyTransformation (applied before sending)
AiRequest   1──1 AiResponse (one response per request)
AiResponse  1──* AiAnnotation (for fix/optimize results)
AiResponse  1──* IndexSuggestion (for optimize/indexAnalysis results)
AiRequest   1──* AiStreamChunk (for streaming responses)
AiRequest   *──1 ChatSession (chat requests reference conversation history)
ChatSession 1──* ChatTurn (ordered list of user/assistant messages)
SchemaContext 1──* SchemaObjectSummary (filtered objects)
SchemaObjectSummary 1──* ColumnSummary (column details)
```
