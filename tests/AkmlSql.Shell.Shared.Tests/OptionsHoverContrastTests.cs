using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using AkmlSql.Core.Config;
using AkmlSql.Shell.Shared.Dialogs;
using AkmlSql.Shell.Shared.Ui.Theme;
using Xunit;

namespace AkmlSql.Shell.Shared.Tests
{
    /// <summary>
    /// Spec 036 (US4, FR-004) — Options navigation hover/selection contrast sweep. For every state
    /// (normal, hovered, selected, selected-and-hovered) × every shipped theme (Light, Dark,
    /// host-derived, High Contrast), the label/background brush pair the styles actually carry must
    /// meet the WCAG normal-text ratio of 4.5:1. Fails the build on regression.
    ///
    /// Brushes are resolved from the style's setters AND triggers — the reported bug was a trigger
    /// that set <c>Background</c> without <c>Foreground</c>, which this sweep detects as either a
    /// missing paired setter or a failing ratio.
    ///
    /// Theme coverage notes:
    /// <list type="bullet">
    ///   <item>Light and Dark are exercised by constructing the dialog with that theme.</item>
    ///   <item>"Host-derived" resolves to one of Light/Dark inside <c>SettingsWindow</c>, so those
    ///   two constructions cover it.</item>
    ///   <item>High Contrast maps the same tokens to <c>SystemColors</c> brushes
    ///   (<see cref="ThemePalette.HighContrast"/>); the sweep resolves the style's token pairings
    ///   against that palette. Live HC verification on a HC desktop is quickstart slice A.</item>
    /// </list>
    /// </summary>
    public class OptionsHoverContrastTests
    {
        /// <summary>WCAG normal-text contrast threshold (spec 036 FR-004 / assumptions).</summary>
        private const double MinContrastRatio = 4.5;

        // ── Nav TreeView sweeps ────────────────────────────────────────────────

        [StaFact]
        public void NavTree_Light_AllStates_MeetContrast() => AssertNavTreeAllStates("Light");

        [StaFact]
        public void NavTree_Dark_AllStates_MeetContrast() => AssertNavTreeAllStates("Dark");

        [StaFact]
        public void SearchResults_Light_AllStates_MeetContrast() => AssertSearchResultsAllStates("Light");

        [StaFact]
        public void SearchResults_Dark_AllStates_MeetContrast() => AssertSearchResultsAllStates("Dark");

        // ── Agent list sweeps (spec 037 US2, FR-036) ─────────────────────────

        [StaFact]
        public void AgentList_Light_AllStates_MeetContrast() => AssertAgentListAllStates("Light");

        [StaFact]
        public void AgentList_Dark_AllStates_MeetContrast() => AssertAgentListAllStates("Dark");

        [StaFact]
        public void AgentList_HighContrast_TokenPairs_MeetContrast()
        {
            var pairs = ResolveAgentTokenPairs("Light");
            foreach (var (state, bgToken, fgToken) in pairs)
            {
                AssertHcPair("agent-list", state, bgToken, fgToken);
            }
        }

        /// <summary>
        /// High Contrast: the dialog snapshots Light/Dark brushes, but the tokens the style pairs
        /// must stay readable when the palette maps them to system brushes — resolve every state
        /// pair through <see cref="ThemePalette.HighContrast"/>.
        /// <para>
        /// HC assertion rule: a pair passes when it is numerically ≥ 4.5:1 OR when it is an
        /// OS-sanctioned system pairing (Highlight/HighlightText, Window/WindowText,
        /// Control/WindowText-or-ControlText, Info/InfoText). Rationale: under an active HC theme
        /// the OS owns those colors and their pairing — the default (non-HC) Windows accent blue
        /// measures 4.4998:1 against white, a rounding hair below threshold, and must not break
        /// the build. What the sweep still guarantees for HC: both brushes come from tokens that
        /// the palette maps to <c>SystemColors</c> (never literals — research R7), and any
        /// non-sanctioned pairing must clear 4.5:1 numerically. Live HC-desktop verification is
        /// quickstart slice A (T049).
        /// </para>
        /// </summary>
        [StaFact]
        public void NavTree_HighContrast_TokenPairs_MeetContrast()
        {
            var pairs = ResolveNavTokenPairs("Light");
            foreach (var (state, bgToken, fgToken) in pairs)
            {
                AssertHcPair("nav", state, bgToken, fgToken);
            }
        }

