# Research: Multi-Area Bug Fixes and UI Polish (015)

**Date**: 2026-04-14  
**Branch**: `015-bug-fixes-polish`

---

## 1. IntelliSense — UPDATE SET Column Completion

**Decision**: Fix alias resolution in the UPDATE statement path; column completions are already partially wired.

**Findings**:
- `ClauseType.UpdateSet` exists in `CursorContextAnalyzer.cs:18` and `ColumnProvider.cs:33` already includes it in `ExpressionClauses`.
- **Root cause**: The alias resolver (`CompletionEngine.cs:101-109`) does not extract the target table from an `UPDATE <table> SET` statement. `context.AvailableAliases` is empty for UPDATE queries, so `ColumnProvider.GetCompletions()` falls through with zero items.
- **Fix**: Extend the alias resolution fallback (token-based scan, `CompletionEngine.cs:101-109`) to also detect the UPDATE target table pattern (`UPDATE <schema.table> SET`) and inject it as an implicit alias, so ColumnProvider finds columns for that table.

**Alternatives considered**: New `UpdateSetColumn` clause type (unnecessary — existing `ClauseType.UpdateSet` + alias fix is sufficient).

---

## 2. IntelliSense — ALTER TABLE Column Completion

**Decision**: Add `AlterTableColumn` ClauseType and extend `DetermineClauseType()` to recognize `ALTER TABLE <table> ALTER COLUMN` context.

**Findings**:
- `ClauseType.Alter` (`CursorContextAnalyzer.cs:207`) only triggers object (table/view/proc) completions via `ObjectProvider`. There is no specialized sub-context for `ALTER TABLE … ALTER COLUMN`.
- **Fix**: 
  1. Add `AlterTableColumn` to the `ClauseType` enum.
  2. In `DetermineClauseType()`: when walking backward from cursor, detect the pattern `COLUMN ← ALTER ← <table_token> ← TABLE ← ALTER` and return `AlterTableColumn`.
  3. Extend `ColumnProvider.CanHandle()` to include `ClauseType.AlterTableColumn`, extracting the table name from the backward token scan (similar to the UPDATE fix).

**Alternatives considered**: Parsing full AST for ALTER — too expensive for completion; token scan is the established pattern here.

---

## 3. Analysis Button — Silent Failure

**Decision**: The "Analysis" command exists and is wired (`AiIndexAnalysisCommand`, `AnalysisController`) but results may not be visible. Investigate the specific toolbar button the user is clicking.

**Findings**:
- Three analysis paths exist:
  1. **Live analysis** (`AnalysisController.cs:60-69`) — debounced 300ms on every document change, sends `MessageTypes.RequestAnalyze (25)`. Results go to DiagnosticTagger + ErrorListReporter (inline squiggles + Error List).
  2. **AI Index Analysis** (`AiIndexAnalysisCommand.cs:134`) — sends `MessageTypes.AiIndexAnalysis (74)`. Results rendered as editor annotations.
  3. **Bulk Analysis** (`BulkAnalysisCommand.cs`) — batch mode.
- **Root cause of silence**: Live analysis results appear in the VS Error List and as inline squiggles — these may not be visible if the Error List window is not open. The "Analysis" toolbar button likely maps to a command that is not properly connected or displays results in a channel the user doesn't see.
- **Fix needed**: Confirm which `CmdId` the "Analysis" toolbar button is bound to; ensure the command handler sends the IPC request AND surfaces results in a visible, self-contained panel (or shows the Error List).
- **Logging gap**: `AnalysisController.cs:78` logs at WARNING level — debug-level logging for analysis start/completion is absent.

**Alternatives considered**: None; the engine analysis capability itself is correct.

---

## 4. Search — "No Active Database Connection" False Negative

**Decision**: One-line bug fix in `NavigationRequestHandler.cs:166`.

**Findings**:
- **Root cause** (confirmed): `NavigationRequestHandler.cs:166` checks only `if (string.IsNullOrEmpty(databaseName))` but not `connectionString`. A session with `connectionString = null` and a non-null `databaseName` passes the guard, then calls `_schemaCacheManager.GetCache(null, databaseName)` — cache miss → silent empty results.
- Compare: `GetObjectDefinition` (line 43) and `FindReferences` (line 112) both check `connectionString`. ObjectSearch is the outlier.
- **Fix**: Change line 166 to `if (string.IsNullOrEmpty(connectionString) || string.IsNullOrEmpty(databaseName))`.

**Alternatives considered**: None — pure bug fix.

---

## 5. DROP TABLE Safety Warning Not Triggering

**Decision**: Safety detection code is correct; root cause is likely a configuration or environment-suppression issue, not a code bug.

