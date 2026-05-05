# Contract: Theme-Aware Surface

**Branch**: `016-wpf-theme-refresh` | **Date**: 2026-04-30
**Status**: Authoritative obligations every AKML-owned WPF surface MUST satisfy after migration.

This contract complements `theme-tokens.md`. The token catalog says **what colors exist**; this document says **what every surface that uses them must do**.

---

## Scope

Applies to every AKML-owned WPF type listed in `spec.md` Key Entities — modal `Window` subclasses, dockable tool-window content (`UserControl`), and editor adornments / margins (`IWpfTextViewMargin` and adornment-layer hosts).

Does not apply to:
- Code paths that consume *the host's* theme APIs directly (the host owns those).
- Test code, design-time stubs, or build scripts.

---

## Obligations

### O1 — Register with the theme registry

A surface must merge `ThemeRegistry.Resources` into its own `Resources` exactly once, before any chrome controls are added to the visual tree.

**Pattern A (preferred — base class)**:

```csharp
internal sealed class MyDialog : ThemeAwareWindow { ... }
```

`ThemeAwareWindow` and `ThemeAwareUserControl` perform the merge in their constructors.

**Pattern B (direct inheritance — when the surface needs bespoke construction order)**:

```csharp
internal sealed class SafetyWarningDialog : Window
{
    public SafetyWarningDialog()
    {
        ThemeRegistry.Instance.AttachTo(this);
        // ... rest of constructor
    }
}
```

`AttachTo` is idempotent and safe to call multiple times.

### O2 — Use `SetResourceReference` for chrome colors

Every chrome property that should track theme changes (Background, Foreground, BorderBrush, Fill, Stroke, etc.) MUST be set via `SetResourceReference`, never via direct brush assignment.

```csharp
// Correct — participates in live theme switching:
border.SetResourceReference(Border.BackgroundProperty, ThemeTokens.SurfacePanel);

// Forbidden for chrome — frozen at construction, ignores theme changes:
border.Background = ThemeManager.Instance.SurfacePanelBrush;
```

Exceptions (direct assignment is allowed):
- Brushes that are deliberately theme-independent and are derived from a single `Status*` token (e.g., a one-off success/danger overlay), provided the assignment uses a `ThemeTokens` lookup, not a literal.
- Brushes used inside a static `DataTemplate` or `Style` that's already theme-aware via its own `DynamicResource` references.

### O3 — Use `Typography` and `Spacing` constants

Surfaces MUST consume:
- `Typography.UiFont` / `Typography.MonoFont` for font family.
- `Typography.Body` / `BodyStrong` / `H1`–`H4` / `Small` for font size.
- `Typography.WeightRegular` / `WeightSemiBold` / `WeightBold` for font weight.
- `Spacing.Xs` / `Sm` / `Md` / `Lg` / `Xl` / `Xxl` for `Margin`, `Padding`, and grid gaps.

Literal `new FontFamily("Segoe UI")`, magic-number margins (`new Thickness(13)`), and ad-hoc `12.5` font sizes are forbidden. Surfaces with a justified deviation (e.g., a typographic experiment) document it inline with a comment that names the constant the deviation diverges from.

### O4 — Honor existing convention contracts

Migration MUST NOT regress the conventions documented in `CLAUDE.md`:

| Convention | Where it applies |
|------------|------------------|
| Set `Owner` via DTE HWND before `ShowDialog()` | Every modal dialog. `ThemeAwareWindow` does this; direct-inheritance surfaces must continue to do it themselves. |
| Cancel button is `IsCancel = true` and gets focus on `Loaded`; destructive button is *not* `AcceptButton` | `SafetyWarningDialog`, any new dialog with a destructive primary action. |
| Frozen brushes — never mutate after creation | Every brush in `ThemePalette` is `Freeze()`-d before being added to the dictionary. Surfaces never `Freeze()` a brush themselves. |
| Hoist `FontFamily` to static readonly | Replaced by `Typography.UiFont` / `MonoFont`. |
| `IWpfTextViewMargin` spinner pattern (Ellipse + StrokeDashArray + RotateTransform animation) | `SchemaProgressMargin` continues to use this pattern; only its stroke color migrates to `ThemeTokens.EditorSpinnerStroke`. |

### O5 — Behavior preservation

Migration is presentational. After migration each surface MUST:
- Open and close in the same situations as before.
- Save and load the same data with the same semantics.
- Honor the same keyboard shortcuts, focus order, and accessibility primitives (UI Automation names, `AutomationProperties.Name`).
- Pass the same regression smoke test that exercised the surface before migration (manual; no shell test harness exists).

A migration PR that changes any of the above is no longer just a migration — it crosses into behavioral change and must be split.

### O6 — Subscribe to `VariantChanged` only when imperative work is needed

Surfaces driven entirely by `SetResourceReference` (the common case) update automatically on theme change and MUST NOT subscribe to `ThemeRegistry.VariantChanged`.

Surfaces with imperative theme-dependent work — re-rendering a syntax-highlighted preview, regenerating a snapshot image, restarting an animation that captured a specific brush, etc. — MAY subscribe, with these constraints:
- Subscribe in `Loaded`, unsubscribe in `Unloaded`.
- Marshal to the dispatcher inside the handler if the work touches WPF state.
- Keep the work under 100 ms; longer work goes on a background task that posts back to the UI thread when ready.

### O7 — High Contrast graceful degradation

