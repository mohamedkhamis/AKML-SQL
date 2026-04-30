# Phase 1 Data Model: WPF Theme & Visual Style Refresh

**Branch**: `016-wpf-theme-refresh` | **Date**: 2026-04-30

This refresh is presentational; it introduces no new persisted entities. The data model below describes the **in-memory** entities that compose the design system and how they flow through the runtime.

## Entities

### `ThemeVariant` (enum)

The internal representation of which palette is active.

| Value | Meaning |
|-------|---------|
| `Light` | Light palette (default for SSMS / VS Light themes). |
| `Dark` | Dark palette (default for SSMS / VS Dark themes). |
| `HighContrast` | Forced when Windows High Contrast mode is detected, regardless of user preference. Maps tokens to `SystemColors.*` brushes. |

Replaces the existing `VsThemeKind` (which had a never-fully-implemented `Blue` value).

---

### `ThemePreference` (string)

The user's stored choice in `config.json` under the existing `Theme` key. No schema change.

| Value | Resolution |
|-------|------------|
| `"light"` | Force `ThemeVariant.Light` (overridden by High Contrast). |
| `"dark"` | Force `ThemeVariant.Dark` (overridden by High Contrast). |
| `"system"` | Resolve to whichever variant matches the host's `VSColorTheme` luminance (overridden by High Contrast). |
| anything else | Treated as `"light"`. |

Persistence: existing `AppSettings.Theme` field. Validation: `ConfigManager.Load` already coerces null/empty to `"light"`.

---

### `ThemeToken` (string constant + role)

A semantic name plus a documented UI role. Tokens are addressed by string keys (compile-time–safe via constants in `ThemeTokens`).

**Fields**:

| Field | Type | Description |
|-------|------|-------------|
| `Key` | `string` | Resource-dictionary key, e.g. `"Akml.Brush.Surface.Panel"`. |
| `Role` | enum (documentation only) | Group: `Surface`, `Text`, `Border`, `Accent`, `Status`, `Editor`, `Chat`. |
| `Description` | `string` (doc) | When to use this token vs. its neighbors. |

Tokens are *not* persisted. They are compile-time constants (file: `Ui/Theme/ThemeTokens.cs`). The full catalog lives in `contracts/theme-tokens.md`.

**Validation rule**: Every chrome-color `SetResourceReference` in `src/AkmlSql.Shell.Shared` MUST resolve to a key declared in `ThemeTokens`. Static audit (D9 in research) enforces this.

---

### `ThemePalette` (internal map)

Per-variant `Token.Key → SolidColorBrush` map. One palette per `ThemeVariant`.

**Fields**:

| Field | Type | Description |
|-------|------|-------------|
| `Variant` | `ThemeVariant` | Which variant this palette serves. |
| `Brushes` | `IReadOnlyDictionary<string, SolidColorBrush>` | Token key → frozen `SolidColorBrush`. |

**Lifecycle**: created once at startup per variant, immutable afterwards. Brushes inside the dictionary are `Freeze()`-d before insertion.

**Validation rule**: Every key declared in `ThemeTokens` MUST appear in every `ThemePalette` — missing-key access is a programming error and throws at construction time (fail fast).

---

### `ThemeRegistry` (singleton, in-memory)

The runtime authority that holds the active palette and exposes it through a `ResourceDictionary` that AKML windows merge into their own `Resources`.

**Fields**:

| Field | Type | Description |
|-------|------|-------------|
| `Current` | `ThemeVariant` | Currently-active variant (after preference + High Contrast resolution). |
| `Resources` | `ResourceDictionary` | Active dictionary: `Token.Key → SolidColorBrush`. Mutated whole-brush on variant change; brushes themselves remain frozen. |
| `VariantChanged` | `event EventHandler` | Fires after a variant swap completes. Surfaces that need imperative work (e.g., re-running an animation) subscribe. |

**State transitions**:

