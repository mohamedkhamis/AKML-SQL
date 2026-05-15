# Implementation Plan: Phase 10 — SQL Prompt Parity Closure & Bug Fixes

**Branch**: `019-phase10-parity-closure` | **Date**: 2026-05-13 | **Spec**: [spec.md](./spec.md)
**Input**: Feature specification from `/specs/019-phase10-parity-closure/spec.md`

## Summary

Phase 10 closes the verified gap between AKML SQL and Redgate SQL Prompt 11.3 across 14 user stories spanning seven workflow areas (completion UX, analysis surfaces, navigation, refactoring, tab coloring, AI, and code health), finishes the one remaining bug from spec 015 (installer branding), absorbs the in-flight Options Dialog Phase 2 work currently on branch `018-options-dialog-phase2`, completes spec 016's WPF theme refresh across the remaining ~15 WPF surfaces, and resolves the 14 code-level TODOs flagged by the 2026-05-05 codebase audit. The work reuses every existing engine primitive (`SafetyCheckHandler`, `WildcardExpansionHandler`, `AnalysisEngine`, `RefactoringEngine`, `EnvironmentMatcher`, `NoformatScanner`, `AiRequestHandler`, `ThemeRegistry`) and is therefore predominantly shell-side wiring + UI work plus one engine refactor (`PipeRpcServer` dispatch table) and one Core refactor (`AppSettings.cs` per-domain split).

## Technical Context

**Language/Version**: C# 12 (LangVersion `latest`) on .NET Framework 4.7.2 (six shell projects); `AkmlSql.Core` dual-targets `netstandard2.0` + `net10.0`; `AkmlSql.Engine` runs on .NET 10.
**Primary Dependencies**: WPF (PresentationFramework / PresentationCore / WindowsBase via .NET Framework), Visual Studio SDK (15.9.3 for SSMS 20, 16.0.208 for VS 2019, 17.14.x for SSMS 21/22 + VS 2022/2026), MessagePack for IPC, Serilog 4.x, System.Text.Json 8.x, xunit for tests. **No new NuGet packages introduced by this work.**
**Storage**: `%AppData%\AKML SQL\config.json` (existing — extends only the existing nested settings sections; no schema migration). API keys remain DPAPI-encrypted with the `dpapi:` prefix (shipped by spec 015 US13). New AppSettings properties for the 14 user stories are added incrementally to existing nested settings classes.
**Testing**: xunit for `AkmlSql.Core.Tests`, `AkmlSql.Engine.Tests`, `AkmlSql.Formatting.Tests`, and `AkmlSql.E2E.Tests` (current baseline Engine ≥ 867, Core ≥ 526, Formatting ≥ 458). Shell projects continue to have no test harness — shell verification is manual smoke-test against `quickstart.md` plus the static-audit script for chrome-color hex literals (`scripts/audit-wpf-theme.ps1` already shipped by spec 016).
**Target Platform**: Windows; SSMS 20 / 21 / 22, VS 2019 / 2022 / 2026 (six host targets, all consuming `AkmlSql.Shell.Shared` via `.projitems`). The engine is .NET 10 self-contained `win-x64` PublishTrimmed.
**Project Type**: Visual Studio / SSMS extension (desktop, in-process WPF in the shell; out-of-process engine connected by MessagePack-over-named-pipe IPC). No new project subtypes introduced.
**Performance Goals**: Pre-execution safety check completes in < 500 ms for 99% of statements (inherited SC-008 from spec 014). Find Invalid Objects streams partial results within 2 s and completes within 30 s for a 5,000-object database (FR-036 / SC-010). Code Analysis Issues window refreshes within 1 s of pause-in-typing (FR-012). Lightbulb Apply Fix replaces text within 1 s and clears the squiggle (FR-015 / SC-012). Theme switch propagates within 1 s (inherited from spec 016). Command Palette result-set ranks within 250 ms of keystroke. Smart Rename preview generation completes in < 5 s for a column with up to 50 dependents.
**Constraints**: Code-only WPF — no XAML files (per `CLAUDE.md` WPF UI conventions). Single source compiled against six VS SDK versions via `.projitems`. No new IPC `MessageType` integers beyond the three already reserved by spec 014 Phase 2 (`90/190 FindInvalidObjects`, `91/191 FindUnusedVariables`, `92/192 EncryptedObjectDecryption`). WCAG AA contrast for body text and primary actions in both Light and Dark themes (inherited from spec 016). DPAPI key storage already in place; this plan does not relax that constraint. The 8 WinForms dialogs listed in spec.md A11 remain on pre-refresh chrome (spec 016 A-final exclusion). The pre-execution safety check MUST fire across all four execute paths (`F5`, `Shift+F5`, `Alt+Shift+F5`, `Ctrl+Shift+F5` — the latter two newly introduced by US10).
**Scale/Scope**: 14 user stories. 81 functional requirements (FR-001..FR-081). 21 measurable success criteria. ~25 new shell files (Column Picker, Issues window, Smart Rename dialog, AI selection adornment, etc.) plus ~15 WPF surface migrations. 1 engine refactor (PipeRpcServer dispatch table). 1 Core refactor (AppSettings split). 14 code-audit TODOs resolved or deleted. Across 6 host targets via `.projitems`.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

