# Implementation Plan: SQL Prompt Parity — Close the Gap

**Branch**: `014-sql-prompt-parity` | **Date**: 2026-04-09 | **Spec**: [spec.md](./spec.md)
**Input**: Feature specification from `specs/014-sql-prompt-parity/spec.md`

## Summary

Close the remaining functional gap between AKML SQL and Red Gate SQL Prompt 11.3 by delivering 20 prioritized user stories covering pre-execution safety dialogs, in-popup Column Picker, wildcard expansion, Command Palette, environment-based tab coloring, dockable Code Analysis Issues window, the full `Ctrl+B` refactoring chord family, two-tab Object Definition box, inline formatting markers, AI feature reach (Explain SQL, Query Index Analysis, comment-to-SQL, fix-on-error), AI keyboard shortcuts, dual-instance awareness, completion polish (toggle, refresh, commit keys, category filter, MS_Description tooltips, encrypted decryption, temp-table IntelliSense), Smart Rename with dependency preview, Find Invalid Objects, Summarize Script + navigation chords, result-grid productivity (Copy as IN, Script as INSERT, Open in Excel), code-analysis lightbulb auto-fixes, two new execution shortcuts (`Alt+Shift+F5`, `Ctrl+Shift+F5`), F1 contextual help, and `Ctrl+Q` Browse Open Tabs.

The implementation reuses every existing component documented in CLAUDE.md (`SafetyCheckHandler`, `SchemaMetadataService`, `RefactoringEngine`, `AnalysisEngine`, `AiRequestHandler`, `CompletionEngine`, `EnvironmentDetector`, `TabColoringManager`, `AkmlCompletionPopup`, `ConfigManager`, `TsqlParserService`, `WildcardExpansionHandler`, `NoformatScanner`). New surfaces are added on the shell side (dockable tool windows, dialogs, command bindings, MEF margin/adornments) and the engine side (a small set of new request/response RPC types).

## Technical Context

**Language/Version**: C# (LangVersion `latest`) — .NET Framework 4.7.2 for shell extensions (SSMS 20/21/22, VS 2019/2022/2026), .NET 10 for the engine and updater.

**Primary Dependencies**:
- VS SDK 15.9.3 (SSMS 20 / VS 2017 IsolatedShell, x86), 16.0.208 (VS 2019, x86), 17.14.x (SSMS 21/22, VS 2022/2026, x64).
- Microsoft.SqlServer.TransactSql.ScriptDom (`TSql170Parser`) for parsing.
- MessagePack 2.x for IPC framing; System.Text.Json 8.x for config.
- Microsoft.Data.SqlClient 5.x for engine-side schema queries.
- Serilog 4.x + Serilog.Sinks.File for structured logging.
- Microsoft.VisualStudio.Shell.{15,16,17}, Microsoft.VisualStudio.Text.UI.Wpf for WPF margins, tool windows, MEF exports.
- xUnit 2.x + Microsoft.NET.Test.Sdk 17.x for tests.

