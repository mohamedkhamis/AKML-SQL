# Implementation Plan: WPF Theme & Visual Style Refresh

**Branch**: `016-wpf-theme-refresh` | **Date**: 2026-04-30 | **Spec**: [spec.md](./spec.md)
**Input**: Feature specification from `/specs/016-wpf-theme-refresh/spec.md`

## Summary

The Options window and ~24 other AKML-owned WPF surfaces look unfinished, drift visually from each other, and behave poorly when the host (SSMS / VS) is in Dark or Light theme. This plan delivers a single, code-only WPF design system: a centralized **token registry** of named semantic brushes, a **ResourceDictionary-based live-switch mechanism** that all AKML windows share, a **redesigned Options window** as the visual reference, and **incremental migration** of every other surface onto the same tokens. Editor margins and popups follow last. The existing `ThemeManager` becomes a thin, deprecated facade during migration so surfaces can move one at a time without a flag day.

## Technical Context

**Language/Version**: C# 12 (LangVersion `latest`) on .NET Framework 4.7.2 (shell projects); Core dual-targets `netstandard2.0` + `net10.0`.
**Primary Dependencies**: WPF (PresentationFramework / PresentationCore / WindowsBase via .NET Framework), Visual Studio SDK (15.9.3 / 16.0.208 / 17.14.x), Serilog 4.x. No new NuGet packages introduced by this work.
**Storage**: `%AppData%/AKML SQL/config.json` (existing — extends only the existing `Theme` field; no schema migration).
**Testing**: xunit (Core tests only — shell projects have no test harness; design-system verification is manual against the design reference plus a one-off audit script).
**Target Platform**: Windows; SSMS 20 / 21 / 22, VS 2019 / 2022 / 2026 (six host targets, all consuming `AkmlSql.Shell.Shared` via `.projitems`).
**Project Type**: Visual Studio / SSMS extension (desktop, in-process WPF).
**Performance Goals**: Theme switch propagates to every open AKML window within 1 second (FR-008 / SC-004). Options window cold-open time stays within 10% of today's baseline (SC-006). No allocation-per-paint regressions (frozen brushes preserved).
**Constraints**: Code-only WPF — no XAML files (mandated by `.projitems` shared-project pattern documented in `CLAUDE.md`). Single source compiled against six VS SDK versions. No reliance on `Application.Current` lifecycle (host owns it). WCAG AA contrast for body text and primary actions in both themes (FR-010 / SC-005).
**Scale/Scope**: ~13 modal dialogs + 5 dockable tool windows + ~6 editor margins/adornments = ~24 surfaces touched. ~30 semantic tokens in the new registry. Single `ThemeManager` consolidated; ~30 obsoleted properties retired as migration completes.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

No constitution file exists at `.specify/memory/constitution.md`. Default project conventions documented in `CLAUDE.md` (the **WPF UI conventions** and **Editor margin spinner pattern** sections) act as the de-facto rule set this plan must respect. Concretely:

- **Code-only WPF** (no XAML) — preserved by all design choices below.
- **`ThemeManager.Instance` for all chrome colors** — enforced by the new token registry, which `ThemeManager` becomes a thin facade over during migration.
- **Frozen brushes** — preserved; the new `ThemeRegistry` returns frozen `SolidColorBrush` instances and swaps them whole on theme change rather than mutating in place.
- **Hoist `FontFamily` to static readonly** — enforced by the new design system (single `Typography` static class).
- **Set `Owner` via DTE HWND** for modal dialogs — preserved; the new `ThemeAwareWindow` base class encapsulates this.
- **FR-005 safety dialog cancel-button discipline** — preserved by leaving `SafetyWarningDialog` semantics untouched.

**Result**: GATE PASSES. No deviations require justification in the Complexity Tracking section.

**Re-check after Phase 1 design**: GATE STILL PASSES. The Phase 1 contracts (`theme-tokens.md`, `theme-aware-surface.md`) and the data model entrench these conventions — they do not introduce XAML, do not require `Application.Current`, do not mutate frozen brushes, and do not change `SafetyWarningDialog` semantics. The migration sequence keeps `ThemeManager` as the single chrome-color authority throughout.

## Project Structure

### Documentation (this feature)

