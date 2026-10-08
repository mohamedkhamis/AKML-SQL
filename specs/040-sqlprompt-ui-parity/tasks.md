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

- [X] T001 Build the whole solution green in one pass, with the MSBuild commands above, from `C:\Repos\AKML\AKML-SQL`. Fix nothing yet: just confirm it builds.
- [X] T002 [P] Run `tests/AkmlSql.Core.Tests`, `tests/AkmlSql.Engine.Tests`, `tests/AkmlSql.IntelliSense.Tests`, `tests/AkmlSql.Formatting.Tests`, `tests/AkmlSql.Site.Tests`, `tests/AkmlSql.Web.Tests` (`dotnet test <csproj>`) and `tests/AkmlSql.Shell.Shared.Tests` (MSBuild build, then `dotnet test` the built dll).
  - Record pass/fail counts in `specs/040-sqlprompt-ui-parity/baseline.md`. Include the completion-corpus pass rate (`CorpusGateTests`) and the format-parity golden count.
  - List any failures that were already failing: `PerformanceBaselineTests`, the History 2 ms timing test, `VisualReferenceCoverageTests`, the `sp031-*` goldens.
- [X] T003 [P] Copy `%AppData%\AKML SQL\config.json`, `%AppData%\AKML SQL\history\` and `%AppData%\AKML SQL\profiles\` to `%UserProfile%\Documents\AKML SQL backups\spec-040-<yyyyMMdd>\`, and record the path in `specs/040-sqlprompt-ui-parity/baseline.md`. Manual checks later reset settings and delete history.

**Checkpoint**: baseline recorded. From now on, any new red in T002's list is a regression this feature caused.

---

## Phase 2: Foundational (blocking prerequisites)

**Purpose**: shared UI pieces that several stories use. **US2, US3, US4, US5 and US6 depend on this phase.**

- [X] T004 Write `tests/AkmlSql.Shell.Shared.Tests/SqlPreviewViewTests.cs` (`[StaFact]`, in the "AkmlSql ThemeRegistry" collection) **before** the control in T005; it also covers FR-033 (coloured preview). Cover:
  - `ExpandTabs("ab\tc", 4) == "ab  c"`, `ExpandTabs("\tx", 2) == "  x"`, and multi-line text where the tab stops restart on each line;
  - keyword, string and comment tokens produce `Run`s whose foreground resolves to the matching token brush;
  - `HighlightLines = {1}` sets that line's background;
  - text over `MaxDisplayChars` ends with the notice line;
  - the `RichTextBox` is read-only but focusable, so text can be selected.
- [X] T005 Create the read-only, selectable SQL preview control `SqlPreviewView` in `src/AkmlSql.Shell.Shared/Ui/SqlPreview/SqlPreviewView.cs`, and add it to the projitems. Research R20.
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
- [X] T006 [P] Extract a shared hex → frozen brush helper into `src/AkmlSql.Shell.Shared/Tabs/HexBrush.cs`, and add it to the projitems.
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

- [X] T007 [P] [US1] Write `tests/AkmlSql.Shell.Shared.Tests/OptionsThemeSafetyTests.cs` (`[StaFact]`, `AppDataIsolatedTest`, ThemeRegistry collection). Use the host-variant seam from T021 and `SettingsWindow.TestBuildWindowForRenderTest`. Four cases:
  1. With `theme:"system"` and a Dark host variant, building the window and loading controls leaves `config.json` byte-identical and `ThemeChangeRequested == false`.
  2. With a saved `"dark"` theme, calling `ResetAllToDefaultsCore()` then discarding leaves `config.json` unchanged.
  3. Importing a settings file whose theme differs doesn't write `config.json`.
  4. When the user picks Light while the window is Dark, `ThemeChangeRequested == true`, `WorkingCopy` contains an unsaved edit made on another page, and `config.json` is unchanged.
- [X] T008 [P] [US1] Write `tests/AkmlSql.Shell.Shared.Tests/OptionsPageResetTests.cs`.
  - **Sentinels:** seed `CodeAnalysis.RuleOverrides["ST001"]={Enabled=false}`, one `Ai.Agents` entry, one `Navigation.ConnectionAliases` entry, `Safety.EnvironmentSeverity["PRODUCTION"]`, and `Formatter.ActiveProfile="Collapsed"`.
  - **For every registered page key:** call `ResetPageToDefaultsCore(key)` and assert every sentinel survives, except the fields that page shows. The Formatting page resets `ActiveProfile`; the AI Assistance page resets the agents.
  - **Unsaved edits:** an unsaved edit on a different page survives a page reset.
  - **Confirmation text:** `ResetConfirmationText(key)` uses the page's display name (e.g. contains "Suggestions › Tooltips" or the post-US6 label), and for AI Assistance contains the agent count.
- [X] T009 [P] [US1] Write `tests/AkmlSql.Core.Tests/Config/PreserveInstallStateTests.cs`: `ConfigManager.PreserveInstallState(from, to)` copies `InstallId`, `InstalledTargets`, `LastUpdateCheck`, `NativeIntelliSensePrompted`, `DisabledNativeIntelliSense`, `CommandPalette.UsageCounts`, `CommandPalette.RecentItems` and `ConfigVersion`, and nothing else.
- [X] T010 [P] [US1] Write `tests/AkmlSql.Shell.Shared.Tests/OptionsLiveSettingsTests.cs`.
  - Build the window and read the private `_searchIndex` by reflection (the pattern in `OptionsConnectionsHelpTests.cs:117-131`).
  - Assert every `(PageKey, Label)` is in an explicit allow-list declared in the test. That list is today's labels minus the hidden rows (research R1, spec Appendix A).
  - Assert none of the hidden labels appear: "Keyword casing", "List all database columns after a SELECT statement", "Freeze headers", "Encrypt at rest", "Record failed executions", "Format on paste", "Format on save", "Format on delimiter", "Confirm before bulk format", "Validate formatting preserves semantics", "Respect --noformat regions", "Named regions", "Show preview before applying", "Rename scope", "Chat panel", and the Labs rows.
  - Assert the tree has no leaf tagged `Schema Cache` or `Labs`.
- [X] T011 [P] [US1] Write `tests/AkmlSql.Engine.Tests/Handlers/CompletionHandlerSettingsTests.cs`, extending the setup in `CompletionHandlerTests.cs`. Assert:
  - `IntelliSense.MaxSuggestions = 10` → at most 10 items for `SELECT * FROM dbo.` on the test schema;
  - `FuzzyMatch = false` → only case-insensitive prefix matches: `unit` finds `UnitPrice` but not `QuantityPerUnit`;
  - `ShowNullability = false` → no `NULL`/`NOT NULL` in `SecondaryText`;
  - all three detail flags off → `SecondaryText` has no type, key or `•` prefix;
  - defaults reproduce today's text exactly.
- [X] T012 [P] [US1] Write `tests/AkmlSql.IntelliSense.Tests/Completion/ColumnProviderSecondaryTextTests.cs` covering every flag combination of `FormatSecondaryText` (types, nullability, key indicators including IDENTITY/COMPUTED, and the table suffix `" • Products"` only when something precedes it).
- [X] T013 [P] [US1] Write `tests/AkmlSql.Shell.Shared.Tests/CompletionPrefixOnlyFilterTests.cs`. `CompletionItemModel.MatchesFilter` and `FilterScore` in prefix-only mode:
  - use `FilterText` when it is set (`"p.UnitPrice"` matches `"p.Unit"`);
  - reject substring and CamelCase matches;
  - keep today's behaviour when prefix-only is off.
- [X] T014 [P] [US1] Write `tests/AkmlSql.Shell.Shared.Tests/ErrorListGateTests.cs` for the pure helper `ErrorListReporter.ShouldPublish(CodeAnalysisSettings)` and for the static registry's `ReapplyAll()` (with a fake reporter that records clear and republish calls).
- [X] T015 [P] [US1] Write `tests/AkmlSql.Shell.Shared.Tests/CompletionTriggerPolicyTests.cs` for the pure helper `CompletionTriggerPolicy.Decide(char typed, bool ctrlSpace, IntelliSenseSettings s)` (created in T035):
  - `TriggerDelayMs = 0` → Immediate; `TriggerDelayMs = 1000` → Delayed(1000);
  - typed `.` with `AfterDot = false` → None; with `AfterDot = true` → the delay rule;
  - Ctrl+Space → always Immediate;
  - a non-identifier character (space, `;`) → None.
- [X] T016 [P] [US1] Write `tests/AkmlSql.Shell.Shared.Tests/AnalysisTriggerPolicyTests.cs`: `AnalysisController.ShouldAnalyzeOnEdit(CodeAnalysisSettings s)` returns `s.RunOnType`, and false whenever `s.Enabled` is false.
- [X] T017 [P] [US1] Write `tests/AkmlSql.Shell.Shared.Tests/FormatterEnabledGateTests.cs`: `FormatActionHelper.FormatterDisabledMessage(FormatterSettings s)` returns null when `Enabled` is true, and `AKML SQL formatting is off — turn it on in Options › Format › Styles.` when false.
- [X] T018 [P] [US1] Write `tests/AkmlSql.Shell.Shared.Tests/SnippetGateTests.cs` for the pure `SnippetGate` (created in T039):
  - `ShouldOfferSnippets` is false when `Snippets.Enabled` or `IntelliSense.SnippetsInCompletion` is false;
  - `ExpansionEnabled` follows `Snippets.Enabled`;
  - `FormatOnExpand` returns `Snippets.FormatOnExpand`.
- [X] T019 [P] [US1] Write `tests/AkmlSql.Shell.Shared.Tests/BulkFormatWizardDefaultsTests.cs` (`[StaFact]`, AppData isolated): constructing `BulkFormatWizard` with `Formatter.CreateBackups = false` leaves the backup checkbox unchecked; with `true` it is checked.
- [X] T020 [P] [US1] Write `tests/AkmlSql.Shell.Shared.Tests/OptionsReopenLoopTests.cs` (`[StaFact]`, AppData isolated, `OptionsCommand.TestRpcAccessor` fake), driving `ShowOptions` through `WindowFactoryOverride` (T023) with scripted fake dialogs:
  - `NextStep` returns `Reopen` whenever a theme change was requested, `Save` for OK and `Cancel` otherwise;
  - a theme change opens the next window with the previous window's `WorkingCopy` and `CurrentPageKey`, and `config.json` is unchanged;
  - OK then saves once (one `AnalysisSettingsChanged` notification), and the saved theme is the `ThemeRegistry` preference;
  - Cancel after a theme change writes nothing and restores the original theme preference.

### Implementation for User Story 1 — OPT-02 (Theme drop-down)

- [X] T021 [US1] In `src/AkmlSql.Shell.Shared/Ui/Theme/HostThemeWatcher.cs`, add `internal static Func<ThemeVariant>? VariantOverrideForTests` and a read-only accessor `CurrentHostVariant` that returns the override when set and otherwise `LastDetectedHostVariant`.
- [X] T022 [US1] In `src/AkmlSql.Shell.Shared/Dialogs/SettingsWindow.cs`, fix the Theme handler (research R2):
  1. Add `internal static PageTheme ResolvePageTheme(string? pref)`: "dark" → Dark; "system" → Dark when `HostThemeWatcher.CurrentHostVariant == ThemeVariant.Dark`, else Light; anything else → Light. Use it in the constructor (~:183-184).
  2. Add a `_loadingControls` flag, set with try/finally around the whole body of `LoadSettingsToControls` (~:1953-1974).
  3. In `OnThemeSelectionChanged` (~:1660-1701):
     - return at once while `_loadingControls`;
     - map the index to "dark", "light" or "system";
     - return if `ResolvePageTheme(pick) == _theme`;
     - otherwise run `SaveControlsToSettings()` **without** `ConfigManager.Save`, then `ThemeRegistry.Instance.SetPreference(pick)`, `ThemeChangeRequested = true`, `_dialogResult = true`, and `_window?.Close()`.
  4. Add `internal AppSettings WorkingCopy => _settings;` and `internal string? CurrentPageKey`, the selected tree leaf's `Tag`.
  5. Extract `internal void ResetAllToDefaultsCore()` from `OnResetAllClick` (~:1932-1947). The click handler keeps only the confirmation.
- [X] T023 [US1] Rework the loop in `OptionsCommand.ShowOptions` (`src/AkmlSql.Shell.Shared/Commands/OptionsCommand.cs` ~:52-92):
  - Capture `var originalTheme = settings.Theme` before the loop.
  - After `ShowDialog`, **check `window.ThemeChangeRequested` first**. When set, continue the loop with `settings = window.WorkingCopy; pageKey = window.CurrentPageKey ?? pageKey;`.
  - On OK: `SaveAndNotify(window.GetSettings())`, then `ThemeRegistry.Instance.SetPreference(settings.Theme)`.
  - On Cancel: `ThemeRegistry.Instance.SetPreference(originalTheme)`, then return false.
  - Keep the method's signature (`Func<string?,string?,bool>` callers in `Ai/AiChatPanel.cs:135` must still compile).
  - Make the loop testable: put the decision in `internal static OptionsLoopStep NextStep(bool dialogResult, bool themeChangeRequested)` (`Reopen` when a theme change was requested, else `Save` on OK, else `Cancel`), and create each window through `internal static Func<AppSettings, IOptionsDialog>? WindowFactoryOverride`. `IOptionsDialog` is a small internal interface over `ShowDialog(pageKey)`, `ThemeChangeRequested`, `WorkingCopy`, `CurrentPageKey`, `InitialAgentId` and `GetSettings()`, which `SettingsWindow` implements. Production code never sets the override (tested by T020).

### Implementation for User Story 1 — OPT-03 (Restore Defaults)

- [X] T024 [P] [US1] Add `public static void PreserveInstallState(AppSettings from, AppSettings to)` to `src/AkmlSql.Core/Config/ConfigManager.cs` (fields as in T009). Use it in `SettingsWindow` Import, replacing the inline copy (~:1780-1782).
- [X] T025 [US1] Replace the per-page switch in `ResetPageToDefaultsCore(string pageKey)` (`SettingsWindow.cs` ~:1845-1929) with this, so hidden fields survive:
  ```
  if (!_pageControlsByKey.TryGetValue(pageKey, out var controls)) throw new InvalidOperationException(...)
  SaveControlsToSettings()
  _loadingControls = true
  try { controls.Reset(new AppSettings()); controls.Save(_settings); controls.Load(_settings); }
  finally { _loadingControls = false }
  ```
  - For the `"Tabs & UI"` key, also restore `_settings.Tabs.ColoringRules` to `new TabSettings().ColoringRules`, then call `PopulateColoringRulesList()`. The rules are shown on that page.
  - In `OnResetThisPageClick` (~:1813-1836), remove the call that reloads every page (it discarded unsaved edits on other pages).
- [X] T026 [US1] Add `internal string ResetConfirmationText(string pageKey)` to `SettingsWindow.cs` and use it in `OnResetThisPageClick`.
  - First line: `Reset the settings on {_pageBuilders[pageKey].Display}?`.
  - For `"AI Assistance"` with agents, add a second line: `This also removes your {n} AI agent(s) and their API keys.`
  - For `"Tabs & UI"`, add: `This also restores the default environments and rules.`
  - Never show the raw page key.
- [X] T027 [US1] In `ResetAllToDefaultsCore()` (`SettingsWindow.cs`): create `var fresh = new AppSettings();`, then `ConfigManager.PreserveInstallState(_settings, fresh); _settings = fresh; LoadSettingsToControls();`. The loading flag from T022 blocks the theme handler.
- [X] T028 [US1] In `SettingsWindow.cs` Import (~:1788), change the success text to `Settings imported. Click OK to save them, or Cancel to discard.`.
- [X] T029 [US1] Update `tests/AkmlSql.Shell.Shared.Tests/WindowChromeTests.cs`:
  - Replace `ResetPageToDefaultsCore_HasCaseForEveryRegisteredPageKey` (~:201-235) with `ResetPageToDefaultsCore_ResetsEveryRegisteredPageWithoutThrowing`, which builds the window and calls it for each key.
  - Keep `PageControls_RegisteredForEveryPageBuilder`.

### Implementation for User Story 1 — OPT-01 (wire seven settings, hide the rest)

- [X] T030 [P] [US1] In `src/AkmlSql.IntelliSense/Completion/Providers/ColumnProvider.cs`:
  - Make `FormatSecondaryText` (~:557-581) an instance method gated by new public bool properties `ShowDataTypes`, `ShowNullability` and `ShowKeyIndicators` (all default true).
    - `TypeDisplay` needs ShowDataTypes; `NULL`/`NOT NULL` needs ShowNullability; `PK`, `IDENTITY` and `COMPUTED` need ShowKeyIndicators.
  - At the call sites (~:314, ~:342, ~:407, ~:525), append the `" • {table}"` suffix only when the formatted part isn't empty, and otherwise use just the table name.
  - Defaults must produce today's text byte-for-byte.
- [X] T031 [P] [US1] In `src/AkmlSql.IntelliSense/Completion/Providers/ObjectProvider.cs` (~:629-633), add `ShowKeyIndicators` (default true) and show the table-level `🔑 FK ↔ …` text only when it is true. Keep the −500 sort adjustment either way.
- [X] T032 [US1] In `src/AkmlSql.IntelliSense/Completion/CompletionEngine.cs`:
  - add `public bool FuzzyMatchEnabled { get; set; } = true;`;
  - at the filter (~:502-511), use `.Where(x => FuzzyMatchEnabled ? x.score > 0 : x.score >= 800)` (800 = case-insensitive prefix in `FuzzyMatcher`);
  - add `ShowDataTypes`, `ShowNullability` and `ShowKeyIndicators` properties that push into `_columnProvider` and `_objectProvider` next to where `ColumnScopeMode` is applied (~:447-457).

  Depends on T030 and T031.
- [X] T033 [US1] In `src/AkmlSql.Engine/Handlers/Completion/CompletionHandler.cs`, after ~:75 (where settings are read for each request), apply:
  - `_engine.SetMaxSuggestions(Math.Clamp(settings.IntelliSense.MaxSuggestions, 5, 200))`
  - `_engine.FuzzyMatchEnabled = settings.IntelliSense.FuzzyMatch`
  - `_engine.ShowDataTypes = settings.IntelliSense.ShowDataTypes`
  - `_engine.ShowNullability = settings.IntelliSense.ShowNullability`
  - `_engine.ShowKeyIndicators = settings.IntelliSense.ShowPkFk`

  They refresh after Options OK through the existing `AnalysisSettingsChanged` → `InvalidateSettings` path. Depends on T032.
- [X] T034 [US1] Carry `FilterText` and add prefix-only filtering in the shell:
  - `src/AkmlSql.Shell.Shared/Editor/Completion/CompletionItemModel.cs`: add `FilterText`; in `MatchesFilter`/`FilterScore`, prefix-only mode uses a case-insensitive `StartsWith` against `FilterText ?? DisplayText`.
  - `src/AkmlSql.Shell.Shared/Editor/Completion/CompletionController.cs`: populate `FilterText` from the DTO (~:879-887), and latch `Popup.PrefixOnly = !IntelliSense.FuzzyMatch` in `LatchPopupSettings` (~:771-776).
  - `src/AkmlSql.Shell.Shared/Editor/Completion/AkmlCompletionPopup.cs`: honour `PrefixOnly` in `ApplyFilter` (~:415-425).
- [X] T035 [US1] Create the pure helper `src/AkmlSql.Shell.Shared/Editor/Completion/CompletionTriggerPolicy.cs` (add it to the projitems; tested by T015) with `static TriggerDecision Decide(char typed, bool ctrlSpace, IntelliSenseSettings s)` returning Immediate, Delayed(ms) or None. Then, in `src/AkmlSql.Shell.Shared/Editor/Completion/CompletionController.cs`, route every automatic trigger through it to add the trigger delay and the after-dot switch:
  - `AutoTriggerCompletion()` (~:724-728) calls the existing `TriggerCompletionDebounced()` (~:752-763) with the delay from `SettingsSnapshot().IntelliSense.TriggerDelayMs` instead of the constant `DebounceMs`. When the delay is 0, trigger immediately as today.
  - The debounce callback calls `TriggerCompletion()` (not `FetchAndShowCompletions()`), so the filter text is recomputed.
  - Cancel the timer in `DismissPopup()` (~:1346), on commit and on Esc.
  - Gate the dot trigger (~:427) on `IntelliSense.AfterDot`.
  - Ctrl+Space paths (~:370-392, ~:548-560) stay immediate.

  Depends on T034 (same file).
- [X] T036 [US1] Wire "Show in Error List":
  - In `src/AkmlSql.Shell.Shared/Analysis/ErrorListReporter.cs`:
    - add `internal static bool ShouldPublish(CodeAnalysisSettings s) => s.ShowInErrorList;`;
    - add a 2-second cached settings read;
    - add a static weak registry of live reporters with `internal static void ReapplyAll()`;
    - in `RefreshTaskList` (~:48-80), when `ShouldPublish` is false, clear the tasks, call `Refresh()` and return.
  - `ReapplyAll()` re-runs `RefreshTaskList` with each reporter's controller's `CurrentIssues`.
  - Call `ErrorListReporter.ReapplyAll()` from `OptionsCommand.SaveAndNotify` (`Commands/OptionsCommand.cs` ~:110-134).
- [X] T037 [US1] In `src/AkmlSql.Shell.Shared/Analysis/AnalysisController.cs`, add `internal static bool ShouldAnalyzeOnEdit(CodeAnalysisSettings s) => s.Enabled && s.RunOnType;` (tested by T016) and gate the edit-triggered analysis (~:48-52) on it, with a 2 s cached settings read. Opening a document and the explicit "run analysis" commands still analyse.
- [X] T038 [P] [US1] In `src/AkmlSql.Shell.Shared/Formatting/FormatActionHelper.cs`, add the pure `internal static string? FormatterDisabledMessage(FormatterSettings s)` (tested by T017), returning `AKML SQL formatting is off — turn it on in Options › Format › Styles.` when `Enabled` is false and null otherwise. Add `internal static bool EnsureFormatterEnabled()`, which writes that message to the status bar and returns false when it isn't null.
  - Call it at the start of `Execute` in `FormatDocumentCommand.cs`, `FormatSelectionCommand.cs` and `src/AkmlSql.Shell.Shared/Productivity/BulkFormatCommand.cs`.
- [X] T039 [US1] In `src/AkmlSql.Shell.Shared/Editor/Completion/CompletionController.cs`:
  - Create the pure helper `src/AkmlSql.Shell.Shared/Snippets/SnippetGate.cs` (add it to the projitems; tested by T018) with `ShouldOfferSnippets(AppSettings)` (= `Snippets.Enabled && IntelliSense.SnippetsInCompletion`), `ExpansionEnabled(AppSettings)` (= `Snippets.Enabled`) and `FormatOnExpand(AppSettings)`.
  - Replace the hard-coded `FormatOnExpand = true` (~:1129, ~:1179) with `SnippetGate.FormatOnExpand(SettingsSnapshot())`.
  - Filter snippet items (ObjectType 4) out of the list when `SnippetGate.ShouldOfferSnippets` is false (replacing the `SnippetsInCompletion` check at ~:856-871), and skip Tab expansion when `SnippetGate.ExpansionEnabled` is false.

  Depends on T035 (same file).
- [X] T040 [P] [US1] In `src/AkmlSql.Shell.Shared/Ui/BulkFormatWizard.cs` (~:138-144), set the backup checkbox's initial `Checked` from `ConfigManager.Load().Formatter.CreateBackups` instead of `true` (tested by T019).
- [X] T041 [P] [US1] Hide dead rows on the Suggestions pages:
  - `src/AkmlSql.Shell.Shared/Dialogs/Pages/IntelliSensePage.cs`: "Keyword casing" dropdown (~:51).
  - `SuggestionTypesPage.cs`: "List all database columns after a SELECT statement" (~:28).
  - `CompletionPolishPage.cs`: rows at ~:23 (MS_Description), ~:28 (parameter highlight), ~:41 (decrypt), ~:46 (temp-table IntelliSense) and ~:54 (column picker default sort). Keep "Show the object definition box" (~:33).

  For each row, remove the `RowFactory` call, its `ctx.RegisterSearch` call and the matching assignments in the page's `Controls.Load`/`Save`, so the saved value is never overwritten. Leave the `AppSettings` properties alone.
- [X] T042 [P] [US1] Hide dead rows, using the same technique as T041:
  - `ConnectionsMemoryPage.cs`: the cache group header and rows (~:42-54). Keep the SQL-auth row and "Manage…".
  - `QualificationPage.cs`: "Qualify columns with table name or alias" and its header (~:32-34).
  - `FormattingPage.cs`: Format on paste/save/delimiter (~:53, ~:57, ~:61), Confirm before bulk format (~:68), Respect --noformat regions (~:78) and Validate formatting preserves semantics (~:83). Keep Enable SQL formatter, Show active style in status bar and Create backups.
- [X] T043 [P] [US1] Hide dead rows, using the same technique as T041:
  - `EditorPage.cs`: Named regions (~:24), Document Outline toggle (~:36).
  - `NavigationPage.cs`: Go to Definition, Peek Definition, Find All References and Object Search toggles (~:16, ~:20, ~:24, ~:28). If the page is left with no rows, keep it with one info row, "Navigation commands are in AKML SQL › Navigate.", until US6 moves it.
  - `RefactoringPage.cs`: Show preview before applying (~:18), Create backups (~:23), Format after refactoring (~:28), Include string literals in rename scope (~:41), Rename scope (~:46).
- [X] T044 [P] [US1] Hide dead rows, using the same technique as T041:
  - `GridPage.cs`: Freeze headers (~:29).
  - `CodeAnalysisPage.cs`: Analyze on save (~:28).
  - `SnippetsPage.cs`: Show in IntelliSense completions (~:28), Filter by SQL context (~:38), Track usage for ranking (~:43), Personal folder (~:51). Also add "Takes effect after SSMS restarts" to the Team folder description (~:56).
  - `AiAssistancePage.cs`: Chat panel toggle (~:329).
- [X] T045 [US1] Remove the Suggestions › Database (`Schema Cache`) and `Labs` pages. Every row on both does nothing.
  - In `SettingsWindow.cs`, delete their entries from `_pageBuilders` (~:64, ~:79), the nav tree (~:537, ~:569-570) and `pages[]` (~:1140-1167).
  - Delete `src/AkmlSql.Shell.Shared/Dialogs/Pages/SchemaCachePage.cs` and `LabsPage.cs`, and remove them from the projitems.
  - Update any test that referenced them (search `tests/AkmlSql.Shell.Shared.Tests` for `SchemaCache` and `Labs`).
- [X] T046 [P] [US1] Fix stale descriptions:
  - `ConnectionScopePage.cs`: the linked-server description saying it "currently has no effect". It works now; describe what it does.
  - Remove "(Phase B)" / "Phase A and Phase B" jargon from any remaining Options description (search `Dialogs/Pages`).
- [X] T047 [US1] Build (MSBuild), then run the Shell, Engine and IntelliSense test suites, `CorpusGateTests` (the pass rate must not drop) and the format-parity goldens. Run quickstart.md scenarios 1–10 manually in SSMS 22 and record the results in `specs/040-sqlprompt-ui-parity/baseline.md` under "US1 verification".

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

- [X] T048 [P] [US2] Write `tests/AkmlSql.Core.Tests/Ipc/HistoryActionContractTests.cs`, following `HistoryRecordRequestTests`. Round-trip:
  - `HistoryActionRequest` keys 9–12;
  - `HistoryActionResponse` keys 8 and 11;
  - `HistoryEntryDto` key 17.

  Also assert a payload serialised **without** the new keys deserialises with them null or false (the legacy shape).
- [X] T049 [P] [US2] Write `tests/AkmlSql.Engine.Tests/History/HistorySchemaV3Tests.cs`. Build a v2 database file through the internal `HistoryDatabase(dbPath)` constructor, then use raw SQL to:
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
- [X] T050 [P] [US2] Write `tests/AkmlSql.Engine.Tests/History/HistoryGroupActionsTests.cs`. Record three runs in one session, then check each action with `GroupScope = true`:
  - Delete removes every row, its `history_versions` and its `query_sessions` row, and returns `DeletedCount = 3`.
  - ToggleFavorite sets `is_favorite` on all rows and returns `IsFavorite = true`. After another run, a second toggle clears every row.
  - GetVersions returns distinct texts newest first, and the count equals the grouped row's `VersionCount`.

  With `GroupScope = null`, the per-id behaviour is unchanged, except that `DeletedCount` is now set.
- [X] T051 [P] [US2] Write `tests/AkmlSql.Engine.Tests/History/HistoryOpenStateTests.cs`:
  - `SetOpenStatus` with `SessionKey` and `OwnerPid` marks every row of that session open and sets `open_pid`; `IsOpen = false` clears both.
  - `ReconcileOpen(ownerPid, openKeys)`:
    - closes rows owned by `ownerPid` whose session isn't in `openKeys`;
    - closes rows whose `open_pid` is a process that doesn't exist (use `int.MaxValue - 1`) and returns their group's representative id in `RestorableEntryIds`;
    - does **not** touch rows owned by a live process (use `Environment.ProcessId`);
    - leaves rows with `open_pid IS NULL` (web rows) unchanged.
- [X] T052 [P] [US2] Write `tests/AkmlSql.Engine.Tests/History/HistorySnapshotSearchTests.cs`. After `SaveVersionBySourceAsync`:
  - a search for the new text finds the entry, and a search for the replaced text doesn't;
  - `executed_at` is ISO "o";
  - `content_hash` matches the new text;
  - ordering is by time across rows written before and after.
- [X] T053 [P] [US2] Write `tests/AkmlSql.Engine.Tests/History/HistoryGroupFiltersTests.cs`:
  - With grouping on, `IsOpen = false` never returns a group with any open run.
  - `FavoritesOnly` returns a group whose older run is starred.
  - `TotalCount` equals the number of groups the filters return.
- [X] T054 [US2] Update `tests/AkmlSql.Engine.Tests/History/HistoryVersionSnapshotBySourceTests.cs` (~:84-100) to expect the ISO "o" `executed_at` instead of the space format. This is a deliberate behaviour change (research R15).
- [X] T055 [P] [US2] Write `tests/AkmlSql.Shell.Shared.Tests/HistoryPagingTests.cs`:
  - With a fake search client serving 250 matches at page size 100, `HasMoreEntries` stays true until all 250 are loaded, then false.
  - When a page returns fewer rows than the page size, `HasMoreEntries` is false.
- [X] T056 [P] [US2] Extend `tests/AkmlSql.Shell.Shared.Tests/QuerySessionKeyTests.cs`:
  - `DocumentSessionKeys.TryGet` returns false for an unknown document and doesn't create a key.
  - `Adopt(fullName, key)` makes `TryGet` return that key.
  - `Adopt` of a key that another open document holds returns false, and `TryGet` for the new document still returns false.
  - `TryFindDocument(key)` returns the document that holds the key, and null once that document is forgotten.
- [X] T057 [P] [US2] Write `tests/AkmlSql.Shell.Shared.Tests/HistoryPreviewAndActionsTests.cs`, using a fake client:
  - selecting an entry requests `GetFullSql` once per id (cached), doesn't set `IsLoading`, and the cache is cleared by Refresh;
  - Delete and ToggleFavorite invoked for a row send that row's id with `GroupScope = true` when grouping is on, even when `SelectedEntries` holds other rows;
  - Delete asks for confirmation through an injectable prompt with the text `Remove '‹name›' and its history?`.
- [X] T058 [P] [US2] Write `tests/AkmlSql.Shell.Shared.Tests/ReconcileOpenRequestTests.cs` for the pure `OpenStateReporter.BuildReconcileRequest(int pid, IEnumerable<(string FullName, string? Key)> documents)` (created in T069):
  - it keeps only documents named `*.sql` or `SQLQuery*` that have a key;
  - it sets `Action = ReconcileOpen` and `OwnerPid = pid`;
  - it returns an empty `OpenSessionKeys` array (not null) when no document qualifies.
- [X] T059 [P] [US2] Write `tests/AkmlSql.Engine.Tests/History/HistoryRequestHandlerTests.cs`, sending `HistoryActionRequest`s through `HistoryRequestHandler` (T067) against a temporary database:
  - with `GroupScope = true`, Delete, ToggleFavorite and GetVersions act on the whole query session and return `DeletedCount`, `IsFavorite` and `Versions`;
  - without `GroupScope`, the same actions keep today's per-id behaviour, and per-id Delete now also sets `DeletedCount`;
  - `SetOpenStatus` with `SessionKey` and `OwnerPid` opens and closes every row of that session, and the old `EntryIds` path still works;
  - `ReconcileOpen` returns `RestorableEntryIds` for the groups whose owner process is gone, newest first.
- [X] T060 [P] [US2] Write `tests/AkmlSql.Shell.Shared.Tests/OpenStateReporterTests.cs` for the open-state decisions in `OpenStateReporter` (T069):
  - `OnRecorded(key, pid)` returns a `SetOpenStatus` request with `IsOpen = true`, that `SessionKey` and that `OwnerPid`;
  - `OnActivated(key, pid)` returns the same open request, or null when the key is null;
  - `OnClosing(key, pid, shuttingDown)` returns a close request, or null when the key is null or `shuttingDown` is true.
- [X] T061 [P] [US2] Write `tests/AkmlSql.Shell.Shared.Tests/HistoryVersionLoadGuardTests.cs` for `VersionLoadGuard` (T072): when two loads overlap and the older one finishes last, its result is discarded and the newer one's is kept; a single load is always current.

### Implementation for User Story 2

- [X] T062 [P] [US2] Add the DTO keys (contracts/ipc.md):
  - `src/AkmlSql.Core/Ipc/Messages/HistoryActionRequest.cs`: `[Key(9)] bool? GroupScope`, `[Key(10)] string? SessionKey`, `[Key(11)] int? OwnerPid`, `[Key(12)] string[]? OpenSessionKeys`, and the action constant `ReconcileOpen = 11`.
  - The response class (same folder; search for `class HistoryActionResponse`): `[Key(8)] bool? IsFavorite` and `[Key(11)] long[]? RestorableEntryIds`. Keys 9 and 10 are reserved for US5; add them now as nullable `string[]? Servers`/`Databases` so the numbering stays contiguous.
  - `HistoryEntryDto.cs`: `[Key(17)] string? SessionKey`.
- [X] T063 [US2] Add schema v3 to `src/AkmlSql.Engine/History/HistoryDatabase.cs` `InitializeCoreAsync` (~:90-230), per data-model.md §2.1:
  - `SchemaVersion = 3`;
  - `ALTER TABLE history ADD COLUMN open_pid INTEGER NULL`, in the same try/catch "duplicate column" pattern as ~:147-159;
  - `CREATE TRIGGER IF NOT EXISTS history_au AFTER UPDATE OF sql_text ON history …` (delete the old text, insert the new);
  - a one-time repair guarded by the `metadata` key `history_v3`, under `BEGIN IMMEDIATE`, that:
    1. normalises space-format `executed_at` and `history_versions.saved_at` to ISO "o";
    2. deletes orphan `history_versions`;
    3. runs `INSERT INTO history_fts(history_fts) VALUES('rebuild')`;
    4. writes the flag in the same transaction.

  The file is shared with the web engine and other SSMS instances, so everything must be idempotent.
- [X] T064 [US2] In `SaveVersionBySourceAsync` (`HistoryDatabase.cs` ~:1735-1765):
  - write `executed_at = DateTime.UtcNow.ToString("o")` (not `datetime('now')`);
  - update `content_hash` with the same hash function the insert path uses;
  - look the row up by session key when a `SessionKey` is supplied (new optional parameter), falling back to source.
- [X] T065 [US2] Add group-scoped operations to `HistoryDatabase.cs`, resolving the group from an entry id with the GroupKey expression (~:910):
  - `DeleteGroupAsync(long entryId)`: one transaction deleting `history_versions` for the group's ids, then the `history` rows, then the `query_sessions` row. Returns the number of rows deleted.
  - `ToggleFavoriteGroupAsync(long entryId)`: sets all rows to `1 - MAX(is_favorite)` and returns the new state.
  - `GetVersionsForGroupAsync(long entryId)`: runs and snapshots of the group, de-duplicated by content hash, newest first, id tiebreak.
  - Make per-id `DeleteAsync` return its count.
- [X] T066 [US2] Add open state and group-level filters to `HistoryDatabase.cs`:
  - `SetOpenStatusBySessionAsync(string sessionKey, bool isOpen, int ownerPid)`, updating every row of the session.
  - `ReconcileOpenAsync(int ownerPid, string[] openKeys)`, returning the ids from dead owners. Check liveness with `Process.GetProcessById` inside try/catch (`ArgumentException` = dead).
  - Move the `FavoritesOnly`/`IsOpen` filters in `SearchAsync` (~:889-896) to the grouped outer level: `WHERE` on `MAX(is_favorite)` / `MAX(is_open)`. Compute `TotalCount` with `SELECT COUNT(*) FROM (<grouped query without LIMIT>)`. The flat mode keeps per-row filters.
  - Delete the dead `CloseByTabTitleAsync` (~:1276-1287).
- [X] T067 [US2] In `src/AkmlSql.Engine/History/HistoryRequestHandler.cs` (~:178-420):
  - route `GroupScope == true` for Delete, ToggleFavorite and GetVersions to the group methods;
  - set `DeletedCount` for both Delete paths;
  - return `IsFavorite`;
  - route `SetOpenStatus` with `SessionKey` and `OwnerPid` to `SetOpenStatusBySessionAsync`, keeping the old `EntryIds` path;
  - add `case ReconcileOpen` returning `RestorableEntryIds`.
  - Tested by T059.
- [X] T068 [US2] Add `public static bool TryGet(string fullName, out string key)` (never creates a key), `public static bool Adopt(string fullName, string key)` and `public static string? TryFindDocument(string sessionKey)` (the full name of the open document holding that key, or null; History's Open query uses it) to `src/AkmlSql.Shell.Shared/History/DocumentSessionKeys.cs`. `Adopt` returns false, and adopts nothing, when another open document already holds that key; the new document then gets its own key when it first runs.
- [X] T069 [US2] Create the pure helper `src/AkmlSql.Shell.Shared/History/OpenStateReporter.cs` (add it to the projitems; tested by T058 and T060). It holds every open-state decision; callers only send what it returns:
  - `BuildReconcileRequest(int pid, IEnumerable<(string FullName, string? Key)> documents)` (used by T070);
  - `OnRecorded(string key, int pid)` → a `SetOpenStatus` request with `IsOpen = true`, `SessionKey` and `OwnerPid`;
  - `OnActivated(string? key, int pid)` → the same open request, or null when the document has no key;
  - `OnClosing(string? key, int pid, bool shuttingDown)` → the matching close request, or null when there is no key or SSMS is shutting down.

  Then, in `src/AkmlSql.Shell.Shared/History/ExecutionCapture.cs`:
  1. **Record (~:720-740):** send the history record with `SendRequestAsync<HistoryRecordResponse, HistoryRecordRequest>` instead of a notification. Once it returns, send `OpenStateReporter.OnRecorded(key, Process.GetCurrentProcess().Id)`.
  2. **`OnWindowActivated` (~:434-516):** for the document that **gained** focus, send `OnActivated(key, pid)`, with the key from `DocumentSessionKeys.TryGet`, when it isn't null.
  3. **`OnDocumentClosing` (~:302-335):** call `TryGet` **before** `Forget`, then send `OnClosing(key, pid, ShuttingDown)` when it isn't null.
  4. Add `public static volatile bool ShuttingDown`.

  All sends are fire-and-forget on a background task, with errors logged and never thrown into the UI thread.
- [X] T070 [US2] In `src/AkmlSql.Ssms22/AkmlSqlPackage.cs`, using `OpenStateReporter` (T069):
  - **Startup (~:193-199):** once the engine launch task completes, send the request from `OpenStateReporter.BuildReconcileRequest(currentPid, documents)` on a background task, where `documents` are every open DTE document's full name with its `DocumentSessionKeys.TryGet` key. Keep the returned `RestorableEntryIds` in a static for US5 (`History/HistoryRestoreState.cs`, new small static holder, added to the projitems).
  - **Shutdown:** in `Dispose(bool)` (~:602-610), and in a `QueryClose` override if one exists, set `ExecutionCapture.ShuttingDown = true` **first**.
- [X] T071 [US2] In `src/AkmlSql.Shell.Shared/History/HistoryViewModel.cs`:
  - **Paging:** set `HasMoreEntries => _lastPageCount == PageSize && Entries.Count < TotalCount` (~:182), recording `_lastPageCount` after each page (~:386, ~:461).
  - **Preview:** add `Task<string?> GetPreviewTextAsync(HistoryEntryDto e)`, using `GetFullSql` with a per-id cache cleared on Refresh. It must **not** touch `IsLoading`.
  - **Row actions:** make Delete and ToggleFavorite take the row entry explicitly (not `SelectedEntries`) and send `GroupScope = true` when deduplication/grouping is on.
  - **Delete:** ask `Remove '‹name›' and its history?` through an injectable `Func<string,bool> ConfirmPrompt`, defaulting to a MessageBox.
  - **ToggleFavorite:** apply the returned `IsFavorite` to the row.
  - **Open query (new tab):** call `DocumentSessionKeys.Adopt(newDoc.FullName, entry.SessionKey)` when a session key is present. `Adopt` refuses a key another open tab holds (T068), so an older version opened beside the open query starts its own session.
- [X] T072 [US2] In `src/AkmlSql.Shell.Shared/History/HistoryToolWindowControl.cs`:
  - **Preview:** replace the preview `TextBlock` and `RenderPreview` (~:1292-1302, ~:1473-1571) with a `SqlPreviewView` (T005). Its `Text` comes from `GetPreviewTextAsync`, and `HighlightTerms` from `HistorySearchTerms.Extract`.
  - **Versions:** load them (~:1684-1781) with `GroupScope = true`, guarded against stale responses by a new `src/AkmlSql.Shell.Shared/History/VersionLoadGuard.cs` (add it to the projitems; tested by T061): `Begin()` returns a token for each load, and `IsCurrent(token)` is false once a later load has begun, so a slower, older response is dropped.
  - **Open marker:** replace the red/green `connDot` (~:948-958) with a 3 px `AccentPrimary` left bar shown when `IsOpen`, and remove the `OpenClosedColorConverter` usage. The footer dot (~:1264) shows green only when the entry is open.
  - **Star and ⋯:** pass their row's entry to the view-model commands.
- [X] T073 [US2] In `src/AkmlSql.Shell.Shared/Dialogs/Pages/HistoryPage.cs`:
  - hide "Record failed executions" (~:24) and "Encrypt at rest" (~:47), with the T041 technique;
  - rename "Enable deduplication" to **Group repeated runs of the same query**, with the description "Show one row per query tab, with its runs and versions inside.";
  - add "Takes effect after SSMS restarts" to the descriptions of Enable SQL history recording, Retention, Max entries and Disable automatic history trimming.
  - Update the allow-list in `tests/AkmlSql.Shell.Shared.Tests/OptionsLiveSettingsTests.cs` (T010) in the same change, so the US1 test gate stays green.
- [X] T074 [US2] Build, then run the Core, Engine and Shell suites, plus `tests/AkmlSql.Web.Tests` History tests: `WebHistoryLogicTests` must be unchanged. Run quickstart.md scenarios 11–17 and record the results in `baseline.md` under "US2 verification".

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

- [X] T075 [P] [US3] Write `tests/AkmlSql.Shell.Shared.Tests/FormatStylesRowLayoutTests.cs` (`[StaFact]`), building the window headlessly as `FormatStylesWindowFixTests` does.
  - Load the SQL Prompt schema and set the window size to 1060×680.
  - For each of the 14 pages, run `Measure`/`Arrange`, then assert for every label `TextBlock`: it is not inside a horizontal `StackPanel`; `TextTrimming == None`; and either its unconstrained width (`Measure(new Size(double.PositiveInfinity, double.PositiveInfinity))`) is at most its arranged width, or it wraps (its `ActualHeight` is greater than one line height). This catches mid-word clipping that a plain desired-width check misses.
  - For every Bool option, assert the `CheckBox.Content` is the label, so clicking the text toggles it.
- [X] T076 [P] [US3] Write `tests/AkmlSql.Shell.Shared.Tests/FormatStylesPreviewTabTests.cs`: `FormatStylesEditorViewModel.PreviewTabSize` returns `sqlPrompt.whitespace.numberOfSpacesInTabs` for SQL Prompt-model styles and `whitespace.tabSize` for AKML-model styles, and follows option edits.
- [X] T077 [P] [US3] Write `tests/AkmlSql.Shell.Shared.Tests/FormatStylesImportExportTests.cs`, extending `FormatStylesLifecycleTests` with a fake IPC client:
  - After a successful import plus activation, `Profiles` shows `IsActive` on the imported style, and the window's header state and set-active button are in sync.
  - Import with `IsDirty` calls `DirtyDecisionHandler` **before** the import request is sent.
  - Export with `IsDirty` for the loaded style calls the save prompt: Cancel aborts, Yes saves and then exports, No exports the saved file.
  - Importing a style whose name matches a built-in or an existing style asks for a new name (through an injectable name prompt) and imports under that name; the built-in is untouched.

### Implementation for User Story 3

- [X] T078 [US3] Rework `BuildSettingRow` in `src/AkmlSql.Shell.Shared/Formatting/FormatStylesEditorWindow.cs` (~:1995-2049) and the Bool branch of `BuildControlForSetting` (~:2081-2095):
  - **Bool options:** a `CheckBox` whose `Content` is a `TextBlock` (`TextWrapping.Wrap`, label text, the existing foreground token), spanning both columns (`Grid.SetColumnSpan(checkbox, 2)`), with no separate label cell. Pass that `TextBlock` as `GatedRow.Label` so `RefreshIfGate` keeps working.
  - **Other kinds:**
    - The label column is `new ColumnDefinition { Width = GridLength.Auto, SharedSizeGroup = "lbl", MinWidth = 200 }`.
    - The label is a wrapping `TextBlock` with no horizontal `StackPanel`; `MaxWidth` is bound to 45 % of the host width.
    - The control column keeps its 280 px cap.
  - **Host:** set `Grid.IsSharedSizeScope="True"` on `_settingControlsHost` (~:1577).
  - **Tooltips:** they include the full label text.
- [X] T079 [US3] Add `internal int PreviewTabSize` to `src/AkmlSql.Shell.Shared/Formatting/FormatStylesEditorViewModel.cs`:
  - SQL Prompt model: `GetWorkingValue("sqlPrompt.whitespace.numberOfSpacesInTabs")`.
  - Otherwise: `GetWorkingValue("whitespace.tabSize")`.
  - Parse as int, default 4, clamp 1–16. Raise `PropertyChanged` when either option changes.
- [X] T080 [US3] In `FormatStylesEditorWindow.cs`, show the formatted preview in a `SqlPreviewView` (T005) with `TabSize = _viewModel.PreviewTabSize`, updated on `PreviewText`/`PreviewTabSize` changes (~:2284-2301).
  - Keep `_previewTextBox` only for "Edit sample" mode, and toggle visibility between the two (~:1692-1778).
  - Keep the dark card for now; US4 moves it to theme tokens.
  - *Done differently:* the card's colours moved to theme tokens here (T100's colour part). `SqlPreviewView` colours SQL from theme tokens, which are unreadable on the fixed dark card in the light theme. T100 keeps the line-number switch.
- [X] T081 [US3] Import (`FormatStylesEditorWindow.cs` ~:1175-1248):
  - **Before** showing the file dialog, when `_viewModel.IsDirty`, ask via `PromptSaveDecision`: Yes saves, No discards, Cancel aborts.
  - After a successful import and `SetActiveProfile`, run the same sequence as set-active (~:1119-1145): `await _viewModel.RefreshProfilesAsync(); RestoreListSelection(name);`, sync `_setActiveButton`, `UpdateHeaderState();`, then update the status bar.
  - When the imported style's name matches a built-in or an existing style, show the existing `StyleNameDialog` pre-filled with `‹name› (imported)` and import under the chosen name. A built-in style is never overwritten.
- [X] T082 [US3] Export (`FormatStylesEditorWindow.cs` ~:1147-1168): when `_viewModel.IsDirty && name == _viewModel.LoadedProfileName`, show `Save changes to '{name}' before exporting?` (Yes / No / Cancel).
  - Yes → `await SaveSelectedStyleAsync()`, then export.
  - No → export the saved file.
  - Cancel → stop.
- [X] T083 [US3] Build, then run the Shell suite. Run quickstart.md scenarios 18–21, and capture the style editor at its default size (Northwind) with `tests/AkmlSql.UiTests` or manually. Record the results in `baseline.md` under "US3 verification".

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

- [X] T084 [P] [US4] Write `tests/AkmlSql.Core.Tests/Ipc/FormatSelectionResponseTests.cs`: key 7 `ProfileFallbackWarning` round-trips, and the legacy shape deserialises with null.
- [X] T085 [P] [US4] Extend `tests/AkmlSql.Engine.Tests/Formatter/ProfileFallbackWarningTests.cs`: `FormatSelection` with a missing profile name returns a non-null `ProfileFallbackWarning` with the same text as `FormatDocument`.
- [X] T086 [P] [US4] Write `tests/AkmlSql.Shell.Shared.Tests/FormatStylesSearchTests.cs`:
  - The view model's `Search("comma")` returns only groups whose options mention "comma" in DisplayName, Description, Note, Subgroup, EnumLabels or the option id, with per-group counts, and the first match's id.
  - An empty query returns every group with no counts.
- [X] T087 [P] [US4] Write `tests/AkmlSql.Shell.Shared.Tests/FormatStylesChangeMarkersTests.cs`:
  - `IsChanged(id)` is true when the working value ≠ the catalog default.
  - `ChangedCount(groupId)` counts per page.
  - `ResetOption(id)` restores the default and lowers the count.
  - `MovedLines` is computed positionally between the previous and current preview, **only** after an option edit (not after a style or page switch).
  - `IsDirty` returns to false when the user sets every edited option back to its saved value.
- [X] T088 [P] [US4] Write `tests/AkmlSql.Shell.Shared.Tests/StyleNameDialogValidationTests.cs`:
  - empty name, a name longer than 80 characters, illegal file-name characters, "..", and an existing name (case-insensitive, trimmed) each show a message and disable OK;
  - Rename allows the current name, including a change of case only;
  - a valid name enables OK.
- [X] T089 [P] [US4] Write `tests/AkmlSql.Shell.Shared.Tests/ActiveStyleMenuTests.cs`, with a fake `ActiveStyleCache`:
  - slot N shows style N's name, checked when active, visible only when style N exists;
  - more than 30 styles → only 30 are shown, and the last visible slot is style 30;
  - invoking a slot sets `Formatter.ActiveProfile` (AppData isolated) and raises the cache's refresh.
- [X] T090 [P] [US4] Write `tests/AkmlSql.Shell.Shared.Tests/StatusBarTransientTests.cs`, with an `IVsStatusbar` fake (or an internal text sink seam added to `StatusBarManager`):
  - `ShowTransient` shows the text, then restores the idle text after the timeout;
  - while the transaction indicator is active, the idle text isn't restored over it;
  - `SetActiveProfile` does nothing when `ShowProfileInStatusBar` is false.
- [X] T091 [P] [US4] Write `tests/AkmlSql.Shell.Shared.Tests/FormatStylesKeyboardTests.cs` (`[StaFact]`, window built headlessly with a fake engine client), calling `HandleKey(key, modifiers, listFocused)` (T102):
  - Ctrl+S saves only when Save is enabled; Ctrl+F focuses the search box;
  - F2 on the list opens rename through the injectable name-dialog hook;
  - Delete on a built-in, team or active style shows a status message and doesn't delete;
  - Enter on the list makes the selected style active;
  - the style list's ⋮ button has `AutomationProperties.Name` "Style actions" (T185).
- [X] T092 [P] [US4] Write `tests/AkmlSql.Shell.Shared.Tests/FormatFeedbackTests.cs` for `FormatFeedback.Report(profileName, success, fallbackWarning)` (T109):
  - success with no warning → the status-bar test hook receives `Formatted with 'X'`;
  - with a warning → the warn-once notifier hook (`FormatFailureNotifier.ProfileFallbackNotifierOverride`) gets the text, and no "Formatted with" message appears;
  - failure → neither message.
- [X] T093 [P] [US4] Write `tests/AkmlSql.Shell.Shared.Tests/ActiveStyleCacheTests.cs` for `ActiveStyleCache` (T104), with a fake ProfileList client, a fake clock and AppData isolated:
  - `RefreshAsync` marks as active the style named by `Formatter.ActiveProfile` in config;
  - several `RequestRefresh` calls within 5 s cause one refresh, and a call after 5 s causes another;
  - `Changed` fires once per completed refresh;
  - when the ProfileList call fails, `Styles` keeps the last snapshot and `Changed` doesn't fire.
- [X] T094 [P] [US4] Extend `tests/AkmlSql.Shell.Shared.Tests/FormatStylesRowLayoutTests.cs` (T075) for the row additions in T103:
  - every Integer option has ▲ and ▼ buttons that step the value by 1 and stop at the range limits;
  - every option with a Note or "Takes effect when …" text shows it in a `TextBlock` under its row;
  - options with an `EnabledWhen` gate are indented by `Spacing.Lg` more than their parent.

### Implementation for User Story 4

- [X] T095 [P] [US4] Add `[Key(7)] public string? ProfileFallbackWarning { get; set; }` to `src/AkmlSql.Core/Ipc/Messages/FormatSelectionResponse.cs`. In `HandleFormatSelection` (`src/AkmlSql.Engine/Formatter/FormatRequestHandler.cs` ~:68), use the `LoadProfile(name, out var warning)` overload (~:895-914) and set the field.
- [X] T096 [US4] In `FormatStylesEditorViewModel.cs` and `FormatStylesEditorWindow.cs`:
  - keep the parsed `FormatStylesSchemaModel` model (today a local at ~:1812) as `_viewModel.SchemaModel`, and keep a `Dictionary<string, TreeViewItem>` groupId → leaf;
  - add `internal SearchResult Search(string query)`, matching DisplayName, Description, Note, Subgroup, EnumLabels and the option id, case-insensitive. It returns the matching group ids with counts, the matching option ids, and the first match.
- [X] T097 [US4] In `FormatStylesEditorWindow.cs`, add the option search box:
  - A `TextBox` above the page tree (~:1473-1511) with placeholder `Search for options…`, and a 150 ms debounce.
  - The tree hides leaves (and emptied categories) with no matches, and each shown leaf gets a count badge (`Typography.Small`, `AccentPrimary`).
  - `UpdateRightForGroup` highlights matching rows with the `SurfaceSelection` background.
  - Enter selects the first match's page and scrolls to its row.
  - Esc, handled in the TextBox's `PreviewKeyDown` with `e.Handled = true`, clears the query when there is one. When the box is already empty, Esc falls through to the window's `IsCancel` Close.
  - *Done differently:* the leaf badges ("(N)" matches, "● N" changed) inherit the leaf's foreground instead of `AccentPrimary`, so they stay readable on the selected (accent) leaf.
- [X] T098 [US4] In `FormatStylesEditorViewModel.cs`:
  - add `IsChanged(string id)` (working value ≠ `_schemaDefaults[id]`), `ChangedCount(string groupId)` and `ResetOption(string id)` (sets the default, queues the preview);
  - replace the sticky `IsDirty` with a recomputation against the saved values: rebuild them the way `RevertChanges` does (~:737-738) into `_savedValues` whenever a style loads or saves;
  - capture a `markChanges` flag alongside the preview sequence in `QueuePreviewAsync` (~:472-548): true only from `SetWorkingValue`/`ResetOption`;
  - store `PreviousPreviewLines` and expose `MovedLines` (a positional compare, as in `src/AkmlSql.Web/Pages/Styles.razor` ~:478-509).
- [X] T099 [US4] In `FormatStylesEditorWindow.cs`:
  - a changed option shows a bold label and a small `↺` button, tooltip `Back to SQL Prompt's default (‹value›)`, that calls `ResetOption`;
  - tree leaves show the changed count (distinct from search counts: prefix `●` or use a different token);
  - after an option edit, set `SqlPreviewView.HighlightLines = MovedLines` for 2 s with a `DispatcherTimer`, then clear it.
