# Research: SQL Prompt UI/UX parity for Options, SQL History and format styles

**Feature**: 040-sqlprompt-ui-parity | **Date**: 2026-09-27 | **Spec**: [spec.md](./spec.md)

Phase 0 of the plan. Five parallel code investigations covered:
- the Options window;
- the settings that do nothing;
- SQL History, in the shell and the engine;
- the style editor and the formatter;
- the menu, help, accessibility and tests.

They read `master` at `e75771b` on this branch. Each decision below states what was chosen,
why, and what else was considered. File references are relative to `src/` unless stated, and
line numbers are from 2026-09-27.

Findings that changed the plan compared with the gap plan (`doc/_Prompt-Gap/11-…md`):

| # | Finding | Effect |
|---|---|---|
| N1 | The AKML SQL menu users see in SSMS is the **DTE-built fallback** (`AkmlSqlPackage.EnsureTopLevelMenu`, `Ssms22/AkmlSqlPackage.cs:383-490`, 15 hard-coded items), not the VSCT menu. The VSCT parent `IDM_VS_MENU_BAR` is declared as 0x0081, which is `IDM_VS_MENU_EDIT`, and SSMS 22 doesn't expose `guidSHLMainMenu` (`doc/progress.md:578-583`). | X-01 and STY-07 must build their structure in the DTE menu (R24, R25) |
| N2 | **Session recovery is not wired in SSMS 22.** Nothing calls `SessionRecoveryInitializer.InitializeAsync`, `SessionAutoSave.OnShutdown` or `ExecutionCapture.Shutdown`, so the WinForms recovery dialog never appears. | HIS-14 becomes a history-based restore (R17) |
| N3 | **Deleting history leaves orphan versions.** `PRAGMA foreign_keys=ON` is set only on the init connection (`Engine/History/HistoryDatabase.cs:95`). | HIS-04 deletes versions explicitly (R14) |
| N4 | **The full-text index is corrupted by snapshots** (no `AFTER UPDATE` trigger), and `executed_at` is written in two formats. | HIS-05 adds a trigger plus a one-time repair migration (R15) |
| N5 | **The Theme drop-down handler fires during Load.** It runs on open with `theme:"system"` on a dark host, and on Restore all / Import / General reset. It saves half-loaded controls to disk: all AI agents are lost, and `Close()` runs before `ShowDialog()`. | OPT-02 is P1-critical (R2) |
| N6 | **F1 was never wired.** `F1HelpListener.Open` has no caller, and its URLs point at GitHub files that don't exist. The docs site has **no Options topic**. | X-03 wires F1 and adds a topic (R26); spec FR-062 updated |
| N7 | The desktop style editor **doesn't keep the parsed schema model**; each page rebuilds its rows. | STY-04/05 need a model-level index (R23) |
| N8 | Format-time actions are **per profile**. `ApplyLayout`, `ApplyCasing`, `ExpandWildcards`, `QualifyObjectNames` and `AddAsKeyword` are **never read** by the pipeline, AS is an AST stub, and no alias-style option exists. `FormatRequest.IncludeActions` (Key 4) exists but is unread. | STY-11 lists only real actions (R29); spec FR-065 updated |
| N9 | Several **more** Options rows have no reader: Trigger after dot, Enable snippets, Format after expansion, Enable SQL formatter, Respect --noformat, Format after refactoring, Find All References / Object Search / Document Outline toggles, Analyze while typing / on save. | Handled under FR-001's "found while building" clause (R1) |
| N10 | The row menu's **Delete acts on `SelectedEntries`**, which can be other rows, while star and ⋯ set only `SelectedEntry` (`History/HistoryViewModel.cs:736`). | HIS-12 fix (R21) |

---

## Options

### R1 — Settings that do nothing: wire seven (plus cheap extras), hide the rest (OPT-01, FR-001–003)

**Decision**

| Setting | Wiring | Where |
|---|---|---|
| **MaxSuggestions** | Set per request: `_engine.SetMaxSuggestions(Math.Clamp(v, 5, 200))`, after `IntelliSense/…/CompletionHandler.cs:75` | Engine |
| **FuzzyMatch** | When off, match case-insensitive prefix only (score ≥ 800), on both sides:<br>- **Engine:** `CompletionEngine.FuzzyMatchEnabled` gates `CompletionEngine.cs:506`.<br>- **Shell:** `AkmlCompletionPopup` gets a latched `PrefixOnly` flag (set like `CtrlTransparencyEnabled` in `LatchPopupSettings`, `Editor/Completion/CompletionController.cs:771-776`).<br>- `CompletionItemModel` must carry `FilterText` (today dropped at `:879-887`) so `alias.Column` / `dbo.Table` items keep matching. | Both |
| **ShowDataTypes, ShowNullability, ShowPkFk** | `ColumnProvider.FormatSecondaryText` (`IntelliSense/Completion/Providers/ColumnProvider.cs:557-581`) becomes an instance method gated by three provider flags, set from `CompletionHandler` → `CompletionEngine` (next to `ColumnScopeMode`, `CompletionEngine.cs:447-457`).<br>- IDENTITY and COMPUTED follow **ShowPkFk**.<br>- The table-level "🔑 FK ↔" text (`ObjectProvider.cs:629-633`) follows ShowPkFk.<br>- When every part is off, drop the leading " • ".<br>- **No** new "FK" column badge: it would change the default output. | Engine |
| **TriggerDelayMs** | Route `AutoTriggerCompletion()` (`CompletionController.cs:724-728`) through the existing but unused `TriggerCompletionDebounced()` (`:752-763`), using the setting instead of the fixed 150 ms.<br>- 0 means immediate.<br>- Ctrl+Space stays immediate.<br>- Cancel the timer in `DismissPopup`, on commit and on Esc. | Shell |
| **ShowInErrorList** | Gate inside `Analysis/ErrorListReporter.RefreshTaskList` (`:48-80`): when off, clear the tasks and return. On Options OK, re-apply to live reporters, using a static registry of reporters and `AnalysisController.CurrentIssues`. | Shell |

