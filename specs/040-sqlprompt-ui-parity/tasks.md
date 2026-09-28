---
description: "Task list for 040-sqlprompt-ui-parity"
---

# Tasks: SQL Prompt UI/UX parity for Options, SQL History and format styles

**Input**: Design documents from `/specs/040-sqlprompt-ui-parity/`
**Prerequisites**: [plan.md](./plan.md), [spec.md](./spec.md), [research.md](./research.md), [data-model.md](./data-model.md), [contracts/ipc.md](./contracts/ipc.md), [contracts/ui.md](./contracts/ui.md), [quickstart.md](./quickstart.md)
**Background**: `doc/_Prompt-Gap/11-UI-UX-Plan-Options-History-Styles.md` (the gap plan: items OPT-01…09, HIS-01…14, STY-01…11, X-01…04, with reproduction steps and code evidence)

**Tests are not optional here.** Constitution III requires new behaviour to land with tests. In
each story, write the test tasks first, see them fail, then implement.

**Organization**: one phase per user story, in spec priority order: US1–US3 = P1, US4–US5 = P2,
US6–US7 = P3. Deliver P1 before P2, and P2 before P3 (FR-071).

## Format: `[ID] [P?] [Story] Description`

- **[P]**: can run in parallel: a different file, with no dependency on an unfinished task.
- **[Story]**: `[US1]`…`[US7]`, mapping to the user stories in spec.md.
- Line numbers (`~:123`) are from `master` at `e75771b` and will drift. Search for the named
  method if they no longer match.

## Path conventions and rules for every task

- Repository root: `C:\Repos\AKML\AKML-SQL`. Sources are under `src/`, tests under `tests/`.
- **Shell code** lives in `src/AkmlSql.Shell.Shared` and is compiled into
  `src/AkmlSql.Ssms22` and into `tests/AkmlSql.Shell.Shared.Tests` (net472) through
  `src/AkmlSql.Shell.Shared/AkmlSql.Shell.Shared.projitems`.
  - **Every new `.cs` file under Shell.Shared MUST be added** as a `<Compile Include>` entry in
    that projitems file. Every deleted one MUST be removed from it.
- **Build shell projects with full MSBuild only, never `dotnet build`:**
  ```bash
  MSBUILD="/c/Program Files/Microsoft Visual Studio/18/Insiders/MSBuild/Current/Bin/MSBuild.exe"
  "$MSBUILD" AKML-SQL.slnx -t:Restore -v:quiet && "$MSBUILD" AKML-SQL.slnx -t:Build -p:Configuration=Release -m -v:minimal
  ```
- **WPF is built in code (no XAML).**
  - Colours come from `ThemeRegistry` / `ThemeTokens` through `SetResourceReference`, or, inside
    the Options window, from `PageTheme`. Never hard-code chrome colours.
  - Fonts come from `Typography`, spacing from `Spacing`.
  - Freeze any `SolidColorBrush` you create.
- **Core (`src/AkmlSql.Core`) must stay `netstandard2.0`-safe:** plain `{ get; set; }`, no
  `init`, no records.
- **MessagePack DTOs:** append `[Key(n)]` in order and never renumber. New fields are nullable
  or have safe defaults.
- **Git:** no git commands at all. Leave changes uncommitted (Constitution IV).
- **Screenshots:** Northwind database only.

---

## Phase 1: Setup

**Purpose**: a trustworthy baseline before anything changes.

- [ ] T001 Build the whole solution green in one pass, with the MSBuild commands above, from `C:\Repos\AKML\AKML-SQL`. Fix nothing yet: just confirm it builds.
- [ ] T002 [P] Run `tests/AkmlSql.Core.Tests`, `tests/AkmlSql.Engine.Tests`, `tests/AkmlSql.IntelliSense.Tests`, `tests/AkmlSql.Formatting.Tests`, `tests/AkmlSql.Site.Tests`, `tests/AkmlSql.Web.Tests` (`dotnet test <csproj>`) and `tests/AkmlSql.Shell.Shared.Tests` (MSBuild build, then `dotnet test` the built dll).
  - Record pass/fail counts in `specs/040-sqlprompt-ui-parity/baseline.md`. Include the completion-corpus pass rate (`CorpusGateTests`) and the format-parity golden count.
  - List any failures that were already failing: `PerformanceBaselineTests`, the History 2 ms timing test, `VisualReferenceCoverageTests`, the `sp031-*` goldens.
- [ ] T003 [P] Copy `%AppData%\AKML SQL\config.json`, `%AppData%\AKML SQL\history\` and `%AppData%\AKML SQL\profiles\` to `%UserProfile%\Documents\AKML SQL backups\spec-040-<yyyyMMdd>\`, and record the path in `specs/040-sqlprompt-ui-parity/baseline.md`. Manual checks later reset settings and delete history.

**Checkpoint**: baseline recorded. From now on, any new red in T002's list is a regression this feature caused.

---

## Phase 2: Foundational (blocking prerequisites)

**Purpose**: shared UI pieces that several stories use. **US2, US3, US4, US5 and US6 depend on this phase.**

- [ ] T004 Create the read-only, selectable SQL preview control `SqlPreviewView` in `src/AkmlSql.Shell.Shared/Ui/SqlPreview/SqlPreviewView.cs`, and add it to the projitems. Research R20.
  - **Base:** a `UserControl` containing a gutter `TextBlock` (line numbers) plus a `RichTextBox` (`IsReadOnly=true`, `IsDocumentEnabled=true`, no border, `Typography.MonoFont`, `Typography.Body`) that holds one `FlowDocument` `Paragraph`.
  - **Properties** (plain CLR properties that re-render on set):
    - `string Text`
    - `int TabSize` (default 4)
    - `IReadOnlyList<string> HighlightTerms`
    - `IReadOnlyCollection<int> HighlightLines` (0-based)
    - `bool ShowLineNumbers` (default true)
    - `int MaxDisplayChars` (default 262144)
  - **Rendering:**
    1. Expand tabs with `internal static string ExpandTabs(string text, int tabSize)`, column-aware: a tab advances to the next multiple of `tabSize` within the line.
    2. Split the text into runs with `AkmlSql.Core.Text.SqlPreviewTokenizer.Tokenize`.
    3. Colour each run with `SetResourceReference(TextElement.ForegroundProperty, …)`: keyword → `ThemeTokens.AccentPrimary`, string → `ThemeTokens.StatusSuccess`, comment → `ThemeTokens.TextSecondary`, default → `ThemeTokens.TextPrimary`.
    4. Give runs matching `HighlightTerms` (case-insensitive) the background `ThemeTokens.HistoryMatchHighlight`.
    5. Give lines in `HighlightLines` the background `ThemeTokens.SurfaceSelection`.
    6. When the text is longer than `MaxDisplayChars`, show only that many characters and a final muted line: `— Showing the first 256 KB. Open the query to see all of it.`
  - **Context menu:** "Copy" and "Copy all".
  - **Theme:** call `ThemeRegistry.Instance.AttachTo(this)`.
- [ ] T005 [P] Write `tests/AkmlSql.Shell.Shared.Tests/SqlPreviewViewTests.cs` (`[StaFact]`, in the "AkmlSql ThemeRegistry" collection). Cover:
  - `ExpandTabs("ab\tc", 4) == "ab  c"`, `ExpandTabs("\tx", 2) == "  x"`, and multi-line text where the tab stops restart on each line;
  - keyword, string and comment tokens produce `Run`s whose foreground resolves to the matching token brush;
  - `HighlightLines = {1}` sets that line's background;
  - text over `MaxDisplayChars` ends with the notice line;
  - the `RichTextBox` is read-only but focusable, so text can be selected.
- [ ] T006 [P] Extract a shared hex → frozen brush helper into `src/AkmlSql.Shell.Shared/Tabs/HexBrush.cs`, and add it to the projitems.
  - API: `static bool TryParse(string hex, out Color color)` and `static SolidColorBrush Get(string hex)` (cached per hex string, frozen, falls back to transparent).
  - Replace the private `ParseHexColor` (~:851) and `CreateBrushFromHex` (~:1080) in `src/AkmlSql.Shell.Shared/Tabs/TabColoringManager.cs` with calls to it.
  - Tests: `tests/AkmlSql.Shell.Shared.Tests/HexBrushTests.cs` (valid `#RRGGBB`, invalid input, caching returns the same frozen instance).

**Checkpoint**: shared preview and brush helpers are ready. User stories can start.

---

## Phase 3: User Story 1 — Options I can trust (Priority: P1) 🎯 MVP

**Goal**:
- Every visible Options setting works.
- Restore Defaults resets only what its page shows.
- Cancel really cancels, including after a theme change, Restore all or Import.

It covers gap items OPT-01, OPT-02 and OPT-03.

**Independent Test**: quickstart.md scenarios 1–10.

### Tests for User Story 1 (write first; they must fail)

- [ ] T007 [P] [US1] Write `tests/AkmlSql.Shell.Shared.Tests/OptionsThemeSafetyTests.cs` (`[StaFact]`, `AppDataIsolatedTest`, ThemeRegistry collection). Use the host-variant seam from T015 and `SettingsWindow.TestBuildWindowForRenderTest`. Four cases:
  1. With `theme:"system"` and a Dark host variant, building the window and loading controls leaves `config.json` byte-identical and `ThemeChangeRequested == false`.
  2. With a saved `"dark"` theme, calling `ResetAllToDefaultsCore()` then discarding leaves `config.json` unchanged.
  3. Importing a settings file whose theme differs doesn't write `config.json`.
  4. When the user picks Light while the window is Dark, `ThemeChangeRequested == true`, `WorkingCopy` contains an unsaved edit made on another page, and `config.json` is unchanged.
- [ ] T008 [P] [US1] Write `tests/AkmlSql.Shell.Shared.Tests/OptionsPageResetTests.cs`.
  - **Sentinels:** seed `CodeAnalysis.RuleOverrides["ST001"]={Enabled=false}`, one `Ai.Agents` entry, one `Navigation.ConnectionAliases` entry, `Safety.EnvironmentSeverity["PRODUCTION"]`, and `Formatter.ActiveProfile="Collapsed"`.
  - **For every registered page key:** call `ResetPageToDefaultsCore(key)` and assert every sentinel survives, except the fields that page shows. The Formatting page resets `ActiveProfile`; the AI Assistance page resets the agents.
  - **Unsaved edits:** an unsaved edit on a different page survives a page reset.
  - **Confirmation text:** `ResetConfirmationText(key)` uses the page's display name (e.g. contains "Suggestions › Tooltips" or the post-US6 label), and for AI Assistance contains the agent count.
- [ ] T009 [P] [US1] Write `tests/AkmlSql.Core.Tests/Config/PreserveInstallStateTests.cs`: `ConfigManager.PreserveInstallState(from, to)` copies `InstallId`, `InstalledTargets`, `LastUpdateCheck`, `NativeIntelliSensePrompted`, `DisabledNativeIntelliSense`, `CommandPalette.UsageCounts`, `CommandPalette.RecentItems` and `ConfigVersion`, and nothing else.
- [ ] T010 [P] [US1] Write `tests/AkmlSql.Shell.Shared.Tests/OptionsLiveSettingsTests.cs`.
  - Build the window and read the private `_searchIndex` by reflection (the pattern in `OptionsConnectionsHelpTests.cs:117-131`).
  - Assert every `(PageKey, Label)` is in an explicit allow-list declared in the test. That list is today's labels minus the hidden rows (research R1, spec Appendix A).
  - Assert none of the hidden labels appear: "Keyword casing", "List all database columns after a SELECT statement", "Freeze headers", "Encrypt at rest", "Record failed executions", "Format on paste", "Format on save", "Format on delimiter", "Confirm before bulk format", "Validate formatting preserves semantics", "Respect --noformat regions", "Named regions", "Show preview before applying", "Rename scope", "Chat panel", and the Labs rows.
  - Assert the tree has no leaf tagged `Schema Cache` or `Labs`.
- [ ] T011 [P] [US1] Write `tests/AkmlSql.Engine.Tests/Handlers/CompletionHandlerSettingsTests.cs`, extending the setup in `CompletionHandlerTests.cs`. Assert:
  - `IntelliSense.MaxSuggestions = 10` → at most 10 items for `SELECT * FROM dbo.` on the test schema;
  - `FuzzyMatch = false` → only case-insensitive prefix matches: `unit` finds `UnitPrice` but not `QuantityPerUnit`;
  - `ShowNullability = false` → no `NULL`/`NOT NULL` in `SecondaryText`;
  - all three detail flags off → `SecondaryText` has no type, key or `•` prefix;
  - defaults reproduce today's text exactly.
- [ ] T012 [P] [US1] Write `tests/AkmlSql.IntelliSense.Tests/Completion/ColumnProviderSecondaryTextTests.cs` covering every flag combination of `FormatSecondaryText` (types, nullability, key indicators including IDENTITY/COMPUTED, and the table suffix `" • Products"` only when something precedes it).
- [ ] T013 [P] [US1] Write `tests/AkmlSql.Shell.Shared.Tests/CompletionPrefixOnlyFilterTests.cs`. `CompletionItemModel.MatchesFilter` and `FilterScore` in prefix-only mode:
  - use `FilterText` when it is set (`"p.UnitPrice"` matches `"p.Unit"`);
  - reject substring and CamelCase matches;
  - keep today's behaviour when prefix-only is off.
- [ ] T014 [P] [US1] Write `tests/AkmlSql.Shell.Shared.Tests/ErrorListGateTests.cs` for the pure helper `ErrorListReporter.ShouldPublish(CodeAnalysisSettings)` and for the static registry's `ReapplyAll()` (with a fake reporter that records clear and republish calls).

### Implementation for User Story 1 — OPT-02 (Theme drop-down)

