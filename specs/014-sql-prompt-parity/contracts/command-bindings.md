# Command Bindings Contract

**Branch**: `014-sql-prompt-parity` | **Date**: 2026-04-09

This document defines every new keyboard chord, command id, and VSCT binding added by spec 014. Each entry must be added to all 6 host `.vsct` files (`AkmlSql.Ssms20`, `AkmlSql.Ssms21`, `AkmlSql.Ssms22`, `AkmlSql.VS2019`, `AkmlSql.VS2022`, `AkmlSql.VS2026`).

## Existing GUIDs (CLAUDE.md "Architecture")

```text
guidAkmlSqlPackage    = {A1B2C3D4-1111-2222-3333-444455556666}
guidAkmlSqlCmdSet     = {A1B2C3D4-1111-2222-3333-444455557777}
```

## New CommandID assignments

| Symbol | Hex ID | User Story | Binding |
|---|---|---|---|
| `cmdidExecuteCurrentBatch` | `0x0150` | US20 | `Alt+Shift+F5` |
| `cmdidExecuteToCursor` | `0x0151` | US20 | `Ctrl+Shift+F5` |
| `cmdidScriptObjectAsAlter` | `0x0152` | US13 | `F12` |
| `cmdidSelectInObjectExplorer` | `0x0153` | US13 | `Ctrl+F12` |
| `cmdidSummarizeScript` | `0x0154` | US13 | `Ctrl+B, Ctrl+S` |
| `cmdidFindUnusedDeclarations` | `0x0155` | US13 | `Ctrl+B, Ctrl+F` |
| `cmdidApplyCasing` | `0x0156` | US7 | `Ctrl+B, Ctrl+U` |
| `cmdidQualifyObjectNames` | `0x0157` | US7 | `Ctrl+B, Ctrl+Q` |
| `cmdidExpandWildcards` | `0x0158` | US7 | `Ctrl+B, Ctrl+W` |
| `cmdidInsertSemicolons` | `0x0159` | US7 | `Ctrl+B, Ctrl+C` |
| `cmdidToggleBrackets` | `0x015A` | US7 | `Ctrl+B, Ctrl+B` |
| `cmdidInlineProcedure` | `0x015B` | US7 | `Ctrl+B, Ctrl+I` |
| `cmdidEncapsulateAsProcedure` | `0x015C` | US7 | `Ctrl+B, Ctrl+E` |
| `cmdidAiOpenChat` | `0x015D` | US10 | `Alt+Z` |
| `cmdidAiFixSelection` | `0x015E` | US10 | `Shift+Alt+R` |
| `cmdidAiOptimizeSelection` | `0x015F` | US10 | `Ctrl+Alt+Z` |
| `cmdidAiManualGhostText` | `0x0160` | US10 | `Ctrl+Alt+Up` |
| `cmdidToggleSuggestions` | `0x0161` | US19 | `Ctrl+Shift+P` |
| `cmdidRefreshSchemaCache` | `0x0162` | US19 | `Ctrl+Shift+D` |
| `cmdidBrowseOpenTabs` | `0x0163` | US20 | `Ctrl+Q` |
| `cmdidShowCommandPalette` | `0x0164` | US4 | `Alt+S` (SSMS) / `Alt+P` (VS) |
| `cmdidShowSafetyWarning` | `0x0165` | US1 | (programmatic; no chord) |
| `cmdidShowSmartRenameDialog` | `0x0166` | US15 | `F2` (extends existing rename) |
| `cmdidShowFindInvalidObjects` | `0x0167` | US14 | (Object Explorer right-click) |
| `cmdidShowCodeAnalysisIssuesWindow` | `0x0168` | US6 | (menu only) |
| `cmdidDisableFormattingForSelection` | `0x0169` | US9 | (action list only, `Ctrl`) |
| `cmdidColumnPickerToggle` | `0x016A` | US2 | `Ctrl+Left` (in popup) |
| `cmdidColumnPickerSelectAll` | `0x016B` | US2 | `Ctrl+A` (in picker) |
| `cmdidObjectDefinitionBoxResize` | `0x016C` | US8 | (mouse drag; no chord) |
| `cmdidApplyAnalysisFix` | `0x016D` | US17 | (lightbulb click) |
| `cmdidShowIssueDetails` | `0x016E` | US17 | `Ctrl+hover` |
| `cmdidResultGridCopyAsInClause` | `0x016F` | US16 | (right-click) |
| `cmdidResultGridScriptAsInsert` | `0x0170` | US16 | (right-click) |
| `cmdidResultGridOpenInExcel` | `0x0171` | US16 | (right-click) |
| `cmdidExplainSql` | `0x0172` | US18 | (right-click) |
| `cmdidQueryIndexAnalysis` | `0x0173` | US18 | (AI menu) |
| `cmdidCommentToSql` | `0x0174` | US18 | `Tab` after `-- generate:` (filter) |
| `cmdidF1Help` | `0x0175` | FR-104 | `F1` |

