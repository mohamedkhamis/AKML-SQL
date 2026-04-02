# IPC Contract: AI-Powered SQL Assistance

**Date**: 2026-03-25
**Protocol**: Named pipe, MessagePack serialization, `[MessagePackObject]` POCOs

## Message Types

### Shell → Engine (70–89)

| ID | Name | Request Class | Description |
|----|------|---------------|-------------|
| 70 | AiTextToSql | `AiTextToSqlRequest` | Natural language → SQL generation |
| 71 | AiExplain | `AiExplainRequest` | Explain selected SQL |
| 72 | AiFix | `AiFixRequest` | Fix SQL with error context |
| 73 | AiOptimize | `AiOptimizeRequest` | Optimize SQL for performance |
| 74 | AiIndexAnalysis | `AiIndexAnalysisRequest` | Suggest missing indexes |
| 75 | AiChat | `AiChatRequest` | Chat panel message |
| 76 | AiGhostText | `AiGhostTextRequest` | Inline completion prediction |
| 77 | AiProviderTest | `AiProviderTestRequest` | Validate provider configuration |
| 78 | AiStreamCancel | `AiStreamCancelRequest` | Cancel an in-progress streaming response |

### Engine → Shell (170–189)

| ID | Name | Response Class | Description |
|----|------|----------------|-------------|
| 170 | AiTextToSqlResult | `AiTextToSqlResponse` | Generated SQL |
| 171 | AiExplainResult | `AiExplainResponse` | Structured explanation |
| 172 | AiFixResult | `AiFixResponse` | Fixed SQL with diff annotations |
| 173 | AiOptimizeResult | `AiOptimizeResponse` | Optimized SQL with annotations |
| 174 | AiIndexAnalysisResult | `AiIndexAnalysisResponse` | Index creation scripts |
| 175 | AiChatResult | `AiChatResponse` | Chat panel response |
| 176 | AiGhostTextResult | `AiGhostTextResponse` | Ghost text prediction |
| 177 | AiProviderTestResult | `AiProviderTestResponse` | Connection test result |
| 178 | AiStreamChunk | `AiStreamChunkMessage` | Streaming delta text |

---

## Request Schemas

### AiTextToSqlRequest

```
[Key(0)] string SessionId          // Active editor session
[Key(1)] string Prompt             // Natural language description
```

### AiExplainRequest

```
[Key(0)] string SessionId
[Key(1)] string SelectedSql        // SQL to explain
```

### AiFixRequest

```
[Key(0)] string SessionId
[Key(1)] string FailingSql         // SQL that produced the error
[Key(2)] string ErrorMessage       // SQL Server error message
[Key(3)] int    ErrorNumber        // SQL Server error number
```

### AiOptimizeRequest

```
[Key(0)] string SessionId
[Key(1)] string SelectedSql        // SQL to optimize
```

### AiIndexAnalysisRequest

```
[Key(0)] string SessionId
[Key(1)] string SelectedSql        // Query to analyze
[Key(2)] string? ExecutionPlanXml  // Optional execution plan XML
```

### AiChatRequest

```
[Key(0)] string SessionId
[Key(1)] string Message            // User's chat message
[Key(2)] List<ChatTurnDto> History // Previous conversation turns
```

### ChatTurnDto

```
[Key(0)] string Role               // "user" or "assistant"
[Key(1)] string Content            // Message text
```

### AiGhostTextRequest

```
[Key(0)] string SessionId
[Key(1)] string DocumentText       // Full document text
[Key(2)] int    CursorOffset       // Cursor position (0-based character offset)
[Key(3)] string PrecedingText      // ~500 chars before cursor for context
```

### AiProviderTestRequest

```
[Key(0)] string Provider           // Provider type name
[Key(1)] string ApiKey             // Encrypted API key (DPAPI blob)
[Key(2)] string? Endpoint          // Custom endpoint URL
[Key(3)] string Model              // Model name to test
```

### AiStreamCancelRequest

```
[Key(0)] int RequestId             // ID of the request to cancel
```

---

## Response Schemas

### AiTextToSqlResponse

```
[Key(0)] bool    Success
[Key(1)] string? GeneratedSql      // Generated SQL (null on failure)
[Key(2)] string? ErrorMessage      // Error description on failure
[Key(3)] int     TokensUsed
[Key(4)] int     LatencyMs
```

### AiExplainResponse