No constitution file exists at `.specify/memory/constitution.md`. Default project conventions documented in `CLAUDE.md` (the **Architecture**, **WPF UI conventions**, **Editor margin spinner pattern**, **Code Conventions**, and **Git Rules** sections) act as the de-facto rule set this plan must respect. Concretely:

- **Code-only WPF** (no XAML) — preserved by all design choices below. New WPF surfaces (Column Picker, Issues window, Smart Rename dialog, AI selection adornment) follow the same construction pattern as `SafetyWarningDialog` / `HistoryDiffWindow` / `ProfileEditorDialog`.
- **`ThemeRegistry` / `ThemeTokens` for all chrome colors** — every new WPF surface added by this plan consumes only `ThemeTokens` keys via `SetResourceReference`. The legacy `ThemeManager` `[Obsolete]` facade is acceptable for migration during the 15 spec-016 surface migrations but no new code introduces a fresh `ThemeManager` consumer.
- **Frozen brushes** — preserved by `ThemeRegistry` consumption pattern. Where semantic colors are intentionally theme-independent (e.g. environment colors like Production red, severity colors in safety dialog), explicit frozen `SolidColorBrush` allocations remain inside the static-audit script's documented exception list.
- **Hoist `FontFamily` to static readonly** — every new WPF surface in this plan reuses the existing `Typography.UiFont` and `Typography.MonoFont` constants from spec 016.
- **Set `Owner` via DTE HWND** for modal dialogs — encapsulated by `ThemeAwareWindow`; the Smart Rename dialog, Column Picker (when shown modally), and any new dialogs inherit from `ThemeAwareWindow`.
- **FR-005 safety-dialog cancel-button discipline** — preserved; the new execution shortcuts in US10 hook the same `ExecutionInterceptor` path that already enforces this.
- **Atomic config writes** — preserved; new AppSettings properties added to existing nested classes do not change the persistence path. `ConfigManager.SaveAsync()` continues to use the `File.Replace` / `File.Move(overwrite:true)` pattern.
- **Engine ↔ shell IPC over named pipe with `[4-byte length][4-byte XOR CRC][MessagePack(RpcMessage)]` framing** — preserved; the three new MessageType ints are already reserved by spec 014 Phase 2 and the new handlers (`FindInvalidObjectsHandler`, `FindUnusedVariablesHandler`, `EncryptedObjectDecryptionHandler`) follow the existing `IMessageHandler` pattern.
- **Implementation-first-with-test-backfill** — per the established convention from specs 010–015. Each user-story phase's task list will include test tasks immediately *after* the implementation tasks they validate, not before.
- **Never run `git add` / `git commit` / `git push` without explicit user approval** — per the user's hard rule and project `CLAUDE.md`. The plan describes implementation but the executing agent will not commit anything without explicit instruction.