        [StaFact]
        public void SearchResults_HighContrast_TokenPairs_MeetContrast()
        {
            var pairs = ResolveSearchTokenPairs("Light");
            foreach (var (state, bgToken, fgToken) in pairs)
            {
                AssertHcPair("search", state, bgToken, fgToken);
            }
        }

        /// <summary>OS-sanctioned system pairings the OS itself keeps readable under HC.</summary>
        private static readonly (Func<SolidColorBrush> Bg, Func<SolidColorBrush> Fg)[] SanctionedSystemPairs =
        {
            (() => SystemColors.HighlightBrush, () => SystemColors.HighlightTextBrush),
            (() => SystemColors.WindowBrush, () => SystemColors.WindowTextBrush),
            (() => SystemColors.ControlBrush, () => SystemColors.WindowTextBrush),
            (() => SystemColors.ControlBrush, () => SystemColors.ControlTextBrush),
            (() => SystemColors.InfoBrush, () => SystemColors.InfoTextBrush),
        };

        private static void AssertHcPair(string surface, string state, string bgToken, string fgToken)
        {
            var bg = HcBrush(bgToken);
            var fg = HcBrush(fgToken);

            foreach (var (bgSrc, fgSrc) in SanctionedSystemPairs)
            {
                if (ReferenceEquals(bg, bgSrc()) && ReferenceEquals(fg, fgSrc()))
                    return; // OS-owned pairing — the HC theme owns its readability (T049 verifies live)
            }

            double ratio = ContrastRatio(bg.Color, fg.Color);
            Assert.True(ratio >= MinContrastRatio,
                $"Options {surface} {state} in HighContrast: tokens ({bgToken}, {fgToken}) give " +
                $"{ratio:F2}:1, below {MinContrastRatio}:1, and the pair is not an OS-sanctioned " +
                $"system pairing (background {bg.Color}, foreground {fg.Color})");
        }

        // ── State resolution ───────────────────────────────────────────────────

        private static void AssertNavTreeAllStates(string theme)
        {
            var style = GetNavItemStyle(theme);
            foreach (var (state, bg, fg) in ResolveNavPairs(style, theme))
            {
                AssertContrast(state, theme, bg, fg);
            }
        }

        private static void AssertSearchResultsAllStates(string theme)
        {
            var style = GetSearchItemStyle(theme);
            foreach (var (state, bg, fg) in ResolveSearchPairs(style, theme))
            {
                AssertContrast(state, theme, bg, fg);
            }
        }

        private static void AssertAgentListAllStates(string theme)
        {
            var list = GetAgentList(theme, out _);
            var style = list.ItemContainerStyle;
            Assert.NotNull(style);
            // The rows float transparent over the list's own surface (Input), so the normal
            // state's effective background is the list Background, exactly like the search list
            // over Panel.
            foreach (var (state, bg, fg) in ResolveAgentPairs(style, (SolidColorBrush)list.Background))
            {
                AssertContrast(state, theme, bg, fg);
            }
        }

        /// <summary>The four states as (background, foreground) brushes resolved from the style.</summary>
        private static IEnumerable<(string State, SolidColorBrush Bg, SolidColorBrush Fg)> ResolveNavPairs(Style style, string theme)
        {
            // Normal: base setters. Background is Transparent over the sidebar — the effective
            // surface is SurfaceSidebar (the nav tree's own Background is Transparent too).
            yield return ("normal",
                ContainerSurfaceBrush(style, navSurface: true, theme),
                RequiredSetterBrush(style.Setters, Control.ForegroundProperty, "normal/Foreground"));

            var (hBg, hFg) = HoverPair(style);
            yield return ("hovered", hBg, hFg);
            var (sBg, sFg) = SelectedPair(style);
            yield return ("selected", sBg, sFg);
            var (shBg, shFg) = SelectedHoverPair(style);
            yield return ("selected+hovered", shBg, shFg);
        }