**Findings**:
- `SafetyCheckHandler.cs:200-227` correctly uses ScriptDOM AST matching (`DropTableStatement`, `DropDatabaseStatement`) — unit tests passing.
- **Suppression paths** that can silently bypass the dialog:
  1. `safety.dropConfirmation = false` in `config.json` (ExecutionInterceptor.cs:314-317).
  2. The active environment's `Safety.EnvironmentSeverity` is set to `"Disabled"` (ExecutionInterceptor.cs:200-204).
- **Fix**: 
  1. Verify defaults: `dropConfirmation` must default to `true` in `AppSettings`.
  2. Log a WARNING when a safety check is suppressed by config/environment so the user can diagnose.
  3. Add a one-time nudge in the UI if `dropConfirmation` is `false`.

**Alternatives considered**: None; AST detection is sound.

---

## 6. SQL History — Star Badge Count

**Decision**: The "Starred" filter tab header shows a star emoji but no numeric badge. Add a live count binding.

**Findings**:
- `HistoryToolWindowControl.cs:225` — the Starred filter button has no numeric badge binding; only a star emoji.
- `HistoryToolWindowControl.cs:941-952` — the main status bar shows total entry count but not the starred count.
- `HistoryEntryDto.cs:62` — `IsFavorite` boolean flag is persisted correctly; toggling works.
- **Fix**: Add a `StarredCount` computed property to `HistoryViewModel` that counts `IsFavorite == true` entries. Bind a `TextBlock` badge overlay on the Starred filter button to `StarredCount`. Update `StarredCount` whenever `IsFavorite` is toggled.

---

## 7. SQL History — Advanced Search Not Working

**Decision**: The parser is complete; investigate why search results aren't surfacing in the UI.

**Findings**:
- `HistorySearchParser.cs:9-117` is a full implementation with prefix filters (`server:`, `database:`, `db:`, `sql:`, `name:`, `starred:`, `open:`), boolean operators, quoted phrases, and wildcard support.
- **Suspected gap**: The `CamelCaseTokens` list produced by the parser (lines 102-138) is passed to the backend for post-filtering. If the engine's FTS5 query builder doesn't consume `CamelCaseTokens`, camel-case searches silently return no results.
- **Fix**: Add integration test coverage for advanced search round-trips (parser → engine → result). Trace the `CamelCaseTokens` path from `HistorySearchParser` through the engine handler to confirm it's applied.

---

## 8. SQL History — Query Rename Already Implemented

**Decision**: Rename is implemented; improve discoverability and label quality only.

**Findings**:
- `HistoryToolWindowControl.cs:648` — "Rename" context menu item exists.
- `HistoryToolWindowControl.cs:1437-1480` — full rename handler: shows input dialog, sends `HistoryActions.RenameQuery` IPC, updates `entry.TabTitle` locally.
- `HistoryToolWindowControl.cs:1797-1819` — `QueryNameConverter` displays `TabTitle` first, then truncated SQL.
- **Fix needed**: Improve the query name label in the history list to show a friendly placeholder (e.g., "Untitled query — click to rename") when `TabTitle` is empty, making rename discoverable without documentation.

---

## 9. Schema Progress — Move to Bottom-Right Notification Box

**Decision**: Refactor `SchemaProgressMargin` from `IWpfTextViewMargin` (top strip) to an AdornmentLayer overlay anchored bottom-right.

**Findings**:
- `SchemaProgressMargin.cs:24` — implements `IWpfTextViewMargin` (registered as a margin provider).
- Current layout: full-width `Border` (height 20) with `StackPanel` (spinner `Ellipse` + `TextBlock` status).
- The spinner and animation logic (`RotateTransform`, `DoubleAnimation`, `DispatcherTimer`) are correct per CLAUDE.md spec — reuse them.
- **Fix**:
  1. Remove `IWpfTextViewMargin` interface and provider registration.
  2. Implement `IWpfTextViewCreationListener` with `AdornmentLayerDefinition` ("AkmlSchemaProgress", IsOverlayLayer = true).
  3. On schema-loading start: create a `Border` overlay (fixed ~280×56px), position at bottom-right using `Canvas.SetRight` / `Canvas.SetBottom` within the adornment layer's `Canvas`.
  4. Re-use existing spinner `Ellipse`, `TextBlock`, and `FadeOut` animation (`SchemaProgressMargin.cs:352-363`).
  5. Respond to `ITextView.ViewportWidthChanged` / `ViewportHeightChanged` to reposition on resize.

**Alternatives considered**: WPF Popup — rejected (Popup leaks outside the editor window boundary). Adornment layer is the correct VS extension pattern.

---

## 10. Dark Theme — Faded Text in Dropdowns and Button Hover

**Decision**: Two independent fixes in `SettingsWindow.cs`.

**Findings**:

**Dropdown text (faded)**:
- `SettingsWindow.cs:2033` — `ComboBoxItem` style sets `TextElement.ForegroundProperty = _theme.FgPrimary`. Correct.
- `SettingsWindow.cs:2037` — hover trigger applies `_theme.SelectedText` (white). Correct.
- **Likely issue**: VS/SSMS host theme inheritance can override `TextElement.Foreground` at the `ComboBox` level. Fix: apply `TextElement.ForegroundProperty` directly on the `ComboBox` wrapper element AND set `ItemContainerStyle` explicitly so each item's foreground isn't inherited from the host.

