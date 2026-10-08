# Implementation Plan: SQL Prompt UI/UX parity for Options, SQL History and format styles

**Branch**: `040-sqlprompt-ui-parity` | **Date**: 2026-09-27 | **Spec**: [spec.md](./spec.md)
**Input**: Feature specification from `/specs/040-sqlprompt-ui-parity/spec.md`; gap plan `doc/_Prompt-Gap/11-UI-UX-Plan-Options-History-Styles.md`

## Summary

This feature makes the SSMS 22 plugin's **Options window**, **SQL History window** and
**format style editing** match Redgate SQL Prompt's interaction patterns. First, though, it
makes those screens honest. It delivers all 38 gap-plan items in seven slices, in priority
order (spec FR-071).

**Approach:** repair the data behind the screens, then reshape the screens, reusing seams the
codebase already has.

**Options**
- Hide or wire every setting that does nothing (R1).
- Make the Theme drop-down incapable of saving behind Cancel (R2).
- Reset pages through their own `Save`, so hidden data survives (R3).
- Rename the tree around SQL Prompt's layout without changing page keys (R4).
- Add parent gating, number fields, palette toggles, an environments grid and themed
  sub-windows (R5–R9).

**SQL History**
- Add schema v3 to the shared SQLite store: owner-aware open state, the missing FTS update
  trigger, and a one-time repair of the index, timestamps and orphan versions (R11, R14, R15).
- Make row actions group-scoped (R14).
- Show a full-text preview (R10).
- Fix paging (R12).
- Widen search and add Advanced search (R13, R18).
- Build restore-on-start on history, replacing the never-wired session-recovery path (R17).
- Add live refresh and full keyboard use (R20, R21).

**Format styles**
- Make labels readable, the preview tab-true, and Import / Export honest (R22).
- Add option search and SQL Prompt-default change markers ported from the web editor (R23).
- Add an Active Style menu built from command slots in the DTE menu users actually see (R24).
- Say which style formatted the code (R30).
- Add a team style folder (R28).
- Add a Format SQL actions list limited to actions the formatter can perform (R29).

**Shared**
- A declarative, grouped AKML SQL menu (R25).
- F1 wired to the docs site, with a new Options topic (R26).
- One window-title helper, and accessible names on icon buttons (R27).
- One `SqlPreviewView` control serving both History and the style editor (R20).

## Technical Context