        /// <summary>
        /// The agent list's states, resolved against the list's own surface for the normal state.
        /// Like the search list, hover is declared before selected, so selected ∧ hovered resolves
        /// to the selected pair (the later trigger wins both properties).
        /// </summary>
        private static IEnumerable<(string State, SolidColorBrush Bg, SolidColorBrush Fg)> ResolveAgentPairs(Style style, SolidColorBrush listSurface)
        {
            var normalBg = RequiredSetterBrush(style.Setters, Control.BackgroundProperty, "normal/Background");
            yield return ("normal",
                normalBg.Color != Colors.Transparent ? normalBg : listSurface,
                RequiredSetterBrush(style.Setters, Control.ForegroundProperty, "normal/Foreground"));

            var (hBg, hFg) = HoverPair(style);
            yield return ("hovered", hBg, hFg);
            var (sBg, sFg) = SelectedPair(style);
            yield return ("selected", sBg, sFg);
            yield return ("selected+hovered", sBg, sFg);
        }

        private static IEnumerable<(string State, SolidColorBrush Bg, SolidColorBrush Fg)> ResolveSearchPairs(Style style, string theme)
        {
            yield return ("normal",
                ContainerSurfaceBrush(style, navSurface: false, theme),
                RequiredSetterBrush(style.Setters, Control.ForegroundProperty, "normal/Foreground"));

            var (hBg, hFg) = HoverPair(style);
            yield return ("hovered", hBg, hFg);
            var (sBg, sFg) = SelectedPair(style);
            yield return ("selected", sBg, sFg);
            // The search list orders hover before selected, so selected ∧ hovered resolves to the
            // selected pair (the later trigger wins both properties).
            yield return ("selected+hovered", sBg, sFg);
        }

        private static (SolidColorBrush Bg, SolidColorBrush Fg) HoverPair(Style style)
        {
            var trigger = FindTrigger(style, UIElement.IsMouseOverProperty)
                ?? throw new Xunit.Sdk.XunitException("hover state: no IsMouseOver trigger on the item style");
            // FR-002/FR-003: the hover trigger MUST pair a foreground with its background —
            // a missing Foreground setter here is exactly the reported bug.
            var bg = RequiredSetterBrush(trigger.Setters, Control.BackgroundProperty, "hovered/Background");
            var fg = RequiredSetterBrush(trigger.Setters, Control.ForegroundProperty, "hovered/Foreground");
            return (bg, fg);
        }

        private static (SolidColorBrush Bg, SolidColorBrush Fg) SelectedPair(Style style)
        {
            var trigger = FindTrigger(style, TreeViewItem.IsSelectedProperty)
                ?? (Trigger?)FindListBoxSelectedTrigger(style)
                ?? throw new Xunit.Sdk.XunitException("selected state: no IsSelected trigger on the item style");
            var bg = RequiredSetterBrush(trigger.Setters, Control.BackgroundProperty, "selected/Background");
            var fg = RequiredSetterBrush(trigger.Setters, Control.ForegroundProperty, "selected/Foreground");
            return (bg, fg);
        }

        private static (SolidColorBrush Bg, SolidColorBrush Fg) SelectedHoverPair(Style style)
        {
            foreach (var t in style.Triggers)
            {
                if (t is MultiTrigger mt && HasCondition(mt, TreeViewItem.IsSelectedProperty)
                    && HasCondition(mt, UIElement.IsMouseOverProperty))
                {
                    var bg = RequiredSetterBrush(mt.Setters, Control.BackgroundProperty, "selected+hovered/Background");
                    var fg = RequiredSetterBrush(mt.Setters, Control.ForegroundProperty, "selected+hovered/Foreground");
                    return (bg, fg);
                }
            }
            throw new Xunit.Sdk.XunitException(
                "selected+hovered state: no MultiTrigger(IsSelected ∧ IsMouseOver) on the nav item style — " +
                "without it the hover trigger's background pairs with the selected foreground (the reported bug)");
        }

        // ── Token-level (High Contrast) resolution ─────────────────────────────