- [X] T100 [US4] Move the preview in `FormatStylesEditorWindow.cs` to theme colours (the colours were done early, in T080):
  - remove the fixed `PreviewBgBrush`/`PreviewTextBrush`/`PreviewMutedBrush` card colours (~:117-128, ~:1609-1611) in favour of `ThemeTokens.EditorPanelBackground` (or `SurfaceInput`), `TextPrimary` and `TextSecondary` through `SetResourceReference`;
  - turn on `ShowLineNumbers`;
  - keep the amber warning bar semantic colours.
- [X] T101 [US4] Friendlier names:
  - `src/AkmlSql.Shell.Shared/Formatting/StyleNameDialog.cs`: accept `IReadOnlyCollection<string> existingNames` and `string? currentName`. `Revalidate` (~:147-159) checks empty, longer than 80 characters, illegal characters, "..", and duplicates (OrdinalIgnoreCase, trimmed; the current name is exempt). OK is disabled until valid.
  - `ShowNewStyle`/`ShowRename` pass the names from `_viewModel.Profiles`.
  - Add `ShowCopyStyle(owner, existingNames, suggested)`.
  - `FormatStylesEditorWindow.OnCopyStyleAsync` (~:1111-1117) prompts with `UniqueName($"{name} copy")` pre-filled, then calls a new `FormatStylesEditorViewModel.CopyProfileAsync(source, newName)`.
  - The Import rename prompt (T081) also passes the existing style names, so a clashing name is caught before the import.
  - *Done:* the duplicate check was already in place for Import (T081); Copy now asks for a name (`ShowCopyStyle`) and calls `CopyProfileAsync(source, newName)`.
