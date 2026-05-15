# Contract: Commands and Keyboard Bindings

This contract catalogs every new command and chord binding introduced by Phase 10. Each row names the command ID, the keyboard chord (single key or two-key chord), the target host menu / VSCT placement, and the spec FR it satisfies.

**Corrected 2026-05-13** (post first-review): the original draft allocated `0x0200..0x021F` for new commands. That range is **fully occupied** by existing commands in `src/AkmlSql.Shell.Shared/PackageGuids.cs` (`CmdFormatDocument=0x0200`..`CmdEditProfile=0x0220`). The corrected allocation uses `0x0900..0x093F`, consistent with the existing per-phase grouping (Phase 8 = `0x0600`, Phase 9 = `0x0700`, Phase 10 core parity = `0x0800`; this spec 019 = Phase-10-closure = `0x0900`). Additionally, many "new" entries in the original draft were actually chord rebinds of pre-existing commands; those are correctly identified below as **reuse** rows.

## Command Set GUID

All new commands continue to live under the shared command-set GUID:

```
{A1B2C3D4-1111-2222-3333-444455557777}
```

defined in `src/AkmlSql.Shell.Shared/PackageGuids.cs` (existing). Container class is `CommandIds`. The command set is the same across all six host VSCT files.

## Command ID Allocation (corrected)

Command IDs in the range `0x0900` – `0x093F` are reserved for Phase 10 closure. Existing IDs through `0x0804` remain unchanged.

### Genuinely new commands (22 IDs)

| Hex | Name | Story | Chord / Trigger | Host placement |
|---|---|---|---|---|
| `0x0900` | `CmdColumnPickerOpen` | US2 | `Ctrl+Left Arrow` (when completion popup is open) | `IOleCommandTarget` filter — not in VSCT main menu |
| `0x0901` | `CmdTabWildcardExpand` | US2 | `Tab` (when caret is right after `*`) | `IOleCommandTarget` filter — not in VSCT main menu |
| `0x0902` | `CmdShowCodeAnalysisIssues` | US3 | (none — invoked via AKML SQL menu and Command Palette) | AKML SQL menu → "Show Code Analysis Issues" |
| `0x0903` | `CmdLightbulbApplyFix` | US3 | (none — invoked via lightbulb popup button) | Lightbulb popup button |
| `0x0904` | `CmdLightbulbDisableRule` | US3 | (none — invoked via lightbulb popup button) | Lightbulb popup button |
| `0x0905` | `CmdTabColorAssignServer` | US4 | (none — right-click submenu item) | Right-click tab → "Tab Color (Server)" |
| `0x0906` | `CmdTabColorAssignDatabase` | US4 | (none — right-click submenu item) | Right-click tab → "Tab Color (Database)" |
| `0x0907` | `CmdTabColorAssignServerGroup` | US4 | (none — right-click submenu item; conditional on Registered Server Group membership) | Right-click tab → "Tab Color (Server Group)" |
| `0x0908` | `CmdSummarizeScript` | US7 | `Ctrl+B, Ctrl+S` | AKML SQL menu → Navigation → "Summarize Script" + chord |
| `0x0909` | `CmdScriptAsAlter` | US7 | `F12` | AKML SQL menu → Navigation → "Script Object as ALTER" + chord (replaces SSMS native F12 for AKML-owned identifier resolution; falls through to SSMS default when no match) |
| `0x090A` | `CmdSelectInObjectExplorer` | US7 | `Ctrl+F12` | AKML SQL menu → Navigation → "Select in Object Explorer" + chord |
| `0x090B` | `CmdFindUnusedVariables` | US7 | `Ctrl+B, Ctrl+F` | AKML SQL menu → Navigation → "Find Unused Variables and Parameters" + chord |
| `0x090C` | `CmdBrowseOpenTabs` | US7 | `Ctrl+Q` | AKML SQL menu → Navigation → "Browse Open Tabs" + chord |
| `0x090D` | `CmdShowFindInvalidObjects` | US8 | (none — invoked via Object Explorer right-click and Command Palette) | Object Explorer database node right-click → "Find Invalid Objects" |
| `0x090E` | `CmdInlineStoredProcedure` | US10 | `Ctrl+B, Ctrl+I` | AKML SQL menu → Refactor → "Inline Stored Procedure" + chord |
| `0x090F` | `CmdSmartRename` | US10 | `F2` (when caret is on an identifier matching a DB object) | AKML SQL menu → Refactor → "Smart Rename" + chord. Falls through to host native `F2` if no AKML-resolvable identifier is under the caret. Distinct from the existing `CmdSafeRename` (document-scope rename); Smart Rename is database-scope. |
| `0x0910` | `CmdExecuteCurrentBatch` | US10 | `Alt+Shift+F5` | AKML SQL menu → Execution → "Execute Current Batch" + chord. Triggers the existing `ExecutionInterceptor` safety check. |
| `0x0911` | `CmdToggleSuggestions` | US11 | `Ctrl+Shift+P` | AKML SQL menu → IntelliSense → "Toggle Suggestions" + chord. Per-session toggle. |
| `0x0912` | `CmdCycleCategoryFilterForward` | US11 | `Ctrl+Down Arrow` (when completion popup is open) | `IOleCommandTarget` filter — not in main menu |
| `0x0913` | `CmdCycleCategoryFilterBackward` | US11 | `Ctrl+Up Arrow` (when completion popup is open) | `IOleCommandTarget` filter — not in main menu |
| `0x0914` | `CmdDisableFormattingForSelection` | US11 | (none — invoked via editor Actions list) | Editor Actions list → "Disable formatting for selected text" |
| `0x0915` | `CmdAiManualGhostText` | US13 | `Ctrl+Alt+Up Arrow` | `IOleCommandTarget` filter |

