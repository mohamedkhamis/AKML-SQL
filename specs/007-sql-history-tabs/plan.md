# Implementation Plan: SQL History & Tab Management

**Branch**: `007-sql-history-tabs` | **Date**: 2026-03-24 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/007-sql-history-tabs/spec.md`

## Summary

Phase 7 adds three tightly coupled feature groups to AKML SQL: (1) SQL History — a persistent, searchable log of every SQL execution with full context capture, stored in a local SQLite database; (2) Tab Management — environment-aware tab coloring, session recovery, and tab productivity tools; (3) Execution Safety — production server guards, dangerous operation warnings, and transaction reminders. All features follow the existing shell↔engine architecture: the shell captures execution events and renders UI, the engine manages storage, search, and session data via new IPC message types.

## Technical Context

**Language/Version**: C# / .NET Framework 4.7.2 (shell extensions), .NET 10 (engine)
**Primary Dependencies**: Microsoft.Data.Sqlite (new, for history DB), System.Security.Cryptography.ProtectedData (DPAPI encryption), VS SDK 15.9–17.14 (per target), MessagePack 2.x (IPC), Serilog 4.x (logging)
**Storage**: SQLite database (`%AppData%\AKML SQL\history\sqlhistory.db`) with FTS5 full-text index; JSON files for session snapshots (`%AppData%\AKML SQL\sessions\`)
**Testing**: xunit 2.x with Microsoft.NET.Test.Sdk 17.x, IDisposable fixtures, real file I/O with temp directories
**Target Platform**: Windows (SSMS 20/21/22, VS 2019/2022/2026)
**Project Type**: Desktop IDE extension (VS/SSMS plugin)
**Performance Goals**: History search < 3s for 100K entries; history recording imperceptible to user; session auto-save with no UI pauses; tab coloring renders within 1s of connection
**Constraints**: Shell runs in .NET Framework 4.7.2 (no modern APIs); all UI must run on VS UI thread; history DB must support concurrent writes from multiple SSMS instances; max 1 MB SQL per history entry; max 100K entries
**Scale/Scope**: Up to 100K history entries, 5 retained sessions, 20 recently-closed tabs, unlimited environment rules

## Constitution Check

*No constitution file found — gate skipped. No violations to justify.*

## Project Structure

### Documentation (this feature)

```text
specs/007-sql-history-tabs/
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/           # Phase 1 output (IPC message contracts)
│   ├── history-ipc.md
│   ├── tabs-ipc.md
│   └── safety-ipc.md
└── tasks.md             # Phase 2 output (/speckit.tasks)
```

### Source Code (repository root)

```text
src/
├── AkmlSql.Core/
│   ├── Config/
│   │   └── AppSettings.cs              # Add HistorySettings, TabSettings, SafetySettings sections
│   ├── Ipc/
│   │   ├── RpcMessage.cs               # Add message type constants (40-59, 140-159)
│   │   └── Messages/
│   │       ├── HistoryRecordRequest.cs  # New: shell→engine execution capture
│   │       ├── HistorySearchRequest.cs  # New: search/filter request
│   │       ├── HistorySearchResponse.cs # New: search results
│   │       ├── HistoryActionRequest.cs  # New: favorite/delete/export actions
│   │       ├── HistoryActionResponse.cs # New: action results
│   │       ├── SessionSaveRequest.cs    # New: session snapshot save
│   │       ├── SessionRestoreRequest.cs # New: request session restore
│   │       ├── SessionRestoreResponse.cs# New: session tab data
│   │       ├── SafetyCheckRequest.cs    # New: pre-execution safety check
│   │       └── SafetyCheckResponse.cs   # New: warning type/details
│   └── Models/
│       ├── History/
│       │   ├── HistoryEntry.cs          # New: execution record entity
│       │   ├── HistoryFilter.cs         # New: search/filter criteria
│       │   └── ExportFormat.cs          # New: CSV/JSON/SQL enum
│       ├── Tabs/
│       │   ├── EnvironmentRule.cs       # New: tab coloring rule
│       │   ├── SessionSnapshot.cs       # New: session recovery data
│       │   └── ClosedTabEntry.cs        # New: recently-closed tab record
│       └── Safety/
│           ├── SafetyWarningType.cs     # New: warning type enum
│           └── TransactionState.cs      # New: open transaction tracking
├── AkmlSql.Engine/
│   ├── History/
│   │   ├── HistoryDatabase.cs           # New: SQLite storage with FTS5
│   │   ├── HistoryRequestHandler.cs     # New: IPC handler for history operations
│   │   ├── HistoryRetentionService.cs   # New: retention cleanup
│   │   └── HistoryEncryption.cs         # New: DPAPI wrapper for optional encryption
│   ├── Sessions/
│   │   ├── SessionRequestHandler.cs     # New: IPC handler for session save/restore
│   │   └── SessionStorage.cs            # New: JSON file-based session persistence
│   └── Safety/
│       └── SafetyCheckHandler.cs        # New: SQL parsing for dangerous operations
├── AkmlSql.Shell.Shared/
│   ├── Commands/
│   │   ├── HistoryPanelCommand.cs       # New: open History panel (Ctrl+Alt+H)
│   │   ├── RestoreClosedTabCommand.cs   # New: Ctrl+Shift+T handler
│   │   ├── CloseUnmodifiedCommand.cs    # New: close all unmodified tabs
│   │   ├── DuplicateTabCommand.cs       # New: duplicate current tab
│   │   └── PinTabCommand.cs             # New: pin/unpin tab
│   ├── History/
│   │   ├── HistoryToolWindow.cs         # New: dockable VS tool window
│   │   ├── HistoryToolWindowControl.cs  # New: WPF UserControl for history UI
│   │   ├── HistoryViewModel.cs          # New: MVVM ViewModel for history panel
│   │   └── ExecutionCapture.cs          # New: hooks execution events to record history
│   ├── Tabs/
│   │   ├── TabColoringManager.cs        # New: applies colors to document tabs
│   │   ├── EnvironmentDetector.cs       # New: matches server names to rules
│   │   ├── WindowTitleManager.cs        # New: custom SSMS window titles
│   │   ├── TabTooltipProvider.cs        # New: extended tab tooltips
│   │   └── ClosedTabStack.cs            # New: LIFO stack for Ctrl+Shift+T
│   ├── Sessions/
│   │   ├── SessionAutoSave.cs           # New: periodic session capture
│   │   └── SessionRecoveryDialog.cs     # New: recovery UI on startup
│   ├── Safety/
│   │   ├── ExecutionInterceptor.cs      # New: pre-execution safety checks
│   │   ├── SafetyWarningDialog.cs       # New: confirmation dialogs
│   │   └── TransactionMonitor.cs        # New: open transaction tracking + reminders
│   └── StatusBar/
│       └── StatusBarManager.cs          # Extend: add transaction indicator
tests/
├── AkmlSql.Core.Tests/
│   ├── History/
│   │   ├── HistoryDatabaseTests.cs      # New: SQLite CRUD, FTS5 search, retention
│   │   └── HistoryEntryTests.cs         # New: model validation, hash generation
│   ├── Tabs/
│   │   ├── EnvironmentDetectorTests.cs  # New: pattern matching rules
│   │   └── SessionSnapshotTests.cs      # New: serialization roundtrip
│   └── Safety/
│       └── SafetyCheckTests.cs          # New: SQL parsing for dangerous ops
```

**Structure Decision**: Phase 7 follows the existing architecture — new model classes in Core (shared), new handlers in Engine (out-of-process), new commands and UI in Shell.Shared (in-process). New IPC message types bridge the shell↔engine boundary. SQLite is engine-side only (net10.0). History UI uses a dockable VS tool window (IVsWindowPane) with WPF controls, following the MVVM pattern established by ProfileEditorViewModel and CompletionPopup.

## Complexity Tracking

> No Constitution Check violations to justify.
