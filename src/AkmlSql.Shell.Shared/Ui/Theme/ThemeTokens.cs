using System;
using System.Windows;
using System.Windows.Media;

namespace AkmlSql.Shell.Shared.Ui.Theme
{
    /// <summary>
    /// String constants for every semantic theme token.
    /// Surfaces consume these via <c>FrameworkElement.SetResourceReference(&lt;property&gt;, ThemeTokens.&lt;Token&gt;)</c>.
    /// Authoritative catalog with Light/Dark/HighContrast values lives in
    /// <c>specs/016-wpf-theme-refresh/contracts/theme-tokens.md</c>.
    /// <para>
    /// Spec 020 (SQL Prompt visual parity) introduces non-brush token families: <c>Spacing.*</c>
    /// (scalar DIU values) and <c>Typography.*</c> (FontFamily + Size + Weight composites via
    /// <see cref="TypographySpec"/>). These are invariant across themes (same value in Light /
    /// Dark / HighContrast) and are written directly to <c>ThemeRegistry.Resources</c> rather than
    /// participating in <c>ThemePalette</c>'s per-variant brush dictionary. They are intentionally
    /// NOT listed in <see cref="All"/> — that array is the brush-completeness validation set.
    /// </para>
    /// </summary>
    public static class ThemeTokens
    {
        // --- Surface group ---
        public const string SurfaceCanvas         = "Akml.Brush.Surface.Canvas";
        public const string SurfacePanel          = "Akml.Brush.Surface.Panel";
        public const string SurfaceElevated       = "Akml.Brush.Surface.Elevated";
        public const string SurfaceSidebar        = "Akml.Brush.Surface.Sidebar";
        public const string SurfaceInput          = "Akml.Brush.Surface.Input";
        public const string SurfaceInputReadOnly  = "Akml.Brush.Surface.InputReadOnly";
        public const string SurfaceHover          = "Akml.Brush.Surface.Hover";
        public const string SurfaceSelection      = "Akml.Brush.Surface.Selection";
        public const string SurfaceSelectionStrong = "Akml.Brush.Surface.SelectionStrong";

        // --- Text group ---
        public const string TextPrimary     = "Akml.Brush.Text.Primary";
        public const string TextSecondary   = "Akml.Brush.Text.Secondary";
        public const string TextDisabled    = "Akml.Brush.Text.Disabled";
        public const string TextPlaceholder = "Akml.Brush.Text.Placeholder";
        public const string TextLink        = "Akml.Brush.Text.Link";
        public const string TextOnAccent    = "Akml.Brush.Text.OnAccent";
        public const string TextOnDanger    = "Akml.Brush.Text.OnDanger";

        // --- Border group ---
        public const string BorderDefault  = "Akml.Brush.Border.Default";
        public const string BorderStrong   = "Akml.Brush.Border.Strong";
        public const string BorderSubtle   = "Akml.Brush.Border.Subtle";
        public const string BorderFocus    = "Akml.Brush.Border.Focus";
        public const string BorderSplitter = "Akml.Brush.Border.Splitter";

        // --- Accent group ---
        public const string AccentPrimary        = "Akml.Brush.Accent.Primary";
        public const string AccentPrimaryHover   = "Akml.Brush.Accent.PrimaryHover";
        public const string AccentPrimaryPressed = "Akml.Brush.Accent.PrimaryPressed";

        // --- Status group ---
        public const string StatusSuccess = "Akml.Brush.Status.Success";
        public const string StatusWarning = "Akml.Brush.Status.Warning";
        public const string StatusDanger  = "Akml.Brush.Status.Danger";
        public const string StatusInfo    = "Akml.Brush.Status.Info";

        // --- Editor group (in-editor adornments / popups / margins) ---
        public const string EditorMarginBackground = "Akml.Brush.Editor.MarginBackground";
        public const string EditorSpinnerStroke    = "Akml.Brush.Editor.SpinnerStroke";
        public const string EditorPopupBackground  = "Akml.Brush.Editor.PopupBackground";
        public const string EditorPopupBorder      = "Akml.Brush.Editor.PopupBorder";

        // --- Chat group (AI Chat tool window message bubbles) ---
        public const string ChatUserBubble      = "Akml.Brush.Chat.UserBubble";
        public const string ChatAssistantBubble = "Akml.Brush.Chat.AssistantBubble";
        public const string ChatSystemBubble    = "Akml.Brush.Chat.SystemBubble";