**Extras found while building** (FR-001 clause). Wired because each is about half a day or less:
- **Trigger after dot:** gate `CompletionController.cs:427`.
- **Format after expansion:** replace hard-coded `FormatOnExpand = true` (`:1129/1179`).
- **Enable SQL formatter:** gate Format Document / Selection / Bulk in `FormatActionHelper`.
- **Enable snippets:** gate snippet expansion and snippet items in completions.
- **Analyze while typing:** gate `AnalysisController`'s edit trigger (`:48-52`).
- **Create backups** (Format): seed the Bulk Format wizard checkbox (`Ui/BulkFormatWizard.cs:138-144`).

**Hidden** (each row, with its `RegisterSearch` call, removed from its page; its property kept):
- every Appendix A row;
- Format after refactoring;
- the Find All References / Object Search / Document Outline / Go to Definition / Peek toggles;
- Analyze on save;
- Refactoring › Create backups;
- Format › Respect --noformat regions (it affects only Bulk Format, where the engine always respects the regions, `Engine/Formatter/FormatRequestHandler.cs:500`; wiring it needs a new request field, so it is hidden and behaviour stays "always respect");
- the duplicate Snippets › Show in completions;
- Labs › Ghost text.

**Consequences of hiding**
- Pages that end up empty leave the tree: **Suggestions › Database** (all three rows dead) and **Labs** (all three dead).
- Snippets › Team folder gets "Takes effect after SSMS restarts" (read at engine start, `Engine/EngineHandlerRegistry.cs:61`).

**Guard.** A reflection test enumerates every registered search row and matches it against an allow-list of `(pageKey, label) → wiring test name`. A row missing from the allow-list fails the build's test run.

**Behaviour change to accept.** TriggerDelayMs is saved as 100 in existing configs. Honouring it delays the auto popup by 100 ms after the last keystroke; Ctrl+Space is unaffected. Users who want the old instant popup set 0. The label already promises a delay.

**Rationale**
- The user chose "wire the cheap ones, hide the rest" (Clarification Q1).
- Every wiring above reuses an existing seam: the settings snapshot, `InvalidateSettings`, the popup latch, the debounce method, or the reporter.
- Defaults reproduce today's output, so the completion corpus (1,342 cases, ~97.5 %) is unaffected.

**Alternatives considered**
- Wire everything: weeks of work (format-on-paste/save, encryption, refactor preview).
- A generic "disabled / coming soon" row state: rejected by the user.

### R2 — Theme drop-down: never save, never fire during Load (OPT-02, FR-004)

**Decision**
1. Add `internal static PageTheme ResolvePageTheme(string? pref)` ("dark" → Dark, "system" → host variant through a test seam, else Light). Use it in the constructor (`Dialogs/SettingsWindow.cs:183-184`) and in the handler.
2. Add a `_loadingControls` flag, set with try/finally around `LoadSettingsToControls` (`:1953-1974`). That one spot covers open, Import, Reset page and Restore all.
3. Inside the handler, resolve the pick. If the page theme is unchanged, return. Otherwise:
   - run `SaveControlsToSettings()` **into memory only** (drop `ConfigManager.Save`);
   - call `ThemeRegistry.SetPreference(pick)` as a live preview;
   - set `ThemeChangeRequested = true` and close.
4. Expose `WorkingCopy` and `CurrentPageKey`.
5. Change `OptionsCommand.ShowOptions` (`Commands/OptionsCommand.cs:52-92`):
   - On `ThemeChangeRequested`: `settings = w.WorkingCopy; pageKey = w.CurrentPageKey; continue`.
   - On Cancel: restore the original preference.
   - On OK: apply `ThemeRegistry.SetPreference(settings.Theme)`.
6. Extract `ResetAllToDefaultsCore()` from `OnResetAllClick` so tests don't hit the MessageBox.

**Rationale**
- This is the smallest change that makes Cancel mean Cancel. It fixes the worst case (N5: opening Options with "system" on a dark host wipes AI agents).
- The existing reopen loop is kept.

**Alternatives considered**
- Attach the handler after the first Load only: Import and Reset reload, so it still fires.
- Attach Options to ThemeRegistry live, with no reopen: bigger change, and `PageTheme` has no High Contrast palette.

### R3 — Restore Defaults: reset through each page's own Save (OPT-03, FR-005–007)

**Decision**

`ResetPageToDefaultsCore(key)` becomes:
1. `SaveControlsToSettings()`, to keep unsaved edits on other pages.
2. `controls.Reset(new AppSettings())` (which is `Load(defaults)`), with theme events suppressed.
3. `controls.Save(_settings)`.

Each page's `Save` writes exactly the fields that page shows, so hidden fields survive:
- rule overrides;
- connection aliases;
- environment severities;
- the fields listed in the agent report.

Per page:

| Page | On reset |
|---|---|
| Format › Styles | Shows the active style, so resetting it resets the active style. This is correct under FR-005. |
| AI Assistance | Its Save empties the agent list. The confirmation lists what goes: "This also removes your N AI agents and their API keys." |
| Queries › Color | Rules and environments are shown there, so reset restores the default environments and rules. |
| Restore all | Uses a `PreserveInstallState(from, to)` helper shared with Import, keeping InstallId, InstalledTargets, LastUpdateCheck, NativeIntelliSensePrompted, DisabledNativeIntelliSense, CommandPalette usage/recents and ConfigVersion. |

- The confirmation uses `IPageBuilder.Display` (e.g. "Suggestions › Tooltips"), not the page key.
- Remove the reload-all-pages call in `OnResetThisPageClick`, which also threw away unsaved edits on other pages.
- Tests:
  - Sentinel values in RuleOverrides, Ai.Agents, ConnectionAliases, EnvironmentSeverity and ActiveProfile survive every page reset (except the page that shows them).
  - `WindowChromeTests.ResetPageToDefaultsCore_HasCaseForEveryRegisteredPageKey` is rewritten; the switch disappears.

