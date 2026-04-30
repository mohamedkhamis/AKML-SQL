# Feature Specification: WPF Theme & Visual Style Refresh

**Feature Branch**: `016-wpf-theme-refresh`
**Created**: 2026-04-30
**Status**: Draft
**Input**: User description: "please revisit AKML Option Menu Style (is very bad) also i want to work fine at dark and light, please do it in professional way and change WPF style of it and of all screens"

## Summary

The AKML SQL Options window and several other WPF surfaces in the extension look unfinished, visually inconsistent, and behave poorly when the host (SSMS / Visual Studio) is in Dark vs. Light theme. The goal is to deliver a professional, cohesive visual design system that the Options window adopts first, then propagates to every other extension-owned WPF surface (modal dialogs, dockable tool windows, in-editor adornments, popups). Both Dark and Light themes must look intentional, readable, and aligned with host conventions in either mode. The user must never see mismatched chrome, illegible text, or "default WPF gray" from this extension again.

## User Scenarios & Testing *(mandatory)*

### User Story 1 — Open Options and immediately recognize it as a polished, modern dialog (Priority: P1)

The user opens the AKML SQL Options window from the Tools menu (or via its shortcut). The window appears with consistent typography, generous spacing, a clear navigation column, focused content area, an unobtrusive search box, and primary/secondary actions pinned in a footer. Whether the host is in Dark or Light mode, the dialog reads as deliberately designed — colors are harmonious with the host, contrast is comfortable, hover/selection feedback is obvious, and the experience never resembles a developer-built admin form.

**Why this priority**: This is the explicit complaint that triggered the request — the Options window is the primary surface users judge the product's polish by. If P1 ships alone, the most painful symptom is gone and the design system established here becomes the template for the rest.

**Independent Test**: Open the Options window in both Dark and Light themes on at least one SSMS host and one Visual Studio host. Visually verify against an agreed reference (mockup or curated screenshot) that the layout, typography, navigation, controls, and footer match the new design language. Switch themes via the dropdown and confirm the dialog re-renders correctly without leaving stale colors.

**Acceptance Scenarios**:

1. **Given** SSMS is running in Dark theme, **When** the user opens AKML SQL → Options, **Then** the window background, navigation pane, content panel, inputs, headings, and buttons all use the dark palette and pass a quick visual review for legibility and consistency.
2. **Given** SSMS is running in Light theme, **When** the user opens AKML SQL → Options, **Then** the window uses the light palette and looks equally polished — no stark white panels next to off-white panels, no near-invisible borders, no remaining dark-theme remnants.
3. **Given** the Options window is open, **When** the user changes the AKML theme dropdown from Dark to Light (or vice-versa), **Then** the window re-renders into the chosen theme within one second and no element retains the previous theme's colors.
4. **Given** the Options window is open at default size, **When** the user resizes it (smaller or larger) and navigates between pages, **Then** layout reflows cleanly — no clipping, no horizontal scrollbars at default-or-larger widths, no controls bunched into corners.

---

### User Story 2 — Every other AKML SQL window/dialog matches the Options window's design language (Priority: P2)

The user opens the SQL History tool window, the About dialog, the Snippet Manager, the Profile Editor, the Refactoring Preview, the Session Recovery dialog, the Safety Warning dialog, the AI Chat tool window, the Document Outline, the Object Search dialog, the Command Palette, the Bulk Analysis Result dialog, the Log Viewer, the Cell Edit dialog, the Bulk Format Progress dialog, and the Text-to-SQL Input dialog — and each one feels like it belongs to the same product family. Same typography scale, same spacing, same button hierarchy, same border treatment, same theme behavior. None show "default WPF" chrome or hardcoded colors that fight the active theme.

**Why this priority**: The user explicitly asked for "all screens" to be redone. Once P1 establishes the design system, P2 propagates it. P2 ships independently of P1 in the sense that any one surface can be migrated and shipped without blocking others, but the value compounds as more surfaces adopt the new system.

**Independent Test**: For each surface in the inventory (see Key Entities below), open it in both Dark and Light themes and visually verify against the same design reference used for the Options window. The surface must use only the centralized theme tokens — no hardcoded chrome hex remains in its source.

**Acceptance Scenarios**:

1. **Given** any AKML-owned dialog or tool window listed in the surface inventory, **When** opened in either theme, **Then** its background, foreground, border, accent, and interactive-state colors come exclusively from the centralized theme token source.
2. **Given** the user is in Dark theme, **When** they open any two AKML surfaces back-to-back, **Then** both use visually identical dark palette swatches for shared roles (background, text, accent, border) — no surface looks like a one-off.
3. **Given** the user is in Light theme, **When** they open the SQL History tool window adjacent to the Options window (e.g., docked side-by-side), **Then** the two surfaces' chrome harmonizes — no jarring contrast between them.