        // ------------------------------------------------------------------------
        // Spec 020 (US1, P1): SQL Prompt visual parity — brush families that DO
        // participate in ThemePalette's per-variant validation. Source hex values:
        // doc/SQL-PROMPT/SQL-Prompt-Features/SQL_Prompt_Features_Core.md §1.2 (IconBadge),
        // doc/SQL-PROMPT/SQL-Prompt-History/SQL_Prompt_SQL_History.md §16.2 (History),
        // and SQL Prompt's environment-colour palette for TabColor swatches.
        // ------------------------------------------------------------------------

        // --- IconBadge group — solid foreground colour per object type. ---
        //   Each token resolves to the saturated badge text colour; consumers compute the
        //   20%-alpha background overlay at render time (the SQL Prompt rendering model).
        public const string IconBadgeTable       = "Akml.Brush.IconBadge.Table";       // Yellow  — T
        public const string IconBadgeView        = "Akml.Brush.IconBadge.View";        // Teal    — V
        public const string IconBadgeColumn     = "Akml.Brush.IconBadge.Column";       // Blue    — C
        public const string IconBadgeStoredProc = "Akml.Brush.IconBadge.StoredProc";   // Purple  — P
        public const string IconBadgeFunction   = "Akml.Brush.IconBadge.Function";     // Orange  — F
        public const string IconBadgeSnippet    = "Akml.Brush.IconBadge.Snippet";      // Green   — S
        public const string IconBadgeKeyword    = "Akml.Brush.IconBadge.Keyword";      // Gray    — K
        public const string IconBadgeDatabase   = "Akml.Brush.IconBadge.Database";     // Red     — D
        public const string IconBadgeSchema     = "Akml.Brush.IconBadge.Schema";       // Green2  — Sc
        public const string IconBadgeTrigger    = "Akml.Brush.IconBadge.Trigger";      // DarkRed — Tr
        public const string IconBadgeIndex      = "Akml.Brush.IconBadge.Index";        // DimGray — Ix
        public const string IconBadgeSynonym    = "Akml.Brush.IconBadge.Synonym";      // Teal2   — Sy

        // --- TabColor group — environment-colour swatch palette (8 swatches). ---
        //   Used by Tab Coloring for per-server / per-database / per-connection visual cues.
        //   Aligned with the existing EnvironmentMatcher defaults (Red / Amber / Green / Blue)
        //   plus four extension swatches (Teal / Purple / Pink / Gray) for user customisation.
        public const string TabColorRed    = "Akml.Brush.TabColor.Red";
        public const string TabColorAmber  = "Akml.Brush.TabColor.Amber";
        public const string TabColorGreen  = "Akml.Brush.TabColor.Green";
        public const string TabColorBlue   = "Akml.Brush.TabColor.Blue";
        public const string TabColorTeal   = "Akml.Brush.TabColor.Teal";
        public const string TabColorPurple = "Akml.Brush.TabColor.Purple";
        public const string TabColorPink   = "Akml.Brush.TabColor.Pink";
        public const string TabColorGray   = "Akml.Brush.TabColor.Gray";

        // --- History group — SQL History window status icons and match highlight. ---
        //   Semantic markers; track separately from Status.* so a future History redesign
        //   doesn't ripple through every Status consumer (and vice versa).
        public const string HistoryOpenIcon       = "Akml.Brush.History.OpenIcon";
        public const string HistoryClosedIcon     = "Akml.Brush.History.ClosedIcon";
        public const string HistoryStarActive     = "Akml.Brush.History.Star.Active";
        public const string HistoryStarInactive   = "Akml.Brush.History.Star.Inactive";
        public const string HistoryMatchHighlight = "Akml.Brush.History.MatchHighlight";

        // ------------------------------------------------------------------------
        // Spec 020: SQL Prompt visual parity — non-brush token families.
        // These are theme-invariant scalars / fonts. They live in ThemeRegistry.Resources
        // alongside brushes, but are NOT in `All` because they don't participate in
        // ThemePalette's per-variant brush completeness check.
        // ------------------------------------------------------------------------

        // --- Spacing scalars (DIU; invariant across themes) ---
        //   Surfaces resolve via (double)Resources[ThemeTokens.SpacingS], or via WPF
        //   `{DynamicResource Akml.Scalar.Spacing.S}` in XAML for thickness / margin properties.
        public const string SpacingXs = "Akml.Scalar.Spacing.XS";  // 4 DIU
        public const string SpacingS  = "Akml.Scalar.Spacing.S";   // 8 DIU
        public const string SpacingM  = "Akml.Scalar.Spacing.M";   // 12 DIU
        public const string SpacingL  = "Akml.Scalar.Spacing.L";   // 16 DIU