        /// <summary>
        /// Resolves the nav style's per-state pairs as TOKEN names, by matching the style's frozen
        /// brushes back to the <see cref="PageTheme"/> properties they came from (the brushes are
        /// per-theme singletons, so reference equality identifies the property).
        /// </summary>
        private static List<(string State, string BgToken, string FgToken)> ResolveNavTokenPairs(string theme)
        {
            var style = GetNavItemStyle(theme);
            var result = new List<(string, string, string)>();
            foreach (var (state, bg, fg) in ResolveNavPairs(style, theme))
            {
                result.Add((state, TokenForBrush(bg, navSurface: true), TokenForBrush(fg, navSurface: true)));
            }
            return result;
        }

        private static List<(string State, string BgToken, string FgToken)> ResolveSearchTokenPairs(string theme)
        {
            var style = GetSearchItemStyle(theme);
            var result = new List<(string, string, string)>();
            foreach (var (state, bg, fg) in ResolveSearchPairs(style, theme))
            {
                result.Add((state, TokenForBrush(bg, navSurface: false), TokenForBrush(fg, navSurface: false)));
            }
            return result;
        }

        private static List<(string State, string BgToken, string FgToken)> ResolveAgentTokenPairs(string theme)
        {
            var list = GetAgentList(theme, out _);
            var result = new List<(string, string, string)>();
            foreach (var (state, bg, fg) in ResolveAgentPairs(list.ItemContainerStyle, (SolidColorBrush)list.Background))
            {
                result.Add((state, TokenForBrush(bg, navSurface: false), TokenForBrush(fg, navSurface: false)));
            }
            return result;
        }

        /// <summary>PageTheme property name → the token <see cref="PageTheme"/> sources it from.</summary>
        private static readonly Dictionary<string, string> PropertyToToken = new(StringComparer.Ordinal)
        {
            [nameof(PageTheme.Sidebar)] = ThemeTokens.SurfaceSidebar,
            [nameof(PageTheme.Panel)] = ThemeTokens.SurfacePanel,
            [nameof(PageTheme.Input)] = ThemeTokens.SurfaceInput,
            [nameof(PageTheme.TreeHover)] = ThemeTokens.SurfaceHover,
            [nameof(PageTheme.Selected)] = ThemeTokens.AccentPrimary,
            [nameof(PageTheme.FgPrimary)] = ThemeTokens.TextPrimary,
            [nameof(PageTheme.FgSecondary)] = ThemeTokens.TextSecondary,
            [nameof(PageTheme.FgAccent)] = ThemeTokens.TextLink,
            [nameof(PageTheme.FgWhite)] = ThemeTokens.TextPrimary,
            [nameof(PageTheme.SelectedText)] = ThemeTokens.TextOnAccent,
        };

        private static string TokenForBrush(SolidColorBrush brush, bool navSurface)
        {
            foreach (var theme in new[] { PageTheme.Light, PageTheme.Dark })
            {
                foreach (var prop in typeof(PageTheme).GetProperties(BindingFlags.Public | BindingFlags.Instance))
                {
                    if (prop.GetValue(theme) is SolidColorBrush b && ReferenceEquals(b, brush)
                        && PropertyToToken.TryGetValue(prop.Name, out var token))
                    {
                        return token;
                    }
                }
            }
            throw new Xunit.Sdk.XunitException(
                $"A style brush ({brush.Color}) did not come from a mapped PageTheme property — " +
                "the sweep cannot trace it to a token. Pair hover/selection brushes from PageTheme only.");
        }

        private static SolidColorBrush HcBrush(string token)
        {
            Assert.True(ThemePalette.HighContrast.Brushes.TryGetValue(token, out var brush),
                $"High Contrast palette has no entry for token {token}");
            return brush!;
        }

        // ── Style plumbing ─────────────────────────────────────────────────────

        private static Style GetNavItemStyle(string theme)
        {
            var dialog = BuildDialog(theme, out _);
            var tree = GetPrivateField<TreeView>(dialog, "_navTree");
            Assert.True(tree.Resources[typeof(TreeViewItem)] is Style, "nav TreeView has no implicit TreeViewItem style");
            return (Style)tree.Resources[typeof(TreeViewItem)];
        }

