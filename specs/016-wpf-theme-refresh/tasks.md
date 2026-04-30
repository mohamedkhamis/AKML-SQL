---
description: "Task list for feature 016-wpf-theme-refresh"
---

# Tasks: WPF Theme & Visual Style Refresh

**Input**: Design documents from `specs/016-wpf-theme-refresh/`
**Prerequisites**: plan.md ✓, spec.md ✓, research.md ✓, data-model.md ✓, contracts/theme-tokens.md ✓, contracts/theme-aware-surface.md ✓, quickstart.md ✓

**Tests**: Not requested in spec; shell projects have no test harness (per plan.md § Testing). All verification is manual smoke testing per `quickstart.md` § 6 + the static audit script (T016 / T052).

**Organization**: Tasks are grouped by user story to enable independent implementation and demoing. US1 is the MVP — once the foundational infrastructure (Phase 2) and US1 (Phase 3) are complete, the redesigned Options window can ship as a self-contained increment that other surfaces follow.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Different file, no dependency on prior incomplete tasks — can run in parallel.
- **[Story]**: User story label (US1–US4). Setup, Foundational, and Polish phases have no story label.
- All file paths are relative to the repo root.

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Create the home of the new design system inside the shared shell project; nothing user-visible yet.

- [X] T001 Create the directory `src/AkmlSql.Shell.Shared/Ui/Theme/` for the new design system home (no files yet — Phase 2 fills it).
- [X] T002 Update `src/AkmlSql.Shell.Shared/AkmlSql.Shell.Shared.projitems` to add `<Compile Include>` entries for every Phase 2 file: `Ui\Theme\ThemeVariant.cs`, `ThemeTokens.cs`, `Typography.cs`, `Spacing.cs`, `ThemePalette.cs`, `ThemeRegistry.cs`, `HostThemeWatcher.cs`, `FocusVisualStyles.cs`, `ThemeAwareWindow.cs`, `ThemeAwareUserControl.cs`. Add the entries before the files exist so Phase 2 tasks can be parallelized.
- [X] T003 Capture the static-audit baseline: run the audit grep from `quickstart.md` § 5 against `src/AkmlSql.Shell.Shared/**/*.cs` and write the current hit count + per-file breakdown to `specs/016-wpf-theme-refresh/audit-baseline.txt` (this is the number we drive to zero by the end of the feature).

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Build the entire theme infrastructure (most of US3 P3 work). All user-story phases depend on this.

**⚠️ CRITICAL**: No US1, US2, or US4 surface migration can begin until this phase is complete.

