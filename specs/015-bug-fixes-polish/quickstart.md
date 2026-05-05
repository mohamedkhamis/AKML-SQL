# Developer Quickstart: 015-bug-fixes-polish

**Branch**: `015-bug-fixes-polish`  
**Date**: 2026-04-14

---

## Work Areas at a Glance

This branch covers 14 independently testable fixes across four areas. Each group below lists the files to touch, what to change, and how to verify.

---

## Group A — IntelliSense Fixes (US1)

**Files**:
- `src/AkmlSql.Engine/Completion/CursorContextAnalyzer.cs`
- `src/AkmlSql.Engine/Completion/CompletionEngine.cs` (alias fallback, lines 101-109)
- `src/AkmlSql.Engine/Completion/Providers/ColumnProvider.cs`

**Changes**:
1. `CursorContextAnalyzer`: Add `AlterTableColumn` to `ClauseType` enum. In `DetermineClauseType()`, detect backward token pattern `COLUMN ← ALTER ← <table> ← TABLE ← ALTER` and return `AlterTableColumn`. Extract `<table>` into context.
2. `ColumnProvider`: Add `ClauseType.AlterTableColumn` to `ExpressionClauses` (or handle in `CanHandle`). Use extracted table name from context to fetch columns.
3. `CompletionEngine` alias fallback: After the token-based alias scan (`lines 101-109`), if `ClauseType == UpdateSet` and `AvailableAliases` is still empty, scan backward for `UPDATE <table> SET` and inject `<table>` as the implicit alias.

**Verify**:
```
dotnet test tests/AkmlSql.Core.Tests/ -t  # find completion tests
# Load SSMS/VS with extension
# Type: UPDATE Users SET [trigger completion] → expect column names
# Type: ALTER TABLE Users ALTER COLUMN [trigger] → expect column names
```

---

## Group B — Analysis + Search Fixes (US2, US3)

**Files**:
- `src/AkmlSql.Engine/Navigation/NavigationRequestHandler.cs` (line 166)
- `src/AkmlSql.Shell.Shared/Commands/` — identify Analysis toolbar command

**Changes**:
1. `NavigationRequestHandler.cs:166`: Change guard from `string.IsNullOrEmpty(databaseName)` to `string.IsNullOrEmpty(connectionString) || string.IsNullOrEmpty(databaseName)`.
2. Analysis button: Confirm `CmdId` bound to the toolbar "Analysis" button. If mapped to live-analysis channel, ensure the Error List / diagnostics panel is opened when the command fires.
3. Add DEBUG log entries in `AnalysisController` for analysis start/end.

**Verify**:
```
# Search fix: Connect to any DB → open Object Search → type table name → expect results
# Analysis: Open query with SELECT * → click Analysis → expect Error List populated
```

---

## Group C — Safety Warning Fix (US4)

**Files**:
- `src/AkmlSql.Core/Config/AppSettings.cs` (`SafetySettings.DropConfirmation` default)
- `src/AkmlSql.Shell.Shared/Safety/ExecutionInterceptor.cs` (suppression logging)

**Changes**:
1. Confirm `DropConfirmation` defaults to `true` in `AppSettings` constructor/initializer.
2. In `ExecutionInterceptor`: when a safety check is bypassed (lines 314-317 or 200-204), emit `Log.Warning("Safety check suppressed: {reason}", ...)`.

**Verify**:
```
# Execute: DROP TABLE dbo.TestTable → SafetyWarningDialog must appear
# Disable in config (dropConfirmation: false) → execute DROP → log must show suppression warning
```

---

## Group D — SQL History Fixes (US5, US6, US9)

**Files**:
- `src/AkmlSql.Shell.Shared/History/HistoryToolWindowControl.cs`
- `src/AkmlSql.Shell.Shared/History/HistoryViewModel.cs`
- `src/AkmlSql.Shell.Shared/History/HistorySearchParser.cs` (advanced search trace)

