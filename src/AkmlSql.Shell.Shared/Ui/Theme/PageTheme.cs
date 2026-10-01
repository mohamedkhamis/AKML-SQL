#nullable enable
using System.Windows.Media;

namespace AkmlSql.Shell.Shared.Ui.Theme
{
    /// <summary>
    /// Holds all frozen brushes for a single Options-dialog theme variant.
    /// Lifted from the previous nested <c>SettingsWindow.ThemeBrushSet</c> so that
    /// per-page builders (<see cref="Dialogs.Pages.IPageBuilder"/>) can take it as a constructor
    /// parameter without depending on <c>SettingsWindow</c> internals.
    ///
    /// Brushes flow from <see cref="ThemePalette"/> via the semantic tokens in
    /// <see cref="ThemeTokens"/> — chrome literals stay out of this class.
    /// </summary>
    internal sealed class PageTheme
    {
        public SolidColorBrush Main { get; }
        public SolidColorBrush Sidebar { get; }
        public SolidColorBrush Panel { get; }
        public SolidColorBrush Input { get; }
        public SolidColorBrush InputReadOnly { get; }
        public SolidColorBrush Button { get; }
        public SolidColorBrush ButtonHover { get; }
        public SolidColorBrush Selected { get; }
        public SolidColorBrush Border { get; }
        public SolidColorBrush ComboBorder { get; }
        public SolidColorBrush FgPrimary { get; }
        public SolidColorBrush FgSecondary { get; }
        public SolidColorBrush FgAccent { get; }
        public SolidColorBrush FgWhite { get; }
        public SolidColorBrush SelectedText { get; }
        public SolidColorBrush Sep { get; }
        public SolidColorBrush Transparent { get; }
        public SolidColorBrush TreeHover { get; }
        public SolidColorBrush Caret { get; }

        /// <summary>Accent TINT for selected rows on light menus (the chat agent picker's
        /// dropdown) — <see cref="ThemeTokens.SurfaceSelection"/>. Unlike <see cref="Selected"/>
        /// (the saturated accent surface built to carry <see cref="SelectedText"/>), this reads
        /// correctly under <see cref="FgPrimary"/> text in both themes.</summary>
        public SolidColorBrush SelectionTint { get; }

        /// <summary>Spec 040 (OPT-05): label colour of a child option while its parent option is off.</summary>
        public SolidColorBrush TextDisabled { get; }

        /// <summary>Spec 040 (OPT-09): face of a primary (accent) button under the mouse —
        /// <see cref="ThemeTokens.AccentPrimaryHover"/>.</summary>
        public SolidColorBrush AccentHover { get; }

        /// <summary>Spec 040 (OPT-09): face of a primary (accent) button while pressed —
        /// <see cref="ThemeTokens.AccentPrimaryPressed"/>.</summary>
        public SolidColorBrush AccentPressed { get; }

        private PageTheme(
            Color main, Color sidebar, Color panel, Color input, Color inputReadOnly,
            Color button, Color buttonHover, Color selected,
            Color border, Color comboBorder,
            Color fgPrimary, Color fgSecondary, Color fgAccent, Color fgWhite,
            Color selectedText, Color sep, Color treeHover, Color caret, Color selectionTint, Color textDisabled,
            Color accentHover, Color accentPressed)
        {
            Main = Freeze(new SolidColorBrush(main));
            Sidebar = Freeze(new SolidColorBrush(sidebar));
            Panel = Freeze(new SolidColorBrush(panel));
            Input = Freeze(new SolidColorBrush(input));
            InputReadOnly = Freeze(new SolidColorBrush(inputReadOnly));
            Button = Freeze(new SolidColorBrush(button));
            ButtonHover = Freeze(new SolidColorBrush(buttonHover));
            Selected = Freeze(new SolidColorBrush(selected));
            Border = Freeze(new SolidColorBrush(border));
            ComboBorder = Freeze(new SolidColorBrush(comboBorder));
            FgPrimary = Freeze(new SolidColorBrush(fgPrimary));
            FgSecondary = Freeze(new SolidColorBrush(fgSecondary));
            FgAccent = Freeze(new SolidColorBrush(fgAccent));
            FgWhite = Freeze(new SolidColorBrush(fgWhite));
            SelectedText = Freeze(new SolidColorBrush(selectedText));
            Sep = Freeze(new SolidColorBrush(sep));
            Transparent = Freeze(new SolidColorBrush(Colors.Transparent));
            TreeHover = Freeze(new SolidColorBrush(treeHover));
            Caret = Freeze(new SolidColorBrush(caret));
            SelectionTint = Freeze(new SolidColorBrush(selectionTint));
            TextDisabled = Freeze(new SolidColorBrush(textDisabled));
            AccentHover = Freeze(new SolidColorBrush(accentHover));
            AccentPressed = Freeze(new SolidColorBrush(accentPressed));
        }

        public static readonly PageTheme Dark = FromPalette(ThemePalette.Dark);
        public static readonly PageTheme Light = FromPalette(ThemePalette.Light);

        /// <summary>
        /// Spec 040 (OPT-09, T169) — the Options window under Windows high contrast. Built from
        /// <see cref="ThemePalette.HighContrast"/>, which maps every token to a Windows system colour
        /// (Window, WindowText, Highlight, GrayText …), so the dialog follows the active contrast
        /// theme instead of painting the fixed Light or Dark values over it. A snapshot: the system
        /// colours are read once, when the palette is built.
        /// </summary>
        public static readonly PageTheme HighContrast = FromPalette(ThemePalette.HighContrast);

        private static PageTheme FromPalette(ThemePalette p)
        {
            Color C(string token) => ((SolidColorBrush)p.Brushes[token]).Color;
            return new PageTheme(
                main:          C(ThemeTokens.SurfaceCanvas),
                sidebar:       C(ThemeTokens.SurfaceSidebar),
                panel:         C(ThemeTokens.SurfacePanel),
                input:         C(ThemeTokens.SurfaceInput),
                inputReadOnly: C(ThemeTokens.SurfaceInputReadOnly),
                button:        C(ThemeTokens.SurfaceElevated),
                buttonHover:   C(ThemeTokens.SurfaceHover),
                selected:      C(ThemeTokens.AccentPrimary),
                border:        C(ThemeTokens.BorderDefault),
                comboBorder:   C(ThemeTokens.BorderDefault),
                fgPrimary:     C(ThemeTokens.TextPrimary),
                fgSecondary:   C(ThemeTokens.TextSecondary),
                fgAccent:      C(ThemeTokens.TextLink),
                fgWhite:       C(ThemeTokens.TextPrimary),
                selectedText:  C(ThemeTokens.TextOnAccent),
                sep:           C(ThemeTokens.BorderSubtle),
                treeHover:     C(ThemeTokens.SurfaceHover),
                caret:         C(ThemeTokens.TextPrimary),
                selectionTint: C(ThemeTokens.SurfaceSelection),
                textDisabled:  C(ThemeTokens.TextDisabled),
                accentHover:   C(ThemeTokens.AccentPrimaryHover),
                accentPressed: C(ThemeTokens.AccentPrimaryPressed)
            );
        }

        internal static SolidColorBrush Freeze(SolidColorBrush b) { b.Freeze(); return b; }
    }
}