- [ ] T015 [US1] In `src/AkmlSql.Shell.Shared/Ui/Theme/HostThemeWatcher.cs`, add `internal static Func<ThemeVariant>? VariantOverrideForTests` and a read-only accessor `CurrentHostVariant` that returns the override when set and otherwise `LastDetectedHostVariant`.
- [ ] T016 [US1] In `src/AkmlSql.Shell.Shared/Dialogs/SettingsWindow.cs`, fix the Theme handler (research R2):
  1. Add `internal static PageTheme ResolvePageTheme(string? pref)`: "dark" → Dark; "system" → Dark when `HostThemeWatcher.CurrentHostVariant == ThemeVariant.Dark`, else Light; anything else → Light. Use it in the constructor (~:183-184).
  2. Add a `_loadingControls` flag, set with try/finally around the whole body of `LoadSettingsToControls` (~:1953-1974).
  3. In `OnThemeSelectionChanged` (~:1660-1701):
     - return at once while `_loadingControls`;
     - map the index to "dark", "light" or "system";
     - return if `ResolvePageTheme(pick) == _theme`;
     - otherwise run `SaveControlsToSettings()` **without** `ConfigManager.Save`, then `ThemeRegistry.Instance.SetPreference(pick)`, `ThemeChangeRequested = true`, `_dialogResult = true`, and `_window?.Close()`.
  4. Add `internal AppSettings WorkingCopy => _settings;` and `internal string? CurrentPageKey`, the selected tree leaf's `Tag`.
  5. Extract `internal void ResetAllToDefaultsCore()` from `OnResetAllClick` (~:1932-1947). The click handler keeps only the confirmation.
- [ ] T017 [US1] Rework the loop in `OptionsCommand.ShowOptions` (`src/AkmlSql.Shell.Shared/Commands/OptionsCommand.cs` ~:52-92):
  - Capture `var originalTheme = settings.Theme` before the loop.
  - After `ShowDialog`, **check `window.ThemeChangeRequested` first**. When set, continue the loop with `settings = window.WorkingCopy; pageKey = window.CurrentPageKey ?? pageKey;`.
  - On OK: `SaveAndNotify(window.GetSettings())`, then `ThemeRegistry.Instance.SetPreference(settings.Theme)`.
  - On Cancel: `ThemeRegistry.Instance.SetPreference(originalTheme)`, then return false.
  - Keep the method's signature (`Func<string?,string?,bool>` callers in `Ai/AiChatPanel.cs:135` must still compile).

### Implementation for User Story 1 — OPT-03 (Restore Defaults)

- [ ] T018 [P] [US1] Add `public static void PreserveInstallState(AppSettings from, AppSettings to)` to `src/AkmlSql.Core/Config/ConfigManager.cs` (fields as in T009). Use it in `SettingsWindow` Import, replacing the inline copy (~:1780-1782).
- [ ] T019 [US1] Replace the per-page switch in `ResetPageToDefaultsCore(string pageKey)` (`SettingsWindow.cs` ~:1845-1929) with this, so hidden fields survive:
  ```
  if (!_pageControlsByKey.TryGetValue(pageKey, out var controls)) throw new InvalidOperationException(...)
  SaveControlsToSettings()
  _loadingControls = true
  try { controls.Reset(new AppSettings()); controls.Save(_settings); controls.Load(_settings); }
  finally { _loadingControls = false }
  ```
  - For the `"Tabs & UI"` key, also restore `_settings.Tabs.ColoringRules` to `new TabSettings().ColoringRules`, then call `PopulateColoringRulesList()`. The rules are shown on that page.
  - In `OnResetThisPageClick` (~:1813-1836), remove the call that reloads every page (it discarded unsaved edits on other pages).
- [ ] T020 [US1] Add `internal string ResetConfirmationText(string pageKey)` to `SettingsWindow.cs` and use it in `OnResetThisPageClick`.
  - First line: `Reset the settings on {_pageBuilders[pageKey].Display}?`.
  - For `"AI Assistance"` with agents, add a second line: `This also removes your {n} AI agent(s) and their API keys.`
  - For `"Tabs & UI"`, add: `This also restores the default environments and rules.`
  - Never show the raw page key.
- [ ] T021 [US1] In `ResetAllToDefaultsCore()` (`SettingsWindow.cs`): create `var fresh = new AppSettings();`, then `ConfigManager.PreserveInstallState(_settings, fresh); _settings = fresh; LoadSettingsToControls();`. The loading flag from T016 blocks the theme handler.
- [ ] T022 [US1] In `SettingsWindow.cs` Import (~:1788), change the success text to `Settings imported. Click OK to save them, or Cancel to discard.`.
- [ ] T023 [US1] Update `tests/AkmlSql.Shell.Shared.Tests/WindowChromeTests.cs`:
  - Replace `ResetPageToDefaultsCore_HasCaseForEveryRegisteredPageKey` (~:201-235) with `ResetPageToDefaultsCore_ResetsEveryRegisteredPageWithoutThrowing`, which builds the window and calls it for each key.
  - Keep `PageControls_RegisteredForEveryPageBuilder`.

### Implementation for User Story 1 — OPT-01 (wire seven settings, hide the rest)

- [ ] T024 [P] [US1] In `src/AkmlSql.IntelliSense/Completion/Providers/ColumnProvider.cs`:
  - Make `FormatSecondaryText` (~:557-581) an instance method gated by new public bool properties `ShowDataTypes`, `ShowNullability` and `ShowKeyIndicators` (all default true).
    - `TypeDisplay` needs ShowDataTypes; `NULL`/`NOT NULL` needs ShowNullability; `PK`, `IDENTITY` and `COMPUTED` need ShowKeyIndicators.
  - At the call sites (~:314, ~:342, ~:407, ~:525), append the `" • {table}"` suffix only when the formatted part isn't empty, and otherwise use just the table name.
  - Defaults must produce today's text byte-for-byte.
- [ ] T025 [P] [US1] In `src/AkmlSql.IntelliSense/Completion/Providers/ObjectProvider.cs` (~:629-633), add `ShowKeyIndicators` (default true) and show the table-level `🔑 FK ↔ …` text only when it is true. Keep the −500 sort adjustment either way.
- [ ] T026 [US1] In `src/AkmlSql.IntelliSense/Completion/CompletionEngine.cs`:
  - add `public bool FuzzyMatchEnabled { get; set; } = true;`;
  - at the filter (~:502-511), use `.Where(x => FuzzyMatchEnabled ? x.score > 0 : x.score >= 800)` (800 = case-insensitive prefix in `FuzzyMatcher`);
  - add `ShowDataTypes`, `ShowNullability` and `ShowKeyIndicators` properties that push into `_columnProvider` and `_objectProvider` next to where `ColumnScopeMode` is applied (~:447-457).

  Depends on T024 and T025.
- [ ] T027 [US1] In `src/AkmlSql.Engine/Handlers/Completion/CompletionHandler.cs`, after ~:75 (where settings are read for each request), apply:
  - `_engine.SetMaxSuggestions(Math.Clamp(settings.IntelliSense.MaxSuggestions, 5, 200))`
  - `_engine.FuzzyMatchEnabled = settings.IntelliSense.FuzzyMatch`
  - `_engine.ShowDataTypes = settings.IntelliSense.ShowDataTypes`
  - `_engine.ShowNullability = settings.IntelliSense.ShowNullability`
  - `_engine.ShowKeyIndicators = settings.IntelliSense.ShowPkFk`

  They refresh after Options OK through the existing `AnalysisSettingsChanged` → `InvalidateSettings` path. Depends on T026.
- [ ] T028 [US1] Carry `FilterText` and add prefix-only filtering in the shell:
  - `src/AkmlSql.Shell.Shared/Editor/Completion/CompletionItemModel.cs`: add `FilterText`; in `MatchesFilter`/`FilterScore`, prefix-only mode uses a case-insensitive `StartsWith` against `FilterText ?? DisplayText`.
  - `src/AkmlSql.Shell.Shared/Editor/Completion/CompletionController.cs`: populate `FilterText` from the DTO (~:879-887), and latch `Popup.PrefixOnly = !IntelliSense.FuzzyMatch` in `LatchPopupSettings` (~:771-776).
  - `src/AkmlSql.Shell.Shared/Editor/Completion/AkmlCompletionPopup.cs`: honour `PrefixOnly` in `ApplyFilter` (~:415-425).
- [ ] T029 [US1] In `src/AkmlSql.Shell.Shared/Editor/Completion/CompletionController.cs`, add the trigger delay and the after-dot switch:
  - `AutoTriggerCompletion()` (~:724-728) calls the existing `TriggerCompletionDebounced()` (~:752-763) with the delay from `SettingsSnapshot().IntelliSense.TriggerDelayMs` instead of the constant `DebounceMs`. When the delay is 0, trigger immediately as today.
  - The debounce callback calls `TriggerCompletion()` (not `FetchAndShowCompletions()`), so the filter text is recomputed.
  - Cancel the timer in `DismissPopup()` (~:1346), on commit and on Esc.
  - Gate the dot trigger (~:427) on `IntelliSense.AfterDot`.
  - Ctrl+Space paths (~:370-392, ~:548-560) stay immediate.

  Depends on T028 (same file).
- [ ] T030 [US1] Wire "Show in Error List":
  - In `src/AkmlSql.Shell.Shared/Analysis/ErrorListReporter.cs`:
    - add `internal static bool ShouldPublish(CodeAnalysisSettings s) => s.ShowInErrorList;`;
    - add a 2-second cached settings read;
    - add a static weak registry of live reporters with `internal static void ReapplyAll()`;
    - in `RefreshTaskList` (~:48-80), when `ShouldPublish` is false, clear the tasks, call `Refresh()` and return.
  - `ReapplyAll()` re-runs `RefreshTaskList` with each reporter's controller's `CurrentIssues`.
  - Call `ErrorListReporter.ReapplyAll()` from `OptionsCommand.SaveAndNotify` (`Commands/OptionsCommand.cs` ~:110-134).
- [ ] T031 [US1] In `src/AkmlSql.Shell.Shared/Analysis/AnalysisController.cs`, gate the edit-triggered analysis (~:48-52) on `CodeAnalysis.RunOnType` (2 s cached read). Opening a document and the explicit "run analysis" commands still analyse.
- [ ] T032 [P] [US1] In `src/AkmlSql.Shell.Shared/Formatting/FormatActionHelper.cs`, add `internal static bool EnsureFormatterEnabled()`. It reads `Formatter.Enabled`; when false it writes `AKML SQL formatting is off — turn it on in Options › Format › Styles.` to the status bar and returns false.
  - Call it at the start of `Execute` in `FormatDocumentCommand.cs`, `FormatSelectionCommand.cs` and `src/AkmlSql.Shell.Shared/Productivity/BulkFormatCommand.cs`.
- [ ] T033 [US1] In `src/AkmlSql.Shell.Shared/Editor/Completion/CompletionController.cs`:
  - Replace the hard-coded `FormatOnExpand = true` (~:1129, ~:1179) with `SettingsSnapshot().Snippets.FormatOnExpand`.
  - When `Snippets.Enabled` is false, filter snippet items (ObjectType 4) out of the list, as `SnippetsInCompletion` does at ~:856-871, and skip Tab expansion.

  Depends on T029 (same file).
- [ ] T034 [P] [US1] In `src/AkmlSql.Shell.Shared/Ui/BulkFormatWizard.cs` (~:138-144), set the backup checkbox's initial `Checked` from `ConfigManager.Load().Formatter.CreateBackups` instead of `true`.
- [ ] T035 [P] [US1] Hide dead rows on the Suggestions pages:
  - `src/AkmlSql.Shell.Shared/Dialogs/Pages/IntelliSensePage.cs`: "Keyword casing" dropdown (~:51).
  - `SuggestionTypesPage.cs`: "List all database columns after a SELECT statement" (~:28).
  - `CompletionPolishPage.cs`: rows at ~:23 (MS_Description), ~:28 (parameter highlight), ~:41 (decrypt), ~:46 (temp-table IntelliSense) and ~:54 (column picker default sort). Keep "Show the object definition box" (~:33).

  For each row, remove the `RowFactory` call, its `ctx.RegisterSearch` call and the matching assignments in the page's `Controls.Load`/`Save`, so the saved value is never overwritten. Leave the `AppSettings` properties alone.
- [ ] T036 [P] [US1] Hide dead rows, using the same technique as T035:
  - `ConnectionsMemoryPage.cs`: the cache group header and rows (~:42-54). Keep the SQL-auth row and "Manage…".
  - `QualificationPage.cs`: "Qualify columns with table name or alias" and its header (~:32-34).
  - `FormattingPage.cs`: Format on paste/save/delimiter (~:53, ~:57, ~:61), Confirm before bulk format (~:68), Respect --noformat regions (~:78) and Validate formatting preserves semantics (~:83). Keep Enable SQL formatter, Show active style in status bar and Create backups.
- [ ] T037 [P] [US1] Hide dead rows, using the same technique as T035:
  - `EditorPage.cs`: Named regions (~:24), Document Outline toggle (~:36).
  - `NavigationPage.cs`: Go to Definition, Peek Definition, Find All References and Object Search toggles (~:16, ~:20, ~:24, ~:28). If the page is left with no rows, keep it with one info row, "Navigation commands are in AKML SQL › Navigate.", until US6 moves it.
  - `RefactoringPage.cs`: Show preview before applying (~:18), Create backups (~:23), Format after refactoring (~:28), Include string literals in rename scope (~:41), Rename scope (~:46).
- [ ] T038 [P] [US1] Hide dead rows, using the same technique as T035:
  - `GridPage.cs`: Freeze headers (~:29).
  - `CodeAnalysisPage.cs`: Analyze on save (~:28).
  - `SnippetsPage.cs`: Show in IntelliSense completions (~:28), Filter by SQL context (~:38), Track usage for ranking (~:43), Personal folder (~:51). Also add "Takes effect after SSMS restarts" to the Team folder description (~:56).
  - `AiAssistancePage.cs`: Chat panel toggle (~:329).