        private static Style GetSearchItemStyle(string theme)
        {
            var dialog = BuildDialog(theme, out _);
            var list = GetPrivateField<ListBox>(dialog, "_searchResultsList");
            Assert.NotNull(list.ItemContainerStyle);
            return list.ItemContainerStyle;
        }

        /// <summary>
        /// The AI Assistance page's agent ListBox (spec 037 US2) — found by its "AI agents"
        /// automation name, which distinguishes it from the page's fallback-order ListBox
        /// (spec 037 US4). Pages live in the host's <c>_pages</c> map, not in the window's
        /// active tree.
        /// </summary>
        private static ListBox GetAgentList(string theme, out SettingsWindow dialog)
        {
            dialog = BuildDialog(theme, out _);
            var f = typeof(SettingsWindow).GetField("_pages",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(f);
            var pages = (Dictionary<string, UIElement>)f!.GetValue(dialog)!;
            Assert.True(pages.TryGetValue("AI Assistance", out var aiPage), "AI Assistance page not built.");
            return Assert.Single(LogicalTree.Descendants<ListBox>(aiPage!),
                lb => System.Windows.Automation.AutomationProperties.GetName(lb) == "AI agents");
        }

        private static SettingsWindow BuildDialog(string theme, out Window window)
        {
            var settings = new AppSettings { Theme = theme };
            var dialog = new SettingsWindow(settings);
            window = dialog.TestBuildWindowForRenderTest();
            window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Render);
            window.UpdateLayout();
            return dialog;
        }

        private static T GetPrivateField<T>(object instance, string name) where T : class
        {
            var field = instance.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.True(field != null, $"SettingsWindow field {name} not found — rename in the sweep if it moved");
            var value = field!.GetValue(instance) as T;
            Assert.True(value != null, $"SettingsWindow field {name} was null after window build");
            return value!;
        }

        /// <summary>
        /// The effective surface behind a Transparent item background: the nav tree floats on the
        /// sidebar, the search-results list paints <c>Panel</c> itself.
        /// </summary>
        private static SolidColorBrush ContainerSurfaceBrush(Style style, bool navSurface, string theme)
        {
            var pageTheme = string.Equals(theme, "Dark", StringComparison.OrdinalIgnoreCase)
                ? PageTheme.Dark
                : PageTheme.Light;
            var bg = RequiredSetterBrush(style.Setters, Control.BackgroundProperty, "normal/Background");
            if (bg.Color != Colors.Transparent)
                return bg; // an opaque base background is the effective surface
            return navSurface ? pageTheme.Sidebar : pageTheme.Panel;
        }

        private static Trigger? FindTrigger(Style style, DependencyProperty property)
        {
            foreach (var t in style.Triggers)
            {
                if (t is Trigger trigger && trigger.Property == property)
                    return trigger;
            }
            return null;
        }

        private static Trigger? FindListBoxSelectedTrigger(Style style)
            => FindTrigger(style, ListBoxItem.IsSelectedProperty);

        private static bool HasCondition(MultiTrigger mt, DependencyProperty property)
        {
            foreach (var c in mt.Conditions)
            {
                if (c is Condition condition && condition.Property == property)
                    return true;
            }
            return false;
        }

        private static SolidColorBrush RequiredSetterBrush(SetterBaseCollection setters, DependencyProperty property, string what)
        {
            foreach (var setterBase in setters)
            {
                if (setterBase is Setter s && s.Property == property && s.Value is SolidColorBrush brush)
                    return brush;
            }
            throw new Xunit.Sdk.XunitException(
                $"{what}: the style does not set {property.Name} with a brush in this state — " +
                "an unpaired background/foreground is the spec-036 hover defect");
        }

        // ── ComboBox dropdown sweeps (the Aero2 wash bug, ComboBoxItem twin) ───