**Changes**:

**Star badge (US5)**:
1. Add `StarredCount` property to `HistoryViewModel`: `Entries.Count(e => e.IsFavorite)`, raise `PropertyChanged` on every toggle.
2. In `HistoryToolWindowControl.cs:225`, add a `TextBlock` badge overlay on the Starred filter button bound to `StarredCount`.

**Query rename discoverability (US9)** — already implemented:
1. In `QueryNameConverter` (lines 1797-1819), when `TabTitle` is null/empty, display a muted-color placeholder like "(rename me)" or "(click to name)" to surface the feature.
2. Add a tooltip to the Rename menu item: "Give this query a descriptive name".

**Advanced search (US6)**:
1. Trace `CamelCaseTokens` path from `HistorySearchParser.cs` → engine handler → FTS5 query builder. Add logging.
2. Write an integration test: parse `"SLT"` (CamelCase for SELECT) and verify results.

**Verify**:
```
# Star badge: Star 3 queries → badge shows "3" → un-star one → badge shows "2"
# Rename: Right-click a history entry → "Rename" appears with tooltip → rename persists
# Advanced search: Type "database:AdventureWorks" → only AdventureWorks queries returned
```

---

## Group E — Schema Progress Notification Box (US7)

**Files**:
- `src/AkmlSql.Shell.Shared/Editor/SchemaProgress/SchemaProgressMargin.cs` — refactor to adornment layer
- `src/AkmlSql.Shell.Shared/Editor/SchemaProgress/SchemaProgressMarginProvider.cs` (if exists) — update registration

**Changes**:
1. Remove `IWpfTextViewMargin` interface. Replace with `IWpfTextViewCreationListener`.
2. Define `[Export(typeof(AdornmentLayerDefinition))]` attribute: `Name = "AkmlSchemaProgress"`, `IsOverlayLayer = true`, `Order = After(PredefinedAdornmentLayers.CurrentLineHighlighter)`.
3. In `TextViewCreated()`: attach to `ITextView.LayoutChanged` and `ViewportWidthChanged`/`ViewportHeightChanged` to reposition.
4. Create notification box `Border` (280×56px), place on `adornmentLayer.Canvas` using `Canvas.SetRight(border, 12)` / `Canvas.SetBottom(border, 12)`.
5. Re-use existing `Ellipse` spinner, `TextBlock` status, and `FadeOut()` animation from current implementation.

**Verify**:
```
# Connect to a DB with large schema → observe bottom-right notification appears (not at top of editor)
# Verify spinner appears and fades out on load complete
# Resize VS/SSMS window → notification stays in bottom-right corner
```

---

## Group F — Dark Theme Fix (US8)

**Files**:
- `src/AkmlSql.Shell.Shared/Dialogs/SettingsWindow.cs` (MakeButton ~line 2275, ThemeComboBoxVisualTree ~line 2028)

**Changes**:

**Button hover**:
In `MakeButton()`, update `MouseEnter` to also set `btn.Foreground`:
```csharp
btn.MouseEnter += (s, e) => {
    btn.Background = theme.ButtonHover;
    btn.Foreground = _theme.FgPrimary; // ensure contrast on hover
};
btn.MouseLeave += (s, e) => {
    btn.Background = theme.ButtonBackground;
    btn.Foreground = _theme.FgPrimary;
};
```

**Dropdown text**:
Verify `ComboBoxItem` style in `ThemeComboBoxVisualTree()` applies `TextElement.ForegroundProperty` at the `ComboBox` level (not just `ComboBoxItem`) to prevent VS host theme inheritance from overriding it.

**Verify**:
```
# Open SQL Options → switch to Dark theme
# Hover OK, Cancel, Import, Export → text must remain fully legible
# Open any dropdown → all option labels must be high-contrast, not faded
```

---

## Group G — Document Outline Fix (US10)