Reserved range `0x0916` – `0x093F` is left open for unforeseen Phase 10 additions during implementation.

### Reused existing commands — chord binding additions only (no new ID)

These commands already exist in `PackageGuids.cs` `CommandIds`. Phase 10 adds the SQL-Prompt-equivalent chord binding to each. No new command ID is allocated.

| Existing ID | Existing Name | Story | New chord / wiring | Notes |
|---|---|---|---|---|
| `0x0600` | `CmdCommandPalette` | US6 | `Alt+S` (SSMS), `Alt+P` (VS) — add chord bindings; modify backing window to aggregate 4 sources per spec 014 FR-048 | Backing class extended (per data-model.md §6); chord added in all 6 VSCT files |
| `0x0215` | `CmdToggleBrackets` | US10 | `Ctrl+B, Ctrl+B` chord binding | Already shipped without this specific chord; rebind to FR-041 |
| `0x0402` | `CmdExtractToProc` | US10 | `Ctrl+B, Ctrl+E` chord binding | "Encapsulate as Stored Procedure" is the SQL Prompt name for the same operation as AKML's existing "Extract to Stored Procedure" |
| `0x0602` | `CmdExecuteToCursor` | US10 | `Ctrl+Shift+F5` chord binding + safety-check wiring | Command already exists; spec adds the chord AND requires `ExecutionInterceptor` to hook it (FR-046) |
| `0x0400` | `CmdSafeRename` | — | (kept as-is, document-scope only) | Distinct from new `CmdSmartRename=0x090F`; both available; user picks via Options |
| `0x0705` | `CmdAiChatPanel` | US13 | `Alt+Z` chord binding | Command already exists; spec adds the chord |
| `0x0702` | `CmdAiFix` | US13 | `Shift+Alt+R` chord binding | Command already exists; spec adds the chord |
| `0x0703` | `CmdAiOptimize` | US13 | `Ctrl+Alt+Z` chord binding | Command already exists; spec adds the chord |
| `0x0701` | `CmdAiExplain` | US13 | Right-click selection + Command Palette discoverability | Command already exists; spec adds the new entry points |
| `0x0704` | `CmdAiIndexAnalysis` | US13 | AKML SQL menu + Command Palette discoverability | Command already exists; spec adds menu and palette entries |