        // --- Typography composites (invariant across themes) ---
        //   Each key resolves to a frozen TypographySpec — Family / Size / Weight.
        //   Consumers: var t = (TypographySpec)Resources[ThemeTokens.TypographyChrome];
        //              element.FontFamily = t.Family; element.FontSize = t.Size; element.FontWeight = t.Weight;
        public const string TypographyChrome      = "Akml.Type.Chrome";       // Segoe UI 12 Normal
        public const string TypographyChromeTitle = "Akml.Type.ChromeTitle";  // Segoe UI 14 SemiBold
        public const string TypographyEditor      = "Akml.Type.Editor";       // Consolas 12 Normal (overridden by host editor font when surface is an editor adornment)
        public const string TypographyIconBadge   = "Akml.Type.IconBadge";    // Segoe UI 9 SemiBold

        /// <summary>
        /// All token keys. Used by <see cref="ThemePalette"/> to validate every variant
        /// has a brush for every key, and by tests/audits.
        /// </summary>
        public static readonly string[] All =
        {
            SurfaceCanvas, SurfacePanel, SurfaceElevated, SurfaceSidebar,
            SurfaceInput, SurfaceInputReadOnly, SurfaceHover,
            SurfaceSelection, SurfaceSelectionStrong,
            TextPrimary, TextSecondary, TextDisabled, TextPlaceholder,
            TextLink, TextOnAccent, TextOnDanger,
            BorderDefault, BorderStrong, BorderSubtle, BorderFocus, BorderSplitter,
            AccentPrimary, AccentPrimaryHover, AccentPrimaryPressed,
            StatusSuccess, StatusWarning, StatusDanger, StatusInfo,
            EditorMarginBackground, EditorSpinnerStroke,
            EditorPopupBackground, EditorPopupBorder,
            ChatUserBubble, ChatAssistantBubble, ChatSystemBubble,

            // Spec 020 — IconBadge group
            IconBadgeTable, IconBadgeView, IconBadgeColumn, IconBadgeStoredProc,
            IconBadgeFunction, IconBadgeSnippet, IconBadgeKeyword, IconBadgeDatabase,
            IconBadgeSchema, IconBadgeTrigger, IconBadgeIndex, IconBadgeSynonym,

            // Spec 020 — TabColor group
            TabColorRed, TabColorAmber, TabColorGreen, TabColorBlue,
            TabColorTeal, TabColorPurple, TabColorPink, TabColorGray,

            // Spec 020 — History group
            HistoryOpenIcon, HistoryClosedIcon, HistoryStarActive, HistoryStarInactive,
            HistoryMatchHighlight,
        };

        /// <summary>
        /// All Spacing token keys (theme-invariant DIU scalars). Resources lookup yields a <see cref="double"/>.
        /// </summary>
        public static readonly string[] AllSpacing =
        {
            SpacingXs, SpacingS, SpacingM, SpacingL,
        };

        /// <summary>
        /// All Typography token keys (theme-invariant FontFamily + Size + Weight). Resources lookup yields a <see cref="TypographySpec"/>.
        /// </summary>
        public static readonly string[] AllTypography =
        {
            TypographyChrome, TypographyChromeTitle, TypographyEditor, TypographyIconBadge,
        };
    }

    /// <summary>
    /// Immutable composite of <see cref="FontFamily"/> + size (DIU) + <see cref="FontWeight"/>.
    /// Used as the resolved value of every <c>Typography.*</c> token in <see cref="ThemeTokens"/>.
    /// FontFamily references are hoisted to static readonly per the CLAUDE.md WPF convention so
    /// a single <see cref="FontFamily"/> instance is shared across all consumers.
    /// </summary>
    public sealed class TypographySpec
    {
        public FontFamily Family { get; }
        public double Size { get; }
        public FontWeight Weight { get; }

        public TypographySpec(FontFamily family, double size, FontWeight weight)
        {
            Family = family ?? throw new ArgumentNullException(nameof(family));
            if (size <= 0) throw new ArgumentOutOfRangeException(nameof(size), "Font size must be positive.");
            Size = size;
            Weight = weight;
        }
    }
}