**Files**:
- `src/AkmlSql.Shell.Shared/Productivity/DocumentOutline/DocumentOutlineViewModel.cs` (attachment / buffer content check)
- `src/AkmlSql.Shell.Shared/Productivity/DocumentOutline/DocumentOutlineControl.xaml` (add Refresh button)
- `src/AkmlSql.Shell.Shared/Commands/DocumentOutlineCommand.cs` (content-type registration if needed)

**Changes**:
1. Add null/empty guard logging in `RequestOutlineUpdate()` — log if buffer text is empty when update is triggered.
2. Verify `[ContentType("tsql")]` is applied to the `IWpfTextViewCreationListener` export attribute.
3. Add a "Refresh" `Button` to the outline tool window (FR-019a). On click, call `RequestOutlineUpdate()`.
4. If buffer text is empty at attachment time, defer first outline request until `ITextBuffer.Changed` fires once.

**Verify**:
```
# Open a .sql file with: WITH MyCTE AS (...) SELECT ... CREATE PROCEDURE dbo.GetData ...
# Open Document Outline → CTE and procedure must appear as nodes
# Edit to add another CTE → click Refresh → new node appears
# Open blank file → outline shows "No SQL structure found"
```

---

## Group H — Installer Changes (US11, US12)

**Files**:
- `src/AkmlSql.Installer/AkmlSqlSetup.iss`
- `src/Directory.Build.props`
- `build.ps1`
- 7× `*/source.extension.vsixmanifest` / `*/extension.vsixmanifest`

**Changes**:

**Remove desktop shortcut (US11)**:
- Delete `AkmlSqlSetup.iss:146` (Icons entry with `Tasks: desktopicon`).
- Delete `AkmlSqlSetup.iss:148-149` ([Tasks] desktopicon entry).

**Version scheme (US12)**:
- `Directory.Build.props`: Replace `1.$(GitCommitCount).$(_BuildStamp)` with `1.$(_BuildYear).$(_BuildStamp)` where `_BuildYear = $([System.DateTime]::UtcNow.AddHours(2).ToString("yy"))`.
- `build.ps1`: Compute `$Version = "1.$Year.$Stamp"` and pass to ISCC as `/DMyAppVersion=$Version`, and to MSBuild as `-p:Version=$Version`.
- VSIX manifests: Replace hardcoded `Version="1.0.0"` with the MSBuild `$(Version)` property (done via `build.ps1` or MSBuild task).

**Verify**:
```
# Run build.ps1 → inspect installer pages → no desktop shortcut checkbox
# Check About dialog → version matches 1.26.MMDDHHMM pattern
# Check vsixmanifest inside .vsix → version matches
```

---

## Group I — AI Inline Help (US13)

**Files**:
- `src/AkmlSql.Shell.Shared/Dialogs/SettingsWindow.cs` (AI Assistance section, ~line 1659)

**Changes**:
Add a `TextBlock` below each AI provider's API key field:

**Claude**:
> "Get your API key at console.anthropic.com → API Keys. Example model: `claude-sonnet-4-6`."

**Gemini**:
> "Get your API key at aistudio.google.com → Get API key. Example model: `gemini-2.0-flash`."

Style with `_theme.PlaceholderText` foreground, `FontSize = 11`, `TextWrapping = Wrap`.

**Verify**:
```
# Open SQL Options → AI Assistance tab
# Verify help text appears below each provider's API key field
# Text is legible in both Light and Dark themes
```

---

## Build & Test

```bash
# Build engine and run tests
dotnet test tests/AkmlSql.Core.Tests/AkmlSql.Core.Tests.csproj

# Build a specific shell extension
MSBUILD="/c/Program Files/Microsoft Visual Studio/2022/Enterprise/MSBuild/Current/Bin/MSBuild.exe"
"$MSBUILD" src/AkmlSql.Ssms22/AkmlSql.Ssms22.csproj -t:Restore,Build -p:Configuration=Release -v:minimal

# Full build with version injection
./build.ps1 -Configuration Release
```
