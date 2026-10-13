# Options dialog "Rebalanced tree" (design B) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Rearrange the SSMS Options dialog as the user-approved design B of `artifacts/options-redesign-demo`: fitted rows on every page, settings moved to where a user looks for them, and in-page tabs on the pages that still run long.

**Architecture:** Pages stay `IPageBuilder` sections composed by `CombinedPage`; a new `TabbedPage` subclass groups sections into `OptionsTabStrip` tabs (the AI page's idiom). RowFactory gains a two-column helper and a tooltip helper so builders can fit rows without new row kinds. Moves are new compositions in `SettingsWindow.RegisterCombinedPages` plus tree leaves; section keys stay stable wherever possible so search, Command Palette ids and F1 anchors keep resolving.

**Tech Stack:** C# / .NET Framework 4.7.2 WPF (programmatic, no XAML), `PageTheme` brushes, xunit 2 + Xunit.StaFact in `tests/AkmlSql.Shell.Shared.Tests`.

**Spec:** the approved demo `artifacts/options-redesign-demo/app.js` (`LAYOUTS.b`) and this conversation's findings; the AI page precedent is commit da7e030.

## Global Constraints

- Every setting present today stays reachable and round-trips through Load/Save (the demo's "Where each setting goes" table is the map). Only the "Related" info row on Join conditions is removed.
- Chrome colours only from `PageTheme`; semantic hex only for status colours. Frozen brushes.
- Keep section page keys and `HelpTopic` anchors wherever a page survives; new combined pages reuse an existing anchor. No docs-site anchor changes.
- Top-level tree names stay SQL Prompt's (spec 040): General, Suggestions, Inserted code, Format, Queries, Editor, Code analysis, AI assistance; Snippets becomes its own leaf.
- Never run git add/commit/push; the user approves commits.
- Shell projects build with MSBuild (`AkmlSql.Ssms22`), tests with `dotnet test tests/AkmlSql.Shell.Shared.Tests`.

## Review Focus

1. A searched or palette-jumped row on a hidden tab must reveal its tab (OptionsTabStrip hook) — covered by the AI page test; each new tabbed page gets one reveal test.
2. A section whose master switch lives on another tab (JOIN rows under "Enable IntelliSense", restore rows under "Enable SQL history") must still grey when the master is off — tests in Tasks 3 and 6.
3. Deep links by section key (`ShowOptions("Safety")`, `("ConnectionsMemory")`, `("History")`) must select the new owner leaf — `OptionsCombinedPagesTests` theory rows updated in each task.
4. Settings moved between builders (BracketMode, AddParentheses to Qualification; defBox to IntelliSense; restore rows to HistoryRestore) must save to the same config fields — round-trip tests per task.
5. The dialog at its 720 px minimum width: two-column grids must compress, not clip — one render check at the end.

---

### Task 1: RowFactory two-column grid and tooltip helpers

**Files:**
- Modify: `src/AkmlSql.Shell.Shared/Dialogs/Pages/RowFactory.cs`
- Test: `tests/AkmlSql.Shell.Shared.Tests/RowFactoryColumnsTests.cs`

**Interfaces:**
- Produces: `public (StackPanel Left, StackPanel Right) AddColumns(StackPanel panel)` — appends a two-star-column Grid to `panel` and returns the two column panels; rows added to them keep the group indent. `public void Tip(FrameworkElement row, string text)` — sets the row's ToolTip (the long explanation the short description no longer carries).

- [ ] Step 1: write the failing test (two columns live in one Grid with two star columns; a toggle added to Left is a descendant of the Grid; Tip sets ToolTip).
- [ ] Step 2: run it, expect compile failure on `AddColumns`.
- [ ] Step 3: implement `AddColumns` (Grid with 2 `1*` columns, right column `Margin = (12,0,0,0)`; column panels are StackPanels) and `Tip`.
- [ ] Step 4: run, expect pass.

### Task 2: TabbedPage

**Files:**
- Modify: `src/AkmlSql.Shell.Shared/Dialogs/Pages/CombinedPages.cs`
- Test: `tests/AkmlSql.Shell.Shared.Tests/TabbedPageTests.cs`

**Interfaces:**
- Produces: `internal abstract class TabbedPage : CombinedPage` with `protected TabbedPage(params (string Label, IPageBuilder[] Sections)[] tabs)`, `IReadOnlyList<(string Label, IReadOnlyList<IPageBuilder> Sections)> Tabs`, `OptionsTabStrip? Strip` (the strip built last; test seam), `protected virtual void OnSectionBuilt(IPageBuilder section, IPageControls controls)`, and a `Build(StackPanel, PageContext)` that builds the strip and each tab's sections into that tab's panel. `CombinedPage.Build` becomes virtual. `SettingsWindow.BuildPages` must call `TabbedPage.BuildSections(hostPanel, ctx, registerSection)` instead of iterating `Sections` itself — add `public virtual IReadOnlyList<(string Key, IPageControls Controls)> BuildSections(StackPanel panel, PageContext ctx)` on CombinedPage (default: sequential) and override in TabbedPage; SettingsWindow stores each returned pair under its key.

- [ ] Step 1: failing test: a TabbedPage with two tabs builds an OptionsTabStrip with 2 panels; section rows of tab 1 are descendants of `Strip.Panels[1]`; BuildSections returns one pair per section in tab order.
- [ ] Step 2: run, expect compile failure.
- [ ] Step 3: implement.
- [ ] Step 4: run, pass. Update `SettingsWindow.BuildPages` to use `BuildSections` (behaviour unchanged for plain CombinedPages).

### Task 3: Suggestions › Behavior as two tabs

**Files:**
- Modify: `IntelliSensePage.cs` (fitted rows; absorbs the definition-box toggle; exposes `IntelliSenseControls.Enabled`), `CombinedPages.cs` (`SuggestionsBehaviorPage : TabbedPage`), `SettingsWindow.cs` (registration)
- Create: `JoinAssistancePage.cs` (key `JoinOptions`, rows: JOIN clause assistance, Suggest table aliases, Use matching column names; `MasterSwitch` property for greying)
- Delete: `CompletionPolishPage.cs`, `JoinCompletionPage.cs`
- Tests: new `SuggestionsBehaviorLayoutTests.cs`; update `HelpRoutingTests` (drop CompletionPolish), `OptionsCombinedPagesTests` (drop CompletionPolish row), `OptionsConnectionsHelpTests:106` (IntelliSense), `OptionsLiveSettingsTests:51` (IntelliSense).

- [ ] Failing tests: page has tabs "Completion" and "JOIN assistance"; the definition-box checkbox saves `CompletionPolish.ShowObjectDefinitionBox`; JOIN rows live on tab 1 and grey when Enable IntelliSense is off; the three JOIN settings round-trip.
- [ ] Implement; run the four updated test classes; pass.

### Task 4: Inserted code as three tabs

**Files:**
- Modify: `QualificationPage.cs` (gains Bracket identifiers + Add parentheses; Display "Inserted code › Qualification & brackets"), `SpecialCharactersPage.cs` (closing characters only; Display "Inserted code › Closing characters"), `InsertStatementsPage.cs` (cols2), `AliasesPage.cs` (boxes side by side), `CombinedPages.cs` (`InsertedCodePage : TabbedPage` Statements / Aliases / Closing characters)
- Tests: new `InsertedCodeLayoutTests.cs`; update `OptionsLiveSettingsTests:61-62` (Qualification).

- [ ] Failing tests: three tabs; BracketMode and AddParentheses round-trip through the Qualification section; closing-character rows on tab 2.

### Task 5: Queries › Execution & warnings

**Files:**
- Modify: `SafetyPage.cs` (Display "Queries › Execution & warnings › Warnings", Title "Warnings", cols2), `GridPage.cs`, `ExecutionPage.cs` (fitted, Excel toggle merged into Results grid, Notifications merged into Execution), `CombinedPages.cs` (`ResultsExecutionPage : TabbedPage` {Results & execution: Grid, Execution; Warnings: Safety}; Display "Queries › Execution & warnings"), `SettingsWindow.cs` (tree: remove Suggestions › Warnings leaf, rename Queries leaf; BuildPages: drop "Safety")
- Tests: `OptionsNavStructureTests.Expected`, `OptionsCombinedPagesTests` (+ Safety → ResultsExecution), new `QueriesLayoutTests.cs`.

### Task 6: History as two tabs

**Files:**
- Create: `HistoryRestorePage.cs` (key `HistoryRestore`, Display "Queries › History › Restoring queries", HelpTopic `topics/options#queries-history`; rows Restore on start, When restoring, Max to restore, Reconnect, Auto-save, Max closed tabs; `MasterSwitch`)
- Modify: `HistoryPage.cs` (Recording + Storage only, Display "Queries › History › Recording"; `HistoryControls.Enabled` seam), `CombinedPages.cs` (`HistoryAllPage : TabbedPage` key `HistoryAll`, Display "Queries › History"), `SettingsWindow.cs` (register HistoryRestore; tree leaf key HistoryAll; BuildPages "HistoryAll")
- Tests: `OptionsNavStructureTests`, `HelpRoutingTests` (+HistoryAll, +HistoryRestore), `OptionsCombinedPagesTests` (+History/HistoryRestore → HistoryAll), new `HistoryLayoutTests.cs` (restore settings round-trip; greying).

### Task 7: General gains SQL Server connections; Snippets leaf; Lists & scope

**Files:**
- Modify: `CombinedPages.cs` (`GeneralAllPage : CombinedPage(General, ConnectionsMemory)` key `GeneralAll`; `SuggestionsListsPage(SuggestionTypes, ConnectionScope)` Display "Suggestions › Lists & scope"), `ConnectionsMemoryPage.cs` (Display "General › SQL Server connections", header), `SnippetsPage.cs` (Display "Snippets"), `GeneralPage.cs`, `SuggestionTypesPage.cs`, `ConnectionScopePage.cs` (fitted), `SettingsWindow.cs` (tree, BuildPages, theme wiring for a combined General)
- Tests: `OptionsNavStructureTests`, `HelpRoutingTests` (+GeneralAll), `OptionsCombinedPagesTests` (General/ConnectionsMemory → GeneralAll; Snippets no longer a section), new `GeneralLayoutTests.cs` (theme combo still wired on the combined page; SQL creds round-trip).

### Task 8: Fitted rows on Format, Editor, Code analysis, Color

**Files:** `FormattingPage.cs` (FormatSqlRows: toggles cols2, dropdowns paired), `EditorPage.cs`, `RefactoringPage.cs`, `CodeAnalysisPage.cs`, `TabsPage.cs` (short descriptions only).
- Tests: existing suites stay green; `ScreensPerPageTests.cs` measures every page's content height at 591 px and asserts none exceeds 1.5 × 495 px except Color (the rules grid) — the fit gate.

### Task 9: Window, docs, verification

- `SettingsWindow.cs` final tree and registration; `specs/040-sqlprompt-ui-parity/contracts/ui.md` tree table rows; `doc/progress.md` entry.
- Full shell suite, Release MSBuild of `AkmlSql.Ssms22`, render tour at 880 and 720 px (throwaway test, deleted after).