- [X] T004 [P] Create `ThemeVariant` enum (`Light`, `Dark`, `HighContrast`) in `src/AkmlSql.Shell.Shared/Ui/Theme/ThemeVariant.cs` per `data-model.md` § ThemeVariant.
- [X] T005 [P] Create `ThemeTokens` static class in `src/AkmlSql.Shell.Shared/Ui/Theme/ThemeTokens.cs` with one `public const string` per token key from `contracts/theme-tokens.md` (Surface group: 9 keys; Text: 7; Border: 5; Accent: 3; Status: 4; Editor: 4; Chat: 3 = 35 constants). Key format: `Akml.Brush.<Group>.<Name>`.
- [X] T006 [P] Create `Typography` static class in `src/AkmlSql.Shell.Shared/Ui/Theme/Typography.cs` with `static readonly FontFamily UiFont` (`"Segoe UI"`), `MonoFont` (`"Consolas"`), font-size doubles (`Small`, `Body`, `BodyStrong`, `H4`, `H3`, `H2`, `H1`), and `FontWeight` constants (`WeightRegular`, `WeightSemiBold`, `WeightBold`) per `data-model.md` § Typography.
- [X] T007 [P] Create `Spacing` static class in `src/AkmlSql.Shell.Shared/Ui/Theme/Spacing.cs` with `static readonly double` values: `Xs=4, Sm=8, Md=12, Lg=16, Xl=24, Xxl=32` per `data-model.md` § Spacing.
- [X] T008 Create `ThemePalette` in `src/AkmlSql.Shell.Shared/Ui/Theme/ThemePalette.cs`: an internal class holding three `IReadOnlyDictionary<string, SolidColorBrush>` instances (one per `ThemeVariant`), populated with frozen brushes for every key declared in `ThemeTokens`. Light + Dark colors come from `contracts/theme-tokens.md`; High Contrast brushes wrap the named `SystemColors.*` brushes. Construction validates that every `ThemeTokens` key has an entry in every variant; throw at construction if any key is missing. Depends on T004, T005.
- [X] T009 Create `ThemeRegistry` singleton in `src/AkmlSql.Shell.Shared/Ui/Theme/ThemeRegistry.cs` with: a `ResourceDictionary` field, `Current` getter, `Initialize(ThemePreference)` method, `SetVariant(ThemeVariant)` method that swaps brushes by key in the dictionary, `AttachTo(FrameworkElement)` method that merges the dictionary into the element's `Resources`, and `event EventHandler VariantChanged` raised after a swap. The variant resolver applies precedence `HighContrast > explicit-preference > host-detected`. Depends on T004, T008.
- [X] T010 Create `HostThemeWatcher` in `src/AkmlSql.Shell.Shared/Ui/Theme/HostThemeWatcher.cs` per `data-model.md` § HostThemeWatcher: subscribe to `Microsoft.VisualStudio.PlatformUI.VSColorTheme.ThemeChanged` (with try/catch fallback to a one-shot `SystemColors.Window` luminance read), subscribe to `SystemParameters.StaticPropertyChanged` filtered to `HighContrast` and `ClientAreaAnimation`. Expose `LastDetectedHostVariant`, `IsHighContrast`, `AnimationsEnabled` properties and an `event EventHandler AnimationsEnabledChanged`. On any change, marshal to the WPF dispatcher and call `ThemeRegistry.SetVariant(...)` with the resolved variant. Depends on T009.
- [X] T011 [P] Create `FocusVisualStyles` helper in `src/AkmlSql.Shell.Shared/Ui/Theme/FocusVisualStyles.cs` with a single `static Style HighStakes` exposing a `FocusVisualStyle` that draws a 1.5px outer border in `BorderFocus` (via `SetResourceReference` so the style itself reacts to theme changes). Surfaces apply this via `control.FocusVisualStyle = FocusVisualStyles.HighStakes` on primary/destructive buttons, nav items, search inputs, and toggle switches (FR-018 / contract O9). Depends on T005.
- [X] T012 Create `ThemeAwareWindow` base class in `src/AkmlSql.Shell.Shared/Ui/Theme/ThemeAwareWindow.cs` deriving from `Window`. Constructor: calls `ThemeRegistry.Instance.AttachTo(this)`, applies `SetResourceReference(BackgroundProperty, ThemeTokens.SurfaceCanvas)` and `SetResourceReference(ForegroundProperty, ThemeTokens.TextPrimary)`, sets `Owner` from DTE HWND inside a try/catch (pattern from `src/AkmlSql.Shell.Shared/History/HistoryDiffWindow.cs`), and sets `WindowStartupLocation = CenterOwner`. Depends on T005, T009.
- [X] T013 Create `ThemeAwareUserControl` base class in `src/AkmlSql.Shell.Shared/Ui/Theme/ThemeAwareUserControl.cs` deriving from `UserControl`. Same as T012 minus the Owner/StartupLocation logic. Depends on T005, T009.
- [X] T014 Convert `src/AkmlSql.Shell.Shared/Ui/ThemeManager.cs` into a thin `[Obsolete]` facade: each existing color property returns `(SolidColorBrush)ThemeRegistry.Instance.Resources[ThemeTokens.<Mapped>]` based on the role mapping in `research.md` § D2. Mark every property `[Obsolete("Use ThemeTokens.<key> with SetResourceReference. Will be removed after migration.")]`. Remove `VsThemeKind.Blue` (or alias to `Light`). Existing call sites continue to compile and produce correct colors. Depends on T009.
- [X] T015 Wire `HostThemeWatcher.Initialize()` and `ThemeRegistry.Initialize(preference)` into shell package startup. Add a single one-line call in each of the 6 host packages right after `LoggerFactory.Initialize()`: `src/AkmlSql.Ssms20/AkmlSqlPackage.cs`, `src/AkmlSql.Ssms21/AkmlSqlPackage.cs`, `src/AkmlSql.Ssms22/AkmlSqlPackage.cs`, `src/AkmlSql.VS2019/AkmlSqlPackage.cs`, `src/AkmlSql.VS2022/AkmlSqlPackage.cs`, `src/AkmlSql.VS2026/AkmlSqlPackage.cs`. Read the user's `Theme` preference via `ConfigManager.Load().Theme`. Depends on T010.
- [X] T016 Create static-audit script at `scripts/audit-wpf-theme.ps1` that searches `src/AkmlSql.Shell.Shared/**/*.cs` (excluding `Ui/Theme/`) for `Color\.From(Rgb|Argb)`, `Brushes\.[A-Z]\w+`, and `#[0-9A-Fa-f]{6}` literals; exits non-zero on any hit; supports an explicit allow-list passed via parameter for justified semantic constants. Document usage in `quickstart.md` § 5 (already linked).

