# IPC Messages Contract

**Branch**: `014-sql-prompt-parity` | **Date**: 2026-04-09

This document defines every new IPC `RpcMessage` type added by spec 014. All types follow the existing convention in `src/AkmlSql.Core/Ipc/Messages/`: a single MessagePack-serializable C# class with `[MessagePackObject]` and `[Key(N)]` attributes, a paired Request/Response, and a stable `MessageType` integer.

The existing `MessageTypes` integer ranges (per `src/AkmlSql.Core/Ipc/RpcMessage.cs`):

| Range | Purpose |
|---|---|
| `0–79` | Existing requests |
| `80` | `SchemaStatusRequest` (added by commit `2c34133`) |
| `100–179` | Existing responses |
| `180` | `SchemaStatusResponse` |

Spec 014 reserves the following new ranges:

| Range | Purpose |
|---|---|
| `90–110` | Spec 014 requests |
| `190–210` | Spec 014 responses |
| `300–305` | Spec 014 notifications (no response) |

---

## Request / Response message types

### `SafetyCheckRequest` (extended) — existing type, augmented payload

`MessageType = SafetyCheckRequest = 23` (existing)

**Augmented fields**:
- `IncludeMergeWithoutFilter: bool` (default `true`) — covers FR-002
- `IncludeInsideJoin: bool` (default `true`) — covers FR-002
- `IncludeProcedureBodies: bool` (default `true`) — covers FR-003

**Response shape** (existing `SafetyCheckResponse`) extends with:
- `Findings: SafetyFinding[]` — one per detected unsafe statement
- `SafetyFinding { RuleId: string, StatementText: string, StatementType: string, StartLine: int, StartColumn: int, EndLine: int, EndColumn: int, Reason: string }`

### `ExplainSqlRequest` (US18 / FR-084)

```text
MessageType = ExplainSqlRequest = 90
{
  Key(0) SessionId: string
  Key(1) Sql: string                    // The selected SQL
  Key(2) DatabaseName: string           // Active database for context
  Key(3) ServerVersion: string?         // Optional, for AI prompt enrichment
  Key(4) MaxAnswerTokens: int           // Default 2000
}
```

```text
MessageType = ExplainSqlResponse = 190
{
  Key(0) Status: enum { Ok, RateLimited, Disabled, Error }
  Key(1) Explanation: string?           // Plain-language paragraph(s)
  Key(2) FollowupSuggestions: string[]  // Up to 3 (FR-090)
  Key(3) ErrorMessage: string?
  Key(4) DurationMs: int                // For SC-015 measurement
}
```

### `QueryIndexAnalysisRequest` (US18 / FR-085)

```text
MessageType = QueryIndexAnalysisRequest = 91
{
  Key(0) SessionId: string
  Key(1) Sql: string                    // A SELECT statement with WHERE / JOIN
  Key(2) DatabaseName: string
  Key(3) IncludeStats: bool             // Default true; if false, no statistics gather
}
```

```text
MessageType = QueryIndexAnalysisResponse = 191
{
  Key(0) Status: enum { Ok, RateLimited, Disabled, Error, NoEligibleStatement }
  Key(1) Recommendation: IndexAnalysisRecommendation?
  Key(2) ErrorMessage: string?
  Key(3) DurationMs: int                // For SC-016 measurement
}

IndexAnalysisRecommendation {
  ExistingPlanSummary: string
  HintedPlanSummary: string
  EstimatedImpactPercent: double
  CreateIndexScript: string
  Confidence: enum { High, Medium, Low }
}
```

### `CommentToSqlRequest` (US18 / FR-087)

```text
MessageType = CommentToSqlRequest = 92
{
  Key(0) SessionId: string
  Key(1) CommentText: string            // Natural-language description
  Key(2) ContextSqlBefore: string?      // Optional surrounding SQL for context
  Key(3) DatabaseName: string
}
```

```text
MessageType = CommentToSqlResponse = 192
{
  Key(0) Status: enum { Ok, RateLimited, Disabled, Error }
  Key(1) GeneratedSql: string?
  Key(2) ErrorMessage: string?
}
```

### `FindInvalidObjectsRequest` (US14 / FR-065..FR-068)

```text
MessageType = FindInvalidObjectsRequest = 93
{
  Key(0) SessionId: string
  Key(1) DatabaseName: string
  Key(2) ChunkSize: int                 // Default 50
}
```

```text
MessageType = FindInvalidObjectsResponse = 193
{
  Key(0) Status: enum { Ok, PermissionDenied, Error }
  Key(1) Records: InvalidObjectRecord[] // Streamed in chunks via notification 300
  Key(2) IsFinalChunk: bool
  Key(3) TotalScanned: int
  Key(4) ErrorMessage: string?
}

InvalidObjectRecord {
  Schema: string
  Name: string
  Type: enum { Table, View, Procedure, Function, Trigger, Synonym }
  ErrorMessage: string
  SourceLine: int?
  MissingDependency: string?
  ScannedAtUtc: DateTime
}
```