- [X] T102 [US4] Keyboard in `FormatStylesEditorWindow.cs`, using the `SettingsWindow.OnWindowKeyDown` pattern (~:1638-1658). Put all key handling in `internal bool HandleKey(Key key, ModifierKeys mods, bool listFocused)`, called from the window's `PreviewKeyDown`, with an injectable rename-dialog hook (tested by T091):
  - **Window:** Ctrl+S → Save (when enabled); Ctrl+F → focus the search box.
  - **Style list:** F2 → Rename; Delete → Delete (refuse built-in, team and active styles with `SetStatus` explaining why); Enter → Set active.
  - *Done differently:* there is no "team" style yet (no shared-folder styles exist in the list model), so Delete refuses built-in and active styles. The ⋮ glyph's accessible name (T185) was set here because T091 tests it.
- [X] T103 [US4] In `FormatStylesEditorWindow.cs` (tested by T094):
  - **Integers:** in `BuildControlForSetting` (~:2097-2156), add small ▲/▼ buttons that step by 1 within the range and reuse the existing validation.
  - **Notes:** show each option's Note and "Takes effect when …" as grey `Typography.Small` text **under** the row, not only in the tooltip.
  - **Child options:** indent options with an `EnabledWhen` gate by `Spacing.Lg`.
- [X] T104 [US4] Create `src/AkmlSql.Shell.Shared/Formatting/ActiveStyleCache.cs` (add it to the projitems):
  - a thread-safe snapshot `IReadOnlyList<(string Name, string Source, bool IsActive)> Styles`;
  - `Task RefreshAsync()` via the existing ProfileList IPC, marking active from `ConfigManager.Load().Formatter.ActiveProfile`;
  - `void RequestRefresh()`, throttled to once per 5 s, fire-and-forget;
  - an event `Changed`.
  - Refresh points:
    - package load (`AkmlSqlPackage`, after engine launch);
    - after `FormatStylesEditorWindow.Launch` returns (`Commands/FormatStylesCommand.cs`, `Dialogs/Pages/FormattingPage.cs` ~:98);
    - after `OptionsCommand.SaveAndNotify`.
  - Take the client accessor (`IRpcClientAccessor`) and a clock (`Func<DateTime>`) through an internal constructor, so it can be tested (T093). Production uses the engine client and `DateTime.UtcNow`.