**Checkpoint**: Foundation ready — US1 / US2 / US4 surface migrations can begin (US3 is largely already delivered by Phase 2; remaining US3 polish lives in Phase 5).

---

## Phase 3: User Story 1 — Polished Options Window (Priority: P1) 🎯 MVP

**Goal**: The redesigned Options window opens, looks deliberately designed, and matches the in-repo SQL Prompt reference (layout, navigation, hierarchy, typographic emphasis) re-skinned in AKML tokens, in both Dark and Light themes.

**Independent Test**: From SSMS 22 (or any host), Tools → AKML SQL → Options. Visually compare to `doc/SQL-PROMPT/SQL-Prompt-Option/SQL_Prompt_Options_Dialog.md` and `13_options_dialog.svg` in both themes; switch theme via the dropdown and confirm the window re-renders within one second; confirm focus rings appear on the OK button, nav items, search box, and theme dropdown.

### Implementation for User Story 1

- [X] T017 [US1] Rebuild `src/AkmlSql.Shell.Shared/Dialogs/SettingsWindow.cs` against the design system. Inherit `ThemeAwareWindow`. Delete the file's private `ThemeBrushSet` class entirely. Replace every `new SolidColorBrush(Color.FromRgb(...))` chrome assignment with `SetResourceReference(<Property>, ThemeTokens.<Token>)`. Replace literal `new Thickness(8)` / `new Thickness(13)` with `Spacing.Sm` / `Spacing.Md` / etc. Replace literal font sizes with `Typography.Body` / `H3` / `H1` etc. Replace `new FontFamily("Segoe UI")` with `Typography.UiFont`. Match the layout (left sidebar nav + right content panel + footer with primary/secondary actions), navigation pattern, hierarchy, and typographic emphasis from `doc/SQL-PROMPT/SQL-Prompt-Option/SQL_Prompt_Options_Dialog.md` and `13_options_dialog.svg`. Preserve every existing setting page, all existing controls, and the search-index behavior — this is presentational only (FR-014).
- [ ] T018 [US1] Replace the close-and-reopen loop in `src/AkmlSql.Shell.Shared/Commands/OptionsCommand.cs`'s `Execute` method with a single `new SettingsWindow(settings).ShowDialog()`. Delete the `while(true) { ... ThemeChangeRequested ... continue; }` block — `ThemeRegistry.SetVariant(...)` now propagates the change without reopening the window (FR-008). Keep the `ConfigManager.Save(updated)` and `_ = client.SendNotificationAsync(MessageTypes.AnalysisSettingsChanged, ...)` post-save behavior. Depends on T017.
- [X] T019 [US1] In `SettingsWindow`, set `FocusVisualStyle = FocusVisualStyles.HighStakes` on every high-stakes control: the OK button, the navigation `TreeViewItem`s, the search `TextBox`, the theme dropdown `ComboBox`, and any Reset/Cancel buttons in the footer (FR-018 / contract O9). Verify `FocusVisualStyle = null` is *not* present on these controls. Depends on T017.
- [X] T020 [US1] Run the smoke test from `quickstart.md` § 6 (steps 1–9) against the rebuilt SettingsWindow in SSMS 22: build (`MSBuild` per `CLAUDE.md`), deploy, clear MEF cache, then verify Light + Dark + live-switch + host-follow + High Contrast + focus visibility + reduced motion. Capture before/after screenshots in both themes for the PR description.

**Checkpoint**: Options window is the visual reference — all subsequent surfaces match this. Ship as MVP increment if wanted.

---

## Phase 4: User Story 2 — All Other AKML Surfaces Match the Design Language (Priority: P2)

**Goal**: Every non-Options dialog and tool window in the surface inventory adopts the same design language and theme behavior, so any two AKML surfaces opened side-by-side read as part of the same product.

**Independent Test**: Open any migrated surface in Light and Dark; visually compare against the SettingsWindow reference; confirm static audit shows zero hits in that file; confirm it passes `quickstart.md` § 6 steps 1–9.

### Pre-migration cleanup

- [X] T021 [US2] Delete the legacy `src/AkmlSql.Shell.Shared/Dialogs/SettingsDialog.cs` (1,444 lines of dead code per `research.md` § D5). First grep `src/AkmlSql.Shell.Shared/**/*.cs` for `SettingsDialog` to confirm zero callers. Remove the corresponding `<Compile Include>` from `AkmlSql.Shell.Shared.projitems`.

### Migrate dialogs (each one is one PR-sized task — different files, parallelizable)

