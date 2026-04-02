# Research: AI-Powered SQL Assistance

**Date**: 2026-03-25
**Branch**: `009-ai-sql-assistance`

## 1. Multi-Provider Abstraction Layer

**Decision**: Use `Microsoft.Extensions.AI` (v10.4.1) as the unified `IChatClient` abstraction. All providers implement or adapt to this interface.

**Rationale**: Microsoft.Extensions.AI provides `IChatClient` — a standard abstraction for chat completions with built-in middleware pipeline (logging, caching, telemetry), streaming via `IAsyncEnumerable<StreamingChatCompletionUpdate>`, and composable `DelegatingChatClient` for custom middleware (redaction, rate limiting). All major provider SDKs already implement `IChatClient` or have official adapter packages.

**Alternatives Considered**:
- **Semantic Kernel**: Too heavy for this use case; brings orchestration, planning, and memory abstractions not needed. AKML SQL only needs chat completions.
- **Custom abstraction**: Would require maintaining adapters for each provider. Microsoft.Extensions.AI already does this and is widely adopted.
- **Direct SDK calls per provider**: No abstraction means provider-switching logic scattered throughout the codebase. Rejected for maintainability.

**Provider Factory Pattern**:
```
AiProviderFactory.Create(config) → IChatClient
  "anthropic"  → AnthropicClient(apiKey, model).AsIChatClient()
  "openai"     → OpenAIClient(apiKey).GetChatClient(model).AsIChatClient()
  "azure"      → AzureOpenAIClient(endpoint, credential).GetChatClient(model).AsIChatClient()
  "gemini"     → thin IChatClient adapter over Mscc.GenerativeAI
  "ollama"     → OllamaApiClient(baseUrl, model)  [already implements IChatClient]
  "lmstudio"   → OpenAIClient(apiKey, endpoint=localhost:1234).GetChatClient(model).AsIChatClient()
  "custom"     → OpenAIClient(apiKey, endpoint=customUrl).GetChatClient(model).AsIChatClient()
```

## 2. NuGet Package Selection

**Decision**: Use the following packages in the Engine (.NET 10, win-x64, PublishTrimmed):

| Package | Version | Purpose |
|---------|---------|---------|
| `Microsoft.Extensions.AI` | 10.4.1 | Core `IChatClient` abstraction + middleware |
| `Microsoft.Extensions.AI.OpenAI` | 10.4.1 | OpenAI / Azure OpenAI / LM Studio / Custom endpoint adapter |
| `Anthropic.SDK` | 5.10.0 | Anthropic Claude (built-in `IChatClient`) |
| `OllamaSharp` | 5.4.25 | Ollama (built-in `IChatClient` + `IEmbeddingGenerator`) |
| `Mscc.GenerativeAI` | 3.1.0 | Google Gemini (needs thin adapter) |
| `System.Security.Cryptography.ProtectedData` | 10.0.5 | DPAPI for API key encryption |
| `Microsoft.ML.Tokenizers` | 2.0.0 | Token counting without API calls |
| `Microsoft.ML.Tokenizers.Data.Cl100kBase` | 2.0.0 | GPT-4/3.5 tokenizer data (also good proxy for Claude/Gemini) |

**Rationale**: All packages target `netstandard2.0` at minimum. The unified `IChatClient` interface means the engine handler code is provider-agnostic. Only the factory needs provider-specific knowledge.

**Alternatives Considered**:
- `Google.Cloud.AIPlatform.V1`: Too heavy (full gRPC stack for Vertex AI). `Mscc.GenerativeAI` is lighter and supports both Google AI Studio and Vertex AI.
- `Tiktoken` / `SharpToken`: Good tokenizer alternatives, but `Microsoft.ML.Tokenizers` is Microsoft-maintained and trim-friendly.
- `Azure.AI.OpenAI` v2.1.0: Transitive dependency via `Microsoft.Extensions.AI.OpenAI`, not added directly.

## 3. Streaming AI Responses over Named Pipe IPC

**Decision**: Chunked streaming messages over the existing named pipe protocol.

**Rationale**: AI responses can take 5-30 seconds. Buffering the full response would leave users staring at a loading spinner. Streaming provides progressive feedback.