- [X] T105 [US4] Create `src/AkmlSql.Shell.Shared/Commands/RegisteredCommands.cs` (add it to the projitems) with `internal static readonly IReadOnlyCollection<int> Ids`, listing every command id the SSMS package registers.
  - Start from `TryInitCommand` in `src/AkmlSql.Ssms22/AkmlSqlPackage.cs` (~:62-143), and add the Active Style slots and Edit Styles (T106).
  - In `AkmlSqlPackage`, after registration, add a debug-only check that every registered id is in `RegisteredCommands.Ids`, logging any that are missing.
  - Keep the list in sync whenever a command is added.
- [X] T106 [US4] Add the command ids and VSCT buttons:
  - `src/AkmlSql.Shell.Shared/PackageGuids.cs`: `CmdActiveStyleSlot0 = 0x0920` through `CmdActiveStyleSlot29 = 0x093D` (a base constant plus a count is enough) and `CmdEditStyles = 0x093E`, inside the reserved free range, checking there are no clashes.
  - `src/AkmlSql.Ssms22/AkmlSqlSsms22.vsct`: matching IDSymbols and 31 `<Button>`s with `DynamicVisibility` and `TextChanges`, placeholder text "Style", in `AkmlSqlMenuGroup` for now. US7 regroups them.
- [X] T107 [US4] Create `src/AkmlSql.Shell.Shared/Formatting/ActiveStyleMenuCommands.cs` (add it to the projitems):
  - **Initialize:** register 30 `OleMenuCommand`s whose `BeforeQueryStatus` sets `Text`, `Checked` and `Visible` from `ActiveStyleCache` (slot index → style), and calls `ActiveStyleCache.RequestRefresh()`.
  - **Invoke:**
    1. `ConfigManager.Load()`, set `Formatter.ActiveProfile`, `ConfigManager.Save`;
    2. `StatusBarManager.SetActiveProfile` (gated, T108);
    3. `ActiveStyleCache.RequestRefresh()`.
  - **Edit Styles…:** run the same path as `FormatStylesCommand`.
  - Register it in `AkmlSqlPackage.TryInitCommand` (`src/AkmlSql.Ssms22/AkmlSqlPackage.cs` ~:62-143).