**Rationale**
- Reuses code every page already has (`IPageControls.Reset`, which the host never called).
- Deletes the 80-line reset switch and its drift risk.

**Alternatives considered**
- A per-page field list: duplicates what `Save` already knows.
- A typed binding per row: the cleanest long-term, but it rewrites all 25 pages.

### R4 — Options tree: rename labels, keep page keys (OPT-04, FR-050)

**Decision**
- Build the tree in FR-050's order.
- Page keys (the internal `Tag` strings) **stay the same**, because deep links, tests and `_pageBuilders` keys use them.
- The tree label and `IPageBuilder.Display` change; `Title` becomes `Display`'s last segment.
- Moves:
  - Snippets page → under Suggestions.
  - Safety page → "Warnings & highlighting" under Suggestions.
  - "INSERT statements" → "Objects & statements".
  - Navigation → top level.
  - Color (key `"Tabs & UI"`) → under Queries.
  - Restore-on-start rows move from the Tabs page to the History page (R17).
  - Schema-cache rows still shown → Connections & Memory.
  - Suggestions › Database and Labs leave the tree (R1).
- Sentence case everywhere ("Inserted code", "Warnings & highlighting").
- Update `OptionsNavStructureTests` (it pins "Inserted Code" and the Suggestions/JoinOptions placement) and `WindowChromeTests`.
- Odd labels:
  - "Tables Alias" → "Suggest table aliases";
  - "Temperature (x10)" → "Creativity (temperature)";
  - remove "Phase A/B", "Future-pending" and the stale linked-server "currently has no effect".
- Page headers keep the "?" button; its help text moves to plain language.

**Rationale**
- The user chose SQL Prompt's arrangement (Q2).
- Keeping keys makes the move a label change, not a data change.

**Alternatives considered**
- Renaming keys: breaks `ShowOptions("AI Assistance")` callers and tests.

### R5 — Child options: parent gating in RowFactory (OPT-05, FR-051)

**Decision**
- Add an optional `parent` argument (a `CheckBox`) to RowFactory's row methods.
- Children get +20 px indent, and `IsEnabled` is bound to `parent.IsChecked`.
- The label foreground swaps to a new `PageTheme.TextDisabled` brush (from `ThemeTokens.TextDisabled`).
- A tooltip reads "Takes effect when ‹parent› is on" — the pattern the style editor already uses in `RefreshIfGate` (`Formatting/FormatStylesEditorWindow.cs:1981-1991`).

**Parents**

| Page | Master | Children |
|---|---|---|
| Suggestions › Behavior | Enable IntelliSense | all rows |
| Queries › History | Enable SQL history recording | all rows |
| Warnings & highlighting | Enable transaction reminder | Reminder interval |
| Queries › Color | Enable tab coloring | Gradient, grid |
| Code Analysis | Enable code analysis | all rows |
| Snippets | Enable snippets | all rows |
| Special characters | Automatically insert closing characters | the five characters |
| Format › Styles | Enable SQL formatter | all rows |

There is no "Show execution warnings" master today, and none is added.

**Rationale**
- SQL Prompt's pattern.
- The style editor already proves the idiom in this codebase.

**Alternatives considered**
- Hiding children instead of greying them: SQL Prompt greys them.

### R6 — Number fields for wide ranges (OPT-06, FR-052)

**Decision**
- Add `RowFactory.AddNumber(label, min, max, step, unit, description)`: a text box, ▲▼ buttons and a unit label, with validation like the style editor's integer box (red border, last valid value kept).
- Replace these sliders (every range > 100 values):

  | Page | Setting | Range |
  |---|---|---|
  | Suggestions › Behavior | Maximum suggestions | 5–200 |
  | Suggestions › Behavior | Trigger delay | 0–2000 ms |
  | Queries › History | Retention | 1–3650 days |
  | Queries › History | Max entries | 1,000–1,000,000 |
  | AI Assistance | Max response tokens | 128–128,000 |
  | AI Assistance | Timeout | 5–300 s |
  | Queries › Execution | Notification threshold | 5–300 s |
  | Queries › History | Auto-save interval (moved there by R17) | 30–300 s |
  | Warnings & highlighting | Reminder interval | 30–3600 s |

  (Schema refresh interval is hidden by R1.)
- Units come out of the label text into the unit label.

**Rationale**
- FR-052's rule applies to Maximum suggestions too; the gap plan's "keep as slider" is overridden.

**Alternatives considered**
- Slider plus text box: twice the controls, no benefit.

### R7 — Options in the Command Palette (OPT-07, FR-053)

**Decision**
- Add a static `SettingsWindow.BuildOptionsCatalog(AppSettings)`. It runs `BuildPages` on a throwaway instance on the UI thread and returns `(PageKey, PageDisplay, Label, Description, Kind, Row)` from the existing search index.
- It is cached per session and listed under a new **Options** category once the query is at least 2 characters.
- **Toggle** rows (Option A from research):
  1. `s = ConfigManager.Load()`
  2. `controls.Load(s)`
  3. flip the CheckBox (`((Border)row).Child`)
  4. `controls.Save(s)`
  5. `OptionsCommand.SaveAndNotify(s)`
  6. The palette stays open and shows the new On/Off state.
- The AI Assistance page is excluded (its rows edit the selected agent), as are Info and Button rows.
- Other kinds: close the palette and call a new `ShowOptions(pageKey, agentId, focusLabel)` **overload**. The `Func<string?,string?,bool>` assignment at `Ai/AiChatPanel.cs:135` must keep compiling. The overload reuses the search commit's scroll-and-flash code.
- `FlashRow` is extended to any `Panel`, so slider, dropdown and text rows flash too.
- Model: an `OptionPaletteEntry` (a `CommandEntry` subclass with `INotifyPropertyChanged` IsOn/StateText), an `opt:` id prefix, and an item template selector.

**Rationale**
- Reuses the Options pages as the single source of truth, so no second per-setting binding table.

**Alternatives considered**
- Typed bindings per setting: cleaner, but it rewrites every page (see R3).

### R8 — Tab colour environments (OPT-08, FR-054)