---

### User Story 3 — Theme tokens and switching infrastructure are robust and centralized (Priority: P3)

A developer working in this codebase can find a single, authoritative source of theme tokens (named semantic roles like "panel background", "primary text", "accent", "destructive", etc.). Each surface consumes those tokens directly without re-declaring its own brushes or hardcoding hex. When the user changes the AKML theme preference, every open AKML window updates immediately — the user does not have to close and reopen anything.

**Why this priority**: P3 is the foundation that prevents the same problem from recurring. Without it, P1 and P2 fix today's issues but new code added next month could re-introduce the same drift. P3 ships independently because the token registry can be introduced without changing visual output, then surfaces migrate to it incrementally.

**Independent Test**: A grep of the shell-shared codebase for hardcoded chrome color literals (e.g., `Color.FromRgb`, `#XXXXXX`) returns only references inside the centralized token source or explicitly justified semantic constants (success-green, danger-red, warning-amber). Live theme switching is tested by opening 3+ AKML surfaces simultaneously, switching theme, and verifying all of them update in place without being closed.

**Acceptance Scenarios**:

1. **Given** a contributor adds a new dialog, **When** they need a background color, **Then** they have one obvious place to look up the named token and a documented contract for what colors are appropriate for which UI roles.
2. **Given** any number of AKML surfaces are open in either host, **When** the user changes the AKML theme preference, **Then** all open AKML surfaces re-render in the new theme without being closed first.
3. **Given** the host VS / SSMS theme changes at runtime (user switches the IDE theme), **When** the AKML theme preference is set to "system", **Then** open AKML surfaces detect the change and re-render to match.

---

### User Story 4 — Editor adornments, margins, and popups stay consistent with the host (Priority: P4)

The schema-progress margin, completion popup chrome, peek-definition control, analysis squiggles' tooltips, and any AKML-owned editor adornments use theme tokens that visually agree with the active host theme — no light spinners on a dark editor, no opaque popups that obscure text, no high-contrast accent colors that clash with the selected VS color theme.

**Why this priority**: These surfaces are smaller and individually less prominent than the dialogs and tool windows, but cumulatively contribute to "feels native vs. feels foreign." Lower priority because each one is small and the user's complaint centered on the Options window first.

**Independent Test**: Trigger each editor-side adornment (open a file with a non-cached schema to show the progress margin, type to trigger completion, hover to trigger peek, etc.) in both themes and verify chrome legibility and harmony with the surrounding editor.

**Acceptance Scenarios**:

1. **Given** the schema cache is loading, **When** the editor margin spinner is visible in Dark theme, **Then** the spinner color, gutter background, and any text labels are legible against the editor's dark background and consume the centralized accent token.
2. **Given** the user triggers the completion popup, **When** suggestions appear in either theme, **Then** popup background, item foreground, hover/selection states, and metadata text all come from the centralized tokens.

---

### Edge Cases

- **Mid-dialog theme change.** While the Options window is open, the user changes the AKML theme dropdown. The window must finish re-rendering without losing the user's in-flight (unsaved) edits to other settings.
- **Host theme changes mid-session, AKML preference is "system".** The user switches VS / SSMS from Dark to Light while AKML windows are open and AKML's preference is "system". Open AKML surfaces must update to track the host within the same session, not on next reopen.
- **Windows High Contrast active.** When the operating system has a High Contrast theme active, AKML chrome must remain readable — either by following the High Contrast palette or by choosing a safe fallback. The detail of which approach is in scope is in the Clarifications below.
- **Modal dialog parented to a window in a conflicting theme.** A modal dialog (e.g., Safety Warning) is shown while a tool window in a different theme variant is also visible. The modal must use the AKML theme preference, not whatever its owner happens to be drawing.
- **Resizing below intended minimum.** If the Options window is shrunk below the design's minimum size, layout must clip gracefully (e.g., scrollable content area) rather than overlapping or cutting off footer buttons.
- **High DPI / non-100% scaling.** On displays with 125% / 150% / 175% scaling, typography, padding, and icons must render without blurring or misaligned baselines.
- **Right-to-left input in text fields.** RTL content in any text input must render correctly without breaking the surrounding chrome layout.
- **Slow theme switch.** Theme switching must not block the UI thread for more than a few hundred milliseconds — the user must continue to be able to interact with the host immediately after switching.
- **Reduce-motion preference active.** When the user has disabled "Show animations" in Windows, the schema-progress spinner falls back to a static "Loading…" indicator and theme switches are instantaneous. The system must detect a runtime change to this preference (e.g., user toggles it during a session) and adjust currently-running animations accordingly.
- **Existing user customizations preserved.** Any settings the user has previously configured (analysis rules, formatting profiles, snippet locations, AI keys, etc.) must remain intact through the visual refresh — the redesign is chrome-only.
- **Legacy `SettingsDialog` code path.** The codebase contains a second, older `SettingsDialog` class alongside the current `SettingsWindow`. Disposition (retire / merge / leave) is in the Clarifications below.