**Result**: GATE PASSES. No deviations require justification in the Complexity Tracking section.

**Re-check after Phase 1 design**: GATE STILL PASSES. The Phase 1 artifacts (`data-model.md`, `contracts/commands.md`, `contracts/settings.md`, `quickstart.md`) entrench these conventions — they do not introduce XAML, do not require `Application.Current`, do not mutate frozen brushes, do not change `SafetyWarningDialog` semantics, do not reserve new MessageType integers beyond the three already allocated by spec 014, and do not change the engine ↔ shell IPC frame format. The PipeRpcServer dispatch-table refactor (US14, FR-080) preserves the public IPC contract — the refactor is internal-only.

## Project Structure

### Documentation (this feature)

```text
specs/019-phase10-parity-closure/
├── plan.md              # This file (/speckit.plan command output)
├── spec.md              # Feature spec (already exists)
├── research.md          # Phase 0 output (/speckit.plan command)
├── data-model.md        # Phase 1 output (/speckit.plan command)
├── quickstart.md        # Phase 1 output (/speckit.plan command)
├── contracts/           # Phase 1 output (/speckit.plan command)
│   ├── commands.md          # New command IDs, VSCT chord bindings, host-target placements
│   └── settings.md          # New AppSettings properties with JSON key, type, default
├── checklists/
│   └── requirements.md  # Spec quality checklist (already exists, all items pass)
└── tasks.md             # Phase 2 output (/speckit.tasks — NOT created by /speckit.plan)
```

### Source Code (repository root)

This work touches `src/AkmlSql.Shell.Shared/` (the shared `.projitems`) primarily, with smaller engine and Core changes for the IPC handlers and refactors. Project layout below shows the new and modified subtrees only — unchanged subtrees are omitted.

