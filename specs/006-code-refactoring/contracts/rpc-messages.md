# RPC Message Contracts: Code Refactoring

**Branch**: `006-code-refactoring` | **Date**: 2026-03-23

All messages use MessagePack serialization via the existing frame protocol (8-byte header + payload).
New message type constants are added to `AkmlSql.Core/Ipc/MessageTypes.cs`.

---

## Lightweight Refactoring (extends existing FormatAction protocol)

Lightweight operations reuse the existing `FormatAction` (13) / `FormatActionResult` (113) message pair.
New `FormatActionType` enum values 8–15 are added to `AkmlSql.Core/Ipc/Messages/FormatActionRequest.cs`.

### FormatActionRequest (existing — extended)

```
[MessagePackObject]
FormatActionRequest {
  [Key(0)] string SessionId
  [Key(1)] int    RequestId
  [Key(2)] string DocumentText
  [Key(3)] int    ActionType          // FormatActionType enum value (0-15)
  [Key(4)] string ProfileName         // Active formatter profile (may be empty)
  [Key(5)] int    SelectionStart      // 0 = act on full document
  [Key(6)] int    SelectionLength     // 0 = act on full document
}
```

### FormatActionResponse (existing — no changes needed)

```
[MessagePackObject]
FormatActionResponse {
  [Key(0)] string FormattedText       // Full replacement document text
  [Key(1)] string[] Warnings          // e.g., "Could not resolve columns for table: Orders"
}
```

**New ActionType values** (added to existing `FormatActionType` enum):

| Value | Name | Engine behaviour |
|-------|------|-----------------|
| 8  | RemoveSemicolons | Remove all `;` statement terminators |
| 9  | ExpandInsertColumns | Add column list to `INSERT INTO … VALUES` using schema cache |
| 10 | ExpandExecParameters | Add named `@param =` to bare `EXEC sp_name val, val` calls |
| 11 | ExpandUpdateColumns | Expand `UPDATE … SET` to include all columns from schema cache |
| 12 | ConvertOldStyleJoins | Convert comma-separated FROM to ANSI JOIN; equi-predicates → ON |
| 13 | AddGroupByColumns | Append GROUP BY clause from non-aggregated SELECT columns |
| 14 | EncapsulateBeginEnd | Wrap selected statement(s) in `BEGIN … END` block |
| 15 | ReplaceDeprecatedSyntax | Apply Phase 5 deprecated-construct fixes in one pass |

---

## Heavyweight Refactoring — Preview

### RequestRefactorPreview (MessageType = 30)

Sent by the shell to compute all proposed changes for a refactoring operation before applying.

```
[MessagePackObject]
RefactorPreviewRequest {
  [Key(0)]  string   SessionId
  [Key(1)]  int      RequestId
  [Key(2)]  int      OperationType       // RefactorOperationType enum (0–7)
  [Key(3)]  int      Scope               // RefactorScope enum: 0=CurrentScript, 1=ProjectDirectory
  [Key(4)]  string   DocumentText        // Current editor document text
  [Key(5)]  string   DocumentPath        // Absolute path of current document (for cross-file)
  [Key(6)]  int      SelectionStart      // Char offset of selection (0 = no selection)
  [Key(7)]  int      SelectionLength     // 0 = no selection
  [Key(8)]  string[] AdditionalFilePaths // Other .sql files to search (for ProjectDirectory scope)
  [Key(9)]  string   NewName             // SafeRename: the replacement identifier
  [Key(10)] string   ExtractedUnitName   // CTE/proc/view/table name to create
  [Key(11)] string   OriginalIdentifier  // SafeRename: the identifier to rename
}
```

### RefactorPreviewResult (MessageType = 130)

```
[MessagePackObject]
RefactorPreviewResponse {
  [Key(0)] int                RefactorChangeInfo[]  Changes           // Sorted: file path then offset descending
  [Key(1)] string[]           Warnings              // Non-blocking advisory messages
  [Key(2)] string[]           Errors                // Blocking issues (name collision, parse error)
  [Key(3)] bool               CanApply              // False if any blocking error
  [Key(4)] string[]           GeneratedObjectTexts  // New SQL text blocks (proc body, CTE, view def)
}

[MessagePackObject]
RefactorChangeInfo {
  [Key(0)] string FilePath          // Absolute path; empty string = current editor document
  [Key(1)] int    StartOffset       // Char offset in file
  [Key(2)] int    EndOffset         // Exclusive end offset
  [Key(3)] string OldText           // Text being replaced
  [Key(4)] string NewText           // Replacement text
  [Key(5)] int    Line              // 1-based line (display only)
  [Key(6)] int    Column            // 1-based column (display only)
  [Key(7)] string ContextSnippet   // ±2 surrounding lines for diff view
  [Key(8)] string ChangeCategory    // "rename" | "structure" | "wrap" | "declaration"
}
```

---

## Heavyweight Refactoring — Apply

### RequestRefactorApply (MessageType = 31)

Sent after the user approves selected changes in the preview dialog.

```
[MessagePackObject]
RefactorApplyRequest {
  [Key(0)] string             SessionId
  [Key(1)] int                RequestId
  [Key(2)] int                OperationType
  [Key(3)] RefactorChangeInfo[] ApprovedChanges   // User-approved subset of preview changes
  [Key(4)] bool               CreateBackups       // Write .refactor-backup files
  [Key(5)] bool               FormatAfterRefactor // Apply active formatter profile after
  [Key(6)] string             SessionProfileName  // Active formatter profile name
}
```

### RefactorApplyResult (MessageType = 131)

```
[MessagePackObject]
RefactorApplyResponse {
  [Key(0)] bool     Success               // True if all approved changes applied
  [Key(1)] int      AppliedCount          // Number of changes successfully applied
  [Key(2)] string[] FailedFilePaths       // Files that could not be written
  [Key(3)] string[] BackupFilePaths       // Created backup file paths
  [Key(4)] string   UpdatedDocumentText   // New text for current editor document
}
```

---

## Error Handling

| Scenario | Response field | Shell behaviour |
|----------|---------------|-----------------|
| Parse error in document | `Errors[]` populated, `CanApply = false` | Show error in preview; block Apply button |
| Name collision | `Errors[]` populated, `CanApply = false` | Show error with conflicting name; block Apply |
| File locked during apply | `FailedFilePaths[]` populated, `Success = false` | Show error summary listing skipped files |
| Schema cache miss | `Warnings[]` populated | Warn in preview; proceed with partial expansion |
| Timeout (30s) | Engine cancels and returns error RpcMessage | Shell shows "Refactoring timed out" |