> **Discovered during Phase 4 implementation (2026-04-30):** 8 dialogs in this list are `System.Windows.Forms.Form` subclasses, not WPF — the WPF token system does not apply to them. They are marked **DEFERRED — WinForms** below. Migrating them requires either a parallel WinForms theme adapter (separate, future spec) or a port to WPF (also separate). See spec.md § Assumptions.

- [ ] T022 [DEFERRED — WinForms] [US2] Migrate `src/AkmlSql.Shell.Shared/Dialogs/AboutDialog.cs` (WinForms `Form`) — out of WPF token scope.
- [X] T023 [P] [US2] Migrate `src/AkmlSql.Shell.Shared/Safety/SafetyWarningDialog.cs`. **Preserve** the FR-005 cancel-button discipline: Cancel remains `IsCancel = true` AND focused on `Loaded`; the destructive button is *not* `AcceptButton`. Apply `FocusVisualStyles.HighStakes` to the Cancel button and the destructive (Drop / Proceed) button. The destructive button's background uses `ThemeTokens.StatusDanger` with `TextOnDanger`. Verify the type-to-confirm pattern still works.
- [ ] T024 [DEFERRED — WinForms] [US2] Migrate `src/AkmlSql.Shell.Shared/Dialogs/BulkAnalysisResultDialog.cs` (WinForms `Form`) — out of WPF token scope.
- [ ] T025 [DEFERRED — WinForms] [US2] Migrate `src/AkmlSql.Shell.Shared/Dialogs/LogViewerDialog.cs` (WinForms `Form`) — out of WPF token scope.
- [ ] T026 [DEFERRED — WinForms] [US2] Migrate `src/AkmlSql.Shell.Shared/Refactoring/RefactoringPreviewDialog.cs` (WinForms `Form`) — out of WPF token scope.
- [ ] T027 [DEFERRED — WinForms] [US2] Migrate `src/AkmlSql.Shell.Shared/Sessions/SessionRecoveryDialog.cs` (WinForms `Form`) — out of WPF token scope.
- [ ] T028 [P] [US2] Migrate `src/AkmlSql.Shell.Shared/Snippets/SnippetManagerDialog.cs`.
- [ ] T029 [DEFERRED — WinForms] [US2] Migrate `src/AkmlSql.Shell.Shared/Ui/BulkFormatProgressDialog.cs` (WinForms `Form`) — out of WPF token scope. If WinForms theme adapter is built, branch on `HostThemeWatcher.AnimationsEnabled` per FR-019 / O10 for any indeterminate progress.
- [X] T030 [P] [US2] Migrate `src/AkmlSql.Shell.Shared/Ui/ProfileEditorDialog.cs` — verify the formatter preview region uses `ThemeTokens.Editor.PopupBackground` so the syntax-highlighted preview matches host editor chrome.
- [ ] T031 [DEFERRED — WinForms] [US2] Migrate `src/AkmlSql.Shell.Shared/Ai/TextToSqlInputDialog.cs` (WinForms `Form`) — out of WPF token scope.
- [ ] T032 [DEFERRED — WinForms] [US2] Migrate `src/AkmlSql.Shell.Shared/Productivity/Grid/CellEditDialog.cs` (WinForms `Form`) — out of WPF token scope.
- [X] T033 [P] [US2] Migrate `src/AkmlSql.Shell.Shared/History/HistoryDiffWindow.cs`.

### Migrate tool windows and their controls (each [P], different files)

- [X] T034 [P] [US2] Migrate `src/AkmlSql.Shell.Shared/History/HistoryToolWindowControl.cs`. Collapse history-specific tokens onto general semantic tokens: `HistoryStarActive` → `Status.Warning`, `HistoryOpenIcon` → `Status.Success`, `HistoryClosedIcon` → `Status.Danger`, `HistoryQueryName` → `Text.Primary`, `HistoryMetadata` → `Text.Secondary`, `HistoryVersionCurrent` → `Text.Link`, history backgrounds → `Surface.*`. Inherit `ThemeAwareUserControl`. Apply `FocusVisualStyles.HighStakes` to the search input and the four filter chips (All / Starred / Open / Closed).
- [X] T035 [P] [US2] Migrate `src/AkmlSql.Shell.Shared/Ai/AiChatToolWindow.cs` — inherit `ThemeAwareUserControl` (or wrap content in one), use `Chat.UserBubble` / `Chat.AssistantBubble` / `Chat.SystemBubble` for message backgrounds. Apply `FocusVisualStyles.HighStakes` to the send button and prompt input. **Implementation note (2026-04-30):** the chrome lives in `AiChatPanel.cs` (33-line `AiChatToolWindow.cs` is just a `ToolWindowPane` shell that hosts `AiChatPanel`). `AiChatPanel` now inherits `ThemeAwareUserControl`. `Chat.SystemBubble` is reserved for system/status messages — currently the chat only renders user + assistant turns, so only `ChatUserBubble` and `ChatAssistantBubble` are wired today.
- [X] T036 [P] [US2] Migrate `src/AkmlSql.Shell.Shared/Productivity/DocumentOutline/DocumentOutlineControl.cs` — apply `FocusVisualStyles.HighStakes` to outline item entries (treat as nav items).
- [ ] T037 [P] [US2] Migrate `src/AkmlSql.Shell.Shared/Productivity/Navigation/ObjectSearchWindow.cs` — apply `FocusVisualStyles.HighStakes` to the search input and result-list items.
- [ ] T038 [P] [US2] Migrate `src/AkmlSql.Shell.Shared/Productivity/CommandPalette/CommandPaletteWindow.cs` — apply `FocusVisualStyles.HighStakes` to the command input and result-list items.

