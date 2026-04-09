# Spec 014 — SQL Prompt Parity progress tracker

**Branch**: `014-sql-prompt-parity`
**Spec**: [`specs/014-sql-prompt-parity/spec.md`](../specs/014-sql-prompt-parity/spec.md)
**Plan**: [`specs/014-sql-prompt-parity/plan.md`](../specs/014-sql-prompt-parity/plan.md)
**Tasks**: [`specs/014-sql-prompt-parity/tasks.md`](../specs/014-sql-prompt-parity/tasks.md)
**Started**: 2026-04-09

This file is updated as each task in `tasks.md` lands, so anyone can see at a glance what's done, what's in flight, and what is still planned without grep-ing the task file.

## Phase status

| Phase | Title | Tasks | Status |
|---|---|---|---|
| 1 | Setup | T001–T005 | ✅ **COMPLETE** (2026-04-10) |
| 2 | Foundational | T006–T020 | ✅ **COMPLETE** (2026-04-10) — see audit reduction notes below |
| 3 | US1 Pre-execution safety (P1) **MVP** | T021–T035 | _pending_ |
| 4 | US5 Tab coloring (P2) | T036–T044 | _pending_ |
| 5 | US2 Column Picker (P2) | T045–T052 | _pending_ |
| 6 | US3 Wildcard `*`+Tab (P2) | T053–T056 | _pending_ |
| 7 | US4 Command Palette (P2) | T057–T067 | _pending_ |
| 8 | US6 Code Analysis Issues window (P2) | T068–T075 | _pending_ |
| 9 | US13 Script navigation chords (P2) | T076–T088 | _pending_ |
| 10 | US14 Find Invalid Objects (P2) | T089–T097 | _pending_ |
| 11 | US17 Lightbulbs (P2) | T098–T106 | _pending_ |
| 12 | US7 Ctrl+B chord family (P3) | T107–T118 | _pending_ |
| 13 | US8 Object Definition Box (P3) | T119–T126 | _pending_ |
| 14 | US9 Format markers (P3) | T127–T130 | _pending_ |
| 15 | US10 AI keyboard shortcuts (P3) | T131–T137 | _pending_ |
| 16 | US11 Dual-instance regression (P3) | T138–T140 | _pending_ |
| 17 | US12 Settings audit (P3) | T141–T146 | _pending_ |
| 18 | US15 Smart Rename (P3) | T147–T160 | _pending_ |
| 19 | US16 Result-grid productivity (P3) | T161–T169 | _pending_ |
| 20 | US18 AI Explain/Index/Comment (P3) | T170–T185 | _pending_ |
| 21 | US19 Completion polish (P3) | T186–T201 | _pending_ |
| 22 | US20 Execution shortcuts + Browse Tabs (P3) | T202–T210 | _pending_ |
| 23 | Polish | T211–T220 | _pending_ |

## Reused infrastructure (audit findings, 2026-04-09)

A pre-implementation audit revealed that the previous specs (010, 011, 012, 013) already laid down most of the IPC layer that spec 014 originally planned to scaffold. Re-used as-is:

- `MessageType` integers already defined: `SafetyCheck`/`SafetyCheckResult`, `RequestRefactorPreview`/`RefactorPreviewResult`, `RequestRefactorApply`/`RefactorApplyResult`, `WildcardExpansion`/`WildcardExpansionResult`, `RequestAnalyze`/`AnalysisResult`, `DocumentOutline`/`DocumentOutlineResult`, `GetObjectDefinition`/`GetObjectDefinitionResult`, `FindReferences`/`FindReferencesResult`, `ObjectSearch`/`ObjectSearchResult`, `CrudGeneration`/`CrudGenerationResult`, `ScriptAs`/`ScriptAsResult`, `GridExport`/`GridExportResult`, `StatementBoundary`/`StatementBoundaryResult`, `AiTextToSql`, `AiExplain`, `AiFix`, `AiOptimize`, `AiIndexAnalysis`, `AiChat`, `AiGhostText`, `AiProviderTest`, `AiStreamCancel`, `HistoryRecord`/`HistoryRecordResult`, `HistorySearch`/`HistorySearchResult`, `HistoryAction`/`HistoryActionResult`, `SessionSave`/`SessionRestore`/`SessionDelete`, `SchemaStatusRequest`/`SchemaStatusResponse`.