## Requirements *(mandatory)*

### Functional Requirements

#### Visual design system

- **FR-001**: The redesigned AKML SQL Options window MUST present a single, coherent visual design — defined typography scale, defined spacing scale, defined elevation/border treatment, and defined control hierarchy (primary action, secondary action, link, destructive action) — that matches the in-repo reference at `doc/SQL-PROMPT/SQL-Prompt-Option/SQL_Prompt_Options_Dialog.md` and `doc/SQL-PROMPT/SQL-Prompt-Option/13_options_dialog.svg` (layout, navigation, hierarchy, typographic emphasis), re-skinned in AKML palette tokens.
- **FR-002**: The same visual design language MUST be applied to every AKML-owned WPF surface listed in the surface inventory (see Key Entities), so any two surfaces opened side-by-side read as part of the same product.
- **FR-003**: The design system MUST define a small set of named, semantic theme tokens (e.g., panel background, surface background, primary text, secondary text, accent, accent-on-accent text, border, hover background, selection background, danger, success, warning) and every AKML chrome color MUST resolve to one of those tokens — never to an inline literal hex value, except for explicitly semantic constants (e.g., the red used for the Drop button, the amber used for warnings).
- **FR-004**: The Options window MUST present its existing settings categories using a navigation pattern that scales to the current category count without horizontal scrolling at the design's default size.
- **FR-005**: All AKML-owned WPF surfaces MUST present primary actions, secondary actions, and destructive actions with visually distinct, consistent treatments so the user can recognize action severity at a glance, in both themes.

#### Theme support

- **FR-006**: Every AKML-owned WPF surface MUST render correctly in both Dark and Light themes — readable text, visible borders, sufficient contrast for foreground-on-background pairs at the body text size.
- **FR-007**: The user MUST be able to choose Dark, Light, or "follow host" as the AKML theme preference, and that choice MUST persist across sessions.
- **FR-008**: When the user changes the AKML theme preference, every AKML window currently open MUST update to the new theme within one second, without being closed and reopened by the user.
- **FR-009**: When the AKML theme preference is "follow host" and the host VS / SSMS theme changes at runtime, AKML windows MUST detect the change and update to match.
- **FR-010**: Each Dark/Light theme variant MUST achieve at least WCAG AA contrast for body text on its standard panel background and for primary actions against their accent background.

#### Centralized infrastructure

- **FR-011**: The codebase MUST expose theme tokens as ready-to-bind frozen WPF brushes (not raw colors that each call site must wrap and freeze itself), so that consumers can bind directly without per-call allocation.
- **FR-012**: Adding a new theme-aware surface MUST require only consuming the existing token source — no new theme detection logic, no new brush freezing helpers, no new "set the user theme on init" plumbing per surface.
- **FR-013**: The system MUST provide a documented, single-page reference (in `doc/` or `docs/`) listing every named token, what UI role it represents, and its Dark and Light values, so contributors and reviewers have one source of truth.

#### Behavior preservation

- **FR-014**: Every existing setting, command, action, and feature in the affected windows MUST continue to function — the refresh is presentational, not behavioral.
- **FR-015**: User-saved data (settings in `config.json`, snippets, formatting profiles, history database, AI configuration, etc.) MUST remain untouched and continue to load correctly.
- **FR-016**: Keyboard navigation, screen reader compatibility, and existing dialog button semantics (e.g., Esc cancels, Enter does not accidentally trigger destructive actions) MUST be preserved or improved — never regressed.
- **FR-018**: AKML-owned WPF surfaces MUST render a visible keyboard-focus indicator on **high-stakes interactive controls** — primary actions, destructive actions, navigation items, search inputs, and toggle switches — using the centralized focus token. Other controls retain the WPF/OS default focus chrome.
- **FR-019**: AKML animations MUST honor the Windows "Show animations" accessibility preference (exposed by the OS as a system parameter). When animations are disabled, the schema-progress margin MUST replace its rotating spinner with a static "Loading…" indicator, and theme switches MUST be instantaneous (no crossfade, no fade-through-blank).