**Decision**
- Add `TabSettings.Environments: List<TabEnvironment{Name, Color}>` and `ColoringRule.Environment` (name).
- **One-time migration in `ConfigManager.Load`:** build environments from the distinct `(Label, Color)` pairs of the existing rules. The defaults are PRODUCTION #FF4444, STAGING #FFB800, DEV #44BB44 and AZURE #4488FF.
- Rules keep filling `Label` and `Color` from their environment on save, because Safety keys on Label: the PROD check at `Safety/ExecutionInterceptor.cs:257-262`, and `EnvironmentSeverity`.

**UI**
- The Color page gets a themed grid:
  - Columns: Server / group pattern · Database · Environment (swatch + dropdown).
  - "+ Add server/database", ↑↓ reorder (renumbers `Order` and fixes duplicate orders), Remove, and the hint "You can use wildcards (*)".
- **Edit environments…** opens a themed WPF dialog:
  - Name · Colour, using the WPF colour grid of the 8 TabColor tokens plus "Custom…" with the WinForms `ColorDialog`.
  - "Use gradient colors" and "Restore default environments".
- `MatchTarget`/`DatabaseName` become editable.
- `EnvironmentMatcher.Match` gains "server AND database" for server rules with a non-empty DatabaseName. The existing tests pin that server rules ignore DatabaseName when it's empty.

**Rationale**
- SQL Prompt's model.
- Keeping Label/Color filled means Safety needs no change.

**Alternatives considered**
- Replacing Label with an environment reference everywhere: touches Safety, which is out of scope.

### R9 — Theme every Options sub-window (OPT-09, FR-055)

**Decision**
- Add `ThemedButton.ApplySecondary(Button, PageTheme)` / `ApplyPrimary(...)`, cached per PageTheme like `ComboBoxTheming`. Use them in `SettingsWindow.MakeButton`/`MakePrimaryButton`, `RowFactory.AddButton`, `AiAgentListView.MakeButton` and the Color page.
- Port `Analysis/ManageRulesDialog.cs` from WinForms to a WPF `ThemeAwareWindow` ("AKML SQL – Code analysis rules"):
  - a grouped checklist by category (rule id, name, severity combo, fix ✓);
  - a description pane for the selected rule;
  - "Settings file:" showing `Constants.ConfigFilePath`;
  - the session-suppressed strip;
  - Save / Cancel.

  Inputs and outputs (`GetOverrides`, `RestoreSessionSuppressions`) are unchanged.
- Add a "Manage rules…" button on the Code Analysis page. After it closes, the page reloads `RuleOverrides` from disk (the precedent is `FormattingPage.RefreshActiveStyleFromDisk`).
- The rule editor becomes the themed Color grid (R8).
- The search badges use theme brushes.

**Rationale**
- These are the windows the review found light in Dark.
- `ThemeAwareWindow` is the codebase's standard.

**Alternatives considered**
- Attaching Options to ThemeRegistry: risks mixed colours (no High Contrast `PageTheme`).

---

## SQL History

### R10 — Full query in the preview (HIS-01, FR-010)

**Decision**
- Add a view-model `GetPreviewTextAsync(entry or version)`:
  - calls the existing `GetFullSql` action;
  - caches per entry id, cleared on refresh;
  - does **not** toggle `IsLoading` (today's `GetFullSqlAsync` blocks scrolling).
- The list keeps the 500-character snippet.
- Very large texts (over 256 KB) show the first 256 KB with "Showing the first 256 KB — Open to see all", so the UI thread stays responsive (spec edge case).

**Alternatives considered**
- Removing the `substr` from the search query: it moves up to 1 MB per row over IPC for every page.

### R11 — Open / closed state with an owner (HIS-02, FR-011–012)

**Decision**

**Schema v3**
- Add `history.open_pid INTEGER NULL`, the shell process that has the query open.
- `is_open` stays.
- Web rows never set either column, so web behaviour is unchanged.

**IPC**
- `HistoryActionRequest [Key(10)] string? SessionKey` and `[Key(11)] int? OwnerPid`.
- `SetOpenStatus` works by session key (every row of the session).
- New action code **11 `ReconcileOpen`** (OwnerPid, open session keys): closes rows owned by this PID that are not in the list, and rows whose owner process isn't running.

**Shell**
- **Execute:** switch the history record to `SendRequestAsync` (RequestId ≠ 0; the handler already responds). Once it returns, send `SetOpenStatus(true, key, pid)`.
- **Activate:** the document that gained focus, if it has a key.
- **Close:** add `DocumentSessionKeys.TryGet` and call it *before* `Forget` (`History/ExecutionCapture.cs:311`); send `SetOpenStatus(false)`.
- **Startup:** after the engine connects, send `ReconcileOpen(pid, keys of open documents)`.
- **Package shutdown:** set a "shutting down" flag, so shutdown's document closes don't mark rows closed (R17).
- **Opened from History:** the new document adopts the entry's session key. This needs `HistoryEntryDto [Key(17)] SessionKey` and `DocumentSessionKeys.Adopt`.

**Filters**
- The open/closed and favourite filters move to the outer, grouped level: a `WHERE` on the group's `MAX()` values, counted over the grouped subquery.
- UI: open rows get a 3 px accent bar. The red/green dot is removed.

**Rationale**
- The shared DB (web engine plus one engine per SSMS instance, auto-restarting) makes engine-side "close all at startup" unsafe (research §4). The owner PID solves multi-instance and crashes.

**Alternatives considered**
- Engine marks open on insert: web rows would stay open forever.
- Mark all closed at startup: clears other instances' open tabs.

### R12 — Paging to the end (HIS-03, FR-013)

**Decision**
- `HasMoreEntries => _lastPageCount == PageSize && Entries.Count < TotalCount`.
- The engine offset stays in pre-filter row space, because of the CamelCase in-memory filter.
- The engine's grouped `TotalCount` is already correct, so this is shell-only.

### R13 — Search scope and debounce (HIS-07, FR-040)

**Decision**

**Engine:** replace the FTS join with:
```sql
(h.id IN (SELECT rowid FROM history_fts WHERE history_fts MATCH @s)
 OR COALESCE(qs.name, h.tab_title, '') LIKE @l
 OR h.source LIKE @l
 OR h.server LIKE @l
 OR h.database_name LIKE @l)
```
- The count query gains the same `LEFT JOIN query_sessions`.
- LIKE terms are built from `Core/Text/HistorySearchTerms.Extract`.
- The fallback's clause matcher (`HistoryDatabase.cs:951`) is updated.
- The parser gains `path:` and `date:[yyyyMMdd TO yyyyMMdd]`. `type:` is not needed: there is only one object type.

**Shell**
- 250 ms debounce on text changes; Enter searches at once.
- The first result is selected after a search.
- Placeholder "Search".
- A "?" popup lists the syntax the parser supports, generated from one table the parser tests also use.

**Alternatives considered**
- Adding FTS columns: dropping and recreating the FTS table and triggers breaks older engines sharing the file.

### R14 — Group-scoped row actions (HIS-04, FR-014)

**Decision**
- `HistoryActionRequest [Key(9)] bool? GroupScope`, for Delete, ToggleFavorite and GetVersions. The engine resolves the group from `EntryIds[0]` with the GroupKey expression.

| Action | With `GroupScope` |
|---|---|
| **Delete** | One transaction: delete the group's `history_versions` rows **explicitly** (the cascade isn't active, F3), then the `history` rows (FTS stays in sync through `history_ad`), then the `query_sessions` row. |
| **Favourite** | `is_favorite = 1 - MAX(group)` on all rows. The response gains `[Key(8)] bool? IsFavorite`. |
| **Versions** | The panel and the row's "M versions" use one definition: the **distinct texts** of the group's runs and snapshots, by time. `GetVersions` returns runs and snapshots merged and de-duplicated by content hash. |