- DTO files already exist for every one of the above message types under `src/AkmlSql.Core/Ipc/Messages/`.

- All ~50 dispatch cases are already wired in `src/AkmlSql.Engine/Server/PipeRpcServer.cs DispatchAsync`.

- `AppSettings` already has the sections most of spec 014 needs: `IntelliSense`, `Cache`, `Formatter`, `Snippets`, `CodeAnalysis`, `Refactoring`, `History`, `Tabs`, `Safety`, `Grid`, `EditorProductivity`, `ExecutionProductivity`, `Navigation`, `CommandPalette`, `Ai`. Spec 014 only adds individual *properties* to these (and one new sub-section for completion polish).

### Genuinely new for spec 014

| Item | Why new | Allocated id |
|---|---|---|
| `FindInvalidObjects` request/response | No previous spec covered DB-wide invalid-reference scanning | `90` / `190` |
| `FindUnusedVariables` request/response | No previous spec covered the unused-variable analysis | `91` / `191` |
| `EncryptedObjectDecryption` request/response | New for spec 014 — DAC-based decryption | `92` / `192` |

These three are the only **new** MessageType integers reserved by spec 014. Everything else reuses existing transport.

## Test baseline

| Suite | Baseline at 2026-04-09 | Current |
|---|---|---|
| Engine (`tests/AkmlSql.Engine.Tests`) | 867 | 867 |
| Core (`tests/AkmlSql.Core.Tests`) | 459 | 478 (+19 from Phase 2 spec-014 additions) |

Both must stay green for every milestone (SC-009). Engine baseline preserved exactly. Core grew by 19 new spec-014 tests covering the new `AppSettings` properties (10 tests) and the 8 new IPC DTOs + MessageType cross-check (9 tests).

**Known flake**: `ConfigManagerTests.Load_WhenFileAbsent_CreatesDefaultsAndSavesFile` (Phase 7 test, completely unrelated to spec 014) intermittently fails ~1 in 3 runs because of a parallel-test-runner race on the shared `%APPDATA%\AKML SQL\config.json` path. Confirmed not a spec-014 regression.

## Phase 2 reduction (2026-04-10)

The original Phase 2 plan called for ~30 new DTO files, 14 new MessageType ints, and 8 new `AppSettings` sections. After auditing the existing IPC layer (commit 2026-04-10), the following work proved unnecessary because previous specs already covered it:

| Original task | Reality | Status |
|---|---|---|
| 8 new `AppSettings` sections | 1 truly new section (`CompletionPolish`) + property additions to 6 existing sections | revised |
| 14 new MessageType ints | 3 truly new (FindInvalidObjects/FindUnusedVariables/EncryptedObjectDecryption) | revised |
| ~30 new DTO files | 8 truly new (3 for each new MessageType range, minus those that reuse the existing types) | revised |
| Stub all 14 dispatch cases | Stub 3 | revised |
| Create F1HelpListener | Same — actually new | unchanged |
| Extend `AppSettingsTests.cs` | 10 new test methods | done |
| Extend `IpcMessagesTests.cs` | 8 new test methods + 1 MessageType cross-check | done |

Net: Phase 2 produced 11 file changes / creations instead of the planned ~50. Every previously-existing message type, every dispatch case, and every settings section that the spec required was reused as-is from the deep work done by specs 010, 011, 012, and 013 — a pleasant surprise from the audit. The audit findings are now memorialised in the "Reused infrastructure" section above so future user-story phases know which transports they can lean on.

## Update protocol

When a task is completed: update `tasks.md` (mark `[X]`), update this file's Phase status row, and run the relevant test suite to confirm baseline remains green.
