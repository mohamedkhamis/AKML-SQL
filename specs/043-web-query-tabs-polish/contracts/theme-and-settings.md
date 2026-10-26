# Contract: theme tokens, generated CSS and editor settings

**Branch**: `043-web-query-tabs-polish` | **Date**: 2026-10-21

The theme CSS is generated from `docs/theme-tokens.json` by `scripts/generate-theme-css.ps1`
and never hand-edited (constitution II). Both outputs (web and site) are regenerated in the same
change so `build.ps1`'s `-CheckOnly` gates and `ThemeCssSyncTests` stay green.

## 1. Token additions and changes

| Path in `theme-tokens.json` | Change | Light | Dark | High contrast |
| --- | --- | --- | --- | --- |
| `typography.editor.lineHeight` | new number | 1.25 | 1.25 | 1.25 |
| `groups.Editor.Selection` | new colour | measured | measured | `Highlight` |
| `groups.Editor.ActiveLine` | new colour | measured | measured | `Canvas` with a `ButtonText` outline rule in CSS |
| `groups.Editor.MatchingBracket` | new colour | measured | measured | `Highlight` |
| `groups.Editor.SearchMatch` | new colour | measured | measured | `Highlight` |
| `groups.Syntax.String.light` | `#16A34A` → `#15803D` | 5.0:1 on `#FFFFFF` | | |
| `groups.Syntax.Comment.dark` | `#64748B` → `#8A99AF` | | 5.0:1 on `#1E293B` | |
| `groups.Syntax.Number.light`, `groups.Syntax.TableColumn.light` | re-measured; darkened if below 4.5:1 on `#FFFFFF` | | | |

"Measured" means chosen during implementation with the contrast probe so that the pair meets the
target in [web-ui.md §8](./web-ui.md); the values are then fixed in the JSON and the desktop
`ThemePalette` note is updated to say the two palettes agree on String and Comment (FR-022).

## 2. Generator

`scripts/generate-theme-css.ps1` gains one branch in the typography section: a `lineHeight`
number is emitted as `--akml-type-{name}-line-height: {value};` (unitless). Everything else is
unchanged. The `$schema` pointer in the JSON stays as it is (no schema file exists today; out of
scope).

## 3. Generated and runtime CSS variables

| Variable | Source | Used by |
| --- | --- | --- |
| `--akml-type-editor-line-height` | generated | `.akml-editor .cm-scroller { line-height }`, History preview, Styles output, chat turns, refactor preview |
| `--akml-editor-selection`, `--akml-editor-activeline`, `--akml-editor-matchingbracket`, `--akml-editor-searchmatch` | generated | `EditorComponent.razor` style block: `.cm-selectionBackground`, `.cm-activeLine`, `.cm-matchingBracket`, `.cm-searchMatch`; `.cm-cursor` uses `--akml-text-primary` |
| `--akml-editor-font-size` | runtime (exists) | editor host, completion popup, quick info body, peek body, Styles output (the fixed `--akml-type-editor-size` uses are replaced by `var(--akml-editor-font-size, var(--akml-type-editor-size))`) |
| `--akml-editor-font-family` | runtime (new; unset for Default) | every SQL surface reads `var(--akml-editor-font-family, var(--akml-type-editor-family))` |

`WebCssTokenTests` keeps its rules: no new colour literals (the per-file allowlist only shrinks),
every `--akml-*` used is defined by the generated themes or set by `setProperty` in the editor
module.

## 4. Editor settings

| Setting | Catalog id | Control | Store field | Export key |
| --- | --- | --- | --- | --- |
| Font | `editor-font-family` (new) | select: Default (Consolas, Cascadia Mono, Courier New, monospace), Cascadia Mono, Consolas, System monospace | `EditorSettings.FontFamily` (enum, default Default) | `editor.fontFamily` (string; absent = keep) |
| Font size | `editor-font-size` | number 9..32 | `FontSize` | `editor.fontSize` |
| Word wrap | `editor-word-wrap` | toggle | `WordWrap` | `editor.wordWrap` |
| Tab size | `editor-tab-size` | number 1..8 | `TabSize` | `editor.tabSize` |
| Reset session | `editor-reset-session` | button | clears every `editorTabs` record and the legacy `editorSession/current` | |

Reset description: "Default font, 13 px, off and 4" (the test that pins "13 px" still passes).
`SettingsPortabilityService`'s envelope is unchanged (format `akmlsql-web-settings`, version 1):
an older export without `fontFamily` imports as "keep current".

The font stacks the enum resolves to:

| Enum | Stack set in `--akml-editor-font-family` |
| --- | --- |
| Default | unset (the generated `--akml-type-editor-family`) |
| CascadiaMono | `'Cascadia Mono', 'Cascadia Code', Consolas, monospace` |
| Consolas | `Consolas, 'Cascadia Mono', monospace` |
| SystemMonospace | `ui-monospace, monospace` |

## 5. Contrast gate pairs added to `WorkspaceAccessibilityTests`

Per theme (light, dark; high contrast is checked for visibility, not ratio): `syntax-keyword`,
`syntax-string`, `syntax-number`, `syntax-comment`, `syntax-function`, `syntax-tablecolumn` on
`surface-editor` ≥ 4.5:1; `text-primary` on `editor-activeline` ≥ 4.5:1; `text-primary` on
`editor-selection` ≥ 4.5:1; `editor-matchingbracket` and `editor-searchmatch` distinguishable from
`surface-editor` ≥ 3:1.
