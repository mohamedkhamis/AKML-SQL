# WPF Theming

How to make AKML's WPF surfaces follow the active theme. Read this before
adding a new dialog, tool window, or editor adornment.

The authoritative documents live in `specs/016-wpf-theme-refresh/`:

- `contracts/theme-tokens.md` — the token catalog (the colours).
- `contracts/theme-aware-surface.md` — the obligations every surface must satisfy.
- `quickstart.md` — operational recipes (longer than this page).

This file is the single page you should be able to read end-to-end and start
writing theme-aware UI without further questions. Cross-links to the
authoritative specs are inline where they matter.

---

## TL;DR

1. **Inherit `ThemeAwareWindow` for modal dialogs**, `ThemeAwareUserControl`
   for tool-window content. The base classes attach the theme registry, set
   `Background` / `Foreground` to the right tokens, and (for `Window`) wire
   up the DTE-derived `Owner` HWND. You usually don't need anything else.
2. **Set chrome via `SetResourceReference`, never direct brush assignment.**
   That's how theme switching propagates without re-opening the surface.
3. **Reference colours via `ThemeTokens.<Name>` constants**, never raw hex
   or `Brushes.X` literals.
4. **Use `Typography.*` for fonts and `Spacing.*` for margins/padding.**
   No `new FontFamily("Segoe UI")`, no `new Thickness(13)`.
5. **Run `scripts/audit-wpf-theme.ps1` before every PR.** It catches drift.

If your change adds a chrome colour literal anywhere outside
`src/AkmlSql.Shell.Shared/Ui/Theme/`, it's a bug.

---

## The mental model

Three pieces:

```
                ┌────────────────────────┐
                │     ThemeRegistry      │  singleton
                │  (Resources dictionary)│
                └────────────┬───────────┘
                             │ AttachTo(this)
                             ▼
            ┌────────────────────────────────┐
            │  Window / UserControl Resources │
            │   (merged dictionaries)         │
            └────────────────────────────────┘
                             ▲
                             │ SetResourceReference(prop, ThemeTokens.X)
                             │
            ┌────────────────────────────────┐
            │     Border / Button / etc.      │
            └────────────────────────────────┘
```

- `ThemeRegistry` is a singleton holding a `ResourceDictionary` keyed by
  token strings (`"Akml.Brush.Surface.Panel"` etc.). On variant change it
  swaps the brushes inside the dictionary by key.
- A surface calls `ThemeRegistry.Instance.AttachTo(this)` once during
  construction. That merges the registry's dictionary into the surface's
  own `Resources` so resource lookups find the tokens.
- A surface sets chrome via `SetResourceReference(prop, ThemeTokens.<Name>)`
  rather than direct brush assignment. WPF then re-resolves the property
  whenever the registry swaps brushes — no manual subscription needed.

