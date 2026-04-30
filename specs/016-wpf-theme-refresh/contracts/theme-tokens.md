# Contract: Theme Token Catalog

**Branch**: `016-wpf-theme-refresh` | **Date**: 2026-04-30
**Status**: Authoritative public contract for the AKML WPF design system.

This document is the **single source of truth** for every theme token surfaces are allowed to consume. Any chrome color used by an AKML-owned WPF surface MUST resolve to a token below. Surfaces MUST NOT define their own colors.

The token list is intentionally small (~30 entries). When a new surface needs a color that none of these tokens covers, the answer is almost always "use an existing token in a different way" — adding a new token requires updating this contract.

---

## Resource key format

All token keys live in the AKML resource namespace and follow the pattern:

```
Akml.Brush.<Group>.<Name>
```

`<Group>` is one of: `Surface`, `Text`, `Border`, `Accent`, `Status`, `Editor`, `Chat`.

C# constants matching every key live in `Ui/Theme/ThemeTokens.cs`. Surfaces use the constants, never raw strings.

---

## Surface group

Backgrounds, panels, hover states, and selection states.

| Token | Key | Role | Light | Dark | High Contrast |
|-------|-----|------|-------|------|----------------|
| `SurfaceCanvas` | `Akml.Brush.Surface.Canvas` | Outermost dialog / window background. | `#F0F0F0` | `#2D2D3B` | `SystemColors.WindowBrush` |
| `SurfacePanel` | `Akml.Brush.Surface.Panel` | Content panel (inside a dialog), tool-window content area. | `#FFFFFF` | `#1E1E2E` | `SystemColors.WindowBrush` |
| `SurfaceElevated` | `Akml.Brush.Surface.Elevated` | Cards / floating elevated regions inside a panel. | `#FFFFFF` | `#252836` | `SystemColors.WindowBrush` |
| `SurfaceSidebar` | `Akml.Brush.Surface.Sidebar` | Settings nav column, History query list column. | `#FFFFFF` | `#1E1E2E` | `SystemColors.ControlBrush` |
| `SurfaceInput` | `Akml.Brush.Surface.Input` | Editable inputs: TextBox, ComboBox, search box. | `#FFFFFF` | `#2D2D3B` | `SystemColors.WindowBrush` |
| `SurfaceInputReadOnly` | `Akml.Brush.Surface.InputReadOnly` | Read-only inputs and alternating-row backgrounds. | `#F8F8F8` | `#252836` | `SystemColors.ControlBrush` |
| `SurfaceHover` | `Akml.Brush.Surface.Hover` | Pointer hover over rows/items in lists, trees, menu items. | `#F0F0F0` | `#252836` | `SystemColors.HighlightBrush` |
| `SurfaceSelection` | `Akml.Brush.Surface.Selection` | Selected list/tree row background (subtle). | `#1F0078D4` (≈12% accent) | `#260078D4` (≈15% accent) | `SystemColors.HighlightBrush` |
| `SurfaceSelectionStrong` | `Akml.Brush.Surface.SelectionStrong` | Selected state where a strong accent fill is needed (e.g., active filter chip). | `#0078D4` | `#0078D4` | `SystemColors.HighlightBrush` |

## Text group

Foreground colors. Each token has an implicit minimum contrast contract against the surfaces it commonly appears on.