**Button hover (faded)**:
- `SettingsWindow.cs:2275` — `MouseEnter` handler sets only `Background = theme.ButtonHover`. Foreground is not updated.
- In dark theme, `ButtonHover` is a lighter shade of dark gray — the lighter background makes `FgPrimary` (#D4D4D4) appear lower-contrast.
- **Fix**: In `MakeButton()`, update `MouseEnter` to also set `Foreground = _theme.FgPrimary` (or white) and `MouseLeave` to restore it, ensuring text stays legible on hover background.

---

## 11. Document Outline — Empty Window

**Decision**: The SQL parser and IPC are fully implemented. Investigate why the tree view renders empty.

**Findings**:
- `DocumentOutlineBuilder.cs:21-58` — full implementation: `TsqlParserService.Parse()`, extracts procedures, functions, views, triggers, CTEs, temp tables, BEGIN/END, `--region` markers.
- `DocumentOutlineViewModel.cs:112-115` — returns empty array if buffer text is empty.
- `DocumentOutlineHandler.cs:36-73` — correct IPC handler.
- **Likely cause of empty window**: The `IWpfTextViewCreationListener` may not be attaching to the correct buffer type, or the buffer text passed is empty/null at attachment time (race with editor initialization).
- **Fix**: Add null/empty guard logging to `DocumentOutlineViewModel.RequestOutlineUpdate()`. Verify that `IContentTypeRegistrationService` registers the handler for `tsql` content type. Add the on-demand Refresh button (US10, FR-019a) so users can manually trigger a re-parse.

---

## 12. Installer — Remove Desktop Shortcut

**Decision**: Remove the `[Tasks]` and `[Icons]` entries for desktop shortcut.

**Findings**:
- `AkmlSqlSetup.iss:146` — `[Icons]` entry with `Tasks: desktopicon`.
- `AkmlSqlSetup.iss:148-149` — `[Tasks]` entry named `desktopicon`.
- **Fix**: Delete both lines. No other code references `desktopicon`.

---

## 13. Version Scheme — Major.YY.MMDDHHmm

**Decision**: Align `Directory.Build.props` to the confirmed `1.YY.MMDDHHmm` format; make VSIX manifests dynamic.

**Findings**:
- `src/Directory.Build.props:3` — already computes `1.{GitCommitCount}.{MMddHHmm}` (e.g., `1.42.04140511`).
- **Mismatch**: Uses `GitCommitCount` for the middle segment; spec requires 2-digit year (`YY`).
- **Fix**: Change `Directory.Build.props` line 11 to use `$([System.DateTime]::UtcNow.AddHours(2).ToString("yy"))` for the `YY` segment: `Version=1.$(_BuildYear).$(_BuildStamp)`.
- 7 VSIX manifests have hardcoded `Version="1.0.0"`. These should be injected by the build script (already uses MSBuild `-p:Version=...` or can be patched in `build.ps1`).
- `AkmlSqlSetup.iss:51` — hardcoded `"1.0.0"` in Inno Setup; inject via `/DMyAppVersion=...` CLI flag from `build.ps1`.

---

## 14. AI Assistance — Inline Help Text

**Decision**: Add inline help text paragraphs below each AI provider's API key field in `SettingsWindow.cs`.

**Findings**:
- `SettingsWindow.cs:1659` — existing API Key input panel.
- `SettingsWindow.cs:2744` / `2919` — load/save API key via `CredentialManager`.
- **DPAPI already implemented**: `CredentialManager.cs` (100 lines) uses `DataProtectionScope.CurrentUser`, `dpapi:` prefix for encrypted keys, and memory zeroing. No new security code needed.
- **Fix**: Below the API Key `TextBox` in the AI Assistance section, add a `TextBlock` with provider-specific help (e.g., "Claude: Get your API key at console.anthropic.com. Example model: claude-sonnet-4-6"). Repeat for Gemini (aistudio.google.com).

---

## 15. Installer — Icon and Banner

**Decision**: Asset files exist at correct paths; this is a design/replacement task not a code task.

**Findings**:
- `AkmlSqlSetup.iss:80-82` — `icon.ico`, `sidebar.bmp`, `banner.bmp` correctly referenced.
- All three files present in `src/AkmlSql.Installer/assets/`.
- Required sizes: `sidebar.bmp` = 164×314px (Inno Setup wizard sidebar), `banner.bmp` = 497×58px (Inno Setup header banner), `icon.ico` = multi-size ICO (16, 32, 48, 256px).
- **No code change needed** — replace asset files with branded versions. Out of scope for this implementation; tracked as design deliverable.