### Cross-surface verification

- [ ] T039 [US2] Re-run `scripts/audit-wpf-theme.ps1` (T016) against `src/AkmlSql.Shell.Shared/**/*.cs`; verify the only remaining hits are inside `Ui/Theme/` or in the explicit semantic-constant allow-list. Update `specs/016-wpf-theme-refresh/audit-baseline.txt` with the post-US2 count. Depends on T022–T038.
- [ ] T040 [US2] Smoke-verify each migrated surface in T022–T038 per `quickstart.md` § 6 steps 1–9 (Light + Dark + live switch + host follow + High Contrast + focus + reduced motion). Capture screenshots; record per-surface pass/fail in `specs/016-wpf-theme-refresh/us2-smoke-results.md`. Depends on T022–T038.

**Checkpoint**: All dialogs and tool windows match the design language. Editor adornments (Phase 6) and final cleanup (Phase 5) remain.

---

## Phase 5: User Story 3 — Centralized Theme Tokens & Live Switching (Priority: P3)

**Goal**: A contributor can find a single, authoritative source of theme tokens; live switching propagates to every open AKML surface without close+reopen; the legacy `ThemeManager` is fully retired.

**Note**: Most of US3's *value* is delivered by Phase 2's foundational work. This phase covers the *user-story-specific* verifications and the contributor-facing artifacts that complete the experience.

**Independent Test**: Open ≥3 AKML surfaces simultaneously, switch theme via the Options dropdown, confirm all update within one second without being closed; a new contributor reading `docs/wpf-theming.md` can add a new theme-aware dialog without further questions; grep across `src/AkmlSql.Shell.Shared/**/*.cs` for `ThemeManager.Instance` returns zero hits.

### Implementation for User Story 3

- [ ] T041 [US3] Multi-window live-switch verification per SC-004: open `SettingsWindow` (Tools → Options), `HistoryToolWindow` (Ctrl+Alt+H), and `AiChatToolWindow` simultaneously. Switch theme via Options → Theme dropdown from Light → Dark and back. Time the propagation; record results in `specs/016-wpf-theme-refresh/us3-live-switch-results.md`. Must be ≤ 1s for every window.
- [ ] T042 [US3] Host-theme follow verification per FR-009: set AKML preference to "system". Change the host (SSMS 22 or VS 2022) Light/Dark theme via the host's own Tools → Options → Environment → General → Color theme. Confirm AKML windows track within the same session. Record in same results file.
- [ ] T043 [US3] Author the contributor reference at `docs/wpf-theming.md` per FR-013: include the full token catalog from `contracts/theme-tokens.md` (or link to it), the "add a new theme-aware dialog" recipe from `quickstart.md` § 2, the "migrate an existing surface" recipe from `quickstart.md` § 3, the "add a new token" recipe from `quickstart.md` § 4, and the static-audit usage from `quickstart.md` § 5. This is the single page a new contributor reads to be productive (SC-008).
- [ ] T044 [US3] Remove `[Obsolete]` properties from `src/AkmlSql.Shell.Shared/Ui/ThemeManager.cs` once each property has zero remaining callers in the shared project. After T040, grep for each property name; delete properties whose grep returns zero hits. Repeat until `ThemeManager` has no chrome-color properties. Optionally delete the class entirely if no remaining responsibility justifies it.
- [ ] T045 [US3] Remove the transitional `VsThemeKind.Blue` enum value (or alias) and any related dead branches in `ThemeManager` and `HostThemeWatcher`. Confirm no callers reference `VsThemeKind.Blue` anywhere in `src/`.