| Token | Key | Role | Light | Dark | High Contrast |
|-------|-----|------|-------|------|----------------|
| `TextPrimary` | `Akml.Brush.Text.Primary` | Default body text, primary headings. | `#1E1E1E` | `#D4D4D4` | `SystemColors.WindowTextBrush` |
| `TextSecondary` | `Akml.Brush.Text.Secondary` | Secondary labels, descriptions, metadata, inactive nav items. | `#555555` | `#8892A8` | `SystemColors.GrayTextBrush` |
| `TextDisabled` | `Akml.Brush.Text.Disabled` | Disabled controls' text. | `#A0A0A0` | `#5C6370` | `SystemColors.GrayTextBrush` |
| `TextPlaceholder` | `Akml.Brush.Text.Placeholder` | Placeholder text inside empty inputs. | `#A0A0A0` | `#6E6E6E` | `SystemColors.GrayTextBrush` |
| `TextLink` | `Akml.Brush.Text.Link` | Hyperlink-styled text and "Advanced search" affordances. | `#0078D4` | `#4F8CFF` | `SystemColors.HotTrackBrush` |
| `TextOnAccent` | `Akml.Brush.Text.OnAccent` | Foreground placed on `Accent.Primary` background (e.g., primary button label). | `#FFFFFF` | `#FFFFFF` | `SystemColors.HighlightTextBrush` |
| `TextOnDanger` | `Akml.Brush.Text.OnDanger` | Foreground placed on `Status.Danger` background (e.g., destructive button label). | `#FFFFFF` | `#FFFFFF` | `SystemColors.HighlightTextBrush` |

## Border group

| Token | Key | Role | Light | Dark | High Contrast |
|-------|-----|------|-------|------|----------------|
| `BorderDefault` | `Akml.Brush.Border.Default` | Default border for inputs, panels, cards. | `#CCCCCC` | `#3A3F4E` | `SystemColors.WindowFrameBrush` |
| `BorderStrong` | `Akml.Brush.Border.Strong` | Higher-contrast border for emphasis. | `#999999` | `#5C6370` | `SystemColors.WindowFrameBrush` |
| `BorderSubtle` | `Akml.Brush.Border.Subtle` | Internal separators inside cards/panels. | `#EAEAEA` | `#2A2D3A` | `SystemColors.ControlDarkBrush` |
| `BorderFocus` | `Akml.Brush.Border.Focus` | Keyboard-focus ring on inputs and buttons. | `#0078D4` | `#4F8CFF` | `SystemColors.HotTrackBrush` |
| `BorderSplitter` | `Akml.Brush.Border.Splitter` | `GridSplitter` thumb color. | `#CCCCCC` | `#3A3F4E` | `SystemColors.ControlDarkBrush` |

## Accent group

| Token | Key | Role | Light | Dark | High Contrast |
|-------|-----|------|-------|------|----------------|
| `AccentPrimary` | `Akml.Brush.Accent.Primary` | Primary action background, active nav-item background, accent fills. | `#0078D4` | `#0078D4` | `SystemColors.HighlightBrush` |
| `AccentPrimaryHover` | `Akml.Brush.Accent.PrimaryHover` | Hover state on accent backgrounds. | `#106EBE` | `#1A8CDC` | `SystemColors.HighlightBrush` |
| `AccentPrimaryPressed` | `Akml.Brush.Accent.PrimaryPressed` | Pressed/active state on accent backgrounds. | `#005A9E` | `#0066B5` | `SystemColors.HighlightBrush` |

## Status group

Semantic colors for product-meaningful states. Light and Dark values are deliberately the same hue family — only luminance shifts — so the meaning reads identically across themes.

| Token | Key | Role | Light | Dark | High Contrast |
|-------|-----|------|-------|------|----------------|
| `StatusSuccess` | `Akml.Brush.Status.Success` | "Open tab" icon, success badges, completion confirmations. | `#2ECC71` | `#3DD68C` | `SystemColors.HighlightBrush` |
| `StatusWarning` | `Akml.Brush.Status.Warning` | Active stars/favorites, warning badges, mild caution UI. | `#F39C12` | `#FBBF24` | `SystemColors.HighlightBrush` |
| `StatusDanger` | `Akml.Brush.Status.Danger` | Destructive button background ("Drop", "Delete"), error states, "closed" tab icon. | `#E74C3C` | `#FF5C5C` | `SystemColors.HighlightBrush` |
| `StatusInfo` | `Akml.Brush.Status.Info` | Informational badges, "currently open" version highlight in History. | `#0078D4` | `#4F8CFF` | `SystemColors.HotTrackBrush` |

## Editor group

Tokens specific to in-editor adornments (margins, popups, tooltips). Kept in their own group because their context is the host editor surface, not an AKML window.