**Storage**:
- Config: `%AppData%\AKML SQL\config.json` (single source of truth — A12).
- SQL History: `%AppData%\AKML SQL\history\sqlhistory.db` (SQLite, existing).
- Schema cache: in-memory `ConcurrentDictionary` per session; LRU eviction (existing).
- Snippets: `%AppData%\AKML SQL\snippets\personal\*.akmlsnippet` and `<InstallDir>\Engine\snippets\` (existing).
- New per-spec persistence: none. Every new toggle goes into `config.json` (FR-058..FR-060, A12).

**Testing**:
- xUnit for engine and core unit tests (existing 867 + 459 baselines stay green — SC-009).
- Manual host-integration testing via the `hotswap-ssms22.sh` script for shell-side features (no automated SSMS host harness exists).
- Two real SQL Server instances are required for User Story 11 acceptance (A11) and for User Stories 14, 15, 18.
- New test files added per user story under `tests/AkmlSql.Engine.Tests/...` and (where applicable) `tests/AkmlSql.Core.Tests/...`.

**Target Platform**: Windows-only. SSMS 20 (VS 2017 IsolatedShell, x86), SSMS 21/22 (x64), VS 2019 (x86), VS 2022/2026 (x64). Engine and updater are `win-x64` self-contained single-file with `PublishTrimmed=true`.

**Project Type**: Multi-project IDE extension. Six shell extension projects (one per host) all import `AkmlSql.Shell.Shared.projitems`; one Core library (`netstandard2.0` + `net10.0`); one Engine process (`net10.0`); one Updater (`net10.0`); one Inno Setup installer.

**Performance Goals**:
- Completion popup latency: same as today, ≤ 80 ms p95 from keystroke to visible suggestions.
- Pre-execution safety check (FR-009 / SC-008): ≤ 500 ms in 99% of statements; non-blocking toast on miss.
- Find Invalid Objects (FR-065 / SC-012): scan a 5,000-object DB in ≤ 30 s; stream partial results within 2 s.
- Smart Rename Apply (FR-071 / SC-013): zero broken dependents in 100% of test runs.
- AI Explain (SC-015): ≤ 10 s for 95% of selections ≤ 500 lines.
- AI Query Index Analysis (SC-016): ≤ 30 s for 95% of statements against tables up to 1M rows.
- Suggestions toggle (`Ctrl+Shift+P`) (SC-017): ≤ 100 ms from keypress to popup suppression.

**Constraints**:
- IPC frame max 16 MB (existing).
- Document size limit: 10 MB per session (existing — `MaxDocumentSizeChars`).
- Snippet JSON limit: 1 MB (existing — `MaxSnippetJsonChars`).
- Cache invalidation must always precede new connection use (existing rule, validated in commit `835d662`).
- All file paths from IPC must be absolute (existing).
- Never call `.GetAwaiter().GetResult()` on the completion path (existing rule, enforced in commit `835d662`).
- Never modify SSMS / VS settings, MEF caches, registry, or layouts (memory: feedback_never_touch_settings).
- Preserve full SC-009 baseline: 867+ engine tests and 459+ core tests pass for every milestone.

**Scale/Scope**:
- 20 user stories, 105 functional requirements (FR-001..FR-105), 19 success criteria, 21 assumptions.
- 6 shell host targets (SSMS 20/21/22, VS 2019/2022/2026) — every shell-side feature must compile against all 6 SDKs via the shared `.projitems`.
- ~14 new IPC message types added across the 8 new user stories (US13–US20). All other stories (US1–US12) reuse existing IPC types.
- Zero new persistence layers (A12).

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

The repository does not have a `.specify/memory/constitution.md` file. The de-facto constitution for AKML SQL is `CLAUDE.md` (project root) plus the global rules in `~/.claude/CLAUDE.md`. The relevant gates derived from those documents:

| Gate | Source | Status |
|---|---|---|
| **G1 — Cross-host parity** | CLAUDE.md "Architecture / Shared Project Pattern" | PASS. Every new shell-side capability is added to `AkmlSql.Shell.Shared.projitems` so it compiles against all 6 host SDKs. The two SSMS-only features (Find Invalid Objects right-click on Object Explorer, FR-065; Browse Open Tabs `Ctrl+Q`, FR-105) are gated behind a host-detection check and degrade gracefully in VS hosts. |
| **G2 — Process isolation** | CLAUDE.md "Process Boundary: Shell ↔ Engine" | PASS. All new schema queries, AI calls, refactoring routines, and analysis fixes execute in the engine process. The shell only handles UI rendering and command dispatch. |
| **G3 — IPC discipline** | CLAUDE.md "IPC API" + commit `835d662` (no sync-over-async on completion path) | PASS. All new request types follow the existing `RpcMessage` framing. The completion path remains synchronous + cache-only. AI calls and Find Invalid Objects use the existing async dispatch path. |
| **G4 — Schema cache rules** | CLAUDE.md "Schema Cache Lifecycle" | PASS. New features that need schema metadata (Smart Rename, Find Invalid Objects, Object Definition Box, Find Unused Variables) read from the existing Phase A / Phase B caches. New work does not invalidate or restructure the cache. |
| **G5 — Settings storage** | CLAUDE.md "Configuration" + spec assumption A12 | PASS. Every new toggle is added to `AppSettings` POCO and serialized into `%AppData%\AKML SQL\config.json`. No new persistence layer. |
| **G6 — Logger init order** | CLAUDE.md "Build Gotchas" | PASS. New shell-side code does not call `LoggerFactory.Initialize()` or `LoadValidator.Validate()` before command registration. |
| **G7 — Don't touch IDE settings** | Memory: feedback_never_touch_settings + CLAUDE.md "Build Gotchas / Installer safety" | PASS. New features only read SSMS/VS state via `EnvDTE` interfaces; never write registry, never touch ComponentModelCache or privateregistry.bin (those are installer-only paths the user has approved). |
| **G8 — Test coverage baseline** | SC-009 + CLAUDE.md test commands | PASS. 867 Engine tests + 459 Core tests are the baseline. Every milestone must keep them green. New test files are added under `tests/AkmlSql.Engine.Tests/...` per the existing convention. |
| **G9 — Async correctness** | CLAUDE.md "Async patterns" | PASS. New IPC handlers are `async Task<RpcMessage?>`. `CancellationToken` is threaded through every new SQL query. No `.GetAwaiter().GetResult()` on the IPC dispatch or completion path. |
| **G10 — Security** | CLAUDE.md "Security" | PASS. New file path inputs validate via `Path.GetFullPath()`. Snippets remain ≤ 1 MB. Document size remains ≤ 10 MB. AI requests scrub the connection string before logging. |
| **G11 — Git workflow** | Global CLAUDE.md "Git Workflow ABSOLUTE HARD RULE" | PASS for plan generation. The plan output never invokes git; commits are created only when the user explicitly says so. |

**Result**: 11/11 gates pass. No constitution violations. Complexity tracking section is empty.

## Project Structure

### Documentation (this feature)

```text
specs/014-sql-prompt-parity/
├── plan.md                     # This file
├── research.md                 # Phase 0 — research decisions and rationales
├── data-model.md               # Phase 1 — entities, fields, persistence
├── quickstart.md               # Phase 1 — manual end-to-end verification walk-through
├── contracts/
│   ├── ipc-messages.md         # New RpcMessage types added by US13..US20
│   ├── settings-schema.md      # New AppSettings sections added by US1..US20
│   └── command-bindings.md     # New keyboard chords and command-set GUIDs
├── checklists/
│   └── requirements.md         # Existing — passes 16/16
└── spec.md                     # Existing — 735 lines, 20 user stories, FR-001..FR-105
```

### Source Code (repository root)

The repository structure is fixed by the existing multi-project layout. Plan-level deltas reference real paths only.

```text
src/
├── AkmlSql.Core/                       # netstandard2.0 + net10.0
│   ├── Config/
│   │   └── AppSettings.cs              # Add: ExecutionWarnings, TabColoring,
│   │                                    #      CommandPalette, Ai, CompletionPolish,
│   │                                    #      ResultGrid, Lightbulbs, Navigation
│   └── Ipc/
│       ├── RpcMessage.cs               # Add ~14 new MessageType ints
│       └── Messages/
│           ├── SafetyCheckRequest.cs   # Existing — augmented for FR-002/003
│           ├── ExplainSqlRequest.cs    # NEW (US18 / FR-084)
│           ├── ExplainSqlResponse.cs   # NEW
│           ├── QueryIndexAnalysisRequest.cs  # NEW (FR-085)
│           ├── QueryIndexAnalysisResponse.cs # NEW
│           ├── CommentToSqlRequest.cs  # NEW (FR-087)
│           ├── CommentToSqlResponse.cs # NEW
│           ├── FindInvalidObjectsRequest.cs  # NEW (FR-065..FR-068)
│           ├── FindInvalidObjectsResponse.cs # NEW
│           ├── SmartRenamePreviewRequest.cs  # NEW (FR-069..FR-073)
│           ├── SmartRenamePreviewResponse.cs # NEW
│           ├── SmartRenameApplyRequest.cs    # NEW
│           ├── SmartRenameApplyResponse.cs   # NEW
│           ├── SummarizeScriptRequest.cs     # NEW (FR-061)
│           ├── SummarizeScriptResponse.cs    # NEW
│           ├── ScriptObjectAsAlterRequest.cs # NEW (FR-062)
│           ├── ScriptObjectAsAlterResponse.cs# NEW
│           ├── FindUnusedVariablesRequest.cs # NEW (FR-064)
│           ├── FindUnusedVariablesResponse.cs# NEW
│           ├── AnalysisFixRequest.cs   # NEW (FR-079..FR-083)
│           ├── AnalysisFixResponse.cs  # NEW
│           ├── ResultGridScriptRequest.cs    # NEW (FR-074..FR-078)
│           └── ResultGridScriptResponse.cs   # NEW
│
├── AkmlSql.Engine/                     # net10.0, win-x64, single-file, trimmed
│   ├── Server/
│   │   └── PipeRpcServer.cs            # Wire new MessageTypes to handlers
│   ├── Completion/
│   │   ├── CompletionEngine.cs         # Honor SuggestionsSuppressed flag
│   │   └── Providers/
│   │       └── TempTableProvider.cs    # NEW (US19 / FR-100)
│   ├── Schema/
│   │   ├── SchemaMetadataService.cs    # Add InvalidObjectScanAsync, RenameDependencyAsync
│   │   └── EncryptedObjectDecryptor.cs # NEW (FR-098)
│   ├── Refactoring/
│   │   ├── RefactoringEngine.cs        # Add InlineProcedureAsync, EncapsulateAsAsync,
│   │   │                                #      InsertSemicolonsAsync, ToggleBracketsAsync
│   │   ├── SmartRenameEngine.cs        # NEW (US15)
│   │   ├── SummarizeScriptEngine.cs    # NEW (US13)
│   │   ├── FindUnusedEngine.cs         # NEW (US13)
│   │   └── ResultGridScriptEngine.cs   # NEW (US16)
│   ├── Analysis/
│   │   ├── AnalysisEngine.cs           # Existing
│   │   ├── RuleRegistry.cs             # Existing
│   │   └── AnalysisFixDispatcher.cs    # NEW (US17 — wires fix routines to RuleId)
│   └── Ai/
│       ├── AiRequestHandler.cs         # Add ExplainAsync, QueryIndexAnalysisAsync,
│       │                                #      CommentToSqlAsync, FixOnErrorAsync
│       ├── ExplainSqlHandler.cs        # NEW
│       ├── QueryIndexAnalysisHandler.cs# NEW
│       └── CommentToSqlHandler.cs      # NEW
│
├── AkmlSql.Shell.Shared/               # .projitems imported by all 6 hosts
│   ├── Editor/
│   │   ├── Completion/
│   │   │   ├── AkmlCompletionPopup.cs  # Extend with ColumnPicker tab + ObjectDefBox
│   │   │   ├── ColumnPickerControl.cs  # NEW (US2)
│   │   │   ├── ObjectDefinitionBox.cs  # NEW (US8)
│   │   │   └── CompletionToggleService.cs # NEW (US19 / FR-092)
│   │   ├── Lightbulbs/
│   │   │   ├── LightbulbAdornment.cs   # NEW (US17)
│   │   │   └── IssueDetailsPopup.cs    # NEW (US17)
│   │   └── ResultGridHook.cs           # NEW (US16)
│   ├── Dialogs/
│   │   ├── SafetyWarningDialog.cs      # NEW (US1)
│   │   ├── SmartRenameDialog.cs        # NEW (US15)
│   │   ├── SummarizeScriptDialog.cs    # NEW (US13)
│   │   └── BrowseOpenTabsDialog.cs     # NEW (US20 / FR-105)
│   ├── ToolWindows/
│   │   ├── CodeAnalysisIssuesToolWindow.cs   # NEW (US6)
│   │   ├── InvalidObjectsToolWindow.cs       # NEW (US14)
│   │   └── FindUnusedVariablesToolWindow.cs  # NEW (US13)
│   ├── CommandPalette/
│   │   ├── CommandPaletteWindow.cs     # Extend (US4)
│   │   └── CommandPaletteSources/      # NEW
│   │       ├── AkmlCommandSource.cs
│   │       ├── AkmlOptionsSource.cs
│   │       ├── HostCommandSource.cs
│   │       └── DatabaseObjectSource.cs # SSMS-only
│   ├── TabColoring/
│   │   ├── TabColoringManager.cs       # Existing
│   │   ├── EnvironmentDetector.cs      # Existing
│   │   └── EnvironmentPaletteWindow.cs # NEW (US5 / FR-043)
│   ├── Execution/
│   │   ├── ExecutionInterceptor.cs     # NEW (US1 / FR-001..FR-009)
│   │   ├── ExecuteCurrentBatchCommand.cs   # NEW (US20 / FR-101)
│   │   └── ExecuteToCursorCommand.cs       # NEW (US20 / FR-102)
│   ├── Refactoring/
│   │   ├── CtrlBChordHandler.cs        # NEW (US7 / FR-028..FR-030)
│   │   └── ScriptObjectAsAlterCommand.cs   # NEW (US13 / FR-062)
│   └── Help/
│       └── F1HelpListener.cs           # NEW (FR-104)
│
├── AkmlSql.Ssms20/AkmlSqlSsms20.vsct   # New CommandID rows for chords + tool windows
├── AkmlSql.Ssms21/AkmlSqlSsms21.vsct   # ditto
├── AkmlSql.Ssms22/AkmlSqlSsms22.vsct   # ditto
├── AkmlSql.VS2019/AkmlSqlVS2019.vsct   # ditto
├── AkmlSql.VS2022/AkmlSqlVS2022.vsct   # ditto
└── AkmlSql.VS2026/AkmlSqlVS2026.vsct   # ditto

