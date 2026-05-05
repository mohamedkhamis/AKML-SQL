# Phase 0 Research: WPF Theme & Visual Style Refresh

**Branch**: `016-wpf-theme-refresh` | **Date**: 2026-04-30

## Inputs

- Spec: `specs/016-wpf-theme-refresh/spec.md` (4 user stories, 17 FRs, 8 SCs).
- Spec resolved 3 inline clarifications by default; this research locks those defaults in unless `/speckit.clarify` overrides them.
- Existing code: `src/AkmlSql.Shell.Shared/Ui/ThemeManager.cs` (560 lines, returns `Color`, callers wrap+freeze each call site), `src/AkmlSql.Shell.Shared/Dialogs/SettingsWindow.cs` (3,203 lines, has its own private `ThemeBrushSet` enum).
- Constraint: no XAML — code-only WPF in `.projitems` shared project.
- Hosts: SSMS 20 (VS 2017 IsolatedShell, VS SDK 15.9.3), VS 2019 (16.0.208), SSMS 21/22 + VS 2022/2026 (17.14.x).

## Decisions and Rationale

### D1 — Live theme switching mechanism

**Decision**: A `ResourceDictionary` populated by a `ThemeRegistry` singleton, merged into each AKML-owned window's `Resources` at construction time. Surfaces consume tokens via `FrameworkElement.SetResourceReference(<DependencyProperty>, <token-key>)`. On theme change, the registry replaces brush values in the dictionary by key, and WPF's `DynamicResource` propagation re-resolves every consumer automatically.

**Rationale**:
- WPF's resource-reference system is the only built-in mechanism for live brush propagation that does not require `INotifyPropertyChanged` plumbing on every brush at every call site.
- Brushes remain *frozen* (CLAUDE.md convention). The registry swaps the *brush instance* in the dictionary; each instance is fully frozen for its lifetime. Mutating `brush.Color` in place would force unfreezing and lose perf.
- Code-only WPF supports this pattern via `SetResourceReference`; XAML is not required.
- Avoids `Application.Current.Resources` because the host (SSMS / VS) owns `Application.Current`, and other extensions may pollute the global resource namespace. Per-window merge keeps tokens scoped.

**Alternatives considered**:
- **`INotifyPropertyChanged` brush properties on `ThemeManager`**: Requires every assignment site to use a `Binding` instead of a direct property set. Migration cost across ~24 surfaces with ~hundreds of brush assignments is prohibitive. Rejected.
- **Mutating `SolidColorBrush.Color` in place on theme change**: Brushes can no longer be `Freeze()`-d (mutation breaks the freeze contract). Loses cross-thread safety, change-notification skip, and the per-paint allocation savings the codebase relies on. Rejected.
- **Close-and-reopen each window (current `OptionsCommand` pattern)**: User-visible blink, loses scroll position and unsaved edits, doesn't cover tool windows that the user can't close+reopen casually. Rejected by FR-008.
- **`Application.Current.Resources` global merge**: Conflicts with host and other extensions; pollutes the host's resource namespace; theme-change side-effects could surprise non-AKML windows. Rejected.

### D2 — Token taxonomy

**Decision**: Two-tier system. **Primitive tier** (~15 raw colors per variant — gray scale + accent ramp + semantic colors) is internal. **Semantic tier** (~30 named role tokens) is the public API. Surfaces consume semantic tokens only.

Token roles (initial inventory; final list lives in `contracts/theme-tokens.md`):

| Group | Tokens |
|-------|--------|
| Surface | `Surface.Canvas`, `Surface.Panel`, `Surface.Elevated`, `Surface.Sidebar`, `Surface.Input`, `Surface.InputReadOnly`, `Surface.Hover`, `Surface.Selection`, `Surface.SelectionStrong` |
| Text | `Text.Primary`, `Text.Secondary`, `Text.Disabled`, `Text.Placeholder`, `Text.Link`, `Text.OnAccent`, `Text.OnDanger` |
| Border | `Border.Default`, `Border.Strong`, `Border.Subtle`, `Border.Focus`, `Border.Splitter` |
| Accent | `Accent.Primary`, `Accent.PrimaryHover`, `Accent.PrimaryPressed` |
| Semantic | `Status.Success`, `Status.Warning`, `Status.Danger`, `Status.Info` |
| Editor | `Editor.MarginBackground`, `Editor.SpinnerStroke`, `Editor.PopupBackground`, `Editor.PopupBorder` |