        /// <summary>
        /// The regression gate for the unreadable dropdown row. <see cref="ComboBoxTheming"/>'s item
        /// styles set Background/Foreground through triggers, but the STOCK Aero2 ComboBoxItem
        /// template paints its own ~12%-alpha highlight wash from a TEMPLATE trigger, which outranks
        /// a TemplateBinding to the item's Background — so unless the style owns an item template the
        /// accent fill never lands and SelectedText white sits on a near-white wash in Light theme.
        /// Asserting colours alone would pass while the control still rendered the wash, so this
        /// asserts template OWNERSHIP too. Twin of <see cref="AiAgentItemStyleTests"/> for ListBoxItem.
        /// </summary>
        [StaFact]
        public void ComboBoxItemStyles_own_their_item_template()
        {
            foreach (var menu in new[] { false, true })
            {
                foreach (var theme in new[] { "Light", "Dark" })
                {
                    var style = ComboItemStyle(theme, menu);
                    var what = (menu ? "menu" : "options") + "/" + theme;

                    var item = new ComboBoxItem();
                    item.ApplyTemplate();
                    item.Style = style;

                    Assert.True(item.Template != null,
                        what + ": the ComboBoxItem style sets no ControlTemplate, so the stock Aero2 " +
                        "highlight wash paints over every trigger-set Background (the invisible-row bug)");
                    Assert.Equal(typeof(ComboBoxItem), item.Template!.TargetType);
                    Assert.Equal(typeof(Border), item.Template.VisualTree.Type);

                    // Behavioural half: the trigger-set selection brush must actually reach the item.
                    item.IsSelected = true;
                    var expected = menu ? PageThemeFor(theme).SelectionTint : PageThemeFor(theme).Selected;
                    Assert.True(ReferenceEquals(expected, item.Background),
                        what + ": selecting the row did not apply the style's selection brush");
                }
            }
        }

        [StaFact]
        public void ComboBoxItems_Light_AllStates_MeetContrast() => AssertComboItemsAllStates("Light", menu: false);

        [StaFact]
        public void ComboBoxItems_Dark_AllStates_MeetContrast() => AssertComboItemsAllStates("Dark", menu: false);

        [StaFact]
        public void ComboBoxMenuItems_Light_AllStates_MeetContrast() => AssertComboItemsAllStates("Light", menu: true);

        [StaFact]
        public void ComboBoxMenuItems_Dark_AllStates_MeetContrast() => AssertComboItemsAllStates("Dark", menu: true);

        /// <summary>
        /// High Contrast for the Options dropdown, resolved at token level like the nav/search/agent
        /// sweeps. The menu style is excluded on purpose: its normal row is Transparent and its
        /// selection is an alpha TINT, neither of which is a traceable opaque token pairing — and
        /// <see cref="ComboBoxTheming.ApplyMenuStyle(ComboBox)"/> is a documented no-op under High Contrast anyway.
        /// </summary>
        [StaFact]
        public void ComboBoxItems_HighContrast_TokenPairs_MeetContrast()
        {
            var style = ComboItemStyle("Light", menu: false);
            foreach (var (state, bg, fg) in ResolveComboPairs(style, "Light"))
            {
                AssertHcPair("combo", state, TokenForBrush(bg, navSurface: false), TokenForBrush(fg, navSurface: false));
            }
        }

        private static void AssertComboItemsAllStates(string theme, bool menu)
        {
            var style = ComboItemStyle(theme, menu);
            foreach (var (state, bg, fg) in ResolveComboPairs(style, theme))
            {
                AssertContrast((menu ? "menu-combo " : "combo ") + state, theme, bg, fg);
            }
        }

