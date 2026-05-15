# Options Dialog — Redgate SQL Prompt Parity (Design Spec)

> **Status:** Brainstorming-approved 2026-05-06
> **Author:** Mohamed Khamis (with Claude Code)
> **Scope:** Three-phase redesign of the AKML SQL Options dialog to reach full Redgate SQL Prompt feature parity, plus fix the light-theme submenu invisibility bug.
> **Branching:** The current branch `016-wpf-theme-refresh` carries uncommitted Phase 6 follow-up work. That work finishes and merges first. **Phase 1 of this spec is a fresh branch off `master` (or a follow-up branch off whatever `016-*` lands as)** — the option-redesign work does not piggyback on the WPF-theme-refresh branch. Each subsequent phase is its own branch off the previous one's merge commit.

---

## 1. Problem Statement

The current AKML Options dialog (`src/AkmlSql.Shell.Shared/Dialogs/SettingsWindow.cs`, ~3,000 LoC code-only WPF) has three problems:

1. **Light-theme submenu invisibility bug.** Nested `TreeViewItem` text renders white-on-white in the light theme until selected. Cause: `_navTree.Resources[SystemColors.ControlTextBrushKey]` is overridden to `_theme.SelectedText` (white), and `ItemContainerStyle.Setters` don't cascade reliably to manually-added child `TreeViewItem`s. Children fall back to the overridden white brush.
2. **Visual quality is sub-par.** Cramped spacing, weak page chrome, no per-page Restore-Defaults link, no bottom Import/Export bar, missing zebra row styling consistency.
3. **Functional gaps vs. Redgate SQL Prompt.** No "Types of suggestion" / "Qualification & Brackets" / "INSERT statements" / "JOIN completion" / "Labs" pages. No multi-style management for formatting (single `Default` profile only). No Redgate `.sqlpromptstyle` import. No environment color editor under Tabs › Color.

The user has chosen **full Redgate parity**, **hybrid tree structure** (Redgate names where parity exists, AKML grouping for AKML-extras), **multi-style management with built-in styles + Redgate import**, and a **three-phase delivery**.

## 2. Goals & Non-Goals

### Goals

- Fix the light-theme submenu bug as a global theme-correctness pass (audit other dialogs for the same pattern).
- Restructure the page tree to mirror Redgate SQL Prompt's IA, with AKML-only sections grouped under sensible parent nodes (Editor, Tabs, etc.).
- Add the five missing SQL Prompt pages: `Suggestions › Types`, `Inserted Code › Qualification & Brackets`, `Inserted Code › INSERT statements`, `Inserted Code › JOIN completion`, `Miscellaneous › Labs`.
- Ship a bottom button bar with `Restore All Defaults`, `Import…`, `Export…` (operating on the existing `config.json` schema).
- Upgrade `ProfileEditorDialog` into a full Format Styles Editor with multi-style management.
- Ship 4 read-only built-in styles (`Compact`, `Aligned`, `Verbose`, `Redgate Compatible`).
- Implement a `.sqlpromptstyle` → `.akmlstyle` translator with best-effort mapping and an explicit warning report for unsupported settings.
- Build an environment color editor reachable from `Tabs › Color`.
- Split `SettingsWindow.cs` into per-page files for maintainability.

### Non-Goals (Out of Scope)

- Localization of dialog strings (English only).
- Accessibility audit (tab order, screen reader labels).
- Per-project `.akmlsettings` override of the active style.
- Importing the full Redgate `.sqlpromptoptionsettings` (only `.sqlpromptstyle` translation).
- Cloud-sync of styles via Redgate Platform.

## 3. Locked Design Decisions

These came out of the brainstorming Q&A and are not up for re-debate during implementation:

| Decision | Choice |
|---|---|
| Scope level | C — Full Redgate parity |
| Tree structure | Option B — Hybrid (Redgate names where parity exists, AKML grouping for extras) |
| Format styles model | A — Full multi-style with shipped read-only built-ins |
| Style file imports supported | `.akmlstyle` (native) + `.sqlpromptstyle` (Redgate via translator) |
| Format-trigger toggles (FormatOnPaste, FormatOnSave, etc.) | Stay on the Options page (global), NOT inside per-style files |
| Settings export format | Existing `config.json` schema (no separate XML format) |
| Delivery model | Approach B — Three phases, separate PRs |
| Refactoring page placement | Moves from "Inserted Code" to "Editor" group |
| Format Styles Editor opening pattern | Modal child of Options dialog (Options stays under, dimmed) |
| Built-in style action `Copy` | Allowed; produces a writable user-styles copy as the canonical "create custom style" workflow |