- [ ] T039 [US1] Remove the Suggestions › Database (`Schema Cache`) and `Labs` pages. Every row on both does nothing.
  - In `SettingsWindow.cs`, delete their entries from `_pageBuilders` (~:64, ~:79), the nav tree (~:537, ~:569-570) and `pages[]` (~:1140-1167).
  - Delete `src/AkmlSql.Shell.Shared/Dialogs/Pages/SchemaCachePage.cs` and `LabsPage.cs`, and remove them from the projitems.
  - Update any test that referenced them (search `tests/AkmlSql.Shell.Shared.Tests` for `SchemaCache` and `Labs`).
- [ ] T040 [P] [US1] Fix stale descriptions:
  - `ConnectionScopePage.cs`: the linked-server description saying it "currently has no effect". It works now; describe what it does.
  - Remove "(Phase B)" / "Phase A and Phase B" jargon from any remaining Options description (search `Dialogs/Pages`).
- [ ] T041 [US1] Build (MSBuild), then run the Shell, Engine and IntelliSense test suites, `CorpusGateTests` (the pass rate must not drop) and the format-parity goldens. Run quickstart.md scenarios 1–10 manually in SSMS 22 and record the results in `specs/040-sqlprompt-ui-parity/baseline.md` under "US1 verification".

**Checkpoint**: Options is honest and Cancel-safe. US1 can ship alone.

---

## Phase 4: User Story 2 — SQL History I can trust (Priority: P1)

**Goal**:
- Full preview.
- Real open/closed state.
- Scrolling reaches the end.
- Row actions act on the whole grouped query.
- Search stays correct after snapshots.
- Only working settings are shown.

It covers gap items HIS-01 to HIS-06.

**Independent Test**: quickstart.md scenarios 11–17.

### Tests for User Story 2 (write first)

- [ ] T042 [P] [US2] Write `tests/AkmlSql.Core.Tests/Ipc/HistoryActionContractTests.cs`, following `HistoryRecordRequestTests`. Round-trip:
  - `HistoryActionRequest` keys 9–12;
  - `HistoryActionResponse` keys 8 and 11;
  - `HistoryEntryDto` key 17.

  Also assert a payload serialised **without** the new keys deserialises with them null or false (the legacy shape).
- [ ] T043 [P] [US2] Write `tests/AkmlSql.Engine.Tests/History/HistorySchemaV3Tests.cs`. Build a v2 database file through the internal `HistoryDatabase(dbPath)` constructor, then use raw SQL to:
  - insert rows whose `executed_at`/`saved_at` use the space format;
  - insert an orphan `history_versions` row;
  - update `history.sql_text` directly so the full-text index is out of step.

  Then initialise and assert:
  - all timestamps are ISO "o";
  - the orphan is gone;
  - `INSERT INTO history_fts(history_fts) VALUES('integrity-check')` succeeds;
  - `metadata.history_v3 = '1'`;
  - the `open_pid` column and the `history_au` trigger exist;
  - a second initialisation changes nothing.
- [ ] T044 [P] [US2] Write `tests/AkmlSql.Engine.Tests/History/HistoryGroupActionsTests.cs`. Record three runs in one session, then check each action with `GroupScope = true`:
  - Delete removes every row, its `history_versions` and its `query_sessions` row, and returns `DeletedCount = 3`.
  - ToggleFavorite sets `is_favorite` on all rows and returns `IsFavorite = true`. After another run, a second toggle clears every row.
  - GetVersions returns distinct texts newest first, and the count equals the grouped row's `VersionCount`.

  With `GroupScope = null`, the per-id behaviour is unchanged, except that `DeletedCount` is now set.
- [ ] T045 [P] [US2] Write `tests/AkmlSql.Engine.Tests/History/HistoryOpenStateTests.cs`:
  - `SetOpenStatus` with `SessionKey` and `OwnerPid` marks every row of that session open and sets `open_pid`; `IsOpen = false` clears both.
  - `ReconcileOpen(ownerPid, openKeys)`:
    - closes rows owned by `ownerPid` whose session isn't in `openKeys`;
    - closes rows whose `open_pid` is a process that doesn't exist (use `int.MaxValue - 1`) and returns their group's representative id in `RestorableEntryIds`;
    - does **not** touch rows owned by a live process (use `Environment.ProcessId`);
    - leaves rows with `open_pid IS NULL` (web rows) unchanged.
- [ ] T046 [P] [US2] Write `tests/AkmlSql.Engine.Tests/History/HistorySnapshotSearchTests.cs`. After `SaveVersionBySourceAsync`:
  - a search for the new text finds the entry, and a search for the replaced text doesn't;
  - `executed_at` is ISO "o";
  - `content_hash` matches the new text;
  - ordering is by time across rows written before and after.
- [ ] T047 [P] [US2] Write `tests/AkmlSql.Engine.Tests/History/HistoryGroupFiltersTests.cs`:
  - With grouping on, `IsOpen = false` never returns a group with any open run.
  - `FavoritesOnly` returns a group whose older run is starred.
  - `TotalCount` equals the number of groups the filters return.
- [ ] T048 [US2] Update `tests/AkmlSql.Engine.Tests/History/HistoryVersionSnapshotBySourceTests.cs` (~:84-100) to expect the ISO "o" `executed_at` instead of the space format. This is a deliberate behaviour change (research R15).
- [ ] T049 [P] [US2] Write `tests/AkmlSql.Shell.Shared.Tests/HistoryPagingTests.cs`:
  - With a fake search client serving 250 matches at page size 100, `HasMoreEntries` stays true until all 250 are loaded, then false.
  - When a page returns fewer rows than the page size, `HasMoreEntries` is false.
- [ ] T050 [P] [US2] Extend `tests/AkmlSql.Shell.Shared.Tests/QuerySessionKeyTests.cs`:
  - `DocumentSessionKeys.TryGet` returns false for an unknown document and doesn't create a key.
  - `Adopt(fullName, key)` makes `TryGet` return that key.
- [ ] T051 [P] [US2] Write `tests/AkmlSql.Shell.Shared.Tests/HistoryPreviewAndActionsTests.cs`, using a fake client:
  - selecting an entry requests `GetFullSql` once per id (cached), doesn't set `IsLoading`, and the cache is cleared by Refresh;
  - Delete and ToggleFavorite invoked for a row send that row's id with `GroupScope = true` when grouping is on, even when `SelectedEntries` holds other rows;
  - Delete asks for confirmation through an injectable prompt with the text `Remove '‹name›' and its history?`.

### Implementation for User Story 2

- [ ] T052 [P] [US2] Add the DTO keys (contracts/ipc.md):
  - `src/AkmlSql.Core/Ipc/Messages/HistoryActionRequest.cs`: `[Key(9)] bool? GroupScope`, `[Key(10)] string? SessionKey`, `[Key(11)] int? OwnerPid`, `[Key(12)] string[]? OpenSessionKeys`, and the action constant `ReconcileOpen = 11`.
  - The response class (same folder; search for `class HistoryActionResponse`): `[Key(8)] bool? IsFavorite` and `[Key(11)] long[]? RestorableEntryIds`. Keys 9 and 10 are reserved for US5; add them now as nullable `string[]? Servers`/`Databases` so the numbering stays contiguous.
  - `HistoryEntryDto.cs`: `[Key(17)] string? SessionKey`.
- [ ] T053 [US2] Add schema v3 to `src/AkmlSql.Engine/History/HistoryDatabase.cs` `InitializeCoreAsync` (~:90-230), per data-model.md §2.1:
  - `SchemaVersion = 3`;
  - `ALTER TABLE history ADD COLUMN open_pid INTEGER NULL`, in the same try/catch "duplicate column" pattern as ~:147-159;
  - `CREATE TRIGGER IF NOT EXISTS history_au AFTER UPDATE OF sql_text ON history …` (delete the old text, insert the new);
  - a one-time repair guarded by the `metadata` key `history_v3`, under `BEGIN IMMEDIATE`, that:
    1. normalises space-format `executed_at` and `history_versions.saved_at` to ISO "o";
    2. deletes orphan `history_versions`;
    3. runs `INSERT INTO history_fts(history_fts) VALUES('rebuild')`;
    4. writes the flag in the same transaction.

  The file is shared with the web engine and other SSMS instances, so everything must be idempotent.
- [ ] T054 [US2] In `SaveVersionBySourceAsync` (`HistoryDatabase.cs` ~:1735-1765):
  - write `executed_at = DateTime.UtcNow.ToString("o")` (not `datetime('now')`);
  - update `content_hash` with the same hash function the insert path uses;
  - look the row up by session key when a `SessionKey` is supplied (new optional parameter), falling back to source.
- [ ] T055 [US2] Add group-scoped operations to `HistoryDatabase.cs`, resolving the group from an entry id with the GroupKey expression (~:910):
  - `DeleteGroupAsync(long entryId)`: one transaction deleting `history_versions` for the group's ids, then the `history` rows, then the `query_sessions` row. Returns the number of rows deleted.
  - `ToggleFavoriteGroupAsync(long entryId)`: sets all rows to `1 - MAX(is_favorite)` and returns the new state.
  - `GetVersionsForGroupAsync(long entryId)`: runs and snapshots of the group, de-duplicated by content hash, newest first, id tiebreak.
  - Make per-id `DeleteAsync` return its count.
- [ ] T056 [US2] Add open state and group-level filters to `HistoryDatabase.cs`:
  - `SetOpenStatusBySessionAsync(string sessionKey, bool isOpen, int ownerPid)`, updating every row of the session.
  - `ReconcileOpenAsync(int ownerPid, string[] openKeys)`, returning the ids from dead owners. Check liveness with `Process.GetProcessById` inside try/catch (`ArgumentException` = dead).
  - Move the `FavoritesOnly`/`IsOpen` filters in `SearchAsync` (~:889-896) to the grouped outer level: `WHERE` on `MAX(is_favorite)` / `MAX(is_open)`. Compute `TotalCount` with `SELECT COUNT(*) FROM (<grouped query without LIMIT>)`. The flat mode keeps per-row filters.
  - Delete the dead `CloseByTabTitleAsync` (~:1276-1287).
- [ ] T057 [US2] In `src/AkmlSql.Engine/History/HistoryRequestHandler.cs` (~:178-420):
  - route `GroupScope == true` for Delete, ToggleFavorite and GetVersions to the group methods;
  - set `DeletedCount` for both Delete paths;
  - return `IsFavorite`;
  - route `SetOpenStatus` with `SessionKey` and `OwnerPid` to `SetOpenStatusBySessionAsync`, keeping the old `EntryIds` path;
  - add `case ReconcileOpen` returning `RestorableEntryIds`.
- [ ] T058 [US2] Add `public static bool TryGet(string fullName, out string key)` (never creates a key) and `public static void Adopt(string fullName, string key)` to `src/AkmlSql.Shell.Shared/History/DocumentSessionKeys.cs`.
- [ ] T059 [US2] In `src/AkmlSql.Shell.Shared/History/ExecutionCapture.cs`:
  1. **Record (~:720-740):** send the history record with `SendRequestAsync<HistoryRecordResponse, HistoryRecordRequest>` instead of a notification. Once it returns, send `HistoryAction SetOpenStatus` with `IsOpen = true`, `SessionKey` and `OwnerPid = Process.GetCurrentProcess().Id`.
  2. **`OnWindowActivated` (~:434-516):** for the document that **gained** focus, if `DocumentSessionKeys.TryGet` finds a key, send `SetOpenStatus(true)`.
  3. **`OnDocumentClosing` (~:302-335):** call `TryGet` **before** `Forget`. Unless `ShuttingDown` is set, send `SetOpenStatus(false, key, pid)`.
  4. Add `public static volatile bool ShuttingDown`.

  All sends are fire-and-forget on a background task, with errors logged and never thrown into the UI thread.
- [ ] T060 [US2] In `src/AkmlSql.Ssms22/AkmlSqlPackage.cs`:
  - **Startup (~:193-199):** once the engine launch task completes, send `ReconcileOpen` on a background task. Pass `OwnerPid` = the current process id, and `OpenSessionKeys` = the keys from `DocumentSessionKeys.TryGet` for every open DTE document named `*.sql` or `SQLQuery*`. Keep the returned `RestorableEntryIds` in a static for US5 (`History/HistoryRestoreState.cs`, new small static holder, added to the projitems).
  - **Shutdown:** in `Dispose(bool)` (~:602-610), and in a `QueryClose` override if one exists, set `ExecutionCapture.ShuttingDown = true` **first**.
- [ ] T061 [US2] In `src/AkmlSql.Shell.Shared/History/HistoryViewModel.cs`:
  - **Paging:** set `HasMoreEntries => _lastPageCount == PageSize && Entries.Count < TotalCount` (~:182), recording `_lastPageCount` after each page (~:386, ~:461).
  - **Preview:** add `Task<string?> GetPreviewTextAsync(HistoryEntryDto e)`, using `GetFullSql` with a per-id cache cleared on Refresh. It must **not** touch `IsLoading`.
  - **Row actions:** make Delete and ToggleFavorite take the row entry explicitly (not `SelectedEntries`) and send `GroupScope = true` when deduplication/grouping is on.
  - **Delete:** ask `Remove '‹name›' and its history?` through an injectable `Func<string,bool> ConfirmPrompt`, defaulting to a MessageBox.
  - **ToggleFavorite:** apply the returned `IsFavorite` to the row.
  - **Open in New Tab:** call `DocumentSessionKeys.Adopt(newDoc.FullName, entry.SessionKey)` when a session key is present.