`ThemeAwareWindow` and `ThemeAwareUserControl` do the `AttachTo` plus
default `Background` / `Foreground` references for you. Use them. Direct
inheritance (`: Window`) is allowed only when you need bespoke construction
order — see [Special cases](#special-cases) below.

---

## Adding a new theme-aware dialog

Recipe for a new modal dialog (the most common case).

```csharp
using System.Windows;
using System.Windows.Controls;
using AkmlSql.Shell.Shared.Ui.Theme;

internal sealed class MyDialog : ThemeAwareWindow
{
    public MyDialog()
    {
        // ThemeAwareWindow has already merged the registry, set the
        // DTE-derived Owner, and applied Background/Foreground references.

        Title  = "My dialog";
        Width  = 480;
        Height = 360;

        var root = new StackPanel { Margin = new Thickness(Spacing.Lg) };

        // Heading
        var heading = new TextBlock
        {
            Text       = "Heading text",
            FontFamily = Typography.UiFont,
            FontSize   = Typography.H3,
            FontWeight = Typography.WeightSemiBold,
            Margin     = new Thickness(0, 0, 0, Spacing.Md),
        };
        heading.SetResourceReference(TextBlock.ForegroundProperty, ThemeTokens.TextPrimary);
        root.Children.Add(heading);

        // Body
        var body = new TextBlock
        {
            Text         = "Body text",
            FontFamily   = Typography.UiFont,
            FontSize     = Typography.Body,
            TextWrapping = TextWrapping.Wrap,
            Margin       = new Thickness(0, 0, 0, Spacing.Lg),
        };
        body.SetResourceReference(TextBlock.ForegroundProperty, ThemeTokens.TextSecondary);
        root.Children.Add(body);

        // Footer with primary + secondary buttons
        var footer = new DockPanel { LastChildFill = false };

        var cancelBtn = new Button
        {
            Content  = "Cancel",
            IsCancel = true,
            Padding  = new Thickness(Spacing.Lg, Spacing.Sm, Spacing.Lg, Spacing.Sm),
            Margin   = new Thickness(Spacing.Sm, 0, 0, 0),
            FocusVisualStyle = FocusVisualStyles.HighStakes,
        };
        cancelBtn.SetResourceReference(Button.BackgroundProperty,  ThemeTokens.SurfaceElevated);
        cancelBtn.SetResourceReference(Button.ForegroundProperty,  ThemeTokens.TextPrimary);
        cancelBtn.SetResourceReference(Button.BorderBrushProperty, ThemeTokens.BorderDefault);
        DockPanel.SetDock(cancelBtn, Dock.Right);
        footer.Children.Add(cancelBtn);

        var okBtn = new Button
        {
            Content   = "OK",
            IsDefault = true,
            Padding   = new Thickness(Spacing.Lg, Spacing.Sm, Spacing.Lg, Spacing.Sm),
            Margin    = new Thickness(Spacing.Sm, 0, 0, 0),
            FocusVisualStyle = FocusVisualStyles.HighStakes,
        };
        okBtn.SetResourceReference(Button.BackgroundProperty, ThemeTokens.AccentPrimary);
        okBtn.SetResourceReference(Button.ForegroundProperty, ThemeTokens.TextOnAccent);
        DockPanel.SetDock(okBtn, Dock.Right);
        footer.Children.Add(okBtn);

        root.Children.Add(footer);
        Content = root;
    }
}
```

Three things to call out:

- **No raw colours anywhere.** Every brush is a `SetResourceReference`
  against a `ThemeTokens.*` constant.
- **No magic numbers.** Margins use `Spacing.*`; font sizes use
  `Typography.*`.
- **Cancel discipline.** `IsCancel = true` on Cancel; `IsDefault = true`
  on OK is fine *only when OK is non-destructive*. For destructive primary
  actions (Drop / Delete), see
  `src/AkmlSql.Shell.Shared/Safety/SafetyWarningDialog.cs` — Cancel
  becomes the focused control on `Loaded` and the destructive button is
  *not* `IsDefault`.

Tool-window content (`UserControl`) is the same pattern with
`ThemeAwareUserControl` instead of `ThemeAwareWindow`.

---

## Migrating an existing surface

When porting an existing dialog or tool window to the system, work through
this checklist for each file:

1. **Identify the surface.** Inventory in
   `specs/016-wpf-theme-refresh/data-model.md` § Surface inventory.
2. **Read the surface's source.** Note every place it sets a brush, font,
   or margin literal.
3. **Change the base class.** `: Window` → `: ThemeAwareWindow`,
   `: UserControl` → `: ThemeAwareUserControl`. Drop any explicit `Owner`
   handling — the base does it.
4. **Replace brush assignments with `SetResourceReference`.** Use the
   token role table in `contracts/theme-tokens.md` to pick the right
   token. When in doubt:
   - Backgrounds → `Surface.Canvas` / `Surface.Panel` / `Surface.Elevated`
     / `Surface.Sidebar` / `Surface.Input` / `Surface.Hover` / `Surface.Selection`
   - Foregrounds → `Text.Primary` / `Text.Secondary` / `Text.Disabled` /
     `Text.Placeholder` / `Text.Link` / `Text.OnAccent` / `Text.OnDanger`
   - Borders → `Border.Default` / `Border.Strong` / `Border.Subtle` /
     `Border.Focus` / `Border.Splitter`
   - Accent fills → `Accent.Primary` / `AccentPrimary.Hover` / `AccentPrimary.Pressed`
   - Status (success/warning/danger/info icons & badges) → `Status.Success`
     / `Status.Warning` / `Status.Danger` / `Status.Info`
5. **Replace font literals with `Typography.*`.** Drop any local
   `static readonly FontFamily` declarations that duplicate
   `Typography.UiFont` / `MonoFont`.
6. **Replace margin/padding magic numbers with `Spacing.*`.**
7. **Delete any surface-local `ThemeBrushSet` / private brush helpers**
   (e.g., the one in the original `SettingsWindow.cs`).
8. **Add `FocusVisualStyle = FocusVisualStyles.HighStakes`** to high-stakes
   controls — primary actions, destructive actions, navigation items,
   search inputs, toggle switches. See [O9 in
   `theme-aware-surface.md`](../specs/016-wpf-theme-refresh/contracts/theme-aware-surface.md#o9--visible-focus-indicator-on-high-stakes-controls-fr-018).
9. **Run the static audit** (next section). It must report zero hits in
   your file (modulo intentional `Brushes.Transparent` placeholders).
10. **Smoke test** — Light + Dark + live switch + host follow + High
    Contrast + focus + reduced motion. Procedure in
    `quickstart.md` § 6.

When migrating a surface that uses `FrameworkElementFactory` (data
templates, item container styles), use the convenience extension:

```csharp
factory.SetResourceBinding(TextBlock.ForegroundProperty, ThemeTokens.TextPrimary);
```

`SetResourceBinding` lives in `Ui/Theme/FocusVisualStyles.cs` and wraps
the value in a `DynamicResourceExtension`. Inside `Style.Setter` the
equivalent is `new DynamicResourceExtension(ThemeTokens.X)`.

---

## Adding a new theme token

**Don't, unless absolutely necessary.** The catalog is intentionally small
(~35 tokens). When a new surface needs a colour that no existing token
covers, the answer is almost always "use an existing token in a different
way" — adding a new token requires updating the contract.

If you must:

1. **Confirm no existing token covers your role.** Search
   `contracts/theme-tokens.md`. If still unclear, propose a token in your
   PR description and let the reviewer adjudicate.
2. **Open the contract first.** Add a row in the appropriate group in
   `contracts/theme-tokens.md` with Light, Dark, and High Contrast values.
   Verify contrast against the WCAG AA contract section.
3. **Add the constant** to `Ui/Theme/ThemeTokens.cs`. Key format:
   `"Akml.Brush.<Group>.<Name>"`.
4. **Add the brush mapping** in every variant's palette in
   `Ui/Theme/ThemePalette.cs` (Light, Dark, High Contrast). Construction
   throws if any variant is missing the new key.
5. **Update `ThemeTokens.All`** with the new constant — used by tests and
   the audit.
6. **Update this page** if the addition changes the role taxonomy.
7. **Only then**, use the new token in your surface.

---

## The audit script

`scripts/audit-wpf-theme.ps1` greps the shared shell project for chrome
literals and exits non-zero on any unjustified hit. It's the gate every
migration PR has to pass.

Patterns it flags:

- `Color.FromRgb(...)`
- `Color.FromArgb(...)`
- `Brushes.<Capitalised>` (e.g., `Brushes.Red`, `Brushes.Transparent`)
- Bare `#RRGGBB` hex literals

Exempt files: anything under `src/AkmlSql.Shell.Shared/Ui/Theme/` is
considered design-system internal.

Run it from the repo root:

```powershell
pwsh.exe ./scripts/audit-wpf-theme.ps1
```

The script doesn't currently know about FR-003 carveouts (domain icon
palettes, semantic constants like the safety dialog amber border, the 30%
yellow `HistorySearchHighlight`). Those are tracked manually in
`specs/016-wpf-theme-refresh/audit-baseline.txt`. A future polish task
(T052) adds an explicit allow-list parameter.

---

## Common patterns

### Hosting a popup `Window` (not a dialog)

`Command Palette` and `Object Search` use `WindowStyle = WindowStyle.None`
+ `AllowsTransparency = true` to render rounded corners. The base
`ThemeAwareWindow` sets `Background = SurfaceCanvas`, which would paint a
solid rectangle and hide the rounded corners. Override it after the base
constructor runs:

```csharp
public MyPopup() : base()
{
    AllowsTransparency = true;
    WindowStyle        = WindowStyle.None;
    Background         = Brushes.Transparent;   // theme-independent placeholder
    // ...
}
```

`Brushes.Transparent` is the one place direct brush assignment is
acceptable — it has no theme meaning. Document the "why" in a comment so
future readers and the audit reviewer don't assume it's drift.

### Imperative state-driven theming

When a surface needs to swap a brush imperatively (e.g., a filter chip
that toggles between active/inactive), use `SetResourceReference` to
re-bind the property — don't reach into `ThemeRegistry.Instance.Resources`
directly:

```csharp
private static void ApplyFilterTabState(Button tab, bool isActive)
{
    if (isActive)
    {
        tab.SetResourceReference(Control.BackgroundProperty,  ThemeTokens.SurfaceSelectionStrong);
        tab.SetResourceReference(Control.BorderBrushProperty, ThemeTokens.AccentPrimary);
    }
    else
    {
        tab.SetResourceReference(Control.BackgroundProperty,  ThemeTokens.SurfaceElevated);
        tab.SetResourceReference(Control.BorderBrushProperty, ThemeTokens.BorderDefault);
    }
}
```

This keeps the live theme-switch behaviour: when the user changes theme
while the chip is active, the active brushes still update.

### Reading a brush directly from the registry (rare)

Inside an `IValueConverter.Convert`, the binding fires after the visual
tree is constructed, so `SetResourceReference` is awkward (you'd need to
return a `DynamicResourceExtension`, which most converters don't). Read
the brush from the registry directly:

```csharp
public object Convert(object value, ...)
{
    var key = (bool)value ? ThemeTokens.StatusSuccess : ThemeTokens.StatusDanger;
    return ThemeRegistry.Instance.Resources[key];
}
```

The tradeoff: the brush returned is the one current at the time of
`Convert`. WPF re-evaluates value-converter bindings when the source value
changes, but not when the registry swaps brushes. In practice this is
fine for icon foregrounds whose source value (`IsOpen`, `IsFavorite`)
typically changes when the user interacts with the data, but it means
existing-state icon colours don't track theme switches in real-time. If
that matters, drive the colour from a `MultiBinding` that depends on a
property on the registry.

For most surfaces in the inventory this isn't a problem — theme switches
re-enter the layout and re-evaluate bindings naturally.

### Reduced motion

When `SystemParameters.ClientAreaAnimation` is `false`, motion is
suppressed. `HostThemeWatcher.AnimationsEnabled` exposes this; subscribe
to `HostThemeWatcher.AnimationsEnabledChanged` if your surface has
animations:

```csharp
// In Loaded:
HostThemeWatcher.Instance.AnimationsEnabledChanged += OnAnimationsEnabledChanged;
ApplyMotionPreference(HostThemeWatcher.Instance.AnimationsEnabled);

// In Unloaded:
HostThemeWatcher.Instance.AnimationsEnabledChanged -= OnAnimationsEnabledChanged;
```

The current scope: spinners and the theme-switch transition. Don't
introduce new decorative animations without a separate spec entry —
motion budget is intentionally tight.

### Frozen brushes

Don't `Freeze()` brushes yourself. Every brush in `ThemePalette` is
already frozen before being added to the dictionary, and
`SetResourceReference` shares the frozen instance across consumers. The
old `private static SolidColorBrush Freeze(SolidColorBrush b) { b.Freeze(); return b; }`
helper from the pre-system code is unnecessary in migrated surfaces.

### Hoisted FontFamily

`Typography.UiFont` and `Typography.MonoFont` are the canonical
class-level statics. Don't redeclare them locally. If you genuinely need a
different family (e.g., `"Cascadia Code, Consolas"` because the surface
prefers it), declare a `static readonly FontFamily` at file scope; never
do `new FontFamily(...)` per call site.

---

## Special cases

### When to inherit `Window` directly instead of `ThemeAwareWindow`

Allowed only when the surface needs bespoke construction order. The
canonical example is `SafetyWarningDialog`, where Cancel must be
`IsCancel = true` AND focused on `Loaded`, and the destructive button
must *not* be `AcceptButton`. The base class's defaults conflict with
that, so it inherits `Window` and calls
`ThemeRegistry.Instance.AttachTo(this)` itself.

If you take this route, you also assume responsibility for:

- `ThemeRegistry.Instance.AttachTo(this)` in the constructor.
- `Owner` via DTE HWND (pattern in
  `src/AkmlSql.Shell.Shared/History/HistoryDiffWindow.cs`, copied into
  `ThemeAwareWindow.OnLoadedSetOwner`).
- `WindowStartupLocation = CenterOwner`.
- Background/Foreground tokens via `SetResourceReference`.

### Window factories (no inheritance possible)

`CommandPaletteWindow` and `ObjectSearchWindow.ShowInputDialog` create a
`Window` instance imperatively (factory pattern, not subclass). Use the
manual attach pattern:

```csharp
var window = new Window { ... };
ThemeRegistry.Instance.AttachTo(window);
window.SetResourceReference(Window.BackgroundProperty, ThemeTokens.SurfacePanel);
window.SetResourceReference(Window.ForegroundProperty, ThemeTokens.TextPrimary);
```

### Editor adornments and margins

`IWpfTextViewMargin` and adornment-layer hosts don't inherit `Window` or
`UserControl`. They construct their own visual tree and aren't attached
to a logical-tree root that would inherit the registry. Two patterns:

- **The host control still gets the registry** if it's a `FrameworkElement`
  added under a tool window or wrapped in a `UserControl` — call
  `ThemeRegistry.Instance.AttachTo(rootElement)` on the topmost element
  you control.
- **Brushes need to be pulled from the registry** for cases where
  `SetResourceReference` doesn't fit the host's API
  (`IGlyphFactory.GenerateGlyph` returns a `UIElement` per line; you can't
  re-bind it across theme switches without re-rendering). Read from
  `ThemeRegistry.Instance.Resources[key]` and accept that lines rendered
  under the prior theme will repaint when their text run changes.

### Domain icon palettes (FR-003 carveout)

Some palettes are deliberately theme-independent: `SqlPromptIcons.cs` (12
SQL object type colours), the safety-dialog severity constants
(`AmberBorder` / `ErrorBorder` / `BtnPrimary`), the 30 % yellow
`HistorySearchHighlight`, `SettingsWindow`'s 5 setting-kind badge
colours. These are domain semantic constants — "Table is blue" reads the
same in Light and Dark on purpose. They're listed in
`audit-baseline.txt` as carveouts and should NOT migrate to tokens.

If you're tempted to "clean up" one of these literals, stop and check
the spec — you're probably about to break a deliberate signalling
choice.

---

## Conventions you must not regress

These are listed in `theme-aware-surface.md` § O4. Quick summary:

| Convention | Where it applies |
|------------|------------------|
| `Owner` set via DTE HWND before `ShowDialog()` | Every modal dialog. `ThemeAwareWindow` does it. |
| Cancel is `IsCancel = true` AND focused on `Loaded`; destructive button is *not* `IsDefault` | Safety / destructive dialogs. |
| Brushes are pre-frozen by the palette; never call `Freeze()` yourself | Every surface. |
| `Typography.UiFont` / `MonoFont` instead of `new FontFamily(...)` | Every surface. |
| `IWpfTextViewMargin` spinner uses `Ellipse` + `StrokeDashArray` + `RotateTransform` (not a rotated `Border`) | `SchemaProgressMargin` and any future spinner. |

`CLAUDE.md` § "WPF UI conventions" is the master copy of these.

---

## Where to ask

- **Token role unclear** ("which token do I use for X?") — re-read
  `contracts/theme-tokens.md` § the relevant group. If still unclear,
  propose a token in the PR description and the reviewer adjudicates.
- **Need a feature the system doesn't support** (a 4th theme variant,
  dynamic per-row brushes that aren't in the catalog) — re-read
  `specs/016-wpf-theme-refresh/research.md` to confirm it wasn't already
  considered and rejected. If it's genuinely new, open a discussion
  before writing code.
- **Convention conflict** (e.g., a surface that needs to ignore live
  theme switching for a specific frame) — document the deviation in the
  surface-local comment AND in `research.md`, and proceed only after
  reviewer agreement.

---

## Reference

- Token catalog: [`specs/016-wpf-theme-refresh/contracts/theme-tokens.md`](../specs/016-wpf-theme-refresh/contracts/theme-tokens.md)
- Surface obligations: [`specs/016-wpf-theme-refresh/contracts/theme-aware-surface.md`](../specs/016-wpf-theme-refresh/contracts/theme-aware-surface.md)
- Operational recipes (longer): [`specs/016-wpf-theme-refresh/quickstart.md`](../specs/016-wpf-theme-refresh/quickstart.md)
- Data model (`ThemeRegistry`, `ThemePalette`, etc.): [`specs/016-wpf-theme-refresh/data-model.md`](../specs/016-wpf-theme-refresh/data-model.md)
- Static-audit baseline & carveouts: [`specs/016-wpf-theme-refresh/audit-baseline.txt`](../specs/016-wpf-theme-refresh/audit-baseline.txt)
- Project-wide WPF conventions: [`CLAUDE.md`](../CLAUDE.md) § WPF UI conventions