## 4. Final Tree Structure

```
▾ Suggestions
  • Behavior                  (existing IntelliSense page, Phase 1 polish)
  • Types of suggestion       Phase 2 NEW
  • Database                  (existing Schema Cache page)
▾ Inserted Code
  • Qualification & Brackets  Phase 2 NEW
  • INSERT statements         Phase 2 NEW
  • JOIN completion           Phase 2 NEW
▾ Format
  • Styles                    Phase 3 REWORKED — dropdown + button only
▾ Editor
  • Productivity              (existing)
  • Navigation                (existing)
  • Refactoring               (moved from Inserted Code in Phase 1)
▾ Queries
  • History                   (existing)
  • Execution Warnings        (existing Safety)
  • Query Results             (existing Grid)
  • Execution                 (existing)
▾ Tabs
  • Color                     Phase 3 ENHANCED — env editor button
  Code Analysis               (leaf, existing)
  Snippets                    (leaf, existing)
  AI Assistance               (leaf, existing Prompt AI)
▾ Miscellaneous
  • Main                      (existing General)
  • Labs                      Phase 2 NEW
```

## 5. Architecture

### 5.1 Component boundaries

- **`SettingsWindow`** (post-Phase 2): owns dialog chrome only (sidebar, content host, search, bottom bar). ~600 LoC.
- **`Dialogs/Pages/*Page.cs`** (Phase 2 split): one file per tree leaf. Each implements `IPageBuilder.Build(PageContext ctx) → UIElement` and exposes a `*Controls` POCO that `SettingsWindow.LoadSettings`/`SaveSettings` iterate.
- **`PageContext`**: passes the active `ThemeBrushSet`, the live `AppSettings` reference, the `RowFactory` for consistent row styling, and the `RegisterSearchEntry` callback.
- **`RowFactory`**: single source of truth for `AddToggle` / `AddDropdown` / `AddSlider` / `AddTextBox` / `AddInfoRow` / `AddReadOnlyField`. Enforces zebra striping via a row counter.
- **`ProfileEditorDialog`** (Phase 3): Format Styles Editor. Three columns — Style List | Category Tree + Options | Live Preview.
- **`ProfileEditorViewModel`**: holds `UserStyles`, `BuiltInStyles`, `SelectedStyle`, `ActiveStyle`, `IsDirty`. Pure VM, no WPF deps.
- **`RedgateStyleImporter`**: pure data, no UI. `Import(path) → ImportResult(Style, IReadOnlyList<ImportWarning>)`.
- **`EnvironmentColorEditor`**: small modal child window over Options. Reuses existing `TabColoringManager` and `ColoringRule` model.
- **Built-in styles**: `src/AkmlSql.Formatting/Profiles/BuiltInStyles/*.akmlstyle`, deployed to `<extension>\BuiltInStyles\` by the installer.

### 5.2 Files affected

| Change | Path | Purpose |
|---|---|---|
| Modify | `src/AkmlSql.Shell.Shared/Dialogs/SettingsWindow.cs` | Bug fix, polish, restructure, Import/Export bar (P1+P2) |
| Modify | `src/AkmlSql.Shell.Shared/Ui/ProfileEditorDialog.cs` | 3-column layout, Style List panel, action toolbar (P3) |
| Modify | `src/AkmlSql.Shell.Shared/Ui/ProfileEditorViewModel.cs` | Multi-style state (P3) |
| New | `src/AkmlSql.Shell.Shared/Dialogs/Pages/IPageBuilder.cs` | Page interface (P2) |
| New | `src/AkmlSql.Shell.Shared/Dialogs/Pages/PageContext.cs` | Per-page state container (P2) |
| New | `src/AkmlSql.Shell.Shared/Dialogs/Pages/RowFactory.cs` | Row-builder helpers + zebra striping (P2) |
| New | `src/AkmlSql.Shell.Shared/Dialogs/Pages/SuggestionTypesPage.cs` | (P2) |
| New | `src/AkmlSql.Shell.Shared/Dialogs/Pages/QualificationPage.cs` | (P2) |
| New | `src/AkmlSql.Shell.Shared/Dialogs/Pages/InsertStatementsPage.cs` | (P2) |
| New | `src/AkmlSql.Shell.Shared/Dialogs/Pages/JoinCompletionPage.cs` | (P2) |
| New | `src/AkmlSql.Shell.Shared/Dialogs/Pages/LabsPage.cs` | (P2) |
| Move/refactor | `src/AkmlSql.Shell.Shared/Dialogs/Pages/<each>.cs` | All 14 existing pages migrated to per-file (P2) |
| New | `src/AkmlSql.Shell.Shared/Tabs/EnvironmentColorEditor.cs` | Modal sub-dialog (P3) |
| New | `src/AkmlSql.Formatting/Profiles/RedgateStyleImporter.cs` | `.sqlpromptstyle` translator (P3) |
| New | `src/AkmlSql.Formatting/Profiles/BuiltInStyles/Compact.akmlstyle` | Built-in (P3) |
| New | `src/AkmlSql.Formatting/Profiles/BuiltInStyles/Aligned.akmlstyle` | Built-in (P3) |
| New | `src/AkmlSql.Formatting/Profiles/BuiltInStyles/Verbose.akmlstyle` | Built-in (P3) |
| New | `src/AkmlSql.Formatting/Profiles/BuiltInStyles/RedgateCompatible.akmlstyle` | Built-in (P3) |
| Modify | `src/AkmlSql.Core/Config/AppSettings.cs` | New POCOs: `LabsSettings`, `IntelliSenseSettings.Qualification`, `IntelliSenseSettings.InsertOptions`, `IntelliSenseSettings.JoinOptions`, `IntelliSenseSettings.SuggestionTypes` (P2) |
| Modify | `src/AkmlSql.Shell.Shared/Tabs/TabColoringManager.cs` | Surface "edit environments" entry point (P3) |
| Modify | `src/AkmlSql.Installer/AkmlSqlSetup.iss` | Deploy `BuiltInStyles/*.akmlstyle` (P3) |
| New tests | `tests/AkmlSql.Shell.Shared.Tests/` | New test project for chrome + VM tests (P1) |
| New tests | `tests/AkmlSql.Engine.Tests/Profiles/RedgateStyleImporterTests.cs` | (P3) |
| New tests | `tests/AkmlSql.Engine.Tests/Profiles/BuiltInStylesTests.cs` | (P3) |
| New tests | `tests/AkmlSql.Engine.Tests/Config/SettingsImportExportTests.cs` | (P2) |
| New fixtures | `tests/AkmlSql.Engine.Tests/Fixtures/RedgateStyles/*.sqlpromptstyle` | 5+ real Redgate exports (P3) |

## 6. Phase 1 — Bug fix + Polish + Restructure (1–2 days)

### 6.1 Light-theme submenu bug fix

**Cause chain (verified against lines 513–540):**

1. Line 524 `itemStyle.Setters.Add(new Setter(Control.ForegroundProperty, _theme.FgPrimary))` — base setter exists, so any TreeViewItem the style reaches gets a dark foreground (visible on white sidebar).
2. Line 540 `_navTree.ItemContainerStyle = itemStyle;` — applies the style only to *direct* TreeView children (parent group items). WPF's `ItemContainerStyle` does NOT cascade to grandchildren — each parent `TreeViewItem` has its own `ItemContainerStyle` that defaults to null for *its* children.
3. Line 520 `_navTree.Resources[SystemColors.ControlTextBrushKey] = _theme.SelectedText;` — overrides the default control-text brush to `_theme.SelectedText` (white in light theme). Anything inside `_navTree` that resolves to `ControlTextBrushKey` becomes white.

**Why parents render but children don't:** parents get explicit Foreground=FgPrimary from `ItemContainerStyle` (factor 1+2 working). Children miss the style cascade (factor 2 fails for grandchildren) → fall back to default Foreground resolution → hit the overridden `ControlTextBrushKey` (factor 3) → white-on-white.

**The minimal correct fix** (one line):

```csharp
// Line 540: change from
_navTree.ItemContainerStyle = itemStyle;
// to
_navTree.Resources[typeof(TreeViewItem)] = itemStyle;
```

Implicit style by type matches every `TreeViewItem` in the visual subtree regardless of nesting depth. With every TreeViewItem styled, factor 3 (`ControlTextBrushKey` override) becomes irrelevant — nothing falls back through that path.

**Defense-in-depth (optional, recommended):** also drop lines 519–520. The `ControlTextBrushKey` and `ControlBrushKey` overrides aren't needed once items are styled, and they would silently bite any future code that adds a non-styled control inside the tree. Removing them is safe but not required to fix this bug.

**Audit:** grep for `SystemColors.ControlTextBrushKey` across `src/AkmlSql.Shell.Shared/`. If found in `HistoryDiffWindow.cs`, `SafetyWarningDialog.cs`, or other dialogs, audit and fix in same PR (likely the same pattern was copied).

### 6.2 Visual polish

| Element | Change |
|---|---|
| Page title | Accent blue (`TextLink` token), 14pt semibold, breadcrumb format `Suggestions › Behavior` |
| Restore Defaults | Top-right of page header, blue underlined link, resets only the current page |
| Group headers | Section caps: 12pt semibold + 8px top padding + 1px subtle bottom border in `BorderSubtle` |
| Setting rows | Consistent zebra striping via row counter (no hardcoded alt bg) |
| Tree text | Wrapped in `TextBlock` with `Padding = 8,4` for bigger click targets |
| Page padding | `16,16,16,16` → `20,18,28,18` |
| Sidebar width | `220` → `240` |

### 6.3 Tree restructure (relabel only)

| Old path | New path |
|---|---|
| `Inserted Code › Refactoring` | `Editor › Refactoring` |
| Leaf `Editor › Productivity` | Nested under `Editor` group |
| Leaf `Editor › Navigation` | Nested under `Editor` group |
| `Tabs (Color)` | `Tabs › Color` |
| `Miscellaneous` | `Miscellaneous › Main` |

No data migration. `AppSettings` schema unchanged.

### 6.4 Phase 1 tests

- `WindowChromeTests.TreeViewItems_AllVisibleInLightTheme` — render check, every `TreeViewItem.Foreground.Color != Sidebar.Color`
- `WindowChromeTests.TreeViewItems_AllVisibleInDarkTheme`
- `WindowChromeTests.PageHeader_HasRestoreLink` — every page has the link
- Manual: open in both themes, visually verify

### 6.5 Phase 1 acceptance criteria

Phase 1 is **only** complete when, with both themes:

1. Every TreeViewItem at every depth shows its label clearly (the original bug fix).
2. The dialog visually reads at parity with `doc/SQL-PROMPT/SQL-Prompt-Option/13_options_dialog.svg`: blue accented page title + breadcrumb, top-right Restore-Defaults link, group caps with subtle borders, consistent zebra rows, sidebar with adequate spacing.
3. The grep audit (§6.1) has been run and any other occurrences of `SystemColors.ControlTextBrushKey` in `src/AkmlSql.Shell.Shared/` are either justified or fixed.
4. No regression in dark theme — open the dialog cold in dark theme, navigate every existing page, settings load and save correctly.

The user's original complaint was "very bad UI" — these criteria are the explicit verification bar. PR description must show side-by-side screenshots (light and dark) before merge.

## 7. Phase 2 — New pages + Bottom bar (3–4 days, mostly the split)

**Effort breakdown:** the page-file split is most of Phase 2 — realistically 2–3 days alone given the ~80 control fields and the tightly-coupled `LoadSettings`/`SaveSettings` indirection. The five new pages and the bottom bar add 1 day on top. Plan accordingly: don't promise the new pages early in the phase.

### 7.1 Page-file split refactor

Refactor `SettingsWindow.cs` (~3,000 LoC) into per-page files in `src/AkmlSql.Shell.Shared/Dialogs/Pages/`. Each file ~80–250 LoC. `SettingsWindow.cs` shrinks to ~600 LoC: chrome + page registration only. Order of operations: split smallest page (Snippets, ~5 settings) first as a template; validate Save/Load round-trip; then template the rest. Each split lands as a separate commit so regressions bisect to a specific page.

The split is the single largest chunk of mechanical work in this entire effort. There's no clean way to shortcut it — every page reads from and writes to its own subset of `AppSettings`, and those reads/writes currently sit on private fields scattered across `SettingsWindow.cs`. The per-page `*Controls` POCO returned by `Build` is the only safe indirection.

### 7.2 New AppSettings sub-objects

```csharp
// Added to IntelliSenseSettings
public SuggestionTypesSettings SuggestionTypes { get; set; } = new();
public QualificationSettings Qualification { get; set; } = new();
public InsertOptionsSettings InsertOptions { get; set; } = new();
public JoinOptionsSettings JoinOptions { get; set; } = new();

// New top-level
public LabsSettings Labs { get; set; } = new();

public class SuggestionTypesSettings
{
    public bool IncludeSystemObjects { get; set; }              // default false
    public bool SuggestAllColumnsAfterSelect { get; set; }      // default false
    public ColumnSuggestionScope ColumnScope { get; set; }      // default ReferencedOnly
    public bool IncludeKeywords { get; set; } = true;
    // ShowSnippets is read from existing SnippetSettings
}

public enum ColumnSuggestionScope { All, ReferencedOnly }

public class QualificationSettings
{
    public SchemaQualifyMode SchemaMode { get; set; } = SchemaQualifyMode.NonDefaultOnly;
    public BracketMode BracketMode { get; set; } = BracketMode.WhenRequired;
    public bool QualifyColumnsWithTableOrAlias { get; set; } = true;
}

public enum SchemaQualifyMode { Always, NonDefaultOnly, Never }
public enum BracketMode { Always, WhenRequired, Never }

public class InsertOptionsSettings
{
    public bool IncludeColumns { get; set; } = true;
    public bool IncludeDefaultsAsComments { get; set; } = true;
    public bool IncludeProcParamInfo { get; set; } = true;
    // IncludeDataTypeComments reads existing FormatterSettings.InsertColumnsIncludeTypes
}

public class JoinOptionsSettings
{
    public bool MatchByColumnName { get; set; } = true;
    // SuggestJoinConditions reads existing IntelliSenseSettings.JoinAssist
    // AutoGenerateAlias reads existing IntelliSenseSettings.AutoAlias
}

public class LabsSettings
{
    public bool GhostTextCompletion { get; set; }
    public bool ParallelSchemaCache { get; set; }
    public bool SharedSnippetSync { get; set; }
}
```

All fields default-construct, so old `config.json` files load without migration.

### 7.3 Engine wiring

- `CompletionEngine.QualifyObjects` is replaced by reads from `Qualification.SchemaMode` / `BracketMode` / `QualifyColumnsWithTableOrAlias`.
- `CompletionEngine` honors `SuggestionTypes.IncludeSystemObjects`, `IncludeKeywords`, `ColumnScope`.
- `WildcardExpansionHandler` honors `InsertOptions.IncludeColumns` / `IncludeDefaultsAsComments` / `IncludeProcParamInfo`.
- `JoinOnFkProvider` honors `JoinOptions.MatchByColumnName`.
- Each new flag is wired in the same commit as the option, with an integration test asserting end-to-end behavior.

### 7.4 Bottom button bar

Three buttons added to the left of the existing OK/Cancel:

| Button | Behavior |
|---|---|
| Restore All Defaults | Confirmation dialog. On confirm: `_settings = AppSettings.Defaults();` rebuild every page from fresh settings (dialog stays open). |
| Import… | OpenFileDialog `*.json`. Validates parse. On success: applies and rebuilds. On parse failure: error with `JsonException` line/column. |
| Export… | SaveFileDialog `*.json`, default name `akmlsql-options-YYYY-MM-DD.json`. Writes current dialog state via existing `JsonOptions.Default`. |

Format reuses the existing `config.json` schema. No XML, no per-style/.casettings/snippet inclusion.

### 7.5 Phase 2 tests

- `SettingsImportExportTests.RoundTrip_Defaults` — defaults → JSON → parse → equals defaults
- `SettingsImportExportTests.RoundTrip_AllNewFields`
- `SettingsImportExportTests.Import_RejectsInvalidJson`
- `SettingsImportExportTests.Import_TolerantOfMissingFields` — back-compat safety net
- `PageBuilderTests.AllPagesBuildWithoutThrowing`
- `EnginePolicyTests.NewQualificationFlags_AffectCompletion`
- Manual: click each new button, verify round-trip works on disk

## 8. Phase 3 — Style Editor + Redgate import + Env colors (4–6 days)

### 8.1 Format › Styles options page (slimmed)

Two controls only:

- `Active style:` dropdown (Your + Built-in, with ✓ marker on the active one)
- `[ Edit Formatting Styles… ]` button → opens `ProfileEditorDialog` modal over Options

Plus the **Format triggers** group (FormatOnPaste, FormatOnSave, FormatOnDelimiter, ConfirmBulk, CreateBackups, RespectNoformat, SemanticValidation) — these are workflow settings, not formatting rules, so they stay on the Options page.

### 8.2 Format Styles Editor — 3-column layout

Existing `ProfileEditorDialog` is 1100×750 with two columns. Adding a Style List column likely requires bumping width. **Target: 1280×750**, but tune empirically — the goal is no horizontal scrollbar and the Live Preview still showing 60-char SQL lines without wrapping.

```
┌─ Edit Formatting Styles ────────────────────────────────────[~1280×750]┐
│ STYLE LIST   │ CATEGORY TREE         │ OPTIONS + LIVE PREVIEW         │
├──────────────┼───────────────────────┼────────────────────────────────┤
│ Your Styles  │ ▾ Global              │  ┌─ Casing ──────────────┐     │
│ ✓ My Style   │   Whitespace          │  │ Keyword case: UPPER  ▼│     │
│   Team Style │   Lists               │  │ Function case: lower ▼│     │
│              │   Parentheses         │  │ ...                   │     │
│ AKML Styles  │   Casing  ◄─selected  │  └───────────────────────┘     │
│   Compact 🔒 │ ▾ Statements          │                                │
│   Aligned 🔒 │   ...                 │  ┌─ Live Preview ────────┐     │
│   Verbose 🔒 │ ▾ Clauses             │  │ ...                   │     │
│   Redgate 🔒 │ ▾ Expressions         │  │ ↓ formatted ↓         │     │
│ ──────────── │ ▾ Other               │  │ ...                   │     │
│ [+ Create]   │                       │  └───────────────────────┘     │
│ [⎘ Copy]     │                       │                                │
│ [✎ Rename]   │                       │                                │
│ [✕ Delete]   │                       │                                │
│ [↑ Import]   │                       │                                │
│ [↓ Export]   │                       │                                │
└──────────────┴───────────────────────┴────────────────────────────────┘
                                                  [Cancel] [Save] [Save & Apply]
```

🔒 = read-only (built-in). When a built-in is selected, right-pane controls render disabled; toolbar shows only `Copy` and `Export`.

### 8.3 ProfileEditorViewModel state

```csharp
public ObservableCollection<StyleEntry> UserStyles { get; }       // %AppData%\AKML SQL\Styles\*.akmlstyle
public ObservableCollection<StyleEntry> BuiltInStyles { get; }    // <ext>\BuiltInStyles\*.akmlstyle (read-only)
public StyleEntry SelectedStyle { get; set; }                     // currently being edited
public StyleEntry ActiveStyle { get; set; }                       // = FormatterSettings.ActiveProfile
public bool IsDirty { get; }                                      // unsaved changes on SelectedStyle
public event EventHandler<SaveDirtyRequest> OnSwitchWhileDirty;   // raised by view to prompt
```

`Save & Apply` writes `SelectedStyle` to disk *and* sets `FormatterSettings.ActiveProfile = SelectedStyle.Name` (atomic temp+rename, same pattern as ConfigManager).

### 8.4 Built-in styles

Four files in `src/AkmlSql.Formatting/Profiles/BuiltInStyles/`:

| Style | Hallmark settings |
|---|---|
| Compact | No blank lines between statements; JOIN keyword on same line as table; max line 120; no per-column INSERT formatting |
| Aligned | Right-aligned keywords; columns aligned in lists; blank line between statements; max line 100 |
| Verbose | Every clause on its own line; max line 80; mandatory parens around CASE; expanded CTE bodies |
| Redgate Compatible | Settings tuned to mirror SQL Prompt's factory defaults |

Built-ins ship as JSON (same `.akmlstyle` schema). Installer deploys to `<extension>\BuiltInStyles\`. `BuiltInStyles` directory is loaded read-only at editor open time. **`Copy` is the canonical "create custom style" entry point** — pick a built-in, click Copy, name your style, edit freely.

### 8.5 Style file actions

| Action | Behavior |
|---|---|
| Create | Modal: Name + base style. Validate name (unique, no path separators, no reserved chars). Write `<name>.akmlstyle`. |
| Copy | Modal: Name (suggest "Copy of *X*"). Copy file. Works on built-ins and user styles. |
| Rename | F2 or right-click. Rename file on disk. If renaming active style, atomically update `FormatterSettings.ActiveProfile`. Disabled for built-ins. |
| Delete | Confirmation. Disabled for built-ins and active style. Delete file. |
| Export | SaveFileDialog → `.akmlstyle` JSON. Works for user and built-ins. |
| Import | OpenFileDialog with `*.akmlstyle` and `*.sqlpromptstyle` filters. Native: copy to user styles dir. Redgate: route to `RedgateStyleImporter`. |

### 8.6 Redgate `.sqlpromptstyle` importer

`src/AkmlSql.Formatting/Profiles/RedgateStyleImporter.cs`:

```csharp
public sealed record ImportResult(AkmlStyle Style, IReadOnlyList<ImportWarning> Warnings);
public sealed record ImportWarning(string RedgateKey, string Reason, string DefaultedTo);

public static class RedgateStyleImporter
{
    public static ImportResult Import(string sqlpromptstylePath);
}
```

Translation strategy — static `Dictionary<string, IRedgateMapping>` keyed by Redgate JSON property path:

- **Direct map (~70%):** same concept, value translates 1:1
- **Compatible map (~15%):** concept exists with different shape; lossy → warning
- **Unmapped (~15%):** Redgate has it, AKML doesn't → AKML default + warning

Phase 3 starts with a **1-day spike**: collect 8–10 real `.sqlpromptstyle` exports (team members + public GitHub dotfiles), hand-map their fields. Lock the translation table from spike output. Anything not seen in spike defaults gracefully with a warning.

After import, UI shows:

```
Imported "MyTeamStyle.sqlpromptstyle" as "MyTeamStyle"
✓ 47 settings translated
⚠ 8 settings not yet supported by AKML — see details
   • InsertStatements.AlignAssignmentOperators (using AKML default)
   • CTE.AlignCommas (using AKML default)
   ...
   [ Show all ]   [ Open in Editor ]   [ OK ]
```

### 8.7 Environment color editor

Sub-dialog opened from a button on `Tabs › Color`. Reuses existing `TabColoringManager` and `ColoringRule` model — purely UI work.

```
┌─ Environments & Tab Colors ────────────────────────────────────[680×520]┐
│  Environments                                                            │
│  ●  Production       #E74C3C    [Edit] [Delete]                         │
│  ●  Staging          #F39C12    [Edit] [Delete]                         │
│  ●  Testing          #3498DB    [Edit] [Delete]                         │
│  ●  Development      #2ECC71    [Edit] [Delete]                         │
│  ●  Local            #95A5A6    [Edit] [Delete]                         │
│  [+ Add Environment]                                                     │
│                                                                          │
│  Assignments                                                             │
│  ┌────────┬────────────────────┬──────────────┬──────────┐              │
│  │ Type   │ Pattern            │ Environment  │ Priority │              │
│  ├────────┼────────────────────┼──────────────┼──────────┤              │
│  │ Server │ *.prod.example.com │ Production   │ 3        │              │
│  │ Db     │ *_dev              │ Development  │ 4        │              │
│  └────────┴────────────────────┴──────────────┴──────────┘              │
│  [+ Add]  [Edit]  [Remove]    Priority order applied bottom→top         │
│  ☑ Use gradient colors on tabs                                          │
│                                              [Apply]  [OK]  [Cancel]    │
└──────────────────────────────────────────────────────────────────────┘
```

Add Assignment sub-dialog: type dropdown (Server/Database/Group) + pattern textbox with `*` wildcard support + environment dropdown. Pattern parsed by `EnvironmentDetector` for validation.

### 8.8 Phase 3 tests

- `RedgateStyleImporterTests.Import_RedgateDefault` — Redgate "Default" style imports clean
- `RedgateStyleImporterTests.Import_RedgateCompact`
- `RedgateStyleImporterTests.Import_PartiallySupported` — mix of supported + unsupported + correct warnings
- `RedgateStyleImporterTests.Import_MalformedJson` — bad JSON → throws with line info
- `BuiltInStylesTests.AllBuiltInsParse` — all 4 ship as valid `.akmlstyle`
- `BuiltInStylesTests.AllBuiltInsFormatSampleSql` — each built-in formats a 30-line canonical SQL fixture without throwing
- `ProfileEditorViewModelTests.SwitchingDirty_PromptsForSave`
- `ProfileEditorViewModelTests.RenameActive_UpdatesAppSettings`
- `ProfileEditorViewModelTests.DeleteActive_IsRejected`
- `EnvironmentColorEditorTests.RoundTrip_AssignmentRules`
- Manual: real-world Redgate import, env editor end-to-end

## 9. Risks (ranked)

### High

- **R1. Redgate `.sqlpromptstyle` schema is undocumented.** No public reference; reverse-engineered from samples.
  - *Mitigation:* 1-day spike at start of Phase 3 to lock the translation table. Cap at v10+ if v9 differs dramatically.

- **R2. Page-file split refactor breaks Save/Load.** ~80 control fields all directly accessed by `LoadSettings`/`SaveSettings`.
  - *Mitigation:* Smallest page (Snippets) first as template. Each page split as a separate commit so regressions bisect.

### Medium

- **R3. Built-in style content tuning.** Compact/Aligned/Verbose/Redgate-Compatible need real SQL fixtures to validate.
  - *Mitigation:* 30-line canonical SQL fixture in `AllBuiltInsFormatSampleSql`. Visual review in PR.

- **R4. Multi-style state edge cases.** Rename active, delete only style, name collision, concurrent edit.
  - *Mitigation:* Each edge case has an explicit `ProfileEditorViewModelTests` test. File-system ops in try/catch with graceful UI errors.

### Low

- **R5. AppSettings backwards compatibility.** Old `config.json` predates new fields.
  - *Mitigation:* All new fields default-construct. `RoundTrip_TolerantOfMissingFields` test enforces this.

- **R6. Engine integration of new IntelliSense settings.**
  - *Mitigation:* Each flag has an `EnginePolicyTests` test asserting end-to-end behavior. Wired in same PR as option.

- **R7. Theme bug exists elsewhere.** `HistoryDiffWindow`, `SafetyWarningDialog`, others may use `ControlTextBrushKey` override.
  - *Mitigation:* Phase 1 grep audit; fix any other occurrences in same PR.

## 10. Out of Scope

- Localization of dialog strings.
- Accessibility audit (tab order, screen reader labels).
- Per-project `.akmlsettings` override of active style.
- Full Redgate `.sqlpromptoptionsettings` import (only `.sqlpromptstyle`).
- Redgate Platform cloud sync.

## 11. References

- `doc/SQL-PROMPT/SQL-Prompt-Option/SQL_Prompt_Options_Dialog.md` — source-of-truth Redgate IA reference
- `doc/SQL-PROMPT/SQL-Prompt-Option/13_options_dialog.svg` — visual mockup
- `doc/SQL-PROMPT/SQL-Prompt-Option/14_format_styles_editor.svg` — Style editor mockup
- `src/AkmlSql.Shell.Shared/Dialogs/SettingsWindow.cs` — current dialog implementation
- `src/AkmlSql.Shell.Shared/Ui/ProfileEditorDialog.cs` — current style editor
- `src/AkmlSql.Core/Config/AppSettings.cs` — settings schema
- `src/AkmlSql.Shell.Shared/Tabs/TabColoringManager.cs` — environment coloring backend
- `CLAUDE.md` § "WPF UI conventions" — theming and dialog rules to follow