**Language/Version**: C# — `net472` (shell, LangVersion latest, programmatic WPF, no XAML); `netstandard2.0` + `net10.0` (Core); `net10.0` (engine, IntelliSense, Formatting, Analysis)
**Primary Dependencies**: existing only — VS SDK 17.14.x (EnvDTE CommandBars, `OleMenuCommand`, `ToolWindowPane`), WPF, WinForms (`ColorDialog` only), MessagePack (IPC), Microsoft.Data.Sqlite + FTS5 (history), System.Text.Json (config), Serilog
**Storage**: `%AppData%/AKML SQL/config.json` (`AppSettings`, atomic writes); `%AppData%/AKML SQL/history/sqlhistory.db` (SQLite WAL, FTS5, **shared with the web engine and with every SSMS instance's engine**); `%AppData%/AKML SQL/profiles` plus an optional team style folder
**Testing**: xunit 2.x:
- `AkmlSql.Core.Tests` (DTOs, LineDiff, date groups, environments migration);
- `AkmlSql.Engine.Tests` (history v3, group actions, reconcile, search, drafts, team styles, format actions, completion wiring);
- `AkmlSql.IntelliSense.Tests`;
- `AkmlSql.Formatting.Tests`;
- `AkmlSql.Shell.Shared.Tests` (net472, `[StaFact]`, compiles the Shell.Shared projitems);
- `AkmlSql.Site.Tests` (F1 slugs);
- `AkmlSql.UiTests` (FlaUI, manual, deployed build).

Ratchets: completion corpus and format-parity goldens.
**Target Platform**: Windows x64 — SSMS 22 shell (in-process, net472) over the out-of-process .NET 10 engine
**Project Type**: Desktop IDE extension with an out-of-process engine (shell ↔ engine over named pipe + MessagePack)
**Performance Goals**:
- History search as you type returns within 250 ms after the pause for 100,000 entries (spec SC-006/SC-014).
- A new run appears in an open History window within 1 s.
- The Options window opens within its current time.
- No new per-keystroke disk I/O in the host.
- The style search filters within one frame for 115 options.

**Constraints**:
- No new IPC message types, only additive keys and action codes.
- No new settings store.
- The history schema change must be idempotent and safe under two engines on one file.
- Web edition code must not change (FR-070).
- Defaults reproduce today's output, so the corpora don't move.
- Core stays `netstandard2.0`-safe.
- Shell builds with full MSBuild only.
- New shell files are listed in `AkmlSql.Shell.Shared.projitems`.

**Scale/Scope**: 38 gap-plan items, 7 slices, about 27 new source and doc files, about 70 new test files and about 70 modified files; about 41 working days.

## Constitution Check

*GATE: evaluated before Phase 0, re-evaluated after Phase 1 design. Constitution v1.0.0.*

| Principle | Pre-design | Post-design | Notes |
|---|---|---|---|
| **I. Process Isolation & Host Safety** | PASS | PASS | See details below. |
| **II. Build Integrity** | PASS | PASS | No SDK, toolchain or package change. No new theme tokens: History font sizes map onto the existing `Typography` scale, and the new `PageTheme.TextDisabled` reuses the existing `TextDisabled` token, so `generate-theme-css.ps1 -CheckOnly` is untouched. Shell builds with MSBuild; `doc/deployment.md`'s stale MSBuild path is corrected (R33). |
| **III. Tests & Corpora Non-Regressible** | PASS | PASS | See details below. |
| **IV. Git Consent** | PASS | PASS | No git mutation is part of this plan. The branch was created by `/speckit.specify` at the user's request. Work is delivered uncommitted. |
| **V. Simplicity & Convention Fidelity** | PASS | PASS | See the reuse list below. |

**Principle I, in detail**
- Every data change runs in the engine: history schema, group actions, reconcile, search,
  drafts, team style folder, Format SQL actions and completion flags.
- The shell only reads settings, draws UI and sends existing messages with additive keys.
- New shell code is UI:
  - `SqlPreviewView`, which reuses the shell-side `SqlPreviewTokenizer` already used by History;
  - palette entries;
  - menu slots;
  - dialogs.
- Shared logic goes in Core, not the shell: `LineDiff`, `HistoryDateGroups` and
  `EnvironmentMatcher`.
- The only shell-side work of any size is the draft/autosave timer, which reuses the existing
  snapshot message.

**Principle III, in detail**
- Every slice lands with tests in the matching `tests/` project.
- Completion wiring keeps every default equal to today's behaviour: max 50, fuzzy on, all
  detail parts on. `CorpusGateTests` (1,342 cases, about 97.5 %) must not drop.
- Format SQL actions only apply to interactive requests. The pipeline API and CLI keep
  `profile.FormatActions`, so `tests/format-parity` goldens can't move.
- `HistoryVersionSnapshotBySourceTests`, which pins the old space timestamp, is updated as a
  deliberate behaviour change (R15). That is not a corpus.

**Simplicity evidence** (Principle V)

1. **Reset** reuses each page's own `Load`/`Save` (`IPageControls.Reset` already existed and was
   never called). It deletes the 80-line reset switch instead of growing it (R3).
2. **Palette options** reuse the Options search index and page controls; no second
   per-setting binding table (R7).
3. **Restore on start** reuses the history store and snapshot message. The never-wired
   session-recovery classes are removed rather than revived (R17).