- [X] T108 [US4] In `src/AkmlSql.Shell.Shared/StatusBar/StatusBarManager.cs`:
  - add `ShowTransient(string text, int seconds)`: a `DispatcherTimer` restores `_idleText` unless `_transactionIndicatorActive`;
  - make `SetActiveProfile` a no-op when `ConfigManager.Load().Formatter.ShowProfileInStatusBar` is false (2 s cached read);
  - gate the editor's call in `FormatStylesEditorWindow.UpdateStatusBarActiveStyle` (~:1371-1383) the same way;
  - in `OptionsCommand.SaveAndNotify`, set or clear the active-style idle text from the saved settings;
  - add an internal text-sink seam for T090.
- [X] T109 [US4] Show which style formatted the code, through a new `src/AkmlSql.Shell.Shared/Formatting/FormatFeedback.cs` (add it to the projitems; tested by T092) with `internal static void Report(string profileName, bool success, string? fallbackWarning)`, which both commands call:
  - `src/AkmlSql.Shell.Shared/Formatting/FormatDocumentCommand.cs` (~:84-121): on success with no `ProfileFallbackWarning`, call `StatusBarManager.ShowTransient($"Formatted with '{profileName}'", 4)`.
  - `FormatSelectionCommand.cs` (~:56-92): the same, and call `FormatFailureNotifier.NotifyProfileFallbackOnce(response.ProfileFallbackWarning)` when it is present.
- [X] T110 [US4] Add an interim menu placement in `src/AkmlSql.Ssms22/AkmlSqlPackage.cs` `EnsureTopLevelMenu` (~:383-490), until US7 replaces the builder:
  - Add an "Active Style" `msoControlPopup` whose controls are the 30 slots plus "Edit Styles…" (`dte.Commands.Item(guid, id).AddControl`).
  - Find SSMS's query-editor context command bar by enumerating `dte.CommandBars` names that contain "SQL" and "Context", or equal "Code Window". Log every candidate name at Debug level and pick the first match. Add the same popup and a "Format Document" control to it.
  - When nothing matches, log `Active Style: no editor context menu found` at Information level and continue (quickstart scenario 26 may be waived).
- [X] T111 [US4] Build, then run the Core, Engine and Shell suites. Run quickstart.md scenarios 22–28 and record the results in `baseline.md` under "US4 verification", including which context bar name was found in T110.

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

**Depends on**: US2 (open state, group actions, `RestorableEntryIds`) and T005 (SqlPreviewView).

### Tests for User Story 5 (write first)

- [X] T112 [P] [US5] Write `tests/AkmlSql.Core.Tests/History/HistoryDateGroupsTests.cs`: `HistoryDateGroups.For(now, t)` gives Today, Yesterday, This week (from `CultureInfo.CurrentCulture.DateTimeFormat.FirstDayOfWeek`), Last week, This month and Older. Test the boundaries at midnight, week start and month start, in UTC and local time. A time that fits several groups goes in the first one that matches, in the order listed (yesterday is Yesterday, not This week).
- [X] T113 [P] [US5] Write `tests/AkmlSql.Core.Tests/Text/LineDiffTests.cs`: identical texts; a pure insert; a pure delete; a changed line (reported as changed, not delete plus add, when lines align); empty left or right; CRLF and LF inputs normalised.
- [X] T114 [P] [US5] Write `tests/AkmlSql.Core.Tests/Ipc/HistoryUs5ContractTests.cs`: `HistoryRecordRequest` key 12, `HistorySearchRequest` key 13 and `HistoryActionResponse` keys 9–10 round-trip, the legacy shapes still deserialise, and `ExecutionStatus.NotExecuted == 3`.
- [X] T115 [P] [US5] Write `tests/AkmlSql.Engine.Tests/History/HistorySearchScopeTests.cs`:
  - free text matches a session name, source path, server or database through `LIKE`, as well as SQL through the full-text index;
  - `PathFilter` limits results to the source;
  - `DateFrom`/`DateTo` filter on ISO timestamps;
  - an invalid full-text query still falls back to `LIKE`;
  - `TotalCount` is correct in grouped mode.
- [X] T116 [P] [US5] Write `tests/AkmlSql.Engine.Tests/History/HistoryFilterValuesAndDraftTests.cs`:
  - `GetFilterValues` returns distinct, sorted, non-empty servers and databases, capped at 500.
  - A record with `IsDraft = true` is stored with `status = 3`, listed, and excluded from `exec_count`.
  - A later real run in the same session keeps the draft text as a version.
- [X] T117 [P] [US5] Write `tests/AkmlSql.Shell.Shared.Tests/HistorySearchParserTests.cs`: `path:foo`, `date:[20260901 TO 20260927]` and every row of `HistorySearchParser.HelpRows` parse to the expected filters; unknown prefixes stay free text.
- [X] T118 [P] [US5] Write `tests/AkmlSql.Shell.Shared.Tests/HistoryInteractionTests.cs`, using a fake client and an injectable scheduler/clock:
  - typing triggers one search 250 ms after the last change;
  - after a search the first row is selected;
  - a `HistoryRecorded` refresh keeps the selected id;
  - Advanced search state maps presets to `DateFrom`/`DateTo`, and is saved to settings only when `RememberAdvancedSearch` is on;
  - row-menu actions target the invoked row;
  - while the engine is disconnected, the fake scheduler runs a connection check every 5 s. Once the fake client reconnects, the checks stop and the list reloads. `RetryCommand` searches again at once;
  - removing a chip from `ActiveFilterChips` clears only that filter and searches again.
- [X] T119 [P] [US5] Write `tests/AkmlSql.Shell.Shared.Tests/HistoryRestoreServiceTests.cs`, with fake opener and prompt:
  - **Always** opens up to `RestoreMaxQueries` entries, passing the reconnect flag;
  - **Prompt** shows the prompt with the entries and opens only the chosen ones;
  - **Never** opens nothing;
  - `Tabs.SessionRecovery = false` disables restore entirely.
- [X] T120 [P] [US5] Write `tests/AkmlSql.Shell.Shared.Tests/DraftCapturePolicyTests.cs` for the pure `DraftCapturePolicy` (created in T133):
  - `ShouldCaptureDraft(docName, text, hasKey)` is true only for `*.sql` or `SQLQuery*` names with non-whitespace text and no session key;
  - `TruncateToLimit(text, kb)` leaves text within the limit unchanged, and cuts longer text to `kb * 1024` characters with a final line `-- [truncated by AKML SQL: query larger than ‹kb› KB]`;
  - `SelectAutosaveTargets(docs)` returns only dirty query documents.
- [X] T121 [P] [US5] Write `tests/AkmlSql.Shell.Shared.Tests/RestoreClosedTabFallbackTests.cs`, with a fake history client and opener: with an empty `ClosedTabStack`, the command asks history for the newest closed entry (`IsOpen = false`, first row) and opens it; with a non-empty stack, history isn't asked.
- [X] T122 [P] [US5] Write `tests/AkmlSql.Shell.Shared.Tests/HistoryVersionActionsTests.cs`, using the view model with a fake client and the injectable hooks from T137:
  - with `SelectedVersion` set, Open, Copy and Re-execute receive that version's text;
  - with none selected, they receive the entry's full text;
  - "Compare with current" passes the version text and the current full text to `ShowCompare`.
  - Open on a query that is open in a tab, with no older version selected, calls `ActivateDocument` for that tab instead of `OpenDocument` (T140).
- [X] T123 [P] [US5] Write `tests/AkmlSql.Shell.Shared.Tests/HistoryKeyboardTests.cs` (`[StaFact]`, control built headlessly with a fake view model), calling `HandleListKey(key, modifiers)` (T139):
  - Enter opens; Delete removes after the confirmation hook; F2 renames and is refused while the query is open; Ctrl+C copies the SQL; Space stars or un-stars; each returns handled;
  - Esc in the search box first clears the text, then the filters;
  - the row star and ⋯ are focusable `Button`s with `AutomationProperties.Name` "Star query" and "Query actions";
  - every icon-only toolbar control has the `AutomationProperties.Name` listed in T185: "Refresh", "Show starred queries only", "Show open queries only", "Show closed queries only", "Filter by server or database", "More actions" and "Clear search".
- [X] T124 [P] [US5] Write `tests/AkmlSql.Engine.Tests/History/HistoryScaleTests.cs` (`[Trait("Category","Performance")]`, skippable like `PerformanceBaselineTests`): insert 100,000 rows in about 20,000 sessions, then:
  - a grouped search page (100 rows) and its `TotalCount` each return in under 250 ms after a warm-up query;
  - paging with the fixed `HasMoreEntries` rule reaches the last page;
  - a free-text search that also matches names and servers stays under 250 ms.
- [X] T125 [P] [US5] Write `tests/AkmlSql.Engine.Tests/History/HistoryRequestHandlerUs5Tests.cs`, through `HistoryRequestHandler` (T130) against a temporary database:
  - `GetFilterValues` returns distinct, sorted `Servers` and `Databases`, leaving out empty values;
  - a `HistoryRecordRequest` with `IsDraft = true` is stored with status `NotExecuted` and isn't counted as a run;
  - a search with `PathFilter` returns only entries whose source contains the value.

### Implementation for User Story 5 — Core and engine

- [X] T126 [P] [US5] Create `src/AkmlSql.Core/Models/History/HistoryDateGroups.cs` (enum plus `For(DateTime nowLocal, DateTime whenLocal)` plus `Label(group)`: "Today", "Yesterday", "This week", "Last week", "This month", "Older"). **Do not change** `HistoryDateBucket.Of`: the web edition and `WebHistoryLogicTests` depend on it.
- [X] T127 [P] [US5] Create `src/AkmlSql.Core/Text/LineDiff.cs`: `static IReadOnlyList<LineDiffEntry> Diff(string left, string right)` using an LCS over lines. Each `LineDiffEntry` is (`Kind`: Same, Added, Removed or Changed, `LeftLine?`, `RightLine?`, `LeftText`, `RightText`). Pair adjacent Removed and Added runs as Changed.
- [X] T128 [P] [US5] Add the DTO changes:
  - `src/AkmlSql.Core/Models/History/ExecutionStatus.cs`: `NotExecuted = 3`;
  - `HistoryRecordRequest.cs`: `[Key(12)] bool IsDraft`;
  - `HistorySearchRequest.cs`: `[Key(13)] string? PathFilter`;
  - `HistoryActionRequest.cs`: action constant `GetFilterValues = 12`.

  Response keys 9–10 were added in T062.
- [X] T129 [US5] In `src/AkmlSql.Engine/History/HistoryDatabase.cs` `SearchAsync` (~:833-1136):
  - replace the full-text `INNER JOIN` with the OR clause from data-model.md §2.4: full-text `IN` subquery **or** `LIKE` on `COALESCE(qs.name, h.tab_title, '')`, `h.source`, `h.server` and `h.database_name`;
  - build the `LIKE` terms from `HistorySearchTerms.Extract`, requiring every term;
  - add the same `LEFT JOIN query_sessions qs` to the count query;
  - update the fallback clause matcher (~:951);
  - add `PathFilter` (`h.source LIKE`), and compare dates with `datetime()`;
  - add `GetFilterValuesAsync()` over `GetDistinctServersAsync`/`GetDistinctDatabasesAsync` (~:1141-1176), capped at 500;
  - on insert, store drafts (`IsDraft`) with `status = 3`, and make grouped `exec_count` count `status <> 3`;
  - when a real run is recorded for a session whose latest row is a draft, copy the draft text into `history_versions` first.
- [X] T130 [US5] In `src/AkmlSql.Engine/History/HistoryRequestHandler.cs`, route `GetFilterValues` (fill `Servers`/`Databases`), and pass `IsDraft` and `PathFilter` through (tested by T125).

### Implementation for User Story 5 — shell

- [X] T131 [US5] Add the History settings to `src/AkmlSql.Core/Config/AppSettings.cs` `HistorySettings` (~:758-789), per data-model.md §1.2: `MaxQuerySizeKb` (1024), `RestoreMaxQueries` (20), `ReconnectRestoredQueries` (true), `RememberAdvancedSearch` (false), `HistoryAdvancedSearchState? AdvancedSearch`, plus the new `HistoryAdvancedSearchState` class.
- [X] T132 [US5] In `src/AkmlSql.Shell.Shared/History/HistorySearchParser.cs`, parse `path:` into `PathFilter`, and `date:[yyyyMMdd TO yyyyMMdd]` into `DateFrom`/`DateTo`. Add `internal static IReadOnlyList<(string Syntax, string Meaning)> HelpRows` listing every supported form: `name:`, `path:`, `sql:`, `server:`, `database:`/`db:`, `starred:true|false`, `open:true|false`, `date:[… TO …]`, `"phrase"`, `OR`, `NOT`, `word*`.
- [X] T133 [US5] In `src/AkmlSql.Shell.Shared/History/ExecutionCapture.cs`:
  - create the pure helper `src/AkmlSql.Shell.Shared/History/DraftCapturePolicy.cs` (add it to the projitems; tested by T120) with `ShouldCaptureDraft`, `TruncateToLimit` and `SelectAutosaveTargets`, and use it for the rules below;
  - truncate captured text to `History.MaxQuerySizeKb` KB with `DraftCapturePolicy.TruncateToLimit`;
  - raise a new `public static event Action<long?>? HistoryRecorded` after the awaited record from T069;
  - on `OnDocumentClosing` for a `.sql`/`SQLQuery*` document with non-empty text and **no** session key, send a record with `IsDraft = true` (plus the server and database, if known) before closing;
  - add a `DispatcherTimer` every `Tabs.AutoSaveInterval` seconds (when `Tabs.SessionRecovery` is on) that snapshots dirty open query documents: `SaveVersion` when a session key exists, otherwise an `IsDraft` record plus `Adopt` of the new session key once the response returns;
  - stop the timer when `ShuttingDown`.
- [X] T134 [US5] In `src/AkmlSql.Shell.Shared/History/HistoryViewModel.cs` (tested by T118):
  - **Search:** debounce `SearchText` changes by 250 ms (a `DispatcherTimer`, injectable for tests); select the first entry after each search.
  - **Advanced search:** add an `AdvancedSearch` object (Period preset → `DateFrom`/`DateTo`; `Server`/`Database` filled from `GetFilterValues`; `Starred`; `OpenOnly`) and an `ActiveFilterChips` collection with remove commands. Load it from, and save it to, settings when `RememberAdvancedSearch` is on.
  - **Live refresh:** subscribe to `ExecutionCapture.HistoryRecorded` and re-query on the UI thread, preserving the selected entry id and scroll offset (exposed for the control). Preserve them after star and delete too.
  - **Disconnected:** show the overlay, run a 5-second probe while it is up, and add `RetryCommand`.
- [X] T135 [US5] In `src/AkmlSql.Shell.Shared/History/HistoryToolWindowControl.cs`, the search area:
  - placeholder `Search` (~:208);
  - a `?` button beside the box opening a popup that lists `HistorySearchParser.HelpRows`;
  - a collapsible **Advanced search** panel under the box: Period combo (Everything, Last week, Last month, Last 3 months, Custom), two `DatePicker`s shown only for Custom, Server and Database combos, Starred and Open toggles, and Reset;
  - a chips row showing the active filters, each with a remove button.

  Texts come from contracts/ui.md §4.