| Token | Key | Role | Light | Dark | High Contrast |
|-------|-----|------|-------|------|----------------|
| `EditorMarginBackground` | `Akml.Brush.Editor.MarginBackground` | Schema-progress margin gutter background. | `#FBFBFB` | `#252526` | `SystemColors.ControlBrush` |
| `EditorSpinnerStroke` | `Akml.Brush.Editor.SpinnerStroke` | Spinner arc stroke color. | `#0078D4` | `#4F8CFF` | `SystemColors.HotTrackBrush` |
| `EditorPopupBackground` | `Akml.Brush.Editor.PopupBackground` | Completion popup, peek control, analysis tooltip background. | `#FFFFFF` | `#252526` | `SystemColors.WindowBrush` |
| `EditorPopupBorder` | `Akml.Brush.Editor.PopupBorder` | Border around editor popups. | `#CCCCCC` | `#3A3F4E` | `SystemColors.WindowFrameBrush` |

## Chat group

Tokens specific to the AI Chat tool window's message bubbles.

| Token | Key | Role | Light | Dark | High Contrast |
|-------|-----|------|-------|------|----------------|
| `ChatUserBubble` | `Akml.Brush.Chat.UserBubble` | Background of messages from the user. | `#E5F1FB` | `#1A3A5C` | `SystemColors.WindowBrush` |
| `ChatAssistantBubble` | `Akml.Brush.Chat.AssistantBubble` | Background of messages from the assistant. | `#F5F5F5` | `#252836` | `SystemColors.ControlBrush` |
| `ChatSystemBubble` | `Akml.Brush.Chat.SystemBubble` | Background of system / status messages. | `#FFF8E1` | `#3A3000` | `SystemColors.InfoBrush` |

---

## Contrast contract (WCAG AA)

For each Light and Dark variant, the following pairings MUST satisfy WCAG AA contrast ratios:

| Foreground | Background | Min ratio | AA bar |
|------------|------------|-----------|--------|
| `TextPrimary` | `SurfaceCanvas` | 4.5:1 | body text |
| `TextPrimary` | `SurfacePanel` | 4.5:1 | body text |
| `TextSecondary` | `SurfacePanel` | 4.5:1 | body text |
| `TextOnAccent` | `AccentPrimary` | 4.5:1 | body text |
| `TextOnDanger` | `StatusDanger` | 4.5:1 | body text |
| `BorderDefault` | `SurfaceCanvas` | 3:1 | UI component |
| `BorderFocus` | `SurfaceCanvas` | 3:1 | UI component |
| `AccentPrimary` | `SurfaceCanvas` | 3:1 | UI component |

High Contrast variant satisfies these by construction (delegates to `SystemColors.*`).

---

## Token consumption rules (binding contract)

Every consumer of these tokens MUST follow these rules:

1. **No raw color literals for chrome.** No `Color.FromRgb(...)`, `Color.FromArgb(...)`, `Brushes.<X>`, or `#XXXXXX` literals in `src/AkmlSql.Shell.Shared/**/*.cs` outside `Ui/Theme/` and an explicit allow-list of semantic constants. Static audit (research D9) enforces this.
2. **Use `SetResourceReference`, not direct property assignment**, for any chrome color that should track theme changes:

   ```csharp
   border.SetResourceReference(Border.BackgroundProperty, ThemeTokens.SurfacePanel);
   ```

   Direct `border.Background = brush;` does **not** participate in live theme switching and is forbidden for chrome.

3. **Reference tokens via `ThemeTokens.<Constant>`**, never via raw key strings. The `ThemeTokens` class is the only place a key string appears.

4. **Adding a token requires updating this document first.** PRs that add a new token without amending this catalog are rejected.

5. **Removing a token requires migrating all consumers first.** A token is removed only after a grep for its `ThemeTokens.<Constant>` usage in `src/AkmlSql.Shell.Shared` returns zero hits.

---

## Stability

This catalog is the public contract surfaces depend on. Renames and removals are breaking changes inside the shared project and require a coordinated migration. Adding a new token is non-breaking.
