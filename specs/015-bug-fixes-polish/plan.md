# Implementation Plan: Multi-Area Bug Fixes and UI Polish (015)

**Branch**: `015-bug-fixes-polish` | **Date**: 2026-04-14 | **Spec**: [spec.md](spec.md)  
**Input**: Feature specification from `specs/015-bug-fixes-polish/spec.md`

## Summary

14 independently deliverable fixes across Installer, Query Page, SQL History, and SQL Options. Research confirmed several issues are outright bugs with pinpointed root causes (one-line fix for Search, alias-resolver gap for IntelliSense), while others require structural refactoring (SchemaProgress → adornment layer) or discoverability improvements to already-working features (query rename, Document Outline). No new IPC message types are required. DPAPI key storage and Document Outline SQL parsing are already implemented — the work is wiring, positioning, and polish.

## Technical Context

**Language/Version**: C# with `LangVersion latest` — .NET Framework 4.7.2 (all shell projects), .NET 10 (Engine, Updater, Tests)  
**Primary Dependencies**: VS SDK 15.9.3–17.14.x, WPF, MessagePack, Serilog 4.x, System.Text.Json 8.x, xunit 2.x  
**Storage**: `%AppData%/AKML SQL/config.json` (settings), Windows Credential Manager DPAPI (AI API keys), SQLite via engine history store  
**Testing**: `dotnet test tests/AkmlSql.Core.Tests/` — xunit, .NET 10  
**Target Platform**: Windows 10/11; SSMS 20/21/22; VS 2019/2022/2026  
**Project Type**: VS/SSMS extension (6 shell projects sharing `.projitems`) + out-of-process .NET 10 engine  
**Performance Goals**: Completion suggestions ≤ 500ms; Analysis results ≤ 5s; Search results ≤ 3s  
**Constraints**: Shell must target net472; no `async/await` deadlocks in VS thread model; IPC frames ≤ 16 MB; all shell projects built with MSBuild (not `dotnet build`)  
**Scale/Scope**: Single-user desktop extension; history capped at 1,000 entries; schema cache ≤ 500+ tables

## Constitution Check

*No `constitution.md` found in `.specify/memory/`. Applying CLAUDE.md project guidelines as the effective constitution.*

**Gates evaluated against CLAUDE.md**:

| Gate | Status | Notes |
|---|---|---|
| Shell projects use MSBuild (not dotnet build) | ✅ Pass | All build commands use MSBuild per CLAUDE.md |
| WPF uses ThemeManager — no hardcoded chrome colors | ✅ Pass | Group F and G fixes explicitly use `ThemeManager.Instance` |
| Frozen brushes for all new `SolidColorBrush` | ✅ Pass | Quickstart specifies `Freeze()` pattern |
| Adornment spinner uses Ellipse + StrokeDashArray | ✅ Pass | Group E reuses existing correct spinner from SchemaProgressMargin |
| No `GetAwaiter().GetResult()` in IPC handlers | ✅ Pass | All handlers remain `async Task<RpcMessage?>` |
| `ConfigManager.Load()` result cached (not per-call) | ✅ Pass | No new per-call `Load()` calls introduced |
| Path validation uses `Path.GetFullPath()` canonical check | ✅ Pass | No new file path inputs added |
| API keys stored via DPAPI, not plain-text | ✅ Pass | CredentialManager already implemented; inline help confirms this |

**No violations requiring justification.**

## Project Structure

### Documentation (this feature)

```text
specs/015-bug-fixes-polish/
├── plan.md              # This file
├── research.md          # Phase 0 — root cause analysis for all 14 issues
├── data-model.md        # Phase 1 — entities, fields, state transitions
├── quickstart.md        # Phase 1 — per-group change guide
├── contracts/
│   └── ipc-changes.md   # Phase 1 — IPC contract delta (no new message types)
└── tasks.md             # Phase 2 output (/speckit.tasks — NOT created here)
```

### Source Code — Files Touched by This Feature