```
[Startup]
   ├─ Read AppSettings.Theme → ThemePreference
   ├─ Subscribe HostThemeWatcher (VSColorTheme.ThemeChanged + SystemParameters.HighContrast)
   ├─ Resolve initial variant
   └─ Populate Resources from ThemePalette[variant]

[User changes AKML preference]   OR   [Host theme changes (preference="system")]   OR   [HighContrast toggles]
   ├─ Resolve new variant (HighContrast > preference > host detection)
   ├─ For each key in ThemePalette[newVariant].Brushes:
   │     Resources[key] = ThemePalette[newVariant].Brushes[key]
   ├─ DynamicResource consumers (every SetResourceReference call site) auto-resolve
   └─ Raise VariantChanged
```

**Validation rule**: Brush swaps complete within 1 second across all open AKML windows (FR-008 / SC-004). The whole-brush swap path runs on the UI thread; ~30 dictionary writes + WPF resource invalidation comfortably meets this budget on a modern machine.

---

### `HostThemeWatcher` (singleton, in-memory)

Listens for environmental theme changes and feeds them to `ThemeRegistry`. Also exposes the user's animation preference for motion-aware surfaces (FR-019).

**Fields**:

| Field | Type | Description |
|-------|------|-------------|
| `LastDetectedHostVariant` | `ThemeVariant` | Latest classification of the host's theme (Light/Dark; HighContrast handled separately). |
| `IsHighContrast` | `bool` | Mirrors `SystemParameters.HighContrast`. |
| `AnimationsEnabled` | `bool` | Mirrors `SystemParameters.ClientAreaAnimation`. Read by `SchemaProgressMargin` (spinner vs static label) and by `ThemeRegistry` (instantaneous swap vs fade). |

**Subscriptions**:

| Source | Event | Action |
|--------|-------|--------|
| `VSColorTheme` (VS PlatformUI) | `ThemeChanged` | Re-classify host luminance, update `LastDetectedHostVariant`, ask `ThemeRegistry` to re-resolve. |
| `SystemParameters` | `StaticPropertyChanged` (filter for `HighContrast`) | Update `IsHighContrast`, ask `ThemeRegistry` to re-resolve. |
| `SystemParameters` | `StaticPropertyChanged` (filter for `ClientAreaAnimation`) | Update `AnimationsEnabled`. Surfaces read the new value the next time they start an animation; running animations are not canceled mid-loop. |

**Events**:

| Event | When raised | Subscribers |
|-------|-------------|-------------|
| `AnimationsEnabledChanged` | After `AnimationsEnabled` flips. | `SchemaProgressMargin` listens to swap its visual representation (spinner ↔ static label) on the next `Loaded` cycle. Other surfaces typically don't need this — they branch on the field at start-of-animation time. |

**Failure mode**: if `VSColorTheme` is unavailable on a niche host (e.g., older SSMS 20 build), the watcher logs a warning at startup and falls back to a one-shot `SystemColors.Window` luminance read. Subsequent host theme changes are missed; user can still set Dark/Light explicitly via the AKML preference. `AnimationsEnabled` is independent of `VSColorTheme` and remains functional regardless.

---

### `Typography` (static class)

Theme-independent font definitions.

**Fields**:

| Field | Type | Value |
|-------|------|-------|
| `UiFont` | `static readonly FontFamily` | `new FontFamily("Segoe UI")` |
| `MonoFont` | `static readonly FontFamily` | `new FontFamily("Consolas")` |
| `Small` | `static readonly double` | `11.0` |
| `Body` | `static readonly double` | `12.5` |
| `BodyStrong` | `static readonly double` | `13.0` |
| `H4` | `static readonly double` | `14.0` |
| `H3` | `static readonly double` | `16.0` |
| `H2` | `static readonly double` | `19.0` |
| `H1` | `static readonly double` | `22.0` |
| `WeightRegular` | `static readonly FontWeight` | `FontWeights.Regular` |
| `WeightSemiBold` | `static readonly FontWeight` | `FontWeights.SemiBold` |
| `WeightBold` | `static readonly FontWeight` | `FontWeights.Bold` |

---

### `Spacing` (static class)

Theme-independent pixel scale.

**Fields**:

| Field | Type | Value |
|-------|------|-------|
| `Xs` | `static readonly double` | `4` |
| `Sm` | `static readonly double` | `8` |
| `Md` | `static readonly double` | `12` |
| `Lg` | `static readonly double` | `16` |
| `Xl` | `static readonly double` | `24` |
| `Xxl` | `static readonly double` | `32` |