**Rationale**:
- Two-tier matches industry standard (Material You, Fluent, Radix, Tailwind/shadcn). Decouples *what color to use* (semantic) from *what value that color resolves to in this theme* (primitive). Theme variants change primitive mappings without touching consumers.
- The current `ThemeManager` mixes primitive and semantic and exposes ~30 properties with names like `HistoryStarActive` (very specific) sitting next to `Background` (very generic). That ad-hoc structure is a major source of the visual drift the spec calls out. The new taxonomy collapses surface-specific tokens into reusable semantic tokens — `HistoryStarActive` becomes `Status.Warning` (the amber used for active stars/warnings/highlights) consistently across the product.
- ~30 tokens fits in one screen of documentation (FR-013 single-page reference) and is small enough to memorize.

**Alternatives considered**:
- **One-tier (semantic only, no primitive layer)**: Simpler initially but creates duplication when two semantic tokens happen to map to the same color in a variant — when one of them needs to change, you only realize the duplication after the fact. Rejected.
- **More tokens (~60+)**: Material/Fluent token sets are huge because they're product-agnostic. AKML SQL is a focused product; ~30 covers every observed use without bloat. Rejected.
- **Surface-specific token names (preserve `HistoryStarActive` etc.)**: Forces every new surface to declare its own tokens; replicates the current drift. Rejected.

### D3 — Host theme detection at runtime

**Decision**: `HostThemeWatcher` subscribes to `Microsoft.VisualStudio.PlatformUI.VSColorTheme.ThemeChanged` (available across all six host targets — confirmed via existing references to `Microsoft.VisualStudio.Shell.*` in shared-project consumers). On change, the watcher inspects `EnvironmentColors.ToolWindowBackgroundBrushKey` luminance to classify the host as Dark/Light/HighContrast and notifies `ThemeRegistry`. If `VSColorTheme` is somehow unavailable at runtime (defensive guard), fall back to a one-shot `SystemColors.Window` luminance check at startup (the existing `ThemeManager.DetectFromEnvironment` approach).

**Rationale**:
- `VSColorTheme.ThemeChanged` is the documented Microsoft API for this purpose; it covers all VS-derived hosts including SSMS.
- The fallback covers any niche host configuration where the event source is missing without crashing the extension.
- The user's preference is layered on top: when the AKML preference is `light` or `dark`, the watcher is still wired but only its luminance reading is used for the (rare) `system` preference. Switching the AKML preference itself is handled by the same `ThemeRegistry.SetVariant` path.

**Alternatives considered**:
- **Polling `SystemColors.Window`** (current approach): Misses runtime VS theme changes; mis-detects when OS theme differs from VS theme (e.g., light Windows + Dark VS). Already proven brittle. Rejected as primary mechanism, kept as fallback only.
- **Reading the registry** (`HKCU\Software\Microsoft\VisualStudio\17.0\General\CurrentTheme`): Path differs per host version; not all hosts honor it identically; slow. Rejected.

### D4 — Windows High Contrast handling (per spec Q3 default)

**Decision**: Add a third internal variant `HighContrast`. `HostThemeWatcher` listens to `SystemParameters.StaticPropertyChanged` and inspects `SystemParameters.HighContrast`. When true, force the `HighContrast` variant regardless of the AKML preference. The High Contrast palette uses Windows system colors (`SystemColors.WindowBrush`, `SystemColors.WindowTextBrush`, `SystemColors.HighlightBrush`, etc.) so it follows whichever High Contrast scheme the user has selected.

**Rationale**:
- Matches the spec Q3 default: "safe-fallback palette so the extension remains usable" without designing a separate first-class High Contrast palette.
- Using `SystemColors.*` is the WPF idiom — the OS guarantees these contrast appropriately within whatever High Contrast scheme is active. The extension does not have to design for every High Contrast color scheme.
- Forcing the variant (rather than letting the user override) is the accessibility-correct behavior — High Contrast is an accessibility setting, not a stylistic preference.