### `SmartRenamePreviewRequest` (US15 / FR-069..FR-073)

```text
MessageType = SmartRenamePreviewRequest = 94
{
  Key(0) SessionId: string
  Key(1) DatabaseName: string
  Key(2) TargetSchema: string
  Key(3) TargetName: string
  Key(4) TargetColumnOrParam: string?   // Null when renaming an object itself
  Key(5) NewName: string
}
```

```text
MessageType = SmartRenamePreviewResponse = 194
{
  Key(0) Status: enum { Ok, NotFound, PermissionDenied, Error }
  Key(1) Plan: SmartRenamePlan?
  Key(2) ErrorMessage: string?
}
```

### `SmartRenameApplyRequest` (US15 / FR-071)

```text
MessageType = SmartRenameApplyRequest = 95
{
  Key(0) SessionId: string
  Key(1) DatabaseName: string
  Key(2) PlanId: Guid                   // From the preceding Preview
  Key(3) ConfirmedScript: string        // The script the user saw and approved
}
```

```text
MessageType = SmartRenameApplyResponse = 195
{
  Key(0) Status: enum { Applied, RolledBack, Cancelled, Error }
  Key(1) RolledBackReason: string?
  Key(2) AffectedDependentCount: int
}
```

### `SummarizeScriptRequest` (US13 / FR-061)

```text
MessageType = SummarizeScriptRequest = 96
{
  Key(0) SessionId: string
  Key(1) DocumentText: string
}
```

```text
MessageType = SummarizeScriptResponse = 196
{
  Key(0) Status: enum { Ok, ParseError }
  Key(1) Outline: ScriptOutlineNode[]
  Key(2) ErrorMessage: string?
}

ScriptOutlineNode {
  Id: Guid
  ParentId: Guid?
  StatementType: enum { Use, Create, Alter, Select, Insert, Update, Delete, Exec, ExecAs, Revert, Drop, Truncate, Merge, Other }
  Label: string
  StartLine: int
  StartColumn: int
  EndLine: int
  EndColumn: int
}
```

### `ScriptObjectAsAlterRequest` (US13 / FR-062)

```text
MessageType = ScriptObjectAsAlterRequest = 97
{
  Key(0) SessionId: string
  Key(1) DatabaseName: string
  Key(2) ObjectIdentifier: string       // E.g. "dbo.MyProc" or just "MyProc"
}
```

```text
MessageType = ScriptObjectAsAlterResponse = 197
{
  Key(0) Status: enum { Ok, NotFound, PermissionDenied, Error }
  Key(1) ResolvedSchema: string?
  Key(2) ResolvedName: string?
  Key(3) AlterScript: string?
  Key(4) WasDecrypted: bool             // For encrypted procs/funcs
  Key(5) ErrorMessage: string?
}
```

### `FindUnusedVariablesRequest` (US13 / FR-064)

```text
MessageType = FindUnusedVariablesRequest = 98
{
  Key(0) SessionId: string
  Key(1) DocumentText: string
}
```

```text
MessageType = FindUnusedVariablesResponse = 198
{
  Key(0) Status: enum { Ok, ParseError }
  Key(1) Unused: UnusedDeclaration[]
  Key(2) ErrorMessage: string?
}

UnusedDeclaration {
  Kind: enum { Variable, Parameter }
  Name: string
  DeclaredLine: int
  DeclaredColumn: int
  EnclosingObject: string?
}
```

### `AnalysisFixRequest` (US17 / FR-079..FR-083)

```text
MessageType = AnalysisFixRequest = 99
{
  Key(0) SessionId: string
  Key(1) DocumentText: string
  Key(2) RuleId: string
  Key(3) StartLine: int
  Key(4) StartColumn: int
  Key(5) EndLine: int
  Key(6) EndColumn: int
  Key(7) ApplyToAllOccurrences: bool   // Shift+click on lightbulb (edge case)
}
```

```text
MessageType = AnalysisFixResponse = 199
{
  Key(0) Status: enum { Applied, NoFixAvailable, WaitingForSchema, Error }
  Key(1) NewDocumentText: string?       // Full document with fix(es) applied
  Key(2) AffectedSpans: TextSpan[]      // Spans that were modified
  Key(3) ErrorMessage: string?
}

TextSpan {
  StartLine: int
  StartColumn: int
  EndLine: int
  EndColumn: int
}
```

### `ResultGridScriptRequest` (US16 / FR-074..FR-078)

```text
MessageType = ResultGridScriptRequest = 100
{
  Key(0) SessionId: string
  Key(1) Mode: enum { CopyAsInClause, ScriptAsInsert, OpenInExcel }
  Key(2) Context: ResultGridContext
  Key(3) IncludeIdentityInsert: bool    // Only used for ScriptAsInsert
  Key(4) PreservePrecision: bool        // Only used for OpenInExcel
}
```