#### Disposition of legacy `SettingsDialog`

- **FR-017**: A decision about the legacy `SettingsDialog` (retire / merge / leave) MUST be made before the refresh ships, and the chosen disposition MUST be implemented so the codebase contains exactly one user-facing settings UI by the end of this work, OR a documented reason exists for keeping both. (See Clarifications.)

### Key Entities

The "WPF surfaces" the refresh applies to (the surface inventory):

- **Modal dialogs**: `SettingsWindow` (the Options window — primary target), `AboutDialog`, `SafetyWarningDialog`, `BulkAnalysisResultDialog`, `LogViewerDialog`, `RefactoringPreviewDialog`, `SessionRecoveryDialog`, `SnippetManagerDialog`, `BulkFormatProgressDialog`, `ProfileEditorDialog`, `TextToSqlInputDialog`, `CellEditDialog`, `HistoryDiffWindow`.
- **Dockable tool windows**: `HistoryToolWindow` / `HistoryToolWindowControl`, `AiChatToolWindow`, `DocumentOutlineToolWindow` / `DocumentOutlineControl`, `ObjectSearchWindow`, `CommandPaletteWindow`.
- **In-editor adornments and margins**: schema-progress margin, completion popup chrome, peek-definition control, analysis-finding tooltips, editor toolbar.
- **Theme infrastructure**: the centralized theme token source (currently `ThemeManager`), its consumers, and the persistence of the user's theme preference in `config.json`.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A reviewer comparing the redesigned Options window against `doc/SQL-PROMPT/SQL-Prompt-Option/SQL_Prompt_Options_Dialog.md` and `13_options_dialog.svg`, in both Dark and Light themes, judges the dialog as "matches the reference (layout, navigation, hierarchy, typographic emphasis), re-skinned in AKML tokens" without revisions on the first review pass.
- **SC-002**: 100% of AKML-owned WPF surfaces in the inventory render in both themes with no theme-specific visual defect found during a structured walkthrough (defined as: readable text, visible borders, no leftover-from-other-theme colors, harmonious palette).
- **SC-003**: A grep across `src/AkmlSql.Shell.Shared/**/*.cs` for hardcoded chrome color literals returns zero matches outside the centralized token source and explicitly listed semantic-color constants (e.g., destructive-red, success-green, warning-amber).
- **SC-004**: The user can change the AKML theme preference and see every currently-open AKML surface update within 1 second, without closing or reopening any window.
- **SC-005**: For each Dark and Light variant, body text contrast against its standard panel background and primary-action contrast against its accent background both meet or exceed WCAG AA (4.5:1 for body text, 3:1 for large text and UI components).
- **SC-006**: Time from "user clicks Tools → AKML SQL → Options" to "Options window is visible and interactive" remains within 10% of today's baseline (i.e., the redesign does not introduce a perceptible startup regression).
- **SC-007**: Zero regressions in saved settings, snippets, profiles, history, or AI configuration after upgrading from a pre-refresh build to a post-refresh build (verified via a before/after smoke test on a populated `%AppData%/AKML SQL/` directory).
- **SC-008**: A new contributor, given only the design-system reference document, can add a new theme-aware dialog without asking how to handle theme tokens, brush freezing, or live-switch behavior.

## Assumptions