**Checkpoint**: Theme infrastructure is fully productized; new contributors can self-serve.

---

## Phase 6: User Story 4 — Editor Adornments, Margins, and Popups (Priority: P4)

**Goal**: Schema-progress margin, completion popup chrome, peek control, analysis tooltip chrome, and editor toolbar align to the same tokens; reduced-motion preference is honored.

**Independent Test**: Open a SQL file with a non-cached schema (triggers `SchemaProgressMargin`); type to trigger the completion popup; hover an analysis squiggle; verify each adornment renders in tokens that harmonize with the host editor in both themes. Toggle Windows "Show animations" off and confirm the schema-progress spinner becomes a static "Loading…" label.

### Implementation for User Story 4

- [ ] T046 [P] [US4] Migrate `src/AkmlSql.Shell.Shared/Editor/SchemaProgress/SchemaProgressMargin.cs`. Replace literal stroke / fill / foreground colors with `Editor.SpinnerStroke` / `Editor.MarginBackground` / `Text.Secondary` references. Subscribe to `HostThemeWatcher.AnimationsEnabledChanged` in `Loaded`, unsubscribe in `Unloaded`. When `AnimationsEnabled` is `false`, render a static `TextBlock` reading `Loading…` styled with `Editor.SpinnerStroke` and `Typography.Body` instead of the rotating `Ellipse` (FR-019 / O10). Preserve the `StrokeDashArray { 10, 30 }` + `RotateTransform` pattern from `CLAUDE.md` § "Editor margin spinner pattern" when animations are enabled.
- [ ] T047 [P] [US4] Migrate `src/AkmlSql.Shell.Shared/Editor/Toolbar/EditorToolbar.cs` — token replacement + `Typography` / `Spacing`. Apply `FocusVisualStyles.HighStakes` to the toolbar buttons.
- [ ] T048 [P] [US4] Migrate completion popup chrome in `src/AkmlSql.Shell.Shared/Editor/Completion/CompletionController.cs` — popup background and border use `Editor.PopupBackground` / `Editor.PopupBorder`. Item foreground uses `Text.Primary`; selected-item background uses `Surface.SelectionStrong`; metadata text uses `Text.Secondary`. Hover state uses `Surface.Hover`.
- [ ] T049 [P] [US4] Migrate `src/AkmlSql.Shell.Shared/Productivity/Navigation/PeekDefinitionControl.cs` — `Editor.PopupBackground` + `Editor.PopupBorder` for chrome.
- [ ] T050 [P] [US4] Migrate analysis-finding tooltip chrome in `src/AkmlSql.Shell.Shared/Analysis/AnalysisController.cs` (and any tooltip composer it uses) — `Editor.PopupBackground` + `Editor.PopupBorder`. Severity badges use `Status.Warning` / `Status.Danger` / `Status.Info` per finding severity.
- [ ] T051 [US4] Smoke-verify editor adornments per `quickstart.md` § 6 in both themes. Specifically exercise: schema-progress margin (Phase A loading), completion popup (typing `SELECT * FROM ` triggers it), peek (Ctrl+click an object name), analysis tooltip (hover a squiggle), editor toolbar buttons. Then disable Windows animations and confirm the spinner becomes a static "Loading…" label and theme switches are instantaneous. Record in `specs/016-wpf-theme-refresh/us4-smoke-results.md`. Depends on T046–T050.

**Checkpoint**: Every AKML surface in the inventory follows the design system. The feature is functionally complete; remaining work is verification and documentation polish.

---

## Phase 7: Polish & Cross-Cutting Concerns

**Purpose**: Final gates — measurable success criteria from the spec.