---

### `ThemeAwareWindow` / `ThemeAwareUserControl` (base classes)

Convenience base classes for AKML-owned `Window` and `UserControl` instances.

**Responsibilities**:

| Responsibility | `ThemeAwareWindow` | `ThemeAwareUserControl` |
|----------------|---------------------|-------------------------|
| Merge `ThemeRegistry.Resources` into own `Resources` at construction | ✓ | ✓ |
| Set DTE-derived `Owner` HWND before `ShowDialog()` | ✓ | — |
| Apply default `Background` via `SetResourceReference(BackgroundProperty, ThemeTokens.SurfaceCanvas)` | ✓ | ✓ |
| Apply default `Foreground` via `SetResourceReference(ForegroundProperty, ThemeTokens.TextPrimary)` | ✓ | ✓ |
| Subscribe `ThemeRegistry.VariantChanged` on `Loaded`, unsubscribe on `Unloaded` | ✓ | ✓ |

These base classes are *opt-in* — surfaces may continue inheriting `Window` / `UserControl` directly if they need bespoke construction order (e.g., `SafetyWarningDialog`'s focus-on-Cancel discipline). Direct inheritors call a single `ThemeRegistry.AttachTo(this)` helper to get equivalent registration without subclassing.

---

## Surface inventory (entities affected, not new entities)

The 24 surfaces in scope (full names in `spec.md` Key Entities):

| Tier | Surface count | Examples |
|------|---------------|----------|
| Modal dialogs | 13 | `SettingsWindow`, `AboutDialog`, `SafetyWarningDialog`, `HistoryDiffWindow`, `RefactoringPreviewDialog`, `SnippetManagerDialog`, `ProfileEditorDialog`, ... |
| Tool windows + their controls | 5 | `HistoryToolWindow` + `HistoryToolWindowControl`, `AiChatToolWindow`, `DocumentOutlineToolWindow` + `DocumentOutlineControl`, `ObjectSearchWindow`, `CommandPaletteWindow` |
| Editor adornments | ~6 | `SchemaProgressMargin`, `EditorToolbar`, `CompletionController` (popup chrome), `PeekDefinitionControl`, analysis tooltip chrome |

Each surface, after migration, must:
- Reference only tokens defined in `ThemeTokens` for chrome colors.
- Use `SetResourceReference` rather than direct brush assignment for theme-bound properties.
- Read typography and spacing from `Typography` / `Spacing` rather than literals.
- Continue to satisfy any pre-existing CLAUDE.md convention specific to its tier (e.g., DTE owner for modals, ellipse-spinner pattern for margins, FR-005 cancel-button discipline for safety dialogs).

## Persistence

No persistence schema changes. The existing `AppSettings.Theme` field continues to hold one of `"light"` / `"dark"` / `"system"`. No migration step required.

## Concurrency model

- `ThemeRegistry` is UI-thread–owned. All variant swaps occur on the WPF dispatcher.
- `HostThemeWatcher` event handlers marshal back to the dispatcher via `Dispatcher.BeginInvoke` before touching the registry.
- `ThemePalette` instances are immutable post-construction; safe to read from any thread (though there's no current need to).
- Frozen brushes are inherently thread-safe (WPF guarantee).

## Diagram

```
+--------------------+        +----------------------+       +------------------+
| HostThemeWatcher   |  →     | ThemeRegistry        |  →    | ResourceDictionary
| (VS event +        |        | (singleton, UI thread|       | merged into each
|  HighContrast)     |        |  variant resolver)   |       | AKML Window/Control
+--------------------+        +----------------------+       +------------------+
                                       ↑
                                       │ palette lookup
                                       │
                              +--------+---------+
                              | ThemePalette[V]  |   for V in {Light, Dark,
                              | (immutable map)  |              HighContrast}
                              +------------------+
                                       ↑
                                       │ key constants
                                       │
                              +------------------+
                              | ThemeTokens      |
                              | (string consts)  |
                              +------------------+
```