tests/
├── AkmlSql.Core.Tests/
│   ├── Config/
│   │   └── AppSettingsTests.cs         # Existing — extend with new sections
│   └── Ipc/
│       └── Messages/
│           └── IpcMessagesTests.cs     # Existing — extend with new types
└── AkmlSql.Engine.Tests/
    ├── Refactoring/
    │   ├── SmartRenameEngineTests.cs       # NEW
    │   ├── SummarizeScriptEngineTests.cs   # NEW
    │   ├── FindUnusedEngineTests.cs        # NEW
    │   └── ResultGridScriptEngineTests.cs  # NEW
    ├── Schema/
    │   ├── InvalidObjectScanTests.cs       # NEW
    │   └── EncryptedObjectDecryptorTests.cs# NEW
    ├── Ai/
    │   ├── ExplainSqlHandlerTests.cs       # NEW (mocks AI client)
    │   ├── CommentToSqlHandlerTests.cs     # NEW
    │   └── QueryIndexAnalysisHandlerTests.cs # NEW
    ├── Completion/
    │   ├── TempTableProviderTests.cs       # NEW
    │   └── CompletionToggleTests.cs        # NEW
    └── Analysis/
        └── AnalysisFixDispatcherTests.cs   # NEW
```

**Structure Decision**: The existing multi-project layout is preserved. New code lives under the existing `AkmlSql.Engine`, `AkmlSql.Core`, and `AkmlSql.Shell.Shared` namespaces. No new top-level projects. Every shell-side surface goes into `AkmlSql.Shell.Shared` so all 6 host extensions inherit it via the shared `.projitems`. New IPC types are added under `AkmlSql.Core/Ipc/Messages/` following the existing record-style POCO + MessagePack convention.

## Complexity Tracking

> Empty — Constitution Check passes 11/11 with no violations.

| Violation | Why Needed | Simpler Alternative Rejected Because |
|---|---|---|
| _(none)_ | _(n/a)_ | _(n/a)_ |