- [ ] T052 Run final static audit: `scripts/audit-wpf-theme.ps1` against `src/AkmlSql.Shell.Shared/**/*.cs`. Must exit `0` with zero hits outside `Ui/Theme/` and the explicit allow-list of semantic constants (per SC-003). If any hits remain, fix or justify in the allow-list.
- [ ] T053 Verify WCAG AA contrast for every pairing in `contracts/theme-tokens.md` § "Contrast contract" using a contrast-checker tool (e.g., the WebAIM contrast checker or equivalent). Record per-pairing measured ratios in `specs/016-wpf-theme-refresh/contrast-audit.md`. All must meet AA bar (4.5:1 body text, 3:1 UI components) per SC-005. If any fail, adjust the corresponding palette value in `ThemePalette.cs` and re-measure.
- [ ] T054 Measure Options window cold-open time (Tools → AKML SQL → Options → first paint) post-refresh in SSMS 22, ten samples each, then compare against a pre-refresh baseline taken before any Phase 2/3 work. Record in `specs/016-wpf-theme-refresh/perf-audit.md`. Must be within 10% of baseline per SC-006.
- [ ] T055 Verify zero data regressions per SC-007: install a pre-refresh build, populate `%AppData%/AKML SQL/` with non-trivial settings, snippets, formatting profiles, history, and AI keys. Install the post-refresh build. Confirm every saved entity loads correctly: settings appear unchanged in the redesigned SettingsWindow; snippets are listed; profiles open in the editor; history rows display; AI keys remain set. Record results.
- [ ] T056 Update `CLAUDE.md` § "WPF UI conventions" to point contributors at `ThemeRegistry`, `ThemeTokens`, `ThemeAwareWindow`, `Typography`, `Spacing`, and `FocusVisualStyles.HighStakes` instead of `ThemeManager.Instance`. Replace the "freeze brushes" guidance with "theme tokens are pre-frozen via SetResourceReference; do not freeze brushes per-call-site." Add a one-line pointer to `docs/wpf-theming.md`.
- [ ] T057 Final review pass against `contracts/theme-aware-surface.md` § "Acceptance criteria for a migrated surface": confirm all 10 criteria hold for every surface in the inventory. Walk the inventory list; for each, run quickstart steps 1–9 once more in SSMS 22; mark each surface as ✓ on a final tracker in `specs/016-wpf-theme-refresh/final-acceptance.md`. The feature ships when every surface is ✓.

---

## Dependencies & Execution Order

### Phase Dependencies

- **Phase 1 (Setup)** → no prerequisites; T001/T002/T003 can run sequentially in any one session.
- **Phase 2 (Foundational)** → blocks Phase 3, 4, 6. T004–T007 are pure-leaf and parallel; T008–T015 form a small dependency tree (`ThemePalette` → `ThemeRegistry` → `HostThemeWatcher` → wiring); T011 (FocusVisualStyles) depends only on T005; T016 (audit script) is independent.
- **Phase 3 (US1)** → depends on Phase 2 complete. T017→T018→T019→T020 sequential within US1.
- **Phase 4 (US2)** → depends on Phase 2 complete. T021 first (delete legacy). T022–T038 are independent across files and run in parallel. T039–T040 are aggregation tasks that depend on all migrations.
- **Phase 5 (US3)** → depends on Phase 4 substantially complete (otherwise multi-window verification is meaningless). T041 / T042 / T043 are independent; T044 depends on T040 (so the obsolete-property grep returns zero); T045 is independent.
- **Phase 6 (US4)** → depends on Phase 2 complete. T046–T050 parallel; T051 aggregates.
- **Phase 7 (Polish)** → depends on Phases 3, 4, 5, 6 complete. T052–T057 mostly sequential; T053 / T054 / T055 can run in parallel since they touch different artifacts.

### User Story Dependencies

- **US1 (P1)**: depends on Phase 2 only.
- **US2 (P2)**: depends on Phase 2; benefits from US1 being complete first because the SettingsWindow rebuild is the visual reference for what migrated surfaces should look like — but US2 surfaces *can* technically migrate before US1 if reviewers accept token correctness without a visual reference.
- **US3 (P3)**: most of US3 lives in Phase 2 (foundational); the residual user-story tasks (T041–T045) depend on US2 being substantially complete (the multi-window verification needs migrated surfaces to be meaningful, and the `ThemeManager` cleanup needs callers gone).
- **US4 (P4)**: depends on Phase 2 only; can run in parallel with US2.

### Parallel Opportunities

- **Phase 1**: T001 → T002 → T003 sequential; trivial.
- **Phase 2**: launch T004, T005, T006, T007, T011, T016 in parallel after T002 completes. Then T008. Then T009. Then T010, T012, T013 in parallel. Then T014, T015 sequential.
- **Phase 4 (the bulk migration)**: T022–T038 are 17 independent file edits. With multiple developers (or successive sessions) all 17 can run concurrently.
- **Phase 6**: T046–T050 are 5 independent files — same pattern.
- **Phase 7**: T053, T054, T055 in parallel after T052 passes.

### Within Each User Story

- Models / static classes before services (Phase 2 ordering already encodes this).
- Each migration task is a self-contained unit: read the existing file, swap brushes/typography/spacing per the rules in `quickstart.md` § 3, apply focus styles to high-stakes controls, smoke-test, commit.
- Don't reorder: do T039 (audit) and T040 (smoke verification) only after the migration tasks they aggregate complete.

---

## Parallel Example: Phase 4 (US2 bulk migration)