- [ ] T062 [US2] In `src/AkmlSql.Shell.Shared/History/HistoryToolWindowControl.cs`:
  - **Preview:** replace the preview `TextBlock` and `RenderPreview` (~:1292-1302, ~:1473-1571) with a `SqlPreviewView` (T004). Its `Text` comes from `GetPreviewTextAsync`, and `HighlightTerms` from `HistorySearchTerms.Extract`.
  - **Versions:** load them (~:1684-1781) with `GroupScope = true`, guarded against stale responses (a sequence number; drop responses that don't match the current entry).
  - **Open marker:** replace the red/green `connDot` (~:948-958) with a 3 px `AccentPrimary` left bar shown when `IsOpen`, and remove the `OpenClosedColorConverter` usage. The footer dot (~:1264) shows green only when the entry is open.
  - **Star and ⋯:** pass their row's entry to the view-model commands.
- [ ] T063 [US2] In `src/AkmlSql.Shell.Shared/Dialogs/Pages/HistoryPage.cs`:
  - hide "Record failed executions" (~:24) and "Encrypt at rest" (~:47), with the T035 technique;
  - rename "Enable deduplication" to **Group repeated runs of the same query**, with the description "Show one row per query tab, with its runs and versions inside.";
  - add "Takes effect after SSMS restarts" to the descriptions of Enable SQL history recording, Retention, Max entries and Disable automatic history trimming.
- [ ] T064 [US2] Build, then run the Core, Engine and Shell suites, plus `tests/AkmlSql.Web.Tests` History tests: `WebHistoryLogicTests` must be unchanged. Run quickstart.md scenarios 11–17 and record the results in `baseline.md` under "US2 verification".

**Checkpoint**: History is trustworthy. US2 works with or without US1.

---

## Phase 5: User Story 3 — A style editor I can read and trust (Priority: P1)

**Goal**:
- Readable option labels, and clicking a label toggles its checkbox.
- The live preview uses the style's tab width.
- Import and Export keep the list and the active style honest.

It covers gap items STY-01 to STY-03.

**Independent Test**: quickstart.md scenarios 18–21.

### Tests for User Story 3 (write first)

- [ ] T065 [P] [US3] Write `tests/AkmlSql.Shell.Shared.Tests/FormatStylesRowLayoutTests.cs` (`[StaFact]`), building the window headlessly as `FormatStylesWindowFixTests` does.
  - Load the SQL Prompt schema and set the window size to 1060×680.
  - For each of the 14 pages, run `Measure`/`Arrange` and assert every label `TextBlock` has `DesiredSize.Width <= ActualWidth + 0.5` (no clipping) and `TextTrimming == None`.
  - For every Bool option, assert the `CheckBox.Content` is the label, so clicking the text toggles it.
- [ ] T066 [P] [US3] Write `tests/AkmlSql.Shell.Shared.Tests/FormatStylesPreviewTabTests.cs`: `FormatStylesEditorViewModel.PreviewTabSize` returns `sqlPrompt.whitespace.numberOfSpacesInTabs` for SQL Prompt-model styles and `whitespace.tabSize` for AKML-model styles, and follows option edits.
- [ ] T067 [P] [US3] Write `tests/AkmlSql.Shell.Shared.Tests/FormatStylesImportExportTests.cs`, extending `FormatStylesLifecycleTests` with a fake IPC client:
  - After a successful import plus activation, `Profiles` shows `IsActive` on the imported style, and the window's header state and set-active button are in sync.
  - Import with `IsDirty` calls `DirtyDecisionHandler` **before** the import request is sent.
  - Export with `IsDirty` for the loaded style calls the save prompt: Cancel aborts, Yes saves and then exports, No exports the saved file.

### Implementation for User Story 3

- [ ] T068 [US3] Rework `BuildSettingRow` in `src/AkmlSql.Shell.Shared/Formatting/FormatStylesEditorWindow.cs` (~:1995-2049) and the Bool branch of `BuildControlForSetting` (~:2081-2095):
  - **Bool options:** a `CheckBox` whose `Content` is a `TextBlock` (`TextWrapping.Wrap`, label text, the existing foreground token), spanning both columns (`Grid.SetColumnSpan(checkbox, 2)`), with no separate label cell. Pass that `TextBlock` as `GatedRow.Label` so `RefreshIfGate` keeps working.
  - **Other kinds:**
    - The label column is `new ColumnDefinition { Width = GridLength.Auto, SharedSizeGroup = "lbl", MinWidth = 200 }`.
    - The label is a wrapping `TextBlock` with no horizontal `StackPanel`; `MaxWidth` is bound to 45 % of the host width.
    - The control column keeps its 280 px cap.
  - **Host:** set `Grid.IsSharedSizeScope="True"` on `_settingControlsHost` (~:1577).
  - **Tooltips:** they include the full label text.
- [ ] T069 [US3] Add `internal int PreviewTabSize` to `src/AkmlSql.Shell.Shared/Formatting/FormatStylesEditorViewModel.cs`:
  - SQL Prompt model: `GetWorkingValue("sqlPrompt.whitespace.numberOfSpacesInTabs")`.
  - Otherwise: `GetWorkingValue("whitespace.tabSize")`.
  - Parse as int, default 4, clamp 1–16. Raise `PropertyChanged` when either option changes.
- [ ] T070 [US3] In `FormatStylesEditorWindow.cs`, show the formatted preview in a `SqlPreviewView` (T004) with `TabSize = _viewModel.PreviewTabSize`, updated on `PreviewText`/`PreviewTabSize` changes (~:2284-2301).
  - Keep `_previewTextBox` only for "Edit sample" mode, and toggle visibility between the two (~:1692-1778).
  - Keep the dark card for now; US4 moves it to theme tokens.
- [ ] T071 [US3] Import (`FormatStylesEditorWindow.cs` ~:1175-1248):
  - **Before** showing the file dialog, when `_viewModel.IsDirty`, ask via `PromptSaveDecision`: Yes saves, No discards, Cancel aborts.
  - After a successful import and `SetActiveProfile`, run the same sequence as set-active (~:1119-1145): `await _viewModel.RefreshProfilesAsync(); RestoreListSelection(name);`, sync `_setActiveButton`, `UpdateHeaderState();`, then update the status bar.
- [ ] T072 [US3] Export (`FormatStylesEditorWindow.cs` ~:1147-1168): when `_viewModel.IsDirty && name == _viewModel.LoadedProfileName`, show `Save changes to '{name}' before exporting?` (Yes / No / Cancel).
  - Yes → `await SaveSelectedStyleAsync()`, then export.
  - No → export the saved file.
  - Cancel → stop.
- [ ] T073 [US3] Build, then run the Shell suite. Run quickstart.md scenarios 18–21, and capture the style editor at its default size (Northwind) with `tests/AkmlSql.UiTests` or manually. Record the results in `baseline.md` under "US3 verification".

**Checkpoint**: all P1 stories are done. **Stop and review before starting P2** (FR-071).

---

## Phase 6: User Story 4 — Find, compare and switch styles quickly (Priority: P2)

**Goal**:
- Option search.
- Markers for options changed from SQL Prompt's default.
- A coloured preview.
- An Active Style menu.
- Friendlier list actions.
- "Formatted with …" feedback.

It covers gap items STY-04 to STY-09.

**Independent Test**: quickstart.md scenarios 22–28.

### Tests for User Story 4 (write first)

- [ ] T074 [P] [US4] Write `tests/AkmlSql.Core.Tests/Ipc/FormatSelectionResponseTests.cs`: key 7 `ProfileFallbackWarning` round-trips, and the legacy shape deserialises with null.
- [ ] T075 [P] [US4] Extend `tests/AkmlSql.Engine.Tests/Formatter/ProfileFallbackWarningTests.cs`: `FormatSelection` with a missing profile name returns a non-null `ProfileFallbackWarning` with the same text as `FormatDocument`.
- [ ] T076 [P] [US4] Write `tests/AkmlSql.Shell.Shared.Tests/FormatStylesSearchTests.cs`:
  - The view model's `Search("comma")` returns only groups whose options mention "comma" in DisplayName, Description, Note, Subgroup, EnumLabels or the option id, with per-group counts, and the first match's id.
  - An empty query returns every group with no counts.
- [ ] T077 [P] [US4] Write `tests/AkmlSql.Shell.Shared.Tests/FormatStylesChangeMarkersTests.cs`:
  - `IsChanged(id)` is true when the working value ≠ the catalog default.
  - `ChangedCount(groupId)` counts per page.
  - `ResetOption(id)` restores the default and lowers the count.
  - `MovedLines` is computed positionally between the previous and current preview, **only** after an option edit (not after a style or page switch).
  - `IsDirty` returns to false when the user sets every edited option back to its saved value.
- [ ] T078 [P] [US4] Write `tests/AkmlSql.Shell.Shared.Tests/StyleNameDialogValidationTests.cs`:
  - empty name, illegal file-name characters, "..", and an existing name (case-insensitive, trimmed) each show a message and disable OK;
  - Rename allows the current name, including a change of case only;
  - a valid name enables OK.
- [ ] T079 [P] [US4] Write `tests/AkmlSql.Shell.Shared.Tests/ActiveStyleMenuTests.cs`, with a fake `ActiveStyleCache`:
  - slot N shows style N's name, checked when active, visible only when style N exists;
  - more than 30 styles → only 30 are shown, and the last visible slot is style 30;
  - invoking a slot sets `Formatter.ActiveProfile` (AppData isolated) and raises the cache's refresh.
- [ ] T080 [P] [US4] Write `tests/AkmlSql.Shell.Shared.Tests/StatusBarTransientTests.cs`, with an `IVsStatusbar` fake (or an internal text sink seam added to `StatusBarManager`):
  - `ShowTransient` shows the text, then restores the idle text after the timeout;
  - while the transaction indicator is active, the idle text isn't restored over it;
  - `SetActiveProfile` does nothing when `ShowProfileInStatusBar` is false.

### Implementation for User Story 4

- [ ] T081 [P] [US4] Add `[Key(7)] public string? ProfileFallbackWarning { get; set; }` to `src/AkmlSql.Core/Ipc/Messages/FormatSelectionResponse.cs`. In `HandleFormatSelection` (`src/AkmlSql.Engine/Formatter/FormatRequestHandler.cs` ~:68), use the `LoadProfile(name, out var warning)` overload (~:895-914) and set the field.
- [ ] T082 [US4] In `FormatStylesEditorViewModel.cs` and `FormatStylesEditorWindow.cs`:
  - keep the parsed `FormatStylesSchemaModel` model (today a local at ~:1812) as `_viewModel.SchemaModel`, and keep a `Dictionary<string, TreeViewItem>` groupId → leaf;
  - add `internal SearchResult Search(string query)`, matching DisplayName, Description, Note, Subgroup, EnumLabels and the option id, case-insensitive. It returns the matching group ids with counts, the matching option ids, and the first match.
- [ ] T083 [US4] In `FormatStylesEditorWindow.cs`, add the option search box:
  - A `TextBox` above the page tree (~:1473-1511) with placeholder `Search for options…`, and a 150 ms debounce.
  - The tree hides leaves (and emptied categories) with no matches, and each shown leaf gets a count badge (`Typography.Small`, `AccentPrimary`).
  - `UpdateRightForGroup` highlights matching rows with the `SurfaceSelection` background.
  - Enter selects the first match's page and scrolls to its row.
  - Esc, handled in the TextBox's `PreviewKeyDown` with `e.Handled = true`, clears the query when there is one. When the box is already empty, Esc falls through to the window's `IsCancel` Close.
- [ ] T084 [US4] In `FormatStylesEditorViewModel.cs`:
  - add `IsChanged(string id)` (working value ≠ `_schemaDefaults[id]`), `ChangedCount(string groupId)` and `ResetOption(string id)` (sets the default, queues the preview);
  - replace the sticky `IsDirty` with a recomputation against the saved values: rebuild them the way `RevertChanges` does (~:737-738) into `_savedValues` whenever a style loads or saves;
  - capture a `markChanges` flag alongside the preview sequence in `QueuePreviewAsync` (~:472-548): true only from `SetWorkingValue`/`ResetOption`;
  - store `PreviousPreviewLines` and expose `MovedLines` (a positional compare, as in `src/AkmlSql.Web/Pages/Styles.razor` ~:478-509).
- [ ] T085 [US4] In `FormatStylesEditorWindow.cs`:
  - a changed option shows a bold label and a small `↺` button, tooltip `Back to SQL Prompt's default (‹value›)`, that calls `ResetOption`;
  - tree leaves show the changed count (distinct from search counts: prefix `●` or use a different token);
  - after an option edit, set `SqlPreviewView.HighlightLines = MovedLines` for 2 s with a `DispatcherTimer`, then clear it.
- [ ] T086 [US4] Move the preview in `FormatStylesEditorWindow.cs` to theme colours:
  - remove the fixed `PreviewBgBrush`/`PreviewTextBrush`/`PreviewMutedBrush` card colours (~:117-128, ~:1609-1611) in favour of `ThemeTokens.EditorPanelBackground` (or `SurfaceInput`), `TextPrimary` and `TextSecondary` through `SetResourceReference`;
  - turn on `ShowLineNumbers`;
  - keep the amber warning bar semantic colours.
- [ ] T087 [US4] Friendlier names:
  - `src/AkmlSql.Shell.Shared/Formatting/StyleNameDialog.cs`: accept `IReadOnlyCollection<string> existingNames` and `string? currentName`. `Revalidate` (~:147-159) checks empty, illegal characters, "..", and duplicates (OrdinalIgnoreCase, trimmed; the current name is exempt). OK is disabled until valid.
  - `ShowNewStyle`/`ShowRename` pass the names from `_viewModel.Profiles`.
  - Add `ShowCopyStyle(owner, existingNames, suggested)`.
  - `FormatStylesEditorWindow.OnCopyStyleAsync` (~:1111-1117) prompts with `UniqueName($"{name} copy")` pre-filled, then calls a new `FormatStylesEditorViewModel.CopyProfileAsync(source, newName)`.
- [ ] T088 [US4] Keyboard in `FormatStylesEditorWindow.cs`, using the `SettingsWindow.OnWindowKeyDown` pattern (~:1638-1658):
  - **Window:** Ctrl+S → Save (when enabled); Ctrl+F → focus the search box.
  - **Style list:** F2 → Rename; Delete → Delete (refuse built-in, team and active styles with `SetStatus` explaining why); Enter → Set active.
- [ ] T089 [US4] In `FormatStylesEditorWindow.cs`:
  - **Integers:** in `BuildControlForSetting` (~:2097-2156), add small ▲/▼ buttons that step by 1 within the range and reuse the existing validation.
  - **Notes:** show each option's Note and "Takes effect when …" as grey `Typography.Small` text **under** the row, not only in the tooltip.
  - **Child options:** indent options with an `EnabledWhen` gate by `Spacing.Lg`.
- [ ] T090 [US4] Create `src/AkmlSql.Shell.Shared/Formatting/ActiveStyleCache.cs` (add it to the projitems):
  - a thread-safe snapshot `IReadOnlyList<(string Name, string Source, bool IsActive)> Styles`;
  - `Task RefreshAsync()` via the existing ProfileList IPC, marking active from `ConfigManager.Load().Formatter.ActiveProfile`;
  - `void RequestRefresh()`, throttled to once per 5 s, fire-and-forget;
  - an event `Changed`.
  - Refresh points:
    - package load (`AkmlSqlPackage`, after engine launch);
    - after `FormatStylesEditorWindow.Launch` returns (`Commands/FormatStylesCommand.cs`, `Dialogs/Pages/FormattingPage.cs` ~:98);
    - after `OptionsCommand.SaveAndNotify`.
- [ ] T091 [US4] Add the command ids and VSCT buttons:
  - `src/AkmlSql.Shell.Shared/PackageGuids.cs`: `CmdActiveStyleSlot0 = 0x0920` through `CmdActiveStyleSlot29 = 0x093D` (a base constant plus a count is enough) and `CmdEditStyles = 0x093E`, inside the reserved free range, checking there are no clashes.
  - `src/AkmlSql.Ssms22/AkmlSqlSsms22.vsct`: matching IDSymbols and 31 `<Button>`s with `DynamicVisibility` and `TextChanges`, placeholder text "Style", in `AkmlSqlMenuGroup` for now. US7 regroups them.
- [ ] T092 [US4] Create `src/AkmlSql.Shell.Shared/Formatting/ActiveStyleMenuCommands.cs` (add it to the projitems):
  - **Initialize:** register 30 `OleMenuCommand`s whose `BeforeQueryStatus` sets `Text`, `Checked` and `Visible` from `ActiveStyleCache` (slot index → style), and calls `ActiveStyleCache.RequestRefresh()`.
  - **Invoke:**
    1. `ConfigManager.Load()`, set `Formatter.ActiveProfile`, `ConfigManager.Save`;
    2. `StatusBarManager.SetActiveProfile` (gated, T093);
    3. `ActiveStyleCache.RequestRefresh()`.
  - **Edit Styles…:** run the same path as `FormatStylesCommand`.
  - Register it in `AkmlSqlPackage.TryInitCommand` (`src/AkmlSql.Ssms22/AkmlSqlPackage.cs` ~:62-143).
- [ ] T093 [US4] In `src/AkmlSql.Shell.Shared/StatusBar/StatusBarManager.cs`:
  - add `ShowTransient(string text, int seconds)`: a `DispatcherTimer` restores `_idleText` unless `_transactionIndicatorActive`;
  - make `SetActiveProfile` a no-op when `ConfigManager.Load().Formatter.ShowProfileInStatusBar` is false (2 s cached read);
  - gate the editor's call in `FormatStylesEditorWindow.UpdateStatusBarActiveStyle` (~:1371-1383) the same way;
  - in `OptionsCommand.SaveAndNotify`, set or clear the active-style idle text from the saved settings;
  - add an internal text-sink seam for T080.
- [ ] T094 [US4] Show which style formatted the code:
  - `src/AkmlSql.Shell.Shared/Formatting/FormatDocumentCommand.cs` (~:84-121): on success with no `ProfileFallbackWarning`, call `StatusBarManager.ShowTransient($"Formatted with '{profileName}'", 4)`.
  - `FormatSelectionCommand.cs` (~:56-92): the same, and call `FormatFailureNotifier.NotifyProfileFallbackOnce(response.ProfileFallbackWarning)` when it is present.
- [ ] T095 [US4] Add an interim menu placement in `src/AkmlSql.Ssms22/AkmlSqlPackage.cs` `EnsureTopLevelMenu` (~:383-490), until US7 replaces the builder:
  - Add an "Active Style" `msoControlPopup` whose controls are the 30 slots plus "Edit Styles…" (`dte.Commands.Item(guid, id).AddControl`).
  - Find SSMS's query-editor context command bar by enumerating `dte.CommandBars` names that contain "SQL" and "Context", or equal "Code Window". Log every candidate name at Debug level and pick the first match. Add the same popup and a "Format Document" control to it.
  - When nothing matches, log `Active Style: no editor context menu found` at Information level and continue (quickstart scenario 26 may be waived).
- [ ] T096 [US4] Build, then run the Core, Engine and Shell suites. Run quickstart.md scenarios 22–28 and record the results in `baseline.md` under "US4 verification", including which context bar name was found in T095.

**Checkpoint**: style editing matches SQL Prompt's patterns.

---

## Phase 7: User Story 5 — SQL History that works like SQL Prompt's (Priority: P2)

**Goal**:
- Search as you type, plus Advanced search.
- Rows that show where the query ran.
- Versions you can open and compare.
- Full keyboard use.
- A tidy row menu.
- Live refresh.
- Restore on start.

It covers gap items HIS-07 to HIS-14.

**Independent Test**: quickstart.md scenarios 29–37.

**Depends on**: US2 (open state, group actions, `RestorableEntryIds`) and T004 (SqlPreviewView).

### Tests for User Story 5 (write first)

- [ ] T097 [P] [US5] Write `tests/AkmlSql.Core.Tests/History/HistoryDateGroupsTests.cs`: `HistoryDateGroups.For(now, t)` gives Today, Yesterday, This week (from `CultureInfo.CurrentCulture.DateTimeFormat.FirstDayOfWeek`), Last week, This month and Older. Test the boundaries at midnight, week start and month start, in UTC and local time.
- [ ] T098 [P] [US5] Write `tests/AkmlSql.Core.Tests/Text/LineDiffTests.cs`: identical texts; a pure insert; a pure delete; a changed line (reported as changed, not delete plus add, when lines align); empty left or right; CRLF and LF inputs normalised.
- [ ] T099 [P] [US5] Write `tests/AkmlSql.Core.Tests/Ipc/HistoryUs5ContractTests.cs`: `HistoryRecordRequest` key 12, `HistorySearchRequest` key 13 and `HistoryActionResponse` keys 9–10 round-trip, the legacy shapes still deserialise, and `ExecutionStatus.NotExecuted == 3`.
- [ ] T100 [P] [US5] Write `tests/AkmlSql.Engine.Tests/History/HistorySearchScopeTests.cs`:
  - free text matches a session name, source path, server or database through `LIKE`, as well as SQL through the full-text index;
  - `PathFilter` limits results to the source;
  - `DateFrom`/`DateTo` filter on ISO timestamps;
  - an invalid full-text query still falls back to `LIKE`;
  - `TotalCount` is correct in grouped mode.
- [ ] T101 [P] [US5] Write `tests/AkmlSql.Engine.Tests/History/HistoryFilterValuesAndDraftTests.cs`:
  - `GetFilterValues` returns distinct, sorted, non-empty servers and databases, capped at 500.
  - A record with `IsDraft = true` is stored with `status = 3`, listed, and excluded from `exec_count`.
  - A later real run in the same session keeps the draft text as a version.
- [ ] T102 [P] [US5] Write `tests/AkmlSql.Shell.Shared.Tests/HistorySearchParserTests.cs`: `path:foo`, `date:[20260901 TO 20260927]` and every row of `HistorySearchParser.HelpRows` parse to the expected filters; unknown prefixes stay free text.
- [ ] T103 [P] [US5] Write `tests/AkmlSql.Shell.Shared.Tests/HistoryInteractionTests.cs`, using a fake client and an injectable scheduler/clock:
  - typing triggers one search 250 ms after the last change;
  - after a search the first row is selected;
  - a `HistoryRecorded` refresh keeps the selected id;
  - Advanced search state maps presets to `DateFrom`/`DateTo`, and is saved to settings only when `RememberAdvancedSearch` is on;
  - row-menu actions target the invoked row.
- [ ] T104 [P] [US5] Write `tests/AkmlSql.Shell.Shared.Tests/HistoryRestoreServiceTests.cs`, with fake opener and prompt:
  - **Always** opens up to `RestoreMaxQueries` entries, passing the reconnect flag;
  - **Prompt** shows the prompt with the entries and opens only the chosen ones;
  - **Never** opens nothing;
  - `Tabs.SessionRecovery = false` disables restore entirely.

### Implementation for User Story 5 — Core and engine

- [ ] T105 [P] [US5] Create `src/AkmlSql.Core/Models/History/HistoryDateGroups.cs` (enum plus `For(DateTime nowLocal, DateTime whenLocal)` plus `Label(group)`: "Today", "Yesterday", "This week", "Last week", "This month", "Older"). **Do not change** `HistoryDateBucket.Of`: the web edition and `WebHistoryLogicTests` depend on it.
- [ ] T106 [P] [US5] Create `src/AkmlSql.Core/Text/LineDiff.cs`: `static IReadOnlyList<LineDiffEntry> Diff(string left, string right)` using an LCS over lines. Each `LineDiffEntry` is (`Kind`: Same, Added, Removed or Changed, `LeftLine?`, `RightLine?`, `LeftText`, `RightText`). Pair adjacent Removed and Added runs as Changed.
- [ ] T107 [P] [US5] Add the DTO changes:
  - `src/AkmlSql.Core/Models/History/ExecutionStatus.cs`: `NotExecuted = 3`;
  - `HistoryRecordRequest.cs`: `[Key(12)] bool IsDraft`;
  - `HistorySearchRequest.cs`: `[Key(13)] string? PathFilter`;
  - `HistoryActionRequest.cs`: action constant `GetFilterValues = 12`.

  Response keys 9–10 were added in T052.
- [ ] T108 [US5] In `src/AkmlSql.Engine/History/HistoryDatabase.cs` `SearchAsync` (~:833-1136):
  - replace the full-text `INNER JOIN` with the OR clause from data-model.md §2.4: full-text `IN` subquery **or** `LIKE` on `COALESCE(qs.name, h.tab_title, '')`, `h.source`, `h.server` and `h.database_name`;
  - build the `LIKE` terms from `HistorySearchTerms.Extract`, requiring every term;
  - add the same `LEFT JOIN query_sessions qs` to the count query;
  - update the fallback clause matcher (~:951);
  - add `PathFilter` (`h.source LIKE`), and compare dates with `datetime()`;
  - add `GetFilterValuesAsync()` over `GetDistinctServersAsync`/`GetDistinctDatabasesAsync` (~:1141-1176), capped at 500;
  - on insert, store drafts (`IsDraft`) with `status = 3`, and make grouped `exec_count` count `status <> 3`;
  - when a real run is recorded for a session whose latest row is a draft, copy the draft text into `history_versions` first.
- [ ] T109 [US5] In `src/AkmlSql.Engine/History/HistoryRequestHandler.cs`, route `GetFilterValues` (fill `Servers`/`Databases`), and pass `IsDraft` and `PathFilter` through.

### Implementation for User Story 5 — shell

- [ ] T110 [US5] Add the History settings to `src/AkmlSql.Core/Config/AppSettings.cs` `HistorySettings` (~:758-789), per data-model.md §1.2: `MaxQuerySizeKb` (1024), `RestoreMaxQueries` (20), `ReconnectRestoredQueries` (true), `RememberAdvancedSearch` (false), `HistoryAdvancedSearchState? AdvancedSearch`, plus the new `HistoryAdvancedSearchState` class.
- [ ] T111 [US5] In `src/AkmlSql.Shell.Shared/History/HistorySearchParser.cs`, parse `path:` into `PathFilter`, and `date:[yyyyMMdd TO yyyyMMdd]` into `DateFrom`/`DateTo`. Add `internal static IReadOnlyList<(string Syntax, string Meaning)> HelpRows` listing every supported form: `name:`, `path:`, `sql:`, `server:`, `database:`/`db:`, `starred:true|false`, `open:true|false`, `date:[… TO …]`, `"phrase"`, `OR`, `NOT`, `word*`.
- [ ] T112 [US5] In `src/AkmlSql.Shell.Shared/History/ExecutionCapture.cs`:
  - truncate captured text to `History.MaxQuerySizeKb` KB;
  - raise a new `public static event Action<long?>? HistoryRecorded` after the awaited record from T059;
  - on `OnDocumentClosing` for a `.sql`/`SQLQuery*` document with non-empty text and **no** session key, send a record with `IsDraft = true` (plus the server and database, if known) before closing;
  - add a `DispatcherTimer` every `Tabs.AutoSaveInterval` seconds (when `Tabs.SessionRecovery` is on) that snapshots dirty open query documents: `SaveVersion` when a session key exists, otherwise an `IsDraft` record plus `Adopt` of the new session key once the response returns;
  - stop the timer when `ShuttingDown`.
- [ ] T113 [US5] In `src/AkmlSql.Shell.Shared/History/HistoryViewModel.cs`:
  - **Search:** debounce `SearchText` changes by 250 ms (a `DispatcherTimer`, injectable for tests); select the first entry after each search.
  - **Advanced search:** add an `AdvancedSearch` object (Period preset → `DateFrom`/`DateTo`; `Server`/`Database` filled from `GetFilterValues`; `Starred`; `OpenOnly`) and an `ActiveFilterChips` collection with remove commands. Load it from, and save it to, settings when `RememberAdvancedSearch` is on.
  - **Live refresh:** subscribe to `ExecutionCapture.HistoryRecorded` and re-query on the UI thread, preserving the selected entry id and scroll offset (exposed for the control). Preserve them after star and delete too.
  - **Disconnected:** show the overlay, run a 5-second probe while it is up, and add `RetryCommand`.
- [ ] T114 [US5] In `src/AkmlSql.Shell.Shared/History/HistoryToolWindowControl.cs`, the search area:
  - placeholder `Search` (~:208);
  - a `?` button beside the box opening a popup that lists `HistorySearchParser.HelpRows`;
  - a collapsible **Advanced search** panel under the box: Period combo (Everything, Last week, Last month, Last 3 months, Custom), two `DatePicker`s shown only for Custom, Server and Database combos, Starred and Open toggles, and Reset;
  - a chips row showing the active filters, each with a remove button.

  Texts come from contracts/ui.md §4.
- [ ] T115 [US5] In `HistoryToolWindowControl.cs`, the rows (~:866-1000) and groups (~:685-688, ~:789-840):
  - Line 2 right shows `server · database`, plus an environment badge when `EnvironmentMatcher.Match(rules, server, db)` matches. The rules come from `ConfigManager.Load().Tabs.ColoringRules`; the badge is the rule label, on a `HexBrush.Get(color)` background, with a contrasting foreground.
  - Grouping uses `HistoryDateGroups`, and group headers show `Label (n)`.
  - `×N · M versions` uses the muted `TextSecondary` colour.
  - Draft rows show `Not executed`.
- [ ] T116 [US5] Versions in `HistoryToolWindowControl.cs` (~:1684-1804) and `HistoryViewModel.cs`:
  - each version row gets a page glyph and `server · environment`;
  - add `SelectedVersion` to the view model, and make Open, Copy and Re-execute use the selected version's text when set;
  - add a **Compare with current** context item on a version row.
- [ ] T117 [US5] Upgrade `src/AkmlSql.Shell.Shared/History/HistoryDiffWindow.cs`:
  - Each side's header reads `‹name› — ‹HistoryTimeFormat.Absolute(time)›`.
  - Both sides are rendered from `LineDiff.Diff`, one `SqlPreviewView` per side, aligned line by line with blank filler lines. Added, removed and changed lines get tints from `StatusSuccess`, `StatusDanger` and `StatusWarning` at low opacity, through theme resources.
  - It serves both "Compare with current" (a version against the entry's current text) and the existing two-row Compare.
- [ ] T118 [US5] Keyboard and focus in `HistoryToolWindowControl.cs`:
  - **List `PreviewKeyDown`:** Enter → Open, Delete → Remove (with confirmation), F2 → Rename (refused while open), Ctrl+C → Copy SQL, Space → toggle star.
  - **Tab order:** search → list → versions → preview.
  - **Row controls:** the row star and ⋯ (~:874-909) become focusable `Button`s (templated to look the same), with `AutomationProperties.Name` "Star query" and "Query actions".
  - **Search box Esc:** clear the text; when it is already empty, clear the filters.
- [ ] T119 [US5] Row and toolbar menus in `HistoryToolWindowControl.cs` (~:1077-1133, ~:1967) and `HistoryViewModel.cs` (~:579, ~:736, ~:785-810, ~:832-918):
  - **Row ⋯:** shown on hover or keyboard focus only. Items, in order, as in contracts/ui.md §4: Open query, Open in new tab, Copy SQL, Re-execute, Rename query, Compare…, Remove query and its history, Remove queries older than this…. Every item acts on that row.
  - **Toolbar ⋯:** holds `Export…` (moved from the row menu) and `Clear history…` (the existing DeleteAll action; confirmation `Remove all queries except starred ones? This can't be undone.`).
  - **Remove older:** the confirmation formats the date with `HistoryTimeFormat.Absolute`.
  - **Re-execute:** reuse Open's `ScriptFactory` connection code (~:1988-2082). When it can't connect, open the text in a new tab and put `Connect, then run (F5).` in the status bar.
- [ ] T120 [US5] In `HistoryToolWindowControl.cs` (~:722-779, ~:1351-1392):
  - replace the "Loading..." text with a 12×12 ellipse spinner (the `SchemaProgressMargin` pattern in CLAUDE.md);
  - disconnected overlay: `History is unavailable — the AKML engine isn't connected.` plus a `Retry` button bound to `RetryCommand`;
  - empty states for the preview (`Select a query to see it here.`) and the versions pane (`No earlier versions.`).
- [ ] T121 [US5] Create `src/AkmlSql.Shell.Shared/History/HistoryRestoreService.cs` (add it to the projitems):
  - **Input:** `HistoryRestoreState.RestorableEntryIds` (from T060).
  - **Settings:** `Tabs.SessionRecovery` (master), `Tabs.RestoreOnStartup` ("always" / "prompt" / "never") and `History.RestoreMaxQueries`.
  - **Opening:** fetch each entry's full SQL, then open it with the same code as Open in New Tab, including the connection when `History.ReconnectRestoredQueries` is on, and `DocumentSessionKeys.Adopt`.
  - **Test seams:** injectable opener and prompt.
- [ ] T122 [US5] Create `src/AkmlSql.Shell.Shared/History/RestoreQueriesDialog.cs` (a `ThemeAwareWindow`, added to the projitems). Title `AKML SQL – Restore queries`. It lists the entries (name, `server · database`, time) with checkboxes, all checked by default, and has **Restore selected** and **Not now** buttons.
- [ ] T123 [US5] In `src/AkmlSql.Ssms22/AkmlSqlPackage.cs`, once `ReconcileOpen` completes (T060), run `HistoryRestoreService` on the UI thread (`JoinableTaskFactory.SwitchToMainThreadAsync`).
- [ ] T124 [US5] Move and add History settings rows:
  - `src/AkmlSql.Shell.Shared/Dialogs/Pages/HistoryPage.cs` gains, with existing RowFactory methods (US6 turns the wide ranges into number fields): **Maximum query size** (KB, 16–1024), **Restore open queries when SSMS starts** (`Tabs.SessionRecovery`), **When restoring** (Always / Ask / Never → `Tabs.RestoreOnStartup`), **Maximum number of queries to restore**, **Automatically reconnect restored queries**, **Auto-save interval**, **Max closed tabs** and **Remember advanced search settings**.
  - Remove the moved rows from `TabsPage.cs` (~:73-95).
  - Page reset (T019) keeps working, because each page's `Save` writes only its own rows.
- [ ] T125 [US5] In `src/AkmlSql.Shell.Shared/Commands/RestoreClosedTabCommand.cs` (~:58-121), when `ClosedTabStack` is empty, search history for the most recent closed entry (`IsOpen = false`, first row) and open it through the History open path. The status bar says `Restored '‹name›' from SQL History.`.
- [ ] T126 [US5] Remove the never-wired session-recovery path: `src/AkmlSql.Shell.Shared/Sessions/SessionAutoSave.cs`, `SessionRecoveryInitializer*.cs` and `SessionRecoveryDialog.cs`, together with their projitems entries.
  - First search `src/` (including `src/AkmlSql.Web` and `src/AkmlSql.Engine`) for other callers of the engine's session storage (`src/AkmlSql.Engine/Sessions/*`). Delete the engine handler and files **only** if nothing else uses them; otherwise leave them and note why in this task.
  - Update or remove any tests that referenced the deleted classes.
- [ ] T127 [US5] Build, then run the Core, Engine, Shell and Web.Tests (History) suites. Run quickstart.md scenarios 29–37 and record the results in `baseline.md` under "US5 verification".

**Checkpoint**: SQL History matches SQL Prompt's patterns. **Stop and review before P3.**

---

## Phase 8: User Story 6 — Options arranged the way SQL Prompt users expect (Priority: P3)

**Goal**:
- SQL Prompt's tree and names.
- Child options indented and greyed out.
- Number fields for wide ranges.
- Options in the Command Palette.
- A tab-colour environments grid.
- Every sub-window themed.

It covers gap items OPT-04 to OPT-09.

**Independent Test**: quickstart.md scenarios 38–43.

**Depends on**: US1 (the dead rows are already hidden). US5 T124 must be done before T137 converts History's sliders.

### Tests for User Story 6 (write first)

- [ ] T128 [P] [US6] Rewrite `tests/AkmlSql.Shell.Shared.Tests/OptionsNavStructureTests.cs` to pin contracts/ui.md §1 exactly: group headers, leaf labels, order and page keys (Tags), plus breadcrumb (`Display`) == tree path. Keep the existing AI deep-link assertion (`TestBuildWindowForRenderTest("AI Assistance")`); it now finds a top-level leaf "AI assistance".
- [ ] T129 [P] [US6] Write `tests/AkmlSql.Shell.Shared.Tests/RowFactoryGatingAndNumberTests.cs`:
  - a child row is disabled with its label in `TextDisabled` while its parent CheckBox is unchecked, and re-enabled on check;
  - `AddNumber` accepts `250000`, rejects `abc` (red border, previous value kept), clamps to the range, and ▲/▼ step by `step`.
- [ ] T130 [P] [US6] Write `tests/AkmlSql.Shell.Shared.Tests/OptionsPaletteTests.cs`:
  - `SettingsWindow.BuildOptionsCatalog(settings)` contains "Show nullability info" (Kind Toggle, PageKey `IntelliSense`), and excludes every AI Assistance row and every Info/Button row.
  - Toggling that entry through the palette handler changes **only** `IntelliSense.ShowNullability` in `config.json` (compare the JSON before and after, AppData isolated).
  - `ShowOptions(pageKey, null, "Retention (days)")` selects the History page and focuses that row.
- [ ] T131 [P] [US6] Write `tests/AkmlSql.Core.Tests/Config/TabEnvironmentsMigrationTests.cs`:
  - stock rules → four environments (PRODUCTION #FF4444, STAGING #FFB800, DEV #44BB44, AZURE #4488FF) and every rule linked;
  - duplicate labels with different colours → `Label (2)`;
  - a second load is idempotent;
  - no rules → the four defaults are seeded.

  Also write `tests/AkmlSql.Core.Tests/Tabs/EnvironmentMatcherServerDatabaseTests.cs`: a server rule with a non-empty `DatabaseName` matches only that server **and** database. Existing matcher tests stay green.
- [ ] T132 [P] [US6] Write `tests/AkmlSql.Shell.Shared.Tests/ManageRulesWindowTests.cs` (`[StaFact]`):
  - the WPF window, given the same rule DTOs as the old dialog, returns identical `GetOverrides()` for a set of edits: only changed rows; a row set back to its default removes the override;
  - `RestoreSessionSuppressions` lists the restored ids;
  - rows are grouped by category.
- [ ] T133 [P] [US6] Write `tests/AkmlSql.Shell.Shared.Tests/ThemedButtonPageThemeTests.cs`: `ThemedButton.ApplySecondary(button, PageTheme.Dark)` gives the button a template whose background, hover and pressed brushes come from the dark `PageTheme`, not the stock Aero chrome, using the pattern in `OptionsHoverContrastTests`.

### Implementation for User Story 6

- [ ] T134 [US6] Rebuild the Options tree to contracts/ui.md §1 in `src/AkmlSql.Shell.Shared/Dialogs/SettingsWindow.cs`:
  - **Tree** (`CreateSidebar` ~:527-570): `AddTreeGroup`/`AddTreeLeaf` calls in the exact order and labels, in sentence case.
  - **`pages[]`** (~:1140-1167): the same order.
  - **Page keys stay unchanged.**
  - **Each page's `Display` and `Title`** (`src/AkmlSql.Shell.Shared/Dialogs/Pages/*Page.cs`): `Display` is the breadcrumb (e.g. `Suggestions › Warnings & highlighting` for `SafetyPage`, `Inserted code › Objects & statements` for `InsertStatementsPage`, `Queries › Color` for `TabsPage`, `Navigation` for `NavigationPage`); `Title` is the last segment.
- [ ] T135 [P] [US6] Plain-language labels:
  - "Tables Alias" → "Suggest table aliases" (`IntelliSensePage.cs`);
  - "Temperature (x10)" → "Creativity (temperature)" (`AiAssistancePage.cs`);
  - group headers in sentence case on every page (e.g. "Refresh behavior", "Rename options");
  - every `ctx.RegisterSearch` label equals its on-page label (e.g. the column picker "Default sort"; the alias map label);
  - remove the "SQL Prompt style" wording from the Behavior descriptions.
- [ ] T136 [US6] Parent gating (research R5):
  - `src/AkmlSql.Shell.Shared/Dialogs/Pages/PageTheme.cs`: add `TextDisabled` (from `ThemeTokens.TextDisabled` for both palettes).
  - `src/AkmlSql.Shell.Shared/Dialogs/Pages/RowFactory.cs`: add an optional `CheckBox? parent` to `AddToggle`, `AddDropdown`, `AddTextInput`, `AddButton` and `AddNumber`. When set:
    - add +20 px indent;
    - bind `IsEnabled` to `parent.IsChecked`;
    - swap the label foreground between `FgPrimary` and `TextDisabled` on the parent's Checked/Unchecked;
    - add the tooltip `Takes effect when "‹parent label›" is on`.
  - Apply it on:
    - `IntelliSensePage` (Enable IntelliSense → all rows);
    - `HistoryPage` (Enable SQL history → all rows);
    - `SafetyPage` (Enable transaction reminder → Reminder interval);
    - `TabsPage` (Enable tab coloring → gradient and grid);
    - `CodeAnalysisPage` (Enable code analysis → all rows);
    - `SnippetsPage` (Enable snippets → all rows);
    - `SpecialCharactersPage` (auto-close master → five characters);
    - `FormattingPage` (Enable SQL formatter → all other rows).
- [ ] T137 [US6] Number fields:
  - Add `RowFactory.AddNumber(StackPanel panel, string label, int min, int max, int step, string unit, string description = "", CheckBox? parent = null)`, returning `(FrameworkElement Row, TextBox Box)` with ▲/▼ buttons, the unit `TextBlock`, and validation (red border, last valid value kept).
  - Replace these sliders, updating each page's `Controls` `Load`/`Save`:
    - `IntelliSensePage` Maximum suggestions (~:41) and Trigger delay (~:46);
    - `HistoryPage` Retention, Max entries, Maximum query size and Auto-save interval;
    - `AiAssistancePage` Max response tokens (~:286) and Timeout (~:296);
    - `ExecutionPage` Notification threshold (~:26);
    - `SafetyPage` Reminder interval (~:52).
  - Remove units from the label text.
  - Extend `FlashRow` (`SettingsWindow.cs` ~:1111-1131) to any `Panel`, not only `Border`.
- [ ] T138 [US6] Options in the Command Palette (research R7):
  - `SettingsWindow.cs`: add `internal static IReadOnlyList<OptionsCatalogEntry> BuildOptionsCatalog(AppSettings settings)`. It builds pages on a throwaway instance on the UI thread and returns PageKey, PageDisplay, Label, Description and Kind, with the Row kept internally. Cache it per session.
  - Create `src/AkmlSql.Shell.Shared/Productivity/CommandPalette/OptionPaletteEntry.cs` (a `CommandEntry` subclass implementing `INotifyPropertyChanged`, with IsOn and StateText; Id `opt:{pageKey}:{label}`) and add it to the projitems.
  - `CommandRegistry.cs`: an **Options** category listed when the query is at least 2 characters, excluding the AI Assistance page and Info/Button rows. Names read `‹PageDisplay› › ‹Label›`.
  - `CommandPaletteViewModel.cs` `ExecuteCommand` (~:141-177):
    - for `opt:` Toggle entries: `s = ConfigManager.Load()`, then `controls.Load(s)`, flip the CheckBox, `controls.Save(s)`, `OptionsCommand.SaveAndNotify(s)`; update IsOn; **don't** raise `CloseRequested`, and don't count usage;
    - for other kinds: close and call `OptionsCommand.ShowOptions(pageKey, null, label)`.
  - `CommandPaletteWindow.cs`: an item template selector that shows `On`/`Off` for option entries.
- [ ] T139 [US6] Add an overload `internal static bool ShowOptions(string? pageKey, string? agentId, string? focusLabel)` to `src/AkmlSql.Shell.Shared/Commands/OptionsCommand.cs`. Keep the 2-argument method delegating to it, because `Ai/AiChatPanel.cs:135`'s `Func<string?,string?,bool>` must still compile.
  - After the window is built, find the `SearchEntry` with that label on that page and reuse the scroll-and-flash code from `CommitSelectedSearchResult` (`SettingsWindow.cs` ~:1046-1072).
- [ ] T140 [US6] Environments model:
  - Create `src/AkmlSql.Core/Models/Tabs/TabEnvironment.cs` (Name, Color).
  - `src/AkmlSql.Core/Config/AppSettings.cs`: add `TabSettings.Environments` and `ColoringRule.Environment`.
  - `src/AkmlSql.Core/Config/ConfigManager.cs`: run the migration from data-model.md §1.3 in both `Load` overloads, idempotently.
  - `src/AkmlSql.Core/Models/Tabs/EnvironmentMatcher.cs` (~:39-64): a server rule with a non-empty `DatabaseName` requires the database to match as well.
  - When rules are saved, write each rule's `Label` and `Color` from its environment. Safety keys on `Label` (`Safety/ExecutionInterceptor.cs:257-262`, `EnvironmentSeverity`).
- [ ] T141 [US6] Queries › Color page grid (`src/AkmlSql.Shell.Shared/Dialogs/Pages/TabsPage.cs`):
  - replace the ListBox and Add/Edit/Remove buttons (~:33-70) with a themed grid: `ListView`/`GridView` built from `PageTheme` brushes, following the `AiAgentListView.BuildItemStyle` pattern;
  - columns: **Server / group pattern** (text), **Database** (text, optional), **Environment** (swatch plus combo of environment names);
  - **+ Add server/database**, **Remove** and ↑/↓ buttons; ↑/↓ renumber `Order` 0..n-1 after every change;
  - hint `You can use wildcards (*)`;
  - an **Edit environments…** button;
  - rules and environments are part of the page's `Load`/`Save`;
  - remove the host-owned rule CRUD and `ShowRuleEditor` from `SettingsWindow.cs` (~:2001-2151), and remove the now-unused hooks (~:1191-1196).
- [ ] T142 [US6] Create `src/AkmlSql.Shell.Shared/Dialogs/EditEnvironmentsDialog.cs` (`ThemeAwareWindow`, title `AKML SQL – Edit environments`, added to the projitems):
  - **List:** Name (editable) and Colour (a swatch that opens a colour grid of the 8 `ThemeTokens.TabColor*` colours plus **Custom…**, which opens `System.Windows.Forms.ColorDialog` and stores `#RRGGBB`).
  - **Options:** **Use gradient colors** (`Tabs.GradientColors`) and **Restore default environments**.
  - **Validation:** names unique and 1–40 characters. Deleting an environment that rules use is refused, with the rule patterns named.
  - Save / Cancel.
- [ ] T143 [US6] Themed buttons (research R9):
  - `src/AkmlSql.Shell.Shared/Ui/Theme/ThemedButton.cs`: add `ApplySecondary(Button, PageTheme)` and `ApplyPrimary(Button, PageTheme)`, with templates cached per `PageTheme`, following `ComboBoxTheming.ThemeCache`.
  - Use them in:
    - `SettingsWindow.MakeButton`/`MakePrimaryButton` (~:1467-1521);
    - `RowFactory.AddButton` (~:135-163);
    - `AiAgentListView.MakeButton` (~:333-343; add a `PageTheme` parameter; callers at ~:65-68 and `AiAssistancePage.cs` ~:241-244);
    - the Color page buttons.
  - Replace the hard-coded search badge colours (`SettingsWindow.cs` ~:905-912) with `PageTheme` brushes.
- [ ] T144 [US6] Port `src/AkmlSql.Shell.Shared/Analysis/ManageRulesDialog.cs` from WinForms to a WPF `ThemeAwareWindow`, title `AKML SQL – Code analysis rules`:
  - rules grouped by category with expandable headers; each row has Enabled (CheckBox), Rule id, Name, a Severity combo (Hint / Information / Warning / Error) and a Fix ✓ glyph;
  - a description pane for the selected rule;
  - `Settings file:` showing `Constants.ConfigFilePath`;
  - the session-suppressed strip with **Restore**;
  - Save / Cancel.

  Keep the public surface that `ManageRulesCommand.cs` uses unchanged: the constructor inputs, `ShowDialog` result, `GetOverrides()` and `RestoreSessionSuppressions`.

  Also add a **Manage rules…** button to `CodeAnalysisPage.cs`. It opens the same window, then reloads `_settings.CodeAnalysis.RuleOverrides` from disk (the pattern in `FormattingPage.RefreshActiveStyleFromDisk` ~:197-207), so OK in Options doesn't write stale overrides.
- [ ] T145 [US6] Build, then run the Core and Shell suites. Run quickstart.md scenarios 38–43, including a dark-theme pass through every Options sub-window, and record the results in `baseline.md` under "US6 verification".

**Checkpoint**: Options matches SQL Prompt's arrangement.

---

## Phase 9: User Story 7 — Polish and team sharing (Priority: P3)

**Goal**:
- A grouped AKML SQL menu.
- One title style.
- F1 help.
- Accessible names.
- Team style folder.
- Format SQL actions.

It covers gap items X-01 to X-04, STY-10 and STY-11.

**Independent Test**: quickstart.md scenarios 44–49.

### Tests for User Story 7 (write first)

- [ ] T146 [P] [US7] Write `tests/AkmlSql.Site.Tests/Docs/F1SlugTests.cs`, following the `FooterDocLinksTests` pattern:
  - read `src/AkmlSql.Shell.Shared/Help/F1HelpRegistrations.cs` and every `HelpTopic` in `src/AkmlSql.Shell.Shared/Dialogs/Pages/*.cs` as text, and extract the slug and anchor;
  - assert each slug exists in `DocsCatalog.Scan(repo/doc, options)`;
  - assert each anchor matches a heading id in that doc (Markdig AutoIdentifiers rules).
- [ ] T147 [P] [US7] Write `tests/AkmlSql.Core.Tests/Config/WindowTitlesTests.cs` (`WindowTitles.For("Options") == "AKML SQL – Options"`). Also write `tests/AkmlSql.Shell.Shared.Tests/WindowTitleUsageTests.cs`, a text scan asserting that every `Title =` and `Text =` assignment on a Window or Form in `src/AkmlSql.Shell.Shared` uses `WindowTitles.For(...)`, apart from an explicit allow-list (file dialogs, tool-window captions, message-box captions).
- [ ] T148 [P] [US7] Write `tests/AkmlSql.Shell.Shared.Tests/AkmlMenuTableTests.cs` against the declarative table (T152), exposed as `internal static AkmlMenuTable.Entries`:
  - exactly 12 top-level entries, in contracts/ui.md §2 order;
  - Help ▸ ends with Check for Updates then About AKML SQL;
  - every command id in the table is in the set of command ids the package registers (a list exported by a new `internal static IReadOnlyCollection<int> RegisteredCommandIds` in `AkmlSqlPackage`, or a shared constants list);
  - no unregistered command (TextToSql, AI Optimize, AI Index Analysis, CRUD, Grid Find) appears.
- [ ] T149 [P] [US7] Write `tests/AkmlSql.Formatting.Tests/Profiles/TeamStyleFolderTests.cs`:
  - a style in a team folder is listed with `Source = "team"`;
  - it is read-only when the folder has a read-only attribute or ACL (simulate with a provider flag or a temp folder marked read-only);
  - name precedence is user > team > built-in;
  - an unreachable folder (a non-existent UNC path) returns the other styles within 2 s, and `TeamFolderUnavailable = true`.

  Also add engine tests in `tests/AkmlSql.Engine.Tests/Formatter/TeamStyleWriteRefusalTests.cs`: Save, Rename, Delete and Reset on a read-only team style return `Success = false` with the contract error text.
- [ ] T150 [P] [US7] Write `tests/AkmlSql.Engine.Tests/Formatter/FormatSqlActionsTests.cs`:
  - `Actions = null` → output identical to today for three golden inputs;
  - semicolons insert and remove;
  - brackets add and remove;
  - `ApplyCasing = false` keeps the original keyword case;
  - `ApplyLayout = false` keeps the original whitespace but still applies the semicolons action;
  - `ExpandWildcards = true` expands `SELECT *` using a schema-cache fixture (the pattern in `FormatActionDispatchTests.cs`).
- [ ] T151 [P] [US7] Write `tests/AkmlSql.Core.Tests/Ipc/FormattingContractTests.cs`: round trips for `FormatSqlActionsDto`, `FormatRequest` key 5, `FormatSelectionRequest` key 5 and `ProfileInfo` keys 9–10, plus the legacy shapes.

  Also write `tests/AkmlSql.Shell.Shared.Tests/HistoryAccessibilityTests.cs`: every icon-only control in the History toolbar and row template, and the style list ⋮, has a non-empty `AutomationProperties.Name`.

### Implementation for User Story 7

- [ ] T152 [US7] Build the menu from one declarative table:
  - Create `src/AkmlSql.Shell.Shared/Commands/AkmlMenuTable.cs` (added to the projitems): a declarative tree of groups, submenus and command ids matching contracts/ui.md §2, including the Active Style ▸ submenu (the US4 slots plus Edit Styles…).
  - Rewrite `EnsureTopLevelMenu` in `src/AkmlSql.Ssms22/AkmlSqlPackage.cs` (~:383-490) to build from it:
    - nested `msoControlPopup` (type 10) submenus;
    - `BeginGroup = true` for separators;
    - the version marker `popup.Tag = "akml-menu-v2"`;
    - when an existing "AKML SQL" popup lacks the marker, delete it and rebuild **once**;
    - the AI ▸ submenu shown only when AI is enabled, reusing `AiCommandVisibility`;
    - the editor context popup from T095 kept.
  - Remove the interim placement code from T095 that the builder now covers.
- [ ] T153 [US7] In `src/AkmlSql.Ssms22/AkmlSqlSsms22.vsct`:
  - add groups and menus mirroring contracts/ui.md §2 (new IDSymbols for the submenus and groups, avoiding every id in `PackageGuids.cs` and the reserved ranges);
  - re-parent the buttons;
  - keep every command id and key binding unchanged;
  - after building, confirm `dte.ExecuteCommand("AKML_SQL.FormatDocument")` still resolves (the editor toolbar and completion popup use it). If the canonical name changed, pin it with `<Strings><CanonicalName>`.
- [ ] T154 [US7] Window titles and icon:
  - Create `src/AkmlSql.Core/Config/WindowTitles.cs` (`public static string For(string name) => Constants.ProductName + " – " + name;`).
  - Apply it to every window and form listed in contracts/ui.md §5. File references are in research R27; the list includes `SettingsWindow.cs:283`, `FormatStylesEditorWindow.cs:136`, `StyleNameDialog.cs:171/185`, `ImportSummaryDialog.cs:46`, `SnippetManagerDialog.cs:48`, `HistoryDiffWindow.cs:22`, `ObjectSearchWindow.cs:49` and the WinForms forms.
  - Add `src/AkmlSql.Shell.Shared/Ui/WindowIcon.cs` (added to the projitems): it sets the window icon from an embedded `akml.ico`, copied from `src/AkmlSql.Installer/assets/icon.ico` into `src/AkmlSql.Shell.Shared/Resources/akml.ico` and declared as `<EmbeddedResource>` in the projitems. Call it from `ThemeAwareWindow` and from the WPF windows above that don't derive from it.
- [ ] T155 [US7] Wire F1 help:
  - Create `src/AkmlSql.Shell.Shared/Help/HelpBinding.cs` (added to the projitems): `Attach(UIElement element, Func<string> topicKey)` adds a `CommandBinding(ApplicationCommands.Help)` that calls `F1HelpListener.Open`.
  - `F1HelpRegistrations.cs`: `DocBase = "https://akml.khamis.work/docs/"`; remap the existing keys to real slugs (`topics/formatting`, `topics/sql-history`, `topics/snippets`, `topics/static-analysis`, `topics/intellisense`, …) and remove keys with no topic.
  - `IPageBuilder`: add `string HelpTopic { get; }`, implemented on every page with the values in contracts/ui.md §1.
  - `SettingsWindow.OnWindowKeyDown` (~:1638): F1 → the current page's topic.
  - `FormatStylesEditorWindow`: set `HasHelpButton = true`, override `InvokeDialogHelp()`, and attach `HelpBinding` → `topics/formatting#edit-styles-with-live-preview`.
  - `src/AkmlSql.Shell.Shared/History/HistoryToolWindow.cs`: handle `VSConstants.VSStd97CmdID.F1Help` in the pane's command target (implement `IOleCommandTarget` on the pane if needed) → `topics/sql-history`.
- [ ] T156 [P] [US7] Write the docs:
  - Create `doc/topics/options.md`, with one `##` section per Options page. Headings must produce exactly the anchors in contracts/ui.md §1 (e.g. `## Suggestions: Behavior` → check the generated id matches `suggestions-behavior`, and adjust the heading text if needed). Each section describes, in plain language, only the settings still shown after US1.
  - Update `doc/topics/formatting.md` (Active Style menu, option search, change markers, team style folder, Format SQL actions) and `doc/topics/sql-history.md` (search syntax, Advanced search, open marker, versions and compare, restore on start, keyboard).
- [ ] T157 [US7] Accessibility and type:
  - `src/AkmlSql.Shell.Shared/History/HistoryToolWindowControl.cs`: add `AutomationProperties.Name` to every icon-only control ("Refresh", "Show starred queries only", "Show open queries only", "Show closed queries only", "Filter by server or database", "More actions", "Clear search").
  - Map the hard-coded `FontSize` values (research R27: 9, 9.5, 10, 10.5, 11, 11.5 → `Typography.Small`; 12 → `Typography.Body`; 14 → `Typography.H4`).
  - Replace `OpenClosedColorConverter` and `FavoriteColorConverter` (~:2193, ~:2461) with `SetResourceReference` on `ThemeTokens.HistoryOpenIcon`, `HistoryClosedIcon`, `HistoryStarActive` and `HistoryStarInactive`, so they follow theme changes.
  - Give the style list ⋮ in `FormatStylesEditorWindow.cs` the name "Style actions".
- [ ] T158 [US7] Team style folder, engine side:
  - `src/AkmlSql.Core/Config/AppSettings.cs`: add `FormatterSettings.TeamStyleFolder` (default "").
  - `src/AkmlSql.Formatting/Profiles/ProfileManager.cs`:
    - an optional `Func<string?> teamFolderProvider` constructor parameter;
    - a third read-only directory, with name precedence user > team > built-in;
    - a write probe (create and delete a temp file) that sets `IsReadOnly`;
    - the directory-stamp cache extended with the team stamp, and skipped when unreachable (2 s timeout on existence and enumeration, via `Task.Run` plus `Wait`);
    - `List()` sets `Source` and `IsReadOnly`, and exposes `TeamFolderUnavailable`.
  - `src/AkmlSql.Engine/EngineHandlerRegistry.cs` (~:55): pass `() => ctx.EnsureSettings().Formatter.TeamStyleFolder`.
  - `src/AkmlSql.Core/Ipc/Messages/ProfileInfo.cs`: `[Key(9)] string? Source` and `[Key(10)] bool IsReadOnly`.
  - `src/AkmlSql.Engine/Formatter/FormatRequestHandler.cs` profile handlers: Save, Rename, Delete and Reset on a read-only team style return the contracts/ipc.md error text.
- [ ] T159 [US7] Team style folder, shell side:
  - `src/AkmlSql.Shell.Shared/Dialogs/Pages/FormattingPage.cs`: a **Team style folder** text row with a **…** button (`System.Windows.Forms.FolderBrowserDialog`), validated as rooted (`Path.IsPathRooted`, then `Path.GetFullPath`).
  - `FormatStylesEditorViewModel.cs` `StyleListItem`: add `Source` and `IsReadOnly`.
  - `FormatStylesEditorWindow.cs`:
    - a **TEAM STYLES** group in the list;
    - read-only team styles: options disabled, Save, Rename and Delete disabled, Copy allowed;
    - when the team folder is unavailable, a muted row `Team styles unavailable — ‹folder› can't be reached`.
- [ ] T160 [US7] Format SQL actions, settings and DTO:
  - `src/AkmlSql.Core/Config/AppSettings.cs`: a `FormatSqlActions` class and `FormatterSettings.FormatSqlActions`. Defaults come from the built-in profiles' `formatActions` (read `src/AkmlSql.Formatting/Profiles/BuiltIn/*.akmlstyle`; use the common value).
  - Create `src/AkmlSql.Core/Ipc/Messages/FormatSqlActionsDto.cs` per contracts/ipc.md.
  - `FormatRequest.cs` and `FormatSelectionRequest.cs`: `[Key(5)] FormatSqlActionsDto? Actions`.
- [ ] T161 [US7] Format SQL actions, engine side:
  - `src/AkmlSql.Engine/Formatter/FormatRequestHandler.cs` `HandleFormat` / `HandleFormatSelection`: when `request.Actions != null`, use it instead of `profile.FormatActions`.
  - `src/AkmlSql.Formatting/Pipeline/FormatterPipeline.cs`:
    - add pipeline options `ApplyLayout` and `ApplyCasing`. When layout is off, skip the layout stage and keep the original whitespace; when casing is off, skip the casing stage.
    - Stage 8 takes the semicolons and brackets choices from the options.
    - All defaults preserve today's behaviour.
  - `src/AkmlSql.Engine/Handlers/Formatting/FormattingHandlers.cs` (~:27-28): pass `ctx.SchemaCache` and `ctx.Sessions`, and after validation run the schema-aware Expand wildcards / Qualify object names operations (the same ones `HandleFormatAction` uses) when requested.
- [ ] T162 [US7] Format SQL actions, shell side:
  - `src/AkmlSql.Shell.Shared/Formatting/FormatDocumentCommand.cs` (~:86) and `FormatSelectionCommand.cs`: send `Actions` built from `Formatter.FormatSqlActions`, and the **real** editor session id (the `RefactorCommandHelper.TryGetActiveEditor()` pattern) instead of a random GUID.
  - `src/AkmlSql.Shell.Shared/Dialogs/Pages/FormattingPage.cs`: add a group headed `When you run Format SQL, AKML SQL will:` with Apply layout, Apply casing, Semicolons (Insert / Remove / Leave), Square brackets (Add / Remove / Leave), Expand wildcards and Qualify object names.
- [ ] T163 [US7] Build, then run the Core, Engine, Formatting, Site and Shell suites, plus the format-parity goldens (they must be unchanged). Run quickstart.md scenarios 44–49 and record the results in `baseline.md` under "US7 verification".

**Checkpoint**: all 38 gap-plan items are delivered.

---

## Phase 10: Polish and cross-cutting concerns

- [ ] T164 Run a full solution build (restore + build, MSBuild), then every suite from T002. Compare with `baseline.md`: new failures are regressions to fix. Confirm the `CorpusGateTests` pass rate hasn't dropped and the format-parity goldens are unchanged.
- [ ] T165 [P] Extend `tests/AkmlSql.UiTests/SsmsScreenshotTour.cs`. Add captures of:
  - the AKML SQL menu expanded, and the Active Style ▸ submenu;
  - Options in light and dark (the Behavior, History and Color pages);
  - SQL History with Advanced search open;
  - the Format Styles window at its default size on the Lists page.

  Keep Northwind only, and keep the forbidden-words assertion. Run it against the deployed build (quickstart §0 deploy).
- [ ] T166 [P] Update `doc/progress.md`: append a `## Spec 040 — SQL Prompt UI/UX parity: Options, SQL History, format styles (2026-09-28)` section in the existing format ("What the investigation found", "What was built", "Verification" with pass counts, "Issues hit", "Open").
- [ ] T167 [P] Update `CLAUDE.md`:
  - "Latest merged work": add a spec 040 bullet;
  - "Open follow-ups": list the items in T168;
  - the Documentation table row and the Progress paragraph that name "most recently spec 037";
  - correct the stale "Theme colors come from `ThemeManager.Instance`" guidance (~:256) to ThemeRegistry / ThemeTokens / `SetResourceReference`.

  Also update `doc/deployment.md`: the stale VS 2022 MSBuild path and the "never build via .slnx" note (see CLAUDE.md Build Commands).
- [ ] T168 [P] Record the follow-ups that stay open, in this file's "Deferred" section and in `doc/progress.md`:
  - commands with no registered handler (TextToSql, AI Optimize, AI Index Analysis, Generate CRUD Procedures, Find in Results Grid);
  - the VSCT menu parent (`IDM_VS_MENU_BAR` = 0x0081 is Edit), which leaves the VSCT menu invisible in SSMS 22;
  - AS keyword and column alias style in Format SQL actions;
  - Record failed executions and Encrypt at rest (hidden);
  - the editor context menu, if scenario 26 was waived.
- [ ] T169 [P] Update `doc/_Prompt-Gap/11-UI-UX-Plan-Options-History-Styles.md`: add an "Implementation status (spec 040)" note at the top mapping each OPT/HIS/STY/X item to done or deferred, with the task ids. Refresh the file 10/11 lines in `doc/_Prompt-Gap/00-INDEX-and-Questions.md`.
- [ ] T170 Run quickstart.md end to end (scenarios 1–49) on the deployed build. Record pass, fail or waived (with a reason) for each scenario in `specs/040-sqlprompt-ui-parity/baseline.md` under "Final verification". Restore the developer's settings and history from the T003 backup if a scenario damaged them.

---

## Deferred

*(Fill in during T168. Each entry: item · reason · where it's recorded.)*

---

## Dependencies and execution order

### Phase dependencies

- **Phase 1 (Setup)** → **Phase 2 (Foundational)** → user stories.
- **US1 (Phase 3)**: needs Phase 2 only through T004 (not strictly). It can start right after Phase 1.
- **US2 (Phase 4)**: needs T004 (SqlPreviewView). Independent of US1.
- **US3 (Phase 5)**: needs T004. Independent of US1 and US2.
- **US4 (Phase 6)**: needs US3, which it builds on (the preview control in the editor, rows). It also touches `OptionsCommand.SaveAndNotify`, which US1 changes (T017, T030), so merge after US1.
- **US5 (Phase 7)**: needs US2 (open state, `RestorableEntryIds`, group actions) and T006 (HexBrush).
- **US6 (Phase 8)**: needs US1 (hidden rows, reset rewrite). T137 needs US5 T124 (the History rows exist before they are converted to number fields).
- **US7 (Phase 9)**: needs US4 (Active Style slots, T091–T095). It touches pages that US6 renamed, so merge after US6.
- **Polish (Phase 10)**: after every story it covers.

**Delivery rule** (FR-071): finish and verify **US1 + US2 + US3** before merging P2 work; finish **US4 + US5** before P3.

### Within each story

- Tests first (they must fail), then Core or DTO changes, then the engine, then the shell, then verification.
- These files are edited by several tasks in sequence. Never mark two tasks on the same file `[P]`:
  - `CompletionController.cs`: T028 → T029 → T033;
  - `SettingsWindow.cs`: T016 → T019 → T020 → T021 → T022 → T039;
  - `HistoryDatabase.cs`: T053 → T054 → T055 → T056 → T108;
  - `FormatStylesEditorWindow.cs`: T068 → T070 → T071 → T072 → T083 → T085 → T086 → T087 → T088 → T089 → T159;
  - `AkmlSqlPackage.cs`: T060 → T092 → T095 → T123 → T152.

## Parallel opportunities

- **Setup:** T002 and T003 in parallel.
- **Foundational:** T005 and T006 in parallel once T004 exists (T005 tests T004).
- **US1:**
  - all tests T007–T014 in parallel;
  - then T018, T024, T025, T032, T034, T035–T038 and T040 in parallel (different files);
  - the engine chain T024 → T026 → T027 runs alongside the shell chain T028 → T029 → T033.
- **US2:** tests T042–T047 and T049–T051 in parallel; T052 and T058 in parallel with the engine chain.
- **US3:** tests T065–T067 in parallel.
- **US4:** tests T074–T080 in parallel; T081, T090 and T091 in parallel.
- **US5:** tests T097–T104 in parallel; T105, T106 and T107 in parallel; the engine T108 → T109 alongside the shell T110 → T111.
- **US6:** tests T128–T133 in parallel; T135 and T140 in parallel.
- **US7:** tests T146–T151 in parallel; T156 (docs) in parallel with any code task.
- **Across stories** (with separate developers): US1, US2 and US3 can proceed at the same time after Phase 2.

### Parallel example: User Story 2

```text
Together: T042 (Core DTO tests) · T043 (schema v3) · T044 (group actions) · T045 (open state) · T046 (snapshot search) · T047 (group filters) · T049 (paging) · T050 (session keys) · T051 (preview/actions)
Then:     T052 (DTO keys) ∥ T058 (DocumentSessionKeys)  →  T053 → T054 → T055 → T056 → T057 (engine)  ∥  T059 → T060 (capture/package)  →  T061 → T062 → T063 (UI)  →  T064
```

## Implementation strategy

### MVP first

1. Phase 1 and Phase 2.
2. **US1 (Options I can trust)**: the MVP. It removes the worst risk (Options silently saving half-loaded settings and wiping AI agents) and every setting that lies.
3. Stop: run quickstart 1–10, then review.

### Incremental delivery

1. **P1 block:** US1 → US2 → US3, each verified by its quickstart scenarios, then review. This is the "make the screen tell the truth" release.
2. **P2 block:** US4 → US5, then review.
3. **P3 block:** US6 → US7 → Polish.
4. After every story: build, run the suites, compare with `baseline.md`, and run that story's quickstart scenarios.

### Notes

- Each story leaves the product working. Nothing half-done blocks shipping at a checkpoint.
- If a line reference no longer matches, search for the method named in the task; do not guess.
- When a task finds a setting, command or behaviour that differs from research.md, stop and record the difference in the task before changing course.