**Pattern**:
- New message type `AiStreamChunk` (Engine → Shell): carries `RequestId`, `Text` (delta), `IsComplete`, `FinishReason`
- Engine `await foreach`'s the `IAsyncEnumerable` from `IChatClient.GetStreamingResponseAsync()`
- For each chunk, engine sends an `AiStreamChunk` frame over the pipe
- Shell accumulates chunks by `RequestId` and updates UI progressively
- Cancellation: Shell sends `AiStreamCancel` message; engine cancels the `CancellationTokenSource`
- Chunk coalescing: Batch tokens every 50ms or 5 tokens (whichever first) to reduce IPC frame overhead

**Alternatives Considered**:
- Full-response buffering: Simpler but terrible UX for 10-30 second waits. Rejected.
- Separate streaming pipe: Unnecessary complexity. The existing frame protocol handles mixed message types well.

## 4. API Key Encryption

**Decision**: Use Windows DPAPI (`System.Security.Cryptography.ProtectedData`) with `DataProtectionScope.CurrentUser`.

**Rationale**: No key management required. OS-level per-user encryption. Works offline. Two lines of code. The encrypted blob is stored in `config.json` as a base64 string. Only the same Windows user on the same machine can decrypt it.

**Pattern**:
- Fixed app-specific entropy: SHA256 of "AkmlSql-ApiKey-v1" (not secret, but prevents other apps from using null entropy)
- One DPAPI blob per provider (so revoking one key doesn't require re-encrypting all)
- Zero plaintext byte arrays after use
- Store as `"apiKey": "dpapi:base64encodedblob"` in config.json (prefix for format detection)

**Alternatives Considered**:
- Windows Credential Manager: 2500-byte value limit; credentials visible in Windows UI; requires P/Invoke or wrapper. Better as optional alternative.
- ASP.NET Core Data Protection: Heavy for desktop use case; designed for web scenarios.
- Manual AES: Unnecessary complexity when DPAPI handles key management.

**Note**: The project already uses `System.Security.Cryptography.ProtectedData` in `HistoryEncryption.cs` — same pattern, same package.

## 5. Schema Context Compression

**Decision**: Multi-level compression with relevance filtering, compact DDL-like representation, and token estimation via `Microsoft.ML.Tokenizers`.

**Rationale**: A 1000-table database could consume 200K+ tokens uncompressed. The spec requires < 200ms preparation for up to 1000 tables and max 500 objects per request.

**Compression Levels**:
- **Level 1 (always)**: Database name, schema names, table/view names with row counts (~1 token/table, ~200 tokens for 200 tables)
- **Level 2 (on demand)**: Column names and types for referenced tables using compact format: `dbo.Orders(OrderId INT PK, CustomerId INT FK→dbo.Customers, OrderDate DATE)` (~500-2000 tokens)
- **Level 3 (on demand)**: Primary keys, foreign keys, indexes for referenced tables (~300-1000 tokens)
- **Level 4 (on demand)**: Extended property descriptions (~200-500 tokens)

**Relevance Filtering**: Parse the user's prompt/query for table/column name matches against the schema cache. Include matched tables plus 1-hop FK-connected tables. Cap at 500 objects.

**Token Estimation**: Use `Cl100kBase` tokenizer as proxy for all providers (within ~15% accuracy for Claude/Gemini). Apply 1.1x multiplier as conservative estimate.

**Alternatives Considered**:
- Full CREATE TABLE DDL: ~4x more tokens than compact format. Rejected.
- JSON schema representation: Even more verbose than DDL. Rejected.
- Progressive loading via function/tool calling: Most token-efficient but requires tool-use-capable models and adds latency. Consider as future enhancement.

## 6. Privacy Redaction Patterns

**Decision**: AST-based literal redaction via existing `TsqlParserService`; HMAC-based deterministic identifier hashing with per-session keys.

**Rationale**: The AST approach correctly handles edge cases (escaped quotes, literals in comments, dynamic SQL) that regex approaches miss. HMAC with per-session keys prevents cross-session correlation.

**Literal Redaction (schemaOnly mode)**:
1. Parse SQL with `TSql170Parser`
2. Walk AST visiting `Literal` nodes
3. Replace: `StringLiteral` → `'__STR_N__'`, `IntegerLiteral` → `__INT_N__`, `NumericLiteral` → `__NUM_N__`
4. Keep mapping `N → original_value` for response de-substitution
5. Preserve: `NULL`, well-known constants, boolean-like values
6. Fallback for unparseable SQL: conservative regex replacement

**Identifier Hashing (anonymous mode)**:
1. Generate per-session 32-byte random key via `RandomNumberGenerator`
2. For each identifier: `HMAC-SHA256(key, identifier_lowercase)` → first 8 bytes → hex
3. Preserve category prefix: `t_a3f2b1c8` (table), `c_7e4d9f0a` (column), `s_1b2c3d4e` (schema)
4. Same input → same output within session (deterministic for FK consistency)
5. In-memory `Dictionary<string, string>` for reverse mapping to de-hash AI responses
6. Session key discarded on session end

**Alternatives Considered**:
- Regex-based redaction: Fails on escaped quotes, literals in comments, `GO` separators. Rejected as primary approach; kept as fallback.
- SHA256 without HMAC: Same hash across sessions enables correlation attacks. Rejected.
- Persistent identifier mapping: Allows cross-session tracking. Rejected for privacy.

## 7. IPC Message Type Allocation

**Decision**: Phase 9 AI features use message types 70-89 (Shell → Engine) and 170-189 (Engine → Shell).

**Rationale**: Follows the existing numbering convention. Phase 8 uses 60-68 / 160-168. The 70-89 range provides 20 slots — enough for 7 AI features plus streaming, cancellation, and provider test messages.

**Allocated Message Types**:
| ID | Direction | Name |
|----|-----------|------|
| 70 | Shell → Engine | `AiTextToSql` |
| 71 | Shell → Engine | `AiExplain` |
| 72 | Shell → Engine | `AiFix` |
| 73 | Shell → Engine | `AiOptimize` |
| 74 | Shell → Engine | `AiIndexAnalysis` |
| 75 | Shell → Engine | `AiChat` |
| 76 | Shell → Engine | `AiGhostText` |
| 77 | Shell → Engine | `AiProviderTest` |
| 78 | Shell → Engine | `AiStreamCancel` |
| 170 | Engine → Shell | `AiTextToSqlResult` |
| 171 | Engine → Shell | `AiExplainResult` |
| 172 | Engine → Shell | `AiFixResult` |
| 173 | Engine → Shell | `AiOptimizeResult` |
| 174 | Engine → Shell | `AiIndexAnalysisResult` |
| 175 | Engine → Shell | `AiChatResult` |
| 176 | Engine → Shell | `AiGhostTextResult` |
| 177 | Engine → Shell | `AiProviderTestResult` |
| 178 | Engine → Shell | `AiStreamChunk` |

## 8. Existing Codebase Patterns (Summary)

**Schema Cache**: `SchemaCacheManager` → `DatabaseCache` → `DatabaseObject` with `Column`, `Parameter`, `Index`. Phase A < 500ms, Phase B background. FK index for O(1) lookup. All data needed for AI schema context is already cached.

**IPC**: Named pipe `akmlsql-engine-{SID}-{PID}`, MessagePack serialization, 16 MB max frame, `[MessagePackObject]` POCOs with `[Key(N)]` attributes.

**Config**: `AppSettings` POCO in `config.json`. Nested settings classes with `[JsonPropertyName]` attributes. Atomic writes via temp file + rename.

**Commands**: `OleMenuCommand` with `BeforeQueryStatus`, registered in `AkmlSqlPackage.InitializeAsync()` before non-critical init. Two overloads for `Package`/`AsyncPackage`.

**Tool Windows**: `ToolWindowPane` subclass hosting WPF `UserControl`. Registered via `[ProvideToolWindow]` attribute. Opened via `FindToolWindow()`.

**Adornments**: `IWpfTextViewCreationListener` (MEF export) → `IAdornmentLayer` for inline WPF elements in the editor. Existing examples: StickyScrollAdornment, MinimapAdornment.

**Encryption**: `ProtectedData.Protect/Unprotect` already used in `HistoryEncryption.cs`. Same pattern applies for API keys.