```text
src/
├── Directory.Build.props                              # Version scheme: 1.YY.MMDDHHmm
│
├── AkmlSql.Core/
│   └── Config/AppSettings.cs                         # SafetySettings.DropConfirmation default = true
│
├── AkmlSql.Engine/
│   ├── Completion/
│   │   ├── CursorContextAnalyzer.cs                  # Add AlterTableColumn ClauseType + detection
│   │   ├── CompletionEngine.cs                        # Fix UPDATE alias fallback (lines 101-109)
│   │   └── Providers/ColumnProvider.cs               # Add AlterTableColumn to CanHandle
│   └── Navigation/
│       └── NavigationRequestHandler.cs               # Fix Search guard (line 166: add connectionString check)
│
├── AkmlSql.Shell.Shared/
│   ├── Commands/
│   │   └── DocumentOutlineCommand.cs                 # Verify content-type registration
│   ├── Dialogs/
│   │   └── SettingsWindow.cs                         # Dark theme fix (MakeButton hover), AI inline help
│   ├── Editor/SchemaProgress/
│   │   └── SchemaProgressMargin.cs                   # Refactor to AdornmentLayer bottom-right overlay
│   ├── History/
│   │   ├── HistoryToolWindowControl.cs               # Star badge binding, rename discoverability
│   │   ├── HistoryViewModel.cs                       # Add StarredCount computed property
│   │   └── HistorySearchParser.cs                    # Trace/fix CamelCaseTokens path
│   ├── Productivity/DocumentOutline/
│   │   ├── DocumentOutlineViewModel.cs               # Fix buffer attachment + add Refresh trigger
│   │   └── DocumentOutlineControl.xaml               # Add Refresh button (FR-019a)
│   └── Safety/
│       └── ExecutionInterceptor.cs                   # Add suppression warning log
│
└── AkmlSql.Installer/
    └── AkmlSqlSetup.iss                              # Remove desktop shortcut task/icon

build.ps1                                              # Inject version into ISCC + VSIX manifests

# VSIX manifests (version 1.0.0 → dynamic) — 7 files:
src/AkmlSql.Ssms20/source.extension.vsixmanifest
src/AkmlSql.Ssms21/source.extension.vsixmanifest
src/AkmlSql.Ssms22/extension.vsixmanifest
src/AkmlSql.VS2019/source.extension.vsixmanifest
src/AkmlSql.VS2022/extension.vsixmanifest
src/AkmlSql.VS2022/source.extension.vsixmanifest
src/AkmlSql.VS2026/extension.vsixmanifest
```

**Structure Decision**: Existing multi-project shared-projitems structure — no new projects added. All engine changes in `AkmlSql.Engine`, all shell changes in `AkmlSql.Shell.Shared` (shared across all 6 shell targets). Build infrastructure in `Directory.Build.props` + `build.ps1`.

## Implementation Groups

Nine independently deliverable groups, ordered by risk/dependency:

| Group | User Stories | Area | Risk | Dependencies |
|-------|-------------|------|------|-------------|
| A | US1 | IntelliSense: UPDATE SET + ALTER TABLE columns | Medium | Engine only; no shell changes |
| B | US2, US3 | Analysis visibility + Search one-line fix | Low | Engine + shell wiring investigation |
| C | US4 | DROP TABLE safety warning | Low | Config default + suppression logging |
| D | US5, US6, US9 | History: star badge, advanced search, rename label | Low | Shell only |
| E | US7 | Schema progress → bottom-right notification | Medium | Shell WPF; IWpfTextViewMargin → AdornmentLayer |
| F | US8 | Dark theme: dropdown + button hover text | Low | Shell WPF: SettingsWindow.cs only |
| G | US10 | Document Outline: fix empty window + Refresh button | Medium | Shell + engine IPC (already wired) |
| H | US11, US12 | Installer: remove shortcut, version scheme | Low | build.ps1 + .iss + Directory.Build.props |
| I | US13 | AI inline help text | Low | Shell WPF: SettingsWindow.cs only |

### Out of Scope

- **US14 (Installer icon/banner)**: Design deliverable — asset files exist at correct paths. Replace `src/AkmlSql.Installer/assets/{icon.ico,sidebar.bmp,banner.bmp}` when branded assets are provided. No code change required.
- Analysis engine rule logic changes — existing rules are not being modified.
- New AI provider integrations — inline help text only, not new provider wiring.

## Complexity Tracking

No constitution violations requiring justification. No complexity additions beyond the spec.