```text
src/AkmlSql.Shell.Shared/
├── Editor/
│   ├── Completion/
│   │   ├── ColumnPickerControl.cs                       # NEW — US2: multi-select column picker WPF control (Ctrl+Left popup mode)
│   │   ├── ColumnPickerSelection.cs                     # NEW — US2: selection state POCO
│   │   ├── TabWildcardExpansionFilter.cs                # NEW — US2: IOleCommandTarget for Tab after *
│   │   ├── CompletionToggleListener.cs                  # NEW — US11: Ctrl+Shift+P session-toggle state machine
│   │   ├── CompletionCategoryFilter.cs                  # NEW — US11: Ctrl+Up/Down category-cycle
│   │   ├── TempTableSchemaCollector.cs                  # NEW — US11: parse #temp from CREATE/INTO in active script
│   │   └── AkmlCompletionPopup.cs                       # MODIFIED — hosts ColumnPicker, parameter highlight bolding
│   ├── Execution/
│   │   ├── ExecuteCurrentBatchCommand.cs                # NEW — US10: Alt+Shift+F5 wiring
│   │   ├── ExecuteToCursorCommand.cs                    # NEW — US10: Ctrl+Shift+F5 wiring
│   │   └── ExecutionInterceptor.cs                      # MODIFIED — hook the two new execute paths
│   ├── Adornments/
│   │   ├── AiSelectionIconAdornment.cs                  # NEW — US13: AI icon at right edge of selection
│   │   └── LightbulbDetailsPopup.cs                     # NEW — US3: Ctrl-hover popup with rule id + remediation + Apply Fix
│   └── Signature/
│       └── ParameterHighlighter.cs                      # NEW — US11: bold next-expected parameter
├── Tabs/
│   ├── TabContextMenuExtender.cs                        # NEW — US4: Tab Color (Server/Database/Server Group) right-click submenus
│   ├── TabColoringManager.cs                            # MODIFIED — WCAG-AA clamp under High Contrast
│   └── SsmsConnectionContextResolver.cs                 # NEW — US14 (BUG-A8/A10): shared resolver replacing two TODOs
├── Productivity/
│   ├── CodeAnalysisIssuesWindow.cs                      # NEW — US3: dockable tool window
│   ├── CodeAnalysisIssuesPackage.cs                     # NEW — US3: tool window provider registration
│   ├── CrudGenerationDialog.cs                          # NEW — US14 (BUG-A7): schema/table/operation dialog
│   ├── Navigation/
│   │   ├── ScriptOutlineWindow.cs                       # NEW — US7: Ctrl+B,Ctrl+S Summarize Script
│   │   ├── ScriptAsAlterCommand.cs                      # NEW — US7: F12 binding
│   │   ├── SelectInObjectExplorerCommand.cs             # NEW — US7: Ctrl+F12 binding
│   │   ├── FindUnusedVariablesCommand.cs                # NEW — US7: Ctrl+B,Ctrl+F binding
│   │   ├── BrowseOpenTabsPopup.cs                       # NEW — US7: Ctrl+Q
│   │   └── FindInvalidObjectsWindow.cs                  # NEW — US8: dockable tool window
│   ├── Refactoring/
│   │   ├── SmartRenameDialog.cs                         # NEW — US10: F2 / Object Explorer right-click
│   │   ├── BracketsToggleCommand.cs                     # NEW — US10: Ctrl+B,Ctrl+B
│   │   ├── InlineStoredProcedureCommand.cs              # NEW — US10: Ctrl+B,Ctrl+I
│   │   └── EncapsulateAsStoredProcedureCommand.cs       # NEW — US10: Ctrl+B,Ctrl+E
│   └── Grid/
│       ├── GridAccessHelper.cs                          # MODIFIED — US14 (BUG-A11): SSMS 20 fallback path
│       └── ResultGridProductivityAudit.cs               # MODIFIED — US9: verify Copy-as-IN NULL message, Script-as-INSERT IDENTITY toggle, Excel precision
├── CommandPalette/
│   ├── ICommandPaletteSource.cs                         # NEW — US6: source interface
│   ├── CommandPaletteEntry.cs                           # NEW — US6: entry POCO
│   ├── Sources/
│   │   ├── AkmlCommandSource.cs                         # NEW — US6: enumerate registered OleMenuCommands
│   │   ├── AkmlOptionsSource.cs                         # NEW — US6: reflect over AppSettings tagged properties
│   │   ├── HostCommandSource.cs                         # NEW — US6: DTE.Commands enumeration
│   │   └── DatabaseObjectSource.cs                      # NEW — US6: SSMS-only, reads from active DatabaseCache
│   └── CommandPaletteWindow.cs                          # MODIFIED — aggregate four sources, recent-items
├── Formatting/
│   ├── DisableFormattingForSelectionCommand.cs          # NEW — US11: editor action wrapping selection in markers
│   ├── FormatRequestDispatcher.cs                       # NEW — US14 (BUG-A4..A6): shared dispatcher
│   ├── FormatOnSaveHandler.cs                           # MODIFIED — uses FormatRequestDispatcher (or deleted)
│   ├── FormatOnPasteHandler.cs                          # MODIFIED — uses FormatRequestDispatcher (or deleted)
│   └── FormatOnDelimiterHandler.cs                      # MODIFIED — uses FormatRequestDispatcher (or deleted)
├── Ai/
│   ├── AiKeyboardShortcuts.cs                           # NEW — US13: Alt+Z, Shift+Alt+R, Ctrl+Alt+Z, Ctrl+Alt+Up
│   ├── AiHistoryTab.cs                                  # NEW — US13: AI panel history tab UI
│   ├── AiFollowUpButtons.cs                             # NEW — US13: 1-3 follow-up prompt buttons
│   ├── ExplainSqlCommand.cs                             # NEW — US13: right-click selection action
│   ├── QueryIndexAnalysisCommand.cs                     # NEW — US13: ML-based plan analysis
│   ├── AutoFixOnErrorToast.cs                           # NEW — US13: non-blocking toast after execution failure
│   └── CommentToSqlListener.cs                          # NEW — US13: `-- generate:` + Tab trigger
├── Help/
│   ├── F1HelpListener.cs                                # MODIFIED — register every new UI surface
│   └── F1HelpRegistrations.cs                           # NEW — central registration of every surface key
├── Dialogs/
│   └── SettingsWindow.cs                                # MODIFIED — migrate to ThemeTokens; reflect new settings entries
├── Editor/
│   ├── SignatureHelpSource.cs                           # DECISION — US14 (BUG-A1/A3): wire via PipeRpcClient OR delete
│   └── QuickInfoSource.cs                               # DECISION — US14 (BUG-A2): wire via PipeRpcClient OR delete
└── Snippets/SnippetManagerDialog.cs                     # MODIFIED — migrate to ThemeTokens

src/AkmlSql.Engine/
├── Server/
│   ├── PipeRpcServer.cs                                 # MODIFIED — US14 FR-080: replace 55-case switch with Dictionary<int, IMessageHandler>
│   ├── IMessageHandler.cs                               # NEW — US14: handler interface
│   └── HandlerRegistry.cs                               # NEW — US14: handler registration
├── Refactoring/
│   ├── SafetyCheckHandler.cs                            # No change — already covers DELETE/UPDATE/MERGE/JOIN/proc bodies
│   ├── SmartRenameHandler.cs                            # NEW — US10: DB-wide dependency analysis, transactional script generation
│   └── WildcardExpansionHandler.cs                      # No change — engine path used by US2 Tab key wiring
├── Analysis/
│   ├── FindUnusedVariablesHandler.cs                    # NEW — US7: MessageType 91
│   ├── FindInvalidObjectsHandler.cs                     # NEW — US8: MessageType 90
│   ├── EncryptedObjectDecryptionHandler.cs              # NEW — US11: MessageType 92 (DAC-based)
│   └── AnalysisIssueExporter.cs                         # NEW — US3: CSV export logic for Issues window
└── Ai/
    └── AiRequestHandler.cs                              # MODIFIED — comment-to-SQL transport (reuses AiTextToSql); panel-history persistence

src/AkmlSql.Core/
├── Config/
│   ├── AppSettings.cs                                   # MODIFIED — US14 FR-081: split into per-domain files; root <200 lines
│   ├── IntelliSenseSettings.cs                          # NEW (split out) — also extended with completion-polish properties
│   ├── CodeAnalysisSettings.cs                          # NEW (split out) — also extended with US3 lightbulb properties
│   ├── TabSettings.cs                                   # NEW (split out) — also extended with US4 WCAG-clamp property
│   ├── NavigationSettings.cs                            # NEW (split out) — also extended with US7 chord enables
│   ├── CommandPaletteSettings.cs                        # NEW (split out) — also extended with recent-items + 4-source toggles
│   ├── AiSettings.cs                                    # NEW (split out) — also extended with US13 shortcut + comment-to-SQL + auto-fix-on-error
│   ├── SafetySettings.cs                                # NEW (split out)
│   ├── GridSettings.cs                                  # NEW (split out)
│   ├── RefactoringSettings.cs                           # NEW (split out)
│   ├── FormatterSettings.cs                             # NEW (split out) — also extended with US11 marker action
│   ├── HistorySettings.cs                               # NEW (split out)
│   ├── ExecutionProductivitySettings.cs                 # NEW (split out)
│   ├── EditorProductivitySettings.cs                    # NEW (split out)
│   ├── SnippetSettings.cs                               # NEW (split out)
│   ├── CacheSettings.cs                                 # NEW (split out)
│   ├── CompletionPolishSettings.cs                      # MODIFIED — US11: 8 new properties beyond what spec 014 Phase 2 added
│   └── ThemeSettings.cs                                 # NEW (split out)
└── Ipc/Messages/
    ├── FindInvalidObjectsRequest.cs                     # No change — already shipped by spec 014 Phase 2
    ├── FindInvalidObjectsResponse.cs                    # No change — already shipped
    ├── InvalidObjectRecord.cs                           # No change — already shipped
    ├── FindUnusedVariablesRequest.cs                    # No change — already shipped
    ├── FindUnusedVariablesResponse.cs                   # No change — already shipped
    ├── UnusedDeclarationDto.cs                          # No change — already shipped
    ├── EncryptedObjectDecryptionRequest.cs              # No change — already shipped
    └── EncryptedObjectDecryptionResponse.cs             # No change — already shipped

src/AkmlSql.Installer/
├── AkmlSqlSetup.iss                                     # MODIFIED — US5: WizardImageFile + WizardSmallImageFile; T096 native IntelliSense restore
└── assets/
    ├── banner.bmp                                       # NEW or VERIFIED — US5 / FR-021
    └── icon.ico                                         # NEW or VERIFIED — US5 / FR-020

doc/
├── progress.md                                          # MODIFIED — US1 FR-002: replace "100% parity" with pointer to PRD §3
├── bugs.md                                              # MODIFIED — US1 FR-004: append historical-closure note
├── AKML_SQL_Gap_Analysis_1.md                           # MODIFIED — US1 FR-005: "Superseded by Phase 10 PRD §3" banner
└── AKML-SQL-Phase10-SqlPromptParity-and-Bugs-PRD.md     # No change — already authored

CLAUDE.md                                                # MODIFIED — US1 FR-003: Active branch line + Spec 014 status

specs/014-sql-prompt-parity/
└── tasks.md                                             # MODIFIED — US1 FR-006: mark US1/US5 [X], point at this spec for remaining

tests/AkmlSql.Engine.Tests/
├── Refactoring/
│   ├── SmartRenameHandlerTests.cs                       # NEW — US10
│   └── ScriptOutlineBuilderTests.cs                     # NEW — US7 Summarize Script
├── Analysis/
│   ├── FindInvalidObjectsHandlerTests.cs                # NEW — US8
│   ├── FindUnusedVariablesHandlerTests.cs               # NEW — US7
│   └── EncryptedObjectDecryptionHandlerTests.cs         # NEW — US11
├── Completion/
│   ├── TabWildcardExpansionContextTests.cs              # NEW — US2 Tab-after-* context detection
│   ├── ColumnPickerSelectionTests.cs                    # NEW — US2 picker selection / qualification logic
│   ├── TempTableSchemaCollectorTests.cs                 # NEW — US11 #temp parser
│   └── CompletionCategoryFilterTests.cs                 # NEW — US11 Ctrl+Up/Down cycle
└── Server/
    └── PipeRpcServerDispatchTests.cs                    # NEW — US14 FR-080 dispatch table

tests/AkmlSql.Core.Tests/
├── Config/
│   ├── AppSettingsRoundTripTests.cs                     # MODIFIED — round-trip after the per-domain split
│   └── CompletionPolishSettingsTests.cs                 # MODIFIED — 8 new properties (US11)
└── CommandPalette/
    └── FuzzyMatcherCommandPaletteTests.cs               # NEW — US6 fuzzy ranking across 4 sources

scripts/
└── audit-wpf-theme.ps1                                  # No change — already shipped by spec 016; consumed by SC-015
```

**Structure Decision**: The work is distributed across `Shell.Shared` (most user-visible features), `Engine` (3 new handlers + 1 dispatch refactor), and `Core` (settings split + extensions). The host-specific shell projects (`AkmlSql.Ssms20/21/22`, `AkmlSql.VS2019/2022/2026`) only receive VSCT updates for new command IDs (see `contracts/commands.md`) — no per-host code is added. No new top-level projects are introduced; no new NuGet packages; no new IPC `MessageType` integers (the three Phase 10 needs were already reserved by spec 014 Phase 2 at `90/91/92`).

## Complexity Tracking

> Constitution Check passed; no violations to justify.

(table omitted — no entries)