4. **One preview control** serves History and the style editor (R20).
5. **Change markers** port the web editor's rules instead of inventing new ones (R23).
6. **The Active Style menu** uses `OleMenuCommand` slots, the same `BeforeQueryStatus`
   technique as `PinTabCommand`. No new menu framework (R24).
7. **Group actions** add one optional key to the existing request, not new action codes (R14).
8. **Settings that do nothing** are hidden with their properties kept. No "coming soon"
   mechanism (R1).

**Deviation declared**: none. Complexity Tracking is empty.

**Post-design re-check note**
- Phase 1 added:
  - one history schema version (v3, additive plus a one-time repair);
  - four Core types (`FormatSqlActionsDto`, `TabEnvironment`, `HistoryDateGroups`, `LineDiff`);
  - one shell control (`SqlPreviewView`);
  - one enum value (`NotExecuted`);
  - additive MessagePack keys on seven existing DTOs.
- It removes the unwired session-recovery path and the reset switch.
- The gates stand.
- Watch item for implementation: the shared history file (two engines, several SSMS
  instances). The v3 migration must stay `BEGIN IMMEDIATE`, idempotent and flag-guarded, and
  `open_pid` owner rules must never clear another live instance's rows (R11).

## Project Structure

### Documentation (this feature)

```text
specs/040-sqlprompt-ui-parity/
├── plan.md              # This file
├── spec.md              # Feature specification (7 user stories, 48 FRs, 17 SCs, clarifications)
├── research.md          # Phase 0 — findings N1..N10, decisions R1..R33
├── data-model.md        # Phase 1 — settings, history schema v3, IPC keys, view models, state machines
├── quickstart.md        # Phase 1 — 49 manual validation scenarios (acceptance gate)
├── contracts/
│   ├── ipc.md           # Additive MessagePack keys and action codes
│   └── ui.md            # Options tree, AKML SQL menu, keyboard maps, exact texts, window titles
├── checklists/
│   └── requirements.md  # Spec quality checklist (all pass)
└── tasks.md             # Phase 2 output (/speckit.tasks — NOT created here)
```

### Source Code (repository root)

Slices: **[A]** US1 Options trust · **[B]** US2 History trust · **[C]** US3 style editor
trust · **[D]** US4 style patterns · **[E]** US5 History patterns · **[F]** US6 Options
patterns · **[G]** US7 polish and sharing.