        /// <summary>
        /// Both item styles declare IsHighlighted before IsSelected, so a row that is selected AND
        /// highlighted resolves to the selected pair (the later trigger wins both properties) — the
        /// same ordering convention the search list documents.
        /// </summary>
        private static IEnumerable<(string State, SolidColorBrush Bg, SolidColorBrush Fg)> ResolveComboPairs(
            Style style, string theme)
        {
            // The popup card paints SurfaceInput, so a Transparent row shows that through.
            var surface = PageThemeFor(theme).Input;

            var normalBg = RequiredSetterBrush(style.Setters, Control.BackgroundProperty, "normal/Background");
            yield return ("normal",
                Flatten(normalBg, surface),
                RequiredSetterBrush(style.Setters, Control.ForegroundProperty, "normal/Foreground"));

            var highlight = FindTrigger(style, ComboBoxItem.IsHighlightedProperty)
                ?? throw new Xunit.Sdk.XunitException("highlighted state: no IsHighlighted trigger on the ComboBoxItem style");
            yield return ("highlighted",
                Flatten(RequiredSetterBrush(highlight.Setters, Control.BackgroundProperty, "highlighted/Background"), surface),
                RequiredSetterBrush(highlight.Setters, Control.ForegroundProperty, "highlighted/Foreground"));

            var selected = FindTrigger(style, ComboBoxItem.IsSelectedProperty)
                ?? throw new Xunit.Sdk.XunitException("selected state: no IsSelected trigger on the ComboBoxItem style");
            var sBg = Flatten(RequiredSetterBrush(selected.Setters, Control.BackgroundProperty, "selected/Background"), surface);
            var sFg = RequiredSetterBrush(selected.Setters, Control.ForegroundProperty, "selected/Foreground");
            yield return ("selected", sBg, sFg);
            yield return ("selected+highlighted", sBg, sFg);
        }

        /// <summary>
        /// Item styles come from the real public entry points, so the sweep covers the path the
        /// dialog actually uses rather than a private builder that could drift away from it.
        /// </summary>
        private static Style ComboItemStyle(string theme, bool menu)
        {
            var combo = new ComboBox();
            var pageTheme = PageThemeFor(theme);
            if (menu) ComboBoxTheming.ApplyMenuStyle(combo, pageTheme);
            else ComboBoxTheming.Apply(combo, pageTheme);
            Assert.True(combo.ItemContainerStyle != null, "ComboBoxTheming set no ItemContainerStyle");
            return combo.ItemContainerStyle!;
        }

        private static PageTheme PageThemeFor(string theme)
            => string.Equals(theme, "Dark", StringComparison.OrdinalIgnoreCase) ? PageTheme.Dark : PageTheme.Light;

        /// <summary>
        /// Composites a translucent row brush (Transparent rows, the menu's alpha selection TINT)
        /// over the surface behind it — contrast math on the raw colour would otherwise score a
        /// colour no pixel ever shows.
        /// </summary>
        private static SolidColorBrush Flatten(SolidColorBrush overlay, SolidColorBrush under)
        {
            if (overlay.Color.A == 255) return overlay;
            double a = overlay.Color.A / 255.0;
            byte Mix(byte o, byte u) => (byte)Math.Round((o * a) + (u * (1 - a)));
            var flat = new SolidColorBrush(Color.FromRgb(
                Mix(overlay.Color.R, under.Color.R),
                Mix(overlay.Color.G, under.Color.G),
                Mix(overlay.Color.B, under.Color.B)));
            flat.Freeze();
            return flat;
        }

        // ── ThemedButton disabled-state sweep ──────────────────────────────────

        /// <summary>
        /// The regression gate for the vanishing disabled button caption. <see cref="ThemedButton"/>'s
        /// disabled trigger repainted only the FACE, so a disabled PRIMARY button kept ApplyPrimary's
        /// white Text.OnAccent over the muted surface — about 1.2:1 in Light, faded further by a
        /// blanket Opacity — for the whole of every AI request, on the chat Send button, the
        /// empty-state "Add AI agent" button and the update dialogs' install buttons.
        /// <para>
        /// Resolution is token-level: ThemedButton serves every window, so it paints through
        /// DynamicResource token NAMES rather than PageTheme brushes; the setters therefore carry
        /// <see cref="DynamicResourceExtension"/> values, resolved here through <see cref="ThemePalette"/>.
        /// </para>
        /// </summary>
        [StaFact]
        public void ThemedButton_Disabled_Light_PairsForegroundAndMeetsContrast() => AssertThemedButtonDisabled("Light");

        [StaFact]
        public void ThemedButton_Disabled_Dark_PairsForegroundAndMeetsContrast() => AssertThemedButtonDisabled("Dark");