```bash
# After T021 (delete legacy SettingsDialog), all 17 surface migrations are independent files.
# A team or a single agent with multiple workers can run these concurrently:

Task: "T022 Migrate AboutDialog.cs"
Task: "T023 Migrate SafetyWarningDialog.cs (preserve FR-005 cancel discipline)"
Task: "T024 Migrate BulkAnalysisResultDialog.cs"
Task: "T025 Migrate LogViewerDialog.cs"
Task: "T026 Migrate RefactoringPreviewDialog.cs"
Task: "T027 Migrate SessionRecoveryDialog.cs"
Task: "T028 Migrate SnippetManagerDialog.cs"
Task: "T029 Migrate BulkFormatProgressDialog.cs"
Task: "T030 Migrate ProfileEditorDialog.cs"
Task: "T031 Migrate TextToSqlInputDialog.cs"
Task: "T032 Migrate CellEditDialog.cs"
Task: "T033 Migrate HistoryDiffWindow.cs"
Task: "T034 Migrate HistoryToolWindowControl.cs"
Task: "T035 Migrate AiChatToolWindow.cs"
Task: "T036 Migrate DocumentOutlineControl.cs"
Task: "T037 Migrate ObjectSearchWindow.cs"
Task: "T038 Migrate CommandPaletteWindow.cs"

# Then T039 (audit) and T040 (smoke) run after all 17 finish.
```

---

## Implementation Strategy

### MVP First (US1 only)

1. Complete Phase 1 (Setup): T001 → T002 → T003.
2. Complete Phase 2 (Foundational): T004–T016. **Critical** — blocks everything.
3. Complete Phase 3 (US1): T017 → T018 → T019 → T020.
4. **STOP and VALIDATE**: smoke-test the redesigned Options window in SSMS 22 in both themes. Capture screenshots.
5. Optionally ship as an internal preview / beta build to confirm the design language before bulk migration.

### Incremental Delivery

1. Setup + Foundational → infrastructure ready (no user-visible change yet).
2. Add US1 (Options window) → ship MVP. ← **First user-visible release.**
3. Add US2 in batches (e.g., 3–5 surfaces per increment) → each batch is a shippable improvement.
4. Add US4 (editor adornments) → final user-visible polish.
5. Add US3 cleanup (T044 / T045) and Phase 7 verifications → feature complete.

Each batch leaves the codebase in a shippable state because `ThemeManager` remains an `[Obsolete]` facade until the very end.

### Parallel Team Strategy

With multiple developers or agents:

1. One developer runs Phase 1 + Phase 2 (mostly sequential due to dependency tree).
2. Once Phase 2 is done, divide the 17 US2 surfaces among developers — each surface is one self-contained migration PR.
3. One developer concurrently handles US1 (rebuilding SettingsWindow against the same tokens).
4. After US2 substantially completes, US3 cleanup tasks fall to whoever owns the design system.
5. US4 (editor adornments) can run any time after Phase 2.

---

## Notes

- Every migration PR must include screenshots in both themes (per `contracts/theme-aware-surface.md` § Reviewer's quick checklist).
- Static audit script (T016) must run cleanly per PR — partial migrations are allowed only if the count strictly decreases vs. the baseline.
- `ThemeManager` stays intact and `[Obsolete]` until T044 confirms zero callers remain.
- The user has explicit git rules in `CLAUDE.md`: never commit/push without explicit approval. Each task can be committed individually but only when the user says "commit".
- The SQL History fix from branch `015-bug-fixes-polish` (`HistoryToolWindowControl.cs:228` adding `_filterStarred.Child = null;`) is currently uncommitted on this branch's working tree; per `research.md` § D10 it should land on `015` independently before this work.
- High DPI (125% / 150% / 175% scaling) verification happens during T020 (US1 smoke) and T040 (US2 smoke) — capture screenshots at multiple scales for the design reviewer.

---

## Summary

- **Total tasks**: 57.
- **Per user story**: US1 = 4 tasks (T017–T020). US2 = 20 tasks (T021–T040). US3 = 5 tasks (T041–T045). US4 = 6 tasks (T046–T051). Foundational = 13 tasks (T004–T016). Setup = 3 (T001–T003). Polish = 6 (T052–T057).
- **Parallel opportunities**: 6 in Phase 2 (T004 / T005 / T006 / T007 / T011 / T016), 2 more (T012 / T013) after T009, 17 in Phase 4 (T022–T038), 5 in Phase 6 (T046–T050), 3 in Phase 7 (T053 / T054 / T055).
- **MVP scope**: Phase 1 + Phase 2 + Phase 3 (US1) — 20 tasks; ships the redesigned Options window as a self-contained increment.