Surfaces MUST render legibly when `ThemeRegistry.Current == ThemeVariant.HighContrast`. The token system delegates colors to `SystemColors.*`, so most surfaces get this for free. Surfaces with custom drawing (e.g., the schema progress spinner arc) MUST use the same token references as everything else; bespoke gradients and per-color tweaks are not permitted in High Contrast mode.

### O8 — No `Application.Current.Resources` writes

A surface MUST NOT mutate `Application.Current.Resources` for theming. The host owns `Application.Current`. AKML's resources live in a private `ResourceDictionary` owned by `ThemeRegistry`, merged per-window via `Resources.MergedDictionaries.Add(...)`.

### O9 — Visible focus indicator on high-stakes controls (FR-018)

Surfaces MUST render a visible keyboard-focus indicator on **high-stakes interactive controls** using the `BorderFocus` token. High-stakes controls are:

- Primary actions (default OK / Apply / primary buttons).
- Destructive actions (Drop / Delete / Discard / Remove).
- Navigation items in tree, list, or sidebar nav patterns (e.g., Settings page nav, History query list).
- Search inputs (Options page search box, History search box).
- Toggle switches and other state-flipping affordances.

The focus indicator is implemented as a 1–2 px outer ring or border in `BorderFocus`, applied while the control has keyboard focus and removed when focus leaves. Surfaces MUST NOT suppress the focus visual via `FocusVisualStyle = null` on these controls. Other controls (regular checkboxes, sliders, plain comboboxes, free-text inputs, list-row items not used as nav) retain the WPF/OS default focus chrome and do not need explicit `BorderFocus` styling.

### O10 — Honor reduced-motion preference (FR-019)

Surfaces with motion MUST honor `SystemParameters.ClientAreaAnimation`:

- **Spinners and indeterminate progress.** When `ClientAreaAnimation` is `false`, surfaces MUST render a static "Loading…" text label or equivalent non-animated indicator instead of a rotating spinner. Concretely: `SchemaProgressMargin` replaces its `Ellipse` + `RotateTransform` with a static `TextBlock` reading `Loading…` in `Editor.SpinnerStroke`.
- **Theme-switch transitions.** When `ClientAreaAnimation` is `false`, theme switches MUST be instantaneous — no crossfade, no fade-through-blank, no animated brush interpolation. The dictionary swap completes in a single dispatcher tick.
- **Runtime preference change.** Surfaces respond to changes in this preference within the same session. `HostThemeWatcher` tracks the value and surfaces with running animations restart in the appropriate mode on the next `Loaded` cycle (existing animations need not be canceled mid-loop, but the next time the surface starts an animation it MUST honor the current preference).

Surfaces MUST NOT introduce new decorative animations beyond what's already present. The motion budget for this spec is: spinner (already exists) + theme-switch transition. Anything else requires a separate spec entry.

---

## Acceptance criteria for a migrated surface

A surface is considered migrated when **all** of the following hold:

1. ✅ **Static audit clean.** Grep across the surface's source file finds zero `Color.FromRgb`, `Color.FromArgb`, `#XXXXXX` (chrome), `Brushes.<X>` (chrome), or magic-number font sizes / margins outside the explicit allow-list.
2. ✅ **`SetResourceReference` for chrome.** Every chrome `Background` / `Foreground` / `BorderBrush` / `Fill` / `Stroke` assignment is a `SetResourceReference` call against a `ThemeTokens.*` constant.
3. ✅ **Typography and spacing centralized.** Font family / size / weight come from `Typography`; margins/padding come from `Spacing`.
4. ✅ **Theme-switch smoke passes.** Open the surface; switch theme via the AKML preference; the surface re-renders within 1 second with no leftover brushes from the prior variant.
5. ✅ **Live host-theme follow passes (when preference="system").** Change the host's VS / SSMS theme while the surface is open; the surface follows.
6. ✅ **High Contrast smoke passes.** Enable Windows High Contrast; the surface remains legible (text readable, controls distinguishable, no pure-color regions disappearing).
7. ✅ **Convention contracts upheld.** All applicable rows from O4 still hold.
8. ✅ **Behavior unchanged.** O5 verified by hand-running the surface's primary acceptance scenarios.
9. ✅ **Focus visible on high-stakes controls (O9).** Tab through the surface; every primary action, destructive action, nav item, search input, and toggle shows the `BorderFocus` indicator.
10. ✅ **Reduced-motion smoke passes (O10).** Disable Windows "Show animations"; reopen the surface (or trigger its motion); spinner becomes static, theme switch is instantaneous.

When all 10 criteria hold for every surface in the inventory, the feature ships.

---

## Reviewer's quick checklist

For each migration PR a reviewer should ask:

- [ ] Does the diff add any color literals outside `Ui/Theme/`?
- [ ] Does the diff use `SetResourceReference` everywhere a brush is assigned to a chrome property?
- [ ] Does the diff replace literal `new Thickness(...)` and font-size magic numbers with `Spacing` / `Typography` references?
- [ ] If the surface inherits `Window` directly, does it still set `Owner` via DTE HWND?
- [ ] If the surface is a safety-warning–style dialog, does Cancel still focus on `Loaded` and is the destructive button still *not* `AcceptButton`?
- [ ] If the surface has high-stakes controls (primary/destructive button, nav item, search input, toggle), do they show the `BorderFocus` indicator on keyboard focus, and is `FocusVisualStyle = null` *not* set on them?
- [ ] If the surface has motion (spinner / progress / theme-switch transition), does it branch on `SystemParameters.ClientAreaAnimation` and degrade gracefully when motion is disabled?
- [ ] Does the diff include a manual smoke screenshot in both Light and Dark themes?