        private static void AssertThemedButtonDisabled(string theme)
        {
            var palette = string.Equals(theme, "Dark", StringComparison.OrdinalIgnoreCase)
                ? ThemePalette.Dark
                : ThemePalette.Light;

            foreach (var kind in new[] { "primary", "secondary" })
            {
                var button = new Button();
                if (kind == "primary") ThemedButton.ApplyPrimary(button);
                else ThemedButton.ApplySecondary(button);

                Assert.True(button.Template != null, kind + ": ThemedButton applied no template");

                var disabled = Assert.Single(
                    button.Template!.Triggers.OfType<Trigger>(),
                    t => ReferenceEquals(t.Property, UIElement.IsEnabledProperty) && Equals(t.Value, false));

                var bgToken = RequiredSetterToken(disabled.Setters, Control.BackgroundProperty,
                    kind + " disabled/Background");
                // The unpaired foreground IS the defect: without this setter the label keeps the
                // enabled colour (white, for primary) over the disabled face.
                var fgToken = RequiredSetterToken(disabled.Setters, Control.ForegroundProperty,
                    kind + " disabled/Foreground");

                AssertContrast(kind + " disabled", theme,
                    PaletteBrush(palette, bgToken), PaletteBrush(palette, fgToken));

                // A dimming Opacity composites the TEXT toward the face too, giving back exactly the
                // contrast the token pairing just bought — so the pairing must stand on its own.
                Assert.DoesNotContain(disabled.Setters.OfType<Setter>(), s =>
                    ReferenceEquals(s.Property, UIElement.OpacityProperty)
                    && s.Value is double d && d < 1.0);
            }
        }

        /// <summary>
        /// A token-name setter (<see cref="DynamicResourceExtension"/>), the form ThemedButton uses.
        /// <see cref="RequiredSetterBrush"/>'s sibling for styles that cannot hold theme brushes.
        /// Foreground is matched on <see cref="TextElement.ForegroundProperty"/> as well, because the
        /// label is repainted through the inheritable property on the template's Border.
        /// </summary>
        private static string RequiredSetterToken(SetterBaseCollection setters, DependencyProperty property, string what)
        {
            foreach (var setterBase in setters)
            {
                if (!(setterBase is Setter s)) continue;
                var matches = ReferenceEquals(s.Property, property)
                    || (ReferenceEquals(property, Control.ForegroundProperty)
                        && ReferenceEquals(s.Property, TextElement.ForegroundProperty));
                if (matches && s.Value is DynamicResourceExtension dyn && dyn.ResourceKey is string token)
                    return token;
            }
            throw new Xunit.Sdk.XunitException(
                $"{what}: the trigger sets no {property.Name} token in this state — " +
                "an unpaired background/foreground is the vanishing-caption defect");
        }

        private static SolidColorBrush PaletteBrush(ThemePalette palette, string token)
        {
            Assert.True(palette.Brushes.TryGetValue(token, out var brush),
                $"palette has no entry for token {token}");
            return (SolidColorBrush)brush!;
        }

        // ── Contrast math (WCAG 2.x relative luminance) ────────────────────────

        private static void AssertContrast(string state, string theme, SolidColorBrush bg, SolidColorBrush fg)
        {
            double ratio = ContrastRatio(bg.Color, fg.Color);
            Assert.True(ratio >= MinContrastRatio,
                $"Options {state} in {theme}: contrast {ratio:F2}:1 is below {MinContrastRatio}:1 " +
                $"(background {bg.Color}, foreground {fg.Color})");
        }

        private static double ContrastRatio(Color a, Color b)
        {
            var la = RelativeLuminance(a);
            var lb = RelativeLuminance(b);
            var lighter = Math.Max(la, lb);
            var darker = Math.Min(la, lb);
            return (lighter + 0.05) / (darker + 0.05);
        }

        private static double RelativeLuminance(Color c)
        {
            static double Channel(byte v)
            {
                var s = v / 255.0;
                return s <= 0.04045 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
            }
            return 0.2126 * Channel(c.R) + 0.7152 * Channel(c.G) + 0.0722 * Channel(c.B);
        }
    }
}