**Alternatives considered**:
- **Ignore High Contrast** (status quo): Extension chrome may become illegible against high-contrast OS chrome. Accessibility regression. Rejected.
- **Design a fully-themed High Contrast palette**: Significantly more design and review work; spec Q3 alternative A explicitly defers this. Rejected for this spec.

### D5 — Disposition of legacy `SettingsDialog` (per spec Q2 default)

**Decision**: Delete `src/AkmlSql.Shell.Shared/Dialogs/SettingsDialog.cs` after grep-confirming nothing references it. The current `SettingsWindow` is the live Options UI, registered through `OptionsCommand`. The legacy file is dead code adding maintenance noise.

**Rationale**:
- `OptionsCommand.Execute` constructs `new SettingsWindow(...)` only — no path constructs `SettingsDialog`. Confirmed by grep at planning time.
- The legacy file confuses contributors and search results. Removing it sharpens the codebase.

**Alternatives considered**:
- **Leave in place**: Adds 1,444 lines of dead code that searches will continue to surface. Rejected.

### D6 — Migration order and surface coverage (spec Q1 default = all four tiers)

**Decision**: Sequence implementation in dependency order, not user-story priority order:

1. **Infrastructure first** (P3 in product priority, but technically a prerequisite): build `Ui/Theme/` (registry, palette, watcher, base classes, tokens). Ship the new `ThemeManager` facade so existing surfaces continue to compile against the deprecated API while migration proceeds.
2. **Reference surface (P1)**: rebuild `SettingsWindow` against the new tokens. This is the visual reference that subsequent surfaces match.
3. **Bulk migration (P2)**: every dialog and tool window in the surface inventory, in any order. Each migration is independently testable and deployable.
4. **Editor surfaces (P4)**: schema-progress margin, completion popup chrome, peek control, editor toolbar.
5. **Cleanup**: remove `[Obsolete]` properties from `ThemeManager`; delete legacy `SettingsDialog.cs`.

**Rationale**:
- Step 1 must precede 2/3/4 because they all consume it.
- The product priority of P3 (infrastructure) being lower than P1 reflects user-visible value, not technical sequencing — in spec-kit terms, the *value* of P3 is delivered in P1's first surface, but the *code* of P3 lands first.
- Step 2 establishes the visual contract before bulk migration so reviewers have something to compare against.
- The `[Obsolete]` facade strategy means the migration is incremental — no flag day, no parallel-stack period, every commit leaves the codebase in a shippable state.

**Alternatives considered**:
- **Big-bang migration in one PR**: Reviewer fatigue, merge conflicts with branch `015-bug-fixes-polish`, no incremental verification possible. Rejected.
- **Migrate by feature area instead of by surface**: Same end state, but couples unrelated surfaces and inflates each PR. Rejected.

### D7 — Backward compatibility with existing `ThemeManager`

**Decision**: Convert `ThemeManager` from a direct color provider to a thin **facade** over `ThemeRegistry`. Each existing property becomes a one-line lookup of the corresponding new semantic token, marked `[Obsolete("Use ThemeTokens.<key> with SetResourceReference. Will be removed after migration.")]`. As surfaces migrate, their old `ThemeManager` calls disappear; once a property has zero callers in the shared project, it is deleted.

**Rationale**:
- Keeps the migration trivially incremental — every commit compiles, every host build compiles.
- `[Obsolete]` warnings act as a built-in TODO list visible in the IDE.
- No behavioral change for surfaces that haven't migrated yet — they keep getting the same colors, just resolved through the new pipeline.

**Alternatives considered**:
- **Delete `ThemeManager` outright on day one**: Forces every surface to migrate in the same PR. Rejected (see D6).
- **Keep `ThemeManager` permanently**: Indefinite drift between old and new APIs; new contributors won't know which to use. Rejected.

### D8 — Typography and spacing

**Decision**: Two new static classes alongside the token registry:
- `Typography` — `static readonly FontFamily UiFont`, `static readonly FontFamily MonoFont`, named font sizes (`Small`, `Body`, `BodyStrong`, `H4`, `H3`, `H2`, `H1`), named weights.
- `Spacing` — named pixel constants (`Xs = 4`, `Sm = 8`, `Md = 12`, `Lg = 16`, `Xl = 24`, `Xxl = 32`).