```text
specs/016-wpf-theme-refresh/
├── plan.md              # This file (/speckit.plan command output)
├── spec.md              # Feature spec (already exists)
├── research.md          # Phase 0 output (/speckit.plan command)
├── data-model.md        # Phase 1 output (/speckit.plan command)
├── quickstart.md        # Phase 1 output (/speckit.plan command)
├── contracts/           # Phase 1 output (/speckit.plan command)
│   ├── theme-tokens.md          # Public token catalog (semantic role → key → Dark/Light values)
│   └── theme-aware-surface.md   # Contract every WPF surface must satisfy
├── checklists/
│   └── requirements.md  # Spec quality checklist (already exists)
└── tasks.md             # Phase 2 output (/speckit.tasks — NOT created by /speckit.plan)
```

### Source Code (repository root)

This work touches only `src/AkmlSql.Shell.Shared/` (the shared `.projitems`). Project layout below shows the new and modified subtrees only — unchanged subtrees are omitted.

```text
src/AkmlSql.Shell.Shared/
├── Ui/
│   ├── Theme/                          # NEW — design system home
│   │   ├── ThemeTokens.cs              # Public string constants for every semantic token key
│   │   ├── ThemeRegistry.cs            # Internal: ResourceDictionary, brush swap on variant change
│   │   ├── ThemeVariant.cs             # Enum: Light, Dark, HighContrast
│   │   ├── ThemePalette.cs             # Internal: token → Color mapping per variant
│   │   ├── Typography.cs               # NEW — static FontFamily/sizes/weights
│   │   ├── Spacing.cs                  # NEW — static spacing scale (4/8/12/16/24/32 px)
│   │   ├── ThemeAwareWindow.cs         # Base Window — merges registry, sets DTE owner, theme-change subscribe
│   │   ├── ThemeAwareUserControl.cs    # Base UserControl — same for tool-window content
│   │   └── HostThemeWatcher.cs         # NEW — subscribes to VSColorTheme.ThemeChanged and HighContrast change
│   └── ThemeManager.cs                 # MODIFIED — thin facade over ThemeRegistry; properties marked [Obsolete]
├── Dialogs/
│   ├── SettingsWindow.cs               # MODIFIED — rebuilt against new tokens (P1 reference surface)
│   ├── SettingsDialog.cs               # DELETED — legacy, per spec Q2 default
│   ├── AboutDialog.cs                  # MODIFIED — migrated to tokens (P2)
│   ├── BulkAnalysisResultDialog.cs     # MODIFIED — migrated to tokens (P2)
│   ├── LogViewerDialog.cs              # MODIFIED — migrated to tokens (P2)
│   └── ...                             # all dialogs in inventory (P2)
├── History/
│   ├── HistoryToolWindowControl.cs     # MODIFIED — migrated to tokens (P2)
│   └── HistoryDiffWindow.cs            # MODIFIED — migrated to tokens (P2)
├── Ai/
│   ├── AiChatToolWindow.cs             # MODIFIED — migrated to tokens (P2)
│   └── TextToSqlInputDialog.cs         # MODIFIED — migrated to tokens (P2)
├── Productivity/                       # Tool windows + dialogs migrated to tokens (P2)
├── Snippets/SnippetManagerDialog.cs    # MODIFIED — migrated (P2)
├── Refactoring/RefactoringPreviewDialog.cs  # MODIFIED — migrated (P2)
├── Sessions/SessionRecoveryDialog.cs   # MODIFIED — migrated (P2)
├── Safety/SafetyWarningDialog.cs       # MODIFIED — migrated (P2; semantics preserved)
└── Editor/
    ├── SchemaProgress/SchemaProgressMargin.cs   # MODIFIED — migrated (P4)
    ├── Toolbar/EditorToolbar.cs                 # MODIFIED — migrated (P4)
    ├── Completion/CompletionController.cs       # MODIFIED — popup chrome migrated (P4)
    └── ...                             # remaining editor adornments (P4)

docs/
└── wpf-theming.md                      # NEW — single-page contributor reference (FR-013)

tests/
└── (no new shell tests — shell projects have no harness; see Testing note above)
```

**Structure Decision**: The work is confined to the shared shell project (`AkmlSql.Shell.Shared`) — engine, IPC, and host-specific shell projects (`AkmlSql.Ssms20/21/22`, `AkmlSql.VS2019/2022/2026`) are untouched. The new `Ui/Theme/` subdirectory becomes the single home for the design system; everything else is migration in place. The Core library and tests directories are not modified.

## Complexity Tracking

> Constitution Check passed; no violations to justify.

(table omitted — no entries)
