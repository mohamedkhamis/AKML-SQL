# Data Model: Code Refactoring Toolkit

**Branch**: `006-code-refactoring` | **Date**: 2026-03-23

---

## Entities

### RefactoringOperation

Represents a single refactoring capability offered by the engine.

| Field | Type | Description |
|-------|------|-------------|
| `OperationType` | `RefactorOperationType` (enum) | SafeRename, ExtractToCte, ExtractToProc, ExtractToDerivedTable, EncapsulateAsView, ConvertTempToTableVar, ConvertTableVarToTemp, ParameterizeValues |
| `OperationClass` | `RefactorOperationClass` (enum) | Lightweight (instant, no dialog) or Heavyweight (preview-confirm-apply) |
| `Parameters` | `Dictionary<string, string>` | Operation-specific inputs (e.g., NewName, CteAlias, ProcName) |
| `DocumentText` | `string` | Full current document text |
| `SelectionStart` | `int` | Character offset of user selection start (0 = no selection) |
| `SelectionLength` | `int` | Length of user selection |
| `SessionId` | `string` | Engine session for schema cache access |
| `Scope` | `RefactorScope` (enum) | CurrentScript, ProjectDirectory |
| `AdditionalFiles` | `string[]` | Paths of additional files (for cross-file rename) |

---

### RefactorChangeInfo

A single proposed text replacement in a specific file, returned in the preview response.

| Field | Type | Description |
|-------|------|-------------|
| `FilePath` | `string` | Absolute file path (`string.Empty` = current editor document) |
| `StartOffset` | `int` | Character offset in the file where replacement begins |
| `EndOffset` | `int` | Character offset where replacement ends (exclusive) |
| `OldText` | `string` | Original text being replaced (for diff display) |
| `NewText` | `string` | Replacement text |
| `Line` | `int` | 1-based line number (for display in preview dialog) |
| `Column` | `int` | 1-based column number |
| `ContextSnippet` | `string` | ±2 lines of surrounding context for diff view |
| `ChangeCategory` | `string` | "rename" / "structure" / "wrap" / "declaration" (grouping hint for UI) |

**State transitions**:
```
[Proposed] → [Approved] → [Applied]
           → [Rejected]  (user unchecks in dialog)
```

---

### RefactorPreviewResult

The complete set of changes proposed for a refactoring operation, returned to the shell for display in the preview dialog.

| Field | Type | Description |
|-------|------|-------------|
| `OperationType` | `RefactorOperationType` | Echo of the requested operation |
| `Changes` | `RefactorChangeInfo[]` | All proposed changes, sorted by file path then offset descending |
| `Warnings` | `string[]` | Non-blocking advisory messages (e.g., "2 references left in WHERE clause due to non-equi conditions") |
| `Errors` | `string[]` | Blocking issues that prevent apply (e.g., "Name collision: OrderDate already exists in scope") |
| `CanApply` | `bool` | False if any blocking errors exist |
| `GeneratedObjects` | `string[]` | New object text to be created (procedure body, CTE block) — displayed in preview |

---

### RefactorApplyRequest

Sent from shell to engine after user approves selected changes in the preview dialog.

| Field | Type | Description |
|-------|------|-------------|
| `OperationType` | `RefactorOperationType` | The original operation type |
| `ApprovedChanges` | `RefactorChangeInfo[]` | Only the changes the user checked (subset of preview result) |
| `CreateBackups` | `bool` | Whether to write `.refactor-backup` files before modifying |
| `FormatAfterRefactor` | `bool` | Whether to apply the active formatter profile after changes |
| `SessionId` | `string` | Engine session |

---

### RefactorApplyResult

| Field | Type | Description |
|-------|------|-------------|
| `Success` | `bool` | True if all approved changes were applied |
| `AppliedCount` | `int` | Number of changes successfully applied |
| `FailedFiles` | `string[]` | File paths that could not be written (read-only, locked) |
| `BackupPaths` | `string[]` | Paths of created backup files |
| `UpdatedDocumentText` | `string` | New text for the current editor document (replaces buffer content) |

---

### RefactoringSettings (AppSettings extension)

| Field | JSON Key | Default | Description |
|-------|----------|---------|-------------|
| `PreviewBeforeApply` | `previewBeforeApply` | `true` | Show preview dialog for heavyweight operations |
| `CreateBackups` | `createBackups` | `true` | Back up files before cross-file modification |
| `FormatAfterRefactor` | `formatAfterRefactor` | `true` | Apply active formatter profile after each operation |
| `RenameScope` | `renameScope` | `"currentScript"` | Default scope: `currentScript` or `projectDirectory` |
| `IncludeCommentsInRename` | `includeCommentsInRename` | `true` | Search comments for identifier occurrences during rename |
| `IncludeStringLiteralsInRename` | `includeStringLiterals` | `false` | Search string literals (risky — off by default) |

---

## Enumerations

### RefactorOperationType
```
SafeRename            = 0   // Heavyweight
ExtractToCte          = 1   // Heavyweight
ExtractToProc         = 2   // Heavyweight
ExtractToDerivedTable = 3   // Heavyweight
EncapsulateAsView     = 4   // Heavyweight
ConvertTempToTableVar = 5   // Lightweight (with warning dialog)
ConvertTableVarToTemp = 6   // Lightweight (with warning dialog)
ParameterizeValues    = 7   // Lightweight
```

### RefactorScope
```
CurrentScript     = 0   // Current editor document only
ProjectDirectory  = 1   // All .sql files in the directory of the current document (recursive)
```

### FormatActionType (extended from Phase 3)
```
// Existing (Phase 3)
CasingOnly            = 0
InsertSemicolons      = 1
ExpandWildcards       = 3
QualifyNames          = 4
ToggleBrackets        = 5
ToggleAs              = 7

// New (Phase 6)
RemoveSemicolons      = 8
ExpandInsertColumns   = 9
ExpandExecParameters  = 10
ExpandUpdateColumns   = 11
ConvertOldStyleJoins  = 12
AddGroupByColumns     = 13
EncapsulateBeginEnd   = 14
ReplaceDeprecatedSyntax = 15
```

---

## Relationships

```
AppSettings
  └── RefactoringSettings          (1:1)

RefactoringOperation
  └── RefactorChangeInfo[]         (1:N — generated by preview)

RefactorPreviewResult
  ├── RefactorChangeInfo[]         (1:N — proposed changes)
  └── GeneratedObjects[]           (1:N — new SQL text blocks)

RefactorApplyRequest
  └── RefactorChangeInfo[]         (1:N — approved subset from preview)

RefactorApplyResult
  └── BackupPaths[]                (1:N — one per modified file)
```

---

## IPC Message Type Assignments

| Constant Name | Value | Direction | Description |
|---------------|-------|-----------|-------------|
| `RequestRefactorPreview` | 30 | Shell→Engine | Compute preview for a refactoring operation |
| `RequestRefactorApply` | 31 | Shell→Engine | Apply approved changes |
| `RefactorPreviewResult` | 130 | Engine→Shell | Preview change set + warnings |
| `RefactorApplyResult` | 131 | Engine→Shell | Apply outcome + backup paths |

Lightweight refactoring continues to use the existing `FormatAction` (13) / `FormatActionResult` (113) message pair with new `FormatActionType` values 8–15.