Surfaces consume typography and spacing the same way they consume primitive constants today (direct field access). These do not need to live in the `ResourceDictionary` because they don't change per-theme.

**Rationale**:
- Hoisting `FontFamily` to static is a CLAUDE.md convention already.
- A spacing scale eliminates the current "every margin is whatever the developer typed" problem visible in `SettingsWindow.cs` and `HistoryToolWindowControl.cs`.
- Keeping these out of the resource dictionary avoids unnecessary `SetResourceReference` calls for values that don't change.

**Alternatives considered**:
- **Put typography in the ResourceDictionary**: Adds runtime resolution for values that never change. Rejected.

### D9 — Verification approach

**Decision**: Three verification layers:
1. **Static audit** (automated): A simple grep-based audit script (`scripts/audit-wpf-theme.ps1` or the equivalent shell command in `quickstart.md`) flags any `Color.FromRgb`, `Color.FromArgb`, `#XXXXXX`, or `Brushes.<X>` literal in `src/AkmlSql.Shell.Shared/**/*.cs` outside `Ui/Theme/` and an explicit allow-list of semantic constants. Must return zero hits before each migration PR merges (SC-003).
2. **Manual visual review** against the design reference for each migrated surface (SC-001, SC-002).
3. **Live-switch smoke test**: open ≥3 AKML windows simultaneously, switch theme, verify all update inside one second (SC-004).

**Rationale**:
- Static audit prevents new drift via grep — fast, deterministic, runs per-PR.
- Manual review is unavoidable for a presentational refresh — automated visual diff tooling is out of scope and not justified for a Windows desktop product.
- The smoke test is cheap and catches the most likely regression class (per-window theme detection vs. centralized).

**Alternatives considered**:
- **WPF UI Automation tests**: Heavy infrastructure for a code-only WPF extension that has no shell test harness today. Rejected.
- **Visual regression tooling (Percy / Chromatic)**: Web-oriented, doesn't apply. Rejected.

### D10 — Coordination with branch `015-bug-fixes-polish`

**Decision**: Treat this branch as starting from `015-bug-fixes-polish`'s tip (already true — `016-wpf-theme-refresh` was branched from there). When `015` merges to `master`, rebase `016` and re-resolve any conflicts in the surface inventory. The single uncommitted change (`HistoryToolWindowControl.cs` SQL History fix) currently in the working tree is unrelated to theming and should land independently as part of `015`.

**Rationale**:
- Preserves the bug fix from being entangled with a presentational refresh.
- Avoids a rebase footgun where the SQL History fix gets re-applied by both branches and creates a confusing merge.

**Alternatives considered**:
- **Carry the fix in this branch**: Couples a bug fix to a much larger refactor; delays the fix unnecessarily. Rejected.

### D11 — Visible focus indicator on high-stakes controls (FR-018, added by `/speckit.clarify`)

**Decision**: The `BorderFocus` token (already declared in `contracts/theme-tokens.md`) is consumed only by **high-stakes controls** — primary actions, destructive actions, navigation items, search inputs, and toggle switches. Other controls keep the WPF/OS default focus chrome. Surfaces apply the indicator via a `FocusVisualStyle` that draws a 1–2 px outer border using `BorderFocus`, attached to those control instances. Surfaces MUST NOT suppress focus visuals (`FocusVisualStyle = null`) on high-stakes controls.

**Rationale**:
- Pragmatic accessibility win without per-control style work for every checkbox and slider.
- The `BorderFocus` token already exists in the contract; this decision pins down where it's required vs. optional.
- Aligns with WCAG 2.1 SC 2.4.7 (Focus Visible) for the highest-impact controls.
- Avoids visual noise of a focus ring on every WPF control, which can clash with VS/SSMS native styling.

**Alternatives considered**:
- **Mandate visible focus on every interactive control**: Higher accessibility ceiling but significantly more implementation work and risks visual conflict with host chrome. Rejected for this spec; revisit in a future accessibility-focused spec.
- **Defer entirely**: Leaves keyboard-only users with mostly-invisible focus; regresses the "professional" bar the user asked for. Rejected.

