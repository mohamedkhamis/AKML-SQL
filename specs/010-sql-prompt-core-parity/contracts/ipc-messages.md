# IPC Message Contracts: SQL Prompt Core Feature Parity

All new features use the existing MessagePack-over-named-pipe IPC protocol. This document lists NEW or MODIFIED message types needed.

## Existing Messages (No Changes Required)

| MessageType | Direction | Purpose |
|-------------|-----------|---------|
| 20 (SnippetExpand) | Shell→Engine | Expand snippet by shortcode |
| 21 (SnippetList) | Shell→Engine | List/search snippets |
| 22 (SnippetSave) | Shell→Engine | Save new/edited snippet |
| 23 (SnippetDelete) | Shell→Engine | Delete snippet by ID |
| 24 (SnippetImport) | Shell→Engine | Import snippet file (stub) |
| 30 (RefactorPreview) | Shell→Engine | Preview refactoring changes |
| 31 (RefactorApply) | Shell→Engine | Apply refactoring changes |
| 55 (SafetyCheck) | Shell→Engine | Check SQL for destructive patterns |
| 60 (GetObjectDefinition) | Shell→Engine | Get CREATE script for object |
| 64 (DocumentOutline) | Shell→Engine | Get document structure tree |

## New Messages

### SafetyCheck Enhancement (MessageType 55 — modify existing)

No new message type needed. Existing `SafetyCheckRequest` and `SafetyCheckResponse` cover all requirements. The audit logging happens shell-side via Serilog after the dialog result is known.

### SnippetImport Implementation (MessageType 24 — implement existing stub)

The `SnippetImportRequest` and `SnippetImportResponse` message types exist but the handler is stubbed. Implementation needed in `SnippetRequestHandler.HandleImport()`.

### DocumentOutline Implementation (MessageType 64 — implement existing stub)

Request: `DocumentOutlineRequest` (exists)
- SessionId: string
- DocumentText: string (full SQL text)

Response: `DocumentOutlineResponse` (exists)
- Success: bool
- Nodes: OutlineNodeDto[] (tree structure)
- Error: string?

Engine handler needs implementation: parse SQL with TSql170Parser, walk AST to build tree of procedures, functions, views, CTEs, temp tables, and batch boundaries.

## No New IPC Messages Required

All features either:
1. Use existing IPC messages as-is (Safety, Snippets, Refactoring)
2. Need implementation of already-defined stubs (Import, DocumentOutline)
3. Are shell-only features requiring no engine communication (Bookmarks, Grid sort/filter, Settings UI)