```text
src/AkmlSql.Core/
├── Config/AppSettings.cs                         [A][E][F][G] new props: History.{MaxQuerySizeKb,RestoreMaxQueries,
│                                                   ReconnectRestoredQueries,RememberAdvancedSearch,AdvancedSearch},
│                                                   Tabs.Environments, ColoringRule.Environment,
│                                                   Formatter.{TeamStyleFolder,FormatSqlActions}
├── Config/ConfigManager.cs                       [F] environments migration on Load; [A] PreserveInstallState helper
├── Config/WindowTitles.cs                        [G] NEW — "AKML SQL – " + name
├── Config/TeamStyleFolderValidator.cs            [G] NEW — rooted/canonical check for the team style folder
├── Config/FormatSqlActionsMapper.cs              [G] NEW — settings → FormatSqlActionsDto
├── Ipc/Messages/HistoryActionRequest.cs          [B][E] keys 9–12; codes 11 ReconcileOpen, 12 GetFilterValues
├── Ipc/Messages/HistoryActionResponse.cs         [B][E] keys 8–10  (file holding the response type)
├── Ipc/Messages/HistoryEntryDto.cs               [B] key 17 SessionKey
├── Ipc/Messages/HistoryRecordRequest.cs          [E] key 12 IsDraft
├── Ipc/Messages/HistorySearchRequest.cs          [E] key 13 PathFilter
├── Ipc/Messages/FormatRequest.cs                 [G] key 5 Actions
├── Ipc/Messages/FormatSelectionRequest.cs        [G] key 5 Actions
├── Ipc/Messages/FormatSelectionResponse.cs       [D] key 7 ProfileFallbackWarning
├── Ipc/Messages/FormatSqlActionsDto.cs           [G] NEW
├── Ipc/Messages/ProfileInfo.cs                   [G] keys 9 Source, 10 IsReadOnly
├── Models/History/ExecutionStatus.cs             [E] NotExecuted = 3
├── Models/History/HistoryDateGroups.cs           [E] NEW — Today/Yesterday/This week/Last week/This month/Older
├── Models/Tabs/EnvironmentMatcher.cs             [F] server AND database for server rules with DatabaseName
├── Models/Tabs/TabEnvironment.cs                 [F] NEW
├── Models/Tabs/EnvironmentValidator.cs           [F] NEW — unique names, #RRGGBB, in-use delete check
├── Models/Tabs/ColoringRuleOrdering.cs           [F] NEW — move rules, renumber Order 0..n-1
└── Text/LineDiff.cs                              [E] NEW — LCS line diff

src/AkmlSql.Engine/
├── History/HistoryDatabase.cs                    [B] schema v3 (open_pid, history_au trigger, repair), group-scoped
│                                                   delete/favourite/versions, SetOpenStatus by session, ReconcileOpen,
│                                                   ISO timestamps in SaveVersion, grouped filters;
│                                                   [E] OR-LIKE search scope, PathFilter, date, GetFilterValues, drafts
├── History/HistoryRequestHandler.cs              [B][E] new keys and codes, DeletedCount
├── Handlers/Completion/CompletionHandler.cs      [A] MaxSuggestions, FuzzyMatch, detail flags per request
├── Formatter/FormatRequestHandler.cs             [A] RespectNoformat from request; [D] selection fallback warning;
│                                                   [G] honour FormatSqlActionsDto, team read-only refusals
├── Handlers/Formatting/FormattingHandlers.cs     [G] pass SchemaCache/Sessions to HandleFormat
├── EngineHandlerRegistry.cs                      [G] ProfileManager team-folder provider
└── Sessions/*                                    [E] remove session-storage handler if nothing else uses it (check web first)

src/AkmlSql.IntelliSense/Completion/
├── CompletionEngine.cs                           [A] FuzzyMatchEnabled, detail flags plumbing
├── Providers/ColumnProvider.cs                   [A] FormatSecondaryText instance + flags
└── Providers/ObjectProvider.cs                   [A] FK text follows ShowPkFk

src/AkmlSql.Formatting/
├── Profiles/ProfileManager.cs                    [G] third (team, read-only) directory, source/readonly in List
└── Pipeline/FormatterPipeline.cs                 [G] ApplyLayout / ApplyCasing gates for interactive actions

src/AkmlSql.Shell.Shared/
├── AkmlSql.Shell.Shared.projitems                [all] register new files
├── Commands/RegisteredCommands.cs                [D] NEW — every command id the package registers (menu-table test source)
├── Commands/AkmlMenuTable.cs                     [G] NEW — declarative AKML SQL menu (contracts/ui.md §2)
├── Editor/Completion/CompletionTriggerPolicy.cs  [A] NEW — pure trigger decision (delay, after-dot, Ctrl+Space)
├── Snippets/SnippetGate.cs                       [A] NEW — pure snippet switches
├── History/OpenStateReporter.cs                  [B] NEW — ReconcileOpen request and open/close decisions
├── History/HistoryRestoreState.cs                [B] NEW — holds RestorableEntryIds from startup
├── History/VersionLoadGuard.cs                   [B] NEW — drops stale version-list responses
├── History/DraftCapturePolicy.cs                 [E] NEW — draft, truncation and autosave rules
├── Dialogs/SettingsWindow.cs                     [A] theme handler + loading flag + WorkingCopy, reset via page Save,
│                                                   PreserveInstallState, readable confirmations, Import text;
│                                                   [F] tree (labels only), BuildOptionsCatalog, FlashRow any Panel,
│                                                   Color grid host; [G] F1 → page HelpTopic
├── Dialogs/Pages/IPageBuilder.cs                 [G] + HelpTopic
├── Dialogs/Pages/RowFactory.cs                   [F] parent gating, AddNumber, themed AddButton
├── Ui/Theme/PageTheme.cs                         [F] + TextDisabled, + HighContrast palette
├── Dialogs/Pages/*Page.cs                        [A] remove dead rows (and their RegisterSearch); labels, restart notes;
│                                                   [F] gating parents, number rows; [E] History page gains restore rows;
│                                                   [G] Format page: team folder, Format SQL actions
├── Dialogs/Pages/TabsPage.cs → Color page        [F] environments grid, Edit environments dialog
├── Dialogs/EditEnvironmentsDialog.cs             [F] NEW (ThemeAwareWindow, colour picker)
├── Dialogs/Pages/AiAgentListView.cs              [F] themed buttons
├── Commands/OptionsCommand.cs                    [A] reopen with working copy, theme restore on Cancel/OK;
│                                                   [F] ShowOptions(pageKey, agentId, focusLabel) overload;
│                                                   [D] status-bar refresh in SaveAndNotify
├── Analysis/ManageRulesDialog.cs                 [F] WPF ThemeAwareWindow port (same inputs/outputs)
├── Analysis/ErrorListReporter.cs                 [A] ShowInErrorList gate + live re-apply
├── Analysis/AnalysisController.cs                [A] RunOnType gate
├── Editor/Completion/CompletionController.cs     [A] trigger delay debounce, AfterDot, PrefixOnly latch, FilterText
├── Editor/Completion/AkmlCompletionPopup.cs      [A] prefix-only filtering
├── Editor/Completion/CompletionItemModel.cs      [A] FilterText
├── Formatting/FormatStylesEditorWindow.cs        [C] rows (CheckBox content, shared label column), import/export
│                                                   sequencing; [D] search box, change markers, key bindings,
│                                                   SqlPreviewView; [G] team group, read-only, F1
├── Formatting/FormatStylesEditorViewModel.cs     [C] tab expansion; [D] schema model index, ChangedCount/IsChanged/
│                                                   ResetOption, moved lines, recomputed IsDirty
├── Formatting/StyleNameDialog.cs                 [D] live validation with existing names
├── Formatting/ActiveStyleCache.cs                [D] NEW
├── Formatting/ActiveStyleMenuCommands.cs         [D] NEW — 30 slots + Edit Styles…
├── Formatting/FormatDocumentCommand.cs           [A] Enabled gate; [D] "Formatted with"; [G] Actions + real SessionId
├── Formatting/FormatSelectionCommand.cs          [A][D][G] same, plus fallback warning
├── Formatting/FormatActionHelper.cs              [A] Formatter.Enabled gate
├── StatusBar/StatusBarManager.cs                 [D] ShowTransient, toggle respected
├── History/HistoryToolWindowControl.cs           [B] open bar, preview via SqlPreviewView (full text), row-bound menu;
│                                                   [E] search box + "?", Advanced panel, chips, rows with db/env,
│                                                   date groups with counts, versions actions, keyboard, overflow menu,
│                                                   spinner, Retry; [G] AutomationProperties, Typography, tokens, F1
├── History/HistoryViewModel.cs                   [B] HasMore fix, preview cache, group-scoped actions;
│                                                   [E] debounce, advanced filters, keep selection/scroll, HistoryRecorded
├── History/HistoryDiffWindow.cs                  [E] LineDiff highlighting, labelled sides
├── History/ExecutionCapture.cs                   [B] record as request, SetOpenStatus on execute/activate/close,
│                                                   ReconcileOpen at connect, shutdown flag; [E] drafts + autosave
│                                                   snapshots, HistoryRecorded event, MaxQuerySizeKb
├── History/DocumentSessionKeys.cs                [B] TryGet, Adopt (refuses a key another tab holds), TryFindDocument
├── History/HistorySearchParser.cs                [E] path:, date:[…], help table
├── History/RestoreQueriesDialog.cs               [E] NEW — themed WPF restore prompt
├── History/HistoryRestoreService.cs              [E] NEW — startup restore (Always/Prompt/Never, max N, reconnect)
├── Commands/RestoreClosedTabCommand.cs           [E] fall back to the most recent closed history entry
├── Sessions/SessionAutoSave.cs, SessionRecovery*  [E] REMOVE (never wired; replaced by R17)
├── Productivity/CommandPalette/*                 [F] Options category, OptionPaletteEntry, toggle-in-place
├── Tabs/HexBrush.cs                              [E] NEW — shared hex → frozen brush (from TabColoringManager)
├── Tabs/TabColoringManager.cs                    [E][F] use HexBrush; environments
├── Help/F1HelpRegistrations.cs, HelpBinding.cs   [G] docs base URL, slugs; HelpBinding NEW
├── Ui/SqlPreview/SqlPreviewView.cs               [E][D] NEW — selectable coloured preview, gutter, highlights, tabs
└── Ui/Theme/ThemedButton.cs                      [F] PageTheme overloads

src/AkmlSql.Ssms22/
├── AkmlSqlPackage.cs                             [G] declarative EnsureTopLevelMenu (v2 marker, submenus, separators),
│                                                   editor context bar; [D] Active Style slots; [E] restore at start,
│                                                   shutdown hook; [B] ReconcileOpen after engine connects
└── AkmlSqlSsms22.vsct                            [D][G] slot buttons 0x0920–0x093E, groups mirroring contracts/ui.md §2

doc/
├── topics/options.md                             [G] NEW — one section per Options page (F1 targets)
├── topics/formatting.md, topics/sql-history.md   [D][E][G] updated for the new behaviour
├── deployment.md                                 [G] stale MSBuild path corrected
└── progress.md                                   [all] "Spec 040" section

tests/
├── AkmlSql.Core.Tests/{Ipc,Config,Text,History,Tabs}/…   DTO round-trips, migration, LineDiff, date groups, matcher
├── AkmlSql.Engine.Tests/{History,Handlers,Formatter}/…    v3 migration, group actions, handler routing, reconcile, search, drafts,
│                                                           completion flags, team styles, actions, selection warning
├── AkmlSql.IntelliSense.Tests/…                            detail text flags, prefix-only matching
├── AkmlSql.Shell.Shared.Tests/…                            Options theme/reset/reopen loop/tree/gating/number/palette/allow-list,
│                                                           History paging/debounce/row target/open-state decisions/version guard, style rows/tabs/search/
│                                                           markers/import-export/name validation, active style cache
├── AkmlSql.Site.Tests/Docs/F1SlugTests.cs                  every F1 slug and anchor exists
└── AkmlSql.UiTests/                                        tour extended: menu, Active Style, Options light/dark,
                                                            History keyboard, style editor at default size
```

**Structure Decision**:
- Use the existing layout. There is no new project.
- Shell code goes into `AkmlSql.Shell.Shared` (registered in its projitems), and the package
  wiring stays in `AkmlSql.Ssms22`.
- Engine and data work goes into `AkmlSql.Engine`, `AkmlSql.IntelliSense` and
  `AkmlSql.Formatting`.
- Shared types go into `AkmlSql.Core`.
- The web edition (`AkmlSql.Web`) isn't touched.

**Delivery order** (FR-071):
1. [A] + [B] + [C] (P1): merged and verified first.
2. [D] + [E] (P2).
3. [F] + [G] (P3).

The shared `SqlPreviewView` lands with the first slice that needs it, [B] for the History
preview, and is extended in [D] and [E].

## Complexity Tracking

> Constitution Check has no violations. Nothing to justify.