```text
MessageType = ResultGridScriptResponse = 200
{
  Key(0) Status: enum { Ok, NoRows, UnsupportedColumnType, Error }
  Key(1) Payload: string?               // Clipboard text or Excel file path
  Key(2) Warnings: string[]             // E.g. "10 NULLs omitted from IN clause"
  Key(3) ErrorMessage: string?
}
```

### `EncryptedObjectDecryptionRequest` (US19 / FR-098)

```text
MessageType = EncryptedObjectDecryptionRequest = 101
{
  Key(0) SessionId: string
  Key(1) DatabaseName: string
  Key(2) Schema: string
  Key(3) Name: string
}
```

```text
MessageType = EncryptedObjectDecryptionResponse = 201
{
  Key(0) Status: enum { Ok, DacUnavailable, NotEncrypted, NotFound, Error }
  Key(1) DecryptedScript: string?
  Key(2) WasDecrypted: bool
  Key(3) ErrorMessage: string?
}
```

### `RefreshSchemaCacheRequest` (US19 / FR-093)

```text
MessageType = RefreshSchemaCacheRequest = 102
{
  Key(0) SessionId: string
  Key(1) DatabaseName: string
  Key(2) IncludePhaseB: bool            // Default true
}
```

```text
MessageType = RefreshSchemaCacheResponse = 202
{
  Key(0) Status: enum { Started, AlreadyRunning, NoSession, Error }
  Key(1) ErrorMessage: string?
}
```

---

## Notification message types (no response)

### `AnalysisIssuesPushed` (US6, US17)

```text
MessageType = AnalysisIssuesPushed = 300
{
  Key(0) SessionId: string
  Key(1) DocumentPath: string
  Key(2) Issues: AnalysisIssue[]
  Key(3) RunAtUtc: DateTime
}

AnalysisIssue {
  RuleId: string
  Severity: enum { Info, Warning, Error, Hint }
  Description: string
  ProblemText: string
  RemediationText: string
  StartLine: int
  StartColumn: int
  EndLine: int
  EndColumn: int
  IsAutoFixable: bool
  Category: enum { BP, PE, ST, SE, DE, DEP, EX, NM }
}
```

### `InvalidObjectsScanProgress` (US14)

```text
MessageType = InvalidObjectsScanProgress = 301
{
  Key(0) SessionId: string
  Key(1) Records: InvalidObjectRecord[]   // Chunk
  Key(2) IsFinalChunk: bool
  Key(3) TotalScannedSoFar: int
}
```

### `SmartRenameApplyProgress` (US15)

```text
MessageType = SmartRenameApplyProgress = 302
{
  Key(0) SessionId: string
  Key(1) Stage: enum { ParsingScript, ExecutingRename, RewritingDependents, Committing }
  Key(2) PercentComplete: int            // 0..100
}
```

---

## Backwards compatibility

All new types use new MessageType ints in the reserved ranges. No existing message type number is reused or repurposed. Older clients that do not know the new types will receive `MessageType = Error` from the engine's default dispatch path (the existing `PipeRpcServer.DispatchAsync` already returns an error for unknown types).

## Handler ownership (engine side)

| Message | Handler class | New / existing |
|---|---|---|
| `SafetyCheckRequest` (extended) | `SafetyCheckHandler` | existing |
| `ExplainSqlRequest` | `ExplainSqlHandler` | NEW |
| `QueryIndexAnalysisRequest` | `QueryIndexAnalysisHandler` | NEW |
| `CommentToSqlRequest` | `CommentToSqlHandler` | NEW |
| `FindInvalidObjectsRequest` | `SchemaMetadataService.ScanInvalidObjectsAsync` | NEW |
| `SmartRenamePreviewRequest` / `SmartRenameApplyRequest` | `SmartRenameEngine` | NEW |
| `SummarizeScriptRequest` | `SummarizeScriptEngine` | NEW |
| `ScriptObjectAsAlterRequest` | `SchemaMetadataService.GetObjectAsAlterAsync` | NEW |
| `FindUnusedVariablesRequest` | `FindUnusedEngine` | NEW |
| `AnalysisFixRequest` | `AnalysisFixDispatcher` | NEW |
| `ResultGridScriptRequest` | `ResultGridScriptEngine` | NEW |
| `EncryptedObjectDecryptionRequest` | `EncryptedObjectDecryptor` | NEW |
| `RefreshSchemaCacheRequest` | `SchemaCacheManager.ForceRefreshAsync` | NEW |
| `AnalysisIssuesPushed` (notification) | `AnalysisEngine.OnAnalysisCompleted → PipeRpcServer.SendNotification` | NEW wiring |
| `InvalidObjectsScanProgress` | `SchemaMetadataService.ScanInvalidObjectsAsync` (chunk emitter) | NEW |
| `SmartRenameApplyProgress` | `SmartRenameEngine.ApplyAsync` | NEW |

All new handlers MUST be `async Task<RpcMessage?>` and accept a `CancellationToken` per CLAUDE.md async patterns.