- [X] T136 [US5] In `HistoryToolWindowControl.cs`, the rows (~:866-1000) and groups (~:685-688, ~:789-840):
  - Line 2 right shows `server · database`, plus an environment badge when `EnvironmentMatcher.Match(rules, server, db)` matches. The rules come from `ConfigManager.Load().Tabs.ColoringRules`; the badge is the rule label, on a `HexBrush.Get(color)` background, with a contrasting foreground.
  - Grouping uses `HistoryDateGroups`, and group headers show `Label (n)`.
  - `×N · M versions` uses the muted `TextSecondary` colour.
  - Draft rows show `Not executed`.
- [X] T137 [US5] Versions in `HistoryToolWindowControl.cs` (~:1684-1804) and `HistoryViewModel.cs`:
  - each version row gets a page glyph and `server · environment`;
  - add `SelectedVersion` to the view model, and make Open, Copy and Re-execute use the selected version's text when set. Route them through injectable hooks (`OpenDocument`, `ActivateDocument`, `SetClipboard`, `Execute`, `ShowCompare`), so they can be tested (T122);
  - add a **Compare with current** context item on a version row.
- [X] T138 [US5] Upgrade `src/AkmlSql.Shell.Shared/History/HistoryDiffWindow.cs`:
  - Each side's header reads `‹name› — ‹HistoryTimeFormat.Absolute(time)›`.
  - Both sides are rendered from `LineDiff.Diff`, one `SqlPreviewView` per side, aligned line by line with blank filler lines. Added, removed and changed lines get tints from `StatusSuccess`, `StatusDanger` and `StatusWarning` at low opacity, through theme resources.
  - It serves both "Compare with current" (a version against the entry's current text) and the existing two-row Compare.
- [X] T139 [US5] Keyboard and focus in `HistoryToolWindowControl.cs`. Put the list key mapping in `internal bool HandleListKey(Key key, ModifierKeys mods)` (tested by T123):
  - **List `PreviewKeyDown`:** Enter → Open, Delete → Remove (with confirmation), F2 → Rename (refused while open), Ctrl+C → Copy SQL, Space → toggle star.
  - **Tab order:** search → list → versions → preview.
  - **Row controls:** the row star and ⋯ (~:874-909) become focusable `Button`s (templated to look the same), with `AutomationProperties.Name` "Star query" and "Query actions".
  - **Search box Esc:** clear the text; when it is already empty, clear the filters.
- [X] T140 [US5] Row and toolbar menus in `HistoryToolWindowControl.cs` (~:1077-1133, ~:1967) and `HistoryViewModel.cs` (~:579, ~:736, ~:785-810, ~:832-918):
  - **Row ⋯:** shown on hover or keyboard focus only. Items, in order, as in contracts/ui.md §4: Open query, Copy SQL, Re-execute, Rename query, Compare…, Remove query and its history, Remove queries older than this…. Every item acts on that row.
  - **Toolbar ⋯:** holds `Export…` (moved from the row menu) and `Clear history…` (the existing DeleteAll action; confirmation `Remove all queries except starred ones? This can't be undone.`).
  - **Remove older:** the confirmation formats the date with `HistoryTimeFormat.Absolute`.
  - **Re-execute:** reuse Open's `ScriptFactory` connection code (~:1988-2082). When it can't connect, open the text in a new tab and put `Connect, then run (F5).` in the status bar.
  - **Open query** (row menu, Open button and Enter): when the query is open in a tab and no older version is selected, switch to that tab (`DocumentSessionKeys.TryFindDocument(entry.SessionKey)`, through the `ActivateDocument` hook); otherwise open the text in a new tab, as today.
- [X] T141 [US5] In `HistoryToolWindowControl.cs` (~:722-779, ~:1351-1392):
  - replace the "Loading..." text with a 12×12 ellipse spinner (the `SchemaProgressMargin` pattern in CLAUDE.md);
  - disconnected overlay: `History is unavailable — the AKML engine isn't connected.` plus a `Retry` button bound to `RetryCommand`;
  - empty states for the preview (`Select a query to see it here.`) and the versions pane (`No earlier versions.`).
- [X] T142 [US5] Create `src/AkmlSql.Shell.Shared/History/HistoryRestoreService.cs` (add it to the projitems):
  - **Input:** `HistoryRestoreState.RestorableEntryIds` (from T070).
  - **Settings:** `Tabs.SessionRecovery` (master), `Tabs.RestoreOnStartup` ("always" / "prompt" / "never") and `History.RestoreMaxQueries`.
  - **Opening:** fetch each entry's full SQL, then open it with the same code as Open query's new-tab path, including the connection when `History.ReconnectRestoredQueries` is on, and `DocumentSessionKeys.Adopt` (same rule as T071).
  - **Test seams:** injectable opener and prompt.
- [X] T143 [US5] Create `src/AkmlSql.Shell.Shared/History/RestoreQueriesDialog.cs` (a `ThemeAwareWindow`, added to the projitems). Title `AKML SQL – Restore queries`. It lists the entries (name, `server · database`, time) with checkboxes, all checked by default, and has **Restore selected** and **Not now** buttons.
- [X] T144 [US5] In `src/AkmlSql.Ssms22/AkmlSqlPackage.cs`, once `ReconcileOpen` completes (T070), run `HistoryRestoreService` on the UI thread (`JoinableTaskFactory.SwitchToMainThreadAsync`).
- [X] T145 [US5] Move and add History settings rows:
  - `src/AkmlSql.Shell.Shared/Dialogs/Pages/HistoryPage.cs` gains, with existing RowFactory methods (US6 turns the wide ranges into number fields): **Maximum query size** (KB, 16–1024), **Restore open queries when SSMS starts** (`Tabs.SessionRecovery`), **When restoring** (Always / Ask / Never → `Tabs.RestoreOnStartup`), **Maximum number of queries to restore**, **Automatically reconnect restored queries**, **Auto-save interval**, **Max closed tabs** and **Remember advanced search settings**.
  - Remove the moved rows from `TabsPage.cs` (~:73-95).
  - Page reset (T025) keeps working, because each page's `Save` writes only its own rows.
  - Update the allow-list in `tests/AkmlSql.Shell.Shared.Tests/OptionsLiveSettingsTests.cs` (T010) in the same change, so the US1 test gate stays green.
- [X] T146 [US5] In `src/AkmlSql.Shell.Shared/Commands/RestoreClosedTabCommand.cs` (~:58-121), when `ClosedTabStack` is empty, search history for the most recent closed entry (`IsOpen = false`, first row) and open it through the History open path. The status bar says `Restored '‹name›' from SQL History.`. Make the history client and the opener injectable (tested by T121).
- [X] T147 [US5] Remove the never-wired session-recovery path: `src/AkmlSql.Shell.Shared/Sessions/SessionAutoSave.cs`, `SessionRecoveryInitializer*.cs` and `SessionRecoveryDialog.cs`, together with their projitems entries.
  - First search `src/` (including `src/AkmlSql.Web` and `src/AkmlSql.Engine`) for other callers of the engine's session storage (`src/AkmlSql.Engine/Sessions/*`). Delete the engine handler and files **only** if nothing else uses them; otherwise leave them and note why in this task.
  - Update or remove any tests that referenced the deleted classes.
  - **Done (2026-09-29):** the three shell files were not even in the projitems (never compiled). Nothing in `src/` (Web, Engine, shell) sent `SessionSave`/`SessionRestore`/`SessionDelete` (50–52), so the engine's `Sessions/SessionRequestHandler.cs` and `SessionStorage.cs`, their registration in `EngineHandlerRegistry`, and the seven Core `Session*Request/Response` + `RecoverableSessionDto` messages were deleted too. The constants 50–52 are removed and the numbers marked reserved. Two engine tests had an unused `using AkmlSql.Engine.Sessions;`, now removed.
  - **Implementation notes for US5 (T135–T146):** restore on start needed each entry's name, server, database and session key, so `HistoryActions.GetEntries = 13` and `HistoryActionResponse.Entries` (Key 12) were added; version rows carry `Server`/`Database` (`HistoryVersionDto` Keys 3–4) for "server · environment". The open-in-new-tab code moved to `HistoryQueryOpener` (shared by Open query, Re-execute, restore on start and the Ctrl+Shift+T fallback). "Remove queries older than this…" formats the time with `HistoryTimeFormat.Absolute` (per T140) rather than the contract's `d MMM yyyy HH:mm` placeholder. Rename is refused while the query is open with "'‹name›' is open in a tab. Close it to rename the query."
- [X] T148 [US5] Build, then run the Core, Engine, Shell and Web.Tests (History) suites. Run quickstart.md scenarios 29–37 and record the results in `baseline.md` under "US5 verification".

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

**Depends on**: US1 (the dead rows are already hidden). US5 T145 must be done before T161 converts History's sliders.

### Tests for User Story 6 (write first)

- [X] T149 [P] [US6] Rewrite `tests/AkmlSql.Shell.Shared.Tests/OptionsNavStructureTests.cs` to pin contracts/ui.md §1 exactly: group headers, leaf labels, order and page keys (Tags), plus breadcrumb (`Display`) == tree path. Keep the existing AI deep-link assertion (`TestBuildWindowForRenderTest("AI Assistance")`); it now finds a top-level leaf "AI assistance".
- [X] T150 [P] [US6] Write `tests/AkmlSql.Shell.Shared.Tests/RowFactoryGatingAndNumberTests.cs`:
  - a child row is disabled with its label in `TextDisabled` while its parent CheckBox is unchecked, and re-enabled on check;
  - `AddNumber` accepts `250000`, rejects `abc` (red border, previous value kept), clamps to the range, and ▲/▼ step by `step`.
- [X] T151 [P] [US6] Write `tests/AkmlSql.Shell.Shared.Tests/OptionsPaletteTests.cs`:
  - `SettingsWindow.BuildOptionsCatalog(settings)` contains "Show nullability info" (Kind Toggle, PageKey `IntelliSense`), and excludes every AI Assistance row and every Info/Button row.
  - Toggling that entry through the palette handler changes **only** `IntelliSense.ShowNullability` in `config.json` (compare the JSON before and after, AppData isolated).
  - `ShowOptions(pageKey, null, "Retention (days)")` passes the label to the window through `WindowFactoryOverride` (T023). A `SettingsWindow` built without being shown, with `InitialFocusLabel = "Retention (days)"`, selects the History page and focuses that row once loaded.
- [X] T152 [P] [US6] Write `tests/AkmlSql.Core.Tests/Config/TabEnvironmentsMigrationTests.cs`:
  - stock rules → four environments (PRODUCTION #FF4444, STAGING #FFB800, DEV #44BB44, AZURE #4488FF) and every rule linked;
  - duplicate labels with different colours → `Label (2)`;
  - a second load is idempotent;
  - no rules → the four defaults are seeded.

  Also write `tests/AkmlSql.Core.Tests/Tabs/EnvironmentMatcherServerDatabaseTests.cs`: a server rule with a non-empty `DatabaseName` matches only that server **and** database. Existing matcher tests stay green.
- [X] T153 [P] [US6] Write `tests/AkmlSql.Shell.Shared.Tests/ManageRulesWindowTests.cs` (`[StaFact]`):
  - the WPF window, given the same rule DTOs as the old dialog, returns identical `GetOverrides()` for a set of edits: only changed rows; a row set back to its default removes the override;
  - `RestoreSessionSuppressions` lists the restored ids;
  - rows are grouped by category.
- [X] T154 [P] [US6] Write `tests/AkmlSql.Shell.Shared.Tests/ThemedButtonPageThemeTests.cs`: `ThemedButton.ApplySecondary(button, PageTheme.Dark)` gives the button a template whose background, hover and pressed brushes come from the dark `PageTheme`, not the stock Aero chrome, using the pattern in `OptionsHoverContrastTests`.
- [X] T155 [P] [US6] Write `tests/AkmlSql.Core.Tests/Tabs/EnvironmentValidatorTests.cs` for the pure `EnvironmentValidator` (created in T164):
  - names are unique (case-insensitive) and 1–40 characters;
  - colours match `^#[0-9A-Fa-f]{6}$`;
  - `CanDelete(name, rules)` is false when a rule uses the environment, and returns the rule patterns for the message.
- [X] T156 [P] [US6] Write `tests/AkmlSql.Core.Tests/Tabs/ColoringRuleOrderingTests.cs` for `ColoringRuleOrdering.Move(rules, index, delta)` (created in T164):
  - moving up or down swaps neighbours;
  - moves past either end are ignored;
  - `Order` is always renumbered 0..n-1, which also fixes duplicate orders left by older configs.
- [X] T157 [P] [US6] Write `tests/AkmlSql.Shell.Shared.Tests/PageThemeHighContrastTests.cs` (`[StaFact]`):
  - with the high-contrast override on (a test seam next to `HostThemeWatcher.VariantOverrideForTests`), the Options window builds with `PageTheme.HighContrast`;
  - every `PageTheme.HighContrast` brush comes from `ThemePalette.HighContrast` (system colours), not the Light or Dark values.

### Implementation for User Story 6

- [X] T158 [US6] Rebuild the Options tree to contracts/ui.md §1 in `src/AkmlSql.Shell.Shared/Dialogs/SettingsWindow.cs`:
  - **Tree** (`CreateSidebar` ~:527-570): `AddTreeGroup`/`AddTreeLeaf` calls in the exact order and labels, in sentence case.
  - **`pages[]`** (~:1140-1167): the same order.
  - **Page keys stay unchanged.**
  - **Each page's `Display` and `Title`** (`src/AkmlSql.Shell.Shared/Dialogs/Pages/*Page.cs`): `Display` is the breadcrumb (e.g. `Suggestions › Warnings & highlighting` for `SafetyPage`, `Inserted code › Objects & statements` for `InsertStatementsPage`, `Queries › Color` for `TabsPage`, `Navigation` for `NavigationPage`); `Title` is the last segment.
- [X] T159 [P] [US6] Plain-language labels:
  - "Tables Alias" → "Suggest table aliases" (`IntelliSensePage.cs`);
  - "Temperature (x10)" → "Creativity (temperature)" (`AiAssistancePage.cs`);
  - group headers in sentence case on every page (e.g. "Refresh behavior", "Rename options");
  - every `ctx.RegisterSearch` label equals its on-page label (e.g. the column picker "Default sort"; the alias map label);
  - remove the "SQL Prompt style" wording from the Behavior descriptions.
  - Update the allow-list in `tests/AkmlSql.Shell.Shared.Tests/OptionsLiveSettingsTests.cs` (T010) in the same change, so the US1 test gate stays green.
- [X] T160 [US6] Parent gating (research R5):
  - `src/AkmlSql.Shell.Shared/Ui/Theme/PageTheme.cs`: add `TextDisabled` (from `ThemeTokens.TextDisabled` for both palettes).
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
- [X] T161 [US6] Number fields:
  - Add `RowFactory.AddNumber(StackPanel panel, string label, int min, int max, int step, string unit, string description = "", CheckBox? parent = null)`, returning `(FrameworkElement Row, TextBox Box)` with ▲/▼ buttons, the unit `TextBlock`, and validation (red border, last valid value kept).
  - Replace these sliders, updating each page's `Controls` `Load`/`Save`:
    - `IntelliSensePage` Maximum suggestions (~:41) and Trigger delay (~:46);
    - `HistoryPage` Retention, Max entries, Maximum query size and Auto-save interval;
    - `AiAssistancePage` Max response tokens (~:286) and Timeout (~:296);
    - `ExecutionPage` Notification threshold (~:26);
    - `SafetyPage` Reminder interval (~:52).
  - Remove units from the label text.
  - Extend `FlashRow` (`SettingsWindow.cs` ~:1111-1131) to any `Panel`, not only `Border`.
  - Update the allow-list in `tests/AkmlSql.Shell.Shared.Tests/OptionsLiveSettingsTests.cs` (T010) in the same change, so the US1 test gate stays green.
- [X] T162 [US6] Options in the Command Palette (research R7):
  - `SettingsWindow.cs`: add `internal static IReadOnlyList<OptionsCatalogEntry> BuildOptionsCatalog(AppSettings settings)`. It builds pages on a throwaway instance on the UI thread and returns PageKey, PageDisplay, Label, Description and Kind, with the Row kept internally. Cache it per session.
  - Create `src/AkmlSql.Shell.Shared/Productivity/CommandPalette/OptionPaletteEntry.cs` (a `CommandEntry` subclass implementing `INotifyPropertyChanged`, with IsOn and StateText; Id `opt:{pageKey}:{label}`) and add it to the projitems.
  - `CommandRegistry.cs`: an **Options** category listed when the query is at least 2 characters, excluding the AI Assistance page and Info/Button rows. Names read `‹PageDisplay› › ‹Label›`.
  - `CommandPaletteViewModel.cs` `ExecuteCommand` (~:141-177):
    - for `opt:` Toggle entries: `s = ConfigManager.Load()`, then `controls.Load(s)`, flip the CheckBox, `controls.Save(s)`, `OptionsCommand.SaveAndNotify(s)`; update IsOn; **don't** raise `CloseRequested`, and don't count usage;
    - for other kinds: close and call `OptionsCommand.ShowOptions(pageKey, null, label)`.
  - `CommandPaletteWindow.cs`: an item template selector that shows `On`/`Off` for option entries.
- [X] T163 [US6] Add an overload `internal static bool ShowOptions(string? pageKey, string? agentId, string? focusLabel)` to `src/AkmlSql.Shell.Shared/Commands/OptionsCommand.cs`. Keep the 2-argument method delegating to it, because `Ai/AiChatPanel.cs:135`'s `Func<string?,string?,bool>` must still compile.
  - After the window is built, find the `SearchEntry` with that label on that page and reuse the scroll-and-flash code from `CommitSelectedSearchResult` (`SettingsWindow.cs` ~:1046-1072).
  - Add `InitialFocusLabel` to `IOptionsDialog` (T023) and to `SettingsWindow`, applied when the window loads. `ShowOptions` passes `focusLabel` through `WindowFactoryOverride`.
- [X] T164 [US6] Environments model:
  - Create the pure helpers `src/AkmlSql.Core/Models/Tabs/EnvironmentValidator.cs` (tested by T155) and `src/AkmlSql.Core/Models/Tabs/ColoringRuleOrdering.cs` (tested by T156).
  - Create `src/AkmlSql.Core/Models/Tabs/TabEnvironment.cs` (Name, Color).
  - `src/AkmlSql.Core/Config/AppSettings.cs`: add `TabSettings.Environments` and `ColoringRule.Environment`.
  - `src/AkmlSql.Core/Config/ConfigManager.cs`: run the migration from data-model.md §1.3 in both `Load` overloads, idempotently.
  - `src/AkmlSql.Core/Models/Tabs/EnvironmentMatcher.cs` (~:39-64): a server rule with a non-empty `DatabaseName` requires the database to match as well.
  - When rules are saved, write each rule's `Label` and `Color` from its environment. Safety keys on `Label` (`Safety/ExecutionInterceptor.cs:257-262`, `EnvironmentSeverity`).
- [X] T165 [US6] Queries › Color page grid (`src/AkmlSql.Shell.Shared/Dialogs/Pages/TabsPage.cs`):
  - replace the ListBox and Add/Edit/Remove buttons (~:33-70) with a themed grid: `ListView`/`GridView` built from `PageTheme` brushes, following the `AiAgentListView.BuildItemStyle` pattern;
  - columns: **Server / group pattern** (text), **Database** (text, optional), **Environment** (swatch plus combo of environment names);
  - **+ Add server/database**, **Remove** and ↑/↓ buttons; ↑/↓ move rules with `ColoringRuleOrdering.Move`, which renumbers `Order` 0..n-1;
  - hint `You can use wildcards (*)`;
  - an **Edit environments…** button;
  - rules and environments are part of the page's `Load`/`Save`;
  - remove the host-owned rule CRUD and `ShowRuleEditor` from `SettingsWindow.cs` (~:2001-2151), and remove the now-unused hooks (~:1191-1196).
- [X] T166 [US6] Create `src/AkmlSql.Shell.Shared/Dialogs/EditEnvironmentsDialog.cs` (`ThemeAwareWindow`, title `AKML SQL – Edit environments`, added to the projitems):
  - **List:** Name (editable) and Colour (a swatch that opens a colour grid of the 8 `ThemeTokens.TabColor*` colours plus **Custom…**, which opens `System.Windows.Forms.ColorDialog` and stores `#RRGGBB`).
  - **Options:** **Use gradient colors** (`Tabs.GradientColors`) and **Restore default environments**.
  - **Validation:** through `EnvironmentValidator` (names unique and 1–40 characters, `#RRGGBB` colours). Deleting an environment that rules use is refused, with the rule patterns named.
  - Save / Cancel.
- [X] T167 [US6] Themed buttons (research R9):
  - `src/AkmlSql.Shell.Shared/Ui/Theme/ThemedButton.cs`: add `ApplySecondary(Button, PageTheme)` and `ApplyPrimary(Button, PageTheme)`, with templates cached per `PageTheme`, following `ComboBoxTheming.ThemeCache`.
  - Use them in:
    - `SettingsWindow.MakeButton`/`MakePrimaryButton` (~:1467-1521);
    - `RowFactory.AddButton` (~:135-163);
    - `AiAgentListView.MakeButton` (~:333-343; add a `PageTheme` parameter; callers at ~:65-68 and `AiAssistancePage.cs` ~:241-244);
    - the Color page buttons.
  - Replace the hard-coded search badge colours (`SettingsWindow.cs` ~:905-912) with `PageTheme` brushes.
- [X] T168 [US6] Port `src/AkmlSql.Shell.Shared/Analysis/ManageRulesDialog.cs` from WinForms to a WPF `ThemeAwareWindow`, title `AKML SQL – Code analysis rules`:
  - rules grouped by category with expandable headers; each row has Enabled (CheckBox), Rule id, Name, a Severity combo (Hint / Information / Warning / Error) and a Fix ✓ glyph;
  - a description pane for the selected rule;
  - `Settings file:` showing `Constants.ConfigFilePath`;
  - the session-suppressed strip with **Restore**;
  - Save / Cancel.

  Keep the public surface that `ManageRulesCommand.cs` uses unchanged: the constructor inputs, `ShowDialog` result, `GetOverrides()` and `RestoreSessionSuppressions`.

  Also add a **Manage rules…** button to `CodeAnalysisPage.cs`. It opens the same window, then reloads `_settings.CodeAnalysis.RuleOverrides` from disk (the pattern in `FormattingPage.RefreshActiveStyleFromDisk` ~:197-207), so OK in Options doesn't write stale overrides.
- [X] T169 [US6] High contrast in the Options window:
  - `src/AkmlSql.Shell.Shared/Ui/Theme/PageTheme.cs`: add a `HighContrast` snapshot built from `ThemePalette.HighContrast` (it delegates to Windows system colours), including the new `TextDisabled`;
  - `SettingsWindow.ResolvePageTheme` (T022) returns `PageTheme.HighContrast` when `SystemParameters.HighContrast` is on (or the host variant is HighContrast), whatever the saved theme;
  - the other AKML windows already follow `ThemeRegistry`'s high-contrast palette; check them in the screenshot tour (T193) with Windows high contrast on;
  - tested by T157.
- [X] T170 [US6] Build, then run the Core and Shell suites. Run quickstart.md scenarios 38–43, including a dark-theme pass through every Options sub-window, and record the results in `baseline.md` under "US6 verification".

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

- [X] T171 [P] [US7] Write `tests/AkmlSql.Site.Tests/Docs/F1SlugTests.cs`, following the `FooterDocLinksTests` pattern:
  - read `src/AkmlSql.Shell.Shared/Help/F1HelpRegistrations.cs` and every `HelpTopic` in `src/AkmlSql.Shell.Shared/Dialogs/Pages/*.cs` as text, and extract the slug and anchor;
  - assert each slug exists in `DocsCatalog.Scan(repo/doc, options)`;
  - assert each anchor matches a heading id in that doc (Markdig AutoIdentifiers rules).
- [X] T172 [P] [US7] Write `tests/AkmlSql.Core.Tests/Config/WindowTitlesTests.cs` (`WindowTitles.For("Options") == "AKML SQL – Options"`). Also write `tests/AkmlSql.Shell.Shared.Tests/WindowTitleUsageTests.cs`, a text scan asserting that every `Title =` and `Text =` assignment on a Window or Form in `src/AkmlSql.Shell.Shared` uses `WindowTitles.For(...)`, apart from an explicit allow-list (file dialogs, tool-window captions, message-box captions).
- [X] T173 [P] [US7] Write `tests/AkmlSql.Shell.Shared.Tests/AkmlMenuTableTests.cs` against the declarative table (T180), exposed as `internal static AkmlMenuTable.Entries`:
  - exactly 12 top-level entries, in contracts/ui.md §2 order;
  - Help ▸ ends with Check for Updates then About AKML SQL;
  - every command id in the table is in `RegisteredCommands.Ids` (T105; it lives in `AkmlSql.Shell.Shared`, so the shell test project compiles it);
  - no unregistered command (TextToSql, AI Optimize, AI Index Analysis, CRUD, Grid Find) appears.
- [X] T174 [P] [US7] Write `tests/AkmlSql.Formatting.Tests/Profiles/TeamStyleFolderTests.cs`:
  - a style in a team folder is listed with `Source = "team"`;
  - it is read-only when the folder has a read-only attribute or ACL (simulate with a provider flag or a temp folder marked read-only);
  - name precedence is user > team > built-in;
  - an unreachable folder (a non-existent UNC path) returns the other styles within 2 s, and `TeamFolderUnavailable = true`.

  Also add engine tests in `tests/AkmlSql.Engine.Tests/Formatter/TeamStyleWriteRefusalTests.cs`: Save, Rename, Delete and Reset on a read-only team style return `Success = false` with the contract error text.
- [X] T175 [P] [US7] Write `tests/AkmlSql.Engine.Tests/Formatter/FormatSqlActionsTests.cs`:
  - `Actions = null` → output identical to today for three golden inputs;
  - semicolons insert and remove;
  - brackets add and remove;
  - `ApplyCasing = false` keeps the original keyword case;
  - `ApplyLayout = false` keeps the original whitespace but still applies the semicolons action;
  - `ExpandWildcards = true` expands `SELECT *` using a schema-cache fixture (the pattern in `FormatActionDispatchTests.cs`).
- [X] T176 [P] [US7] Write `tests/AkmlSql.Core.Tests/Ipc/FormattingContractTests.cs`: round trips for `FormatSqlActionsDto`, `FormatRequest` key 5, `FormatSelectionRequest` key 5 and `ProfileInfo` keys 9–10, plus the legacy shapes.

  Also write `tests/AkmlSql.Shell.Shared.Tests/HistoryAccessibilityTests.cs`: every icon-only control in the History toolbar and row template, and the style list ⋮, has a non-empty `AutomationProperties.Name`.
- [X] T177 [P] [US7] Write `tests/AkmlSql.Shell.Shared.Tests/HelpRoutingTests.cs` (`[StaFact]`), using the test hook `F1HelpListener.OpenOverride` (T183):
  - the Options window's `CurrentHelpTopic` equals the selected page's `HelpTopic`, for every page;
  - the Format Styles window's help topic is `topics/formatting#edit-styles-with-live-preview`;
  - executing `ApplicationCommands.Help` on an element with `HelpBinding.Attach` calls the hook with the attached key.
- [X] T178 [P] [US7] Write `tests/AkmlSql.Core.Tests/Config/TeamStyleFolderValidatorTests.cs` for `TeamStyleFolderValidator.Normalize(string? input)` (created in T186):
  - empty or whitespace → OK, and the folder is off;
  - a relative path → error;
  - a rooted local or UNC path → OK, returned in its `Path.GetFullPath` form;
  - `..` segments are resolved by that canonical form.
- [X] T179 [P] [US7] Write `tests/AkmlSql.Core.Tests/Config/FormatSqlActionsMapperTests.cs` for `FormatSqlActionsMapper.ToDto(FormatSqlActions)` (created in T188):
  - each setting maps to its DTO field (semicolons and brackets strings → 0/1/2);
  - the defaults of `new FormatSqlActions()` equal the common `formatActions` of the built-in profiles in `src/AkmlSql.Formatting/Profiles/BuiltIn/*.akmlstyle`.

### Implementation for User Story 7

- [X] T180 [US7] Build the menu from one declarative table:
  - Create `src/AkmlSql.Shell.Shared/Commands/AkmlMenuTable.cs` (added to the projitems): a declarative tree of groups, submenus and command ids matching contracts/ui.md §2, including the Active Style ▸ submenu (the US4 slots plus Edit Styles…).
  - Rewrite `EnsureTopLevelMenu` in `src/AkmlSql.Ssms22/AkmlSqlPackage.cs` (~:383-490) to build from it:
    - nested `msoControlPopup` (type 10) submenus;
    - `BeginGroup = true` for separators;
    - the version marker `popup.Tag = "akml-menu-v2"`;
    - when an existing "AKML SQL" popup lacks the marker, delete it and rebuild **once**;
    - the AI ▸ submenu shown only when AI is enabled, reusing `AiCommandVisibility`;
    - the editor context popup from T110 kept.
  - Remove the interim placement code from T110 that the builder now covers.
- [X] T181 [US7] In `src/AkmlSql.Ssms22/AkmlSqlSsms22.vsct`:
  - add groups and menus mirroring contracts/ui.md §2 (new IDSymbols for the submenus and groups, avoiding every id in `PackageGuids.cs` and the reserved ranges);
  - re-parent the buttons;
  - keep every command id and key binding unchanged;
  - after building, confirm `dte.ExecuteCommand("AKML_SQL.FormatDocument")` still resolves (the editor toolbar and completion popup use it). If the canonical name changed, pin it with `<Strings><CanonicalName>`.
- [X] T182 [US7] Window titles and icon:
  - Create `src/AkmlSql.Core/Config/WindowTitles.cs` (`public static string For(string name) => Constants.ProductName + " – " + name;`).
  - Apply it to every window and form listed in contracts/ui.md §5. File references are in research R27; the list includes `SettingsWindow.cs:283`, `FormatStylesEditorWindow.cs:136`, `StyleNameDialog.cs:171/185`, `ImportSummaryDialog.cs:46`, `SnippetManagerDialog.cs:48`, `HistoryDiffWindow.cs:22`, `ObjectSearchWindow.cs:49` and the WinForms forms.
  - Add `src/AkmlSql.Shell.Shared/Ui/WindowIcon.cs` (added to the projitems): it sets the window icon from an embedded `akml.ico`, copied from `src/AkmlSql.Installer/assets/icon.ico` into `src/AkmlSql.Shell.Shared/Resources/akml.ico` and declared as `<EmbeddedResource>` in the projitems. Call it from `ThemeAwareWindow` and from the WPF windows above that don't derive from it.
- [X] T183 [US7] Wire F1 help:
  - Add a test hook `internal static Action<string>? OpenOverride` to `F1HelpListener` (used instead of launching the browser when set), and `internal string? CurrentHelpTopic` to `SettingsWindow` and `FormatStylesEditorWindow` (tested by T177).
  - Create `src/AkmlSql.Shell.Shared/Help/HelpBinding.cs` (added to the projitems): `Attach(UIElement element, Func<string> topicKey)` adds a `CommandBinding(ApplicationCommands.Help)` that calls `F1HelpListener.Open`.
  - `F1HelpRegistrations.cs`: `DocBase = "https://akml.khamis.work/docs/"`; remap the existing keys to real slugs (`topics/formatting`, `topics/sql-history`, `topics/snippets`, `topics/static-analysis`, `topics/intellisense`, …) and remove keys with no topic.
  - `IPageBuilder`: add `string HelpTopic { get; }`, implemented on every page with the values in contracts/ui.md §1.
  - `SettingsWindow.OnWindowKeyDown` (~:1638): F1 → the current page's topic.
  - `FormatStylesEditorWindow`: set `HasHelpButton = true`, override `InvokeDialogHelp()`, and attach `HelpBinding` → `topics/formatting#edit-styles-with-live-preview`.
  - `src/AkmlSql.Shell.Shared/History/HistoryToolWindow.cs`: handle `VSConstants.VSStd97CmdID.F1Help` in the pane's command target (implement `IOleCommandTarget` on the pane if needed) → `topics/sql-history`.
- [X] T184 [P] [US7] Write the docs:
  - Create `doc/topics/options.md`, with one `##` section per Options page. Headings must produce exactly the anchors in contracts/ui.md §1 (e.g. `## Suggestions: Behavior` → check the generated id matches `suggestions-behavior`, and adjust the heading text if needed). Each section describes, in plain language, only the settings still shown after US1.
  - Update `doc/topics/formatting.md` (Active Style menu, option search, change markers, team style folder, Format SQL actions) and `doc/topics/sql-history.md` (search syntax, Advanced search, open marker, versions and compare, restore on start, keyboard).
- [X] T185 [US7] Accessibility and type (tested by T123 and T091):
  - `src/AkmlSql.Shell.Shared/History/HistoryToolWindowControl.cs`: add `AutomationProperties.Name` to every icon-only control ("Refresh", "Show starred queries only", "Show open queries only", "Show closed queries only", "Filter by server or database", "More actions", "Clear search").
  - Map the hard-coded `FontSize` values (research R27: 9, 9.5, 10, 10.5, 11, 11.5 → `Typography.Small`; 12 → `Typography.Body`; 14 → `Typography.H4`).
  - Replace `OpenClosedColorConverter` and `FavoriteColorConverter` (~:2193, ~:2461) with `SetResourceReference` on `ThemeTokens.HistoryOpenIcon`, `HistoryClosedIcon`, `HistoryStarActive` and `HistoryStarInactive`, so they follow theme changes.
  - Give the style list ⋮ in `FormatStylesEditorWindow.cs` the name "Style actions".
- [X] T186 [US7] Team style folder, engine side:
  - Create `src/AkmlSql.Core/Config/TeamStyleFolderValidator.cs` with `Normalize(string? input)` → (ok, full path or null, error) (tested by T178).
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
- [X] T187 [US7] Team style folder, shell side:
  - `src/AkmlSql.Shell.Shared/Dialogs/Pages/FormattingPage.cs`: a **Team style folder** text row with a **…** button (`System.Windows.Forms.FolderBrowserDialog`), validated with `TeamStyleFolderValidator.Normalize`.
  - `FormatStylesEditorViewModel.cs` `StyleListItem`: add `Source` and `IsReadOnly`.
  - `FormatStylesEditorWindow.cs`:
    - a **TEAM STYLES** group in the list;
    - read-only team styles: options disabled, Save, Rename and Delete disabled, Copy allowed;
    - when the team folder is unavailable, a muted row `Team styles unavailable — ‹folder› can't be reached`.
  - Update the allow-list in `tests/AkmlSql.Shell.Shared.Tests/OptionsLiveSettingsTests.cs` (T010) in the same change, so the US1 test gate stays green.
- [X] T188 [US7] Format SQL actions, settings and DTO:
  - `src/AkmlSql.Core/Config/AppSettings.cs`: a `FormatSqlActions` class and `FormatterSettings.FormatSqlActions`. Defaults come from the built-in profiles' `formatActions` (read `src/AkmlSql.Formatting/Profiles/BuiltIn/*.akmlstyle`; use the common value).
  - Create `src/AkmlSql.Core/Ipc/Messages/FormatSqlActionsDto.cs` per contracts/ipc.md.
  - `FormatRequest.cs` and `FormatSelectionRequest.cs`: `[Key(5)] FormatSqlActionsDto? Actions`.
  - Create `src/AkmlSql.Core/Config/FormatSqlActionsMapper.cs` with `ToDto(FormatSqlActions)` (tested by T179).
- [X] T189 [US7] Format SQL actions, engine side:
  - `src/AkmlSql.Engine/Formatter/FormatRequestHandler.cs` `HandleFormat` / `HandleFormatSelection`: when `request.Actions != null`, use it instead of `profile.FormatActions`.
  - `src/AkmlSql.Formatting/Pipeline/FormatterPipeline.cs`:
    - add pipeline options `ApplyLayout` and `ApplyCasing`. When layout is off, skip the layout stage and keep the original whitespace; when casing is off, skip the casing stage.
    - Stage 8 takes the semicolons and brackets choices from the options.
    - All defaults preserve today's behaviour.
  - `src/AkmlSql.Engine/Handlers/Formatting/FormattingHandlers.cs` (~:27-28): pass `ctx.SchemaCache` and `ctx.Sessions`, and after validation run the schema-aware Expand wildcards / Qualify object names operations (the same ones `HandleFormatAction` uses) when requested.
- [X] T190 [US7] Format SQL actions, shell side:
  - `src/AkmlSql.Shell.Shared/Formatting/FormatDocumentCommand.cs` (~:86) and `FormatSelectionCommand.cs`: send `Actions = FormatSqlActionsMapper.ToDto(settings.Formatter.FormatSqlActions)`, and the **real** editor session id (the `RefactorCommandHelper.TryGetActiveEditor()` pattern) instead of a random GUID.
  - `src/AkmlSql.Shell.Shared/Dialogs/Pages/FormattingPage.cs`: add a group headed `When you run Format SQL, AKML SQL will:` with Apply layout, Apply casing, Semicolons (Insert / Remove / Leave), Square brackets (Add / Remove / Leave), Expand wildcards and Qualify object names.
  - Update the allow-list in `tests/AkmlSql.Shell.Shared.Tests/OptionsLiveSettingsTests.cs` (T010) in the same change, so the US1 test gate stays green.
- [X] T191 [US7] Build, then run the Core, Engine, Formatting, Site and Shell suites, plus the format-parity goldens (they must be unchanged). Run quickstart.md scenarios 44–49 and record the results in `baseline.md` under "US7 verification".

**Checkpoint**: all 38 gap-plan items are delivered.

---

## Phase 10: Polish and cross-cutting concerns

- [X] T192 Run a full solution build (restore + build, MSBuild), then every suite from T002. Compare with `baseline.md`: new failures are regressions to fix. Confirm the `CorpusGateTests` pass rate hasn't dropped and the format-parity goldens are unchanged.
- [X] T193 [P] Extend `tests/AkmlSql.UiTests/SsmsScreenshotTour.cs`. Add captures of:
  - the AKML SQL menu expanded, and the Active Style ▸ submenu;
  - Options in light and dark (the Behavior, History and Color pages);
  - SQL History with Advanced search open;
  - the Format Styles window at its default size on the Lists page.

  Keep Northwind only, and keep the forbidden-words assertion. Run it against the deployed build (quickstart §0 deploy).
- [X] T194 [P] Update `doc/progress.md`: append a `## Spec 040 — SQL Prompt UI/UX parity: Options, SQL History, format styles (2026-09-28)` section in the existing format ("What the investigation found", "What was built", "Verification" with pass counts, "Issues hit", "Open").
- [X] T195 [P] Update `CLAUDE.md`:
  - "Latest merged work": add a spec 040 bullet;
  - "Open follow-ups": list the items in T196;
  - the Documentation table row and the Progress paragraph that name "most recently spec 037";
  - correct the stale "Theme colors come from `ThemeManager.Instance`" guidance (~:256) to ThemeRegistry / ThemeTokens / `SetResourceReference`.

  Also update `doc/deployment.md`: the stale VS 2022 MSBuild path and the "never build via .slnx" note (see CLAUDE.md Build Commands).
- [X] T196 [P] Record the follow-ups that stay open, in this file's "Deferred" section and in `doc/progress.md`:
  - commands with no registered handler (TextToSql, AI Optimize, AI Index Analysis, Generate CRUD Procedures, Find in Results Grid);
  - the VSCT menu parent (`IDM_VS_MENU_BAR` = 0x0081 is Edit), which leaves the VSCT menu invisible in SSMS 22;
  - AS keyword and column alias style in Format SQL actions;
  - Record failed executions and Encrypt at rest (hidden);
  - the editor context menu, if scenario 26 was waived.
- [X] T197 [P] Update `doc/_Prompt-Gap/11-UI-UX-Plan-Options-History-Styles.md`: add an "Implementation status (spec 040)" note at the top mapping each OPT/HIS/STY/X item to done or deferred, with the task ids. Refresh the file 10/11 lines in `doc/_Prompt-Gap/00-INDEX-and-Questions.md`.
- [X] T198 Run quickstart.md end to end (scenarios 1–49) on the deployed build. Record pass, fail or waived (with a reason) for each scenario in `specs/040-sqlprompt-ui-parity/baseline.md` under "Final verification". Restore the developer's settings and history from the T003 backup if a scenario damaged them.

---

## Deferred

- Commands with no registered handler — Text to SQL, AI Optimize, AI Index Analysis, Generate CRUD Procedures, Find in Results Grid, and Split Table (`SplitTableCommand.Initialize` registers no handler for `CmdSplitTable`) · not placed in the AKML SQL menu (contracts/ui.md §2 rule); their VSCT buttons stay hidden by default · `doc/progress.md` (Spec 040 › Open)
- VSCT menu parent — the VSCT top-level menu is parented to `IDM_VS_MENU_BAR` (0x0081 is Edit) and stays invisible in SSMS 22; the visible AKML SQL menu is built at runtime from `AkmlMenuTable` · a VSCT-only fix needs the SSMS menu-bar group id · `doc/progress.md`
- Format SQL actions: AS keyword and column-alias style · SQL Prompt has them; no formatter support yet · `doc/progress.md`
- History settings "Record failed executions" and "Encrypt at rest" · hidden (US1, HIS-06) until they do something; saved values kept · `doc/progress.md`
- UI Automation reaches only the first row of each group in a grouped WPF list (SQL History, Format Styles list) when it walks the tree; rows are reachable through the list's ItemContainer pattern · a WPF navigation limit seen during verification; screen-reader impact unchecked · `doc/progress.md`
- `ConfigManager.Save` swallows transient I/O errors (a file briefly locked by a scanner), which makes `DisableRuleFixActionTests.Invoke_preserves_an_existing_severity_override` fail about one run in six · a retry on transient errors would close it · `doc/progress.md`

---

## Dependencies and execution order

### Phase dependencies

- **Phase 1 (Setup)** → **Phase 2 (Foundational)** → user stories.
- **US1 (Phase 3)**: needs Phase 2 only through T005 (not strictly). It can start right after Phase 1.
- **US2 (Phase 4)**: needs T005 (SqlPreviewView). Independent of US1.
- **US3 (Phase 5)**: needs T005. Independent of US1 and US2.
- **US4 (Phase 6)**: needs US3, which it builds on (the preview control in the editor, rows). It also touches `OptionsCommand.SaveAndNotify`, which US1 changes (T023, T036), so merge after US1.
- **US5 (Phase 7)**: needs US2 (open state, `RestorableEntryIds`, group actions) and T006 (HexBrush).
- **US6 (Phase 8)**: needs US1 (hidden rows, reset rewrite). T161 needs US5 T145 (the History rows exist before they are converted to number fields).
- **US7 (Phase 9)**: needs US4 (Active Style slots: T106, T107 and T110). It touches pages that US6 renamed, so merge after US6.
- **Polish (Phase 10)**: after every story it covers.

**Delivery rule** (FR-071): finish and verify **US1 + US2 + US3** before merging P2 work; finish **US4 + US5** before P3.

### Within each story

- Tests first (they must fail), then Core or DTO changes, then the engine, then the shell, then verification.
- These files are edited by several tasks in sequence. Never mark two tasks on the same file `[P]`:
  - `CompletionController.cs`: T034 → T035 → T039;
  - `SettingsWindow.cs`: T022 → T025 → T026 → T027 → T028 → T045 → T158 → T161 → T162 → T163 → T165 → T167 → T182 → T183;
  - `HistoryDatabase.cs`: T063 → T064 → T065 → T066 → T129;
  - `FormatStylesEditorWindow.cs`: T078 → T080 → T081 → T082 → T096 → T097 → T099 → T100 → T101 → T102 → T103 → T182 → T183 → T185 → T187;
  - `AkmlSqlPackage.cs`: T070 → T105 → T107 → T110 → T144 → T180.
- **Testing note:** some shell wiring can only be seen in a running SSMS: the VSCT buttons and groups (T106, T181), the menu placement in `EnsureTopLevelMenu` (T110, T180), the restore prompt (T143), the startup restore hook (T144), the startup open-tab check and shutdown flag (T070, quickstart 12), and sending the real editor session id for Format SQL actions (T190, quickstart 49). The decisions behind them are unit-tested (slots T089, menu table T173, restore rules T119). The wiring itself is verified by its quickstart scenarios and the screenshot tour (T193), which the constitution's Development Workflow makes the acceptance gate for "done".

## Parallel opportunities

- **Setup:** T002 and T003 in parallel.
- **Foundational:** write the preview tests (T004) first; then T005 (the control) and T006 in parallel.
- **US1:**
  - all US1 test tasks in parallel;
  - then T024, T030, T031, T038, T040, T041–T044 and T046 in parallel (different files);
  - the engine chain T030 → T032 → T033 runs alongside the shell chain T034 → T035 → T039.
- **US2:** all US2 test tasks in parallel except T054 (it edits an existing test file); T062 and T068 in parallel with the engine chain.
- **US3:** tests T075–T077 in parallel.
- **US4:** all US4 test tasks in parallel; T095, T104 and T106 in parallel.
- **US5:** all US5 test tasks in parallel; T126, T127 and T128 in parallel; the engine T129 → T130 alongside the settings and parser chain T131 → T132.
- **US6:** all US6 test tasks in parallel; T159 and T164 in parallel.
- **US7:** all US7 test tasks in parallel; T184 (docs) in parallel with any code task.
- **Across stories** (with separate developers): US1, US2 and US3 can proceed at the same time after Phase 2.

### Parallel example: User Story 2

```text
Together: T048 (Core DTO tests) · T049 (schema v3) · T050 (group actions) · T051 (open state) · T052 (snapshot search) · T053 (group filters) · T055 (paging) · T056 (session keys) · T057 (preview/actions) · T058 (reconcile request) · T059 (handler routing) · T060 (open-state decisions) · T061 (version guard)
Then:     T062 (DTO keys) ∥ T068 (DocumentSessionKeys)  →  T063 → T064 → T065 → T066 → T067 (engine)  ∥  T069 → T070 (capture/package)  →  T071 → T072 → T073 (UI)  →  T074
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