```
[Key(0)] bool    Success
[Key(1)] string? Purpose           // One-sentence summary
[Key(2)] string? StepByStep        // Numbered clause explanation
[Key(3)] string? KeyDetails        // Data types, performance, edge cases
[Key(4)] string? Suggestions       // Optional improvements
[Key(5)] string? ErrorMessage
[Key(6)] int     TokensUsed
[Key(7)] int     LatencyMs
```

### AiFixResponse

```
[Key(0)] bool    Success
[Key(1)] string? FixedSql          // Corrected SQL
[Key(2)] string? Explanation       // What was wrong and what was changed
[Key(3)] List<AnnotationDto>? Annotations  // Inline diff annotations
[Key(4)] string? ErrorMessage
[Key(5)] int     TokensUsed
[Key(6)] int     LatencyMs
```

### AnnotationDto

```
[Key(0)] int    StartLine          // 0-based start line
[Key(1)] int    EndLine            // 0-based end line
[Key(2)] string Category           // "safe" or "review"
[Key(3)] string Description        // What was changed and why
```

### AiOptimizeResponse

```
[Key(0)] bool    Success
[Key(1)] string? OptimizedSql      // Performance-improved SQL
[Key(2)] string? Explanation       // Overview of changes
[Key(3)] List<AnnotationDto>? Annotations
[Key(4)] List<IndexSuggestionDto>? IndexSuggestions
[Key(5)] string? ErrorMessage
[Key(6)] int     TokensUsed
[Key(7)] int     LatencyMs
```

### IndexSuggestionDto

```
[Key(0)] string       CreateScript        // Full CREATE INDEX statement
[Key(1)] string       TableName
[Key(2)] List<string> Columns
[Key(3)] List<string>? IncludeColumns
[Key(4)] string       EstimatedImprovement
[Key(5)] int          EstimatedSizeKb
[Key(6)] string       WriteImpact         // "Low", "Medium", "High"
```

### AiIndexAnalysisResponse

```
[Key(0)] bool    Success
[Key(1)] List<IndexSuggestionDto>? Suggestions
[Key(2)] string? Summary            // Overall analysis summary
[Key(3)] string? ErrorMessage
[Key(4)] int     TokensUsed
[Key(5)] int     LatencyMs
```

### AiChatResponse

```
[Key(0)] bool    Success
[Key(1)] string? Response           // AI response text (markdown)
[Key(2)] List<CodeActionDto>? CodeActions  // Actionable buttons
[Key(3)] string? ErrorMessage
[Key(4)] int     TokensUsed
[Key(5)] int     LatencyMs
```

### CodeActionDto

```
[Key(0)] string Label              // Button text (e.g., "Apply Fix", "Copy Script")
[Key(1)] string ActionType         // "applyToEditor", "copyToClipboard", "runQuery"
[Key(2)] string Code               // SQL or script content for the action
```

### AiGhostTextResponse

```
[Key(0)] bool    Success
[Key(1)] string? PredictedText     // Ghost text to display
[Key(2)] int     CursorOffset      // Echo of request cursor position
[Key(3)] string? ErrorMessage
[Key(4)] int     TokensUsed
[Key(5)] int     LatencyMs
```

### AiProviderTestResponse

```
[Key(0)] bool    Success
[Key(1)] string? ModelName         // Confirmed model name from provider
[Key(2)] string? ProviderVersion   // Provider API version
[Key(3)] string? ErrorMessage
[Key(4)] int     LatencyMs
```

### AiStreamChunkMessage

```
[Key(0)] int     RequestId         // Correlates to original request
[Key(1)] string  Text              // Delta text
[Key(2)] bool    IsComplete        // True for final chunk
[Key(3)] string? FinishReason      // "stop", "length", "error", null
[Key(4)] int     TokensUsed        // Running total
```

---

## Streaming Protocol

1. Shell sends a request (e.g., `AiTextToSql` with `RequestId = N`)
2. Engine begins processing and streams back `AiStreamChunk` messages with `RequestId = N`
3. Each chunk carries delta text; shell appends to accumulated result
4. Final chunk has `IsComplete = true` and `FinishReason = "stop"`
5. After the final chunk, engine sends the full typed response (e.g., `AiTextToSqlResult`)
6. If the user cancels, shell sends `AiStreamCancel` with `RequestId = N`; engine cancels the provider request and sends a final chunk with `FinishReason = "cancelled"`

**Chunk coalescing**: Engine batches tokens every 50ms or 5 tokens (whichever first) into a single `AiStreamChunk` frame to reduce IPC overhead.
