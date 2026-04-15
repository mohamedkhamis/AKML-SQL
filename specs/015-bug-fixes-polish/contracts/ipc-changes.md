# IPC Contract Changes: 015-bug-fixes-polish

**Date**: 2026-04-14  
**Protocol**: MessagePack over named pipe `akmlsql-engine-{SID}-{PID}`

This document describes **only the IPC contract changes introduced by this feature**. For the full IPC reference, see `docs/ipc-api.md`.

---

## No New Message Types

This feature introduces **no new IPC message types**. All fixes use existing message types. The changes below are to handler behavior and shell-side session data, not the wire protocol.

---

## Modified: ObjectSearch Request Validation

**Message type**: `RequestObjectSearch` (MessageTypes.RequestObjectSearch)  
**Handler**: `NavigationRequestHandler.cs:147-206`

**Bug fix (breaking behavior correction)**:

| | Before (broken) | After (fixed) |
|---|---|---|
| Guard condition | `string.IsNullOrEmpty(databaseName)` | `string.IsNullOrEmpty(connectionString) \|\| string.IsNullOrEmpty(databaseName)` |
| When `connectionString = null` | Passes guard → cache miss → empty results (no error) | Returns `Success=false, Error="No active database connection for this session"` |

No payload schema change — `ObjectSearchRequest` and `ObjectSearchResponse` structures are unchanged.

---

## Modified: Completion — AlterTableColumn Context

**Message type**: `RequestCompletion (3)` → `CompletionResult (101)`  
**Handler**: `CompletionEngine.cs` via `CursorContextAnalyzer`

**Behavior change** (no schema change):

| Trigger text | Before | After |
|---|---|---|
| `ALTER TABLE Users ALTER COLUMN ` | Returns object names (tables/views) | Returns column names for `Users` |
| `UPDATE Users SET ` (no alias) | Returns no columns (empty alias map) | Returns column names for `Users` |

`CompletionRequest` and `CompletionResponse` schemas are unchanged. `CompletionItem.ObjectType = Column (2)` is already defined.

---

## Modified: Analysis — Logging Behavior

**Message type**: `RequestAnalyze (25)` / `AnalysisResult`  
**Handler**: `AnalysisController.cs` (shell-side), engine analysis pipeline

**Behavior addition** (no schema change):
- Engine handler now emits a DEBUG log entry on analysis start and a DEBUG/WARNING entry on completion (rule count + duration).
- Shell-side `AnalysisController` emits an INFO log on trigger, DEBUG on result receipt.

---

## Unchanged: DocumentOutline

**Message type**: `DocumentOutlineRequest (64)` / `DocumentOutlineResult`  
**Handler**: `DocumentOutlineHandler.cs`

No changes to protocol or handler logic. Fix is in the shell-side `IWpfTextViewCreationListener` attachment and content-type registration.

---

## Security: AI API Key — Not Transmitted Over IPC

AI provider API keys are read from Windows Credential Manager by the **shell** process and transmitted to the engine only as needed per-request. Keys are **never** stored in `config.json` and **never** persisted in the IPC session state. The engine does not cache API keys between requests.
