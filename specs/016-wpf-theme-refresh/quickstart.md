# Quickstart: WPF Theme & Visual Style Refresh

**Branch**: `016-wpf-theme-refresh` | **Date**: 2026-04-30
**Audience**: A contributor implementing the work, reviewing a PR against this branch, or QA-ing the result.

This is the operational guide. The **why** lives in `spec.md`; the **what** lives in `plan.md`, `data-model.md`, and `contracts/`. Use this file when you need to actually do something.

---

## 1. Read these in order before writing code

1. `spec.md` — what we're shipping.
2. `plan.md` — Constitution Check, project structure, performance budgets.
3. `research.md` — design decisions and what was rejected (so you don't re-litigate them).
4. `contracts/theme-tokens.md` — the token catalog. Keep it open while migrating any surface.
5. `contracts/theme-aware-surface.md` — the obligations every surface must satisfy.
6. `data-model.md` — what `ThemeRegistry`, `ThemePalette`, `HostThemeWatcher` etc. look like at runtime.

---

## 2. Add a new theme-aware dialog

Recipe for a new modal dialog (the most common case). Replace the bracketed parts.

```csharp
internal sealed class [MyDialog] : ThemeAwareWindow
{
    public [MyDialog]()
    {
        // ThemeAwareWindow has already merged ThemeRegistry.Resources, set the
        // DTE-derived Owner, and applied Background/Foreground references.

        Title = "[Dialog title]";
        Width  = 480;
        Height = 360;

        var root = new StackPanel { Margin = new Thickness(Spacing.Lg) };

        // Heading
        var heading = new TextBlock
        {
            Text       = "[Heading text]",
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
            Text         = "[Body text]",
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
            Content   = "Cancel",
            IsCancel  = true,
            Padding   = new Thickness(Spacing.Lg, Spacing.Sm, Spacing.Lg, Spacing.Sm),
            Margin    = new Thickness(Spacing.Sm, 0, 0, 0),
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
- **No raw colors anywhere.** Every brush is a `SetResourceReference` against a `ThemeTokens.*` constant.
- **No magic numbers.** Margins use `Spacing.*`; font sizes use `Typography.*`.
- **Cancel discipline.** `IsCancel = true` on Cancel; `IsDefault = true` on OK is fine *only when OK is non-destructive*. For destructive primary actions, see the Safety dialog example in `src/AkmlSql.Shell.Shared/Safety/SafetyWarningDialog.cs` — Cancel becomes the focused control on `Loaded` and OK/Drop is *not* default.

---

## 3. Migrate an existing surface

Each migration is one self-contained PR. Workflow:

1. **Identify the surface.** Pick one from `data-model.md` § Surface inventory.
2. **Read the surface's source.** Note every place it sets a brush, font, or margin literal.
3. **Replace brush assignments with `SetResourceReference` calls** mapping to `ThemeTokens.*`. Use the role table in `contracts/theme-tokens.md` to pick the right token.
4. **Replace font literals with `Typography.*`.** Drop any local `static readonly FontFamily` declarations that duplicate `Typography.UiFont` / `MonoFont`.
5. **Replace margin/padding magic numbers with `Spacing.*`.**
6. **Delete any surface-local `ThemeBrushSet` / private brush helpers** (e.g., the one in `SettingsWindow.cs`).
7. **Run the static audit** (Section 5). It must return zero hits in your file.
8. **Smoke test.** Section 6.
9. **Open the PR.** Reviewer checklist is in `contracts/theme-aware-surface.md` § Reviewer's quick checklist.

When migrating `SettingsWindow` (the P1 reference surface), you are also defining the visual reference everything else compares against. Do not rush this one.

---

## 4. Add a new theme token

Don't, unless absolutely necessary. The catalog is intentionally small.

If you have to:

1. Confirm no existing token covers your role. Search `contracts/theme-tokens.md`. Ask in PR review if you're unsure.
2. Open the contract first. Add a row in the appropriate group in `contracts/theme-tokens.md` with Light, Dark, and High Contrast values. Verify contrast against the WCAG AA contract section.
3. Add the constant to `Ui/Theme/ThemeTokens.cs`.
4. Add the brush mapping in every variant's palette in `Ui/Theme/ThemePalette.cs`.
5. Update `docs/wpf-theming.md` if the addition changes the role taxonomy.
6. Only then, use the new token in your surface.

---

## 5. Static audit (run before every migration PR)

The audit catches drift: any `Color.FromRgb` / `Color.FromArgb` / `Brushes.<X>` / `#XXXXXX` chrome literal outside the design system home.

```bash
# From repo root. Allow-list paths must contain the design system itself.
grep -rEn 'Color\.From(Rgb|Argb)|Brushes\.[A-Z][a-zA-Z]+' src/AkmlSql.Shell.Shared \
    --include='*.cs' \
    | grep -v '/Ui/Theme/'
```

Expected after migration completes: empty output. During migration, expect output proportional to surfaces still on the old API. Your migration PR must reduce the count by exactly the surface(s) you migrated and not increase it anywhere.

PowerShell variant:

```powershell
Select-String -Path src\AkmlSql.Shell.Shared\*.cs -Pattern 'Color\.From(Rgb|Argb)|Brushes\.[A-Z][a-zA-Z]+' -Recurse |
    Where-Object { $_.Path -notmatch '\\Ui\\Theme\\' }
```

---

## 6. Smoke test (run for every migrated surface)

Manual; takes ~3 minutes per surface.

1. **Build**: per `CLAUDE.md` build commands — build at least one shell target that hosts the surface (e.g., `AkmlSql.Ssms22` for any surface, since SSMS 22 is the default development host).
2. **Deploy**: copy the built VSIX into `C:\Program Files\Microsoft SQL Server Management Studio 22\Release\Common7\IDE\Extensions\AkmlSql\` and clear the MEF cache (see `docs/deployment.md`).
3. **Open the surface in Light theme**: Tools → AKML SQL → Options, set Theme to "Light", then open the surface. Verify chrome legibility, no leftover dark brushes, all interactive states (hover, selection, focus) read correctly.
4. **Open the surface in Dark theme**: switch theme to "Dark". Same verification.
5. **Live switch**: with the surface open, change theme between Light and Dark. The surface should re-render within 1 second; no element should retain the prior theme's colors.
6. **Live host-theme follow**: set AKML theme to "system". Change the host's VS / SSMS theme via the host's own Options dialog. The AKML surface should follow.
7. **High Contrast**: enable Windows High Contrast (Win+Alt+PrtScn or Settings → Accessibility → Contrast themes). The surface should remain readable.
8. **Focus visibility (high-stakes controls only)**: tab through the surface. Every primary action button, destructive button, navigation item, search input, and toggle switch should show a visible `BorderFocus` ring while focused. Other controls may use the WPF/OS default. Confirm `FocusVisualStyle = null` is *not* set on any high-stakes control.
9. **Reduced motion**: open Settings → Accessibility → Visual effects → "Show animations in Windows" → Off. Reopen the surface or trigger its motion. The schema-progress spinner should become a static "Loading…" label; theme switches should be instantaneous (no fade). Toggle the OS preference back on and verify motion resumes on the next animation start.

If any step fails, the migration is incomplete.

---

## 7. Where to ask questions

- **Token role unclear** ("which token do I use for X?") → re-read `contracts/theme-tokens.md` § the relevant group. If still unclear, propose a token in the PR description and the reviewer adjudicates.
- **Need a feature the system doesn't support** (a 4th theme variant, dynamic per-row brushes that aren't in the catalog) → re-read `research.md` to confirm it wasn't already considered and rejected. If it's genuinely new, open a discussion before writing code.
- **Convention conflict** (e.g., a surface that needs to ignore live theme switching for a specific frame) → document the deviation in `research.md` and proceed only after agreement.

---

## 8. Definition of done for the feature

The feature ships when:

- ✅ Every surface in `data-model.md` § Surface inventory has been migrated and passes Section 6.
- ✅ Static audit (Section 5) is clean.
- ✅ `SettingsDialog.cs` (legacy) is deleted.
- ✅ `ThemeManager` retains zero `[Obsolete]` properties (all callers migrated).
- ✅ `docs/wpf-theming.md` is published and matches the contract.
- ✅ All 8 success criteria in `spec.md` § Success Criteria are verified.