- The refresh is purely presentational — no changes to settings semantics, IPC messages, or persisted file formats.
- WPF code-only construction (no XAML) remains the constraint, per the existing shared-project (`.projitems`) architecture documented in `CLAUDE.md`.
- The existing palette decisions (SQL Prompt-aligned dark/light values for the Options window) remain a valid starting point and will be carried into the new token system rather than re-derived from scratch.
- "Light" and "Dark" are the two required theme variants. A "Blue" mode currently appears in `ThemeManager.VsThemeKind` but is not in user-facing scope unless the Clarification on additional themes opts in.
- Live theme switching applies to AKML-owned windows only; the host's own chrome is the host's responsibility.
- The user's reference for "professional" is the visual quality bar set by Redgate SQL Prompt's Options dialog, which the existing `SettingsWindow` source already cites as inspiration.
- Existing accessibility primitives (keyboard focus, screen reader names, tab order) will be preserved or improved.
- The work targets all six host targets (SSMS 20/21/22, VS 2019/2022/2026) since the affected files live in `AkmlSql.Shell.Shared`.
- **WinForms dialogs are out of scope for the WPF token system.** Implementation discovery (during Phase 4 / spec 016) found that 8 of the 13 inventory dialogs are `System.Windows.Forms.Form` subclasses, not WPF — `AboutDialog`, `BulkAnalysisResultDialog`, `LogViewerDialog`, `RefactoringPreviewDialog`, `SessionRecoveryDialog`, `BulkFormatProgressDialog`, `TextToSqlInputDialog`, `CellEditDialog`. WinForms uses `System.Drawing.Color` + `BackColor`/`ForeColor`, a separate UI stack incompatible with WPF `ResourceDictionary` and `SetResourceReference`. These surfaces remain on their pre-refresh chrome and require either a parallel WinForms theme adapter (separate, future spec) or a port to WPF (also separate). The WPF token system continues to apply to the remaining ~10 WPF surfaces.

## Dependencies

- The existing centralized theme source `src/AkmlSql.Shell.Shared/Ui/ThemeManager.cs` and its consumers (every WPF surface in the shared project).
- The existing `Theme` field in `AppSettings` (`config.json`) for persisting the user's preference.
- The existing `OptionsCommand` flow (which currently closes-and-reopens the window on theme change — see `src/AkmlSql.Shell.Shared/Commands/OptionsCommand.cs`).
- Coordination with any in-flight work on branch `015-bug-fixes-polish` that touches the same WPF surfaces (notably the recent `HistoryToolWindowControl` fix).

## Out of Scope

- Functional changes to settings, snippets, profiles, refactoring, AI integration, history search, or any other behavior unrelated to chrome.
- Localization of UI strings (the refresh keeps existing English strings; localization is a separate effort).
- Replacing code-only WPF construction with XAML.
- Redesigning the host's own chrome or any non-AKML-owned surfaces.
- Reworking the engine, IPC, or any non-WPF code path.
- Adding new settings, new commands, or new surfaces.

## Clarifications

### Session 2026-04-30

- Q: What is the agreed visual reference for the redesigned Options window (SC-001)? → A: Match the in-repo SQL Prompt Options reference (`doc/SQL-PROMPT/SQL-Prompt-Option/SQL_Prompt_Options_Dialog.md` + `13_options_dialog.svg`) for layout, navigation, hierarchy, and typographic emphasis, re-skinned in AKML palette tokens.
- Q: Should the design system mandate a visible keyboard focus indicator across all interactive controls, only high-stakes ones, or none? → A: High-stakes only — primary/destructive buttons, navigation items, search inputs, and toggle switches (FR-018). Other controls keep the WPF/OS default focus chrome.
- Q: Should AKML animations respect the Windows "reduce motion" / "show animations" preference? → A: Yes (FR-019). When disabled, the schema-progress spinner becomes a static "Loading…" indicator and theme switches are instantaneous (no crossfade or fade-through-blank).

The decisions below (Q1–Q3) were applied as defaults during initial spec authoring before `/speckit.plan`; re-run `/speckit.clarify` if a default is wrong.

### Q1: Scope of "all screens"

**Default**: All four tiers in the surface inventory above (modal dialogs, dockable tool windows, in-editor adornments/margins, theme infrastructure) are in scope.

**Alternative narrower scope**: Only modal dialogs and dockable tool windows. Editor adornments/margins (schema-progress margin, completion popup chrome, peek control) defer to a follow-up spec.

### Q2: Disposition of legacy `SettingsDialog`

**Default**: Delete `src/AkmlSql.Shell.Shared/Dialogs/SettingsDialog.cs` after confirming nothing references it. The current `SettingsWindow` is the live Options UI; the legacy file is dead code that adds maintenance noise.

**Alternative**: Leave it in place as reference material until the refresh ships, then remove it as a follow-up.

### Q3: Windows High Contrast and additional theme variants

**Default**: Detect Windows High Contrast mode and apply a safe-fallback palette (e.g., the Light variant with maximum contrast values) so the extension remains usable. Do not introduce a fully separate High Contrast palette in this spec. Do not introduce or maintain a "Blue" theme variant; the existing `VsThemeKind.Blue` enumeration value is removed or treated as an alias for the appropriate Light/Dark variant.

**Alternative A (more accessible)**: Add a fully designed High Contrast palette as a third first-class theme variant.

**Alternative B (legacy preservation)**: Keep the "Blue" variant as a separately-themed third option for users who currently have it selected.