### Host-native commands referenced (no AKML allocation)

| Source | Command | Story | Usage |
|---|---|---|---|
| `guidVSStd97` | `cmdidF1Help` | US7 (FR-104) | F1 routed through `F1HelpListener` — no custom AKML command ID; the listener handles `cmdidF1Help` from `IOleCommandTarget` chain of the focused surface |

## Chord Binding Pattern

Two-key chords (e.g., `Ctrl+B, Ctrl+S`) are bound in each host's VSCT file as:

```xml
<KeyBinding guid="guidAkmlSqlCmdSet" id="CmdSummarizeScript"
            mod1="Control" key1="VK_B"
            mod2="Control" key2="VK_S"
            editor="guidVSStd97" />
```

Single-key bindings (e.g., `F12`) use a single `mod1` / `key1` pair (`mod1` may be `"0"`). The `editor` GUID is `guidVSStd97` for editor-scoped bindings; tool-window-scoped bindings use the appropriate context GUID per host.

## Host VSCT Files Touched

All six host VSCT files receive the same new `<KeyBinding>` entries plus menu-item placements:

- `src/AkmlSql.Ssms20/AkmlSqlSsms20.vsct`
- `src/AkmlSql.Ssms21/AkmlSqlSsms21.vsct`
- `src/AkmlSql.Ssms22/AkmlSqlSsms22.vsct`
- `src/AkmlSql.VS2019/AkmlSqlVS2019.vsct`
- `src/AkmlSql.VS2022/AkmlSqlVS2022.vsct`
- `src/AkmlSql.VS2026/AkmlSqlVS2026.vsct`

The new menu groups are placed under the existing AKML SQL top-level menu in each host. SSMS 21 / 22 / VS 2019 / 2022 / 2026 use the existing `IDG_VS_TOOLS_EXT_TOOLS` mounting; SSMS 20 uses the existing `IDG_VS_MM_TOOLSADDINS` mounting (per `CLAUDE.md` AutoLoad UI Contexts table).

## Invariants

1. **No conflict with existing commands**: every new ID in `0x0900..0x0915` has been verified clear in `PackageGuids.cs` `CommandIds` as of HEAD `3ec5755`.
2. **No conflict with existing host commands** for the new chords: `Alt+S` in SSMS opens the History submenu by default but only when Object Explorer has focus; the Command Palette binding takes effect only when the editor has focus, so the conflict is benign. `F12` and `F2` use `OLECMDERR_E_NOTSUPPORTED` fallthrough so the host's native behavior remains intact when no AKML resolution applies.
3. **Safety check coverage**: `CmdExecuteCurrentBatch` (new) and `CmdExecuteToCursor` (existing, newly chorded) MUST invoke `ExecutionInterceptor.CheckBeforeExecuteAsync` on their about-to-run text. This is non-negotiable per FR-046 / spec 014 US1.
4. **F12 fallthrough**: `CmdScriptAsAlter` MUST return `OLECMDERR_E_NOTSUPPORTED` from `IOleCommandTarget.Exec` when the caret is not on an AKML-resolvable identifier, allowing SSMS's native `F12` (which navigates to function definitions in C# / etc.) to remain functional in mixed-file scenarios.
5. **F2 fallthrough**: same as F12 — `CmdSmartRename` falls through when the caret is not on an AKML-resolvable database identifier; the host's native F2 (rename file etc.) still works.
6. **Command Palette discoverability**: every new command above (and every command newly chorded) MUST appear in `CommandPaletteWindow` via `AkmlCommandSource.GetEntries()`. Per FR-022 / FR-023.