## Chord registration pattern

Each chord is declared in the `.vsct` `<KeyBindings>` section like:

```text
<KeyBinding guid="guidAkmlSqlCmdSet" id="cmdidApplyCasing"
            editor="guidVSStd97" key1="B" mod1="Control"
            key2="U" mod2="Control" />
```

Where `guidVSStd97` is the standard editor scope (`{8d8529d3-625d-4a0d-a52a-c2a4a92ab8c3}`) so chords only fire inside an editor.

## Menu placement

All new commands also appear in the **AKML SQL** top-level menu under appropriate sub-menus (FR-029) to ensure discoverability for users who don't memorize chords. Sub-menu structure:

```text
AKML SQL
├── Refactoring
│   ├── Apply Casing                  Ctrl+B, Ctrl+U
│   ├── Qualify Object Names          Ctrl+B, Ctrl+Q
│   ├── Expand Wildcards              Ctrl+B, Ctrl+W
│   ├── Insert Semicolons             Ctrl+B, Ctrl+C
│   ├── Add/Remove Brackets           Ctrl+B, Ctrl+B
│   ├── Inline Procedure              Ctrl+B, Ctrl+I
│   ├── Encapsulate as Procedure      Ctrl+B, Ctrl+E
│   └── Smart Rename...               F2
├── Navigation
│   ├── Summarize Script              Ctrl+B, Ctrl+S
│   ├── Script Object as ALTER        F12
│   ├── Select in Object Explorer     Ctrl+F12
│   ├── Find Unused Variables         Ctrl+B, Ctrl+F
│   └── Browse Open Tabs              Ctrl+Q
├── Analysis
│   ├── Show All Issues
│   ├── Find Invalid Objects
│   └── Toggle Code Analysis
├── AI
│   ├── Open AI Chat                  Alt+Z
│   ├── Explain Selection
│   ├── Fix Selection                 Shift+Alt+R
│   ├── Optimize Selection            Ctrl+Alt+Z
│   ├── Query Index Analysis
│   └── Generate Ghost Text           Ctrl+Alt+Up
├── Execution
│   ├── Execute Current Batch         Alt+Shift+F5
│   └── Execute To Cursor             Ctrl+Shift+F5
├── Format
│   ├── Format Document               (existing) Ctrl+K, Ctrl+Y
│   ├── Disable Formatting for Selection
│   └── (existing items)
├── (existing top-level items: Settings, Send Feedback, About, Check Updates, View Logs)
└── Command Palette                   Alt+S (SSMS) / Alt+P (VS)
```

## Conflicts and resolutions

| Chord | SSMS default | Resolution |
|---|---|---|
| `Alt+Shift+F5` | unbound | OK |
| `Ctrl+Shift+F5` | unbound | OK |
| `F12` | bound to "Go to Definition" in some VS editors | Use `Editor` scope so AKML SQL only fires inside `.sql` files; in VS hosts `F12` continues to mean Go to Definition for non-SQL files |
| `Ctrl+F12` | bound in some VS hosts to "Go to Implementation" | Same scope-restriction |
| `Ctrl+B, Ctrl+B` | unbound | OK |
| `Ctrl+B, Ctrl+U` | unbound | OK |
| `Ctrl+Q` | bound in VS to "Quick Launch" | Settings toggle: respect VS native binding when running in VS hosts; only bind in SSMS by default |
| `Alt+Z` | unbound | OK |
| `Shift+Alt+R` | unbound | OK |
| `Ctrl+Alt+Z` | unbound | OK |
| `Ctrl+Alt+Up` | bound to "Move Line Up" in some hosts | Honor host setting; fall back to no binding when conflict detected |
| `Alt+S` | SSMS top menu shortcut for "Tools > Settings" | SSMS only — degrades in VS where `Alt+P` is used |
| `Ctrl+Shift+P` | bound in VS hosts to "Go to All" / Quick Open | Settings toggle: respect host binding; only bind in SSMS by default |
| `Ctrl+Shift+D` | unbound in SSMS; bound in VS to "Window Layout" | Same scope-restriction as above |

For every chord that conflicts with a host default, the new binding is in `<Editor>` scope only and the corresponding `Navigation` setting (`EnableX`) defaults to `true` only on hosts where the chord is unconflicted, `false` elsewhere. Users can re-enable per chord in Options.

## Programmatic command invocation

All new chords route through `Microsoft.VisualStudio.Shell.OleMenuCommandService.AddCommand` in the existing `AkmlSqlPackage` class. Each command's `Execute` handler is a thin shell wrapper that dispatches to the corresponding IPC request type (see `ipc-messages.md`) on the engine.

## Discoverability

Every command listed above is also surfaced in the **Command Palette** (US4) so users can search "smart rename", "script as alter", "explain", etc. without memorizing chords. The Command Palette source for AKML SQL commands enumerates the registered `OleMenuCommand` set at startup.