### D12 — Reduced-motion preference handling (FR-019, added by `/speckit.clarify`)

**Decision**: `HostThemeWatcher` exposes an `AnimationsEnabled` flag mirroring `SystemParameters.ClientAreaAnimation` and listens to `SystemParameters.StaticPropertyChanged` (filtered to that property) alongside its existing High Contrast subscription. Surfaces with motion (`SchemaProgressMargin`, theme-switch transitions) read the flag at the moment they start an animation. When `false`, the schema-progress margin renders a static `TextBlock` reading `Loading…` styled with `Editor.SpinnerStroke` and `Typography.Body`; theme-switch transitions become a single dispatcher-tick brush swap with no fade.

**Rationale**:
- `SystemParameters.ClientAreaAnimation` is the documented WPF mirror of the Windows "Show animations" accessibility preference. Listening to `SystemParameters.StaticPropertyChanged` is the supported runtime-change pattern.
- Co-locating the flag inside `HostThemeWatcher` keeps the "environment watcher" surface area to a single class — surfaces don't have to subscribe to two different sources.
- Reading the flag *at animation start time* (rather than canceling running animations) avoids fighting WPF's storyboard lifecycle and matches user expectation: "I just toggled the preference; the next thing that animates respects it."
- The fallback "Loading…" label rather than a translated equivalent is acceptable for this spec — localization is explicitly out of scope.

**Alternatives considered**:
- **Cancel running storyboards immediately on preference change**: Race conditions with WPF's animation clock; the visual "snap to static" is jarring inside a single session. Rejected.
- **Read the preference once at startup**: Misses runtime toggles; the user has to restart the host to see the effect. Rejected.
- **Pass the flag via a separate static class** (e.g., `MotionPreferences.AnimationsEnabled`): Two watchers means two subscription lifecycles to manage. Rejected.

## Resolved spec clarifications

The spec contained three default-resolved clarifications (Q1–Q3). Decisions above lock these in:

- **Q1 (scope)**: D6 confirms all four tiers are in scope.
- **Q2 (legacy `SettingsDialog`)**: D5 confirms deletion.
- **Q3 (High Contrast / Blue)**: D4 confirms High Contrast as fallback variant; the unused `Blue` enum value is removed (or aliased to `Light`) when `VsThemeKind` is replaced by `ThemeVariant`.

If `/speckit.clarify` later overrides any default, the corresponding decision above is revisited.

## Outstanding risks and open questions

- **`VSColorTheme.ThemeChanged` availability on SSMS 20 (VS 2017 IsolatedShell)**. Needs a smoke test on the SSMS 20 build target during implementation; if the event is unavailable there, the fallback luminance path (D3) covers the gap, but the user experience on SSMS 20 is "manual theme set, no auto-follow" — acceptable per FR-009 since the user can set Dark or Light explicitly.
- **High DPI rendering of new chrome**. The new spacing scale and typography sizes must be reviewed at 100% / 125% / 150% / 175% scaling. Captured as an Edge Case in the spec; verified during P1 review.
- **Animation safety on UI thread**. `SchemaProgressMargin` already uses an `Ellipse` + `RotateTransform` animation with a 1.1s loop. Theme migration must not break the running animation; brush swap must not stop the storyboard.
- **AI Chat tool window**. `AiChatToolWindow` likely has its own per-message-bubble color logic (system/user/assistant). Those need their own semantic tokens (`Chat.SystemBubble`, `Chat.UserBubble`, `Chat.AssistantBubble`) added to the registry rather than collapsed into generic `Surface.*` tokens.

## Summary

All NEEDS CLARIFICATION resolved. The plan adopts a `ResourceDictionary` + `SetResourceReference` live-switch architecture with a two-tier (~30 semantic tokens over ~15 primitives) palette, an `[Obsolete]`-facade migration path for `ThemeManager`, and `VSColorTheme.ThemeChanged` + High Contrast detection wired through a single `HostThemeWatcher`. Implementation sequence: infrastructure → reference surface (Options) → bulk migration (P2 surfaces) → editor adornments → cleanup.