- The v3 migration deletes orphan `history_versions`.
- The engine now sets `DeletedCount`, which fixes the web's always-0 result as a side effect of the same handler.
- The web keeps its per-id behaviour: `GroupScope` null means today's semantics (FR-070).
- The desktop always sends `GroupScope = true` for grouped rows.

**Alternatives considered**
- New action codes: fail loudly on older engines, but double the handler surface.
- The chosen additive key degrades to per-row on an old engine. That is acceptable, because the shell and engine ship together.

### R15 — Snapshots keep search true (HIS-05, FR-015)

**Decision**
- Keep `SaveVersion` updating the row's `sql_text`, so search finds the current text.
- Write `executed_at` in the insert's ISO "o" format.
- Update `content_hash`.
- Add the missing trigger: `history_au AFTER UPDATE OF sql_text` (delete the old text, insert the new).
- **v3 migration**, guarded by metadata flag `history_v3`, under `BEGIN IMMEDIATE`, idempotent:
  - `INSERT INTO history_fts(history_fts) VALUES('rebuild')`;
  - normalise every space-format `executed_at` / `saved_at` to ISO;
  - add `open_pid`;
  - delete orphan versions.
- `HistoryVersionSnapshotBySourceTests` (which pins the space format) is updated.

**Rationale**
- FR-015 requires finding the current text. The trigger plus rebuild fixes existing corrupt indexes on upgrade.

**Alternatives considered**
- Snapshots only in `history_versions`: search would miss the current text.

### R16 — History settings honesty (HIS-06, FR-016)

**Decision**
- Hide Encrypt at rest (whole-file encryption can't coexist with WAL mode and two engines) and Record failed executions (SSMS's after-execute event exposes no status).
- Rename deduplication to "Group repeated runs of the same query".
- Add "Takes effect after SSMS restarts" to Enable, Retention, Max entries and Disable trim.
- Add **Maximum query size** (SQL Prompt has it): 1 MB is today's hard-coded capture limit, and the setting honours values up to that.
- Add "Remember advanced search settings" (R18).

### R17 — History-based restore instead of the unwired session recovery (HIS-14, FR-047)

**Decision**

Replace the never-wired `SessionAutoSave` / `SessionRecoveryInitializer` / WinForms `SessionRecoveryDialog` path with SQL Prompt's model, built on history:

| Piece | Behaviour |
|---|---|
| **Draft capture** | On document close, and every `AutoSaveInterval` seconds for dirty open query documents (the existing setting, moved to the History page), unsaved text is recorded:<br>- documents that have a history session get a version snapshot;<br>- documents that don't get a **draft** entry: `HistoryRecordRequest [Key(12)] bool IsDraft`, stored with new status `NotExecuted`.<br>Drafts show "Not executed" and aren't counted as runs. |
| **Shutdown** | A package shutdown hook sets "shutting down", so shutdown's closes keep `is_open`/`open_pid`. A crash leaves them set as well. |
| **Startup restore** | After `ReconcileOpen`, the queries still owned by a dead PID are the ones open at last exit. What happens depends on `RestoreOnStartup`:<br>- **Always:** reopen up to "Maximum number of queries to restore" (default 20), reconnecting each when "Automatically reconnect restored queries" is on (server and database are stored; reuse `OpenInNewTab`'s connection code).<br>- **Prompt:** a themed WPF prompt lists them with checkboxes.<br>- **Never:** do nothing. |
| **Closed queries** | Drafts and closed runs appear under the Closed filter (HIS-02), so a closed, never-saved tab is findable after restart.<br>- Ctrl+Shift+T keeps its fast in-memory stack.<br>- When the stack is empty, it opens the most recently closed history entry. |
| **Old path** | The unwired classes (`Sessions/SessionAutoSave.cs`, `SessionRecoveryInitializer`, `SessionRecoveryDialog.cs`) are deleted, together with the engine's session storage handler if nothing else calls it (check web usage first). |

**Rationale**
- Reuses the history store instead of reviving a parallel, never-shipped mechanism (Constitution V).
- It matches SQL Prompt's behaviour.

**Alternatives considered**
- Wiring the old JSON session files: a second store for the same text, and a WinForms dialog to restyle.

### R18 — Advanced search panel (HIS-08, FR-041)

**Decision**
- New action code **12 `GetFilterValues`**, exposing `GetDistinctServersAsync` / `GetDistinctDatabasesAsync` (`HistoryDatabase.cs:1141-1176`). The response gains `[Key(9)] string[]? Servers`, `[Key(10)] string[]? Databases`.
- A collapsible panel: Period (Everything / Last week / Last month / Last 3 months / Custom with two `DatePicker`s) → `DateFrom`/`DateTo`; Server ▾; Database ▾; State (Starred, Open); Reset.
- Active filters show as removable chips under the box.
- `History.RememberAdvancedSearch` (default false) persists the panel's state in config.
- Date comparisons use `datetime()` (after the R15 normalisation, a plain ISO string compare is also correct).

### R19 — Rows, date groups, environment (HIS-09, FR-042)

**Decision**
- Row line 2, right: "server · database", plus the environment name in its colour. The environment comes from `Core/Models/Tabs/EnvironmentMatcher.Match(rules, server, db)` over rules from settings, **not** `EnvironmentDetector`, which only loads rules when colouring was on at start-up.
- The hex → brush helper is extracted from `TabColoringManager` into a shared `Tabs/HexBrush` helper.
- Date groups come from a **new** `HistoryDateGroups.For(now, t)`: Today / Yesterday / This week / Last week / This month / Older.
  - `HistoryDateBucket.Of` is unchanged, because the web and `WebHistoryLogicTests` pin it.
  - Group headers show counts.
- Open bar: see R11.

### R20 — Versions, diff, preview and keyboard (HIS-10, HIS-11, FR-043–044)

**Decision**

**One shared `SqlPreviewView`** (a new shell control, `Ui/SqlPreview/`), used by both History and the style editor:
- read-only, selectable text: a `RichTextBox` with a FlowDocument, `IsReadOnly`, `IsDocumentEnabled`;
- tokens from `SqlPreviewTokenizer`, coloured through theme tokens: keyword AccentPrimary, string StatusSuccess, comment TextSecondary;
- a line-number gutter;
- optional search-term highlight (History) and line highlight (the style editor's moved lines);
- tab expansion to a given width;
- Copy / Copy all.

**Versions**
- The selected version (or the entry) drives Open, Copy and Re-execute.
- A `LoadVersionHistory` stale-response guard replaces `async void`: sequence check and cancellation.
- Version rows get a page glyph and "server · environment".

**Compare**
- "Compare with current" on a version, and the existing two-row Compare.
- Both go through an upgraded `HistoryDiffWindow`: headers name each side and its time, and a line diff (new `Core/Text/LineDiff.cs`, LCS over lines, unit-tested) highlights added, removed and changed lines.

**Keyboard**
- Enter open · Delete remove (confirm) · F2 rename · Ctrl+C copy SQL · Space star · Tab / Shift+Tab list ↔ preview.
- Star and ⋯ become focusable buttons with `AutomationProperties.Name`.

**Rationale**
- One preview component serves HIS-01/11 and STY-02/05/06, so there is no second renderer.

### R21 — Row menu and live refresh (HIS-12, HIS-13, FR-045–046)

**Decision**

**Row menu**
- Row actions act on the **clicked row**. This fixes N10: ⋯ and star capture the row, and don't use `SelectedEntries`.
- Hover-only ⋯ with SQL Prompt's wording, plus AKML's extras.
- Delete and "Remove older" confirm with readable dates (`HistoryTimeFormat.Absolute`).
- A toolbar ⋯ holds **Export…** and a new **Clear history…** (the existing DeleteAll action, non-favourites only, stated in the confirmation).
- Re-execute reuses Open's `ScriptFactory` connection code. When that can't connect (SQL auth), it opens the text and asks the user to connect.

**Live refresh**
- New static event `ExecutionCapture.HistoryRecorded`, raised after the awaited record (R11).
- The view model re-queries on the UI thread, keeping the selected id and scroll offset.
- Star and delete also keep them.
- A spinner (the SchemaProgressMargin ellipse pattern) replaces "Loading…".
- Disconnected: an overlay with **Retry**, plus a 5-second reconnect probe while it's shown. No new engine event is needed.

---

## Format styles

### R22 — Readable rows, tab-true preview, honest import/export (STY-01–03, FR-020–023)

**Decision**

**Rows (STY-01)**
- On/off options: `CheckBox.Content` = a wrapping TextBlock spanning both columns. That TextBlock is passed to `GatedRow.Label`, so gating keeps working.
- Other kinds: label column `Auto` with `SharedSizeGroup="lbl"` on the host (`Grid.IsSharedSizeScope`), a 200 px minimum and a 45 % maximum, with wrapping. The control column keeps its 280 px cap.
- The tooltip includes the full label.
- Notes show inline in grey under the row (FR-036).

**Preview (STY-02)**
- The view model expands tabs column-aware before display, using `sqlPrompt.whitespace.numberOfSpacesInTabs` or AKML's `whitespace.tabSize`.
- The formatted text is untouched.
- Rendered by `SqlPreviewView` (R20). The TextBox stays only for "Edit sample" mode.

**Import (STY-03)**
- Before Import: if there are unsaved edits, ask first. Today the prompt comes *after* the import has already become active.
- After Import: the same sequence as set-active (`FormatStylesEditorWindow.cs:1119-1145`): `RefreshProfilesAsync` → `RestoreListSelection` → set-active button sync → `UpdateHeaderState` → status bar.

**Export (STY-03)**
- If `IsDirty && name == LoadedProfileName`, prompt "Save changes to 'X' before exporting?" (Yes / No / Cancel).

### R23 — Search, change markers, coloured preview (STY-04–06, FR-030–033)

**Decision**

**Search (STY-04)**
- Keep the parsed `FormatStylesSchemaModel` and a groupId → leaf map (today a local at `:1812`).
- The search box indexes DisplayName, Description, Note, Subgroup, EnumLabels and the option id. It filters tree leaves, adds a count badge per leaf, and highlights matching rows on page render.
- Enter jumps to the first match.
- Esc is handled in the box first (it clears and sets `e.Handled`), because the Close button has `IsCancel`.

**Change markers (STY-05)** — ported from `Web/Pages/Styles.razor`, read-only
- An option is changed when `value != catalog Default` (SQL Prompt's default, as in the web editor; spec FR-031 updated).
- Changed options: bold label and a ↺ that resets to the default.
- Tree badge: the per-page count.
- Moved-line highlight: a positional compare of the new preview against the previous one, only after option edits (a `markChanges` flag captured with the preview sequence), tinted with SurfaceSelection for about 2 s.
- `IsDirty` is recomputed against the saved values, as the web editor does, instead of being sticky.

**Preview colours (STY-06)**
- `SqlPreviewView` with theme tokens. The fixed `#1E2230` card goes (decision 4).

**Alternatives considered**
- Comparing against a customised built-in's shipped values: needs a new IPC flag, and the built-ins are AKML-model projections.

### R24 — Active Style menu without VSCT dynamic items (STY-07, FR-034–035)

**Decision**

**Command slots**
- Reserve 30 command ids (`0x0920`–`0x093D`, inside the free reserved range; `PackageGuids.cs`).
- Each slot is an `OleMenuCommand` whose `BeforeQueryStatus` sets `Text` to the slot's style name, `Checked` on the active style, and `Visible` only when a style exists for it.
- Plus **Edit Styles…** (`0x093E`).

**Style list**
- A shell `ActiveStyleCache` fetches the list asynchronously through ProfileList IPC, because `BeforeQueryStatus` can't wait on IPC.
- It refreshes at package load, after the Format Styles window closes, after Options OK, and in the background on every menu query.

**Menu placement**
- **AKML SQL menu:** an "Active Style" popup inside the DTE-built menu (R25), with the slots added through `Commands.Item(guid, id).AddControl`.
- **Editor context menu:** the same popup added to SSMS's query-editor context command bar. Find the bar at runtime by name; the candidate names are to be confirmed during implementation with the UiTests harness. If no bar can be found, log it and skip; the menu entry still satisfies SC-011.

**Choosing a style**
- It writes `Formatter.ActiveProfile` (`ConfigManager` round-trip).
- It calls `StatusBarManager.SetActiveProfile`, gated on "Show active style in status bar". The Format Styles window's call (`:1377`) is gated too.
- `OptionsCommand.SaveAndNotify` also refreshes the status bar.

**Rationale**
- The DTE menu is what users see (N1), and DTE popups can't host VSCT `DynamicItemStart`.

**Alternatives considered**
- VSCT `DynamicItemStart`: invisible in SSMS 22 (N1).

### R25 — Organise the AKML SQL menu (X-01, FR-060)

**Decision**
- Rebuild `EnsureTopLevelMenu` from a single declarative table: groups → items, with `BeginGroup` separators and nested `msoControlPopup` submenus for Refactor ▸, Navigate ▸, AI ▸ and Help ▸, plus Active Style ▸ (R24).
- It also covers the commands missing from today's 15-item fallback.
- The existing "menu already exists" check changes to a **version marker**: the popup's `Tag`, e.g. `"akml-menu-v2"`. An old fallback menu is removed and rebuilt once.
- The VSCT keeps its buttons (they define the commands and key bindings). Its groups mirror the same structure, so VS hosts that do show it match.
- Command ids and canonical names are unchanged; the palette, editor toolbar and `ExecuteCommand("AKML_SQL.FormatDocument")` depend on them.
- Commands with no registered handler are **not** placed in the menu: TextToSql, AiOptimize, AiIndexAnalysis, CrudGeneration, GridFind. This follows FR-001's spirit. They are recorded as follow-ups.

**Rationale**
- Change what users actually see (N1).

**Alternatives considered**
- Fixing the VSCT parent: a previous attempt failed silently (`doc/progress.md:583`). It remains a follow-up, not part of this feature.

### R26 — F1 help (X-03, FR-062)

**Decision**
- A shared `HelpBinding.Attach(element, Func<string> topicKey)`: a `CommandBinding(ApplicationCommands.Help)` whose handler calls `F1HelpListener.Open(key)`.

| Surface | How F1 is caught | Topic |
|---|---|---|
| Options window | The window's `KeyDown` already handles keys | The current page's `IPageBuilder.HelpTopic` (new member) |
| Format Styles window | `DialogWindow`: `HasHelpButton = true` with `InvokeDialogHelp` override, plus the binding | `topics/formatting#edit-styles-with-live-preview` |
| SQL History | The tool window pane handles `VSStd97CmdID.F1Help` in its command target, because VS turns F1 into Help.F1Help before WPF sees it | `topics/sql-history` |

- `F1HelpRegistrations.DocBase` becomes `https://akml.khamis.work/docs/`.
- Broken GitHub targets are remapped to existing slugs, or removed.
- New doc **`doc/topics/options.md`**: one section per Options page, with anchors matching page keys. The site ingests it automatically (spec 034).
- A site test (the `FooterDocLinksTests` pattern) checks that every F1 slug and anchor exists in `DocsCatalog`.

### R27 — Titles, accessibility, type (X-02, X-04, FR-061, FR-063)

**Decision**

**Titles (X-02)**
- A `WindowTitles.For(string name)` helper returns "AKML SQL – " + name (en dash).
- It is applied to every window and form title listed in the research (≈40, WPF and WinForms). MessageBox captions keep `Constants.ProductName`, apart from the few with no product name, which get it.
- Window icon: a shared helper sets the AKML icon from a new embedded `akml.ico` resource (copied from `Installer/assets/icon.ico`).

**Accessibility and type (X-04)**
- `AutomationProperties.Name` on every icon-only control in the three areas: the History toolbar, clear search, star and ⋯, and the style list ⋮.
- History font sizes map to the existing `Typography` scale: 9–11.5 → Small (11), 12 → Body (12.5), 14 → H4. **No** new tokens (Constitution II gate).
- `OpenClosedColorConverter`/`FavoriteColorConverter` are replaced by `SetResourceReference` on the History* tokens, so they follow theme changes.

### R28 — Shared team style folder (STY-10, FR-064)

**Decision**
- `FormatterSettings.TeamStyleFolder` (string, default empty) is set on Options › Format › Styles with "…" folder browse.
- `ProfileManager` takes a third, read-only directory through a `Func<string?>` provider. The engine passes `ctx.EnsureSettings().Formatter.TeamStyleFolder`, re-read after `InvalidateSettings`.
- Precedence: user custom > team > built-in, for names.
- Team styles are listed in a **Team styles** group, and are read-only unless the folder is writable (probed with a temp-file create/delete).
- Writes to a read-only team style are refused by the engine.
- `ProfileInfo` gains `[Key(9)] string? Source` ("builtIn" / "user" / "team") and `[Key(10)] bool IsReadOnly`.
- If the folder is unreachable, the list shows "Team styles unavailable" and user styles keep working.
- The directory-stamp cache gains the third stamp. Scans are skipped when the folder is unreachable (timeout 2 s).
- The web edition's code is untouched. It will see team styles served by the engine, and writes to them are refused.

### R29 — Format SQL actions (STY-11, FR-065)

**Decision**
- `FormatterSettings.FormatSqlActions`:
  - ApplyLayout, ApplyCasing, ExpandWildcards and QualifyObjectNames: `bool`;
  - Semicolons: Insert / Remove / Leave;
  - Brackets: Add / Remove / Leave.
- Defaults equal the built-in profiles' `FormatActions` values, so current output is unchanged.
- **Interactive path only:**
  - `FormatDocumentCommand` / `FormatSelectionCommand` send the list through the existing, unread `FormatRequest.IncludeActions` (Key 4), together with the editor's real SessionId (today a random GUID).
  - `FormatDocumentHandler` passes `ctx.SchemaCache`/`ctx.Sessions`.
  - The engine honours the list:
    - ApplyLayout off → skip the layout stage and keep the original whitespace;
    - ApplyCasing gate;
    - semicolons and brackets through Stage 8;
    - expand and qualify through the schema-aware operations after validation.
- The CLI, bulk and pipeline API keep using `profile.FormatActions`, so `tests/format-parity` goldens are unaffected.
- AS keyword and column alias style are left out (N8; spec FR-065 updated).

### R30 — Format feedback (STY-09, FR-037)

**Decision**
- `FormatSelectionResponse [Key(7)] string? ProfileFallbackWarning` (additive, like `FormatResponse` Key 6).
- `HandleFormatSelection` uses the `LoadProfile(name, out warning)` overload (`FormatRequestHandler.cs:68` → `:895-914`).
- The shell calls `NotifyProfileFallbackOnce`.
- New `StatusBarManager.ShowTransient(text, seconds)`, which restores the idle text with a `DispatcherTimer` and respects the transaction indicator. It shows "Formatted with 'X'" when there is no fallback warning.

### R31 — Friendlier style-list actions (STY-08, FR-036)

**Decision**
- `StyleNameDialog` takes the existing names. It validates live (empty, illegal characters, duplicate case-insensitively; the current name is exempt for Rename) and disables OK until the name is valid.
- Copy opens it with "X copy" pre-filled.
- Window `InputBindings`, copying Options' `OnWindowKeyDown` pattern:
  - Ctrl+S Save; Ctrl+F focus search; Esc clears search first;
  - on the list: F2 Rename; Delete Delete (refused with a status message for built-ins and the active style); Enter Set active.
- Integer options get ▲▼. Notes show inline. Child options are indented one step (`RefreshIfGate` unchanged).

---

## Cross-cutting

### R32 — Tests and verification

**Decision**

**Unit tests (xunit)**, in the matching projects:

| Project | Covers |
|---|---|
| **Core.Tests** | New DTO keys: round trip plus legacy shape, following `HistoryRecordRequestTests`. `LineDiff`, `HistoryDateGroups`, environments migration, `EnvironmentMatcher` server+db. |
| **Engine.Tests** | v3 migration (FTS rebuild, ISO normalisation, orphans); group actions; `ReconcileOpen`; search scope (LIKE branches); `GetFilterValues`; drafts; team style folder; FormatSelection fallback warning; `IncludeActions`; completion wiring (MaxSuggestions, fuzzy, detail flags). |
| **Shell.Shared.Tests** (net472, `[StaFact]`, compiled through projitems — new files must be added to `AkmlSql.Shell.Shared.projitems`) | Options: theme handler, field-scoped reset, sentinel survival, nav tree, gating, AddNumber, palette catalog, dead-settings allow-list.<br>History: HasMore, debounce, selection-preserving refresh, row-menu target.<br>Styles: labels (render test), tab expansion, search index, change markers, import/export sequencing, name validation. |
| **Site.Tests** | F1 slugs resolve. |
| **UiTests (FlaUI, manual, deployed build)** | The screenshot tour extended: menu structure, Active Style menu, Options pages in light and dark, History keyboard use, the style editor at default size. Northwind only. |

**Ratchets:** the completion corpus and format-parity goldens must stay green. All defaults reproduce today's output (R1, R29).

**Known flaky or red tests:** `PerformanceBaselineTests`, the 2 ms History timing test, `VisualReferenceCoverageTests`, and the `sp031-*` goldens are excused as pre-existing (`doc/progress.md`).

### R33 — Build and deploy for manual checks

**Decision**
- Build the shell with full MSBuild (CLAUDE.md path); build the engine with `dotnet publish`.
- Deploy to `…\SSMS 22\Release\Common7\IDE\Extensions\AkmlSql\` with the engine in `\Engine`.
- Clear `ComponentModelCache`, then restart SSMS. The DTE menu is rebuilt once through its version marker (R25).
- `doc/deployment.md`'s stale MSBuild path and "never build via .slnx" note are corrected in the same change (Constitution: documentation currency).
- CLAUDE.md's stale `ThemeManager.Instance` guidance is corrected to ThemeRegistry / ThemeTokens.
